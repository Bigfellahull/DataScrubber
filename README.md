# DataScrubber

A local CLI that strips personally identifiable information (PII) and security
secrets from text data so it can safely be pasted into an LLM for analysis.
All processing is local; the binary makes no network calls at runtime.

## Features

- **Rule-based detection** for emails, IPv4/IPv6, URLs, phone numbers,
  Luhn-validated credit cards, well-known API key patterns (AWS, GitHub,
  Stripe, Slack, JWT) plus a high-entropy fallback, password-style
  assignments, user-home path segments, and MAC addresses.
- **NER-based detection** (optional) for `Person`, `Organization`, and
  `Location` mentions via a local ONNX token-classification model.
- **One-way redaction** by default; **reversible mode** writes a per-run
  mapping file so you can rehydrate an LLM response back to the originals.
- **Directory mode** mirrors an input tree under an output directory with
  glob include/exclude filters.
- **Streaming pipeline** for large files and stdin so memory stays bounded.
- **Configurable** via JSON: custom regex rules, dictionaries, allow-list,
  and per-type NER thresholds.
- **Reporting**: human-readable or JSON-line per-type summaries, with
  `--dry-run` and `--quiet` modes.
- **Build-time privacy gate** that fails the build if any forbidden
  networking assembly creeps into the dependency graph.

## Quick start

```sh
# --no-ner is required until you supply a model (see "NER model" below).
echo "Email alice@example.com from 10.0.1.5" | dotnet run --project src/DataScrubber.Cli -- - --no-ner
# → Email [EMAIL] from [IPV4]
```

Without `--no-ner` the CLI will exit `4` and direct you to supply a model.

## Install

