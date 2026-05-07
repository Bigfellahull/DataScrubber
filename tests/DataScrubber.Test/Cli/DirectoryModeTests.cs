namespace DataScrubber.Test.Cli;

using FluentAssertions;
using Xunit;

/// <summary>
///     End-to-end coverage for M5 directory mode (AC1, AC8, AC9). Each test
///     stages a temp tree, invokes the CLI out-of-process, and asserts on the
///     mirrored output and per-file error continuation behaviour.
/// </summary>
public class DirectoryModeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scrub-m5-dir-" + Guid.NewGuid());

    public DirectoryModeTests()
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
    public void Ac1_RecursiveLogIncludeMirrorsTreeAndScrubsEachFile()
    {
        string input = Path.Combine(_root, "logs");
        string output = Path.Combine(_root, "scrubbed");
        Directory.CreateDirectory(Path.Combine(input, "sub"));

        File.WriteAllText(Path.Combine(input, "a.log"), "Email alice@example.com from 10.0.1.5");
        File.WriteAllText(Path.Combine(input, "sub", "b.log"), "Email bob@example.com from 10.0.1.6");
        File.WriteAllText(Path.Combine(input, "ignore.txt"), "this should be skipped under --include *.log");

        (int code, _, _) = CliProcessRunner.RunCli(
            $"\"{input}\" -o \"{output}\" --recursive --include \"**/*.log\" --no-ner");

        code.Should().Be(0);
        File.ReadAllText(Path.Combine(output, "a.log")).Should().Be("Email [EMAIL] from [IPV4]");
        File.ReadAllText(Path.Combine(output, "sub", "b.log")).Should().Be("Email [EMAIL] from [IPV4]");
        File.Exists(Path.Combine(output, "ignore.txt")).Should().BeFalse();
    }

    [Fact]
    public void Ac1_NonRecursiveDoesNotDescend()
    {
        string input = Path.Combine(_root, "logs");
        string output = Path.Combine(_root, "scrubbed");
        Directory.CreateDirectory(Path.Combine(input, "sub"));

        File.WriteAllText(Path.Combine(input, "top.log"), "alice@example.com");
        File.WriteAllText(Path.Combine(input, "sub", "deep.log"), "bob@example.com");

        (int code, _, _) = CliProcessRunner.RunCli(
            $"\"{input}\" -o \"{output}\" --include \"**/*.log\" --no-ner");

        code.Should().Be(0);
        File.Exists(Path.Combine(output, "top.log")).Should().BeTrue();
        File.Exists(Path.Combine(output, "sub", "deep.log")).Should().BeFalse();
    }

    [Fact]
    public void Ac1_DefaultIncludesPickUpTxtLogAndMd()
    {
        string input = Path.Combine(_root, "logs");
        string output = Path.Combine(_root, "scrubbed");
        Directory.CreateDirectory(input);

        File.WriteAllText(Path.Combine(input, "doc.md"), "x@x.com");
        File.WriteAllText(Path.Combine(input, "rec.log"), "y@y.com");
        File.WriteAllText(Path.Combine(input, "note.txt"), "z@z.com");
        File.WriteAllText(Path.Combine(input, "binary.bin"), "skipped");

        (int code, _, _) = CliProcessRunner.RunCli(
            $"\"{input}\" -o \"{output}\" --recursive --no-ner");

        code.Should().Be(0);
        File.Exists(Path.Combine(output, "doc.md")).Should().BeTrue();
        File.Exists(Path.Combine(output, "rec.log")).Should().BeTrue();
        File.Exists(Path.Combine(output, "note.txt")).Should().BeTrue();
        File.Exists(Path.Combine(output, "binary.bin")).Should().BeFalse();
    }

    [Fact]
    public void Ac9_DirectoryWithoutOutputExitsWith2()
    {
        string input = Path.Combine(_root, "logs");
        Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "a.log"), "x");

        (int code, _, string stderr) = CliProcessRunner.RunCli($"\"{input}\" --no-ner");

        code.Should().Be(2);
        stderr.Should().Contain("directory mode requires --output");
    }

    [Fact]
    public void Ac9_DirectoryOutputDashIsRejected()
    {
        string input = Path.Combine(_root, "logs");
        Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "a.log"), "x");

        (int code, _, _) = CliProcessRunner.RunCli($"\"{input}\" -o - --no-ner");

        code.Should().Be(2);
    }

    [Fact]
    public void Ac9_OutputOverlappingInputIsRejected()
    {
        string input = Path.Combine(_root, "logs");
        Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "a.log"), "x");

        // Output is the same directory as input.
        (int code, _, string stderr) = CliProcessRunner.RunCli(
            $"\"{input}\" -o \"{input}\" --no-ner");

        code.Should().Be(2);
        stderr.Should().Contain("must not be the same as or contained within");
    }

    [Fact]
    public void Ac8_UnreadableFileLogsWarningAndContinues()
    {
        // Skip on Windows; chmod-based unreadable doesn't translate.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string input = Path.Combine(_root, "logs");
        string output = Path.Combine(_root, "scrubbed");
        Directory.CreateDirectory(input);

        string readable = Path.Combine(input, "ok.log");
        string unreadable = Path.Combine(input, "denied.log");
        File.WriteAllText(readable, "alice@example.com");
        File.WriteAllText(unreadable, "bob@example.com");
        File.SetUnixFileMode(unreadable, UnixFileMode.None);

        try
        {
            (int code, _, string stderr) = CliProcessRunner.RunCli(
                $"\"{input}\" -o \"{output}\" --include \"**/*.log\" --no-ner");

            code.Should().Be(0);
            // Spec mandates exact human form "Warning: <path>: <message>".
            stderr.Should().Contain($"Warning: {unreadable}:");
            File.Exists(Path.Combine(output, "ok.log")).Should().BeTrue();
        }
        finally
        {
            File.SetUnixFileMode(unreadable, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void Ac8_QuietModeSuppressesPerFileWarning()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string input = Path.Combine(_root, "logs");
        string output = Path.Combine(_root, "scrubbed");
        Directory.CreateDirectory(input);
        string unreadable = Path.Combine(input, "denied.log");
        File.WriteAllText(Path.Combine(input, "ok.log"), "alice@example.com");
        File.WriteAllText(unreadable, "bob@example.com");
        File.SetUnixFileMode(unreadable, UnixFileMode.None);

        try
        {
            (int code, _, string stderr) = CliProcessRunner.RunCli(
                $"\"{input}\" -o \"{output}\" --include \"**/*.log\" --quiet --no-ner");

            code.Should().Be(0);
            stderr.Should().NotContain("Warning:");
        }
        finally
        {
            File.SetUnixFileMode(unreadable, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void ReversibleDirectoryModeWritesPerFileMapAndScrubs()
    {
        string input = Path.Combine(_root, "logs");
        string output = Path.Combine(_root, "scrubbed");
        Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "a.log"), "alice@example.com");
        File.WriteAllText(Path.Combine(input, "b.log"), "bob@example.com");

        (int code, _, string stderr) = CliProcessRunner.RunCli(
            $"\"{input}\" -o \"{output}\" --recursive --include \"**/*.log\" --reversible --no-ner");

        code.Should().Be(0);
        File.ReadAllText(Path.Combine(output, "a.log")).Should().Contain("[EMAIL_001]");
        File.ReadAllText(Path.Combine(output, "b.log")).Should().Contain("[EMAIL_001]");
        File.Exists(Path.Combine(output, "a.map.json")).Should().BeTrue();
        File.Exists(Path.Combine(output, "b.map.json")).Should().BeTrue();
        stderr.Should().Contain("contains raw PII");
    }

    [Fact]
    public void ReversibleDirectoryModeRejectsExplicitMapFile()
    {
        string input = Path.Combine(_root, "logs");
        string output = Path.Combine(_root, "scrubbed");
        string map = Path.Combine(_root, "shared.map.json");
        Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "a.log"), "alice@example.com");

        (int code, _, _) = CliProcessRunner.RunCli(
            $"\"{input}\" -o \"{output}\" --recursive --reversible --map-file \"{map}\" --no-ner");

        code.Should().Be(2);
    }

    [Fact]
    public void EmptyDirectoryExitsWith0AndProducesEmptyOutput()
    {
        string input = Path.Combine(_root, "logs");
        string output = Path.Combine(_root, "scrubbed");
        Directory.CreateDirectory(input);

        (int code, _, _) = CliProcessRunner.RunCli(
            $"\"{input}\" -o \"{output}\" --recursive --no-ner");

        code.Should().Be(0);
        // Output dir does not need to be created when there are no files.
    }

    [Fact]
    public void ExcludeGlobSkipsMatchingFiles()
    {
        string input = Path.Combine(_root, "logs");
        string output = Path.Combine(_root, "scrubbed");
        Directory.CreateDirectory(input);

        File.WriteAllText(Path.Combine(input, "keep.log"), "alice@example.com");
        File.WriteAllText(Path.Combine(input, "skip.log"), "bob@example.com");

        (int code, _, _) = CliProcessRunner.RunCli(
            $"\"{input}\" -o \"{output}\" --include \"**/*.log\" --exclude \"**/skip.log\" --no-ner");

        code.Should().Be(0);
        File.Exists(Path.Combine(output, "keep.log")).Should().BeTrue();
        File.Exists(Path.Combine(output, "skip.log")).Should().BeFalse();
    }
}
