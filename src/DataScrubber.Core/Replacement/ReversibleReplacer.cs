namespace DataScrubber.Replacement;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DataScrubber.Detection;
using DataScrubber.Mapping;

/// <summary>
///     Replaces each detection span with a numbered token of the form
///     <c>[&lt;TYPE&gt;_NNN]</c>. Sequence numbers are allocated per
///     <see cref="DetectionType"/> and reset every run; identical original
///     substrings within a run share a token regardless of which detector
///     produced them (the first detection's type wins for the entry). The
///     emitted entries can be persisted into a mapping file so the
///     substitution can later be reversed by <see cref="Rehydrator"/>.
/// </summary>
public sealed class ReversibleReplacer : IReplacer
{
    /// <inheritdoc />
    public ReplacementResult Replace(string input, IReadOnlyList<Detection> detections, ReplacerOptions options)
    {
        ReversibleReplacementResult full = ReplaceWithMapping(input, detections, options);
        return new ReplacementResult(full.Output, full.Applied);
    }

    /// <summary>
    ///     Rewrites <paramref name="input"/> with reversible tokens and returns
    ///     the entries needed to populate a mapping file alongside the
    ///     rewritten output.
    /// </summary>
    /// <param name="input">The original input.</param>
    /// <param name="detections">The non-overlapping detections to replace.</param>
    /// <param name="options">Replacement options.</param>
    /// <returns>The rewrite result, including the mapping entries.</returns>
    public ReversibleReplacementResult ReplaceWithMapping(
        string input,
        IReadOnlyList<Detection> detections,
        ReplacerOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(detections);
        ArgumentNullException.ThrowIfNull(options);

        if (detections.Count == 0)
        {
            return new ReversibleReplacementResult(input, [], []);
        }

        // Reserve any token-shaped substring that already exists in the input
        // outside a detection span. The counter must skip these so an
        // allocated token cannot collide with literal user text and corrupt
        // the round trip on rehydrate.
        HashSet<string> reservedTokens = CollectReservedTokens(input, detections);

        Dictionary<DetectionType, int> counters = [];
        Dictionary<string, int> entryIndexByOriginal = new(StringComparer.Ordinal);
        List<MappingEntry> entries = [];

        StringBuilder builder = new(input.Length);
        int cursor = 0;
        List<Detection> applied = new(detections.Count);

        foreach (Detection detection in detections)
        {
            if (detection.Start < cursor || detection.Start + detection.Length > input.Length)
            {
                continue;
            }

            if (detection.Start > cursor)
            {
                builder.Append(input, cursor, detection.Start - cursor);
            }

            string original = input.Substring(detection.Start, detection.Length);

            if (entryIndexByOriginal.TryGetValue(original, out int index))
            {
                MappingEntry existing = entries[index];
                builder.Append(existing.Token);
                entries[index] = existing with { Occurrences = existing.Occurrences + 1 };
            }
            else
            {
                if (TokenFormat.ContainsToken(original))
                {
                    throw new TokenShapedOriginalException(original);
                }

                int next = counters.TryGetValue(detection.Type, out int current) ? current + 1 : 1;
                string token;
                while (true)
                {
                    token = string.Create(
                        CultureInfo.InvariantCulture,
                        $"[{detection.Type.ToTagName()}_{next:D3}]");
                    if (!reservedTokens.Contains(token))
                    {
                        break;
                    }

                    next++;
                }

                counters[detection.Type] = next;
                MappingEntry entry = new(token, original, detection.Type, 1);
                entryIndexByOriginal[original] = entries.Count;
                entries.Add(entry);
                builder.Append(token);
            }

            cursor = detection.Start + detection.Length;
            applied.Add(detection);
        }

        if (cursor < input.Length)
        {
            builder.Append(input, cursor, input.Length - cursor);
        }

        return new ReversibleReplacementResult(builder.ToString(), applied, entries);
    }

    private static HashSet<string> CollectReservedTokens(string input, IReadOnlyList<Detection> detections)
    {
        HashSet<string> reserved = new(StringComparer.Ordinal);
        foreach (Match match in TokenFormat.Regex().Matches(input))
        {
            int start = match.Index;
            int end = start + match.Length;
            if (!OverlapsAnyDetection(start, end, detections))
            {
                reserved.Add(match.Value);
            }
        }

        return reserved;
    }

    private static bool OverlapsAnyDetection(int start, int end, IReadOnlyList<Detection> detections)
    {
        foreach (Detection detection in detections)
        {
            if (detection.Start < end && start < detection.Start + detection.Length)
            {
                return true;
            }
        }

        return false;
    }
}
