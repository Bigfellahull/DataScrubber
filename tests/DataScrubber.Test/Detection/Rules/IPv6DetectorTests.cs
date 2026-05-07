namespace DataScrubber.Test.Detection.Rules;

using DataScrubber.Detection;
using DataScrubber.Detection.Rules;
using FluentAssertions;
using Xunit;

public class IPv6DetectorTests
{
    private static IReadOnlyList<Detection> Detect(string input)
        => new IPv6Detector().Detect(input.AsMemory(), DetectionContext.Empty).ToList();

    [Theory]
    [InlineData("2001:0db8:85a3:0000:0000:8a2e:0370:7334")]
    [InlineData("2001:db8::1")]
    [InlineData("::1")]
    [InlineData("fe80::1ff:fe23:4567:890a")]
    public void DetectsValidAddresses(string text)
    {
        IReadOnlyList<Detection> detections = Detect(text);
        detections.Should().NotBeEmpty();
        detections.Should().Contain(d => d.Length == text.Length);
        detections[0].Type.Should().Be(DetectionType.IPv6);
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("foo:bar")]
    public void RejectsNonAddresses(string text)
    {
        Detect(text).Should().BeEmpty();
    }

    [Fact]
    public void DoesNotMatchSixGroupMacAddress()
    {
        Detect("01:23:45:67:89:ab").Should().BeEmpty();
    }

    [Fact]
    public void TaggedWithIpv6Rule()
    {
        Detect("::1")[0].SourceRule.Should().Be("ipv6.standard");
    }
}
