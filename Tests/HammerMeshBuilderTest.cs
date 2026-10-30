using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.IO;

namespace Tests.IO
{
    public class HammerMeshBuilderTest
    {
        [Test]
        public async Task AddVerticesAppliesRotationScaleAndTranslation()
        {
            var builder = new HammerMeshBuilder();
            var positions = new[]
            {
                new Vector3(1f, 2f, 3f),
                new Vector3(-1f, 0f, 2f),
            };
            var transform = new Matrix4x4(
                0f, 2f, 0f, 0f,
                -1f, 0f, 0f, 0f,
                0f, 0f, 3f, 0f,
                10f, 20f, 30f, 1f);

            var baseVertex = builder.AddVertices(positions, transform);
            var actual = builder.Mesh.VertexHandles.Select(handle => builder.Mesh.Positions[handle]).ToArray();

            using (Assert.Multiple())
            {
                await Assert.That(baseVertex).IsZero();
                await AssertVector(actual[0], new Vector3(8f, 22f, 39f));
                await AssertVector(actual[1], new Vector3(10f, 18f, 36f));
            }
        }

        private static async Task AssertVector(Vector3 actual, Vector3 expected)
        {
            using (Assert.Multiple())
            {
                await Assert.That(actual.X).IsEqualTo(expected.X);
                await Assert.That(actual.Y).IsEqualTo(expected.Y);
                await Assert.That(actual.Z).IsEqualTo(expected.Z);
            }
        }
    }
}
