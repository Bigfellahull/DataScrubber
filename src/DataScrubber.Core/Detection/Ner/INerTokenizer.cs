namespace DataScrubber.Detection.Ner;

/// <summary>
///     Tokenizer abstraction used by <see cref="OnnxNerDetector"/>. Implementations
///     produce subword token IDs paired with character offsets back into the
///     original input, and expose the special-token IDs the detector needs to
///     frame model windows.
/// </summary>
public interface INerTokenizer
{
    /// <summary>The vocabulary ID of the <c>[CLS]</c> sentence-start token.</summary>
    int ClsTokenId { get; }

    /// <summary>The vocabulary ID of the <c>[SEP]</c> sentence-end token.</summary>
    int SepTokenId { get; }

    /// <summary>The vocabulary ID of the <c>[PAD]</c> padding token.</summary>
    int PadTokenId { get; }

    /// <summary>
    ///     Tokenises <paramref name="input"/> into subword token IDs with
    ///     character offsets. Special framing tokens are not included; the
    ///     detector adds <c>[CLS]</c> and <c>[SEP]</c> per window.
    /// </summary>
    /// <param name="input">The text to tokenise.</param>
    /// <returns>Token IDs and character offsets in the same order.</returns>
    TokenizedInput Tokenize(string input);
}
