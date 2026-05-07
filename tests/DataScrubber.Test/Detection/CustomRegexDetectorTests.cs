namespace DataScrubber.Test.Detection;

using DataScrubber.Configuration;
using DataScrubber.Detection;
using FluentAssertions;
using Xunit;

public class CustomRegexDetectorTests
{
    [Fact]
    public void EmittedDetectionsCarryRuleId()
    {
        CustomRegexDetector detector = CustomRegexDetector.Compile([
            new CustomRule
            {
                Id = "ProjectCodename",
                Type = DetectionType.Organization,
                Pattern = @"(?i)\bproject[-_ ]?(orion|atlas|nimbus)\b",
            },
        ]);

        IReadOnlyList<Detection> detected = [.. detector.Detect("Discussed Project Orion and project_atlas".AsMemory(), DetectionContext.Empty)];

        detected.Should().HaveCount(2);
        detected.Should().AllSatisfy(d =>
        {
            d.Type.Should().Be(DetectionType.Organization);
            d.SourceRule.Should().Be("ProjectCodename");
            d.Confidence.Should().Be(0.9);
        });
    }

    [Fact]
    public void DefaultConfidenceIsApplied()
    {
        CustomRegexDetector detector = CustomRegexDetector.Compile([
            new CustomRule { Id = "x", Type = DetectionType.Person, Pattern = "@@" },
        ]);

        IReadOnlyList<Detection> detected = [.. detector.Detect("foo @@ bar".AsMemory(), DetectionContext.Empty)];

        detected.Should().ContainSingle();
        detected[0].Confidence.Should().Be(0.9);
    }

    [Fact]
    public void ExplicitConfidenceOverridesDefault()
    {
        CustomRegexDetector detector = CustomRegexDetector.Compile([
            new CustomRule { Id = "x", Type = DetectionType.Person, Pattern = "@@", Confidence = 0.42 },
        ]);

        IReadOnlyList<Detection> detected = [.. detector.Detect("foo @@ bar".AsMemory(), DetectionContext.Empty)];

        detected[0].Confidence.Should().Be(0.42);
    }

    [Fact]
    public void MalformedRegexThrowsCompileException()
    {
        Action act = () => CustomRegexDetector.Compile([
            new CustomRule { Id = "broken", Type = DetectionType.Person, Pattern = "(?<unterminated" },
        ]);

        CustomRuleCompileException ex = act.Should().Throw<CustomRuleCompileException>().Which;
        ex.RuleIndex.Should().Be(0);
        ex.RuleId.Should().Be("broken");
    }

    [Fact]
    public void EmptyInputProducesNoDetections()
    {
        CustomRegexDetector detector = CustomRegexDetector.Compile([
            new CustomRule { Id = "x", Type = DetectionType.Person, Pattern = "anything" },
        ]);

        detector.Detect(ReadOnlyMemory<char>.Empty, DetectionContext.Empty).Should().BeEmpty();
    }

    [Fact]
    public void EmptyMatchesAreSkipped()
    {
        CustomRegexDetector detector = CustomRegexDetector.Compile([
            new CustomRule { Id = "x", Type = DetectionType.Person, Pattern = "(?=a)" },
        ]);

        detector.Detect("abc".AsMemory(), DetectionContext.Empty).Should().BeEmpty();
    }

    [Fact]
    public void EmptyDetectorYieldsNothing()
    {
        CustomRegexDetector.Empty.Detect("any input".AsMemory(), DetectionContext.Empty).Should().BeEmpty();
    }

    [Fact]
    public void CompileTimeoutIsPositive()
    {
        // Sanity-check the public guard surface so the SPEC's 200 ms ceiling
        // can be tracked from tests.
        CustomRegexDetector.CompileTimeout.Should().Be(TimeSpan.FromMilliseconds(200));
    }
}
