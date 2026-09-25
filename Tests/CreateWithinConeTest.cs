using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Initializers;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    /// <summary>
    /// The cone initializer, which spawns on a ring around a transform's forward axis and moves
    /// outward along the cone surface.
    /// </summary>
    public class CreateWithinConeTest
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public async Task SpawnsOnTheRingAndLeavesAlongTheConeSurface()
        {
            var state = ConeState();
            var collection = Spawn(state, offset: 10f, coneAngle: 0f, speed: 5f, collapseOffset: false);

            using (Assert.Multiple())
            {
                // A zero cone angle collapses the ring onto the axis, so the particle starts the whole
                // offset along the transform's forward direction.
                await AssertVector(collection.Current[0].Position, new Vector3(0f, 10f, 0f));
                await AssertVector(collection.Current[0].Velocity, new Vector3(0f, 5f, 0f));
            }
        }

        [Test]
        public async Task CollapsedOffsetKeepsTheRingInTheTransformsPlane()
        {
            var state = ConeState();
            var collection = Spawn(state, offset: 10f, coneAngle: 0f, speed: 5f, collapseOffset: true);

            using (Assert.Multiple())
            {
                // The offset no longer pushes along the axis, but the velocity still runs along the cone.
                await AssertVector(collection.Current[0].Position, Vector3.Zero);
                await AssertVector(collection.Current[0].Velocity, new Vector3(0f, 5f, 0f));
            }
        }

        [Test]
        public async Task VelocityLeavesAtHalfTheDrawnConeAngle()
        {
            foreach (var offset in new[] { 10f, 0f })
            {
                var state = ConeState();
                var collection = Spawn(state, offset, coneAngle: 90f, speed: 5f, collapseOffset: false);
                var velocity = collection.Current[0].Velocity;

                await Assert.That(velocity.Length()).IsEqualTo(5f).Within(Tolerance);

                // The full cone angle is 90 degrees, so the direction is always 45 degrees off forward,
                // which a zero offset would not give without the one unit floor on the ring.
                var cosine = MathF.Abs(Vector3.Dot(velocity / velocity.Length(), state.GetControlPoint(0).Orientation));
                await Assert.That(cosine).IsEqualTo(MathF.Sqrt(0.5f)).Within(1e-3f);
            }
        }

        [Test]
        public async Task NegativeOffsetFlipsTheConeBackwards()
        {
            var state = ConeState();
            var collection = Spawn(state, offset: -10f, coneAngle: 0f, speed: 5f, collapseOffset: false);

            using (Assert.Multiple())
            {
                await AssertVector(collection.Current[0].Position, new Vector3(0f, -10f, 0f));
                await AssertVector(collection.Current[0].Velocity, new Vector3(0f, -5f, 0f));
            }
        }

        [Test]
        public async Task ReadsItsControlPointFromATransformInput()
        {
            var state = ParticleTestHarness.StateWithControlPoints(new Vector3(500f, 0f, 0f));

            var data = KVObject.Collection();
            data.Add("m_TransformInput", ParticleTestHarness.TransformInput(0));
            data.Add("m_flInnerAngle", ParticleTestHarness.LiteralInput(0f));
            data.Add("m_flOuterAngle", ParticleTestHarness.LiteralInput(0f));
            data.Add("m_flOffset", ParticleTestHarness.LiteralInput(4f));
            data.Add("m_flSpeed", ParticleTestHarness.LiteralInput(0f));

            var collection = ParticleTestHarness.Collection(1);
            new CreateWithinCone(ParticleTestHarness.Parser(data)).Initialize(ref collection.Current[0], collection, state);

            await AssertVector(collection.Current[0].Position, new Vector3(504f, 0f, 0f));
        }

        /// <summary>A state whose first control point sits at the origin yawed 90 degrees, so forward is +Y.</summary>
        private static ParticleSystemState ConeState()
        {
            var state = ParticleTestHarness.StateWithControlPoints(Vector3.Zero);
            state.SetControlPointRotation(0, EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(0f, 90f, 0f)));
            return state;
        }

        private static ParticleCollection Spawn(ParticleSystemState state, float offset, float coneAngle, float speed, bool collapseOffset)
        {
            var data = KVObject.Collection();
            data.Add("m_TransformInput", ParticleTestHarness.TransformInput(0, useOrientation: true));
            data.Add("m_flInnerAngle", ParticleTestHarness.LiteralInput(coneAngle));
            data.Add("m_flOuterAngle", ParticleTestHarness.LiteralInput(coneAngle));
            data.Add("m_flOffset", ParticleTestHarness.LiteralInput(offset));
            data.Add("m_flSpeed", ParticleTestHarness.LiteralInput(speed));
            data.Add("m_bCollapseOffset", collapseOffset);

            var collection = ParticleTestHarness.Collection(1);
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
