using System;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Linux physics collision mesh renderer: parses a compiled physics resource and renders it through the
/// shared <see cref="PhysSceneCore"/>. Parsing happens on the GL thread, inside the shared host.
/// </summary>
internal sealed class PhysGlRenderer : SceneCoreGlRenderer
{
    public PhysGlRenderer(string fileName)
        : base("physics", (context, rendererContext, host) => CreateCore(context, rendererContext, host, fileName))
    {
    }

    private static PhysSceneCore CreateCore(LinuxSceneViewerContext context, ValveResourceFormat.Renderer.RendererContext rendererContext, GUI.Linux.Types.GLViewers.IGLViewerHost host, string fileName)
    {
        var resource = new ValveResourceFormat.Resource
        {
            FileName = fileName
        };
        resource.Read(fileName);

        if (resource.DataBlock is not PhysAggregateData phys)
        {
            resource.Dispose();
            throw new InvalidOperationException($"Resource is not a physics collision mesh: {fileName}");
        }

        return new PhysSceneCore(context, rendererContext, host, resource, phys);
    }
}
