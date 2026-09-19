using System.IO;
using Microsoft.Extensions.Logging;

namespace GUI.Linux.Types.GLViewers;

/// <summary>
/// What the platform-neutral scene viewer core needs from the file it was opened from. Implemented by
/// each shell's GUI context; deliberately smaller than the Windows <c>VrfGuiContext</c>.
/// </summary>
public interface ISceneViewerContext
{
    /// <summary>Logger used for viewer diagnostics.</summary>
    ILogger SceneLogger { get; }

    /// <summary>Optional loading phase reporter, if a loading panel is listening.</summary>
    IProgress<string>? LoadingProgress { get; }

    /// <summary>Drops cached resources after the scene has finished loading.</summary>
    void ClearCache();

    /// <summary>Opens the default IBL cubemap resource, or null when the shell has none.</summary>
    Stream? OpenDefaultCubemapStream();
}
