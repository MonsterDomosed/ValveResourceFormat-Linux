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

    /// <summary>The light chrome, mirroring the light theme's semantic tokens.</summary>
    public static GraphChromeColors Light { get; } = new(
        new SKColor(0xF4, 0xF4, 0xF6),
        new SKColor(0xFF, 0xFF, 0xFF),
        new SKColor(0x1B, 0x1B, 0x1F),
        new SKColor(0x5A, 0x5A, 0x64),
        new SKColor(0xC9, 0xC9, 0xD0),
        new SKColor(0x2F, 0x6F, 0xE0));
}
