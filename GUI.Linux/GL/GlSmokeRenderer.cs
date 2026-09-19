using System;
using GUI.Linux.Types.GLViewers;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat.Renderer;

namespace GUI.Linux.GL;

/// <summary>
/// Minimal renderer that proves the Avalonia GL host can initialize the Renderer's GL environment
/// and render frames. It clears the framebuffer and reports GL/Renderer identity.
/// </summary>
internal sealed class GlSmokeRenderer : IGLViewportRenderer
{
    private int frames;

    public bool ContinuousRendering => true;

    public void Initialize(GraphicsDevice device, GraphicsContext context, GUI.Linux.Types.GLViewers.IGLViewerHost host)
    {
        context.Begin();

        try
        {
            GLEnvironment.Initialize(NullLogger.Instance);
            GLEnvironment.SetDefaultRenderState();
            GLEnvironment.EnableParallelShaderCompile();

            Program.StdOut.WriteLine($"[gl] version: {OpenTK.Graphics.OpenGL.GL.GetString(OpenTK.Graphics.OpenGL.StringName.Version)}");
            Program.StdOut.WriteLine($"[gl] renderer: {OpenTK.Graphics.OpenGL.GL.GetString(OpenTK.Graphics.OpenGL.StringName.Renderer)}");
            Program.StdOut.WriteLine($"[gl] gpu: {GLEnvironment.GpuRendererAndDriver}");
        }
        finally
        {
            context.End();
        }
    }

    public void Render(GraphicsContext context, int width, int height, GUI.Linux.Types.GLViewers.ViewerInputState input, double frameTime)
    {
        context.Begin();

        try
        {
            OpenTK.Graphics.OpenGL.GL.Viewport(0, 0, width, height);
            OpenTK.Graphics.OpenGL.GL.ClearColor(0.1f, 0.2f, 0.3f, 1f);
            OpenTK.Graphics.OpenGL.GL.Clear(
                OpenTK.Graphics.OpenGL.ClearBufferMask.ColorBufferBit | OpenTK.Graphics.OpenGL.ClearBufferMask.DepthBufferBit);

            var error = OpenTK.Graphics.OpenGL.GL.GetError();

            if (frames++ == 0)
            {
                Program.StdOut.WriteLine($"[gl] first frame {width}x{height}, glError={error}, focus={input.HasFocus}");
            }
        }
        finally
        {
            context.End();
        }
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
