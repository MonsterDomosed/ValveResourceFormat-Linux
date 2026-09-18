using ValveResourceFormat;

namespace GUI.Linux.Viewers;

/// <summary>
/// Messages and type checks for views that need the GL viewport. Phase 4 ports the non-GL content;
/// these boundaries keep the missing GPU views explicit instead of silently absent.
/// </summary>
internal static class LinuxGlBoundary
{
    public const string NavMesh =
        "Rendering the nav mesh requires the GL viewport, which is not implemented on Linux yet (Phase 5).";

    public const string Graph =
        "Rendering this uncompiled AG1 animation graph is not ported to Linux yet; compiled AG2 animation graphs "
        + "render in the AG2 ANIMATION GRAPH tab.";

    public const string Resource =
        "This resource has a GPU rendered view (model/texture/map/particle). The GL viewport is not implemented "
        + "on Linux yet (Phase 5). The tabs above show the data that does not need GL.";

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
