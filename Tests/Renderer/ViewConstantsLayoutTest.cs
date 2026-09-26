using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Renderer.Shaders;

namespace Tests.Renderer
{
    /// <summary>
    /// Pins the view constants block the shaders read to the C# struct that fills it. The two declare the
    /// same set twice, and the struct is marshalled into a reused buffer, so a field added, dropped,
    /// reordered or retyped on one side alone does not fail anything: it just shifts or misreads every
    /// member after it, and every shader that includes the block then draws with someone else's fog, depth
    /// or camera.
    /// </summary>
    [NotInParallel(nameof(ShaderBindingTest))]
    public class ViewConstantsLayoutTest
    {
        private static string Preprocess(string shaderFile)
            => new ShaderParser().PreprocessShader(shaderFile, new ShaderLoader.ParsedShaderData());

        // A member declaration of a uniform block, with the array suffix the shadow cascades carry
        private static readonly Regex MemberPattern = new(
            @"\b(?<type>\w+)\s+(?<name>\w+)\s*(?<array>\[[^\]]*\])?\s*;", RegexOptions.CultureInvariant);

        /// <summary>One member of the block: its type, its element count, and where std140 puts it.</summary>
        private sealed record Member(string Type, string Name, int Count, int Offset, int Size);

        /// <summary>
        /// The members of a uniform block, in declaration order, each with the offset std140 gives it. The
        /// two rules that matter here are that a vector is aligned the way a matrix of the same component
        /// count is, and that a block lays its members out the way it lays a matrix's columns out: each one
        /// starts on a 16 byte boundary.
        /// </summary>
        private static List<Member> GlslMembers(string source, string blockName)
        {
            var body = Regex.Match(source, $@"uniform\s+{blockName}\s*\{{(?<body>[^}}]*)\}}", RegexOptions.CultureInvariant)
                .Groups["body"].Value;

            if (body.Length == 0)
            {
                throw new InvalidOperationException($"The shader has no {blockName} uniform block");
            }

            var members = new List<Member>();
            var offset = 0;

            foreach (Match match in MemberPattern.Matches(body))
            {
                var type = match.Groups["type"].Value;
                var elementSize = SizeOf(type);
                var count = 1;

                offset = Align(offset, AlignmentOf(type));

                if (int.TryParse(match.Groups["array"].Value.Trim('[', ']'), out var length))
                {
                    count = length;

                    // Array elements are spaced like a matrix's columns, so a 4 byte element still takes 16
                    elementSize = Align(elementSize, 16);
                }

                var size = elementSize * count;

                members.Add(new Member(type, match.Groups["name"].Value, count, offset, size));
                offset += size;
            }

            return members;
        }

        private static int Align(int offset, int alignment) => (offset + alignment - 1) / alignment * alignment;

        private static int AlignmentOf(string type) => type switch
        {
            "bool" or "int" or "uint" or "float" => 4,
            "vec2" or "bvec2" or "ivec2" or "uvec2" => 8,
            _ => 16, // every vec3, vec4 and matNxN
        };

        private static int SizeOf(string type) => type switch
        {
            "bool" or "int" or "uint" or "float" => 4,
            "vec2" or "bvec2" or "ivec2" or "uvec2" => 8,
            "vec3" or "bvec3" or "ivec3" or "uvec3" => 12,
            "vec4" or "bvec4" or "ivec4" or "uvec4" => 16,
            "mat2" or "mat2x2" => 32,
            "mat3" or "mat3x3" => 48,
            "mat4" or "mat4x4" => 64,
            _ => throw new InvalidOperationException($"Unknown GLSL member type \"{type}\""),
        };

