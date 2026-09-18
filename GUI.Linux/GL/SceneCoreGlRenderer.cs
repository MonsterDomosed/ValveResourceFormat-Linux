using System;
using System.Linq;
using GUI.Types.GLViewers;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat.Renderer;
using ImageFormat = ValveResourceFormat.CompiledShader.ImageFormat;

namespace GUI.Linux.GL;

/// <summary>
/// Avalonia host for a shared <see cref="GLSceneViewerCore"/>. Subclasses only supply the viewer-specific
/// scene core factory; the GL environment, framebuffer, frame driving and readback come from
/// <see cref="ViewportGlRenderer"/>.
/// </summary>
internal class SceneCoreGlRenderer : ViewportGlRenderer
{
    /// <summary>Builds the viewer-specific scene core once a GL context exists.</summary>
    public delegate GLSceneViewerCore CoreFactory(LinuxSceneViewerContext context, RendererContext rendererContext, IGLViewerHost host);

    private readonly CoreFactory factory;
    private GLSceneViewerCore? core;

    /// <summary>Current renderer camera position, used by the self-check to confirm input moved it.</summary>
    public System.Numerics.Vector3 CameraLocation => core?.Renderer.Camera.Location ?? default;

    protected GLSceneViewerCore? Core => core;

    /// <summary>The hosted scene core, exposed for the self-check to drive keyboard shortcuts.</summary>
    internal GLSceneViewerCore? SceneCore => core;

    public SceneCoreGlRenderer(string label, CoreFactory factory)
        : base(label)
    {
        this.factory = factory;
    }

    protected override void InitializeRenderer(GraphicsContext context)
    {
#pragma warning disable CA2000 // Ownership is transferred to the scene viewer core, which disposes it
        var rendererContext = new RendererContext(FileLoader!, NullLogger.Instance);
#pragma warning restore CA2000

        MainFramebuffer = Framebuffer.Prepare("MainFramebuffer", 4, 4, SampleCount, ImageFormat.RGBA16161616F, ImageFormat.D32);
        MainFramebuffer.Initialize();

        var sceneContext = new LinuxSceneViewerContext(NullLogger.Instance);
        core = factory(sceneContext, rendererContext, Host!);
        core.Load(MainFramebuffer, SampleCount);

        Program.StdOut.WriteLine($"[gl] {Label} scene loaded: {core.Scene.AllNodes.Count()} nodes, {SampleCount}x MSAA");
    }

    protected override void RenderFrame(GraphicsContext context, ViewerInputState input, float frameTime)
    {
        if (core is null)
        {
            return;
        }

        core.Paused = false;
        core.Update(frameTime);
        core.Paint(frameTime);
    }

    protected override void OnResize(int width, int height) => core?.Resize(width, height);

    public override void OnKeyDown(ViewerKey key) => core?.OnKeyDown(key);

    protected override void OnDispose() => core?.Dispose();
}
