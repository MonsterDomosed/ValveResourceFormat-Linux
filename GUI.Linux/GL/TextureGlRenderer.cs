using System;
using GUI.Types.GLViewers;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Shaders;
using ValveResourceFormat.ResourceTypes;
using FramebufferTarget = OpenTK.Graphics.OpenGL.FramebufferTarget;
using OpenGL = OpenTK.Graphics.OpenGL.GL;

namespace GUI.Linux.GL;

/// <summary>
/// Linux texture viewer renderer. Decodes/upload the texture through the shared material loader and
/// draws it with the internal <c>texture_decode</c> shader over a checkerboard, with wheel zoom and
/// drag pan. It is a direct-to-screen viewer and therefore builds on <see cref="ViewportGlRenderer"/>
/// rather than the scene core. Channel/mip/cube/sprite controls are not ported yet.
/// </summary>
internal sealed class TextureGlRenderer : ViewportGlRenderer
{
    private readonly string fileName;

    private RendererContext? rendererContext;
    private ValveResourceFormat.Resource? resource;
    private RenderTexture? texture;
    private Shader? shader;
    private float originalWidth;
    private float originalHeight;

    private float textureScale = 1f;
    private float scaleOld = 1f;
    private System.Numerics.Vector2 position;
    private System.Numerics.Vector2 positionOld;
    private float scaleChangeTime = 10f;
    private bool fitted;

    public TextureGlRenderer(string fileName)
        : base("texture")
    {
        this.fileName = fileName;
    }

