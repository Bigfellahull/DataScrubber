namespace DataScrubber.Test.Detection.Rules;

using DataScrubber.Detection;
using DataScrubber.Detection.Rules;
using FluentAssertions;
using Xunit;

public class IPv4DetectorTests
{
    private static IReadOnlyList<Detection> Detect(string input)
        => new IPv4Detector().Detect(input.AsMemory(), DetectionContext.Empty).ToList();

    [Theory]
    [InlineData("10.0.1.5")]
    [InlineData("192.168.1.255")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    public void DetectsValidAddresses(string text)
    {
        IReadOnlyList<Detection> detections = Detect(text);
        detections.Should().ContainSingle();
        detections[0].Type.Should().Be(DetectionType.IPv4);
        detections[0].Length.Should().Be(text.Length);
    }

    [Theory]
    [InlineData("999.999.999.999")]
    [InlineData("256.0.0.1")]
    [InlineData("1.2.3")]
    public void RejectsInvalidAddresses(string text)
    {
        Detect(text).Should().BeEmpty();
    }

    [Fact]
    public void FindsAddressInsideLogLine()
    {
        IReadOnlyList<Detection> detections = Detect("connect to 10.0.1.5:443");
        detections.Should().ContainSingle();
        detections[0].Start.Should().Be("connect to ".Length);
        detections[0].Length.Should().Be("10.0.1.5".Length);
    }

    [Fact]
    public void TaggedWithIpv4Rule()
    {
        Detect("1.2.3.4")[0].SourceRule.Should().Be("ipv4.dotted");
    }
}
