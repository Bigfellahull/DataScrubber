namespace DataScrubber.Test.Cli;

using System.Security.Cryptography;
using System.Text;
using DataScrubber.Mapping;
using FluentAssertions;
using Xunit;

public class ReversibleEndToEndTests
{
    [Fact]
    public void ScrubThenRehydrateRoundTripsInputBytes()
    {
        string workDir = Path.Combine(Path.GetTempPath(), $"reversible-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        string inputPath = Path.Combine(workDir, "input.txt");
        string scrubbedPath = Path.Combine(workDir, "scrubbed.txt");
        string rehydratedPath = Path.Combine(workDir, "rehydrated.txt");
        string mapPath = Path.Combine(workDir, "input.map.json");

        const string body = "Email alice@example.com from 10.0.1.5\n";
        File.WriteAllText(inputPath, body, new UTF8Encoding(false));

        try
        {
            (int scrubCode, _, string scrubStderr) = CliProcessRunner.RunCli(
                $"\"{inputPath}\" --reversible --no-ner -o \"{scrubbedPath}\"");
            scrubCode.Should().Be(0);
            File.Exists(mapPath).Should().BeTrue("default map path is <input-stem>.map.json next to the output");
            scrubStderr.Should().Contain("contains raw PII");

            string scrubbed = File.ReadAllText(scrubbedPath);
            scrubbed.Should().Be("Email [EMAIL_001] from [IPV4_001]\n");

            (int rehydrateCode, _, _) = CliProcessRunner.RunCli(
                $"rehydrate \"{scrubbedPath}\" --map \"{mapPath}\" -o \"{rehydratedPath}\"");
            rehydrateCode.Should().Be(0);

            File.ReadAllText(rehydratedPath).Should().Be(body);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public void ExplicitMapFilePathIsHonoured()
    {
        string workDir = Path.Combine(Path.GetTempPath(), $"reversible-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        string inputPath = Path.Combine(workDir, "input.txt");
        string scrubbedPath = Path.Combine(workDir, "scrubbed.txt");
        string mapPath = Path.Combine(workDir, "custom.map.json");

        File.WriteAllText(inputPath, "alice@x.test", new UTF8Encoding(false));

        try
        {
            (int code, _, _) = CliProcessRunner.RunCli(
                $"\"{inputPath}\" --reversible --no-ner -o \"{scrubbedPath}\" --map-file \"{mapPath}\"");
            code.Should().Be(0);
            File.Exists(mapPath).Should().BeTrue();
            File.Exists(Path.Combine(workDir, "input.map.json")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public void ReRunningOverwritesMapFile()
    {
        string workDir = Path.Combine(Path.GetTempPath(), $"reversible-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        string inputPath = Path.Combine(workDir, "input.txt");
        string scrubbedPath = Path.Combine(workDir, "scrubbed.txt");
        string mapPath = Path.Combine(workDir, "input.map.json");

        File.WriteAllText(inputPath, "alice@x.test", new UTF8Encoding(false));

        try
        {
            (int firstCode, _, _) = CliProcessRunner.RunCli(
                $"\"{inputPath}\" --reversible --no-ner -o \"{scrubbedPath}\"");
            firstCode.Should().Be(0);
            DateTimeOffset firstCreated = MappingFileReader.Read(mapPath).CreatedAt;

            File.WriteAllText(inputPath, "bob@x.test", new UTF8Encoding(false));
            Thread.Sleep(20);

            (int secondCode, _, _) = CliProcessRunner.RunCli(
                $"\"{inputPath}\" --reversible --no-ner -o \"{scrubbedPath}\"");
            secondCode.Should().Be(0);
            MappingFile second = MappingFileReader.Read(mapPath);
            second.Entries.Should().ContainSingle().Which.Original.Should().Be("bob@x.test");
            second.CreatedAt.Should().BeAfter(firstCreated);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public void MapFileWarningIncludesAbsolutePath()
    {
        string workDir = Path.Combine(Path.GetTempPath(), $"reversible-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        string inputPath = Path.Combine(workDir, "input.txt");
        string scrubbedPath = Path.Combine(workDir, "scrubbed.txt");
        string mapPath = Path.Combine(workDir, "input.map.json");

        File.WriteAllText(inputPath, "alice@x.test", new UTF8Encoding(false));

        try
        {
            (int code, _, string stderr) = CliProcessRunner.RunCli(
                $"\"{inputPath}\" --reversible --no-ner -o \"{scrubbedPath}\"");
            code.Should().Be(0);
            stderr.Should().Contain($"WARNING: {Path.GetFullPath(mapPath)} contains raw PII. Treat it as sensitive.");
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public void OutputWriteFailureAfterMapWriteStillEmitsWarning()
    {
        string workDir = Path.Combine(Path.GetTempPath(), $"reversible-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        string inputPath = Path.Combine(workDir, "input.txt");
        string mapPath = Path.Combine(workDir, "custom.map.json");
        string missingDirOutput = Path.Combine(workDir, "missing-dir", "out.txt");

        File.WriteAllText(inputPath, "alice@x.test", new UTF8Encoding(false));

        try
        {
            (int code, _, string stderr) = CliProcessRunner.RunCli(
                $"\"{inputPath}\" --reversible --no-ner -o \"{missingDirOutput}\" --map-file \"{mapPath}\"");
            code.Should().Be(1, "output write to a non-existent directory must fail");
            File.Exists(mapPath).Should().BeTrue("the map was written before the output failed");
            stderr.Should().Contain($"WARNING: {Path.GetFullPath(mapPath)} contains raw PII",
                "users must be warned whenever a raw-PII map is on disk, even on partial failure");
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public void SourceSha256ReflectsRawInputBytesIncludingBom()
    {
        string workDir = Path.Combine(Path.GetTempPath(), $"reversible-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        string inputPath = Path.Combine(workDir, "input.txt");
        string scrubbedPath = Path.Combine(workDir, "scrubbed.txt");
        string mapPath = Path.Combine(workDir, "input.map.json");

        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] body = Encoding.UTF8.GetBytes("alice@x.test\n");
        byte[] raw = [.. bom, .. body];
        File.WriteAllBytes(inputPath, raw);

        try
        {
            (int code, _, _) = CliProcessRunner.RunCli(
                $"\"{inputPath}\" --reversible --no-ner -o \"{scrubbedPath}\"");
            code.Should().Be(0);

            string expectedSha = Convert.ToHexStringLower(SHA256.HashData(raw));
            MappingFile mapping = MappingFileReader.Read(mapPath);
            mapping.SourceSha256.Should().Be(expectedSha);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public void MapFileWarningEmittedExactlyOnce()
    {
        string workDir = Path.Combine(Path.GetTempPath(), $"reversible-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        string inputPath = Path.Combine(workDir, "input.txt");
        string scrubbedPath = Path.Combine(workDir, "scrubbed.txt");

        File.WriteAllText(inputPath, "alice@x.test", new UTF8Encoding(false));

        try
        {
            (int code, _, string stderr) = CliProcessRunner.RunCli(
                $"\"{inputPath}\" --reversible --no-ner -o \"{scrubbedPath}\"");
            code.Should().Be(0);

            int matches = 0;
            int idx = 0;
            const string needle = "WARNING:";
            while ((idx = stderr.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
            {
                matches++;
                idx += needle.Length;
            }

            matches.Should().Be(1, "spec mandates exactly one warning per run");
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public void MapFileFlagWithoutReversibleExitsWithCode2()
    {
        string mapPath = Path.Combine(Path.GetTempPath(), $"map-{Guid.NewGuid():N}.json");
        (int code, _, _) = CliProcessRunner.RunCli(
            $"- --map-file \"{mapPath}\"",
            stdin: "alice@x.test");
        code.Should().Be(2);
    }

    [Fact]
    public void LiteralTokenShapedTextRoundTripsExactly()
    {
        string workDir = Path.Combine(Path.GetTempPath(), $"reversible-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        string inputPath = Path.Combine(workDir, "input.txt");
        string scrubbedPath = Path.Combine(workDir, "scrubbed.txt");
        string rehydratedPath = Path.Combine(workDir, "rehydrated.txt");
        string mapPath = Path.Combine(workDir, "input.map.json");

        // Reviewer's adversarial AC3 case: a literal token-shaped substring
        // sits next to a real email. The allocator must not assign [EMAIL_001]
        // to the email or the round trip will swap "alice@example.com literal
        // [EMAIL_001]" → "alice@example.com literal alice@example.com".
        const string body = "alice@example.com literal [EMAIL_001]\n";
        File.WriteAllText(inputPath, body, new UTF8Encoding(false));

        try
        {
            (int scrubCode, _, _) = CliProcessRunner.RunCli(
                $"\"{inputPath}\" --reversible --no-ner -o \"{scrubbedPath}\"");
            scrubCode.Should().Be(0);

            (int rehydrateCode, _, _) = CliProcessRunner.RunCli(
                $"rehydrate \"{scrubbedPath}\" --map \"{mapPath}\" -o \"{rehydratedPath}\"");
            rehydrateCode.Should().Be(0);

            File.ReadAllText(rehydratedPath).Should().Be(body);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public void TokenShapedOriginalIsRejectedAtScrubTime()
    {
        string workDir = Path.Combine(Path.GetTempPath(), $"reversible-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        string inputPath = Path.Combine(workDir, "input.txt");
        string scrubbedPath = Path.Combine(workDir, "scrubbed.txt");
        string mapPath = Path.Combine(workDir, "input.map.json");

        // The password value [EMAIL_001] is itself token-shaped. Allowing this
        // through would corrupt rehydration on a second pass, so the writer
        // refuses up-front rather than producing a non-idempotent mapping.
        File.WriteAllText(inputPath, "password=[EMAIL_001] alice@example.com\n", new UTF8Encoding(false));

        try
        {
            (int code, _, string stderr) = CliProcessRunner.RunCli(
                $"\"{inputPath}\" --reversible --no-ner -o \"{scrubbedPath}\"");
            code.Should().Be(1);
            stderr.Should().Contain("token-shaped");
            File.Exists(mapPath).Should().BeFalse("a non-idempotent mapping must not reach disk");
            File.Exists(scrubbedPath).Should().BeFalse("no output should be written when reversibility is impossible");
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public void StdoutOutputPlacesMapNextToInput()
    {
        string workDir = Path.Combine(Path.GetTempPath(), $"reversible-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        string inputPath = Path.Combine(workDir, "input.txt");
        string expectedMapPath = Path.Combine(workDir, "input.map.json");

        File.WriteAllText(inputPath, "alice@x.test", new UTF8Encoding(false));

        try
        {
            (int code, _, _) = CliProcessRunner.RunCli($"\"{inputPath}\" --reversible --no-ner");
            code.Should().Be(0);
            File.Exists(expectedMapPath).Should().BeTrue("when output is stdout, the map goes next to the input");
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public void StdinInputPlacesMapInWorkingDirectoryWithTimestampStem()
    {
        string workDir = Path.Combine(Path.GetTempPath(), $"reversible-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        try
        {
            (int code, _, string stderr) = CliProcessRunner.RunCli(
                "- --reversible --no-ner", stdin: "alice@x.test", workingDirectory: workDir);
            code.Should().Be(0);

            string[] mapFiles = Directory.GetFiles(workDir, "scrub-*.map.json");
            mapFiles.Should().ContainSingle();
            string fileName = Path.GetFileName(mapFiles[0]);
            fileName.Should().MatchRegex(@"^scrub-\d{4}-\d{2}-\d{2}T\d{6}Z\.map\.json$");
            stderr.Should().Contain(mapFiles[0]);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }
}
