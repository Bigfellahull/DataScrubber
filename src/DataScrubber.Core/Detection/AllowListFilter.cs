namespace DataScrubber.Detection;

using System.Text;

/// <summary>
///     Drops detections whose original substring is an exact, case-sensitive
///     match for an allow-list entry. Comparison is performed after
///     normalising both the substring and every entry to NFC so configs
///     written in either composed or decomposed form behave identically.
///     The filter is the post-merge step that wires up
///     <see cref="DetectionContext.ShouldDrop"/> for the in-process pipeline.
/// </summary>
public sealed class AllowListFilter
{
    private readonly HashSet<string> _entries;

    /// <summary>
    ///     Creates a filter from a sequence of allow-list entries. Empty or
    ///     <c>null</c> entries are silently ignored.
    /// </summary>
    /// <param name="entries">The allow-list entries from the config.</param>
    public AllowListFilter(IEnumerable<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = new HashSet<string>(StringComparer.Ordinal);
        foreach (string entry in entries)
        {
            if (string.IsNullOrEmpty(entry))
            {
                continue;
            }

            _entries.Add(entry.Normalize(NormalizationForm.FormC));
        }
    }

    /// <summary>An allow-list filter that never drops anything.</summary>
    public static AllowListFilter Empty { get; } = new([]);

    /// <summary>Whether the filter has any entries.</summary>
    public bool IsEmpty => _entries.Count == 0;

    /// <summary>
    ///     Returns <c>true</c> when the substring of <paramref name="input"/>
    ///     covered by <paramref name="detection"/> is an exact case-sensitive
    ///     match for an allow-list entry.
    /// </summary>
    /// <param name="input">The full input the detection was produced against.</param>
    /// <param name="detection">The candidate detection.</param>
    /// <returns><c>true</c> if the detection should be dropped.</returns>
    public bool ShouldDrop(string input, Detection detection)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (_entries.Count == 0)
        {
            return false;
        }

        if (detection.Start < 0 || detection.Length <= 0 || detection.End > input.Length)
        {
            return false;
        }

        string substring = input.Substring(detection.Start, detection.Length);
        if (!substring.IsNormalized(NormalizationForm.FormC))
        {
            substring = substring.Normalize(NormalizationForm.FormC);
        }

        return _entries.Contains(substring);
    }

    /// <summary>
    ///     Returns a new list containing only the detections that survive the
    ///     allow-list. Order is preserved.
    /// </summary>
    /// <param name="input">The full input the detections were produced against.</param>
    /// <param name="detections">The merged detections to filter.</param>
    /// <returns>The filtered detection list.</returns>
    public IReadOnlyList<Detection> Filter(string input, IReadOnlyList<Detection> detections)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(detections);

        if (_entries.Count == 0)
        {
            return detections;
        }

        List<Detection> kept = new(detections.Count);
        foreach (Detection detection in detections)
        {
            if (!ShouldDrop(input, detection))
            {
                kept.Add(detection);
            }
        }

        return kept;
    }
}
