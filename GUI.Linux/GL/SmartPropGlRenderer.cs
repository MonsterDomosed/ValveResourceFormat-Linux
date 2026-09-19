using System;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Linux smart prop renderer: parses a compiled smart prop and renders its referenced models through the
/// shared <see cref="SmartPropSceneCore"/>. Parsing happens on the GL thread.
/// </summary>
internal sealed class SmartPropGlRenderer : SceneCoreGlRenderer
{
    public SmartPropGlRenderer(string fileName)
        : base("smartprop", (context, rendererContext, host) => CreateCore(context, rendererContext, host, fileName))
    {
    }

    private static SmartPropSceneCore CreateCore(LinuxSceneViewerContext context, ValveResourceFormat.Renderer.RendererContext rendererContext, GUI.Linux.Types.GLViewers.IGLViewerHost host, string fileName)
    {
        var resource = new ValveResourceFormat.Resource
        {
            FileName = fileName
        };
        resource.Read(fileName);

        if (resource.DataBlock is not SmartProp smartProp)
        {
            resource.Dispose();
            throw new InvalidOperationException($"Resource is not a smart prop: {fileName}");
        }

        return new SmartPropSceneCore(context, rendererContext, host, resource, smartProp);
    }
}
