using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GUI.Linux.GL;
using GUI.Linux.Types.Graphs.Core;
using GUI.Linux.Types.Viewers;

namespace GUI.Linux.Viewers;

/// <summary>
/// Linux graph viewer: a real GL viewport tab rendering the shared graph presenter plus the portable
/// resource data tabs. The caller supplies the graph build action so AG2, AG1, pulse and entity I/O
/// graphs all reuse the same presenter and renderer.
/// </summary>
internal sealed class GraphGlViewer : IViewer
{
    private readonly ResourceDataViewer dataViewer;
    private readonly Func<GraphView, IDisposable?>? build;
    private readonly string tabName;

    public GraphGlViewer(ResourceDataViewer dataViewer, Func<GraphView, IDisposable?>? build, string tabName)
    {
        this.dataViewer = dataViewer;
        this.build = build;
        this.tabName = tabName;
    }

    public Task LoadAsync(Stream? stream) => dataViewer.LoadAsync(stream);

    public ViewerContent GetContent()
    {
        List<ViewerTab> tabs = [];

        if (build is not null)
        {
            tabs.Add(new ViewerTab(tabName, new ViewerContent.GlViewport(() => new GraphGlRenderer(build)), Select: true));
        }

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
