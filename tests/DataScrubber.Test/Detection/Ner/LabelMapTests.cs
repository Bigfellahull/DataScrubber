namespace DataScrubber.Test.Detection.Ner;

using DataScrubber.Detection;
using DataScrubber.Detection.Ner;
using FluentAssertions;
using Xunit;

public class LabelMapTests
{
    [Fact]
    public void ArrayLabelsAreParsedToBioPrefixesAndDetectionTypes()
    {
        LabelMap map = new(["O", "B-PER", "I-PER", "B-ORG", "I-ORG", "B-LOC", "I-LOC"]);

        map[0].Should().Be(BioLabel.Outside);
        map[1].Should().Be(new BioLabel(BioPrefix.Begin, DetectionType.Person, "B-PER"));
        map[2].Should().Be(new BioLabel(BioPrefix.Inside, DetectionType.Person, "I-PER"));
        map[3].Should().Be(new BioLabel(BioPrefix.Begin, DetectionType.Organization, "B-ORG"));
        map[4].Should().Be(new BioLabel(BioPrefix.Inside, DetectionType.Organization, "I-ORG"));
        map[5].Should().Be(new BioLabel(BioPrefix.Begin, DetectionType.Location, "B-LOC"));
        map[6].Should().Be(new BioLabel(BioPrefix.Inside, DetectionType.Location, "I-LOC"));
    }

    [Fact]
    public void LongFormSynonymsParseToTheSameDetectionType()
    {
        LabelMap map = new(["B-PERSON", "I-ORGANIZATION", "B-LOCATION"]);

        map[0].Type.Should().Be(DetectionType.Person);
        map[1].Type.Should().Be(DetectionType.Organization);
        map[2].Type.Should().Be(DetectionType.Location);
    }

    [Fact]
    public void NonTargetedLabelsCollapseToOutside()
    {
        LabelMap map = new(["O", "B-MISC", "I-MISC", "B-DATE"]);

        map[1].Should().Be(BioLabel.Outside with { Raw = "B-MISC" });
        map[2].Should().Be(BioLabel.Outside with { Raw = "I-MISC" });
        map[3].Should().Be(BioLabel.Outside with { Raw = "B-DATE" });
    }

    [Fact]
    public void LoadAcceptsArrayJson()
    {
        string path = Path.Combine(Path.GetTempPath(), $"labels-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """["O","B-PER","I-PER"]""");
        try
        {
            LabelMap map = LabelMap.Load(path);
            map.Count.Should().Be(3);
            map[1].Type.Should().Be(DetectionType.Person);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadAcceptsIndexedObjectJson()
    {
        string path = Path.Combine(Path.GetTempPath(), $"labels-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"0":"O","1":"B-PER","2":"I-PER"}""");
        try
        {
            LabelMap map = LabelMap.Load(path);
            map.Count.Should().Be(3);
            map[1].Type.Should().Be(DetectionType.Person);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadOnMissingFileThrowsNerModelLoadExceptionWithPath()
    {
        string nonexistent = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");
        FluentActions.Invoking(() => LabelMap.Load(nonexistent))
            .Should().Throw<NerModelLoadException>()
            .Where(ex => ex.MissingPath == nonexistent);
    }

    [Fact]
    public void LoadOnNonStringLabelValuesThrowsNerModelLoadException()
    {
        // GetString() throws InvalidOperationException for non-string values;
        // the loader must wrap it so the CLI exits 4 (not generic 1).
        AssertLoadFails("""["O", 5]""");
    }

    [Fact]
    public void LoadOnNonDecimalIndexedKeyThrowsNerModelLoadException()
    {
        AssertLoadFails("""{"foo": "O"}""");
    }

    [Fact]
    public void LoadOnNonContiguousIndicesThrowsNerModelLoadException()
    {
        AssertLoadFails("""{"0": "O", "2": "B-PER"}""");
    }

    [Fact]
    public void LoadOnMalformedJsonThrowsNerModelLoadException()
    {
        AssertLoadFails("not json");
    }

    [Fact]
    public void LoadOnRootKindThatIsNeitherArrayNorObjectThrowsNerModelLoadException()
    {
        AssertLoadFails("\"O\"");
    }

    private static void AssertLoadFails(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"labels-bad-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        try
        {
            FluentActions.Invoking(() => LabelMap.Load(path))
                .Should().Throw<NerModelLoadException>()
                .Where(ex => ex.MissingPath == path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
