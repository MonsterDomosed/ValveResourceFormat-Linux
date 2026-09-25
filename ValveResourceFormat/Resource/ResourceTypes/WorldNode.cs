using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a world node resource.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/worldrenderer/WorldNode_t">WorldNode_t</seealso>
    public class WorldNode : KeyValuesOrNTRO
    {
        /// <summary>
        /// Gets the scene objects.
        /// </summary>
        public IReadOnlyList<KVObject> SceneObjects
            => Data.GetArray("m_sceneObjects");

        /// <summary>
        /// Layer indices for <see cref="SceneObjects"/>.
        /// For <see cref="AggregateSceneObjects"/> use the dedicated 'm_nLayer' member.
        /// Value may be null if the node has no layer system.
        /// </summary>
        public IReadOnlyList<long>? SceneObjectLayerIndices
            => Data.ContainsKey("m_sceneObjectLayerIndices")
                ? Data.GetIntegerArray("m_sceneObjectLayerIndices")
                : null;

        /// <summary>
        /// Gets the aggregate scene objects.
        /// </summary>
        public IReadOnlyList<KVObject> AggregateSceneObjects
            => Data.ContainsKey("m_aggregateSceneObjects")
                ? Data.GetArray("m_aggregateSceneObjects")
                : [];

        /// <summary>
        /// Gets the clutter scene objects.
        /// </summary>
        public IReadOnlyList<KVObject> ClutterSceneObjects
            => Data.ContainsKey("m_clutterSceneObjects")
                ? Data.GetArray("m_clutterSceneObjects")
                : [];

        /// <summary>
        /// Gets the visibility cluster ids that precomputed scene-object and aggregate membership indexes into.
        /// </summary>
        public IReadOnlyList<long> VisClusterMembership
            => visClusterMembership ??= Data.ContainsKey("m_visClusterMembership")
                ? Data.GetIntegerArray("m_visClusterMembership")
                : [];

        private long[]? visClusterMembership;

        /// <summary>
        /// Gets precomputed visibility clusters for a scene object, or <see langword="null"/> when it uses bounds.
        /// </summary>
        public ushort[]? GetSceneObjectVisClusters(KVObject sceneObject)
        {
            ArgumentNullException.ThrowIfNull(sceneObject);
            var flags = sceneObject.ContainsKey("m_nObjectTypeFlags")
                ? sceneObject.GetEnumValue<ObjectTypeFlags>("m_nObjectTypeFlags", normalize: true)
                : default;
            if ((flags & ObjectTypeFlags.PrecomputedVismembers) == 0 || !sceneObject.ContainsKey("m_VisClusterMemberBits"))
            {
                return null;
            }

            var packed = BitOperations.RotateRight((uint)sceneObject.GetIntegerProperty("m_VisClusterMemberBits"), 8);
            return SliceVisClusterMembership((int)(packed & 0xFFFFFF), (int)(packed >> 24));
        }

        /// <summary>
        /// Gets precomputed visibility clusters for an aggregate fragment, or <see langword="null"/> when it uses bounds.
        /// </summary>
        public ushort[]? GetAggregateMeshVisClusters(KVObject aggregateMesh)
        {
            ArgumentNullException.ThrowIfNull(aggregateMesh);
            var flags = aggregateMesh.ContainsKey("m_objectFlags")
                ? aggregateMesh.GetEnumValue<ObjectTypeFlags>("m_objectFlags", normalize: true)
                : default;
            var count = aggregateMesh.GetInt32Property("m_nVisClusterMemberCount");
            if ((flags & ObjectTypeFlags.PrecomputedVismembers) == 0 || count == 0)
            {
                return null;
            }

            return SliceVisClusterMembership(aggregateMesh.GetInt32Property("m_nVisClusterMemberOffset"), count);
        }

        private ushort[] SliceVisClusterMembership(int offset, int count)
        {
            var membership = VisClusterMembership;
            var clusters = new ushort[offset < 0 ? 0 : Math.Clamp(membership.Count - offset, 0, count)];
            for (var i = 0; i < clusters.Length; i++)
            {
                clusters[i] = (ushort)membership[offset + i];
            }

            return clusters;
        }

        /// <summary>
        /// Gets the layer names.
        /// </summary>
        public IReadOnlyList<string> LayerNames
            => Data.ContainsKey("m_layerNames")
                ? Data.GetArray<string>("m_layerNames")
                : [];
    }
}
