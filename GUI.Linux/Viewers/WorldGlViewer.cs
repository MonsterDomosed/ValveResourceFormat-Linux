using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GUI.Linux.GL;
using GUI.Types.GLViewers;
using GUI.Types.Viewers;

namespace GUI.Linux.Viewers;

/// <summary>
/// Linux world/map/world-node viewer: a real GL viewport tab plus the portable resource data tabs.
/// The GL tab renders through the shared <see cref="GLSceneViewerCore"/> and the existing world
/// loaders; the renderer factory lets the shell pick the world, map or world-node core.
/// </summary>
internal sealed class WorldGlViewer : IViewer
{
    private readonly ResourceDataViewer dataViewer;
    private readonly Func<IGLViewportRenderer> createRenderer;
    private readonly string tabName;

    public WorldGlViewer(IViewerContext context, string fileName, ResourceDataViewer dataViewer, Func<IGLViewportRenderer>? createRenderer = null, string tabName = "MAP")
    {
        this.dataViewer = dataViewer;
        this.createRenderer = createRenderer ?? (() => new WorldGlRenderer(fileName));
        this.tabName = tabName;
    }

    public Task LoadAsync(Stream? stream) => dataViewer.LoadAsync(stream);

    public ViewerContent GetContent()
    {
        List<ViewerTab> tabs =
        [
            new ViewerTab(tabName, new ViewerContent.GlViewport(createRenderer), Select: true),
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
