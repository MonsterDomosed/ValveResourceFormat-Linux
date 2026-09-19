namespace GUI.Linux.Types.GLViewers;

/// <summary>
/// Scene-inspector commands the sidebar sends to the render thread. One value is produced per
/// frame; the flags say which fields the render thread should apply.
/// </summary>
internal readonly record struct SceneSidebarCommand(
    bool RenderModeChanged,
    string? RenderMode,
    bool WireframeChanged,
    bool Wireframe,
    bool BaseGridChanged,
    bool BaseGrid,
    bool LightBackgroundChanged,
    bool LightBackground,
    bool SolidBackgroundChanged,
    bool SolidBackground,
    bool StaticOctreeChanged,
    bool StaticOctree,
    bool DynamicOctreeChanged,
    bool DynamicOctree,
    bool VisDebugChanged,
    bool VisDebug,
    bool PhysicsTracesChanged,
    bool PhysicsTraces,
    bool ShowSpeedChanged,
    bool ShowSpeed,
    bool PerfModeChanged,
    int PerfMode,
    bool LayersChanged,
    string[]? Layers,
    bool ResetCameraRequested,
    bool SaveCameraRequested,
    string? CameraName,
    bool ApplyCameraRequested,
    string? ApplyCameraName,
    bool DeleteCameraRequested,
    string? DeleteCameraName);

/// <summary>A consistent copy of the scene sidebar state, safe to read from the UI thread.</summary>
internal readonly record struct SceneSidebarSnapshot(
    bool Ready,
    string[] RenderModes,
    string CurrentRenderMode,
    bool Wireframe,
    bool BaseGrid,
    bool LightBackground,
    bool SolidBackground,
    bool StaticOctree,
    bool DynamicOctree,
    bool VisDebug,
    bool PhysicsTraces,
    bool ShowSpeed,
    int PerfMode,
    string[] LayerNames,
    string[] EnabledLayers,
    string[] SavedCameras);

/// <summary>
/// Thread-safe bridge between the viewer sidebar controls (UI thread) and a scene core (render
/// thread). The UI only queues commands and reads snapshots; the render thread applies the commands
/// and publishes the current state.
/// </summary>
internal sealed class SceneSidebarSession
{
    private readonly object sync = new();

    private bool ready;
    private string[] renderModes = [];
    private string currentRenderMode = string.Empty;
    private bool wireframe;
    private bool baseGrid;
    private bool lightBackground;
    private bool solidBackground;
    private bool staticOctree;
    private bool dynamicOctree;
    private bool visDebug;
    private bool physicsTraces;
    private bool showSpeed;
    private int perfMode;
    private string[] layerNames = [];
    private string[] enabledLayers = [];
    private string[] savedCameras = [];

    private bool renderModeChanged;
    private string? pendingRenderMode;
    private bool wireframeChanged;
    private bool pendingWireframe;
    private bool baseGridChanged;
    private bool pendingBaseGrid;
    private bool lightBackgroundChanged;
    private bool pendingLightBackground;
    private bool solidBackgroundChanged;
    private bool pendingSolidBackground;
    private bool staticOctreeChanged;
    private bool pendingStaticOctree;
    private bool dynamicOctreeChanged;
    private bool pendingDynamicOctree;
    private bool visDebugChanged;
    private bool pendingVisDebug;
    private bool physicsTracesChanged;
    private bool pendingPhysicsTraces;
    private bool showSpeedChanged;
    private bool pendingShowSpeed;
    private bool perfModeChanged;
    private int pendingPerfMode;
    private bool layersChanged;
    private string[]? pendingLayers;
    private bool resetCameraRequested;
    private bool saveCameraRequested;
    private string? pendingCameraName;
    private bool applyCameraRequested;
    private string? pendingApplyCamera;
    private bool deleteCameraRequested;
    private string? pendingDeleteCamera;

