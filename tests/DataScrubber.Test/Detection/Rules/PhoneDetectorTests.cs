namespace DataScrubber.Test.Detection.Rules;

using DataScrubber.Detection;
using DataScrubber.Detection.Rules;
using FluentAssertions;
using Xunit;

public class PhoneDetectorTests
{
    private static IReadOnlyList<Detection> Detect(string input)
        => new PhoneDetector().Detect(input.AsMemory(), DetectionContext.Empty).ToList();

    [Theory]
    [InlineData("+1 (415) 555-2671")]
    [InlineData("415-555-2671")]
    [InlineData("(415) 555-2671")]
    public void DetectsNanp(string text)
    {
        IReadOnlyList<Detection> detections = Detect(text);
        detections.Should().ContainSingle();
        detections[0].Type.Should().Be(DetectionType.Phone);
        detections[0].SourceRule.Should().Be("phone.nanp");
    }

    [Theory]
    [InlineData("version 1.0.5.2")]
    [InlineData("call us tomorrow")]
    public void RejectsNonPhoneSequences(string text)
    {
        Detect(text).Should().BeEmpty();
    }

    [Fact]
    public void DetectsInternationalNumberWithSeparatorsAndExtension()
    {
        const string text = "+44 20 7946 0958 x1234";
        IReadOnlyList<Detection> detections = Detect(text);
        detections.Should().NotBeEmpty();

        Detection longest = detections.OrderByDescending(d => d.Length).First();
        longest.Length.Should().Be(text.Length);
        longest.SourceRule.Should().Be("phone.e164");
    }

    [Fact]
    public void DetectsStrictE164()
    {
        IReadOnlyList<Detection> detections = Detect("+447946095812345");
        detections.Should().ContainSingle();
        detections[0].SourceRule.Should().Be("phone.e164");
    }
}
