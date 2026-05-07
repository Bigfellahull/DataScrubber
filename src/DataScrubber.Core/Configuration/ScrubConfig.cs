namespace DataScrubber.Configuration;

using System.Text.Json;
using System.Text.Json.Serialization;
using DataScrubber.Detection;

/// <summary>
///     Parsed representation of the user configuration file. Loaded once per
///     CLI invocation and treated as immutable thereafter. The schema lives
///     here and is the single source of truth — sample configs in
///     <c>samples/scrub.config.json</c> round-trip through this type.
/// </summary>
public sealed record ScrubConfig
{
    /// <summary>The schema version this milestone produces and accepts.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    ///     Schema version pin. Required; must equal
    ///     <see cref="CurrentSchemaVersion"/>.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public required int SchemaVersion { get; init; }

    /// <summary>The <c>rules</c> section. Defaults to <see cref="RulesConfig.Empty"/>.</summary>
    [JsonPropertyName("rules")]
    public RulesConfig Rules { get; init; } = RulesConfig.Empty;

    /// <summary>
    ///     Per-type dictionaries used by <c>DictionaryDetector</c>. Keys are
    ///     <see cref="DetectionType"/> names; values are the literal phrases
    ///     to match.
    /// </summary>
    [JsonPropertyName("dictionaries")]
    public IReadOnlyDictionary<DetectionType, IReadOnlyList<string>> Dictionaries { get; init; }
        = new Dictionary<DetectionType, IReadOnlyList<string>>();

    /// <summary>
    ///     Exact case-sensitive strings that, when an entire detection's
    ///     original text matches one of them, drop the detection before
    ///     replacement.
    /// </summary>
    [JsonPropertyName("allowList")]
    public IReadOnlyList<string> AllowList { get; init; } = [];

    /// <summary>The <c>ner</c> section. Defaults to <see cref="NerConfig.Empty"/>.</summary>
    [JsonPropertyName("ner")]
    public NerConfig Ner { get; init; } = NerConfig.Empty;

    /// <summary>The built-in defaults applied when no config file is found.</summary>
    public static ScrubConfig Defaults { get; } = new() { SchemaVersion = CurrentSchemaVersion };

    /// <summary>
    ///     Parses and validates a JSON configuration document. The strict
    ///     <see cref="JsonUnmappedMemberHandling.Disallow"/> policy rejects any
    ///     unknown key; further semantic checks ensure the schema version is
    ///     supported and threshold values lie in <c>(0.0, 1.0]</c>.
    /// </summary>
    /// <param name="json">The configuration document.</param>
    /// <returns>The validated configuration.</returns>
    /// <exception cref="ScrubConfigException">Raised on any parse or validation failure.</exception>
    public static ScrubConfig Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        ScrubConfig parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ScrubConfig>(json, JsonOptions)
                ?? throw new ScrubConfigException("$", "configuration document was JSON null");
        }
        catch (JsonException ex)
        {
            throw new ScrubConfigException(NormalisePath(ex.Path), ex.Message.TrimEnd('.'));
        }

        return Validate(parsed);
    }

    private static ScrubConfig Validate(ScrubConfig config)
    {
        if (config.SchemaVersion != CurrentSchemaVersion)
        {
            throw new ScrubConfigException(
                "$.schemaVersion",
                $"unsupported schemaVersion {config.SchemaVersion}; this build only accepts {CurrentSchemaVersion}");
        }

        // STJ does not enforce nullable annotations; coerce nulls to empty shapes.
        ScrubConfig normalised = config with
        {
            Rules = config.Rules ?? RulesConfig.Empty,
            Dictionaries = config.Dictionaries ?? new Dictionary<DetectionType, IReadOnlyList<string>>(),
            AllowList = config.AllowList ?? [],
            Ner = config.Ner ?? NerConfig.Empty,
        };

        ValidateThresholds(normalised.Ner.Thresholds);
        return normalised;
    }

    private static void ValidateThresholds(IReadOnlyDictionary<DetectionType, double> thresholds)
    {
        foreach ((DetectionType type, double value) in thresholds)
        {
            if (value is not (> 0.0 and <= 1.0) || double.IsNaN(value))
            {
                throw new ScrubConfigException(
                    $"$.ner.thresholds.{type}",
                    $"threshold {value} is outside the allowed range (0.0, 1.0]");
            }
        }
    }

    private static string NormalisePath(string? path)
        => string.IsNullOrEmpty(path) ? "$" : path;

    /// <summary>
    ///     The <see cref="JsonSerializerOptions"/> used for both parsing and
    ///     round-tripping. Strict member handling is the binding contract;
    ///     loose modes are not exposed to callers.
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = BuildJsonOptions();

    private static JsonSerializerOptions BuildJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = false,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter<DetectionType>(allowIntegerValues: false));
        return options;
    }
}
