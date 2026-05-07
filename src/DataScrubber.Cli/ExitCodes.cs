namespace DataScrubber.Cli;

/// <summary>
///     Process exit codes used by the <c>scrub</c> CLI. Matches the SPEC's
///     reserved range so later milestones (strict mode) can layer in without
///     conflict.
/// </summary>
public static class ExitCodes
{
    /// <summary>Successful run.</summary>
    public const int Success = 0;

    /// <summary>Generic runtime error (e.g. unexpected I/O failure).</summary>
    public const int GenericError = 1;

    /// <summary>Invalid arguments, invalid configuration, or an unsupported feature flag.</summary>
    public const int InvalidArguments = 2;

    /// <summary>The input file could not be found or read.</summary>
    public const int InputNotFound = 3;

    /// <summary>The NER model, tokenizer, or label map could not be located, opened, or parsed.</summary>
    public const int NerModelLoadFailed = 4;
}
