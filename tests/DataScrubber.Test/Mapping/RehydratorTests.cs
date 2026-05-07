namespace DataScrubber.Test.Mapping;

using DataScrubber.Detection;
using DataScrubber.Mapping;
using FluentAssertions;
using Xunit;

public class RehydratorTests
{
    [Fact]
    public void SubstitutesKnownTokens()
    {
        MappingFile mapping = new(
            1,
            DateTimeOffset.UtcNow,
            "abc",
            [
                new MappingEntry("[PERSON_001]", "Alice", DetectionType.Person, 1),
                new MappingEntry("[EMAIL_001]", "alice@x.test", DetectionType.Email, 1),
            ]);

        RehydrationResult result = new Rehydrator()
            .Rehydrate("Hi [PERSON_001], please confirm at [EMAIL_001].", mapping);

        result.Output.Should().Be("Hi Alice, please confirm at alice@x.test.");
        result.UnknownTokens.Should().BeEmpty();
    }

    [Fact]
    public void IsIdempotent()
    {
        MappingFile mapping = new(
            1,
            DateTimeOffset.UtcNow,
            "abc",
            [new MappingEntry("[PERSON_001]", "Alice", DetectionType.Person, 1)]);

        Rehydrator rehydrator = new();
        RehydrationResult first = rehydrator.Rehydrate("Hi [PERSON_001].", mapping);
        RehydrationResult second = rehydrator.Rehydrate(first.Output, mapping);

        second.Output.Should().Be(first.Output);
    }

    [Fact]
    public void ReportsUnknownTokensAndLeavesThemInPlace()
    {
        MappingFile mapping = new(
            1,
            DateTimeOffset.UtcNow,
            "abc",
            [new MappingEntry("[PERSON_001]", "Alice", DetectionType.Person, 1)]);

        RehydrationResult result = new Rehydrator()
            .Rehydrate("Hi [PERSON_001] and [PERSON_999].", mapping);

        result.Output.Should().Be("Hi Alice and [PERSON_999].");
        result.UnknownTokens.Should().BeEquivalentTo(["[PERSON_999]"]);
    }

    [Fact]
    public void DeduplicatesUnknownTokenReports()
    {
        MappingFile mapping = new(1, DateTimeOffset.UtcNow, "abc", []);

        RehydrationResult result = new Rehydrator()
            .Rehydrate("[PERSON_001] [PERSON_001] [EMAIL_001]", mapping);

        result.UnknownTokens.Should().BeEquivalentTo(["[PERSON_001]", "[EMAIL_001]"]);
    }

    [Fact]
    public void HandlesCompoundTypeNames()
    {
        MappingFile mapping = new(
            1,
            DateTimeOffset.UtcNow,
            "abc",
            [new MappingEntry("[CREDIT_CARD_001]", "4111111111111111", DetectionType.CreditCard, 1)]);

        RehydrationResult result = new Rehydrator().Rehydrate("[CREDIT_CARD_001]", mapping);

        result.Output.Should().Be("4111111111111111");
    }

    [Fact]
    public void AcceptsTokensWithMoreThanThreeDigits()
    {
        MappingFile mapping = new(
            1,
            DateTimeOffset.UtcNow,
            "abc",
            [new MappingEntry("[PERSON_1000]", "Zoe", DetectionType.Person, 1)]);

        RehydrationResult result = new Rehydrator().Rehydrate("Hi [PERSON_1000]", mapping);

        result.Output.Should().Be("Hi Zoe");
    }
}
