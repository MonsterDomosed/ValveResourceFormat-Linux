using System;
using System.Numerics;
using GUI.Linux.Types.GLViewers;
using GUI.Linux.Types.Graphs.Core;
using SkiaSharp;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.Shaders;
using FramebufferTarget = OpenTK.Graphics.OpenGL.FramebufferTarget;
using OpenGL = OpenTK.Graphics.OpenGL.GL;

namespace GUI.Linux.GL;

/// <summary>
/// Linux graph viewer renderer. The shared <see cref="GraphView"/> draws the graph into a CPU
/// <see cref="SKSurface"/>, the bitmap is uploaded through the shared material loader and shown with
/// the internal <c>texture_decode</c> shader. Pan/zoom and node interaction are driven from the
/// neutral <see cref="ViewerInputState"/>, so the graph presenter itself is identical on Windows.
/// </summary>
internal sealed class GraphGlRenderer : ViewportGlRenderer
{
    private readonly Func<GraphView, IDisposable?> build;

    private IDisposable? builder;
    private GraphView? view;
    private RendererContext? rendererContext;
    private Shader? shader;
    private RenderTexture? graphTexture;
    private SKBitmap? bitmap;

    private SKRect graphBounds;
    private bool needsFit = true;
    private float scale = 1f;
    private Vector2 position;

    // Raster cache: the graph is only re-drawn and re-uploaded when something it depends on changes.
    private (int Width, int Height, float Scale, Vector2 Position, int Version, SKRect Bounds)? rasterKey;

    private bool previousLeft;

    public GraphGlRenderer(Func<GraphView, IDisposable?> build)
        : base("graph")
    {
        this.build = build;
    }

    /// <summary>Nodes in the presented graph, for diagnostics.</summary>
    public int NodeCount => view?.NodeCount ?? 0;

    /// <summary>Wires in the presented graph, for diagnostics.</summary>
    public int WireCount => view?.WireCount ?? 0;

    /// <summary>Current zoom factor, for diagnostics.</summary>
    public float Scale => scale;

