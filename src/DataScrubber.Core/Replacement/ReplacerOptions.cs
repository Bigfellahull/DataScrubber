namespace DataScrubber.Replacement;

/// <summary>
///     Options that influence how an <see cref="IReplacer"/> rewrites the
///     input. M1 has no behaviour-altering knobs; later milestones extend
///     this type without breaking callers.
/// </summary>
public sealed record ReplacerOptions
{
    /// <summary>
    ///     A reusable default instance.
    /// </summary>
    public static ReplacerOptions Default { get; } = new();
}
