#!/usr/bin/env bash
#
# release.sh — local + CI release builder for DataScrubber.
#
# Runs the validation gate (build, unit tests, optional integration and
# bounded-memory tests, format check), then produces a self-contained
# single-file binary per supported RID, packs each into a tarball or zip,
# and emits a SHA-256 manifest.
#
# Usage:
#   scripts/release.sh <version>
#
# Example:
#   scripts/release.sh 1.0.0
#   scripts/release.sh 1.0.0-rc.1
#
# Environment overrides:
#   RUN_LARGE_STREAM=1     run the env-gated 1 GB streaming AC2 test
#   DATASCRUBBER_NER_MODEL  override model dir for integration tests
#                          (defaults to <repo>/models when not set)
#   RIDS                   space-separated RID list (override default matrix)
#   SKIP_FORMAT=1          skip the dotnet format check
#
set -euo pipefail

if [[ "${1:-}" == "" ]]; then
    echo "usage: $0 <version>" >&2
    exit 2
fi
VERSION="$1"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$REPO_ROOT/release-artifacts"

# Default RID matrix. Override via RIDS env var.
DEFAULT_RIDS=(osx-arm64 osx-x64 linux-x64 linux-arm64 win-x64)
read -r -a RIDS <<<"${RIDS:-${DEFAULT_RIDS[@]}}"

cd "$REPO_ROOT"

heading() {
    printf '\n==> %s\n' "$1"
}

heading "DataScrubber release $VERSION"
dotnet --info | head -3

# ---------------------------------------------------------------------------
# Validation gate
# ---------------------------------------------------------------------------
heading "Build (Release) — runs EnforceNoNetwork (DSP0001-DSP0004)"
dotnet build -c Release

heading "Unit tests"
dotnet test --filter "Category!=Integration" -c Release --no-build

MODEL_DIR="${DATASCRUBBER_NER_MODEL:-$REPO_ROOT/models}"
if [[ -f "$MODEL_DIR/ner.onnx" && -f "$MODEL_DIR/tokenizer.json" && -f "$MODEL_DIR/labels.json" ]]; then
    heading "Integration tests (model: $MODEL_DIR)"
    DATASCRUBBER_NER_MODEL="$MODEL_DIR" \
        dotnet test --filter "Category=Integration" -c Release --no-build
else
    heading "Skipping integration tests"
    echo "  No model at $MODEL_DIR (ner.onnx/tokenizer.json/labels.json)."
    echo "  Set DATASCRUBBER_NER_MODEL or place files under <repo>/models/ to run them."
fi

if [[ "${RUN_LARGE_STREAM:-0}" == "1" ]]; then
    heading "1 GB streaming bounded-memory test"
    DATASCRUBBER_RUN_LARGE_STREAM=1 \
        dotnet test \
            --filter "FullyQualifiedName~Ac2_OneGbStdinProcessesWithBoundedRss" \
            -c Release --no-build
else
    heading "Skipping 1 GB streaming test"
    echo "  Set RUN_LARGE_STREAM=1 to enable."
fi

if [[ "${SKIP_FORMAT:-0}" != "1" ]]; then
    heading "Format check"
    dotnet format --verify-no-changes
else
    heading "Skipping format check (SKIP_FORMAT=1)"
fi

# ---------------------------------------------------------------------------
# Per-RID publish
# ---------------------------------------------------------------------------
rm -rf "$OUT"
mkdir -p "$OUT"

for RID in "${RIDS[@]}"; do
    BUNDLE_NAME="scrub-$VERSION-$RID"
    PUBLISH_DIR="$OUT/$BUNDLE_NAME"
    mkdir -p "$PUBLISH_DIR/models"

    heading "Publishing $RID -> $PUBLISH_DIR"
    dotnet publish src/DataScrubber.Cli \
        -c Release \
        -r "$RID" \
        --self-contained \
        -o "$PUBLISH_DIR" \
        -p:Version="$VERSION" \
        -p:AssemblyVersion="${VERSION%%-*}.0" \
        -p:FileVersion="${VERSION%%-*}.0" \
        -p:InformationalVersion="$VERSION" \
        --nologo \
        --verbosity minimal

    cp "$REPO_ROOT/README.md" "$PUBLISH_DIR/README.md"
    cat >"$PUBLISH_DIR/models/README.md" <<'MODEL_README'
The DataScrubber NER model is not bundled with the binary.

Place the following files in this directory (next to the scrub binary):

    ner.onnx        ONNX token-classification model with inputs
                    `input_ids` and `attention_mask`.
    tokenizer.json  HuggingFace-style tokenizer config (WordPiece /
                    BERT-family).
    labels.json     JSON array or indexed object mapping label IDs to
                    names like `B-PER`, `I-ORG`, `B-LOC`, `O`.

Or pass `--model <path>` to point at the files individually.

If the model is not found and `--no-ner` is not set, scrub exits with
code 4 and names the missing path.
MODEL_README

    # Smoke-test the binary when we are running on the matching RID.
    case "$(uname -ms)" in
        "Darwin arm64")  CURRENT_RID=osx-arm64 ;;
        "Darwin x86_64") CURRENT_RID=osx-x64 ;;
        "Linux x86_64")  CURRENT_RID=linux-x64 ;;
        "Linux aarch64") CURRENT_RID=linux-arm64 ;;
        *)               CURRENT_RID="" ;;
    esac
    if [[ "$RID" == "$CURRENT_RID" && -n "$CURRENT_RID" ]]; then
        heading "Smoke test ($RID --help)"
        "$PUBLISH_DIR/scrub" --help >/dev/null
    fi

    heading "Packaging $BUNDLE_NAME"
    if [[ "$RID" == win-* ]]; then
        if ! command -v zip >/dev/null 2>&1; then
            echo "error: 'zip' is required to package $RID artifacts" >&2
            exit 1
        fi
        ( cd "$OUT" && zip -qr "$BUNDLE_NAME.zip" "$BUNDLE_NAME" )
    else
        ( cd "$OUT" && tar -czf "$BUNDLE_NAME.tar.gz" "$BUNDLE_NAME" )
    fi
    rm -rf "$PUBLISH_DIR"
done

# ---------------------------------------------------------------------------
# SHA-256 manifest
# ---------------------------------------------------------------------------
heading "Computing SHA-256 manifest"
(
    cd "$OUT"
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum scrub-*.tar.gz scrub-*.zip 2>/dev/null | sort >SHA256SUMS.txt
    else
        shasum -a 256 scrub-*.tar.gz scrub-*.zip 2>/dev/null | sort >SHA256SUMS.txt
    fi
)

heading "Release $VERSION ready"
ls -lh "$OUT"
echo
echo "Manifest:"
cat "$OUT/SHA256SUMS.txt"