    public void SetRenderMode(string renderMode) => Set(ref renderModeChanged, ref pendingRenderMode, renderMode);
    public void SetWireframe(bool value) => Set(ref wireframeChanged, ref pendingWireframe, value);
    public void SetBaseGrid(bool value) => Set(ref baseGridChanged, ref pendingBaseGrid, value);
    public void SetLightBackground(bool value) => Set(ref lightBackgroundChanged, ref pendingLightBackground, value);
    public void SetSolidBackground(bool value) => Set(ref solidBackgroundChanged, ref pendingSolidBackground, value);
    public void SetStaticOctree(bool value) => Set(ref staticOctreeChanged, ref pendingStaticOctree, value);
    public void SetDynamicOctree(bool value) => Set(ref dynamicOctreeChanged, ref pendingDynamicOctree, value);
    public void SetVisDebug(bool value) => Set(ref visDebugChanged, ref pendingVisDebug, value);
    public void SetPhysicsTraces(bool value) => Set(ref physicsTracesChanged, ref pendingPhysicsTraces, value);
    public void SetShowSpeed(bool value) => Set(ref showSpeedChanged, ref pendingShowSpeed, value);
    public void SetPerfMode(int value) => Set(ref perfModeChanged, ref pendingPerfMode, value);
    public void SetLayers(string[] layers) { lock (sync) { layersChanged = true; pendingLayers = layers; } }
    public void ResetCamera() { lock (sync) { resetCameraRequested = true; } }
    public void SaveCamera(string name) { lock (sync) { saveCameraRequested = true; pendingCameraName = name; } }
    public void ApplyCamera(string name) { lock (sync) { applyCameraRequested = true; pendingApplyCamera = name; } }
    public void DeleteCamera(string name) { lock (sync) { deleteCameraRequested = true; pendingDeleteCamera = name; } }

    private void Set<T>(ref bool changed, ref T pending, T value)
    {
        lock (sync)
        {
            changed = true;
            pending = value;
        }
    }

    /// <summary>Takes the pending commands and clears them. Called on the render thread each frame.</summary>
    public SceneSidebarCommand ConsumeCommands()
    {
        lock (sync)
        {
            var command = new SceneSidebarCommand(
                renderModeChanged, pendingRenderMode,
                wireframeChanged, pendingWireframe,
                baseGridChanged, pendingBaseGrid,
                lightBackgroundChanged, pendingLightBackground,
                solidBackgroundChanged, pendingSolidBackground,
                staticOctreeChanged, pendingStaticOctree,
                dynamicOctreeChanged, pendingDynamicOctree,
                visDebugChanged, pendingVisDebug,
                physicsTracesChanged, pendingPhysicsTraces,
                showSpeedChanged, pendingShowSpeed,
                perfModeChanged, pendingPerfMode,
                layersChanged, pendingLayers,
                resetCameraRequested,
                saveCameraRequested, pendingCameraName,
                applyCameraRequested, pendingApplyCamera,
                deleteCameraRequested, pendingDeleteCamera);

            renderModeChanged = false;
            wireframeChanged = false;
            baseGridChanged = false;
            lightBackgroundChanged = false;
            solidBackgroundChanged = false;
            staticOctreeChanged = false;
            dynamicOctreeChanged = false;
            visDebugChanged = false;
            physicsTracesChanged = false;
            showSpeedChanged = false;
            perfModeChanged = false;
            layersChanged = false;
            pendingLayers = null;
            resetCameraRequested = false;
            saveCameraRequested = false;
            pendingCameraName = null;
            applyCameraRequested = false;
            pendingApplyCamera = null;
            deleteCameraRequested = false;
            pendingDeleteCamera = null;

            return command;
        }
    }

    /// <summary>Publishes the render thread's current state for the UI.</summary>
    public void Publish(
        bool isReady,
        string[] modes,
        string activeMode,
        bool isWireframe,
        bool isBaseGrid,
        bool isLightBackground,
        bool isSolidBackground,
        bool isStaticOctree,
        bool isDynamicOctree,
        bool isVisDebug,
        bool isPhysicsTraces,
        bool isShowSpeed,
        int perf,
        string[] layers,
        string[] enabled,
        string[] cameras)
    {
        lock (sync)
        {
            ready = isReady;
            renderModes = modes;
            currentRenderMode = activeMode;
            wireframe = isWireframe;
            baseGrid = isBaseGrid;
            lightBackground = isLightBackground;
            solidBackground = isSolidBackground;
            staticOctree = isStaticOctree;
            dynamicOctree = isDynamicOctree;
            visDebug = isVisDebug;
            physicsTraces = isPhysicsTraces;
            showSpeed = isShowSpeed;
            perfMode = perf;
            layerNames = layers;
            enabledLayers = enabled;
            savedCameras = cameras;
        }
    }

    public SceneSidebarSnapshot GetSnapshot()
    {
        lock (sync)
        {
            return new(
                ready, renderModes, currentRenderMode,
                wireframe, baseGrid, lightBackground, solidBackground,
                staticOctree, dynamicOctree, visDebug, physicsTraces, showSpeed,
                perfMode, layerNames, enabledLayers, savedCameras);
        }
    }
}
