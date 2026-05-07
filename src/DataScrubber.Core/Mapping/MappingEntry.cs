namespace DataScrubber.Mapping;

using DataScrubber.Detection;

/// <summary>
///     A single token-to-original substitution recorded by
///     <see cref="DataScrubber.Replacement.ReversibleReplacer"/>. Persisted
///     into a mapping file so reversible runs can be inverted by
///     <see cref="Rehydrator"/>.
/// </summary>
/// <param name="Token">The bracketed reversible token, e.g. <c>[PERSON_001]</c>.</param>
/// <param name="Original">The exact original substring the token replaced.</param>
/// <param name="Type">The detection type the token represents.</param>
/// <param name="Occurrences">The number of times the original appeared in the input.</param>
public sealed record MappingEntry(
    string Token,
    string Original,
    DetectionType Type,
    int Occurrences);
