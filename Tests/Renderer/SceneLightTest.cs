using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;

namespace Tests.Renderer
{
    /// <summary>
    /// A light is placed from one transform, so the same matrix has to give its node, its position and the
    /// direction it faces. These cover that, and that a spawn group's placement (a 3D sky's reference)
    /// reaches all three while an unparented map's lights land where they always did.
    /// </summary>
    public class SceneLightTest
    {
        private const float Tolerance = 1e-5f;

        /// <summary>Placements a light can be dropped into: an unparented map, a template parent, a sky reference.</summary>
        private static readonly Matrix4x4[] Parents =
        [
            Matrix4x4.Identity,
            Matrix4x4.CreateTranslation(new Vector3(64f, -128f, 32f)),
            EntityTransformHelper.ToRigidTransformationMatrix(new Vector3(0f, 35f, 0f), new Vector3(1024f, -512f, 96f)),
        ];

        private static readonly Matrix4x4[] Placements =
        [
            Matrix4x4.Identity,
            EntityTransformHelper.ToRigidTransformationMatrix(new Vector3(50f, 43f, 0f), Vector3.Zero),
            EntityTransformHelper.ToRigidTransformationMatrix(new Vector3(-30f, 135f, 20f), new Vector3(10f, 20f, 30f)),
            Matrix4x4.CreateScale(new Vector3(2f, 3f, 4f)),
            Parents[1] * EntityTransformHelper.ToRigidTransformationMatrix(new Vector3(15f, -160f, -75f), new Vector3(-5f, 6f, 7f)),
            Parents[2] * EntityTransformHelper.ToRigidTransformationMatrix(new Vector3(0f, 35f, 0f), new Vector3(1024f, -512f, 96f)),
        ];

        private readonly List<IDisposable> harnessContexts = [];

        [After(HookType.Test)]
        public void DisposeHarnessContexts()
        {
            for (var i = harnessContexts.Count - 1; i >= 0; i--)
            {
                harnessContexts[i].Dispose();
            }

            harnessContexts.Clear();
        }

        private Scene CreateScene()
        {
            var fileLoader = new GameFileLoader(null, null);
            harnessContexts.Add(fileLoader);

            var context = new RendererContext(fileLoader, NullLogger.Instance);
            harnessContexts.Add(context);

            var scene = new Scene(context);
            harnessContexts.Add(scene);

            return scene;
        }

        private static EntityLump.Entity MakeEntity(params (string Key, string Value)[] properties)
        {
            var entity = new EntityLump.Entity { ParentLump = new EntityLump { Resource = new Resource() } };

            foreach (var (key, value) in properties)
            {
                entity.Add(key, value);
            }

            return entity;
        }

        private static async Task AssertClose(Vector3 actual, Vector3 expected, string because)
        {
            await Assert.That(actual.X).IsEqualTo(expected.X).Within(Tolerance).Because($"X for {because}");
            await Assert.That(actual.Y).IsEqualTo(expected.Y).Within(Tolerance).Because($"Y for {because}");
            await Assert.That(actual.Z).IsEqualTo(expected.Z).Within(Tolerance).Because($"Z for {because}");
        }

        /// <summary>
        /// The node, the position and the direction all come from the matrix handed in, whatever was there
        /// before: nothing is left over from an earlier placement.
        /// </summary>
        [Test]
        public async Task PlaceAtTakesTransformPositionAndDirectionFromOneMatrix()
        {
            var scene = CreateScene();

            foreach (var placement in Placements)
            {
                var light = new SceneLight(scene)
                {
                    Transform = Matrix4x4.CreateScale(-7f) * Matrix4x4.CreateTranslation(new Vector3(999f)),
                    Position = new Vector3(999f),
                    Direction = new Vector3(-1f, 5f, 0f),
                };

                light.PlaceAt(placement);

                await Assert.That(light.Transform).IsEqualTo(placement).Because($"the node for {placement}");

                await AssertClose(light.Position, placement.Translation, $"the position for {placement}");

                // Forward is the direction UnitX goes, which for a row-vector matrix is the first row. Read
                // through a transform rather than a row so the helper and this cannot both be wrong the
                // same way, and normalized so an authored scale does not shorten it.
                var expected = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, placement));

