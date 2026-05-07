namespace DataScrubber.Test.Detection.Rules;

using DataScrubber.Detection;
using DataScrubber.Detection.Rules;
using FluentAssertions;
using Xunit;

public class UrlDetectorTests
{
    private static IReadOnlyList<Detection> Detect(string input)
        => new UrlDetector().Detect(input.AsMemory(), DetectionContext.Empty).ToList();

    [Theory]
    [InlineData("https://internal.acme.local/api")]
    [InlineData("http://example.com")]
    [InlineData("ftp://files.example.org/path")]
    public void DetectsSchemedUrls(string text)
    {
        IReadOnlyList<Detection> detections = Detect(text);
        detections.Should().ContainSingle();
        detections[0].Type.Should().Be(DetectionType.Url);
        detections[0].Length.Should().Be(text.Length);
    }

    [Theory]
    [InlineData("just a sentence")]
    [InlineData("foo://")]
    [InlineData("://no-scheme.com")]
    public void RejectsNonUrls(string text)
    {
        Detect(text).Should().BeEmpty();
    }

    [Fact]
    public void TrimsTrailingPunctuation()
    {
        IReadOnlyList<Detection> detections = Detect("see https://example.com/path.");
        detections.Should().ContainSingle();
        string matchedSegment = "see https://example.com/path."[detections[0].Start..(detections[0].Start + detections[0].Length)];
        matchedSegment.Should().Be("https://example.com/path");
    }

    [Fact]
    public void TaggedWithUrlRule()
    {
        Detect("https://x.io")[0].SourceRule.Should().Be("url.scheme");
    }
}
