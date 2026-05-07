namespace DataScrubber.Test.Cli;

using System.Text.Json;
using FluentAssertions;
using Xunit;

/// <summary>
///     Coverage for M5 AC4 (human report) and AC5 (JSON-line report).
/// </summary>
public class ReportFormatTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scrub-m5-report-" + Guid.NewGuid());

    public ReportFormatTests()
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
    public void Ac4_ReportFlagPrintsHumanFormAfterWritingOutput()
    {
        string input = Path.Combine(_root, "fixture.txt");
        string output = Path.Combine(_root, "fixture.out.txt");
        File.WriteAllText(input, "alice@example.com from 10.0.1.5");

        (int code, _, string stderr) = CliProcessRunner.RunCli(
            $"\"{input}\" -o \"{output}\" --report --no-ner");

        code.Should().Be(0);
        File.Exists(output).Should().BeTrue();
        File.ReadAllText(output).Should().Contain("[EMAIL]");
        stderr.Should().Contain("DataScrubber report");
        stderr.Should().Contain("Email");
        stderr.Should().Contain("IPv4");
        stderr.Should().Contain("Total detections");
    }

    [Fact]
    public void Ac5_JsonLogsReportWritesFileEventAndSummaryEvent()
    {
        string input = Path.Combine(_root, "fixture.txt");
        File.WriteAllText(input, "alice@example.com from 10.0.1.5");

        (int code, _, string stderr) = CliProcessRunner.RunCli(
            $"\"{input}\" --report --json-logs --no-ner");

        code.Should().Be(0);

        // Filter to lines that look like our report payloads.
        string[] reportLines = stderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("{\"event\":\"file") || l.StartsWith("{\"event\":\"summary"))
            .ToArray();

        reportLines.Should().HaveCount(2);

        using JsonDocument fileDoc = JsonDocument.Parse(reportLines[0]);
        fileDoc.RootElement.GetProperty("event").GetString().Should().Be("file");
        fileDoc.RootElement.GetProperty("path").GetString().Should().Be(input);
        fileDoc.RootElement.GetProperty("counts").GetProperty("Email").GetInt32().Should().Be(1);
        fileDoc.RootElement.GetProperty("counts").GetProperty("IPv4").GetInt32().Should().Be(1);
        fileDoc.RootElement.GetProperty("durationMs").GetInt64().Should().BeGreaterThanOrEqualTo(0);

        using JsonDocument summaryDoc = JsonDocument.Parse(reportLines[1]);
        summaryDoc.RootElement.GetProperty("event").GetString().Should().Be("summary");
        summaryDoc.RootElement.GetProperty("files").GetInt32().Should().Be(1);
        summaryDoc.RootElement.GetProperty("totalDetections").GetInt32().Should().Be(2);
        summaryDoc.RootElement.GetProperty("durationMs").GetInt64().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Ac5_JsonLogsDirectoryModeEmitsOneFileEventPerProcessedFile()
    {
        string input = Path.Combine(_root, "logs");
        string output = Path.Combine(_root, "scrubbed");
        Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "a.log"), "alice@example.com");
        File.WriteAllText(Path.Combine(input, "b.log"), "10.0.1.5");

        (int code, _, string stderr) = CliProcessRunner.RunCli(
            $"\"{input}\" -o \"{output}\" --recursive --include \"**/*.log\" --report --json-logs --no-ner");

        code.Should().Be(0);

        string[] fileEvents = stderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Contains("\"event\":\"file\""))
            .ToArray();
        fileEvents.Should().HaveCount(2);

        string[] summaryEvents = stderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Contains("\"event\":\"summary\""))
            .ToArray();
        summaryEvents.Should().HaveCount(1);

        using JsonDocument summaryDoc = JsonDocument.Parse(summaryEvents[0]);
        summaryDoc.RootElement.GetProperty("files").GetInt32().Should().Be(2);
        summaryDoc.RootElement.GetProperty("totalDetections").GetInt32().Should().Be(2);
    }

    [Fact]
    public void Ac3_HumanReportEmitsZeroRowsForEveryDetectionTypeOnEmptyInput()
    {
        // V17 boundary: zero detections still produces an aligned per-type
        // report with every DetectionType visible at 0. Without this, a
        // user cannot tell "detector found nothing" from "detector skipped".
        string input = Path.Combine(_root, "empty.txt");
        File.WriteAllText(input, "");

        (int code, _, string stderr) = CliProcessRunner.RunCli(
            $"\"{input}\" --report --no-ner");

        code.Should().Be(0);
        foreach (string typeName in System.Enum.GetNames<DataScrubber.Detection.DetectionType>())
        {
            stderr.Should().Contain(typeName, $"the human report must list {typeName}");
        }
        stderr.Should().Contain("Total detections");
        stderr.Should().Contain("Duration (ms)");
    }

    [Fact]
    public void Ac3_JsonReportEmitsEveryDetectionTypeKeyAtZeroOnEmptyInput()
    {
        string input = Path.Combine(_root, "empty.txt");
        File.WriteAllText(input, "");

        (int code, _, string stderr) = CliProcessRunner.RunCli(
            $"\"{input}\" --report --json-logs --no-ner");

        code.Should().Be(0);
        string fileLine = stderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(l => l.Contains("\"event\":\"file\""));

        using JsonDocument doc = JsonDocument.Parse(fileLine);
        JsonElement counts = doc.RootElement.GetProperty("counts");
        foreach (string typeName in System.Enum.GetNames<DataScrubber.Detection.DetectionType>())
        {
            counts.GetProperty(typeName).GetInt32().Should().Be(0, $"{typeName} should be present at 0");
        }
    }

    [Fact]
    public void NoReportFlagAndNoDryRunSuppressesReport()
    {
        string input = Path.Combine(_root, "fixture.txt");
        File.WriteAllText(input, "alice@example.com");

        (int code, _, string stderr) = CliProcessRunner.RunCli(
            $"\"{input}\" --no-ner");

        code.Should().Be(0);
        stderr.Should().NotContain("DataScrubber report");
        stderr.Should().NotContain("\"event\":\"summary\"");
    }
}
