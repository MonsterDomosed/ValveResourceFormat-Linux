using GUI.Linux.Types.GLViewers;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Shared scene core configured for a single mesh: loads the default lighting and adds a
/// <see cref="MeshSceneNode"/>, sharing <see cref="GLSceneViewerCore"/> for all rendering.
/// </summary>
internal sealed class MeshSceneCore : GLSceneViewerCore
{
    private readonly ValveResourceFormat.Resource resource;
    private readonly Mesh mesh;

    public MeshSceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, ValveResourceFormat.Resource resource, Mesh mesh)
        : base(context, rendererContext, host, Frustum.CreateEmpty())
    {
        this.resource = resource;
        this.mesh = mesh;
        SaveImageWithTransparentBackground = true;
    }

    public override void PreSceneLoad()
    {
        // Load renderer resources, then default lighting.
        RunPreSceneLoad();
        LoadDefaultLighting();
    }

    protected override void LoadScene()
    {
        Scene.Add(new MeshSceneNode(Scene, mesh, 0), false);
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
