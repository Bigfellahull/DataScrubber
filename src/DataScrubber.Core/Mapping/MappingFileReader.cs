namespace DataScrubber.Mapping;

using System.Text;
using System.Text.Json;

/// <summary>
///     Reads a <see cref="MappingFile"/> from disk with strict member
///     handling. The reader inspects <c>schemaVersion</c> first so a
///     forward-incompatible file is rejected with
///     <see cref="UnsupportedMappingSchemaException"/> rather than failing
///     deeper inside JSON deserialisation.
/// </summary>
public static class MappingFileReader
{
    /// <summary>
    ///     Reads and deserialises the mapping file at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The path to the mapping file.</param>
    /// <returns>The deserialised <see cref="MappingFile"/>.</returns>
    /// <exception cref="UnsupportedMappingSchemaException">The file's schema version is not understood by this build.</exception>
    /// <exception cref="JsonException">The file is not valid JSON or contains unmapped members.</exception>
    /// <exception cref="InvalidDataException">The file is empty or its root is null.</exception>
    public static MappingFile Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string raw = File.ReadAllText(path, new UTF8Encoding(false));
        int schemaVersion = ReadSchemaVersion(raw);
        if (schemaVersion != MappingFile.CurrentSchemaVersion)
        {
            throw new UnsupportedMappingSchemaException(schemaVersion);
        }

        MappingFile mapping = JsonSerializer.Deserialize<MappingFile>(raw, MappingFileSerialization.Read)
            ?? throw new InvalidDataException("Mapping file is empty.");

        ValidateMapping(mapping);
        return mapping;
    }

    private static void ValidateMapping(MappingFile mapping)
    {
        ValidateSourceSha256(mapping.SourceSha256);

        // The record type declares Entries as non-nullable, but a JSON
        // payload with `"entries": null` slips a runtime null past the
        // deserializer; the cast lets the null check compile without the
        // CS8073 always-false warning.
        if ((object?)mapping.Entries is null)
        {
            throw new InvalidDataException("Mapping file entries field is null.");
        }

        foreach (MappingEntry entry in mapping.Entries)
        {
            ValidateEntry(entry);
        }
    }

    private static void ValidateEntry(MappingEntry entry)
    {
        if ((object?)entry is null)
        {
            throw new InvalidDataException("Mapping entries contains a null entry.");
        }

        if (string.IsNullOrEmpty(entry.Token))
        {
            throw new InvalidDataException("Mapping entry token is missing or empty.");
        }

        if ((object?)entry.Original is null)
        {
            throw new InvalidDataException("Mapping entry original is null.");
        }

        if (TokenFormat.ContainsToken(entry.Original))
        {
            throw new InvalidDataException(
                $"Mapping entry original '{entry.Original}' is token-shaped; rehydration would not be idempotent.");
        }
    }

    private static void ValidateSourceSha256(string sha)
    {
        if (string.IsNullOrEmpty(sha))
        {
            throw new InvalidDataException("Mapping file sourceSha256 is missing or empty.");
        }

        if (sha.Length != 64 || !IsLowercaseHex(sha))
        {
            throw new InvalidDataException("Mapping file sourceSha256 must be 64 lowercase hex characters.");
        }
    }

    private static bool IsLowercaseHex(string s)
    {
        foreach (char c in s)
        {
            if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static int ReadSchemaVersion(string raw)
    {
        using JsonDocument doc = JsonDocument.Parse(raw);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Mapping file root must be a JSON object.");
        }

        if (!doc.RootElement.TryGetProperty("schemaVersion", out JsonElement element))
        {
            throw new InvalidDataException("Mapping file is missing the schemaVersion field.");
        }

        if (element.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidDataException("Mapping file schemaVersion must be a number.");
        }

        return element.GetInt32();
    }
}
