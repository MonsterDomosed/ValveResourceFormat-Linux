using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GUI.Linux.Utils;
using ValvePak;
using ValveResourceFormat.IO;

namespace Tests;

public class GameFileLoaderTest
{
    [Test]
    public async Task AddonDependenciesOverrideBaseGameFiles()
    {
        var root = Directory.CreateTempSubdirectory("vrf-game-loader-");

        try
        {
            var currentSource = Path.Combine(TestContext.TestDirectory!, "Files", "small_map_with_material.vpk");
            var baseSource = Path.Combine(TestContext.TestDirectory!, "Files", "point_template_test.vpk");

            using var currentSourcePackage = ReadPackage(currentSource);
            using var baseSourcePackage = ReadPackage(baseSource);

            var currentPaths = currentSourcePackage.Entries!
                .SelectMany(static group => group.Value)
                .Select(static entry => entry.GetFullPath())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var target = baseSourcePackage.Entries!
                .SelectMany(static group => group.Value)
                .Select(static entry => entry.GetFullPath())
                .First(path => !currentPaths.Contains(path));

            var steamPath = Path.Combine(root.FullName, "steamapps");
            var gameParent = Path.Combine(steamPath, "common", "Game");
            var gameRoot = Path.Combine(gameParent, "game");
            var addonRoot = Path.Combine(gameParent, "game_addons", "testaddon");
            var currentPath = Path.Combine(addonRoot, "current.vpk");
            var dependencyPath = Path.Combine(steamPath, "workshop", "content", "730", "123", "123_dir.vpk");

            Directory.CreateDirectory(gameRoot);
            Directory.CreateDirectory(addonRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(dependencyPath)!);
            File.Copy(currentSource, currentPath);
            File.Copy(baseSource, Path.Combine(gameRoot, "pak01_dir.vpk"));
            File.Copy(baseSource, dependencyPath);
            await File.WriteAllTextAsync(Path.Combine(addonRoot, "addoninfo.txt"), """
                "addoninfo"
                {
                    "Dependencies"
                    {
                        "1" "123"
                    }
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(gameRoot, "gameinfo.gi"), """
                "gameinfo"
                {
                    "game" "TestGame"
                    "FileSystem"
                    {
                        "SearchPaths"
                        {
                            "Game" "game"
                            "AddonRoot" "game_addons"
                        }
                        "SteamAppId" "730"
                        "AllowAddonDependencies" "1"
                    }
                }
                """);

            using var currentPackage = ReadPackage(currentPath);
            using var loader = new GameFileLoader(currentPackage, currentPath);
            var found = loader.FindFile(target);

            await Assert.That(loader.GameName).IsEqualTo("TestGame");
            await Assert.That(found.Package).IsNotNull();
            await Assert.That(found.Package!.FileName).IsEqualTo(Path.Combine(Path.GetDirectoryName(dependencyPath)!, "123"));
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Test]
    [NotInParallel(nameof(GameFileLoaderTest))]
    public async Task ConfiguredGameInfoLoadsBasePackages()
    {
        var root = Directory.CreateTempSubdirectory("vrf-game-content-");
        var oldSearchPaths = Settings.Config.GameSearchPaths;

        try
        {
            var source = Path.Combine(TestContext.TestDirectory!, "Files", "point_template_test.vpk");
            using var sourcePackage = ReadPackage(source);
            var target = sourcePackage.Entries!
                .SelectMany(static group => group.Value)
                .Select(static entry => entry.GetFullPath())
                .First();

            var gameRoot = Path.Combine(root.FullName, "game");
            var gameInfoPath = Path.Combine(gameRoot, "gameinfo.gi");
            Directory.CreateDirectory(gameRoot);
            File.Copy(source, Path.Combine(gameRoot, "pak01_dir.vpk"));
            await File.WriteAllTextAsync(gameInfoPath, """
                "gameinfo"
                {
                    "game" "TestGame"
                    "FileSystem"
                    {
                        "SearchPaths"
                        {
                            "Game" "game"
                        }
                    }
                }
                """);

            Settings.Config.GameSearchPaths = [gameInfoPath];
            using var context = GameContentContext.Discover();
            var found = context.FileLoader.FindFile(target);

            await Assert.That(found.Package).IsNotNull();
        }
        finally
        {
            Settings.Config.GameSearchPaths = oldSearchPaths;
            root.Delete(true);
        }
    }

    private static Package ReadPackage(string path)
    {
        var package = new Package();
        package.Read(path);
        return package;
    }
}
