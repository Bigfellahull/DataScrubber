namespace DataScrubber.Test.Cli;

using FluentAssertions;
using Xunit;

/// <summary>
///     One end-to-end test per M4 acceptance criterion. The CLI is invoked
///     via the same out-of-process runner used by the rest of the suite so
///     argument parsing, config loading, detection, and replacement are all
///     exercised together.
/// </summary>
public class ConfigEndToEndTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "scrub-m4-" + Guid.NewGuid());

    public ConfigEndToEndTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Ac1_CustomRegexRedactsProjectCodenames()
    {
        string input = WriteInput("Discussed Project Orion and project_atlas plans.");
        string config = WriteConfig("""
            {
              "schemaVersion": 1,
              "rules": {
                "custom": [
                  {
                    "id": "ProjectCodename",
                    "type": "Organization",
                    "pattern": "(?i)\\bproject[-_ ]?(orion|atlas|nimbus)\\b"
                  }
                ]
              }
            }
            """);

        (int code, string stdout, _) = RunCli(input, config);

        code.Should().Be(0);
        stdout.Should().Contain("[ORGANIZATION]");
        stdout.Should().NotContain("Project Orion");
        stdout.Should().NotContain("project_atlas");
    }

    [Fact]
    public void Ac2_DictionariesRedactKnownPhrases()
    {
        string input = WriteInput("Visited Acme Corp then Initech for lunch.");
        string config = WriteConfig("""
            {
              "schemaVersion": 1,
              "dictionaries": { "Organization": ["Acme Corp", "Initech"] }
            }
            """);

        (int code, string stdout, _) = RunCli(input, config);

        code.Should().Be(0);
        stdout.Should().NotContain("Acme Corp");
        stdout.Should().NotContain("Initech");
        stdout.Split("[ORGANIZATION]").Should().HaveCount(3);
    }

    [Fact]
    public void Ac3_DisabledMacAddressLeavesMacUntouched()
    {
        string input = WriteInput("Adapter 01:23:45:67:89:ab is online.");
        string config = WriteConfig("""
            { "schemaVersion": 1, "rules": { "disabled": ["MacAddress"] } }
            """);

        (int code, string stdout, _) = RunCli(input, config);

        code.Should().Be(0);
        stdout.Should().Contain("01:23:45:67:89:ab");
        stdout.Should().NotContain("[MAC_ADDRESS]");
    }

    [Fact]
    public void Ac4_AllowListPreservesExactCaseSensitiveMatch()
    {
        string input = WriteInput("Mail noreply@example.com and other@example.com");
        string config = WriteConfig("""
            { "schemaVersion": 1, "allowList": ["noreply@example.com"] }
            """);

        (int code, string stdout, _) = RunCli(input, config);

        code.Should().Be(0);
        stdout.Should().Contain("noreply@example.com");
        stdout.Should().NotContain("other@example.com");
        stdout.Should().Contain("[EMAIL]");
    }

    [Fact]
    public void Ac4_AllowListIsCaseSensitive()
    {
        string input = WriteInput("Mail NOREPLY@example.com please");
        string config = WriteConfig("""
            { "schemaVersion": 1, "allowList": ["noreply@example.com"] }
            """);

        (int code, string stdout, _) = RunCli(input, config);

        code.Should().Be(0);
        stdout.Should().NotContain("NOREPLY@example.com");
        stdout.Should().Contain("[EMAIL]");
    }

    [Fact]
    public void Ac5_NerThresholdConfigIsAcceptedAndLoadedByCli()
    {
        // AC5's behavioral coverage (raised threshold drops a 0.90 detection)
        // lives in ThresholdOverrideTests because it needs a deterministic
        // tokenizer/session pair. This end-to-end test pins the wiring path
        // through RunCommand: a config that sets ner.thresholds parses
        // cleanly, makes it through ConfigLoader, and the CLI exits 0 with
        // the rule pipeline unaffected.
        string input = WriteInput("Adapter 01:23:45:67:89:ab is online.");
        string config = WriteConfig("""
            { "schemaVersion": 1, "ner": { "thresholds": { "Person": 0.95, "Organization": 0.85 } } }
            """);

        (int code, string stdout, _) = RunCli(input, config);

        code.Should().Be(0);
        stdout.Should().Contain("[MAC_ADDRESS]");
    }

    [Fact]
    public void Ac6_UnknownTopLevelKeyExitsWith2()
    {
        string input = WriteInput("any text");
        string config = WriteConfig("""{ "schemaVersion": 1, "extraneous": true }""");

        (int code, _, string stderr) = RunCli(input, config);

        code.Should().Be(2);
        stderr.Should().Contain("Config error at");
        stderr.Should().Contain("extraneous");
    }

    [Fact]
    public void Ac6_UnknownDetectionTypeExitsWith2()
    {
        string input = WriteInput("any text");
        string config = WriteConfig("""
            { "schemaVersion": 1, "dictionaries": { "Bogus": ["x"] } }
            """);

        (int code, _, string stderr) = RunCli(input, config);

        code.Should().Be(2);
        stderr.Should().Contain("Config error at");
    }

    [Fact]
    public void Ac6_UnparseableRegexExitsWith2()
    {
        string input = WriteInput("any text");
        string config = WriteConfig("""
            {
              "schemaVersion": 1,
              "rules": { "custom": [ { "id": "broken", "type": "Person", "pattern": "(?<unterminated" } ] }
            }
            """);

        (int code, _, string stderr) = RunCli(input, config);

        code.Should().Be(2);
        stderr.Should().Contain("$.rules.custom[0].pattern");
    }

    [Fact]
    public void Ac6_ThresholdOutOfRangeExitsWith2()
    {
        string input = WriteInput("any text");
        string config = WriteConfig("""
            { "schemaVersion": 1, "ner": { "thresholds": { "Person": 1.5 } } }
            """);

        (int code, _, string stderr) = RunCli(input, config);

        code.Should().Be(2);
        stderr.Should().Contain("$.ner.thresholds.Person");
    }

    [Fact]
    public void Ac7_ExplicitConfigWinsOverCwdConfig()
    {
        // Two configs: one in cwd that disables MacAddress, one passed
        // explicitly that does not. Explicit must win; MAC should be
        // redacted under the explicit config even though the cwd config
        // would have left it.
        string explicitConfig = Path.Combine(_tempRoot, "explicit.json");
        File.WriteAllText(explicitConfig, """{ "schemaVersion": 1 }""");

        string cwdConfig = Path.Combine(_tempRoot, "scrub.config.json");
        File.WriteAllText(cwdConfig, """{ "schemaVersion": 1, "rules": { "disabled": ["MacAddress"] } }""");

        string input = Path.Combine(_tempRoot, "input.txt");
        File.WriteAllText(input, "MAC 01:23:45:67:89:ab here");

        // Run with explicit config; cwd is the same dir but explicit wins.
        (int code, string stdout, _) = CliProcessRunner.RunCli(
            $"\"{input}\" --no-ner --config \"{explicitConfig}\"",
            workingDirectory: _tempRoot);

        code.Should().Be(0);
        stdout.Should().NotContain("01:23:45:67:89:ab");
        stdout.Should().Contain("[MAC_ADDRESS]");
    }

    [Fact]
    public void Ac7_CwdConfigUsedWhenNoExplicitOption()
    {
        // No --config; cwd config should disable MacAddress.
        string cwdConfig = Path.Combine(_tempRoot, "scrub.config.json");
        File.WriteAllText(cwdConfig, """{ "schemaVersion": 1, "rules": { "disabled": ["MacAddress"] } }""");

        string input = Path.Combine(_tempRoot, "input.txt");
        File.WriteAllText(input, "MAC 01:23:45:67:89:ab here");

        (int code, string stdout, _) = CliProcessRunner.RunCli(
            $"\"{input}\" --no-ner",
            workingDirectory: _tempRoot);

        code.Should().Be(0);
        stdout.Should().Contain("01:23:45:67:89:ab");
    }

    [Fact]
    public void Ac7_UserConfigDirUsedWhenNoCwdConfig()
    {
        // The allow-list entry must be a substring that the rule pipeline
        // would otherwise redact, so the test observes config-driven
        // behaviour and not a string that survives regardless.
        string xdgRoot = Path.Combine(_tempRoot, "xdg");
        Directory.CreateDirectory(Path.Combine(xdgRoot, "datascrubber"));
        File.WriteAllText(
            Path.Combine(xdgRoot, "datascrubber", "config.json"),
            """{ "schemaVersion": 1, "allowList": ["user@example.com"] }""");

        string runDir = Path.Combine(_tempRoot, "run");
        Directory.CreateDirectory(runDir);

        string input = Path.Combine(runDir, "input.txt");
        File.WriteAllText(input, "send to alice@example.com or user@example.com or bob@example.com");

        IReadOnlyDictionary<string, string?> env = new Dictionary<string, string?>
        {
            ["XDG_CONFIG_HOME"] = xdgRoot,
            ["APPDATA"] = xdgRoot,
        };

        (int code, string stdout, _) = CliProcessRunner.RunCli(
            $"\"{input}\" --no-ner",
            workingDirectory: runDir,
            environment: env);

        code.Should().Be(0);
        stdout.Should().Contain("user@example.com", "the user-config-dir allow-list must keep this email");
        stdout.Should().NotContain("alice@example.com", "non-allow-listed emails must be redacted");
        stdout.Should().NotContain("bob@example.com", "non-allow-listed emails must be redacted");
    }

    [Fact]
    public void Ac7_DefaultsUsedWhenNoConfigPresent()
    {
        // Stage env vars so user-config-dir resolution lands in an empty tree
        // and the loader falls through to ScrubConfig.Defaults. Without an
        // allow-list in defaults the standard rule pipeline still applies.
        string xdgRoot = Path.Combine(_tempRoot, "empty-xdg");
        Directory.CreateDirectory(xdgRoot);

        string runDir = Path.Combine(_tempRoot, "run-defaults");
        Directory.CreateDirectory(runDir);

        string input = Path.Combine(runDir, "input.txt");
        File.WriteAllText(input, "Email alice@example.com from 10.0.1.5");

        IReadOnlyDictionary<string, string?> env = new Dictionary<string, string?>
        {
            ["XDG_CONFIG_HOME"] = xdgRoot,
            ["APPDATA"] = xdgRoot,
        };

        (int code, string stdout, _) = CliProcessRunner.RunCli(
            $"\"{input}\" --no-ner",
            workingDirectory: runDir,
            environment: env);

        code.Should().Be(0);
        stdout.Should().Be("Email [EMAIL] from [IPV4]");
    }

    [Fact]
    public void Ac7_FullMatrixExplicitWinsOverCwdAndUserConfig()
    {
        // Each layer allow-lists a different email; the email kept in output
        // identifies which layer was actually consulted. With all four layers
        // staged the explicit one must win.
        string explicitConfig = Path.Combine(_tempRoot, "explicit.json");
        File.WriteAllText(explicitConfig, """{ "schemaVersion": 1, "allowList": ["explicit@example.com"] }""");

        string runDir = Path.Combine(_tempRoot, "matrix");
        Directory.CreateDirectory(runDir);
        File.WriteAllText(
            Path.Combine(runDir, "scrub.config.json"),
            """{ "schemaVersion": 1, "allowList": ["cwd@example.com"] }""");

        string xdgRoot = Path.Combine(_tempRoot, "matrix-xdg");
        Directory.CreateDirectory(Path.Combine(xdgRoot, "datascrubber"));
        File.WriteAllText(
            Path.Combine(xdgRoot, "datascrubber", "config.json"),
            """{ "schemaVersion": 1, "allowList": ["user@example.com"] }""");

        string input = Path.Combine(runDir, "input.txt");
        File.WriteAllText(input, "explicit@example.com cwd@example.com user@example.com");

        IReadOnlyDictionary<string, string?> env = new Dictionary<string, string?>
        {
            ["XDG_CONFIG_HOME"] = xdgRoot,
            ["APPDATA"] = xdgRoot,
        };

        (int code, string stdout, _) = CliProcessRunner.RunCli(
            $"\"{input}\" --no-ner --config \"{explicitConfig}\"",
            workingDirectory: runDir,
            environment: env);

        code.Should().Be(0);
        stdout.Should().Contain("explicit@example.com", "the explicit layer's allow-list must win");
        stdout.Should().NotContain("cwd@example.com", "the cwd layer must be skipped when explicit is provided");
        stdout.Should().NotContain("user@example.com", "the user-config layer must be skipped when explicit is provided");
    }

    [Fact]
    public void DisablingSpecificSourceRuleSuppressesOnlyThatRule()
    {
        // Disable apikey.entropy specifically — well-known patterns like the
        // GitHub token (rule apikey.github) must still redact.
        string input = WriteInput(
            "Token ghp_abcdefghijklmnopqrstuvwxyz0123456789AB plus secret abcdefghijklmnopqrstuvwxyz012345");
        string config = WriteConfig("""
            { "schemaVersion": 1, "rules": { "disabled": ["apikey.entropy"] } }
            """);

        (int code, string stdout, _) = RunCli(input, config);

        code.Should().Be(0);
        stdout.Should().Contain("[API_KEY]", "the GitHub-token rule must still fire");
        stdout.Should().Contain("abcdefghijklmnopqrstuvwxyz012345", "the entropy rule should be disabled");
    }

    private string WriteInput(string text)
    {
        string path = Path.Combine(_tempRoot, "input-" + Guid.NewGuid() + ".txt");
        File.WriteAllText(path, text);
        return path;
    }

    private string WriteConfig(string json)
    {
        string path = Path.Combine(_tempRoot, "config-" + Guid.NewGuid() + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    private (int Code, string Stdout, string Stderr) RunCli(string input, string configPath)
        => CliProcessRunner.RunCli($"\"{input}\" --no-ner --config \"{configPath}\"", workingDirectory: _tempRoot);
}
