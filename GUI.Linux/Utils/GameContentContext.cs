using System.IO;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace GUI.Linux.Utils;

/// <summary>
/// Portable game/content context: a configured <see cref="GameFileLoader"/> for the selected installed
/// game, or for the user-configured search paths. No UI-toolkit dependency, so both shells can share it.
/// </summary>
public sealed class GameContentContext : IDisposable
{
    /// <summary>The selected installed game, or null when only user search paths (or nothing) were used.</summary>
    public GameContentLocator.GameInstall? Install { get; }

    /// <summary>The configured loader used to resolve game resources.</summary>
    public GameFileLoader FileLoader { get; }

    /// <summary>Whether an installed game's content root was resolved.</summary>
    public bool HasGameContent => Install != null;

    private GameContentContext(GameContentLocator.GameInstall? install, GameFileLoader fileLoader)
    {
        Install = install;
        FileLoader = fileLoader;
    }

    /// <summary>
    /// Builds a context. User-configured search paths take precedence (matching the Windows shell);
    /// otherwise installed Steam games are discovered and <paramref name="preferredGameName"/> (or the
    /// first game found) is selected.
    /// </summary>
    public static GameContentContext Discover(string? preferredGameName = null)
    {
        try
        {
            var configured = Settings.Config.GameSearchPaths;

            if (configured.Count > 0)
            {
                var configuredLoader = new NormalizingFileLoader(null, null);

                foreach (var path in configured)
                {
                    try
                    {
                        if (path.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
                        {
                            configuredLoader.AddPackageToSearch(path);
                        }
                        else if (Directory.Exists(path))
                        {
                            configuredLoader.AddDiskPathToSearch(path);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Warn(nameof(GameContentContext), $"Failed to add search path '{path}': {e.Message}");
                    }
                }

                return new GameContentContext(null, configuredLoader);
            }

            var installs = GameContentLocator.DiscoverInstalledGames();

            var install = preferredGameName != null
                ? installs.Find(game => string.Equals(game.Name, preferredGameName, StringComparison.OrdinalIgnoreCase))
                : null;

            install ??= installs.Count > 0 ? installs[0] : null;

            if (install == null)
            {
                return new GameContentContext(null, new NormalizingFileLoader(null, null));
            }

            Log.Info(nameof(GameContentContext), $"Using installed game '{install.Name}' ({install.AppId}) at '{install.ContentRoot}'");

            var discoveredLoader = new NormalizingFileLoader(null, install.GameInfoPath);
            return new GameContentContext(install, discoveredLoader);
        }
        catch (Exception e)
        {
            Log.Warn(nameof(GameContentContext), $"Game content discovery failed: {e.Message}");
            return new GameContentContext(null, new NormalizingFileLoader(null, null));
        }
    }

    public void Dispose()
    {
        FileLoader.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A <see cref="GameFileLoader"/> that normalizes Windows-style backslash paths to forward slashes
    /// before lookup, matching the Windows shell's loader and the format world-node names use.
    /// </summary>
    private sealed class NormalizingFileLoader(Package? currentPackage, string? currentFileName)
        : GameFileLoader(currentPackage, currentFileName)
    {
        private static string Normalize(string file) => file.Replace('\\', '/');

        /// <inheritdoc/>
        public override Resource? LoadFile(string file) => base.LoadFile(Normalize(file));

        /// <inheritdoc/>
        public override Resource? LoadFileCompiled(string file) => base.LoadFileCompiled(Normalize(file));

        /// <inheritdoc/>
        public override (string? PathOnDisk, Package? Package, PackageEntry? PackageEntry) FindFile(string file, bool logNotFound = true)
            => base.FindFile(Normalize(file), logNotFound);
    }
}
