using System.Text.RegularExpressions;

namespace DataScrubber.Detection.Rules;

/// <summary>
///     API-key / token detector. Aggregates the six sub-rules
///     <see cref="AwsKeyDetector"/>, <see cref="GitHubKeyDetector"/>,
///     <see cref="StripeKeyDetector"/>, <see cref="SlackKeyDetector"/>,
///     <see cref="JwtKeyDetector"/>, and <see cref="EntropyKeyDetector"/>.
///     Each sub-rule is its own <see cref="IDetector"/> so a future
///     configuration layer can disable or tune them individually.
/// </summary>
public sealed class ApiKeyDetector : IDetector
{
    private readonly IReadOnlyList<IDetector> _subRules;

    /// <summary>
    ///     Constructs the default aggregator that runs every shipped sub-rule.
    /// </summary>
    public ApiKeyDetector() : this(
    [
        new AwsKeyDetector(),
        new AwsSecretKeyDetector(),
        new GitHubKeyDetector(),
        new StripeKeyDetector(),
        new SlackKeyDetector(),
        new JwtKeyDetector(),
        new EntropyKeyDetector(),
    ])
    {
    }

    /// <summary>
    ///     Constructs an aggregator over a custom set of API-key sub-rules.
    /// </summary>
    /// <param name="subRules">The sub-rules to aggregate.</param>
    public ApiKeyDetector(IEnumerable<IDetector> subRules)
    {
        ArgumentNullException.ThrowIfNull(subRules);
        _subRules = [.. subRules];
    }

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
    {
        foreach (IDetector subRule in _subRules)
        {
            foreach (Detection detection in subRule.Detect(input, ctx))
            {
                yield return detection;
            }
        }
    }
}

/// <summary>
///     Detects AWS access keys (<c>AKIA</c> prefix). Rule ID
///     <c>apikey.aws</c>; confidence <c>1.0</c>.
/// </summary>
public sealed partial class AwsKeyDetector : IDetector
{
    private const string RuleId = "apikey.aws";

