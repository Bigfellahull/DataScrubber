namespace DataScrubber.Cli.Reporting;

using DataScrubber.Detection;

/// <summary>
///     A per-run summary of scrubbed files and the detection counts each one
///     contributed. Produced by <see cref="ReportBuilder"/> and rendered to
///     stderr by <see cref="HumanReportFormatter"/> or
///     <see cref="JsonReportFormatter"/>.
/// </summary>
/// <param name="Files">Per-file entries in the order they were processed.</param>
/// <param name="Totals">Sum of per-type counts across every file.</param>
/// <param name="DurationMs">Total wall-clock duration of the run, in milliseconds.</param>
public sealed record Report(
    IReadOnlyList<FileReport> Files,
    IReadOnlyDictionary<DetectionType, int> Totals,
    long DurationMs)
{
    /// <summary>Total number of detections across every file in the run.</summary>
    public int TotalDetections => Totals.Values.Sum();
}

/// <summary>
///     One file's contribution to a <see cref="Report"/>. The file path is the
///     source path passed to the CLI; for stdin runs the path is the literal
///     string <c>-</c>.
/// </summary>
/// <param name="Path">The source path the file was read from.</param>
/// <param name="Counts">Per-type detection counts for this file.</param>
/// <param name="DurationMs">Wall-clock duration spent on this file, in milliseconds.</param>
public sealed record FileReport(
    string Path,
    IReadOnlyDictionary<DetectionType, int> Counts,
    long DurationMs);
