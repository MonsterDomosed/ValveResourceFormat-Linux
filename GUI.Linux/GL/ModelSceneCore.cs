using System;
using System.Linq;
using GUI.Linux.Types.GLViewers;
using GUI.Linux.Viewers;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using static ValveResourceFormat.Renderer.PickingTexture;

namespace GUI.Linux.GL;

/// <summary>
/// Scene core configured for a single model: loads the default lighting and adds a
/// <see cref="ModelSceneNode"/>, shares <see cref="GLSceneViewerCore"/> for all rendering, and
/// bridges the animation controls to the node's <see cref="AnimationController"/> through a
/// <see cref="ModelAnimationSession"/>.
/// </summary>
internal sealed class ModelSceneCore : GLSceneViewerCore
{
    private readonly ValveResourceFormat.Resource resource;
    private readonly Model model;
    private readonly ModelAnimationSession session;
    private readonly Action<string>? openResource;

    private ModelSceneNode? modelSceneNode;
    private HitboxSetSceneNode? hitboxSetSceneNode;
    private SkeletonSceneNode? skeletonSceneNode;
    private string[] animations = [];
    private string[] hitboxSets = [];
    private string? activeHitboxSet;
    private string[] attachmentNames = [];

    public ModelSceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, ValveResourceFormat.Resource resource, Model model, ModelAnimationSession session, Action<string>? openResource = null)
        : base(context, rendererContext, host, Frustum.CreateEmpty())
    {
        this.resource = resource;
        this.model = model;
        this.session = session;
        this.openResource = openResource;
        SaveImageWithTransparentBackground = true;
    }

    /// <summary>The camera is framed on the model bounds by <see cref="ResetCamera"/> instead.</summary>
    protected override bool CenterCameraOnNodes => false;

    /// <summary>Current world bounds of the rendered model, used by the self-check.</summary>
    internal ValveResourceFormat.Utils.AABB ModelBounds => modelSceneNode?.BoundingBox ?? default;

    /// <summary>The model viewer rotates a little faster than the shared default.</summary>
    protected override float CameraSensitivityScale => 1.75f;

    /// <summary>Frames the model with a little extra margin so pose and attachments are not clipped.</summary>
    protected override float FramePadding => 1.25f;

    public override void PreSceneLoad()
    {
        RunPreSceneLoad();
        LoadDefaultLighting();
    }

    protected override void LoadScene()
    {
        modelSceneNode = new ModelSceneNode(Scene, model);
        Scene.Add(modelSceneNode, true);

        animations = [.. modelSceneNode.Animations.Keys.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];

        if (model.Skeleton.Bones.Length > 0 || model.Attachments.Count > 0)
        {
            attachmentNames = [.. model.Attachments.Keys.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];
            skeletonSceneNode = new SkeletonSceneNode(Scene, modelSceneNode.AnimationController.Pose, model.Skeleton, model.Attachments);
            Scene.Add(skeletonSceneNode, true);
        }

        if (model.HitboxSets.Count > 0)
        {
            hitboxSets = [.. model.HitboxSets.Keys.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];
            hitboxSetSceneNode = new HitboxSetSceneNode(Scene, modelSceneNode.AnimationController, model.HitboxSets);
            Scene.Add(hitboxSetSceneNode, true);
        }

        // Open on the bind pose; playback starts only when the user picks an animation.
        modelSceneNode.AnimationController.IsPaused = true;
        modelSceneNode.AnimationController.Looping = true;
        modelSceneNode.AnimationController.FrametimeMultiplier = 1f;
    }

    public override void PostSceneLoad()
    {
        base.PostSceneLoad();

        Input.OrbitModeAlways = true;
        ResetCamera();
    }

    protected override void OnPrePaint(float frameTime)
    {
        base.OnPrePaint(frameTime);

        ApplyCommands();
        FollowModelTransform();
        PublishState();
    }

    /// <summary>
    /// Keeps the debug overlays where the model is, so they follow anything that moves it. They stay
    /// root nodes because the scene only updates root nodes itself.
    /// </summary>
    private void FollowModelTransform()
    {
        if (modelSceneNode is null)
        {
            return;
        }

        if (skeletonSceneNode is not null)
        {
            skeletonSceneNode.Transform = modelSceneNode.Transform;
        }

        if (hitboxSetSceneNode is not null)
        {
            hitboxSetSceneNode.Transform = modelSceneNode.Transform;
        }
    }

    private void ApplyCommands()
    {
        if (modelSceneNode is null)
        {
            return;
        }

        var controller = modelSceneNode.AnimationController;
        var command = session.ConsumeCommands();

        if (command.ResetViewRequested)
        {
            ResetCamera();
        }

        if (command.AnimationChanged)
        {
            if (command.AnimationName is null)
            {
                modelSceneNode.SetAnimation(null);
            }
            else
            {
                modelSceneNode.SetAnimationByName(command.AnimationName);
            }
        }

        if (command.LoopingChanged)
        {
            controller.Looping = command.Looping;
        }

        if (command.SpeedChanged)
        {
            controller.FrametimeMultiplier = command.Speed;
        }

        if (command.PlayingChanged)
        {
            if (command.Playing)
            {
                if (controller.ActiveAnimation is not null && controller.ActiveClipFinished)
                {
                    controller.Time = 0f;
                }

                controller.IsPaused = false;
            }
            else
            {
                controller.IsPaused = true;
            }
        }

        if (command.SeekRequested && controller.ActiveAnimation is { CycleFrames: > 0 } animation)
        {
            controller.Frame = (int)MathF.Round(command.SeekFraction * animation.CycleFrames);
        }

        if (command.RestartRequested && controller.ActiveAnimation is not null)
        {
            controller.Time = 0f;
        }

        if (command.HitboxSetChanged && hitboxSetSceneNode is not null)
        {
            activeHitboxSet = command.HitboxSetName;
            hitboxSetSceneNode.SetHitboxSet(command.HitboxSetName);
        }

        if (skeletonSceneNode is not null)
        {
            if (command.SkeletonChanged)
            {
                skeletonSceneNode.ShowBones = command.ShowBones;
            }

            if (command.AttachmentsChanged)
            {
                skeletonSceneNode.ShowAttachments = command.ShowAttachments;

                if (command.SelectedAttachments is { } selected)
                {
                    skeletonSceneNode.SelectedAttachments.Clear();
                    skeletonSceneNode.SelectedAttachments.UnionWith(selected);
                }
            }
        }
    }

    private void PublishState()
    {
        if (modelSceneNode is null)
        {
            session.Publish(false, animations, null, false, true, 1f, 0, 0, 0f, 0f, 0f, hitboxSets, activeHitboxSet, attachmentNames, model.Skeleton.Bones.Length > 0);
            return;
        }

        var controller = modelSceneNode.AnimationController;
        var animation = controller.ActiveAnimation;
        var time = controller.Time;
        var localTime = 0f;

        if (animation is { Duration: > 0f })
        {
            var (cycle, _, _) = animation.GetCyclePosition(time);
            localTime = time - cycle * animation.Duration;
        }

        session.Publish(
            true,
            animations,
            animation?.Name,
            !controller.IsPaused,
            controller.Looping,
            controller.FrametimeMultiplier,
            controller.Frame,
            animation?.FrameCount ?? 0,
            localTime,
            animation?.Duration ?? 0f,
            animation?.Fps ?? 0f,
            hitboxSets,
            activeHitboxSet,
            attachmentNames,
            model.Skeleton.Bones.Length > 0);
    }

    protected override void OnPicked(object? sender, PickingResponse pickingResponse)
    {
        if (modelSceneNode is null)
        {
            return;
        }

        if (pickingResponse.PixelInfo.ObjectId == 0)
        {
            SelectedNodeRenderer?.SelectNode(null);
            return;
        }

        if (pickingResponse.Intent == PickingIntent.Select)
        {
            SelectedNodeRenderer?.SelectNode(Scene.Find(pickingResponse.PixelInfo.ObjectId));
            return;
        }

        if (pickingResponse.Intent == PickingIntent.Open)
        {
            var reference = modelSceneNode.GetReferenceMeshes()
                .FirstOrDefault(mesh => mesh.MeshIndex == pickingResponse.PixelInfo.MeshId);

            if (reference.MeshName is { Length: > 0 } meshName)
            {
                openResource?.Invoke(meshName + GameFileLoader.CompiledFileSuffix);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            resource.Dispose();
        }
    }
}
