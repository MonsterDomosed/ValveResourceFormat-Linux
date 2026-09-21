using System;
using System.Linq;
using GUI.Linux.Types.GLViewers;
using GUI.Linux.Viewers;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

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

    private ModelSceneNode? modelSceneNode;
    private string[] animations = [];

    public ModelSceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, ValveResourceFormat.Resource resource, Model model, ModelAnimationSession session)
        : base(context, rendererContext, host, Frustum.CreateEmpty())
    {
        this.resource = resource;
        this.model = model;
        this.session = session;
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
        PublishState();
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
    }

    private void PublishState()
    {
        if (modelSceneNode is null)
        {
            session.Publish(false, animations, null, false, true, 1f, 0, 0, 0f, 0f, 0f);
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
            animation?.Fps ?? 0f);
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
