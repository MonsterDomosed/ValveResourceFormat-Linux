using System.IO;
using GUI.Linux.Utils;
using ValveResourceFormat.IO;

namespace GUI.Linux;

/// <summary>
/// Process-wide Linux game content context. Discovery happens once and is shared by every GL viewport,
/// so the (potentially large) game VPK directory is only read once. Also supports adding map VPKs to
/// the shared search paths on demand.
/// </summary>
internal static class LinuxGameContent
{
    private static GameContentContext? context;
    private static readonly HashSet<string> AddedPackages = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The shared content context, discovered on first use.</summary>
    public static GameContentContext Context => context ??= CreateContext();

    private static GameContentContext CreateContext()
    {
        var selected = Settings.Config.SelectedGame;
        var preferred = string.IsNullOrEmpty(selected) ? "Deadlock" : selected;
        return GameContentContext.Discover(preferred);
    }

    /// <summary>Rebuilds the shared context after the selected game changes.</summary>
    public static void Reset()
    {
        context?.Dispose();
        context = null;
        AddedPackages.Clear();
    }

    /// <summary>The shared game file loader, used by renderer contexts.</summary>
    public static GameFileLoader FileLoader => Context.FileLoader;

    /// <summary>Adds a VPK (e.g. a map VPK) to the shared search paths, once.</summary>
    public static bool AddSearchPackage(string vpkPath)
    {
        if (!File.Exists(vpkPath) || !AddedPackages.Add(vpkPath))
        {
            return false;
        }

        try
        {
            Context.FileLoader.AddPackageToSearch(vpkPath);
            return true;
        }
        catch (Exception e)
        {
            AddedPackages.Remove(vpkPath);
            Log.Warn(nameof(LinuxGameContent), $"Failed to add package '{vpkPath}': {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Adds the map VPK for a map or world resource path (for example <c>maps/start.vmap_c</c> or
    /// <c>maps/start/world.vwrld</c>) when it exists under the discovered installation, so its world,
    /// entity and prop resources can be resolved.
    /// </summary>
    public static bool EnsureMapVpkLoaded(string mapOrWorldPath)
    {
        if (Context.Install is not { } install)
        {
            return false;
        }

        var normalized = mapOrWorldPath.Replace('\\', '/');
        var directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? string.Empty;
        var mapDirectory = Path.GetFileName(directory);

        if (mapDirectory.Length == 0 || string.Equals(mapDirectory, "maps", StringComparison.OrdinalIgnoreCase))
        {
            mapDirectory = Path.GetFileNameWithoutExtension(normalized);
        }

        var mapVpk = Path.Combine(install.ContentRoot, "maps", mapDirectory + ".vpk");

        return AddSearchPackage(mapVpk);
    }
}
