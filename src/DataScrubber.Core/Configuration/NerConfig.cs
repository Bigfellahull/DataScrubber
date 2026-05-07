namespace DataScrubber.Configuration;

using System.Text.Json.Serialization;
using DataScrubber.Detection;
using DataScrubber.Detection.Ner;

/// <summary>
///     The <c>ner</c> section of the user configuration. Holds per-type
///     confidence thresholds that override the M3 defaults from
///     <see cref="NerThresholds.Defaults"/>. Missing keys fall back to the
///     defaults.
/// </summary>
public sealed record NerConfig
{
    /// <summary>
    ///     Per-type minimum confidence. Each value must lie in <c>(0.0, 1.0]</c>;
    ///     values outside the range fail config validation.
    /// </summary>
    [JsonPropertyName("thresholds")]
    public IReadOnlyDictionary<DetectionType, double> Thresholds { get; init; }
        = new Dictionary<DetectionType, double>();

    /// <summary>An empty <see cref="NerConfig"/> with no overrides.</summary>
    public static NerConfig Empty { get; } = new();

    /// <summary>
    ///     Builds a <see cref="NerThresholds"/> by deep-merging
    ///     <see cref="Thresholds"/> over <see cref="NerThresholds.Defaults"/>.
    ///     Per-type keys missing from the config keep their default value.
    /// </summary>
    /// <returns>The merged thresholds.</returns>
    public NerThresholds ToNerThresholds()
    {
        NerThresholds defaults = NerThresholds.Defaults;
        return new NerThresholds(
            Person: GetThreshold(DetectionType.Person, defaults.Person),
            Organization: GetThreshold(DetectionType.Organization, defaults.Organization),
            Location: GetThreshold(DetectionType.Location, defaults.Location));
    }

    private double GetThreshold(DetectionType type, double fallback)
        => Thresholds.TryGetValue(type, out double value) ? value : fallback;
}

/// <summary>
///     The <c>rules</c> section of the user configuration. Holds the list of
///     disabled rule identifiers and the user-supplied custom regex rules.
/// </summary>
public sealed record RulesConfig
{
    /// <summary>
    ///     Identifiers to disable. Each entry matches either a
    ///     <see cref="DetectionType"/> name (disables every rule producing that
    ///     type) or a specific <c>SourceRule</c> ID (e.g. <c>apikey.entropy</c>).
    /// </summary>
    [JsonPropertyName("disabled")]
    public IReadOnlyList<string> Disabled { get; init; } = [];

    /// <summary>The user-supplied custom regex rules.</summary>
    [JsonPropertyName("custom")]
    public IReadOnlyList<CustomRule> Custom { get; init; } = [];

    /// <summary>An empty <see cref="RulesConfig"/> with no disables and no custom rules.</summary>
    public static RulesConfig Empty { get; } = new();
}
