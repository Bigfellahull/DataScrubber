namespace DataScrubber.Test.Mapping;

using System.Text;
using System.Text.Json;
using DataScrubber.Detection;
using DataScrubber.Mapping;
using FluentAssertions;
using Xunit;

public class MappingFileTests
{
    [Fact]
    public void RoundTripsThroughDisk()
    {
        MappingFile original = new(
            MappingFile.CurrentSchemaVersion,
            new DateTimeOffset(2026, 5, 7, 14, 0, 0, TimeSpan.Zero),
            new string('a', 64),
            [
                new MappingEntry("[PERSON_001]", "John Smith", DetectionType.Person, 3),
                new MappingEntry("[EMAIL_001]", "john@x.test", DetectionType.Email, 1),
            ]);

        string path = Path.Combine(Path.GetTempPath(), $"map-{Guid.NewGuid():N}.json");
        try
        {
            MappingFileWriter.Write(original, path);
            MappingFile read = MappingFileReader.Read(path);

            read.SchemaVersion.Should().Be(original.SchemaVersion);
            read.CreatedAt.Should().Be(original.CreatedAt);
            read.SourceSha256.Should().Be(original.SourceSha256);
            read.Entries.Should().BeEquivalentTo(original.Entries);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void JsonUsesCamelCaseAndStringEnums()
    {
        MappingFile mapping = new(
            1,
            new DateTimeOffset(2026, 5, 7, 14, 0, 0, TimeSpan.Zero),
            "abc",
            [new MappingEntry("[PERSON_001]", "John", DetectionType.Person, 1)]);

        string path = Path.Combine(Path.GetTempPath(), $"map-{Guid.NewGuid():N}.json");
        try
        {
            MappingFileWriter.Write(mapping, path);
            string json = File.ReadAllText(path);

            json.Should().Contain("\"schemaVersion\":");
            json.Should().Contain("\"createdAt\":");
            json.Should().Contain("\"sourceSha256\":");
            json.Should().Contain("\"entries\":");
            json.Should().Contain("\"token\":");
            json.Should().Contain("\"type\": \"Person\"");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void StrictReaderRejectsUnknownMember()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "createdAt": "2026-05-07T14:00:00+00:00",
              "sourceSha256": "abc",
              "entries": [],
              "extraField": "not allowed"
            }
            """;

        string path = Path.Combine(Path.GetTempPath(), $"map-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json, new UTF8Encoding(false));
            Action act = () => MappingFileReader.Read(path);
            act.Should().Throw<JsonException>();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void RejectsUnsupportedSchemaVersion()
    {
        const string json = """
            {
              "schemaVersion": 2,
              "createdAt": "2026-05-07T14:00:00+00:00",
              "sourceSha256": "abc",
              "entries": []
            }
            """;

        string path = Path.Combine(Path.GetTempPath(), $"map-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json, new UTF8Encoding(false));
            Action act = () => MappingFileReader.Read(path);
            act.Should()
                .Throw<UnsupportedMappingSchemaException>()
                .Where(ex => ex.SchemaVersion == 2)
                .WithMessage("*Map schema version 2 is not supported*");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void RejectsMissingSchemaVersionWithClearMessage()
    {
        const string json = """
            {
              "createdAt": "2026-05-07T14:00:00+00:00",
              "sourceSha256": "abc",
              "entries": []
            }
            """;

        string path = Path.Combine(Path.GetTempPath(), $"map-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json, new UTF8Encoding(false));
            Action act = () => MappingFileReader.Read(path);
            act.Should().Throw<InvalidDataException>().WithMessage("*schemaVersion*");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void RejectsNullSourceSha256()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "createdAt": "2026-05-07T14:00:00+00:00",
              "sourceSha256": null,
              "entries": []
            }
            """;

        AssertReadThrowsInvalidData(json, "*sourceSha256*");
    }

    [Fact]
    public void RejectsNullEntries()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "createdAt": "2026-05-07T14:00:00+00:00",
              "sourceSha256": "0000000000000000000000000000000000000000000000000000000000000000",
              "entries": null
            }
            """;

        AssertReadThrowsInvalidData(json, "*entries*");
    }

    [Fact]
    public void RejectsNullEntryToken()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "createdAt": "2026-05-07T14:00:00+00:00",
              "sourceSha256": "0000000000000000000000000000000000000000000000000000000000000000",
              "entries": [
                { "token": null, "original": "Alice", "type": "Person", "occurrences": 1 }
              ]
            }
            """;

        AssertReadThrowsInvalidData(json, "*token*");
    }

    [Fact]
    public void RejectsNullEntryOriginal()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "createdAt": "2026-05-07T14:00:00+00:00",
              "sourceSha256": "0000000000000000000000000000000000000000000000000000000000000000",
              "entries": [
                { "token": "[PERSON_001]", "original": null, "type": "Person", "occurrences": 1 }
              ]
            }
            """;

        AssertReadThrowsInvalidData(json, "*original*");
    }

    [Fact]
    public void RejectsTokenShapedOriginalInMapping()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "createdAt": "2026-05-07T14:00:00+00:00",
              "sourceSha256": "0000000000000000000000000000000000000000000000000000000000000000",
              "entries": [
                { "token": "[PASSWORD_001]", "original": "[EMAIL_001]", "type": "Password", "occurrences": 1 }
              ]
            }
            """;

        AssertReadThrowsInvalidData(json, "*token-shaped*");
    }

    private static void AssertReadThrowsInvalidData(string json, string messageWildcard)
    {
        string path = Path.Combine(Path.GetTempPath(), $"map-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json, new UTF8Encoding(false));
            Action act = () => MappingFileReader.Read(path);
            act.Should().Throw<InvalidDataException>().WithMessage(messageWildcard);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void RejectsInvalidSourceSha256()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "createdAt": "2026-05-07T14:00:00+00:00",
              "sourceSha256": "abc",
              "entries": []
            }
            """;

        string path = Path.Combine(Path.GetTempPath(), $"map-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json, new UTF8Encoding(false));
            Action act = () => MappingFileReader.Read(path);
            act.Should().Throw<InvalidDataException>().WithMessage("*sourceSha256*");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void OverwritesExistingMapFile()
    {
        const string firstSha = "1111111111111111111111111111111111111111111111111111111111111111";
        const string secondSha = "2222222222222222222222222222222222222222222222222222222222222222";
        MappingFile first = new(
            1,
            DateTimeOffset.UtcNow,
            firstSha,
            [new MappingEntry("[PERSON_001]", "Alice", DetectionType.Person, 1)]);
        MappingFile second = new(
            1,
            DateTimeOffset.UtcNow,
            secondSha,
            [new MappingEntry("[EMAIL_001]", "x@y.test", DetectionType.Email, 1)]);

        string path = Path.Combine(Path.GetTempPath(), $"map-{Guid.NewGuid():N}.json");
        try
        {
            MappingFileWriter.Write(first, path);
            MappingFileWriter.Write(second, path);
            MappingFile read = MappingFileReader.Read(path);
            read.SourceSha256.Should().Be(secondSha);
            read.Entries.Should().ContainSingle().Which.Token.Should().Be("[EMAIL_001]");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void TempFileNotLeftBehindAfterSuccessfulWrite()
    {
        MappingFile mapping = new(1, DateTimeOffset.UtcNow, "abc", []);
        string path = Path.Combine(Path.GetTempPath(), $"map-{Guid.NewGuid():N}.json");
        try
        {
            MappingFileWriter.Write(mapping, path);
            File.Exists(path + ".tmp").Should().BeFalse();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
