using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks
{
    /// <summary>Binary split-plane world visibility.</summary>
    public sealed class BspVoxelVisibility : IWorldVisibility
    {
        private const int BitsPerWord = 32;

        /// <summary>Represents a split plane and its two children.</summary>
        public readonly struct Node(Vector3 normal, float distance, int front, int back)
        {
            /// <summary>Gets the plane normal.</summary>
            public Vector3 Normal => normal;

            /// <summary>Gets the plane distance.</summary>
            public float Distance => distance;

            /// <summary>Gets the positive-side child.</summary>
            public int Front => front;

            /// <summary>Gets the negative-side child.</summary>
            public int Back => back;

            /// <summary>Gets the signed distance of a point from the plane.</summary>
            public float DistanceTo(Vector3 point) => Vector3.Dot(normal, point) - distance;
        }

        /// <summary>Represents a leaf cluster.</summary>
        public readonly struct Cluster(AABB bounds, int rowOffset, float nearVisibleDistance, float farVisibleDistance)
        {
            /// <summary>Gets the cluster bounds.</summary>
            public AABB Bounds => bounds;

            /// <summary>Gets the visibility row offset.</summary>
            public int RowOffset => rowOffset;

            /// <summary>Gets the near visibility distance.</summary>
            public float NearVisibleDistance => nearVisibleDistance;

            /// <summary>Gets the far visibility distance.</summary>
            public float FarVisibleDistance => farVisibleDistance;

            /// <summary>Gets whether the cluster has a usable box.</summary>
            public bool HasBounds => bounds.Size != Vector3.Zero;
        }

        /// <summary>Gets the split-plane nodes.</summary>
        public Node[] Nodes { get; private set; } = [];

        /// <summary>Gets the clusters.</summary>
        public Cluster[] Clusters { get; private set; } = [];

        /// <summary>Gets the packed visibility rows.</summary>
        public byte[] VisData { get; private set; } = [];

        /// <inheritdoc/>
        public Vector3 MinBounds { get; private set; }

        /// <inheritdoc/>
        public Vector3 MaxBounds { get; private set; }

        /// <inheritdoc/>
        public int ClusterCount => Clusters.Length;

        /// <summary>Gets the number of bytes in one row.</summary>
        public int BytesPerCluster { get; private set; }

        /// <inheritdoc/>
        public int ClusterBitfieldWordCount => (Math.Max(ClusterCount, 1) + BitsPerWord - 1) / BitsPerWord;

        /// <inheritdoc/>
        public bool HasVisibilityData => Nodes.Length > 0 && ClusterCount > 0 && BytesPerCluster > 0 && VisData.Length > 0;

        /// <inheritdoc/>
        public ReadOnlyMemory<byte> SunVisibility => default;

        /// <summary>Reads the old split-plane layout.</summary>
        public void Read(KVObject data)
        {
            ArgumentNullException.ThrowIfNull(data);

            var clusters = data.GetArray("m_clusters");
            var nodes = data.GetArray("m_nodes");
            Clusters = new Cluster[clusters.Count];
            BytesPerCluster = (clusters.Count + 7) / 8;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (var i = 0; i < clusters.Count; i++)
            {
                var cluster = clusters[i];
                var bounds = new AABB(
                    cluster.GetSubCollection("m_vMins").ToVector3(),
                    cluster.GetSubCollection("m_vMaxs").ToVector3());
                Clusters[i] = new Cluster(
                    bounds,
                    cluster.GetInt32Property("m_nData"),
                    cluster.GetFloatProperty("m_flNearVisibleDistance"),
                    cluster.GetFloatProperty("m_flFarVisibleDistance"));

                if (Clusters[i].HasBounds)
                {
                    min = Vector3.Min(min, bounds.Min);
                    max = Vector3.Max(max, bounds.Max);
                }
            }

            if (min.X <= max.X)
            {
                MinBounds = min;
                MaxBounds = max;
            }

            var nodeCount = data.ContainsKey("m_nNodeCount")
                ? Math.Clamp((int)data.GetIntegerProperty("m_nNodeCount"), 0, nodes.Count)
                : nodes.Count;
            Nodes = new Node[nodeCount];
            for (var i = 0; i < nodeCount; i++)
            {
                var children = nodes[i].GetIntegerArray("m_nChildren");
                Nodes[i] = new Node(
                    nodes[i].GetSubCollection("m_vSplitNormal").ToVector3(),
                    nodes[i].GetFloatProperty("m_flDist"),
                    children.Length > 0 ? (int)children[0] : 0,
                    children.Length > 1 ? (int)children[1] : 0);
            }

            if (!IsTree())
            {
                Nodes = [];
            }

            var dwords = data.GetIntegerArray("m_visDataDwords");
            VisData = new byte[dwords.Length * sizeof(uint)];
            for (var i = 0; i < dwords.Length; i++)
            {
                BitConverter.TryWriteBytes(VisData.AsSpan(i * sizeof(uint)), (uint)dwords[i]);
            }
        }

        private bool IsTree()
        {
            if (Nodes.Length == 0)
            {
                return true;
            }

            var reached = new bool[Nodes.Length];
            var pending = new Stack<int>();
            reached[0] = true;
            pending.Push(0);
            while (pending.TryPop(out var index))
            {
                foreach (var child in (ReadOnlySpan<int>)[Nodes[index].Front, Nodes[index].Back])
                {
                    if (child < 0 || child >= Nodes.Length)
                    {
                        continue;
                    }

                    if (reached[child])
                    {
                        return false;
                    }

                    reached[child] = true;
                    pending.Push(child);
                }
            }

            return true;
        }

        private bool IsCluster(int child, out int cluster)
        {
            cluster = child - Nodes.Length;
            return cluster >= 0;
        }

        /// <inheritdoc/>
        public int GetClusterForPosition(Vector3 position)
        {
            if (Nodes.Length == 0)
            {
                return -1;
            }

            var index = 0;
            var visited = 0;
            while (visited++ <= Nodes.Length)
            {
                var node = Nodes[index];
                var child = node.DistanceTo(position) >= 0f ? node.Front : node.Back;
                if (IsCluster(child, out var cluster))
                {
                    return cluster < ClusterCount ? cluster : -1;
                }

                if ((uint)child >= (uint)Nodes.Length)
                {
                    return -1;
                }

                index = child;
            }

            return -1;
        }

        /// <inheritdoc/>
        public ReadOnlyMemory<byte> GetVisibilityRowForPoint(Vector3 point)
        {
            var cluster = GetClusterForPosition(point);
            return cluster < 0 ? default : GetVisibilityRow(cluster);
        }

        /// <summary>Gets a cluster visibility row.</summary>
        public ReadOnlyMemory<byte> GetVisibilityRow(int clusterId)
        {
            if (clusterId < 0 || clusterId >= ClusterCount)
            {
                return default;
            }

            var offset = Clusters[clusterId].RowOffset;
            return offset < 0 || offset + BytesPerCluster > VisData.Length
                ? default
                : VisData.AsMemory(offset, BytesPerCluster);
        }

        /// <inheritdoc/>
        public void GetVisClustersForBox(Vector3 min, Vector3 max, Span<uint> clusterBits)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(clusterBits.Length, ClusterBitfieldWordCount);
            clusterBits = clusterBits[..ClusterBitfieldWordCount];
            clusterBits.Clear();
            if (Nodes.Length > 0)
            {
                Descend(0, new AABB(Vector3.Min(min, max), Vector3.Max(min, max)), clusterBits);
            }
        }

        private void Descend(int index, AABB box, Span<uint> clusterBits)
        {
            if ((uint)index >= (uint)Nodes.Length)
            {
                return;
            }

            var node = Nodes[index];
            var front = new Vector3(
                node.Normal.X < 0f ? box.Min.X : box.Max.X,
                node.Normal.Y < 0f ? box.Min.Y : box.Max.Y,
                node.Normal.Z < 0f ? box.Min.Z : box.Max.Z);
            if (node.DistanceTo(front) >= 0f)
            {
                Visit(node.Front, box, clusterBits);
            }

            if (node.DistanceTo(box.Min + box.Max - front) <= 0f)
            {
                Visit(node.Back, box, clusterBits);
            }
        }

        private void Visit(int child, AABB box, Span<uint> clusterBits)
        {
            if (IsCluster(child, out var cluster))
            {
                if (cluster < ClusterCount)
                {
                    clusterBits[cluster / BitsPerWord] |= 1u << (cluster % BitsPerWord);
                }
            }
            else
            {
                Descend(child, box, clusterBits);
            }
        }

        /// <inheritdoc/>
        public Dictionary<ushort, List<(Vector3 Min, Vector3 Max)>> BuildClusterChildBounds()
        {
            var result = new Dictionary<ushort, List<(Vector3 Min, Vector3 Max)>>();
            for (var i = 0; i < Clusters.Length; i++)
            {
                if (Clusters[i].HasBounds)
                {
                    result[(ushort)i] = [(Clusters[i].Bounds.Min, Clusters[i].Bounds.Max)];
                }
            }

            return result;
        }
    }
}
