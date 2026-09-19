namespace GUI.Linux.Types.Graphs.Core;

/// <summary>
/// Platform-neutral mouse button flags the graph presenter understands. Hosts map their native
/// button type (Avalonia pointer properties) onto this.
/// </summary>
[Flags]
internal enum GraphMouseButton
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Middle = 1 << 2,
}

/// <summary>
/// Platform-neutral modifier key flags the graph presenter understands. Hosts map their native
/// modifier type (Avalonia <c>KeyModifiers</c>) onto this.
/// </summary>
[Flags]
internal enum GraphModifiers
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alt = 1 << 2,
}
