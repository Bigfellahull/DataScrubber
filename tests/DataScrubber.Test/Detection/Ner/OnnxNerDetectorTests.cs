namespace DataScrubber.Test.Detection.Ner;

using DataScrubber.Detection;
using DataScrubber.Detection.Ner;
using FluentAssertions;
using Xunit;

public class OnnxNerDetectorTests
{
    private static readonly LabelMap _defaultLabels =
        new(["O", "B-PER", "I-PER", "B-ORG", "I-ORG", "B-LOC", "I-LOC"]);

    [Fact]
    public void DetectsSinglePersonSpanFromMockedSession()
    {
        FakeTokenizer tokenizer = new("Sarah called",
        [
            new FakeToken("Sarah",  0, 5, 10),
            new FakeToken("called", 6, 6, 11),
        ]);

        FakeSession session = new(_defaultLabels, _ =>
        [
            // [CLS], Sarah, called, [SEP], pad...
            (0, 0.99f),  // CLS → O
            (1, 0.95f),  // Sarah → B-PER
            (0, 0.99f),  // called → O
            (0, 0.99f),  // SEP → O
        ]);

        OnnxNerDetector detector = BuildDetector(tokenizer, session, NerThresholds.Defaults);
        IReadOnlyList<Detection> detections = [.. detector.Detect("Sarah called".AsMemory(), DetectionContext.Empty)];

        detections.Should().ContainSingle();
        detections[0].Type.Should().Be(DetectionType.Person);
        detections[0].Start.Should().Be(0);
        detections[0].Length.Should().Be(5);
        detections[0].Confidence.Should().BeGreaterThanOrEqualTo(0.85);
        detections[0].SourceRule.Should().Be("ner.onnx");
    }

    [Fact]
    public void BelowThresholdSpansAreDropped()
    {
        FakeTokenizer tokenizer = new("Sarah",
        [
            new FakeToken("Sarah", 0, 5, 10),
        ]);

        FakeSession session = new(_defaultLabels, _ =>
        [
            (0, 0.99f),
            (1, 0.84f),  // B-PER at 0.84, just under 0.85 threshold
            (0, 0.99f),
        ]);

        OnnxNerDetector detector = BuildDetector(tokenizer, session, NerThresholds.Defaults);
        IReadOnlyList<Detection> detections = [.. detector.Detect("Sarah".AsMemory(), DetectionContext.Empty)];

        detections.Should().BeEmpty("0.84 is below the Person threshold of 0.85");
    }

    [Fact]
    public void LocationThresholdIsLowerThanPersonAndOrganization()
    {
        FakeTokenizer tokenizer = new("Berlin",
        [
            new FakeToken("Berlin", 0, 6, 10),
        ]);

        FakeSession session = new(_defaultLabels, _ =>
        [
            (0, 0.99f),
            (5, 0.81f),  // B-LOC, above 0.80 threshold
            (0, 0.99f),
        ]);

        OnnxNerDetector detector = BuildDetector(tokenizer, session, NerThresholds.Defaults);
        IReadOnlyList<Detection> detections = [.. detector.Detect("Berlin".AsMemory(), DetectionContext.Empty)];

        detections.Should().ContainSingle();
        detections[0].Type.Should().Be(DetectionType.Location);
    }

    [Fact]
    public void NerDetectionsCarryNerDetectorPriority()
    {
        FakeTokenizer tokenizer = new("Sarah",
        [
            new FakeToken("Sarah", 0, 5, 10),
        ]);

        FakeSession session = new(_defaultLabels, _ =>
        [
            (0, 0.99f),
            (1, 0.95f),
            (0, 0.99f),
        ]);

        OnnxNerDetector detector = BuildDetector(tokenizer, session, NerThresholds.Defaults);
        Detection person = detector.Detect("Sarah".AsMemory(), DetectionContext.Empty).Single();

        DetectorPriority.For(person.Type).Should().Be(DetectorPriority.Ner);
    }

