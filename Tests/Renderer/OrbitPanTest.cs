using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Input;

namespace Tests.Renderer
{
    /// <summary>
    /// The right-drag pan moves the orbit target in the plane of the screen. It used to read only the
    /// horizontal mouse delta, so dragging the mouse up or down moved nothing.
    ///
    /// The harness drives the real <see cref="UserInput"/> without a GL context, the same way
    /// <see cref="PlayerMovementTest"/> does.
    /// </summary>
    public class OrbitPanTest
    {
        private const float Distance = 100f;
        private const float FrameTime = 1f / 60f;
        private const float DragPixels = 10f;

        // The pan follows the pointer delta and the orbit distance only, so every axis moves by this
        // many units regardless of the frame rate the drag is sampled at
        private const float ExpectedOffset = DragPixels * Distance / 330f;
        private const float Tolerance = 1e-3f;

        private sealed class Harness : IDisposable
        {
            private readonly GameFileLoader FileLoader;
            private readonly RendererContext Context;
            private readonly ValveResourceFormat.Renderer.Renderer Renderer;

            public UserInput Input { get; }

            public Harness()
            {
                FileLoader = new GameFileLoader(null, null);
                Context = new RendererContext(FileLoader, NullLogger.Instance);
                Renderer = new ValveResourceFormat.Renderer.Renderer(Context);
                Input = new UserInput(Renderer);

                // Sit a hundred units behind the origin looking along +X, so the camera basis is the
                // world basis and the orbit target is already framed
                Input.Camera.SetLocationPitchYaw(new Vector3(-Distance, 0, 0), 0f, 0f);
                Input.Camera.RecalculateDirectionVectors();
                Input.OrbitTarget = Vector3.Zero;
                Input.OrbitModeAlways = true;
            }

            /// <summary>Drags with the right button held and returns how the camera moved.</summary>
            public (Vector3 Delta, Vector3 Right, Vector3 Up, float Distance) Drag(Vector2 mouseDelta, float frameTime = FrameTime)
            {
                var before = Input.Camera.Location;

                Input.Tick(frameTime, TrackedKeys.MouseRight, mouseDelta, new Camera());

                return (Input.Camera.Location - before, Input.Camera.Right, Input.Camera.Up, Input.OrbitDistance);
            }

            public void Dispose()
            {
                Renderer.Dispose();
                Context.Dispose();
                FileLoader.Dispose();
            }
        }

        [Test]
        public async Task VerticalDragLiftsTheView()
        {
            using var harness = new Harness();

            var (delta, right, up, distance) = harness.Drag(new Vector2(0f, DragPixels));

            // Dragging down has to move the camera up, so the model follows the pointer
            await Assert.That(Vector3.Dot(delta, up)).IsEqualTo(ExpectedOffset).Within(Tolerance);
            await Assert.That(Vector3.Dot(delta, right)).IsEqualTo(0f).Within(Tolerance);
            await Assert.That(distance).IsEqualTo(Distance).Within(Tolerance);
        }

        [Test]
        public async Task HorizontalDragSlidesTheView()
        {
            using var harness = new Harness();

            var (delta, right, up, distance) = harness.Drag(new Vector2(DragPixels, 0f));

            await Assert.That(Vector3.Dot(delta, right)).IsEqualTo(-ExpectedOffset).Within(Tolerance);
            await Assert.That(Vector3.Dot(delta, up)).IsEqualTo(0f).Within(Tolerance);
            await Assert.That(distance).IsEqualTo(Distance).Within(Tolerance);
        }

        [Test]
        public async Task DragDistanceDoesNotDependOnFrameRate()
        {
            using var slow = new Harness();
            using var fast = new Harness();

            var (slowDelta, _, _, _) = slow.Drag(new Vector2(DragPixels, DragPixels), 1f / 30f);
            var (fastDelta, _, _, _) = fast.Drag(new Vector2(DragPixels, DragPixels), 1f / 165f);

            // A long frame must not make the same drag travel further; an idle viewer's first frame
            // carries a long frame time, which used to turn one pointer move into a jump.
            await Assert.That(fastDelta.Length()).IsEqualTo(slowDelta.Length()).Within(Tolerance);
        }

        [Test]
        public async Task DiagonalDragMovesBothAxes()
        {
            using var harness = new Harness();

            var (delta, right, up, distance) = harness.Drag(new Vector2(DragPixels, DragPixels));

            await Assert.That(Vector3.Dot(delta, right)).IsEqualTo(-ExpectedOffset).Within(Tolerance);
            await Assert.That(Vector3.Dot(delta, up)).IsEqualTo(ExpectedOffset).Within(Tolerance);
            await Assert.That(delta.Length()).IsEqualTo(ExpectedOffset * MathF.Sqrt(2f)).Within(Tolerance);
            await Assert.That(distance).IsEqualTo(Distance).Within(Tolerance);
        }
    }
}
