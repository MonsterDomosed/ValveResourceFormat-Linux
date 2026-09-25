using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Initializers;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    /// <summary>
    /// Control point transforms are read at the moment a particle was created, not at the end of the
    /// frame it was created in, so particles emitted through a frame follow a moving source.
    /// </summary>
    public class ParticleSpawnTransformsTest
    {
        private const float Tolerance = 1e-3f;

        /// <summary>The frame is 0.1s long, so a particle created half way through it is half way along the move.</summary>
        private const float FrameLength = 0.1f;
        private const float FrameStartAge = 1f;
        private const float HalfFrameCreationTime = 1.05f;
        private const float FrameEndCreationTime = 1.1f;

        [Test]
        public async Task CreateWithinSphereReadsTheControlPointAtTheCreationTime()
        {
            var state = ParticleTestHarness.StateInFrame(Vector3.Zero, new Vector3(100f, 0f, 0f), FrameStartAge, FrameLength);

            var data = KVObject.Collection();
            data.Add("m_TransformInput", ParticleTestHarness.TransformInput(0));
            data.Add("m_fRadiusMin", ParticleTestHarness.LiteralInput(0f));
            data.Add("m_fRadiusMax", ParticleTestHarness.LiteralInput(0f));

            var collection = ParticleTestHarness.Collection(1, (_, ref p) => p.CreationTime = HalfFrameCreationTime);
            new CreateWithinSphereTransform(ParticleTestHarness.Parser(data)).Initialize(ref collection.Current[0], collection, state);

            // Half way through the move, rather than where the control point ended up.
            await AssertVector(collection.Current[0].Position, new Vector3(50f, 0f, 0f));
        }

        [Test]
        public async Task CreateWithinConeReadsTheTransformAtTheCreationTime()
        {
            var state = ParticleTestHarness.StateInFrame(Vector3.Zero, new Vector3(100f, 0f, 0f), FrameStartAge, FrameLength);

            var collection = SpawnCone(state, HalfFrameCreationTime);

            // The ring is placed 10 units along forward from the interpolated transform origin.
            await AssertVector(collection.Current[0].Position, new Vector3(60f, 0f, 0f));
        }

        [Test]
        public async Task SpawnTransformBlendsTheRotationAtTheCreationTime()
        {
            var state = ParticleTestHarness.StateInFrame(Vector3.Zero, Vector3.Zero, FrameStartAge, FrameLength);

            var point = state.GetControlPoint(0);
            point.Rotation = Quaternion.Identity;
            point.Orientation = Vector3.UnitX;
            point.RotationPrevious = EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(0f, 90f, 0f));
            point.OrientationPrevious = Vector3.UnitY;

            var collection = SpawnCone(state, HalfFrameCreationTime);

            // Half way between facing +X and facing +Y, so 10 units out is 45 degrees across.
            await AssertVector(collection.Current[0].Position, new Vector3(7.0711f, 7.0711f, 0f));
        }

        [Test]
        public async Task SpawnAtTheEndOfTheFrameUsesTheCurrentTransform()
        {
            var state = ParticleTestHarness.StateInFrame(Vector3.Zero, new Vector3(100f, 0f, 0f), FrameStartAge, FrameLength);

            var collection = SpawnCone(state, FrameEndCreationTime);

            await AssertVector(collection.Current[0].Position, new Vector3(110f, 0f, 0f));
        }

        [Test]
        public async Task CreateAlongPathClampsACreationTimePastTheCurrentAge()
        {
            var state = ParticleTestHarness.StateInFrame(Vector3.Zero, new Vector3(100f, 0f, 0f), FrameStartAge, FrameLength);

            var end = state.GetControlPoint(1);
            end.PositionPrevious = new Vector3(200f, 0f, 0f);
            end.Position = new Vector3(300f, 0f, 0f);
            end.PreviousStepTime = FrameLength;

            var data = KVObject.Collection();
            data.Add("m_PathParams", ParticleTestHarness.PathParams(0, 1));
            data.Add("m_fT", ParticleTestHarness.LiteralInput(1f));

            // A creation time beyond the current age cannot be placed inside the frame, so the path
            // uses the control points as they are now instead of extrapolating past them.
            var collection = ParticleTestHarness.Collection(1, (_, ref p) => p.CreationTime = FrameEndCreationTime + 0.5f);
            new CreateAlongPath(ParticleTestHarness.Parser(data)).Initialize(ref collection.Current[0], collection, state);

            await AssertVector(collection.Current[0].Position, new Vector3(300f, 0f, 0f));
        }

        [Test]
        public async Task InitialVelocityNoiseRotatesTheVelocityByTheCreationTimeTransform()
        {
            var state = ParticleTestHarness.StateInFrame(Vector3.Zero, Vector3.Zero, FrameStartAge, FrameLength);

            var point = state.GetControlPoint(0);
            point.Rotation = EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(0f, 90f, 0f));
            point.Orientation = Vector3.UnitY;
            point.RotationPrevious = Quaternion.Identity;
            point.OrientationPrevious = Vector3.UnitX;

            var data = KVObject.Collection();
            data.Add("m_TransformInput", ParticleTestHarness.TransformInput(0, useOrientation: true));
            data.Add("m_vecOutputMin", ParticleTestHarness.VectorInput(Vector3.Zero));
            data.Add("m_vecOutputMax", ParticleTestHarness.VectorInput(new Vector3(1f, 0f, 0f)));
            data.Add("m_vecAbsVal", ParticleTestHarness.FloatArray(new Vector3(1f, 0f, 0f)));

            var collection = ParticleTestHarness.Collection(1, (_, ref p) => p.CreationTime = HalfFrameCreationTime);
            new InitialVelocityNoise(ParticleTestHarness.Parser(data)).Initialize(ref collection.Current[0], collection, state);

            var velocity = collection.Current[0].Velocity;

            // Only the local X component is fed from the noise, so the direction is the interpolated
            // frame's forward: 45 degrees across, not the end-of-frame transform's +Y.
            await Assert.That(velocity.Length()).IsGreaterThan(0.05f);
            var direction = velocity / velocity.Length();
            await Assert.That(Vector3.Dot(direction, new Vector3(0.7071f, 0.7071f, 0f))).IsEqualTo(1f).Within(1e-2f);
            await Assert.That(direction.Z).IsEqualTo(0f).Within(1e-2f);
        }

        [Test]
        public async Task RingWaveReadsTheTransformAtTheCreationTime()
        {
            var state = ParticleTestHarness.StateInFrame(Vector3.Zero, new Vector3(100f, 0f, 0f), FrameStartAge, FrameLength);

            var collection = SpawnRingWave(state, HalfFrameCreationTime);

            // A collapsed ring centred on the transform lands on the interpolated origin.
            await AssertVector(collection.Current[0].Position, new Vector3(50f, 0f, 0f));
        }

        [Test]
        public async Task RingWaveLaysTheRingOutInTheCreationTimeFrame()
        {
            var state = ParticleTestHarness.StateInFrame(Vector3.Zero, Vector3.Zero, FrameStartAge, FrameLength);

            var point = state.GetControlPoint(0);
            point.Rotation = EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(0f, 90f, 0f));
            point.Orientation = Vector3.UnitY;
            point.RotationPrevious = Quaternion.Identity;
            point.OrientationPrevious = Vector3.UnitX;

            var collection = SpawnRingWave(state, HalfFrameCreationTime, radius: 1f);

            // One particle per orbit puts the single ring point on the transform's local +X, so the
            // ring plane is the interpolated frame rather than the end-of-frame one.
            await AssertVector(collection.Current[0].Position, new Vector3(0.7071f, 0.7071f, 0f));
        }

        [Test]
        public async Task SpawnTransformKeepsTheCurrentRotationWhenNoPreviousOneWasRecorded()
        {
            var state = ParticleTestHarness.StateInFrame(Vector3.Zero, Vector3.Zero, FrameStartAge, FrameLength);

            // The control point turned, but the step that recorded its history did not see the turn, so
            // there is no earlier rotation to blend from.
            var point = state.GetControlPoint(0);
            point.Rotation = EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(0f, 90f, 0f));
            point.Orientation = Vector3.UnitY;

            var collection = SpawnCone(state, HalfFrameCreationTime);

            // Blending from an unset rotation would land halfway between facing nothing and facing +Y.
            await AssertVector(collection.Current[0].Position, new Vector3(0f, 10f, 0f));
        }

        [Test]
        public async Task NormalAlignToCPTakesAnAxisOfTheCreationTimeTransform()
        {
            var state = ParticleTestHarness.StateInFrame(Vector3.Zero, Vector3.Zero, FrameStartAge, FrameLength);

            var point = state.GetControlPoint(0);
            point.Rotation = EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(0f, 90f, 0f));
            point.Orientation = Vector3.UnitY;
            point.RotationPrevious = Quaternion.Identity;
            point.OrientationPrevious = Vector3.UnitX;

            using (Assert.Multiple())
            {
                // Half way between the two frames, the interpolated left axis is 45 degrees across.
                await AssertVector(AlignNormal(state, HalfFrameCreationTime, "PARTICLE_CP_AXIS_Y"), new Vector3(-0.7071f, 0.7071f, 0f));

                // The same axis on a control point that never turned, read at the end of its life.
                await AssertVector(AlignNormal(state, FrameEndCreationTime, "PARTICLE_CP_AXIS_Y"), new Vector3(-1f, 0f, 0f));

                // A negative axis is the opposite one.
                await AssertVector(AlignNormal(state, HalfFrameCreationTime, "PARTICLE_CP_AXIS_NEGATIVE_Y"), new Vector3(0.7071f, -0.7071f, 0f));
            }
        }

        private static Vector3 AlignNormal(ParticleSystemState state, float creationTime, string axis)
        {
            var data = KVObject.Collection();
            data.Add("m_transformInput", ParticleTestHarness.TransformInput(0, useOrientation: true));
            data.Add("m_nControlPointAxis", axis);

            var collection = ParticleTestHarness.Collection(1, (_, ref p) => p.CreationTime = creationTime);
            new NormalAlignToCP(ParticleTestHarness.Parser(data)).Initialize(ref collection.Current[0], collection, state);

            return collection.Current[0].Normal;
        }

        private static ParticleCollection SpawnRingWave(ParticleSystemState state, float creationTime, float radius = 0f)
        {
            var data = KVObject.Collection();
            data.Add("m_TransformInput", ParticleTestHarness.TransformInput(0, useOrientation: true));
            data.Add("m_bEvenDistribution", true);
            data.Add("m_flParticlesPerOrbit", ParticleTestHarness.LiteralInput(1f));
            data.Add("m_flInitialRadius", ParticleTestHarness.LiteralInput(radius));
            data.Add("m_flThickness", ParticleTestHarness.LiteralInput(0f));

            var collection = ParticleTestHarness.Collection(1, (_, ref p) => p.CreationTime = creationTime);
            new RingWave(ParticleTestHarness.Parser(data)).Initialize(ref collection.Current[0], collection, state);

            return collection;
        }

        private static ParticleCollection SpawnCone(ParticleSystemState state, float creationTime)
        {
            var data = KVObject.Collection();
            data.Add("m_TransformInput", ParticleTestHarness.TransformInput(0, useOrientation: true));
            data.Add("m_flInnerAngle", ParticleTestHarness.LiteralInput(0f));
            data.Add("m_flOuterAngle", ParticleTestHarness.LiteralInput(0f));
            data.Add("m_flOffset", ParticleTestHarness.LiteralInput(10f));
            data.Add("m_flSpeed", ParticleTestHarness.LiteralInput(0f));

            var collection = ParticleTestHarness.Collection(1, (_, ref p) => p.CreationTime = creationTime);
            new CreateWithinCone(ParticleTestHarness.Parser(data)).Initialize(ref collection.Current[0], collection, state);

            return collection;
        }

        private static async Task AssertVector(Vector3 actual, Vector3 expected)
        {
            using (Assert.Multiple())
            {
                await Assert.That(actual.X).IsEqualTo(expected.X).Within(Tolerance);
                await Assert.That(actual.Y).IsEqualTo(expected.Y).Within(Tolerance);
                await Assert.That(actual.Z).IsEqualTo(expected.Z).Within(Tolerance);
            }
        }
    }
}
