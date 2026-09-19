using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.Types.Viewers;

/// <summary>
/// Non-GL content of a compiled resource: every data block as KV3 text/hex, the reconstructed
/// decompiled tabs and a browsable entity list. GL-backed viewers (models, textures, maps,
/// particles, ...) are a separate boundary.
/// </summary>
public sealed class ResourceDataViewer(IViewerContext viewerContext, IFileLoader? fileLoader = null) : IViewer
{
    private ValveResourceFormat.Resource? resource;

    /// <summary>The type of the loaded resource.</summary>
    public ResourceType ResourceType { get; private set; }

    /// <summary>The parsed resource, or null before <see cref="LoadAsync"/>; used by GL viewer routing.</summary>
    public ValveResourceFormat.Resource? Resource => resource;

    public static bool IsAccepted(ushort headerVersion) => headerVersion == ValveResourceFormat.Resource.KnownHeaderVersion;

    public Task LoadAsync(Stream? stream)
    {
        resource = new ValveResourceFormat.Resource { FileName = viewerContext.FileName };

        if (stream != null)
        {
            resource.Read(stream, verifyFileSize: false);
        }
        else
        {
            resource.Read(viewerContext.FileName);
        }

        ResourceType = resource.ResourceType;
        return Task.CompletedTask;
    }

    public ViewerContent GetContent()
    {
        var res = resource ?? throw new InvalidOperationException("Viewer was not loaded.");

        List<ViewerTab> tabs =
        [
            new("Resource", new ViewerContent.Text(BuildSummary(res), GUI.Linux.Utils.HighlightLanguage.None), Select: true),
        ];

        foreach (var block in res.Blocks)
        {
            tabs.Add(new ViewerTab(block.Type.ToString(), ResourceBlockContent.GetBlockContent(res, block)));

            if (block is ResourceIntrospectionManifest manifest)
            {
                if (manifest.ReferencedStructs.Count > 0)
                {
                    tabs.Add(new ViewerTab("Introspection: Structs", new ViewerContent.Grid(manifest.ReferencedStructs)));
                }

                if (manifest.ReferencedEnums.Count > 0)
                {
                    tabs.Add(new ViewerTab("Introspection: Enums", new ViewerContent.Grid(manifest.ReferencedEnums)));
                }
            }
        }

        if (res.ResourceType == ResourceType.EntityLump && res.DataBlock is EntityLump entityLump)
        {
            tabs.Add(new ViewerTab("Entity List", new ViewerContent.Grid(entityLump.GetEntities())));
        }

        tabs.AddRange(ResourceBlockContent.BuildReconstructedTabs(res, fileLoader));

        return new ViewerContent.Tabs(tabs);
    }

    public void Dispose()
    {
        resource?.Dispose();
        resource = null;
        GC.SuppressFinalize(this);
    }

    private static string BuildSummary(ValveResourceFormat.Resource resource)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"File: {resource.FileName}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Type: {resource.ResourceType}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Version: {resource.HeaderVersion}");

        if (resource.DataBlock is { } dataBlock)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"Data block: {dataBlock.Type}");
        }

        builder.AppendLine();
        builder.AppendLine("Blocks:");

        foreach (var block in resource.Blocks)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"  {block.Type,-16} {block.Size,10} bytes");
        }

        return builder.ToString();
    }
}
