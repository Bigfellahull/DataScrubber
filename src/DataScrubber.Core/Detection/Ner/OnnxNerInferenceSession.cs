namespace DataScrubber.Detection.Ner;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

/// <summary>
///     Production <see cref="INerInferenceSession"/> backed by
///     <see cref="InferenceSession"/>. Wraps the model's expected input names
///     (<c>input_ids</c>, <c>attention_mask</c>, optional <c>token_type_ids</c>)
///     and the <c>logits</c> output. Constructing an instance loads the model
///     into memory; <see cref="Dispose"/> releases it.
/// </summary>
public sealed class OnnxNerInferenceSession : INerInferenceSession
{
    private readonly InferenceSession _session;
    private readonly string _inputIdsName;
    private readonly string _attentionMaskName;
    private readonly string? _tokenTypeIdsName;
    private readonly string _logitsName;

    /// <inheritdoc />
    public int NumLabels { get; }

    private OnnxNerInferenceSession(
        InferenceSession session,
        string inputIdsName,
        string attentionMaskName,
        string? tokenTypeIdsName,
        string logitsName,
        int numLabels)
    {
        _session = session;
        _inputIdsName = inputIdsName;
        _attentionMaskName = attentionMaskName;
        _tokenTypeIdsName = tokenTypeIdsName;
        _logitsName = logitsName;
        NumLabels = numLabels;
    }

    /// <summary>
    ///     Loads the ONNX model at <paramref name="modelPath"/> and resolves
    ///     its input/output node names.
    /// </summary>
    /// <param name="modelPath">Absolute path to the ONNX model file.</param>
    /// <returns>A ready-to-use session.</returns>
    /// <exception cref="NerModelLoadException">Raised if the model is missing or the runtime cannot load it.</exception>
    public static OnnxNerInferenceSession Load(string modelPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelPath);

        if (!File.Exists(modelPath))
        {
            throw new NerModelLoadException($"NER model not found: {modelPath}", modelPath);
        }

        InferenceSession? session = null;
        try
        {
            session = new InferenceSession(modelPath);
            string inputIdsName = ResolveNodeName(session.InputMetadata, ["input_ids"], modelPath, "input");
            string attentionMaskName = ResolveNodeName(session.InputMetadata, ["attention_mask"], modelPath, "input");
            string? tokenTypeIdsName = session.InputMetadata.ContainsKey("token_type_ids") ? "token_type_ids" : null;
            string logitsName = ResolveNodeName(session.OutputMetadata, ["logits"], modelPath, "output");

            int numLabels = session.OutputMetadata[logitsName].Dimensions[^1];
            if (numLabels <= 0)
            {
                throw new NerModelLoadException(
                    $"NER model at {modelPath} has dynamic label dimension; cannot determine number of labels",
                    modelPath);
            }

            OnnxNerInferenceSession instance = new(
                session,
                inputIdsName,
                attentionMaskName,
                tokenTypeIdsName,
                logitsName,
                numLabels);
            session = null;
            return instance;
        }
        catch (NerModelLoadException)
        {
            session?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            session?.Dispose();
            throw new NerModelLoadException(
                $"failed to load NER model at {modelPath}: {ex.Message}",
                modelPath,
                ex);
        }
    }

    /// <inheritdoc />
    public float[] Run(long[] inputIds, long[] attentionMask, long[]? tokenTypeIds)
    {
        ArgumentNullException.ThrowIfNull(inputIds);
        ArgumentNullException.ThrowIfNull(attentionMask);

        if (inputIds.Length != attentionMask.Length)
        {
            throw new ArgumentException(
                "input_ids and attention_mask must be the same length",
                nameof(attentionMask));
        }

        int[] dims = [1, inputIds.Length];
        DenseTensor<long> idsTensor = new(inputIds, dims);
        DenseTensor<long> maskTensor = new(attentionMask, dims);

        List<NamedOnnxValue> inputs =
        [
            NamedOnnxValue.CreateFromTensor(_inputIdsName, idsTensor),
            NamedOnnxValue.CreateFromTensor(_attentionMaskName, maskTensor),
        ];

        if (_tokenTypeIdsName is not null)
        {
            long[] segments = tokenTypeIds ?? new long[inputIds.Length];
            DenseTensor<long> segTensor = new(segments, dims);
            inputs.Add(NamedOnnxValue.CreateFromTensor(_tokenTypeIdsName, segTensor));
        }

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _session.Run(inputs);
        DisposableNamedOnnxValue match = outputs.First(o => o.Name == _logitsName);
        return [.. match.AsTensor<float>()];
    }

    /// <inheritdoc />
    public void Dispose() => _session.Dispose();

    private static string ResolveNodeName(
        IReadOnlyDictionary<string, NodeMetadata> metadata,
        string[] candidates,
        string modelPath,
        string kind)
    {
        foreach (string name in candidates)
        {
            if (metadata.ContainsKey(name))
            {
                return name;
            }
        }

        throw new NerModelLoadException(
            $"NER model at {modelPath} has no {kind} named any of: {string.Join(", ", candidates)}",
            modelPath);
    }
}
