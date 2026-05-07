namespace DataScrubber.Test.Replacement;

using DataScrubber.Detection;
using DataScrubber.Replacement;
using FluentAssertions;
using Xunit;

public class OneWayReplacerTests
{
    private static ReplacementResult Replace(string input, IReadOnlyList<Detection> detections)
        => new OneWayReplacer().Replace(input, detections, ReplacerOptions.Default);

    [Fact]
    public void ReplacesSingleDetectionWithTypeTag()
    {
        const string input = "Email alice@example.com from 10.0.1.5";
        IReadOnlyList<Detection> merged = DetectionMerger.Merge(
            RuleBasedDetector.CreateDefault().Detect(input.AsMemory(), DetectionContext.Empty));

        ReplacementResult result = Replace(input, merged);

        result.Output.Should().Be("Email [EMAIL] from [IPV4]");
    }

    [Fact]
    public void NoDetectionsReturnsByteIdenticalInput()
    {
        const string input = "no PII whatsoever, just normal prose.";
        ReplacementResult result = Replace(input, []);
        result.Output.Should().Be(input);
        result.Applied.Should().BeEmpty();
    }

    [Fact]
    public void EmptyInputReturnsEmptyOutput()
    {
        ReplacementResult result = Replace(string.Empty, []);
        result.Output.Should().BeEmpty();
        result.Applied.Should().BeEmpty();
    }

    [Fact]
    public void PreservesCrlfLineEndings()
    {
        string input = "first 10.0.1.5 line\r\nsecond 10.0.1.6 line\r\n";
        IReadOnlyList<Detection> merged = DetectionMerger.Merge(
            RuleBasedDetector.CreateDefault().Detect(input.AsMemory(), DetectionContext.Empty));

        ReplacementResult result = Replace(input, merged);

        result.Output.Should().Be("first [IPV4] line\r\nsecond [IPV4] line\r\n");
    }

    [Fact]
    public void CreditCardTagUsesUnderscore()
    {
        const string input = "card 4111 1111 1111 1111 stop";
        IReadOnlyList<Detection> merged = DetectionMerger.Merge(
            RuleBasedDetector.CreateDefault().Detect(input.AsMemory(), DetectionContext.Empty));

        ReplacementResult result = Replace(input, merged);
        result.Output.Should().Be("card [CREDIT_CARD] stop");
    }

    [Fact]
    public void UserPathReplacementKeepsSurroundingPath()
    {
        const string input = "log at /Users/sampleuser/work/app.log";
        IReadOnlyList<Detection> merged = DetectionMerger.Merge(
            RuleBasedDetector.CreateDefault().Detect(input.AsMemory(), DetectionContext.Empty));

        ReplacementResult result = Replace(input, merged);
        result.Output.Should().Be("log at /Users/[USER_PATH]/work/app.log");
    }

    [Fact]
    public void PasswordAssignmentKeepsKeyword()
    {
        const string input = "password=hunter2 stays diagnostic";
        IReadOnlyList<Detection> merged = DetectionMerger.Merge(
            RuleBasedDetector.CreateDefault().Detect(input.AsMemory(), DetectionContext.Empty));

        ReplacementResult result = Replace(input, merged);
        result.Output.Should().Be("password=[PASSWORD] stays diagnostic");
    }

    [Fact]
    public void PreservesUtf8BomInsideInput()
    {
        const string bom = "﻿";
        string input = bom + "first 10.0.1.5 line\r\n";
        IReadOnlyList<Detection> merged = DetectionMerger.Merge(
            RuleBasedDetector.CreateDefault().Detect(input.AsMemory(), DetectionContext.Empty));

        ReplacementResult result = Replace(input, merged);

        result.Output.Should().StartWith(bom);
        result.Output.Should().Be(bom + "first [IPV4] line\r\n");
    }
}
