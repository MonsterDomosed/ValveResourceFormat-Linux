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
    bool ResetViewRequested,
    bool HitboxSetChanged,
    string? HitboxSetName,
    bool SkeletonChanged,
    bool ShowBones,
    bool AttachmentsChanged,
    bool ShowAttachments,
    string[]? SelectedAttachments,
    bool RootMotionChanged,
    bool WalkWithRootMotion);

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
    float Fps,
    string[] HitboxSets,
    string? ActiveHitboxSet,
    string[] Attachments,
    bool HasBones,
    bool HasRootMotion)
{
    /// <summary>Whether the model exposes at least one playable animation.</summary>
    public bool HasAnimations => Animations.Length > 0;

    /// <summary>Whether the model exposes any hitbox sets to draw.</summary>
    public bool HasHitboxSets => HitboxSets.Length > 0;

    /// <summary>Whether the model exposes any attachment points to draw.</summary>
    public bool HasAttachments => Attachments.Length > 0;
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

    /// <summary>
    /// Called after a command is queued so the host can wake an idle render loop. Without it a viewer
    /// that only draws on demand would not show the command until the pointer moved.
    /// </summary>
    public Action? RequestFrame { get; set; }

    private void Wake() => RequestFrame?.Invoke();

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
    private bool hitboxSetChanged;
    private string? pendingHitboxSet;
    private string[] hitboxSets = [];
    private string? activeHitboxSet;
    private bool skeletonChanged;
    private bool pendingShowBones;
    private bool attachmentsChanged;
    private bool pendingShowAttachments;
    private string[]? pendingAttachments;
    private string[] attachments = [];
    private bool hasBones;
    private bool rootMotionChanged;
    private bool pendingWalkWithRootMotion;
    private bool hasRootMotion;

    /// <summary>Selects an animation by name, or the bind pose when <paramref name="name"/> is null.</summary>
    public void SelectAnimation(string? name)
    {
        lock (sync)
        {
            animationChanged = true;
            pendingAnimation = name;
        }

        Wake();
    }

    /// <summary>Requests that playback pause or resume.</summary>
    public void SetPlaying(bool value)
    {
        lock (sync)
        {
            playingChanged = true;
            pendingPlaying = value;
        }

        Wake();
    }

    /// <summary>Requests looping on or off.</summary>
    public void SetLooping(bool value)
    {
        lock (sync)
        {
            loopingChanged = true;
            pendingLooping = value;
        }

        Wake();
    }

    /// <summary>Requests a playback speed, clamped to the supported range.</summary>
    public void SetSpeed(float value)
    {
        lock (sync)
        {
            speedChanged = true;
            pendingSpeed = Math.Clamp(value, MinSpeed, MaxSpeed);
        }

        Wake();
    }

    /// <summary>Requests a seek to a fraction of the animation cycle, clamped to [0, 1].</summary>
    public void ScrubTo(float fraction)
    {
        lock (sync)
        {
            seekRequested = true;
            pendingSeek = Math.Clamp(fraction, 0f, 1f);
        }

        Wake();
    }

    /// <summary>Requests that the active animation restart from its beginning.</summary>
    public void Restart()
    {
        lock (sync)
        {
            restartRequested = true;
        }

        Wake();
    }

    /// <summary>Requests that the camera reframe the model.</summary>
    public void ResetView()
    {
        lock (sync)
        {
            resetViewRequested = true;
        }

        Wake();
    }

    /// <summary>Selects a hitbox set to draw, or draws none when <paramref name="name"/> is null.</summary>
    public void SelectHitboxSet(string? name)
    {
        lock (sync)
        {
            hitboxSetChanged = true;
            pendingHitboxSet = name;
        }

        Wake();
    }

    /// <summary>Requests that the bone hierarchy be drawn or hidden.</summary>
    public void SetShowBones(bool value)
    {
        lock (sync)
        {
            skeletonChanged = true;
            pendingShowBones = value;
        }

        Wake();
    }

    /// <summary>Requests that the attachment markers be drawn or hidden.</summary>
    public void SetShowAttachments(bool value)
    {
        lock (sync)
        {
            attachmentsChanged = true;
            pendingShowAttachments = value;
        }

        Wake();
    }

    /// <summary>Requests that only the named attachments are drawn.</summary>
    public void SelectAttachments(string[] names)
    {
        lock (sync)
        {
            attachmentsChanged = true;
            pendingAttachments = names;
        }

        Wake();
    }

    /// <summary>Requests that the model and camera walk along the animation's root motion.</summary>
    public void SetWalkWithRootMotion(bool value)
    {
        lock (sync)
        {
            rootMotionChanged = true;
            pendingWalkWithRootMotion = value;
        }

        Wake();
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
                restartRequested, resetViewRequested,
                hitboxSetChanged, pendingHitboxSet,
                skeletonChanged, pendingShowBones,
                attachmentsChanged, pendingShowAttachments, pendingAttachments,
                rootMotionChanged, pendingWalkWithRootMotion);

            animationChanged = false;
            pendingAnimation = null;
            playingChanged = false;
            loopingChanged = false;
            speedChanged = false;
            seekRequested = false;
            restartRequested = false;
            resetViewRequested = false;
            hitboxSetChanged = false;
            pendingHitboxSet = null;
            skeletonChanged = false;
            attachmentsChanged = false;
            pendingAttachments = null;
            rootMotionChanged = false;

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
        float framesPerSecond,
        string[] hitboxSetNames,
        string? activeSet,
        string[] attachmentNames,
        bool modelHasBones,
        bool activeAnimationHasRootMotion)
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
            hitboxSets = hitboxSetNames;
            activeHitboxSet = activeSet;
            attachments = attachmentNames;
            hasBones = modelHasBones;
            hasRootMotion = activeAnimationHasRootMotion;
        }
    }

    /// <summary>Copies the current state for reading on the UI thread.</summary>
    public ModelAnimationSnapshot GetSnapshot()
    {
        lock (sync)
        {
            return new(ready, animations, activeAnimation, playing, looping, speed, frame, frameCount, time, duration, fps, hitboxSets, activeHitboxSet, attachments, hasBones, hasRootMotion);
        }
    }
}
