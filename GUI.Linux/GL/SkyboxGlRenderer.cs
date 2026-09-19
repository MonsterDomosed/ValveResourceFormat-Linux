using System;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Linux skybox renderer: parses a compiled <c>sky.vfx</c> material and renders it as the scene's 2D
/// skybox through the shared <see cref="SkyboxSceneCore"/>. Parsing happens on the GL thread, inside
/// the shared host.
/// </summary>
internal sealed class SkyboxGlRenderer : SceneCoreGlRenderer
{
    public SkyboxGlRenderer(string fileName)
        : base("skybox", (context, rendererContext, host) => CreateCore(context, rendererContext, host, fileName))
    {
    }

    private static SkyboxSceneCore CreateCore(LinuxSceneViewerContext context, ValveResourceFormat.Renderer.RendererContext rendererContext, GUI.Linux.Types.GLViewers.IGLViewerHost host, string fileName)
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

        return new SkyboxSceneCore(context, rendererContext, host, resource);
    }
}
