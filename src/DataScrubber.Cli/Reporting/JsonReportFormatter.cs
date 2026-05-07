namespace DataScrubber.Cli.Reporting;

using System.Text.Json;
using DataScrubber.Detection;

/// <summary>
///     Renders a <see cref="Report"/> as the line-delimited JSON form specified
///     by M5: one <c>{"event":"file",…}</c> object per file, then a final
///     <c>{"event":"summary",…}</c> object. Output is written to a
///     caller-supplied <see cref="TextWriter"/> so the destination (stderr by
///     default) and quiet-mode suppression stay in CLI hands.
/// </summary>
public static class JsonReportFormatter
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    ///     Writes <paramref name="report"/> to <paramref name="writer"/> as one
    ///     JSON line per file followed by a single summary line.
    /// </summary>
    /// <param name="report">The report to render.</param>
    /// <param name="writer">The destination writer.</param>
    public static void Write(Report report, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(writer);

        foreach (FileReport file in report.Files)
        {
            FileEvent payload = new(
                Event: "file",
                Path: file.Path,
                Counts: ToStringKeyed(file.Counts),
                DurationMs: file.DurationMs);
            writer.WriteLine(JsonSerializer.Serialize(payload, _options));
        }

        SummaryEvent summary = new(
            Event: "summary",
            Files: report.Files.Count,
            TotalDetections: report.TotalDetections,
            DurationMs: report.DurationMs);
        writer.WriteLine(JsonSerializer.Serialize(summary, _options));
    }

    private static Dictionary<string, int> ToStringKeyed(IReadOnlyDictionary<DetectionType, int> counts)
    {
        Dictionary<string, int> result = [];
        foreach (DetectionType type in Enum.GetValues<DetectionType>())
        {
            result[type.ToString()] = counts.GetValueOrDefault(type, 0);
        }
        return result;
    }

    private sealed record FileEvent(
        string Event,
        string Path,
        Dictionary<string, int> Counts,
        long DurationMs);

    private sealed record SummaryEvent(
        string Event,
        int Files,
        int TotalDetections,
        long DurationMs);
}
