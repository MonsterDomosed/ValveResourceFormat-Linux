using System.Linq;
using System.Threading.Tasks;
using GUI.Linux.Viewers;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Graphs;
using ValveResourceFormat.IO;

namespace Tests
{
    public class MapGraphDataTest
    {
        [Test]
        public async Task LoadsMapEntitiesForEntityIOGraph()
        {
            using var package = new Package();
            package.Read(TestFixtures.Path("entity_io_param_map_test.vpk"));
            using var loader = new GameFileLoader(package, TestFixtures.Path("entity_io_param_map_test.vpk"));
            using var mapResource = loader.LoadFile("maps/wtf.vmap_c");

            var data = WorldGlViewer.LoadGraphData(mapResource!, loader);
            var document = new GraphDocument();
            EntityIOGraphBuilder.Build(document, data.Entities.ToList());

            using (Assert.Multiple())
            {
                await Assert.That(data.Entities.Count).IsGreaterThan(0);
                await Assert.That(data.Entities.Any(static entity => entity.Connections is { Count: > 0 })).IsTrue();
                await Assert.That(document.WireCount).IsGreaterThan(0);
            }
        }
    }
}
