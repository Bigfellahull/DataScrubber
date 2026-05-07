namespace DataScrubber.Test.Detection;

using DataScrubber.Detection;
using FluentAssertions;
using Xunit;

public class DetectionMergerTests
{
    [Fact]
    public void NonOverlappingDetectionsArePreserved()
    {
        IReadOnlyList<Detection> merged = DetectionMerger.Merge(
        [
            new Detection(0, 5, DetectionType.Email, 1.0, "a"),
            new Detection(10, 4, DetectionType.IPv4, 1.0, "b"),
        ]);

        merged.Should().HaveCount(2);
    }

    [Fact]
    public void LongerSpanWinsOnOverlap()
    {
        IReadOnlyList<Detection> merged = DetectionMerger.Merge(
        [
            new Detection(0, 5, DetectionType.Email, 1.0, "short"),
            new Detection(0, 12, DetectionType.Url, 1.0, "long"),
        ]);

        merged.Should().ContainSingle();
        merged[0].Length.Should().Be(12);
    }

    [Fact]
    public void HigherConfidenceWinsOnEqualLength()
    {
        IReadOnlyList<Detection> merged = DetectionMerger.Merge(
        [
            new Detection(0, 6, DetectionType.ApiKey, 0.7, "low"),
            new Detection(0, 6, DetectionType.ApiKey, 1.0, "high"),
        ]);

        merged.Should().ContainSingle();
        merged[0].SourceRule.Should().Be("high");
    }

    [Fact]
    public void RulePriorityWinsOverNerOnEqualLengthAndConfidence()
    {
        IReadOnlyList<Detection> merged = DetectionMerger.Merge(
        [
            new Detection(0, 6, DetectionType.Person, 1.0, "ner"),
            new Detection(0, 6, DetectionType.Email,  1.0, "rule"),
        ]);

        merged.Should().ContainSingle();
        merged[0].Type.Should().Be(DetectionType.Email);
    }

    [Fact]
    public void AdjacentDetectionsAreNotMerged()
    {
        IReadOnlyList<Detection> merged = DetectionMerger.Merge(
        [
            new Detection(0, 5, DetectionType.Email, 1.0, "a"),
            new Detection(5, 5, DetectionType.Email, 1.0, "b"),
        ]);

        merged.Should().HaveCount(2);
    }

    [Fact]
    public void OutputIsSortedByStart()
    {
        IReadOnlyList<Detection> merged = DetectionMerger.Merge(
        [
            new Detection(20, 4, DetectionType.IPv4, 1.0, "b"),
            new Detection(0,  5, DetectionType.Email, 1.0, "a"),
        ]);

        merged.Select(d => d.Start).Should().BeInAscendingOrder();
    }

    [Fact]
    public void EmptyInputProducesEmptyOutput()
    {
        DetectionMerger.Merge([]).Should().BeEmpty();
    }

    [Fact]
    public void RuleVsRuleOverlapResolvesToLongerSpanInRealisticInput()
    {
        const string input = "see https://user@example.com/path for details";
        IEnumerable<Detection> raw = RuleBasedDetector.CreateDefault().Detect(input.AsMemory(), DetectionContext.Empty);
        IReadOnlyList<Detection> merged = DetectionMerger.Merge(raw);

        Detection over = merged.Single(d => input.AsSpan(d.Start, d.Length).Contains("example.com", StringComparison.Ordinal));
        over.Type.Should().Be(DetectionType.Url);
        input.Substring(over.Start, over.Length).Should().Be("https://user@example.com/path");
    }

    [Fact]
    public void RuleEmailWinsOverNerPersonOnIdenticalSpan()
    {
        // V9 boundary case: identical-length overlap. The rule detector wins
        // on the shared "sarah@acme.com" span via the rules > NER priority,
        // even though both produce the same span at confidence 1.0.
        IReadOnlyList<Detection> merged = DetectionMerger.Merge(
        [
            new Detection(0, 14, DetectionType.Email,  1.0, "email.basic"),
            new Detection(0, 14, DetectionType.Person, 1.0, "ner.onnx"),
        ]);

        merged.Should().ContainSingle();
        merged[0].Type.Should().Be(DetectionType.Email);
    }

    [Fact]
    public void NerPersonSurvivesNonOverlappingPositionWhenEmailRuleWinsItsOwnSpan()
    {
        // V9: "Email Sarah at sarah@acme.com" — rule beats NER on the email
        // span; the standalone "Sarah" is non-overlapping and survives.
        IReadOnlyList<Detection> merged = DetectionMerger.Merge(
        [
            new Detection(6, 5,  DetectionType.Person, 1.0, "ner.onnx"),
            new Detection(15, 14, DetectionType.Email, 1.0, "email.basic"),
            new Detection(15, 5,  DetectionType.Person, 0.95, "ner.onnx"),
        ]);

        merged.Should().HaveCount(2);
        merged[0].Type.Should().Be(DetectionType.Person);
        merged[0].Start.Should().Be(6);
        merged[1].Type.Should().Be(DetectionType.Email);
        merged[1].Start.Should().Be(15);
    }

    [Fact]
    public void LongerNerSpanWinsOverShorterRuleSpanOnDifferentLengthOverlap()
    {
        // The merger's "longer span wins" rule applies regardless of source.
        // A multi-word NER span (e.g. "Acme Corp Berlin") that swallows a
        // short rule span keeps the longer NER detection.
        IReadOnlyList<Detection> merged = DetectionMerger.Merge(
        [
            new Detection(0, 5, DetectionType.Email, 1.0, "email.basic"),
            new Detection(0, 16, DetectionType.Organization, 0.92, "ner.onnx"),
        ]);

        merged.Should().ContainSingle();
        merged[0].Type.Should().Be(DetectionType.Organization);
    }
}
