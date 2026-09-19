using System.IO;
using System.Threading.Tasks;
using SkiaSharp;
using Svg.Skia;

namespace GUI.Linux.Types.Viewers;

/// <summary>Rasterizes an SVG vector image to PNG bytes at load time for the shell to display.</summary>
public sealed class SvgVectorViewer(IViewerContext viewerContext) : IViewer
{
    private const int MaxDimension = 4096;
    private const int FallbackSize = 512;

    private byte[]? png;

    public static bool IsAccepted(string fileName)
    {
        return fileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
    }

    public Task LoadAsync(Stream? stream)
    {
        using var svg = new SKSvg();

        if (stream != null)
        {
            svg.Load(stream);
        }
        else
        {
            svg.Load(viewerContext.FileName);
        }

        var picture = svg.Picture ?? throw new InvalidDataException("Failed to load SVG");

        var bounds = picture.CullRect;
        var width = (int)Math.Ceiling(bounds.Width);
        var height = (int)Math.Ceiling(bounds.Height);

        if (width <= 0 || height <= 0)
        {
            width = FallbackSize;
            height = FallbackSize;
        }

        var scale = Math.Min(1f, (float)MaxDimension / Math.Max(width, height));
        width = Math.Max(1, (int)(width * scale));
        height = Math.Max(1, (int)(height * scale));

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        png = data.ToArray();

        return Task.CompletedTask;
    }

    public ViewerContent GetContent()
    {
        var content = new ViewerContent.EncodedImage(png ?? throw new InvalidOperationException("Viewer was not loaded."));
        png = null;
        return content;
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
