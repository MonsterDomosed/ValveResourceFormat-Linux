using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Graphs;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Tests.IO
{
    public class EntityIOParamMapTest
    {
        private static readonly string VpkPath = TestFixtures.Path("entity_io_param_map_test.vpk");

        private static EntityLump LoadEntityLump()
        {
            using var package = new Package();
            package.Read(VpkPath);
            package.ReadEntry(package.FindEntry("maps/wtf/entities/default_ents.vents_c")!, out var bytes);

            using var resource = new Resource();
            resource.Read(new MemoryStream(bytes));
            return (EntityLump)resource.DataBlock!;
        }

        [Test]
        public async Task ParsesParamMapsFromLump()
        {
            var connections = LoadEntityLump().GetEntities()
                .SelectMany(static entity => entity.Connections ?? [])
                .ToList();

            var maintainOffset = connections.Single(static connection => connection.InputName == "SetParentMaintainOffset" && connection.OutputName == "OnTrigger");
            var parent = maintainOffset.ParamMap!["pNewParent"]["value"];

            var routed = connections.Single(static connection => connection.OutputName == "OnDestructibleHitGroupDamageLevelChanged" && connection.InputName == "SetTextString");
            var empty = connections.Single(static connection => connection.InputName == "string_string_string");
            var legacy = connections.Single(static connection => connection.TargetName == "!activator");

            using (Assert.Multiple())
            {
                await Assert.That((string)parent).IsEqualTo("[PR#]io_breaker");
                await Assert.That((string)routed.ParamMap!["pText"]["src"]).IsEqualTo("nDamageLevel");
                await Assert.That((bool)routed.ParamMap!["bLocalize"]["value"]).IsFalse();
                await Assert.That(empty.ParamMap).IsNotNull();
                await Assert.That(empty.ParamMap!.Count).IsEqualTo(0);
                await Assert.That(legacy.ParamMap).IsNull();
                await Assert.That(legacy.OverrideParam).IsEqualTo("tesfasdfas");
            }
        }

        [Test]
        public async Task FormatsParamMapsInEntityDump()
        {
            var dump = LoadEntityLump().ToEntityDumpString();

            using (Assert.Multiple())
            {
                await Assert.That(dump).Contains("pNewParent=entity_name:\"[PR#]io_breaker\"");
                await Assert.That(dump).Contains("pText<-nDamageLevel");
                await Assert.That(dump).Contains("bLocalize=false");
            }
        }

        [Test]
        public async Task WritesParamMapsToVmap()
        {
            using var package = new Package();
            package.Read(VpkPath);
            using var loader = new GameFileLoader(package, VpkPath);
            using var vmapResource = loader.LoadFile("maps/wtf.vmap_c");

            var vmapData = new MapExtract(vmapResource!, loader).ToValveMap();
            using var vmap = Datamodel.Datamodel.Load(new MemoryStream(vmapData), Datamodel.Codecs.DeferredMode.Disabled);

            var connections = vmap.AllElements
                .Where(static element => element.ClassName == "DmeConnectionData")
                .ToList();

            Datamodel.Element ParamMapOf(string outputName, string inputName)
                => (Datamodel.Element)connections.Single(connection => (string)connection["outputName"]! == outputName && (string)connection["inputName"]! == inputName)["paramMap"]!;

            var maintainOffset = ParamMapOf("OnTrigger", "SetParentMaintainOffset");
            var parent = (Datamodel.Element)((Datamodel.Element)maintainOffset["pNewParent"]!)["value"]!;
            var routed = ParamMapOf("OnDestructibleHitGroupDamageLevelChanged", "SetTextString");
            var empty = ParamMapOf("OnTrigger", "string_string_string");

            using (Assert.Multiple())
            {
                await Assert.That(maintainOffset.ClassName).IsEqualTo("DmElement");
                await Assert.That(maintainOffset.Name).IsEqualTo("paramMap");
                await Assert.That((string)parent["specific_type"]!).IsEqualTo("entity_name");
                await Assert.That((string)parent["value"]!).IsEqualTo("io_breaker");
                await Assert.That((string)((Datamodel.Element)routed["pText"]!)["src"]!).IsEqualTo("nDamageLevel");
                await Assert.That((bool)((Datamodel.Element)routed["bLocalize"]!)["value"]!).IsFalse();
                await Assert.That(empty.Count).IsEqualTo(0);
                await Assert.That(connections.Single(static connection => (string)connection["targetName"]! == "!activator").ContainsKey("paramMap")).IsFalse();
            }
        }

        [Test]
        public async Task LabelsMappedEntityIOConnections()
        {
            var entities = LoadEntityLump().GetEntities();
            var document = new GraphDocument();
            EntityIOGraphBuilder.Build(document, entities);

            await Assert.That(document.Wires.Any(static wire => wire.Label?.Contains("(mapped params)", StringComparison.Ordinal) == true)).IsTrue();
        }
    }
}
