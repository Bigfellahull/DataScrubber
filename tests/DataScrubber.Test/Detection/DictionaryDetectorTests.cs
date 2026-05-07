namespace DataScrubber.Test.Detection;

using System.Text;
using DataScrubber.Detection;
using FluentAssertions;
using Xunit;

public class DictionaryDetectorTests
{
    [Fact]
    public void DetectsSingleEntry()
    {
        DictionaryDetector detector = new(new Dictionary<DetectionType, IReadOnlyList<string>>
        {
            [DetectionType.Organization] = ["Initech"],
        });

        IReadOnlyList<Detection> detected = [.. detector.Detect("I joined Initech yesterday".AsMemory(), DetectionContext.Empty)];

        detected.Should().ContainSingle();
        detected[0].Type.Should().Be(DetectionType.Organization);
        detected[0].Start.Should().Be(9);
        detected[0].Length.Should().Be("Initech".Length);
        detected[0].SourceRule.Should().StartWith("dict.");
    }

    [Fact]
    public void DetectsMultiWordEntry()
    {
        DictionaryDetector detector = new(new Dictionary<DetectionType, IReadOnlyList<string>>
        {
            [DetectionType.Organization] = ["Acme Corp"],
        });

        IReadOnlyList<Detection> detected = [.. detector.Detect("Visited Acme Corp yesterday".AsMemory(), DetectionContext.Empty)];

        detected.Should().ContainSingle();
        detected[0].Length.Should().Be("Acme Corp".Length);
    }

    [Fact]
    public void IsCaseSensitive()
    {
        DictionaryDetector detector = new(new Dictionary<DetectionType, IReadOnlyList<string>>
        {
            [DetectionType.Organization] = ["Initech"],
        });

        IReadOnlyList<Detection> detected = [.. detector.Detect("I joined initech yesterday".AsMemory(), DetectionContext.Empty)];

        detected.Should().BeEmpty();
    }

    [Fact]
    public void HonoursWordBoundaries()
    {
        DictionaryDetector detector = new(new Dictionary<DetectionType, IReadOnlyList<string>>
        {
            [DetectionType.Organization] = ["Initech"],
        });

        IReadOnlyList<Detection> detected = [.. detector.Detect("InitechCorp is a substring".AsMemory(), DetectionContext.Empty)];

        detected.Should().BeEmpty();
    }

    [Fact]
    public void LongestEntryWinsAtStartOfMatch()
    {
        DictionaryDetector detector = new(new Dictionary<DetectionType, IReadOnlyList<string>>
        {
            [DetectionType.Organization] = ["Acme", "Acme Corp"],
        });

        IReadOnlyList<Detection> detected = [.. detector.Detect("Visited Acme Corp yesterday".AsMemory(), DetectionContext.Empty)];

        detected.Should().ContainSingle();
        detected[0].Length.Should().Be("Acme Corp".Length);
    }

    [Fact]
    public void EmptyDictionaryYieldsNoDetections()
    {
        DictionaryDetector detector = new(new Dictionary<DetectionType, IReadOnlyList<string>>());

        IReadOnlyList<Detection> detected = [.. detector.Detect("anything goes here".AsMemory(), DetectionContext.Empty)];

        detected.Should().BeEmpty();
    }

    [Fact]
    public void DetectsMultipleTypesInSameInput()
    {
        DictionaryDetector detector = new(new Dictionary<DetectionType, IReadOnlyList<string>>
        {
            [DetectionType.Organization] = ["Initech"],
            [DetectionType.Person] = ["Bill"],
        });

        IReadOnlyList<Detection> detected = [.. detector.Detect("Bill joined Initech".AsMemory(), DetectionContext.Empty)];

        detected.Should().HaveCount(2);
        detected.Select(d => d.Type).Should().Contain([DetectionType.Person, DetectionType.Organization]);
    }

    [Fact]
    public void MatchesEntryRegardlessOfInputUnicodeNormalisation()
    {
        // "Café" entry (NFC) must match both NFC and NFD inputs.
        const string entryNfc = "Café";
        DictionaryDetector detector = new(new Dictionary<DetectionType, IReadOnlyList<string>>
        {
            [DetectionType.Organization] = [entryNfc],
        });

        string nfdInput = ("Visited " + entryNfc + " yesterday").Normalize(NormalizationForm.FormD);
        IReadOnlyList<Detection> detected = [.. detector.Detect(nfdInput.AsMemory(), DetectionContext.Empty)];

        detected.Should().ContainSingle();
        nfdInput.Substring(detected[0].Start, detected[0].Length)
            .Should().Be(entryNfc.Normalize(NormalizationForm.FormD));
    }
}
