using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.OpenGL.Egl;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GUI.Linux.Types.GLViewers;
using GUI.Linux.Utils;
using ValveResourceFormat.Renderer;
using OpenGL = OpenTK.Graphics.OpenGL.GL;

namespace GUI.Linux.GL;

/// <summary>
/// Production OpenGL viewport for Linux, built on Avalonia's <see cref="OpenGlControlBase"/>.
///
/// The GL context is created by Avalonia and is current for the whole render callback, so the
/// renderer runs entirely on Avalonia's render thread. Frames are rendered on demand (input,
/// resize, or explicit requests) and only requested continuously when the renderer asks for it, so
/// the UI thread is never busy-spun.
/// </summary>
internal sealed class AvaloniaGlViewport : OpenGlControlBase, IGLViewerHost
{
    private static readonly EglInterface EglApi = new();

    /// <summary>Raised after every rendered frame. Used by automated checks.</summary>
    public event Action? FrameRendered;

    /// <inheritdoc/>
    public ViewerInputState Input { get; } = new();

    /// <summary>Creates the renderer once a GL context exists.</summary>
    public Func<IGLViewportRenderer>? RendererFactory { get; init; }

    private GraphicsDevice? device;
    private GraphicsContext? context;
    private IGLViewportRenderer? renderer;
    private int appliedVsync = -1;
    private long lastTicks;
    private Point lastPointer;
    private Framebuffer screenFramebuffer = Framebuffer.GLDefaultFramebuffer;
    private bool cursorHidden;
    private DispatcherTimer? framePacer;
    private DispatcherTimer? statePoller;
    private Window? hostWindow;
    private bool hostWindowMinimized;

