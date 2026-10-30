using System.IO;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks
{
    /// <summary>Region-box world visibility.</summary>
    public sealed class RegionBoxVoxelVisibility : IWorldVisibility
    {
        private const int BitsPerWord = 32;
        private const int NodeBytes = 8;
        private const int NodeExtraBytes = 12;
        private const int RegionBytes = 28;
        private const float QueryGrowth = 1f;
        private const int NowhereCluster = 1;
        private const ushort NoClusterValue = 0xFFFF;

        /// <summary>The cluster id used for regions without a cluster.</summary>
        public const ushort NoCluster = NoClusterValue;

        /// <summary>Represents an octree node.</summary>
        public readonly struct Node(uint packed, ushort regionCount)
        {
            /// <summary>Gets whether this is a leaf.</summary>
            public bool IsLeaf => (packed & 1) != 0;

            /// <summary>Gets the child or region offset.</summary>
            public uint Offset => packed >> 1;

            /// <summary>Gets the number of regions in a leaf.</summary>
            public ushort RegionCount => regionCount;
        }

        /// <summary>Represents a region box.</summary>
        public readonly struct Region(AABB bounds, ushort clusterId, ushort mergedRow = 0)
        {
            /// <summary>Gets the region bounds.</summary>
            public AABB Bounds => bounds;

            /// <summary>Gets the cluster id.</summary>
            public ushort ClusterId => clusterId;

            /// <summary>Gets whether the region has a cluster.</summary>
            public bool HasCluster => clusterId != NoClusterValue;

            /// <summary>Gets the one-based merged row index.</summary>
            public ushort MergedRow => mergedRow;
        }

        /// <summary>Gets the minimum bounds.</summary>
        public Vector3 MinBounds { get; private set; }

        /// <summary>Gets the maximum bounds.</summary>
        public Vector3 MaxBounds { get; private set; }

        /// <summary>Gets the grid size.</summary>
        public float GridSize { get; private set; }

        /// <summary>Gets the octree nodes.</summary>
        public Node[] Nodes { get; private set; } = [];

        /// <summary>Gets the regions.</summary>
        public Region[] Regions { get; private set; } = [];

        /// <summary>Gets the number of clusters.</summary>
        public int ClusterCount { get; private set; }

        /// <summary>Gets the bytes per visibility row.</summary>
        public int BytesPerCluster { get; private set; }

        /// <summary>Gets the packed visibility rows.</summary>
        public byte[] VisBlocks { get; private set; } = [];

        /// <summary>Gets the row offsets of ordinary clusters.</summary>
        public int[] ClusterRowOffsets { get; private set; } = [];

        /// <summary>Gets the merged visibility rows.</summary>
        public byte[] MergedVisBlocks { get; private set; } = [];

        /// <summary>Gets the sky row offset.</summary>
        public int SkyRowOffset { get; private set; } = -1;

        /// <summary>Gets the sun row offset.</summary>
        public int SunRowOffset { get; private set; } = -1;

        /// <summary>Gets whether the rows are raw.</summary>
        public bool IsRawPvs { get; private set; }

        /// <inheritdoc/>
        public bool HasVisibilityData => IsRawPvs && Nodes.Length > 0 && ClusterCount > 0 && BytesPerCluster > 0;

        /// <inheritdoc/>
        public int ClusterBitfieldWordCount => (Math.Max(ClusterCount, 1) + BitsPerWord - 1) / BitsPerWord;

        /// <inheritdoc/>
        public ReadOnlyMemory<byte> SunVisibility => SunRowOffset < 0 ? default : RowMemory(SunRowOffset);

        /// <summary>Reads the region-box payload.</summary>
        public void Read(KVObject data, BinaryReader reader, long blockOffset)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(reader);

            MinBounds = data.GetSubCollection("m_vMinBounds").ToVector3();
            MaxBounds = data.GetSubCollection("m_vMaxBounds").ToVector3();
            GridSize = data.GetFloatProperty("m_flGridSize");
            IsRawPvs = data.GetStringProperty("m_nPVSCompression") is "VOXVIS_COMPRESS_RAW";

            var nodeCount = data.GetInt32Property("m_nNodeCount");
            var regionCount = data.GetInt32Property("m_nRegionCount");
            var treeSize = data.GetInt32Property("m_nTreeSize");
            var pvsSize = data.GetInt32Property("m_nPVSSizeCompressed");
            var clusters = data.GetArray("m_clusters");
            ClusterCount = clusters.Count;
            BytesPerCluster = (ClusterCount + 7) / 8;

            var blockOffsets = data.GetIntegerArray("m_blockOffset");
            ClusterRowOffsets = new int[ClusterCount];
            for (var i = 0; i < ClusterCount; i++)
            {
                ClusterRowOffsets[i] = RowOffset(clusters[i], blockOffsets);
            }

            if (data.ContainsKey("m_skyVisibilityCluster"))
            {
                SkyRowOffset = RowOffset(data.GetSubCollection("m_skyVisibilityCluster"), blockOffsets);
            }

            if (data.ContainsKey("m_sunVisibilityCluster"))
            {
                SunRowOffset = RowOffset(data.GetSubCollection("m_sunVisibilityCluster"), blockOffsets);
            }

            if (nodeCount < 0 || regionCount < 0 || nodeCount * (NodeBytes + NodeExtraBytes) + (long)regionCount * RegionBytes != treeSize)
            {
                return;
            }

            reader.BaseStream.Position = blockOffset;
            Nodes = new Node[nodeCount];
            for (var i = 0; i < nodeCount; i++)
            {
                Nodes[i] = new Node(reader.ReadUInt32(), reader.ReadUInt16());
                reader.BaseStream.Position += 2;
            }

            reader.BaseStream.Position += (long)nodeCount * NodeExtraBytes;
            var mergeLists = data.ContainsKey("m_mergeLists") ? data.GetArray("m_mergeLists") : [];
            Regions = new Region[regionCount];
            for (var i = 0; i < regionCount; i++)
            {
                var min = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                var max = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                var packed = reader.ReadUInt16();
                var mergedRow = mergeLists.Count > 0 ? (ushort)(packed >> 2) : (ushort)0;
                Regions[i] = new Region(new AABB(min, max), reader.ReadUInt16(), mergedRow);
            }

            reader.BaseStream.Position = blockOffset + treeSize;
            VisBlocks = reader.ReadBytes(pvsSize);
            if (mergeLists.Count > 0)
            {
                ReadMergedRows(mergeLists, data.GetIntegerArray("m_mergeClusters"));
            }
        }

        private void ReadMergedRows(IReadOnlyList<KVObject> mergeLists, long[] mergeClusters)
        {
            MergedVisBlocks = new byte[mergeLists.Count * BytesPerCluster];
            for (var i = 0; i < mergeLists.Count; i++)
            {
                var first = mergeLists[i].GetInt32Property("m_nFirstCluster");
                var count = mergeLists[i].GetInt32Property("m_nClusterCount");
                var merged = MergedVisBlocks.AsSpan(i * BytesPerCluster, BytesPerCluster);
                for (var j = first; j < first + count && j < mergeClusters.Length; j++)
                {
                    var cluster = (int)mergeClusters[j];
                    if (cluster < 0 || cluster >= ClusterCount)
                    {
                        continue;
                    }

                    var row = RowMemory(ClusterRowOffsets[cluster]).Span;
                    for (var b = 0; b < row.Length; b++)
                    {
                        merged[b] |= row[b];
                    }
                }
            }
        }

        private static int RowOffset(KVObject cluster, long[] blockOffsets)
        {
            var blockIndex = cluster.GetInt32Property("m_nBlockIndex");
            var offset = cluster.GetInt32Property("m_nOffsetIntoBlock");
            return (int)(blockIndex < 0 || blockIndex >= blockOffsets.Length ? 0 : blockOffsets[blockIndex]) + offset;
        }

        /// <inheritdoc/>
        public int GetClusterForPosition(Vector3 position)
        {
            var region = FindRegion(position);
            return region < 0 ? -1 : Regions[region].ClusterId;
        }

        private int FindRegion(Vector3 position)
        {
            var leaf = FindLeafNode(position);
            if (leaf < 0)
            {
                return -1;
            }

            var node = Nodes[leaf];
            var regions = LeafRegions(node);
            for (var i = 0; i < regions.Length; i++)
            {
                var region = regions[i];
                if (region.HasCluster && region.ClusterId < ClusterCount && region.Bounds.Contains(position))
                {
                    return (int)node.Offset + i;
                }
            }

            return -1;
        }

        /// <summary>Gets an ordinary cluster row.</summary>
        public ReadOnlyMemory<byte> GetVisibilityRow(int clusterId)
        {
            if (!HasVisibilityData || clusterId < 0 || clusterId >= ClusterCount)
            {
                return default;
            }

            return RowMemory(ClusterRowOffsets[clusterId]);
        }

        /// <inheritdoc/>
        public ReadOnlyMemory<byte> GetVisibilityRowForPoint(Vector3 point)
        {
            var index = FindRegion(point);
            if (index < 0)
            {
                return GetVisibilityRow(0);
            }

            var region = Regions[index];
            var mergedOffset = (region.MergedRow - 1L) * BytesPerCluster;
            if (region.MergedRow > 0 && mergedOffset + BytesPerCluster <= MergedVisBlocks.Length)
            {
                return MergedVisBlocks.AsMemory((int)mergedOffset, BytesPerCluster);
            }

            return GetVisibilityRow(region.ClusterId);
        }

        /// <inheritdoc/>
        public void GetVisClustersForBox(Vector3 min, Vector3 max, Span<uint> clusterBits)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(clusterBits.Length, ClusterBitfieldWordCount);
            clusterBits = clusterBits[..ClusterBitfieldWordCount];
            clusterBits.Clear();
            if (clusterBits.IsEmpty)
            {
                return;
            }

            var query = new AABB(Vector3.Min(min, max) - new Vector3(QueryGrowth), Vector3.Max(min, max) + new Vector3(QueryGrowth));
            if (Nodes.Length == 0 || query.Contains(new AABB(MinBounds, MaxBounds)))
            {
                clusterBits[0] = 1;
                return;
            }

            QueryOctreeBox(0, new AABB(MinBounds, MaxBounds), query, clusterBits);
            if (!clusterBits.ContainsAnyExcept(0u) && clusterBits.Length * BitsPerWord > NowhereCluster)
            {
                clusterBits[NowhereCluster / BitsPerWord] |= 1u << (NowhereCluster % BitsPerWord);
            }
        }

        private void QueryOctreeBox(uint nodeIndex, AABB nodeBounds, AABB box, Span<uint> clusterBits)
        {
            if (nodeIndex >= Nodes.Length || !nodeBounds.Intersects(box))
            {
                return;
            }

            var node = Nodes[nodeIndex];
            if (node.IsLeaf)
            {
                var capacity = clusterBits.Length * BitsPerWord;
                foreach (var region in LeafRegions(node))
                {
                    if (region.HasCluster && region.ClusterId < capacity && region.Bounds.Intersects(box))
                    {
                        clusterBits[region.ClusterId / BitsPerWord] |= 1u << (region.ClusterId % BitsPerWord);
                    }
                }

                return;
            }

            var mid = nodeBounds.Center;
            for (uint octant = 0; octant < 8; octant++)
            {
                var childMin = nodeBounds.Min;
                var childMax = nodeBounds.Max;
                if ((octant & 1) != 0) { childMin.X = mid.X; } else { childMax.X = mid.X; }
                if ((octant & 2) != 0) { childMin.Y = mid.Y; } else { childMax.Y = mid.Y; }
                if ((octant & 4) != 0) { childMin.Z = mid.Z; } else { childMax.Z = mid.Z; }
                var childBounds = new AABB(childMin, childMax);
                if (childBounds.Intersects(box))
                {
                    QueryOctreeBox(node.Offset + octant, childBounds, box, clusterBits);
                }
            }
        }

        private ReadOnlySpan<Region> LeafRegions(Node node)
        {
            var start = (int)node.Offset;
            return start >= Regions.Length ? [] : Regions.AsSpan(start, Math.Min(node.RegionCount, Regions.Length - start));
        }

        private int FindLeafNode(Vector3 point)
        {
            if (Nodes.Length == 0 || !new AABB(MinBounds, MaxBounds).Contains(point))
            {
                return -1;
            }

            var nodeIndex = 0;
            var min = MinBounds;
            var max = MaxBounds;
            while (!Nodes[nodeIndex].IsLeaf)
            {
                var mid = (min + max) * 0.5f;
                var octant = 0;
                if (mid.X < point.X) { octant |= 1; min.X = mid.X; } else { max.X = mid.X; }
                if (mid.Y < point.Y) { octant |= 2; min.Y = mid.Y; } else { max.Y = mid.Y; }
                if (mid.Z < point.Z) { octant |= 4; min.Z = mid.Z; } else { max.Z = mid.Z; }
                nodeIndex = (int)Nodes[nodeIndex].Offset + octant;
                if ((uint)nodeIndex >= Nodes.Length)
                {
                    return -1;
                }
            }

            return nodeIndex;
        }

        /// <inheritdoc/>
        public Dictionary<ushort, List<(Vector3 Min, Vector3 Max)>> BuildClusterChildBounds()
        {
            var result = new Dictionary<ushort, List<(Vector3 Min, Vector3 Max)>>();
            foreach (var region in Regions)
            {
                if (!region.HasCluster || region.ClusterId >= ClusterCount)
                {
                    continue;
                }

                if (!result.TryGetValue(region.ClusterId, out var list))
                {
                    list = [];
                    result[region.ClusterId] = list;
                }

                list.Add((region.Bounds.Min, region.Bounds.Max));
            }

            return result;
        }

        private ReadOnlyMemory<byte> RowMemory(int offset)
            => offset < 0 || offset + BytesPerCluster > VisBlocks.Length
                ? default
                : VisBlocks.AsMemory(offset, BytesPerCluster);
    }
}
