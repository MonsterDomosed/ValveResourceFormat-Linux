using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GUI.Linux.GL;
using GUI.Linux.Types.Audio;
using GUI.Linux.Types.Browser;
using GUI.Linux.Types.Graphs.Core;
using GUI.Linux.Types.GLViewers;
using GUI.Linux.Types.Viewers;
using GUI.Linux.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Graphs;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    /// <summary>
    /// Covers the portable Linux application logic: package browsing, sound
    /// decoding/waveform, game discovery and the graph presenter.
    /// </summary>
    public class LinuxGuiTest
    {
        private static string FilePath(string name)
            => Path.Combine(TestContext.TestDirectory!, "Files", name);

        [Test]
        public async Task ResourceViewerHidesRawMeshBufferTabs()
        {
            var path = FilePath("lod_test.vmdl_c");

            using (var resource = new Resource())
            {
                resource.Read(path);

                // The fixture must actually contain the raw buffers, or this test proves nothing.
                await Assert.That(resource.Blocks.Any(static block => block.Type is BlockType.MVTX or BlockType.MIDX or BlockType.MADJ))
                    .IsTrue().Because("the fixture should have raw mesh buffers");
            }

            using var viewer = new ResourceDataViewer(new LocalViewerContext(path));
            await viewer.LoadAsync(stream: null);

            var content = viewer.GetContent();
            var tabs = ((ViewerContent.Tabs)content).Items.Select(static tab => tab.Name).ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(tabs).DoesNotContain("MVTX");
                await Assert.That(tabs).DoesNotContain("MIDX");
                await Assert.That(tabs).DoesNotContain("MADJ");
                await Assert.That(tabs).Contains("Resource");
            }
        }

        [Test]
        public async Task ResourceBlockTextIsDeferred()
        {
            using var resource = new Resource();
            resource.Read(FilePath("lod_test.vmdl_c"));
            var block = resource.Blocks.OfType<BinaryKV3>().First();

            var content = ResourceBlockContent.GetBlockContent(resource, block);

            await Assert.That(content).IsTypeOf<ViewerContent.LazyContent>();
        }

        [Test]
        public async Task OversizedKeyValuesBlockSkipsTextConversion()
        {
            using var resource = new Resource();
            resource.Read(FilePath("lod_test.vmdl_c"));
            var block = resource.Blocks.OfType<BinaryKV3>().First();
            block.Size = (uint)(64L * 1024 * 1024 + 1);

            var lazy = (ViewerContent.LazyContent)ResourceBlockContent.GetBlockContent(resource, block);
            var content = lazy.GetContent();

            await Assert.That(content).IsTypeOf<ViewerContent.Text>();
            await Assert.That(((ViewerContent.Text)content).Content).Contains("too large to display as text");
        }

        private sealed class LocalViewerContext(string fileName) : IViewerContext
        {
            public string FileName { get; } = fileName;
        }

        [Test]
        [Arguments("beep.vsnd_c")]
        [Arguments("impulse.vsnd_c")]
        [Arguments("abad_ally_06.vsnd_c")]
        [Arguments("death_taser_m_05.vsnd_c")]
        [Arguments("primal_anger_03.vsnd_c")]
        public async Task DecodesSoundFixtures(string file)
        {
            using var resource = new Resource();
            resource.Read(FilePath(file));
            var sound = (Sound)resource.DataBlock!;

            var decoded = SoundDecoder.Decode(sound);

            using (Assert.Multiple())
            {
                await Assert.That(decoded.FrameCount).IsGreaterThan(0);
                await Assert.That(decoded.SampleRate).IsGreaterThan(0);
                await Assert.That(decoded.Channels).IsEqualTo((int)sound.Channels);
                await Assert.That(decoded.Samples.Length).IsEqualTo(decoded.FrameCount * decoded.Channels);
            }
        }

        [Test]
        public async Task DecodesBothWavAndMp3Fixtures()
        {
            var types = new HashSet<Sound.AudioFileType>();
            var fixtures = new[] { "beep.vsnd_c", "impulse.vsnd_c", "abad_ally_06.vsnd_c", "death_taser_m_05.vsnd_c", "primal_anger_03.vsnd_c" };

            foreach (var file in fixtures)
            {
                using var resource = new Resource();
                resource.Read(FilePath(file));
                var sound = (Sound)resource.DataBlock!;

                types.Add(sound.SoundType);
                await Assert.That(SoundDecoder.Decode(sound).FrameCount).IsGreaterThan(0);
            }

            await Assert.That(types).Contains(Sound.AudioFileType.WAV);
            await Assert.That(types).Contains(Sound.AudioFileType.MP3);
        }

        [Test]
        [Arguments("beep.vsnd_c")]
        [Arguments("abad_ally_06.vsnd_c")]
        public async Task ComputesWaveformPeaks(string file)
        {
            using var resource = new Resource();
            resource.Read(FilePath(file));
            var decoded = SoundDecoder.Decode((Sound)resource.DataBlock!);

            var peaks = SoundWaveform.Compute(decoded, 200);

            using (Assert.Multiple())
            {
                await Assert.That(peaks.Length).IsEqualTo(200);
                await Assert.That(peaks.Any(peak => peak.Max > peak.Min)).IsTrue();
                await Assert.That(peaks.All(peak => peak.Min is >= -1f and <= 1f && peak.Max is >= -1f and <= 1f)).IsTrue();
            }
        }

        [Test]
        public async Task BuildsAndSearchesPackageTree()
        {
            using var package = new Package();
            package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
            package.Read(FilePath("dota_riverflow_fx.vpk"));

            var root = PackageTree.Build(package);

            await Assert.That(root.TotalFileCount).IsGreaterThan(0);
            await Assert.That(root.TotalSize).IsGreaterThan(0);

            var firstEntry = package.Entries!.Values.SelectMany(static entries => entries).First();
            var query = Path.GetFileNameWithoutExtension(firstEntry!.GetFileName());
            var matches = PackageTree.Search(root, query, PackageSearchMode.FileNamePartialMatch);

            using (Assert.Multiple())
            {
                await Assert.That(matches.Count).IsGreaterThan(0);
                await Assert.That(matches.Any(entry => entry.GetFileName() == firstEntry.GetFileName())).IsTrue();
            }
        }

        [Test]
        public async Task BuildsRecentFileBrowserNodes()
        {
            var node = GameBrowser.BuildRecentFiles([FilePath("beep.vsnd_c")]);

            using (Assert.Multiple())
            {
                await Assert.That(node.Children.Count).IsEqualTo(1);
                await Assert.That(node.Children[0].Path).IsEqualTo(FilePath("beep.vsnd_c"));
            }
        }

        [Test]
        public async Task GameContentDiscoveryDoesNotThrow()
        {
            var installs = GameContentLocator.DiscoverInstalledGames();

            using (Assert.Multiple())
            {
                await Assert.That(installs).IsNotNull();
                await Assert.That(GameContentLocator.FindInstalledGame("ThisGameDoesNotExist")).IsNull();
            }
        }

        [Test]
        public async Task LinuxSceneContextLoadsDefaultCubemap()
        {
            var context = new LinuxSceneViewerContext(NullLogger.Instance);
            using var stream = context.OpenDefaultCubemapStream();

            await Assert.That(stream).IsNotNull();

            using var resource = new Resource();
            resource.Read(stream!);
            await Assert.That(resource.DataBlock).IsTypeOf<Texture>();
        }

        [Test]
        [NotInParallel(nameof(LinuxGuiTest))]
        public async Task SceneCoreAppliesRenderingSettings()
        {
            var oldMaxTextureSize = Settings.Config.MaxTextureSize;
            var oldFieldOfView = Settings.Config.FieldOfView;
            var oldViewmodelFieldOfView = Settings.Config.ViewmodelFieldOfView;

            try
            {
                using var fileLoader = new GameFileLoader(null, null);
                using var rendererContext = new RendererContext(fileLoader, NullLogger.Instance);
                using var core = new SettingsSceneCore(new SettingsSceneContext(), rendererContext, new SettingsSceneHost());

                Settings.Config.MaxTextureSize = 2048;
                Settings.Config.FieldOfView = 100f;
                Settings.Config.ViewmodelFieldOfView = 70f;
                core.ApplySettingsToRenderState();

                using (Assert.Multiple())
                {
                    await Assert.That(rendererContext.MaxTextureSize).IsEqualTo(2048);
                    await Assert.That(rendererContext.FieldOfView).IsEqualTo(100f);
                    await Assert.That(rendererContext.ViewmodelFieldOfView).IsEqualTo(70f);
                    await Assert.That(core.Renderer.Camera.FieldOfView).IsEqualTo(100f);
                    await Assert.That(core.Input.Camera.FieldOfView).IsEqualTo(100f);
                }
            }
            finally
            {
                Settings.Config.MaxTextureSize = oldMaxTextureSize;
                Settings.Config.FieldOfView = oldFieldOfView;
                Settings.Config.ViewmodelFieldOfView = oldViewmodelFieldOfView;
            }
        }

        [Test]
        public async Task DefaultLayerSelectionDisablesUnlistedNodes()
        {
            using var fileLoader = new GameFileLoader(null, null);
            using var rendererContext = new RendererContext(fileLoader, NullLogger.Instance);
            using var core = new SettingsSceneCore(new SettingsSceneContext(), rendererContext, new SettingsSceneHost());
            var node = new TestSceneNode(core.Scene) { LayerName = "hidden" };
            core.Scene.Add(node, dynamic: true);

            core.ApplyDefaultLayers(["visible"]);

            await Assert.That(node.LayerEnabled).IsFalse();
        }

        [Test]
        public async Task SceneCoreDisposesRendererContext()
        {
            using var fileLoader = new GameFileLoader(null, null);
            using var rendererContext = new RendererContext(fileLoader, NullLogger.Instance);
            var core = new SettingsSceneCore(new SettingsSceneContext(), rendererContext, new SettingsSceneHost());

            core.Dispose();

            await Assert.That(rendererContext.CancellationToken.IsCancellationRequested).IsTrue();
        }

        [Test]
        public async Task GraphPresenterExposesBuiltGraph()
        {
            using var resource = new Resource();
            resource.Read(FilePath("de_inferno_script.vpulse_c"));
            var data = ((BinaryKV3)resource.DataBlock!).Data;

            using var view = new GraphView(GraphPalette.Default);
            new PulseGraphBuilder(data).Build(view.Document);

            using (Assert.Multiple())
            {
                await Assert.That(view.NodeCount).IsEqualTo(14);
                await Assert.That(view.WireCount).IsEqualTo(12);
                await Assert.That(view.GetGraphBounds().IsEmpty).IsFalse();
            }
        }

        private sealed class SettingsSceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host)
            : GLSceneViewerCore(context, rendererContext, host)
        {
            public void ApplyDefaultLayers(IEnumerable<string> layers) => SetDefaultEnabledLayers(layers);

            protected override void LoadScene()
            {
            }
        }

        private sealed class TestSceneNode(Scene scene) : SceneNode(scene);

        private sealed class SettingsSceneContext : ISceneViewerContext
        {
            public Microsoft.Extensions.Logging.ILogger SceneLogger { get; } = NullLogger.Instance;
            public IProgress<string>? LoadingProgress => null;

            public void ClearCache()
            {
            }

            public Stream? OpenDefaultCubemapStream() => null;
        }

        private sealed class SettingsSceneHost : IGLViewerHost
        {
            public ViewerInputState Input { get; } = new();
            public bool IsVisible => true;
            public Framebuffer ScreenFramebuffer => null!;

            public void RequestFrame()
            {
            }

            public void RequestFullscreen()
            {
            }

            public void SetClipboardImage(SKBitmap bitmap)
            {
            }
        }
    }
}
