using System.IO;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    public class LegacyVisibilityTest
    {
        [Test]
        public async Task BspVisibilityResolvesPlanesRowsAndBoxes()
        {
            var data = KVObject.Collection();
            var clusters = KVObject.Array();
            clusters.Add(Cluster(new Vector3(0f), new Vector3(10f), 0));
            clusters.Add(Cluster(new Vector3(0f), new Vector3(10f), 1));
            data.Add("m_clusters", clusters);

            var nodes = KVObject.Array();
            var node = KVObject.Collection();
            node.Add("m_vSplitNormal", VectorValues(new Vector3(1f, 0f, 0f)));
            node.Add("m_flDist", new KVObject(0f));
            node.Add("m_nChildren", IntegerValues([1, 2]));
            nodes.Add(node);
            data.Add("m_nodes", nodes);
            data.Add("m_visDataDwords", IntegerValues([0x00000005]));

            var visibility = new BspVoxelVisibility();
            visibility.Read(data);

            var clustersInBox = new uint[1];
            visibility.GetVisClustersForBox(new Vector3(-2f), new Vector3(-1f), clustersInBox);
            var negativeClusters = clustersInBox[0];

            clustersInBox[0] = 0;
            visibility.GetVisClustersForBox(new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f), clustersInBox);

            using (Assert.Multiple())
            {
                await Assert.That(visibility.HasVisibilityData).IsTrue();
                await Assert.That(visibility.GetClusterForPosition(new Vector3(-1f, 0f, 0f))).IsEqualTo(1);
                await Assert.That(visibility.GetClusterForPosition(new Vector3(1f, 0f, 0f))).IsZero();
                await Assert.That(negativeClusters & 0x3).IsEqualTo(0x2u);
                await Assert.That(clustersInBox[0] & 0x3).IsEqualTo(0x3u);
                await Assert.That(visibility.GetVisibilityRowForPoint(new Vector3(-1f, 0f, 0f)).Length).IsEqualTo(1);
            }
        }

        [Test]
        public async Task RegionBoxVisibilityResolvesRegionsAndCatchAllBoxes()
        {
            var data = KVObject.Collection();
            data.Add("m_vMinBounds", VectorValues(new Vector3(-1f)));
            data.Add("m_vMaxBounds", VectorValues(new Vector3(1f)));
            data.Add("m_flGridSize", new KVObject(1f));
            data.Add("m_nPVSCompression", new KVObject("VOXVIS_COMPRESS_RAW"));
            data.Add("m_nNodeCount", new KVObject(1));
            data.Add("m_nRegionCount", new KVObject(1));
            data.Add("m_nTreeSize", new KVObject(48));
            data.Add("m_nPVSSizeCompressed", new KVObject(1));
            data.Add("m_blockOffset", IntegerValues([0]));

            var clusters = KVObject.Array();
            var cluster = KVObject.Collection();
            cluster.Add("m_nBlockIndex", new KVObject(0));
            cluster.Add("m_nOffsetIntoBlock", new KVObject(0));
            clusters.Add(cluster);
            data.Add("m_clusters", clusters);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(1u);
                writer.Write((ushort)1);
                writer.Write((ushort)0);
                writer.Write(new byte[12]);
                foreach (var value in new[] { -1f, -1f, -1f, 1f, 1f, 1f })
                {
                    writer.Write(value);
                }

                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((byte)0xFF);
            }

            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var visibility = new RegionBoxVoxelVisibility();
            visibility.Read(data, reader, 0);

            var clustersInBox = new uint[1];
            visibility.GetVisClustersForBox(new Vector3(-0.5f), new Vector3(0.5f), clustersInBox);
            var insideCluster = clustersInBox[0];

            clustersInBox[0] = 0;
            visibility.GetVisClustersForBox(new Vector3(-2f), new Vector3(2f), clustersInBox);

            using (Assert.Multiple())
            {
                await Assert.That(visibility.HasVisibilityData).IsTrue();
                await Assert.That(visibility.GetClusterForPosition(Vector3.Zero)).IsZero();
                await Assert.That(visibility.GetClusterForPosition(new Vector3(2f))).IsEqualTo(-1);
                await Assert.That(insideCluster & 1).IsEqualTo(1u);
                await Assert.That(clustersInBox[0] & 1).IsEqualTo(1u);
                await Assert.That(visibility.GetVisibilityRowForPoint(Vector3.Zero).Length).IsEqualTo(1);
            }
        }

        private static KVObject Cluster(Vector3 min, Vector3 max, int dataOffset)
        {
            var cluster = KVObject.Collection();
            cluster.Add("m_vMins", VectorValues(min));
            cluster.Add("m_vMaxs", VectorValues(max));
            cluster.Add("m_nData", new KVObject(dataOffset));
            cluster.Add("m_flNearVisibleDistance", new KVObject(0f));
            cluster.Add("m_flFarVisibleDistance", new KVObject(1f));
            return cluster;
        }

        private static KVObject VectorValues(Vector3 value)
        {
            var values = KVObject.Array();
            values.Add(new KVObject(value.X));
            values.Add(new KVObject(value.Y));
            values.Add(new KVObject(value.Z));
            return values;
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
    }
}