    protected override void InitializeRenderer(GraphicsContext context)
    {
#pragma warning disable CA2000 // Renderer resources are released in OnDispose
        rendererContext = new RendererContext(FileLoader!, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
#pragma warning restore CA2000

        view = new GraphView(GraphPalette.Default);
        builder = build(view);

        shader = rendererContext.ShaderLoader.LoadShader(
            "texture_decode",
            (TextureViewerShader.GetTextureTypeDefine(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D), (byte)1));

        Program.StdOut.WriteLine($"[gl] graph loaded: {view.NodeCount} nodes, {view.WireCount} wires");
    }

    protected override void RenderFrame(GraphicsContext context, ViewerInputState input, float frameTime)
    {
        if (view is null || shader is null || rendererContext is null || Host is null || ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            return;
        }

        if (needsFit)
        {
            graphBounds = view.GetGraphBounds();
            FitToViewport();
        }

        HandleInput(input);

        // Moving nodes changes the graph bounds; keep the origin stable so the view does not jump.
        var newGraphBounds = view.GetGraphBounds();
        var deltaLeft = newGraphBounds.Left - graphBounds.Left;
        var deltaTop = newGraphBounds.Top - graphBounds.Top;

        if (deltaLeft != 0f || deltaTop != 0f)
        {
            position -= new Vector2(deltaLeft * scale, deltaTop * scale);
        }

        graphBounds = newGraphBounds;

        EnsureRaster();

        var fbo = Host.ScreenFramebuffer;
        OpenGL.Viewport(0, 0, ViewportWidth, ViewportHeight);
        fbo.ClearColor = new OpenTK.Mathematics.Color4(0.13f, 0.15f, 0.2f, 1f);
        fbo.ClearMask = OpenTK.Graphics.OpenGL.ClearBufferMask.ColorBufferBit;
        fbo.BindAndClear(FramebufferTarget.Framebuffer);

        OpenGL.DepthMask(false);
        OpenGL.Disable(OpenTK.Graphics.OpenGL.EnableCap.DepthTest);

        shader.Use();
        shader.SetUniform("g_bTextureViewer", true);
        shader.SetUniform("g_bShowLightBackground", false);
        shader.SetUniform("g_vViewportSize", new Vector2(ViewportWidth, ViewportHeight));
        shader.SetUniform("g_vCheckerboardTheme", new Vector3(0.13f, 0.15f, 0.2f));

        shader.SetUniform("g_bCapturingScreenshot", false);
        shader.SetUniform("g_vViewportPosition", Vector2.Zero);
        shader.SetUniform("g_flScale", 1f);

        shader.SetTexture(0, "g_tInputTexture", graphTexture!);
        shader.SetUniform("g_vInputTextureSize", new Vector4(ViewportWidth, ViewportHeight, 1f, 1f));
        shader.SetUniform("g_nSelectedMip", 0);
        shader.SetUniform("g_nSelectedDepth", 0);
        shader.SetUniform("g_nSelectedCubeFace", 0);
        shader.SetUniform("g_nSelectedChannels", (int)ChannelMapping.RGBA.PackedValue);
        shader.SetUniform("g_bVisualizeTiling", false);
        shader.SetUniform("g_nChannelSplitMode", 0);
        shader.SetUniform("g_nCubemapProjectionType", 0);
        shader.SetUniform("g_nDecodeFlags", 0);
        shader.SetUniform("g_nSpriteSheetMode", 0);
        shader.SetUniform("g_vSpriteFrameMinMax", new Vector4(0f, 0f, 1f, 1f));

        OpenGL.BindVertexArray(rendererContext.MeshBufferCache.EmptyVAO);
        OpenGL.DrawArrays(OpenTK.Graphics.OpenGL.PrimitiveType.Triangles, 0, 3);
    }

    private void EnsureRaster()
    {
        var key = (ViewportWidth, ViewportHeight, scale, position, view!.VisualVersion, graphBounds);

        if (rasterKey == key)
        {
            return;
        }

        rasterKey = key;

        if (bitmap is null || bitmap.Width != ViewportWidth || bitmap.Height != ViewportHeight)
        {
            bitmap?.Dispose();
            bitmap = new SKBitmap(new SKImageInfo(ViewportWidth, ViewportHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        }

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Translate(-position.X, -position.Y);
            canvas.Scale(scale, scale);
            canvas.Translate(-graphBounds.Left, -graphBounds.Top);

            var visibleRect = new SKRect(
                position.X / scale + graphBounds.Left,
                position.Y / scale + graphBounds.Top,
                (position.X + ViewportWidth) / scale + graphBounds.Left,
                (position.Y + ViewportHeight) / scale + graphBounds.Top);

            visibleRect.Inflate(50f / scale, 50f / scale);

            view.RenderToCanvas(canvas, visibleRect, scale);
        }

        var uploaded = MaterialLoader.LoadBitmapTexture(bitmap);
        graphTexture?.Delete();
        graphTexture = uploaded;
    }

    private void HandleInput(ViewerInputState input)
    {
        if (view is null)
        {
            return;
        }

        if (input.Wheel != 0f)
        {
            ZoomAtCursor(input);
        }

        var graphPoint = ScreenToGraph(input.X, input.Y);
        var modifiers = CurrentModifiers(input);

        var leftPressed = input.Left && !previousLeft;
        var leftReleased = !input.Left && previousLeft;

        if (leftPressed)
        {
            view.HandleMouseDown(graphPoint, GraphMouseButton.Left, modifiers);
        }

        if (input.Left && view.IsMoving)
        {
            view.HandleMouseMove(graphPoint, modifiers);
        }
        else if ((input.Left || input.Middle) && !view.IsMoving)
        {
            // Pan when the drag did not land on a node, or on middle mouse anywhere.
            position -= input.Delta;
        }
        else if (!input.Left && !input.Middle && !view.IsMoving)
        {
            view.HandleMouseMove(graphPoint, modifiers);
        }

        if (leftReleased)
        {
            view.HandleMouseUp(graphPoint, GraphMouseButton.Left);
        }

        previousLeft = input.Left;
    }

    private static GraphModifiers CurrentModifiers(ViewerInputState input)
    {
        var modifiers = GraphModifiers.None;

        if ((input.Keys & ViewerKey.Shift) != 0)
        {
            modifiers |= GraphModifiers.Shift;
        }

        if ((input.Keys & ViewerKey.Control) != 0)
        {
            modifiers |= GraphModifiers.Control;
        }

        if ((input.Keys & ViewerKey.Alt) != 0)
        {
            modifiers |= GraphModifiers.Alt;
        }

        return modifiers;
    }

    private void ZoomAtCursor(ViewerInputState input)
    {
        var cursor = new Vector2(input.X, input.Y);
        var before = (cursor + position) / scale;

        scale = Math.Clamp(scale * (input.Wheel > 0f ? 1.25f : 1f / 1.25f), MinScale(), 2f);
        position = before * scale - cursor;
    }

    private float MinScale()
    {
        if (graphBounds.IsEmpty || ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            return 0.01f;
        }

        var fitScale = Math.Min(ViewportWidth / graphBounds.Width, ViewportHeight / graphBounds.Height);
        return Math.Max(0.01f, Math.Min(1f, 0.3f * fitScale));
    }

    private void FitToViewport()
    {
        if (graphBounds.IsEmpty || ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            return;
        }

        var scaleX = (ViewportWidth * 0.9f) / graphBounds.Width;
        var scaleY = (ViewportHeight * 0.9f) / graphBounds.Height;
        scale = Math.Clamp(Math.Min(scaleX, scaleY), MinScale(), 2f);

        position = new Vector2(
            -(ViewportWidth - graphBounds.Width * scale) / 2f,
            -(ViewportHeight - graphBounds.Height * scale) / 2f);

        needsFit = false;
    }

    /// <summary>Converts a viewport pixel into graph coordinates using the active transform.</summary>
    private SKPoint ScreenToGraph(float screenX, float screenY)
        => new(screenX / scale + position.X / scale + graphBounds.Left, screenY / scale + position.Y / scale + graphBounds.Top);

    protected override void OnDispose()
    {
        builder?.Dispose();
        builder = null;
        view?.Dispose();
        view = null;
        bitmap?.Dispose();
        bitmap = null;
        graphTexture?.Delete();
        graphTexture = null;
        shader = null;
        rendererContext?.Dispose();
        rendererContext = null;
    }
}
