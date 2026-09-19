using GUI.Linux.Audio;
using GUI.Linux.Types.GLViewers;
using GUI.Linux.Utils;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.GL;

/// <summary>
/// Shared scene core configured for a world/map: runs the existing <see cref="WorldLoader"/> to stream
/// world geometry, entities, static props and lighting into the scene, then spawns the player entity.
/// The Linux counterpart of the Windows world viewer's scene loading, sharing
/// <see cref="GLSceneViewerCore"/> for all rendering. Entity sound events play through the portable
/// <see cref="SoundEventPlayer"/> and a PulseAudio device; the Windows-only sidebar, entity info
/// popup, saved-camera controls and streaming world-node UI are intentionally not ported.
/// </summary>
internal sealed class WorldSceneCore : GLSceneViewerCore
{
    private readonly ValveResourceFormat.Resource resource;
    private readonly ValveResourceFormat.Resource? mapResource;
    private readonly World world;
    private readonly ResourceExtRefList? externalReferences;
#pragma warning disable CA2213 // Disposed in DisposeAudio, which the base Dispose invokes
    private SoundEventPlayer? soundPlayer;
#pragma warning restore CA2213

    /// <summary>Whether scene sound events are playing, for diagnostics.</summary>
    internal bool HasSoundPlayer => soundPlayer is not null;

    public WorldSceneCore(ISceneViewerContext context, RendererContext rendererContext, IGLViewerHost host, ValveResourceFormat.Resource resource, World world, ResourceExtRefList? externalReferences, ValveResourceFormat.Resource? mapResource = null)
        : base(context, rendererContext, host)
    {
        this.resource = resource;
        this.mapResource = mapResource;
        this.world = world;
        this.externalReferences = externalReferences;
    }

    protected override void LoadScene()
    {
        ReportLoadingStatus("Loading world geometry…");

        var loadedWorld = new WorldLoader(world, Scene);
        loadedWorld.Load(externalReferences);

        if (loadedWorld.SkyboxScene != null)
        {
            Renderer.SkyboxScene = loadedWorld.SkyboxScene;
        }

        if (loadedWorld.Skybox2D != null)
        {
            Renderer.Skybox2D = loadedWorld.Skybox2D;
        }

        NavMeshSceneNode.AddNavNodesToScene(loadedWorld.NavMesh, Scene);
        CS2BombDamageSceneNode.AddBakedBombDamageToScene(loadedWorld.BombDamage, Scene);

        if (loadedWorld.SpawnCameraMatrix is { } spawnCamera)
        {
            Input.Camera.SetFromTransformMatrix(spawnCamera);
        }
        else
        {
            Input.Camera.SetLocation(new Vector3(256));
            Input.Camera.LookAt(Vector3.Zero);
        }

        Input.EntitySystem = Scene.EntitySystem;
        Scene.EntitySystem.SpawnPlayer(Input.PlayerMovement);

        CreateSoundPlayer();
    }

    private void CreateSoundPlayer()
    {
        try
        {
#pragma warning disable CA2000 // The player takes ownership of the device and disposes it
            var device = new PulseAudioDevice();
#pragma warning restore CA2000

            if (!device.Available)
            {
                Log.Info(nameof(WorldSceneCore), $"Scene sound disabled: {device.ErrorMessage}");
                device.Dispose();
                return;
            }

            soundPlayer = new SoundEventPlayer(LinuxGameContent.FileLoader, device);
            soundPlayer.LoadSoundEvents();
            soundPlayer.LoadSoundscapes();
            soundPlayer.Suspended = true;
            soundPlayer.Volume = Settings.Config.Volume;
        }
        catch (Exception e)
        {
            Log.Warn(nameof(WorldSceneCore), $"Failed to start scene sound: {e.Message}");
            soundPlayer?.Dispose();
            soundPlayer = null;
        }
    }

    protected override void UpdateAudio(float frameTime)
    {
        if (soundPlayer is not null)
        {
            soundPlayer.Volume = Settings.Config.Volume;
            soundPlayer.Suspended = Paused;
        }
    }

    protected override void UpdateAudioInFrame()
    {
        if (soundPlayer is not null && !Paused)
        {
            soundPlayer.Update(Renderer.Camera);
        }
    }

    protected override void DisposeAudio()
    {
        soundPlayer?.Dispose();
        soundPlayer = null;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            resource.Dispose();
            mapResource?.Dispose();
        }
    }
}
