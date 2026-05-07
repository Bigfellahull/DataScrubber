namespace DataScrubber.Test.Detection.Rules;

using DataScrubber.Detection;
using DataScrubber.Detection.Rules;
using FluentAssertions;
using Xunit;

public class UserPathDetectorTests
{
    private static IReadOnlyList<Detection> Detect(string input)
        => new UserPathDetector().Detect(input.AsMemory(), DetectionContext.Empty).ToList();

    [Fact]
    public void DetectsPosixUsernameSegment()
    {
        const string text = "/Users/sampleuser/projects/x/log.txt";
        IReadOnlyList<Detection> detections = Detect(text);
        detections.Should().ContainSingle();
        Detection d = detections[0];
        text.Substring(d.Start, d.Length).Should().Be("sampleuser");
        d.SourceRule.Should().Be("userpath.posix");
    }

    [Fact]
    public void DetectsHomeUsernameSegment()
    {
        IReadOnlyList<Detection> detections = Detect("/home/alice/.config");
        detections.Should().ContainSingle();
    }

    [Theory]
    [InlineData("/etc/hosts")]
    [InlineData("/var/log/syslog")]
    public void IgnoresNonUserPaths(string text)
    {
        Detect(text).Should().BeEmpty();
    }

    [Fact]
    public void DetectsWindowsUsernameWithSpace()
    {
        const string text = @"C:\Users\Sample User\AppData\Roaming\thing";
        IReadOnlyList<Detection> detections = Detect(text);
        detections.Should().ContainSingle();
        Detection d = detections[0];
        text.Substring(d.Start, d.Length).Should().Be("Sample User");
        d.SourceRule.Should().Be("userpath.windows");
    }

    [Fact]
    public void RequiresBoundaryBeforePath()
    {
        Detect("xyz/Users/sampleuser/file.txt").Should().BeEmpty();
    }
}
