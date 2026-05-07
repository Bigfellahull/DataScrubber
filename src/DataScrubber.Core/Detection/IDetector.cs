namespace DataScrubber.Detection;

/// <summary>
///     A pure detector that scans an input buffer and emits zero or more
///     <see cref="Detection"/> records. Implementations must be deterministic
///     and free of mutable shared state so they can run in parallel and be
///     unit-tested in isolation.
/// </summary>
public interface IDetector
{
    /// <summary>
    ///     Scans <paramref name="input"/> and yields detections in any order.
    ///     The merger sorts and resolves overlaps downstream; detectors must
    ///     not de-duplicate across detector boundaries themselves.
    /// </summary>
    /// <param name="input">The full input as a read-only memory buffer.</param>
    /// <param name="ctx">Per-run context information.</param>
    /// <returns>The detections found inside <paramref name="input"/>.</returns>
    IEnumerable<Detection> Detect(ReadOnlyMemory<char> input, DetectionContext ctx);
}
