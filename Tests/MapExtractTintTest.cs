using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.DmxModel;

namespace Tests.IO
{
    public class MapExtractTintTest
    {
        [Test]
        public async Task DrawCallTintUsesCompilerGammaAndPreservesAlpha()
        {
            var drawCall = KVObject.Collection();
            drawCall.Add("m_vTintColor", VectorValues(new Vector3(0.5f, 0.25f, 0.125f)));
            drawCall.Add("m_flAlpha", new KVObject(0.5f));

            var tint = MapExtract.GetDrawCallTint(drawCall);

            using (Assert.Multiple())
            {
                await Assert.That(tint.X).IsEqualTo(186.0837f).Within(0.01f);
                await Assert.That(tint.Y).IsEqualTo(135.7927f).Within(0.01f);
                await Assert.That(tint.Z).IsEqualTo(99.0934f).Within(0.01f);
                await Assert.That(tint.W).IsEqualTo(127.5f).Within(0.01f);
            }
        }

        [Test]
        public async Task FragmentTintPreservesVectorAndAlphaValues()
        {
            var vectorFragment = KVObject.Collection();
            vectorFragment.Add("m_vTintColor", VectorValues(new Vector3(10f, 20f, 30f)));
            var vector4Fragment = KVObject.Collection();
            vector4Fragment.Add("m_vTintColor", VectorValues(new Vector4(10f, 20f, 30f, 40f)));
            var emptyFragment = KVObject.Collection();

            using (Assert.Multiple())
            {
                await AssertVector(MapExtract.GetFragmentTint(vectorFragment), new Vector4(10f, 20f, 30f, 255f));
                await AssertVector(MapExtract.GetFragmentTint(vector4Fragment), new Vector4(10f, 20f, 30f, 40f));
                await AssertVector(MapExtract.GetFragmentTint(emptyFragment), new Vector4(255f));
            }
        }

        [Test]
        public async Task ColorConversionRoundsAndClampsChannels()
        {
            var color = MapExtract.ConvertToColor32(new Vector4(1.6f, 254.6f, -2f, 300f));

            using (Assert.Multiple())
            {
                await Assert.That(color.R).IsEqualTo((byte)2);
                await Assert.That(color.G).IsEqualTo((byte)255);
                await Assert.That(color.B).IsEqualTo((byte)0);
                await Assert.That(color.A).IsEqualTo((byte)255);
            }
        }

        [Test]
        public async Task NamedFoliageStreamsKeepTheirCompilerNames()
        {
            var pivotPaint = new VBIB.RenderInputLayoutField("pivotpaint", DXGI_FORMAT.R32G32B32A32_FLOAT, 0)
            {
                SemanticIndex = 2,
            };
            var foliageAnimation = new VBIB.RenderInputLayoutField("FoliageAnimation", DXGI_FORMAT.R32_FLOAT, 0);
            var normal = new VBIB.RenderInputLayoutField("NORMAL", DXGI_FORMAT.R32G32B32_FLOAT, 0);

            using (Assert.Multiple())
            {
                await Assert.That(DmxMeshBuilder.GetStreamName(pivotPaint)).IsEqualTo("PivotPaint$2");
                await Assert.That(DmxMeshBuilder.GetStreamName(foliageAnimation)).IsEqualTo("FoliageAnimation$0");
                await Assert.That(DmxMeshBuilder.GetStreamName(normal)).IsEqualTo("normal$0");
            }
        }

        private static KVObject VectorValues(Vector3 value)
        {
            var values = KVObject.Array();
            values.Add(new KVObject(value.X));
            values.Add(new KVObject(value.Y));
            values.Add(new KVObject(value.Z));
            return values;
        }

        private static KVObject VectorValues(Vector4 value)
        {
            var values = VectorValues(new Vector3(value.X, value.Y, value.Z));
            values.Add(new KVObject(value.W));
            return values;
        }

        private static async Task AssertVector(Vector4 actual, Vector4 expected)
        {
            using (Assert.Multiple())
            {
                await Assert.That(actual.X).IsEqualTo(expected.X);
                await Assert.That(actual.Y).IsEqualTo(expected.Y);
                await Assert.That(actual.Z).IsEqualTo(expected.Z);
                await Assert.That(actual.W).IsEqualTo(expected.W);
            }
        }
    }
}
