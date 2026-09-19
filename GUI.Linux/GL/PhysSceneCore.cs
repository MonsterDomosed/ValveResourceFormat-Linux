using GUI.Linux.Types.GLViewers;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Shared scene core configured for a physics collision mesh: loads default lighting, builds the
/// physics world and adds the <see cref="PhysSceneNode"/> set. The Linux counterpart of the Windows
/// model viewer's physics scene loading, sharing <see cref="GLSceneViewerCore"/> for all rendering.
/// </summary>
internal sealed class PhysSceneCore : GLSceneViewerCore
{
    private readonly ValveResourceFormat.Resource resource;
    private readonly PhysAggregateData phys;

    public PhysSceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, ValveResourceFormat.Resource resource, PhysAggregateData phys)
        : base(context, rendererContext, host, Frustum.CreateEmpty())
    {
        this.resource = resource;
        this.phys = phys;
    }

    public override void PreSceneLoad()
    {
        RunPreSceneLoad();
        LoadDefaultLighting();
    }

    protected override void LoadScene()
    {
        if (phys.Parts.Length > 0)
        {
            Scene.PhysicsWorld = new Rubikon(phys);
            Input.PlayerMovement.GridPlaneCollisionEnabled = true;
        }

        foreach (var physSceneNode in PhysSceneNode.CreatePhysSceneNodes(Scene, phys, null))
        {
            physSceneNode.Enabled = true;
            physSceneNode.IsTranslucentRenderMode = false;
            Scene.Add(physSceneNode, false);
        }

        Scene.PostProcessInfo.AddPostProcessVolume(new ScenePostProcessVolume(Scene)
        {
            HasBloom = true,
            IsMaster = true,
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            resource.Dispose();
        }
    }
}
