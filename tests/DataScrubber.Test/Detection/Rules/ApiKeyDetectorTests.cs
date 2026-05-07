namespace DataScrubber.Test.Detection.Rules;

using DataScrubber.Detection;
using DataScrubber.Detection.Rules;
using FluentAssertions;
using Xunit;

public class ApiKeyDetectorTests
{
    private static IReadOnlyList<Detection> Detect(string input)
        => new ApiKeyDetector().Detect(input.AsMemory(), DetectionContext.Empty).ToList();

    [Fact]
    public void DetectsAwsAccessKey()
    {
        IReadOnlyList<Detection> detections = Detect("AKIAIOSFODNN7EXAMPLE");
        detections.Should().ContainSingle();
        detections[0].SourceRule.Should().Be("apikey.aws");
    }

    [Fact]
    public void DetectsAwsSecretKeyInKnownContext()
    {
        const string text = "aws_secret_access_key = wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";
        IReadOnlyList<Detection> detections = Detect(text);
        Detection secretHit = detections.Single(d => d.SourceRule == "apikey.aws_secret");
        text.Substring(secretHit.Start, secretHit.Length).Should().Be("wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY");
    }

    [Fact]
    public void IgnoresFortyCharBase64WithoutAwsContext()
    {
        const string text = "filename: wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";
        Detect(text).Where(d => d.SourceRule == "apikey.aws_secret").Should().BeEmpty();
    }

    [Fact]
    public void DetectsJwt()
    {
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        IReadOnlyList<Detection> detections = Detect(jwt);
        detections.Should().NotBeEmpty();
        detections.Should().Contain(d => d.SourceRule == "apikey.jwt");
    }

    [Fact]
    public void DetectsStripeLiveKey()
    {
        IReadOnlyList<Detection> detections = Detect("sk_live_abcdef0123456789ABCDEF");
        detections.Should().Contain(d => d.SourceRule == "apikey.stripe");
    }

    [Fact]
    public void DetectsGitHubToken()
    {
        IReadOnlyList<Detection> detections = Detect("ghp_" + new string('a', 36));
        detections.Should().Contain(d => d.SourceRule == "apikey.github");
    }

    [Fact]
    public void DetectsSlackToken()
    {
        IReadOnlyList<Detection> detections = Detect("xoxb-1234567890-abcdefghij");
        detections.Should().Contain(d => d.SourceRule == "apikey.slack");
    }

    [Fact]
    public void EntropyFallbackFiresNearKeyword()
    {
        const string text = "secret=h7Q9zP2bN4mC6vK8eR1tY3wU5xZ0jL4Mq";
        IReadOnlyList<Detection> detections = Detect(text);
        detections.Should().Contain(d => d.SourceRule == "apikey.entropy");
    }

    [Fact]
    public void EntropyFallbackIgnoresLowEntropyToken()
    {
        const string text = "token=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        IReadOnlyList<Detection> entropyHits = Detect(text)
            .Where(d => d.SourceRule == "apikey.entropy")
            .ToList();
        entropyHits.Should().BeEmpty();
    }

    [Fact]
    public void EntropyFallbackIgnoresTokenWithNoNearbyKeyword()
    {
        const string text = "h7Q9zP2bN4mC6vK8eR1tY3wU5xZ0jL4Mq plain text and stuff";
        IReadOnlyList<Detection> entropyHits = Detect(text)
            .Where(d => d.SourceRule == "apikey.entropy")
            .ToList();
        entropyHits.Should().BeEmpty();
    }

    [Fact]
    public void EntropyFallbackConfidenceIsLower()
    {
        const string text = "secret=h7Q9zP2bN4mC6vK8eR1tY3wU5xZ0jL4Mq";
        IReadOnlyList<Detection> detections = Detect(text);
        detections.First(d => d.SourceRule == "apikey.entropy").Confidence.Should().BeApproximately(0.7, 0.0001);
    }
}
