using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GUI.Linux.GL;
using GUI.Types.Viewers;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.Viewers;

/// <summary>
/// Linux animation graph viewer: a real GL viewport tab rendering the shared graph presenter plus the
/// portable resource data tabs. The graph is built by the same <c>NmGraphBuilder</c> the Windows
/// viewer uses.
/// </summary>
internal sealed class GraphGlViewer : IViewer
{
    private readonly ResourceDataViewer dataViewer;

    public GraphGlViewer(ResourceDataViewer dataViewer)
    {
        this.dataViewer = dataViewer;
    }

    public Task LoadAsync(Stream? stream) => dataViewer.LoadAsync(stream);

    public ViewerContent GetContent()
    {
        List<ViewerTab> tabs = [];

        if (dataViewer.Resource?.DataBlock is BinaryKV3 graphData)
        {
            tabs.Add(new ViewerTab("AG2 ANIMATION GRAPH", new ViewerContent.GlViewport(() => new GraphGlRenderer(graphData.Data)), Select: true));
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
