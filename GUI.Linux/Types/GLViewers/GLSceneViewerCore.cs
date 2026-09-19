using System.Diagnostics;
using System.Linq;
using GUI.Linux.Utils;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Utils;
using static ValveResourceFormat.Renderer.PickingTexture;
using ImageFormat = ValveResourceFormat.CompiledShader.ImageFormat;
using OpenGL = OpenTK.Graphics.OpenGL.GL;

namespace GUI.Linux.Types.GLViewers;

/// <summary>
/// Platform-neutral scene viewer. Owns the renderer, scene, camera/input state and the whole
/// load/update/paint/resize flow. The Avalonia host drives it by forwarding GL context
/// ownership, size, input and presentation; viewer-specific behaviour is supplied by subclasses.
/// </summary>
public abstract class GLSceneViewerCore : IDisposable
{
    public ValveResourceFormat.Renderer.Renderer Renderer { get; }
    public UserInput Input { get; protected set; }

    public ValveResourceFormat.Renderer.TextRenderer TextRenderer { get; protected set; }
    private readonly CrosshairRenderer crosshairRenderer;

    public PickingTexture? Picker { get; set; }

    public QuadOverdraw? QuadOverdrawRenderer { get; set; }

    public Scene Scene { get; }
    public Scene? SkyboxScene => Renderer.SkyboxScene;
    public ISceneViewerContext Context { get; }

    /// <summary>The GL host driving this core (input, presentation, frame requests).</summary>
    protected IGLViewerHost Host { get; }

    public Framebuffer? MainFramebuffer { get; private set; }
    protected Framebuffer ScreenFramebuffer => Host.ScreenFramebuffer;
    protected int NumSamples { get; private set; } = 1;

    public bool Paused { get; set; } = true;

    public bool ShowBaseGrid { get; set; }
    public bool ShowLightBackground { get; set; }
    public bool ShowSolidBackground { get; set; }
    public bool ShowStaticOctree { get; set; }
    public bool ShowDynamicOctree { get; set; }
    public bool ShowVisDebug { get; set; }
    public bool ShowPhysicsTraces { get; set; }

    public bool ShowSpeed { get; set; }
    private PhysicsTraceDebugRenderer? physicsTraceRenderer;

    private enum PerfDisplay
    {
        Off,
        Stats,
        Timings,
        Allocations,
    }

    private PerfDisplay perfDisplay;

    /// <summary>Set by escape to release the mouse in walk mode, cleared by clicking back into the viewport.</summary>
    private bool mouseReleased;
    private bool roundStarted;

    private readonly List<RenderModes.RenderMode> renderModes = new(RenderModes.Items.Count);
    private InfiniteGrid? baseGrid;
    public SelectedNodeRenderer? SelectedNodeRenderer { get; set; }

    private static readonly TimeSpan FpsUpdateTimeSpan = TimeSpan.FromSeconds(0.1);

    private readonly float[] frameTimes = new float[30];
    private int frameTimeNextId;
    private int frameTimeCount;

    private readonly ValveResourceFormat.Renderer.TextRenderer.TextBuffer fpsText = new("FPS: 10000  CPU: 10000.0ms  GPU: 10000.0ms");
    private readonly ValveResourceFormat.Renderer.TextRenderer.TextBuffer speedText = new("Speed: 100000.0 u/s");
    private int frametimeQuery1;
    private int frametimeQuery2;

    private long lastFrameTimestamp;
    private long lastFpsUpdate;

    public Vector2 SunAngles { get; set; }
    private bool loadedDefaultLighting;

    private Vector2 lastMouseDelta;
    private Vector2 pointerDownPosition;

    /// <summary>Whether the scene is centered on the first node's bounds after loading.</summary>
    protected virtual bool CenterCameraOnNodes => true;

    /// <summary>Uses the world viewer's camera offset mode.</summary>
    protected virtual bool IsWorldViewer => false;

    /// <summary>Uses the animation viewer's camera offset mode.</summary>
    protected virtual bool IsAnimationViewer => false;