Pre-built self-contained binaries for `osx-arm64`, `osx-x64`, `linux-x64`,
`linux-arm64`, and `win-x64` are attached to each
[GitHub Release](https://github.com/Bigfellahull/DataScrubber/releases).
Download the archive for your platform, unpack, and run `./scrub --help`.
Each release also ships a `SHA256SUMS.txt` you can verify against.

To build from source:

```sh
dotnet build -c Release
dotnet publish src/DataScrubber.Cli -c Release -r <RID> --self-contained
```

## NER model

The named-entity recogniser detects `Person`, `Organization`, and `Location`
mentions using a local ONNX token-classification model. The CLI loads the
model from `<exe-dir>/models/ner.onnx` by default; pass `--model <path>` to
override.

**The CLI ships with no bundled model.** Place the following files in
`<exe-dir>/models/` (or any directory pointed at by `--model`):

| File             | Purpose                                                                       |
| ---------------- | ----------------------------------------------------------------------------- |
| `ner.onnx`       | ONNX token-classification model with inputs `input_ids`, `attention_mask`.    |
| `tokenizer.json` | HuggingFace-style tokenizer config. WordPiece (BERT-family) is supported.     |
| `labels.json`    | Either a JSON array (`["O","B-PER",…]`) or an indexed object (`{"0":"O",…}`). |

Recognised label names are `B-PER` / `I-PER` (or long-form `B-PERSON`, etc.),
`B-ORG` / `I-ORG`, and `B-LOC` / `I-LOC`. Other labels collapse to `O`.

Default confidence thresholds: `0.85` for `Person` and `Organization`, `0.80`
for `Location`. Override per-type via the config file (see "Configuration").

If the model files are missing and `--no-ner` is not set, the CLI exits with
code `4` and names the missing path.

## Reversible mode and the mapping file

When run with `--reversible`, the CLI writes a `.map.json` next to the output
containing the original entities. Identical originals share a numbered token
(`[PERSON_001]`, `[PERSON_002]`, …) so an LLM response that preserves the
tokens can be rehydrated back to the originals via:

```sh
scrub rehydrate response.txt --map input.map.json -o final.txt
```

The mapping file is created with mode `0600` on POSIX, and a stderr warning
is printed naming its absolute path. Treat the file as raw PII; it is
intentionally not encrypted.

## Directory mode

When the input path is a directory, `scrub` walks the tree and mirrors the
output under `--output`. The walker uses `--include` / `--exclude` globs and
defaults to `**/*.txt,**/*.log,**/*.md`.

```sh
scrub ./logs -o ./scrubbed --recursive --include "**/*.log" --no-ner
```

- Directory mode requires an explicit `--output` directory; `-o -` is rejected.
- The output path must not equal or contain the input path.
- Symbolic links are not followed.
- Per-file read or write failures emit a stderr line of the form
  `Warning: <path>: <message>` and continue with the next file. The exit
  code is `0` if any file succeeded, `1` if every file failed, and `0` for
  an empty input tree. `--quiet` suppresses these warnings.
- `--reversible` works in directory mode: each file gets its own
  `.map.json` next to its mirrored output. `--map-file` is rejected because
  it would conflate per-file mappings.

## Streaming for large files

Files at or above `--stream-threshold-mb` (default `50`, override with
`--stream-threshold-mb <n>`) and `-` (stdin) are processed via a bounded
sliding window. The carry buffer between iterations is capped at 4 KB; an
entity that spans more than 4 KB is documented as missed cleanly.
Reversible mode always reads the input fully so it is exempt from streaming.

```sh
cat huge.log | scrub - --no-ner
```

## Reporting, dry-run, quiet, and JSON logs

- `--report` always emits a per-type detection summary on stderr after
  processing. The default is human-readable, aligned columns; every
  detection type appears (with `0` when absent) so users can distinguish
  "found none" from "skipped".
- `--dry-run` skips writes (output files and mapping files) and always emits
  the report (subject to `--quiet`). Useful for tuning thresholds before
  committing to a write.
- `--json-logs` switches both logs and the report to JSON-line output. The
  report becomes one `{"event":"file","path":...}` per file followed by one
  final `{"event":"summary","files":N,"totalDetections":M,"durationMs":...}`.
- `--quiet` suppresses non-error stderr output, including the report.
  Errors still surface so a non-zero exit reason is visible.

## Configuration

`scrub.config.json` lets you tune detection without recompiling. Resolution
order: explicit `--config <path>` > `./scrub.config.json` >
`$XDG_CONFIG_HOME/datascrubber/config.json` (Linux/macOS) or
`%APPDATA%\DataScrubber\config.json` (Windows) > built-in defaults. First
match wins; layers are not merged.

```json
{
  "schemaVersion": 1,
  "rules": {
    "disabled": ["MacAddress"],
    "custom": [
      { "id": "ProjectCodename", "type": "Organization", "pattern": "(?i)\\bproject[-_ ]?(orion|atlas|nimbus)\\b" }
    ]
  },
  "dictionaries": {
    "Organization": ["Acme Corp", "Initech"]
  },
  "allowList": ["AcmeOpenSource", "noreply@example.com"],
  "ner": {
    "thresholds": { "Person": 0.85, "Organization": 0.85, "Location": 0.80 }
  }
}
```

Allow-list semantics: exact case-sensitive match against a detection's
original text. Invalid JSON, unknown top-level keys, or unknown
`DetectionType` references cause exit code `2` with a precise error.

## Privacy posture (no network)

`DataScrubber.Cli` and `DataScrubber.Core` are forbidden from referencing the
following assemblies, enforced by the `EnforceNoNetwork` MSBuild target in
`build/EnforceNoNetwork.targets`:

- `System.Net.Http`
- `System.Net.Sockets`
- `System.Net.WebSockets`
- `System.Net.NameResolution`

The target runs three complementary checks:

1. **Package-graph scan** (`DSP0001` / `DSP0002`) — fails the build if any
   NuGet package contributes a forbidden assembly file via
   `@(ReferencePath)` or `@(ReferenceCopyLocalPaths)`. Framework runtime
   packages (`Microsoft.NETCore.App*`, `runtime.*`, etc.) and
   `Microsoft.ML.OnnxRuntime` are exempt by design.
2. **Output-assembly IL AssemblyRef scan** (`DSP0003`) — opens the compiled
   `DataScrubber.Cli.dll` / `DataScrubber.Core.dll` and inspects the
   AssemblyReference table. If the source code calls into any forbidden BCL
   type (e.g. `new HttpClient()`), the C# compiler emits an `AssemblyRef`
   to `System.Net.Http` and this scan fires. This catches direct framework
   usage that the package-graph scan cannot — `System.Net.Http` ships with
   the framework, not in any NuGet package on net10.0.
3. **Package-assembly IL AssemblyRef scan** (`DSP0004`) — for every
   NuGet-package-supplied reference (modulo the framework / OnnxRuntime
   allow-list) it inspects that DLL's own AssemblyReference table. If a
   third-party package's own assembly references a forbidden BCL networking
   assembly, the build fails before the binary ships.

The check is build-time only; there is no runtime sandbox. New native
dependencies require an explicit allow-list update, which is the intended
friction.

## Exit codes

| Code | Meaning                                                  |
| ---- | -------------------------------------------------------- |
| `0`  | Success.                                                 |
| `1`  | Generic runtime error (e.g. write failure).              |
| `2`  | Invalid arguments, configuration, or mapping schema.     |
| `3`  | Input file not found or unreadable.                      |
| `4`  | NER model not found or failed to load.                   |

## Building and testing

```sh
dotnet build -c Release
dotnet test --filter "Category!=Integration"
```

`Category=Integration` tests require a real model and are gated on the
`DATASCRUBBER_NER_MODEL` environment variable pointing at a directory with
`ner.onnx`, `tokenizer.json`, and `labels.json`. The 1 GB stdin streaming
test is gated separately by `DATASCRUBBER_RUN_LARGE_STREAM=1` so the suite
stays fast on dev machines.

## Cutting a release

The release workflow runs on a `v*.*.*` tag push (or via the GitHub Actions
"Release" workflow `workflow_dispatch`):

```sh
git tag v1.2.3
git push origin v1.2.3
```

That triggers `scripts/release.sh`, which runs the full validation gate
(build, unit tests, integration tests if a model is present, format check),
publishes self-contained single-file binaries for every supported RID,
packs them, computes a SHA-256 manifest, and attaches the artifacts to a
GitHub Release.

## License

MIT — see [LICENSE](LICENSE).
