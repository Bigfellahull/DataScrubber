using System.Text.RegularExpressions;

namespace DataScrubber.Detection.Rules;

/// <summary>
///     IPv6 address detector. Recognises the full eight-group form and every
///     standard <c>::</c> compression variant. Emits detections with rule ID
///     <c>ipv6.standard</c> and confidence <c>1.0</c>.
/// </summary>
public sealed partial class IPv6Detector : IDetector
{
    private const string RuleId = "ipv6.standard";

    [GeneratedRegex(
        @"(?<![0-9A-Fa-f:])(?:" +
        @"(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}" +
        @"|(?:[0-9A-Fa-f]{1,4}:){1,7}:" +
        @"|(?:[0-9A-Fa-f]{1,4}:){1,6}:[0-9A-Fa-f]{1,4}" +
        @"|(?:[0-9A-Fa-f]{1,4}:){1,5}(?::[0-9A-Fa-f]{1,4}){1,2}" +
        @"|(?:[0-9A-Fa-f]{1,4}:){1,4}(?::[0-9A-Fa-f]{1,4}){1,3}" +
        @"|(?:[0-9A-Fa-f]{1,4}:){1,3}(?::[0-9A-Fa-f]{1,4}){1,4}" +
        @"|(?:[0-9A-Fa-f]{1,4}:){1,2}(?::[0-9A-Fa-f]{1,4}){1,5}" +
        @"|[0-9A-Fa-f]{1,4}:(?::[0-9A-Fa-f]{1,4}){1,6}" +
        @"|:(?::[0-9A-Fa-f]{1,4}){1,7}" +
        @"|::" +
        @")(?![0-9A-Fa-f:])",
        RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
    {
        string text = input.ToString();
        foreach (Match match in Pattern().Matches(text))
        {
            // The "::" variant alone is too generic (matches a bare double colon
            // anywhere, e.g. inside "key::value"). Require at least one hex group.
            if (match.Length < 3)
            {
                continue;
            }

            yield return new Detection(match.Index, match.Length, DetectionType.IPv6, 1.0, RuleId);
        }
    }
}
