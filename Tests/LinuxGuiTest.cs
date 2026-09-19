using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GUI.Linux.Types.Audio;
using GUI.Linux.Types.Browser;
using GUI.Linux.Types.Graphs.Core;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Graphs;
using ValveResourceFormat.IO;
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
    }
}
