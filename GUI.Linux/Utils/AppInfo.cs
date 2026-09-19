namespace GUI.Linux.Utils;

/// <summary>
/// Build information that is set by each platform shell at startup. Kept shell-neutral so portable
/// code (settings, viewers) never has to reference a platform entry point.
/// </summary>
public static class AppInfo
{
    /// <summary>Full product version, including the commit hash suffix when present.</summary>
    public static string ProductVersion { get; set; } = "0.0.0";

    /// <summary>Human readable version shown in the UI.</summary>
    public static string DisplayVersion { get; set; } = "0.0.0";

    /// <summary>Whether this build was produced by CI for a tagged stable release.</summary>
    public static bool IsReleaseBuild { get; set; }
}
