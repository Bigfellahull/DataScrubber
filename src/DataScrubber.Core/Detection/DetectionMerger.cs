namespace DataScrubber.Detection;

/// <summary>
///     Resolves overlaps between detections produced by independent detectors.
///     The algorithm is greedy and deterministic: detections are sorted by
///     start position ascending and length descending, then a single pass
///     keeps the winner for each overlap group using the tie-break order
///     (longest span, then highest confidence, then lowest detector priority).
///     Adjacent detections of the same type are not merged.
/// </summary>
public static class DetectionMerger
{
    /// <summary>
    ///     Returns the resolved, non-overlapping list of detections in
    ///     left-to-right order. The input is not mutated; the result is a new
    ///     list whose elements come from <paramref name="detections"/>.
    /// </summary>
    /// <param name="detections">The unsorted detections from one or more detectors.</param>
    /// <returns>A new list of non-overlapping detections sorted by start index.</returns>
    public static IReadOnlyList<Detection> Merge(IEnumerable<Detection> detections)
    {
        ArgumentNullException.ThrowIfNull(detections);

        List<Detection> sorted = [.. detections];
        sorted.Sort(static (a, b) =>
        {
            int byStart = a.Start.CompareTo(b.Start);
            return byStart != 0 ? byStart : b.Length.CompareTo(a.Length);
        });

        List<Detection> kept = new(sorted.Count);
        foreach (Detection candidate in sorted)
        {
            if (candidate.Length <= 0)
            {
                continue;
            }

            if (kept.Count == 0)
            {
                kept.Add(candidate);
                continue;
            }

            Detection current = kept[^1];
            if (!current.OverlapsWith(candidate))
            {
                kept.Add(candidate);
                continue;
            }

            if (Prefer(candidate, current))
            {
                kept[^1] = candidate;
            }
        }

        return kept;
    }

    private static bool Prefer(Detection candidate, Detection current)
    {
        if (candidate.Length != current.Length)
        {
            return candidate.Length > current.Length;
        }

        if (candidate.Confidence != current.Confidence)
        {
            return candidate.Confidence > current.Confidence;
        }

        return DetectorPriority.For(candidate.Type) < DetectorPriority.For(current.Type);
    }
}
