using System;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;

namespace GUI.Linux.GL;

/// <summary>
/// Linux world-visibility renderer: parses the VXVS block and renders voxel clusters through the shared
/// <see cref="VoxelVisibilitySceneCore"/>. Parsing happens on the GL thread, inside the shared host.
/// </summary>
internal sealed class VoxelVisibilityGlRenderer : SceneCoreGlRenderer
{
    public VoxelVisibilityGlRenderer(string fileName)
        : base("voxelvisibility", (context, rendererContext, host) => CreateCore(context, rendererContext, host, fileName))
    {
    }

    private static VoxelVisibilitySceneCore CreateCore(LinuxSceneViewerContext context, ValveResourceFormat.Renderer.RendererContext rendererContext, GUI.Linux.Types.GLViewers.IGLViewerHost host, string fileName)
    {
        var resource = new ValveResourceFormat.Resource
        {
            FileName = fileName
        };
        resource.Read(fileName);

        if (resource.GetBlockByType(BlockType.VXVS) is not VoxelVisibility voxelVisibility)
        {
            resource.Dispose();
            throw new InvalidOperationException($"Resource has no voxel visibility data: {fileName}");
        }

        return new VoxelVisibilitySceneCore(context, rendererContext, host, resource, voxelVisibility);
    }
}
