namespace DataScrubber.Test.Detection.Rules;

using DataScrubber.Detection;
using DataScrubber.Detection.Rules;
using FluentAssertions;
using Xunit;

public class EmailDetectorTests
{
    private static IReadOnlyList<Detection> Detect(string input)
        => new EmailDetector().Detect(input.AsMemory(), DetectionContext.Empty).ToList();

    [Theory]
    [InlineData("alice@example.com")]
    [InlineData("bob+filter@sub.domain.io")]
    [InlineData("a.b.c@example.com")]
    public void DetectsBasicEmail(string text)
    {
        IReadOnlyList<Detection> detections = Detect(text);
        detections.Should().ContainSingle().Which.Type.Should().Be(DetectionType.Email);
        detections[0].Length.Should().Be(text.Length);
    }

    [Fact]
    public void DetectsIdnDomainAddress()
    {
        IReadOnlyList<Detection> detections = Detect("user@bücher.example");
        detections.Should().ContainSingle();
        detections[0].Type.Should().Be(DetectionType.Email);
    }

    [Fact]
    public void DetectsQuotedLocalPart()
    {
        const string text = "\"weird name\"@x.test";
        IReadOnlyList<Detection> detections = Detect(text);
        detections.Should().ContainSingle();
        detections[0].Length.Should().Be(text.Length);
    }

    [Theory]
    [InlineData("alice@@example.com")]
    [InlineData("not.an.email")]
    [InlineData("no-domain@")]
    [InlineData("@no-local.com")]
    public void RejectsNearMisses(string text)
    {
        Detect(text).Should().BeEmpty();
    }

    [Fact]
    public void DetectionIsTaggedWithEmailRule()
    {
        Detect("a@b.io")[0].SourceRule.Should().Be("email.basic");
    }
}
