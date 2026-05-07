namespace DataScrubber.Test.Detection.Rules;

using DataScrubber.Detection;
using DataScrubber.Detection.Rules;
using FluentAssertions;
using Xunit;

public class PasswordAssignmentDetectorTests
{
    private static IReadOnlyList<Detection> Detect(string input)
        => new PasswordAssignmentDetector().Detect(input.AsMemory(), DetectionContext.Empty).ToList();

    [Fact]
    public void DetectsBareValueAfterEquals()
    {
        const string text = "password=hunter2";
        IReadOnlyList<Detection> detections = Detect(text);
        detections.Should().ContainSingle();
        Detection d = detections[0];
        text.Substring(d.Start, d.Length).Should().Be("hunter2");
        d.SourceRule.Should().Be("password.assignment");
    }

    [Fact]
    public void DetectsQuotedValueAfterColon()
    {
        const string text = "api_key: \"abcdef\"";
        IReadOnlyList<Detection> detections = Detect(text);
        detections.Should().ContainSingle();
        text.Substring(detections[0].Start, detections[0].Length).Should().Be("\"abcdef\"");
    }

    [Fact]
    public void DetectsMultiWordQuotedValue()
    {
        const string text = "secret = \"two words\"";
        IReadOnlyList<Detection> detections = Detect(text);
        detections.Should().ContainSingle();
        text.Substring(detections[0].Start, detections[0].Length).Should().Be("\"two words\"");
    }

    [Fact]
    public void DoesNotMatchKeywordWithoutAssignment()
    {
        Detect("password requirements: must be 8+ chars").Should().BeEmpty();
    }
}
