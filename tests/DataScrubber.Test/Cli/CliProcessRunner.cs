namespace DataScrubber.Test.Cli;

using System.Diagnostics;

internal static class CliProcessRunner
{
    public static string CliProjectPath { get; } = LocateCliProject();

    public static string CliDllPath { get; } = LocateCliDll();

    public static (int ExitCode, string Stdout, string Stderr) RunCli(
        string arguments,
        string? stdin = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        // Invoke the built dll directly rather than via `dotnet run --project`
        // because `dotnet run` resets the spawned process's working directory
        // to the project directory, which masks the WorkingDirectory caller
        // wants to test (e.g. stdin → cwd map placement).
        ProcessStartInfo info = new("dotnet", $"\"{CliDllPath}\" {arguments}")
        {
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(CliProjectPath),
        };

        info.Environment["DOTNET_NOLOGO"] = "1";
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        if (environment is not null)
        {
            foreach ((string key, string? value) in environment)
            {
                if (value is null)
                {
                    info.Environment.Remove(key);
                }
                else
                {
                    info.Environment[key] = value;
                }
            }
        }

        using Process process = Process.Start(info)!;

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }

        process.WaitForExit(120_000);

        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }

    private static string LocateCliProject()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "src", "DataScrubber.Cli", "DataScrubber.Cli.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("DataScrubber.Cli.csproj not found above the test runner directory.");
    }

    private static string LocateCliDll()
    {
        string config = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Release";
        string projDir = Path.GetDirectoryName(CliProjectPath)!;
        string candidate = Path.Combine(projDir, "bin", config, "net10.0", "scrub.dll");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException($"scrub.dll not found at {candidate}.");
    }
}