    [GeneratedRegex(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
        => MatchEnumerator.Run(input.ToString(), Pattern(), DetectionType.ApiKey, RuleId);
}

/// <summary>
///     Detects AWS secret-access keys: a 40-character token drawn from the
///     base64 alphabet (<c>A–Z</c>, <c>a–z</c>, <c>0–9</c>, <c>/</c>, <c>+</c>,
///     <c>=</c>) sitting within thirty characters of an AWS- or secret-related
///     keyword. Rule ID <c>apikey.aws_secret</c>; confidence <c>1.0</c>.
/// </summary>
public sealed partial class AwsSecretKeyDetector : IDetector
{
    private const string RuleId = "apikey.aws_secret";
    private const int KeywordWindow = 30;

    private static readonly string[] _keywords =
    [
        "aws_secret_access_key",
        "aws_access_key_id",
        "aws_secret",
        "aws.secret",
        "secret_access_key",
        "secretaccesskey",
        "aws",
    ];

    [GeneratedRegex(
        @"(?<![A-Za-z0-9/+=])[A-Za-z0-9/+=]{40}(?![A-Za-z0-9/+=])",
        RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
    {
        string text = input.ToString();
        foreach (Match match in TokenPattern().Matches(text))
        {
            if (HasNearbyKeyword(text, match.Index))
            {
                yield return new Detection(match.Index, match.Length, DetectionType.ApiKey, 1.0, RuleId);
            }
        }
    }

    private static bool HasNearbyKeyword(string text, int tokenStart)
    {
        int beforeStart = Math.Max(0, tokenStart - KeywordWindow);
        ReadOnlySpan<char> before = text.AsSpan(beforeStart, tokenStart - beforeStart);

        foreach (string keyword in _keywords)
        {
            if (before.LastIndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
///     Detects GitHub personal-access / app tokens (<c>ghp_</c>, <c>gho_</c>,
///     etc.). Rule ID <c>apikey.github</c>; confidence <c>1.0</c>.
/// </summary>
public sealed partial class GitHubKeyDetector : IDetector
{
    private const string RuleId = "apikey.github";

    [GeneratedRegex(@"\bgh[pousr]_[A-Za-z0-9]{36,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
        => MatchEnumerator.Run(input.ToString(), Pattern(), DetectionType.ApiKey, RuleId);
}

/// <summary>
///     Detects Stripe live and test keys (<c>sk_live_…</c>, <c>pk_live_…</c>,
///     <c>rk_live_…</c>, plus their <c>_test_</c> counterparts). Rule ID
///     <c>apikey.stripe</c>; confidence <c>1.0</c>.
/// </summary>
public sealed partial class StripeKeyDetector : IDetector
{
    private const string RuleId = "apikey.stripe";

    [GeneratedRegex(@"\b(?:sk|pk|rk)_(?:live|test)_[A-Za-z0-9]{16,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
        => MatchEnumerator.Run(input.ToString(), Pattern(), DetectionType.ApiKey, RuleId);
}

/// <summary>
///     Detects Slack tokens (<c>xoxb-</c>, <c>xoxa-</c>, etc.). Rule ID
///     <c>apikey.slack</c>; confidence <c>1.0</c>.
/// </summary>
public sealed partial class SlackKeyDetector : IDetector
{
    private const string RuleId = "apikey.slack";

    [GeneratedRegex(@"\bxox[abprs]-[A-Za-z0-9-]{10,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
        => MatchEnumerator.Run(input.ToString(), Pattern(), DetectionType.ApiKey, RuleId);
}

/// <summary>
///     Detects three-segment JSON Web Tokens (<c>eyJ…</c>). Rule ID
///     <c>apikey.jwt</c>; confidence <c>1.0</c>.
/// </summary>
public sealed partial class JwtKeyDetector : IDetector
{
    private const string RuleId = "apikey.jwt";

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_\-]+\.eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\b", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
        => MatchEnumerator.Run(input.ToString(), Pattern(), DetectionType.ApiKey, RuleId);
}

/// <summary>
///     Detects high-entropy tokens (length ≥ 32, Shannon entropy &gt; 4.5)
///     that sit within three characters of a sensitive keyword
///     (<c>key</c>, <c>token</c>, <c>secret</c>, <c>bearer</c>,
///     <c>authorization</c>, case-insensitive). Rule ID <c>apikey.entropy</c>;
///     confidence <c>0.7</c>.
/// </summary>
public sealed partial class EntropyKeyDetector : IDetector
{
    private const string RuleId = "apikey.entropy";
    private const double EntropyThreshold = 4.5;
    private const int KeywordWindow = 3;

    private static readonly string[] _keywords =
    [
        "key", "token", "secret", "bearer", "authorization",
    ];

    [GeneratedRegex(@"\b[A-Za-z0-9_\-]{32,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    /// <inheritdoc />
    public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
    {
        string text = input.ToString();
        foreach (Match match in TokenPattern().Matches(text))
        {
            if (!HasNearbyKeyword(text, match.Index, match.Length))
            {
                continue;
            }

            if (ShannonEntropy(match.ValueSpan) <= EntropyThreshold)
            {
                continue;
            }

            yield return new Detection(match.Index, match.Length, DetectionType.ApiKey, 0.7, RuleId);
        }
    }

    private static bool HasNearbyKeyword(string text, int tokenStart, int tokenLength)
    {
        int beforeStart = Math.Max(0, tokenStart - 32);
        int afterStart = tokenStart + tokenLength;
        int afterEnd = Math.Min(text.Length, afterStart + 32);

        ReadOnlySpan<char> before = text.AsSpan(beforeStart, tokenStart - beforeStart);
        ReadOnlySpan<char> after = text.AsSpan(afterStart, afterEnd - afterStart);

        foreach (string keyword in _keywords)
        {
            int idxBefore = before.LastIndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idxBefore >= 0 && before.Length - (idxBefore + keyword.Length) <= KeywordWindow)
            {
                return true;
            }

            int idxAfter = after.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idxAfter is >= 0 and <= KeywordWindow)
            {
                return true;
            }
        }

        return false;
    }

    private static double ShannonEntropy(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
        {
            return 0;
        }

        Span<int> counts = stackalloc int[128];
        foreach (char c in span)
        {
            if (c < counts.Length)
            {
                counts[c]++;
            }
        }

        double entropy = 0;
        double length = span.Length;
        foreach (int count in counts)
        {
            if (count == 0)
            {
                continue;
            }

            double probability = count / length;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }
}

internal static class MatchEnumerator
{
    public static IEnumerable<Detection> Run(string text, Regex pattern, DetectionType type, string ruleId)
    {
        foreach (Match match in pattern.Matches(text))
        {
            yield return new Detection(match.Index, match.Length, type, 1.0, ruleId);
        }
    }
}
