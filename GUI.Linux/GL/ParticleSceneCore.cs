using GUI.Linux.Types.GLViewers;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Shared scene core configured for a particle system: loads default lighting, adds a
/// <see cref="ParticleSceneNode"/> and frames the camera, sharing <see cref="GLSceneViewerCore"/> for
/// all rendering.
/// </summary>
internal sealed class ParticleSceneCore : GLSceneViewerCore
{
    private readonly ValveResourceFormat.Resource resource;
    private readonly ParticleSystem particleSystem;
    private readonly ParticleSnapshot? particleSnapshot;
    private readonly ParticleSession session;

    private ParticleSceneNode? particleNode;
    private int appliedRestartCount;
    private bool appliedPlaying = true;
    private bool appliedLooping = true;
    private ParticlePlaybackMode appliedMode = ParticlePlaybackMode.NormalWithEndCap;

    public ParticleSceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, ValveResourceFormat.Resource resource, ParticleSystem particleSystem, ParticleSession session, ParticleSnapshot? particleSnapshot = null)
        : base(context, rendererContext, host, Frustum.CreateEmpty())
    {
        this.resource = resource;
        this.particleSystem = particleSystem;
        this.particleSnapshot = particleSnapshot;
        this.session = session;
    }

    /// <summary>The simulated particle node, for the self-check.</summary>
    internal ParticleSceneNode? ParticleNode => particleNode;

    public override void PreSceneLoad()
    {
        RunPreSceneLoad();
        LoadDefaultLighting();
    }

    public override void PostSceneLoad()
    {
        RunPostSceneLoad();

        if (particleSnapshot is { } snapshot)
        {
            var bounds = ValveResourceFormat.Particles.SnapshotParticleSystem.GetBounds(snapshot);
            var center = bounds.Center;
            var size = MathF.Max(bounds.Size.Length(), 64f);

            Input.Camera.SetLocation(center + new Vector3(size));
            Input.Camera.LookAt(center);
            return;
        }

        Input.Camera.SetLocation(new Vector3(200, 200, 200));
        Input.Camera.LookAt(Vector3.Zero);
    }

    /// <summary>Particles animate on their own, but a finished system can stop redrawing.</summary>
    protected override bool SceneWantsContinuousFrames => HasLiveParticles;

    protected override void LoadScene()
    {
        Scene.LightingInfo.UseSceneBoundsForSunLightFrustum = false;

        particleNode = new ParticleSceneNode(Scene, particleSystem, particleSnapshot, true)
        {
            Transform = Matrix4x4.Identity
        };

        Scene.Add(particleNode, true);
    }

    protected override void OnPrePaint(float frameTime)
    {
        base.OnPrePaint(frameTime);

        if (particleNode is null)
        {
            return;
        }

        var state = session.GetState();

        if (state.RestartCount != appliedRestartCount)
        {
            appliedRestartCount = state.RestartCount;
            particleNode.Restart();
            appliedPlaying = true;
        }

        if (state.Playing != appliedPlaying)
        {
            appliedPlaying = state.Playing;
            particleNode.IsPaused = !state.Playing;
        }

        if (state.Looping != appliedLooping)
        {
            appliedLooping = state.Looping;
            particleNode.Loop = state.Looping;
        }

        if (state.Mode != appliedMode)
        {
            appliedMode = state.Mode;
            particleNode.PlaybackMode = state.Mode;
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
