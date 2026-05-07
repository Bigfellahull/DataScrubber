namespace DataScrubber.Cli;

using Microsoft.Extensions.Logging;

/// <summary>
///     Builds the <see cref="ILoggerFactory"/> shared by the <c>scrub</c>
///     subcommands. All output is routed to stderr regardless of level so
///     stdout stays reserved for the scrubbed content. Quiet mode raises the
///     threshold to <see cref="LogLevel.Error"/>; <c>--json-logs</c> swaps the
///     simple console formatter for the JSON-line one.
/// </summary>
internal static class CliLoggers
{
    /// <summary>
    ///     Creates a logger factory whose minimum level is <c>Information</c>
    ///     when <paramref name="verbose"/> is <c>true</c> and <c>Warning</c>
    ///     otherwise.
    /// </summary>
    /// <param name="verbose">Whether to raise the threshold to <c>Information</c>.</param>
    /// <returns>The configured <see cref="ILoggerFactory"/>.</returns>
    public static ILoggerFactory Create(bool verbose)
        => Create(verbose, quiet: false, jsonLogs: false);

    /// <summary>
    ///     Creates a logger factory configured for the requested run posture.
    ///     <paramref name="quiet"/> wins over <paramref name="verbose"/>: when
    ///     it is set the minimum level is forced to <see cref="LogLevel.Error"/>
    ///     so only error-level events reach stderr. <paramref name="jsonLogs"/>
    ///     selects the JSON-line console formatter.
    /// </summary>
    /// <param name="verbose">Raises the threshold from <c>Warning</c> to <c>Information</c>.</param>
    /// <param name="quiet">Forces the threshold to <c>Error</c>.</param>
    /// <param name="jsonLogs">Use the JSON-line console formatter instead of the simple one.</param>
    /// <returns>The configured <see cref="ILoggerFactory"/>.</returns>
    public static ILoggerFactory Create(bool verbose, bool quiet, bool jsonLogs)
    {
        LogLevel level = quiet
            ? LogLevel.Error
            : (verbose ? LogLevel.Information : LogLevel.Warning);

        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(level);
            builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            if (jsonLogs)
            {
                builder.AddJsonConsole(options =>
                {
                    options.IncludeScopes = false;
                    options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
                    {
                        Indented = false,
                    };
                });
            }
            else
            {
                builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.IncludeScopes = false;
                    options.TimestampFormat = null;
                });
            }
        });
    }
}
