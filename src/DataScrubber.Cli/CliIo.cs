namespace DataScrubber.Cli;

using System.Text;
using Microsoft.Extensions.Logging;

/// <summary>
///     Shared text I/O helpers for the <c>scrub</c> subcommands. Centralises
///     the UTF-8 BOM handling, stdin/stdout fall-throughs, and the mapping
///     from common file-system exceptions to <see cref="ExitCodes"/>, so each
///     command file stays focused on its own logic.
/// </summary>
internal static class CliIo
{
    /// <summary>
    ///     Reads <paramref name="input"/> as raw bytes plus the decoded text,
    ///     mapping any I/O failure to a logged exit code. The byte buffer is
    ///     used by callers that need to hash the on-disk content before
    ///     scrubbing.
    /// </summary>
    /// <param name="input">A file path or <c>-</c> for stdin.</param>
    /// <param name="logger">Logger for error diagnostics.</param>
    /// <param name="bytes">The raw input bytes (including any BOM).</param>
    /// <param name="text">The UTF-8 decoded text with the BOM stripped.</param>
    /// <returns>An exit code on failure, or <c>null</c> on success.</returns>
    public static int? TryReadInputBytes(string input, ILogger logger, out byte[] bytes, out string text)
    {
        bytes = [];
        text = string.Empty;
        try
        {
            (bytes, text) = ReadInputBytes(input);
            return null;
        }
        catch (FileNotFoundException ex)
        {
            logger.LogError("input not found: {Path}", ex.FileName ?? input);
            return ExitCodes.InputNotFound;
        }
        catch (DirectoryNotFoundException)
        {
            logger.LogError("input not found: {Path}", input);
            return ExitCodes.InputNotFound;
        }
        catch (UnauthorizedAccessException)
        {
            logger.LogError("input not readable: {Path}", input);
            return ExitCodes.InputNotFound;
        }
        catch (IOException ex)
        {
            logger.LogError("I/O error reading input: {Message}", ex.Message);
            return ExitCodes.GenericError;
        }
    }

    /// <summary>
    ///     Reads <paramref name="input"/> as decoded text, mapping any I/O
    ///     failure to a logged exit code.
    /// </summary>
    /// <param name="input">A file path or <c>-</c> for stdin.</param>
    /// <param name="logger">Logger for error diagnostics.</param>
    /// <param name="text">The UTF-8 decoded text with the BOM stripped.</param>
    /// <returns>An exit code on failure, or <c>null</c> on success.</returns>
    public static int? TryReadInputText(string input, ILogger logger, out string text)
    {
        text = string.Empty;
        try
        {
            text = ReadInputText(input);
            return null;
        }
        catch (FileNotFoundException ex)
        {
            logger.LogError("input not found: {Path}", ex.FileName ?? input);
            return ExitCodes.InputNotFound;
        }
        catch (DirectoryNotFoundException)
        {
            logger.LogError("input not found: {Path}", input);
            return ExitCodes.InputNotFound;
        }
        catch (UnauthorizedAccessException)
        {
            logger.LogError("input not readable: {Path}", input);
            return ExitCodes.InputNotFound;
        }
        catch (IOException ex)
        {
            logger.LogError("I/O error reading input: {Message}", ex.Message);
            return ExitCodes.GenericError;
        }
    }

    /// <summary>
    ///     Writes <paramref name="text"/> to <paramref name="output"/> (a file
    ///     path, or <c>-</c>/<c>null</c> for stdout), mapping any I/O failure
    ///     to a logged exit code.
    /// </summary>
    /// <param name="output">A file path, <c>-</c>, or <c>null</c> for stdout.</param>
    /// <param name="text">The text to emit.</param>
    /// <param name="logger">Logger for error diagnostics.</param>
    /// <returns>An exit code on failure, or <c>null</c> on success.</returns>
    public static int? TryWriteOutput(string? output, string text, ILogger logger)
    {
        try
        {
            WriteOutput(output, text);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            logger.LogError("I/O error writing output: {Message}", ex.Message);
            return ExitCodes.GenericError;
        }
    }

    private static (byte[] Bytes, string Text) ReadInputBytes(string input)
    {
        if (input == "-")
        {
            using MemoryStream buffer = new();
            using Stream stdin = Console.OpenStandardInput();
            stdin.CopyTo(buffer);
            byte[] bytes = buffer.ToArray();
            return (bytes, DecodeUtf8(bytes));
        }

        byte[] fileBytes = File.ReadAllBytes(input);
        return (fileBytes, DecodeUtf8(fileBytes));
    }

    private static string ReadInputText(string input)
    {
        if (input == "-")
        {
            using StreamReader reader = new(Console.OpenStandardInput(), new UTF8Encoding(false));
            return reader.ReadToEnd();
        }

        return File.ReadAllText(input, new UTF8Encoding(false));
    }

    /// <summary>
    ///     Decodes <paramref name="bytes"/> as UTF-8, stripping a leading BOM
    ///     when present. Exposed so other components (e.g. directory-mode
    ///     reversible processing) can hash the on-disk bytes and consume the
    ///     decoded text without going through the file/stdin reader plumbing.
    /// </summary>
    /// <param name="bytes">The raw on-disk bytes.</param>
    /// <returns>The UTF-8 decoded text with the BOM stripped.</returns>
    public static string DecodeUtf8Bom(byte[] bytes) => DecodeUtf8(bytes);

    private static string DecodeUtf8(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteOutput(string? output, string text)
    {
        if (output is null or "-")
        {
            using StreamWriter writer = new(Console.OpenStandardOutput(), new UTF8Encoding(false));
            writer.Write(text);
            return;
        }

        File.WriteAllText(output, text, new UTF8Encoding(false));
    }
}
