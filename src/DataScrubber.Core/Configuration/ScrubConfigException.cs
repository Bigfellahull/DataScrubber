namespace DataScrubber.Configuration;

/// <summary>
///     Thrown when a user-supplied configuration file cannot be parsed or
///     validated. The message is formatted as
///     <c>Config error at &lt;jsonPath&gt;: &lt;message&gt;</c> so the offending
///     JSON path is always identified for the operator. Callers map this to
///     CLI exit code <c>2</c>.
/// </summary>
public sealed class ScrubConfigException : Exception
{
    /// <summary>
    ///     Creates a <see cref="ScrubConfigException"/> formatted as
    ///     <c>Config error at &lt;jsonPath&gt;: &lt;message&gt;</c>.
    /// </summary>
    /// <param name="jsonPath">The JSON path of the offending value.</param>
    /// <param name="message">The reason the value was rejected.</param>
    public ScrubConfigException(string jsonPath, string message)
        : base($"Config error at {jsonPath}: {message}")
    {
        JsonPath = jsonPath;
    }

    /// <summary>The JSON path of the offending value.</summary>
    public string JsonPath { get; }
}