    public AvaloniaGlViewport()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        // Avalonia routes pointer input through rendered geometry, and the OpenGL surface alone is not
        // hit-testable, so the viewport would never receive pointer events. A transparent fill gives it
        // a hit target without drawing anything visible.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
    }

    /// <inheritdoc/>
    public Framebuffer ScreenFramebuffer => screenFramebuffer;

    /// <summary>The renderer currently hosted, for shell diagnostics.</summary>
    internal IGLViewportRenderer? ViewportRenderer => renderer;

    /// <summary>Asks for another frame.</summary>
    public void RequestFrame()
    {
        if (IsVisible)
        {
            RequestNextFrameRendering();
        }
    }

    void IGLViewerHost.RequestFrame() => RequestFrame();

    void IGLViewerHost.RequestFullscreen() => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.WindowState = window.WindowState == WindowState.FullScreen
                ? WindowState.Normal
                : WindowState.FullScreen;
        }
    }

    private void CaptureScreenshot(Action<SkiaSharp.SKBitmap> handler)
    {
        if (renderer is not ViewportGlRenderer viewportRenderer)
        {
            return;
        }

        viewportRenderer.RequestScreenshot(bitmap =>
        {
            try
            {
                handler(bitmap);
            }
            finally
            {
                bitmap.Dispose();
            }
        });
        RequestNextFrameRendering();
    }

    void IGLViewerHost.SetClipboardImage(SkiaSharp.SKBitmap bitmap) => AppClipboard.SetImage(bitmap);

    internal static double ClampFrameTime(double elapsed) => Math.Clamp(elapsed, 0.0, 1.0);

    protected override void OnOpenGlInit(GlInterface gl)
    {
        OpenGL.LoadBindings(new GlBindingsContext(gl));
        ApplyVerticalSync();

        device = GraphicsDevice.Create();
        context = device.CreateContext(new AvaloniaGraphicsSurface());

#pragma warning disable CA2000 // Disposed in OnOpenGlDeinit
        renderer = RendererFactory?.Invoke()
            ?? throw new InvalidOperationException($"{nameof(RendererFactory)} must be set before the viewport is attached.");
#pragma warning restore CA2000

        renderer.Initialize(device, context, this);
        (renderer as ViewportGlRenderer)?.OnVisibilityChanged(IsVisible);
        lastTicks = Stopwatch.GetTimestamp();
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (renderer is null || context is null || !IsVisible)
        {
            return;
        }

        ApplyVerticalSync();

        var now = Stopwatch.GetTimestamp();
        var frameTime = ClampFrameTime((now - lastTicks) / (double)Stopwatch.Frequency);
        lastTicks = now;

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var width = Math.Max(1, (int)(Bounds.Width * scaling));
        var height = Math.Max(1, (int)(Bounds.Height * scaling));

        screenFramebuffer = fb == 0
            ? Framebuffer.GLDefaultFramebuffer
            : Framebuffer.FromExternalHandle(fb, "AvaloniaScreen");

        renderer.Render(context, width, height, Input, frameTime);
        Input.EndFrame();

        FrameRendered?.Invoke();

        if (renderer.ContinuousRendering && IsVisible && IsRenderActive)
        {
            ScheduleNextFrame();
        }
    }

    /// <summary>
    /// False while the host window is minimized. Avalonia keeps delivering render callbacks for an
    /// iconified window, so the frame loop has to stop itself, otherwise a window nobody can see
    /// keeps redrawing at full cost.
    /// </summary>
    private bool IsRenderActive => !hostWindowMinimized;

    private bool HostWindowMinimized => hostWindow?.WindowState == WindowState.Minimized;

    /// <summary>Queues the next continuous frame, honouring the configured frame rate limit.</summary>
    private void ScheduleNextFrame()
    {
        if (Settings.Config.FrameRateLimit <= 0)
        {
            RequestNextFrameRendering();
            return;
        }

        // The timer owns the schedule, so exactly one frame is asked for per slot. Requesting straight
        // from the render callback as well would race the timer that already asked for this frame and
        // deliver two frames per slot.
        if (framePacer is null)
        {
            framePacer = new DispatcherTimer(DispatcherPriority.Render);
            framePacer.Tick += OnFramePacerTick;
        }

        framePacer.Stop();
        framePacer.Interval = TimeSpan.FromSeconds(1.0 / Settings.Config.FrameRateLimit);
        framePacer.Start();
    }

    private void OnFramePacerTick(object? sender, EventArgs e)
    {
        framePacer?.Stop();

        if (renderer is not null && IsVisible && IsRenderActive)
        {
            RequestNextFrameRendering();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        hostWindow = TopLevel.GetTopLevel(this) as Window;
        hostWindowMinimized = HostWindowMinimized;

        statePoller = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        statePoller.Tick += OnHostStatePollTick;
        statePoller.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        hostWindow = null;
        hostWindowMinimized = false;
        framePacer?.Stop();
        statePoller?.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Notices a window that was minimized or restored. Avalonia has no public window state event, and
    /// a minimized window stops being drawn, so a one second poll is what lets the loop notice that
    /// nobody is looking at it and stop feeding the GPU.
    /// </summary>
    private void OnHostStatePollTick(object? sender, EventArgs e)
    {
        var minimized = HostWindowMinimized;

        if (minimized == hostWindowMinimized)
        {
            return;
        }

        hostWindowMinimized = minimized;

        if (minimized)
        {
            framePacer?.Stop();
            return;
        }

        // Restoring from minimized leaves the surface without a fresh frame, so ask for one.
        RequestNextFrameRendering();
    }

    private void ApplyVerticalSync()
    {
        if (appliedVsync == Settings.Config.Vsync)
        {
            return;
        }

        var context = EglApi.GetCurrentContext();
        if (context != IntPtr.Zero && EglApi.SwapInterval(context, Settings.Config.Vsync))
        {
            appliedVsync = Settings.Config.Vsync;
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        framePacer?.Stop();
        statePoller?.Stop();
        renderer?.Dispose();
        renderer = null;
        context = null;
        device = null;
        appliedVsync = -1;
        screenFramebuffer = Framebuffer.GLDefaultFramebuffer;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && renderer is ViewportGlRenderer viewportRenderer)
        {
            viewportRenderer.OnVisibilityChanged(IsVisible);

            if (IsVisible)
            {
                lastTicks = Stopwatch.GetTimestamp();
                RequestNextFrameRendering();
            }
        }

        if (change.Property == BoundsProperty && renderer is not null)
        {
            RequestNextFrameRendering();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Focus();
        Input.MouseOverViewport = true;
        e.Pointer.Capture(this);
        lastPointer = e.GetPosition(this);
        Input.X = (float)lastPointer.X;
        Input.Y = (float)lastPointer.Y;
        Input.Captured = true;

        // The drag starts from this point, so movement from hovering before the press is discarded
        // instead of being applied in one jump by the first frame of the drag.
        Input.Delta = Vector2.Zero;
        UpdateButtons(e.GetCurrentPoint(this).Properties);

        if (renderer is ViewportGlRenderer viewportRenderer)
        {
            var pointer = GetRenderPointerPosition(e);
            viewportRenderer.OnPointerDown(pointer.X, pointer.Y, Input.Keys, e.ClickCount);
        }

        // Hide the pointer while dragging the camera so it does not run into the window edges or
        // distract from the model being inspected.
        if (Input.Left || Input.Right)
        {
            HideCursor();
        }

        RequestNextFrameRendering();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        // The scene core only advances the camera while the pointer is over the viewport, so track the
        // real pointer presence here rather than relying on callers to set it.
        Input.MouseOverViewport = true;

        var position = e.GetPosition(this);
        UpdateButtons(e.GetCurrentPoint(this).Properties);

        // Hovering is not sampled every frame anymore, so accumulating it here would hand the whole
        // hover travel to the first frame of the next drag and make the camera jump to wherever the
        // pointer had wandered.
        if (FollowsPointer)
        {
            Input.Delta += new Vector2((float)(position.X - lastPointer.X), (float)(position.Y - lastPointer.Y));
        }

        lastPointer = position;
        Input.X = (float)position.X;
        Input.Y = (float)position.Y;

        // A scene viewer only needs a frame while the pointer is doing something. Asking for one for
        // every hover motion would keep an idle viewer redrawing.
        if (FollowsPointer)
        {
            RequestNextFrameRendering();
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);

        // Entering is not a move: the first move has to measure from where the pointer came in, not
        // from wherever it last was inside the viewport.
        if (!IsDragging)
        {
            lastPointer = e.GetPosition(this);
        }

        Input.MouseOverViewport = true;
        RequestNextFrameRendering();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        // A drag that runs past the edge of the viewport keeps driving the camera, because the pointer
        // is captured; only a free pointer leaving means the camera should stop following it.
        Input.MouseOverViewport = IsDragging;
        RequestNextFrameRendering();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        e.Pointer.Capture(null);
        Input.Captured = false;
        UpdateButtons(e.GetCurrentPoint(this).Properties);

        if (renderer is ViewportGlRenderer viewportRenderer)
        {
            var pointer = GetRenderPointerPosition(e);
            viewportRenderer.OnPointerUp(pointer.X, pointer.Y, Input.Keys);
        }

        RestoreCursor();
        RequestNextFrameRendering();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        Input.MouseOverViewport = true;
        Input.Wheel += (float)e.Delta.Y;
        RequestNextFrameRendering();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        Input.Captured = false;
        UpdateButtons(default);
        RestoreCursor();
    }

    private Vector2 GetRenderPointerPosition(PointerEventArgs e)
    {
        var position = e.GetPosition(this);
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        return new Vector2((float)(position.X * scaling), (float)(position.Y * scaling));
    }

    private void HideCursor()
    {
        if (cursorHidden)
        {
            return;
        }

        cursorHidden = true;
        Cursor = new Cursor(StandardCursorType.None);
    }

    private void RestoreCursor()
    {
        if (!cursorHidden)
        {
            return;
        }

        cursorHidden = false;
        Cursor = Cursor.Default;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.C)
        {
            CaptureScreenshot(AppClipboard.SetImage);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.S)
        {
            CaptureScreenshot(SaveScreenshot);
            e.Handled = true;
            return;
        }

        var key = MapKey(e.Key);
        Input.Keys |= key | MapModifiers(e.KeyModifiers);

        // Viewer shortcuts (Tab performance overlay, Delete, Escape) are handled by the core, not by
        // the held-key input state.
        if (key != ViewerKey.None && renderer is ViewportGlRenderer viewportRenderer)
        {
            viewportRenderer.OnKeyDown(key);
        }

        RequestNextFrameRendering();
    }

    private static void SaveScreenshot(SkiaSharp.SKBitmap bitmap)
    {
        var path = AppFileDialogs.SaveFile("Save screenshot", "screenshot.png", "png", "PNG image|*.png");

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        Input.Keys &= ~MapKey(e.Key);
        RequestNextFrameRendering();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        Input.HasFocus = true;
        RequestNextFrameRendering();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        Input.HasFocus = false;
        Input.Left = false;
        Input.Middle = false;
        Input.Right = false;
        Input.Keys = ViewerKey.None;
        RestoreCursor();
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Whether a drag is in progress. The core sets <see cref="ViewerInputState.Captured"/> while mouse
    /// look owns the pointer, so walk mode counts too even without a button.
    /// </summary>
    private bool IsDragging => Input.Left || Input.Middle || Input.Right || Input.Captured;

    /// <summary>
    /// Whether pointer movement currently does something in the view. Besides a drag, scene viewers
    /// adjust the sun on ctrl-hover, and the graph and texture viewers follow the pointer for hover
    /// feedback, so those keep sampling motion.
    /// </summary>
    private bool FollowsPointer => IsDragging
        || renderer is not SceneCoreGlRenderer
        || (Input.Keys & ViewerKey.Control) != 0;

    private void UpdateButtons(PointerPointProperties properties)
    {
        Input.Left = properties.IsLeftButtonPressed;
        Input.Middle = properties.IsMiddleButtonPressed;
        Input.Right = properties.IsRightButtonPressed;

        SetKey(ViewerKey.MouseLeft, Input.Left);
        SetKey(ViewerKey.MouseRight, Input.Right);
    }

    private void SetKey(ViewerKey key, bool pressed)
    {
        if (pressed)
        {
            Input.Keys |= key;
        }
        else
        {
            Input.Keys &= ~key;
        }
    }

    private static ViewerKey MapModifiers(KeyModifiers modifiers)
    {
        var keys = ViewerKey.None;

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            keys |= ViewerKey.Shift;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            keys |= ViewerKey.Alt;
        }

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            keys |= ViewerKey.Control;
        }

        return keys;
    }

    private static ViewerKey MapKey(Key key) => key switch
    {
        Key.W or Key.Up => ViewerKey.W,
        Key.S or Key.Down => ViewerKey.S,
        Key.A or Key.Left => ViewerKey.A,
        Key.D or Key.Right => ViewerKey.D,
        Key.Q => ViewerKey.Q,
        Key.Z => ViewerKey.Z,
        Key.X => ViewerKey.X,
        Key.E => ViewerKey.E,
        Key.F => ViewerKey.F,
        Key.Space => ViewerKey.Space,
        Key.Escape => ViewerKey.Escape,
        Key.Tab => ViewerKey.Tab,
        Key.Delete => ViewerKey.Delete,
        Key.D1 => ViewerKey.Slot1,
        Key.D2 => ViewerKey.Slot2,
        Key.D3 => ViewerKey.Slot3,
        Key.D4 => ViewerKey.Slot4,
        Key.LeftShift or Key.RightShift => ViewerKey.Shift,
        Key.LeftAlt or Key.RightAlt => ViewerKey.Alt,
        Key.LeftCtrl or Key.RightCtrl => ViewerKey.Control,
        _ => ViewerKey.None,
    };
}
