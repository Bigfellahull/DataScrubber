namespace DataScrubber.Test.Configuration;

using System.Text.Json;
using DataScrubber.Configuration;
using DataScrubber.Detection;
using FluentAssertions;
using Xunit;

public class ScrubConfigTests
{
    [Fact]
    public void ParsesMinimalDocument()
    {
        const string json = """{ "schemaVersion": 1 }""";

        ScrubConfig parsed = ScrubConfig.Parse(json);

        parsed.SchemaVersion.Should().Be(1);
        parsed.Rules.Disabled.Should().BeEmpty();
        parsed.Rules.Custom.Should().BeEmpty();
        parsed.Dictionaries.Should().BeEmpty();
        parsed.AllowList.Should().BeEmpty();
        parsed.Ner.Thresholds.Should().BeEmpty();
    }

    [Fact]
    public void ParsesFullSampleDocument()
    {
        string sample = File.ReadAllText(LocateSamplePath());

        ScrubConfig parsed = ScrubConfig.Parse(sample);

        parsed.SchemaVersion.Should().Be(1);
        parsed.Rules.Disabled.Should().BeEquivalentTo(["MacAddress"]);
        parsed.Rules.Custom.Should().HaveCount(1);
        parsed.Rules.Custom[0].Id.Should().Be("ProjectCodename");
        parsed.Rules.Custom[0].Type.Should().Be(DetectionType.Organization);
        parsed.Rules.Custom[0].Confidence.Should().Be(0.9);
        parsed.Dictionaries[DetectionType.Organization].Should().BeEquivalentTo(["Acme Corp", "Initech"]);
        parsed.AllowList.Should().Contain("noreply@example.com");
        parsed.Ner.Thresholds[DetectionType.Person].Should().Be(0.85);
        parsed.Ner.Thresholds[DetectionType.Location].Should().Be(0.80);
    }

    [Fact]
    public void RejectsUnknownTopLevelKey()
    {
        const string json = """{ "schemaVersion": 1, "extraneous": true }""";

        Action act = () => ScrubConfig.Parse(json);

        ScrubConfigException ex = act.Should().Throw<ScrubConfigException>().Which;
        ex.Message.Should().StartWith("Config error at ");
        ex.Message.Should().Contain("extraneous");
    }

    [Fact]
    public void RejectsUnknownNestedKey()
    {
        const string json = """{ "schemaVersion": 1, "rules": { "disabled": [], "extraneous": [] } }""";

        Action act = () => ScrubConfig.Parse(json);

        act.Should().Throw<ScrubConfigException>()
            .Which.Message.Should().Contain("extraneous");
    }

    [Fact]
    public void RejectsUnknownDetectionTypeInDictionaries()
    {
        const string json = """{ "schemaVersion": 1, "dictionaries": { "Bogus": ["foo"] } }""";

        Action act = () => ScrubConfig.Parse(json);

        act.Should().Throw<ScrubConfigException>();
    }

    [Fact]
    public void RejectsUnknownDetectionTypeInCustomRule()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "rules": { "custom": [ { "id": "x", "type": "Bogus", "pattern": ".*" } ] }
            }
            """;

        Action act = () => ScrubConfig.Parse(json);

        act.Should().Throw<ScrubConfigException>()
            .Which.Message.Should().Contain("rules");
    }

    [Fact]
    public void RejectsThresholdAboveOne()
    {
        const string json = """{ "schemaVersion": 1, "ner": { "thresholds": { "Person": 1.5 } } }""";

        Action act = () => ScrubConfig.Parse(json);

        ScrubConfigException ex = act.Should().Throw<ScrubConfigException>().Which;
        ex.JsonPath.Should().Be("$.ner.thresholds.Person");
        ex.Message.Should().Contain("(0.0, 1.0]");
    }

    [Fact]
    public void RejectsThresholdAtOrBelowZero()
    {
        const string json = """{ "schemaVersion": 1, "ner": { "thresholds": { "Organization": 0 } } }""";

        Action act = () => ScrubConfig.Parse(json);

        act.Should().Throw<ScrubConfigException>()
            .Which.JsonPath.Should().Be("$.ner.thresholds.Organization");
    }

    [Fact]
    public void RejectsUnsupportedSchemaVersion()
    {
        const string json = """{ "schemaVersion": 2 }""";

        Action act = () => ScrubConfig.Parse(json);

        ScrubConfigException ex = act.Should().Throw<ScrubConfigException>().Which;
        ex.JsonPath.Should().Be("$.schemaVersion");
        ex.Message.Should().Contain("schemaVersion");
    }

    [Fact]
    public void RejectsMissingSchemaVersion()
    {
        const string json = """{ "allowList": ["x"] }""";

        Action act = () => ScrubConfig.Parse(json);

        act.Should().Throw<ScrubConfigException>();
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        ScrubConfig original = ScrubConfig.Parse(File.ReadAllText(LocateSamplePath()));

        string json = JsonSerializer.Serialize(original, ScrubConfig.JsonOptions);
        ScrubConfig roundTripped = ScrubConfig.Parse(json);

        roundTripped.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void DefaultsHaveCurrentSchemaVersion()
    {
        ScrubConfig.Defaults.SchemaVersion.Should().Be(ScrubConfig.CurrentSchemaVersion);
    }

    private static string LocateSamplePath()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "samples", "scrub.config.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("scrub.config.json sample not found above the test runner directory.");
    }
}
