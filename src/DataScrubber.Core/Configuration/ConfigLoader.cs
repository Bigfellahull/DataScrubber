namespace DataScrubber.Configuration;

using DataScrubber.Detection;

/// <summary>
///     Resolves and loads the user configuration. The resolution order is
///     fixed: explicit <c>--config</c> &gt; <c>./scrub.config.json</c> &gt;
///     <c>$XDG_CONFIG_HOME/datascrubber/config.json</c> (or
///     <c>%APPDATA%\DataScrubber\config.json</c> on Windows) &gt; built-in
///     defaults. First match wins; layers are not merged. Callers log the
///     resolved <see cref="ResolvedScrubConfig.SourcePath"/> at the level they
///     prefer.
/// </summary>
public static class ConfigLoader
{
    /// <summary>The filename used for the current-directory config layer.</summary>
    public const string CurrentDirectoryConfigName = "scrub.config.json";

    /// <summary>The directory name appended to <c>$XDG_CONFIG_HOME</c> / <c>%APPDATA%</c>.</summary>
    public const string UserConfigDirectoryName = "datascrubber";

    /// <summary>The filename used for the user-config-dir layer.</summary>
    public const string UserConfigFileName = "config.json";

    /// <summary>
    ///     Resolves and loads the active configuration. When
    ///     <paramref name="explicitPath"/> is supplied, the file must exist and
    ///     parse cleanly; missing files at the implicit layers fall through to
    ///     the next layer.
    /// </summary>
    /// <param name="explicitPath">Path passed via <c>--config</c>, or <c>null</c>.</param>
    /// <param name="environment">Environment overrides used by tests; defaults to the live process environment.</param>
    /// <param name="currentDirectory">Working directory used by tests; defaults to <see cref="Directory.GetCurrentDirectory"/>.</param>
    /// <returns>The resolved configuration bundle.</returns>
    /// <exception cref="ScrubConfigException">Raised on parse, validation, or compile failure.</exception>
    public static ResolvedScrubConfig Load(
        string? explicitPath,
        IConfigEnvironment? environment = null,
        string? currentDirectory = null)
    {
        IConfigEnvironment env = environment ?? ProcessEnvironment.Instance;
        string cwd = currentDirectory ?? Directory.GetCurrentDirectory();

        string? path = ResolvePath(explicitPath, cwd, env);
        if (path is null)
        {
            return Build(ScrubConfig.Defaults, sourcePath: null);
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ScrubConfigException("$", $"failed to read config file '{path}': {ex.Message}");
        }

        ScrubConfig config = ScrubConfig.Parse(json);
        return Build(config, path);
    }

    /// <summary>
    ///     Resolves the on-disk config path according to the documented
    ///     resolution order. Returns <c>null</c> when no candidate exists, in
    ///     which case the caller falls back to <see cref="ScrubConfig.Defaults"/>.
    /// </summary>
    /// <param name="explicitPath">Path passed via <c>--config</c>, or <c>null</c>.</param>
    /// <param name="currentDirectory">The directory used to resolve <c>./scrub.config.json</c>.</param>
    /// <param name="environment">Environment overrides used to resolve the user-config layer.</param>
    /// <returns>The resolved path, or <c>null</c>.</returns>
    public static string? ResolvePath(string? explicitPath, string currentDirectory, IConfigEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(currentDirectory);
        ArgumentNullException.ThrowIfNull(environment);

        if (!string.IsNullOrEmpty(explicitPath))
        {
            string full = Path.GetFullPath(explicitPath);
            if (!File.Exists(full))
            {
                throw new ScrubConfigException("$", $"config file not found: {full}");
            }

            return full;
        }

        string cwdCandidate = Path.GetFullPath(Path.Combine(currentDirectory, CurrentDirectoryConfigName));
        if (File.Exists(cwdCandidate))
        {
            return cwdCandidate;
        }

        string? userCandidate = ResolveUserConfigPath(environment);
        if (userCandidate is not null && File.Exists(userCandidate))
        {
            return userCandidate;
        }

        return null;
    }

    private static string? ResolveUserConfigPath(IConfigEnvironment environment)
    {
        if (environment.IsWindows)
        {
            string? appData = environment.GetEnvironmentVariable("APPDATA");
            return string.IsNullOrEmpty(appData)
                ? null
                : Path.Combine(appData, "DataScrubber", UserConfigFileName);
        }

        string? baseDir = ResolveUnixConfigBase(environment);
        return baseDir is null
            ? null
            : Path.Combine(baseDir, UserConfigDirectoryName, UserConfigFileName);
    }

    private static string? ResolveUnixConfigBase(IConfigEnvironment environment)
    {
        string? xdg = environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdg))
        {
            return xdg;
        }

        string? home = environment.HomeDirectory;
        return string.IsNullOrEmpty(home) ? null : Path.Combine(home, ".config");
    }

    private static ResolvedScrubConfig Build(ScrubConfig config, string? sourcePath)
    {
        IReadOnlyList<CustomRule> custom = config.Rules.Custom;
        CustomRegexDetector regexDetector;
        try
        {
            regexDetector = CustomRegexDetector.Compile(custom);
        }
        catch (CustomRuleCompileException ex)
        {
            throw new ScrubConfigException(
                $"$.rules.custom[{ex.RuleIndex}].pattern",
                $"failed to compile regex for rule '{ex.RuleId}': {ex.Message}");
        }

        DictionaryDetector dictionaryDetector = new(config.Dictionaries);
        AllowListFilter allowList = new(config.AllowList);

        return new ResolvedScrubConfig(config, regexDetector, dictionaryDetector, allowList, sourcePath);
    }
}

/// <summary>
///     Indirection over <see cref="System.Environment"/> so tests can drive
///     <see cref="ConfigLoader"/> without mutating real process state.
/// </summary>
public interface IConfigEnvironment
{
    /// <summary>Returns <c>true</c> on Windows; selects the <c>%APPDATA%</c> resolution branch.</summary>
    bool IsWindows { get; }

    /// <summary>The current user's home directory, or <c>null</c> when unknown.</summary>
    string? HomeDirectory { get; }

    /// <summary>Looks up an environment variable by name.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The value, or <c>null</c> when unset.</returns>
    string? GetEnvironmentVariable(string name);
}

/// <summary>The default <see cref="IConfigEnvironment"/> backed by the live process.</summary>
public sealed class ProcessEnvironment : IConfigEnvironment
{
    /// <summary>The shared singleton.</summary>
    public static ProcessEnvironment Instance { get; } = new();

    private ProcessEnvironment()
    {
    }

    /// <inheritdoc />
    public bool IsWindows => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public string? HomeDirectory => Environment.GetEnvironmentVariable("HOME")
        ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <inheritdoc />
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
}
