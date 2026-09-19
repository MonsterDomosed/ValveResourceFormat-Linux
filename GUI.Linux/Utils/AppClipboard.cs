using GUI.Linux.Platform;

namespace GUI.Linux.Utils;

public static class AppClipboard
{
    public static void SetText(string text) => PlatformServices.Current.Clipboard.SetText(text);

    public static string GetText() => PlatformServices.Current.Clipboard.GetText();

    public static void SetImage(SkiaSharp.SKBitmap bitmap) => PlatformServices.Current.Clipboard.SetImage(bitmap);
}
