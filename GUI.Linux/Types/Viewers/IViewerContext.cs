namespace GUI.Linux.Types.Viewers;

/// <summary>
/// The minimum a viewer needs from the file it was opened from. Implemented by the platform shell's
/// GUI context so portable viewers do not depend on a shell-specific type.
/// </summary>
public interface IViewerContext
{
    /// <summary>Full path (on disk or virtual path inside a package) of the opened file.</summary>
    string FileName { get; }
}
