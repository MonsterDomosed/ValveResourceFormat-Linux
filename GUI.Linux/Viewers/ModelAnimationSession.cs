namespace GUI.Linux.Viewers;

/// <summary>
/// Animation commands the UI sends to the render thread for a model viewer. One value is produced
/// per frame; the flags say which fields the render thread should apply.
/// </summary>
internal readonly record struct ModelAnimationCommand(
    bool AnimationChanged,
    string? AnimationName,
    bool PlayingChanged,
    bool Playing,
    bool LoopingChanged,
    bool Looping,
    bool SpeedChanged,
    float Speed,
    bool SeekRequested,
    float SeekFraction,
    bool RestartRequested,
    bool ResetViewRequested);

/// <summary>
/// A consistent copy of the model viewer's animation state, safe to read from the UI thread.
/// </summary>
internal readonly record struct ModelAnimationSnapshot(
    bool Ready,
    string[] Animations,
    string? ActiveAnimation,
    bool Playing,
    bool Looping,
    float Speed,
    int Frame,
    int FrameCount,
    float Time,
    float Duration,
    float Fps)
{
    /// <summary>Whether the model exposes at least one playable animation.</summary>
    public bool HasAnimations => Animations.Length > 0;
}

/// <summary>
/// Thread-safe bridge between the Avalonia animation controls (UI thread) and the model scene core
/// (render thread). The UI only queues commands and reads snapshots; the render thread owns the
/// <c>AnimationController</c> and applies the commands during its own frame update.
/// </summary>
internal sealed class ModelAnimationSession
{
    /// <summary>Lowest playback speed the UI offers.</summary>
    public const float MinSpeed = 0.1f;

    /// <summary>Highest playback speed the UI offers.</summary>
    public const float MaxSpeed = 3f;

    private readonly object sync = new();

    private string[] animations = [];
    private bool ready;
    private string? activeAnimation;
    private bool playing = true;
    private bool looping = true;
    private float speed = 1f;
    private int frame;
    private int frameCount;
    private float time;
    private float duration;
    private float fps;

    private bool animationChanged;
    private string? pendingAnimation;
    private bool playingChanged;
    private bool pendingPlaying;
    private bool loopingChanged;
    private bool pendingLooping;
    private bool speedChanged;
    private float pendingSpeed;
    private bool seekRequested;
    private float pendingSeek;
    private bool restartRequested;
    private bool resetViewRequested;

    /// <summary>Selects an animation by name, or the bind pose when <paramref name="name"/> is null.</summary>
    public void SelectAnimation(string? name)
    {
        lock (sync)
        {
            animationChanged = true;
            pendingAnimation = name;
        }
    }

    /// <summary>Requests that playback pause or resume.</summary>
    public void SetPlaying(bool value)
    {
        lock (sync)
        {
            playingChanged = true;
            pendingPlaying = value;
        }
    }

    /// <summary>Requests looping on or off.</summary>
    public void SetLooping(bool value)
    {
        lock (sync)
        {
            loopingChanged = true;
            pendingLooping = value;
        }
    }

    /// <summary>Requests a playback speed, clamped to the supported range.</summary>
    public void SetSpeed(float value)
    {
        lock (sync)
        {
            speedChanged = true;
            pendingSpeed = Math.Clamp(value, MinSpeed, MaxSpeed);
        }
    }

    /// <summary>Requests a seek to a fraction of the animation cycle, clamped to [0, 1].</summary>
    public void ScrubTo(float fraction)
    {
        lock (sync)
        {
            seekRequested = true;
            pendingSeek = Math.Clamp(fraction, 0f, 1f);
        }
    }

    /// <summary>Requests that the active animation restart from its beginning.</summary>
    public void Restart()
    {
        lock (sync)
        {
            restartRequested = true;
        }
    }

    /// <summary>Requests that the camera reframe the model.</summary>
    public void ResetView()
    {
        lock (sync)
        {
            resetViewRequested = true;
        }
    }

    /// <summary>Takes the pending commands and clears them. Called on the render thread each frame.</summary>
    public ModelAnimationCommand ConsumeCommands()
    {
        lock (sync)
        {
            var command = new ModelAnimationCommand(
                animationChanged, pendingAnimation,
                playingChanged, pendingPlaying,
                loopingChanged, pendingLooping,
                speedChanged, pendingSpeed,
                seekRequested, pendingSeek,
                restartRequested, resetViewRequested);

            animationChanged = false;
            pendingAnimation = null;
            playingChanged = false;
            loopingChanged = false;
            speedChanged = false;
            seekRequested = false;
            restartRequested = false;
            resetViewRequested = false;

            return command;
        }
    }

    /// <summary>Publishes the render thread's current animation state for the UI. Allocation-free.</summary>
    public void Publish(
        bool isReady,
        string[] animationNames,
        string? active,
        bool isPlaying,
        bool isLooping,
        float playbackSpeed,
        int currentFrame,
        int totalFrames,
        float currentTime,
        float totalDuration,
        float framesPerSecond)
    {
        lock (sync)
        {
            ready = isReady;
            animations = animationNames;
            activeAnimation = active;
            playing = isPlaying;
            looping = isLooping;
            speed = playbackSpeed;
            frame = currentFrame;
            frameCount = totalFrames;
            time = currentTime;
            duration = totalDuration;
            fps = framesPerSecond;
        }
    }

    /// <summary>Copies the current state for reading on the UI thread.</summary>
    public ModelAnimationSnapshot GetSnapshot()
    {
        lock (sync)
        {
            return new(ready, animations, activeAnimation, playing, looping, speed, frame, frameCount, time, duration, fps);
        }
    }
}
