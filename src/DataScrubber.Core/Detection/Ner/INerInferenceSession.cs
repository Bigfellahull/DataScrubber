namespace DataScrubber.Detection.Ner;

/// <summary>
///     Inference abstraction over the NER model. Production code wires this to
///     <see cref="OnnxNerInferenceSession"/>, which loads an ONNX file via
///     <c>Microsoft.ML.OnnxRuntime</c>; tests substitute a fake to drive the
///     detector with deterministic logits without needing a real model.
/// </summary>
public interface INerInferenceSession : IDisposable
{
    /// <summary>The number of label classes the model produces per token.</summary>
    int NumLabels { get; }

    /// <summary>
    ///     Runs the model on a single window. The output is the per-token
    ///     logits flattened in row-major order: <c>seqLen × NumLabels</c>.
    /// </summary>
    /// <param name="inputIds">Padded token IDs with length <c>seqLen</c>.</param>
    /// <param name="attentionMask">Mask in <c>{0, 1}</c> with length <c>seqLen</c>.</param>
    /// <param name="tokenTypeIds">Optional segment IDs; supplied for BERT-family models, <c>null</c> otherwise.</param>
    /// <returns>The flattened logits.</returns>
    float[] Run(long[] inputIds, long[] attentionMask, long[]? tokenTypeIds);
}
