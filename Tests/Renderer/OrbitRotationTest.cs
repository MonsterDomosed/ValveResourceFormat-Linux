using System.Threading.Tasks;
using ValveResourceFormat.Renderer.Input;

namespace Tests.Renderer
{
    /// <summary>
    /// Orbiting goes over the poles the way a turntable does. The camera used to stop at straight up or
    /// down, so dragging further tilted nothing and the view felt stuck.
    /// </summary>
    public class OrbitRotationTest
    {
        // A hundred pixels of vertical drag turns the view a little over two degrees
        private const float PixelsPerTick = 100f;

        private static void DragOver(OrbitInputHarness orbit, int ticks, float pixels = PixelsPerTick)
        {
            for (var i = 0; i < ticks; i++)
            {
                orbit.Drag(new Vector2(0f, pixels), TrackedKeys.MouseLeft);
            }
        }

        [Test]
        public async Task VerticalDragPassesOverThePole()
        {
            using var orbit = new OrbitInputHarness();

            // Far enough that the pitch would have stopped at a quarter turn before
            DragOver(orbit, 60);

            await Assert.That(MathF.Abs(orbit.Input.Camera.Pitch)).IsGreaterThan(MathF.PI / 2f);

            // Past the pole the camera looks at the model from the other side, upside down
            await Assert.That(orbit.Input.Camera.Up.Z).IsLessThan(0f);
        }

        [Test]
        public async Task RotationKeepsAccumulatingWithoutBound()
        {
            using var orbit = new OrbitInputHarness();

            DragOver(orbit, 60);
            var afterPole = orbit.Input.Camera.Pitch;

            DragOver(orbit, 60);
            var afterMore = orbit.Input.Camera.Pitch;

            // The second drag has to keep turning instead of sitting at a limit
            await Assert.That(MathF.Abs(afterMore)).IsGreaterThan(MathF.Abs(afterPole));
        }

        [Test]
        public async Task AnglesWrapInsteadOfGrowingForever()
        {
            using var orbit = new OrbitInputHarness();

            DragOver(orbit, 4000);

            // sin and cos of a hundred thousand radians are not the ones asked for
            await Assert.That(MathF.Abs(orbit.Input.Camera.Pitch)).IsLessThanOrEqualTo(MathF.Tau);
            await Assert.That(MathF.Abs(orbit.Input.Camera.Yaw)).IsLessThanOrEqualTo(MathF.PI);
        }
    }
}
