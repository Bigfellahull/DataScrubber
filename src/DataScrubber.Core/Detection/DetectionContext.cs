namespace DataScrubber.Detection;

/// <summary>
///     Per-run context passed to every <see cref="IDetector"/> invocation. Lets
///     detectors share state about the current run (e.g. an optional file path
///     for diagnostics) without coupling their signatures to the CLI layer.
/// </summary>
public readonly record struct DetectionContext
{
    /// <summary>
    ///     A logical source identifier (typically a file path) used purely for
    ///     diagnostics. <c>null</c> when the input came from stdin or an in-memory
    ///     buffer.
    /// </summary>
    public string? SourceName { get; init; }

    /// <summary>
    ///     The original input the detection was produced against. Required
    ///     whenever a detector intends to call <see cref="ShouldDrop"/>;
    ///     <c>null</c> contexts skip the allow-list check entirely.
    /// </summary>
    public string? Input { get; init; }

    /// <summary>
    ///     Optional allow-list applied per-detection. The filter is consulted
    ///     by <see cref="ShouldDrop"/> so probabilistic detectors (e.g. NER)
    ///     can drop allow-listed candidates before they reach the merger and
    ///     compete with rule detections. <c>null</c> means no allow-list.
    /// </summary>
    public AllowListFilter? AllowList { get; init; }

    /// <summary>
    ///     A reusable empty context for callers that have no extra information
    ///     to supply.
    /// </summary>
    public static DetectionContext Empty { get; } = new();

    /// <summary>
    ///     Returns <c>true</c> when <paramref name="detection"/> should be
    ///     dropped before downstream merging or replacement. Backed by
    ///     <see cref="AllowList"/>; returns <c>false</c> when no allow-list or
    ///     no <see cref="Input"/> is set.
    /// </summary>
    /// <param name="detection">The candidate detection.</param>
    /// <returns><c>true</c> when the candidate is allow-listed.</returns>
    public bool ShouldDrop(Detection detection)
    {
        if (AllowList is null || Input is null)
        {
            return false;
        }

        return AllowList.ShouldDrop(Input, detection);
    }
}
