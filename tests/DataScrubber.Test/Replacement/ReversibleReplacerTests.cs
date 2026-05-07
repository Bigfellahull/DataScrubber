namespace DataScrubber.Test.Replacement;

using System.Text;
using DataScrubber.Detection;
using DataScrubber.Mapping;
using DataScrubber.Replacement;
using FluentAssertions;
using Xunit;

public class ReversibleReplacerTests
{
    private static ReversibleReplacementResult Replace(string input, IReadOnlyList<Detection> detections)
        => new ReversibleReplacer().ReplaceWithMapping(input, detections, ReplacerOptions.Default);

    private static IReadOnlyList<Detection> Merge(string input)
        => DetectionMerger.Merge(
            RuleBasedDetector.CreateDefault().Detect(input.AsMemory(), DetectionContext.Empty));

    [Fact]
    public void IdenticalOriginalsShareToken()
    {
        const string input = "alice@x.test wrote alice@x.test";
        IReadOnlyList<Detection> merged = Merge(input);

        ReversibleReplacementResult result = Replace(input, merged);

        result.Output.Should().Be("[EMAIL_001] wrote [EMAIL_001]");
        result.Entries.Should().HaveCount(1);
        result.Entries[0].Token.Should().Be("[EMAIL_001]");
        result.Entries[0].Original.Should().Be("alice@x.test");
        result.Entries[0].Type.Should().Be(DetectionType.Email);
        result.Entries[0].Occurrences.Should().Be(2);
    }

    [Fact]
    public void DistinctOriginalsGetDistinctTokens()
    {
        const string input = "alice@x.test, bob@x.test, alice@x.test";
        IReadOnlyList<Detection> merged = Merge(input);

        ReversibleReplacementResult result = Replace(input, merged);

        result.Output.Should().Be("[EMAIL_001], [EMAIL_002], [EMAIL_001]");
        result.Entries.Should().HaveCount(2);
        result.Entries[0].Token.Should().Be("[EMAIL_001]");
        result.Entries[0].Occurrences.Should().Be(2);
        result.Entries[1].Token.Should().Be("[EMAIL_002]");
        result.Entries[1].Occurrences.Should().Be(1);
    }

    [Fact]
    public void PerTypeCountersAreIndependent()
    {
        const string input = "alice@x.test from 10.0.1.5 then bob@x.test from 10.0.1.6";
        IReadOnlyList<Detection> merged = Merge(input);

        ReversibleReplacementResult result = Replace(input, merged);

        result.Output.Should().Be("[EMAIL_001] from [IPV4_001] then [EMAIL_002] from [IPV4_002]");
        result.Entries.Select(e => e.Token).Should().Equal(
            "[EMAIL_001]", "[IPV4_001]", "[EMAIL_002]", "[IPV4_002]");
    }

    [Fact]
    public void TokenFormatIsZeroPadded()
    {
        const string input = "alice@x.test";
        IReadOnlyList<Detection> merged = Merge(input);

        ReversibleReplacementResult result = Replace(input, merged);

        result.Output.Should().Be("[EMAIL_001]");
    }

    [Fact]
    public void CrossTypeIdenticalOriginalsShareToken()
    {
        const string input = "Alice Alice";
        List<Detection> detections =
        [
            new Detection(0, 5, DetectionType.Person, 0.9, "first"),
            new Detection(6, 5, DetectionType.Organization, 0.9, "second"),
        ];

        ReversibleReplacementResult result = Replace(input, detections);

        result.Output.Should().Be("[PERSON_001] [PERSON_001]");
        result.Entries.Should().ContainSingle();
        result.Entries[0].Token.Should().Be("[PERSON_001]");
        result.Entries[0].Type.Should().Be(DetectionType.Person, "the first detection's type wins for the entry");
        result.Entries[0].Occurrences.Should().Be(2);
    }

    [Fact]
    public void CaseSensitiveDeduplication()
    {
        const string input = "alice@x.test wrote ALICE@X.TEST";
        IReadOnlyList<Detection> merged = Merge(input);

        ReversibleReplacementResult result = Replace(input, merged);

        result.Entries.Should().HaveCount(2, "byte-exact, case-sensitive deduplication is documented behaviour");
    }

