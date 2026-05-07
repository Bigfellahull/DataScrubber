namespace DataScrubber.Detection;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
///     Per-type literal-phrase detector. Compiles every dictionary entry into
///     one alternation regex bracketed by word boundaries; longest-first
///     ordering ensures multi-word entries win over their prefixes. Matching
///     is case-sensitive. Entries are indexed in both NFC and NFD forms so
///     inputs in either Unicode normalisation form match consistently.
/// </summary>
public sealed class DictionaryDetector : IDetector
{
    private const string RuleIdPrefix = "dict.";

    private readonly Regex? _pattern;
    private readonly IReadOnlyDictionary<string, DetectionType> _entryToType;

    /// <summary>
    ///     Creates a detector backed by the supplied per-type dictionaries.
    ///     Empty entries are skipped; collisions on the same surface form
    ///     resolve last-writer-wins so explicit later entries can override
    ///     earlier ones.
    /// </summary>
    /// <param name="dictionaries">Per-type literal phrases.</param>
    public DictionaryDetector(IReadOnlyDictionary<DetectionType, IReadOnlyList<string>> dictionaries)
    {
        ArgumentNullException.ThrowIfNull(dictionaries);

        Dictionary<string, DetectionType> entryToType = new(StringComparer.Ordinal);
        foreach ((DetectionType type, IReadOnlyList<string>? entries) in dictionaries)
        {
            if (entries is null)
            {
                continue;
            }

            foreach (string raw in entries)
            {
                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }

                string nfc = raw.Normalize(NormalizationForm.FormC);
                string nfd = raw.Normalize(NormalizationForm.FormD);
                entryToType[nfc] = type;
                entryToType[nfd] = type;
            }
        }

        _entryToType = entryToType;
        _pattern = entryToType.Count == 0 ? null : BuildPattern(entryToType.Keys);
    }

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
    {
        if (_pattern is null || input.IsEmpty)
        {
            yield break;
        }

        string text = input.ToString();
        foreach (Match match in _pattern.Matches(text))
        {
            if (_entryToType.TryGetValue(match.Value, out DetectionType type))
            {
                yield return new Detection(match.Index, match.Length, type, 1.0, RuleIdPrefix + type);
            }
        }
    }

    private static Regex BuildPattern(IEnumerable<string> entries)
    {
        IEnumerable<string> ordered = entries
            .OrderByDescending(static e => e.Length)
            .ThenBy(static e => e, StringComparer.Ordinal);

        string alternation = string.Join("|", ordered.Select(Regex.Escape));
        return new Regex($@"\b(?:{alternation})\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }
}
