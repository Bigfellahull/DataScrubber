namespace DataScrubber.Detection.Ner;

/// <summary>
///     Thrown when the NER model, tokenizer, or label-map cannot be located,
///     opened, or parsed. The CLI translates this into exit code <c>4</c>; the
///     <see cref="MissingPath"/> property carries the offending file path so
///     the message can name it precisely.
/// </summary>
public sealed class NerModelLoadException : Exception
{
    /// <summary>
    ///     The file path that caused the failure (model, tokenizer, or label
    ///     map). Always populated.
    /// </summary>
    public string MissingPath { get; }

    /// <summary>
    ///     Creates a new <see cref="NerModelLoadException"/>.
    /// </summary>
    /// <param name="message">A user-facing description of the failure.</param>
    /// <param name="missingPath">The file path that failed to load.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public NerModelLoadException(string message, string missingPath, Exception? innerException = null)
        : base(message, innerException)
    {
        MissingPath = missingPath;
    }
}
