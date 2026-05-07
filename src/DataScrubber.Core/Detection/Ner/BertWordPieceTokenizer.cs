namespace DataScrubber.Detection.Ner;

using System.Globalization;
using System.Text.Json;

/// <summary>
///     A vendored, minimal BERT-family WordPiece tokenizer that reads a
///     HuggingFace <c>tokenizer.json</c> file. It implements the slice of the
///     spec needed by the M3 NER pipeline:
///     <list type="bullet">
///         <item><description>BERT-style pre-tokenisation (whitespace + punctuation + CJK split).</description></item>
///         <item><description>Optional lower-casing controlled by the file's normaliser configuration.</description></item>
///         <item><description>Greedy longest-match WordPiece lookup with the configured continuing-subword prefix (default <c>##</c>).</description></item>
///         <item><description>Character-offset preservation for every emitted token, used by <see cref="BioSpanReconstructor"/> to re-anchor model spans onto the source input.</description></item>
///     </list>
///     Anything not listed here (BPE, full normalisation chains, byte-fallback) is intentionally out of scope; M4
///     swaps the tokenizer out via <see cref="INerTokenizer"/> if a different scheme is required.
/// </summary>
public sealed class BertWordPieceTokenizer : INerTokenizer
{
    private readonly IReadOnlyDictionary<string, int> _vocab;
    private readonly bool _lowercase;
    private readonly string _continuingSubwordPrefix;
    private readonly int _unkTokenId;

    /// <inheritdoc />
    public int ClsTokenId { get; }

    /// <inheritdoc />
    public int SepTokenId { get; }

    /// <inheritdoc />
    public int PadTokenId { get; }

    private BertWordPieceTokenizer(
        IReadOnlyDictionary<string, int> vocab,
        bool lowercase,
        string continuingSubwordPrefix,
        int unkTokenId,
        int clsTokenId,
        int sepTokenId,
        int padTokenId)
    {
        _vocab = vocab;
        _lowercase = lowercase;
        _continuingSubwordPrefix = continuingSubwordPrefix;
        _unkTokenId = unkTokenId;
        ClsTokenId = clsTokenId;
        SepTokenId = sepTokenId;
        PadTokenId = padTokenId;
    }

