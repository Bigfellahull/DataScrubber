namespace DataScrubber.Detection.Ner;

/// <summary>
///     A local ONNX-backed named-entity recogniser that produces
///     <see cref="DetectionType.Person"/>, <see cref="DetectionType.Organization"/>,
///     and <see cref="DetectionType.Location"/> detections. The model, tokenizer
///     and label map are all loaded from disk; lazy-loading happens on the first
///     call to <see cref="Detect(ReadOnlyMemory{char}, DetectionContext)"/> so
///     constructing a detector is cheap and recoverable.
/// </summary>
/// <remarks>
///     The detector is single-threaded: an instance must not be shared across
///     parallel <see cref="Detect(ReadOnlyMemory{char}, DetectionContext)"/>
///     callers. The CLI is single-threaded per run, which matches.
/// </remarks>
public sealed class OnnxNerDetector : IDetector, IDisposable
{
    private readonly NerModelConfig _config;
    private readonly NerThresholds _thresholds;
    private readonly Func<NerModelConfig, INerTokenizer> _tokenizerFactory;
    private readonly Func<NerModelConfig, INerInferenceSession> _sessionFactory;
    private readonly Func<NerModelConfig, LabelMap> _labelMapFactory;

    private INerTokenizer? _tokenizer;
    private INerInferenceSession? _session;
    private LabelMap? _labels;
    private bool _disposed;

    /// <summary>
    ///     Creates a detector configured for the production wiring: an ONNX
    ///     <see cref="OnnxNerInferenceSession"/>, a vendored
    ///     <see cref="BertWordPieceTokenizer"/>, and a JSON-backed
    ///     <see cref="LabelMap"/>.
    /// </summary>
    /// <param name="config">Resolved paths and shape parameters.</param>
    /// <param name="thresholds">Per-type confidence thresholds. Defaults to <see cref="NerThresholds.Defaults"/>.</param>
    public OnnxNerDetector(NerModelConfig config, NerThresholds? thresholds = null)
        : this(
            config,
            thresholds ?? NerThresholds.Defaults,
            static c => BertWordPieceTokenizer.Load(c.TokenizerPath),
            static c => OnnxNerInferenceSession.Load(c.ModelPath),
            static c => LabelMap.Load(c.LabelMapPath))
    {
    }

    /// <summary>
    ///     Creates a detector with explicit factories. Used by tests to inject
    ///     fakes; production callers should prefer the simpler constructor.
    /// </summary>
    /// <param name="config">Resolved paths and shape parameters.</param>
    /// <param name="thresholds">Per-type confidence thresholds.</param>
    /// <param name="tokenizerFactory">Builds the tokenizer on first <see cref="Detect"/>.</param>
    /// <param name="sessionFactory">Builds the inference session on first <see cref="Detect"/>.</param>
    /// <param name="labelMapFactory">Builds the label map on first <see cref="Detect"/>.</param>
    public OnnxNerDetector(
        NerModelConfig config,
        NerThresholds thresholds,
        Func<NerModelConfig, INerTokenizer> tokenizerFactory,
        Func<NerModelConfig, INerInferenceSession> sessionFactory,
        Func<NerModelConfig, LabelMap> labelMapFactory)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(tokenizerFactory);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(labelMapFactory);

