using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace GUI.Linux.Platform;

/// <summary>
/// Moves the pointer back into the window when a drag runs into its edge, so a drag can carry on
/// across the whole desk instead of stopping where the pointer does.
///
/// Only the X11 backend can do this: a Wayland client is not allowed to move the pointer, and
/// Avalonia's Wayland backend implements neither warping nor pointer locking. Xwayland emulates the
/// warp for X11 clients, which is how continuous grab works in other applications on a Wayland
/// desktop.
/// </summary>
[SuppressMessage("Usage", "CA2216:Disposable types should declare finalizer", Justification = "The connection is closed when the viewport is detached. Xlib must not be called from a finalizer thread.")]
internal sealed class X11PointerWarp : IDisposable
{
    private const string Library = "libX11.so.6";

    private IntPtr display;
    private readonly IntPtr root;

    private X11PointerWarp(IntPtr display)
    {
        this.display = display;
        root = XDefaultRootWindow(display);
    }

    /// <summary>
    /// Connects to the X server when the window belongs to the X11 backend, and returns null otherwise
    /// or when libX11 or the display is unavailable.
    /// </summary>
    public static X11PointerWarp? TryCreate(Window? window)
    {
        if (window?.TryGetPlatformHandle() is not { } handle
            || !string.Equals(handle.HandleDescriptor, "XID", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var display = XOpenDisplay(IntPtr.Zero);

            return display == IntPtr.Zero ? null : new X11PointerWarp(display);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Moves the pointer to a point in the coordinate space of <paramref name="visual"/>, which has to
    /// belong to the window. Returns false when the request could not be made.
    /// </summary>
    public bool Warp(Visual visual, Point point)
    {
        if (display == IntPtr.Zero
            || TopLevel.GetTopLevel(visual) is not Window window
            || visual.TranslatePoint(point, window) is not { } client)
        {
            return false;
        }

        var target = window.PointToScreen(client);

        // The root window spans the whole screen, so the destination is in screen coordinates
        _ = XWarpPointer(display, IntPtr.Zero, root, 0, 0, 0, 0, target.X, target.Y);
        _ = XFlush(display);

        return true;
    }

    /// <summary>The pointer position on the screen, for the self-check.</summary>
    public bool TryGetPosition(out PixelPoint position)
    {
        if (display == IntPtr.Zero
            || XQueryPointer(display, root, out _, out _, out var rootX, out var rootY, out _, out _, out _) == 0)
        {
            position = default;
            return false;
        }

        position = new PixelPoint(rootX, rootY);
        return true;
    }

    public void Dispose()
    {
        if (display == IntPtr.Zero)
        {
            return;
        }

        _ = XCloseDisplay(display);
        display = IntPtr.Zero;
    }

    [DllImport(Library, EntryPoint = "XOpenDisplay")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport(Library, EntryPoint = "XCloseDisplay")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(Library, EntryPoint = "XDefaultRootWindow")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(Library, EntryPoint = "XWarpPointer")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int XWarpPointer(IntPtr display, IntPtr srcWindow, IntPtr destWindow,
        int srcX, int srcY, uint srcWidth, uint srcHeight, int destX, int destY);

    [DllImport(Library, EntryPoint = "XQueryPointer")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int XQueryPointer(IntPtr display, IntPtr window,
        out IntPtr rootReturn, out IntPtr childReturn,
        out int rootX, out int rootY, out int winX, out int winY, out uint mask);

    [DllImport(Library, EntryPoint = "XFlush")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int XFlush(IntPtr display);
}
