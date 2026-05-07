using System.Text.RegularExpressions;

namespace DataScrubber.Detection.Rules;

/// <summary>
///     Pragmatic RFC 5322 email detector. Accepts plus-addressing, dotted local
///     parts, and IDN host labels via Unicode property classes; rejects double
///     <c>@</c>. Emits detections with rule ID <c>email.basic</c> and confidence
///     <c>1.0</c>.
/// </summary>
public sealed partial class EmailDetector : IDetector
{
    private const string RuleId = "email.basic";

    [GeneratedRegex(
        @"(?<![\w.+-])(?:""[^""\r\n]+""|[\p{L}\p{N}._%+\-]+)@[\p{L}\p{N}](?:[\p{L}\p{N}\-]*[\p{L}\p{N}])?(?:\.[\p{L}\p{N}](?:[\p{L}\p{N}\-]*[\p{L}\p{N}])?)+(?![\w])",
        RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
    {
        string text = input.ToString();
        foreach (Match match in Pattern().Matches(text))
        {
            yield return new Detection(match.Index, match.Length, DetectionType.Email, 1.0, RuleId);
        }
    }
}
