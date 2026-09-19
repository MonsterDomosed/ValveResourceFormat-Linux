using GUI.Linux.Types.GLViewers;
using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Linux.GL;

/// <summary>
/// Shared scene core configured for a smart prop: resolves its referenced models through the game file
/// loader and adds a <see cref="ModelSceneNode"/> for each, sharing <see cref="GLSceneViewerCore"/> for
/// all rendering.
/// </summary>
internal sealed class SmartPropSceneCore : GLSceneViewerCore
{
    private readonly ValveResourceFormat.Resource resource;
    private readonly SmartProp smartProp;
    private readonly List<ValveResourceFormat.Resource> childResources = [];

    public SmartPropSceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, ValveResourceFormat.Resource resource, SmartProp smartProp)
        : base(context, rendererContext, host, Frustum.CreateEmpty())
    {
        this.resource = resource;
        this.smartProp = smartProp;
    }

    public override void PreSceneLoad()
    {
        RunPreSceneLoad();
        LoadDefaultLighting();
    }

    protected override void LoadScene()
    {
        var children = smartProp.Data.Root.GetArray("m_Children");

        if (children is null)
        {
            return;
        }

        foreach (var child in children)
        {
            AddChild(child);
        }
    }

    private void AddChild(KVObject child)
    {
        var className = child.GetStringProperty("_class");

        switch (className)
        {
            case "CSmartPropElement_Model":
                AddModel(child.GetStringProperty("m_sModelName"));
                break;

            case "CSmartPropElement_Group":
            case "CSmartPropElement_PickOne":
                var nested = child.GetArray("m_Children");

                if (nested is null)
                {
                    break;
                }

                foreach (var nestedChild in nested)
                {
                    if (nestedChild.GetStringProperty("_class") == "CSmartPropElement_Model")
                    {
                        AddModel(nestedChild.GetStringProperty("m_sModelName"));
                    }
                }

                break;
        }
    }

    private void AddModel(string modelName)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            return;
        }

        var modelResource = Scene.RendererContext.FileLoader.LoadFileCompiled(modelName);

        if (modelResource?.DataBlock is not Model model)
        {
            modelResource?.Dispose();
            Context.SceneLogger.LogWarning("Smart prop model could not be resolved: {Model}", modelName);
            return;
        }

        childResources.Add(modelResource);
        Scene.Add(new ModelSceneNode(Scene, model), true);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            foreach (var childResource in childResources)
            {
                childResource.Dispose();
            }

            childResources.Clear();
            resource.Dispose();
        }
    }
}
