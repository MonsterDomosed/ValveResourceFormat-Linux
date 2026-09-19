using System.Collections.Generic;
using System.Threading.Tasks;
using GUI.Linux.Viewers;

namespace Tests
{
    /// <summary>
    /// Covers the thread-safe animation bridge used by the Linux model viewer. The commands and
    /// snapshots are what the Avalonia controls and the render thread exchange, so their clamping,
    /// defaults and clearing behavior are verified here without a GL context.
    /// </summary>
    public class ModelAnimationSessionTest
    {
        [Test]
        public async Task DefaultsToAutoplayAndLoop()
        {
            var session = new ModelAnimationSession();
            var snapshot = session.GetSnapshot();

            using (Assert.Multiple())
            {
                await Assert.That(snapshot.Ready).IsFalse();
                await Assert.That(snapshot.HasAnimations).IsFalse();
                await Assert.That(snapshot.Playing).IsTrue();
                await Assert.That(snapshot.Looping).IsTrue();
                await Assert.That(snapshot.Speed).IsEqualTo(1f);
                await Assert.That(snapshot.ActiveAnimation).IsNull();
            }
        }

        [Test]
        public async Task ConsumesEveryPendingCommandThenClearsThem()
        {
            var session = new ModelAnimationSession();

            session.SelectAnimation("walk");
            session.SetPlaying(false);
            session.SetLooping(false);
            session.SetSpeed(2f);
            session.ScrubTo(0.5f);
            session.Restart();
            session.ResetView();

            var command = session.ConsumeCommands();

            using (Assert.Multiple())
            {
                await Assert.That(command.AnimationChanged).IsTrue();
                await Assert.That(command.AnimationName).IsEqualTo("walk");
                await Assert.That(command.PlayingChanged).IsTrue();
                await Assert.That(command.Playing).IsFalse();
                await Assert.That(command.LoopingChanged).IsTrue();
                await Assert.That(command.Looping).IsFalse();
                await Assert.That(command.SpeedChanged).IsTrue();
                await Assert.That(command.Speed).IsEqualTo(2f);
                await Assert.That(command.SeekRequested).IsTrue();
                await Assert.That(command.SeekFraction).IsEqualTo(0.5f);
                await Assert.That(command.RestartRequested).IsTrue();
                await Assert.That(command.ResetViewRequested).IsTrue();
            }

            var cleared = session.ConsumeCommands();

            using (Assert.Multiple())
            {
                await Assert.That(cleared.AnimationChanged).IsFalse();
                await Assert.That(cleared.PlayingChanged).IsFalse();
                await Assert.That(cleared.LoopingChanged).IsFalse();
                await Assert.That(cleared.SpeedChanged).IsFalse();
                await Assert.That(cleared.SeekRequested).IsFalse();
                await Assert.That(cleared.RestartRequested).IsFalse();
                await Assert.That(cleared.ResetViewRequested).IsFalse();
            }
        }

        [Test]
        public async Task ClampsSpeedAndSeekToTheirSupportedRanges()
        {
            var session = new ModelAnimationSession();

            session.SetSpeed(99f);
            session.ScrubTo(4f);
            var high = session.ConsumeCommands();

            using (Assert.Multiple())
            {
                await Assert.That(high.Speed).IsEqualTo(ModelAnimationSession.MaxSpeed);
                await Assert.That(high.SeekFraction).IsEqualTo(1f);
            }

            session.SetSpeed(0f);
            session.ScrubTo(-3f);
            var low = session.ConsumeCommands();

            using (Assert.Multiple())
            {
                await Assert.That(low.Speed).IsEqualTo(ModelAnimationSession.MinSpeed);
                await Assert.That(low.SeekFraction).IsEqualTo(0f);
            }
        }

        [Test]
        public async Task SelectingNullReturnsToTheBindPose()
        {
            var session = new ModelAnimationSession();

            session.SelectAnimation(null);
            var command = session.ConsumeCommands();

            using (Assert.Multiple())
            {
                await Assert.That(command.AnimationChanged).IsTrue();
                await Assert.That(command.AnimationName).IsNull();
            }
        }

        [Test]
        public async Task PublishesTheRenderThreadStateToTheUi()
        {
            var session = new ModelAnimationSession();

            session.Publish(
                isReady: true,
                animationNames: ["idle", "walk"],
                active: "walk",
                isPlaying: false,
                isLooping: false,
                playbackSpeed: 2f,
                currentFrame: 5,
                totalFrames: 25,
                currentTime: 0.4f,
                totalDuration: 0.8f,
                framesPerSecond: 30f);

            var snapshot = session.GetSnapshot();

            using (Assert.Multiple())
            {
                await Assert.That(snapshot.Ready).IsTrue();
                await Assert.That(snapshot.HasAnimations).IsTrue();
                await Assert.That(snapshot.Animations.Length).IsEqualTo(2);
                await Assert.That(snapshot.Animations[0]).IsEqualTo("idle");
                await Assert.That(snapshot.Animations[1]).IsEqualTo("walk");
                await Assert.That(snapshot.ActiveAnimation).IsEqualTo("walk");
                await Assert.That(snapshot.Playing).IsFalse();
                await Assert.That(snapshot.Looping).IsFalse();
                await Assert.That(snapshot.Speed).IsEqualTo(2f);
                await Assert.That(snapshot.Frame).IsEqualTo(5);
                await Assert.That(snapshot.FrameCount).IsEqualTo(25);
                await Assert.That(snapshot.Time).IsEqualTo(0.4f);
                await Assert.That(snapshot.Duration).IsEqualTo(0.8f);
                await Assert.That(snapshot.Fps).IsEqualTo(30f);
            }
        }

        [Test]
        public async Task ReportsModelsWithoutAnimations()
        {
            var session = new ModelAnimationSession();

            session.Publish(true, [], null, false, true, 1f, 0, 0, 0f, 0f, 0f);
            var snapshot = session.GetSnapshot();

            using (Assert.Multiple())
            {
                await Assert.That(snapshot.Ready).IsTrue();
                await Assert.That(snapshot.HasAnimations).IsFalse();
                await Assert.That(snapshot.Animations).IsEmpty();
            }
        }
    }
}
