using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Svg.Skia;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.TextureDecoders;

namespace GUI.Linux.Types.Browser;

/// <summary>
/// Produces small preview bitmaps for package entries on the CPU, so the package browser can show
/// thumbnails without a GL context. Textures decode through <see cref="Texture.GenerateBitmap"/>,
/// loose bitmaps through Skia and vectors through Svg.Skia. Anything else returns null and the
/// caller falls back to the entry's type icon.
/// </summary>
internal static class PackageThumbnails
{
    private const int TargetSize = 128;
    private const int MaxCacheEntries = 400;

    private static readonly object Sync = new();
    private static readonly Dictionary<string, Bitmap> Cache = [];
    private static readonly LinkedList<string> LruOrder = [];
    private static readonly Dictionary<string, LinkedListNode<string>> LruNodes = [];

    /// <summary>Whether <paramref name="entry"/> is a type this decoder can render a thumbnail for.</summary>
    public static bool CanThumbnail(PackageEntry entry)
    {
        var name = entry.GetFileName();

        return name.EndsWith(".vtex_c", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns a cached thumbnail, decoding it on the calling thread when it is not cached. Safe to
    /// call from a background thread; the returned bitmap is shared and must not be disposed.
    /// </summary>
    public static Bitmap? Get(Package package, PackageEntry entry, CancellationToken cancellationToken = default)
    {
        var key = $"{package.FileName}\n{entry.GetFullPath()}";

        lock (Sync)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                Touch(key);
                return cached;
            }
        }

        Bitmap? bitmap;
#pragma warning disable CA2000 // Ownership is transferred to the cache and to the caller.
        bitmap = Create(package, entry, cancellationToken);
#pragma warning restore CA2000

        if (bitmap is null)
        {
            return null;
        }

        lock (Sync)
        {
            if (Cache.TryGetValue(key, out var existing))
            {
                // Lost a race, keep the first result.
                bitmap.Dispose();
                Touch(key);
                return existing;
            }

            Cache[key] = bitmap;
            Touch(key);
            Trim();
        }

        return bitmap;
    }

    private static void Touch(string key)
    {
        if (LruNodes.TryGetValue(key, out var node))
        {
            LruOrder.Remove(node);
            LruOrder.AddLast(node);
            return;
        }

        LruNodes[key] = LruOrder.AddLast(key);
    }

    private static void Trim()
    {
        while (Cache.Count > MaxCacheEntries && LruOrder.First is { } oldest)
        {
            LruOrder.RemoveFirst();
            LruNodes.Remove(oldest.Value);
            Cache.Remove(oldest.Value);
        }
    }

    private static Bitmap? Create(Package package, PackageEntry entry, CancellationToken cancellationToken)
    {
        var name = entry.GetFileName();

        try
        {
            if (name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = GameFileLoader.GetPackageEntryStream(package, entry);
                return RasterizeSvg(stream);
            }

            if (name.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ModelThumbnailRenderer.Instance.Render(package, entry);
            }

            if (name.EndsWith(".vtex_c", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = GameFileLoader.GetPackageEntryStream(package, entry);
                using var resource = new Resource { FileName = entry.GetFullPath() };
                resource.Read(stream);

                if (resource.DataBlock is not Texture texture)
                {
                    return null;
                }

                cancellationToken.ThrowIfCancellationRequested();

                using var decoded = texture.GenerateBitmap(mipLevel: (uint)SelectMip(texture, TargetSize), decodeFlags: TextureCodec.ForceLDR);
                return ToAvaloniaBitmap(decoded);
            }

            if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = GameFileLoader.GetPackageEntryStream(package, entry);
                using var decoded = SKBitmap.Decode(stream);
                return decoded is null ? null : ToAvaloniaBitmap(decoded);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // A preview is best effort; unsupported or malformed entries fall back to their icon.
        }

        return null;
    }

    // Picks the smallest mip that is still at least the target size, so large textures are not
    // decoded at full resolution just for a thumbnail.
    private static int SelectMip(Texture texture, int target)
    {
        var max = Math.Max(texture.ActualWidth, texture.ActualHeight);
        var level = 0;

        while (level + 1 < texture.NumMipLevels && (max >> level) > target)
        {
            level++;
        }

        return level;
    }

    private static Bitmap? RasterizeSvg(Stream stream)
    {
        using var svg = new SKSvg();

        if (svg.Load(stream) is not { } picture)
        {
            return null;
        }

        var bounds = picture.CullRect;
        var width = (int)Math.Ceiling(bounds.Width);
        var height = (int)Math.Ceiling(bounds.Height);

        if (width <= 0 || height <= 0)
        {
            width = TargetSize;
            height = TargetSize;
        }

        var scale = Math.Min(1f, (float)TargetSize / Math.Max(width, height));
        var pixelWidth = Math.Max(1, (int)(width * scale));
        var pixelHeight = Math.Max(1, (int)(height * scale));

        using var bitmap = new SKBitmap(pixelWidth, pixelHeight);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);

        return ToAvaloniaBitmap(bitmap);
    }

    internal static Bitmap? ToAvaloniaBitmap(SKBitmap source)
    {
        var max = Math.Max(source.Width, source.Height);

        if (max <= 0)
        {
            return null;
        }

        var scale = max > TargetSize ? (float)TargetSize / max : 1f;
        var width = Math.Max(1, (int)(source.Width * scale));
        var height = Math.Max(1, (int)(source.Height * scale));

        SKBitmap? scaled = null;
        var target = source;

        if (width != source.Width || height != source.Height)
        {
            scaled = source.Resize(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

            if (scaled is not null)
            {
                target = scaled;
            }
        }

        try
        {
            using var image = SKImage.FromBitmap(target);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);

            if (data is null)
            {
                return null;
            }

            using var encoded = new MemoryStream();
            data.SaveTo(encoded);
            encoded.Position = 0;

            return new Bitmap(encoded);
        }
        finally
        {
            scaled?.Dispose();
        }
    }
}
