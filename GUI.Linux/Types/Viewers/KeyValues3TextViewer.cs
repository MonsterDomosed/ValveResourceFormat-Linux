using System.IO;
using System.Text;
using System.Threading.Tasks;
using GUI.Linux.Utils;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Linux.Types.Viewers;

/// <summary>
/// Uncompiled KV3 text files. Shows the document as KV3 text plus a hex tab. Animation graph
/// documents are flagged so the shell can offer its graph viewer.
/// </summary>
public sealed class KeyValues3TextViewer(IViewerContext viewerContext) : IViewer
{
    // A KV3 text file opens with its encoding and format header comment: "<!-- kv3 encoding:...".
    private const uint CommentMagic = 0x2D2D213C; // "<!--"
    private const ushort Kv3Magic = 0x6B20; // " k" of " kv3"

    private string? text;
    private byte[]? bytes;

    public static bool IsAccepted(uint magic, ushort magicSecond)
    {
        return magic == CommentMagic && magicSecond == Kv3Magic;
    }

    /// <summary>Whether the document is an animation graph, which has a GL graph viewer.</summary>
    public bool HasAnimationGraph { get; private set; }

    public async Task LoadAsync(Stream? stream)
    {
        if (stream != null)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer).ConfigureAwait(false);
            bytes = buffer.ToArray();
        }
        else
        {
            bytes = await File.ReadAllBytesAsync(viewerContext.FileName).ConfigureAwait(false);
        }

        text = Encoding.UTF8.GetString(bytes);

        try
        {
            using var parseStream = new MemoryStream(bytes, writable: false);
            var document = KVSerializer.Create(KVSerializationFormat.KeyValues3Text).Deserialize(parseStream);
            var className = document.Root.GetStringProperty("_class");
            HasAnimationGraph = className is "CAnimationGraph" or "CAnimationSubGraph";
        }
        catch (Exception)
        {
            // The text stays viewable even when it is a KV3 dialect we cannot read yet.
        }
    }

    public ViewerContent GetContent()
    {
        List<ViewerTab> tabs =
        [
            new("KV3", new ViewerContent.Text(text ?? string.Empty, HighlightLanguage.KeyValues), Select: true),
            new("Hex", new ViewerContent.HexDump(bytes ?? [])),
        ];

        return new ViewerContent.Tabs(tabs);
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
