using System;
using GUI.Types.GLViewers;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Linux world node renderer: reads a <c>.vwnod_c</c> and renders its geometry through the shared
/// <see cref="WorldNodeSceneCore"/>. Parsing happens on the GL thread.
/// </summary>
internal sealed class WorldNodeGlRenderer : SceneCoreGlRenderer
{
    public WorldNodeGlRenderer(string fileName)
        : base("world node", (context, rendererContext, host) => CreateCore(context, rendererContext, host, fileName))
    {
    }

    private static WorldNodeSceneCore CreateCore(LinuxSceneViewerContext context, ValveResourceFormat.Renderer.RendererContext rendererContext, IGLViewerHost host, string fileName)
    {
        var resource = new ValveResourceFormat.Resource
        {
            FileName = fileName
        };
        resource.Read(fileName);

        if (resource.DataBlock is not WorldNode worldNode)
        {
            resource.Dispose();
            throw new InvalidOperationException($"Resource is not a world node: {fileName}");
        }

        return new WorldNodeSceneCore(context, rendererContext, host, resource, worldNode, resource.ExternalReferences);
    }
}
