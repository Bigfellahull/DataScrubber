namespace DataScrubber.Replacement;

using DataScrubber.Detection;
using DataScrubber.Mapping;

/// <summary>
///     The full result of a <see cref="ReversibleReplacer"/> run: the
///     rewritten output, the detections actually applied, and the entries
///     that should be persisted into a mapping file so the substitution can
///     be reversed.
/// </summary>
/// <param name="Output">The rewritten string with reversible tokens.</param>
/// <param name="Applied">The detections actually applied, in left-to-right order.</param>
/// <param name="Entries">The mapping entries in token-allocation order.</param>
public readonly record struct ReversibleReplacementResult(
    string Output,
    IReadOnlyList<Detection> Applied,
    IReadOnlyList<MappingEntry> Entries);
