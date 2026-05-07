namespace DataScrubber.Detection;

/// <summary>
///     Numeric priority used by <see cref="DetectionMerger"/> as the third tie-breaker
///     (after span length and confidence) when overlapping detections compete for the
///     same span. Lower values win.
/// </summary>
public static class DetectorPriority
{
    /// <summary>Priority assigned to deterministic rule-based detectors.</summary>
    public const int Rules = 0;

    /// <summary>Priority assigned to NER-based detectors. Reserved for M3.</summary>
    public const int Ner = 100;

    /// <summary>
    ///     Returns the canonical priority for a given <see cref="DetectionType"/>.
    ///     Rule types map to <see cref="Rules"/>; NER types
    ///     (<see cref="DetectionType.Person"/>, <see cref="DetectionType.Organization"/>,
    ///     <see cref="DetectionType.Location"/>) map to <see cref="Ner"/>.
    /// </summary>
    /// <param name="type">The detection type.</param>
    /// <returns>The canonical priority value.</returns>
    public static int For(DetectionType type) => type switch
    {
        DetectionType.Person or DetectionType.Organization or DetectionType.Location => Ner,
        _ => Rules,
    };
}
