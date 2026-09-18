using System.Runtime.InteropServices;
using System.Threading;
using GUI.Types.Audio;

namespace GUI.Linux.Audio;

/// <summary>
/// Native Linux playback backend for decoded PCM, using the PulseAudio simple API
/// (<c>libpulse-simple.so.0</c>), which PipeWire's Pulse server also serves. A blocking writer thread
/// feeds PCM16 while the UI reads a position estimate derived from the reported device latency, so
/// pause/seek/loop are just cursor changes on our own sample buffer.
/// </summary>
internal sealed class PulseAudioPlayer : IDisposable
{
    private const string Library = "libpulse-simple.so.0";

    // pa_stream_direction_t / pa_sample_format_t values.
    private const int PaStreamPlayback = 1;
    private const int PaSampleS16Le = 3;

    private const int ChunkFrames = 2048;

    [StructLayout(LayoutKind.Sequential)]
    private struct PaSampleSpec
    {
        public int Format;
        public uint Rate;
        public byte Channels;
    }

    // The string parameters are explicitly marshalled as UTF-8. The analyzer does not accept the
    // LPUTF8Str annotation and wants no string parameters at all, which is not an option here.
#pragma warning disable CA2101
    [DllImport(Library, EntryPoint = "pa_simple_new", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern IntPtr PaSimpleNew(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? server,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int dir,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? dev,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string streamName,
        ref PaSampleSpec spec,
        IntPtr channelMap,
        IntPtr attr,
        out int error);

    [DllImport(Library, EntryPoint = "pa_simple_write", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int PaSimpleWrite(IntPtr simple, byte[] data, nuint bytes, out int error);

    [DllImport(Library, EntryPoint = "pa_simple_flush", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int PaSimpleFlush(IntPtr simple, out int error);

    [DllImport(Library, EntryPoint = "pa_simple_free", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern void PaSimpleFree(IntPtr simple);

    [DllImport(Library, EntryPoint = "pa_simple_get_latency", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern ulong PaSimpleGetLatency(IntPtr simple, out int error);
#pragma warning restore CA2101

    private readonly short[] samples;
    private readonly int channels;
    private readonly int sampleRate;
    private readonly int frameCount;
    private readonly int loopStart;
    private readonly int loopEnd;
    private readonly Lock gate = new();
    private readonly ManualResetEventSlim wake = new(false);
    private readonly byte[] buffer = new byte[ChunkFrames * 2 * 2];

    private IntPtr handle;
    private Thread? thread;
    private volatile bool stopRequested;
    private volatile bool playing;
    private volatile bool looping;
    private volatile float volume = 1f;
    private volatile int positionSnapshot;

    private int baseFrame;
    private long framesQueued;
    private int queueFrame;

    /// <summary>Whether a PulseAudio stream was opened.</summary>
    public bool Available { get; }

    /// <summary>Why playback is unavailable, when <see cref="Available"/> is false.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Whether a valid loop region exists in the decoded sound.</summary>
    public bool HasLoop { get; }

    /// <summary>Playback length.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Whether currently producing samples.</summary>
    public bool IsPlaying => playing;

    /// <summary>Whether the player has been disposed.</summary>
    public bool Disposed { get; private set; }

    /// <summary>Current playback frame, safe to read from the UI thread.</summary>
    public int PositionFrame => positionSnapshot;

    /// <summary>Loop the configured region instead of stopping at the end.</summary>
    public bool Looping
    {
        get => looping;
        set
        {
            looping = value;
            wake.Set();
        }
    }

    /// <summary>Linear volume in [0, 1].</summary>
    public float Volume
    {
        get => volume;
        set => volume = Math.Clamp(value, 0f, 1f);
    }

    public PulseAudioPlayer(DecodedSound sound)
    {
        ArgumentNullException.ThrowIfNull(sound);

        samples = sound.Samples;
        channels = Math.Max(1, sound.Channels);
        sampleRate = Math.Max(1, sound.SampleRate);
        frameCount = sound.FrameCount;
        loopStart = sound.LoopStartFrame;
        loopEnd = sound.LoopEndFrame;
        HasLoop = sound.HasLoop;
        Duration = sound.Duration;

        if (frameCount == 0 || channels > 2)
        {
            ErrorMessage = "No decodable audio samples.";
            return;
        }

        var spec = new PaSampleSpec { Format = PaSampleS16Le, Rate = (uint)sampleRate, Channels = (byte)channels };
        handle = PaSimpleNew(null, "Source 2 Viewer", PaStreamPlayback, null, "Audio playback", ref spec, IntPtr.Zero, IntPtr.Zero, out var error);

        if (handle == IntPtr.Zero)
        {
            ErrorMessage = $"PulseAudio is unavailable (pa_simple_new error {error}).";
            return;
        }

        Available = true;
        thread = new Thread(Pump)
        {
            IsBackground = true,
            Name = "Audio playback",
        };
        thread.Start();
    }

    /// <summary>Starts or resumes playback.</summary>
    public void Play()
    {
        if (!Available)
        {
            return;
        }

        playing = true;
        wake.Set();
    }

    /// <summary>Stops producing samples, holding the current position.</summary>
    public void Pause()
    {
        if (!Available)
        {
            return;
        }

        lock (gate)
        {
            if (!playing)
            {
                return;
            }

            var position = ComputePosition();
            playing = false;
            ResetStreamTo(position);
        }
    }

    /// <summary>Moves the playhead to <paramref name="frame"/>.</summary>
    public void Seek(int frame)
    {
        if (!Available)
        {
            return;
        }

        lock (gate)
        {
            ResetStreamTo(Math.Clamp(frame, 0, frameCount));
        }
    }

    // Caller holds gate. Rebases the writer cursor and drops whatever the device still has buffered.
    private void ResetStreamTo(int frame)
    {
        baseFrame = frame;
        framesQueued = 0;
        queueFrame = frame;
        positionSnapshot = frame;

        if (handle != IntPtr.Zero)
        {
            _ = PaSimpleFlush(handle, out _);
        }
    }

    private int ComputePosition()
    {
        if (handle == IntPtr.Zero)
        {
            return queueFrame;
        }

        var latency = PaSimpleGetLatency(handle, out _);
        var latencyFrames = (long)(latency * (ulong)sampleRate / 1_000_000UL);
        return (int)Math.Clamp(baseFrame + framesQueued - latencyFrames, 0, frameCount);
    }

    private void Pump()
    {
        while (!stopRequested)
        {
            if (!playing)
            {
                wake.Wait(50);
                continue;
            }

            int framesToWrite;

            lock (gate)
            {
                if (!playing)
                {
                    continue;
                }

                if (queueFrame >= frameCount)
                {
                    if (HasLoop && looping)
                    {
                        queueFrame = loopStart;
                    }
                    else
                    {
                        playing = false;
                        positionSnapshot = frameCount;
                        continue;
                    }
                }

                var end = Math.Min(frameCount, queueFrame + ChunkFrames);

                if (HasLoop && looping && queueFrame < loopEnd)
                {
                    end = Math.Min(end, loopEnd);
                }

                framesToWrite = end - queueFrame;
                FillBuffer(queueFrame, framesToWrite);
                queueFrame = end;
            }

            var byteCount = (nuint)(framesToWrite * channels * sizeof(short));
            _ = PaSimpleWrite(handle, buffer, byteCount, out _);

            lock (gate)
            {
                framesQueued += framesToWrite;
                positionSnapshot = ComputePosition();
            }
        }
    }

    // Caller holds gate. Expands the interleaved PCM16 into the little-endian byte buffer, scaled by volume.
    private void FillBuffer(int startFrame, int frameCountToWrite)
    {
        var source = startFrame * channels;
        var volumeNow = volume;

        for (var i = 0; i < frameCountToWrite * channels; i++)
        {
            var scaled = (short)Math.Clamp(samples[source + i] * volumeNow, short.MinValue, short.MaxValue);
            buffer[i * 2] = (byte)(scaled & 0xff);
            buffer[(i * 2) + 1] = (byte)((scaled >> 8) & 0xff);
        }
    }

    public void Dispose()
    {
        if (Disposed)
        {
            return;
        }

        Disposed = true;
        stopRequested = true;
        wake.Set();
        thread?.Join(2000);
        thread = null;

        lock (gate)
        {
            if (handle != IntPtr.Zero)
            {
                PaSimpleFree(handle);
                handle = IntPtr.Zero;
            }
        }

        wake.Dispose();
        GC.SuppressFinalize(this);
    }

    ~PulseAudioPlayer()
    {
        lock (gate)
        {
            if (handle != IntPtr.Zero)
            {
                PaSimpleFree(handle);
                handle = IntPtr.Zero;
            }
        }
    }
}
