using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Initializers;

namespace Tests
{
    /// <summary>
    /// Control points that know when they last changed, the inputs that read that, and the frame
    /// relative placement of a particle created part way through a frame.
    /// </summary>
    public class ParticleControlPointChangesTest
    {
        [Test]
        public async Task ControlPointRecordsWhenItLastChanged()
        {
            var state = new ParticleSystemState();
            state.Age = 2f;

            var point = state.GetControlPoint(0);
            await Assert.That(point.ChangeTime).IsEqualTo(-1f);
            await Assert.That(point.ChangeAge).IsEqualTo(3f);

            state.SetControlPointValue(0, new Vector3(1f, 0f, 0f));

            using (Assert.Multiple())
            {
                await Assert.That(point.ChangeTime).IsEqualTo(2f);
                await Assert.That(point.ChangeAge).IsZero();
            }

            // Rewriting the same position is not a change.
            state.SetControlPointValue(0, new Vector3(1f, 0f, 0f));
            await Assert.That(point.ChangeTime).IsEqualTo(2f);

            // Replacing the control point object is.
            state.Age = 3f;
            state.SetControlPoint(0, new ControlPoint { Position = new Vector3(5f, 0f, 0f) });
            using (Assert.Multiple())
            {
                await Assert.That(state.GetControlPoint(0).ChangeTime).IsEqualTo(3f);
                await Assert.That(state.GetControlPoint(0).ChangeAge).IsZero();
            }
        }

        [Test]
        public async Task ReplayRewindsControlPointChangesToTheStartOfTheClock()
        {
            var simulation = ParticleTestHarness.Simulation(Definition());
            simulation.Update(0.1f, 0f);

            simulation.GetControlPoint(0).Position = new Vector3(10f, 0f, 0f);
            var point = simulation.GetControlPoint(0);
            await Assert.That(point.ChangeTime).IsEqualTo(0.1f).Within(1e-4f);

            simulation.Replay();

            // The clock is back at zero, so a change carried over from before the replay reads as one
            // that happened at the start rather than in the system's previous life.
            using (Assert.Multiple())
            {
                await Assert.That(simulation.RenderState.Age).IsZero();
                await Assert.That(simulation.GetControlPoint(0).ChangeTime).IsZero();
            }
        }

        [Test]
        public async Task ControlPointIsSetInputReportsWhetherTheControlPointHasMoved()
        {
            var state = new ParticleSystemState();
            state.Age = 4f;

            // The stray literal is what an unsupported input would fall back to, so reading it back
            // would mean the type is not being read at all.
            var data = KVObject.Collection();
            data.Add("m_nOutputField", (int)ParticleField.Radius);
            data.Add("m_InputValue", ParticleTestHarness.FloatInput("PF_TYPE_CONTROL_POINT_IS_SET", ("m_flLiteralValue", 42f)));

            var collection = ParticleTestHarness.Collection(1);
            new InitFloat(ParticleTestHarness.Parser(data)).Initialize(ref collection.Current[0], collection, state);
            await Assert.That(collection.Current[0].Radius).IsZero();

            state.SetControlPointValue(0, new Vector3(1f, 0f, 0f));
            new InitFloat(ParticleTestHarness.Parser(data)).Initialize(ref collection.Current[0], collection, state);
            await Assert.That(collection.Current[0].Radius).IsEqualTo(1f);
        }

        [Test]
        public async Task ControlPointChangeAgeInputReadsTheElapsedTime()
        {
            var state = new ParticleSystemState();
            state.Age = 4f;
            state.SetControlPointValue(0, new Vector3(1f, 0f, 0f));
            state.Age = 9f;

            var data = KVObject.Collection();
            data.Add("m_nOutputField", (int)ParticleField.Radius);
            data.Add("m_InputValue", ParticleTestHarness.FloatInput("PF_TYPE_CONTROL_POINT_CHANGE_AGE", ("m_flLiteralValue", 42f)));

            var collection = ParticleTestHarness.Collection(1);
            new InitFloat(ParticleTestHarness.Parser(data)).Initialize(ref collection.Current[0], collection, state);

            await Assert.That(collection.Current[0].Radius).IsEqualTo(5f);
        }

        [Test]
        public async Task SnapshotCountInputReadsTheSnapshotOnTheControlPoint()
        {
            var data = KVObject.Collection();
            data.Add("m_nOutputField", (int)ParticleField.Radius);
            data.Add("m_InputValue", ParticleTestHarness.FloatInput("PF_TYPE_SNAPSHOT_COUNT", ("m_flLiteralValue", 42f)));

            var state = new ParticleSystemState();
            var collection = ParticleTestHarness.Collection(1);
            new InitFloat(ParticleTestHarness.Parser(data)).Initialize(ref collection.Current[0], collection, state);

            // Nothing publishes a snapshot, so there is nothing to count.
            await Assert.That(collection.Current[0].Radius).IsZero();
        }

