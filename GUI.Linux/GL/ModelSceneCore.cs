using GUI.Linux.Types.GLViewers;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Shared scene core configured for a single model: loads the default lighting and adds a
/// <see cref="ModelSceneNode"/>, sharing <see cref="GLSceneViewerCore"/> for all rendering.
/// </summary>
internal sealed class ModelSceneCore : GLSceneViewerCore
{
    private readonly ValveResourceFormat.Resource resource;
    private readonly Model model;

    public ModelSceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, ValveResourceFormat.Resource resource, Model model)
        : base(context, rendererContext, host, Frustum.CreateEmpty())
    {
        this.resource = resource;
        this.model = model;
        SaveImageWithTransparentBackground = true;
    }

    public override void PreSceneLoad()
    {
        RunPreSceneLoad();
        LoadDefaultLighting();
    }

    protected override void LoadScene()
    {
        Scene.Add(new ModelSceneNode(Scene, model), true);
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
