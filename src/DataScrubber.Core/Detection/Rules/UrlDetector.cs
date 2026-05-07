using System.Text.RegularExpressions;

namespace DataScrubber.Detection.Rules;

/// <summary>
///     URL detector. Matches a scheme of the form
///     <c>[a-zA-Z][a-zA-Z0-9+.\-]*://</c> followed by a host and path. Emits
///     detections with rule ID <c>url.scheme</c> and confidence <c>1.0</c>.
///     Trailing punctuation that is not part of the URL (sentence-final
///     <c>.</c>, <c>,</c>, <c>;</c>, <c>:</c>, <c>!</c>, <c>?</c>, closing
///     brackets) is excluded from the span.
/// </summary>
public sealed partial class UrlDetector : IDetector
{
    private const string RuleId = "url.scheme";
    private const string TrailingTrim = ".,;:!?";

    [GeneratedRegex(
        @"\b[a-zA-Z][a-zA-Z0-9+\-.]*://[^\s'""<>()\[\]{}]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
    {
        string text = input.ToString();
        foreach (Match match in Pattern().Matches(text))
        {
            int tail = text.AsSpan(match.Index + 1, match.Length - 1).TrimEnd(TrailingTrim).Length;
            yield return new Detection(match.Index, tail + 1, DetectionType.Url, 1.0, RuleId);
        }
    }
}
