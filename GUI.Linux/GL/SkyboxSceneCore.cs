using GUI.Linux.Types.GLViewers;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace GUI.Linux.GL;

/// <summary>
/// Shared scene core configured for a standalone skybox material preview: loads default lighting and
/// sets the renderer's 2D skybox to the material. The scene has no nodes; only the skybox is drawn.
/// </summary>
internal sealed class SkyboxSceneCore : GLSceneViewerCore
{
    private readonly ValveResourceFormat.Resource resource;

    public SkyboxSceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, ValveResourceFormat.Resource resource)
        : base(context, rendererContext, host, Frustum.CreateEmpty())
    {
        this.resource = resource;
    }

    public override void PreSceneLoad()
    {
        RunPreSceneLoad();
        LoadDefaultLighting();
    }

    protected override void LoadScene()
    {
        var material = Scene.RendererContext.MaterialLoader.LoadMaterial(resource);
        Renderer.Skybox2D = new SceneSkybox2D(material);
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
