using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Initializers;

namespace Tests
{
    /// <summary>
    /// The float mapping types that take a compare value, and the input types that read a control
    /// point rather than the particle.
    /// </summary>
    public class ParticleNumberInputsTest
    {
        [Test]
        public async Task AttributeMappingTakesTheMinMaxAndModOfTheCompareValue()
        {
            using (Assert.Multiple())
            {
                await Assert.That(Mapped("MIN", 5f, 3f)).IsEqualTo(3f);
                await Assert.That(Mapped("MIN", 5f, 7f)).IsEqualTo(5f);
                await Assert.That(Mapped("MIN", 5f, 5f)).IsEqualTo(5f);

                await Assert.That(Mapped("MAX", 5f, 3f)).IsEqualTo(5f);
                await Assert.That(Mapped("MAX", 5f, 7f)).IsEqualTo(7f);
                await Assert.That(Mapped("MAX", 5f, 5f)).IsEqualTo(5f);

                // A truncated remainder, so it keeps the sign of the input.
                await Assert.That(Mapped("MOD", 3f, 7f)).IsEqualTo(1f);
                await Assert.That(Mapped("MOD", 3f, -7f)).IsEqualTo(-1f);
                await Assert.That(Mapped("MOD", 3f, 6f)).IsZero();
            }
        }

        [Test]
        public async Task ModMappingByZeroKeepsTheInput()
        {
            // A zero compare value has no remainder to give, and dividing by it would be a NaN.
            using (Assert.Multiple())
            {
                await Assert.That(Mapped("MOD", 0f, 7f)).IsEqualTo(7f);
                await Assert.That(Mapped("MOD", 0f, -7.5f)).IsEqualTo(-7.5f);
            }
        }

        [Test]
        public async Task ControlPointDistanceInputReadsTheDistanceToItsControlPoint()
        {
            var input = KVObject.Collection();
            input.Add("m_nType", "PF_TYPE_CONTROL_POINT_DISTANCE");
            input.Add("m_nControlPoint", 1);

            var data = KVObject.Collection();
            data.Add("m_nOutputField", (int)ParticleField.Radius);
            data.Add("m_InputValue", input);

            // A control point 3-4-5 away from the particle, and one that is not the one being asked for.
            var state = ParticleTestHarness.StateWithControlPoints(
                new Vector3(1000f, 0f, 0f),
                new Vector3(3f, 4f, 0f));

            var collection = ParticleTestHarness.Collection(1);
            new InitFloat(ParticleTestHarness.Parser(data)).Initialize(ref collection.Current[0], collection, state);

            await Assert.That(collection.Current[0].Radius).IsEqualTo(5f).Within(1e-4f);
        }

        [Test]
        public async Task ControlPointDistanceInputRunsThroughTheAttributeMapping()
        {
            var input = KVObject.Collection();
            input.Add("m_nType", "PF_TYPE_CONTROL_POINT_DISTANCE");
            input.Add("m_nControlPoint", 0);
            input.Add("m_nMapType", "MIN");
            input.Add("m_flCompareValue", 4f);

            var data = KVObject.Collection();
            data.Add("m_nOutputField", (int)ParticleField.Radius);
            data.Add("m_InputValue", input);

            var state = ParticleTestHarness.StateWithControlPoints(new Vector3(5f, 0f, 0f));
            var collection = ParticleTestHarness.Collection(1);
            new InitFloat(ParticleTestHarness.Parser(data)).Initialize(ref collection.Current[0], collection, state);

            await Assert.That(collection.Current[0].Radius).IsEqualTo(4f).Within(1e-4f);
        }

        private static float Mapped(string mapType, float compareValue, float input)
        {
            var data = KVObject.Collection();
            data.Add("m_nMapType", mapType);
            data.Add("m_flCompareValue", compareValue);

            return new AttributeMapping(ParticleTestHarness.Parser(data)).ApplyMapping(input);
        }
    }
}
