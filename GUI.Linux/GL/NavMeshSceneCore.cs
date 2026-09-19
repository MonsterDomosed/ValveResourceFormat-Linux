using GUI.Linux.Types.GLViewers;
using ValveResourceFormat.NavMesh;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;

namespace GUI.Linux.GL;

/// <summary>
/// Scene core configured for a navigation mesh. The rendering itself lives entirely in the shared
/// <see cref="GLSceneViewerCore"/>; this only supplies the viewer-specific scene contents.
/// </summary>
internal sealed class NavMeshSceneCore : GLSceneViewerCore
{
    private readonly NavMeshFile navMeshFile;

    public NavMeshSceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, NavMeshFile navMeshFile)
        : base(context, rendererContext, host)
    {
        this.navMeshFile = navMeshFile;
    }

    protected override void LoadScene() => NavMeshSceneNode.AddNavNodesToScene(navMeshFile, Scene);
}
