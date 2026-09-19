using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using SkiaSharp;
using Svg.Skia;

namespace GUI.Linux.UI;

/// <summary>
/// Rasterizes the embedded UI SVGs with <see cref="SKSvg"/> and caches them per icon, size and
/// theme variant. Icons that ship a <c>_light</c> counterpart use it in the light theme.
/// </summary>
internal static class IconFactory
{
    private const string ResourcePrefix = "GUI.Linux.Icons.Ui.";

    private readonly record struct IconKey(string Name, int Size, bool Light);

    private static readonly object Sync = new();
    private static readonly Dictionary<IconKey, Bitmap> Cache = [];

    /// <summary>Whether the application is currently in the light theme variant.</summary>
    public static bool IsLightTheme
        => (Application.Current as IThemeVariantHost)?.ActualThemeVariant == ThemeVariant.Light;

    /// <summary>Returns the rasterized icon, or null when the asset does not exist.</summary>
    public static Bitmap? Get(string name, int size)
    {
        var key = new IconKey(name, size, IsLightTheme);

        lock (Sync)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var bitmap = Render(key);

        if (bitmap is not null)
        {
            lock (Sync)
            {
                Cache[key] = bitmap;
            }
        }

        return bitmap;
    }

    /// <summary>Drops cached bitmaps so the next request re-reads the active theme variant.</summary>
    public static void ClearCache()
    {
        lock (Sync)
        {
            Cache.Clear();
        }
    }

    private static Bitmap? Render(IconKey key)
    {
        var assembly = typeof(IconFactory).Assembly;
        var lightSuffix = key.Light ? "_light" : string.Empty;

        var stream = (lightSuffix.Length > 0 ? assembly.GetManifestResourceStream($"{ResourcePrefix}{key.Name}{lightSuffix}.svg") : null)
            ?? assembly.GetManifestResourceStream($"{ResourcePrefix}{key.Name}.svg");

        if (stream is null)
        {
            return null;
        }

        using (stream)
        using (var svg = new SKSvg())
        {
            if (svg.Load(stream) is not { } picture)
            {
                return null;
            }

            var cull = picture.CullRect;
            var extent = Math.Max(cull.Width, cull.Height);

            if (extent <= 0f)
            {
                return null;
            }

            var scale = key.Size / extent;

            using var bitmap = new SKBitmap(key.Size, key.Size, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.Translate((key.Size / scale - cull.Width) / 2f - cull.Left, (key.Size / scale - cull.Height) / 2f - cull.Top);
            canvas.DrawPicture(picture);

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var encoded = new MemoryStream();
            data.SaveTo(encoded);
            encoded.Position = 0;

            return new Bitmap(encoded);
        }
    }
}
