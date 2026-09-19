using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GUI.Linux.Utils;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.Utils;

namespace GUI.Linux.Types.Viewers;

/// <summary>
/// Compiled shader collections. Presents the summary of every program in the collection as text.
/// The interactive program/combo tree and bytecode export are not part of this portable viewer.
/// </summary>
public sealed class CompiledShaderViewer(IViewerContext viewerContext) : IViewer
{
    private List<ViewerTab>? tabs;

    public static bool IsAccepted(uint magic) => magic == VfxProgramData.MAGIC;

    public Task LoadAsync(Stream? stream)
    {
        var collection = ShaderCollection.GetShaderCollection(viewerContext.FileName, null);

        tabs = [];

        foreach (var program in collection.OrderBy(static x => x.VcsProgramType))
        {
            using var output = new IndentedTextWriter();
            program.PrintSummary(output, collection.Features);

            tabs.Add(new ViewerTab(
                program.VcsProgramType.ToString(),
                new ViewerContent.Text(output.ToString(), HighlightLanguage.Shaders)));
        }

        return Task.CompletedTask;
    }

    public ViewerContent GetContent()
    {
        var tabs = this.tabs ?? throw new InvalidOperationException("Viewer was not loaded.");
        return new ViewerContent.Tabs(tabs);
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