        /// <summary>
        /// The C# fields each GLSL member fills, in the order the buffer holds them. A bvec3 is three bool
        /// fields and the shadow cascades are the one array, which the struct declares as a second matrix
        /// field; everything else is a member and a field of the same name, spelled the C# way.
        /// </summary>
        private static readonly Dictionary<string, string[]> StructFieldsPerMember = new(StringComparer.Ordinal)
        {
            ["g_matWorldToProjection"] = ["WorldToProjection"],
            ["g_matProjectionToWorld"] = ["ProjectionToWorld"],
            ["g_matWorldToView"] = ["WorldToView"],
            ["g_matViewToProjection"] = ["ViewToProjection"],
            ["g_vInvProjRow3"] = ["InvProjRow3"],
            ["g_vInvViewportSize"] = ["InvViewportSize"],
            ["g_vViewportSize"] = ["ViewportSize"],
            ["g_vCameraPositionWs"] = ["CameraPosition"],
            ["g_flViewportMinZ"] = ["ViewportMinZ"],
            ["g_vCameraDirWs"] = ["CameraDirWs"],
            ["g_flViewportMaxZ"] = ["ViewportMaxZ"],
            ["g_vCameraUpDirWs"] = ["CameraUpDirWs"],
            ["g_flTime"] = ["Time"],
            ["g_matWorldToShadow"] = ["WorldToShadow", "WorldToShadowCascade1"],
            ["_viewPadding1"] = ["_ViewPadding1"],
            ["g_bIsSkybox"] = ["IsSkybox"],
            ["g_flSunShadowBias"] = ["SunLightShadowBias"],
            ["g_bSunShadowsEnabled"] = ["SunShadowsEnabled"],
            ["g_bFogTypeEnabled"] = ["VolumetricFogActive", "GradientFogActive", "CubeFogActive"],
            ["g_iRenderMode"] = ["RenderMode"],
            ["g_vGradientFogBiasAndScale"] = ["GradientFogBiasAndScale"],
            ["g_vGradientFogColor_Opacity"] = ["GradientFogColor_Opacity"],
            ["m_vGradientFogExponents"] = ["GradientFogExponents"],
            ["g_vGradientFogCullingParams"] = ["GradientFogCullingParams"],
            ["g_vCubeFog_Offset_Scale_Bias_Exponent"] = ["CubeFog_Offset_Scale_Bias_Exponent"],
            ["g_vCubeFog_Height_Offset_Scale_Exponent_Log2Mip"] = ["CubeFog_Height_Offset_Scale_Exponent_Log2Mip"],
            ["g_matvCubeFogSkyWsToOs"] = ["CubeFogSkyWsToOs"],
            ["g_vCubeFogCullingParams_ExposureBias_MaxOpacity"] = ["CubeFogCullingParams_ExposureBias_MaxOpacity"],
            ["g_matPrevWorldToProjection"] = ["WorldToProjectionPrev"],
            ["g_vEnvWetness"] = ["EnvWetness"],
            ["g_vEnvWetnessRipple"] = ["EnvWetnessRipple"],
        };

        /// <summary>
        /// The C# type each component of a GLSL member is read into. A vector is one field, but a vector of
        /// booleans is one field per component, which is why the widths have to line up member by member.
        /// </summary>
        private static readonly Dictionary<string, Type[]> CsharpTypesPerComponent = new(StringComparer.Ordinal)
        {
            ["bool"] = [typeof(bool)],
            ["int"] = [typeof(int)],
            ["uint"] = [typeof(uint)],
            ["float"] = [typeof(float)],
            ["vec2"] = [typeof(Vector2)],
            ["vec3"] = [typeof(Vector3)],
            ["vec4"] = [typeof(Vector4)],
            ["bvec3"] = [typeof(bool), typeof(bool), typeof(bool)],
            ["mat4"] = [typeof(Matrix4x4)],
        };

        private static readonly FieldInfo[] StructFieldsInfo =
            typeof(ViewConstants).GetFields(BindingFlags.Public | BindingFlags.Instance);

        /// <summary>Every way the two declarations can disagree, so a test can read the whole list.</summary>
        private static List<string> FindMismatches(string shaderSource, string[]? structFields = null)
        {
            structFields ??= [.. StructFieldsInfo.Select(f => f.Name)];

            var problems = new List<string>();

            // What the shader puts in the block, flattened to one entry per struct field
            var fromShader = new List<(string Member, string Field, Type FieldType, int Offset)>();
            var lastMemberEnd = 0;

            foreach (var member in GlslMembers(shaderSource, "ViewConstants"))
            {
                lastMemberEnd = Math.Max(lastMemberEnd, member.Offset + member.Size);

                if (!StructFieldsPerMember.TryGetValue(member.Name, out var fields)
                    || !CsharpTypesPerComponent.TryGetValue(member.Type, out var csharpTypes))
                {
                    problems.Add($"The shader declares {member.Name} of a type nothing in the struct matches");
                    continue;
                }

                var expectedFields = csharpTypes.Length * member.Count;

                if (fields.Length != expectedFields)
                {
                    problems.Add($"{member.Name} is {expectedFields} fields wide in the shader and {fields.Length} in the mapping");
                    continue;
                }

                var offset = member.Offset;

                for (var i = 0; i < fields.Length; i++)
                {
                    var fieldType = csharpTypes[i % csharpTypes.Length];

                    fromShader.Add((member.Name, fields[i], fieldType, offset));
                    offset += Marshal.SizeOf(fieldType);
                }
            }

            // The same members, the same order, the same types, the same offsets
            for (var i = 0; i < Math.Min(fromShader.Count, structFields.Length); i++)
            {
                var (member, field, fieldType, shaderOffset) = fromShader[i];
                var structField = StructFieldsInfo.FirstOrDefault(f => f.Name == field);
                var structOffset = Marshal.OffsetOf<ViewConstants>(field).ToInt32();

                if (!string.Equals(field, structFields[i], StringComparison.Ordinal))
                {
                    problems.Add($"The shader's {member} fills {field}, but {structFields[i]} is declared there instead");
                }

                if (structField != null && structField.FieldType != fieldType)
                {
                    problems.Add($"{field} is a {structField.FieldType.Name} in the struct and {member} is a {fieldType.Name} there");
                }

                if (structOffset != shaderOffset)
                {
                    problems.Add($"{field} is at byte {structOffset} in the struct and {member} is at byte {shaderOffset} in the shader");
                }
            }

            for (var i = fromShader.Count; i < structFields.Length; i++)
            {
                problems.Add($"{structFields[i]} is in the struct and no member of the shader's block fills it");
            }

            // The block the shaders see is padded out to a whole 16 byte row
            var blockSize = Align(lastMemberEnd, 16);
            var structSize = Marshal.SizeOf<ViewConstants>();

            if (blockSize != structSize)
            {
                problems.Add($"The shader's block is {blockSize} bytes and the struct is {structSize}");
            }

            return problems;
        }

