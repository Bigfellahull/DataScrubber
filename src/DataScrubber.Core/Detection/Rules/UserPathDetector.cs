using System.Text.RegularExpressions;

namespace DataScrubber.Detection.Rules;

/// <summary>
///     Detects username segments inside POSIX <c>/Users/&lt;name&gt;</c> /
///     <c>/home/&lt;name&gt;</c> paths and Windows
///     <c>&lt;drive&gt;:\Users\&lt;name&gt;</c> paths. The detection span
///     covers the username segment only, so the surrounding directory
///     structure remains in the output. Rule IDs: <c>userpath.posix</c>,
///     <c>userpath.windows</c>; confidence <c>1.0</c>.
/// </summary>
public sealed partial class UserPathDetector : IDetector
{
    private const string PosixRule = "userpath.posix";
    private const string WindowsRule = "userpath.windows";

    [GeneratedRegex(
        @"(?<=^|[\s,;:'""<>])/(?:Users|home)/([A-Za-z0-9._\-]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex PosixPattern();

    [GeneratedRegex(
        @"(?<=^|[\s,;:'""<>])[A-Za-z]:\\Users\\([^\\\r\n]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPattern();

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
    {
        string text = input.ToString();

        foreach (Match match in PosixPattern().Matches(text))
        {
            Group name = match.Groups[1];
            if (name.Success && name.Length > 0)
            {
                yield return new Detection(name.Index, name.Length, DetectionType.UserPath, 1.0, PosixRule);
            }
        }

        foreach (Match match in WindowsPattern().Matches(text))
        {
            Group name = match.Groups[1];
            if (name.Success && name.Length > 0)
            {
                yield return new Detection(name.Index, name.Length, DetectionType.UserPath, 1.0, WindowsRule);
            }
        }
    }
}
