using System.IO;
using System.Threading.Tasks;
using GUI.Types.Viewers;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.Viewers;

/// <summary>
/// Picks and loads a viewer for a file. The portable viewers (including the resource/image/nav/shader
/// viewers) are reused unchanged from GUI.Shared; GL-backed views are marked with an explicit
/// boundary tab until the viewport is ported.
/// </summary>
internal static class LinuxViewerFactory
{
    public static async Task<IViewer> CreateAndLoadAsync(string fileName)
    {
        await Task.Yield();

        var context = new LinuxViewerContext(fileName);

        uint magic;
        ushort magicResourceVersion;

        using (var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            Span<byte> magicData = stackalloc byte[6];

            if (stream.Length >= magicData.Length)
            {
                stream.ReadExactly(magicData);
            }

            magic = BitConverter.ToUInt32(magicData[..4]);
            magicResourceVersion = BitConverter.ToUInt16(magicData[4..]);
        }

        // Ordered like the Windows factory for the viewers that are portable.
        if (CompiledShaderViewer.IsAccepted(magic))
        {
            return await LoadAsync(new CompiledShaderViewer(context)).ConfigureAwait(false);
        }

        if (ClosedCaptions.IsAccepted(magic))
        {
            return await LoadAsync(new ClosedCaptions(context)).ConfigureAwait(false);
        }

        if (ToolsAssetInfo.IsAccepted(magic))
        {
            return await LoadAsync(new ToolsAssetInfo(context)).ConfigureAwait(false);
        }

        if (FlexSceneFile.IsAccepted(magic))
        {
            return await LoadAsync(new FlexSceneFile(context)).ConfigureAwait(false);
        }

        if (NavMeshDataViewer.IsAccepted(magic))
        {
            return await LoadAsync(new NavMeshGlViewer(context, fileName)).ConfigureAwait(false);
        }

        if (BinaryKeyValues3.IsAccepted(magic))
        {
            return await LoadAsync(new BinaryKeyValues3(context)).ConfigureAwait(false);
        }

        if (BinaryKeyValues2.IsAccepted(magic, fileName))
        {
            return await LoadAsync(new BinaryKeyValues2(context)).ConfigureAwait(false);
        }

        if (BinaryKeyValues1.IsAccepted(magic))
        {
            return await LoadAsync(new BinaryKeyValues1(context)).ConfigureAwait(false);
        }

        if (ResourceDataViewer.IsAccepted(magicResourceVersion))
        {
            var resourceViewer = new ResourceDataViewer(context);
            await resourceViewer.LoadAsync(stream: null).ConfigureAwait(false);

            if (resourceViewer.ResourceType == ResourceType.Mesh)
            {
                return new MeshGlViewer(context, fileName, resourceViewer);
            }

            if (resourceViewer.ResourceType == ResourceType.Particle)
            {
                return new ParticleGlViewer(context, fileName, resourceViewer);
            }

            if (resourceViewer.ResourceType == ResourceType.WorldVisibility
                && resourceViewer.Resource?.GetBlockByType(BlockType.VXVS) is VoxelVisibility { BaseClusterCount: > 0 })
            {
                return new VoxelVisibilityGlViewer(context, fileName, resourceViewer);
            }

            if (resourceViewer.ResourceType == ResourceType.Model)
            {
                return new ModelGlViewer(context, fileName, resourceViewer);
            }

            if (resourceViewer.ResourceType == ResourceType.SmartProp)
            {
                return new SmartPropGlViewer(context, fileName, resourceViewer);
            }

            if (resourceViewer.ResourceType == ResourceType.World
                && resourceViewer.Resource?.DataBlock is World)
            {
                return new WorldGlViewer(context, fileName, resourceViewer);
            }

            if (resourceViewer.ResourceType == ResourceType.Texture)
            {
                return new TextureGlViewer(context, fileName, resourceViewer);
            }

            if (resourceViewer.ResourceType == ResourceType.NmGraph
                && resourceViewer.Resource?.DataBlock is BinaryKV3)
            {
                return new GraphGlViewer(resourceViewer);
            }

            if (resourceViewer.ResourceType == ResourceType.Material
                && resourceViewer.Resource?.DataBlock is Material material)
            {
                return string.Equals(material.ShaderName, "sky.vfx", StringComparison.OrdinalIgnoreCase)
                    ? new SkyboxGlViewer(context, fileName, resourceViewer)
                    : new MaterialGlViewer(context, fileName, resourceViewer);
            }

            if (resourceViewer.ResourceType == ResourceType.NmSkeleton
                || resourceViewer.ResourceType == ResourceType.NmClip)
            {
                return new AnimationGlViewer(context, fileName, resourceViewer);
            }

            if (resourceViewer.ResourceType == ResourceType.Sound
                && resourceViewer.Resource?.DataBlock is Sound)
            {
                return new AudioViewerView(resourceViewer);
            }

            if (resourceViewer.ResourceType == ResourceType.PhysicsCollisionMesh)
            {
                return new PhysGlViewer(context, fileName, resourceViewer);
            }

            return LinuxGlBoundary.IsGlBacked(resourceViewer.ResourceType)
                ? new BoundaryViewer(resourceViewer, "Viewport", LinuxGlBoundary.Resource)
                : resourceViewer;
        }

        if (ImageViewer.IsAccepted(magic))
        {
            return await LoadAsync(new ImageViewer(context)).ConfigureAwait(false);
        }

        if (SvgVectorViewer.IsAccepted(fileName))
        {
            return await LoadAsync(new SvgVectorViewer(context)).ConfigureAwait(false);
        }

        if (GridNavFile.IsAccepted(magic))
        {
            return await LoadAsync(new GridNavFile(context)).ConfigureAwait(false);
        }

        if (SpirvBinary.IsAccepted(magic, fileName))
        {
            try
            {
                return await LoadAsync(new SpirvBinary(context)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // SPIR-V decompilation relies on a native library that may be unavailable; fall back.
            }
        }

        if (fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            return new LooseAudioViewer(fileName, isWav: true);
        }

        if (fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            return new LooseAudioViewer(fileName, isWav: false);
        }

        if (KeyValues3TextViewer.IsAccepted(magic, magicResourceVersion))
        {
            var kv3Viewer = new KeyValues3TextViewer(context);
            await kv3Viewer.LoadAsync(stream: null).ConfigureAwait(false);

            return kv3Viewer.HasAnimationGraph
                ? new BoundaryViewer(kv3Viewer, "GRAPH", LinuxGlBoundary.Graph)
                : kv3Viewer;
        }

        return await LoadAsync(new ByteViewer(context)).ConfigureAwait(false);
    }

    private static async Task<IViewer> LoadAsync(IViewer viewer)
    {
        await viewer.LoadAsync(stream: null).ConfigureAwait(false);
        return viewer;
    }
}
