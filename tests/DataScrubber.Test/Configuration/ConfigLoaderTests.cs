namespace DataScrubber.Test.Configuration;

using DataScrubber.Configuration;
using FluentAssertions;
using Xunit;

public class ConfigLoaderTests
{
    [Fact]
    public void ReturnsDefaultsWhenNoConfigFileFound()
    {
        using TempDir cwd = TempDir.Create();
        FakeEnvironment env = new(home: cwd.Path);

        ResolvedScrubConfig resolved = ConfigLoader.Load(null, env, cwd.Path);

        resolved.SourcePath.Should().BeNull();
        resolved.Config.Should().BeEquivalentTo(ScrubConfig.Defaults);
    }

    [Fact]
    public void ExplicitPathOverridesEverything()
    {
        using TempDir cwd = TempDir.Create();
        string explicitPath = Path.Combine(cwd.Path, "explicit.json");
        File.WriteAllText(explicitPath, """{ "schemaVersion": 1, "allowList": ["explicit"] }""");

        // Decoy: cwd config that would otherwise be picked up.
        File.WriteAllText(Path.Combine(cwd.Path, "scrub.config.json"),
            """{ "schemaVersion": 1, "allowList": ["cwd"] }""");

        FakeEnvironment env = new(home: cwd.Path);
        ResolvedScrubConfig resolved = ConfigLoader.Load(explicitPath, env, cwd.Path);

        resolved.Config.AllowList.Should().BeEquivalentTo(["explicit"]);
        resolved.SourcePath.Should().Be(Path.GetFullPath(explicitPath));
    }

    [Fact]
    public void CurrentDirectoryWinsOverUserConfig()
    {
        using TempDir cwd = TempDir.Create();
        using TempDir home = TempDir.Create();

        File.WriteAllText(Path.Combine(cwd.Path, "scrub.config.json"),
            """{ "schemaVersion": 1, "allowList": ["cwd"] }""");

        Directory.CreateDirectory(Path.Combine(home.Path, ".config", "datascrubber"));
        File.WriteAllText(Path.Combine(home.Path, ".config", "datascrubber", "config.json"),
            """{ "schemaVersion": 1, "allowList": ["user"] }""");

        FakeEnvironment env = new(home: home.Path);

        ResolvedScrubConfig resolved = ConfigLoader.Load(null, env, cwd.Path);

        resolved.Config.AllowList.Should().BeEquivalentTo(["cwd"]);
    }

    [Fact]
    public void UserConfigUsedWhenCwdAbsent()
    {
        using TempDir cwd = TempDir.Create();
        using TempDir home = TempDir.Create();

        Directory.CreateDirectory(Path.Combine(home.Path, ".config", "datascrubber"));
        File.WriteAllText(Path.Combine(home.Path, ".config", "datascrubber", "config.json"),
            """{ "schemaVersion": 1, "allowList": ["user"] }""");

        FakeEnvironment env = new(home: home.Path);

        ResolvedScrubConfig resolved = ConfigLoader.Load(null, env, cwd.Path);

        resolved.Config.AllowList.Should().BeEquivalentTo(["user"]);
    }

    [Fact]
    public void XdgConfigHomeIsHonouredWhenSet()
    {
        using TempDir cwd = TempDir.Create();
        using TempDir xdg = TempDir.Create();

        Directory.CreateDirectory(Path.Combine(xdg.Path, "datascrubber"));
        File.WriteAllText(Path.Combine(xdg.Path, "datascrubber", "config.json"),
            """{ "schemaVersion": 1, "allowList": ["xdg"] }""");

        FakeEnvironment env = new(home: cwd.Path, xdgConfigHome: xdg.Path);

        ResolvedScrubConfig resolved = ConfigLoader.Load(null, env, cwd.Path);

        resolved.Config.AllowList.Should().BeEquivalentTo(["xdg"]);
    }

    [Fact]
    public void WindowsAppDataLayoutResolves()
    {
        using TempDir cwd = TempDir.Create();
        using TempDir appData = TempDir.Create();

        Directory.CreateDirectory(Path.Combine(appData.Path, "DataScrubber"));
        File.WriteAllText(Path.Combine(appData.Path, "DataScrubber", "config.json"),
            """{ "schemaVersion": 1, "allowList": ["windows"] }""");

        FakeEnvironment env = new(home: cwd.Path, appData: appData.Path, isWindows: true);

        ResolvedScrubConfig resolved = ConfigLoader.Load(null, env, cwd.Path);

        resolved.Config.AllowList.Should().BeEquivalentTo(["windows"]);
    }

    [Fact]
    public void ExplicitPathMissingThrowsConfigError()
    {
        using TempDir cwd = TempDir.Create();
        string missing = Path.Combine(cwd.Path, "nope.json");

        FakeEnvironment env = new(home: cwd.Path);

        Action act = () => ConfigLoader.Load(missing, env, cwd.Path);

        act.Should().Throw<ScrubConfigException>().Which.Message.Should().Contain("not found");
    }

    [Fact]
    public void CompileFailureProducesPathPointingAtRule()
    {
        using TempDir cwd = TempDir.Create();
        string explicitPath = Path.Combine(cwd.Path, "explicit.json");
        File.WriteAllText(explicitPath, """
            {
              "schemaVersion": 1,
              "rules": { "custom": [ { "id": "broken", "type": "Organization", "pattern": "(?<unterminated" } ] }
            }
            """);

        FakeEnvironment env = new(home: cwd.Path);

        Action act = () => ConfigLoader.Load(explicitPath, env, cwd.Path);

        act.Should().Throw<ScrubConfigException>()
            .Which.JsonPath.Should().Be("$.rules.custom[0].pattern");
    }

    private sealed class FakeEnvironment : IConfigEnvironment
    {
        private readonly string? _home;
        private readonly string? _xdgConfigHome;
        private readonly string? _appData;

        public FakeEnvironment(string? home = null, string? xdgConfigHome = null, string? appData = null, bool isWindows = false)
        {
            _home = home;
            _xdgConfigHome = xdgConfigHome;
            _appData = appData;
            IsWindows = isWindows;
        }

        public bool IsWindows { get; }
        public string? HomeDirectory => _home;

        public string? GetEnvironmentVariable(string name) => name switch
        {
            "XDG_CONFIG_HOME" => _xdgConfigHome,
            "APPDATA" => _appData,
            _ => null,
        };
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        private TempDir(string path)
        {
            Path = path;
        }

        public static TempDir Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scrub-config-" + Guid.NewGuid());
            Directory.CreateDirectory(path);
            return new TempDir(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup; tests don't need to fail on transient I/O.
            }
        }
    }
}
