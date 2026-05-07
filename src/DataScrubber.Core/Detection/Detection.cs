namespace DataScrubber.Detection;

/// <summary>
///     A single detected entity inside the input. The span is described in
///     UTF-16 code units relative to the start of the input string. Detectors
///     emit these as the unit of work consumed by the merger and the replacer.
/// </summary>
/// <param name="Start">Zero-based start index of the detected span.</param>
/// <param name="Length">Length of the detected span in characters.</param>
/// <param name="Type">The category the detector assigned.</param>
/// <param name="Confidence">Detector-supplied confidence in the range <c>[0, 1]</c>.</param>
/// <param name="SourceRule">A free-form rule identifier used for diagnostics, configuration, and overlap merging.</param>
public readonly record struct Detection(
    int Start,
    int Length,
    DetectionType Type,
    double Confidence,
    string SourceRule)
{
    /// <summary>
    ///     Exclusive end index of the span, equivalent to <see cref="Start"/> + <see cref="Length"/>.
    /// </summary>
    public int End => Start + Length;

    /// <summary>
    ///     Returns <c>true</c> when this detection's span overlaps the supplied detection's span.
    /// </summary>
    /// <param name="other">The detection to test against.</param>
    /// <returns><c>true</c> if the spans overlap by at least one character; otherwise <c>false</c>.</returns>
    public bool OverlapsWith(Detection other) => Start < other.End && other.Start < End;
}
