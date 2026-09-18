using ValveResourceFormat;

namespace GUI.Linux.Viewers;

/// <summary>
/// Messages and type checks for resource types whose GPU-backed preview is not available in this
/// shell. These boundaries keep the missing views explicit instead of silently absent.
/// </summary>
internal static class LinuxGlBoundary
{
    public const string Graph =
        "Uncompiled AG1 animation graphs are shown as KV3 text on Linux; open the compiled .vanmgrph_c to "
        + "render the graph.";

    public const string Resource =
        "This resource has a GPU rendered preview that is not supported on Linux yet. The tabs above show "
        + "the data that does not require a viewport.";

    public static bool IsGlBacked(ResourceType type) => type switch
    {
        ResourceType.Texture
            or ResourceType.PanoramaVectorGraphic
            or ResourceType.Particle
            or ResourceType.ParticleSnapshot
            or ResourceType.Map
            or ResourceType.World
            or ResourceType.WorldNode
            or ResourceType.Model
            or ResourceType.Mesh
            or ResourceType.SmartProp
            or ResourceType.AnimationGraph
            or ResourceType.NmClip
            or ResourceType.NmSkeleton
            or ResourceType.NmGraph
            or ResourceType.PulseGraphDef
            or ResourceType.EntityLump
            or ResourceType.Material
            or ResourceType.PhysicsCollisionMesh
            or ResourceType.WorldVisibility
            or ResourceType.PostProcessing
            or ResourceType.VData => true,
        _ => false,
    };
}
