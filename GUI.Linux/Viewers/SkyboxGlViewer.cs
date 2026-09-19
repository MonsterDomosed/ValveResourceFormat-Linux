using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GUI.Linux.GL;
using GUI.Linux.Types.Viewers;

namespace GUI.Linux.Viewers;

/// <summary>
/// Linux skybox viewer: a real GL viewport tab for a standalone <c>sky.vfx</c> material plus the
/// portable resource data tabs. The GL tab renders through the shared
/// <see cref="GUI.Linux.Types.GLViewers.GLSceneViewerCore"/>.
/// </summary>
internal sealed class SkyboxGlViewer : IViewer
{
    private readonly string fileName;
    private readonly ResourceDataViewer dataViewer;

    public SkyboxGlViewer(IViewerContext context, string fileName, ResourceDataViewer dataViewer)
    {
        this.fileName = fileName;
        this.dataViewer = dataViewer;
    }

    public Task LoadAsync(Stream? stream) => dataViewer.LoadAsync(stream);

    public ViewerContent GetContent()
    {
        List<ViewerTab> tabs =
        [
            new ViewerTab("SKYBOX", new ViewerContent.GlViewport(() => new SkyboxGlRenderer(fileName)), Select: true),
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
