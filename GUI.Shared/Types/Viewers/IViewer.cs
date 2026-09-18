using System.IO;
using System.Threading.Tasks;

namespace GUI.Types.Viewers;

/// <summary>
/// UI-agnostic viewer. Implementations describe what they want to show with <see cref="GetContent"/>
/// and never reference a UI toolkit; each shell renders the returned <see cref="ViewerContent"/>.
/// </summary>
public interface IViewer : IDisposable
{
    /// <summary>Loads the viewer from a stream, or from <see cref="IViewerContext.FileName"/> when the stream is null.</summary>
    Task LoadAsync(Stream? stream);

    /// <summary>UI agnostic description of the loaded content.</summary>
    ViewerContent? GetContent() => null;

    /// <summary>
    /// Called after the viewer has been made visible. Viewers that render lazily (e.g. GL viewers)
    /// use this to force their first draw. No-op by default.
    /// </summary>
    void NotifyVisible() { }
}
