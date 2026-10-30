using System;
using GUI.Linux.Types.GLViewers;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Linux material renderer: parses a compiled material resource and renders its preview through the
/// shared <see cref="MaterialSceneCore"/>. Parsing happens on the GL thread, inside the shared host.
/// </summary>
internal sealed class MaterialGlRenderer : SceneCoreGlRenderer
{
    public MaterialGlRenderer(string fileName, MaterialInspectorSession session)
        : base("material", (context, rendererContext, host) => CreateCore(context, rendererContext, host, fileName, session))
    {
    }

    private static MaterialSceneCore CreateCore(LinuxSceneViewerContext context, ValveResourceFormat.Renderer.RendererContext rendererContext, IGLViewerHost host, string fileName, MaterialInspectorSession session)
    {
        var resource = new ValveResourceFormat.Resource
        {
            FileName = fileName
        };
        resource.Read(fileName);

        if (resource.DataBlock is not Material)
        {
            resource.Dispose();
            throw new InvalidOperationException($"Resource is not a material: {fileName}");
        }

        return new MaterialSceneCore(context, rendererContext, host, resource, session);
    }
}
