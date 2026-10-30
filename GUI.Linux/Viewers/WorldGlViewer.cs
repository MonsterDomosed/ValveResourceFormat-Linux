using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GUI.Linux.GL;
using GUI.Linux.Shell;
using GUI.Linux.Types.GLViewers;
using GUI.Linux.Types.Graphs.Core;
using GUI.Linux.Types.Viewers;
using GUI.Linux.UI;
using GUI.Linux.Utils;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Graphs;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Linux.Viewers;

internal sealed record MapGraphData(
    IReadOnlyList<EntityLump.Entity> Entities,
    IReadOnlyList<(string Name, KVObject Data)> PulseGraphs);

/// <summary>
/// Linux world/map/world-node viewer: a GL viewport with the scene inspector sidebar, plus the
/// portable resource data tabs. The renderer factory lets the shell pick the world, map or
/// world-node core.
/// </summary>
internal sealed class WorldGlViewer : IViewer
{
    private readonly ResourceDataViewer dataViewer;
    private readonly Func<IGLViewportRenderer> createRenderer;
    private readonly string tabName;
    private MapGraphData? graphData;

    private ViewportWithSidebar? control;

    public WorldGlViewer(IViewerContext context, string fileName, ResourceDataViewer dataViewer, Func<IGLViewportRenderer>? createRenderer = null, string tabName = "MAP")
    {
        this.dataViewer = dataViewer;
        this.createRenderer = createRenderer ?? (() => new WorldGlRenderer(fileName));
        this.tabName = tabName;
    }

    public async Task LoadAsync(Stream? stream)
    {
        await dataViewer.LoadAsync(stream).ConfigureAwait(false);

        var resource = dataViewer.Resource;
        if (resource is null || resource.ResourceType is not (ResourceType.Map or ResourceType.World))
        {
            return;
        }

        if (resource.ResourceType == ResourceType.Map && resource.FileName is { } mapFileName)
        {
            LinuxGameContent.EnsureMapVpkLoaded(WorldLoader.GetWorldNameFromMap(mapFileName));
        }

        graphData = LoadGraphData(resource, LinuxGameContent.FileLoader);
    }

    internal static MapGraphData LoadGraphData(Resource resource, IFileLoader fileLoader)
    {
        var entities = new List<EntityLump.Entity>();
        var pulseGraphs = new List<(string Name, KVObject Data)>();
        var loadedReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (resource.ExternalReferences is { } externalReferences)
        {
            foreach (var reference in externalReferences.ResourceRefInfoList)
            {
                if (!loadedReferences.Add(reference.Name))
                {
                    continue;
                }

                try
                {
                    using var loaded = fileLoader.LoadFileCompiled(reference.Name);
                    if (loaded?.DataBlock is EntityLump entityLump)
                    {
                        entities.AddRange(entityLump.GetEntities());
                    }
                }
                catch (Exception e)
                {
                    Log.Warn(nameof(WorldGlViewer), $"Failed to load map reference '{reference.Name}': {e.Message}");
                }
            }
        }

        var pulseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities)
        {
            if (entity.GetStringProperty("classname") != "point_pulse")
            {
                continue;
            }

            var graphName = entity.GetStringProperty("graph_def");
            if (string.IsNullOrEmpty(graphName) || !pulseNames.Add(graphName))
            {
                continue;
            }

            try
            {
                using var loaded = fileLoader.LoadFileCompiled(graphName);
                if (loaded?.DataBlock is BinaryKV3 pulse)
                {
                    pulseGraphs.Add((graphName, pulse.Data.Root));
                }
            }
            catch (Exception e)
            {
                Log.Warn(nameof(WorldGlViewer), $"Failed to load map Pulse graph '{graphName}': {e.Message}");
            }
        }

        return new MapGraphData(entities, pulseGraphs);
    }

    public ViewerContent GetContent()
    {
        List<ViewerTab> tabs =
        [
            new ViewerTab(tabName, new ViewerContent.CustomControl(() => control = new ViewportWithSidebar(createRenderer, new ViewerSidebar())), Select: true),
        ];

        if (dataViewer.GetContent() is ViewerContent.Tabs data)
        {
            foreach (var tab in data.Items)
            {
                tabs.Add(new ViewerTab(tab.Name, tab.Content));
            }
        }

        if (graphData is { } mapData)
        {
            if (mapData.Entities.Any(static entity => entity.Connections is { Count: > 0 }))
            {
                var entities = mapData.Entities;
                var entitySession = new GraphInspectorSession();
                tabs.Add(new ViewerTab(
                    "ENTITY I/O",
                    new ViewerContent.CustomControl(() => new GraphViewerControl(
                        view =>
                        {
                            EntityIOGraphBuilder.Build(view.Document, entities.ToList());
                            return null;
                        },
                        entitySession))));
            }

            foreach (var (name, definition) in mapData.PulseGraphs)
            {
                var tabName = mapData.PulseGraphs.Count > 1
                    ? $"PULSE GRAPH ({Path.GetFileNameWithoutExtension(name)})"
                    : "PULSE GRAPH";
                var pulseSession = new GraphInspectorSession();
                tabs.Add(new ViewerTab(
                    tabName,
                    new ViewerContent.CustomControl(() => new GraphViewerControl(
                        view =>
                        {
                            new PulseGraphBuilder(definition).Build(view.Document);
                            return null;
                        },
                        pulseSession))));
            }
        }

        return new ViewerContent.Tabs(tabs);
    }

    public void Dispose()
    {
        control?.Dispose();
        control = null;
        dataViewer.Dispose();
        GC.SuppressFinalize(this);
    }
}
