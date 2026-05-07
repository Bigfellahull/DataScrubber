namespace DataScrubber.Detection.Ner;

/// <summary>
///     A single span produced by <see cref="BioSpanReconstructor"/>: the
///     character range it covers in the original input, the detection type
///     selected by the BIO assembler, and the mean per-token probability of
///     the chosen class across the span.
/// </summary>
/// <param name="Start">Zero-based start character offset.</param>
/// <param name="Length">Length of the span in characters.</param>
/// <param name="Type">The detection type assigned to the span.</param>
/// <param name="Confidence">Mean per-token softmax probability of the chosen class.</param>
public readonly record struct NerSpan(int Start, int Length, DetectionType Type, double Confidence)
{
    /// <summary>Exclusive end offset.</summary>
    public int End => Start + Length;
}

/// <summary>
///     Reconstructs <see cref="NerSpan"/> records from per-token model output.
///     The algorithm:
///     <list type="number">
///         <item><description>Per-token softmax over the supplied logits to recover class probabilities.</description></item>
///         <item><description>Per-token argmax to pick the label.</description></item>
///         <item><description>Greedy BIO assembly: <c>B-X</c> opens a span; <c>I-X</c> extends only the matching <c>X</c>; otherwise close.</description></item>
///         <item><description>Confidence is the arithmetic mean of the chosen-class softmax probabilities for the tokens in the span.</description></item>
///     </list>
///     The reconstructor is pure: it has no I/O, no shared state, and never
///     allocates ONNX resources. Callers feed it tokens in source order and
///     character offsets back to the input.
/// </summary>
public static class BioSpanReconstructor
{
    /// <summary>
    ///     Reconstructs spans from one window's model output.
    /// </summary>
    /// <param name="logits">Flat per-token logits, length <c>tokenOffsets.Count × labels.Count</c>.</param>
    /// <param name="tokenOffsets">Character spans for each non-special token, in order.</param>
    /// <param name="labels">The label map used to interpret argmax outputs.</param>
    /// <returns>The reconstructed spans, in token order.</returns>
    public static IReadOnlyList<NerSpan> Reconstruct(
        ReadOnlySpan<float> logits,
        IReadOnlyList<TokenSpan> tokenOffsets,
        LabelMap labels)
    {
        ArgumentNullException.ThrowIfNull(tokenOffsets);
        ArgumentNullException.ThrowIfNull(labels);

        int tokenCount = tokenOffsets.Count;
        int numLabels = labels.Count;

        if (tokenCount == 0 || numLabels == 0)
        {
            return [];
        }

        if (logits.Length != tokenCount * numLabels)
        {
            throw new ArgumentException(
                $"logits length {logits.Length} does not match tokenCount({tokenCount}) * numLabels({numLabels})",
                nameof(logits));
        }

        List<NerSpan> spans = [];

        DetectionType? activeType = null;
        int activeStart = 0;
        int activeLength = 0;
        double activeProbSum = 0;
        int activeTokenCount = 0;

        for (int t = 0; t < tokenCount; t++)
        {
            (int argmax, double maxProb) = SoftmaxArgmax(logits.Slice(t * numLabels, numLabels));
            BioLabel label = labels[argmax];
            TokenSpan offset = tokenOffsets[t];

            // Tokens with zero-length offsets correspond to special or padding
            // positions and must not be allowed to extend a span.
            if (offset.Length <= 0)
            {
                CloseActive();
                continue;
            }

            switch (label)
            {
                case { Prefix: BioPrefix.Begin, Type: { } beginType }:
                    CloseActive();
                    activeType = beginType;
                    activeStart = offset.Start;
                    activeLength = offset.Length;
                    activeProbSum = maxProb;
                    activeTokenCount = 1;
                    break;

                case { Prefix: BioPrefix.Inside, Type: { } insideType } when activeType == insideType:
                    activeLength = offset.End - activeStart;
                    activeProbSum += maxProb;
                    activeTokenCount++;
                    break;

                default:
                    CloseActive();
                    break;
            }
        }

        CloseActive();
        return spans;

        void CloseActive()
        {
            if (activeType is null || activeTokenCount == 0 || activeLength <= 0)
            {
                activeType = null;
                activeTokenCount = 0;
                return;
            }

            spans.Add(new NerSpan(
                activeStart,
                activeLength,
                activeType.Value,
                activeProbSum / activeTokenCount));
            activeType = null;
            activeTokenCount = 0;
        }
    }

    private static (int Argmax, double MaxProb) SoftmaxArgmax(ReadOnlySpan<float> row)
    {
        // Argmax of softmax is the argmax of the raw logits, and the chosen
        // class's probability collapses to 1 / Σexp(logit_i - maxLogit). This
        // skips a redundant exp pass without changing the result.
        int argmax = 0;
        float maxLogit = row[0];
        for (int i = 1; i < row.Length; i++)
        {
            if (row[i] > maxLogit)
            {
                maxLogit = row[i];
                argmax = i;
            }
        }

        double sum = 0;
        for (int i = 0; i < row.Length; i++)
        {
            sum += Math.Exp(row[i] - maxLogit);
        }

        return (argmax, 1.0 / sum);
    }
}
