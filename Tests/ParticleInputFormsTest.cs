using System.Linq;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Constraints;
using ValveResourceFormat.Particles.Initializers;
using ValveResourceFormat.Particles.Operators;

namespace Tests
{
    /// <summary>
    /// The input forms the path and distance functions accept: fields that became float, vector or
    /// transform inputs without a vpcf format bump are read either as the new block or as the older
    /// bare value, and the path functions gained a path parameter and a mid control point.
    /// </summary>
    public class ParticleInputFormsTest
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public async Task CreateAlongPathUsesThePathParameterInput()
        {
            var data = KVObject.Collection();
            data.Add("m_PathParams", ParticleTestHarness.PathParams(0, 1));
            data.Add("m_fT", ParticleTestHarness.LiteralInput(0.25f));

            var state = ParticleTestHarness.StateWithControlPoints(Vector3.Zero, new Vector3(100f, 0f, 0f));
            var collection = ParticleTestHarness.Collection(1);

            new CreateAlongPath(ParticleTestHarness.Parser(data)).Initialize(ref collection.Current[0], collection, state);

            // A quarter of the way along a straight path is a quarter of the way between the control points.
            await AssertVector(collection.Current[0].Position, new Vector3(25f, 0f, 0f));
        }

        [Test]
        public async Task CreateAlongPathDrawsThePathParameterWhenTheInputIsAbsent()
        {
            var data = KVObject.Collection();
            data.Add("m_PathParams", ParticleTestHarness.PathParams(0, 1));

            // Content predating m_fT keeps the old behaviour: a uniform random parameter.
            var positions = SpreadAlongPath(data);

            // The particles land all over the path rather than at one spot on it.
            await Assert.That(positions.Distinct().Count()).IsGreaterThan(1);
            await AssertSpread(positions);
        }

        [Test]
        public async Task CreateAlongPathReadsAnUntypedPathParameterAsARandomDraw()
        {
            var data = KVObject.Collection();
            data.Add("m_PathParams", ParticleTestHarness.PathParams(0, 1));
            data.Add("m_fT", KVObject.Collection());

            // The field's own default is a varying uniform draw over [0, 1), so an empty block still
            // spreads the particles rather than pinning them all to the start of the path.
            var positions = SpreadAlongPath(data);

            await Assert.That(positions.Distinct().Count()).IsGreaterThan(1);
            await AssertSpread(positions);
        }

        [Test]
        public async Task CreateAlongPathRunsTheCurveThroughTheMidControlPoint()
        {
            var data = KVObject.Collection();
            data.Add("m_PathParams", ParticleTestHarness.PathParams(0, 1, mid: 2));
            data.Add("m_fT", ParticleTestHarness.LiteralInput(0.5f));

            var state = ParticleTestHarness.StateWithControlPoints(
                Vector3.Zero,
                new Vector3(100f, 0f, 0f),
                new Vector3(50f, 50f, 0f));
            var collection = ParticleTestHarness.Collection(1);

            new CreateAlongPath(ParticleTestHarness.Parser(data)).Initialize(ref collection.Current[0], collection, state);

            // A mid control point makes the curve pass through that point at half way.
            await AssertVector(collection.Current[0].Position, new Vector3(50f, 50f, 0f));
        }

        [Test]
        public async Task CreateAlongPathSavesThePathParameterWhenAsked()
        {
            var data = KVObject.Collection();
            data.Add("m_PathParams", ParticleTestHarness.PathParams(2, 5));
            data.Add("m_fT", ParticleTestHarness.LiteralInput(0.5f));
            data.Add("m_bSaveOffset", true);

            var state = ParticleTestHarness.StateWithControlPoints(
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 0f),
                new Vector3(10f, 0f, 0f),
                Vector3.Zero,
                Vector3.Zero,
                new Vector3(20f, 0f, 0f));

            var collection = ParticleTestHarness.Collection(1);
            new CreateAlongPath(ParticleTestHarness.Parser(data)).Initialize(ref collection.Current[0], collection, state);

