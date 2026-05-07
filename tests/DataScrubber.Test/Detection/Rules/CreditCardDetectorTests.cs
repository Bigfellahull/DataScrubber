namespace DataScrubber.Test.Detection.Rules;

using DataScrubber.Detection;
using DataScrubber.Detection.Rules;
using FluentAssertions;
using Xunit;

public class CreditCardDetectorTests
{
    private static IReadOnlyList<Detection> Detect(string input)
        => new CreditCardDetector().Detect(input.AsMemory(), DetectionContext.Empty).ToList();

    [Theory]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("4111111111111111")]
    public void DetectsLuhnValidVisa(string text)
    {
        IReadOnlyList<Detection> detections = Detect(text);
        detections.Should().ContainSingle();
        detections[0].Type.Should().Be(DetectionType.CreditCard);
        detections[0].SourceRule.Should().Be("cc.luhn");
    }

    [Theory]
    [InlineData("4111 1111 1111 1112")]
    [InlineData("1234 5678 9012 3456")]
    public void RejectsLuhnFailures(string text)
    {
        Detect(text).Should().BeEmpty();
    }

    [Fact]
    public void DetectsNineteenDigitNumberWhenLuhnPasses()
    {
        string nineteenDigit = BuildLuhnNumber("675964982", 19);
        IReadOnlyList<Detection> detections = Detect(nineteenDigit);
        detections.Should().ContainSingle();
        detections[0].Length.Should().Be(nineteenDigit.Length);
    }

    private static string BuildLuhnNumber(string prefix, int totalDigits)
    {
        string body = prefix.PadRight(totalDigits - 1, '0');
        int sum = 0;
        bool doubleIt = true;
        for (int i = body.Length - 1; i >= 0; i--)
        {
            int d = body[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            sum += d;
            doubleIt = !doubleIt;
        }

        int check = (10 - sum % 10) % 10;
        return body + (char)('0' + check);
    }
}
