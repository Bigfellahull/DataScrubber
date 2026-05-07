namespace DataScrubber.Test.Cli;

using System.Diagnostics;
using System.Text;
using DataScrubber.Cli;
using DataScrubber.Detection;
using FluentAssertions;
using Xunit;

/// <summary>
///     Streaming-mode coverage for M5 AC2 (1 GB stdin processes with bounded
///     memory) and the cross-line carry-buffer guarantee. The 1 GB end-to-end
///     test is gated on an environment variable so the suite stays fast on
///     dev machines.
/// </summary>
public class StreamingTests
{
    [Fact]
    public void StreamingScrubberRedactsRulePatternsInChunkedInput()
    {
        StringBuilder source = new();
        for (int i = 0; i < 100; i++)
        {
            source.Append($"line {i} alice{i}@example.com 10.0.1.5\n");
        }

        StringWriter sink = new();
        StreamingScrubber scrubber = BuildScrubber();
        StringReader reader = new(source.ToString());
        IReadOnlyDictionary<DetectionType, int> counts = scrubber.Process(reader, sink);

        sink.ToString().Should().NotContain("@example.com");
        sink.ToString().Should().Contain("[EMAIL]");
        sink.ToString().Should().Contain("[IPV4]");
        counts[DetectionType.Email].Should().Be(100);
        counts[DetectionType.IPv4].Should().Be(100);
    }

    [Fact]
    public void StreamingScrubberPreservesBytesOutsideDetectionSpans()
    {
        const string input = "first line 10.0.1.5\nsecond line\nthird line bob@example.com\n";

        StringWriter sink = new();
        StreamingScrubber scrubber = BuildScrubber();
        StringReader reader = new(input);
        scrubber.Process(reader, sink);

        sink.ToString().Should().Be("first line [IPV4]\nsecond line\nthird line [EMAIL]\n");
    }

    [Fact]
    public void StreamingScrubberPreservesAbsenceOfTrailingNewline()
    {
        const string input = "Email alice@example.com from 10.0.1.5";

        StringWriter sink = new();
        StreamingScrubber scrubber = BuildScrubber();
        StringReader reader = new(input);
        scrubber.Process(reader, sink);

        sink.ToString().Should().Be("Email [EMAIL] from [IPV4]");
    }

    [Fact]
    public void StreamingScrubberCarriesRuleAcrossChunkBoundaryWithin4Kb()
    {
        // Build an input whose detection straddles a 4 KB boundary by padding
        // the front so the email starts very close to the chunk-end mark.
        // The streaming scrubber's commit logic must postpone any straddling
        // detection into the next iteration's carry rather than splitting it.
        const int padSize = 7800;
        string pad = new('a', padSize);
        string email = "user@example.com";
        string input = pad + " " + email + " trailing";

        StringWriter sink = new();
        StreamingScrubber scrubber = BuildScrubber();
        StringReader reader = new(input);
        scrubber.Process(reader, sink);

        sink.ToString().Should().Contain("[EMAIL]");
        sink.ToString().Should().NotContain("user@example.com");
    }

    [Fact]
    public void StdinDashAlwaysStreamsLargeInputWithoutPanic()
    {
        StringBuilder source = new();
        for (int i = 0; i < 5000; i++)
        {
            source.Append($"line {i} 10.0.1.{i % 255}\n");
        }

        (int code, string stdout, _) = CliProcessRunner.RunCli("- --no-ner", stdin: source.ToString());

        code.Should().Be(0);
        stdout.Should().Contain("[IPV4]");
        stdout.Split('\n').Length.Should().Be(5001);
    }

    [Fact]
    public void StreamingMissesCleanlyForEntitiesLargerThanCarryBuffer()
    {
        // Spec calls out >4 KB entities as missed cleanly. The streamer
        // must not crash, must not retain the unbounded span in carry, and
        // the observable output is the literal bytes flowing through. We
        // assert the giant URL-shaped sequence emerges in the output (no
        // [URL] tag), and that running the chunk completes promptly with
        // no exceptions.
        string giantUrl = "https://example.com/" + new string('a', StreamingScrubber.CarryBufferSize + 2000);
        StringWriter sink = new();
        StreamingScrubber scrubber = BuildScrubber();
        StringReader reader = new(giantUrl);
        scrubber.Process(reader, sink);

        string output = sink.ToString();
        output.Should().Contain(new string('a', 100), "the giant span flows through verbatim");
        output.Should().NotContain("[URL]", "spans larger than the carry budget are missed cleanly");
    }

