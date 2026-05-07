# Repository Guidelines

## Project Structure & Module Organization

DataScrubber is a local .NET CLI (`scrub`) for removing PII and secrets. Source lives under `src/`: `DataScrubber.Core` contains detection, replacement, mapping, configuration, and NER logic; `DataScrubber.Cli` contains commands, file I/O, streaming, logging, and reports. Tests live in `tests/DataScrubber.Test`, grouped by feature (`Cli`, `Detection`, `Replacement`, `Mapping`, `Configuration`, `Build`). Optional NER assets are in `models/`, sample config is in `samples/scrub.config.json`, and the build no-network gate is in `build/EnforceNoNetwork.targets`.

## Build, Test, and Development Commands

- `dotnet restore` restores packages.
- `dotnet build -c Release --no-restore` builds all projects and runs EnforceNoNetwork.
- `dotnet test --filter "Category!=Integration" -c Release --no-build` runs the standard unit suite.
- `dotnet test -c Release` runs all discoverable tests; integration tests require `DATASCRUBBER_NER_MODEL` or files in `models/`.
- `dotnet format --verify-no-changes` verifies formatting and analyzer style.
- `dotnet run --project src/DataScrubber.Cli -- - --no-ner` runs the CLI against stdin in rules-only mode.
- `scripts/release.sh <version>` builds release artifacts and runs the release gate.

## Coding Style & Naming Conventions

Use C# on `net10.0` with nullable references, implicit usings, warnings as errors, and build-enforced style. Follow `.editorconfig`: 4-space C# indentation, 2-space JSON/YAML, LF endings, final newline, and trimmed trailing whitespace. Use file-scoped namespaces. Prefer explicit types except where redundant or unwieldy. Private instance and private static readonly fields use `_camelCase`; private constants use `PascalCase`. Keep comments minimal and explain why, not what. Add XML `/// <summary>` docs to public methods and types, with inner lines indented four spaces after `///`.

Prefer modern C#: primary constructors for dependency capture, target-typed `new()`, collection expressions, switch expressions, pattern matching, raw string literals for structured text, records for immutable DTOs, and `required` members over constructor boilerplate.

## Testing Guidelines

Tests use xUnit, FluentAssertions, Verify.Xunit, and Xunit.SkippableFact. Name test files after the unit or workflow under test, for example `EmailDetectorTests.cs` or `DryRunTests.cs`, and place them in the matching feature folder. Mark model-dependent tests with `Category=Integration`; large streaming coverage is gated by `DATASCRUBBER_RUN_LARGE_STREAM=1`.

## Pre-commit Gate

Before claiming code work is complete or staging a requested commit, complete both review passes. First run `/simplify` against recent changes and fix or document findings about complexity, duplication, missed reuse, brittle patterns, or inefficiency. Then perform a PR-style review covering correctness, project conventions, performance, tests, and security, especially mapping files, model loading, and networking dependencies.

After review, run `dotnet build -c Release`, `dotnet test --filter "Category!=Integration" -c Release`, and `dotnet format --verify-no-changes`. If NER code changed and a model is available, run integration tests too. Report any skipped or failed validation. Do not stage or commit unless explicitly asked.

## Commit & Pull Request Guidelines

Use this commit message structure:

```text
Brief title summarizing the change

**Context and Reasoning:**
Explain why the change was made in 2-3 sentences.

**Technical Changes:**
- List modified files/components and what each change accomplishes.
- Note new behavior, fixes, breaking changes, or migration needs.
```

Avoid vague subjects such as `Fix bugs`. Ask for business context if it is unclear. Pull requests should explain behavior changes, list validation run, link issues, and include CLI output examples for user-visible changes.

## Security & Configuration Tips

All processing must stay local. Do not add runtime networking dependencies to `DataScrubber.Core` or `DataScrubber.Cli`; EnforceNoNetwork rejects forbidden assemblies such as `System.Net.Http`, sockets, websockets, and name resolution. Treat reversible `.map.json` files as raw PII.
