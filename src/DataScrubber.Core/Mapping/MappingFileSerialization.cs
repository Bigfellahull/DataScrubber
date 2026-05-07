namespace DataScrubber.Mapping;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
///     Shared <see cref="JsonSerializerOptions"/> used by the mapping file
///     reader and writer. Centralised so the on-disk shape stays stable and
///     symmetric across reads and writes.
/// </summary>
internal static class MappingFileSerialization
{
    public static JsonSerializerOptions Write { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static JsonSerializerOptions Read { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
}
