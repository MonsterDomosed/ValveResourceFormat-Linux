using GUI.Linux.Types.GLViewers;
using ValveKeyValue;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace GUI.Linux.GL;

/// <summary>
/// Shared scene core configured for a navigation skeleton and, when a clip is supplied, its animation.
/// Loads the default lighting and adds a <see cref="SkeletonSceneNode"/>, sharing
/// <see cref="GLSceneViewerCore"/> for all rendering.
/// </summary>
internal sealed class AnimationSceneCore : GLSceneViewerCore
{
    private readonly ValveResourceFormat.Resource resource;
    private KVObject? skeletonData;
    private readonly AnimationClip? animationClip;

    private AnimationController? animationController;
    private SkeletonSceneNode? skeletonSceneNode;

    public AnimationSceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, ValveResourceFormat.Resource resource, KVObject skeletonData)
        : base(context, rendererContext, host, Frustum.CreateEmpty())
    {
        this.resource = resource;
        this.skeletonData = skeletonData;
    }

    public AnimationSceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, ValveResourceFormat.Resource resource, AnimationClip clip)
        : base(context, rendererContext, host, Frustum.CreateEmpty())
    {
        this.resource = resource;
        animationClip = clip;
    }

    public override void PreSceneLoad()
    {
        RunPreSceneLoad();
        LoadDefaultLighting();
    }

    protected override void LoadScene()
    {
        // A clip resolves its skeleton through the game file loader.
        if (skeletonData is null && animationClip is not null)
        {
            using var skeletonResource = Scene.RendererContext.FileLoader.LoadFileCompiled(animationClip.SkeletonName);

            if (skeletonResource?.DataBlock is not ValveResourceFormat.ResourceTypes.BinaryKV3 binaryKV3)
            {
                throw new InvalidOperationException($"Could not resolve skeleton '{animationClip.SkeletonName}' for clip '{resource.FileName}'");
            }

            skeletonData = binaryKV3.Data;
        }

        var skeleton = Skeleton.FromSkeletonData(skeletonData!);
        animationController = new AnimationController(skeleton, []);

        if (animationClip is not null)
        {
            animationController.SetAnimation(new ClipAnimation(animationClip));
        }

        skeletonSceneNode = new SkeletonSceneNode(Scene, animationController.Pose, skeleton)
        {
            ShowBones = true,
        };

        Scene.Add(skeletonSceneNode, true);

        skeletonSceneNode.Update(new Scene.UpdateContext
        {
            TextRenderer = TextRenderer,
            Timestep = 0f,
            Camera = Renderer.Camera,
        });
    }

    protected override void OnPrePaint(float frameTime) => animationController?.Update(frameTime);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            resource.Dispose();
        }
    }
}
