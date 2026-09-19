using GUI.Linux.Types.GLViewers;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Shared scene core configured for a particle system: loads default lighting, adds a
/// <see cref="ParticleSceneNode"/> and frames the camera. The Linux counterpart of the Windows
/// particle viewer's scene loading, sharing <see cref="GLSceneViewerCore"/> for all rendering.
/// </summary>
internal sealed class ParticleSceneCore : GLSceneViewerCore
{
    private readonly ValveResourceFormat.Resource resource;
    private readonly ParticleSystem particleSystem;
    private readonly ParticleSnapshot? particleSnapshot;

    public ParticleSceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, ValveResourceFormat.Resource resource, ParticleSystem particleSystem, ParticleSnapshot? particleSnapshot = null)
        : base(context, rendererContext, host, Frustum.CreateEmpty())
    {
        this.resource = resource;
        this.particleSystem = particleSystem;
        this.particleSnapshot = particleSnapshot;
    }

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

    protected override void LoadScene()
    {
        Scene.LightingInfo.UseSceneBoundsForSunLightFrustum = false;

        Scene.Add(new ParticleSceneNode(Scene, particleSystem, particleSnapshot, true)
        {
            Transform = Matrix4x4.Identity
        }, true);
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
