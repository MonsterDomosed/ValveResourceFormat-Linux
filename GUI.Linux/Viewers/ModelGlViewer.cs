using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GUI.Linux.Shell;
using GUI.Linux.Types.Viewers;

namespace GUI.Linux.Viewers;

/// <summary>
/// Linux model viewer: an interactive animation viewport with a native sidebar, plus the portable
/// resource data tabs. Rendering, camera and animation run through the shared
/// <see cref="GUI.Linux.Types.GLViewers.GLSceneViewerCore"/> and <see cref="ModelAnimationSession"/>.
/// </summary>
internal sealed class ModelGlViewer : IViewer
{
    private readonly string fileName;
    private readonly ResourceDataViewer dataViewer;
    private readonly ModelAnimationSession session = new();

    private ModelViewerControl? control;

    public ModelGlViewer(IViewerContext context, string fileName, ResourceDataViewer dataViewer)
    {
        this.fileName = fileName;
        this.dataViewer = dataViewer;
    }

    public Task LoadAsync(Stream? stream) => dataViewer.LoadAsync(stream);

    public ViewerContent GetContent()
    {
        List<ViewerTab> tabs =
        [
            new ViewerTab("MODEL", new ViewerContent.CustomControl(() => control = new ModelViewerControl(fileName, session)), Select: true),
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
        control?.Dispose();
        control = null;
        dataViewer.Dispose();
        GC.SuppressFinalize(this);
    }
}
