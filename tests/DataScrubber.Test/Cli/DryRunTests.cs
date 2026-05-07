namespace DataScrubber.Test.Cli;

using FluentAssertions;
using Xunit;

/// <summary>
///     Coverage for M5 AC3: <c>--dry-run</c> writes nothing to disk, the input
///     is unchanged, and the per-type report appears on stderr.
/// </summary>
public class DryRunTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scrub-m5-dry-" + Guid.NewGuid());

    public DryRunTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Ac3_DryRunDoesNotWriteOutputFile()
    {
        string input = Path.Combine(_root, "fixture.txt");
        string output = Path.Combine(_root, "fixture.out.txt");
        File.WriteAllText(input, "Email alice@example.com from 10.0.1.5");

        (int code, string stdout, string stderr) = CliProcessRunner.RunCli(
            $"\"{input}\" -o \"{output}\" --dry-run --no-ner");

        code.Should().Be(0);
        File.Exists(output).Should().BeFalse();
        stdout.Should().BeEmpty();
        stderr.Should().Contain("DataScrubber report");
        stderr.Should().Contain("Email");
    }

    [Fact]
    public void Ac3_DryRunLeavesInputBytesUntouched()
    {
        string input = Path.Combine(_root, "fixture.txt");
        const string original = "Email alice@example.com from 10.0.1.5";
        File.WriteAllText(input, original);
        DateTime before = File.GetLastWriteTimeUtc(input);

        (int code, _, _) = CliProcessRunner.RunCli($"\"{input}\" --dry-run --no-ner");

        code.Should().Be(0);
        File.ReadAllText(input).Should().Be(original);
        File.GetLastWriteTimeUtc(input).Should().Be(before);
    }

    [Fact]
    public void Ac3_DryRunEmitsReportOnStderr()
    {
        string input = Path.Combine(_root, "fixture.txt");
        File.WriteAllText(input, "alice@example.com\n10.0.1.5\n");

        (int code, _, string stderr) = CliProcessRunner.RunCli($"\"{input}\" --dry-run --no-ner");

        code.Should().Be(0);
        stderr.Should().Contain("Email");
        stderr.Should().Contain("IPv4");
        stderr.Should().Contain("Total detections");
        stderr.Should().Contain("Duration (ms)");
    }

    [Fact]
    public void Ac3_DryRunInDirectoryModeProducesNoOutputFiles()
    {
        string input = Path.Combine(_root, "logs");
        string output = Path.Combine(_root, "scrubbed");
        Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "a.log"), "alice@example.com");
        File.WriteAllText(Path.Combine(input, "b.log"), "bob@example.com");

        (int code, _, string stderr) = CliProcessRunner.RunCli(
            $"\"{input}\" -o \"{output}\" --recursive --dry-run --no-ner");

        code.Should().Be(0);
        Directory.Exists(output).Should().BeFalse();
        stderr.Should().Contain("Total detections");
    }

    [Fact]
    public void DryRunWithReversibleSkipsMappingFile()
    {
        string input = Path.Combine(_root, "fixture.txt");
        string mapPath = Path.Combine(_root, "fixture.map.json");
        File.WriteAllText(input, "alice@example.com");

        (int code, _, _) = CliProcessRunner.RunCli(
            $"\"{input}\" --reversible --map-file \"{mapPath}\" --dry-run --no-ner");

        code.Should().Be(0);
        File.Exists(mapPath).Should().BeFalse();
    }
}
