namespace DataScrubber.Configuration;

using System.Text.Json.Serialization;
using DataScrubber.Detection;

/// <summary>
///     A user-supplied regex rule that produces detections of a specific
///     <see cref="DetectionType"/>. The rule's <see cref="Id"/> is used as the
///     <see cref="Detection.SourceRule"/> on every emitted detection so it can
///     be reasoned about in reports and tests just like a built-in rule.
/// </summary>
public sealed record CustomRule
{
    /// <summary>The rule identifier surfaced as <see cref="Detection.SourceRule"/>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The detection category this rule emits.</summary>
    [JsonPropertyName("type")]
    public required DetectionType Type { get; init; }

    /// <summary>The .NET regex pattern. Compiled with <c>Compiled | CultureInvariant</c>.</summary>
    [JsonPropertyName("pattern")]
    public required string Pattern { get; init; }

    /// <summary>Confidence reported on each emitted detection. Defaults to <c>0.9</c>.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; } = 0.9;
}
