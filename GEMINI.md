# DataScrubber

A local CLI that strips personally identifiable information (PII) and security secrets from text data. This project prioritizes privacy and ensures no network calls are made at runtime.

## Project Overview

*   **Technology Stack:** .NET 10.0 (C# latest), Microsoft.ML.OnnxRuntime for NER.
*   **Core Architecture:**
    *   **DataScrubber.Core:** Contains the detection logic (`IDetector`), replacement engine (`IReplacer`), and configuration management.
    *   **DataScrubber.Cli:** A command-line interface built with `System.CommandLine`.
    *   **DataScrubber.Test:** XUnit test suite using `FluentAssertions`, `Verify.Xunit`, and `Xunit.SkippableFact`.
*   **Privacy Posture:** Enforced via `build/EnforceNoNetwork.targets`, which prevents the shipping assemblies from referencing networking-capable BCL assemblies (e.g., `System.Net.Http`).

## Building and Running

### Build
```sh
dotnet restore
dotnet build -c Release                                       # also runs EnforceNoNetwork (DSP0001-DSP0004)
```

### Test
```sh
# Run all tests except integration tests (which require a model)
dotnet test --filter "Category!=Integration" -c Release

# Run all tests (requires model files in ./models/)
dotnet test -c Release

# Check formatting and analyzer style
dotnet format --verify-no-changes
```

### Run (Development)
```sh
# Process stdin with NER disabled
echo "Email alice@example.com" | dotnet run --project src/DataScrubber.Cli -- - --no-ner
```

## Development Conventions

### Coding Style
*   **Nullability:** Enabled (`<Nullable>enable</Nullable>`). All code should be null-safe.
*   **Warnings:** Treated as errors.
*   **Comments:** Minimal. Explain the **why**, never the **what**.
*   **Types:** Prefer explicit types over `var` except where redundant (e.g., `new Foo()`) or unwieldy.
*   **Documentation:** XML comments (`/// <summary>`) for all public methods and types.
    ```csharp
    /// <summary>
    ///     Comment should be indented.
    ///     All lines.
    /// </summary>
    ```

### Naming Conventions
*   Private instance fields and private `static readonly` fields: `_camelCase`.
*   Private `const` fields: `PascalCase`.
*   File-scoped namespaces are required.

### Modern C# Syntax
Target .NET 10 / C# 13+ features:
*   **File-scoped namespaces:** `namespace Foo.Bar;`
*   **Primary constructors:** For simple dependency/value capture.
*   **Target-typed `new()`:** `List<Detection> detections = new();`
*   **Collection expressions:** `int[] ids = [1, 2, 3];`
*   **Switch expressions and Pattern matching:** Prefer over manual checks and if/else chains.
*   **Raw string literals:** `"""` for multi-line text/JSON.
*   **Records:** `public record Detection(...)` for immutable DTOs.

### Adding a New Detector
1.  Implement `IDetector` in `src/DataScrubber.Core/Detection/Rules/`.
2.  Add a corresponding test class in `tests/DataScrubber.Test/Detection/Rules/`.
3.  Register the detector in the configuration/registry.

## Privacy Compliance
*   **NEVER** add dependencies that pull in `System.Net.*`.
*   Enforced by `build/EnforceNoNetwork.targets` (DSP0001-DSP0004).

## Pre-commit Gate
Before completing any code change or committing:
1.  **Simplification Pass:** Check for unnecessary complexity or duplication.
2.  **PR Review Pass:** Manually review for correctness, conventions, performance, and security.
3.  **Validation:**
    *   `dotnet build -c Release`
    *   `dotnet test --filter "Category!=Integration" -c Release`
    *   `dotnet format --verify-no-changes`

## Commit Guidelines
Structure commit messages as follows:

```
Brief title summarizing the change

**Context and Reasoning:**
2-3 sentences explaining WHY the change was made (business context).

**Technical Changes:**
- List specific modifications and their purpose.
- Note any fixes, features, or architectural changes.
```

## Key Directories
*   `src/DataScrubber.Core/Detection/Rules`: Regex and logic-based PII detectors.
*   `src/DataScrubber.Core/Detection/Ner`: ONNX-based NER logic.
*   `src/DataScrubber.Core/Replacement`: Masking/replacement logic.
*   `build/`: Build-time enforcement targets.
*   `models/`: Optional user-supplied NER assets.