    [Fact]
    public void NoDetectionsReturnsByteIdenticalInputAndEmptyMapping()
    {
        const string input = "no PII whatsoever, just normal prose.";
        ReversibleReplacementResult result = Replace(input, []);

        result.Output.Should().Be(input);
        result.Applied.Should().BeEmpty();
        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public void EmptyInputReturnsEmptyOutputAndEmptyMapping()
    {
        ReversibleReplacementResult result = Replace(string.Empty, []);

        result.Output.Should().BeEmpty();
        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public void CounterGrowsPast999WithoutTruncation()
    {
        const int distinctCount = 1000;
        StringBuilder builder = new();
        List<Detection> detections = new(distinctCount);
        for (int i = 0; i < distinctCount; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            int start = builder.Length;
            string name = $"p{i:D4}";
            builder.Append(name);
            detections.Add(new Detection(start, name.Length, DetectionType.Person, 1.0, "test"));
        }

        ReversibleReplacementResult result = Replace(builder.ToString(), detections);

        result.Entries.Should().HaveCount(distinctCount);
        result.Entries[0].Token.Should().Be("[PERSON_001]");
        result.Entries[998].Token.Should().Be("[PERSON_999]");
        result.Entries[^1].Token.Should().Be("[PERSON_1000]");
        result.Output.Should().EndWith("[PERSON_1000]");
    }

    [Fact]
    public void AllocatedTokenSkipsLiteralCollisionOutsideDetection()
    {
        const string input = "alice@example.com [EMAIL_001]";
        List<Detection> detections =
        [
            new Detection(0, 17, DetectionType.Email, 1.0, "test"),
        ];

        ReversibleReplacementResult result = Replace(input, detections);

        result.Output.Should().Be("[EMAIL_002] [EMAIL_001]",
            "the literal [EMAIL_001] outside the email span reserves that counter slot");
        result.Entries.Should().ContainSingle().Which.Token.Should().Be("[EMAIL_002]");
    }

    [Fact]
    public void AllocatorSkipsMultipleLiteralCollisions()
    {
        const string input = "[EMAIL_001] [EMAIL_002] alice@example.com";
        List<Detection> detections =
        [
            new Detection(24, 17, DetectionType.Email, 1.0, "test"),
        ];

        ReversibleReplacementResult result = Replace(input, detections);

        result.Output.Should().Be("[EMAIL_001] [EMAIL_002] [EMAIL_003]");
    }

    [Fact]
    public void LiteralTokenInsideDetectionDoesNotReserveCounter()
    {
        const string input = "[EMAIL_001]";
        List<Detection> detections =
        [
            new Detection(0, input.Length, DetectionType.Email, 1.0, "test"),
        ];

        // The literal IS the detection span, so it would also be a token-shaped
        // original — caught by the existing TokenShapedOriginalException, not
        // by reservation.
        Action act = () => Replace(input, detections);
        act.Should().Throw<TokenShapedOriginalException>();
    }

    [Fact]
    public void RejectsTokenShapedOriginal()
    {
        const string input = "[EMAIL_001]";
        List<Detection> detections =
        [
            new Detection(0, input.Length, DetectionType.Password, 1.0, "test"),
        ];

        Action act = () => Replace(input, detections);
        act.Should().Throw<TokenShapedOriginalException>()
            .Where(ex => ex.Original == "[EMAIL_001]");
    }

    [Fact]
    public void RejectsOriginalContainingTokenShape()
    {
        const string input = "prefix-[PERSON_042]-suffix";
        List<Detection> detections =
        [
            new Detection(0, input.Length, DetectionType.Password, 1.0, "test"),
        ];

        Action act = () => Replace(input, detections);
        act.Should().Throw<TokenShapedOriginalException>();
    }

    [Fact]
    public void ImplementsIReplacerInterface()
    {
        const string input = "alice@x.test";
        IReadOnlyList<Detection> merged = Merge(input);

        IReplacer replacer = new ReversibleReplacer();
        ReplacementResult result = replacer.Replace(input, merged, ReplacerOptions.Default);

        result.Output.Should().Be("[EMAIL_001]");
    }
}
