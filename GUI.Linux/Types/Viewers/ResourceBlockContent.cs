using System.Diagnostics.CodeAnalysis;
using System.IO;
using GUI.Linux.Utils;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Linux.Types.Viewers;

/// <summary>
/// Builds <see cref="ViewerContent"/> for the data blocks of a compiled resource, so the resource
/// viewers show consistent non-GL content.
/// </summary>
public static class ResourceBlockContent
{
    /// <summary>Returns the text/KV content for a block, or a byte dump when it has no textual form.</summary>
    public static ViewerContent GetBlockContent(ValveResourceFormat.Resource resource, Block block)
    {
        try
        {
            return GetTextViewContent(resource.ResourceType, block);
        }
        catch (Exception)
        {
            return new ViewerContent.HexDump(ReadBlockBytes(resource, block));
        }
    }

    /// <summary>Reads the raw bytes of a block from the resource's reader.</summary>
    public static byte[] ReadBlockBytes(ValveResourceFormat.Resource resource, Block block)
    {
        ArgumentNullException.ThrowIfNull(resource.Reader);
        resource.Reader.BaseStream.Position = block.Offset;
        return resource.Reader.ReadBytes((int)block.Size);
    }

    /// <summary>Returns text (KV3/XML/CSS/JS/etc.) for a block.</summary>
    public static ViewerContent.Text GetTextViewContent(ResourceType resourceType, Block block)
    {
        if (TryGetKvDataBlock(block, out var kvRoot, out var kvHeader))
        {
            var doc = new KVDocument(kvHeader, name: null, kvRoot);
            var (kv3Text, sourceMap) = KVSerializer.Create(KVSerializationFormat.KeyValues3Text).SerializeWithSourceMap(doc);
            return new ViewerContent.Text(kv3Text, SourceMap: sourceMap);
        }

        var text = block.ToString();
        var language = HighlightLanguage.KeyValues;

        if ((resourceType == ResourceType.PanoramaLayout || resourceType == ResourceType.PanoramaVectorGraphic) && block.Type == BlockType.DATA)
        {
            language = HighlightLanguage.XML;
        }
        else if (resourceType == ResourceType.PanoramaStyle && block.Type == BlockType.DATA)
        {
            language = HighlightLanguage.CSS;
        }
        else if ((resourceType == ResourceType.PanoramaScript || resourceType == ResourceType.PanoramaTypescript) && block.Type == BlockType.DATA)
        {
            language = HighlightLanguage.JS;
        }

        return new ViewerContent.Text(text, language);
    }

    /// <summary>Whether a block holds KeyValues that should be shown as KV3 text.</summary>
    public static bool TryGetKvDataBlock(Block block, [MaybeNullWhen(false)] out KVObject root, out KVHeader? header)
    {
        switch (block)
        {
            case BinaryKV3 kv3:
                root = kv3.Data.Root;
                header = kv3.Data.Header;
                return true;

            case KeyValuesOrNTRO kvOrNtro:
                root = kvOrNtro.Data;
                header = null;
                return true;

            case NTRO ntro:
                root = ntro.Output;
                header = null;
                return true;

            case ResourceEditInfo2 red2 when red2.Data is not null:
                root = red2.Data.Root;
                header = red2.Data.Header;
                return true;

            default:
                root = null;
                header = null;
                return false;
        }
    }

    /// <summary>
    /// Builds the reconstructed-content tabs (decompiled text) for a resource.
    /// <paramref name="fileLoader"/> may be null when no package context is available.
    /// </summary>
    public static List<ViewerTab> BuildReconstructedTabs(ValveResourceFormat.Resource resource, IFileLoader? fileLoader)
    {
        var tabs = new List<ViewerTab>();

        switch (resource.ResourceType)
        {
            case ResourceType.Sound when resource.DataBlock is Sound { Sentence: { } sentence }:
                tabs.Add(new ViewerTab("Reconstructed phonemes", new ViewerContent.Text(sentence.ToValveSentence())));
                break;

            case ResourceType.Material:
                tabs.Add(new ViewerTab("Reconstructed vmat", new ViewerContent.LazyText(new MaterialExtract(resource, fileLoader).ToValveMaterial)));
                break;

            case ResourceType.EntityLump:
                if (resource.DataBlock is EntityLump entityLump)
                {
                    tabs.Add(new ViewerTab("FGD", new ViewerContent.Text(entityLump.ToForgeGameData())));
                    tabs.Add(new ViewerTab("Entities-Text", new ViewerContent.Text(entityLump.ToEntityDumpString()), Select: true));
                }

                break;

            case ResourceType.PostProcessing:
                if (resource.DataBlock is PostProcessing postProcessingData)
                {
                    tabs.Add(new ViewerTab("Reconstructed vpost", new ViewerContent.Text(postProcessingData.ToValvePostProcessing())));
                }

                break;

            case ResourceType.Texture:
                if (!FileExtract.IsChildResource(resource))
                {
                    var textureExtract = new TextureExtract(resource);
                    tabs.Add(new ViewerTab("Reconstructed vtex", new ViewerContent.Text(textureExtract.ToValveTexture())));

                    if (textureExtract.TryGetMksData(out var _, out var mks))
                    {
                        tabs.Add(new ViewerTab("Reconstructed mks", new ViewerContent.Text(mks)));
                    }
                }

                break;

            case ResourceType.ParticleSnapshot:
                if (!FileExtract.IsChildResource(resource))
                {
                    tabs.Add(new ViewerTab("Reconstructed vsnap", new ViewerContent.Text(new SnapshotExtract(resource).ToValveSnap())));
                }

                break;
        }

        return tabs;
    }
}