        if (config.MaxSeqLen <= 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config),
                "MaxSeqLen must accommodate at least one content token plus [CLS] and [SEP].");
        }

        if (config.Stride < 0 || config.Stride >= config.MaxSeqLen)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config),
                "Stride must be non-negative and strictly less than MaxSeqLen.");
        }

        _config = config;
        _thresholds = thresholds;
        _tokenizerFactory = tokenizerFactory;
        _sessionFactory = sessionFactory;
        _labelMapFactory = labelMapFactory;
    }

    /// <summary>
    ///     Eagerly initialises the tokenizer, label map, and inference session.
    ///     Called by the CLI at start-up so missing-file errors surface before
    ///     the first input byte is read; <see cref="Detect"/> still triggers
    ///     lazy loading if <see cref="EnsureLoaded"/> was not called explicitly.
    /// </summary>
    /// <exception cref="NerModelLoadException">Raised when any artefact is missing or unparsable.</exception>
    public void EnsureLoaded()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Order matters: when `--model` points at a missing file, the user
        // expects the model path to be named in the error. Loading the
        // session first surfaces that case directly. Tokenizer and label-map
        // failures still throw the same exception type with their own paths.
        _session ??= _sessionFactory(_config);
        _tokenizer ??= _tokenizerFactory(_config);
        _labels ??= _labelMapFactory(_config);
    }

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (input.Length == 0)
        {
            return [];
        }

        EnsureLoaded();

        TokenizedInput tokenized = _tokenizer!.Tokenize(input.ToString());
        if (tokenized.TokenIds.Count == 0)
        {
            return [];
        }

        Dictionary<(int Start, int Length, DetectionType Type), NerSpan> dedup = [];
        int contentBudget = _config.MaxSeqLen - 2;
        int step = Math.Max(1, contentBudget - _config.Stride);

        for (int start = 0; start < tokenized.TokenIds.Count; start += step)
        {
            int windowEnd = Math.Min(tokenized.TokenIds.Count, start + contentBudget);
            RunWindow(tokenized, start, windowEnd, dedup);
            if (windowEnd == tokenized.TokenIds.Count)
            {
                break;
            }
        }

        List<Detection> emitted = new(dedup.Count);
        foreach (NerSpan span in dedup.Values)
        {
            if (span.Confidence < _thresholds.For(span.Type))
            {
                continue;
            }

            Detection detection = new(
                span.Start,
                span.Length,
                span.Type,
                span.Confidence,
                "ner.onnx");

            if (ctx.ShouldDrop(detection))
            {
                continue;
            }

            emitted.Add(detection);
        }

        return emitted;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session?.Dispose();
        _disposed = true;
    }

    private void RunWindow(
        TokenizedInput tokenized,
        int start,
        int end,
        Dictionary<(int Start, int Length, DetectionType Type), NerSpan> dedup)
    {
        int contentLength = end - start;
        int seqLen = _config.MaxSeqLen;

        long[] inputIds = new long[seqLen];
        long[] attentionMask = new long[seqLen];
        inputIds[0] = _tokenizer!.ClsTokenId;
        attentionMask[0] = 1;

        for (int i = 0; i < contentLength; i++)
        {
            inputIds[1 + i] = tokenized.TokenIds[start + i];
            attentionMask[1 + i] = 1;
        }

        inputIds[1 + contentLength] = _tokenizer.SepTokenId;
        attentionMask[1 + contentLength] = 1;

        for (int i = 1 + contentLength + 1; i < seqLen; i++)
        {
            inputIds[i] = _tokenizer.PadTokenId;
        }

        float[] logits = _session!.Run(inputIds, attentionMask, null);
        int numLabels = _session.NumLabels;

        int expected = seqLen * numLabels;
        if (logits.Length != expected)
        {
            throw new NerModelLoadException(
                $"NER model produced logits of length {logits.Length}; expected {expected} (seqLen={seqLen}, numLabels={numLabels})",
                _config.ModelPath);
        }

        // Slice past [CLS] to land on the content rows; [SEP]/PAD past the end
        // are simply not addressed. The reconstructor accepts a span so no copy
        // is needed.
        ReadOnlySpan<float> contentLogits = logits.AsSpan(numLabels, contentLength * numLabels);

        TokenSpan[] contentOffsets = new TokenSpan[contentLength];
        for (int i = 0; i < contentLength; i++)
        {
            contentOffsets[i] = tokenized.Offsets[start + i];
        }

        IReadOnlyList<NerSpan> spans = BioSpanReconstructor.Reconstruct(contentLogits, contentOffsets, _labels!);
        foreach (NerSpan span in spans)
        {
            (int Start, int Length, DetectionType Type) key = (span.Start, span.Length, span.Type);
            if (!dedup.TryGetValue(key, out NerSpan existing) || span.Confidence > existing.Confidence)
            {
                dedup[key] = span;
            }
        }
    }
}
