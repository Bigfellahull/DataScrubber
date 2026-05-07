namespace DataScrubber.Mapping;

using System.Text;
using System.Text.Json;

/// <summary>
///     Writes a <see cref="MappingFile"/> to disk atomically with restrictive
///     POSIX permissions. The write goes to <c>&lt;path&gt;.tmp</c> first and
///     is then moved into place; on any exception the temporary file is
///     removed so a half-written map is never left behind.
/// </summary>
public static class MappingFileWriter
{
    /// <summary>
    ///     Serialises <paramref name="mapping"/> as JSON and writes it to
    ///     <paramref name="path"/>. On POSIX the file mode is set to
    ///     <c>0600</c>; on Windows the parent ACL is inherited.
    /// </summary>
    /// <param name="mapping">The mapping document to persist.</param>
    /// <param name="path">The destination path. The parent directory must exist.</param>
    public static void Write(MappingFile mapping, string path)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentException.ThrowIfNullOrEmpty(path);

        string fullPath = Path.GetFullPath(path);
        string tempPath = fullPath + ".tmp";
        string json = JsonSerializer.Serialize(mapping, MappingFileSerialization.Write);

        try
        {
            // UnixCreateMode applies the restrictive mode at creation time so
            // the file is never observable as world-readable, even briefly.
            // The setter is unsupported on Windows, so the platform check is
            // inlined where the analyzer can see it.
            FileStreamOptions options = new()
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (FileStream stream = new(tempPath, options))
            using (StreamWriter writer = new(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    // Best-effort cleanup; the caller surfaces the original write failure.
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
