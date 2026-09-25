using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace Tests
{
    public class PvsCullingTest
    {
        [Test]
        public async Task NodeVisibilityUsesClusterRowsAndDisableFlag()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "world_visibility.vvis_c"));
            var visibility = (VoxelVisibility)resource.GetBlockByType(BlockType.VXVS)!;
            using var scene = new Scene(null!);
            scene.WorldVisibility = visibility;

            var center = (visibility.MinBounds + visibility.MaxBounds) / 2f;
            var node = new TestSceneNode(scene)
            {
                LocalBoundingBox = new AABB(center - Vector3.One, center + Vector3.One),
            };

            scene.CurrentFramePvs = [0];
            await Assert.That(scene.IsNodeInPvs(node)).IsFalse();

            node.Flags = ObjectTypeFlags.DisableVisCulling;
            await Assert.That(scene.IsNodeInPvs(node)).IsTrue();
        }

        private sealed class TestSceneNode(Scene scene) : SceneNode(scene)
        {
        }
    }
}
