using System;
using GUI.Linux.Viewers;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Linux model renderer: parses a compiled model resource and renders it through the shared
/// <see cref="ModelSceneCore"/>. Parsing happens on the GL thread, inside the shared host.
/// </summary>
internal sealed class ModelGlRenderer : SceneCoreGlRenderer
{
    private readonly ModelAnimationSession session;

    public ModelGlRenderer(string fileName, ModelAnimationSession session)
        : base("model", (context, rendererContext, host) => CreateCore(context, rendererContext, host, fileName, session))
    {
        this.session = session;
    }

    /// <summary>The animation bridge between the shell controls and the render thread.</summary>
    internal ModelAnimationSession AnimationSession => session;

    private static ModelSceneCore CreateCore(LinuxSceneViewerContext context, ValveResourceFormat.Renderer.RendererContext rendererContext, GUI.Linux.Types.GLViewers.IGLViewerHost host, string fileName, ModelAnimationSession session)
    {
        var resource = new ValveResourceFormat.Resource
        {
            FileName = fileName
        };
        resource.Read(fileName);

        if (resource.DataBlock is not Model model)
        {
            resource.Dispose();
            throw new InvalidOperationException($"Resource is not a model: {fileName}");
        }

        return new ModelSceneCore(context, rendererContext, host, resource, model, session);
    }
}
