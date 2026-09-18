using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ValveResourceFormat.IO;

namespace GUI.Types.Browser;

/// <summary>What a browser source node points at.</summary>
internal enum BrowserSourceKind
{
    Game,
    Vpk,
    MapVpk,
    File,
}

/// <summary>
/// A node in the source tree the browser starts from: an installed game, a VPK inside it, a map VPK,
/// or a recently opened file. Portable so both shells can present the same sources.
/// </summary>
internal sealed class BrowserSourceNode(string name, string path, BrowserSourceKind kind, int appId)
{
    /// <summary>Display name for the node.</summary>
    public string Name { get; } = name;

    /// <summary>Physical path on disk (a game directory, VPK file, or plain file).</summary>
    public string Path { get; } = path;

    /// <summary>What the node points at.</summary>
    public BrowserSourceKind Kind { get; } = kind;

    /// <summary>Steam AppID the source belongs to, or 0.</summary>
    public int AppId { get; } = appId;

    /// <summary>Nested sources, e.g. a game's VPKs.</summary>
    public List<BrowserSourceNode> Children { get; } = [];
}

/// <summary>
/// Enumerates the sources the browser can navigate: recently opened files and the VPKs of every
/// installed Steam game. Reuses <see cref="GameContentLocator"/> rather than a new game abstraction.
/// </summary>
internal static partial class GameBrowser
{
    [GeneratedRegex("_[0-9]{3}\\.vpk$", RegexOptions.CultureInvariant)]
    private static partial Regex VpkNumberArchive();

    /// <summary>Builds the "Recent files" node from the persisted recent list.</summary>
    public static BrowserSourceNode BuildRecentFiles(IEnumerable<string> recentFiles)
    {
        var node = new BrowserSourceNode("Recent files", string.Empty, BrowserSourceKind.File, 0);

        foreach (var path in recentFiles.Reverse())
        {
            node.Children.Add(new BrowserSourceNode(path.Replace(Path.DirectorySeparatorChar, '/'), path, BrowserSourceKind.File, 0));
        }

        return node;
    }

    /// <summary>Builds one source node per installed Steam game, each with its VPKs as children.</summary>
    public static List<BrowserSourceNode> BuildGameSources()
    {
        var games = new List<BrowserSourceNode>();

        foreach (var install in GameContentLocator.DiscoverInstalledGames())
        {
            games.Add(BuildGame(install));
        }

        return games;
    }

    private static BrowserSourceNode BuildGame(GameContentLocator.GameInstall install)
    {
        var game = new BrowserSourceNode($"[{install.AppId}] {install.Name}  {install.GamePath}", install.GamePath, BrowserSourceKind.Game, install.AppId);
        var vpks = new List<BrowserSourceNode>();

        foreach (var vpk in EnumerateVpks(install.ContentRoot))
        {
            var relative = Path.GetRelativePath(install.ContentRoot, vpk).Replace(Path.DirectorySeparatorChar, '/');
            vpks.Add(new BrowserSourceNode(relative, vpk, BrowserSourceKind.Vpk, install.AppId));
        }

        var mapsDir = Path.Combine(install.ContentRoot, "maps");

        if (Directory.Exists(mapsDir))
        {
            foreach (var vpk in EnumerateVpks(mapsDir))
            {
                var relative = Path.GetRelativePath(install.ContentRoot, vpk).Replace(Path.DirectorySeparatorChar, '/');
                vpks.Add(new BrowserSourceNode(relative, vpk, BrowserSourceKind.MapVpk, install.AppId));
            }
        }

        vpks.Sort(static (a, b) =>
        {
            var priority = Priority(b.Kind).CompareTo(Priority(a.Kind));

            return priority != 0 ? priority : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        game.Children.AddRange(vpks);
        return game;

        static int Priority(BrowserSourceKind kind) => kind == BrowserSourceKind.MapVpk ? 8 : 10;
    }

    private static IEnumerable<string> EnumerateVpks(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.vpk", SearchOption.TopDirectoryOnly)
            .Where(IsSelectableVpk)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase);
    }

    // Mirrors the Windows source scan: skip chunk archives when the _dir.vpk they belong to exists,
    // and skip the baker's per-resource cache, which is not user content.
    private static bool IsSelectableVpk(string path)
    {
        var fileName = Path.GetFileName(path);

        if (fileName.EndsWith("_bakeresourcecache.vpk", StringComparison.Ordinal))
        {
            return false;
        }

        if (!VpkNumberArchive().IsMatch(fileName))
        {
            return true;
        }

        var fixedPackage = string.Concat(path.AsSpan(0, path.Length - 8), "_dir.vpk");
        return !File.Exists(fixedPackage);
    }
}
