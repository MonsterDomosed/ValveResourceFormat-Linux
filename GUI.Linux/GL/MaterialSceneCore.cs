using GUI.Linux.Types.GLViewers;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.SceneNodes;

namespace GUI.Linux.GL;

/// <summary>
/// Shared scene core configured for a material preview: loads default lighting and adds the material
/// preview quad, sharing <see cref="GLSceneViewerCore"/> for all rendering.
/// </summary>
internal sealed class MaterialSceneCore : GLSceneViewerCore
{
    private readonly ValveResourceFormat.Resource resource;
    private readonly MaterialInspectorSession session;

    private RenderMaterial? material;

    public MaterialSceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, ValveResourceFormat.Resource resource, MaterialInspectorSession session)
        : base(context, rendererContext, host, Frustum.CreateEmpty())
    {
        this.resource = resource;
        this.session = session;
    }

    /// <summary>The edited render material, for the self-check.</summary>
    internal RenderMaterial? Material => material;

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

        material = renderMat;
        PublishParameters(renderMat);

        var planeMesh = MeshSceneNode.CreateMaterialPreviewQuad(Scene, renderMat, new Vector2(32));
        planeMesh.Transform = Matrix4x4.CreateRotationZ(float.DegreesToRadians(90f));

        if (!renderMat.IsCs2Water)
        {
            planeMesh.Transform *= Matrix4x4.CreateRotationY(float.DegreesToRadians(90f));
        }

        Scene.Add(planeMesh, false);
    }

    /// <summary>
    /// Hands the editable parameter tables to the controls. Textures are left out: replacing one needs
    /// a file dialog and a rebuild of the material's bindings.
    /// </summary>
    private void PublishParameters(RenderMaterial renderMat)
    {
        var list = new List<MaterialParameter>(renderMat.FloatParams.Count + renderMat.IntParams.Count + renderMat.VectorParams.Count);

        foreach (var (name, value) in renderMat.FloatParams)
        {
            list.Add(new MaterialParameter(name, MaterialParameterKind.Float, value, 0, default));
        }

        foreach (var (name, value) in renderMat.IntParams)
        {
            list.Add(new MaterialParameter(name, MaterialParameterKind.Int, 0f, value, default));
        }

        foreach (var (name, value) in renderMat.VectorParams)
        {
            list.Add(new MaterialParameter(name, MaterialParameterKind.Vector, 0f, 0, value));
        }

        list.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        session.Publish([.. list]);

        Program.StdOut.WriteLine($"[gl] material parameters: {renderMat.FloatParams.Count} floats, "
            + $"{renderMat.IntParams.Count} ints, {renderMat.VectorParams.Count} vectors, {renderMat.Textures.Count} textures");
    }

    protected override void OnPrePaint(float frameTime)
    {
        base.OnPrePaint(frameTime);

        if (material is null)
        {
            return;
        }

        foreach (var change in session.ConsumeChanges())
        {
            switch (change.Kind)
            {
                case MaterialParameterKind.Float:
                    material.FloatParams[change.Name] = change.Float;
                    break;
                case MaterialParameterKind.Int:
                    material.IntParams[change.Name] = change.Int;
                    break;
                case MaterialParameterKind.Vector:
                    material.VectorParams[change.Name] = change.Vector;
                    break;
            }
        }
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
