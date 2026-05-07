using System.Text.RegularExpressions;

namespace DataScrubber.Detection.Rules;

/// <summary>
///     Phone-number detector. Matches NANP-style numbers (with optional
///     country code, parentheses, and separators) and E.164-style numbers
///     (a leading <c>+</c> followed by 8–15 digits, optionally with
///     intra-number separators and a trailing extension). Emits detections
///     with rule IDs <c>phone.nanp</c> or <c>phone.e164</c> and confidence
///     <c>1.0</c>.
/// </summary>
public sealed partial class PhoneDetector : IDetector
{
    private const string NanpRule = "phone.nanp";
    private const string E164Rule = "phone.e164";

    [GeneratedRegex(
        @"(?<!\d)\+?1?[-. ]?\(?\d{3}\)?[-. ]?\d{3}[-. ]?\d{4}(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex NanpPattern();

    [GeneratedRegex(
        @"(?<!\d)\+\d(?:[-. ]?\d){7,14}(?:\s*(?:x|ext\.?)\s*\d+)?(?!\d)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex E164Pattern();

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
    {
        string text = input.ToString();

        foreach (Match match in NanpPattern().Matches(text))
        {
            yield return new Detection(match.Index, match.Length, DetectionType.Phone, 1.0, NanpRule);
        }

        foreach (Match match in E164Pattern().Matches(text))
        {
            yield return new Detection(match.Index, match.Length, DetectionType.Phone, 1.0, E164Rule);
        }
    }
}
