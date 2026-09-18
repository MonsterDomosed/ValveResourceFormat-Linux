using ValveResourceFormat.Renderer;

namespace GUI.Linux.GL;

/// <summary>
/// Presents Avalonia's OpenGL context to the renderer. Avalonia makes the context current for the
/// whole render callback, so the surface only brackets the renderer's command stream.
/// </summary>
internal sealed class AvaloniaGraphicsSurface : IGraphicsSurface
{
    public void Begin()
    {
    }

    public void End()
    {
    }
}
