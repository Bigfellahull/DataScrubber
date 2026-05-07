namespace DataScrubber.Mapping;

/// <summary>
///     Thrown by <see cref="MappingFileReader"/> when it encounters a mapping
///     file whose <see cref="MappingFile.SchemaVersion"/> is not understood by
///     this build. The CLI surfaces this as exit code <c>2</c>.
/// </summary>
public sealed class UnsupportedMappingSchemaException : Exception
{
    /// <summary>
    ///     The schema version that was read from the file.
    /// </summary>
    public int SchemaVersion { get; }

    /// <summary>
    ///     Initialises a new <see cref="UnsupportedMappingSchemaException"/>.
    /// </summary>
    /// <param name="schemaVersion">The unsupported schema version that was read.</param>
    public UnsupportedMappingSchemaException(int schemaVersion)
        : base($"Map schema version {schemaVersion} is not supported by this build (supported: {MappingFile.CurrentSchemaVersion})")
    {
        SchemaVersion = schemaVersion;
    }
}
