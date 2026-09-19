using System;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace GUI.Linux.GL;

/// <summary>
/// Linux navigation-skeleton/animation renderer: parses an NmClip or an NmSkeleton KV3 block and renders
/// it through the shared <see cref="AnimationSceneCore"/>. Parsing happens on the GL thread.
/// </summary>
internal sealed class AnimationGlRenderer : SceneCoreGlRenderer
{
    public AnimationGlRenderer(string fileName)
        : base("animation", (context, rendererContext, host) => CreateCore(context, rendererContext, host, fileName))
    {
    }

    private static AnimationSceneCore CreateCore(LinuxSceneViewerContext context, ValveResourceFormat.Renderer.RendererContext rendererContext, GUI.Linux.Types.GLViewers.IGLViewerHost host, string fileName)
    {
        var resource = new ValveResourceFormat.Resource
        {
            FileName = fileName
        };
        resource.Read(fileName);

        // AnimationClip derives from BinaryKV3, so it must be checked first, or a clip would be parsed
        // as a raw skeleton block.
        if (resource.DataBlock is AnimationClip clip)
        {
            return new AnimationSceneCore(context, rendererContext, host, resource, clip);
        }

        if (resource.DataBlock is BinaryKV3 kv)
        {
            KVObject skeletonData = kv.Data;
            return new AnimationSceneCore(context, rendererContext, host, resource, skeletonData);
        }

        resource.Dispose();
        throw new InvalidOperationException($"Resource is not a navigation skeleton or clip: {fileName}");
    }
}
