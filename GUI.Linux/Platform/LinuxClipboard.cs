using System.IO;
using Avalonia.Input;
using Avalonia.Input.Platform;
using GUI.Platform;
using SkiaSharp;

namespace GUI.Linux.Platform;

/// <summary>Linux clipboard access, backed by Avalonia's platform clipboard (Wayland or X11).</summary>
internal sealed class LinuxClipboard : IClipboardService
{
    public void SetText(string text)
    {
        var clipboard = RequireClipboard();
        AvaloniaSync.Run(() => clipboard.SetTextAsync(text));
    }

    public string GetText()
    {
        var clipboard = RequireClipboard();

        return AvaloniaSync.Run(async () =>
        {
            // Wayland does not hand the selection back to its own owner, so prefer the transfer that
            // was last placed on the clipboard by this process before asking the platform.
            var inProcess = await clipboard.TryGetInProcessDataAsync().ConfigureAwait(true);

            if (inProcess is not null)
            {
                var text = await inProcess.TryGetTextAsync().ConfigureAwait(true);

                if (text is not null)
                {
                    return text;
                }
            }

            return await clipboard.TryGetTextAsync().ConfigureAwait(true) ?? string.Empty;
        });
    }

    public void SetImage(SKBitmap bitmap)
    {
        var clipboard = RequireClipboard();

        AvaloniaSync.Run(async () =>
        {
            using var avaloniaBitmap = ToAvaloniaBitmap(bitmap);
            await clipboard.SetBitmapAsync(avaloniaBitmap).ConfigureAwait(true);
        });
    }

    private static IClipboard RequireClipboard()
        => LinuxPlatform.MainWindow?.Clipboard
            ?? throw new InvalidOperationException("Clipboard access requires the Avalonia shell to be running.");

    private static Avalonia.Media.Imaging.Bitmap ToAvaloniaBitmap(SKBitmap bitmap)
    {
        using var stream = new MemoryStream();

        using (var pixels = bitmap.PeekPixels())
        {
            pixels.Encode(stream, new SKPngEncoderOptions(SKPngEncoderFilterFlags.Sub, zLibLevel: 1));
        }

        stream.Position = 0;
        return new Avalonia.Media.Imaging.Bitmap(stream);
    }
}
