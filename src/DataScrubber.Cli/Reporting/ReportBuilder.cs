namespace DataScrubber.Cli.Reporting;

using System.Diagnostics;
using DataScrubber.Detection;

/// <summary>
///     Single-threaded accumulator used by <see cref="RunCommand"/> to track
///     per-file detection counts during a run. The builder owns its own
///     <see cref="Stopwatch"/> so the run-level <see cref="Report.DurationMs"/>
///     captures everything from CLI start through report emission.
/// </summary>
public sealed class ReportBuilder
{
    private readonly Stopwatch _runTimer = Stopwatch.StartNew();
    private readonly List<FileReport> _files = [];
    private readonly Dictionary<DetectionType, int> _totals = [];

    /// <summary>
    ///     Records a single file's contribution. <paramref name="counts"/> is
    ///     copied (not aliased) so the caller's dictionary remains free to be
    ///     reused or mutated.
    /// </summary>
    /// <param name="path">The source path (or <c>-</c> for stdin).</param>
    /// <param name="counts">Per-type detection counts for this file.</param>
    /// <param name="durationMs">Wall-clock duration spent on this file, in milliseconds.</param>
    public void AddFile(string path, IReadOnlyDictionary<DetectionType, int> counts, long durationMs)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(counts);

        Dictionary<DetectionType, int> snapshot = new(counts.Count);
        foreach ((DetectionType type, int count) in counts)
        {
            snapshot[type] = count;
            _totals[type] = _totals.GetValueOrDefault(type, 0) + count;
        }

        _files.Add(new FileReport(path, snapshot, durationMs));
    }

    /// <summary>
    ///     Returns the number of files added so far. Used by the JSON summary
    ///     payload.
    /// </summary>
    public int FileCount => _files.Count;

    /// <summary>
    ///     Materialises the accumulated report. Stops the internal stopwatch on
    ///     first invocation; subsequent calls return the same duration.
    /// </summary>
    /// <returns>The materialised report.</returns>
    public Report Build()
    {
        if (_runTimer.IsRunning)
        {
            _runTimer.Stop();
        }

        return new Report(_files.ToArray(), new Dictionary<DetectionType, int>(_totals), _runTimer.ElapsedMilliseconds);
    }
}
