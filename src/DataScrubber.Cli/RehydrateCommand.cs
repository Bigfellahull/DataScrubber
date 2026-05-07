namespace DataScrubber.Cli;

using System.CommandLine;
using System.Text.Json;
using DataScrubber.Mapping;
using Microsoft.Extensions.Logging;

/// <summary>
///     The <c>scrub rehydrate</c> subcommand. Reads a scrubbed text (typically
///     an LLM response that preserved the reversible tokens) and a mapping
///     file produced by an earlier <c>scrub --reversible</c> run, and emits a
///     copy of the input where every recognised token is substituted back to
///     its original. Tokens not present in the mapping are left in place and
///     reported on stderr.
/// </summary>
public static class RehydrateCommand
{
    /// <summary>
    ///     Builds the <c>rehydrate</c> subcommand.
    /// </summary>
    /// <returns>The configured <see cref="Command"/>.</returns>
    public static Command Build()
    {
        Argument<string> inputArg = new("input")
        {
            Description = "Path to the scrubbed/edited text, or '-' for stdin.",
        };

        Option<string> mapOption = new("--map")
        {
            Description = "Path to the mapping file produced by `scrub --reversible`.",
            Required = true,
        };

        Option<string?> outputOption = new("--output", "-o")
        {
            Description = "Path to the output file, or '-' for stdout. Defaults to stdout.",
        };

        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Raise the log level from Warning to Information.",
        };

        Command command = new("rehydrate", "Substitute reversible tokens back into a scrubbed text using a mapping file.")
        {
            inputArg,
            mapOption,
            outputOption,
            verboseOption,
        };

        command.SetAction(parseResult => Run(
            parseResult.GetRequiredValue(inputArg),
            parseResult.GetRequiredValue(mapOption),
            parseResult.GetValue(outputOption),
            parseResult.GetValue(verboseOption)));

        return command;
    }

    private static int Run(string input, string mapPath, string? output, bool verbose)
    {
        using ILoggerFactory loggerFactory = CliLoggers.Create(verbose);
        ILogger logger = loggerFactory.CreateLogger("scrub.rehydrate");

        int? mapError = TryReadMapping(mapPath, logger, out MappingFile mapping);
        if (mapError is not null)
        {
            return mapError.Value;
        }

        int? readError = CliIo.TryReadInputText(input, logger, out string text);
        if (readError is not null)
        {
            return readError.Value;
        }

        RehydrationResult result = new Rehydrator().Rehydrate(text, mapping);
        foreach (string token in result.UnknownTokens)
        {
            logger.LogInformation("Unknown token {Token} not in mapping", token);
            Console.Error.WriteLine($"Unknown token {token} left in place");
        }

        return CliIo.TryWriteOutput(output, result.Output, logger) ?? ExitCodes.Success;
    }

    private static int? TryReadMapping(string mapPath, ILogger logger, out MappingFile mapping)
    {
        mapping = default!;
        try
        {
            mapping = MappingFileReader.Read(mapPath);
            return null;
        }
        catch (UnsupportedMappingSchemaException ex)
        {
            logger.LogError("{Message}", ex.Message);
            return ExitCodes.InvalidArguments;
        }
        catch (FileNotFoundException)
        {
            logger.LogError("mapping file not found: {Path}", mapPath);
            return ExitCodes.InputNotFound;
        }
        catch (DirectoryNotFoundException)
        {
            logger.LogError("mapping file not found: {Path}", mapPath);
            return ExitCodes.InputNotFound;
        }
        catch (JsonException ex)
        {
            logger.LogError("invalid mapping file: {Message}", ex.Message);
            return ExitCodes.InvalidArguments;
        }
        catch (InvalidDataException ex)
        {
            logger.LogError("invalid mapping file: {Message}", ex.Message);
            return ExitCodes.InvalidArguments;
        }
    }
}
