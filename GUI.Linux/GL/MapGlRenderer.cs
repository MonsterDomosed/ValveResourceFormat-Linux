using System;
using GUI.Types.GLViewers;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Linux map renderer: reads a <c>.vmap_c</c>, resolves its world through the existing
/// <see cref="WorldLoader.GetWorldNameFromMap"/>, makes sure the map VPK is searchable and renders
/// the world through the shared <see cref="WorldSceneCore"/>. Parsing happens on the GL thread.
/// </summary>
internal sealed class MapGlRenderer : SceneCoreGlRenderer
{
    public MapGlRenderer(string fileName)
        : base("map", (context, rendererContext, host) => CreateCore(context, rendererContext, host, fileName))
    {
    }

    private static WorldSceneCore CreateCore(LinuxSceneViewerContext context, ValveResourceFormat.Renderer.RendererContext rendererContext, IGLViewerHost host, string fileName)
    {
        var mapResource = new ValveResourceFormat.Resource
        {
            FileName = fileName
        };
        mapResource.Read(fileName);

        var worldPath = WorldLoader.GetWorldNameFromMap(fileName);

        // The map's own VPK holds its world, entities and props; make it searchable before loading.
        LinuxGameContent.EnsureMapVpkLoaded(worldPath);

        var worldResource = LinuxGameContent.FileLoader.LoadFileCompiled(worldPath);

        if (worldResource?.DataBlock is not World world)
        {
            worldResource?.Dispose();
            mapResource.Dispose();
            throw new InvalidOperationException($"Failed to resolve world '{worldPath}' for map '{fileName}'.");
        }

        return new WorldSceneCore(context, rendererContext, host, worldResource, world, mapResource.ExternalReferences, mapResource);
    }
}
