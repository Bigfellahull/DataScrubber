namespace DataScrubber.Test.Detection;

using DataScrubber.Detection;
using FluentAssertions;
using Xunit;

public class AllowListTests
{
    [Fact]
    public void DropsExactMatch()
    {
        AllowListFilter filter = new(["noreply@example.com"]);
        const string input = "send to noreply@example.com please";
        Detection detection = new(8, "noreply@example.com".Length, DetectionType.Email, 1.0, "email.basic");

        filter.ShouldDrop(input, detection).Should().BeTrue();
    }

    [Fact]
    public void IsCaseSensitive()
    {
        AllowListFilter filter = new(["noreply@example.com"]);
        const string input = "send to NOREPLY@example.com please";
        Detection detection = new(8, "NOREPLY@example.com".Length, DetectionType.Email, 1.0, "email.basic");

        filter.ShouldDrop(input, detection).Should().BeFalse();
    }

    [Fact]
    public void DoesNotMatchSubstring()
    {
        // Allow-list entry "Sarah" should NOT drop the email containing it,
        // since the comparison is against the full detection span only.
        AllowListFilter filter = new(["Sarah"]);
        const string input = "Email Sarah at sarah@acme.com";
        Detection email = new(15, "sarah@acme.com".Length, DetectionType.Email, 1.0, "email.basic");

        filter.ShouldDrop(input, email).Should().BeFalse();
    }

    [Fact]
    public void EmptyFilterIsNoop()
    {
        AllowListFilter filter = AllowListFilter.Empty;
        Detection any = new(0, 5, DetectionType.Email, 1.0, "email.basic");

        filter.ShouldDrop("hello", any).Should().BeFalse();
        filter.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void FilterAppliesPostMerge()
    {
        AllowListFilter filter = new(["noreply@example.com"]);
        const string input = "noreply@example.com other@example.com";

        IReadOnlyList<Detection> merged =
        [
            new Detection(0, "noreply@example.com".Length, DetectionType.Email, 1.0, "email.basic"),
            new Detection(20, "other@example.com".Length, DetectionType.Email, 1.0, "email.basic"),
        ];

        IReadOnlyList<Detection> result = filter.Filter(input, merged);

        result.Should().ContainSingle();
        result[0].Start.Should().Be(20);
    }

    [Fact]
    public void NormalisesNfcAndNfdEntries()
    {
        // "café" — composed (NFC) vs. decomposed (NFD)
        const string composed = "café";
        const string decomposed = "café";

        AllowListFilter filter = new([decomposed]);
        Detection detection = new(0, composed.Length, DetectionType.Person, 1.0, "x");

        filter.ShouldDrop(composed, detection).Should().BeTrue();
    }

    [Fact]
    public void DetectionContextDelegatesToAllowList()
    {
        AllowListFilter filter = new(["x"]);
        DetectionContext ctx = new() { Input = "x", AllowList = filter };
        Detection detection = new(0, 1, DetectionType.Person, 1.0, "ner.onnx");

        ctx.ShouldDrop(detection).Should().BeTrue();
    }

    [Fact]
    public void DetectionContextWithoutAllowListReturnsFalse()
    {
        DetectionContext ctx = new() { Input = "anything" };
        Detection detection = new(0, 3, DetectionType.Person, 1.0, "ner.onnx");

        ctx.ShouldDrop(detection).Should().BeFalse();
    }
}
