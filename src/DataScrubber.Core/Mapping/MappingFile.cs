namespace DataScrubber.Mapping;

/// <summary>
///     The root document of the mapping file written next to a reversible
///     scrub output. The schema is JSON. Future versions branch on
///     <see cref="SchemaVersion"/>; v1 is the only version produced and
///     accepted by this build.
/// </summary>
/// <param name="SchemaVersion">The schema version. Must be <see cref="CurrentSchemaVersion"/>.</param>
/// <param name="CreatedAt">The UTC instant the mapping was produced.</param>
/// <param name="SourceSha256">Lowercase hex SHA-256 of the raw input bytes prior to scrubbing.</param>
/// <param name="Entries">The token-to-original entries in token-allocation order.</param>
public sealed record MappingFile(
    int SchemaVersion,
    DateTimeOffset CreatedAt,
    string SourceSha256,
    IReadOnlyList<MappingEntry> Entries)
{
    /// <summary>
    ///     The schema version produced and accepted by this build.
    /// </summary>
    public const int CurrentSchemaVersion = 1;
}
