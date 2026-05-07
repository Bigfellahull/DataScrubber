# DataScrubber NER model directory

This directory holds the user-supplied NER model artefacts. The CLI looks here
by default; override with `--model <path>` (the path may point at the
`ner.onnx` file directly or at the directory containing it).

## Required files

- `ner.onnx` — ONNX token-classification model. Inputs: `input_ids` and
  `attention_mask` (int64, shape `[1, seq_len]`); optionally
  `token_type_ids`. Output: `logits` with shape `[1, seq_len, num_labels]`.
- `tokenizer.json` — HuggingFace-style tokenizer config. The bundled tokenizer
  parses the `model.vocab` map plus `model.unk_token` and
  `model.continuing_subword_prefix`. WordPiece (BERT-family) is supported.
- `labels.json` — either a JSON array of label strings, or an object keyed by
  decimal indices. Labels follow the BIO scheme (`O`, `B-PER` / `I-PER`,
  `B-ORG` / `I-ORG`, `B-LOC` / `I-LOC`); long-form synonyms like `B-PERSON`
  are accepted. Anything else collapses to `O`.

## Example model

One compatible English model is
[`onnx-community/bert-base-NER-ONNX`](https://huggingface.co/onnx-community/bert-base-NER-ONNX).
It is a BERT WordPiece token-classification model with `PER`, `ORG`, and
`LOC` labels. The quantized ONNX export is much smaller than the full model.

From the repository root:

```sh
mkdir -p models

curl -L \
  -o models/ner.onnx \
  https://huggingface.co/onnx-community/bert-base-NER-ONNX/resolve/main/onnx/model_quantized.onnx

curl -L \
  -o models/tokenizer.json \
  https://huggingface.co/onnx-community/bert-base-NER-ONNX/resolve/main/tokenizer.json

cat > models/labels.json <<'JSON'
{
  "0": "O",
  "1": "B-MISC",
  "2": "I-MISC",
  "3": "B-PER",
  "4": "I-PER",
  "5": "B-ORG",
  "6": "I-ORG",
  "7": "B-LOC",
  "8": "I-LOC"
}
JSON
```

`MISC` labels are accepted but ignored by DataScrubber because the v1 NER
pipeline only emits `Person`, `Organization`, and `Location`.

Smoke-test the CLI:

```sh
echo "Sarah called Acme Corp from Berlin" | \
  dotnet run --project src/DataScrubber.Cli -- - --model models/ner.onnx
```

Run the model-gated integration tests:

```sh
DATASCRUBBER_NER_MODEL="$PWD/models" dotnet test --filter "Category=Integration"
```

Alternatively, export the variable for the current shell session:

```sh
export DATASCRUBBER_NER_MODEL="$PWD/models"
dotnet test --filter "Category=Integration"
```

## Failure modes

If any of the three files is missing or unparsable, the CLI exits with code
`4` and a stderr message naming the missing path. To run without NER, pass
`--no-ner`.
