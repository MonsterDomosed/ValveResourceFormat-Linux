using GUI.Linux.Types.GLViewers;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;

namespace GUI.Linux.GL;

/// <summary>
/// Shared scene core configured for world-visibility voxel clusters: adds a
/// <see cref="VisibilitySceneNode"/>, sharing <see cref="GLSceneViewerCore"/> for all rendering.
/// </summary>
internal sealed class VoxelVisibilitySceneCore : GLSceneViewerCore
{
    private readonly ValveResourceFormat.Resource resource;
    private readonly VoxelVisibility voxelVisibility;

    public VoxelVisibilitySceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, ValveResourceFormat.Resource resource, VoxelVisibility voxelVisibility)
        : base(context, rendererContext, host)
    {
        this.resource = resource;
        this.voxelVisibility = voxelVisibility;
    }

    protected override void LoadScene()
    {
        var sceneNode = new VisibilitySceneNode(Scene, voxelVisibility)
        {
            LayerName = "Visibility clusters",
        };

        Scene.Add(sceneNode, false);
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
