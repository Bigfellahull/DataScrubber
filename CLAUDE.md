# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DataScrubber is a local CLI (`scrub`) that strips personally identifiable information (PII) and security
secrets from text data so it can safely be pasted into an LLM for analysis. **All processing is local** —
the shipped binary makes no network calls at runtime, and this is enforced at build time (see "Privacy
posture" below).

The CLI supports two passes of detection:

1. **Rule-based detectors** (always available) — emails, IPv4/IPv6, MAC, phone, credit card, URL, API
   keys, password assignments, user paths.
2. **NER pass** (optional, requires a user-supplied ONNX model) — detects `Person`, `Organization`,
   `Location` mentions. Pass `--no-ner` to skip; otherwise the CLI exits `4` if the model is missing.

Output modes: one-way replacement (default) or `--reversible` (writes a `.map.json` next to the output).
A `rehydrate` subcommand reverses a `.map.json` back to the original text.

For a feature overview, see `README.md`.

## Project Structure

```
src/DataScrubber.Core/      Detection, replacement, mapping, configuration, NER (library, no-network).
  Detection/                IDetector + RuleBasedDetector + per-rule detectors under Rules/.
    Rules/                  Email, IPv4, IPv6, Mac, Phone, CreditCard, Url, ApiKey, PasswordAssignment, UserPath.
    Ner/                    ONNX runtime, BERT WordPiece tokenizer, BIO span reconstruction, label map.
  Replacement/              IReplacer with OneWayReplacer and ReversibleReplacer.
  Mapping/                  .map.json schema, reader, writer, rehydrator, token format.
  Configuration/            ScrubConfig load/resolve, custom regex rules, NER thresholds.

src/DataScrubber.Cli/       Command dispatch, file I/O, streaming, reporting (no-network).
  Program.cs                Entry point.
  RunCommand.cs             The `scrub` verb: single-file or directory mode.
  RehydrateCommand.cs       The `rehydrate` verb: reverses .map.json back to original text.
  DirectoryWalker.cs        Recursive walk with --include / --exclude globs.
  StreamingScrubber.cs      Bounded sliding-window streaming for stdin and large files.
  ExitCodes.cs              0=success, 1=generic, 2=invalid args, 3=input not found, 4=NER model load failed.
  Reporting/                Per-type detection summary (HumanReportFormatter, JsonReportFormatter).

tests/DataScrubber.Test/    xUnit + FluentAssertions + Verify.Xunit + Xunit.SkippableFact.
  Cli/                      End-to-end CLI tests, including config, dry-run, directory mode, streaming, reversible.
  Detection/, Replacement/, Mapping/, Configuration/, Build/   Per-feature unit tests.

build/EnforceNoNetwork.targets   MSBuild gate that blocks System.Net.* in shipped assemblies (DSP0001-DSP0004).
Directory.Build.props            net10.0, nullable, implicit usings, warnings-as-errors, embedded debug.
global.json                      Pins SDK 10.0.100 (rollForward latestFeature).
samples/scrub.config.json        Example config: disabled rules, custom regex, dictionaries, allowList, NER thresholds.
models/                          Optional NER assets (ner.onnx, tokenizer.json, labels.json). Not bundled in releases.
scripts/release.sh               Local + CI release builder (validation gate + per-RID self-contained publish + SHA-256 manifest).
```

## Build, Test, and Run Commands

```sh
dotnet restore
dotnet build -c Release                                       # also runs EnforceNoNetwork (DSP0001-DSP0004)
dotnet test --filter "Category!=Integration" -c Release       # standard unit suite
dotnet test -c Release                                        # all tests; integration requires a real NER model
dotnet format --verify-no-changes                             # formatting + analyzer style check

# Run the CLI directly during development:
echo "Email alice@example.com from 10.0.1.5" | \
  dotnet run --project src/DataScrubber.Cli -- - --no-ner

# Release build for all RIDs:
scripts/release.sh 1.0.0
```

### Test gating

- `Category=Integration` — requires a real NER model. Set `DATASCRUBBER_NER_MODEL=<dir>` or place
  `ner.onnx` / `tokenizer.json` / `labels.json` under `<repo>/models/` to run them.
- The 1 GB stdin streaming test is gated behind `DATASCRUBBER_RUN_LARGE_STREAM=1` so the suite stays fast.
- Tests use **xUnit, FluentAssertions, Verify.Xunit, Xunit.SkippableFact**. Name files after the unit
  under test (e.g. `EmailDetectorTests.cs`, `DryRunTests.cs`) and place them in the matching feature folder.

## Privacy Posture (no-network)

`DataScrubber.Core` and `DataScrubber.Cli` are forbidden from referencing `System.Net.Http`,
`System.Net.Sockets`, `System.Net.WebSockets`, or `System.Net.NameResolution`. The `EnforceNoNetwork`
target in `build/EnforceNoNetwork.targets` enforces this at build time via three checks:

1. **DSP0001 / DSP0002** — package-graph scan of `@(ReferencePath)` and `@(ReferenceCopyLocalPaths)`.
2. **DSP0003** — IL `AssemblyRef` scan against the compiled `*.dll` (catches direct `new HttpClient()`).
3. **DSP0004** — IL `AssemblyRef` scan against every NuGet-package-supplied reference assembly
   (catches third-party packages that pull in networking transitively).

Framework runtime packs (`Microsoft.NETCore.App*`, `runtime.*`, etc.) and `Microsoft.ML.OnnxRuntime`
are exempt. Test fixtures and sample/scratch projects are exempt — only the two shipping assemblies enforce
the rule. **Do not add networking dependencies to Core or Cli.** New native dependencies require an
explicit allow-list update; the friction is intentional.

## NER Model Contract

The CLI loads the model from `<exe-dir>/models/` by default (`--model <path>` overrides). The expected
files are:

| File             | Purpose                                                                      |
| ---------------- | ---------------------------------------------------------------------------- |
| `ner.onnx`       | ONNX token-classification model with inputs `input_ids`, `attention_mask`.   |
| `tokenizer.json` | HuggingFace-style WordPiece (BERT-family) tokenizer config.                  |
| `labels.json`    | JSON array (`["O","B-PER",…]`) or indexed object (`{"0":"O",…}`).            |

Recognised labels: `B-PER` / `I-PER` (or long-form `B-PERSON`), `B-ORG` / `I-ORG`, `B-LOC` / `I-LOC`.
Anything else collapses to `O`. The detector is `OnnxNerDetector` and uses `BioSpanReconstructor` to
turn token-level BIO predictions into character spans.

If the model is missing and `--no-ner` is not set, the CLI exits with code `4` and names the missing path.

## Exit Codes

Defined in `src/DataScrubber.Cli/ExitCodes.cs`:

| Code | Meaning                                                                           |
| ---- | --------------------------------------------------------------------------------- |
| `0`  | Success.                                                                          |
| `1`  | Generic runtime error (e.g. unexpected I/O failure).                              |
| `2`  | Invalid arguments / invalid configuration / unsupported feature flag.             |
| `3`  | Input file could not be found or read.                                            |
| `4`  | NER model, tokenizer, or label map could not be located, opened, or parsed.       |

When extending the CLI, reuse these constants — do not introduce ad-hoc exit numbers.

## Reversible Mode and Mapping Files

When run with `--reversible`, the CLI writes a `.map.json` next to the output. The file is created with
mode `0600` on POSIX and a stderr warning is printed naming its absolute path. **Treat `.map.json` files
as raw PII** — they contain the original entities and are intentionally not encrypted in v1. In directory
mode, each input file gets its own per-file `.map.json`; `--map-file` is rejected because it would conflate
mappings.

## Code Style

- Keep inline comments minimal and reserved for genuinely complex code. Only explain the **why**, never the **what** —
  the code itself should be readable enough. If the logic is straightforward, no comment is needed.
- Prefer explicit types over `var`. Only use `var` when the type is redundant (e.g. `new Foo()`) or genuinely unwieldy.
- Add XML documentation comments (`/// <summary>`) to all public methods and types. Indent the inner lines with four
  spaces after `///` so they align visually:
  ```csharp
  /// <summary>
  ///     Comment should be indented.
  ///     All lines.
  /// </summary>
  ```

### Naming Conventions

Enforced by `.editorconfig` (warnings):

- Private instance fields and private `static readonly` fields use `_camelCase`.
- Private `const` fields use `PascalCase`.
- File-scoped namespaces are required (`csharp_style_namespace_declarations = file_scoped:error`).
- Indentation: 4 spaces for C#, 2 spaces for JSON / YAML. LF endings, final newline, trimmed trailing whitespace.

### Modern C# Syntax

Prefer modern C# language features over older equivalents. Target .NET 10 / C# 13+.

- **File-scoped namespaces** — always use `namespace Foo.Bar;` over block-scoped.
  ```csharp
  namespace DataScrubber.Core.Detection;

  public class RuleBasedDetector { }
  ```

- **Primary constructors** — use for classes/structs whose constructor just captures dependencies or values.
  ```csharp
  public class AuditService(ApplicationDbContext db, ILogger<AuditService> logger)
  {
      public Task LogAsync(string action) => db.AuditEntries.AddAsync(new(action, logger.Name)).AsTask();
  }
  ```

- **Target-typed `new()`** — when the type is already stated on the left.
  ```csharp
  List<Detection> detections = new();
  Dictionary<string, int> counts = new() { ["a"] = 1 };
  ```

- **Collection expressions** (`[...]`) — preferred over `new List<T> { ... }`, `Array.Empty<T>()`, or `new[] { ... }`.
  ```csharp
  int[] ids = [1, 2, 3];
  List<string> names = [first, ..rest, last];
  ReadOnlySpan<byte> header = [0xFF, 0xD8];
  ```

- **Switch expressions** — prefer over switch statements and if/else chains when producing a value.
  ```csharp
  string label = status switch
  {
      TaskStatus.Running  => "In progress",
      TaskStatus.Complete => "Done",
      TaskStatus.Failed   => "Error",
      _                   => "Unknown",
  };
  ```

- **Pattern matching** — prefer patterns over manual type checks, null checks, and nested conditionals. Combine
  freely with `is`, `switch` expressions, and `switch` statements.

  - *Type patterns* — replace `as`/cast chains.
    ```csharp
    if (entry is Detection detection)
    {
        logger.LogInformation("Detected {Type}", detection.Type);
    }
    ```

  - *Property patterns* — match on shape; nest and use extended property access (`Roles.Count`).
    ```csharp
    if (user is { IsActive: true, Roles.Count: > 0 })
    {
        Authorize(user);
    }
    ```

  - *Relational and logical patterns* (`<`, `>=`, `and`, `or`, `not`) — clearer than chained comparisons.
    ```csharp
    bool isPrintable = ch is >= ' ' and <= '~';
    bool isVowel     = ch is 'a' or 'e' or 'i' or 'o' or 'u';
    if (response is not null and { StatusCode: >= 200 and < 300 }) { /* ... */ }
    ```

  - *List patterns* — match sequences with `[..]`, slice patterns, and discards.
    ```csharp
    string verdict = segments switch
    {
        []                        => "empty",
        [var only]                => $"single:{only}",
        [var first, .., var last] => $"{first}..{last}",
        [_, _, ..]                => "two or more",
    };
    ```

  - *Switch expression with combined patterns* — type + property + `when` guards in one place.
    ```csharp
    decimal fee = payment switch
    {
        Card    { Network: "amex" }           => 0.035m,
        Card    { Network: var n } when n is "visa" or "mc" => 0.029m,
        Bank    { IsInternational: true }     => 0.015m,
        Cash                                  => 0m,
        null                                  => throw new ArgumentNullException(nameof(payment)),
        _                                     => 0.025m,
    };
    ```

- **Raw string literals** (`"""`) — for JSON, SQL, XML, XSLT, and any multi-line text with quotes or backslashes.
  ```csharp
  string sql = """
      SELECT Id, Name
      FROM   Documents
      WHERE  TenantId = @tenantId
      """;
  ```

- **Record structs / records** — use `record` for immutable DTOs and value-like types; use `record struct` when the
  type is small and copy-by-value semantics are appropriate.
  ```csharp
  public record Detection(int Start, int Length, DetectionType Type, string Original);
  public readonly record struct Rgb(byte R, byte G, byte B);
  ```

- **Global and implicit usings** — rely on them; do not re-add usings that the SDK already imports.
- **`required` members** — prefer over constructor boilerplate when a property must be set at initialization.

## Pre-commit gate

If this session produced code changes, you must complete this workflow before either:
- giving the final “completed work” summary to the user, or
- staging/committing changes, if the user explicitly requested a commit.

Progress updates are allowed before this workflow is complete, but you must not claim the work is finished until the workflow has passed.

### Required Review Passes

Run both review passes independently. Do not substitute one for the other.

1. **Simplification pass**
   Run `/simplify` against the recent changes.

   Check for:
   - unnecessary complexity
   - duplicated logic
   - missed reuse opportunities
   - hacky or brittle patterns
   - avoidable inefficiencies

   For every finding:
   - fix it, or
   - explicitly mark it as a false positive with a short reason.

2. **PR-style review pass**
   Dispatch review agents across the change, or if review agents are unavailable, perform the same review manually.

   The review must cover:
   - correctness
   - project conventions
   - performance
   - tests
   - security (especially: any change that touches mapping files, model loading, or could introduce a networking dependency)

   For every finding:
   - fix it, or
   - explicitly mark it as a false positive with a short reason.

### Validation

After addressing the review findings:
- run `dotnet build -c Release` (this exercises the EnforceNoNetwork DSP0001-DSP0004 gate);
- run `dotnet test --filter "Category!=Integration" -c Release` for the standard suite;
- run `dotnet format --verify-no-changes` to confirm formatting and analyzer style;
- if changes touch the NER pipeline and a model is available locally, run the integration suite as well;
- if any validation step is skipped, explain why;
- if validation fails, either fix the issue or clearly report the failure.

### Final Summary Requirements

Only after the above workflow is complete may you provide the final summary.

The final summary must include:
- what changed
- which review passes were run
- any findings fixed
- any findings intentionally not fixed, with reasons
- validation performed and results

Do not stage or commit changes unless the user explicitly requested a commit.

## Commit Guidelines

When creating commits, always structure the commit message as follows:

### Format:

```
Brief title summarizing the change

**Context and Reasoning:**
Explain WHY the changes were made - what problem was being solved, what
requirement was being met, or what improvement was being implemented.
This should be 2-3 sentences providing business context.

**Technical Changes:**
- List specific files/components that were modified
- Describe what each change accomplishes
- Include any new features, fixes, or architectural changes
- Note any breaking changes or migration requirements

```

### Examples:

**Good commit message:**

```
Add IPv6 detector with zone-id and bracketed-form support

**Context and Reasoning:**
M2's rule-based detector set was missing IPv6, which left a class of network
addresses unscrubbed in real-world log samples. This closes the gap so the
default rules-only run is safe to point at production logs.

**Technical Changes:**
- Added IPv6Detector under DataScrubber.Core/Detection/Rules with full RFC 4291 coverage including zone IDs.
- Wired it into RuleBasedDetector with a stable priority below IPv4.
- Added IPv6DetectorTests with bracketed-URL, zone-id, and false-positive cases.
- Updated the report formatter to surface the new IPv6 type column.
```

**Avoid vague messages like:**

- "Fix bugs"
- "Update components"
- "Refactor code"

Always ask for clarification on the business context if you're unsure why changes are being made.
