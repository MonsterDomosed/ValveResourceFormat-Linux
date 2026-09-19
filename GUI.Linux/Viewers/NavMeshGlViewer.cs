using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GUI.Linux.GL;
using GUI.Linux.Shell;
using GUI.Linux.Types.Viewers;
using GUI.Linux.UI;

namespace GUI.Linux.Viewers;

/// <summary>
/// Linux navigation mesh viewer: a real GL viewport tab plus the portable NavMesh data tabs. The GL
/// tab renders through the shared <see cref="GUI.Linux.Types.GLViewers.GLSceneViewerCore"/>; this type only
/// wires the data presentation and the renderer factory.
/// </summary>
internal sealed class NavMeshGlViewer : IViewer
{
    private readonly string fileName;
    private readonly NavMeshDataViewer dataViewer;

    public NavMeshGlViewer(IViewerContext context, string fileName)
    {
        this.fileName = fileName;
        dataViewer = new NavMeshDataViewer(context);
    }

    public Task LoadAsync(Stream? stream) => dataViewer.LoadAsync(stream);

    public ViewerContent GetContent()
    {
        List<ViewerTab> tabs =
        [
            new ViewerTab("NAV MESH", new ViewerContent.CustomControl(() => new ViewportWithSidebar(() => new NavMeshGlRenderer(fileName), new ViewerSidebar())), Select: true),
        ];

        if (dataViewer.GetContent() is ViewerContent.Tabs data)
        {
            tabs.AddRange(data.Items);
        }

        return new ViewerContent.Tabs(tabs);
    }

    public void Dispose()
    {
        dataViewer.Dispose();
        GC.SuppressFinalize(this);
    }
}
