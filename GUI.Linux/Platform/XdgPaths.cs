using System.IO;

namespace GUI.Linux.Platform;

/// <summary>
/// XDG Base Directory paths. Environment variables are honoured when they hold an absolute path,
/// as required by the specification; otherwise the documented defaults are used.
/// </summary>
internal static class XdgPaths
{
    /// <summary>Where user-specific data files should be written (settings, bookmarks, ...).</summary>
    public static string DataHome => Resolve("XDG_DATA_HOME", Path.Combine(Home, ".local", "share"));

    /// <summary>Where user-specific configuration files should be written.</summary>
    public static string ConfigHome => Resolve("XDG_CONFIG_HOME", Path.Combine(Home, ".config"));

    /// <summary>Where user-specific non-essential (cached) data should be written.</summary>
    public static string CacheHome => Resolve("XDG_CACHE_HOME", Path.Combine(Home, ".cache"));

    /// <summary>Where user-specific state data (logs, history, ...) should be written.</summary>
    public static string StateHome => Resolve("XDG_STATE_HOME", Path.Combine(Home, ".local", "state"));

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string Resolve(string variable, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(variable);

        // The spec says relative paths are invalid and must be ignored.
        return !string.IsNullOrEmpty(value) && Path.IsPathRooted(value) ? value : fallback;
    }
}
