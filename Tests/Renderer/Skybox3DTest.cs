using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace Tests.Renderer
{
    /// <summary>
    /// A 3D sky is placed rigidly and drawn through a second camera that applies the scale. These check
    /// that this is equivalent to scaling the sky into the world: for positions, projection and fog.
    /// </summary>
    public class Skybox3DTest
    {
        private const float Tolerance = 1e-3f;

        /// <summary>Both sides go through different matrix products, so the error grows with the value.</summary>
        private static float ToleranceFor(float expected) => MathF.Max(Tolerance, MathF.Abs(expected) * 2e-4f);

        /// <summary>The sky camera position in the sky map, before the reference is applied.</summary>
        private static readonly Vector3 SkyCameraOrigin = new(-96f, 48f, 64f);

        private static readonly float[] Scales = [1f, 16f];

        private static readonly Matrix4x4[] References =
        [
            Matrix4x4.Identity,
            EntityTransformHelper.ToRigidTransformationMatrix(new Vector3(0f, 35f, 0f), new Vector3(1024f, -512f, 96f)),
            EntityTransformHelper.ToRigidTransformationMatrix(new Vector3(12f, -80f, 25f), new Vector3(-256f, 320f, -64f)),
        ];

        private static readonly Vector3[] SkyPoints =
        [
            new(0f, 0f, 0f),
            new(512f, -256f, 128f),
            new(-1024f, 2048f, -320f),
            new(64f, 64f, 900f),
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

        private Skybox3D CreateSkybox(Matrix4x4 reference, float scale, Vector3? authoredOrigin = null, WorldFogInfo? worldFog = null)
        {
            var scene = CreateScene();

            // The loader applies the reference to the whole sky map, the sky camera included
            var origin = Vector3.Transform(authoredOrigin ?? SkyCameraOrigin, reference);

            return new Skybox3D(scene, reference, origin, scale, worldFog ?? new WorldFogInfo(), []);
        }

        private static Vector3 ToNdc(Vector3 position, in Matrix4x4 viewProjection)
        {
            var clip = Vector4.Transform(new Vector4(position, 1f), viewProjection);

            return clip.AsVector3() / clip.W;
        }

        private static async Task AssertClose(Vector3 actual, Vector3 expected, string because)
        {
            await Assert.That(actual.X).IsEqualTo(expected.X).Within(ToleranceFor(expected.X)).Because($"X for {because}");
            await Assert.That(actual.Y).IsEqualTo(expected.Y).Within(ToleranceFor(expected.Y)).Because($"Y for {because}");
            await Assert.That(actual.Z).IsEqualTo(expected.Z).Within(ToleranceFor(expected.Z)).Because($"Z for {because}");
        }

        private static Camera CreateMainCamera(float farPlane)
        {
            var camera = new Camera(90f);
            camera.SetViewportSize(1920, 1080);
            camera.SetLocationPitchYaw(new Vector3(320f, -180f, 72f), float.DegreesToRadians(-10f), float.DegreesToRadians(25f));
            camera.FarPlane = farPlane;
            camera.CreateProjectionMatrix();
            camera.RecalculateMatrices();

            return camera;
        }

        /// <summary>
        /// A sky point maps to where scaling the sky about its camera and then applying the reference
        /// would put it.
        /// </summary>
        [Test]
        public async Task ToWorldLandsWhereScalingTheSkyWould()
        {
            foreach (var reference in References)
            {
                foreach (var scale in Scales)
                {
                    var skybox = CreateSkybox(reference, scale);

                    foreach (var authored in SkyPoints)
                    {
                        var placed = Vector3.Transform(authored, reference);
                        var scaled = Vector3.Transform((authored - SkyCameraOrigin) * scale, reference);

                        await AssertClose(skybox.ToWorld(placed), scaled, $"{authored} at {scale}x");
                    }
                }
            }
        }

        /// <summary>
        /// The sky drawn through its own camera lands on the same pixels and at the same depth as the sky
        /// scaled into the world and drawn through the main camera. The depth matters for the sky depth
        /// range and the shared depth pyramid.
        /// </summary>
        [Test]
        public async Task SkyCameraProjectsLikeTheScaledSkyDoes()
        {
            foreach (var farPlane in new[] { float.PositiveInfinity, 12000f })
            {
                foreach (var reference in References)
                {
                    foreach (var scale in Scales)
                    {
                        var skybox = CreateSkybox(reference, scale);
                        var main = CreateMainCamera(farPlane);
                        var sky = new Camera();

                        skybox.ConfigureCamera(sky, main);

                        foreach (var authored in SkyPoints)
                        {
                            var placed = Vector3.Transform(authored, reference);

                            var inSky = ToNdc(placed, sky.ViewProjectionMatrix);
                            var inWorld = ToNdc(skybox.ToWorld(placed), main.ViewProjectionMatrix);

                            await AssertClose(inSky, inWorld, $"{authored} at {scale}x, far {farPlane}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gradient fog is authored in world units, so it has to give the same result from the distance
        /// and height the sky camera measures.
        /// </summary>
        [Test]
        public async Task GradientFogReadsTheSameThroughTheSkyCamera()
        {
            var skybox = CreateSkybox(References[1], 16f);

            var fog = new SceneGradientFog(skybox.Scene)
            {
                StartDist = 500f,
                EndDist = 6000f,
                HeightStart = 128f,
                HeightEnd = 2048f,
            };

            var world = fog.GetBiasAndScale(FogSpace.World);
            var inSky = fog.GetBiasAndScale(skybox.FogSpace);

            var worldCulling = fog.CullingParams(FogSpace.World);
            var skyCulling = fog.CullingParams(skybox.FogSpace);

            Matrix4x4.Invert(skybox.SkyToWorld, out var worldToSky);

            foreach (var distance in new[] { 0f, 500f, 3000f, 9000f })
            {
                var skyDistance = distance / skybox.Scale;

                await Assert.That(inSky.X + skyDistance * inSky.Z)
                    .IsEqualTo(world.X + distance * world.Z).Within(Tolerance).Because($"fog at {distance}");
            }

            foreach (var height in new[] { -200f, 0f, 512f, 4000f })
            {
                var skyHeight = Vector3.Transform(new Vector3(0f, 0f, height), worldToSky).Z;

                await Assert.That(inSky.Y + skyHeight * inSky.W)
                    .IsEqualTo(world.Y + height * world.W).Within(Tolerance).Because($"height fog at {height}");
            }

            await Assert.That(MathF.Sqrt(skyCulling.X) * skybox.Scale).IsEqualTo(MathF.Sqrt(worldCulling.X)).Within(Tolerance);
            await Assert.That(Vector3.Transform(new Vector3(0f, 0f, worldCulling.Y), worldToSky).Z)
                .IsEqualTo(skyCulling.Y).Within(Tolerance);
        }

        /// <summary>The same for the cubemap fog.</summary>
        [Test]
        public async Task CubemapFogReadsTheSameThroughTheSkyCamera()
        {
            var skybox = CreateSkybox(References[2], 16f);

            var fog = new SceneCubemapFog(skybox.Scene)
            {
                StartDist = 1200f,
                EndDist = 15000f,
                HeightStart = 64f,
                HeightEnd = 3000f,
                UseHeightFog = true,
            };

            var world = fog.OffsetScaleBiasExponent(FogSpace.World);
            var inSky = fog.OffsetScaleBiasExponent(skybox.FogSpace);

            foreach (var distance in new[] { 0f, 1200f, 7000f, 20000f })
            {
                var skyDistance = distance / skybox.Scale;

                await Assert.That(inSky.X + skyDistance * inSky.Y)
                    .IsEqualTo(world.X + distance * world.Y).Within(Tolerance).Because($"cube fog at {distance}");
            }

            Matrix4x4.Invert(skybox.SkyToWorld, out var worldToSky);

            var worldCulling = fog.CullingParams_Opacity(FogSpace.World);
            var skyCulling = fog.CullingParams_Opacity(skybox.FogSpace);

            await Assert.That(MathF.Sqrt(skyCulling.X) * skybox.Scale).IsEqualTo(MathF.Sqrt(worldCulling.X)).Within(Tolerance);
            await Assert.That(Vector3.Transform(new Vector3(0f, 0f, worldCulling.Y), worldToSky).Z)
                .IsEqualTo(skyCulling.Y).Within(Tolerance);
            await Assert.That(skyCulling.W).IsEqualTo(worldCulling.W);
        }

        /// <summary>
        /// A sky at 1:1 is not magnified, so its scene keeps the placement and the marker size it has
        /// without a sky at all, and its fog reads in world units. A map can ask for this, with a
        /// <c>sky_camera</c> at the origin and a <c>scale</c> of 1.
        /// </summary>
        /// <remarks>
        /// The magnified sky alongside is what gives the identity meaning: it is where each of these goes
        /// when the sky does magnify, so none of them is passing on a value nobody ever set.
        /// </remarks>
        [Test]
        public async Task OneToOneSkyIsNotMagnified()
        {
            var noSky = CreateScene();
            var flat = CreateSkybox(Matrix4x4.Identity, 1f, Vector3.Zero);
            var magnified = CreateSkybox(Matrix4x4.Identity, 16f, Vector3.Zero);

            // Where every scene starts, being a map with no 3D sky at all
            await Assert.That(noSky.ToViewerWorld).IsEqualTo(Matrix4x4.Identity);
            await Assert.That(noSky.MarkerScale).IsEqualTo(1f);

            // A 1:1 sky leaves it exactly there
            await Assert.That(flat.Scene.ToViewerWorld).IsEqualTo(noSky.ToViewerWorld);
            await Assert.That(flat.Scene.MarkerScale).IsEqualTo(noSky.MarkerScale);
            await Assert.That(flat.FogSpace).IsEqualTo(FogSpace.World);

            // World space is the identity on the values fog is authored in
            await Assert.That(FogSpace.World.Distance(1234.5f)).IsEqualTo(1234.5f);
            await Assert.That(FogSpace.World.Height(-678.25f)).IsEqualTo(-678.25f);

            // A point is where the viewer already is, so nothing moves at all
            await AssertClose(flat.ToWorld(new Vector3(320f, -180f, 72f)), new Vector3(320f, -180f, 72f), "an unmoved sky");

            // The same sky magnified: placed wider, its markers shrunk to match, and its fog stepped down
            await Assert.That(magnified.Scene.ToViewerWorld).IsEqualTo(Matrix4x4.CreateScale(16f));
            await Assert.That(magnified.Scene.MarkerScale).IsEqualTo(1f / 16f);
            await Assert.That(magnified.FogSpace).IsNotEqualTo(FogSpace.World);
        }

        /// <summary>
        /// The 1:1 sky is not a special case. A rigid <c>skybox_reference</c> still moves it, and moves
        /// it rigidly, and the markers still keep their size, because the reference is not what magnifies
        /// anything.
        /// </summary>
        [Test]
        public async Task OneToOneSkyIsPlacedRigidlyByItsReference()
        {
            foreach (var reference in References)
            {
                var skybox = CreateSkybox(reference, 1f);

                // The scene is placed by its sky, not left where it was
                await Assert.That(skybox.Scene.ToViewerWorld).IsEqualTo(skybox.SkyToWorld)
                    .Because($"viewer placement for reference {reference.Translation}");
                await Assert.That(skybox.Scene.MarkerScale).IsEqualTo(1f)
                    .Because($"marker scale for reference {reference.Translation}");
                await Assert.That(skybox.FogSpace.DistanceScale).IsEqualTo(1f)
                    .Because($"fog distance scale for reference {reference.Translation}");

                // Rigid: the distance between two points survives, only its direction can change
                var moved = Vector3.Transform(Vector3.Transform(SkyPoints[1], reference), skybox.Scene.ToViewerWorld)
                    - Vector3.Transform(Vector3.Transform(SkyPoints[2], reference), skybox.Scene.ToViewerWorld);
                var authored = (SkyPoints[1] - SkyPoints[2]).Length();

                await Assert.That(moved.Length()).IsEqualTo(authored).Within(ToleranceFor(authored))
                    .Because($"point distance for reference {reference.Translation}");
            }
        }

        /// <summary>
        /// At 1:1 the camera the sky is drawn through is the main camera itself: no magnification to
        /// carry, so none of the position and none of the clip planes either.
        /// </summary>
        [Test]
        public async Task OneToOneSkyCameraIsTheMainCamera()
        {
            foreach (var farPlane in new[] { float.PositiveInfinity, 12000f })
            {
                foreach (var reference in References)
                {
                    var skybox = CreateSkybox(reference, 1f);
                    var magnified = CreateSkybox(reference, 16f);
                    var main = CreateMainCamera(farPlane);
                    var sky = new Camera();
                    var magnifiedSky = new Camera();

                    skybox.ConfigureCamera(sky, main);
                    magnified.ConfigureCamera(magnifiedSky, main);

                    // The camera stands where the viewer is, so it maps back onto the main camera
                    await AssertClose(skybox.ToWorld(sky.Location), main.Location, $"sky camera at {reference.Translation}, far {farPlane}");

                    // Clip planes are the magnification, and at 1:1 there is none to carry. The magnified
                    // sky beside it is what shows the division is there to be left out.
                    await Assert.That(sky.NearPlane).IsEqualTo(main.NearPlane).Because($"near plane at {reference.Translation}, far {farPlane}");
                    await Assert.That(sky.FarPlane).IsEqualTo(main.FarPlane).Because($"far plane at {reference.Translation}, far {farPlane}");

                    await Assert.That(magnifiedSky.NearPlane).IsEqualTo(main.NearPlane / 16f).Because($"magnified near plane at {reference.Translation}, far {farPlane}");
                    await Assert.That(magnifiedSky.FarPlane).IsEqualTo(main.FarPlane / 16f).Because($"magnified far plane at {reference.Translation}, far {farPlane}");
                }
            }
        }

        /// <summary>
        /// An entity of the sky map is authored where the sky map stands, so the reference places it, and
        /// the entity the sky camera comes from is the point the reference gives. How far the rest of the
        /// sky's entities sit from it is what the reference's rotation and the scale carry over.
        /// </summary>
        [Test]
        public async Task SkyEntitiesArePlacedByTheReference()
        {
            foreach (var reference in References)
            {
                var skybox = CreateSkybox(reference, 16f);

                // The sky camera entity is the point the skybox_reference places the sky map by
                await AssertClose(skybox.EntityOriginToWorld(SkyCameraOrigin), reference.Translation, $"the sky camera entity for {reference.Translation}");

                foreach (var authored in SkyPoints)
                {
                    var offset = skybox.EntityOriginToWorld(authored) - skybox.EntityOriginToWorld(SkyCameraOrigin);
                    var expected = Vector3.TransformNormal(authored - SkyCameraOrigin, reference) * skybox.Scale;

                    await AssertClose(offset, expected, $"{authored} for {reference.Translation}");
                }
            }
        }

        /// <summary>
        /// A scale of zero or less would have the fog space and the camera's clip planes divide by it, and
        /// a sky without entities is not a sky.
        /// </summary>
        [Test]
        public void SkyboxRejectsWhatItCannotDraw()
        {
            var scene = CreateScene();

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new Skybox3D(scene, Matrix4x4.Identity, Vector3.Zero, 0f, new WorldFogInfo(), []));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new Skybox3D(scene, Matrix4x4.Identity, Vector3.Zero, -1f, new WorldFogInfo(), []));
            Assert.ThrowsExactly<ArgumentNullException>(() => _ = new Skybox3D(scene, Matrix4x4.Identity, Vector3.Zero, 1f, new WorldFogInfo(), null!));
            Assert.ThrowsExactly<ArgumentNullException>(() => _ = new Skybox3D(null!, Matrix4x4.Identity, Vector3.Zero, 1f, new WorldFogInfo(), []));
        }

        /// <summary>
        /// The fog a view draws with is written into the view constants in that view's own space, which is
        /// what the sky camera measures, not the space the map authored it in.
        /// </summary>
        [Test]
        public async Task SkyFogUniformsAreWrittenInSkySpace()
        {
            var skybox = CreateSkybox(References[1], 16f);

            var fog = new SceneGradientFog(skybox.Scene)
            {
                StartDist = 500f,
                EndDist = 6000f,
                HeightStart = 128f,
                HeightEnd = 2048f,
            };

            var info = new WorldFogInfo
            {
                GradientFogActive = true,
                GradientFog = fog,
            };

            var inSky = new ViewConstants();
            info.SetFogUniforms(inSky, viewerFogEnabled: true, skybox.FogSpace);

            var inWorld = new ViewConstants();
            info.SetFogUniforms(inWorld, viewerFogEnabled: true, FogSpace.World);

            await Assert.That(inSky.GradientFogActive).IsTrue();
            await Assert.That(inSky.GradientFogBiasAndScale).IsEqualTo(fog.GetBiasAndScale(skybox.FogSpace));
            await Assert.That(inSky.GradientFogCullingParams).IsEqualTo(fog.CullingParams(skybox.FogSpace));

            // The sky camera is closer to things and lower down, so the same authored fog has to read
            // differently there, or the conversion did nothing
            await Assert.That(inSky.GradientFogCullingParams).IsNotEqualTo(inWorld.GradientFogCullingParams);
        }

        /// <summary>
        /// The sky is drawn with its own gradient fog when it has an active one and with the map's
        /// otherwise, and always with the map's cubemap fog: the map is the world the viewer stands in, and
        /// the cubemap fog comes with it.
        /// </summary>
        [Test]
        public async Task SkyTakesItsOwnGradientFogOverTheWorldsCubeFog()
        {
            var worldScene = CreateScene();
            var worldGradient = new SceneGradientFog(worldScene) { StartDist = 100f, EndDist = 2000f };
            var worldCube = new SceneCubemapFog(worldScene) { StartDist = 300f, EndDist = 4000f };

            var skyScene = CreateScene();
            var skyGradient = new SceneGradientFog(skyScene) { StartDist = 7000f, EndDist = 9000f };
            var skyCube = new SceneCubemapFog(skyScene) { StartDist = 11000f, EndDist = 13000f };

            Skybox3D Draw(WorldFogInfo world, bool skyGradientActive, bool skyCubeActive)
            {
                skyScene.FogInfo.GradientFog = skyGradient;
                skyScene.FogInfo.GradientFogActive = skyGradientActive;
                skyScene.FogInfo.CubemapFog = skyCube;
                skyScene.FogInfo.CubeFogActive = skyCubeActive;

                return new Skybox3D(skyScene, Matrix4x4.Identity, Vector3.Zero, 16f, world, []);
            }

            var withFog = new WorldFogInfo
            {
                GradientFogActive = true,
                GradientFog = worldGradient,
                CubeFogActive = true,
                CubemapFog = worldCube,
            };

            // A sky map whose fog is all inactive draws exactly what the map draws
            var inherited = Draw(withFog, skyGradientActive: false, skyCubeActive: false);

            await Assert.That(inherited.FogInfo.GradientFogActive).IsTrue();
            await Assert.That(inherited.FogInfo.GradientFog).IsSameReferenceAs(worldGradient);
            await Assert.That(inherited.FogInfo.CubeFogActive).IsTrue();
            await Assert.That(inherited.FogInfo.CubemapFog).IsSameReferenceAs(worldCube);

            // Its own gradient fog when it has an active one, and the map's cubemap fog either way
            var own = Draw(withFog, skyGradientActive: true, skyCubeActive: true);

            await Assert.That(own.FogInfo.GradientFogActive).IsTrue();
            await Assert.That(own.FogInfo.GradientFog).IsSameReferenceAs(skyGradient);
            await Assert.That(own.FogInfo.CubeFogActive).IsTrue();
            await Assert.That(own.FogInfo.CubemapFog).IsSameReferenceAs(worldCube)
                .Because("the map's cubemap fog wins even over the sky map's own");

            // A sky map with fog of its own draws fog even where the map has none
            var bare = new WorldFogInfo();
            var skyOnly = Draw(bare, skyGradientActive: true, skyCubeActive: true);

            await Assert.That(skyOnly.FogInfo.GradientFogActive).IsTrue();
            await Assert.That(skyOnly.FogInfo.GradientFog).IsSameReferenceAs(skyGradient);
            await Assert.That(skyOnly.FogInfo.CubeFogActive).IsFalse().Because("the map has no cubemap fog");
            await Assert.That(skyOnly.FogInfo.CubemapFog).IsNull().Because("the map has no cubemap fog");

            // Neither map nor sky map has fog, so the sky draws none
            var none = Draw(bare, skyGradientActive: false, skyCubeActive: false);

            await Assert.That(none.FogInfo.GradientFogActive).IsFalse();
            await Assert.That(none.FogInfo.GradientFog).IsNull();
            await Assert.That(none.FogInfo.CubeFogActive).IsFalse();
            await Assert.That(none.FogInfo.CubemapFog).IsNull();
        }

        /// <summary>
        /// The overload every existing caller uses still writes world space, so nothing that draws a map
        /// changes what its fog looks like: a 3D sky is drawn through a path of its own, not by scaling
        /// this one.
        /// </summary>
        [Test]
        public async Task SceneFogConstantsAreStillWrittenInWorldSpace()
        {
            var scene = CreateScene();
            var fog = new SceneGradientFog(scene)
            {
                StartDist = 500f,
                EndDist = 6000f,
                HeightStart = 128f,
                HeightEnd = 2048f,
            };

            scene.FogInfo.GradientFog = fog;
            scene.FogInfo.GradientFogActive = true;

            var viewConstants = new ViewConstants();
            scene.SetFogConstants(viewConstants);

            await Assert.That(viewConstants.GradientFogActive).IsTrue();
            await Assert.That(viewConstants.GradientFogBiasAndScale).IsEqualTo(fog.GetBiasAndScale(FogSpace.World));
            await Assert.That(viewConstants.GradientFogCullingParams).IsEqualTo(fog.CullingParams(FogSpace.World));
            await Assert.That(viewConstants.GradientFogColor_Opacity).IsEqualTo(fog.Color_Opacity);
        }
    }
}
