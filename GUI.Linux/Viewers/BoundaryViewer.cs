using System.IO;
using System.Threading.Tasks;
using GUI.Types.Viewers;

namespace GUI.Linux.Viewers;

/// <summary>
/// Appends an explicit "not supported yet" tab to another viewer's content, so resource types that
/// still lack a Linux preview remain visible instead of silently missing.
/// </summary>
internal sealed class BoundaryViewer(IViewer inner, string tabName, string message) : IViewer
{
    /// <summary>The wrapped viewer, exposed for diagnostics.</summary>
    internal IViewer Inner => inner;

    public Task LoadAsync(Stream? stream) => inner.LoadAsync(stream);

    public ViewerContent? GetContent()
    {
        var content = inner.GetContent();
        var tabs = new List<ViewerTab>();

        if (content is ViewerContent.Tabs existing)
        {
            tabs.AddRange(existing.Items);
        }
        else if (content is not null)
        {
            tabs.Add(new ViewerTab("Content", content));
        }

        tabs.Add(new ViewerTab(tabName, new ViewerContent.Text(message)));

        return new ViewerContent.Tabs(tabs);
    }

    public void NotifyVisible() => inner.NotifyVisible();

    public void Dispose()
    {
        inner.Dispose();
        GC.SuppressFinalize(this);
    }
}
