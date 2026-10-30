using System.Threading.Tasks;
using ValveResourceFormat.Renderer.Materials;

namespace Tests.Renderer
{
    /// <summary>
    /// Pins which shader samplers fall back to the neutral white mask when a material does not bind them.
    /// Mask textures multiply a lighting term, so the error checker must never stand in for one: its 4x4
    /// pattern is not 1.0, and shading ambient occlusion with it paints a checkerboard of unlit patches.
    /// </summary>
    public class MaterialDefaultTextureTest
    {
        [Test]
        [Arguments("g_tAmbientOcclusion")]
        [Arguments("g_tLayer1AmbientOcclusion")]
        [Arguments("g_tTintMask")]
        [Arguments("g_tSelfIllumMask")]
        [Arguments("g_tMasks1")]
        public async Task MaskSamplersDefaultToWhite(string samplerName)
        {
            await Assert.That(MaterialLoader.DefaultsToNeutralMask(samplerName)).IsTrue();
        }

        [Test]
        [Arguments("g_tColor")]
        [Arguments("g_tNormalRoughness")]
        [Arguments("g_tGlass")]
        [Arguments("g_tMetalness")]
        public async Task ColourAndNormalSamplersKeepTheErrorChecker(string samplerName)
        {
            await Assert.That(MaterialLoader.DefaultsToNeutralMask(samplerName)).IsFalse();
        }
    }
}
