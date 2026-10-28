using System.Threading.Tasks;
using ValveResourceFormat.Renderer.Input;

namespace Tests.Renderer
{
    /// <summary>
    /// The right-drag pan moves the orbit target in the plane of the screen. It used to read only the
    /// horizontal mouse delta, so dragging the mouse up or down moved nothing.
    /// </summary>
    public class OrbitPanTest
    {
        private const float DragPixels = 10f;

        // The pan follows the pointer delta and the orbit distance only, so every axis moves by this
        // many units regardless of the frame rate the drag is sampled at
        private const float ExpectedOffset = DragPixels * OrbitInputHarness.Distance / 600f;
        private const float Tolerance = 1e-3f;

        [Test]
        public async Task VerticalDragLiftsTheView()
        {
            using var orbit = new OrbitInputHarness();

            var (delta, right, up, distance) = orbit.Drag(new Vector2(0f, DragPixels));

            // Dragging down has to move the camera up, so the model follows the pointer
            await Assert.That(Vector3.Dot(delta, up)).IsEqualTo(ExpectedOffset).Within(Tolerance);
            await Assert.That(Vector3.Dot(delta, right)).IsEqualTo(0f).Within(Tolerance);
            await Assert.That(distance).IsEqualTo(OrbitInputHarness.Distance).Within(Tolerance);
        }

        [Test]
        public async Task HorizontalDragSlidesTheView()
        {
            using var orbit = new OrbitInputHarness();

            var (delta, right, up, distance) = orbit.Drag(new Vector2(DragPixels, 0f));

            await Assert.That(Vector3.Dot(delta, right)).IsEqualTo(-ExpectedOffset).Within(Tolerance);
            await Assert.That(Vector3.Dot(delta, up)).IsEqualTo(0f).Within(Tolerance);
            await Assert.That(distance).IsEqualTo(OrbitInputHarness.Distance).Within(Tolerance);
        }

        [Test]
        public async Task DragDistanceDoesNotDependOnFrameRate()
        {
            using var slow = new OrbitInputHarness();
            using var fast = new OrbitInputHarness();

            var (slowDelta, _, _, _) = slow.Drag(new Vector2(DragPixels, DragPixels), frameTime: 1f / 30f);
            var (fastDelta, _, _, _) = fast.Drag(new Vector2(DragPixels, DragPixels), frameTime: 1f / 165f);

            // A long frame must not make the same drag travel further; an idle viewer's first frame
            // carries a long frame time, which used to turn one pointer move into a jump.
            await Assert.That(fastDelta.Length()).IsEqualTo(slowDelta.Length()).Within(Tolerance);
        }

        [Test]
        public async Task DiagonalDragMovesBothAxes()
        {
            using var orbit = new OrbitInputHarness();

            var (delta, right, up, distance) = orbit.Drag(new Vector2(DragPixels, DragPixels));

            await Assert.That(Vector3.Dot(delta, right)).IsEqualTo(-ExpectedOffset).Within(Tolerance);
            await Assert.That(Vector3.Dot(delta, up)).IsEqualTo(ExpectedOffset).Within(Tolerance);
            await Assert.That(delta.Length()).IsEqualTo(ExpectedOffset * MathF.Sqrt(2f)).Within(Tolerance);
            await Assert.That(distance).IsEqualTo(OrbitInputHarness.Distance).Within(Tolerance);
        }

        [Test]
        public async Task PanKeepsTheOrbitDistance()
        {
            using var orbit = new OrbitInputHarness();

            for (var i = 0; i < 20; i++)
            {
                orbit.Drag(new Vector2(15f, -9f));
            }

            await Assert.That(orbit.Input.OrbitDistance).IsEqualTo(OrbitInputHarness.Distance).Within(Tolerance);
            await Assert.That(orbit.Input.Camera.Pitch).IsEqualTo(0f).Within(Tolerance);
            await Assert.That(orbit.Input.Camera.Yaw).IsEqualTo(0f).Within(Tolerance);
        }
    }
}
