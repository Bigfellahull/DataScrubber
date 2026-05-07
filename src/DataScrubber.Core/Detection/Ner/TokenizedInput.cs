namespace DataScrubber.Detection.Ner;

/// <summary>
///     A character span inside the original input. The tokenizer emits one of
///     these per produced subword token; special tokens (<c>[CLS]</c>,
///     <c>[SEP]</c>, <c>[PAD]</c>) are not produced — the detector adds them
///     when framing windows.
/// </summary>
/// <param name="Start">Zero-based start index of the source character span.</param>
/// <param name="Length">Length of the source character span in UTF-16 code units.</param>
public readonly record struct TokenSpan(int Start, int Length)
{
    /// <summary>Exclusive end index of the span.</summary>
    public int End => Start + Length;
}

/// <summary>
///     The output of <see cref="INerTokenizer.Tokenize(string)"/>: parallel
///     lists of token IDs and their character spans into the source input.
/// </summary>
/// <param name="TokenIds">Vocabulary IDs for each subword token.</param>
/// <param name="Offsets">Source-character offset for each subword token.</param>
public readonly record struct TokenizedInput(
    IReadOnlyList<int> TokenIds,
    IReadOnlyList<TokenSpan> Offsets);
