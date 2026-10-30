using System.Threading.Tasks;
using GUI.Linux.Types.GLViewers;
using ValveResourceFormat.Renderer.SceneNodes;

namespace Tests;

/// <summary>
/// The particle controls are absolute playback state plus a restart counter, so the renderer can
/// apply them from its frame without missing a press.
/// </summary>
public class ParticleSessionTest
{
    [Test]
    public async Task StartsPlayingOnTheWholeEffect()
    {
        var session = new ParticleSession();
        var state = session.GetState();

        await Assert.That(state.Playing).IsTrue();
        await Assert.That(state.Looping).IsTrue();
        await Assert.That(state.Mode).IsEqualTo(ParticlePlaybackMode.NormalWithEndCap);
        await Assert.That(state.RestartCount).IsEqualTo(0);
    }

    [Test]
    public async Task RestartCountsAndResumes()
    {
        var session = new ParticleSession();
        session.SetPlaying(false);

        session.Restart();
        session.Restart();

        var state = session.GetState();

        await Assert.That(state.RestartCount).IsEqualTo(2);
        await Assert.That(state.Playing).IsTrue();
    }

    [Test]
    public async Task PlaybackChangesWakeTheViewer()
    {
        var session = new ParticleSession();
        var wakes = 0;
        session.RequestFrame = () => wakes++;

        session.SetPlaying(false);
        session.SetLooping(false);
        session.SetMode(ParticlePlaybackMode.EndCapOnly);

        await Assert.That(wakes).IsEqualTo(3);
        await Assert.That(session.GetState().Mode).IsEqualTo(ParticlePlaybackMode.EndCapOnly);
        await Assert.That(session.GetState().Looping).IsFalse();
    }
}
