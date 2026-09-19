using GUI.Linux.Types.GLViewers;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;

namespace GUI.Linux.GL;

/// <summary>
/// Shared scene core configured for a material preview: loads default lighting and adds the material
/// preview quad. The Linux counterpart of the Windows material viewer's core scene loading, sharing
/// <see cref="GLSceneViewerCore"/> for all rendering.
/// </summary>
internal sealed class MaterialSceneCore : GLSceneViewerCore
{
    private readonly ValveResourceFormat.Resource resource;

    public MaterialSceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, ValveResourceFormat.Resource resource)
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
        Scene.ShowToolsMaterials = true;

        var renderMat = Scene.RendererContext.MaterialLoader.LoadMaterial(resource, Scene.RenderAttributes);
        renderMat.Shader.EnsureLoaded();
        renderMat.IsOverlay = false; // render without trying to overlay on empty space

        var planeMesh = MeshSceneNode.CreateMaterialPreviewQuad(Scene, renderMat, new Vector2(32));
        planeMesh.Transform = Matrix4x4.CreateRotationZ(float.DegreesToRadians(90f));

        if (!renderMat.IsCs2Water)
        {
            planeMesh.Transform *= Matrix4x4.CreateRotationY(float.DegreesToRadians(90f));
        }

        Scene.Add(planeMesh, false);
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
