namespace DataScrubber.Test.Mapping;

using DataScrubber.Mapping;
using FluentAssertions;
using Xunit;

public class PosixFileModeTests
{
    [Fact]
    public void MapFileWrittenWithUserOnlyMode()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsFreeBSD())
        {
            return;
        }

        MappingFile mapping = new(1, DateTimeOffset.UtcNow, "abc", []);
        string path = Path.Combine(Path.GetTempPath(), $"mode-{Guid.NewGuid():N}.json");

        try
        {
            MappingFileWriter.Write(mapping, path);

            UnixFileMode mode = File.GetUnixFileMode(path);
            mode.Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
