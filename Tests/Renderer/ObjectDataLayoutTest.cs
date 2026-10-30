using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Renderer.Shaders;

namespace Tests.Renderer
{
    /// <summary>
    /// Pins the per node and per draw entries the shaders read to the C# structs that fill them, and the
    /// node tint split those entries are packed from. Each layout is declared twice, once per side of the
    /// boundary, and a size that drifts apart only ever shows up as a draw reading another object's tint or
    /// bones, so it is worth checking without a GL context.
    /// </summary>
    [NotInParallel(nameof(ShaderBindingTest))]
    public class ObjectDataLayoutTest
    {
        private static string Preprocess(string shaderFile)
            => new ShaderParser().PreprocessShader(shaderFile, new ShaderLoader.ParsedShaderData());

        private static Match StructBody(string source, string structName)
            => Regex.Match(source, $@"struct {structName}\s*\{{(?<body>[^}}]*)\}}", RegexOptions.CultureInvariant);

        // std430 sizes of the scalar and vector types the data structs are built from
        private static readonly Dictionary<string, int> TypeSizes = new()
        {
            ["uint"] = 4,
            ["int"] = 4,
            ["float"] = 4,
            ["vec2"] = 8,
            ["uvec2"] = 8,
            ["vec3"] = 12,
            ["uvec3"] = 12,
            ["vec4"] = 16,
            ["uvec4"] = 16,
        };

        // Sums the declared members, so the number comes from the shader rather than from a constant here
        private static int GlslSizeOfStruct(string source, string structName)
        {
            var members = Regex.Matches(StructBody(source, structName).Groups["body"].Value, @"\b(\w+)\s+(\w+)\s*;").Cast<Match>().ToList();

            if (members.Count == 0)
            {
                throw new InvalidOperationException($"The shader does not declare {structName}");
            }

            return members.Sum(m => TypeSizes.TryGetValue(m.Groups[1].Value, out var size)
                ? size
                : throw new InvalidOperationException($"Unknown GLSL member type \"{m.Groups[1].Value}\" in {structName}"));
        }

        private static string[] GlslMembers(string source, string structName)
            => [.. Regex.Matches(StructBody(source, structName).Groups["body"].Value, @"\b(\w+)\s+(\w+)\s*;")
                .Select(m => m.Groups[2].Value)];

        [Test]
        public async Task ObjectDataEntryMatchesTheShaderStructSize()
        {
            // A shader every mesh draw path includes, so the struct is present unconditionally
            var source = Preprocess("default.vert.slang");

            using (Assert.Multiple())
            {
                await Assert.That(Unsafe.SizeOf<ObjectDataStandard>())
                    .IsEqualTo(GlslSizeOfStruct(source, "ObjectDataStandard"));

                await Assert.That(Unsafe.SizeOf<InstanceDataStandard>())
                    .IsEqualTo(GlslSizeOfStruct(source, "InstanceDataStandard"));
            }
        }

        [Test]
        public async Task ObjectDataEntryIsOneVec4PairWide()
        {
            // The per node refresh compares entries with a single 256 bit load, so the node entry has to be
            // exactly that wide for the compare to read nothing but the entry
            await Assert.That(Unsafe.SizeOf<ObjectDataStandard>()).IsEqualTo(32);
        }

        [Test]
        public async Task InstanceDataEntryIsOneVec4PairWide()
        {
            // Same as the node entry, so a node's entry and the draws' entries behind it stay the same width
            await Assert.That(Unsafe.SizeOf<InstanceDataStandard>()).IsEqualTo(32);
        }

        [Test]
        public async Task InstanceDataCarriesSkinningAndTheNodeItDraws()
        {
            var members = GlslMembers(Preprocess("default.vert.slang"), "InstanceDataStandard");

            using (Assert.Multiple())
            {
                await Assert.That(members).Contains("MeshBoneData");
                await Assert.That(members).Contains("Identification");
                await Assert.That(members).Contains("TintAlpha");
            }
        }

        [Test]
        public async Task ObjectDataHoldsLightingOnly()
        {
            // The lighting data is per node, so it no longer travels with the per draw entry
            var members = GlslMembers(Preprocess("default.vert.slang"), "ObjectDataStandard");

            using (Assert.Multiple())
            {
                await Assert.That(members).Contains("VisibleLPV");
                await Assert.That(members).DoesNotContain("TintAlpha");
                await Assert.That(members).DoesNotContain("MeshBoneData");
            }
        }

