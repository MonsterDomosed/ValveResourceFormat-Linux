using System;
using GUI.Types.GLViewers;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Particles;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Linux particle snapshot renderer: reads a <c>.vsnap_c</c>, builds a previewable particle system
/// from it through the existing <see cref="SnapshotParticleSystem"/> and renders it through the
/// shared <see cref="ParticleSceneCore"/>. Parsing happens on the GL thread.
/// </summary>
internal sealed class ParticleSnapshotGlRenderer : SceneCoreGlRenderer
{
    public ParticleSnapshotGlRenderer(string fileName)
        : base("snapshot", (context, rendererContext, host) => CreateCore(context, rendererContext, host, fileName))
    {
    }

    private static ParticleSceneCore CreateCore(LinuxSceneViewerContext context, ValveResourceFormat.Renderer.RendererContext rendererContext, IGLViewerHost host, string fileName)
    {
        var resource = new ValveResourceFormat.Resource
        {
            FileName = fileName
        };
        resource.Read(fileName);

        if (resource.GetBlockByType(BlockType.SNAP) is not ParticleSnapshot snapshot || !SnapshotParticleSystem.CanPreview(snapshot))
        {
            resource.Dispose();
            throw new InvalidOperationException($"Particle snapshot cannot be previewed: {fileName}");
        }

        var particleSystem = SnapshotParticleSystem.Create(snapshot);
        return new ParticleSceneCore(context, rendererContext, host, resource, particleSystem, snapshot);
    }
}
