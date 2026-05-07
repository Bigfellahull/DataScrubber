namespace DataScrubber.Test.Detection.Ner;

using DataScrubber.Detection.Ner;
using FluentAssertions;
using Xunit;

public class TokenizerTests
{
    private const string MinimalTokenizerJson = """
        {
          "model": {
            "type": "WordPiece",
            "vocab": {
              "[PAD]": 0,
              "[UNK]": 1,
              "[CLS]": 2,
              "[SEP]": 3,
              "Sarah": 4,
              "called": 5,
              "Acme": 6,
              "Co": 7,
              "##rp": 8,
              "from": 9,
              "Berlin": 10,
              "Bert": 11,
              "##in": 12,
              ".": 13,
              ",": 14,
              "@": 15,
              "John": 16,
              "Smith": 17
            },
            "unk_token": "[UNK]",
            "continuing_subword_prefix": "##"
          },
          "added_tokens": []
        }
        """;

    [Fact]
    public void TokenizesWhitespaceSeparatedWordsWithCorrectOffsets()
    {
        BertWordPieceTokenizer tokenizer = LoadTokenizer();
        TokenizedInput tokenized = tokenizer.Tokenize("Sarah called");

        tokenized.TokenIds.Should().Equal([4, 5]);
        tokenized.Offsets[0].Should().Be(new TokenSpan(0, 5));
        tokenized.Offsets[1].Should().Be(new TokenSpan(6, 6));
    }

    [Fact]
    public void GreedyWordPieceSplitsOutOfVocabWordsIntoContinuingSubwords()
    {
        BertWordPieceTokenizer tokenizer = LoadTokenizer();
        TokenizedInput tokenized = tokenizer.Tokenize("Corp");

        // "Corp" is not in vocab as a whole; greedy should match "Co" then "##rp".
        tokenized.TokenIds.Should().Equal([7, 8]);
        tokenized.Offsets[0].Should().Be(new TokenSpan(0, 2));
        tokenized.Offsets[1].Should().Be(new TokenSpan(2, 2));
    }

    [Fact]
    public void PunctuationIsItsOwnToken()
    {
        BertWordPieceTokenizer tokenizer = LoadTokenizer();
        TokenizedInput tokenized = tokenizer.Tokenize("Sarah, called.");

        tokenized.TokenIds.Should().Equal([4, 14, 5, 13]);
        tokenized.Offsets.Select(o => o.Start).Should().Equal(0, 5, 7, 13);
    }

    [Fact]
    public void UnknownWordEmitsSingleUnkTokenWithFullWordSpan()
    {
        BertWordPieceTokenizer tokenizer = LoadTokenizer();
        TokenizedInput tokenized = tokenizer.Tokenize("xxxxxxx");

        tokenized.TokenIds.Should().Equal([1]);
        tokenized.Offsets[0].Should().Be(new TokenSpan(0, 7));
    }

    [Fact]
    public void OffsetRoundTripReproducesOriginalSurface()
    {
        BertWordPieceTokenizer tokenizer = LoadTokenizer();
        const string source = "Sarah called Acme Corp.";
        TokenizedInput tokenized = tokenizer.Tokenize(source);

        foreach (TokenSpan offset in tokenized.Offsets)
        {
            string slice = source.Substring(offset.Start, offset.Length);
            slice.Should().NotBeEmpty();
        }

        // Concatenating offset slices and removing whitespace must equal the
        // input minus whitespace; this is the contract BioSpanReconstructor
        // relies on to restore character ranges.
        string concatenated = string.Concat(tokenized.Offsets.Select(o => source.Substring(o.Start, o.Length)));
        concatenated.Should().Be(source.Replace(" ", string.Empty));
    }

    [Fact]
    public void SpecialTokenIdsAreExposed()
    {
        BertWordPieceTokenizer tokenizer = LoadTokenizer();

        tokenizer.ClsTokenId.Should().Be(2);
        tokenizer.SepTokenId.Should().Be(3);
        tokenizer.PadTokenId.Should().Be(0);
    }

    [Fact]
    public void LoadOnMissingFileThrowsNerModelLoadException()
    {
        string nonexistent = Path.Combine(Path.GetTempPath(), $"tok-missing-{Guid.NewGuid():N}.json");
        FluentActions.Invoking(() => BertWordPieceTokenizer.Load(nonexistent))
            .Should().Throw<NerModelLoadException>()
            .Where(ex => ex.MissingPath == nonexistent);
    }

    [Fact]
    public void LoadOnMalformedFileThrowsNerModelLoadException()
    {
        AssertLoadFails("{not json");
    }

    [Fact]
    public void LoadOnNonIntegerVocabValueThrowsNerModelLoadException()
    {
        // GetInt32() throws InvalidOperationException on a non-number JSON
        // value; without the loader wrap, this would surface as exit 1.
        AssertLoadFails("""
            {
              "model": {
                "type": "WordPiece",
                "vocab": { "[UNK]": "not-an-integer", "[CLS]": 1, "[SEP]": 2, "[PAD]": 0 }
              }
            }
            """);
    }

    [Fact]
    public void LoadWithMissingClsTokenThrowsNerModelLoadException()
    {
        AssertLoadFails("""
            {
              "model": {
                "type": "WordPiece",
                "vocab": { "[UNK]": 1, "[SEP]": 2, "[PAD]": 0 }
              }
            }
            """);
    }

    [Fact]
    public void LoadWithMissingModelObjectThrowsNerModelLoadException()
    {
        AssertLoadFails("""{ "added_tokens": [] }""");
    }

    private static void AssertLoadFails(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"tok-bad-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        try
        {
            FluentActions.Invoking(() => BertWordPieceTokenizer.Load(path))
                .Should().Throw<NerModelLoadException>()
                .Where(ex => ex.MissingPath == path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static BertWordPieceTokenizer LoadTokenizer()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tok-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, MinimalTokenizerJson);
        try
        {
            return BertWordPieceTokenizer.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