    [Fact]
    public void OverlappingWindowsDoNotEmitDuplicateDetections()
    {
        // Build a long synthetic input where every other token is a B-PER over
        // many windows. The detector advances by (maxSeqLen - 2 - stride) each
        // window, so window-edge entities must be deduplicated by (Start, Length, Type).
        const int tokenCount = 600;
        FakeToken[] tokens = new FakeToken[tokenCount];
        char[] text = new char[tokenCount * 5];
        for (int i = 0; i < tokenCount; i++)
        {
            int start = i * 5;
            text[start + 0] = 'A';
            text[start + 1] = 'a';
            text[start + 2] = 'a';
            text[start + 3] = 'a';
            text[start + 4] = ' ';
            tokens[i] = new FakeToken("Aaaa", start, 4, 100 + (i % 50));
        }
        string input = new(text);

        FakeTokenizer tokenizer = new(input, tokens);

        // Every content token argmaxes to B-PER with high confidence.
        FakeSession session = new(_defaultLabels, _ => null);
        session.LogitProvider = (long[] inputIds) =>
        {
            int seqLen = inputIds.Length;
            (int Argmax, float MaxProb)[] result = new (int, float)[seqLen];
            result[0] = (0, 0.99f); // [CLS] → O
            for (int t = 1; t < seqLen - 1; t++)
            {
                if (inputIds[t] == 0)
                {
                    result[t] = (0, 0.99f);
                }
                else
                {
                    result[t] = (1, 0.95f);  // B-PER for every content token
                }
            }
            result[seqLen - 1] = (0, 0.99f); // last one
            return result;
        };

        NerModelConfig config = new("model.onnx", "tok.json", "lab.json", MaxSeqLen: 64, Stride: 16);
        OnnxNerDetector detector = BuildDetectorWithConfig(tokenizer, session, NerThresholds.Defaults, config);

        IReadOnlyList<Detection> detections = [.. detector.Detect(input.AsMemory(), DetectionContext.Empty)];

        // The algorithm produces one B-PER span per token. Different windows
        // see the same token at the same character offset; dedup must keep
        // exactly one.
        detections.Should().HaveCount(tokenCount);
        detections.Select(d => d.Start).Distinct().Should().HaveCount(tokenCount);
    }

    [Fact]
    public void MultiTokenEntityInWindowOverlapRegionIsEmittedExactlyOnce()
    {
        // Spec risk M3: entities straddling a window boundary must dedup. With
        // maxSeqLen=8 (contentBudget=6) and stride=4, windows advance by 2 tokens
        // and overlap by 4. A two-token "B-PER, I-PER" spanning indices 3 and 4
        // sits inside both window 0 ([0,6)) and window 1 ([2,8)); both produce
        // identical spans which must collapse to one detection.
        const int total = 10;
        FakeToken[] tokens = new FakeToken[total];
        char[] text = new char[total * 5];
        for (int i = 0; i < total; i++)
        {
            int s = i * 5;
            text[s + 0] = 'A';
            text[s + 1] = 'a';
            text[s + 2] = 'a';
            text[s + 3] = 'a';
            text[s + 4] = ' ';
            tokens[i] = new FakeToken("Aaaa", s, 4, 100 + i);
        }

        FakeTokenizer tokenizer = new(new string(text), tokens);
        FakeSession session = new(_defaultLabels, _ => []);
        session.LogitProvider = (long[] inputIds) =>
        {
            int seqLen = inputIds.Length;
            (int Argmax, float MaxProb)[] result = new (int, float)[seqLen];
            for (int t = 0; t < seqLen; t++)
            {
                long id = inputIds[t];
                int label = id switch
                {
                    103 => 1,  // token index 3 → B-PER
                    104 => 2,  // token index 4 → I-PER
                    _ => 0,
                };
                result[t] = (label, 0.95f);
            }
            return result;
        };

        NerModelConfig config = new("model.onnx", "tok.json", "lab.json", MaxSeqLen: 8, Stride: 4);
        OnnxNerDetector detector = BuildDetectorWithConfig(tokenizer, session, NerThresholds.Defaults, config);

        IReadOnlyList<Detection> detections = [.. detector.Detect(new string(text).AsMemory(), DetectionContext.Empty)];

        detections.Should().ContainSingle();
        detections[0].Type.Should().Be(DetectionType.Person);
        detections[0].Start.Should().Be(tokens[3].Start);
        detections[0].Length.Should().Be(tokens[4].Start + tokens[4].Length - tokens[3].Start);
    }

    [Fact]
    public void EmptyInputProducesNoDetectionsAndDoesNotLoadModel()
    {
        FakeTokenizer tokenizer = new(string.Empty, []);
        FakeSession session = new(_defaultLabels, _ => []);
        OnnxNerDetector detector = BuildDetector(tokenizer, session, NerThresholds.Defaults);

        detector.Detect(string.Empty.AsMemory(), DetectionContext.Empty).Should().BeEmpty();
        session.RunCount.Should().Be(0);
    }

