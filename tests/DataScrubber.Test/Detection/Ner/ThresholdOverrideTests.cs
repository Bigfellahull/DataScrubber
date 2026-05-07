namespace DataScrubber.Test.Detection.Ner;

using DataScrubber.Configuration;
using DataScrubber.Detection;
using DataScrubber.Detection.Ner;
using FluentAssertions;
using Xunit;

public class ThresholdOverrideTests
{
    [Fact]
    public void EmptyConfigYieldsDefaults()
    {
        NerThresholds thresholds = NerConfig.Empty.ToNerThresholds();

        thresholds.Should().Be(NerThresholds.Defaults);
    }

    [Fact]
    public void OverrideForSingleTypeKeepsDefaultsForOthers()
    {
        NerConfig config = new()
        {
            Thresholds = new Dictionary<DetectionType, double> { [DetectionType.Person] = 0.95 },
        };

        NerThresholds thresholds = config.ToNerThresholds();

        thresholds.Person.Should().Be(0.95);
        thresholds.Organization.Should().Be(NerThresholds.Defaults.Organization);
        thresholds.Location.Should().Be(NerThresholds.Defaults.Location);
    }

    [Fact]
    public void AllThreeTypesOverrideAtOnce()
    {
        NerConfig config = new()
        {
            Thresholds = new Dictionary<DetectionType, double>
            {
                [DetectionType.Person] = 0.5,
                [DetectionType.Organization] = 0.6,
                [DetectionType.Location] = 0.7,
            },
        };

        NerThresholds thresholds = config.ToNerThresholds();

        thresholds.Person.Should().Be(0.5);
        thresholds.Organization.Should().Be(0.6);
        thresholds.Location.Should().Be(0.7);
    }

    [Fact]
    public void Ac5_RaisedPersonThresholdDropsConfidence0_90Span()
    {
        // AC5: config ner.thresholds.Person = 0.95 must drop a detection whose
        // confidence is 0.90 (which is above the 0.85 default).
        NerConfig config = new()
        {
            Thresholds = new Dictionary<DetectionType, double> { [DetectionType.Person] = 0.95 },
        };

        IReadOnlyList<Detection> defaultThresholdDetections = RunWithThresholds(NerThresholds.Defaults);
        IReadOnlyList<Detection> raisedThresholdDetections = RunWithThresholds(config.ToNerThresholds());

        defaultThresholdDetections.Should().ContainSingle("0.90 is above the default 0.85 Person threshold");
        raisedThresholdDetections.Should().BeEmpty("0.90 is below the configured 0.95 Person threshold");
    }

    [Fact]
    public void Ac5_ParsedJsonConfigDrivesDetectorThreshold()
    {
        // AC5 end-to-end through the JSON parser: the threshold travels from
        // raw JSON → ScrubConfig.Parse → NerConfig.ToNerThresholds →
        // OnnxNerDetector and suppresses a 0.90 Person detection.
        const string json = """{ "schemaVersion": 1, "ner": { "thresholds": { "Person": 0.95 } } }""";

        ScrubConfig parsed = ScrubConfig.Parse(json);
        NerThresholds thresholds = parsed.Ner.ToNerThresholds();

        IReadOnlyList<Detection> detections = RunWithThresholds(thresholds);

        detections.Should().BeEmpty("the parsed config must propagate the 0.95 Person threshold to the detector");
    }

    private static IReadOnlyList<Detection> RunWithThresholds(NerThresholds thresholds)
    {
        LabelMap labels = new(["O", "B-PER", "I-PER", "B-ORG", "I-ORG", "B-LOC", "I-LOC"]);
        FakePersonTokenizer tokenizer = new();
        FakePersonSession session = new(labels);

        NerModelConfig modelConfig = new("m", "t", "l", MaxSeqLen: 16, Stride: 4);
        OnnxNerDetector detector = new(modelConfig, thresholds, _ => tokenizer, _ => session, _ => labels);

        return [.. detector.Detect("Sarah".AsMemory(), DetectionContext.Empty)];
    }

    private sealed class FakePersonTokenizer : INerTokenizer
    {
        public int ClsTokenId => 1;
        public int SepTokenId => 2;
        public int PadTokenId => 0;

        public TokenizedInput Tokenize(string input)
            => new([10], [new TokenSpan(0, 5)]);
    }

    private sealed class FakePersonSession(LabelMap labels) : INerInferenceSession
    {
        private const float Confidence = 0.90f;

        public int NumLabels { get; } = labels.Count;

        public float[] Run(long[] inputIds, long[] attentionMask, long[]? tokenTypeIds)
        {
            int seqLen = inputIds.Length;
            float[] result = new float[seqLen * NumLabels];
            for (int t = 0; t < seqLen; t++)
            {
                int argmax = t == 1 ? 1 : 0;
                float prob = t == 1 ? Confidence : 0.99f;
                double low = Math.Log((1.0 - prob) / Math.Max(1, NumLabels - 1));
                double high = Math.Log(prob);
                for (int i = 0; i < NumLabels; i++)
                {
                    result[t * NumLabels + i] = (float)(i == argmax ? high : low);
                }
            }

            return result;
        }

        public void Dispose() { }
    }
}
