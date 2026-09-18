using System.IO;
using System.Linq;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// Discovers installed Steam games and their primary content roots, reusing
    /// <see cref="GameFolderLocator"/> for Steam/library discovery. UI-toolkit agnostic so both the
    /// WinForms and Avalonia shells can share it.
    /// </summary>
    public static class GameContentLocator
    {
        /// <summary>
        /// An installed game and the <c>gameinfo.gi</c> of its primary content root.
        /// </summary>
        /// <param name="AppId">Steam AppID.</param>
        /// <param name="Name">Steam app name.</param>
        /// <param name="GamePath">Full path to the installation directory of the app.</param>
        /// <param name="GameInfoPath">Full path to the primary content root's <c>gameinfo.gi</c>.</param>
        public sealed record GameInstall(int AppId, string Name, string GamePath, string GameInfoPath)
        {
            /// <summary>The directory containing the primary <c>gameinfo.gi</c>.</summary>
            public string ContentRoot => Path.GetDirectoryName(GameInfoPath)!;
        }

        /// <summary>
        /// Finds every installed Steam game whose installation contains a resolvable content root.
        /// </summary>
        public static List<GameInstall> DiscoverInstalledGames()
        {
            var installs = new List<GameInstall>();

            foreach (var game in GameFolderLocator.FindAllSteamGames())
            {
                var gameInfoPath = FindPrimaryGameInfo(game.GamePath);

                if (gameInfoPath != null)
                {
                    installs.Add(new GameInstall(game.AppID, game.AppName, game.GamePath, gameInfoPath));
                }
            }

            return installs;
        }

        /// <summary>Finds an installed game by its Steam app name.</summary>
        public static GameInstall? FindInstalledGame(string name)
            => DiscoverInstalledGames().FirstOrDefault(game => string.Equals(game.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Finds an installed game by its Steam AppID.</summary>
        public static GameInstall? FindInstalledGame(int appId)
            => DiscoverInstalledGames().FirstOrDefault(game => game.AppId == appId);

        /// <summary>
        /// Creates a configured <see cref="GameFileLoader"/> for an installed game. The loader's existing
        /// gameinfo/VPK discovery does the work, so no game-specific paths are hardcoded.
        /// </summary>
        public static GameFileLoader CreateFileLoader(GameInstall install) => new(null, install.GameInfoPath);

        /// <summary>
        /// Picks the primary content root: the <c>gameinfo.gi</c> under <c>&lt;game&gt;/game</c> whose
        /// directory holds the main <c>pak01_dir.vpk</c> (falling back to the folder with the most VPKs).
        /// </summary>
        private static string? FindPrimaryGameInfo(string gamePath)
        {
            var gameDir = Path.Combine(gamePath, "game");

            if (!Directory.Exists(gameDir))
            {
                return null;
            }

            try
            {
                return Directory.GetFiles(gameDir, "gameinfo.gi", SearchOption.AllDirectories)
                    .Select(path => (Path: path, Directory: Path.GetDirectoryName(path)!))
                    .Where(candidate => Directory.Exists(candidate.Directory))
                    .Select(candidate => (
                        candidate.Path,
                        VpkCount: Directory.GetFiles(candidate.Directory, "*.vpk").Length,
                        HasMainPak: File.Exists(Path.Combine(candidate.Directory, "pak01_dir.vpk"))))
                    .Where(candidate => candidate.VpkCount > 0)
                    .OrderByDescending(candidate => candidate.HasMainPak)
                    .ThenByDescending(candidate => candidate.VpkCount)
                    .Select(candidate => candidate.Path)
                    .FirstOrDefault();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
