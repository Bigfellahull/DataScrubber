namespace DataScrubber.Test.Cli;

using FluentAssertions;
using Xunit;

/// <summary>
///     End-to-end CLI integration tests that require a real ONNX model. Gated
///     by <c>[Trait("Category","Integration")]</c> and <c>[SkippableFact]</c>;
///     <c>DATASCRUBBER_NER_MODEL</c> must point at a directory holding
///     <c>ner.onnx</c>, <c>tokenizer.json</c>, and <c>labels.json</c>. Tests are
///     reported as skipped (not passed) when the env var is unset.
/// </summary>
[Trait("Category", "Integration")]
public class CliWithModelIntegrationTests
{
    private const string ModelDirEnvVar = "DATASCRUBBER_NER_MODEL";

    [SkippableFact]
    public void ExplicitModelFlagRedactsPersonOrganizationLocation()
    {
        string modelDir = RequireModelDirectory();

        string fixture = Path.GetTempFileName();
        string outputPath = fixture + ".out";
        try
        {
            File.WriteAllText(fixture, "Sarah called Acme Corp from Berlin");
            (int code, _, string stderr) = CliProcessRunner.RunCli(
                $"\"{fixture}\" -o \"{outputPath}\" --model \"{Path.Combine(modelDir, "ner.onnx")}\"");
            code.Should().Be(0, stderr);

            string redacted = File.ReadAllText(outputPath);
            redacted.Should().Contain("[PERSON]");
            redacted.Should().Contain("[ORGANIZATION]");
            redacted.Should().Contain("[LOCATION]");
        }
        finally
        {
            File.Delete(fixture);
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [SkippableFact]
    public void DefaultModelPathRedactsPersonOrganizationLocationWithNoFlags()
    {
        // AC1: literal `scrub fixture.txt -o out.txt` (no flags) must work.
        // The CLI resolves <exe-dir>/models/ner.onnx; we stage the supplied
        // model files there for the duration of the test, then clean up.
        string modelDir = RequireModelDirectory();
        string cliBin = Path.GetDirectoryName(CliProcessRunner.CliDllPath)!;
        string defaultModelDir = Path.Combine(cliBin, "models");
        bool createdDir = !Directory.Exists(defaultModelDir);
        string[] artefacts = ["ner.onnx", "tokenizer.json", "labels.json"];

        Directory.CreateDirectory(defaultModelDir);
        List<string> staged = [];
        foreach (string name in artefacts)
        {
            string source = Path.Combine(modelDir, name);
            string destination = Path.Combine(defaultModelDir, name);
            if (File.Exists(destination))
            {
                continue;
            }

            File.Copy(source, destination);
            staged.Add(destination);
        }

        string fixture = Path.GetTempFileName();
        string outputPath = fixture + ".out";
        try
        {
            File.WriteAllText(fixture, "Sarah called Acme Corp from Berlin");
            (int code, _, string stderr) = CliProcessRunner.RunCli(
                $"\"{fixture}\" -o \"{outputPath}\"");
            code.Should().Be(0, stderr);

            string redacted = File.ReadAllText(outputPath);
            redacted.Should().Contain("[PERSON]");
            redacted.Should().Contain("[ORGANIZATION]");
            redacted.Should().Contain("[LOCATION]");
        }
        finally
        {
            File.Delete(fixture);
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
            foreach (string path in staged)
            {
                File.Delete(path);
            }
            if (createdDir && Directory.Exists(defaultModelDir) && Directory.GetFileSystemEntries(defaultModelDir).Length == 0)
            {
                Directory.Delete(defaultModelDir);
            }
        }
    }

    [SkippableFact]
    public void MixedRuleNerOverlapBeatsNerOnEmailSpan()
    {
        string modelDir = RequireModelDirectory();

        (int code, string stdout, string stderr) = CliProcessRunner.RunCli(
            $"- --model \"{Path.Combine(modelDir, "ner.onnx")}\"",
            stdin: "Email Sarah at sarah@acme.com");
        code.Should().Be(0, stderr);
        stdout.Should().Contain("[EMAIL]");
        stdout.Should().Contain("[PERSON]");
    }

    private static string RequireModelDirectory()
    {
        string? root = Environment.GetEnvironmentVariable(ModelDirEnvVar);
        bool ready = !string.IsNullOrEmpty(root)
            && File.Exists(Directory.Exists(root) ? Path.Combine(root, "ner.onnx") : root);
        Skip.IfNot(
            ready,
            $"{ModelDirEnvVar} unset or model not found. Point it at a directory containing ner.onnx, tokenizer.json, and labels.json to run integration assertions.");
        return Directory.Exists(root) ? root! : Path.GetDirectoryName(root!)!;
    }
}
