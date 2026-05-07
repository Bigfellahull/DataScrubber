namespace DataScrubber.Detection;

/// <summary>
///     Aggregates the M1 rule-based detectors and exposes them as a single
///     <see cref="IDetector"/>. Detector results are concatenated; overlap
///     resolution is the merger's job, not this class's.
/// </summary>
public sealed class RuleBasedDetector : IDetector
{
    private readonly IReadOnlyList<IDetector> _detectors;

    /// <summary>
    ///     Creates a <see cref="RuleBasedDetector"/> that runs the supplied
    ///     detectors in iteration order. The order does not affect correctness
    ///     because the merger resolves overlaps deterministically; it only
    ///     affects the order of unmerged detections.
    /// </summary>
    /// <param name="detectors">The detectors to aggregate.</param>
    public RuleBasedDetector(IEnumerable<IDetector> detectors)
    {
        ArgumentNullException.ThrowIfNull(detectors);
        _detectors = [.. detectors];
    }

    /// <summary>
    ///     Builds a <see cref="RuleBasedDetector"/> wired with every detector
    ///     shipped in M1. The list is the canonical order used by the CLI.
    /// </summary>
    /// <returns>A detector configured with the full M1 rule set.</returns>
    public static RuleBasedDetector CreateDefault() => new(
    [
        new Rules.EmailDetector(),
        new Rules.IPv4Detector(),
        new Rules.IPv6Detector(),
        new Rules.UrlDetector(),
        new Rules.PhoneDetector(),
        new Rules.CreditCardDetector(),
        new Rules.ApiKeyDetector(),
        new Rules.PasswordAssignmentDetector(),
        new Rules.UserPathDetector(),
        new Rules.MacAddressDetector(),
    ]);

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
    {
        foreach (IDetector detector in _detectors)
        {
            foreach (Detection detection in detector.Detect(input, ctx))
            {
                yield return detection;
            }
        }
    }
}
