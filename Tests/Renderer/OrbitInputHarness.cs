using System;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Input;

namespace Tests.Renderer
{
    /// <summary>
    /// Drives the real <see cref="UserInput"/> orbit controls without a GL context, the way
    /// <see cref="PlayerMovementTest"/> drives the movement code. The camera starts a hundred units
    /// behind the origin looking along +X, so the camera basis is the world basis and the orbit target
    /// is already framed.
    /// </summary>
    internal sealed class OrbitInputHarness : IDisposable
    {
        /// <summary>Distance from the camera to the orbit target, in world units.</summary>
        public const float Distance = 100f;

        /// <summary>Frame time used for a tick unless the test asks for another one.</summary>
        public const float FrameTime = 1f / 60f;

        private readonly GameFileLoader fileLoader;
        private readonly RendererContext context;
        private readonly ValveResourceFormat.Renderer.Renderer renderer;

        /// <summary>The input being driven, with orbit mode always on.</summary>
        public UserInput Input { get; }

        public OrbitInputHarness()
        {
            fileLoader = new GameFileLoader(null, null);
            context = new RendererContext(fileLoader, NullLogger.Instance);
            renderer = new ValveResourceFormat.Renderer.Renderer(context);
            Input = new UserInput(renderer);

            Input.Camera.SetLocationPitchYaw(new Vector3(-Distance, 0, 0), 0f, 0f);
            Input.Camera.RecalculateDirectionVectors();
            Input.OrbitTarget = Vector3.Zero;
            Input.OrbitModeAlways = true;
        }

        /// <summary>Runs one tick with the given mouse delta and reports how the camera moved.</summary>
        public (Vector3 Delta, Vector3 Right, Vector3 Up, float Distance) Drag(
            Vector2 mouseDelta,
            TrackedKeys button = TrackedKeys.MouseRight,
            float frameTime = FrameTime)
        {
            var before = Input.Camera.Location;

            Input.Tick(frameTime, button, mouseDelta, new Camera());

            return (Input.Camera.Location - before, Input.Camera.Right, Input.Camera.Up, Input.OrbitDistance);
        }

        public void Dispose()
        {
            renderer.Dispose();
            context.Dispose();
            fileLoader.Dispose();
        }
    }
}