        [Test]
        public async Task RendererCameraDistanceInputReadsTheControlPointNotTheParticle()
        {
            var data = KVObject.Collection();
            data.Add("m_nOutputField", (int)ParticleField.Radius);
            data.Add("m_InputValue", ParticleTestHarness.FloatInput("PF_TYPE_RENDERER_CAMERA_DISTANCE", ("m_nControlPoint", 1f)));

            var state = new ParticleSystemState { CameraPosition = Vector3.Zero };
            state.SetControlPointValue(1, new Vector3(0f, 0f, 40f));

            // The particle is 3 units from the camera but shares every particle's answer.
            var collection = ParticleTestHarness.Collection(1, (_, ref p) => p.Position = new Vector3(3f, 0f, 0f));
            new InitFloat(ParticleTestHarness.Parser(data)).Initialize(ref collection.Current[0], collection, state);

            await Assert.That(collection.Current[0].Radius).IsEqualTo(40f);
        }

        [Test]
        public async Task PerParticleInputsAnswerZeroAtCollectionLevel()
        {
            // A remap that turns the default particle's zero attribute into 5, so an input that still
            // reads the default particle at collection level is visible.
            var input = KVObject.Collection();
            input.Add("m_nType", "PF_TYPE_PARTICLE_FLOAT");
            input.Add("m_nScalarAttribute", (int)ParticleField.CreationTime);
            input.Add("m_nMapType", "REMAP");
            input.Add("m_flInput0", 0f);
            input.Add("m_flInput1", 1f);
            input.Add("m_flOutput0", 5f);
            input.Add("m_flOutput1", 9f);

            var data = KVObject.Collection();
            data.Add("m_InputValue", input);

            var particle = new Particle { CreationTime = 7f };
            var perParticle = ParticleTestHarness.Parser(data).NumberProvider("m_InputValue", new LiteralNumberProvider(-1f));
            var state = new ParticleSystemState();

            using (Assert.Multiple())
            {
                await Assert.That(perParticle.NextNumber(ref particle, state)).IsEqualTo(9f);
                await Assert.That(perParticle.NextNumber(state)).IsZero();
                await Assert.That(perParticle.NextNumber()).IsZero();
            }
        }

        [Test]
        public async Task SpawnInterpolatesTheControlPointOverTheFrame()
        {
            var simulation = ParticleTestHarness.Simulation(Definition());
            simulation.GetControlPoint(0).Position = Vector3.Zero;

            // The first frame only runs the simulation and records the control point's previous state.
            simulation.Update(0.1f, 0f);

            // The control point jumps forward between frames, then the second frame emits a particle
            // part way through itself.
            simulation.GetControlPoint(0).Position = new Vector3(100f, 0f, 0f);
            simulation.Update(0.1f, 0.1f);

            var particles = simulation.Particles;
            await Assert.That(particles.Count).IsEqualTo(1);

            // Half way through the frame, so half way from the previous frame's control point to this
            // one's, rather than pinned to where the control point ended up.
            await Assert.That(particles.Current[0].Position.X).IsEqualTo(50f).Within(1f);
        }

        /// <summary>
        /// A system that emits one particle 0.15s in and spawns it on control point 0, simulated in
        /// 0.01s substeps so a frame is several steps long.
        /// </summary>
        private static KVObject Definition()
        {
            var emitter = KVObject.Collection();
            emitter.Add("_class", "C_OP_InstantaneousEmitter");
            emitter.Add("m_nParticlesToEmit", 1);
            emitter.Add("m_flStartTime", 0.15f);

            var emitters = KVObject.Array();
            emitters.Add(emitter);

            var initializer = KVObject.Collection();
            initializer.Add("_class", "C_INIT_CreateWithinSphereTransform");
            initializer.Add("m_TransformInput", ParticleTestHarness.TransformInput(0));
            initializer.Add("m_fRadiusMin", 0f);
            initializer.Add("m_fRadiusMax", 0f);

            var initializers = KVObject.Array();
            initializers.Add(initializer);

            var definition = KVObject.Collection();
            definition.Add("m_nBehaviorVersion", 7);
            definition.Add("m_nMaxParticles", 16);
            definition.Add("m_flMaximumTimeStep", 0.01f);
            definition.Add("m_flPreSimulationTime", 0f);
            definition.Add("m_Emitters", emitters);
            definition.Add("m_Initializers", initializers);
            return definition;
        }
    }
}
