using System;
using System.Threading;
using ValveResourceFormat.Renderer.SceneNodes;

namespace GUI.Linux.Types.GLViewers;

/// <summary>The particle playback controls' current state.</summary>
internal readonly record struct ParticleSessionState(bool Playing, bool Looping, ParticlePlaybackMode Mode, int RestartCount);

/// <summary>
/// Thread-safe bridge between the particle viewer's playback controls and its scene core. Restarting
/// is counted rather than flagged, so two quick presses are two restarts.
/// </summary>
internal sealed class ParticleSession
{
    private readonly Lock sync = new();

    private ParticleSessionState state = new(
        Playing: true,
        Looping: true,
        Mode: ParticlePlaybackMode.NormalWithEndCap,
        RestartCount: 0);

    /// <summary>Called after a change so an idle viewer redraws.</summary>
    public Action? RequestFrame { get; set; }

    /// <summary>The current state, safe to read from either thread.</summary>
    public ParticleSessionState GetState()
    {
        lock (sync)
        {
            return state;
        }
    }

    /// <summary>Starts or pauses the simulation.</summary>
    public void SetPlaying(bool playing) => Update(current => current with { Playing = playing });

    /// <summary>Loops the effect instead of stopping at the end.</summary>
    public void SetLooping(bool looping) => Update(current => current with { Looping = looping });

    /// <summary>Selects what the effect plays: the whole thing with its endcap, without it, or only the endcap.</summary>
    public void SetMode(ParticlePlaybackMode mode) => Update(current => current with { Mode = mode });

    /// <summary>Restarts the effect from the beginning and resumes it.</summary>
    public void Restart() => Update(current => current with { RestartCount = current.RestartCount + 1, Playing = true });

    private void Update(Func<ParticleSessionState, ParticleSessionState> change)
    {
        lock (sync)
        {
            state = change(state);
        }

        RequestFrame?.Invoke();
    }
}
