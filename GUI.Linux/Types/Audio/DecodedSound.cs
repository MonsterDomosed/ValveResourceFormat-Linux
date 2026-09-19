namespace GUI.Linux.Types.Audio;

/// <summary>
/// Fully decoded audio: interleaved 16-bit PCM plus the format needed to play it. Container parsing
/// and decoding are portable.
/// </summary>
internal sealed class DecodedSound
{
    /// <summary>Interleaved 16-bit PCM samples.</summary>
    public short[] Samples { get; }

    /// <summary>Samples per second.</summary>
    public int SampleRate { get; }

    /// <summary>Channel count (1 mono, 2 stereo).</summary>
    public int Channels { get; }

    /// <summary>Total frames (samples per channel).</summary>
    public int FrameCount { get; }

    /// <summary>Loop start frame, or -1 when the sound does not loop.</summary>
    public int LoopStartFrame { get; }

    /// <summary>Loop end frame, or -1 when the sound does not loop.</summary>
    public int LoopEndFrame { get; }

    /// <summary>Whether a valid loop region was decoded.</summary>
    public bool HasLoop => LoopStartFrame >= 0 && LoopEndFrame > LoopStartFrame;

    /// <summary>Playback duration.</summary>
    public TimeSpan Duration => SampleRate <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)FrameCount / SampleRate);

    public DecodedSound(short[] samples, int sampleRate, int channels, int loopStart, int loopEnd)
    {
        ArgumentNullException.ThrowIfNull(samples);

        Samples = samples;
        SampleRate = sampleRate;
        Channels = channels;
        FrameCount = channels <= 0 ? 0 : samples.Length / channels;

        if (loopStart >= 0 && loopEnd > loopStart && FrameCount > 0)
        {
            LoopStartFrame = Math.Clamp(loopStart, 0, FrameCount);
            LoopEndFrame = Math.Clamp(loopEnd, LoopStartFrame, FrameCount);
        }
        else
        {
            LoopStartFrame = -1;
            LoopEndFrame = -1;
        }
    }
}
