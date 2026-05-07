namespace DataScrubber.Test.Cli;

using FluentAssertions;
using Xunit;

/// <summary>
///     Coverage for M5 AC6: <c>--quiet</c> suppresses non-error stderr output,
///     including the report, but error-level diagnostics still surface so the
///     user understands a non-zero exit.
/// </summary>
public class QuietModeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scrub-m5-quiet-" + Guid.NewGuid());

    public QuietModeTests()
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
    public void Ac6_QuietWithReportSuppressesHumanReport()
    {
        string input = Path.Combine(_root, "fixture.txt");
        string output = Path.Combine(_root, "fixture.out.txt");
        File.WriteAllText(input, "alice@example.com");

        (int code, _, string stderr) = CliProcessRunner.RunCli(
            $"\"{input}\" -o \"{output}\" --report --quiet --no-ner");

        code.Should().Be(0);
        File.Exists(output).Should().BeTrue();
        stderr.Should().NotContain("DataScrubber report");
        stderr.Should().NotContain("Total detections");
    }

    [Fact]
    public void Ac6_QuietWithDryRunSuppressesReport()
    {
        string input = Path.Combine(_root, "fixture.txt");
        File.WriteAllText(input, "alice@example.com");

        (int code, _, string stderr) = CliProcessRunner.RunCli(
            $"\"{input}\" --dry-run --quiet --no-ner");

        code.Should().Be(0);
        stderr.Should().NotContain("DataScrubber report");
    }

    [Fact]
    public void Ac6_QuietWithJsonLogsSuppressesFileAndSummaryEvents()
    {
        string input = Path.Combine(_root, "fixture.txt");
        File.WriteAllText(input, "alice@example.com");

        (int code, _, string stderr) = CliProcessRunner.RunCli(
            $"\"{input}\" --report --json-logs --quiet --no-ner");

        code.Should().Be(0);
        stderr.Should().NotContain("\"event\":\"file\"");
        stderr.Should().NotContain("\"event\":\"summary\"");
    }

    [Fact]
    public void Ac6_QuietStillPrintsErrorOnFailedRun()
    {
        string nonexistent = Path.Combine(_root, "no-such-file.txt");

        (int code, _, string stderr) = CliProcessRunner.RunCli(
            $"\"{nonexistent}\" --quiet --no-ner");

        code.Should().Be(3);
        stderr.Should().Contain("input not found");
    }
}
