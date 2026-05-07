namespace DataScrubber.Cli;

using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DataScrubber.Cli.Reporting;
using DataScrubber.Configuration;
using DataScrubber.Detection;
using DataScrubber.Detection.Ner;
using DataScrubber.Mapping;
using DataScrubber.Replacement;
using Microsoft.Extensions.Logging;

/// <summary>
///     The default <c>scrub</c> command. Reads a UTF-8 text input from a file,
///     stdin, or a directory tree, runs the rule-based detection pipeline,
///     applies one-way or reversible replacement, and writes the result to a
///     file, stdout, or a mirrored output tree.
///     Large inputs (stdin and files at or above <c>--stream-threshold-mb</c>)
///     route through <see cref="StreamingScrubber"/> so memory stays bounded.
///     With <c>--reversible</c> the command also writes a JSON mapping file
///     beside the output and emits a stderr warning naming the absolute path.
///     With NER enabled (the default), an <see cref="OnnxNerDetector"/> runs
///     alongside the rule detectors and contributes
///     <see cref="DetectionType.Person"/> /
///     <see cref="DetectionType.Organization"/> /
///     <see cref="DetectionType.Location"/> spans.
///     With <c>--config</c> (or an auto-discovered config file) the command
///     also runs custom regex / dictionary detectors, drops disabled rule
///     IDs, applies the allow-list post-merge, and overrides NER thresholds.
///     <c>--dry-run</c> skips writes entirely; <c>--report</c> always emits the
///     per-type summary; <c>--quiet</c> suppresses non-error output.
/// </summary>
public sealed class RunCommand
{
    /// <summary>The default <c>models/</c> directory resolved relative to the executable.</summary>
    public static string DefaultModelDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "models");

    /// <summary>The default ONNX model path.</summary>
    public static string DefaultModelPath { get; } = Path.Combine(DefaultModelDirectory, "ner.onnx");

    /// <summary>Default streaming threshold in megabytes.</summary>
    public const int DefaultStreamThresholdMb = 50;

    /// <summary>
    ///     Builds the configured root command tree.
    /// </summary>
    /// <returns>The configured <see cref="RootCommand"/>.</returns>
    public static RootCommand Build()
    {
        Argument<string> inputArg = new("input")
        {
            Description = "Path to the input file, directory, or '-' for stdin.",
        };

        Option<string?> outputOption = new("--output", "-o")
        {
            Description = "Path to the output file or directory, or '-' for stdout. Defaults to stdout for files; required for directory mode.",
        };

        Option<bool> noNerOption = new("--no-ner")
        {
            Description = "Skip the NER detection pass.",
        };

        Option<string?> modelOption = new("--model")
        {
            Description = "Override the NER model path. Defaults to <exe-dir>/models/ner.onnx.",
        };

        Option<bool> reversibleOption = new("--reversible")
        {
            Description = "Enable reversible mode and emit a mapping file.",
        };

        Option<string?> mapFileOption = new("--map-file")
        {
            Description = "Explicit mapping-file path. Only valid with --reversible.",
        };

        Option<string?> configOption = new("--config")
        {
            Description = "Path to a JSON configuration file.",
        };

        Option<bool> verboseOption = new("--verbose")
        {
            Description = "Raise the log level from Warning to Information.",
        };

        Option<bool> recursiveOption = new("--recursive")
        {
            Description = "When the input is a directory, recurse into subdirectories.",
        };

        Option<string?> includeOption = new("--include")
        {
            Description = "Comma-separated include globs in directory mode. Defaults to **/*.txt,**/*.log,**/*.md.",
        };

        Option<string?> excludeOption = new("--exclude")
        {
            Description = "Comma-separated exclude globs in directory mode. Defaults to none.",
        };

        Option<bool> dryRunOption = new("--dry-run")
        {
            Description = "Detect only; skip output writes and mapping files. Always emits the report (subject to --quiet).",
        };

        Option<bool> reportOption = new("--report")
        {
            Description = "Always emit the per-type detection report on stderr, even outside --dry-run.",
        };

        Option<bool> jsonLogsOption = new("--json-logs")
        {
            Description = "Emit logs and the report as JSON lines on stderr.",
        };

        Option<bool> quietOption = new("--quiet")
        {
            Description = "Suppress non-error stderr output. Wins over --report for the human/JSON report.",
        };

        Option<int?> streamThresholdOption = new("--stream-threshold-mb")
        {
            Description = $"File-size threshold (in MiB) above which streaming is used. Default {DefaultStreamThresholdMb}.",
        };

        RootCommand root = new("scrub — local PII / secret redactor")
        {
            inputArg,
            outputOption,
            noNerOption,
            modelOption,
            reversibleOption,
            mapFileOption,
            configOption,
            verboseOption,
            recursiveOption,
            includeOption,
            excludeOption,
            dryRunOption,
            reportOption,
            jsonLogsOption,
            quietOption,
            streamThresholdOption,
        };

        root.SetAction(parseResult => Run(new RunOptions
        {
            Input = parseResult.GetRequiredValue(inputArg),
            Output = parseResult.GetValue(outputOption),
            NoNer = parseResult.GetValue(noNerOption),
            ModelPath = parseResult.GetValue(modelOption),
            Reversible = parseResult.GetValue(reversibleOption),
            MapFile = parseResult.GetValue(mapFileOption),
            ConfigPath = parseResult.GetValue(configOption),
            Verbose = parseResult.GetValue(verboseOption),
            Recursive = parseResult.GetValue(recursiveOption),
            Include = parseResult.GetValue(includeOption),
            Exclude = parseResult.GetValue(excludeOption),
            DryRun = parseResult.GetValue(dryRunOption),
            Report = parseResult.GetValue(reportOption),
            JsonLogs = parseResult.GetValue(jsonLogsOption),
            Quiet = parseResult.GetValue(quietOption),
            StreamThresholdMb = parseResult.GetValue(streamThresholdOption) ?? DefaultStreamThresholdMb,
        }));

        return root;
    }

    private static int Run(RunOptions opts)
    {
        using ILoggerFactory loggerFactory = CliLoggers.Create(opts.Verbose, opts.Quiet, opts.JsonLogs);
        ILogger logger = loggerFactory.CreateLogger("scrub");

        if (opts.MapFile is not null && !opts.Reversible)
        {
            logger.LogError("--map-file requires --reversible.");
            return ExitCodes.InvalidArguments;
        }

        if (opts.NoNer && opts.ModelPath is not null)
        {
            logger.LogError("--model and --no-ner are mutually exclusive.");
            return ExitCodes.InvalidArguments;
        }

        if (opts.StreamThresholdMb < 0)
        {
            logger.LogError("--stream-threshold-mb must be non-negative.");
            return ExitCodes.InvalidArguments;
        }

        ResolvedScrubConfig resolvedConfig;
        try
        {
            resolvedConfig = ConfigLoader.Load(opts.ConfigPath);
        }
        catch (ScrubConfigException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitCodes.InvalidArguments;
        }

        if (resolvedConfig.SourcePath is { } sourcePath)
        {
            logger.LogInformation("Loaded configuration from {Path}", sourcePath);
        }

        InputKind inputKind = ClassifyInput(opts.Input);
        if (inputKind is InputKind.NotFound)
        {
            logger.LogError("input not found: {Path}", opts.Input);
            return ExitCodes.InputNotFound;
        }

        if (inputKind is InputKind.Directory)
        {
            return RunDirectory(opts, resolvedConfig, logger);
        }

        return RunSingle(opts, resolvedConfig, logger, isStdin: inputKind is InputKind.Stdin);
    }

    private static int RunSingle(
        RunOptions opts,
        ResolvedScrubConfig resolvedConfig,
        ILogger logger,
        bool isStdin)
    {
        OnnxNerDetector? nerDetector = null;
        try
        {
            if (!opts.NoNer)
            {
                int? loadError = TryLoadNer(opts.ModelPath, resolvedConfig.Config.Ner, logger, out nerDetector);
                if (loadError is not null)
                {
                    return loadError.Value;
                }
            }

            ReportBuilder reportBuilder = new();
            int code = ProcessSingleSource(opts, resolvedConfig, nerDetector, logger, reportBuilder, isStdin);
            EmitReport(opts, reportBuilder);
            return code;
        }
        finally
        {
            nerDetector?.Dispose();
        }
    }

    private static int ProcessSingleSource(
        RunOptions opts,
        ResolvedScrubConfig resolvedConfig,
        OnnxNerDetector? nerDetector,
        ILogger logger,
        ReportBuilder reportBuilder,
        bool isStdin)
    {
        bool useStreaming = !opts.Reversible && (isStdin || ExceedsThreshold(opts.Input, opts.StreamThresholdMb));

        if (useStreaming)
        {
            return ProcessStreaming(opts, resolvedConfig, nerDetector, logger, reportBuilder, isStdin);
        }

        int? readError = CliIo.TryReadInputBytes(opts.Input, logger, out byte[] inputBytes, out string text);
        if (readError is not null)
        {
            return readError.Value;
        }

        Stopwatch fileTimer = Stopwatch.StartNew();
        IReadOnlyList<Detection> finalDetections;
        try
        {
            finalDetections = text.Length == 0
                ? []
                : ResolveDetections(text, nerDetector, resolvedConfig);
        }
        catch (NerModelLoadException ex)
        {
            logger.LogError("NER model load failed: {Message} (path: {Path})", ex.Message, ex.MissingPath);
            return ExitCodes.NerModelLoadFailed;
        }

        int code = opts.Reversible
            ? RunReversible(opts.Input, opts.Output, opts.MapFile, opts.DryRun, inputBytes, text, finalDetections, logger)
            : RunOneWay(opts.Output, opts.DryRun, text, finalDetections, logger, out finalDetections);

        fileTimer.Stop();
        reportBuilder.AddFile(opts.Input, CountByType(finalDetections), fileTimer.ElapsedMilliseconds);
        return code;
    }

    private static int ProcessStreaming(
        RunOptions opts,
        ResolvedScrubConfig resolvedConfig,
        OnnxNerDetector? nerDetector,
        ILogger logger,
        ReportBuilder reportBuilder,
        bool isStdin)
    {
        IDetector pipeline = BuildPipeline(nerDetector, resolvedConfig);
        DetectionContext baseContext = new()
        {
            SourceName = isStdin ? "-" : opts.Input,
            AllowList = resolvedConfig.AllowList,
        };
        StreamingScrubber scrubber = new(pipeline, baseContext);

        Stopwatch fileTimer = Stopwatch.StartNew();
        IReadOnlyDictionary<DetectionType, int> counts;
        try
        {
            using TextReader reader = OpenReader(opts.Input, isStdin);
            if (opts.DryRun)
            {
                using TextWriter sink = TextWriter.Null;
                counts = scrubber.Process(reader, sink);
            }
            else if (opts.Output is null or "-")
            {
                using StreamWriter writer = new(Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                counts = scrubber.Process(reader, writer);
            }
            else
            {
                EnsureParentDirectory(opts.Output);
                using StreamWriter writer = new(opts.Output, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                counts = scrubber.Process(reader, writer);
            }
        }
        catch (FileNotFoundException ex)
        {
            logger.LogError("input not found: {Path}", ex.FileName ?? opts.Input);
            return ExitCodes.InputNotFound;
        }
        catch (DirectoryNotFoundException)
        {
            logger.LogError("input not found: {Path}", opts.Input);
            return ExitCodes.InputNotFound;
        }
        catch (UnauthorizedAccessException)
        {
            logger.LogError("input not readable: {Path}", opts.Input);
            return ExitCodes.InputNotFound;
        }
        catch (IOException ex)
        {
            logger.LogError("I/O error during streaming: {Message}", ex.Message);
            return ExitCodes.GenericError;
        }
        catch (NerModelLoadException ex)
        {
            logger.LogError("NER model load failed: {Message} (path: {Path})", ex.Message, ex.MissingPath);
            return ExitCodes.NerModelLoadFailed;
        }

        fileTimer.Stop();
        reportBuilder.AddFile(isStdin ? "-" : opts.Input, counts, fileTimer.ElapsedMilliseconds);
        return ExitCodes.Success;
    }

    private static int RunDirectory(
        RunOptions opts,
        ResolvedScrubConfig resolvedConfig,
        ILogger logger)
    {
        if (string.IsNullOrEmpty(opts.Output) || opts.Output == "-")
        {
            logger.LogError("directory mode requires --output to be a directory path.");
            return ExitCodes.InvalidArguments;
        }

        if (opts.Reversible && opts.MapFile is not null)
        {
            logger.LogError("--map-file cannot be combined with directory-mode input; each file gets its own map next to its output.");
            return ExitCodes.InvalidArguments;
        }

        if (DirectoryWalker.PathsOverlap(opts.Input, opts.Output))
        {
            logger.LogError("--output must not be the same as or contained within the input directory.");
            return ExitCodes.InvalidArguments;
        }

        IReadOnlyList<string>? includes = ParseGlobList(opts.Include);
        IReadOnlyList<string>? excludes = ParseGlobList(opts.Exclude);

        DirectoryWalker.WalkResult walk;
        try
        {
            walk = DirectoryWalker.Walk(opts.Input, includes, excludes, opts.Recursive);
        }
        catch (DirectoryNotFoundException)
        {
            logger.LogError("input not found: {Path}", opts.Input);
            return ExitCodes.InputNotFound;
        }
        catch (UnauthorizedAccessException)
        {
            logger.LogError("input not readable: {Path}", opts.Input);
            return ExitCodes.InputNotFound;
        }

        ReportBuilder reportBuilder = new();
        int successCount = 0;
        int failureCount = 0;

        OnnxNerDetector? nerDetector = null;
        try
        {
            if (!opts.NoNer)
            {
                int? loadError = TryLoadNer(opts.ModelPath, resolvedConfig.Config.Ner, logger, out nerDetector);
                if (loadError is not null)
                {
                    return loadError.Value;
                }
            }

            foreach (DirectoryWalker.MatchedFile file in walk.Files)
            {
                bool ok = ProcessOneDirectoryFile(
                    file,
                    walk.Root,
                    Path.GetFullPath(opts.Output),
                    opts,
                    resolvedConfig,
                    nerDetector,
                    logger,
                    reportBuilder);

                if (ok)
                {
                    successCount++;
                }
                else
                {
                    failureCount++;
                }
            }
        }
        finally
        {
            nerDetector?.Dispose();
        }

        EmitReport(opts, reportBuilder);

        if (walk.Files.Count == 0)
        {
            return ExitCodes.Success;
        }

        return successCount == 0 ? ExitCodes.GenericError : ExitCodes.Success;
    }

    private static bool ProcessOneDirectoryFile(
        DirectoryWalker.MatchedFile file,
        string inputRoot,
        string outputRoot,
        RunOptions opts,
        ResolvedScrubConfig resolvedConfig,
        OnnxNerDetector? nerDetector,
        ILogger logger,
        ReportBuilder reportBuilder)
    {
        string outputPath = Path.Combine(outputRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Stopwatch fileTimer = Stopwatch.StartNew();

        try
        {
            // Reversible mode reads the file fully so it can compute SHA + assign
            // stable per-original tokens; streaming is bypassed in this branch.
            bool useStreaming = !opts.Reversible && ExceedsThreshold(file.AbsolutePath, opts.StreamThresholdMb);
            IReadOnlyDictionary<DetectionType, int> counts;

            if (useStreaming)
            {
                IDetector pipeline = BuildPipeline(nerDetector, resolvedConfig);
                DetectionContext baseContext = new()
                {
                    SourceName = file.AbsolutePath,
                    AllowList = resolvedConfig.AllowList,
                };
                StreamingScrubber scrubber = new(pipeline, baseContext);

                using TextReader reader = OpenReader(file.AbsolutePath, isStdin: false);
                if (opts.DryRun)
                {
                    using TextWriter sink = TextWriter.Null;
                    counts = scrubber.Process(reader, sink);
                }
                else
                {
                    EnsureParentDirectory(outputPath);
                    using StreamWriter writer = new(outputPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    counts = scrubber.Process(reader, writer);
                }
            }
            else if (opts.Reversible)
            {
                byte[] inputBytes = File.ReadAllBytes(file.AbsolutePath);
                string text = CliIo.DecodeUtf8Bom(inputBytes);
                IReadOnlyList<Detection> detections = text.Length == 0
                    ? []
                    : ResolveDetections(text, nerDetector, resolvedConfig);

                ReversibleReplacementResult result = new ReversibleReplacer()
                    .ReplaceWithMapping(text, detections, ReplacerOptions.Default);
                string mapPath = Path.Combine(
                    Path.GetDirectoryName(outputPath) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(outputPath) + ".map.json");

                if (!opts.DryRun)
                {
                    EnsureParentDirectory(outputPath);
                    MappingFile mapping = new(
                        MappingFile.CurrentSchemaVersion,
                        DateTimeOffset.UtcNow,
                        ComputeSha256(inputBytes),
                        result.Entries);
                    MappingFileWriter.Write(mapping, mapPath);
                    Console.Error.WriteLine($"WARNING: {mapPath} contains raw PII. Treat it as sensitive.");
                    File.WriteAllText(outputPath, result.Output, new UTF8Encoding(false));
                }

                counts = CountByType(detections);
            }
            else
            {
                string text = File.ReadAllText(file.AbsolutePath, new UTF8Encoding(false));
                IReadOnlyList<Detection> detections = text.Length == 0
                    ? []
                    : ResolveDetections(text, nerDetector, resolvedConfig);
                ReplacementResult result = new OneWayReplacer().Replace(text, detections, ReplacerOptions.Default);
                if (!opts.DryRun)
                {
                    EnsureParentDirectory(outputPath);
                    File.WriteAllText(outputPath, result.Output, new UTF8Encoding(false));
                }
                counts = CountByType(result.Applied);
            }

            fileTimer.Stop();
            reportBuilder.AddFile(file.AbsolutePath, counts, fileTimer.ElapsedMilliseconds);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or FileNotFoundException)
        {
            // Spec form is the literal "Warning: <path>: <message>"; route
            // through stderr directly so the format is byte-stable. Quiet
            // mode still suppresses these because they are non-error output.
            if (!opts.Quiet)
            {
                Console.Error.WriteLine($"Warning: {file.AbsolutePath}: {ex.Message}");
            }
            return false;
        }
        catch (NerModelLoadException ex)
        {
            logger.LogError("NER model load failed: {Message} (path: {Path})", ex.Message, ex.MissingPath);
            return false;
        }
    }

    private static IDetector BuildPipeline(OnnxNerDetector? nerDetector, ResolvedScrubConfig resolvedConfig)
    {
        List<IDetector> detectors = [
            RuleBasedDetector.CreateDefault(),
            resolvedConfig.CustomRegexDetector,
            resolvedConfig.DictionaryDetector,
        ];

        if (nerDetector is not null)
        {
            detectors.Add(nerDetector);
        }

        return new ChainedDetector(detectors, resolvedConfig);
    }

    private static IReadOnlyList<Detection> ResolveDetections(
        string text,
        OnnxNerDetector? nerDetector,
        ResolvedScrubConfig resolvedConfig)
    {
        IDetector pipeline = BuildPipeline(nerDetector, resolvedConfig);
        DetectionContext ctx = new()
        {
            Input = text,
            AllowList = resolvedConfig.AllowList,
        };

        IReadOnlyList<Detection> merged = DetectionMerger.Merge(pipeline.Detect(text.AsMemory(), ctx));

        List<Detection> kept = new(merged.Count);
        foreach (Detection detection in merged)
        {
            if (!ctx.ShouldDrop(detection))
            {
                kept.Add(detection);
            }
        }

        return kept;
    }

    private static int? TryLoadNer(
        string? modelPath,
        NerConfig nerConfig,
        ILogger logger,
        out OnnxNerDetector? detector)
    {
        detector = null;
        NerModelConfig config = NerModelConfig.FromModelPath(modelPath ?? DefaultModelPath);

        try
        {
            OnnxNerDetector instance = new(config, nerConfig.ToNerThresholds());
            instance.EnsureLoaded();
            detector = instance;
            return null;
        }
        catch (NerModelLoadException ex)
        {
            logger.LogError("NER model load failed: {Message} (path: {Path})", ex.Message, ex.MissingPath);
            return ExitCodes.NerModelLoadFailed;
        }
    }

    private static int RunOneWay(
        string? output,
        bool dryRun,
        string text,
        IReadOnlyList<Detection> merged,
        ILogger logger,
        out IReadOnlyList<Detection> applied)
    {
        ReplacementResult result = new OneWayReplacer().Replace(text, merged, ReplacerOptions.Default);
        applied = result.Applied;
        logger.LogInformation("scrubbed input of {Bytes} characters", text.Length);

        if (dryRun)
        {
            return ExitCodes.Success;
        }

        return CliIo.TryWriteOutput(output, result.Output, logger) ?? ExitCodes.Success;
    }

    private static int RunReversible(
        string input,
        string? output,
        string? mapFile,
        bool dryRun,
        byte[] inputBytes,
        string text,
        IReadOnlyList<Detection> merged,
        ILogger logger)
    {
        if (dryRun)
        {
            logger.LogInformation("dry-run: skipping reversible replacement and mapping write for {Path}", input);
            return ExitCodes.Success;
        }

        ReversibleReplacementResult result;
        try
        {
            result = new ReversibleReplacer().ReplaceWithMapping(text, merged, ReplacerOptions.Default);
        }
        catch (TokenShapedOriginalException ex)
        {
            logger.LogError("{Message}", ex.Message);
            return ExitCodes.GenericError;
        }
        logger.LogInformation(
            "scrubbed input of {Bytes} characters with {EntryCount} mapping entries",
            text.Length,
            result.Entries.Count);

        string mapPath = ResolveMapPath(mapFile, input, output);
        MappingFile mapping = new(
            MappingFile.CurrentSchemaVersion,
            DateTimeOffset.UtcNow,
            ComputeSha256(inputBytes),
            result.Entries);

        try
        {
            MappingFileWriter.Write(mapping, mapPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            logger.LogError("failed to write mapping file {Path}: {Message}", mapPath, ex.Message);
            return ExitCodes.GenericError;
        }

        Console.Error.WriteLine($"WARNING: {mapPath} contains raw PII. Treat it as sensitive.");

        return CliIo.TryWriteOutput(output, result.Output, logger) ?? ExitCodes.Success;
    }

    private static string ResolveMapPath(string? explicitPath, string input, string? output)
    {
        if (explicitPath is not null)
        {
            return Path.GetFullPath(explicitPath);
        }

        if (input == "-")
        {
            string timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHHmmss'Z'", CultureInfo.InvariantCulture);
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), $"scrub-{timestamp}.map.json"));
        }

        string mapName = Path.GetFileNameWithoutExtension(input) + ".map.json";
        bool outputIsStdout = output is null or "-";
        string referenceDir = outputIsStdout
            ? Path.GetDirectoryName(Path.GetFullPath(input)) ?? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(output!)) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(referenceDir, mapName));
    }

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static InputKind ClassifyInput(string input)
    {
        if (input == "-")
        {
            return InputKind.Stdin;
        }

        if (Directory.Exists(input))
        {
            return InputKind.Directory;
        }

        if (File.Exists(input))
        {
            return InputKind.File;
        }

        return InputKind.NotFound;
    }

    private static bool ExceedsThreshold(string path, int thresholdMb)
    {
        if (thresholdMb <= 0)
        {
            return true;
        }

        try
        {
            long bytes = new FileInfo(path).Length;
            long thresholdBytes = (long)thresholdMb * 1024L * 1024L;
            return bytes >= thresholdBytes;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static TextReader OpenReader(string input, bool isStdin)
    {
        if (isStdin)
        {
            return new StreamReader(Console.OpenStandardInput(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        }

        FileStream stream = new(input, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    }

    private static void EnsureParentDirectory(string path)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static IReadOnlyList<string>? ParseGlobList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return [.. raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private static IReadOnlyDictionary<DetectionType, int> CountByType(IReadOnlyList<Detection> detections)
    {
        Dictionary<DetectionType, int> counts = [];
        foreach (Detection detection in detections)
        {
            counts[detection.Type] = counts.GetValueOrDefault(detection.Type, 0) + 1;
        }
        return counts;
    }

    private static void EmitReport(RunOptions opts, ReportBuilder reportBuilder)
    {
        if (opts.Quiet)
        {
            return;
        }

        if (!opts.Report && !opts.DryRun)
        {
            return;
        }

        Report report = reportBuilder.Build();
        if (opts.JsonLogs)
        {
            JsonReportFormatter.Write(report, Console.Error);
        }
        else
        {
            HumanReportFormatter.Write(report, Console.Error);
        }
    }

    private enum InputKind
    {
        Stdin,
        File,
        Directory,
        NotFound,
    }

    private sealed record RunOptions
    {
        public required string Input { get; init; }
        public required string? Output { get; init; }
        public required bool NoNer { get; init; }
        public required string? ModelPath { get; init; }
        public required bool Reversible { get; init; }
        public required string? MapFile { get; init; }
        public required string? ConfigPath { get; init; }
        public required bool Verbose { get; init; }
        public required bool Recursive { get; init; }
        public required string? Include { get; init; }
        public required string? Exclude { get; init; }
        public required bool DryRun { get; init; }
        public required bool Report { get; init; }
        public required bool JsonLogs { get; init; }
        public required bool Quiet { get; init; }
        public required int StreamThresholdMb { get; init; }
    }

    /// <summary>
    ///     Streaming-mode pipeline that reproduces the same disable / merge /
    ///     allow-list semantics as <see cref="ResolveDetections"/> but works on
    ///     a per-chunk basis. Each chunk is scanned by every detector in
    ///     <see cref="_detectors"/>, results are merged, disabled rules are
    ///     dropped, and the survivors flow back to <see cref="StreamingScrubber"/>.
    /// </summary>
    private sealed class ChainedDetector(IReadOnlyList<IDetector> detectors, ResolvedScrubConfig resolvedConfig) : IDetector
    {
        private readonly IReadOnlyList<IDetector> _detectors = detectors;
        private readonly ResolvedScrubConfig _resolvedConfig = resolvedConfig;

        public IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx)
        {
            foreach (IDetector detector in _detectors)
            {
                foreach (Detection detection in detector.Detect(input, ctx))
                {
                    if (_resolvedConfig.Config.Rules.Disabled.Count != 0 && _resolvedConfig.IsDisabled(detection))
                    {
                        continue;
                    }
                    yield return detection;
                }
            }
        }
    }
}
