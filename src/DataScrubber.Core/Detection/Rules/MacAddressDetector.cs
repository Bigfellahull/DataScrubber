using System.Text.RegularExpressions;

namespace DataScrubber.Detection.Rules;

/// <summary>
///     Detects MAC addresses (six colon- or hyphen-separated hex pairs).
///     Rule ID <c>mac.colon</c>; confidence <c>1.0</c>.
/// </summary>
public sealed partial class MacAddressDetector : IDetector
{
    private const string RuleId = "mac.colon";

    [GeneratedRegex(
        @"\b(?:[0-9A-Fa-f]{2}[:\-]){5}[0-9A-Fa-f]{2}\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
    {
        string text = input.ToString();
        foreach (Match match in Pattern().Matches(text))
        {
            yield return new Detection(match.Index, match.Length, DetectionType.MacAddress, 1.0, RuleId);
        }
    }
}
