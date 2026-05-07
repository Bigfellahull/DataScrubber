namespace DataScrubber.Test.Cli;

using FluentAssertions;
using Xunit;

public class MissingModelTests
{
    [Fact]
    public void ExplicitMissingModelExitsWithCode4AndNamesPath()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", "ner.onnx");
        (int code, _, string stderr) = CliProcessRunner.RunCli(
            $"- --model \"{missing}\"",
            stdin: "Email alice@example.com");

        code.Should().Be(4);
        stderr.Should().Contain(missing);
    }

    [Fact]
    public void DefaultModelPathMissingAlsoExitsWithCode4()
    {
        // The CI box does not bundle a model, so the default <exe-dir>/models/
        // path is missing. With --no-ner absent, the CLI must surface this as
        // exit 4 — never fall through silently.
        (int code, _, string stderr) = CliProcessRunner.RunCli("-", stdin: "Email alice@example.com");

        code.Should().Be(4);
        stderr.Should().Contain("models");
    }

    [Fact]
    public void NoNerSuppressesModelLoad()
    {
        (int code, string stdout, _) = CliProcessRunner.RunCli("- --no-ner", stdin: "alice@example.com");

        code.Should().Be(0);
        stdout.Should().Be("[EMAIL]");
    }

    [Fact]
    public void ModelAndNoNerTogetherExitWithCode2()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", "ner.onnx");
        (int code, _, _) = CliProcessRunner.RunCli(
            $"- --no-ner --model \"{missing}\"",
            stdin: "alice@example.com");

        code.Should().Be(2);
    }

    [Fact]
    public void MissingModelTakesPrecedenceOverAdjacentMalformedMetadata()
    {
        // The session loads first, so a missing ner.onnx fails before the
        // tokenizer/labels JSONs are even opened. This pins the precedence
        // ordering so a future reorder doesn't silently turn an unparsable
        // tokenizer into a labels-error message. Loader-level malformed-JSON
        // coverage lives in TokenizerTests / LabelMapTests.
        string dir = Path.Combine(Path.GetTempPath(), $"ner-bad-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "tokenizer.json"), "{not json");
            File.WriteAllText(Path.Combine(dir, "labels.json"), """["O", 5]""");

            (int code, _, string stderr) = CliProcessRunner.RunCli(
                $"- --model \"{Path.Combine(dir, "ner.onnx")}\"",
                stdin: "alice@example.com");

            code.Should().Be(4);
            stderr.Should().Contain("ner.onnx");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
