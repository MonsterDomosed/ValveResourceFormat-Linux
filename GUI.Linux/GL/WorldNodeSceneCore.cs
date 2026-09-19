using GUI.Linux.Types.GLViewers;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Shared scene core configured for a single world node: loads default lighting and the node's own
/// geometry, entities and props through the existing <see cref="WorldNodeLoader"/>.
/// </summary>
internal sealed class WorldNodeSceneCore : GLSceneViewerCore
{
    private readonly ValveResourceFormat.Resource resource;
    private readonly WorldNode worldNode;
    private readonly ResourceExtRefList? externalReferences;

    public WorldNodeSceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, ValveResourceFormat.Resource resource, WorldNode worldNode, ResourceExtRefList? externalReferences)
        : base(context, rendererContext, host)
    {
        this.resource = resource;
        this.worldNode = worldNode;
        this.externalReferences = externalReferences;
    }

    public override void PreSceneLoad()
    {
        RunPreSceneLoad();
        LoadDefaultLighting();
    }

    public override void PostSceneLoad()
    {
        RunPostSceneLoad();

        Input.Camera.SetLocation(new Vector3(256));
        Input.Camera.LookAt(Vector3.Zero);
    }

    protected override void LoadScene()
    {
        ReportLoadingStatus("Loading world node…");

        var loader = new WorldNodeLoader(Scene.RendererContext, worldNode, externalReferences);
        loader.Load(Scene);
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