            // The lock-to-saved-path operators read the path parameter and the control point pair back
            // out of the hitbox offset.
            using (Assert.Multiple())
            {
                await Assert.That(collection.Current[0].HitboxOffsetPosition.X).IsEqualTo(0.5f);
                await Assert.That(collection.Current[0].HitboxOffsetPosition.Y).IsEqualTo(2f);
                await Assert.That(collection.Current[0].HitboxOffsetPosition.Z).IsEqualTo(5f);
            }
        }

        [Test]
        public async Task ConstrainDistanceAcceptsBothFormsOfTheControlPoint()
        {
            var state = ParticleTestHarness.StateWithControlPoints(
                new Vector3(1f, 0f, 0f),
                new Vector3(1000f, 0f, 0f),
                new Vector3(2000f, 0f, 0f));

            var fromBlock = KVObject.Collection();
            fromBlock.Add("m_nControlPointNumber", ParticleTestHarness.TransformInput(2));

            var fromBareNumber = KVObject.Collection();
            fromBareNumber.Add("m_nControlPointNumber", 2);

            // A zero-length band snaps every particle exactly onto the center, whatever it started at.
            await AssertVector(ConstrainedCenter(state, fromBlock, new Vector3(7f, 0f, 0f)), new Vector3(2000f, 0f, 0f), 1e-3f);
            await AssertVector(ConstrainedCenter(state, fromBareNumber, new Vector3(7f, 0f, 0f)), new Vector3(2000f, 0f, 0f), 1e-3f);
        }

        [Test]
        public async Task ConstrainDistanceReadsTheCenterOffsetAsAVectorInput()
        {
            var data = KVObject.Collection();
            data.Add("m_nControlPointNumber", ParticleTestHarness.TransformInput(0));
            data.Add("m_CenterOffset", ParticleTestHarness.VectorInput(new Vector3(0f, 10f, 0f)));
            data.Add("m_bGlobalCenter", true);
            data.Add("m_fMaxDistance", ParticleTestHarness.LiteralInput(0f));

            var state = ParticleTestHarness.StateWithControlPoints(new Vector3(1f, 0f, 0f));
            var collection = ParticleTestHarness.Collection(1, (_, ref p) => p.Position = new Vector3(0f, 50f, 0f));

            new ConstrainDistance(ParticleTestHarness.Parser(data))
                .ApplyConstraint(collection, 0.016f, state);

            // A global center uses the offset as a bare world position.
            await AssertVector(collection.Current[0].Position, new Vector3(0f, 10f, 0f), 1e-3f);
        }

        [Test]
        public async Task MaintainSequentialPathReadsTheSlotCountAsAFloatInput()
        {
            var data = KVObject.Collection();
            data.Add("m_PathParams", ParticleTestHarness.PathParams(0, 1));
            data.Add("m_flNumToAssign", ParticleTestHarness.FloatInput("PF_TYPE_COLLECTION_AGE"));
            data.Add("m_flCohesionStrength", ParticleTestHarness.LiteralInput(1f));

            var state = ParticleTestHarness.StateWithControlPoints(Vector3.Zero, new Vector3(100f, 0f, 0f));
            state.Age = 2f;

            var collection = ParticleTestHarness.Collection(3, (_, ref p) => p.Position = Vector3.Zero);

            new MaintainSequentialPath(ParticleTestHarness.Parser(data)).Operate(collection, 0.016f, state, 1f);

            // Collection age 2 means two slots, so the third particle wraps back onto the first slot.
            using (Assert.Multiple())
            {
                await AssertVector(collection.Current[0].Position, Vector3.Zero);
                await AssertVector(collection.Current[1].Position, new Vector3(100f, 0f, 0f));
                await AssertVector(collection.Current[2].Position, Vector3.Zero);
            }
        }

        [Test]
        public async Task MaintainSequentialPathReadsTheOffsetBandAsFloatInputs()
        {
            var data = KVObject.Collection();
            data.Add("m_PathParams", ParticleTestHarness.PathParams(0, 1));
            data.Add("m_flNumToAssign", ParticleTestHarness.LiteralInput(2f));
            data.Add("m_fMaxDistance", ParticleTestHarness.FloatInput("PF_TYPE_COLLECTION_AGE"));
            data.Add("m_flCohesionStrength", ParticleTestHarness.LiteralInput(0f));

            var state = ParticleTestHarness.StateWithControlPoints(Vector3.Zero, new Vector3(100f, 0f, 0f));
            state.Age = 4f;

            var collection = ParticleTestHarness.Collection(2, (_, ref p) => p.Position = Vector3.Zero);

            new MaintainSequentialPath(ParticleTestHarness.Parser(data)).Operate(collection, 0.016f, state, 1f);

            // Full cohesion is off, so the particle keeps its offset, clamped to the max distance of 4.
            using (Assert.Multiple())
            {
                await AssertVector(collection.Current[0].Position, Vector3.Zero);
                await AssertVector(collection.Current[1].Position, new Vector3(96f, 0f, 0f));
            }
        }

        /// <summary>Spawns 16 particles along a straight 100 unit path with the given definition.</summary>
        private static float[] SpreadAlongPath(KVObject data)
        {
            var state = ParticleTestHarness.StateWithControlPoints(Vector3.Zero, new Vector3(100f, 0f, 0f));
            var collection = ParticleTestHarness.Collection(16);

            var initializer = new CreateAlongPath(ParticleTestHarness.Parser(data));

            for (var i = 0; i < collection.Count; i++)
            {
                initializer.Initialize(ref collection.Current[i], collection, state);
            }

            return [.. collection.Current.ToArray().Select(p => p.Position.X)];
        }

        private static async Task AssertSpread(float[] positions)
        {
            using (Assert.Multiple())
            {
                foreach (var x in positions)
                {
                    await Assert.That(x).IsGreaterThanOrEqualTo(0f);
                    await Assert.That(x).IsLessThan(100f);
                }
            }
        }

        private static Vector3 ConstrainedCenter(ParticleSystemState state, KVObject data, Vector3 particlePosition)
        {
            data.Add("m_fMaxDistance", ParticleTestHarness.LiteralInput(0f));

            var collection = ParticleTestHarness.Collection(1, (_, ref p) => p.Position = particlePosition);

            new ConstrainDistance(ParticleTestHarness.Parser(data)).ApplyConstraint(collection, 0.016f, state);

            return collection.Current[0].Position;
        }

        private static async Task AssertVector(Vector3 actual, Vector3 expected, float tolerance = Tolerance)
        {
            using (Assert.Multiple())
            {
                await Assert.That(actual.X).IsEqualTo(expected.X).Within(tolerance);
                await Assert.That(actual.Y).IsEqualTo(expected.Y).Within(tolerance);
                await Assert.That(actual.Z).IsEqualTo(expected.Z).Within(tolerance);
            }
        }
    }
}