    /// <summary>
    ///     Loads a tokenizer from a HuggingFace-style <c>tokenizer.json</c>.
    /// </summary>
    /// <param name="path">Absolute path to <c>tokenizer.json</c>.</param>
    /// <returns>A configured tokenizer.</returns>
    /// <exception cref="NerModelLoadException">Raised when the file is missing or the configuration is unusable.</exception>
    public static BertWordPieceTokenizer Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
        {
            throw new NerModelLoadException($"NER tokenizer not found: {path}", path);
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            using JsonDocument document = JsonDocument.Parse(stream);
            return Parse(document.RootElement, path);
        }
        catch (Exception ex) when (ex is JsonException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or FormatException)
        {
            throw new NerModelLoadException($"failed to parse NER tokenizer at {path}: {ex.Message}", path, ex);
        }
    }

    /// <inheritdoc />
    public TokenizedInput Tokenize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<int> ids = [];
        List<TokenSpan> offsets = [];

        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (IsPunctuation(c) || IsCjk(c))
            {
                EmitWordPieces(input, i, 1, ids, offsets);
                i++;
                continue;
            }

            int wordStart = i;
            while (i < input.Length
                   && !char.IsWhiteSpace(input[i])
                   && !IsPunctuation(input[i])
                   && !IsCjk(input[i]))
            {
                i++;
            }

            EmitWordPieces(input, wordStart, i - wordStart, ids, offsets);
        }

        return new TokenizedInput(ids, offsets);
    }

    private void EmitWordPieces(string input, int start, int length, List<int> ids, List<TokenSpan> offsets)
    {
        if (length <= 0)
        {
            return;
        }

        ReadOnlySpan<char> word = input.AsSpan(start, length);
        string normalized = _lowercase
            ? word.ToString().ToLower(CultureInfo.InvariantCulture)
            : word.ToString();

        // BERT WordPiece greedy: try longest prefix in vocab; on failure, emit
        // the entire word as [UNK] with the full character span.
        int cursor = 0;
        List<int> piecesIds = [];
        List<TokenSpan> piecesOffsets = [];
        bool ok = true;
        while (cursor < normalized.Length)
        {
            int end = normalized.Length;
            int matchedId = -1;
            int matchedEnd = -1;
            while (end > cursor)
            {
                string candidate = cursor == 0
                    ? normalized[cursor..end]
                    : _continuingSubwordPrefix + normalized[cursor..end];
                if (_vocab.TryGetValue(candidate, out int id))
                {
                    matchedId = id;
                    matchedEnd = end;
                    break;
                }

                end--;
            }

            if (matchedId < 0)
            {
                ok = false;
                break;
            }

            piecesIds.Add(matchedId);
            piecesOffsets.Add(new TokenSpan(start + cursor, matchedEnd - cursor));
            cursor = matchedEnd;
        }

        if (!ok)
        {
            ids.Add(_unkTokenId);
            offsets.Add(new TokenSpan(start, length));
            return;
        }

        ids.AddRange(piecesIds);
        offsets.AddRange(piecesOffsets);
    }

    private static BertWordPieceTokenizer Parse(JsonElement root, string path)
    {
        if (!root.TryGetProperty("model", out JsonElement model))
        {
            throw new NerModelLoadException($"NER tokenizer at {path} is missing the 'model' object", path);
        }

        if (!model.TryGetProperty("vocab", out JsonElement vocabElement)
            || vocabElement.ValueKind != JsonValueKind.Object)
        {
            throw new NerModelLoadException($"NER tokenizer at {path} is missing 'model.vocab'", path);
        }

        Dictionary<string, int> vocab = new(StringComparer.Ordinal);
        foreach (JsonProperty property in vocabElement.EnumerateObject())
        {
            vocab[property.Name] = property.Value.GetInt32();
        }

        string unkToken = TryGetString(model, "unk_token") ?? "[UNK]";
        string continuingSubwordPrefix = TryGetString(model, "continuing_subword_prefix") ?? "##";

        bool lowercase = false;
        if (root.TryGetProperty("normalizer", out JsonElement normalizer))
        {
            lowercase = ReadLowercase(normalizer);
        }

        int unkId = ResolveSpecialTokenId(vocab, root, unkToken, path, "unk");
        int clsId = ResolveSpecialTokenId(vocab, root, "[CLS]", path, "cls");
        int sepId = ResolveSpecialTokenId(vocab, root, "[SEP]", path, "sep");
        int padId = ResolveSpecialTokenId(vocab, root, "[PAD]", path, "pad");

        return new BertWordPieceTokenizer(vocab, lowercase, continuingSubwordPrefix, unkId, clsId, sepId, padId);
    }

    private static string? TryGetString(JsonElement element, string property)
        => element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadLowercase(JsonElement normalizer) => normalizer.ValueKind switch
    {
        JsonValueKind.Object when normalizer.TryGetProperty("lowercase", out JsonElement v) && v.ValueKind == JsonValueKind.True => true,
        JsonValueKind.Object when normalizer.TryGetProperty("normalizers", out JsonElement list) && list.ValueKind == JsonValueKind.Array =>
            list.EnumerateArray().Any(ReadLowercase),
        _ => false,
    };

    private static int ResolveSpecialTokenId(
        IReadOnlyDictionary<string, int> vocab,
        JsonElement root,
        string token,
        string path,
        string label)
    {
        if (vocab.TryGetValue(token, out int id))
        {
            return id;
        }

        if (root.TryGetProperty("added_tokens", out JsonElement added) && added.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in added.EnumerateArray())
            {
                if (entry.TryGetProperty("content", out JsonElement content)
                    && content.GetString() == token
                    && entry.TryGetProperty("id", out JsonElement idElement))
                {
                    return idElement.GetInt32();
                }
            }
        }

        throw new NerModelLoadException(
            $"NER tokenizer at {path} is missing the {label} token '{token}'",
            path);
    }

    private static bool IsPunctuation(char c) =>
        char.IsPunctuation(c) || char.IsSymbol(c);

    private static bool IsCjk(char c) =>
        // A coarse but stable CJK range good enough for English-first NER:
        // matches BERT's BasicTokenizer._is_chinese_char heuristic.
        (c >= '一' && c <= '鿿')
        || (c >= '㐀' && c <= '䶿')
        || (c >= '豈' && c <= '﫿');
}
