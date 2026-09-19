namespace GUI.Linux.Types.GLViewers;

/// <summary>
/// The sidebar controls a scene viewer core wants to show, described as intent instead of widgets.
/// Only what the shared core actually calls is here; shells implement it with their own toolkit.
/// </summary>
public interface IGLViewerControls
{
    /// <summary>Shows the current move-speed/zoom modifier text.</summary>
    void SetMoveSpeed(string text);
}
