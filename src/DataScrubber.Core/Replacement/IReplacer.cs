namespace DataScrubber.Replacement;

using DataScrubber.Detection;

/// <summary>
///     Rewrites an input string by substituting each detection span with a
///     placeholder. Implementations differ in placeholder format and in
///     whether they preserve the mapping back to originals.
/// </summary>
public interface IReplacer
{
    /// <summary>
    ///     Rewrites <paramref name="input"/> by replacing the supplied
    ///     <paramref name="detections"/> with type-tagged placeholders.
    ///     Detections must already be merged (non-overlapping); callers
    ///     normally route them through <see cref="DetectionMerger.Merge"/>
    ///     first.
    /// </summary>
    /// <param name="input">The original input.</param>
    /// <param name="detections">The non-overlapping detections to replace.</param>
    /// <param name="options">Replacement options.</param>
    /// <returns>The rewrite result.</returns>
    ReplacementResult Replace(string input, IReadOnlyList<Detection> detections, ReplacerOptions options);
}
