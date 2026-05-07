namespace DataScrubber.Detection;

using System.Text.RegularExpressions;
using DataScrubber.Configuration;

/// <summary>
///     Runs the user-supplied <see cref="CustomRule"/> set against the input
///     and emits detections of the rule's declared <see cref="DetectionType"/>.
///     Patterns are compiled with a 200 ms guard at construction time so
///     pathological backtracking patterns are rejected loudly instead of
///     wedging the process. Runtime backtracking on a successfully compiled
///     pattern is not bounded.
/// </summary>
public sealed class CustomRegexDetector : IDetector
{
    /// <summary>The compile-time guard applied to every <see cref="CustomRule.Pattern"/>.</summary>
    public static TimeSpan CompileTimeout { get; } = TimeSpan.FromMilliseconds(200);

    private readonly IReadOnlyList<CompiledRule> _rules;

    private CustomRegexDetector(IReadOnlyList<CompiledRule> rules)
    {
        _rules = rules;
    }

    /// <summary>An empty detector that yields nothing. Used when the config defines no custom rules.</summary>
    public static CustomRegexDetector Empty { get; } = new([]);

    /// <summary>
    ///     Compiles every supplied rule and returns a ready-to-run detector.
    ///     Rule indexing is preserved so callers can produce JSON-path style
    ///     diagnostics.
    /// </summary>
    /// <param name="rules">The rules from the loaded config.</param>
    /// <returns>A detector wrapping the compiled rules.</returns>
    /// <exception cref="CustomRuleCompileException">Raised when any rule fails the compile guard or produces a malformed regex.</exception>
    public static CustomRegexDetector Compile(IReadOnlyList<CustomRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.Count == 0)
        {
            return Empty;
        }

        List<CompiledRule> compiled = new(rules.Count);
        for (int i = 0; i < rules.Count; i++)
        {
            CustomRule rule = rules[i];
            try
            {
                Regex regex = CompileWithTimeout(rule.Pattern);
                compiled.Add(new CompiledRule(rule.Id, rule.Type, rule.Confidence, regex));
            }
            catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
            {
                throw new CustomRuleCompileException(i, rule.Id, ex.Message, ex);
            }
        }

        return new CustomRegexDetector(compiled);
    }

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
    {
        if (_rules.Count == 0 || input.IsEmpty)
        {
            yield break;
        }

        string text = input.ToString();
        foreach (CompiledRule rule in _rules)
        {
            foreach (Match match in rule.Pattern.Matches(text))
            {
                if (match.Length == 0)
                {
                    continue;
                }

                yield return new Detection(match.Index, match.Length, rule.Type, rule.Confidence, rule.Id);
            }
        }
    }

    private static Regex CompileWithTimeout(string pattern)
    {
        Task<Regex> task = Task.Run(() => new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant));

        bool finished;
        try
        {
            finished = task.Wait(CompileTimeout);
        }
        catch (AggregateException agg) when (agg.InnerException is not null)
        {
            // Task.Wait wraps the underlying parse failure; the caller wants
            // the real exception so it can carry the regex parser's pointer.
            throw agg.InnerException;
        }

        if (!finished)
        {
            // Worker keeps running; process exit reaps it.
            throw new RegexMatchTimeoutException(
                $"regex compilation exceeded {CompileTimeout.TotalMilliseconds:F0} ms");
        }

        return task.Result;
    }

    private sealed record CompiledRule(string Id, DetectionType Type, double Confidence, Regex Pattern);
}

/// <summary>
///     Thrown when a <see cref="CustomRule"/> cannot be compiled. Carries the
///     index of the offending rule so the loader can produce a JSON-path
///     style error pointing at <c>$.rules.custom[i].pattern</c>.
/// </summary>
public sealed class CustomRuleCompileException : Exception
{
    /// <summary>Creates a new <see cref="CustomRuleCompileException"/>.</summary>
    /// <param name="ruleIndex">Zero-based index of the failing rule in the config.</param>
    /// <param name="ruleId">The rule's own identifier.</param>
    /// <param name="message">A human-readable reason.</param>
    /// <param name="inner">The underlying compilation failure.</param>
    public CustomRuleCompileException(int ruleIndex, string ruleId, string message, Exception? inner = null)
        : base(message, inner)
    {
        RuleIndex = ruleIndex;
        RuleId = ruleId;
    }

    /// <summary>Zero-based index of the failing rule in the config.</summary>
    public int RuleIndex { get; }

    /// <summary>The identifier of the failing rule.</summary>
    public string RuleId { get; }
}
