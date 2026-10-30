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
        public async Task HitboxSelectionIsDeliveredOnceAndCanBeCleared()
        {
            var session = new ModelAnimationSession();
            session.Publish(true, [], null, false, true, 1f, 0, 0, 0f, 0f, 0f, ["head", "body"], null, [], true, false, 1, [], 0, true, [], [], [], string.Empty);

            using (Assert.Multiple())
            {
                // Nothing is drawn until a set is picked.
                await Assert.That(session.GetSnapshot().HasHitboxSets).IsTrue();
                await Assert.That(session.GetSnapshot().ActiveHitboxSet).IsNull();
                await Assert.That(session.ConsumeCommands().HitboxSetChanged).IsFalse();
            }

            session.SelectHitboxSet("head");
            var command = session.ConsumeCommands();

            using (Assert.Multiple())
            {
                await Assert.That(command.HitboxSetChanged).IsTrue();
                await Assert.That(command.HitboxSetName).IsEqualTo("head");

                // Consumed once, so the render thread does not re-apply it on every frame.
                await Assert.That(session.ConsumeCommands().HitboxSetChanged).IsFalse();
            }

            session.SelectHitboxSet(null);
            await Assert.That(session.ConsumeCommands().HitboxSetName).IsNull();
        }

        [Test]
        public async Task SkeletonAndAttachmentTogglesAreDeliveredOnce()
        {
            var session = new ModelAnimationSession();
            session.Publish(true, [], null, false, true, 1f, 0, 0, 0f, 0f, 0f, [], null, ["muzzle", "eject"], true, false, 1, [], 0, true, [], [], [], string.Empty);

            using (Assert.Multiple())
            {
                // Bones and attachments are the two ways a model offers these overlays.
                await Assert.That(session.GetSnapshot().HasBones).IsTrue();
                await Assert.That(session.GetSnapshot().HasAttachments).IsTrue();
                await Assert.That(session.ConsumeCommands().SkeletonChanged).IsFalse();
            }

            session.SetShowBones(true);
            session.SetShowAttachments(true);
            session.SelectAttachments(["muzzle"]);

            var command = session.ConsumeCommands();

            using (Assert.Multiple())
            {
                await Assert.That(command.SkeletonChanged).IsTrue();
                await Assert.That(command.ShowBones).IsTrue();
                await Assert.That(command.AttachmentsChanged).IsTrue();
                await Assert.That(command.ShowAttachments).IsTrue();
                await Assert.That(command.SelectedAttachments).IsEquivalentTo(["muzzle"]);

                var second = session.ConsumeCommands();
                await Assert.That(second.SkeletonChanged).IsFalse();
                await Assert.That(second.AttachmentsChanged).IsFalse();
                await Assert.That(second.SelectedAttachments).IsNull();
            }
        }

        [Test]
        public async Task RootMotionToggleIsDeliveredOnceAndGatedOnTheAnimation()
        {
            var session = new ModelAnimationSession();
            session.Publish(true, ["walk"], "walk", true, true, 1f, 0, 25, 0f, 0.8f, 30f, [], null, [], true, true, 1, [], 0, true, [], [], [], string.Empty);

            // Only an animation that carries movement can be walked.
            await Assert.That(session.GetSnapshot().HasRootMotion).IsTrue();

            session.SetWalkWithRootMotion(true);
            var command = session.ConsumeCommands();

            using (Assert.Multiple())
            {
                await Assert.That(command.RootMotionChanged).IsTrue();
                await Assert.That(command.WalkWithRootMotion).IsTrue();
                await Assert.That(session.ConsumeCommands().RootMotionChanged).IsFalse();
            }

            session.Publish(true, ["walk"], "walk", true, true, 1f, 0, 25, 0f, 0.8f, 30f, [], null, [], true, false, 1, [], 0, true, [], [], [], string.Empty);
            await Assert.That(session.GetSnapshot().HasRootMotion).IsFalse();
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
                framesPerSecond: 30f,
                hitboxSetNames: ["head", "body"],
                activeSet: null,
                attachmentNames: ["muzzle"],
                modelHasBones: true,
                activeAnimationHasRootMotion: true,
                totalLods: 4,
                availableLods: [0, 1, 3],
                currentLod: 1,
                isAutoLod: false,
                meshGroupNames: ["body", "head"],
                activeGroups: ["body"],
                materialGroupNames: ["skin_1", "skin_2"],
                currentMaterialGroup: "skin_1");

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
                await Assert.That(snapshot.HasHitboxSets).IsTrue();
                await Assert.That(snapshot.HitboxSets[0]).IsEqualTo("head");
                await Assert.That(snapshot.ActiveHitboxSet).IsNull();
                await Assert.That(snapshot.HasAttachments).IsTrue();
                await Assert.That(snapshot.HasMultipleLods).IsTrue();
                await Assert.That(snapshot.ActiveLod).IsEqualTo(1);
                await Assert.That(snapshot.AutoLod).IsFalse();
                await Assert.That(snapshot.LodLevels.Length).IsEqualTo(3);
                await Assert.That(snapshot.HasMeshGroups).IsTrue();
                await Assert.That(snapshot.ActiveMeshGroups.Length).IsEqualTo(1);
                await Assert.That(snapshot.HasMaterialGroups).IsTrue();
                await Assert.That(snapshot.MaterialGroups.Length).IsEqualTo(2);
                await Assert.That(snapshot.ActiveMaterialGroup).IsEqualTo("skin_1");
                await Assert.That(snapshot.Attachments[0]).IsEqualTo("muzzle");
                await Assert.That(snapshot.HasBones).IsTrue();
                await Assert.That(snapshot.HasRootMotion).IsTrue();
            }
        }

        [Test]
        public async Task DetailCommandsAreDeliveredOnce()
        {
            var session = new ModelAnimationSession();

            session.SetLod(3);
            session.SetMeshGroups(["body"]);
            session.SetMaterialGroup("skin_2");

            var command = session.ConsumeCommands();

            await Assert.That(command.LodChanged).IsTrue();
            await Assert.That(command.LodOverride).IsEqualTo(3);
            await Assert.That(command.MeshGroupsChanged).IsTrue();
            await Assert.That(command.MeshGroups).IsEquivalentTo(["body"]);
            await Assert.That(command.MaterialGroupChanged).IsTrue();
            await Assert.That(command.MaterialGroupName).IsEqualTo("skin_2");

            var empty = session.ConsumeCommands();

            await Assert.That(empty.LodChanged).IsFalse();
            await Assert.That(empty.MeshGroupsChanged).IsFalse();
            await Assert.That(empty.MaterialGroupChanged).IsFalse();
        }

        [Test]
        public async Task AutoLodIsSentAsMinusOne()
        {
            var session = new ModelAnimationSession();

            session.SetLod(null);

            await Assert.That(session.ConsumeCommands().LodOverride).IsEqualTo(-1);
        }

        [Test]
        public async Task ReportsModelsWithoutAnimations()
        {
            var session = new ModelAnimationSession();

            session.Publish(true, [], null, false, true, 1f, 0, 0, 0f, 0f, 0f, [], null, [], false, false, 0, [], 0, true, [], [], [], string.Empty);
            var snapshot = session.GetSnapshot();

            using (Assert.Multiple())
            {
                await Assert.That(snapshot.Ready).IsTrue();
                await Assert.That(snapshot.HasAnimations).IsFalse();
                await Assert.That(snapshot.Animations).IsEmpty();
                await Assert.That(snapshot.HasHitboxSets).IsFalse();
            }
        }
    }
}
