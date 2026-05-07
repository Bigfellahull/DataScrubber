namespace DataScrubber.Test.Detection.Rules;

using DataScrubber.Detection;
using DataScrubber.Detection.Rules;
using FluentAssertions;
using Xunit;

public class MacAddressDetectorTests
{
    private static IReadOnlyList<Detection> Detect(string input)
        => new MacAddressDetector().Detect(input.AsMemory(), DetectionContext.Empty).ToList();

    [Theory]
    [InlineData("01:23:45:67:89:ab")]
    [InlineData("AA-BB-CC-DD-EE-FF")]
    [InlineData("ff:ee:dd:cc:bb:aa")]
    public void DetectsMacAddresses(string text)
    {
        IReadOnlyList<Detection> detections = Detect(text);
        detections.Should().ContainSingle();
        detections[0].Type.Should().Be(DetectionType.MacAddress);
        detections[0].SourceRule.Should().Be("mac.colon");
    }

    [Theory]
    [InlineData("not a mac address")]
    [InlineData("01:23:45:67:89")]
    [InlineData("01:23:45:67:89:zz")]
    public void RejectsNonMacAddresses(string text)
    {
        Detect(text).Should().BeEmpty();
    }
}
