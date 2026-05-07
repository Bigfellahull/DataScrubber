namespace DataScrubber.Replacement;

using System.Text;
using DataScrubber.Detection;

/// <summary>
///     Replaces each detection span with the upper snake-case type tag wrapped
///     in square brackets (e.g. <c>[EMAIL]</c>). Bytes outside detection spans
///     are emitted verbatim, so encoding, whitespace, and line endings are
///     preserved exactly. The mapping is one-way: there is no record kept of
///     the originals.
/// </summary>
public sealed class OneWayReplacer : IReplacer
{
    /// <inheritdoc />
    public ReplacementResult Replace(string input, IReadOnlyList<Detection> detections, ReplacerOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(detections);
        ArgumentNullException.ThrowIfNull(options);

        if (detections.Count == 0)
        {
            return new ReplacementResult(input, []);
        }

        StringBuilder builder = new(input.Length);
        int cursor = 0;
        List<Detection> applied = new(detections.Count);

        foreach (Detection detection in detections)
        {
            if (detection.Start < cursor || detection.Start + detection.Length > input.Length)
            {
                continue;
            }

            if (detection.Start > cursor)
            {
                builder.Append(input, cursor, detection.Start - cursor);
            }

            builder.Append('[').Append(detection.Type.ToTagName()).Append(']');
            cursor = detection.Start + detection.Length;
            applied.Add(detection);
        }

        if (cursor < input.Length)
        {
            builder.Append(input, cursor, input.Length - cursor);
        }

        return new ReplacementResult(builder.ToString(), applied);
    }
}
