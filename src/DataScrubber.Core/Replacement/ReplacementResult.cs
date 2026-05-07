namespace DataScrubber.Replacement;

using DataScrubber.Detection;

/// <summary>
///     The result of running an <see cref="IReplacer"/>. Holds the rewritten
///     output and the (already merged) detections that were applied.
/// </summary>
/// <param name="Output">The rewritten string.</param>
/// <param name="Applied">The detections actually applied, in left-to-right order.</param>
public readonly record struct ReplacementResult(
    string Output,
    IReadOnlyList<Detection> Applied);
