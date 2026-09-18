using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GUI.Linux.GL;
using GUI.Types.Viewers;

namespace GUI.Linux.Viewers;

/// <summary>
/// Linux world/map viewer: a real GL viewport tab plus the portable resource data tabs. The GL tab
/// renders through the shared <see cref="GUI.Types.GLViewers.GLSceneViewerCore"/> and the existing
/// <c>WorldLoader</c>.
/// </summary>
internal sealed class WorldGlViewer : IViewer
{
    private readonly string fileName;
    private readonly ResourceDataViewer dataViewer;

    public WorldGlViewer(IViewerContext context, string fileName, ResourceDataViewer dataViewer)
    {
        this.fileName = fileName;
        this.dataViewer = dataViewer;
    }

    public Task LoadAsync(Stream? stream) => dataViewer.LoadAsync(stream);

    public ViewerContent GetContent()
    {
        List<ViewerTab> tabs =
        [
            new ViewerTab("MAP", new ViewerContent.GlViewport(() => new WorldGlRenderer(fileName)), Select: true),
        ];

        if (dataViewer.GetContent() is ViewerContent.Tabs data)
        {
            foreach (var tab in data.Items)
            {
                tabs.Add(new ViewerTab(tab.Name, tab.Content));
            }
        }

        return new ViewerContent.Tabs(tabs);
    }

    public void Dispose()
    {
        dataViewer.Dispose();
        GC.SuppressFinalize(this);
    }
}
