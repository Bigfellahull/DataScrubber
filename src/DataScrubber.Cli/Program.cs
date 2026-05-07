namespace DataScrubber.Cli;

using System.CommandLine;

/// <summary>
///     Entry point for the <c>scrub</c> executable. Builds the command tree
///     and forwards the parsed result to the run command's handler.
/// </summary>
public static class Program
{
    /// <summary>
    ///     Process entry point.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    public static int Main(string[] args)
    {
        RootCommand root = RunCommand.Build();
        root.Subcommands.Add(RehydrateCommand.Build());
        ParseResult parsed = root.Parse(args);

        if (parsed.Errors.Count > 0)
        {
            foreach (System.CommandLine.Parsing.ParseError error in parsed.Errors)
            {
                Console.Error.WriteLine($"scrub: {error.Message}");
            }

            return ExitCodes.InvalidArguments;
        }

        return parsed.Invoke();
    }
}
