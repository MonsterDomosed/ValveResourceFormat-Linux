using SkiaSharp;

namespace GUI.Linux.Types.Graphs.Core;

/// <summary>
/// The chrome colours a <see cref="GraphPalette"/> derives its non-hue slots from, decoupled from any
/// windowing toolkit's theme model. The shell supplies its own values or uses <see cref="Dark"/>.
/// </summary>
internal readonly record struct GraphChromeColors(
    SKColor AppMiddle,
    SKColor AppSoft,
    SKColor Contrast,
    SKColor ContrastSoft,
    SKColor Border,
    SKColor Accent)
{
    /// <summary>The dark chrome the graph renderer falls back to when a host does not supply one.</summary>
    public static GraphChromeColors Dark { get; } = new(
        new SKColor(34, 39, 51),
        new SKColor(44, 49, 61),
        SKColors.White,
        new SKColor(158, 159, 164),
        new SKColor(51, 57, 74),
        new SKColor(99, 161, 255));
}
