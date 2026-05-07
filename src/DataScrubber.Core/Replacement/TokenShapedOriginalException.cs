namespace DataScrubber.Replacement;

/// <summary>
///     Thrown by <see cref="ReversibleReplacer"/> when a detection's original
///     value is itself shaped like a reversible token (e.g. <c>[EMAIL_001]</c>).
///     Persisting such an entry would break the rehydration idempotency
///     contract because a second rehydrate pass would re-substitute the
///     restored original. The CLI surfaces this as a runtime error.
/// </summary>
public sealed class TokenShapedOriginalException : Exception
{
    /// <summary>
    ///     The original substring that triggered the failure.
    /// </summary>
    public string Original { get; }

    /// <summary>
    ///     Initialises a new <see cref="TokenShapedOriginalException"/>.
    /// </summary>
    /// <param name="original">The token-shaped original substring.</param>
    public TokenShapedOriginalException(string original)
        : base(
            $"Cannot create a reversible mapping when an original value is token-shaped: '{original}'. " +
            "Reversibility requires originals to not match the token regex; otherwise rehydration is not idempotent. " +
            "Omit --reversible or sanitise the input.")
    {
        Original = original;
    }
}
