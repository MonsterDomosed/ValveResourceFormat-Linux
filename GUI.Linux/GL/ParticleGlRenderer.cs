using System;
using GUI.Linux.Types.GLViewers;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Linux particle renderer: parses a compiled particle resource and renders it through the shared
/// <see cref="ParticleSceneCore"/>. Parsing happens on the GL thread, inside the shared host.
/// </summary>
internal sealed class ParticleGlRenderer : SceneCoreGlRenderer
{
    public ParticleGlRenderer(string fileName, ParticleSession session)
        : base("particle", (context, rendererContext, host) => CreateCore(context, rendererContext, host, fileName, session))
    {
    }

    private static ParticleSceneCore CreateCore(LinuxSceneViewerContext context, ValveResourceFormat.Renderer.RendererContext rendererContext, IGLViewerHost host, string fileName, ParticleSession session)
    {
        var resource = new ValveResourceFormat.Resource
        {
            FileName = fileName
        };
        resource.Read(fileName);

        if (resource.DataBlock is not ParticleSystem particleSystem)
        {
            resource.Dispose();
            throw new InvalidOperationException($"Resource is not a particle system: {fileName}");
        }

        return new ParticleSceneCore(context, rendererContext, host, resource, particleSystem, session);
    }
}
