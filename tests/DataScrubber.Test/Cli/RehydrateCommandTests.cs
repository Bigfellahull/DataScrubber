namespace DataScrubber.Test.Cli;

using System.Text;
using FluentAssertions;
using Xunit;

public class RehydrateCommandTests
{
    [Fact]
    public void UnknownTokenLeftInPlaceWithStderrWarning()
    {
        string mapPath = Path.Combine(Path.GetTempPath(), $"rehydrate-{Guid.NewGuid():N}.map.json");
        const string mapJson = """
            {
              "schemaVersion": 1,
              "createdAt": "2026-05-07T14:00:00+00:00",
              "sourceSha256": "0000000000000000000000000000000000000000000000000000000000000000",
              "entries": [
                { "token": "[PERSON_001]", "original": "Alice", "type": "Person", "occurrences": 1 }
              ]
            }
            """;

        try
        {
            File.WriteAllText(mapPath, mapJson, new UTF8Encoding(false));

            (int code, string stdout, string stderr) = CliProcessRunner.RunCli(
                $"rehydrate - --map \"{mapPath}\"",
                stdin: "Hi [PERSON_001] and [PERSON_999].");

            code.Should().Be(0);
            stdout.Should().Be("Hi Alice and [PERSON_999].");
            stderr.Should().Contain("Unknown token [PERSON_999]");
        }
        finally
        {
            if (File.Exists(mapPath))
            {
                File.Delete(mapPath);
            }
        }
    }

    [Fact]
    public void SchemaVersionMismatchExitsWithCode2()
    {
        string mapPath = Path.Combine(Path.GetTempPath(), $"rehydrate-{Guid.NewGuid():N}.map.json");
        const string mapJson = """
            {
              "schemaVersion": 2,
              "createdAt": "2026-05-07T14:00:00+00:00",
              "sourceSha256": "0000000000000000000000000000000000000000000000000000000000000000",
              "entries": []
            }
            """;

        try
        {
            File.WriteAllText(mapPath, mapJson, new UTF8Encoding(false));

            (int code, _, string stderr) = CliProcessRunner.RunCli(
                $"rehydrate - --map \"{mapPath}\"",
                stdin: "anything");

            code.Should().Be(2);
            stderr.Should().Contain("Map schema version 2 is not supported");
        }
        finally
        {
            if (File.Exists(mapPath))
            {
                File.Delete(mapPath);
            }
        }
    }

    [Fact]
    public void IdempotentSecondPassChangesNothing()
    {
        string mapPath = Path.Combine(Path.GetTempPath(), $"rehydrate-{Guid.NewGuid():N}.map.json");
        const string mapJson = """
            {
              "schemaVersion": 1,
              "createdAt": "2026-05-07T14:00:00+00:00",
              "sourceSha256": "0000000000000000000000000000000000000000000000000000000000000000",
              "entries": [
                { "token": "[PERSON_001]", "original": "Alice", "type": "Person", "occurrences": 1 }
              ]
            }
            """;

        try
        {
            File.WriteAllText(mapPath, mapJson, new UTF8Encoding(false));

            (int code1, string firstOutput, _) = CliProcessRunner.RunCli(
                $"rehydrate - --map \"{mapPath}\"",
                stdin: "Hi [PERSON_001].");
            code1.Should().Be(0);

            (int code2, string secondOutput, _) = CliProcessRunner.RunCli(
                $"rehydrate - --map \"{mapPath}\"",
                stdin: firstOutput);
            code2.Should().Be(0);

            secondOutput.Should().Be(firstOutput);
        }
        finally
        {
            if (File.Exists(mapPath))
            {
                File.Delete(mapPath);
            }
        }
    }

    [Fact]
    public void NullSourceSha256ExitsWithCode2()
    {
        string mapPath = Path.Combine(Path.GetTempPath(), $"rehydrate-{Guid.NewGuid():N}.map.json");
        const string mapJson = """
            {
              "schemaVersion": 1,
              "createdAt": "2026-05-07T14:00:00+00:00",
              "sourceSha256": null,
              "entries": []
            }
            """;

        try
        {
            File.WriteAllText(mapPath, mapJson, new UTF8Encoding(false));

            (int code, _, string stderr) = CliProcessRunner.RunCli(
                $"rehydrate - --map \"{mapPath}\"",
                stdin: "anything");

            code.Should().Be(2);
            stderr.Should().Contain("invalid mapping file");
        }
        finally
        {
            if (File.Exists(mapPath))
            {
                File.Delete(mapPath);
            }
        }
    }

    [Fact]
    public void NullEntriesExitsWithCode2()
    {
        string mapPath = Path.Combine(Path.GetTempPath(), $"rehydrate-{Guid.NewGuid():N}.map.json");
        const string mapJson = """
            {
              "schemaVersion": 1,
              "createdAt": "2026-05-07T14:00:00+00:00",
              "sourceSha256": "0000000000000000000000000000000000000000000000000000000000000000",
              "entries": null
            }
            """;

        try
        {
            File.WriteAllText(mapPath, mapJson, new UTF8Encoding(false));

            (int code, _, string stderr) = CliProcessRunner.RunCli(
                $"rehydrate - --map \"{mapPath}\"",
                stdin: "anything");

            code.Should().Be(2);
            stderr.Should().Contain("invalid mapping file");
        }
        finally
        {
            if (File.Exists(mapPath))
            {
                File.Delete(mapPath);
            }
        }
    }

    [Fact]
    public void MissingMapFileExitsWithCode3()
    {
        string nonexistent = Path.Combine(Path.GetTempPath(), $"map-not-here-{Guid.NewGuid():N}.json");
        (int code, _, _) = CliProcessRunner.RunCli(
            $"rehydrate - --map \"{nonexistent}\"",
            stdin: "anything");
        code.Should().Be(3);
    }

    [Fact]
    public void MissingMapOptionExitsWithCode2()
    {
        (int code, _, _) = CliProcessRunner.RunCli("rehydrate -", stdin: "anything");
        code.Should().Be(2);
    }
}
