using System.Diagnostics;
using GUI.Utils;
using ValveResourceFormat.Renderer;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// GL Render control with model controls (render mode, wireframe, grid).
    /// </summary>
    class GLSingleNodeViewer : GLSceneViewer, IDisposable
    {
        public GLSingleNodeViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext)
            : base(vrfGuiContext, rendererContext, Frustum.CreateEmpty())
        {
            // Exported images of a single node render the node over a transparent background.
            SaveTransparentImage = true;
        }

        protected override void AddUiControls()
        {
            AddRenderModeAndWireframeControls();
            AddBaseGridControl();

            Scene.ShowToolsMaterials = true;

            base.AddUiControls();
        }

        private void AddRenderModeAndWireframeControls()
        {
            if (this is GLMaterialViewer)
            {
                return;
            }

            Debug.Assert(UiControl != null);

            using (UiControl.BeginGroup("Render"))
            {
                AddRenderModeSelectionControl();
                AddWireframeToggleControl();
            }
        }

        public override void PreSceneLoad()
        {
            base.PreSceneLoad();
            base.LoadDefaultLighting();
        }

        protected override void LoadScene()
        {
        }

        protected override void OnPaint(float frameTime)
        {
            base.OnPaint(frameTime);
        }

        protected override void OnPicked(object? sender, PickingTexture.PickingResponse pickingResponse)
        {
            //
        }
    }
}
