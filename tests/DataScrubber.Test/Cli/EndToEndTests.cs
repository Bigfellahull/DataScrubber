namespace DataScrubber.Test.Cli;

using FluentAssertions;
using Xunit;

public class EndToEndTests
{
    [Fact]
    public void FileInFileOutScrubsExpectedTokens()
    {
        string tmp = Path.GetTempFileName();
        string output = tmp + ".out";
        try
        {
            File.WriteAllText(tmp, "Email alice@example.com from 10.0.1.5\n");
            (int code, _, _) = CliProcessRunner.RunCli($"\"{tmp}\" -o \"{output}\" --no-ner");
            code.Should().Be(0);
            File.ReadAllText(output).Should().Be("Email [EMAIL] from [IPV4]\n");
        }
        finally
        {
            File.Delete(tmp);
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [Fact]
    public void StdinDashWritesToStdout()
    {
        (int code, string stdout, _) = CliProcessRunner.RunCli("- --no-ner", stdin: "Email alice@example.com from 10.0.1.5");
        code.Should().Be(0);
        stdout.Should().Be("Email [EMAIL] from [IPV4]");
    }

    [Fact]
    public void EmptyInputProducesEmptyOutputAndZeroExit()
    {
        (int code, string stdout, _) = CliProcessRunner.RunCli("- --no-ner", stdin: string.Empty);
        code.Should().Be(0);
        stdout.Should().BeEmpty();
    }

    [Fact]
    public void MissingInputFileExitsWithCode3()
    {
        string nonexistent = Path.Combine(Path.GetTempPath(), "scrub-does-not-exist-" + Guid.NewGuid() + ".txt");
        (int code, _, _) = CliProcessRunner.RunCli($"\"{nonexistent}\" --no-ner");
        code.Should().Be(3);
    }

    [Fact]
    public void InvalidArgumentExitsWithCode2()
    {
        (int code, _, _) = CliProcessRunner.RunCli("--unknown-flag whatever");
        code.Should().Be(2);
    }

    [Fact]
    public void Utf8BomInputProducesBomFreeOutputWithRemainingBytesPreserved()
    {
        string tmp = Path.GetTempFileName();
        string output = tmp + ".out";
        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] body = System.Text.Encoding.UTF8.GetBytes("plain text 10.0.1.5 line\n");
        try
        {
            File.WriteAllBytes(tmp, [.. bom, .. body]);
            (int code, _, _) = CliProcessRunner.RunCli($"\"{tmp}\" -o \"{output}\" --no-ner");
            code.Should().Be(0);

            byte[] outBytes = File.ReadAllBytes(output);
            outBytes.Take(3).Should().NotEqual(bom, "spec mandates 'no BOM written'");
            System.Text.Encoding.UTF8.GetString(outBytes).Should().Be("plain text [IPV4] line\n");
        }
        finally
        {
            File.Delete(tmp);
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }
}