    protected override void InitializeRenderer(GraphicsContext context)
    {
#pragma warning disable CA2000 // Renderer resources are released in OnDispose
        rendererContext = new RendererContext(FileLoader!, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
#pragma warning restore CA2000

        resource = new ValveResourceFormat.Resource
        {
            FileName = fileName
        };
        resource.Read(fileName);

        if (resource.DataBlock is not Texture textureData)
        {
            resource.Dispose();
            resource = null;
            throw new InvalidOperationException($"Resource is not a texture: {fileName}");
        }

        texture = rendererContext.MaterialLoader.LoadTexture(resource);
        originalWidth = texture.Width;
        originalHeight = texture.Height;

        var textureType = TextureViewerShader.GetTextureTypeDefine(texture.Target);
        shader = rendererContext.ShaderLoader.LoadShader("texture_decode", (textureType, (byte)1));

        Program.StdOut.WriteLine($"[gl] texture loaded: {originalWidth}x{originalHeight}, {texture.NumMipLevels} mips, target={texture.Target}");
    }

    protected override void RenderFrame(GraphicsContext context, ViewerInputState input, float frameTime)
    {
        if (texture is null || shader is null || Host is null || rendererContext is null)
        {
            return;
        }

        if (!fitted)
        {
            FitToViewport();
        }

        HandleInput(input);
        scaleChangeTime += frameTime;

        var fbo = Host.ScreenFramebuffer;
        OpenGL.Viewport(0, 0, ViewportWidth, ViewportHeight);
        fbo.ClearColor = new OpenTK.Mathematics.Color4(1f, 1f, 1f, 1f);
        fbo.ClearMask = OpenTK.Graphics.OpenGL.ClearBufferMask.ColorBufferBit;
        fbo.BindAndClear(FramebufferTarget.Framebuffer);

        OpenGL.DepthMask(false);
        OpenGL.Disable(OpenTK.Graphics.OpenGL.EnableCap.DepthTest);

        shader.Use();
        shader.SetUniform("g_bTextureViewer", true);
        shader.SetUniform("g_bShowLightBackground", false);
        shader.SetUniform("g_vViewportSize", new System.Numerics.Vector2(ViewportWidth, ViewportHeight));
        shader.SetUniform("g_vCheckerboardTheme", new System.Numerics.Vector3(0.2f, 0.2f, 0.2f));

        var (scale, positionNow) = GetCurrentPositionAndScale();
        shader.SetUniform("g_bCapturingScreenshot", false);
        shader.SetUniform("g_vViewportPosition", positionNow);
        shader.SetUniform("g_flScale", scale);

        shader.SetTexture(0, "g_tInputTexture", texture);
        shader.SetUniform("g_vInputTextureSize", new System.Numerics.Vector4(originalWidth, originalHeight, texture.Depth, texture.NumMipLevels));
        shader.SetUniform("g_nSelectedMip", 0);
        shader.SetUniform("g_nSelectedDepth", 0);
        shader.SetUniform("g_nSelectedCubeFace", 0);
        shader.SetUniform("g_nSelectedChannels", (int)ChannelMapping.RGBA.PackedValue);
        shader.SetUniform("g_bVisualizeTiling", false);
        shader.SetUniform("g_nChannelSplitMode", 0);
        shader.SetUniform("g_nCubemapProjectionType", 0);
        shader.SetUniform("g_nDecodeFlags", 0);
        shader.SetUniform("g_nSpriteSheetMode", 0);
        shader.SetUniform("g_vSpriteFrameMinMax", new System.Numerics.Vector4(0f, 0f, 1f, 1f));

        OpenGL.BindVertexArray(rendererContext.MeshBufferCache.EmptyVAO);
        OpenGL.DrawArrays(OpenTK.Graphics.OpenGL.PrimitiveType.Triangles, 0, 3);
    }

    private void FitToViewport()
    {
        if (ViewportWidth <= 0 || ViewportHeight <= 0 || originalWidth <= 0 || originalHeight <= 0)
        {
            return;
        }

        // Fit the whole texture, never scaling above 100% on first show.
        textureScale = Math.Min(
            Math.Min(ViewportWidth / originalWidth, ViewportHeight / originalHeight),
            1f);

        if (textureScale <= 0f)
        {
            textureScale = 1f;
        }

        CenterPosition();
        fitted = true;
    }

    private void HandleInput(ViewerInputState input)
    {
        if (input.Wheel != 0f)
        {
            (scaleOld, positionOld) = GetCurrentPositionAndScale();
            scaleChangeTime = 0f;

            textureScale *= input.Wheel > 0f ? 1.25f : 1f / 1.25f;
            textureScale = Math.Clamp(textureScale, 0.05f, 50f);

            var cursor = new System.Numerics.Vector2(input.X, input.Y);
            var beforeScale = (cursor + positionOld) / scaleOld;
            position = (beforeScale * textureScale) - cursor;
        }

        if (input.Left)
        {
            position -= input.Delta;
        }

        // Keep the texture from being panned entirely out of view.
        var scaledWidth = originalWidth * textureScale;
        var scaledHeight = originalHeight * textureScale;
        var slackX = Math.Abs(scaledWidth - ViewportWidth) / 2f + ViewportWidth * 0.5f;
        var slackY = Math.Abs(scaledHeight - ViewportHeight) / 2f + ViewportHeight * 0.5f;
        position = System.Numerics.Vector2.Clamp(position, new(-slackX, -slackY), new(slackX, slackY));
    }

    private void CenterPosition()
    {
        position = -new System.Numerics.Vector2(
            ViewportWidth / 2f - (originalWidth * textureScale / 2f),
            ViewportHeight / 2f - (originalHeight * textureScale / 2f));
        positionOld = position;
        scaleOld = textureScale;
    }

    private (float Scale, System.Numerics.Vector2 Position) GetCurrentPositionAndScale()
    {
        var time = Math.Min(scaleChangeTime / 0.4f, 1.0f);
        time = 1f - MathF.Pow(1f - time, 5f); // easeOutQuint

        var interpolatedPosition = System.Numerics.Vector2.Lerp(positionOld, position, time);
        var interpolatedScale = float.Lerp(scaleOld, textureScale, time);

        return (interpolatedScale, interpolatedPosition);
    }

    protected override void OnDispose()
    {
        texture?.Delete();
        texture = null;
        shader = null;
        resource?.Dispose();
        resource = null;
        rendererContext?.Dispose();
        rendererContext = null;
    }
}
