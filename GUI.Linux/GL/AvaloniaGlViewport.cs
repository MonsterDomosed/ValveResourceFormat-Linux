using System;
using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using GUI.Types.GLViewers;
using GUI.Utils;
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
    /// <summary>Raised after every rendered frame. Used by automated checks.</summary>
    public event Action? FrameRendered;

    /// <inheritdoc/>
    public ViewerInputState Input { get; } = new();

    /// <summary>Creates the renderer once a GL context exists.</summary>
    public Func<IGLViewportRenderer>? RendererFactory { get; init; }

    private GraphicsDevice? device;
    private GraphicsContext? context;
    private IGLViewportRenderer? renderer;
    private long lastTicks;
    private Point lastPointer;
    private Framebuffer screenFramebuffer = Framebuffer.GLDefaultFramebuffer;

    public AvaloniaGlViewport()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// <inheritdoc/>
    public Framebuffer ScreenFramebuffer => screenFramebuffer;

    /// <summary>The renderer currently hosted, for shell diagnostics.</summary>
    internal IGLViewportRenderer? ViewportRenderer => renderer;

    /// <summary>Asks for another frame.</summary>
    public void RequestFrame() => RequestNextFrameRendering();

    void IGLViewerHost.RequestFrame() => RequestNextFrameRendering();

    void IGLViewerHost.RequestFullscreen()
    {
        // Fullscreen is a shell concern; the shell decides how to present the viewport.
    }

    void IGLViewerHost.SetClipboardImage(SkiaSharp.SKBitmap bitmap) => AppClipboard.SetImage(bitmap);

    protected override void OnOpenGlInit(GlInterface gl)
    {
        OpenGL.LoadBindings(new GlBindingsContext(gl));

        device = GraphicsDevice.Create();
        context = device.CreateContext(new AvaloniaGraphicsSurface());

#pragma warning disable CA2000 // Disposed in OnOpenGlDeinit
        renderer = RendererFactory?.Invoke()
            ?? throw new InvalidOperationException($"{nameof(RendererFactory)} must be set before the viewport is attached.");
#pragma warning restore CA2000

        renderer.Initialize(device, context, this);
        lastTicks = Stopwatch.GetTimestamp();
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (renderer is null || context is null)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var frameTime = (now - lastTicks) / (double)Stopwatch.Frequency;
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

        if (renderer.ContinuousRendering)
        {
            RequestNextFrameRendering();
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        renderer?.Dispose();
        renderer = null;
        context = null;
        device = null;
        screenFramebuffer = Framebuffer.GLDefaultFramebuffer;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty && renderer is not null)
        {
            RequestNextFrameRendering();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Focus();
        e.Pointer.Capture(this);
        lastPointer = e.GetPosition(this);
        Input.X = (float)lastPointer.X;
        Input.Y = (float)lastPointer.Y;
        Input.Captured = true;
        UpdateButtons(e.GetCurrentPoint(this).Properties);

        RequestNextFrameRendering();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var position = e.GetPosition(this);
        Input.Delta += new Vector2((float)(position.X - lastPointer.X), (float)(position.Y - lastPointer.Y));
        lastPointer = position;
        Input.X = (float)position.X;
        Input.Y = (float)position.Y;
        UpdateButtons(e.GetCurrentPoint(this).Properties);

        RequestNextFrameRendering();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        e.Pointer.Capture(null);
        Input.Captured = false;
        UpdateButtons(e.GetCurrentPoint(this).Properties);
        RequestNextFrameRendering();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        Input.Wheel += (float)e.Delta.Y;
        RequestNextFrameRendering();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        Input.Captured = false;
        UpdateButtons(default);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        Input.Keys |= MapKey(e.Key) | MapModifiers(e.KeyModifiers);
        RequestNextFrameRendering();
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
        RequestNextFrameRendering();
    }

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
