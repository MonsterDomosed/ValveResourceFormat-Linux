using ValveResourceFormat.Renderer;

namespace GUI.Types.GLViewers;

/// <summary>
/// A renderer hosted by a platform GL viewport (Avalonia <c>OpenGlControlBase</c> or an equivalent
/// host). Initialization and rendering happen on the thread that owns the GL context, so the
/// implementation never needs cross-thread GL access.
/// </summary>
public interface IGLViewportRenderer : IDisposable
{
    /// <summary>When true, a new frame is requested every frame; otherwise frames are on demand.</summary>
    bool ContinuousRendering { get; }

    /// <summary>Called once, with the GL context current and bindings already loaded.</summary>
    void Initialize(GraphicsDevice device, GraphicsContext context, IGLViewerHost host);

    /// <summary>Renders one frame with the GL context current.</summary>
    void Render(GraphicsContext context, int width, int height, ViewerInputState input, double frameTime);
}
