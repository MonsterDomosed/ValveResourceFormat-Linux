using GUI.Types.Graphs.Core;
using GUI.Utils;
using SkiaSharp;

namespace GUI.Types.Graphs;

/// <summary>
/// Builds the graph palette from the Windows shell's <see cref="Themer"/>, keeping the WinForms
/// theme model out of the shared graph renderer.
/// </summary>
internal static class WindowsGraphTheme
{
    public static GraphPalette CreatePalette()
        => new(
            Themer.CurrentTheme == Themer.AppTheme.Dark,
            new GraphChromeColors(
                ToSK(Themer.CurrentThemeColors.AppMiddle),
                ToSK(Themer.CurrentThemeColors.AppSoft),
                ToSK(Themer.CurrentThemeColors.Contrast),
                ToSK(Themer.CurrentThemeColors.ContrastSoft),
                ToSK(Themer.CurrentThemeColors.Border),
                ToSK(Themer.CurrentThemeColors.Accent)));

    private static SKColor ToSK(System.Drawing.Color c) => new(c.R, c.G, c.B, c.A);
}
