using System;
using GUI.Linux.Types.GLViewers;
using GUI.Linux.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using FramebufferTarget = OpenTK.Graphics.OpenGL.FramebufferTarget;
using GetPName = OpenTK.Graphics.OpenGL.GetPName;
using OpenGL = OpenTK.Graphics.OpenGL.GL;
using PixelFormat = OpenTK.Graphics.OpenGL.PixelFormat;
using PixelType = OpenTK.Graphics.OpenGL.PixelType;

namespace GUI.Linux.GL;

/// <summary>
/// Shared Avalonia GL host plumbing for any <see cref="IGLViewportRenderer"/> that is not a scene core:
/// initializes the GL environment, tracks viewport size, drives frames, records a readback for the
/// self-check and disposes cleanly. Scene rendering (<see cref="SceneCoreGlRenderer"/>) and non-scene
/// viewers such as the texture viewer build on this so the host code exists once.
/// </summary>
internal abstract class ViewportGlRenderer : IGLViewportRenderer
{
    protected string Label { get; }

    /// <summary>The host driving this renderer; set before <see cref="InitializeRenderer"/> runs.</summary>
    protected IGLViewerHost? Host { get; private set; }

    /// <summary>The shared game file loader, used for renderer contexts and resource lookups.</summary>
    protected GameFileLoader? FileLoader { get; private set; }

    /// <summary>Offscreen render target, when the viewer renders through one; null for direct-to-screen viewers.</summary>
    protected Framebuffer? MainFramebuffer { get; set; }

    /// <summary>MSAA sample count chosen for this viewport.</summary>
    protected int SampleCount { get; private set; } = 1;

    /// <summary>Raised after the first frame, for the self-check.</summary>
    public event Action? FirstFrameRendered;

    /// <summary>Number of distinct colors in the second frame's readback, proving real content drew.</summary>
    public int ReadbackDistinctColors { get; private set; }

    /// <summary>Pixels in the second frame that differ from the top-left corner pixel.</summary>
    public int ReadbackNonBackgroundPixels { get; private set; }

    public int RenderedFrames { get; private set; }

    /// <summary>Whether the renderer has been disposed.</summary>
    public bool Disposed { get; private set; }

    /// <summary>Current viewport width in pixels.</summary>
    public int ViewportWidth { get; private set; }

    /// <summary>Current viewport height in pixels.</summary>
    public int ViewportHeight { get; private set; }

    public virtual bool ContinuousRendering => true;

    /// <summary>
    /// Forwards a viewer keyboard shortcut from the host to the renderer. Base renderers ignore keys.
    /// </summary>
    public virtual void OnKeyDown(ViewerKey key)
    {
    }

    private Action<SkiaSharp.SKBitmap>? screenshotCallback;

    /// <summary>
    /// Requests a screenshot of the next rendered frame. The callback runs on the UI thread and owns
    /// the bitmap, which it must dispose.
    /// </summary>
    public void RequestScreenshot(Action<SkiaSharp.SKBitmap> onCaptured)
    {
        screenshotCallback = onCaptured;
    }

    protected ViewportGlRenderer(string label)
    {
        Label = label;
    }

    public void Initialize(GraphicsDevice device, GraphicsContext context, IGLViewerHost host)
    {
        Host = host;

        try
        {
            context.Begin();

            try
            {
                GLEnvironment.Initialize(NullLogger.Instance);
                GLEnvironment.SetDefaultRenderState();
                GLEnvironment.EnableParallelShaderCompile();

                var maxSamples = OpenGL.GetInteger(GetPName.MaxSamples);
                SampleCount = Math.Max(1, Math.Min(Settings.Config.AntiAliasingSamples, maxSamples));

                FileLoader = LinuxGameContent.FileLoader;

                InitializeRenderer(context);
            }
            finally
            {
                context.End();
            }
        }
        catch (Exception e)
        {
            Program.StdOut.WriteLine($"[gl] {Label} initialize failed: {e}");
            throw;
        }
    }

