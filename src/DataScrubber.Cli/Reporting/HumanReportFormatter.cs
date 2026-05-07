namespace DataScrubber.Cli.Reporting;

using System.Globalization;
using DataScrubber.Detection;

/// <summary>
///     Renders a <see cref="Report"/> as the human-readable, aligned-column
///     summary specified by D7. Output is written to a caller-supplied
///     <see cref="TextWriter"/> (typically stderr) so callers stay in control
///     of buffering and quiet-mode suppression.
/// </summary>
public static class HumanReportFormatter
{
    private const string Header = "DataScrubber report";
    private const string TotalLabel = "Total detections";
    private const string DurationLabel = "Duration (ms)";

    /// <summary>
    ///     Writes <paramref name="report"/> to <paramref name="writer"/> in the
    ///     human-readable form. Every <see cref="DetectionType"/> appears in
    ///     declaration order — even when its count is zero — so the user can
    ///     distinguish "detector ran and found none" from "detector did not
    ///     run". A total row and the wall-clock duration follow.
    /// </summary>
    /// <param name="report">The report to render.</param>
    /// <param name="writer">The destination writer.</param>
    public static void Write(Report report, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(writer);

        DetectionType[] orderedTypes = Enum.GetValues<DetectionType>();
        int labelWidth = ComputeLabelWidth(orderedTypes);

        writer.WriteLine(Header);
        foreach (DetectionType type in orderedTypes)
        {
            int count = report.Totals.GetValueOrDefault(type, 0);
            writer.WriteLine($"  {type.ToString().PadRight(labelWidth)} {count.ToString(CultureInfo.InvariantCulture).PadLeft(6)}");
        }
        writer.WriteLine($"  {TotalLabel.PadRight(labelWidth)} {report.TotalDetections.ToString(CultureInfo.InvariantCulture).PadLeft(6)}");
        writer.WriteLine($"  {DurationLabel.PadRight(labelWidth)} {report.DurationMs.ToString(CultureInfo.InvariantCulture).PadLeft(6)}");
    }

    private static int ComputeLabelWidth(IReadOnlyList<DetectionType> types)
    {
        int width = Math.Max(TotalLabel.Length, DurationLabel.Length);
        foreach (DetectionType type in types)
        {
            width = Math.Max(width, type.ToString().Length);
        }
        return width;
    }
}
