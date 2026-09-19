namespace GUI.Linux.Types.Audio;

/// <summary>
/// Computes a min/max envelope from decoded audio, one bucket per displayed column, for drawing a
/// waveform. Pure data so the shell only has to render it.
/// </summary>
internal static class SoundWaveform
{
    /// <summary>One column of a waveform: the quietest and loudest sample in the bucket, in [-1, 1].</summary>
    public readonly record struct Peak(float Min, float Max);

    /// <summary>Builds <paramref name="bucketCount"/> peaks spanning the whole decoded sound.</summary>
    public static Peak[] Compute(DecodedSound sound, int bucketCount)
    {
        ArgumentNullException.ThrowIfNull(sound);

        if (bucketCount <= 0 || sound.FrameCount == 0 || sound.Channels <= 0)
        {
            return [];
        }

        var peaks = new Peak[bucketCount];
        var framesPerBucket = (double)sound.FrameCount / bucketCount;
        var samples = sound.Samples;
        var channels = sound.Channels;

        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var startFrame = (int)(bucket * framesPerBucket);
            var endFrame = Math.Min(sound.FrameCount, (int)((bucket + 1) * framesPerBucket));

            if (endFrame <= startFrame)
            {
                endFrame = Math.Min(sound.FrameCount, startFrame + 1);
            }

            var min = 1f;
            var max = -1f;
            var sampleIndex = startFrame * channels;
            var sampleEnd = endFrame * channels;

            for (; sampleIndex < sampleEnd; sampleIndex++)
            {
                var value = samples[sampleIndex] / 32768f;

                if (value < min)
                {
                    min = value;
                }

                if (value > max)
                {
                    max = value;
                }
            }

            peaks[bucket] = new Peak(min, max);
        }

        return peaks;
    }
}
