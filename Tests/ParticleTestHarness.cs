using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    /// <summary>
    /// Builders for synthetic particle function blocks, so a test can drive an initializer, operator
    /// or constraint without a compiled definition on disk.
    /// </summary>
    internal static class ParticleTestHarness
    {
        /// <summary>Seeds one particle of a synthetic collection.</summary>
        public delegate void ParticleSetup(int index, ref Particle particle);

        public static ParticleDefinitionParser Parser(KVObject data) => new(data, NullLogger.Instance);

        /// <summary>A float input block of <paramref name="type"/> carrying the given scalar members.</summary>
        public static KVObject FloatInput(string type, params (string Key, float Value)[] members)
        {
            var block = KVObject.Collection();
            block.Add("m_nType", type);

            foreach (var (key, value) in members)
            {
                block.Add(key, new KVObject(value));
            }

            return block;
        }

        /// <summary>A <c>PF_TYPE_LITERAL</c> float input.</summary>
        public static KVObject LiteralInput(float value) => FloatInput("PF_TYPE_LITERAL", ("m_flLiteralValue", value));

        /// <summary>A <c>PVEC_TYPE_LITERAL</c> vector input.</summary>
        public static KVObject VectorInput(Vector3 value)
        {
            var block = KVObject.Collection();
            block.Add("m_nType", "PVEC_TYPE_LITERAL");
            block.Add("m_vLiteralValue", FloatArray(value));
            return block;
        }

        /// <summary>A bare three element float array, the shape a plain vector field takes.</summary>
        public static KVObject FloatArray(Vector3 value)
        {
            var array = KVObject.Array(3);
            array.Add(new KVObject(value.X));
            array.Add(new KVObject(value.Y));
            array.Add(new KVObject(value.Z));
            return array;
        }

        /// <summary>A <c>PT_TYPE_CONTROL_POINT</c> transform input.</summary>
        public static KVObject TransformInput(int controlPoint, bool useOrientation = false)
        {
            var block = KVObject.Collection();
            block.Add("m_nType", "PT_TYPE_CONTROL_POINT");
            block.Add("m_nControlPoint", controlPoint);
            block.Add("m_bUseOrientation", useOrientation);
            return block;
        }

        /// <summary>An <c>m_PathParams</c> block, optionally naming the control point the curve runs through.</summary>
        public static KVObject PathParams(int start, int end, int mid = -1)
        {
            var block = KVObject.Collection();
            block.Add("m_nStartControlPointNumber", start);
            block.Add("m_nEndControlPointNumber", end);

            if (mid > -1)
            {
                block.Add("m_nMidControlPointNumber", mid);
            }

            return block;
        }

        /// <summary>
        /// A collection of <paramref name="count"/> spawned particles, each seeded by
        /// <paramref name="setup"/> with its index.
        /// </summary>
        public static ParticleCollection Collection(int count, ParticleSetup? setup = null)
        {
            var collection = new ParticleCollection(new Particle(), count);

            for (var i = 0; i < count; i++)
            {
                var index = collection.Add();
                ref var particle = ref collection.Current[index];
                particle.Index = index;
                particle.UniqueParticleId = i;
                particle.ParticleId = i;
                setup?.Invoke(i, ref particle);
            }

            return collection;
        }

        /// <summary>A state whose control points sit on the +X axis at the given positions.</summary>
        public static ParticleSystemState StateWithControlPoints(params Vector3[] positions)
        {
            var state = new ParticleSystemState();

            for (var i = 0; i < positions.Length; i++)
            {
                state.SetControlPointValue(i, positions[i]);
            }

            return state;
        }

        /// <summary>
        /// A state part way through a frame of <paramref name="frameLength"/> seconds that started at
        /// <paramref name="frameStartAge"/>, whose first control point moved from
        /// <paramref name="previous"/> to <paramref name="current"/> over the frame before it. That
        /// history is what lets a spawn be placed part way through the move.
        /// </summary>
        public static ParticleSystemState StateInFrame(Vector3 previous, Vector3 current, float frameStartAge, float frameLength)
        {
            var state = new ParticleSystemState();
            state.Age = frameStartAge;
            state.BeginFrame(frameLength);

            var point = state.GetControlPoint(0);
            point.PositionPrevious = previous;
            point.Position = current;
            point.PreviousStepTime = frameLength;

            state.Age = frameStartAge + frameLength;
            return state;
        }

        /// <summary>A simulation over a synthetic definition that resolves every file lookup to nothing.</summary>
        public static ParticleSystemSimulation Simulation(KVObject definition, string name = "test.vpcf_c")
        {
            using var resource = new Resource(ResourceType.Particle) { FileName = name };
            var system = ParticleSystem.Create(definition);
            system.Resource = resource;
            resource.Blocks.Add(system);

            return new ParticleSystemSimulation(system, new EmptyFileLoader());
        }

        private sealed class EmptyFileLoader : IFileLoader
        {
            public Resource? LoadFile(string file) => null;
            public Resource? LoadFileCompiled(string file) => null;
            public ShaderCollection? LoadShader(string shaderName) => null;
            public Stream? GetFileStream(string file) => null;
        }
    }
}
