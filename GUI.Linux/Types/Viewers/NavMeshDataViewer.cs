using System.IO;
using System.Threading.Tasks;
using GUI.Linux.Utils;
using ValveKeyValue;
using ValveResourceFormat.NavMesh;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Linux.Types.Viewers;

/// <summary>
/// Shows the parsed contents of a nav mesh file (NAV INFO and any embedded KV3 documents). The
/// rendered nav mesh is a GL viewer handled elsewhere.
/// </summary>
public sealed class NavMeshDataViewer(IViewerContext viewerContext) : IViewer
{
    private readonly NavMeshFile navMeshFile = new();

    public static bool IsAccepted(uint magic) => magic == NavMeshFile.MAGIC;

    public Task LoadAsync(Stream? stream)
    {
        if (stream != null)
        {
            navMeshFile.Read(stream);
        }
        else
        {
            navMeshFile.Read(viewerContext.FileName);
        }

        return Task.CompletedTask;
    }

    public ViewerContent GetContent()
    {
        List<ViewerTab> tabs =
        [
            new("NAV INFO", new ViewerContent.Text(navMeshFile.ToString(), HighlightLanguage.None)),
        ];

        AddKvTab(tabs, "NAV CUSTOM DATA", navMeshFile.CustomData);
        AddKvTab(tabs, "NAV UNKNOWN KV3 1", navMeshFile.KV3Unknown1);
        AddKvTab(tabs, "NAV UNKNOWN KV3 2", navMeshFile.KV3Unknown2);
        AddKvTab(tabs, "NAV UNKNOWN KV3 3", navMeshFile.KV3Unknown3);

        return new ViewerContent.Tabs(tabs);
    }

    public void Dispose() => GC.SuppressFinalize(this);

    private static void AddKvTab(List<ViewerTab> tabs, string name, KVDocument? document)
    {
        if (document != null)
        {
            tabs.Add(new ViewerTab(name, new ViewerContent.Text(document.ToKV3String(), HighlightLanguage.None)));
        }
    }
}
