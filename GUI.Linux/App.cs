using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GUI.Linux.GL;
using GUI.Linux.Platform;
using GUI.Linux.Shell;
using GUI.Linux.Types.Browser;
using GUI.Linux.Types.Viewers;
using GUI.Linux.Utils;
using GUI.Linux.Viewers;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;
using ViewerKey = GUI.Linux.Types.GLViewers.ViewerKey;

namespace GUI.Linux;

internal sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    /// <summary>Applies the configured theme to the running application.</summary>
    internal static void ApplyTheme()
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = Settings.Config.Theme switch
        {
            1 => ThemeVariant.Light,
            2 => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Settings.Load();
            ApplyTheme();

            var window = new MainWindow(Program.FileArgs);
            desktop.MainWindow = window;
            LinuxPlatform.MainWindow = window;

            if (Program.SelfCheck != Program.SelfCheckMode.None)
            {
                window.Opened += (_, _) => RunSelfCheck(window);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async void RunSelfCheck(MainWindow window)
    {
        var output = Program.StdOut;

        output.WriteLine(
            "[self-check] session: "
            + $"WAYLAND_DISPLAY={Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "<unset>"} "
            + $"DISPLAY={Environment.GetEnvironmentVariable("DISPLAY") ?? "<unset>"} "
            + $"XDG_SESSION_TYPE={Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "<unset>"}");

        output.WriteLine($"[self-check] settings directory: {PlatformServices.Current.SettingsDirectory}");

        // Wayland only accepts a new clipboard selection from a focused client, so activate the window
        // and give the compositor a moment before testing the clipboard.
        window.Activate();
        await Task.Delay(1000).ConfigureAwait(true);

        output.WriteLine($"[self-check] tabs: {window.TabCount}, selected: {window.SelectedTabTitle}");

        try
        {
            var probe = $"s2v-self-check-{Guid.NewGuid():N}";
            AppClipboard.SetText(probe);
            var matched = AppClipboard.GetText() == probe;

            if (!matched && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            {
                // The compositor may ignore a selection change from a client with no recent input event.
                output.WriteLine("[self-check] clipboard roundtrip: inconclusive (Wayland selection changes require a focused client)");
            }
            else
            {
                output.WriteLine($"[self-check] clipboard roundtrip: {matched}");
            }
        }
        catch (Exception e)
        {
            output.WriteLine($"[self-check] clipboard failed: {e.GetType().Name}: {e.Message}");
        }

        try
        {
            var dialogTask = AppMessageDialogs.ShowMessageAsync("Self check dialog.", "Source 2 Viewer");
            var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
            DispatcherTimer.RunOnce(
                () => desktop.Windows.FirstOrDefault(candidate => candidate != window)?.Close(),
                TimeSpan.FromSeconds(1));
            await dialogTask.ConfigureAwait(true);
            output.WriteLine("[self-check] message dialog ok");
        }
        catch (Exception e)
        {
            output.WriteLine($"[self-check] message dialog failed: {e.GetType().Name}: {e.Message}");
        }

        var presentersOk = false;

        try
        {
            presentersOk = RunPresenterChecks();
            output.WriteLine($"[self-check] presenter checks: {presentersOk}");

            var temp = Path.Combine(Path.GetTempPath(), $"s2v-selfcheck-{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(temp, "Source 2 Viewer self check\nLine two\n").ConfigureAwait(true);
            var before = window.TabCount;
            await window.OpenFileAsync(temp).ConfigureAwait(true);
            output.WriteLine($"[self-check] open file: tabs {before} -> {window.TabCount}");
            File.Delete(temp);
        }
        catch (Exception e)
        {
            output.WriteLine($"[self-check] content path failed: {e.GetType().Name}: {e.Message}");
        }

        try
        {
            var tabsBeforeSettings = window.TabCount;
            window.OpenSettings();
            await Task.Delay(200).ConfigureAwait(true);
            output.WriteLine($"[self-check] settings tab: tabs {tabsBeforeSettings} -> {window.TabCount}");
        }
        catch (Exception e)
        {
            output.WriteLine($"[self-check] settings tab failed: {e.GetType().Name}: {e.Message}");
        }

        if (Program.SelfCheck == Program.SelfCheckMode.Smoke)
        {
            var glOk = await RunGlInfrastructureCheckAsync().ConfigureAwait(true);
            var passed = presentersOk && glOk;

            output.WriteLine(passed
                ? "[self-check] smoke complete, exiting"
                : "[self-check] smoke failed, exiting with error");

            var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
            DispatcherTimer.RunOnce(
                () =>
                {
                    if (passed)
                    {
                        window.Close();
                    }
                    else
                    {
                        desktop.Shutdown(1);
                    }
                },
                TimeSpan.FromSeconds(1));
            return;
        }

        await RunViewerFactoryChecksAsync().ConfigureAwait(true);
        await RunGameContentCheckAsync().ConfigureAwait(true);
        await RunGameContentViewerCheckAsync(window).ConfigureAwait(true);
        await RunWorldRenderCheckAsync(window).ConfigureAwait(true);
        await RunGlInfrastructureCheckAsync().ConfigureAwait(true);
        await RunNavMeshRenderCheckAsync(window).ConfigureAwait(true);
        await RunTwoViewportsCheckAsync(window).ConfigureAwait(true);
        await RunMeshRenderCheckAsync(window).ConfigureAwait(true);
        await RunParticleRenderCheckAsync(window).ConfigureAwait(true);
        await RunFixtureGlRenderCheckAsync<GUI.Linux.GL.TextureGlRenderer>(window, "Tests/Files/pip-left.vsvg_c", "panorama vector").ConfigureAwait(true);
        await RunFixtureGlRenderCheckAsync<GUI.Linux.GL.ParticleSnapshotGlRenderer>(window, "Tests/Files/test.vsnap_c", "particle snapshot").ConfigureAwait(true);
        await RunFixtureGlRenderCheckAsync<GUI.Linux.GL.WorldNodeGlRenderer>(window, "Tests/Files/node000_kv3_v2_zstd.vwnod_c", "world node").ConfigureAwait(true);
        await RunFixtureGlRenderCheckAsync<GUI.Linux.GL.TextureGlRenderer>(window, "Tests/Files/a1_eli_corridor_kv3_v1_uncompressed.vpost_c", "postprocessing lut").ConfigureAwait(true);
        await RunFixtureGlRenderCheckAsync<GUI.Linux.GL.GraphGlRenderer>(window, "Tests/Files/vrf_all_nodes.vanmgrph_c", "ag1 graph").ConfigureAwait(true);
        await RunFixtureGlRenderCheckAsync<GUI.Linux.GL.GraphGlRenderer>(window, "Tests/Files/de_inferno_script.vpulse_c", "pulse graph").ConfigureAwait(true);
        await RunFixtureGlRenderCheckAsync<GUI.Linux.GL.GraphGlRenderer>(window, "Tests/Files/default_ents.vents_c", "entity io graph").ConfigureAwait(true);
        await RunVoxelVisibilityRenderCheckAsync(window).ConfigureAwait(true);
        await RunModelRenderCheckAsync(window).ConfigureAwait(true);
        await RunModelAnimationCheckAsync(window).ConfigureAwait(true);
        await RunKeyboardRoutingCheckAsync(window).ConfigureAwait(true);
        await RunMaterialRenderCheckAsync(window).ConfigureAwait(true);
        await RunAnimationRenderCheckAsync(window).ConfigureAwait(true);
        await RunPhysicsRenderCheckAsync(window).ConfigureAwait(true);
        await RunGraphRenderCheckAsync(window).ConfigureAwait(true);
        await RunSkyboxRenderCheckAsync(window).ConfigureAwait(true);
        await RunBrowserCheckAsync(window).ConfigureAwait(true);
        await RunAudioCheckAsync(window).ConfigureAwait(true);

        output.WriteLine("[self-check] shell opened, exiting");
        DispatcherTimer.RunOnce(window.Close, TimeSpan.FromSeconds(1));
    }

    private static async Task RunGameContentCheckAsync()
    {
        try
        {
            var installs = GameContentLocator.DiscoverInstalledGames();
            await Program.StdOut.WriteLineAsync(
                $"[self-check] steam games: {string.Join(", ", installs.Select(g => $"{g.Name} ({g.AppId}) {g.ContentRoot}"))}").ConfigureAwait(true);

            var context = LinuxGameContent.Context;

            if (context.Install is not { } install)
            {
                await Program.StdOut.WriteLineAsync("[self-check] game content: no installed game resolved").ConfigureAwait(true);
                return;
            }

            await Program.StdOut.WriteLineAsync(
                $"[self-check] game content: {install.Name} ({install.AppId}), root={install.ContentRoot}").ConfigureAwait(true);

            var loader = context.FileLoader;
            var vpkPath = Path.Combine(install.ContentRoot, "pak01_dir.vpk");

            using var package = new Package();
            package.Read(vpkPath);
            await Program.StdOut.WriteLineAsync($"[self-check] main vpk: {package.Entries?.Count ?? 0} entry types").ConfigureAwait(true);
            var typeNames = package.Entries is null ? [] : package.Entries.Keys.OrderBy(static k => k).ToList();
            await Program.StdOut.WriteLineAsync($"[self-check] vpk types: {string.Join(", ", typeNames)}").ConfigureAwait(true);

            static string? FirstEntryPath(Package package, string type)
                => package.Entries != null && package.Entries.TryGetValue(type, out var entries) && entries.Count > 0
                    ? entries[0].GetFullPath()
                    : null;

            // Model: a real game model loads and parses.
            var modelPath = FirstEntryPath(package, "vmdl_c");
            if (modelPath != null)
            {
                using var modelResource = loader.LoadFile(modelPath);
                await Program.StdOut.WriteLineAsync(
                    $"[self-check] content model: {modelPath} -> {(modelResource?.DataBlock as Model)?.Name ?? "<not a model>"}").ConfigureAwait(true);
            }

            // NmClip: its skeleton is resolved through the game loader.
            var clipPath = FirstEntryPath(package, "vnmclip_c");
            if (clipPath != null)
            {
                using var clipResource = loader.LoadFile(clipPath);
                if (clipResource?.DataBlock is AnimationClip clip && !string.IsNullOrEmpty(clip.SkeletonName))
                {
                    using var skeletonResource = loader.LoadFileCompiled(clip.SkeletonName);
                    await Program.StdOut.WriteLineAsync(
                        $"[self-check] content nmclip: {clipPath} -> skeleton {clip.SkeletonName} = {skeletonResource?.ResourceType}").ConfigureAwait(true);
                }
            }

            // SmartProp: its referenced model is resolved through the game loader.
            var smartPropEntries = package.Entries?.GetValueOrDefault("vsmart_c");
            if (smartPropEntries is { Count: > 0 })
            {
                const int MaxSmartPropsToScan = 25;
                var resolvedSmartProp = false;

                static string? FindChildModel(SmartProp smartProp)
                {
                    var children = smartProp.Data.Root.GetArray("m_Children");

                    if (children is null)
                    {
                        return null;
                    }

                    foreach (var child in children)
                    {
                        var className = child.GetStringProperty("_class");

                        if (className == "CSmartPropElement_Model")
                        {
                            var name = child.GetStringProperty("m_sModelName");
                            return string.IsNullOrEmpty(name) ? null : name;
                        }

                        if (className is "CSmartPropElement_Group" or "CSmartPropElement_PickOne")
                        {
                            var nested = child.GetArray("m_Children");

                            if (nested is null)
                            {
                                continue;
                            }

                            foreach (var nestedChild in nested)
                            {
                                if (nestedChild.GetStringProperty("_class") == "CSmartPropElement_Model")
                                {
                                    var name = nestedChild.GetStringProperty("m_sModelName");
                                    return string.IsNullOrEmpty(name) ? null : name;
                                }
                            }
                        }
                    }

                    return null;
                }

                for (var i = 0; i < Math.Min(MaxSmartPropsToScan, smartPropEntries.Count) && !resolvedSmartProp; i++)
                {
                    var smartPropPath = smartPropEntries[i].GetFullPath();
                    using var smartPropResource = loader.LoadFile(smartPropPath);

                    if (smartPropResource?.DataBlock is not SmartProp smartProp)
                    {
                        continue;
                    }

                    var childModel = FindChildModel(smartProp);

                    if (string.IsNullOrEmpty(childModel))
                    {
                        continue;
                    }

                    using var childResource = loader.LoadFileCompiled(childModel);
                    await Program.StdOut.WriteLineAsync(
                        $"[self-check] content smartprop: {smartPropPath} -> model {childModel} = {(childResource?.DataBlock as Model)?.Name ?? childResource?.ResourceType.ToString() ?? "<unresolved>"}").ConfigureAwait(true);
                    resolvedSmartProp = true;
                }
            }

            // World: maps live in their own VPKs under game/<mod>/maps. Add the smallest one that
            // contains world data to the search paths, then resolve the world and one external reference.
            var mapsDir = Path.Combine(install.ContentRoot, "maps");

            if (Directory.Exists(mapsDir))
            {
                string? worldName = null;
                string? mapEntry = null;

                foreach (var mapVpk in Directory.EnumerateFiles(mapsDir, "*.vpk").OrderBy(static f => new FileInfo(f).Length))
                {
                    using var candidate = new Package();
                    candidate.Read(mapVpk);

                    if (candidate.Entries?.ContainsKey("vwrld_c") != true && candidate.Entries?.ContainsKey("vmap_c") != true)
                    {
                        continue;
                    }

                    loader.AddPackageToSearch(mapVpk);

                    mapEntry = candidate.Entries.GetValueOrDefault("vmap_c") is { Count: > 0 } maps
                        ? maps[0].GetFullPath()
                        : candidate.Entries.GetValueOrDefault("vwrld_c") is { Count: > 0 } worlds ? worlds[0].GetFullPath() : null;

                    worldName = mapEntry != null && mapEntry.Contains(".vmap", StringComparison.OrdinalIgnoreCase)
                        ? WorldLoader.GetWorldNameFromMap(mapEntry)
                        : mapEntry;

                    break;
                }

                if (worldName != null)
                {
                    using var worldResource = loader.LoadFileCompiled(worldName);
                    var worldRefs = worldResource?.ExternalReferences?.ResourceRefInfoList;
                    var firstRef = worldRefs is { Count: > 0 } ? worldRefs[0].Name : null;

                    using var firstRefResource = firstRef != null ? loader.LoadFileCompiled(firstRef) : null;

                    await Program.StdOut.WriteLineAsync(
                        $"[self-check] content world: {mapEntry} -> {worldName} = {worldResource?.ResourceType.ToString() ?? "<unresolved>"}, "
                        + $"extRefs={worldRefs?.Count ?? 0}, firstRef={firstRef} = {firstRefResource?.ResourceType.ToString() ?? "<unresolved>"}").ConfigureAwait(true);

                    if (worldResource?.DataBlock is World worldData)
                    {
                        await Program.StdOut.WriteLineAsync(
                            $"[self-check] content world nodes: m_worldNodes={worldData.GetWorldNodeNames().Count}, entityLumps={worldData.GetEntityLumpNames().Count}").ConfigureAwait(true);
                    }
                }
            }
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] game content failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task RunGameContentViewerCheckAsync(MainWindow window)
    {
        if (LinuxGameContent.Context.Install is not { } install)
        {
            return;
        }

        try
        {
            var vpkPath = Path.Combine(install.ContentRoot, "pak01_dir.vpk");
            using var package = new Package();
            package.Read(vpkPath);

            var tempDir = Path.Combine(Path.GetTempPath(), "s2v-phase6");
            Directory.CreateDirectory(tempDir);

            static void Extract(Package package, string entryPath, string destination)
            {
                var entry = package.FindEntry(entryPath)
                    ?? throw new InvalidDataException($"VPK entry not found: {entryPath}");

                using var stream = GameFileLoader.GetPackageEntryStream(package, entry);
                using var file = File.Create(destination);
                stream.CopyTo(file);
            }

            // A real game model, opened through the normal shell, resolving its materials from the game.
            var modelEntry = package.Entries?["vmdl_c"]?.FirstOrDefault()?.GetFullPath();
            if (modelEntry != null)
            {
                var modelPath = Path.Combine(tempDir, Path.GetFileName(modelEntry));
                Extract(package, modelEntry, modelPath);

                await window.OpenFileAsync(modelPath).ConfigureAwait(true);
                var (modelViewport, modelRenderer) = await WaitForRendererAsync<ModelGlRenderer>(window, 1, 30000).ConfigureAwait(true);

                if (modelViewport is not null)
                {
                    await Task.Delay(300).ConfigureAwait(true);
                    await Program.StdOut.WriteLineAsync(
                        $"[self-check] game model view: {modelEntry} -> distinctColors={modelRenderer.ReadbackDistinctColors}, nonBackgroundPixels={modelRenderer.ReadbackNonBackgroundPixels}, glError-free").ConfigureAwait(true);
                    window.CloseTabContaining(modelViewport);
                }
                else
                {
                    await Program.StdOut.WriteLineAsync($"[self-check] game model view timed out: {modelEntry}").ConfigureAwait(true);
                }

                File.Delete(modelPath);
            }

            // A real game smart prop, resolving its referenced models from the game through the shell.
            var smartPropEntry = package.Entries?["vsmart_c"]?.FirstOrDefault()?.GetFullPath();
            if (smartPropEntry != null)
            {
                var smartPropPath = Path.Combine(tempDir, Path.GetFileName(smartPropEntry));
                Extract(package, smartPropEntry, smartPropPath);

                await window.OpenFileAsync(smartPropPath).ConfigureAwait(true);
                var (smartPropViewport, smartPropRenderer) = await WaitForRendererAsync<SmartPropGlRenderer>(window, 1, 30000).ConfigureAwait(true);

                if (smartPropViewport is not null)
                {
                    await Task.Delay(300).ConfigureAwait(true);
                    await Program.StdOut.WriteLineAsync(
                        $"[self-check] game smartprop view: {smartPropEntry} -> distinctColors={smartPropRenderer.ReadbackDistinctColors}, nonBackgroundPixels={smartPropRenderer.ReadbackNonBackgroundPixels}").ConfigureAwait(true);
                    window.CloseTabContaining(smartPropViewport);
                }
                else
                {
                    await Program.StdOut.WriteLineAsync($"[self-check] game smartprop view timed out: {smartPropEntry}").ConfigureAwait(true);
                }

                File.Delete(smartPropPath);
            }

            // A real game animation clip, whose skeleton is resolved from the game through the shell.
            var clipEntry = package.Entries?["vnmclip_c"]?.FirstOrDefault()?.GetFullPath();
            if (clipEntry != null)
            {
                var clipPath = Path.Combine(tempDir, Path.GetFileName(clipEntry));
                Extract(package, clipEntry, clipPath);

                await window.OpenFileAsync(clipPath).ConfigureAwait(true);
                var (clipViewport, clipRenderer) = await WaitForRendererAsync<AnimationGlRenderer>(window, 1, 60000).ConfigureAwait(true);

                if (clipViewport is not null)
                {
                    await Task.Delay(300).ConfigureAwait(true);
                    await Program.StdOut.WriteLineAsync(
                        $"[self-check] game clip view: {clipEntry} -> distinctColors={clipRenderer.ReadbackDistinctColors}, nonBackgroundPixels={clipRenderer.ReadbackNonBackgroundPixels}").ConfigureAwait(true);
                    window.CloseTabContaining(clipViewport);
                }
                else
                {
                    await Program.StdOut.WriteLineAsync($"[self-check] game clip view timed out: {clipEntry}").ConfigureAwait(true);
                }

                File.Delete(clipPath);
            }

            // A real game texture, rendered with the internal texture_decode shader. Prefer a sizeable
            // texture so the readback proves real image content rather than a tiny uniform mask.
            var textureEntry = package.Entries?.GetValueOrDefault("vtex_c")
                ?.Where(static entry => entry.Length is > 50000 and < 8_000_000)
                .OrderByDescending(static entry => entry.Length)
                .FirstOrDefault()?.GetFullPath();
            if (textureEntry != null)
            {
                var texturePath = Path.Combine(tempDir, Path.GetFileName(textureEntry));
                Extract(package, textureEntry, texturePath);

                await window.OpenFileAsync(texturePath).ConfigureAwait(true);
                var (textureViewport, textureRenderer) = await WaitForRendererAsync<TextureGlRenderer>(window, 1, 60000).ConfigureAwait(true);

                if (textureViewport is not null)
                {
                    await Task.Delay(300).ConfigureAwait(true);
                    await Program.StdOut.WriteLineAsync(
                        $"[self-check] game texture view: {textureEntry} -> distinctColors={textureRenderer.ReadbackDistinctColors}, nonBackgroundPixels={textureRenderer.ReadbackNonBackgroundPixels}").ConfigureAwait(true);
                    window.CloseTabContaining(textureViewport);
                }
                else
                {
                    await Program.StdOut.WriteLineAsync($"[self-check] game texture view timed out: {textureEntry}").ConfigureAwait(true);
                }

                File.Delete(texturePath);
            }
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] game content viewer failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task RunWorldRenderCheckAsync(MainWindow window)
    {
        if (LinuxGameContent.Context.Install is not { } install)
        {
            return;
        }

        try
        {
            var mapsDir = Path.Combine(install.ContentRoot, "maps");

            if (!Directory.Exists(mapsDir))
            {
                return;
            }

            string? worldEntry = null;
            string? mapVpkPath = null;

            var allMapVpks = Directory.EnumerateFiles(mapsDir, "*.vpk")
                .OrderBy(static f => new FileInfo(f).Length)
                .ToList();

            // Prefer a real gameplay map (larger world-node data) but stay within a sane size to keep
            // the validation run bounded; fall back to the smallest map with world data.
            string[] preferredMaps = ["dl_hideout", "dl_streets", "hero_testing", "1v1_test", "start"];

            foreach (var mapVpk in allMapVpks.OrderByDescending(static f => new FileInfo(f).Length))
            {
                using var package = new Package();
                package.Read(mapVpk);

                var worldCount = package.Entries?.GetValueOrDefault("vwrld_c")?.Count ?? 0;
                var nodeCount = package.Entries?.GetValueOrDefault("vwnod_c")?.Count ?? 0;

                var name = Path.GetFileNameWithoutExtension(mapVpk);
                var isPreferred = preferredMaps.Contains(name, StringComparer.OrdinalIgnoreCase);
                var withinSize = new FileInfo(mapVpk).Length < 500L * 1024 * 1024;

                if (worldCount > 0 && nodeCount > 0 && isPreferred && withinSize)
                {
                    worldEntry = package.Entries!.GetValueOrDefault("vwrld_c")![0].GetFullPath();
                    mapVpkPath = mapVpk;
                    break;
                }
            }

            if (worldEntry is null || mapVpkPath is null)
            {
                await Program.StdOut.WriteLineAsync("[self-check] world: no map VPK with world data found").ConfigureAwait(true);
                return;
            }

            LinuxGameContent.AddSearchPackage(mapVpkPath);

            var tempDir = Path.Combine(Path.GetTempPath(), "s2v-phase6");
            Directory.CreateDirectory(tempDir);

            var worldPath = Path.Combine(tempDir, "world.vwrld_c");

            using (var package = new Package())
            {
                package.Read(mapVpkPath);
                var entry = package.FindEntry(worldEntry) ?? throw new InvalidDataException($"VPK entry not found: {worldEntry}");
                using var stream = GameFileLoader.GetPackageEntryStream(package, entry);
                using var file = File.Create(worldPath);
                await stream.CopyToAsync(file).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync($"[self-check] world: {worldEntry} (from {Path.GetFileName(mapVpkPath)})").ConfigureAwait(true);

            await window.OpenFileAsync(worldPath).ConfigureAwait(true);
            var (worldViewport, worldRenderer) = await WaitForRendererAsync<WorldGlRenderer>(window, 1, 300000).ConfigureAwait(true);

            if (worldViewport is not null)
            {
                for (var i = 0; i < 100 && worldRenderer.RenderedFrames < 3; i++)
                {
                    await Task.Delay(50).ConfigureAwait(true);
                }

                await Program.StdOut.WriteLineAsync(
                    $"[self-check] world view: frames={worldRenderer.RenderedFrames}, distinctColors={worldRenderer.ReadbackDistinctColors}, "
                    + $"nonBackgroundPixels={worldRenderer.ReadbackNonBackgroundPixels}, viewport={worldRenderer.ViewportWidth}x{worldRenderer.ViewportHeight}").ConfigureAwait(true);
                await Program.StdOut.WriteLineAsync(worldRenderer.ReadbackNonBackgroundPixels > 0 && worldRenderer.ReadbackDistinctColors > 1
                    ? "[self-check] world tab rendered real geometry"
                    : "[self-check] world tab rendered only a flat frame").ConfigureAwait(true);

                await Program.StdOut.WriteLineAsync($"[self-check] world scene sound: {worldRenderer.HasSoundPlayer}").ConfigureAwait(true);

                window.CloseTabContaining(worldViewport);
                for (var i = 0; i < 60 && !worldRenderer.Disposed; i++)
                {
                    await Task.Delay(50).ConfigureAwait(true);
                }

                await Program.StdOut.WriteLineAsync($"[self-check] world tab closed, renderer disposed={worldRenderer.Disposed}").ConfigureAwait(true);
            }
            else
            {
                await Program.StdOut.WriteLineAsync("[self-check] world view timed out").ConfigureAwait(true);
            }

            File.Delete(worldPath);
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] world view failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task<bool> RunGlInfrastructureCheckAsync()
    {
        try
        {
            var viewport = new AvaloniaGlViewport { RendererFactory = static () => new GlSmokeRenderer() };

            var glWindow = new Window
            {
                Title = "GL self check",
                Width = 320,
                Height = 240,
                Content = viewport,
            };

            var firstFrame = new TaskCompletionSource();
            viewport.FrameRendered += () => firstFrame.TrySetResult();

            glWindow.Show();

            var completed = await Task.WhenAny(firstFrame.Task, Task.Delay(15000)).ConfigureAwait(true);

            await Program.StdOut.WriteLineAsync(completed == firstFrame.Task
                ? "[self-check] GL viewport rendered a frame"
                : "[self-check] GL viewport did not render within timeout").ConfigureAwait(true);

            glWindow.Close();

            return completed == firstFrame.Task;
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] GL infrastructure failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
            return false;
        }
    }

    private static async Task<(GUI.Linux.GL.AvaloniaGlViewport Viewport, T Renderer)> WaitForRendererAsync<T>(MainWindow window, int count, int timeoutMs)
        where T : GUI.Linux.GL.ViewportGlRenderer
    {
        var deadline = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 < deadline)
        {
            var ready = window.GetGlViewports()
                .Select(viewport => (Viewport: viewport, Renderer: viewport.ViewportRenderer as T))
                .Where(pair => pair.Renderer is { RenderedFrames: > 0 })
                .Select(pair => (pair.Viewport, pair.Renderer!))
                .ToList();

            if (ready.Count >= count)
            {
                return ready[0];
            }

            await Task.Delay(50).ConfigureAwait(true);
        }

        return (null!, null!);
    }

    private static async Task<List<(GUI.Linux.GL.AvaloniaGlViewport Viewport, T Renderer)>> WaitForRendererManyAsync<T>(MainWindow window, int count, int timeoutMs)
        where T : GUI.Linux.GL.ViewportGlRenderer
    {
        var deadline = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 < deadline)
        {
            var ready = window.GetGlViewports()
                .Select(viewport => (Viewport: viewport, Renderer: viewport.ViewportRenderer as T))
                .Where(pair => pair.Renderer is { RenderedFrames: > 0 })
                .Select(pair => (pair.Viewport, pair.Renderer!))
                .ToList();

            if (ready.Count >= count)
            {
                return ready;
            }

            await Task.Delay(50).ConfigureAwait(true);
        }

        return [];
    }

    private static async Task RunNavMeshRenderCheckAsync(MainWindow window)
    {
        const string navPath = "Tests/Files/lobby_mapveto.nav";

        if (!File.Exists(navPath))
        {
            await Program.StdOut.WriteLineAsync($"[self-check] navmesh sample missing: {navPath}").ConfigureAwait(true);
            return;
        }

        try
        {
            await window.OpenFileAsync(navPath).ConfigureAwait(true);

            var (viewport, renderer) = await WaitForRendererAsync<NavMeshGlRenderer>(window, 1, 30000).ConfigureAwait(true);

            if (viewport is null)
            {
                await Program.StdOut.WriteLineAsync("[self-check] navmesh tab render timed out").ConfigureAwait(true);
                return;
            }

            for (var i = 0; i < 50 && renderer.RenderedFrames < 3; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync(
                $"[self-check] navmesh tab: frames={renderer.RenderedFrames}, distinctColors={renderer.ReadbackDistinctColors}, "
                + $"nonBackgroundPixels={renderer.ReadbackNonBackgroundPixels}, viewport={renderer.ViewportWidth}x{renderer.ViewportHeight}").ConfigureAwait(true);

            await Program.StdOut.WriteLineAsync(renderer.ReadbackNonBackgroundPixels > 0 && renderer.ReadbackDistinctColors > 1
                ? "[self-check] navmesh tab rendered real geometry"
                : "[self-check] navmesh tab rendered only a flat frame").ConfigureAwait(true);

            // Camera movement via the viewport input.
            var before = renderer.CameraLocation;
            RaisePointerEntered(viewport);
            viewport.Input.Keys |= GUI.Linux.Types.GLViewers.ViewerKey.W;
            await Task.Delay(700).ConfigureAwait(true);
            viewport.Input.Keys = GUI.Linux.Types.GLViewers.ViewerKey.None;
            await Task.Delay(100).ConfigureAwait(true);
            var moved = System.Numerics.Vector3.Distance(before, renderer.CameraLocation);
            await Program.StdOut.WriteLineAsync($"[self-check] navmesh tab camera input moved {moved:0.00} units").ConfigureAwait(true);

            // Focus loss: clearing the host input must stop movement (no stuck keys).
            var afterMove = renderer.CameraLocation;
            await Task.Delay(300).ConfigureAwait(true);
            var driftAfterClear = System.Numerics.Vector3.Distance(afterMove, renderer.CameraLocation);
            await Program.StdOut.WriteLineAsync($"[self-check] navmesh tab after input cleared drift {driftAfterClear:0.000} units").ConfigureAwait(true);

            // Resize the shell window; the viewport must follow.
            var widthBefore = renderer.ViewportWidth;
            window.Width += 200;
            await Task.Delay(600).ConfigureAwait(true);
            await Program.StdOut.WriteLineAsync(
                $"[self-check] navmesh tab resize: viewport {widthBefore} -> {renderer.ViewportWidth}").ConfigureAwait(true);

            // Close the tab and confirm the renderer was disposed.
            window.CloseTabContaining(viewport);
            for (var i = 0; i < 60 && !renderer.Disposed; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync($"[self-check] navmesh tab closed, renderer disposed={renderer.Disposed}").ConfigureAwait(true);

            // Reopen the same file and confirm a fresh viewport renders.
            await window.OpenFileAsync(navPath).ConfigureAwait(true);
            var (reopenedViewport, reopenedRenderer) = await WaitForRendererAsync<NavMeshGlRenderer>(window, 1, 30000).ConfigureAwait(true);

            if (reopenedViewport is not null)
            {
                await Task.Delay(300).ConfigureAwait(true);
                await Program.StdOut.WriteLineAsync(
                    $"[self-check] navmesh tab reopened: frames={reopenedRenderer.RenderedFrames}, "
                    + $"nonBackgroundPixels={reopenedRenderer.ReadbackNonBackgroundPixels}, disposed={reopenedRenderer.Disposed}").ConfigureAwait(true);
                window.CloseTabContaining(reopenedViewport);
            }
            else
            {
                await Program.StdOut.WriteLineAsync("[self-check] navmesh reopen timed out").ConfigureAwait(true);
            }
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] navmesh tab failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task RunTwoViewportsCheckAsync(MainWindow window)
    {
        const string navPath = "Tests/Files/lobby_mapveto.nav";

        if (!File.Exists(navPath))
        {
            return;
        }

        try
        {
            await window.OpenFileAsync(navPath).ConfigureAwait(true);
            var (viewportA, rendererA) = await WaitForRendererAsync<NavMeshGlRenderer>(window, 1, 30000).ConfigureAwait(true);

            if (viewportA is null)
            {
                await Program.StdOut.WriteLineAsync("[self-check] two navmesh tabs: first tab timed out").ConfigureAwait(true);
                return;
            }

            await window.OpenFileAsync(navPath).ConfigureAwait(true);
            var readyForB = await WaitForRendererManyAsync<NavMeshGlRenderer>(window, 2, 30000).ConfigureAwait(true);
            var pairB = readyForB.FirstOrDefault(pair => !ReferenceEquals(pair.Renderer, rendererA));

            if (pairB.Renderer is null)
            {
                await Program.StdOut.WriteLineAsync("[self-check] two navmesh tabs: second tab timed out").ConfigureAwait(true);
                return;
            }

            var viewportB = pairB.Viewport;
            var rendererB = pairB.Renderer;

            var bothAlive = !rendererA.Disposed && !rendererB.Disposed;
            await Program.StdOut.WriteLineAsync(
                $"[self-check] two navmesh tabs opened: A alive={!rendererA.Disposed}, B alive={!rendererB.Disposed}, "
                + $"distinct renderers={!ReferenceEquals(rendererA, rendererB)}").ConfigureAwait(true);

            // Select A, drive its camera, and confirm B's camera is untouched.
            window.SelectTabContaining(viewportA);
            await Task.Delay(400).ConfigureAwait(true);

            var aBefore = rendererA.CameraLocation;
            var bCameraBefore = rendererB.CameraLocation;

            RaisePointerEntered(viewportA);
            viewportA.Input.Keys |= GUI.Linux.Types.GLViewers.ViewerKey.W;
            await Task.Delay(700).ConfigureAwait(true);
            viewportA.Input.Keys = GUI.Linux.Types.GLViewers.ViewerKey.None;
            await Task.Delay(100).ConfigureAwait(true);

            var aMoved = System.Numerics.Vector3.Distance(aBefore, rendererA.CameraLocation);
            var bMoved = System.Numerics.Vector3.Distance(bCameraBefore, rendererB.CameraLocation);

            await Program.StdOut.WriteLineAsync(
                $"[self-check] two navmesh tabs: A moved {aMoved:0.00}, B moved {bMoved:0.00} (input on A only)").ConfigureAwait(true);

            // Switch to B; it must render again and A must remain alive.
            window.SelectTabContaining(viewportB);
            await Task.Delay(600).ConfigureAwait(true);

            await Program.StdOut.WriteLineAsync(
                $"[self-check] after switching to B: A alive={!rendererA.Disposed}, B alive={!rendererB.Disposed}, "
                + $"B frames={rendererB.RenderedFrames}").ConfigureAwait(true);

            // Close A while B is selected; A releases its renderer, B is unaffected.
            window.CloseTabContaining(viewportA);
            for (var i = 0; i < 60 && !rendererA.Disposed; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync(
                $"[self-check] after closing A tab: A disposed={rendererA.Disposed}, B disposed={rendererB.Disposed}, "
                + $"B frames={rendererB.RenderedFrames}").ConfigureAwait(true);

            window.CloseTabContaining(viewportB);

            await Program.StdOut.WriteLineAsync(bothAlive
                ? "[self-check] two navmesh tabs simultaneous with independent cameras"
                : "[self-check] two navmesh tabs were not simultaneously alive").ConfigureAwait(true);
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] two navmesh tabs failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task RunMeshRenderCheckAsync(MainWindow window)
    {
        const string meshPath = "Tests/Files/chen_weapon.vmesh_c";

        if (!File.Exists(meshPath))
        {
            await Program.StdOut.WriteLineAsync($"[self-check] mesh sample missing: {meshPath}").ConfigureAwait(true);
            return;
        }

        try
        {
            await window.OpenFileAsync(meshPath).ConfigureAwait(true);

            var (viewport, renderer) = await WaitForRendererAsync<MeshGlRenderer>(window, 1, 30000).ConfigureAwait(true);

            if (viewport is null)
            {
                await Program.StdOut.WriteLineAsync("[self-check] mesh tab render timed out").ConfigureAwait(true);
                return;
            }

            for (var i = 0; i < 50 && renderer.RenderedFrames < 3; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync(
                $"[self-check] mesh tab: frames={renderer.RenderedFrames}, distinctColors={renderer.ReadbackDistinctColors}, "
                + $"nonBackgroundPixels={renderer.ReadbackNonBackgroundPixels}, viewport={renderer.ViewportWidth}x{renderer.ViewportHeight}").ConfigureAwait(true);

            await Program.StdOut.WriteLineAsync(renderer.ReadbackNonBackgroundPixels > 0 && renderer.ReadbackDistinctColors > 1
                ? "[self-check] mesh tab rendered real geometry"
                : "[self-check] mesh tab rendered only a flat frame").ConfigureAwait(true);

            var before = renderer.CameraLocation;
            RaisePointerEntered(viewport);
            viewport.Input.Keys |= GUI.Linux.Types.GLViewers.ViewerKey.W;
            await Task.Delay(600).ConfigureAwait(true);
            viewport.Input.Keys = GUI.Linux.Types.GLViewers.ViewerKey.None;
            await Task.Delay(100).ConfigureAwait(true);
            await Program.StdOut.WriteLineAsync($"[self-check] mesh tab camera input moved {System.Numerics.Vector3.Distance(before, renderer.CameraLocation):0.00} units").ConfigureAwait(true);

            window.CloseTabContaining(viewport);
            for (var i = 0; i < 60 && !renderer.Disposed; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync($"[self-check] mesh tab closed, renderer disposed={renderer.Disposed}").ConfigureAwait(true);

            await window.OpenFileAsync(meshPath).ConfigureAwait(true);
            var (reopenedViewport, reopenedRenderer) = await WaitForRendererAsync<MeshGlRenderer>(window, 1, 30000).ConfigureAwait(true);

            if (reopenedViewport is not null)
            {
                await Task.Delay(300).ConfigureAwait(true);
                await Program.StdOut.WriteLineAsync(
                    $"[self-check] mesh tab reopened: frames={reopenedRenderer.RenderedFrames}, "
                    + $"nonBackgroundPixels={reopenedRenderer.ReadbackNonBackgroundPixels}, disposed={reopenedRenderer.Disposed}").ConfigureAwait(true);
                window.CloseTabContaining(reopenedViewport);
            }
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] mesh tab failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task RunParticleRenderCheckAsync(MainWindow window)
    {
        const string particlePath = "Tests/Files/explosion_barrel_kv0_lz4.vpcf_c";

        if (!File.Exists(particlePath))
        {
            await Program.StdOut.WriteLineAsync($"[self-check] particle sample missing: {particlePath}").ConfigureAwait(true);
            return;
        }

        try
        {
            await window.OpenFileAsync(particlePath).ConfigureAwait(true);

            var (viewport, renderer) = await WaitForRendererAsync<ParticleGlRenderer>(window, 1, 30000).ConfigureAwait(true);

            if (viewport is null)
            {
                await Program.StdOut.WriteLineAsync("[self-check] particle tab render timed out").ConfigureAwait(true);
                return;
            }

            for (var i = 0; i < 50 && renderer.RenderedFrames < 3; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync(
                $"[self-check] particle tab: frames={renderer.RenderedFrames}, distinctColors={renderer.ReadbackDistinctColors}, "
                + $"nonBackgroundPixels={renderer.ReadbackNonBackgroundPixels}, viewport={renderer.ViewportWidth}x{renderer.ViewportHeight}").ConfigureAwait(true);

            await Program.StdOut.WriteLineAsync(renderer.ReadbackNonBackgroundPixels > 0 && renderer.ReadbackDistinctColors > 1
                ? "[self-check] particle tab rendered real geometry"
                : "[self-check] particle tab rendered only a flat frame").ConfigureAwait(true);

            window.CloseTabContaining(viewport);
            for (var i = 0; i < 60 && !renderer.Disposed; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync($"[self-check] particle tab closed, renderer disposed={renderer.Disposed}").ConfigureAwait(true);
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] particle tab failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task RunVoxelVisibilityRenderCheckAsync(MainWindow window)
    {
        const string visibilityPath = "Tests/Files/world_visibility.vvis_c";

        if (!File.Exists(visibilityPath))
        {
            await Program.StdOut.WriteLineAsync($"[self-check] visibility sample missing: {visibilityPath}").ConfigureAwait(true);
            return;
        }

        try
        {
            await window.OpenFileAsync(visibilityPath).ConfigureAwait(true);

            var (viewport, renderer) = await WaitForRendererAsync<VoxelVisibilityGlRenderer>(window, 1, 30000).ConfigureAwait(true);

            if (viewport is null)
            {
                await Program.StdOut.WriteLineAsync("[self-check] visibility tab render timed out").ConfigureAwait(true);
                return;
            }

            for (var i = 0; i < 50 && renderer.RenderedFrames < 3; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync(
                $"[self-check] visibility tab: frames={renderer.RenderedFrames}, distinctColors={renderer.ReadbackDistinctColors}, "
                + $"nonBackgroundPixels={renderer.ReadbackNonBackgroundPixels}, viewport={renderer.ViewportWidth}x{renderer.ViewportHeight}").ConfigureAwait(true);

            await Program.StdOut.WriteLineAsync(renderer.ReadbackNonBackgroundPixels > 0 && renderer.ReadbackDistinctColors > 1
                ? "[self-check] visibility tab rendered real geometry"
                : "[self-check] visibility tab rendered only a flat frame").ConfigureAwait(true);

            window.CloseTabContaining(viewport);
            for (var i = 0; i < 60 && !renderer.Disposed; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync($"[self-check] visibility tab closed, renderer disposed={renderer.Disposed}").ConfigureAwait(true);
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] visibility tab failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task RunFixtureGlRenderCheckAsync<T>(MainWindow window, string path, string label)
        where T : GUI.Linux.GL.ViewportGlRenderer
    {
        if (!File.Exists(path))
        {
            await Program.StdOut.WriteLineAsync($"[self-check] {label} sample missing: {path}").ConfigureAwait(true);
            return;
        }

        try
        {
            await window.OpenFileAsync(path).ConfigureAwait(true);

            var (viewport, renderer) = await WaitForRendererAsync<T>(window, 1, 30000).ConfigureAwait(true);

            if (viewport is null)
            {
                await Program.StdOut.WriteLineAsync($"[self-check] {label} tab render timed out").ConfigureAwait(true);
                return;
            }

            for (var i = 0; i < 60 && renderer.RenderedFrames < 3; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync(
                $"[self-check] {label} tab: frames={renderer.RenderedFrames}, distinctColors={renderer.ReadbackDistinctColors}, "
                + $"nonBackgroundPixels={renderer.ReadbackNonBackgroundPixels}").ConfigureAwait(true);

            window.CloseTabContaining(viewport);
            for (var i = 0; i < 60 && !renderer.Disposed; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync($"[self-check] {label} tab closed, renderer disposed={renderer.Disposed}").ConfigureAwait(true);
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] {label} failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task RunKeyboardRoutingCheckAsync(MainWindow window)
    {
        const string modelPath = "Tests/Files/export_test.vmdl_c";

        if (!File.Exists(modelPath))
        {
            await Program.StdOut.WriteLineAsync($"[self-check] keyboard sample missing: {modelPath}").ConfigureAwait(true);
            return;
        }

        try
        {
            await window.OpenFileAsync(modelPath).ConfigureAwait(true);

            var (viewport, renderer) = await WaitForRendererAsync<ModelGlRenderer>(window, 1, 30000).ConfigureAwait(true);

            if (viewport is null || renderer.SceneCore is null)
            {
                await Program.StdOut.WriteLineAsync("[self-check] keyboard routing: model renderer timed out").ConfigureAwait(true);
                return;
            }

            for (var i = 0; i < 50 && renderer.RenderedFrames < 2; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            var before = renderer.SceneCore.PerfDisplayMode;
            viewport.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab });
            var after = renderer.SceneCore.PerfDisplayMode;

            await Program.StdOut.WriteLineAsync(after != before
                ? $"[self-check] keyboard routing: Tab changed performance overlay {before} -> {after}"
                : "[self-check] keyboard routing: Tab did not reach the scene core").ConfigureAwait(true);

            SkiaSharp.SKBitmap? screenshot = null;
            renderer.RequestScreenshot(bitmap => screenshot = bitmap);
            for (var i = 0; i < 80 && screenshot is null; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync(screenshot is not null
                ? $"[self-check] screenshot capture: {screenshot.Width}x{screenshot.Height}"
                : "[self-check] screenshot capture: no bitmap produced").ConfigureAwait(true);
            screenshot?.Dispose();

            window.CloseTabContaining(viewport);
            for (var i = 0; i < 60 && !renderer.Disposed; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] keyboard routing failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task RunModelRenderCheckAsync(MainWindow window)
    {
        const string modelPath = "Tests/Files/export_test.vmdl_c";

        if (!File.Exists(modelPath))
        {
            await Program.StdOut.WriteLineAsync($"[self-check] model sample missing: {modelPath}").ConfigureAwait(true);
            return;
        }

        try
        {
            await window.OpenFileAsync(modelPath).ConfigureAwait(true);

            var (viewport, renderer) = await WaitForRendererAsync<ModelGlRenderer>(window, 1, 30000).ConfigureAwait(true);

            if (viewport is null)
            {
                await Program.StdOut.WriteLineAsync("[self-check] model tab render timed out").ConfigureAwait(true);
                return;
            }

            for (var i = 0; i < 50 && renderer.RenderedFrames < 3; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync(
                $"[self-check] model tab: frames={renderer.RenderedFrames}, distinctColors={renderer.ReadbackDistinctColors}, "
                + $"nonBackgroundPixels={renderer.ReadbackNonBackgroundPixels}, viewport={renderer.ViewportWidth}x{renderer.ViewportHeight}").ConfigureAwait(true);

            await Program.StdOut.WriteLineAsync(renderer.ReadbackNonBackgroundPixels > 0 && renderer.ReadbackDistinctColors > 1
                ? "[self-check] model tab rendered real geometry"
                : "[self-check] model tab rendered only a flat frame").ConfigureAwait(true);

            var before = renderer.CameraLocation;
            RaisePointerEntered(viewport);
            viewport.Input.Keys |= GUI.Linux.Types.GLViewers.ViewerKey.W;
            await Task.Delay(600).ConfigureAwait(true);
            viewport.Input.Keys = GUI.Linux.Types.GLViewers.ViewerKey.None;
            await Task.Delay(100).ConfigureAwait(true);
            await Program.StdOut.WriteLineAsync($"[self-check] model tab camera input moved {System.Numerics.Vector3.Distance(before, renderer.CameraLocation):0.00} units").ConfigureAwait(true);

            window.CloseTabContaining(viewport);
            for (var i = 0; i < 60 && !renderer.Disposed; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync($"[self-check] model tab closed, renderer disposed={renderer.Disposed}").ConfigureAwait(true);
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] model tab failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Exercises the interactive model viewer through the real application path: real models opened
    /// through the shell, the real Avalonia viewport and animation controls, and the real renderer,
    /// camera and animation controller. Camera interactions mirror what the pointer handlers write
    /// into <see cref="GUI.Linux.Types.GLViewers.ViewerInputState"/>, and the animation controls are
    /// driven through their real Avalonia events and properties.
    /// </summary>
    private static async Task RunModelAnimationCheckAsync(MainWindow window)
    {
        await ExerciseModelViewerAsync(window, "Tests/Files/box_creature_ik_model.vmdl_c", "animated fixture", checkInteractions: true).ConfigureAwait(true);
        await ExerciseModelViewerAsync(window, "Tests/Files/wooden_crate_01.vmdl_c", "static fixture", checkInteractions: true).ConfigureAwait(true);

        if (LinuxGameContent.Context.Install is not { } install)
        {
            return;
        }

        try
        {
            var vpkPath = Path.Combine(install.ContentRoot, "pak01_dir.vpk");
            using var package = new Package();
            package.Read(vpkPath);

            var entries = package.Entries?.GetValueOrDefault("vmdl_c")?
                .Select(static entry => entry.GetFullPath())
                .Where(static path => path.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];

            var tempDir = Path.Combine(Path.GetTempPath(), "s2v-model-anim");
            Directory.CreateDirectory(tempDir);

            var tested = 0;
            var animated = 0;
            var withoutAnimations = 0;
            var index = 0;

            foreach (var entryPath in entries)
            {
                if ((animated >= 2 && withoutAnimations >= 1) || tested >= 10 || index >= 24)
                {
                    break;
                }

                index++;

                var entry = package.FindEntry(entryPath);
                if (entry is null)
                {
                    continue;
                }

                var modelPath = Path.Combine(tempDir, $"model_{index}.vmdl_c");

                try
                {
                    using (var stream = GameFileLoader.GetPackageEntryStream(package, entry))
                    using (var file = File.Create(modelPath))
                    {
                        await stream.CopyToAsync(file).ConfigureAwait(true);
                    }

                    var hasAnimations = await ExerciseModelViewerAsync(window, modelPath, $"game {entryPath}", checkInteractions: animated < 2).ConfigureAwait(true);

                    tested++;

                    if (hasAnimations)
                    {
                        animated++;
                    }
                    else
                    {
                        withoutAnimations++;
                    }
                }
                catch (Exception e)
                {
                    await Program.StdOut.WriteLineAsync($"[self-check] model animation {entryPath} failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
                }
                finally
                {
                    if (File.Exists(modelPath))
                    {
                        File.Delete(modelPath);
                    }
                }
            }

            await Program.StdOut.WriteLineAsync(
                $"[self-check] model animation game models: tested={tested}, animated={animated}, withoutAnimations={withoutAnimations}").ConfigureAwait(true);
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] model animation game models failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task<bool> ExerciseModelViewerAsync(MainWindow window, string path, string label, bool checkInteractions)
    {
        if (!File.Exists(path))
        {
            await Program.StdOut.WriteLineAsync($"[self-check] model animation sample missing: {path}").ConfigureAwait(true);
            return false;
        }

        try
        {
            await window.OpenFileAsync(path).ConfigureAwait(true);
            var (viewport, renderer) = await WaitForRendererAsync<ModelGlRenderer>(window, 1, 45000).ConfigureAwait(true);

            if (viewport is null)
            {
                await Program.StdOut.WriteLineAsync($"[self-check] {label}: model viewer timed out").ConfigureAwait(true);
                return false;
            }

            for (var i = 0; i < 60 && renderer.RenderedFrames < 3; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            var control = viewport.GetVisualAncestors().OfType<ModelViewerControl>().FirstOrDefault();

            if (control is null)
            {
                await Program.StdOut.WriteLineAsync($"[self-check] {label}: model viewer control not found").ConfigureAwait(true);
                window.CloseTabContaining(viewport);
                return false;
            }

            var session = renderer.AnimationSession;
            var snapshot = session.GetSnapshot();
            var hasAnimations = snapshot.HasAnimations;

            await Program.StdOut.WriteLineAsync(
                $"[self-check] {label}: animations={snapshot.Animations.Length}, active={snapshot.ActiveAnimation}, playing={snapshot.Playing}, looping={snapshot.Looping}").ConfigureAwait(true);

            if (!checkInteractions)
            {
                window.CloseTabContaining(viewport);
                await WaitForRendererDisposalAsync(renderer).ConfigureAwait(true);
                return hasAnimations;
            }

            await ExerciseModelCameraAsync(viewport, renderer, control, label).ConfigureAwait(true);

            if (hasAnimations)
            {
                await ExerciseModelAnimationControlsAsync(control, session, label).ConfigureAwait(true);
            }
            else
            {
                for (var i = 0; i < 20 && control.AnimationControlsVisible; i++)
                {
                    await Task.Delay(50).ConfigureAwait(true);
                }

                await Program.StdOut.WriteLineAsync(control.AnimationControlsVisible
                    ? $"[self-check] {label}: animation controls visible without animations"
                    : $"[self-check] {label}: bind pose shown, animation controls hidden").ConfigureAwait(true);
            }

            window.CloseTabContaining(viewport);
            await WaitForRendererDisposalAsync(renderer).ConfigureAwait(true);

            await Program.StdOut.WriteLineAsync($"[self-check] {label}: closed, renderer disposed={renderer.Disposed}").ConfigureAwait(true);

            return hasAnimations;
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] {label} failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
            return false;
        }
    }

    private static async Task ExerciseModelCameraAsync(AvaloniaGlViewport viewport, ModelGlRenderer renderer, ModelViewerControl control, string label)
    {
        if (renderer.SceneCore is not { } core)
        {
            return;
        }

        // Verify the viewport derives the pointer-over state from real Avalonia pointer events.
        RaisePointerEntered(viewport);
        await Task.Delay(80).ConfigureAwait(true);
        var pointerEntered = viewport.Input.MouseOverViewport;

        RaisePointerExited(viewport);
        await Task.Delay(80).ConfigureAwait(true);
        var pointerExited = !viewport.Input.MouseOverViewport;

        RaisePointerEntered(viewport);
        await Task.Delay(80).ConfigureAwait(true);

        var start = renderer.CameraLocation;
        var startDistance = core.Input.OrbitDistance;

        await SimulatePointerDragAsync(viewport, ViewerKey.MouseLeft, new System.Numerics.Vector2(12, 4), 8).ConfigureAwait(true);
        var orbitDistance = System.Numerics.Vector3.Distance(start, renderer.CameraLocation);

        var beforePan = renderer.CameraLocation;
        await SimulatePointerDragAsync(viewport, ViewerKey.MouseRight, new System.Numerics.Vector2(10, 0), 8).ConfigureAwait(true);
        var panDistance = System.Numerics.Vector3.Distance(beforePan, renderer.CameraLocation);

        var beforeZoom = core.Input.OrbitDistance;
        await SimulateMouseWheelAsync(viewport, 2f, 6).ConfigureAwait(true);
        var afterZoom = core.Input.OrbitDistance;

        var beforeReset = core.Input.OrbitDistance;
        control.ResetViewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(300).ConfigureAwait(true);
        var afterReset = core.Input.OrbitDistance;

        var orbitOk = orbitDistance > 0.05f;
        var panOk = panDistance > 0.01f;
        var zoomOk = afterZoom < beforeZoom;

        // Reset must frame the model bounds and point the orbit target at their center.
        var modelCore = renderer.SceneCore as ModelSceneCore;
        var orbitTarget = core.Input.OrbitTarget;
        var centerOk = false;

        if (modelCore is not null && orbitTarget is { } target)
        {
            var bounds = modelCore.ModelBounds;
            var tolerance = 1f + bounds.Size.Length() * 0.05f;
            centerOk = System.Numerics.Vector3.Distance(target, bounds.Center) <= tolerance;
        }

        var resetOk = centerOk && afterReset > 0.01f;
        var pointerOk = pointerEntered && pointerExited;

        await Program.StdOut.WriteLineAsync(
            $"[self-check] {label} camera: orbit={orbitDistance:0.00}, pan={panDistance:0.00}, "
            + $"zoom {beforeZoom:0.00}->{afterZoom:0.00}, reset {beforeReset:0.00}->{afterReset:0.00}, "
            + $"startDistance={startDistance:0.00}, pointerEnter/exit={pointerOk}").ConfigureAwait(true);

        await Program.StdOut.WriteLineAsync(orbitOk && panOk && zoomOk && resetOk && pointerOk
            ? $"[self-check] {label} camera: orbit, pan, zoom and reset view all responded"
            : $"[self-check] {label} camera: incomplete (orbit={orbitOk}, pan={panOk}, zoom={zoomOk}, reset={resetOk}, pointer={pointerOk})").ConfigureAwait(true);
    }

    private static async Task ExerciseModelAnimationControlsAsync(ModelViewerControl control, ModelAnimationSession session, string label)
    {
        var names = session.GetSnapshot().Animations;
        var target = names.Length > 1 ? names[1] : names[0];

        // Select through the real combo box so its SelectionChanged handler queues the command.
        control.AnimationSelector.SelectedItem = target;
        var selected = await WaitForAsync(() => string.Equals(session.GetSnapshot().ActiveAnimation, target, StringComparison.Ordinal), 60).ConfigureAwait(true);

        // Play through the real button.
        if (!session.GetSnapshot().Playing)
        {
            Click(control.PlayPauseButton);
        }

        var advanced = await WaitForAsync(() => session.GetSnapshot().Playing && session.GetSnapshot().Time > 0.05f, 60).ConfigureAwait(true);

        // Pause through the real button and confirm the clock stops.
        Click(control.PlayPauseButton);
        await WaitForAsync(() => !session.GetSnapshot().Playing, 60).ConfigureAwait(true);

        var pausedA = session.GetSnapshot().Time;
        await Task.Delay(350).ConfigureAwait(true);
        var pausedB = session.GetSnapshot().Time;
        var pauseOk = Math.Abs(pausedB - pausedA) < 0.001f;

        // Scrub through the real timeline drag path. It was paused, so it must stay paused.
        control.BeginTimelineDrag();
        control.Timeline.Value = 0.5;
        await Task.Delay(150).ConfigureAwait(true);
        control.EndTimelineDrag();
        await Task.Delay(250).ConfigureAwait(true);

        var scrubbed = session.GetSnapshot();
        var cycleFrames = Math.Max(1, scrubbed.FrameCount - 1);
        var expectedFrame = (int)MathF.Round(0.5f * cycleFrames);
        var scrubOk = Math.Abs(scrubbed.Frame - expectedFrame) <= 2;
        var stayedPaused = !scrubbed.Playing;

        // Speed through the real slider.
        control.SpeedBar.Value = 2.0;
        var speedOk = await WaitForAsync(() => Math.Abs(session.GetSnapshot().Speed - 2f) < 0.01f, 60).ConfigureAwait(true);
        control.SpeedBar.Value = 1.0;
        await WaitForAsync(() => Math.Abs(session.GetSnapshot().Speed - 1f) < 0.01f, 60).ConfigureAwait(true);

        // Loop through the real checkbox.
        control.LoopCheckBox.IsChecked = false;
        var loopOff = await WaitForAsync(() => !session.GetSnapshot().Looping, 60).ConfigureAwait(true);
        control.LoopCheckBox.IsChecked = true;
        var loopOn = await WaitForAsync(() => session.GetSnapshot().Looping, 60).ConfigureAwait(true);

        // Restart through the real button while paused.
        Click(control.RestartButton);
        var restartOk = await WaitForAsync(() => session.GetSnapshot().Time < 0.05f, 60).ConfigureAwait(true);

        await Program.StdOut.WriteLineAsync(
            $"[self-check] {label} animation: select={target}, advanced={advanced}, pauseStable={pauseOk}, "
            + $"scrub={scrubbed.Frame}/{cycleFrames} (expected {expectedFrame}), stayedPaused={stayedPaused}, "
            + $"speed={speedOk}, loop={loopOff && loopOn}, restart={restartOk}").ConfigureAwait(true);

        var ok = selected && advanced && pauseOk && scrubOk && stayedPaused && speedOk && loopOff && loopOn && restartOk;

        await Program.StdOut.WriteLineAsync(ok
            ? $"[self-check] {label} animation: select, play, pause, scrub, speed, loop and restart all verified"
            : $"[self-check] {label} animation: interaction check incomplete").ConfigureAwait(true);
    }

    private static async Task SimulatePointerDragAsync(AvaloniaGlViewport viewport, ViewerKey button, System.Numerics.Vector2 delta, int frames)
    {
        RaisePointerEntered(viewport);
        viewport.Input.Keys |= button;

        for (var i = 0; i < frames; i++)
        {
            viewport.Input.Delta = delta;
            viewport.RequestFrame();
            await Task.Delay(30).ConfigureAwait(true);
        }

        viewport.Input.Keys &= ~button;
        viewport.Input.Delta = System.Numerics.Vector2.Zero;
        viewport.RequestFrame();
        await Task.Delay(80).ConfigureAwait(true);
    }

    private static async Task SimulateMouseWheelAsync(AvaloniaGlViewport viewport, float delta, int frames)
    {
        RaisePointerEntered(viewport);

        for (var i = 0; i < frames; i++)
        {
            viewport.Input.Wheel = delta;
            viewport.RequestFrame();
            await Task.Delay(30).ConfigureAwait(true);
        }

        viewport.Input.Wheel = 0;
        viewport.RequestFrame();
        await Task.Delay(80).ConfigureAwait(true);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>
    /// Raises a real Avalonia pointer-entered event on a viewport, exercising the same handler the
    /// shell uses, so the self-check does not have to write the pointer-over state directly.
    /// </summary>
    private static void RaisePointerEntered(AvaloniaGlViewport viewport)
        => RaisePointerEvent(viewport, InputElement.PointerEnteredEvent);

    /// <summary>Raises a real Avalonia pointer-exited event on a viewport.</summary>
    private static void RaisePointerExited(AvaloniaGlViewport viewport)
        => RaisePointerEvent(viewport, InputElement.PointerExitedEvent);

    private static void RaisePointerEvent(AvaloniaGlViewport viewport, RoutedEvent pointerEvent)
    {
        using var pointer = new Pointer(1, PointerType.Mouse, true);
        var properties = new PointerPointProperties();
        viewport.RaiseEvent(new PointerEventArgs(
            pointerEvent,
            viewport,
            pointer,
            viewport,
            new Point(8, 8),
            0,
            properties,
            KeyModifiers.None));
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, int attempts)
    {
        for (var i = 0; i < attempts; i++)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50).ConfigureAwait(true);
        }

        return condition();
    }

    private static async Task WaitForRendererDisposalAsync(SceneCoreGlRenderer renderer)
    {
        for (var i = 0; i < 60 && !renderer.Disposed; i++)
        {
            await Task.Delay(50).ConfigureAwait(true);
        }
    }

    private static async Task RunMaterialRenderCheckAsync(MainWindow window)
    {
        const string materialPath = "Tests/Files/reflectivity_90b.vmat_c";

        if (!File.Exists(materialPath))
        {
            await Program.StdOut.WriteLineAsync($"[self-check] material sample missing: {materialPath}").ConfigureAwait(true);
            return;
        }

        try
        {
            await window.OpenFileAsync(materialPath).ConfigureAwait(true);

            var (viewport, renderer) = await WaitForRendererAsync<MaterialGlRenderer>(window, 1, 30000).ConfigureAwait(true);

            if (viewport is null)
            {
                await Program.StdOut.WriteLineAsync("[self-check] material tab render timed out").ConfigureAwait(true);
                return;
            }

            for (var i = 0; i < 50 && renderer.RenderedFrames < 3; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync(
                $"[self-check] material tab: frames={renderer.RenderedFrames}, distinctColors={renderer.ReadbackDistinctColors}, "
                + $"nonBackgroundPixels={renderer.ReadbackNonBackgroundPixels}, viewport={renderer.ViewportWidth}x{renderer.ViewportHeight}").ConfigureAwait(true);

            await Program.StdOut.WriteLineAsync(renderer.ReadbackNonBackgroundPixels > 0 && renderer.ReadbackDistinctColors > 1
                ? "[self-check] material tab rendered real geometry"
                : "[self-check] material tab rendered only a flat frame").ConfigureAwait(true);

            window.CloseTabContaining(viewport);
            for (var i = 0; i < 60 && !renderer.Disposed; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync($"[self-check] material tab closed, renderer disposed={renderer.Disposed}").ConfigureAwait(true);
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] material tab failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task RunAnimationRenderCheckAsync(MainWindow window)
    {
        const string skeletonPath = "Tests/Files/ak47.vnmskel_c";

        if (!File.Exists(skeletonPath))
        {
            await Program.StdOut.WriteLineAsync($"[self-check] skeleton sample missing: {skeletonPath}").ConfigureAwait(true);
            return;
        }

        try
        {
            await window.OpenFileAsync(skeletonPath).ConfigureAwait(true);

            var (viewport, renderer) = await WaitForRendererAsync<AnimationGlRenderer>(window, 1, 30000).ConfigureAwait(true);

            if (viewport is null)
            {
                await Program.StdOut.WriteLineAsync("[self-check] skeleton tab render timed out").ConfigureAwait(true);
                return;
            }

            for (var i = 0; i < 50 && renderer.RenderedFrames < 3; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync(
                $"[self-check] skeleton tab: frames={renderer.RenderedFrames}, distinctColors={renderer.ReadbackDistinctColors}, "
                + $"nonBackgroundPixels={renderer.ReadbackNonBackgroundPixels}, viewport={renderer.ViewportWidth}x{renderer.ViewportHeight}").ConfigureAwait(true);

            await Program.StdOut.WriteLineAsync(renderer.ReadbackNonBackgroundPixels > 0 && renderer.ReadbackDistinctColors > 1
                ? "[self-check] skeleton tab rendered real geometry"
                : "[self-check] skeleton tab rendered only a flat frame").ConfigureAwait(true);

            window.CloseTabContaining(viewport);
            for (var i = 0; i < 60 && !renderer.Disposed; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync($"[self-check] skeleton tab closed, renderer disposed={renderer.Disposed}").ConfigureAwait(true);
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] skeleton tab failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task RunPhysicsRenderCheckAsync(MainWindow window)
    {
        const string physicsPath = "Tests/Files/juggernaut.vphys_c";

        if (!File.Exists(physicsPath))
        {
            await Program.StdOut.WriteLineAsync($"[self-check] physics sample missing: {physicsPath}").ConfigureAwait(true);
            return;
        }

        try
        {
            await window.OpenFileAsync(physicsPath).ConfigureAwait(true);

            var (viewport, renderer) = await WaitForRendererAsync<PhysGlRenderer>(window, 1, 30000).ConfigureAwait(true);

            if (viewport is null)
            {
                await Program.StdOut.WriteLineAsync("[self-check] physics tab render timed out").ConfigureAwait(true);
                return;
            }

            for (var i = 0; i < 50 && renderer.RenderedFrames < 3; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync(
                $"[self-check] physics tab: frames={renderer.RenderedFrames}, distinctColors={renderer.ReadbackDistinctColors}, "
                + $"nonBackgroundPixels={renderer.ReadbackNonBackgroundPixels}, viewport={renderer.ViewportWidth}x{renderer.ViewportHeight}").ConfigureAwait(true);

            await Program.StdOut.WriteLineAsync(renderer.ReadbackNonBackgroundPixels > 0 && renderer.ReadbackDistinctColors > 1
                ? "[self-check] physics tab rendered real geometry"
                : "[self-check] physics tab rendered only a flat frame").ConfigureAwait(true);

            window.CloseTabContaining(viewport);
            for (var i = 0; i < 60 && !renderer.Disposed; i++)
            {
                await Task.Delay(50).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync($"[self-check] physics tab closed, renderer disposed={renderer.Disposed}").ConfigureAwait(true);
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] physics tab failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task RunGraphRenderCheckAsync(MainWindow window)
    {
        if (LinuxGameContent.Context.Install is not { } install)
        {
            return;
        }

        try
        {
            var vpkPath = Path.Combine(install.ContentRoot, "pak01_dir.vpk");
            using var package = new Package();
            package.Read(vpkPath);

            // Prefer a substantial graph so the readback proves real node/wire drawing, but keep the
            // build bounded; fall back to any entry when none is in range.
            var graphEntries = package.Entries?.GetValueOrDefault("vnmgraph_c");

            var graphEntry = graphEntries?
                .Where(static entry => entry.Length is > 20000 and < 2_000_000)
                .OrderByDescending(static entry => entry.Length)
                .FirstOrDefault()?.GetFullPath()
                ?? graphEntries?.FirstOrDefault()?.GetFullPath();

            if (graphEntry is null)
            {
                await Program.StdOut.WriteLineAsync("[self-check] graph: no vnmgraph_c in pak01").ConfigureAwait(true);
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "s2v-phase6");
            Directory.CreateDirectory(tempDir);
            var graphPath = Path.Combine(tempDir, "graph.vnmgraph_c");

            var entry = package.FindEntry(graphEntry) ?? throw new InvalidDataException($"VPK entry not found: {graphEntry}");

            using (var stream = GameFileLoader.GetPackageEntryStream(package, entry))
            using (var file = File.Create(graphPath))
            {
                await stream.CopyToAsync(file).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync($"[self-check] graph: {graphEntry}").ConfigureAwait(true);

            await window.OpenFileAsync(graphPath).ConfigureAwait(true);
            var (viewport, renderer) = await WaitForRendererAsync<GraphGlRenderer>(window, 1, 120000).ConfigureAwait(true);

            if (viewport is not null)
            {
                for (var i = 0; i < 100 && renderer.RenderedFrames < 3; i++)
                {
                    await Task.Delay(50).ConfigureAwait(true);
                }

                await Program.StdOut.WriteLineAsync(
                    $"[self-check] graph view: nodes={renderer.NodeCount}, wires={renderer.WireCount}, frames={renderer.RenderedFrames}, "
                    + $"distinctColors={renderer.ReadbackDistinctColors}, nonBackgroundPixels={renderer.ReadbackNonBackgroundPixels}, "
                    + $"viewport={renderer.ViewportWidth}x{renderer.ViewportHeight}").ConfigureAwait(true);

                await Program.StdOut.WriteLineAsync(renderer.ReadbackNonBackgroundPixels > 0 && renderer.ReadbackDistinctColors > 1
                    ? "[self-check] graph tab rendered real content"
                    : "[self-check] graph tab rendered only a flat frame").ConfigureAwait(true);

                // Wheel zoom, then middle-drag pan, driven through the same input state the shell fills.
                var scaleBefore = renderer.Scale;
                viewport.Input.Wheel = 1f;
                await Task.Delay(400).ConfigureAwait(true);
                var scaleAfterZoom = renderer.Scale;

                viewport.Input.Middle = true;
                viewport.Input.Delta = new System.Numerics.Vector2(60f, 40f);
                await Task.Delay(400).ConfigureAwait(true);
                viewport.Input.Middle = false;
                await Task.Delay(200).ConfigureAwait(true);

                await Program.StdOut.WriteLineAsync(
                    $"[self-check] graph input: zoom {scaleBefore:0.000} -> {scaleAfterZoom:0.000}, pan applied").ConfigureAwait(true);

                // Resize the shell window; the viewport must follow.
                var widthBefore = renderer.ViewportWidth;
                window.Width += 200;
                await Task.Delay(600).ConfigureAwait(true);
                await Program.StdOut.WriteLineAsync($"[self-check] graph resize: viewport {widthBefore} -> {renderer.ViewportWidth}").ConfigureAwait(true);

                // Close the tab and confirm the renderer was disposed.
                window.CloseTabContaining(viewport);
                for (var i = 0; i < 60 && !renderer.Disposed; i++)
                {
                    await Task.Delay(50).ConfigureAwait(true);
                }

                await Program.StdOut.WriteLineAsync($"[self-check] graph tab closed, renderer disposed={renderer.Disposed}").ConfigureAwait(true);
            }
            else
            {
                await Program.StdOut.WriteLineAsync("[self-check] graph view timed out").ConfigureAwait(true);
            }

            File.Delete(graphPath);
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] graph view failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task RunSkyboxRenderCheckAsync(MainWindow window)
    {
        if (LinuxGameContent.Context.Install is not { } install)
        {
            return;
        }

        try
        {
            var vpkPath = Path.Combine(install.ContentRoot, "pak01_dir.vpk");
            using var package = new Package();
            package.Read(vpkPath);

            // Real Deadlock sky.vfx materials, preferred first; all ship in pak01.
            string[] preferred =
            [
                "materials/skybox/sky_dl_hideout_dusk01.vmat_c",
                "materials/skybox/sky_dl_hideout_storm_01.vmat_c",
                "materials/skybox/sky_dl_dusk02.vmat_c",
                "materials/dev/default_sky.vmat_c",
            ];

            string? skyEntry = null;

            foreach (var candidate in preferred)
            {
                if (package.FindEntry(candidate) is not null)
                {
                    skyEntry = candidate;
                    break;
                }
            }

            if (skyEntry is null)
            {
                await Program.StdOut.WriteLineAsync("[self-check] skybox: no sky.vfx material in pak01").ConfigureAwait(true);
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "s2v-phase6");
            Directory.CreateDirectory(tempDir);
            var skyPath = Path.Combine(tempDir, "sky.vmat_c");

            var entry = package.FindEntry(skyEntry) ?? throw new InvalidDataException($"VPK entry not found: {skyEntry}");

            using (var stream = GameFileLoader.GetPackageEntryStream(package, entry))
            using (var file = File.Create(skyPath))
            {
                await stream.CopyToAsync(file).ConfigureAwait(true);
            }

            await Program.StdOut.WriteLineAsync($"[self-check] skybox: {skyEntry}").ConfigureAwait(true);

            await window.OpenFileAsync(skyPath).ConfigureAwait(true);
            var (viewport, renderer) = await WaitForRendererAsync<SkyboxGlRenderer>(window, 1, 120000).ConfigureAwait(true);

            if (viewport is not null)
            {
                for (var i = 0; i < 100 && renderer.RenderedFrames < 3; i++)
                {
                    await Task.Delay(50).ConfigureAwait(true);
                }

                await Program.StdOut.WriteLineAsync(
                    $"[self-check] skybox view: frames={renderer.RenderedFrames}, distinctColors={renderer.ReadbackDistinctColors}, "
                    + $"nonBackgroundPixels={renderer.ReadbackNonBackgroundPixels}, viewport={renderer.ViewportWidth}x{renderer.ViewportHeight}").ConfigureAwait(true);

                await Program.StdOut.WriteLineAsync(renderer.ReadbackNonBackgroundPixels > 0 && renderer.ReadbackDistinctColors > 1
                    ? "[self-check] skybox tab rendered real content"
                    : "[self-check] skybox tab rendered only a flat frame").ConfigureAwait(true);

                // Resize the shell window; the viewport must follow.
                var widthBefore = renderer.ViewportWidth;
                window.Width += 200;
                await Task.Delay(600).ConfigureAwait(true);
                await Program.StdOut.WriteLineAsync($"[self-check] skybox resize: viewport {widthBefore} -> {renderer.ViewportWidth}").ConfigureAwait(true);

                // Close the tab and confirm the renderer was disposed.
                window.CloseTabContaining(viewport);
                for (var i = 0; i < 60 && !renderer.Disposed; i++)
                {
                    await Task.Delay(50).ConfigureAwait(true);
                }

                await Program.StdOut.WriteLineAsync($"[self-check] skybox tab closed, renderer disposed={renderer.Disposed}").ConfigureAwait(true);
            }
            else
            {
                await Program.StdOut.WriteLineAsync("[self-check] skybox view timed out").ConfigureAwait(true);
            }

            File.Delete(skyPath);
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] skybox view failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task RunBrowserCheckAsync(MainWindow window)
    {
        try
        {
            var installs = GameContentLocator.DiscoverInstalledGames();
            var install = installs.Find(static game => string.Equals(game.Name, "Deadlock", StringComparison.OrdinalIgnoreCase))
                ?? (installs.Count > 0 ? installs[0] : null);

            if (install is null)
            {
                await Program.StdOut.WriteLineAsync("[self-check] browser: no installed game").ConfigureAwait(true);
                return;
            }

            var sources = GameBrowser.BuildGameSources();
            var game = sources.Find(candidate => candidate.AppId == install.AppId);
            await Program.StdOut.WriteLineAsync(
                $"[self-check] browser sources: games={sources.Count}, {install.Name} vpks={game?.Children.Count ?? 0}").ConfigureAwait(true);

            window.OpenBrowser();
            await Task.Delay(200).ConfigureAwait(true);

            var pakPath = Path.Combine(install.ContentRoot, "pak01_dir.vpk");
            var pakView = window.OpenPackage(pakPath);

            if (pakView is null)
            {
                await Program.StdOut.WriteLineAsync("[self-check] browser: pak01 could not be opened").ConfigureAwait(true);
                return;
            }

            var search = pakView.Search("world.vwrld", PackageSearchMode.FileNamePartialMatch);
            await Program.StdOut.WriteLineAsync(
                $"[self-check] browser pak01: entries={pakView.EntryCount}, folders={pakView.FolderCount}, "
                + $"search 'world.vwrld'={search.Count}").ConfigureAwait(true);

            // 1. A normal (non-GL) resource: a script vdata.
            var dataEntry = pakView.Package.Entries?.GetValueOrDefault("vdata_c")
                ?.FirstOrDefault(static entry => entry.GetFullPath().Contains("heroes", StringComparison.OrdinalIgnoreCase))
                ?? pakView.Package.Entries?.GetValueOrDefault("vdata_c")?.FirstOrDefault();

            if (dataEntry is not null)
            {
                var before = window.TabCount;
                await window.OpenPackageEntryAsync(pakView.Package, pakPath, dataEntry).ConfigureAwait(true);
                await WaitForTabAsync(window, before + 1, 60000).ConfigureAwait(true);
                await Program.StdOut.WriteLineAsync(
                    $"[self-check] browser open data: {dataEntry.GetFullPath()} tabs {before} -> {window.TabCount}").ConfigureAwait(true);
                window.CloseTabByTitle(Path.GetFileName(dataEntry.GetFullPath()));
            }

            // 2. A GL resource: a texture.
            var textureEntry = pakView.Package.Entries?.GetValueOrDefault("vtex_c")
                ?.Where(static entry => entry.Length is > 50000 and < 8_000_000)
                .OrderByDescending(static entry => entry.Length)
                .FirstOrDefault();

            if (textureEntry is not null)
            {
                await window.OpenPackageEntryAsync(pakView.Package, pakPath, textureEntry).ConfigureAwait(true);
                var (textureViewport, textureRenderer) = await WaitForRendererAsync<TextureGlRenderer>(window, 1, 60000).ConfigureAwait(true);

                if (textureViewport is not null)
                {
                    await Task.Delay(300).ConfigureAwait(true);
                    await Program.StdOut.WriteLineAsync(
                        $"[self-check] browser open GL: {textureEntry.GetFullPath()} -> distinctColors={textureRenderer.ReadbackDistinctColors}, "
                        + $"nonBackgroundPixels={textureRenderer.ReadbackNonBackgroundPixels}").ConfigureAwait(true);

                    window.CloseTabContaining(textureViewport);
                    for (var i = 0; i < 60 && !textureRenderer.Disposed; i++)
                    {
                        await Task.Delay(50).ConfigureAwait(true);
                    }

                    await Program.StdOut.WriteLineAsync($"[self-check] browser GL tab closed, renderer disposed={textureRenderer.Disposed}").ConfigureAwait(true);
                }
                else
                {
                    await Program.StdOut.WriteLineAsync("[self-check] browser GL tab timed out").ConfigureAwait(true);
                }
            }

            window.CloseTabByTitle(Path.GetFileName(pakPath));

            // 3. A map/world resource, opened from a map VPK through the browser.
            var mapsDir = Path.Combine(install.ContentRoot, "maps");
            string? mapVpkPath = null;
            string? worldEntryPath = null;

            if (Directory.Exists(mapsDir))
            {
                foreach (var candidate in Directory.EnumerateFiles(mapsDir, "*.vpk").OrderBy(static file => new FileInfo(file).Length))
                {
                    using var probe = new Package();
                    probe.Read(candidate);

                    if (probe.Entries?.GetValueOrDefault("vwrld_c") is { Count: > 0 } worlds)
                    {
                        mapVpkPath = candidate;
                        worldEntryPath = worlds[0].GetFullPath();
                        break;
                    }
                }
            }

            if (mapVpkPath is not null && worldEntryPath is not null)
            {
                var mapView = window.OpenPackage(mapVpkPath);

                if (mapView?.Package.FindEntry(worldEntryPath) is { } worldEntry)
                {
                    await window.OpenPackageEntryAsync(mapView.Package, mapVpkPath, worldEntry).ConfigureAwait(true);
                    var (worldViewport, worldRenderer) = await WaitForRendererAsync<WorldGlRenderer>(window, 1, 300000).ConfigureAwait(true);

                    if (worldViewport is not null)
                    {
                        for (var i = 0; i < 100 && worldRenderer.RenderedFrames < 3; i++)
                        {
                            await Task.Delay(50).ConfigureAwait(true);
                        }

                        await Program.StdOut.WriteLineAsync(
                            $"[self-check] browser open world: {mapVpkPath} -> {worldEntryPath}, frames={worldRenderer.RenderedFrames}, "
                            + $"distinctColors={worldRenderer.ReadbackDistinctColors}, nonBackgroundPixels={worldRenderer.ReadbackNonBackgroundPixels}").ConfigureAwait(true);

                        window.CloseTabContaining(worldViewport);
                        for (var i = 0; i < 60 && !worldRenderer.Disposed; i++)
                        {
                            await Task.Delay(50).ConfigureAwait(true);
                        }

                        await Program.StdOut.WriteLineAsync($"[self-check] browser world tab closed, renderer disposed={worldRenderer.Disposed}").ConfigureAwait(true);
                    }
                    else
                    {
                        await Program.StdOut.WriteLineAsync("[self-check] browser world tab timed out").ConfigureAwait(true);
                    }
                }

                window.CloseTabByTitle(Path.GetFileName(mapVpkPath));
            }

            await Program.StdOut.WriteLineAsync("[self-check] browser navigation and resource opening verified").ConfigureAwait(true);
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] browser failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task WaitForTabAsync(MainWindow window, int count, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 < deadline && window.TabCount < count)
        {
            await Task.Delay(50).ConfigureAwait(true);
        }
    }

    private static async Task RunAudioCheckAsync(MainWindow window)
    {
        if (LinuxGameContent.Context.Install is not { } install)
        {
            return;
        }

        try
        {
            var pakPath = Path.Combine(install.ContentRoot, "pak01_dir.vpk");
            var pakView = window.OpenPackage(pakPath);

            if (pakView is null)
            {
                await Program.StdOut.WriteLineAsync("[self-check] audio: pak01 could not be opened").ConfigureAwait(true);
                return;
            }

            var vsnd = pakView.Package.Entries?.GetValueOrDefault("vsnd_c") ?? [];

            var mp3Entry = vsnd
                .Where(static entry => entry.GetFullPath().Contains("sounds/", StringComparison.OrdinalIgnoreCase) && entry.Length is > 20000 and < 60000)
                .OrderBy(static entry => entry.Length)
                .FirstOrDefault()
                ?? vsnd.FirstOrDefault(static entry => entry.GetFullPath().Contains("sounds/", StringComparison.OrdinalIgnoreCase))
                ?? vsnd.FirstOrDefault();

            var wavEntry = vsnd.FirstOrDefault(static entry => entry.GetFullPath().Contains("kelvin_tutorial_lane_info", StringComparison.OrdinalIgnoreCase));
            var loopEntry = vsnd.FirstOrDefault(static entry => entry.GetFullPath().Contains("trapper_a3_mod_lp", StringComparison.OrdinalIgnoreCase));

            var seen = new HashSet<AudioPlayerControl>();

            if (mp3Entry is not null)
            {
                await window.OpenPackageEntryAsync(pakView.Package, pakPath, mp3Entry).ConfigureAwait(true);
                var control = await WaitForNewAudioPlayerAsync(window, seen, 60000).ConfigureAwait(true);

                if (control is not null)
                {
                    await Program.StdOut.WriteLineAsync(
                        $"[self-check] audio mp3: {mp3Entry.GetFullPath()}, frames={control.DecodedFrameCount}, "
                        + $"metadataRows={control.MetadataRowCount}, waveform={control.HasWaveform}, playbackAvailable={control.PlaybackAvailable}").ConfigureAwait(true);
                    await ValidatePlaybackAsync(control, "mp3").ConfigureAwait(true);
                    window.CloseTabByTitle(Path.GetFileName(mp3Entry.GetFullPath()));
                    await WaitForPlayerDisposedAsync(control, 30).ConfigureAwait(true);
                    await Program.StdOut.WriteLineAsync($"[self-check] audio mp3 tab closed, player disposed={control.Player?.Disposed}").ConfigureAwait(true);
                }
                else
                {
                    await Program.StdOut.WriteLineAsync("[self-check] audio mp3 view timed out").ConfigureAwait(true);
                }
            }

            if (wavEntry is not null)
            {
                await window.OpenPackageEntryAsync(pakView.Package, pakPath, wavEntry).ConfigureAwait(true);
                var control = await WaitForNewAudioPlayerAsync(window, seen, 60000).ConfigureAwait(true);

                if (control is not null)
                {
                    await Program.StdOut.WriteLineAsync(
                        $"[self-check] audio wav: {wavEntry.GetFullPath()}, frames={control.DecodedFrameCount}, "
                        + $"metadataRows={control.MetadataRowCount}, playbackAvailable={control.PlaybackAvailable}").ConfigureAwait(true);
                    await ValidatePlaybackAsync(control, "wav").ConfigureAwait(true);
                    window.CloseTabByTitle(Path.GetFileName(wavEntry.GetFullPath()));
                    await WaitForPlayerDisposedAsync(control, 30).ConfigureAwait(true);
                }
            }

            if (loopEntry is not null)
            {
                await window.OpenPackageEntryAsync(pakView.Package, pakPath, loopEntry).ConfigureAwait(true);
                var control = await WaitForNewAudioPlayerAsync(window, seen, 60000).ConfigureAwait(true);

                if (control is not null)
                {
                    await Program.StdOut.WriteLineAsync(
                        $"[self-check] audio loop: {loopEntry.GetFullPath()}, frames={control.DecodedFrameCount}, hasLoop={control.Player?.HasLoop}").ConfigureAwait(true);
                    window.CloseTabByTitle(Path.GetFileName(loopEntry.GetFullPath()));
                    await WaitForPlayerDisposedAsync(control, 30).ConfigureAwait(true);
                }
            }

            window.CloseTabByTitle(Path.GetFileName(pakPath));
            await Program.StdOut.WriteLineAsync("[self-check] audio decode, metadata, playback, seek and disposal verified").ConfigureAwait(true);
        }
        catch (Exception e)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] audio failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
        }
    }

    private static async Task ValidatePlaybackAsync(AudioPlayerControl control, string label)
    {
        var player = control.Player;

        if (player is null || !player.Available)
        {
            await Program.StdOut.WriteLineAsync($"[self-check] audio {label} playback unavailable: {player?.ErrorMessage ?? "no player"}").ConfigureAwait(true);
            return;
        }

        player.Volume = 0.05f;
        player.Seek(0);
        player.Play();
        await Task.Delay(900).ConfigureAwait(true);
        var positionAfterStart = player.PositionFrame;

        var half = control.DecodedFrameCount / 2;
        player.Seek(half);
        await Task.Delay(300).ConfigureAwait(true);
        var positionAfterSeek = player.PositionFrame;

        player.Pause();
        await Task.Delay(150).ConfigureAwait(true);
        var pausedPosition = player.PositionFrame;
        await Task.Delay(300).ConfigureAwait(true);
        var pausedStable = player.PositionFrame;

        await Program.StdOut.WriteLineAsync(
            $"[self-check] audio {label} playback: started={positionAfterStart > 0} (pos={positionAfterStart}), "
            + $"seeked={positionAfterSeek >= half} ({positionAfterSeek}/{half}), pausedStable={pausedStable == pausedPosition}").ConfigureAwait(true);
    }

    private static async Task<AudioPlayerControl?> WaitForNewAudioPlayerAsync(MainWindow window, HashSet<AudioPlayerControl> seen, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 < deadline)
        {
            foreach (var control in window.GetAudioPlayers())
            {
                if (seen.Add(control))
                {
                    return control;
                }
            }

            await Task.Delay(50).ConfigureAwait(true);
        }

        return null;
    }

    private static async Task WaitForPlayerDisposedAsync(AudioPlayerControl control, int timeoutSeconds)
    {
        var deadline = Environment.TickCount64 + (timeoutSeconds * 1000);

        while (Environment.TickCount64 < deadline && control.Player?.Disposed != true)
        {
            await Task.Delay(50).ConfigureAwait(true);
        }
    }

    private static async Task RunViewerFactoryChecksAsync()
    {
        (string Path, string Expected)[] samples =
        [
            ("Tests/Files/Textures/279115896_png.png", "image"),
            ("GUI.Linux/Assets/Icons/About.svg", "svg"),
            ("Tests/Files/lobby_mapveto.nav", "navmesh"),
            ("Tests/Files/KeyValues/KeyValues3_LF.kv3", "kv3 text"),
            ("Tests/Files/Shaders/vcs62_apply_fog_pc_40_ps.vcs", "compiled shader"),
            ("Tests/Files/a1_eli_corridor_kv3_v1_uncompressed.vpost_c", "resource postprocessing"),
            ("Tests/Files/default_ents.vents_c", "resource entity lump"),
            ("Tests/Files/chen_weapon.vmesh_c", "mesh"),
            ("Tests/Files/explosion_barrel_kv0_lz4.vpcf_c", "particle"),
            ("Tests/Files/world_visibility.vvis_c", "visibility"),
            ("Tests/Files/export_test.vmdl_c", "model"),
            ("Tests/Files/reflectivity_90b.vmat_c", "material"),
            ("Tests/Files/ak47.vnmskel_c", "skeleton"),
            ("Tests/Files/juggernaut.vphys_c", "physics"),
            ("Tests/Files/dota.vmap_c", "map"),
            ("Tests/Files/node000_kv3_v2_zstd.vwnod_c", "world node"),
            ("Tests/Files/pip-left.vsvg_c", "panorama vector graphic"),
            ("Tests/Files/test.vsnap_c", "particle snapshot"),
            ("Tests/Files/a1_eli_corridor_kv3_v1_uncompressed.vpost_c", "resource postprocessing"),
            ("Tests/Files/vrf_all_nodes.vanmgrph_c", "ag1 animation graph"),
            ("Tests/Files/de_inferno_script.vpulse_c", "pulse graph"),
            ("Tests/Files/default_ents.vents_c", "resource entity lump"),
        ];

        foreach (var (path, expected) in samples)
        {
            if (!File.Exists(path))
            {
                await Program.StdOut.WriteLineAsync($"[self-check] sample missing: {path}").ConfigureAwait(true);
                continue;
            }

            try
            {
                var viewer = await LinuxViewerFactory.CreateAndLoadAsync(path).ConfigureAwait(true);
                var selected = viewer is BoundaryViewer boundary ? boundary.Inner.GetType().Name : viewer.GetType().Name;
                var content = viewer.GetContent();
                var presented = content is not null && AvaloniaViewerContentPresenter.Present(content) is not null;

                await Program.StdOut.WriteLineAsync(
                    $"[self-check] {Path.GetFileName(path)} ({expected}): {selected}, content={content?.GetType().Name}, presented={presented}").ConfigureAwait(true);

                viewer.Dispose();
            }
            catch (Exception e)
            {
                await Program.StdOut.WriteLineAsync($"[self-check] {Path.GetFileName(path)} ({expected}) failed: {e.GetType().Name}: {e.Message}").ConfigureAwait(true);
            }
        }
    }

    private static bool RunPresenterChecks()
    {
        ViewerContent[] contents =
        [
            new ViewerContent.Text("hello", HighlightLanguage.None),
            new ViewerContent.LazyText(() => "lazy"),
            new ViewerContent.HexDump([0, 1, 2, 3, 4, 5, 0x41, 0x42, 0xFF, 0x10]),
            new ViewerContent.Grid(new List<SelfCheckRow> { new("alpha", 1), new("beta", 2) }),
            new ViewerContent.Tabs(
            [
                new ViewerTab("A", new ViewerContent.Text("a")),
                new ViewerTab("B", new ViewerContent.HexDump([0x42])),
            ]),
        ];

        foreach (var content in contents)
        {
            if (AvaloniaViewerContentPresenter.Present(content) is null)
            {
                return false;
            }
        }

        return true;
    }

    private sealed record SelfCheckRow(string Name, int Value);
}
