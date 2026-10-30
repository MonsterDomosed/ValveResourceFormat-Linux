using System.Threading.Tasks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Utils;

namespace Tests
{
    public class VisibilityBoxTest
    {
        [Test]
        public async Task InsideBoxCullsOnlyFullyContainedBounds()
        {
            var box = new VisibilityBox(VisibilityBoxMode.Inside, new Vector3(10f));
            box.Enable(Matrix4x4.CreateTranslation(new Vector3(20f, 0f, 0f)));

            var inside = new AABB(new Vector3(19f, -1f, -1f), new Vector3(21f, 1f, 1f));
            var crossing = new AABB(new Vector3(14f, -5f, -5f), new Vector3(26f, 5f, 5f));
            var outside = new AABB(new Vector3(30f, -1f, -1f), new Vector3(31f, 1f, 1f));
            using (Assert.Multiple())
            {
                await Assert.That(box.Contains(new Vector3(20f, 0f, 0f))).IsTrue();
                await Assert.That(box.Contains(inside)).IsTrue();
                await Assert.That(box.Contains(crossing)).IsFalse();
                await Assert.That(box.Touches(outside)).IsFalse();
            }
        }

        [Test]
        public async Task InfoVisibilityBoxIsSimulated()
        {
            await Assert.That(EntityFactory.IsRegistered("info_visibility_box")).IsTrue();
        }

        [Test]
        public async Task OutsideModeCullsBoundsTouchingNoOutsideBox()
        {
            var box = new VisibilityBox(VisibilityBoxMode.Outside, new Vector3(10f));
            box.Enable(Matrix4x4.CreateTranslation(Vector3.Zero));
            var culler = new VisibilityBoxCuller();

            culler.Build([box], Vector3.Zero);

            using (Assert.Multiple())
            {
                await Assert.That(culler.IsActive).IsTrue();
                await Assert.That(culler.OutsideCount).IsEqualTo(1);
                await Assert.That(culler.IsCulled(new AABB(new Vector3(1f), new Vector3(2f)))).IsFalse();
                await Assert.That(culler.IsCulled(new AABB(new Vector3(20f), new Vector3(21f)))).IsTrue();
            }
        }

        [Test]
        public async Task CameraInsideModeOnlyActivatesWhenEyeIsInside()
        {
            var box = new VisibilityBox(VisibilityBoxMode.OutsideWhenEyeInside, new Vector3(10f));
            box.Enable(Matrix4x4.Identity);
            var culler = new VisibilityBoxCuller();

            culler.Build([box], new Vector3(20f));
            await Assert.That(culler.IsActive).IsFalse();

            culler.Build([box], Vector3.Zero);
            await Assert.That(culler.IsActive).IsTrue();
        }
    }
}