    protected GLSceneViewerCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host)
    {
        Context = context;
        Host = host;

        Renderer = new(rendererContext);
        Input = new UserInput(Renderer);
        TextRenderer = new(rendererContext, Renderer.Camera);
        crosshairRenderer = new CrosshairRenderer(rendererContext);
        Scene = Renderer.Scene;
    }

    protected GLSceneViewerCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, Frustum cullFrustum)
        : this(context, rendererContext, host)
    {
        Renderer.LockedCullFrustum = cullFrustum;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases viewer GL resources and the renderer.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        // Delete GL resources before the host disposes the GL context
        physicsTraceRenderer?.Delete();
        physicsTraceRenderer = null;

        DisposeAudio();

        QuadOverdrawRenderer?.Dispose();
        QuadOverdrawRenderer = null;

        Renderer?.Dispose();
    }

    /// <summary>Deletes viewer-specific GL resources before the renderer is disposed.</summary>
    protected virtual void DisposeAudio()
    {
    }

    /// <summary>Plays/updates sound if the viewer has any; no-op in the shared core.</summary>
    protected virtual void UpdateAudio(float frameTime)
    {
    }

    /// <summary>Advances audio inside the frame's timing bracket, if the viewer has any.</summary>
    protected virtual void UpdateAudioInFrame()
    {
    }

    /// <summary>Runs viewer-specific simulation just before a frame is rendered.</summary>
    protected virtual void OnPrePaint(float frameTime)
    {
    }

    /// <summary>Called when the viewer is detached from the render loop so audio can be suspended.</summary>
    public virtual void OnDetachedFromRenderLoop()
    {
        Paused = true;
    }

    /// <summary>
    /// Called once with the GL context current. Sets up renderer resources, loads the scene and
    /// initializes it. <paramref name="mainFramebuffer"/> is the offscreen target the host allocated.
    /// </summary>
    public void Load(Framebuffer mainFramebuffer, int sampleCount)
    {
        MainFramebuffer = mainFramebuffer;
        NumSamples = sampleCount;

        ReportLoadingStatus("Preparing renderer…");

        frametimeQuery1 = GraphicsDevice.CreateQuery(QueryTarget.TimeElapsed, "Frame Time Query");
        frametimeQuery2 = GraphicsDevice.CreateQuery(QueryTarget.TimeElapsed, "Frame Time Query");

        // Needed to fix crash on certain drivers
        OpenGL.BeginQuery(QueryTarget.TimeElapsed, frametimeQuery2);
        OpenGL.EndQuery(QueryTarget.TimeElapsed);

        TextRenderer.Load();
        Renderer.Postprocess.Load(NumSamples);

        Renderer.Postprocess.FullScreenGamma = 2.01f; // 100% Brightness
        Renderer.Postprocess.ExposureCompensation = -0.4f; // eyeballed

        baseGrid = new InfiniteGrid(Scene);
        SelectedNodeRenderer = new(Scene.RendererContext);
        Picker = new(Scene.RendererContext, OnPicked);

        QuadOverdrawRenderer = new(Scene.RendererContext);
        QuadOverdrawRenderer.Load();

        Renderer.ShadowTextureSize = Settings.Config.ShadowResolution;
        Renderer.Initialize();

        Renderer.MainFramebuffer = MainFramebuffer;

        MainFramebuffer.Bind(FramebufferTarget.Framebuffer);

        var timer = Stopwatch.StartNew();
        PreSceneLoad();
        LoadScene();
        timer.Stop();
        Context.SceneLogger.LogDebug("Loading scene time: {Elapsed}, shader variants: {Shaders}, materials: {Materials}", timer.Elapsed, Scene.RendererContext.ShaderLoader.ShaderCount, Scene.RendererContext.MaterialLoader.MaterialCount);

        ReportLoadingStatus("Initializing scene…");

        PostSceneLoad();

        Context.ClearCache();
        OnPostLoad();
    }

    /// <summary>Hook for viewer-specific post-load work such as camera setup.</summary>
    protected virtual void OnPostLoad()
    {
    }

    public virtual void PreSceneLoad()
    {
        RunPreSceneLoad();
    }

    /// <summary>Loads the renderer resources without going through the virtual hook.</summary>
    public void RunPreSceneLoad()
    {
        Renderer.LoadRendererResources();
    }

    public virtual void LoadDefaultLighting()
    {
        using var stream = Context.OpenDefaultCubemapStream();

        if (stream == null)
        {
            return;
        }

        using var resource = new ValveResourceFormat.Resource
        {
            FileName = "vrf_default_cubemap.vtex_c"
        };
        resource.Read(stream);

        Renderer.LoadDefaultLighting(Scene, resource);

        SunAngles = Renderer.DefaultSunAngles;
        loadedDefaultLighting = true;
    }

    public void UpdateSunAngles()
    {
        var angles = SunAngles;
        angles.X = Math.Clamp(angles.X, 0f, 89f);
        angles.Y %= 360f;
        SunAngles = angles;

        Scene.LightingInfo.SetSunDirectionFromAngles(new Vector3(angles.X, angles.Y, 0f));
    }

    public virtual void PostSceneLoad()
    {
        RunPostSceneLoad();
    }

    /// <summary>Initializes the loaded scene without going through the virtual hook.</summary>
    public void RunPostSceneLoad()
    {
        Scene.Initialize();

        if (Scene.PhysicsWorld != null)
        {
            Input.PhysicsWorld = Scene.PhysicsWorld;
        }

        SkyboxScene?.Initialize();

        if (Scene.FogInfo.CubeFogActive)
        {
            var cubemapTexture = Scene.FogInfo.CubemapFog?.CubemapFogTexture;

            if (cubemapTexture != null)
            {
                Renderer.Textures.RemoveAll(t => t.Slot == ReservedTextureSlots.FogCubeTexture);
                Renderer.Textures.Add(new(ReservedTextureSlots.FogCubeTexture, "g_tFogCubeTexture", cubemapTexture));
            }
        }

        if (CenterCameraOnNodes && Scene.AllNodes.Any())
        {
            var first = true;
            var bbox = new AABB();

            foreach (var node in Scene.AllNodes)
            {
                if (first)
                {
                    first = false;
                    bbox = node.BoundingBox;
                    continue;
                }

                bbox = bbox.Union(node.BoundingBox);
            }

            // If there is no bbox, LookAt will break camera, so +1 to location
            var offset = Math.Max(bbox.Max.X, Math.Max(bbox.Max.Y, bbox.Max.Z)) + 1f * 1.5f;
            offset = Math.Clamp(offset, 0f, 2000f);
            var location = new Vector3(offset, 0, offset);

            if (IsAnimationViewer)
            {
                location = new(offset);
            }

            Input.Camera.SetLocation(location);
            Input.Camera.LookAt(bbox.Center);
        }

        Scene.StaticOctree.DebugRenderer = new(Scene.StaticOctree, Scene.RendererContext, false);
        Scene.DynamicOctree.DebugRenderer = new(Scene.DynamicOctree, Scene.RendererContext);
    }

    protected abstract void LoadScene();

    protected virtual void OnPicked(object? sender, PickingResponse pixelInfo)
    {
    }

    /// <summary>Resizes the render targets and cameras to a new viewport size in pixels.</summary>
    public void Resize(int w, int h)
    {
        if (w <= 0 || h <= 0 || MainFramebuffer is null)
        {
            return;
        }

        MainFramebuffer.Resize(w, h, NumSamples);

        Renderer.Camera.SetViewportSize(w, h);

        // The input camera frames objects against its own aspect ratio, so it needs the size too
        Input.Camera.SetViewportSize(w, h);

        Picker?.Resize(w, h);
    }

    /// <summary>Handles a mouse/touch press inside the viewport.</summary>
    public void OnPointerDown(float x, float y, ViewerKey buttons, int clickCount)
    {
        pointerDownPosition = new Vector2(x, y);
        mouseReleased = false;

        if (Input.WalkMode)
        {
            return;
        }

        if (buttons.HasFlag(ViewerKey.MouseLeft) && clickCount == 2)
        {
            var intent = (buttons & ViewerKey.Control) != 0
                ? PickingIntent.Open
                : PickingIntent.Details;
            Picker?.RequestNextFrame((int)x, (int)y, intent);
        }
    }

    /// <summary>Handles a mouse/touch release inside the viewport, picking when it was a click.</summary>
    public void OnPointerUp(float x, float y, ViewerKey buttons)
    {
        if (Input.WalkMode)
        {
            return;
        }

        var dragged = Vector2.Distance(pointerDownPosition, new Vector2(x, y)) > 2f;

        if (!dragged)
        {
            Picker?.RequestNextFrame((int)pointerDownPosition.X, (int)pointerDownPosition.Y, PickingIntent.Select);
        }
    }

    /// <summary>Handles a wheel event; updates the zoom/move-speed label.</summary>
    public void OnPointerWheel(float delta)
    {
        if (Input.WalkMode || delta == 0)
        {
            return;
        }

        Input.OnMouseWheel(delta);
    }

    /// <summary>Handles a keyboard key press for viewer shortcuts.</summary>
    public void OnKeyDown(ViewerKey key)
    {
        if (key == ViewerKey.Tab)
        {
            PerfDisplayMode = (PerfDisplayMode + 1) % 4;
            return;
        }

        if (SelectedNodeRenderer is null)
        {
            return;
        }

        if (key == ViewerKey.Delete)
        {
            SelectedNodeRenderer.DisableSelectedNodes();
            return;
        }

        if (key == ViewerKey.Escape)
        {
            SelectedNodeRenderer.SelectNode(null);
            mouseReleased = true;
        }
    }

    /// <summary>Gets or sets the performance overlay mode (0 = off) shown in the corner.</summary>
    public int PerfDisplayMode
    {
        get => (int)perfDisplay;
        set => perfDisplay = (PerfDisplay)Math.Clamp(value, 0, 3);
    }

    /// <summary>Advances simulation, camera and input for one frame.</summary>
    public void Update(float frameTime)
    {
        UpdateAudio(frameTime);

        var input = Host.Input;

        Input.EnableMouseLook = true;

        if (loadedDefaultLighting && Input.NoClip && (input.Keys & ViewerKey.Control) != 0)
        {
            var delta = new Vector2(lastMouseDelta.Y, lastMouseDelta.X);

            SunAngles += delta;
            Scene.AdjustEnvMapSunAngle(Matrix4x4.CreateRotationZ(-delta.Y / 80f));
            UpdateSunAngles();
            Scene.UpdateBuffers();
            Input.EnableMouseLook = false;
        }

        if (!input.MouseOverViewport && !Input.ForceUpdate && !Input.WalkMode)
        {
            lastMouseDelta = input.Delta;
            input.EndFrame();
            return;
        }

        Input.MouseSensitivity = Settings.Config.MouseSensitivity;
        Input.SmoothCameraEnabled = Settings.Config.SmoothCameraEnabled;

        var pressedKeys = ToTrackedKeys(input.Keys);
        var mouseDelta = input.Delta;
        lastMouseDelta = mouseDelta;

        var wasWalkMode = Input.WalkMode;
        Input.Tick(frameTime, pressedKeys, mouseDelta, Renderer.Camera);

        // cancel unintentional selection
        if (!wasWalkMode && Input.WalkMode)
        {
            SelectedNodeRenderer?.SelectNode(null);

            if (!roundStarted)
            {
                roundStarted = true;
                Scene.EntitySystem.StartRound();
            }
        }

        var wantsMouseLook = Input.WalkMode && !Paused && !mouseReleased;
        var alreadyHoldingCursor = input.Captured;
        input.Captured = wantsMouseLook && (alreadyHoldingCursor || input.MouseOverViewport);

        input.EndFrame();
    }

    /// <summary>Renders one frame.</summary>
    public void Paint(float frameTime)
    {
        Debug.Assert(MainFramebuffer != null);
        Debug.Assert(Picker != null);
        Debug.Assert(SelectedNodeRenderer != null);

        OnPrePaint(frameTime);

        Renderer.PerfStats.Capture = perfDisplay == PerfDisplay.Stats;
        Renderer.PerfStats.Timings.Capture = perfDisplay == PerfDisplay.Timings;
        Renderer.PerfStats.Allocations.Capture = perfDisplay == PerfDisplay.Allocations;

        Renderer.PerfStats.MarkFrameBegin();
        OpenGL.BeginQuery(QueryTarget.TimeElapsed, frametimeQuery1);

        var renderContext = new Scene.RenderContext
        {
            Camera = Renderer.Camera,
            Framebuffer = MainFramebuffer,
            Textures = Renderer.Textures,
            Scene = Scene,
        };

        using (new GLDebugGroup("Update Loop"))
        {
            var updateContext = new Scene.UpdateContext
            {
                TextRenderer = TextRenderer,
                Timestep = frameTime,
                Camera = Renderer.Camera,
            };

            Renderer.Update(updateContext);

            Input.LateUpdate(Renderer.Camera);

            SelectedNodeRenderer.Update(renderContext, updateContext);
        }

        UpdateAudioInFrame();

        Renderer.ForceResolveSceneDepth = ShowBaseGrid;

        var quadOverdrawThisFrame = false;

        using (new GLDebugGroup("Scenes Render"))
        {
            if (Picker.ActiveNextFrame)
            {
                using var _ = new GLDebugGroup("Picker Object Id Render");
                renderContext.ReplacementShader = Picker.Shader;
                renderContext.Framebuffer = Picker;

                Renderer.RenderScenesWithView(renderContext);
                Picker.Finish();
            }
            else if (Picker.IsDebugActive)
            {
                renderContext.ReplacementShader = Picker.DebugShader;
            }
            else if (QuadOverdrawRenderer?.IsActive == true)
            {
                QuadOverdrawRenderer.Prepare(MainFramebuffer.Width, MainFramebuffer.Height);

                quadOverdrawThisFrame = true;
            }

            Renderer.Render(renderContext);

            if (quadOverdrawThisFrame)
            {
                using (new GLDebugGroup("Quad Overdraw Counting Pass"))
                {
                    QuadOverdrawRenderer!.BeginCountingPass(MainFramebuffer);

                    renderContext.OverdrawShader = QuadOverdrawRenderer.SceneShader;
                    Renderer.RenderScenesWithView(renderContext);
                    renderContext.OverdrawShader = null;

                    QuadOverdrawRenderer.EndCountingPass(MainFramebuffer);
                }

                QuadOverdrawRenderer!.Render();
            }
        }

        using (new GLDebugGroup("Lines Render"))
        {
            SelectedNodeRenderer.Render();

            if (ShowStaticOctree && Scene.StaticOctree.DebugRenderer != null)
            {
                Scene.StaticOctree.DebugRenderer.Render();
            }

            if (ShowDynamicOctree && Scene.DynamicOctree.DebugRenderer != null)
            {
                Scene.DynamicOctree.DebugRenderer.Render();
            }

            if (Scene.OcclusionDebugEnabled && Scene.OcclusionDebug != null)
            {
                Scene.OcclusionDebug.Render();
            }

            if (ShowPhysicsTraces && Scene.PhysicsWorld != null)
            {
                physicsTraceRenderer ??= new PhysicsTraceDebugRenderer(Scene.RendererContext);
                physicsTraceRenderer.Render(Scene.PhysicsWorld, Input, Renderer.Camera);
            }

            if (ShowBaseGrid && baseGrid != null)
            {
                baseGrid.Render();

                DrawWorldSpaceText("+X", 10f, Vector3.UnitX * 120f, Color32.Red, renderContext);
                DrawWorldSpaceText("-X", 10f, -Vector3.UnitX * 120f, Color32.Red, renderContext);
                DrawWorldSpaceText("+Y", 10f, Vector3.UnitY * 120f, Color32.Green, renderContext);
                DrawWorldSpaceText("-Y", 10f, -Vector3.UnitY * 120f, Color32.Green, renderContext);
            }
        }

        OpenGL.EndQuery(QueryTarget.TimeElapsed);

        if (Paused)
        {
            DrawLowerCornerText("Paused", new(255, 100, 0));
        }
        else if (Settings.Config.DisplayFps != 0)
        {
            var currentTime = Stopwatch.GetTimestamp();
            var fpsElapsed = Stopwatch.GetElapsedTime(lastFpsUpdate, currentTime);

            // Zero length frames (the first frame after resuming) would inflate the average.
            if (frameTime > 0f)
            {
                frameTimes[frameTimeNextId++] = frameTime;
                frameTimeNextId %= frameTimes.Length;
                frameTimeCount = Math.Min(frameTimeCount + 1, frameTimes.Length);
            }

            if (frameTimeCount > 0 && fpsElapsed >= FpsUpdateTimeSpan)
            {
                var frametimeQuery = frametimeQuery2;
                frametimeQuery2 = frametimeQuery1;
                frametimeQuery1 = frametimeQuery;

                OpenGL.GetQueryObject(frametimeQuery, GetQueryObjectParam.QueryResultNoWait, out long gpuTime);
                var gpuFrameTime = gpuTime / 1_000_000f;

                var frameTimeSum = 0f;

                // Only the samples written so far, the rest of the ring is still zeroed.
                for (var i = 0; i < frameTimeCount; i++)
                {
                    frameTimeSum += frameTimes[i];
                }

                var fps = frameTimeCount / frameTimeSum;
                var cpuFrameTime = Stopwatch.GetElapsedTime(lastFrameTimestamp, currentTime).TotalMilliseconds;

                lastFpsUpdate = currentTime;
                fpsText.Format($"FPS: {fps,-3:0}  CPU: {cpuFrameTime,-4:0.0}ms  GPU: {gpuFrameTime,-4:0.0}ms");
            }

            DrawLowerCornerText(fpsText, Color32.White);
        }

        BlitFramebufferToScreen();

        if (Input.ShowCrosshair)
        {
            crosshairRenderer.Render(Renderer.Camera);
        }

        if (Host.Input.Captured && ShowSpeed)
        {
            TextRenderer.AddTextRelative(new ValveResourceFormat.Renderer.TextRenderer.TextRenderRequest
            {
                X = 0.5f,
                Y = 0.85f,
                Scale = 12f,
                Color = Color32.Yellow,
                Text = speedText.Format($"Speed: {Input.Velocity.AsVector2().Length():0.0} u/s"),
                CenterHorizontal = true,
            }, Renderer.Camera);
        }

        if (ShowVisDebug && Scene.VoxelVisibility != null)
        {
            var pvsPos = Renderer.LockedCullPosition ?? Renderer.Camera.Location;
            var cluster = Scene.VoxelVisibility.GetClusterForPosition(pvsPos);
            var y = 18f;

            void AddLine(string text, Color32 color)
            {
                TextRenderer.AddText(new ValveResourceFormat.Renderer.TextRenderer.TextRenderRequest
                {
                    X = 4f,
                    Y = y,
                    Scale = 14f,
                    Color = color,
                    Text = text,
                });
                y += 16f;
            }

            AddLine(
                cluster <= 1 ? "No PVS at this position" : $"PVS cluster {cluster}",
                cluster <= 1 ? new Color32(255, 0, 0) : Color32.White
            );

            if (Scene.CurrentFramePvs != null)
            {
                var visCount = Scene.CurrentFramePvs.Sum(b => BitOperations.PopCount(b));
                AddLine($"PVS visible: {visCount}/{Scene.VoxelVisibility.BaseClusterCount} clusters", Color32.White);
            }
        }

        if (perfDisplay == PerfDisplay.Stats)
        {
            Renderer.PerfStats.DisplayStats(TextRenderer, Renderer.Camera, Scene, SkyboxScene);
        }
        else if (perfDisplay == PerfDisplay.Timings)
        {
            Renderer.PerfStats.Timings.DisplayTimings(TextRenderer, Renderer.Camera);
        }
        else if (perfDisplay == PerfDisplay.Allocations)
        {
            Renderer.PerfStats.Allocations.DisplayAllocations(TextRenderer, Renderer.Camera);
        }

        TextRenderer.Render(Renderer.Camera, Renderer.ResolvedSceneDepth);
        Picker?.TriggerEventIfAny();

        Renderer.PerfStats.MarkFrameEnd();
    }

    public void DrawLowerCornerText(ValveResourceFormat.Renderer.TextRenderer.TextMemory text, Color32 color, int lineFromBottom = 0)
    {
        Debug.Assert(MainFramebuffer != null);

        TextRenderer.AddText(new ValveResourceFormat.Renderer.TextRenderer.TextRenderRequest
        {
            X = 2f,
            Y = MainFramebuffer.Height - 4f - lineFromBottom * 16f,
            Scale = 14f,
            Color = color,
            Text = text
        });
    }

    protected void DrawWorldSpaceText(string text, float size, Vector3 position, Color32 color, Scene.RenderContext renderContext)
    {
        Scene.WantsSceneDepth = true;
        TextRenderer.AddTextBillboard(position, new ValveResourceFormat.Renderer.TextRenderer.TextRenderRequest
        {
            Scale = size,
            Color = color,
            Text = text,
            CenterVertical = true,
            CenterHorizontal = true,
        }, renderContext.Camera, depthMask: true);
    }

    protected virtual void BlitFramebufferToScreen()
    {
        Debug.Assert(MainFramebuffer != null);
        Renderer.PostprocessRender(MainFramebuffer, ScreenFramebuffer);
    }

    /// <summary>Called by the host after the swap so timing can be recorded.</summary>
    public virtual void OnBufferSwapped(double blockedMs, double framePeriodMs)
    {
        Renderer.PerfStats.Timings.SetBufferSwapTime(blockedMs, framePeriodMs);
    }

    /// <summary>Called by the host once per second-ish frame to advance the frame clock.</summary>
    public void MarkFrameRendered(long timestamp) => lastFrameTimestamp = timestamp;

    /// <summary>Runs the shader/scene warm-up draw. Must run with the GL context current.</summary>
    public void PrewarmRenderer()
    {
        ReportLoadingStatus("Compiling shaders…");

        var start = Stopwatch.GetTimestamp();

        PrewarmDrawCalls();

        Context.SceneLogger.LogDebug("Prewarm time: {Elapsed}", Stopwatch.GetElapsedTime(start));
    }

    /// <summary>
    /// Renders one full frame with culling disabled so the driver specializes every
    /// (program, vertex layout, framebuffer) combination once.
    /// </summary>
    private void PrewarmDrawCalls()
    {
        if (MainFramebuffer is null)
        {
            return;
        }

        Scene.RendererContext.ShaderLoader.LinkLoadedShaders();
        Renderer.DisableAllCulling = true;

        Renderer.Camera.CopyFrom(Input.Camera);
        Renderer.Prewarming = true;

        try
        {
            // A non-zero delta so that particles actually simulate
            Paint(1f / 60f);

            foreach (var particleNode in Scene.AllNodes.OfType<ParticleSceneNode>())
            {
                particleNode.Prewarm(Renderer.Camera);
            }
        }
        finally
        {
            Renderer.DisableAllCulling = false;
            Renderer.Prewarming = false;
        }
    }

    public void ReportLoadingStatus(string status) => Context.LoadingProgress?.Report(status);

    /// <summary>When set, exported images render only the main scene over a transparent background.</summary>
    public bool SaveImageWithTransparentBackground { get; set; }

    private Framebuffer? saveAsFramebuffer;

    /// <summary>Reads the currently presented frame into a bitmap (used for clipboard copies).</summary>
    public virtual SkiaSharp.SKBitmap? ReadPixelsToBitmap()
    {
        if (MainFramebuffer is null)
        {
            return null;
        }

        if (SaveImageWithTransparentBackground)
        {
            return ReadTransparentPixels();
        }

        var bitmap = new SkiaSharp.SKBitmap(ScreenFramebuffer.Width, ScreenFramebuffer.Height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Opaque);
        var pixels = bitmap.GetPixels(out var length);

        BlitFramebufferToScreen();

        ScreenFramebuffer.Bind(FramebufferTarget.ReadFramebuffer);
        OpenGL.ReadPixels(0, 0, ScreenFramebuffer.Width, ScreenFramebuffer.Height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

        // Flip y
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Scale(1, -1, 0, bitmap.Height / 2f);
        canvas.DrawBitmap(bitmap, new SkiaSharp.SKPoint(), SkiaSharp.SKSamplingOptions.Default);

        return bitmap;
    }

    // Render only the main scene nodes into a transparent framebuffer, so opaque-background
    // decorations (grid, text, background) are excluded from the exported image.
    private SkiaSharp.SKBitmap? ReadTransparentPixels()
    {
        Debug.Assert(MainFramebuffer != null);

        var (w, h) = (MainFramebuffer.Width, MainFramebuffer.Height);

        using var _ = GraphicsContext.RenderState.Scope();

        MainFramebuffer.Bind(FramebufferTarget.Framebuffer);
        OpenGL.ClearColor(new OpenTK.Mathematics.Color4(0, 0, 0, 0));
        OpenGL.Clear(MainFramebuffer.ClearMask);

        Renderer.DrawMainScene();

        if (saveAsFramebuffer is null)
        {
            saveAsFramebuffer = Framebuffer.Prepare(nameof(saveAsFramebuffer), w, h, 0, ImageFormat.RGBA8888, null);
            saveAsFramebuffer.ClearMask = ClearBufferMask.ColorBufferBit;
            saveAsFramebuffer.ClearColor = new OpenTK.Mathematics.Color4(0, 0, 0, 0);
            saveAsFramebuffer.Initialize();
        }
        else
        {
            saveAsFramebuffer.Resize(w, h);
        }

        saveAsFramebuffer.BindAndClear();
        Renderer.PostprocessRender(MainFramebuffer, saveAsFramebuffer, flipY: true);

        OpenGL.Flush();
        OpenGL.Finish();

        saveAsFramebuffer.Bind(FramebufferTarget.ReadFramebuffer);
        OpenGL.ReadBuffer(ReadBufferMode.ColorAttachment0);

        var bitmap = new SkiaSharp.SKBitmap(w, h, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Unpremul);
        var pixels = bitmap.GetPixels(out var length);

        OpenGL.ReadPixels(0, 0, w, h, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

        return bitmap;
    }

    /// <summary>Applies user settings to the render state.</summary>
    public void ApplySettingsToRenderState()
    {
        Context.SceneLogger.LogDebug("Applying settings to render state");

        Renderer.Camera.FieldOfView = Settings.Config.FieldOfView;
        Renderer.Camera.CreateProjectionMatrix();

        // The input camera frames objects using its own field of view, so it follows the setting too
        Input.Camera.FieldOfView = Settings.Config.FieldOfView;
        Input.Camera.CreateProjectionMatrix();
    }

    /// <summary>Enables/disables scene layers, e.g. from a layer selection control.</summary>
    public void SetEnabledLayers(HashSet<string> layers)
    {
        Scene.SetEnabledLayers(layers);
        SkyboxScene?.SetEnabledLayers(layers);
    }

    /// <summary>The distinct layer names present in the loaded scene.</summary>
    public List<string> GetLayerNames()
        => Scene.AllNodes.Select(static x => x.LayerName).OfType<string>().Distinct().ToList();

    private void SetRenderMode(string renderMode)
    {
        Debug.Assert(Picker != null);
        Debug.Assert(SelectedNodeRenderer != null);

        Renderer.ViewBuffer!.Data!.RenderMode = RenderModes.GetShaderId(renderMode);

        Renderer.Postprocess.Enabled = Renderer.ViewBuffer.Data.RenderMode == 0;

        Scene.EnableCompaction = renderMode != "Meshlets";
        SkyboxScene?.EnableCompaction = Scene.EnableCompaction;

        Picker.SetRenderMode(renderMode);
        QuadOverdrawRenderer?.SetRenderMode(renderMode);
        SelectedNodeRenderer.SetRenderMode(renderMode);

        foreach (var node in Scene.AllNodes)
        {
            node.SetRenderMode(renderMode);
        }

        if (SkyboxScene != null)
        {
            foreach (var node in SkyboxScene.AllNodes)
            {
                node.SetRenderMode(renderMode);
            }
        }
    }

    /// <summary>Applies a render mode by name; exposed for shell-side selection controls.</summary>
    public void ApplyRenderMode(string renderMode) => SetRenderMode(renderMode);

    /// <summary>The render modes supported by everything currently loaded in the scene.</summary>
    public List<RenderModes.RenderMode> GetAvailableRenderModes()
    {
        if (Picker is null)
        {
            return [];
        }

        var supported = new HashSet<string>(Picker.Shader.RenderModes);

        if (QuadOverdrawRenderer != null)
        {
            supported.UnionWith(QuadOverdrawRenderer.SceneShader.RenderModes);
        }

        foreach (var node in Scene.AllNodes)
        {
            supported.UnionWith(node.GetSupportedRenderModes());
        }

        renderModes.Clear();

        for (var i = 0; i < RenderModes.Items.Count; i++)
        {
            var mode = RenderModes.Items[i];

            if (i > 0)
            {
                if (mode.IsHeader)
                {
                    if (renderModes[^1].IsHeader)
                    {
                        // If we hit a header and the last added item is also a header, remove it
                        renderModes.RemoveAt(renderModes.Count - 1);
                    }
                }
                else if (!supported.Remove(mode.Name))
                {
                    continue;
                }
            }

            renderModes.Add(mode);
        }

        return renderModes;
    }

    private static TrackedKeys ToTrackedKeys(ViewerKey keys)
    {
        var tracked = TrackedKeys.None;

        if (keys.HasFlag(ViewerKey.Shift))
        {
            tracked |= TrackedKeys.Shift;
        }

        if (keys.HasFlag(ViewerKey.Alt))
        {
            tracked |= TrackedKeys.Alt;
        }

        if (keys.HasFlag(ViewerKey.Control))
        {
            tracked |= TrackedKeys.Control;
        }

        if (keys.HasFlag(ViewerKey.W))
        {
            tracked |= TrackedKeys.W;
        }

        if (keys.HasFlag(ViewerKey.A))
        {
            tracked |= TrackedKeys.A;
        }

        if (keys.HasFlag(ViewerKey.S))
        {
            tracked |= TrackedKeys.S;
        }

        if (keys.HasFlag(ViewerKey.D))
        {
            tracked |= TrackedKeys.D;
        }

        if (keys.HasFlag(ViewerKey.Q))
        {
            tracked |= TrackedKeys.Q;
        }

        if (keys.HasFlag(ViewerKey.Z))
        {
            tracked |= TrackedKeys.Z;
        }

        if (keys.HasFlag(ViewerKey.X))
        {
            tracked |= TrackedKeys.X;
        }

        if (keys.HasFlag(ViewerKey.Space))
        {
            tracked |= TrackedKeys.Space;
        }

        if (keys.HasFlag(ViewerKey.Escape))
        {
            tracked |= TrackedKeys.Escape;
        }

        if (keys.HasFlag(ViewerKey.E))
        {
            tracked |= TrackedKeys.E;
        }

        if (keys.HasFlag(ViewerKey.F))
        {
            tracked |= TrackedKeys.F;
        }

        if (keys.HasFlag(ViewerKey.Slot1))
        {
            tracked |= TrackedKeys.Slot1;
        }

        if (keys.HasFlag(ViewerKey.Slot2))
        {
            tracked |= TrackedKeys.Slot2;
        }

        if (keys.HasFlag(ViewerKey.Slot3))
        {
            tracked |= TrackedKeys.Slot3;
        }

        if (keys.HasFlag(ViewerKey.Slot4))
        {
            tracked |= TrackedKeys.Slot4;
        }

        if (keys.HasFlag(ViewerKey.MouseLeft))
        {
            tracked |= TrackedKeys.MouseLeft;
        }

        if (keys.HasFlag(ViewerKey.MouseRight))
        {
            tracked |= TrackedKeys.MouseRight;
        }

        return tracked;
    }

    /// <summary>Maps a neutral input key to the renderer's tracked keys.</summary>
    public static TrackedKeys MapKey(ViewerKey key) => ToTrackedKeys(key);
}
