namespace DataScrubber.Mapping;

using System.Text.RegularExpressions;

/// <summary>
///     The reversible-token format shared by the producer
///     (<see cref="DataScrubber.Replacement.ReversibleReplacer"/>) and the
///     consumer (<see cref="Rehydrator"/>). Centralised so the two stay
///     symmetric and to make the invariant testable in one place.
/// </summary>
internal static partial class TokenFormat
{
    // The spec gives `\[(?<type>[A-Z_]+)_(?<num>\d{3,})\]`, but IPV4 and IPV6
    // tag names contain digits. Widening the type class to [A-Z0-9_] is the
    // smallest fix that keeps the rest of the contract intact; backtracking
    // still binds the trailing `_NNN` to the last `_` before three or more
    // digits.
    [GeneratedRegex(@"\[(?<type>[A-Z0-9_]+)_(?<num>\d{3,})\]", RegexOptions.CultureInvariant)]
    public static partial Regex Regex();

    /// <summary>
    ///     Returns <c>true</c> when <paramref name="value"/> contains any
    ///     substring that matches the reversible-token shape. Used to refuse
    ///     mappings whose originals would re-trigger the rehydrator regex on
    ///     a second pass and therefore violate the idempotency contract.
    /// </summary>
    /// <param name="value">The candidate string.</param>
    /// <returns><c>true</c> when the value contains a token-shaped substring.</returns>
    public static bool ContainsToken(string value) => Regex().IsMatch(value);
}
