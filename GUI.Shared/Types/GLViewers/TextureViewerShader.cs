using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Utils;

namespace GUI.Types.GLViewers;

/// <summary>
/// Shared mapping from an OpenGL texture target to the <c>texture_decode</c> shader's
/// <c>S_TYPE_*</c> combo name. Used by the Windows and Linux texture viewers.
/// </summary>
public static class TextureViewerShader
{
    /// <summary>Returns the shader combo name for a texture target.</summary>
    public static string GetTextureTypeDefine(TextureTarget target) => target switch
    {
        TextureTarget.Texture2D => "S_TYPE_TEXTURE2D",
        TextureTarget.Texture3D => "S_TYPE_TEXTURE3D",
        TextureTarget.Texture2DArray => "S_TYPE_TEXTURE2DARRAY",
        TextureTarget.TextureCubeMap => "S_TYPE_TEXTURECUBEMAP",
        TextureTarget.TextureCubeMapArray => "S_TYPE_TEXTURECUBEMAPARRAY",
        _ => throw new UnexpectedMagicException("Unsupported texture type", (int)target, target.ToString())
    };
}
