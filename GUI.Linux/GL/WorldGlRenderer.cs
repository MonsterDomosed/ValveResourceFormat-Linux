using System;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Linux world/map renderer: parses a compiled world resource and renders it through the shared
/// <see cref="WorldSceneCore"/> using the existing <c>WorldLoader</c>. Parsing happens on the GL thread.
/// </summary>
internal sealed class WorldGlRenderer : SceneCoreGlRenderer
{
    public WorldGlRenderer(string fileName)
        : base("world", (context, rendererContext, host) => CreateCore(context, rendererContext, host, fileName))
    {
    }

    /// <summary>Whether scene sound events loaded, for diagnostics.</summary>
    internal bool HasSoundPlayer => (SceneCore as WorldSceneCore)?.HasSoundPlayer ?? false;

    private static WorldSceneCore CreateCore(LinuxSceneViewerContext context, ValveResourceFormat.Renderer.RendererContext rendererContext, GUI.Linux.Types.GLViewers.IGLViewerHost host, string fileName)
    {
        var resource = new ValveResourceFormat.Resource
        {
            FileName = fileName
        };
        resource.Read(fileName);

        if (resource.DataBlock is not World world)
        {
            resource.Dispose();
            throw new InvalidOperationException($"Resource is not a world: {fileName}");
        }

        ResourceExtRefList? externalReferences = resource.ExternalReferences;
        return new WorldSceneCore(context, rendererContext, host, resource, world, externalReferences);
    }
}
