namespace DataScrubber.Configuration;

using DataScrubber.Detection;

/// <summary>
///     The bundle returned by <see cref="ConfigLoader.Load"/>. Carries the
///     parsed configuration plus the runtime artefacts that depend on it
///     (compiled custom regexes, the dictionary detector, the allow-list
///     filter), the precomputed disabled-rule sets, and the path the config
///     was loaded from.
/// </summary>
public sealed class ResolvedScrubConfig
{
    private readonly IReadOnlySet<string> _disabledRuleIds;
    private readonly IReadOnlySet<DetectionType> _disabledTypes;

    /// <summary>Creates a resolved config bundle.</summary>
    /// <param name="config">The parsed configuration.</param>
    /// <param name="customRegexDetector">A detector wrapping the compiled custom rules.</param>
    /// <param name="dictionaryDetector">A detector wrapping the per-type dictionaries.</param>
    /// <param name="allowList">The allow-list filter.</param>
    /// <param name="sourcePath">The on-disk path the config was loaded from, or <c>null</c> when defaults were used.</param>
    public ResolvedScrubConfig(
        ScrubConfig config,
        CustomRegexDetector customRegexDetector,
        DictionaryDetector dictionaryDetector,
        AllowListFilter allowList,
        string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(customRegexDetector);
        ArgumentNullException.ThrowIfNull(dictionaryDetector);
        ArgumentNullException.ThrowIfNull(allowList);

        Config = config;
        CustomRegexDetector = customRegexDetector;
        DictionaryDetector = dictionaryDetector;
        AllowList = allowList;
        SourcePath = sourcePath;

        (_disabledRuleIds, _disabledTypes) = SplitDisabled(config.Rules.Disabled);
    }

    /// <summary>The parsed configuration.</summary>
    public ScrubConfig Config { get; }

    /// <summary>The compiled custom regex detector.</summary>
    public CustomRegexDetector CustomRegexDetector { get; }

    /// <summary>The per-type dictionary detector.</summary>
    public DictionaryDetector DictionaryDetector { get; }

    /// <summary>The allow-list filter.</summary>
    public AllowListFilter AllowList { get; }

    /// <summary>The on-disk path the config was loaded from, or <c>null</c>.</summary>
    public string? SourcePath { get; }

    /// <summary>
    ///     Returns <c>true</c> when <paramref name="detection"/> matches any
    ///     disabled rule ID or disabled detection type. Backed by precomputed
    ///     hash sets, so the check is <c>O(1)</c> per detection.
    /// </summary>
    /// <param name="detection">The candidate detection.</param>
    /// <returns><c>true</c> when the detection should be filtered before merging.</returns>
    public bool IsDisabled(Detection detection)
    {
        if (_disabledRuleIds.Count == 0 && _disabledTypes.Count == 0)
        {
            return false;
        }

        return _disabledRuleIds.Contains(detection.SourceRule) || _disabledTypes.Contains(detection.Type);
    }

    private static (IReadOnlySet<string> Ids, IReadOnlySet<DetectionType> Types) SplitDisabled(IReadOnlyList<string> disabled)
    {
        if (disabled.Count == 0)
        {
            return (_emptyStrings, _emptyTypes);
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<DetectionType> types = [];
        foreach (string entry in disabled)
        {
            if (Enum.TryParse(entry, ignoreCase: false, out DetectionType type))
            {
                types.Add(type);
            }
            else
            {
                ids.Add(entry);
            }
        }

        return (ids, types);
    }

    private static readonly IReadOnlySet<string> _emptyStrings = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<DetectionType> _emptyTypes = new HashSet<DetectionType>();
}
