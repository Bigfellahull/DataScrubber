using System.Text.RegularExpressions;

namespace DataScrubber.Detection.Rules;

/// <summary>
///     Detects password-style assignments (<c>password=…</c>, <c>token: …</c>,
///     etc.) and emits a detection covering only the value, leaving the
///     keyword and separator intact so the line stays diagnostic. Rule ID:
///     <c>password.assignment</c>; confidence <c>1.0</c>.
/// </summary>
public sealed partial class PasswordAssignmentDetector : IDetector
{
    private const string RuleId = "password.assignment";

    [GeneratedRegex(
        @"\b(?:password|passwd|pwd|secret|token|api_key|apikey|api-key)\b\s*[:=]\s*(""[^""\r\n]+""|'[^'\r\n]+'|\S+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
    {
        string text = input.ToString();
        foreach (Match match in Pattern().Matches(text))
        {
            Group value = match.Groups[1];
            if (value.Success && value.Length > 0)
            {
                yield return new Detection(value.Index, value.Length, DetectionType.Password, 1.0, RuleId);
            }
        }
    }
}
