using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GUI.Linux.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SkiaSharp;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;
using ImageFormat = ValveResourceFormat.CompiledShader.ImageFormat;
using OpenGL = OpenTK.Graphics.OpenGL.GL;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace GUI.Linux.Types.Browser;

/// <summary>
/// Renders bind-pose thumbnails for model entries through a hidden offscreen GL context, so the
/// package browser can preview models without opening a viewer tab. The context is created lazily and
/// reused for every thumbnail; all rendering is serialized because one GL context can only be current
/// on one thread at a time. Any failure disables the renderer and the caller falls back to an icon.
/// </summary>
internal sealed class ModelThumbnailRenderer : IDisposable
{
    private const int Size = 128;

    // Leave margin around the model, and never frame a near-degenerate box so tightly that the
    // camera ends up inside it.
    private const float FramingPadding = 1.5f;
    private const float MinFramingExtent = 0.5f;

    private static readonly Lazy<ModelThumbnailRenderer> InstanceHolder = new(static () => new ModelThumbnailRenderer());

    private readonly object renderLock = new();

    private NativeWindow? window;
    private GraphicsContext? context;
    private RendererContext? rendererContext;
    private Renderer? renderer;
    private TextRenderer? textRenderer;
    private Framebuffer? framebuffer;
    private bool initialized;
    private bool unavailable;
    private bool disposed;

    /// <summary>The shared renderer.</summary>
    public static ModelThumbnailRenderer Instance => InstanceHolder.Value;

    /// <summary>Diagnostic for the first failure, surfaced by the self-check.</summary>
    public string? LastError { get; private set; }

    /// <summary>Distinct colors in the last rendered thumbnail, used by the self-check.</summary>
    public int LastDistinctColors { get; private set; }

    /// <summary>Pixels differing from the top-left pixel in the last thumbnail, used by the self-check.</summary>
    public int LastNonBackgroundPixels { get; private set; }

    /// <summary>
    /// Renders the model in <paramref name="package"/>/<paramref name="entry"/> in its bind pose, or
    /// returns null when it cannot be rendered.
    /// </summary>
    public Bitmap? Render(Package package, PackageEntry entry)
    {
        if (unavailable || disposed)
        {
            return null;
        }

        // GLFW asserts that its functions are called from the thread that created the window, and the
        // window is created on the application's main (UI) thread. Every GL call therefore runs there,
        // which also serializes the single offscreen context.
        if (Dispatcher.UIThread.CheckAccess())
        {
            return RenderGuarded(package, entry);
        }

        Bitmap? result = null;
        Dispatcher.UIThread.Invoke(() => result = RenderGuarded(package, entry));
        return result;
    }

    private Bitmap? RenderGuarded(Package package, PackageEntry entry)
    {
        lock (renderLock)
        {
            try
            {
                EnsureInitialized();
                return RenderCore(package, entry);
            }
            catch (Exception e)
            {
                LastError = e.ToString();
                Log.Warn(nameof(ModelThumbnailRenderer), $"Model thumbnail failed for {entry.GetFullPath()}: {e.Message}");

                if (!initialized)
                {
                    unavailable = true;
                }

                return null;
            }
        }
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        window = new NativeWindow(new NativeWindowSettings
        {
            APIVersion = GLEnvironment.RequiredVersion,
            ClientSize = new Vector2i(Size, Size),
            Flags = ContextFlags.ForwardCompatible | ContextFlags.Offscreen,
            StartVisible = false,
            Title = "Source 2 Viewer Thumbnail",
        });

        OpenGL.LoadBindings(new GLFWBindingsContext());

        rendererContext = new RendererContext(LinuxGameContent.FileLoader, NullLogger.Instance);
        rendererContext.TextureStreaming.Mode = TextureStreamingMode.Immediate;

        context = rendererContext.Device.CreateContext(new GlfwSurface(window!.Context));
        context.Begin();

        try
        {
            GLEnvironment.Initialize(NullLogger.Instance);
            GLEnvironment.SetDefaultRenderState();
            GLEnvironment.EnableParallelShaderCompile();

            framebuffer = Framebuffer.Prepare("ThumbnailFramebuffer", Size, Size, 1, ImageFormat.RGBA16161616F, ImageFormat.D32);
            framebuffer.Initialize();

            renderer = new Renderer(rendererContext);
            textRenderer = new TextRenderer(rendererContext, renderer.Camera);
            textRenderer.Load();
            renderer.Postprocess.Load(1);

            renderer.Initialize();
            renderer.MainFramebuffer = framebuffer;
            renderer.LoadRendererResources();
            renderer.Camera.SetViewportSize(Size, Size);

            ApplyDefaultLighting(renderer.Scene);

            initialized = true;
        }
        catch
        {
            context.End();
            throw;
        }

        context.End();
    }

