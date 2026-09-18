using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Audio;
using GUI.Utils;
using SkiaSharp;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.SceneNodes;
using static ValveResourceFormat.Renderer.PickingTexture;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// Windows (WinForms) scene viewer. Keeps the existing WinForms hosting and sidebar, and delegates
    /// all render/update/load/input logic to the platform-neutral <see cref="GLSceneViewerCore"/> so
    /// the Linux shell can run the same implementation.
    /// </summary>
    internal abstract class GLSceneViewer : GLBaseControl, IGLViewerHost
    {
        private readonly RendererContext rendererContext;
        private readonly Frustum? cullFrustum;
        private readonly ViewerInputState hostInput = new();

        public VrfGuiContext GuiContext;

        /// <summary>Optional sound event player, created by viewers that play scene audio.</summary>
        protected SoundEventPlayer? soundPlayer;

        /// <summary>Gets whether this viewer plays scene audio, i.e. whether <see cref="InitializeSoundPlayer"/> got a device.</summary>
        public bool HasSoundPlayer => soundPlayer != null;

        /// <summary>Gets or sets whether this viewer's audio is silenced, independently of the master volume.</summary>
        public bool Muted
        {
            get => soundPlayer?.Mute ?? false;
            set
            {
                if (soundPlayer != null)
                {
                    soundPlayer.Mute = value;
                }
            }
        }

        private ComboBox? perfDisplayComboBox;

        private readonly List<RenderModes.RenderMode> renderModes = new(RenderModes.Items.Count);
        private int renderModeCurrentIndex;
        private ComboBox? renderModeComboBox;

        private readonly WindowsSceneCore core;

        public ValveResourceFormat.Renderer.Renderer Renderer => core.Renderer;
        public UserInput Input => core.Input;
        public ValveResourceFormat.Renderer.TextRenderer TextRenderer => core.TextRenderer;
        public Scene Scene => core.Scene;
        public Scene? SkyboxScene => core.SkyboxScene;
        public PickingTexture? Picker => core.Picker;
        public SelectedNodeRenderer? SelectedNodeRenderer => core.SelectedNodeRenderer;

        public bool ShowSpeed
        {
            get => core.ShowSpeed;
            set => core.ShowSpeed = value;
        }

        public bool ShowBaseGrid
        {
            get => core.ShowBaseGrid;
            set => core.ShowBaseGrid = value;
        }

        public bool ShowLightBackground
        {
            get => core.ShowLightBackground;
            set => core.ShowLightBackground = value;
        }

        public bool ShowSolidBackground
        {
            get => core.ShowSolidBackground;
            set => core.ShowSolidBackground = value;
        }

        public bool ShowStaticOctree
        {
            get => core.ShowStaticOctree;
            set => core.ShowStaticOctree = value;
        }

        public bool ShowDynamicOctree
        {
            get => core.ShowDynamicOctree;
            set => core.ShowDynamicOctree = value;
        }

        public bool ShowVisDebug
        {
            get => core.ShowVisDebug;
            set => core.ShowVisDebug = value;
        }

        public bool ShowPhysicsTraces
        {
            get => core.ShowPhysicsTraces;
            set => core.ShowPhysicsTraces = value;
        }

        public Vector2 sunAngles
        {
            get => core.SunAngles;
            set => core.SunAngles = value;
        }

        protected GLSceneViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, Frustum cullFrustum) : base(rendererContext)
        {
            GuiContext = vrfGuiContext;
            this.rendererContext = rendererContext;
            this.cullFrustum = cullFrustum;

            core = new WindowsSceneCore(this, cullFrustum);
        }

        protected GLSceneViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext) : base(rendererContext)
        {
            GuiContext = vrfGuiContext;
            this.rendererContext = rendererContext;

            core = new WindowsSceneCore(this);
        }

        public override void Dispose()
        {
            soundPlayer?.Dispose();
            soundPlayer = null;

            core.Dispose();

            base.Dispose();

            perfDisplayComboBox?.Dispose();
            perfDisplayComboBox = null;

            renderModeComboBox?.Dispose();
            renderModeComboBox = null;

#if DEBUG
            ShaderHotReload.ShadersReloaded -= OnHotReload;
#endif
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            using (UiControl.BeginGroup("Debug"))
            {
                UiControl.AddCheckBox("Lock Cull Frustum", false, (v) =>
                {
                    Renderer.LockedCullFrustum = v ? Renderer.Camera.ViewFrustum.Clone() : null;
                    Renderer.LockedCullPosition = v ? Renderer.Camera.Location : null;
                });

                UiControl.AddCheckBox("Show Static Octree", ShowStaticOctree, (v) => ShowStaticOctree = v);
                UiControl.AddCheckBox("Show Dynamic Octree", ShowDynamicOctree, (v) => ShowDynamicOctree = v);
                UiControl.AddCheckBox("Show Tool Materials", Scene.ShowToolsMaterials, (v) =>
                {
                    Scene.ShowToolsMaterials = v;

                    SkyboxScene?.ShowToolsMaterials = v;
                });

                if (this is GLWorldViewer)
                {
                    UiControl.AddCheckBox("Show Occluded Bounds", Scene.OcclusionDebugEnabled, (v) => Scene.OcclusionDebugEnabled = v);

                    if (Scene.VoxelVisibility != null)
                    {
                        UiControl.AddCheckBox("Show Vis Debug", ShowVisDebug, v => ShowVisDebug = v);
                    }
                }

                if (Scene.PhysicsWorld != null)
                {
                    UiControl.AddCheckBox("Debug Physics Traces", ShowPhysicsTraces, v => ShowPhysicsTraces = v);
                }

                UiControl.AddCheckBox("Debug Sound Sources", Renderer.ShowSoundDebug, v => Renderer.ShowSoundDebug = v);

                UiControl.AddCheckBox("Disable threaded sim", !Renderer.ParallelSimulation, v => Renderer.ParallelSimulation = !v);

                perfDisplayComboBox = UiControl.AddSelection("Debug Performance", (_, i) => core.PerfDisplayMode = i);
                perfDisplayComboBox.Items.AddRange(["Off", "Stats", "Timings", "Allocations"]);
                perfDisplayComboBox.SelectedIndex = core.PerfDisplayMode;
            }

            base.AddUiControls();
        }

        public virtual void PreSceneLoad()
        {
            core.RunPreSceneLoad();
        }

        protected void LoadDefaultLighting() => core.LoadDefaultLighting();

        public void UpdateSunAngles() => core.UpdateSunAngles();

        public virtual void PostSceneLoad()
        {
            core.RunPostSceneLoad();
        }

        protected abstract void LoadScene();

        protected abstract void OnPicked(object? sender, PickingTexture.PickingResponse pixelInfo);

        protected void ReportLoadingStatus(string status) => core.ReportLoadingStatus(status);

        public void SetEnabledLayers(HashSet<string> layers) => core.SetEnabledLayers(layers);

        /// <summary>The distinct layer names present in the loaded scene.</summary>
        public List<string> GetLayerNames() => core.GetLayerNames();

        protected void DrawLowerCornerText(ValveResourceFormat.Renderer.TextRenderer.TextMemory text, Color32 color, int lineFromBottom = 0)
            => core.DrawLowerCornerText(text, color, lineFromBottom);

        /// <summary>Applies settings that affect camera framing from the shared core.</summary>
        public void ApplyCoreSettings() => core.ApplySettingsToRenderState();

        /// <summary>When set, exported images render only the main scene over a transparent background.</summary>
        public bool SaveTransparentImage
        {
            set => core.SaveImageWithTransparentBackground = value;
        }

        protected override SkiaSharp.SKBitmap? ReadPixelsToBitmap() => core.ReadPixelsToBitmap();

        public override void OnDetachedFromRenderLoop()
        {
            core.OnDetachedFromRenderLoop();
            soundPlayer?.Suspended = true;
        }

        protected override void OnResize(int w, int h)
        {
            base.OnResize(w, h);

            core.Resize(w, h);
        }

        protected override void OnMouseWheel(int delta, Point location)
        {
            base.OnMouseWheel(delta, location);

            core.OnPointerWheel(delta);
        }

        protected override void OnMouseUp(object? sender, MouseEventArgs e)
        {
            base.OnMouseUp(sender, e);

            core.OnPointerUp(e.X, e.Y, MapMouseButtons(e.Button));
        }

        protected override void OnMouseDown(object? sender, MouseEventArgs e)
        {
            base.OnMouseDown(sender, e);

            core.OnPointerDown(e.X, e.Y, MapMouseButtons(e.Button), e.Clicks);
        }

        protected override void OnKeyDown(Keys keyData)
        {
            core.OnKeyDown(MapKey(keyData));

            base.OnKeyDown(keyData);
        }

        protected override void OnGLLoad()
        {
            base.OnGLLoad();

            Debug.Assert(MainFramebuffer != null);
            core.Load(MainFramebuffer, NumSamples);
        }

        protected override void OnUpdate(float frameTime)
        {
            BuildHostInput();
            core.Update(frameTime);
        }

        protected override void OnPaint(float frameTime)
        {
            core.Paint(frameTime);
        }

        protected override void PrewarmRenderer()
        {
            core.PrewarmRenderer();
        }

        protected override void OnBufferSwapped(double blockedMs, double framePeriodMs)
        {
            core.OnBufferSwapped(blockedMs, framePeriodMs);
        }

        /// <summary>Pushes the GLBaseControl input state into the neutral state the shared core reads.</summary>
        private void BuildHostInput()
        {
            var pressedKeys = ConsumeCurrentlyPressedKeysForUpdate();
            var mouseDelta = ConsumePendingMouseDelta();

            hostInput.Keys = MapKeys(pressedKeys);
            hostInput.Delta = new Vector2(mouseDelta.X, mouseDelta.Y);
            hostInput.MouseOverViewport = MouseOverRenderArea;
            hostInput.HasFocus = true;
        }

        private static ViewerKey MapKeys(TrackedKeys keys)
        {
            var result = ViewerKey.None;

            void Set(TrackedKeys tracked, ViewerKey key)
            {
                if ((keys & tracked) != 0)
                {
                    result |= key;
                }
            }

            Set(TrackedKeys.Shift, ViewerKey.Shift);
            Set(TrackedKeys.Alt, ViewerKey.Alt);
            Set(TrackedKeys.Control, ViewerKey.Control);
            Set(TrackedKeys.W, ViewerKey.W);
            Set(TrackedKeys.A, ViewerKey.A);
            Set(TrackedKeys.S, ViewerKey.S);
            Set(TrackedKeys.D, ViewerKey.D);
            Set(TrackedKeys.Q, ViewerKey.Q);
            Set(TrackedKeys.Z, ViewerKey.Z);
            Set(TrackedKeys.X, ViewerKey.X);
            Set(TrackedKeys.Space, ViewerKey.Space);
            Set(TrackedKeys.Escape, ViewerKey.Escape);
            Set(TrackedKeys.E, ViewerKey.E);
            Set(TrackedKeys.F, ViewerKey.F);
            Set(TrackedKeys.Slot1, ViewerKey.Slot1);
            Set(TrackedKeys.Slot2, ViewerKey.Slot2);
            Set(TrackedKeys.Slot3, ViewerKey.Slot3);
            Set(TrackedKeys.Slot4, ViewerKey.Slot4);
            Set(TrackedKeys.MouseLeft, ViewerKey.MouseLeft);
            Set(TrackedKeys.MouseRight, ViewerKey.MouseRight);

            return result;
        }

        private static ViewerKey MapMouseButtons(MouseButtons button) => button switch
        {
            MouseButtons.Left => ViewerKey.MouseLeft,
            MouseButtons.Right => ViewerKey.MouseRight,
            _ => ViewerKey.None,
        };

        private static ViewerKey MapKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            var result = key switch
            {
                Keys.W or Keys.Up => ViewerKey.W,
                Keys.S or Keys.Down => ViewerKey.S,
                Keys.A or Keys.Left => ViewerKey.A,
                Keys.D or Keys.Right => ViewerKey.D,
                Keys.Q => ViewerKey.Q,
                Keys.Z => ViewerKey.Z,
                Keys.X => ViewerKey.X,
                Keys.E => ViewerKey.E,
                Keys.F => ViewerKey.F,
                Keys.Space => ViewerKey.Space,
                Keys.Escape => ViewerKey.Escape,
                Keys.Delete => ViewerKey.Delete,
                Keys.Tab => ViewerKey.Tab,
                Keys.D1 => ViewerKey.Slot1,
                Keys.D2 => ViewerKey.Slot2,
                Keys.D3 => ViewerKey.Slot3,
                Keys.D4 => ViewerKey.Slot4,
                Keys.LShiftKey or Keys.RShiftKey or Keys.ShiftKey => ViewerKey.Shift,
                Keys.LMenu or Keys.RMenu or Keys.Menu => ViewerKey.Alt,
                Keys.LControlKey or Keys.RControlKey or Keys.ControlKey => ViewerKey.Control,
                _ => ViewerKey.None,
            };

            if (keyData.HasFlag(Keys.Shift))
            {
                result |= ViewerKey.Shift;
            }

            if (keyData.HasFlag(Keys.Alt))
            {
                result |= ViewerKey.Alt;
            }

            if (keyData.HasFlag(Keys.Control))
            {
                result |= ViewerKey.Control;
            }

            return result;
        }

        protected void AddBaseGridControl()
        {
            Debug.Assert(UiControl != null);

            using var _ = UiControl.BeginGroup("Display");

            var lightBackgroundCheckbox = UiControl.AddCheckBox("Light Background", ShowLightBackground, (v) =>
            {
                ShowLightBackground = v;
                Renderer.BaseBackground!.SetLightBackground(ShowLightBackground);
            });

            lightBackgroundCheckbox.Checked = Themer.CurrentTheme == Themer.AppTheme.Light;

            UiControl.AddCheckBox("Solid Background", ShowSolidBackground, (v) =>
            {
                ShowSolidBackground = v;
                Renderer.BaseBackground!.SetSolidBackground(ShowSolidBackground);
            });

            if (this is not GLMaterialViewer)
            {
                ShowBaseGrid = true;
                UiControl.AddCheckBox("Show Grid", ShowBaseGrid, (v) => ShowBaseGrid = v);
            }
        }

        protected void AddWireframeToggleControl()
        {
            if (this is GLMaterialViewer)
            {
                return;
            }

            Debug.Assert(UiControl != null);

            UiControl.AddCheckBox("Show Wireframe", Renderer.IsWireframe, (v) => Renderer.IsWireframe = v);
        }

        protected void AddRenderModeSelectionControl()
        {
            if (renderModeComboBox != null)
            {
                return;
            }

            Debug.Assert(UiControl != null);

            renderModeComboBox = UiControl.AddSelection("Render Mode", (_, i) =>
            {
                if (renderModeCurrentIndex < -1)
                {
                    renderModeCurrentIndex = i;
                    return;
                }

                if (i < 0)
                {
                    return;
                }

                var renderMode = renderModes[i];

                if (renderMode.IsHeader)
                {
                    renderModeComboBox!.SelectedIndex = renderModeCurrentIndex > i ? i - 1 : i + 1;
                    return;
                }

                renderModeCurrentIndex = i;
                core.ApplyRenderMode(renderMode.Name);
            }, true, true);

            SetAvailableRenderModes();
        }

        private void SetAvailableRenderModes(bool keepCurrentSelection = false)
        {
            if (renderModeComboBox != null && Picker != null)
            {
                var selectedIndex = 0;
                var currentlySelected = keepCurrentSelection ? renderModeComboBox.SelectedItem?.ToString() : null;

                renderModes.Clear();
                renderModes.AddRange(core.GetAvailableRenderModes());

                for (var i = 0; i < renderModes.Count; i++)
                {
                    if (renderModes[i].Name == currentlySelected)
                    {
                        selectedIndex = i;
                        break;
                    }
                }

                renderModeComboBox.BeginUpdate();
                renderModeComboBox.Items.Clear();

                foreach (var renderMode in renderModes)
                {
                    renderModeComboBox.Items.Add(new ThemedComboBoxItem { Text = renderMode.Name, IsHeader = renderMode.IsHeader });
                }

                renderModeCurrentIndex = -10;
                renderModeComboBox.SelectedIndex = selectedIndex;
                renderModeComboBox.EndUpdate();
            }
        }

        /// <summary>
        /// Creates <see cref="soundPlayer"/> and loads the game's sound events, wiring up the master volume from
        /// settings and the default mix group volumes. Safe to call once; failures (e.g. no audio device) are logged
        /// and leave <see cref="soundPlayer"/> null. Intended for scene viewers that want to play scene audio.
        /// </summary>
        protected void InitializeSoundPlayer()
        {
            if (soundPlayer != null)
            {
                return;
            }

            try
            {
                // The player takes ownership of the device and disposes it in its own Dispose (called from ours);
                // CA2000 cannot see ownership transfer through the constructor, so this is not actually a leak.
#pragma warning disable CA2000
                soundPlayer = new SoundEventPlayer(GuiContext, new NAudioDevice(), Scene.RendererContext.Logger);
#pragma warning restore CA2000
            }
            catch (COMException e)
            {
                // WASAPI has no usable render endpoint (no audio hardware, headless/RDP session, audio service off).
                // This is an expected environment, not a bug: run without sound rather than failing the viewer.
                Log.Warn(nameof(GLSceneViewer), $"No audio device available, sound playback disabled: {e.Message}");
                return;
            }

            soundPlayer.LoadSoundEvents();
            soundPlayer.LoadSoundscapes();

            soundPlayer.Suspended = true; // start with fade-in
            soundPlayer.Volume = Settings.Config.Volume;
            soundPlayer.MixGroupVolume["Weapons"] = 0.7f;
            soundPlayer.MixGroupVolume["Foley"] = 0.5f;
            soundPlayer.MixGroupVolume["Footsteps"] = 0.4f;
            soundPlayer.MixGroupVolume["PlayerDamage"] = 0.4f;
            soundPlayer.DefaultMixGroupVolume = 0.1f;
        }

