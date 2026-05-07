namespace DataScrubber.Test.Detection.Ner;

using DataScrubber.Detection;
using DataScrubber.Detection.Ner;
using FluentAssertions;
using Xunit;

public class BioSpanReconstructorTests
{
    private static readonly LabelMap _labels =
        new(["O", "B-PER", "I-PER", "B-ORG", "I-ORG", "B-LOC", "I-LOC"]);

    [Fact]
    public void BIBSpanCoversBeginAndContinuingTokensCharacterRange()
    {
        // Tokens (char offsets):
        //   0: "John" [0,4)        → B-PER
        //   1: "Smith" [5,10)      → I-PER
        //   2: "called" [11,17)    → O
        TokenSpan[] offsets =
        [
            new(0, 4),
            new(5, 5),
            new(11, 6),
        ];

        float[] logits = BuildLogits(
            (1, 0.95f),
            (2, 0.93f),
            (0, 0.99f));

        IReadOnlyList<NerSpan> spans = BioSpanReconstructor.Reconstruct(logits, offsets, _labels);

        spans.Should().ContainSingle();
        spans[0].Type.Should().Be(DetectionType.Person);
        spans[0].Start.Should().Be(0);
        spans[0].Length.Should().Be(10);
        spans[0].Confidence.Should().BeApproximately(0.94, 0.05);
    }

    [Fact]
    public void DifferentTypeIInsideTokenForcesSpanClose()
    {
        TokenSpan[] offsets =
        [
            new(0, 4),
            new(5, 5),
        ];

        float[] logits = BuildLogits(
            (1, 0.95f),  // B-PER
            (4, 0.95f)); // I-ORG (different type → close)

        IReadOnlyList<NerSpan> spans = BioSpanReconstructor.Reconstruct(logits, offsets, _labels);

        spans.Should().ContainSingle();
        spans[0].Type.Should().Be(DetectionType.Person);
        spans[0].Length.Should().Be(4);
    }

    [Fact]
    public void OutsideOnlyInputProducesNoSpans()
    {
        TokenSpan[] offsets = [new(0, 4), new(5, 4)];
        float[] logits = BuildLogits((0, 0.99f), (0, 0.99f));

        BioSpanReconstructor.Reconstruct(logits, offsets, _labels).Should().BeEmpty();
    }

    [Fact]
    public void ConsecutiveBeginsProduceTwoSpans()
    {
        TokenSpan[] offsets =
        [
            new(0, 5),
            new(6, 5),
        ];

        float[] logits = BuildLogits(
            (1, 0.92f),
            (3, 0.94f));

        IReadOnlyList<NerSpan> spans = BioSpanReconstructor.Reconstruct(logits, offsets, _labels);

        spans.Should().HaveCount(2);
        spans[0].Type.Should().Be(DetectionType.Person);
        spans[1].Type.Should().Be(DetectionType.Organization);
    }

    [Fact]
    public void ZeroLengthOffsetsClosesActiveSpan()
    {
        TokenSpan[] offsets =
        [
            new(0, 4),
            new(0, 0),  // padding/special — must close
            new(5, 5),
        ];

        float[] logits = BuildLogits(
            (1, 0.95f),
            (2, 0.99f),  // I-PER, but should be ignored due to zero-length
            (1, 0.92f));

        IReadOnlyList<NerSpan> spans = BioSpanReconstructor.Reconstruct(logits, offsets, _labels);

        spans.Should().HaveCount(2);
        spans[0].Length.Should().Be(4);
        spans[1].Start.Should().Be(5);
    }

    [Fact]
    public void LogitsLengthMismatchThrows()
    {
        TokenSpan[] offsets = [new(0, 4)];
        float[] logits = new float[_labels.Count - 1];

        FluentActions.Invoking(() => BioSpanReconstructor.Reconstruct(logits, offsets, _labels))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IInsideWithNoActiveSpanIsTreatedAsOutside()
    {
        TokenSpan[] offsets = [new(0, 4), new(5, 4)];
        float[] logits = BuildLogits(
            (2, 0.99f),  // I-PER with no active → close (no span)
            (0, 0.99f));

        BioSpanReconstructor.Reconstruct(logits, offsets, _labels).Should().BeEmpty();
    }

    private static float[] BuildLogits(params (int Argmax, float MaxProb)[] tokens)
    {
        int numLabels = _labels.Count;
        float[] logits = new float[tokens.Length * numLabels];
        for (int t = 0; t < tokens.Length; t++)
        {
            (int argmax, float prob) = tokens[t];
            // Find logit values that produce the desired argmax probability
            // under a one-vs-rest softmax. Putting "high" on the chosen index
            // and "low" on the rest gives a softmax mass close to `prob` —
            // exact value is not important for these tests since they only
            // assert on argmax behaviour and confidence ordering.
            double low = Math.Log((1.0 - prob) / Math.Max(1, numLabels - 1));
            double high = Math.Log(prob);
            for (int i = 0; i < numLabels; i++)
            {
                logits[t * numLabels + i] = (float)(i == argmax ? high : low);
            }
        }

        return logits;
    }
}
