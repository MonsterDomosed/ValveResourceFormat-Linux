using Avalonia;

namespace GUI.Linux.GL;

/// <summary>
/// Continues a drag that ran into the edge of the screen. The pointer cannot move past the edge, so
/// without this a long drag simply stops there; the view keeps moving the way the drag was going
/// instead, which reads the same as the pointer wrapping round to the other side.
/// </summary>
internal static class EdgeDrag
{
    /// <summary>How close to the edge, in pixels, the pointer counts as being against it.</summary>
    public const double EdgeMargin = 2.0;

    /// <summary>Below this speed the pointer is resting rather than dragging, and nothing continues.</summary>
    public const float MinimumSpeed = 60f;

    /// <summary>Longest virtual step per frame, so a flick into the edge cannot spin the view wildly.</summary>
    public const float MaximumStep = 32f;

    /// <summary>
    /// The screen edges the pointer is sitting against, as a sign per axis (-1, 0 or 1). Both zero
    /// means it is nowhere near an edge.
    /// </summary>
    public static (int X, int Y) EdgesAt(PixelPoint pointer, PixelRect screen)
    {
        var x = pointer.X <= screen.X + EdgeMargin ? -1
            : pointer.X >= screen.Right - EdgeMargin ? 1
            : 0;

        var y = pointer.Y <= screen.Y + EdgeMargin ? -1
            : pointer.Y >= screen.Bottom - EdgeMargin ? 1
            : 0;

        return (x, y);
    }

    /// <summary>
    /// Keeps only the edges the drag is pushing into. Sliding along an edge, or moving away from it,
    /// must not keep the view moving in the direction of the edge.
    /// </summary>
    public static (int X, int Y) PushedInto((int X, int Y) edges, (int X, int Y) dragDirection)
    {
        var x = edges.X != 0 && edges.X == dragDirection.X ? edges.X : 0;
        var y = edges.Y != 0 && edges.Y == dragDirection.Y ? edges.Y : 0;

        return (x, y);
    }

    /// <summary>The virtual pointer movement for one frame, in screen pixels.</summary>
    public static Vector2 Step((int X, int Y) direction, float pixelsPerSecond, float frameTime)
    {
        var distance = Math.Min(pixelsPerSecond * frameTime, MaximumStep);

        return new Vector2(direction.X * distance, direction.Y * distance);
    }
}
