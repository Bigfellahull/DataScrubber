namespace DataScrubber.Detection.Ner;

using System.Text.Json;

/// <summary>
///     The BIO prefix carried alongside a parsed label.
/// </summary>
public enum BioPrefix
{
    /// <summary>The token is outside any tagged span.</summary>
    Outside,

    /// <summary>The token begins a tagged span.</summary>
    Begin,

    /// <summary>The token continues a tagged span.</summary>
    Inside,
}

/// <summary>
///     A parsed label produced by the model: a BIO prefix paired with the
///     <see cref="DetectionType"/> the prefix applies to. Labels that do not
///     map to a NER detection type collapse to <see cref="BioPrefix.Outside"/>
///     with a <c>null</c> <see cref="Type"/>.
/// </summary>
/// <param name="Prefix">Whether the token begins, continues, or is outside a span.</param>
/// <param name="Type">The detection type, or <c>null</c> for outside-of-span tokens.</param>
/// <param name="Raw">The raw label string from the model output, kept for diagnostics.</param>
public readonly record struct BioLabel(BioPrefix Prefix, DetectionType? Type, string Raw)
{
    /// <summary>The canonical "outside" sentinel.</summary>
    public static BioLabel Outside { get; } = new(BioPrefix.Outside, null, "O");
}

/// <summary>
///     Maps the NER model's output indices to BIO-tagged labels. Labels that
///     are not one of <c>PER / PERSON</c>, <c>ORG / ORGANIZATION</c>, or
///     <c>LOC / LOCATION</c> collapse to <see cref="BioLabel.Outside"/>; this
///     keeps the assembler's logic linear in the number of targeted types.
/// </summary>
public sealed class LabelMap
{
    private readonly BioLabel[] _byIndex;

    /// <summary>
    ///     Creates a label map from an ordered sequence of raw label strings.
    ///     Index 0 maps to <paramref name="rawLabels"/>[0], and so on.
    /// </summary>
    /// <param name="rawLabels">The model's raw label strings in output-index order.</param>
    public LabelMap(IEnumerable<string> rawLabels)
    {
        ArgumentNullException.ThrowIfNull(rawLabels);
        _byIndex = [.. rawLabels.Select(Parse)];
    }

    /// <summary>The number of labels in the map.</summary>
    public int Count => _byIndex.Length;

    /// <summary>
    ///     Returns the parsed label at <paramref name="index"/>.
    /// </summary>
    /// <param name="index">The model output index.</param>
    /// <returns>The parsed <see cref="BioLabel"/>.</returns>
    public BioLabel this[int index] => _byIndex[index];

    /// <summary>
    ///     Loads a label map from a JSON file. Two shapes are accepted:
    ///     a JSON array (<c>["O", "B-PER", ...]</c>) or a JSON object whose
    ///     keys are decimal indices (<c>{"0":"O","1":"B-PER",...}</c>) which
    ///     is the format HuggingFace exports.
    /// </summary>
    /// <param name="path">Absolute path to <c>labels.json</c>.</param>
    /// <returns>The loaded <see cref="LabelMap"/>.</returns>
    /// <exception cref="NerModelLoadException">Raised when the file is missing or unparsable.</exception>
    public static LabelMap Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
        {
            throw new NerModelLoadException($"NER label map not found: {path}", path);
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            using JsonDocument document = JsonDocument.Parse(stream);
            return new LabelMap(ReadLabels(document.RootElement));
        }
        catch (Exception ex) when (ex is JsonException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or FormatException)
        {
            throw new NerModelLoadException($"failed to parse NER label map at {path}: {ex.Message}", path, ex);
        }
    }

    private static IEnumerable<string> ReadLabels(JsonElement root) => root.ValueKind switch
    {
        JsonValueKind.Array => [.. root.EnumerateArray().Select(e => e.GetString() ?? string.Empty)],
        JsonValueKind.Object => ReadIndexedLabels(root),
        _ => throw new JsonException("labels.json must be a JSON array or an object keyed by decimal indices"),
    };

    private static IEnumerable<string> ReadIndexedLabels(JsonElement root)
    {
        SortedDictionary<int, string> ordered = [];
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!int.TryParse(property.Name, out int index))
            {
                throw new JsonException($"labels.json key '{property.Name}' is not a decimal index");
            }

            ordered[index] = property.Value.GetString() ?? string.Empty;
        }

        if (ordered.Count > 0 && (ordered.Keys.First() != 0 || ordered.Keys.Last() != ordered.Count - 1))
        {
            throw new JsonException("labels.json indices must be contiguous starting at 0");
        }

        return ordered.Values;
    }

    private static BioLabel Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw == "O")
        {
            return BioLabel.Outside;
        }

        int dash = raw.IndexOf('-');
        if (dash <= 0 || dash == raw.Length - 1)
        {
            return BioLabel.Outside with { Raw = raw };
        }

        BioPrefix prefix = raw[..dash] switch
        {
            "B" => BioPrefix.Begin,
            "I" => BioPrefix.Inside,
            _ => BioPrefix.Outside,
        };

        if (prefix == BioPrefix.Outside)
        {
            return BioLabel.Outside with { Raw = raw };
        }

        DetectionType? type = raw[(dash + 1)..].ToUpperInvariant() switch
        {
            "PER" or "PERSON" => DetectionType.Person,
            "ORG" or "ORGANIZATION" => DetectionType.Organization,
            "LOC" or "LOCATION" => DetectionType.Location,
            _ => null,
        };

        return type is null
            ? BioLabel.Outside with { Raw = raw }
            : new BioLabel(prefix, type, raw);
    }
}