        [Test]
        public async Task SkinningReadsThePackedBoneDataInsteadOfAUniform()
        {
            var source = Preprocess("depth_only.vert.slang");

            using (Assert.Multiple())
            {
                // The per draw uniform is gone; bone data is a field of the object entry the draw reads
                await Assert.That(source).DoesNotContain("uAnimationData");

                await Assert.That(source).Contains("#define meshBoneData (g_instanceBuffer[gl_BaseInstance].MeshBoneData)");
                await Assert.That(source).Contains("#define bAnimated ((meshBoneData & 0x80000000u) != 0u)");
            }
        }

        [Test]
        public async Task InstanceDataCarriesTheMorphRect()
        {
            var members = GlslMembers(Preprocess("depth_only.vert.slang"), "InstanceDataStandard");

            using (Assert.Multiple())
            {
                await Assert.That(members).Contains("MorphVertexIdOffset");
                await Assert.That(members).Contains("MorphAtlasOrigin");
                await Assert.That(members).Contains("MorphAtlasStride");
            }
        }

        [Test]
        public async Task MorphingReadsTheDrawEntryInsteadOfPerDrawUniforms()
        {
            var source = Preprocess("depth_only.vert.slang");

            using (Assert.Multiple())
            {
                // Every mesh shares one composite atlas, so where a draw reads its morph from has to be the
                // draw's own entry; a per draw uniform would be a second declaration of the same thing
                await Assert.That(source).DoesNotContain("uniform int morphVertexIdOffset;");
                await Assert.That(source).DoesNotContain("uniform sampler2D morphCompositeTexture;");

                await Assert.That(source).Contains("#define bMorphed (g_instanceBuffer[gl_BaseInstance].MorphVertexIdOffset != -1)");
                await Assert.That(source).Contains("uniform sampler2D g_tCompositeMorphTextureAtlas;");
            }
        }

        [Test]
        public async Task MorphIsFetchedByTexelFromTheSharedAtlas()
        {
            // The atlas is addressed texel by texel, since a draw indexes its own rect of it by vertex id
            var source = Preprocess("depth_only.vert.slang");

            using (Assert.Multiple())
            {
                await Assert.That(source).DoesNotContain("getMorphUV");

                await Assert.That(source).Contains("ivec2 getMorphTexel(int field)");
                await Assert.That(source).Contains("texelFetch(g_tCompositeMorphTextureAtlas, getMorphTexel(MORPH_FIELD_POSITION_SPEED), 0)");
                await Assert.That(source).Contains("texelFetch(g_tCompositeMorphTextureAtlas, getMorphTexel(MORPH_FIELD_NORMAL_WRINKLE), 0)");
            }
        }

        [Test]
        public async Task MorphOffsetIsAlwaysApplied()
        {
            // The morph is a field of the entry rather than a per draw define, so the depth pass needs no
            // separate switch to apply it and cannot disagree with the material pass about doing so
            var source = Preprocess("depth_only.vert.slang");

            using (Assert.Multiple())
            {
                await Assert.That(source).DoesNotContain("D_MORPHED");
                await Assert.That(source).Contains("vec3 vertexPosition = vPOSITION + getMorphOffset();");
            }
        }

        [Test]
        public async Task PickingResolvesTheNodeThroughTheObjectEntry()
        {
            // The base instance is a draw call's own entry now, so the node it belongs to has to be read
            // out of that entry rather than taken from the base instance directly
            await Assert.That(Preprocess("picking.vert.slang"))
                .Contains("sceneObjectId = g_instanceBuffer[gl_BaseInstance].Identification + gl_InstanceID;");
        }

        [Test]
        public async Task TintIsSplitIntoColorAndAlpha()
        {
            var node = new TestSceneNode();

            node.TintAlpha = new Vector4(0.25f, 0.5f, 0.75f, 0.125f);

            using (Assert.Multiple())
            {
                await Assert.That(node.Tint).IsEqualTo(new Vector3(0.25f, 0.5f, 0.75f));
                await Assert.That(node.Alpha).IsEqualTo(0.125f);
            }
        }

        [Test]
        public async Task TintAlphaRoundTripsThroughTintAndAlpha()
        {
            var node = new TestSceneNode();
            var expected = new Vector4(0.1f, 0.2f, 0.3f, 0.4f);

            node.Tint = new Vector3(expected.X, expected.Y, expected.Z);
            node.Alpha = expected.W;

            await Assert.That(node.TintAlpha).IsEqualTo(expected);
        }

        [Test]
        public async Task TintDefaultsToOpaqueWhite()
        {
            var node = new TestSceneNode();

            using (Assert.Multiple())
            {
                await Assert.That(node.Tint).IsEqualTo(Vector3.One);
                await Assert.That(node.Alpha).IsEqualTo(1f);
            }
        }

        // The tint properties are pure value composition, so a node that never reads its scene is enough
        private sealed class TestSceneNode() : SceneNode(null!);
    }
}