#if DEBUG
        private void OnHotReload(object? sender, string? e)
        {
            using var lockedGl = MakeCurrent();

            if (renderModeComboBox != null)
            {
                SetAvailableRenderModes(true);
            }

            foreach (var node in Scene.AllNodes)
            {
                node.UpdateVertexArrayObjects();
            }

            if (SkyboxScene != null)
            {
                foreach (var node in SkyboxScene.AllNodes)
                {
                    node.UpdateVertexArrayObjects();
                }
            }

            GLControl?.Invalidate();
        }
#endif

        // IGLViewerHost: the shared core reads input and asks the host to present.
        ViewerInputState IGLViewerHost.Input => hostInput;

        bool IGLViewerHost.IsVisible => GLControl?.Visible ?? false;

        Framebuffer IGLViewerHost.ScreenFramebuffer => GLDefaultFramebuffer!;

        void IGLViewerHost.RequestFrame() => GLControl?.Invalidate();

        void IGLViewerHost.RequestFullscreen()
        {
            // F11 is handled by GLBaseControl.
        }

        void IGLViewerHost.SetClipboardImage(SKBitmap bitmap) => AppClipboard.SetImage(bitmap);

        private sealed class WindowsSceneCore : GLSceneViewerCore
        {
            private readonly GLSceneViewer viewer;

            public WindowsSceneCore(GLSceneViewer viewer)
                : base(viewer.GuiContext, viewer.rendererContext, viewer)
            {
                this.viewer = viewer;
            }

            public WindowsSceneCore(GLSceneViewer viewer, Frustum cullFrustum)
                : base(viewer.GuiContext, viewer.rendererContext, viewer, cullFrustum)
            {
                this.viewer = viewer;
            }

            protected override bool CenterCameraOnNodes => viewer is not GLWorldViewer;

            protected override bool IsWorldViewer => viewer is GLWorldViewer;

            protected override bool IsAnimationViewer => viewer is GLAnimationViewer;

            protected override void LoadScene() => viewer.LoadScene();

            protected override void OnPicked(object? sender, PickingResponse pixelInfo) => viewer.OnPicked(sender, pixelInfo);

            public override void PreSceneLoad() => viewer.PreSceneLoad();

            public override void PostSceneLoad() => viewer.PostSceneLoad();

            protected override void DisposeAudio() => viewer.soundPlayer?.Dispose();

            protected override void UpdateAudio(float frameTime)
            {
                if (viewer.soundPlayer != null)
                {
                    viewer.soundPlayer.Volume = Settings.Config.Volume;
                    viewer.soundPlayer.Suspended = Paused;
                }
            }

            protected override void UpdateAudioInFrame()
            {
                if (viewer.soundPlayer == null)
                {
                    return;
                }

                if (!Paused)
                {
                    using (new ProfilerScope("Update Sounds"))
                    {
                        viewer.soundPlayer.Update(Renderer.Camera);
                    }
                }
            }

            protected override void OnPostLoad()
            {
                viewer.GuiContext.GLPostLoadAction?.Invoke(viewer);
                viewer.GuiContext.GLPostLoadAction = null;
            }
        }
    }
}