    private Bitmap? RenderCore(Package package, PackageEntry entry)
    {
        // The model's materials and textures resolve through the shared search paths.
        if (package.FileName is { Length: > 0 } vpkPath)
        {
            LinuxGameContent.AddSearchPackage(vpkPath);
        }

        context!.Begin();

        try
        {
            var scene = renderer!.Scene;
            scene.Clear();
            rendererContext!.MaxTextureSize = Size;
            framebuffer!.Resize(Size, Size, 1);

            using var stream = ValveResourceFormat.IO.GameFileLoader.GetPackageEntryStream(package, entry);
            using var resource = new Resource { FileName = entry.GetFullPath() };
            resource.Read(stream);

            if (resource.DataBlock is not Model model)
            {
                return null;
            }

            var node = new ModelSceneNode(scene, model, isWorldPreview: true);
            scene.Add(node, true);

            renderer.Camera.SetViewportSize(Size, Size);
            ApplyDefaultLighting(scene);
            scene.Initialize();

            var bounds = node.BoundingBox;
            var size = bounds.Size * FramingPadding;
            var extent = size.Length();

            if (extent < MinFramingExtent)
            {
                size *= MinFramingExtent / MathF.Max(extent, 1e-6f);
            }

            renderer.Camera.FrameObjectFromAngle(bounds.Center, size.X, size.Y, size.Z, yaw: 0.72f, pitch: 0.32f);

            var updateContext = new Scene.UpdateContext
            {
                Camera = renderer.Camera,
                TextRenderer = textRenderer!,
                Timestep = 0,
            };

            renderer.Update(updateContext);

            OpenGL.ClearColor(0, 0, 0, 0);
            OpenGL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            renderer.Render(framebuffer);

            framebuffer.Bind(FramebufferTarget.ReadFramebuffer);
            Framebuffer.GLDefaultFramebuffer.Bind(FramebufferTarget.DrawFramebuffer);
            renderer.PostprocessRender(framebuffer, Framebuffer.GLDefaultFramebuffer, flipY: false);
            OpenGL.Flush();

            var bitmap = ReadPixels();

            // Release GPU mesh/material resources while the resource is still alive.
            scene.Clear();
            return bitmap;
        }
        finally
        {
            context.End();
        }
    }

    private static void ApplyDefaultLighting(Scene scene)
    {
        var angles = Renderer.DefaultSunAngles;
        var sunForward = EntityTransformHelper.EulerAnglesToForwardDirection(new Vector3(angles.X, angles.Y, 0f));
        scene.LightingInfo.LightingData.SunDirection = new Vector4(-sunForward, 0f);

        var sun = Renderer.DefaultSunColor;
        scene.LightingInfo.LightingData.SunColor = new Vector4(new Vector3(sun.X, sun.Y, sun.Z) * sun.W, 1f);
    }

    private Bitmap? ReadPixels()
    {
        Framebuffer.GLDefaultFramebuffer.Bind(FramebufferTarget.ReadFramebuffer);

        var stride = Size * 4;
        var pixels = new byte[stride * Size];
        OpenGL.ReadPixels(0, 0, Size, Size, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

        var distinct = new HashSet<uint>();
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

        LastDistinctColors = distinct.Count;
        LastNonBackgroundPixels = nonBackground;

        // OpenGL's origin is bottom-left; flip the rows for a top-down image.
        var flipped = new byte[pixels.Length];

        for (var row = 0; row < Size; row++)
        {
            Array.Copy(pixels, row * stride, flipped, (Size - 1 - row) * stride, stride);
        }

        var info = new SKImageInfo(Size, Size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var image = SKImage.FromPixelCopy(info, flipped);
        using var bitmap = SKBitmap.FromImage(image);

        return PackageThumbnails.ToAvaloniaBitmap(bitmap);
    }

    private sealed class GlfwSurface(IGLFWGraphicsContext glContext) : IGraphicsSurface
    {
        public void Begin() => glContext.MakeCurrent();

        public void End() => glContext.MakeNoneCurrent();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (context is not null)
        {
            context.Begin();
            renderer?.Dispose();
            context.End();
        }

        rendererContext?.Dispose();
        window?.Dispose();
        GC.SuppressFinalize(this);
    }
}
