using System.Text.RegularExpressions;

namespace DataScrubber.Detection.Rules;

/// <summary>
///     IPv4 dotted-quad detector. Each octet is validated to be in the range
///     <c>0–255</c> at the regex level. Emits detections with rule ID
///     <c>ipv4.dotted</c> and confidence <c>1.0</c>.
/// </summary>
public sealed partial class IPv4Detector : IDetector
{
    private const string RuleId = "ipv4.dotted";

    [GeneratedRegex(
        @"(?<!\d)(?:25[0-5]|2[0-4]\d|1\d{2}|[1-9]?\d)(?:\.(?:25[0-5]|2[0-4]\d|1\d{2}|[1-9]?\d)){3}(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
    {
        string text = input.ToString();
        foreach (Match match in Pattern().Matches(text))
        {
            yield return new Detection(match.Index, match.Length, DetectionType.IPv4, 1.0, RuleId);
        }
    }
}