    [Fact]
    public async Task StreamingProcessesLargerThanThresholdInputWithBoundedMemory()
    {
        // Production-scale proxy for AC2. We push ~32 MB of synthetic log
        // lines through the CLI and assert that peak working set stays
        // well below the input size. This intentionally avoids the 1 GB
        // env-gated path so the bounded-memory guarantee is part of the
        // default test run.
        const int targetBytes = 32 * 1024 * 1024;
        const string line = "request from 10.0.1.5 user alice@example.com path /api\n";
        long lineBytes = Encoding.UTF8.GetByteCount(line);
        long lineCount = targetBytes / lineBytes;

        ProcessStartInfo info = new("dotnet", $"\"{CliProcessRunner.CliDllPath}\" - --no-ner")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using Process process = Process.Start(info)!;
        Task drainStdout = Task.Run(() =>
        {
            char[] discard = new char[65536];
            while (process.StandardOutput.Read(discard, 0, discard.Length) > 0)
            {
            }
        });

        long peakRss = 0;
        Task monitor = Task.Run(async () =>
        {
            while (!process.HasExited)
            {
                process.Refresh();
                peakRss = Math.Max(peakRss, process.WorkingSet64);
                await Task.Delay(50);
            }
        });

        for (long i = 0; i < lineCount; i++)
        {
            process.StandardInput.Write(line);
        }
        process.StandardInput.Close();

        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        await drainStdout;
        await monitor;

        process.ExitCode.Should().Be(0);
        // Peak RSS must stay well under the input size — proves the
        // scrubber does not buffer the whole input. .NET runtime baseline
        // is generous; cap at 256 MB which is 8x the input.
        peakRss.Should().BeLessThan(256L * 1024 * 1024,
            "streaming must not retain the input in memory");
    }

    [SkippableFact]
    public void Ac2_OneGbStdinProcessesWithBoundedRss()
    {
        Skip.If(
            Environment.GetEnvironmentVariable("DATASCRUBBER_RUN_LARGE_STREAM") != "1",
            "Set DATASCRUBBER_RUN_LARGE_STREAM=1 to run the 1 GB streaming test.");

        const long oneGb = 1L * 1024 * 1024 * 1024;
        const string line = "request from 10.0.1.5 user alice@example.com path /api/v1/items\n";
        long lineBytes = Encoding.UTF8.GetByteCount(line);
        long lineCount = oneGb / lineBytes;

        ProcessStartInfo info = new("dotnet", $"\"{CliProcessRunner.CliDllPath}\" - --no-ner")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using Process process = Process.Start(info)!;
        Task drainStdout = Task.Run(() =>
        {
            char[] discard = new char[65536];
            while (process.StandardOutput.Read(discard, 0, discard.Length) > 0)
            {
            }
        });

        long peakRss = 0;
        Task monitor = Task.Run(async () =>
        {
            while (!process.HasExited)
            {
                process.Refresh();
                peakRss = Math.Max(peakRss, process.WorkingSet64);
                await Task.Delay(50);
            }
        });

        for (long i = 0; i < lineCount; i++)
        {
            process.StandardInput.Write(line);
        }
        process.StandardInput.Close();

        process.WaitForExit(600_000).Should().BeTrue();
        drainStdout.Wait();
        monitor.Wait();

        process.ExitCode.Should().Be(0);
        // RSS must stay well under 4× the streaming buffer plus interpreter overhead.
        // Allow a 256 MB ceiling — generous for the runtime baseline plus our 4 KB carry.
        peakRss.Should().BeLessThan(256L * 1024 * 1024);
    }

    private static StreamingScrubber BuildScrubber()
    {
        IDetector detector = RuleBasedDetector.CreateDefault();
        return new StreamingScrubber(detector, DetectionContext.Empty);
    }
}
