namespace DataScrubber.Test.Detection.Ner;

using DataScrubber.Detection;
using DataScrubber.Detection.Ner;
using FluentAssertions;
using Xunit;

/// <summary>
///     Integration tests that exercise the real ONNX runtime, real tokenizer,
///     and real label map against a user-supplied model. Gated by
///     <c>[Trait("Category","Integration")]</c> and <c>[SkippableFact]</c>:
///     when <c>DATASCRUBBER_NER_MODEL</c> is unset the tests are reported as
///     skipped (not passed) so CI dashboards distinguish "not run" from
///     "validated".
/// </summary>
[Trait("Category", "Integration")]
public class OnnxNerIntegrationTests
{
    private const string ModelDirEnvVar = "DATASCRUBBER_NER_MODEL";

    [SkippableFact]
    public void GoldenFixtureProducesExpectedRedactions()
    {
        NerModelConfig config = RequireModelConfig();

        using OnnxNerDetector detector = new(config);
        const string source = "Sarah called Acme Corp from Berlin";
        IReadOnlyList<Detection> rule = [.. RuleBasedDetector.CreateDefault().Detect(source.AsMemory(), DetectionContext.Empty)];
        IReadOnlyList<Detection> ner = [.. detector.Detect(source.AsMemory(), DetectionContext.Empty)];
        IReadOnlyList<Detection> merged = DetectionMerger.Merge([.. rule, .. ner]);

        merged.Select(d => d.Type).Should().Contain([
            DetectionType.Person,
            DetectionType.Organization,
            DetectionType.Location,
        ]);
        merged.Should().Contain(d => source.Substring(d.Start, d.Length) == "Sarah" && d.Type == DetectionType.Person);
        merged.Should().Contain(d => source.Substring(d.Start, d.Length) == "Acme Corp" && d.Type == DetectionType.Organization);
        merged.Should().Contain(d => source.Substring(d.Start, d.Length) == "Berlin" && d.Type == DetectionType.Location);
    }

    [SkippableFact]
    public void MixedRuleAndNerOverlapResolvesEmailVsPersonAtSameSpan()
    {
        NerModelConfig config = RequireModelConfig();

        using OnnxNerDetector detector = new(config);
        const string source = "Email Sarah at sarah@acme.com";
        IReadOnlyList<Detection> rule = [.. RuleBasedDetector.CreateDefault().Detect(source.AsMemory(), DetectionContext.Empty)];
        IReadOnlyList<Detection> ner = [.. detector.Detect(source.AsMemory(), DetectionContext.Empty)];
        IReadOnlyList<Detection> merged = DetectionMerger.Merge([.. rule, .. ner]);

        merged.Should().Contain(d => source.Substring(d.Start, d.Length) == "Sarah" && d.Type == DetectionType.Person);
        merged.Should().Contain(d => source.Substring(d.Start, d.Length) == "sarah@acme.com" && d.Type == DetectionType.Email);
    }

    [SkippableFact]
    public void LongParagraphProducesNoDuplicateRedactions()
    {
        NerModelConfig config = RequireModelConfig();

        using OnnxNerDetector detector = new(config);
        const string sentence = "Sarah and John from Acme Corp visited Berlin and Paris yesterday. ";
        System.Text.StringBuilder builder = new(4096);
        while (builder.Length < 4000)
        {
            builder.Append(sentence);
        }
        string source = builder.ToString();

        IReadOnlyList<Detection> ner = [.. detector.Detect(source.AsMemory(), DetectionContext.Empty)];
        IReadOnlyList<Detection> merged = DetectionMerger.Merge(ner);

        merged.Select(d => (d.Start, d.Length, d.Type)).Should().OnlyHaveUniqueItems();
    }

    private static NerModelConfig RequireModelConfig()
    {
        string? root = Environment.GetEnvironmentVariable(ModelDirEnvVar);
        bool ready = !string.IsNullOrEmpty(root)
            && File.Exists(Directory.Exists(root) ? Path.Combine(root, "ner.onnx") : root);
        Skip.IfNot(
            ready,
            $"{ModelDirEnvVar} unset or model not found. Point it at a directory containing ner.onnx, tokenizer.json, and labels.json to run integration assertions.");
        return NerModelConfig.FromModelPath(root!);
    }
}
