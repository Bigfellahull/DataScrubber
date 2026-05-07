namespace DataScrubber.Cli;

using System.IO.Enumeration;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
///     Walks a directory tree and yields the files matching one of the
///     <c>--include</c> globs and none of the <c>--exclude</c> globs. The
///     walker is built around <see cref="FileSystemEnumerable{TResult}"/> so
///     enumeration stays single-threaded and allocation-light. Symbolic links
///     are not followed.
/// </summary>
public static class DirectoryWalker
{
    /// <summary>The default include patterns when <c>--include</c> is omitted.</summary>
    public static IReadOnlyList<string> DefaultIncludes { get; } = ["**/*.txt", "**/*.log", "**/*.md"];

    /// <summary>
    ///     Result of walking one directory: the absolute path the walk was
    ///     rooted at and the matched files in sorted order.
    /// </summary>
    /// <param name="Root">The absolute path the walk was rooted at.</param>
    /// <param name="Files">The matched files in deterministic (ordinal) order.</param>
    public sealed record WalkResult(string Root, IReadOnlyList<MatchedFile> Files);

    /// <summary>
    ///     A matched file plus its path relative to the walk root.
    /// </summary>
    /// <param name="AbsolutePath">The absolute path of the matched file.</param>
    /// <param name="RelativePath">The path of the matched file relative to the walk root, using forward slashes.</param>
    public sealed record MatchedFile(string AbsolutePath, string RelativePath);

    /// <summary>
    ///     Enumerates files under <paramref name="root"/> that match the
    ///     supplied include/exclude globs. When <paramref name="includes"/> is
    ///     <c>null</c> or empty, <see cref="DefaultIncludes"/> is used.
    /// </summary>
    /// <param name="root">The directory to walk.</param>
    /// <param name="includes">Comma-separated or list-form include globs.</param>
    /// <param name="excludes">Exclude globs evaluated after includes.</param>
    /// <param name="recursive">When <c>true</c>, descend into subdirectories.</param>
    /// <returns>The walk result with matched files sorted ordinally by relative path.</returns>
    public static WalkResult Walk(
        string root,
        IReadOnlyList<string>? includes,
        IReadOnlyList<string>? excludes,
        bool recursive)
    {
        ArgumentNullException.ThrowIfNull(root);
        string fullRoot = Path.GetFullPath(root);

        IReadOnlyList<string> effectiveIncludes = (includes is null || includes.Count == 0)
            ? DefaultIncludes
            : includes;
        IReadOnlyList<string> effectiveExcludes = excludes ?? [];

        Regex[] includeRegexes = [.. effectiveIncludes.Select(GlobToRegex)];
        Regex[] excludeRegexes = [.. effectiveExcludes.Select(GlobToRegex)];

        EnumerationOptions enumerationOptions = new()
        {
            RecurseSubdirectories = recursive,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            MatchType = MatchType.Simple,
        };

        FileSystemEnumerable<string> enumerable = new(
            fullRoot,
            (ref FileSystemEntry entry) => entry.ToFullPath(),
            enumerationOptions)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory,
        };

        List<MatchedFile> matched = [];
        foreach (string absolute in enumerable)
        {
            string relative = ToForwardSlash(Path.GetRelativePath(fullRoot, absolute));
            if (!includeRegexes.Any(r => r.IsMatch(relative)))
            {
                continue;
            }

            if (excludeRegexes.Any(r => r.IsMatch(relative)))
            {
                continue;
            }

            matched.Add(new MatchedFile(absolute, relative));
        }

        matched.Sort(static (a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));
        return new WalkResult(fullRoot, matched);
    }

    /// <summary>
    ///     Returns <c>true</c> when <paramref name="output"/> is the same path
    ///     as <paramref name="input"/> or one resolves to a parent of the
    ///     other. Used to reject directory-mode runs whose output would
    ///     overwrite the input tree.
    /// </summary>
    /// <param name="input">The input directory path.</param>
    /// <param name="output">The proposed output directory path.</param>
    /// <returns><c>true</c> when the paths overlap.</returns>
    public static bool PathsOverlap(string input, string output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        string fullInput = NormaliseDirectory(Path.GetFullPath(input));
        string fullOutput = NormaliseDirectory(Path.GetFullPath(output));
        StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(fullInput, fullOutput, comparison)
            || fullInput.StartsWith(fullOutput, comparison)
            || fullOutput.StartsWith(fullInput, comparison);
    }

    private static string NormaliseDirectory(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static string ToForwardSlash(string path)
        => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    /// <summary>
    ///     Converts a glob pattern to an equivalent regular expression. The
    ///     supported tokens are <c>**</c> (recursive directory match),
    ///     <c>*</c> (single-segment wildcard), <c>?</c> (single character) and
    ///     literal characters; <c>/</c> and <c>\</c> both serve as separators
    ///     so Windows paths normalised to forward slashes still match.
    /// </summary>
    /// <param name="pattern">The glob pattern.</param>
    /// <returns>An anchored, case-insensitive <see cref="Regex"/>.</returns>
    public static Regex GlobToRegex(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        StringBuilder sb = new();
        sb.Append('^');

        int i = 0;
        while (i < pattern.Length)
        {
            char c = pattern[i];
            if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                sb.Append(".*");
                i += 2;
                if (i < pattern.Length && (pattern[i] == '/' || pattern[i] == '\\'))
                {
                    i++;
                }
            }
            else if (c == '*')
            {
                sb.Append("[^/\\\\]*");
                i++;
            }
            else if (c == '?')
            {
                sb.Append("[^/\\\\]");
                i++;
            }
            else if (c == '/' || c == '\\')
            {
                sb.Append("[/\\\\]");
                i++;
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
                i++;
            }
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
