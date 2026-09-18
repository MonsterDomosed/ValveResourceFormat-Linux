namespace GUI.Types.GLViewers;

/// <summary>
/// Platform-neutral key/button state, mirroring the Renderer's <c>TrackedKeys</c> so a viewer core
/// can consume it without depending on WinForms or Avalonia input types.
/// </summary>
[Flags]
#pragma warning disable CA2217 // Do not mark enums with FlagsAttribute; mirrors the Renderer's TrackedKeys, which has the same shape
public enum ViewerKey : long
#pragma warning restore CA2217
{
    None = 0,

    Shift = 1 << 0,
    Alt = 1 << 1,
    Control = 1 << 9,

    W = 1 << 2,
    A = 1 << 3,
    S = 1 << 4,
    D = 1 << 5,
    Q = 1 << 6,
    Z = 1 << 7,
    X = 1 << 8,
    Space = 1 << 10,
    Escape = 1 << 11,
    Slot1 = 1 << 12,
    Slot2 = 1 << 13,
    Slot3 = 1 << 14,
    Slot4 = 1 << 16,
    F = 1 << 15,
    E = 1 << 17,
    Delete = 1 << 18,
    Tab = 1 << 19,

    MouseLeft = 1 << 30,
    MouseRight = 1 << 31,
    MouseLeftOrRight = MouseLeft | MouseRight,
}

/// <summary>
/// Per-viewport input state. Both the WinForms and Avalonia hosts fill this from their native events;
/// the viewer core reads it. Deltas and wheel are consumed and cleared once per frame.
/// </summary>
public sealed class ViewerInputState
{
    public float X { get; set; }
    public float Y { get; set; }
    public Vector2 Delta { get; set; }
    public float Wheel { get; set; }
    public ViewerKey Keys { get; set; }
    public bool Left { get; set; }
    public bool Middle { get; set; }
    public bool Right { get; set; }
    public bool HasFocus { get; set; }
    public bool Captured { get; set; }

    /// <summary>Whether the pointer is currently over the viewport's render area.</summary>
    public bool MouseOverViewport { get; set; }

    public void EndFrame()
    {
        Delta = Vector2.Zero;
        Wheel = 0;
    }
}
