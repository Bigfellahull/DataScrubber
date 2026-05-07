namespace DataScrubber.Cli;

using System.Text;
using DataScrubber.Detection;

/// <summary>
///     Streams a UTF-8 text source through the detector pipeline using a
///     bounded sliding window so that 1 GB inputs scrub in constant memory.
///     The carry buffer between iterations is capped at 4 KB; entities that
///     span more than that are documented as a known limitation in M5. Reads
///     are chunk-based rather than line-based so input bytes (including line
///     endings) flow through unchanged, matching the byte-preserving
///     guarantee from D5.
/// </summary>
public sealed class StreamingScrubber
{
    /// <summary>
    ///     Maximum amount of trailing text held over between iterations so
    ///     cross-line entities can still be matched. Acts as the "X" in the
    ///     <c>RSS &lt; 4× streaming buffer</c> bound asserted by AC2.
    /// </summary>
    public const int CarryBufferSize = 4096;

    /// <summary>
    ///     Default chunk size requested per
    ///     <see cref="TextReader.Read(char[], int, int)"/> call. Sized so the
    ///     working set is dominated by
    ///     <see cref="CarryBufferSize"/>: each iteration touches at most
    ///     <c>CarryBufferSize + ChunkSize</c> chars of input plus an output
    ///     buffer of the same order.
    /// </summary>
    public const int ChunkSize = 8192;

    private readonly IDetector _detector;
    private readonly DetectionContext _baseContext;

    /// <summary>
    ///     Creates a streaming scrubber wired to the supplied detector
    ///     pipeline. The same detector is invoked once per (carry + line)
    ///     window; pipelines that combine rules + custom + dictionary +
    ///     optional NER are wrapped into one <see cref="IDetector"/> by the
    ///     caller (see <see cref="RunCommand"/>).
    /// </summary>
    /// <param name="detector">The detector pipeline.</param>
    /// <param name="baseContext">Per-run context shared across iterations (allow-list, source name, etc.).</param>
    public StreamingScrubber(IDetector detector, DetectionContext baseContext)
    {
        ArgumentNullException.ThrowIfNull(detector);
        _detector = detector;
        _baseContext = baseContext;
    }

    /// <summary>
    ///     Reads from <paramref name="reader"/> until EOF, emits scrubbed text
    ///     to <paramref name="writer"/>, and returns the per-type detection
    ///     counts. Reading is chunk-based so the underlying bytes (including
    ///     <c>\r\n</c> / <c>\n</c> sequences) are preserved verbatim outside
    ///     the detection spans.
    /// </summary>
    /// <param name="reader">The input reader.</param>
    /// <param name="writer">The destination writer.</param>
    /// <returns>Per-type detection counts.</returns>
    public IReadOnlyDictionary<DetectionType, int> Process(TextReader reader, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);

        Dictionary<DetectionType, int> counts = [];
        StringBuilder carry = new();
        char[] buffer = new char[ChunkSize];

        while (true)
        {
            int read = reader.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            string chunk = carry.Length == 0
                ? new string(buffer, 0, read)
                : string.Concat(carry.ToString(), new string(buffer, 0, read));
            carry.Clear();
            ProcessChunk(chunk, isLast: false, writer, counts, carry);
        }

        if (carry.Length > 0)
        {
            string finalChunk = carry.ToString();
            carry.Clear();
            ProcessChunk(finalChunk, isLast: true, writer, counts, carry);
        }

        return counts;
    }

    private void ProcessChunk(
        string chunk,
        bool isLast,
        TextWriter writer,
        Dictionary<DetectionType, int> counts,
        StringBuilder carryOut)
    {
        DetectionContext ctx = _baseContext with { Input = chunk };
        IEnumerable<Detection> raw = _detector.Detect(chunk.AsMemory(), ctx);

        // Order matches the in-memory path in RunCommand.ResolveDetections:
        // merge first so the longest/highest-confidence detection wins per
        // span, then run the allow-list against the merged survivors. Doing
        // the allow-list before the merge would let a non-allow-listed inner
        // detection (e.g. NER-detected name) win after the email span was
        // dropped, producing different output across the two paths.
        // Detections longer than the carry budget are discarded outright so
        // the carry never grows beyond a small multiple of CarryBufferSize:
        // M5 documents these as "missed cleanly" entities.
        List<Detection> merged = [.. DetectionMerger.Merge(raw)];
        List<Detection> detections = new(merged.Count);
        foreach (Detection detection in merged)
        {
            if (detection.Length > CarryBufferSize)
            {
                continue;
            }
            if (!ctx.ShouldDrop(detection))
            {
                detections.Add(detection);
            }
        }

        int commitEnd = isLast ? chunk.Length : Math.Max(0, chunk.Length - CarryBufferSize);
        if (!isLast)
        {
            foreach (Detection detection in detections)
            {
                if (detection.Start < commitEnd && detection.End > commitEnd)
                {
                    commitEnd = detection.Start;
                }
            }
            if (commitEnd < 0)
            {
                commitEnd = 0;
            }
        }

        int cursor = 0;
        foreach (Detection detection in detections)
        {
            if (detection.End > commitEnd)
            {
                break;
            }

            if (detection.Start < cursor)
            {
                continue;
            }

            if (detection.Start > cursor)
            {
                writer.Write(chunk.AsSpan(cursor, detection.Start - cursor));
            }

            writer.Write('[');
            writer.Write(detection.Type.ToTagName());
            writer.Write(']');
            cursor = detection.End;
            counts[detection.Type] = counts.GetValueOrDefault(detection.Type, 0) + 1;
        }

        if (cursor < commitEnd)
        {
            writer.Write(chunk.AsSpan(cursor, commitEnd - cursor));
        }

        if (commitEnd < chunk.Length)
        {
            carryOut.Append(chunk, commitEnd, chunk.Length - commitEnd);
        }
    }
}
