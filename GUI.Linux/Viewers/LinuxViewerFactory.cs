using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GUI.Linux.GL;
using GUI.Linux.Types.Viewers;
using GUI.Linux.Utils;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Graphs;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.Viewers;

/// <summary>
/// Picks and loads a viewer for a file. The portable viewers live in this project; GL-backed views
/// that are not ported yet fall back to an explicit boundary tab.
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

        // Ordered so more specific accepted types win over the byte-viewer fallback.
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
#pragma warning disable CA2000 // Ownership is transferred to whichever viewer is returned below
            var resourceViewer = new ResourceDataViewer(context);
#pragma warning restore CA2000
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
                && resourceViewer.Resource?.DataBlock is BinaryKV3 nmGraphData)
            {
                return CreateAg2Graph(resourceViewer, nmGraphData.Data);
            }

            if (resourceViewer.ResourceType == ResourceType.AnimationGraph
                && resourceViewer.Resource?.DataBlock is AnimGraph animGraphData)
            {
                return CreateAg1Graph(resourceViewer, animGraphData.Data);
            }

            if (resourceViewer.ResourceType == ResourceType.PulseGraphDef
                && resourceViewer.Resource?.DataBlock is BinaryKV3 pulseData)
            {
                return CreatePulseGraph(resourceViewer, pulseData.Data);
            }

            if (resourceViewer.ResourceType == ResourceType.EntityLump
                && resourceViewer.Resource?.DataBlock is EntityLump entityLumpData)
            {
                return CreateEntityIoGraph(resourceViewer, entityLumpData);
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

            if (resourceViewer.ResourceType == ResourceType.Map)
            {
                return new WorldGlViewer(context, fileName, resourceViewer, () => new MapGlRenderer(fileName), "MAP");
            }

            if (resourceViewer.ResourceType == ResourceType.WorldNode)
            {
                return new WorldGlViewer(context, fileName, resourceViewer, () => new WorldNodeGlRenderer(fileName), "WORLD NODE");
            }

            if (resourceViewer.ResourceType == ResourceType.PanoramaVectorGraphic)
            {
                return new TextureGlViewer(context, fileName, resourceViewer);
            }

            if (resourceViewer.ResourceType == ResourceType.ParticleSnapshot)
            {
                return new ParticleGlViewer(context, fileName, resourceViewer, () => new ParticleSnapshotGlRenderer(fileName), "SNAPSHOT");
            }

            if (resourceViewer.ResourceType == ResourceType.PostProcessing)
            {
                return resourceViewer.Resource?.DataBlock is PostProcessing postProcessing
                    && postProcessing.Data.ContainsKey("m_colorCorrectionVolumeData")
                    ? new TextureGlViewer(context, fileName, resourceViewer)
                    : resourceViewer;
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

    private static GraphGlViewer CreateAg2Graph(ResourceDataViewer dataViewer, KVObject data)
        => new(dataViewer, view =>
        {
            new NmGraphBuilder(data).Build(view.Document);
            return null;
        }, "AG2 ANIMATION GRAPH");

    private static GraphGlViewer CreateAg1Graph(ResourceDataViewer dataViewer, KVObject data)
        => new(dataViewer, view =>
        {
            var builder = new AnimGraph1Builder(data, LinuxGameContent.FileLoader);
            builder.Build(view.Document);
            return builder;
        }, "AG1 ANIMATION GRAPH");

    private static GraphGlViewer CreatePulseGraph(ResourceDataViewer dataViewer, KVObject data)
        => new(dataViewer, view =>
        {
            new PulseGraphBuilder(data).Build(view.Document);
            return null;
        }, "PULSE GRAPH");

    private static GraphGlViewer CreateEntityIoGraph(ResourceDataViewer dataViewer, EntityLump entityLump)
        => new(dataViewer, view =>
        {
            BuildEntityIo(view.Document, entityLump);
            return null;
        }, "ENTITY I/O");

    private static void BuildEntityIo(GraphDocument document, EntityLump entityLump)
    {
        System.Collections.Generic.List<EntityLump.Entity> entities;

        try
        {
            entities =
            [
                .. ValveResourceFormat.ResourceTypes.EntityLumpTraversal
                    .EnumerateEntities(entityLump, LinuxGameContent.FileLoader, System.Numerics.Matrix4x4.Identity)
                    .Select(static traversed => traversed.Entity),
            ];
        }
        catch (Exception e)
        {
            Log.Warn(nameof(LinuxViewerFactory), $"Failed to traverse child entity lumps: {e.Message}");
            entities = [.. entityLump.GetEntities()];
        }

        EntityIOGraphBuilder.Build(document, entities);
    }
}
