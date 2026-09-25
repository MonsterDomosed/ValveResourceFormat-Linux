using System.Numerics;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    public class WorldNodeVisibilityTest
    {
        [Test]
        public async Task DecodesPrecomputedSceneAndAggregateMembership()
        {
            var node = new TestWorldNode { Resource = new Resource() };
            var data = KVObject.Collection();
            data.Add("m_visClusterMembership", IntegerValues([10, 20, 30, 40]));
            node.SetData(data);

            var packed = (2u << 24) | 1u;
            var sceneObject = KVObject.Collection();
            sceneObject.Add("m_nObjectTypeFlags", new KVObject((int)ObjectTypeFlags.PrecomputedVismembers));
            sceneObject.Add("m_VisClusterMemberBits", new KVObject(BitOperations.RotateLeft(packed, 8)));

            var aggregateMesh = KVObject.Collection();
            aggregateMesh.Add("m_objectFlags", new KVObject((int)ObjectTypeFlags.PrecomputedVismembers));
            aggregateMesh.Add("m_nVisClusterMemberOffset", new KVObject(2));
            aggregateMesh.Add("m_nVisClusterMemberCount", new KVObject(2));

            var sceneClusters = node.GetSceneObjectVisClusters(sceneObject);
            var aggregateClusters = node.GetAggregateMeshVisClusters(aggregateMesh);

            using (Assert.Multiple())
            {
                await Assert.That(sceneClusters).IsEquivalentTo(new ushort[] { 20, 30 });
                await Assert.That(aggregateClusters).IsEquivalentTo(new ushort[] { 30, 40 });
            }
        }

        [Test]
        public async Task IgnoresMembershipWithoutTheCompilerFlag()
        {
            var node = new TestWorldNode { Resource = new Resource() };
            node.SetData(KVObject.Collection());
            var sceneObject = KVObject.Collection();
            sceneObject.Add("m_VisClusterMemberBits", new KVObject(0));

            await Assert.That(node.GetSceneObjectVisClusters(sceneObject)).IsNull();
        }

        private static KVObject IntegerValues(IEnumerable<long> values)
        {
            var array = KVObject.Array();
            foreach (var value in values)
            {
                array.Add(new KVObject(value));
            }

            return array;
        }

        private sealed class TestWorldNode : WorldNode
        {
            public void SetData(KVObject data) => Data = data;
        }
    }
}
