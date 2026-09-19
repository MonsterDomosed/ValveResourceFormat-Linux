using System.IO;
using System.Threading.Tasks;

namespace GUI.Linux.Types.Viewers;

/// <summary>Displays a raster image (PNG/JPEG/GIF) as encoded bytes for the shell to decode.</summary>
public sealed class ImageViewer(IViewerContext viewerContext) : IViewer
{
    private byte[]? bytes;

    public static bool IsAccepted(uint magic)
    {
        return magic == 0x474E5089 || /* png */
               magic << 8 == 0xFFD8FF00 || /* jpg */
               magic << 8 == 0x46494700; /* gif */
    }

    public async Task LoadAsync(Stream? stream)
    {
        bytes = stream != null
            ? await ReadAllBytesAsync(stream).ConfigureAwait(false)
            : await File.ReadAllBytesAsync(viewerContext.FileName).ConfigureAwait(false);
    }

    public ViewerContent GetContent()
    {
        var content = new ViewerContent.EncodedImage(bytes ?? throw new InvalidOperationException("Viewer was not loaded."));
        bytes = null;
        return content;
    }

    public void Dispose() => GC.SuppressFinalize(this);

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
