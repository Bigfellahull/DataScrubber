namespace DataScrubber.Detection.Ner;

/// <summary>
///     Per-type confidence thresholds applied after BIO assembly. M3 ships
///     hard-coded defaults; M4 adds a config surface that overrides them.
///     Spans whose confidence falls below the per-type threshold are dropped
///     before being emitted as detections.
/// </summary>
public sealed record NerThresholds(double Person, double Organization, double Location)
{
    /// <summary>
    ///     The defaults from SPEC §6 D2: <c>0.85</c> for <see cref="DetectionType.Person"/>
    ///     and <see cref="DetectionType.Organization"/>, <c>0.80</c> for
    ///     <see cref="DetectionType.Location"/>.
    /// </summary>
    public static NerThresholds Defaults { get; } = new(0.85, 0.85, 0.80);

    /// <summary>
    ///     Returns the threshold for a given NER detection type. Non-NER
    ///     types fall through to <c>0.0</c>; the detector never emits them so
    ///     the value is never consulted.
    /// </summary>
    /// <param name="type">The detection type.</param>
    /// <returns>The configured threshold.</returns>
    public double For(DetectionType type) => type switch
    {
        DetectionType.Person => Person,
        DetectionType.Organization => Organization,
        DetectionType.Location => Location,
        _ => 0.0,
    };
}
