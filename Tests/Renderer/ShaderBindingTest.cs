using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Renderer.Shaders;

namespace Tests.Renderer
{
    /// <summary>
    /// Pins the SSBO bindings the shader sources declare to the slots the C# side binds them to.
    /// The two are separate declarations of one contract, and a mismatch only ever shows up as
    /// silently wrong data in a draw, so it is worth checking without a GL context.
    /// </summary>
    [NotInParallel(nameof(ShaderRegistryTest))]
    public class ShaderBindingTest
    {
        private static string Preprocess(string shaderFile)
            => new ShaderParser().PreprocessShader(shaderFile, new ShaderLoader.ParsedShaderData());

        // Matches a std430 buffer block, whatever order the layout qualifiers are written in
        private static readonly Regex SsboPattern = new(@"layout\((?<qualifiers>[^)]*)\)[^;{]*\bbuffer\s+(?<name>\w+)",
            RegexOptions.CultureInvariant);

        private static int? SsboBinding(string source, string name)
        {
            foreach (Match match in SsboPattern.Matches(source))
            {
                if (match.Groups["name"].Value != name)
                {
                    continue;
                }

                var binding = Regex.Match(match.Groups["qualifiers"].Value, @"binding\s*=\s*(\d+)", RegexOptions.CultureInvariant);

                return binding.Success ? int.Parse(binding.Groups[1].Value, CultureInfo.InvariantCulture) : null;
            }

            return null;
        }

        [Test]
        public async Task InstancingBuffersUseTheObjectAndTransformSlots()
        {
            // A shader every draw path includes, so the block is present whether or not a feature needs it
            var source = Preprocess("default.vert.slang");

            using (Assert.Multiple())
            {
                await Assert.That(SsboBinding(source, "g_objectBuffer")).IsEqualTo((int)ReservedBufferSlots.Objects);
                await Assert.That(SsboBinding(source, "g_transformBuffer")).IsEqualTo((int)ReservedBufferSlots.Transforms);
            }
        }

        [Test]
        public async Task SkinningReadsBonesFromTheSharedTransformBuffer()
        {
            var source = Preprocess("depth_only.vert.slang");

            using (Assert.Multiple())
            {
                // Bones live in the object transform buffer now, so the skinning path must not
                // bring a second bone buffer of its own along to disagree with it
                await Assert.That(SsboBinding(source, "g_boneTransformBuffer")).IsNull();

                // A bone index is resolved against the shared transform buffer, offset by the object
                await Assert.That(source).Contains("return transforms[boneIndex];");
            }
        }
    }
}