    /// <summary>Creates the viewer's GL resources. The GL context is current.</summary>
    protected abstract void InitializeRenderer(GraphicsContext context);

    /// <summary>Runs the viewer's per-frame work. The GL context is current.</summary>
    protected abstract void RenderFrame(GraphicsContext context, ViewerInputState input, float frameTime);

    /// <summary>Called when the viewport size changes, before the next <see cref="RenderFrame"/>.</summary>
    protected virtual void OnResize(int width, int height)
    {
    }

    /// <summary>Releases the viewer's GL resources. The GL context is current.</summary>
    protected virtual void OnDispose()
    {
    }

    public void Render(GraphicsContext context, int width, int height, ViewerInputState input, double frameTime)
    {
        if (Disposed)
        {
            return;
        }

        context.Begin();

        try
        {
            if (width != ViewportWidth || height != ViewportHeight)
            {
                ViewportWidth = width;
                ViewportHeight = height;
                OnResize(width, height);
            }

            RenderFrame(context, input, (float)frameTime);

            if (RenderedFrames == 0)
            {
                Program.StdOut.WriteLine($"[gl] {Label} first frame {width}x{height}, glError={OpenGL.GetError()}");
                FirstFrameRendered?.Invoke();
            }
            else if (RenderedFrames == 1 && width > 0 && height > 0 && Host is not null)
            {
                ReadBackFrame(width, height);
            }

            if (screenshotCallback is { } capture && RenderedFrames >= 1)
            {
                screenshotCallback = null;
                CaptureScreenshot(width, height, capture);
            }

            RenderedFrames++;
        }
        finally
        {
            context.End();
        }
    }

    private void ReadBackFrame(int width, int height)
    {
        if (Host is null)
        {
            return;
        }

        var screen = Host.ScreenFramebuffer;
        screen.Bind(FramebufferTarget.ReadFramebuffer);

        var pixels = new byte[width * height * 4];
        OpenGL.ReadPixels(0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

        var distinct = new System.Collections.Generic.HashSet<uint>();
        var background = BitConverter.ToUInt32(pixels, 0);
        var nonBackground = 0;

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var pixel = BitConverter.ToUInt32(pixels, i);
            distinct.Add(pixel);

            if (pixel != background)
            {
                nonBackground++;
            }
        }

        ReadbackDistinctColors = distinct.Count;
        ReadbackNonBackgroundPixels = nonBackground;

        Program.StdOut.WriteLine($"[gl] {Label} pixels: distinct={distinct.Count} nonBackground={nonBackground}/{width * height} glError={OpenGL.GetError()}");
    }

    private void CaptureScreenshot(int width, int height, Action<SkiaSharp.SKBitmap> callback)
    {
        if (Host is null || width <= 0 || height <= 0)
        {
            return;
        }

        var screen = Host.ScreenFramebuffer;
        screen.Bind(FramebufferTarget.ReadFramebuffer);

        var pixels = new byte[width * height * 4];
        OpenGL.ReadPixels(0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

        // OpenGL's origin is bottom-left, so flip the rows to produce a top-down image.
        var stride = width * 4;
        var flipped = new byte[pixels.Length];
        for (var row = 0; row < height; row++)
        {
            Array.Copy(pixels, row * stride, flipped, (height - 1 - row) * stride, stride);
        }

        var info = new SkiaSharp.SKImageInfo(width, height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
        using var image = SkiaSharp.SKImage.FromPixelCopy(info, flipped);
        var bitmap = SkiaSharp.SKBitmap.FromImage(image);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                callback(bitmap);
            }
            catch (Exception e)
            {
                Program.StdOut.WriteLine($"[gl] screenshot failed: {e.GetType().Name}: {e.Message}");
            }
        });
    }

    public void Dispose()
    {
        OnDispose();
        MainFramebuffer = null;
        // The file loader is shared process-wide, so it is not disposed with the viewport.
        FileLoader = null;
        Disposed = true;
        GC.SuppressFinalize(this);
    }
}
