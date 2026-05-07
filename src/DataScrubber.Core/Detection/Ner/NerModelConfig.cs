namespace DataScrubber.Detection.Ner;

/// <summary>
///     File-system paths and shape parameters needed to load and run the local
///     ONNX named-entity recogniser. M4 will inject configured instances; M3
///     constructs them in the CLI from the resolved <c>--model</c> path.
/// </summary>
/// <param name="ModelPath">Absolute path to the ONNX model file.</param>
/// <param name="TokenizerPath">Absolute path to the HuggingFace <c>tokenizer.json</c>.</param>
/// <param name="LabelMapPath">Absolute path to <c>labels.json</c> mapping output indices to BIO labels.</param>
/// <param name="MaxSeqLen">The model's maximum sequence length in tokens, including special tokens.</param>
/// <param name="Stride">Token stride between consecutive windows.</param>
public sealed record NerModelConfig(
    string ModelPath,
    string TokenizerPath,
    string LabelMapPath,
    int MaxSeqLen = 256,
    int Stride = 32)
{
    /// <summary>The default maximum sequence length used when callers omit it.</summary>
    public const int DefaultMaxSeqLen = 256;

    /// <summary>The default token stride used when callers omit it.</summary>
    public const int DefaultStride = 32;

    /// <summary>
    ///     Resolves the canonical layout where a model directory holds
    ///     <c>ner.onnx</c>, <c>tokenizer.json</c>, and <c>labels.json</c>.
    ///     <paramref name="modelPath"/> may point at the model file or at the
    ///     containing directory.
    /// </summary>
    /// <param name="modelPath">A model file path or a directory containing the canonical filenames.</param>
    /// <returns>A config rooted at the directory holding the model file.</returns>
    public static NerModelConfig FromModelPath(string modelPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelPath);

        string fullPath = Path.GetFullPath(modelPath);
        bool isDirectory = Directory.Exists(fullPath);
        string modelFile = isDirectory ? Path.Combine(fullPath, "ner.onnx") : fullPath;
        string directory = Path.GetDirectoryName(modelFile) ?? fullPath;

        return new NerModelConfig(
            modelFile,
            Path.Combine(directory, "tokenizer.json"),
            Path.Combine(directory, "labels.json"));
    }
}