    [Fact]
    public void IncoherentSessionLogitsLengthThrowsNerModelLoadException()
    {
        // A real model that returns the wrong-shape logits would otherwise
        // surface as a low-level RangeException; the detector wraps it as a
        // typed NER load failure so the CLI can map to exit 4.
        FakeTokenizer tokenizer = new("Sarah", new[]
        {
            new FakeToken("Sarah", 0, 5, 10),
        });

        BadLengthSession session = new(_defaultLabels);
        OnnxNerDetector detector = BuildDetector(tokenizer, session, NerThresholds.Defaults);

        FluentActions.Invoking(() => detector.Detect("Sarah".AsMemory(), DetectionContext.Empty).ToList())
            .Should().Throw<NerModelLoadException>();
    }

    [Fact]
    public void EnsureLoadedSurfacesNerModelLoadException()
    {
        // The session factory runs first so the user-supplied --model path
        // is named in the error before tokenizer or labels failures.
        NerModelConfig config = new("missing-model.onnx", "missing-tok.json", "missing-lab.json");
        OnnxNerDetector detector = new(
            config,
            NerThresholds.Defaults,
            _ => throw new NerModelLoadException("tok missing", "missing-tok.json"),
            _ => throw new NerModelLoadException("model missing", "missing-model.onnx"),
            _ => throw new NerModelLoadException("labels missing", "missing-lab.json"));

        FluentActions.Invoking(() => detector.EnsureLoaded())
            .Should().Throw<NerModelLoadException>()
            .Where(ex => ex.MissingPath == "missing-model.onnx");
    }

    private static OnnxNerDetector BuildDetector(INerTokenizer tokenizer, INerInferenceSession session, NerThresholds thresholds)
    {
        // A small max sequence length keeps test logits manageable.
        NerModelConfig config = new("m", "t", "l", MaxSeqLen: 16, Stride: 4);
        return BuildDetectorWithConfig(tokenizer, session, thresholds, config);
    }

    private static OnnxNerDetector BuildDetectorWithConfig(
        INerTokenizer tokenizer,
        INerInferenceSession session,
        NerThresholds thresholds,
        NerModelConfig config) =>
        new(config, thresholds, _ => tokenizer, _ => session, _ => _defaultLabels);

    private sealed record FakeToken(string Surface, int Start, int Length, int Id);

    private sealed class FakeTokenizer(string source, IReadOnlyList<FakeToken> tokens) : INerTokenizer
    {
        public int ClsTokenId => 1;

        public int SepTokenId => 2;

        public int PadTokenId => 0;

        public string Source { get; } = source;

        public IReadOnlyList<FakeToken> Tokens { get; } = tokens;

        public TokenizedInput Tokenize(string input)
        {
            input.Should().Be(Source, "fake tokenizer was wired for a specific input");
            int[] ids = [.. Tokens.Select(t => t.Id)];
            TokenSpan[] offsets = [.. Tokens.Select(t => new TokenSpan(t.Start, t.Length))];
            return new TokenizedInput(ids, offsets);
        }
    }

    private sealed class FakeSession(LabelMap labels, Func<long[], (int Argmax, float MaxProb)[]?> logits) : INerInferenceSession
    {
        private readonly LabelMap _labels = labels;

        public int NumLabels => _labels.Count;

        public int RunCount { get; private set; }

        public Func<long[], (int Argmax, float MaxProb)[]?> LogitProvider { get; set; } = logits;

        public float[] Run(long[] inputIds, long[] attentionMask, long[]? tokenTypeIds)
        {
            RunCount++;
            (int Argmax, float MaxProb)[] perToken = LogitProvider(inputIds)
                ?? throw new InvalidOperationException("LogitProvider returned null");

            int seqLen = inputIds.Length;
            float[] result = new float[seqLen * NumLabels];
            for (int t = 0; t < seqLen; t++)
            {
                (int argmax, float prob) = t < perToken.Length ? perToken[t] : (0, 0.99f);
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

    private sealed class BadLengthSession(LabelMap labels) : INerInferenceSession
    {
        public int NumLabels { get; } = labels.Count;

        public float[] Run(long[] inputIds, long[] attentionMask, long[]? tokenTypeIds)
            => new float[NumLabels];

        public void Dispose() { }
    }
}
