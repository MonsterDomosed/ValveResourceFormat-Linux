using SkiaSharp;
using ValveResourceFormat.Renderer;

namespace GUI.Linux.Types.GLViewers;

/// <summary>
/// The window-side responsibilities a GL viewer core needs from the Avalonia viewport host.
/// Kept deliberately small; the host owns the surface, frame scheduling and shell actions.
/// </summary>
public interface IGLViewerHost
{
    /// <summary>Input state for this viewport.</summary>
    ViewerInputState Input { get; }

    /// <summary>Whether the viewport is currently visible.</summary>
    bool IsVisible { get; }

    /// <summary>The framebuffer the finished frame is presented to.</summary>
    Framebuffer ScreenFramebuffer { get; }

    /// <summary>Asks the host to render another frame.</summary>
    void RequestFrame();

    /// <summary>Asks the host to toggle fullscreen for this viewport.</summary>
    void RequestFullscreen();

    /// <summary>Copies a rendered image to the system clipboard.</summary>
    void SetClipboardImage(SKBitmap bitmap);
}
