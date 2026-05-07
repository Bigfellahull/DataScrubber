namespace DataScrubber.Mapping;

/// <summary>
///     Reverses a reversible scrub by substituting every <c>[TYPE_NNN]</c>
///     token in the input with the original entity recorded in the mapping
///     file. Tokens not present in the mapping are left in place and reported
///     so the caller can warn the user.
/// </summary>
public sealed class Rehydrator
{
    /// <summary>
    ///     Substitutes tokens in <paramref name="input"/> with their originals
    ///     from <paramref name="mapping"/>. A single regex pass is used; for
    ///     mappings produced by <see cref="DataScrubber.Replacement.ReversibleReplacer"/>
    ///     (which refuses token-shaped originals) a second pass over the
    ///     output produces no further changes, so rehydration is idempotent.
    /// </summary>
    /// <param name="input">The text containing reversible tokens (e.g. an LLM response).</param>
    /// <param name="mapping">The mapping that resolves tokens to originals.</param>
    /// <returns>The rehydrated output and the set of token strings that did not resolve.</returns>
    public RehydrationResult Rehydrate(string input, MappingFile mapping)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(mapping);

        Dictionary<string, string> tokenToOriginal = new(StringComparer.Ordinal);
        foreach (MappingEntry entry in mapping.Entries)
        {
            tokenToOriginal[entry.Token] = entry.Original;
        }

        SortedSet<string> unknownTokens = new(StringComparer.Ordinal);

        string output = TokenFormat.Regex().Replace(input, match =>
        {
            string token = match.Value;
            if (tokenToOriginal.TryGetValue(token, out string? original))
            {
                return original;
            }

            unknownTokens.Add(token);
            return token;
        });

        return new RehydrationResult(output, unknownTokens);
    }
}

/// <summary>
///     The output of <see cref="Rehydrator.Rehydrate"/>.
/// </summary>
/// <param name="Output">The rehydrated text.</param>
/// <param name="UnknownTokens">Tokens encountered in the input that the mapping did not resolve.</param>
public readonly record struct RehydrationResult(
    string Output,
    IReadOnlyCollection<string> UnknownTokens);
