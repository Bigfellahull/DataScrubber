using System.Text.RegularExpressions;

namespace DataScrubber.Detection.Rules;

/// <summary>
///     Credit-card number detector. Matches 13–19 digit groups separated by
///     spaces or hyphens and rejects the match if the digits fail the Luhn
///     check. Emits detections with rule ID <c>cc.luhn</c> and confidence
///     <c>1.0</c>.
/// </summary>
public sealed partial class CreditCardDetector : IDetector
{
    private const string RuleId = "cc.luhn";

    [GeneratedRegex(
        @"(?<![\d-])\d(?:[ -]?\d){12,18}(?![\d-])",
        RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
    {
        string text = input.ToString();
        foreach (Match match in Pattern().Matches(text))
        {
            if (PassesLuhn(match.ValueSpan))
            {
                yield return new Detection(match.Index, match.Length, DetectionType.CreditCard, 1.0, RuleId);
            }
        }
    }

    private static bool PassesLuhn(ReadOnlySpan<char> span)
    {
        int sum = 0;
        bool doubleDigit = false;
        int digitCount = 0;

        for (int i = span.Length - 1; i >= 0; i--)
        {
            char ch = span[i];
            if (ch is < '0' or > '9')
            {
                continue;
            }

            int digit = ch - '0';
            digitCount++;

            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        return digitCount is >= 13 and <= 19 && sum % 10 == 0;
    }
}
