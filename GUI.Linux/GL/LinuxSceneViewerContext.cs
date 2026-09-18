using System.IO;
using Microsoft.Extensions.Logging;

namespace GUI.Linux.GL;

/// <summary>
/// Minimal <see cref="GUI.Types.GLViewers.ISceneViewerContext"/> for the Linux GL viewport. Until the
/// resource browser and game search paths land, the viewport loads its scene input directly.
/// </summary>
internal sealed class LinuxSceneViewerContext(ILogger logger) : GUI.Types.GLViewers.ISceneViewerContext
{
    public ILogger SceneLogger { get; } = logger;

    public IProgress<string>? LoadingProgress { get; set; }

    public void ClearCache()
    {
    }

    public Stream? OpenDefaultCubemapStream() => null;
}