        [Test]
        public async Task ViewConstantsMatchesTheShaderBlock()
        {
            var source = Preprocess("default.vert.slang");

            // Nothing to compare against if the block did not come through the include
            await Assert.That(GlslMembers(source, "ViewConstants")).IsNotEmpty();

            using (Assert.Multiple())
            {
                await Assert.That(FindMismatches(source)).IsEmpty();
            }
        }

        [Test]
        public async Task ThePerViewTogglesRideInTheViewBlock()
        {
            // They are per view, so they belong with the camera rather than with the scene's lights
            var names = GlslMembers(Preprocess("default.vert.slang"), "ViewConstants").Select(m => m.Name).ToList();

            using (Assert.Multiple())
            {
                await Assert.That(names).Contains("g_bIsSkybox");
                await Assert.That(names).Contains("g_bSunShadowsEnabled");
            }
        }

        [Test]
        public async Task TheSkyboxFlagIsNoLongerPerScene()
        {
            // It is per view now, so the lighting block carries padding where it used to carry the flag
            using (Assert.Multiple())
            {
                await Assert.That(typeof(LightingConstants).GetField("IsSkybox", BindingFlags.Public | BindingFlags.Instance))
                    .IsNull();
                await Assert.That(typeof(LightingConstants).GetField("_LightingPadding1", BindingFlags.Public | BindingFlags.Instance))
                    .IsNotNull();
            }
        }

        [Test]
        public async Task LayoutCheckSeesAFieldOnlyTheShaderHas()
        {
            var source = Preprocess("default.vert.slang");
            var mutated = source.Replace("vec4 g_vEnvWetness;\n", "vec4 g_vEnvWetness;\nfloat g_flSomeNewThing;\n", StringComparison.Ordinal);

            // The mutation has to have landed, or a pass below is passing for the wrong reason
            await Assert.That(mutated).IsNotEqualTo(source);

            var problems = FindMismatches(mutated);

            using (Assert.Multiple())
            {
                await Assert.That(problems).IsNotEmpty();
                await Assert.That(problems.Any(p => p.Contains("g_flSomeNewThing", StringComparison.Ordinal))).IsTrue();
            }
        }

        [Test]
        public async Task LayoutCheckSeesAFieldOnlyTheStructHas()
        {
            var structFields = StructFieldsInfo.Select(f => f.Name).Append("SomeNewField").ToArray();

            var problems = FindMismatches(Preprocess("default.vert.slang"), structFields);

            using (Assert.Multiple())
            {
                await Assert.That(problems).IsNotEmpty();
                await Assert.That(problems.Any(p => p.Contains("SomeNewField", StringComparison.Ordinal))).IsTrue();
            }
        }

        [Test]
        public async Task LayoutCheckSeesAReorderedField()
        {
            var source = Preprocess("default.vert.slang");
            var mutated = source.Replace("vec4 g_vEnvWetness;\nvec4 g_vEnvWetnessRipple;\n",
                "vec4 g_vEnvWetnessRipple;\nvec4 g_vEnvWetness;\n", StringComparison.Ordinal);

            // The same members at the same total size, so only their order and offsets can give it away
            await Assert.That(mutated).IsNotEqualTo(source);

            var problems = FindMismatches(mutated);

            using (Assert.Multiple())
            {
                await Assert.That(problems).IsNotEmpty();
                await Assert.That(problems.Any(p => p.Contains("EnvWetness", StringComparison.Ordinal))).IsTrue();
            }
        }

        [Test]
        public async Task LayoutCheckSeesAFieldTheShaderTypedDifferently()
        {
            // A float and a bool are both four bytes, so only the type comparison can see this one
            var source = Preprocess("default.vert.slang");
            var mutated = source.Replace("bool g_bIsSkybox;\n", "float g_bIsSkybox;\n", StringComparison.Ordinal);

            await Assert.That(mutated).IsNotEqualTo(source);

            var problems = FindMismatches(mutated);

            using (Assert.Multiple())
            {
                await Assert.That(problems).IsNotEmpty();
                await Assert.That(problems.Any(p => p.Contains("g_bIsSkybox", StringComparison.Ordinal))).IsTrue();
            }
        }
    }
}
