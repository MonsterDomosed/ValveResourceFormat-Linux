using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GUI.Linux.GL;
using GUI.Linux.Shell;
using GUI.Linux.Types.GLViewers;
using GUI.Linux.Types.Viewers;
using GUI.Linux.UI;

namespace GUI.Linux.Viewers;

/// <summary>
/// Linux particle viewer: a real GL viewport tab plus the portable resource data tabs. The GL tab
/// renders through the shared <see cref="GLSceneViewerCore"/>; the renderer factory lets the shell
/// pick the particle or particle-snapshot core.
/// </summary>
internal sealed class ParticleGlViewer : IViewer
{
    private readonly ResourceDataViewer dataViewer;
    private readonly Func<IGLViewportRenderer> createRenderer;
    private readonly string tabName;

    public ParticleGlViewer(IViewerContext context, string fileName, ResourceDataViewer dataViewer, Func<IGLViewportRenderer>? createRenderer = null, string tabName = "PARTICLE")
    {
        this.dataViewer = dataViewer;
        this.createRenderer = createRenderer ?? (() => new ParticleGlRenderer(fileName));
        this.tabName = tabName;
    }

    public Task LoadAsync(Stream? stream) => dataViewer.LoadAsync(stream);

    public ViewerContent GetContent()
    {
        List<ViewerTab> tabs =
        [
            new ViewerTab(tabName, new ViewerContent.CustomControl(() => new ViewportWithSidebar(createRenderer, new ViewerSidebar())), Select: true),
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
