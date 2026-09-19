using System;
using GUI.Linux.Types.GLViewers;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Linux mesh renderer: parses a compiled mesh resource and renders it through the shared
/// <see cref="MeshSceneCore"/>. Parsing happens on the GL thread, inside the shared host.
/// </summary>
internal sealed class MeshGlRenderer : SceneCoreGlRenderer
{
    public MeshGlRenderer(string fileName)
        : base("mesh", (context, rendererContext, host) => CreateCore(context, rendererContext, host, fileName))
    {
    }

    private static MeshSceneCore CreateCore(LinuxSceneViewerContext context, ValveResourceFormat.Renderer.RendererContext rendererContext, IGLViewerHost host, string fileName)
    {
        var resource = new ValveResourceFormat.Resource
        {
            FileName = fileName
        };
        resource.Read(fileName);

        if (resource.DataBlock is not Mesh mesh)
        {
            resource.Dispose();
            throw new InvalidOperationException($"Resource is not a mesh: {fileName}");
        }

        return new MeshSceneCore(context, rendererContext, host, resource, mesh);
    }
}