                await AssertClose(light.Direction, expected, $"the direction for {placement}");
                await Assert.That(light.Direction.Length()).IsEqualTo(1f).Within(Tolerance).Because($"a normalized direction for {placement}");
            }
        }

        /// <summary>
        /// A sky's lights are placed by the skybox_reference, so the reference has to turn them. Taking the
        /// direction from the entity's own angles would leave a sky's lights facing the main map's way.
        /// </summary>
        [Test]
        public async Task PlaceAtTurnsDirectionWithTheSpawnGroupPlacement()
        {
            var scene = CreateScene();

            foreach (var parent in Parents)
            {
                var angles = new Vector3(25f, -140f, 15f);
                var local = EntityTransformHelper.ToRigidTransformationMatrix(angles, new Vector3(8f, -4f, 16f));

                var light = new SceneLight(scene);
                light.PlaceAt(local * parent);

                // The authored forward, carried through the placement: a rotation turns it, a translation
                // cannot, and a scale cannot shorten it
                var authored = EntityTransformHelper.EulerAnglesToForwardDirection(angles);
                var expected = Vector3.Normalize(Vector3.TransformNormal(authored, parent));

                await AssertClose(light.Direction, expected, $"a light at angles {angles} under {parent}");

                await AssertClose(light.Position, Vector3.Transform(local.Translation, parent), $"the origin of a light at {angles} under {parent}");

                // Placing the light under no parent has to give the direction the angles alone describe
                var unparented = new SceneLight(scene);
                unparented.PlaceAt(local);

                await AssertClose(unparented.Direction, authored, $"an unparented light at angles {angles}");
            }
        }

        /// <summary>
        /// The keyvalues a light entity carries are in its own space, so reading them leaves the light
        /// unplaced. A caller that forgets to place it gets nothing rather than a light at the map origin.
        /// </summary>
        [Test]
        public async Task FromEntityPropertiesLeavesThePlacementToTheCaller()
        {
            var scene = CreateScene();

            var light = SceneLight.FromEntityProperties(scene, SceneLight.EntityType.Spot, MakeEntity(
                ("origin", "64 -32 128"),
                ("angles", "30 45 0"),
                ("color", "255 128 64"),
                ("brightness", "2")));

            await Assert.That(light.Position).IsEqualTo(Vector3.Zero).Because("the keyvalues are in the entity's own space");
            await Assert.That(light.Direction).IsEqualTo(Vector3.Zero).Because("the keyvalues are in the entity's own space");
            await Assert.That(light.Transform).IsEqualTo(Matrix4x4.Identity).Because("the caller places the light");

            // The properties that are about the light rather than its placement still come from the entity
            await Assert.That(light.Color).IsEqualTo(new Vector3(1f, 128f / 255f, 64f / 255f));
            await Assert.That(light.Brightness).IsEqualTo(2f);
        }

        /// <summary>
        /// A map's own lights are placed from the entity's full transform, so an unparented light ends up
        /// where it did when the loader read the origin and angles off the keyvalues directly.
        /// </summary>
        [Test]
        public async Task AnUnparentedLightLandsWhereItsKeyvaluesSay()
        {
            var scene = CreateScene();

            var entity = MakeEntity(
                ("origin", "64 -32 128"),
                ("angles", "30 45 0"),
                ("scales", "2 2 2"));

            var light = SceneLight.FromEntityProperties(scene, SceneLight.EntityType.Omni, entity);
            light.PlaceAt(EntityTransformHelper.ToTransformationMatrix(entity) * Matrix4x4.Identity);

            await AssertClose(light.Position, new Vector3(64f, -32f, 128f), "the authored origin");
            await AssertClose(light.Direction, EntityTransformHelper.EulerAnglesToForwardDirection(new Vector3(30f, 45f, 0f)), "the authored angles");

            // A scale on the entity does not tilt the light, it is normalized back out of the same matrix
            var scaled = new SceneLight(scene);
            scaled.PlaceAt(EntityTransformHelper.ToTransformationMatrix(MakeEntity(("angles", "0 90 0"), ("scales", "3 3 3"))));

            await AssertClose(scaled.Direction, new Vector3(0f, 1f, 0f), "a scaled light facing +Y");
        }
    }
}
