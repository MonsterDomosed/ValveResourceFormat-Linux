using System.IO;
using System.Text;
using NLayer;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.Types.Audio;

/// <summary>
/// Portable decoder for compiled sound resources and loose audio files. WAV PCM and MP3 decode with
/// managed code (MP3 through NLayer). Formats that have no managed decoder (AAC, WAV ADPCM) are
/// reported as unsupported rather than replaced.
/// </summary>
internal static class SoundDecoder
{
    /// <summary>Decodes a compiled sound resource to interleaved 16-bit PCM.</summary>
    /// <exception cref="NotSupportedException">The format has no decoder available on Linux.</exception>
    public static DecodedSound Decode(Sound sound)
    {
        ArgumentNullException.ThrowIfNull(sound);

        return sound.SoundType switch
        {
            Sound.AudioFileType.WAV => DecodeWav(sound),
            Sound.AudioFileType.MP3 => DecodeMp3(sound.GetSoundStream(), sound.LoopStart, sound.LoopEnd),
            _ => throw new NotSupportedException(
                $"{sound.SoundType} decoding is not available on Linux (no managed decoder)."),
        };
    }

    /// <summary>Decodes a loose RIFF/WAVE PCM stream.</summary>
    /// <exception cref="NotSupportedException">The WAV is not uncompressed PCM.</exception>
    public static DecodedSound DecodeWavStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        if (new string(reader.ReadChars(4)) != "RIFF" || reader.ReadInt32() < 4 || new string(reader.ReadChars(4)) != "WAVE")
        {
            throw new InvalidDataException("Not a RIFF/WAVE stream.");
        }

        var channels = 0;
        var sampleRate = 0;
        var bits = 0;
        byte[]? pcm = null;

        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            var next = reader.BaseStream.Position + size + (size & 1);

            if (id == "fmt ")
            {
                var formatTag = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // byte rate
                reader.ReadUInt16(); // block align
                bits = reader.ReadUInt16();

                if (formatTag != 1)
                {
                    throw new NotSupportedException("Only uncompressed PCM WAV is supported on Linux.");
                }
            }
            else if (id == "data")
            {
                pcm = reader.ReadBytes(size);
            }

            reader.BaseStream.Position = Math.Min(next, reader.BaseStream.Length);
        }

        if (pcm is null || channels <= 0 || sampleRate <= 0 || bits is not (8 or 16))
        {
            throw new InvalidDataException("WAV has no readable PCM data chunk.");
        }

        return new DecodedSound(PcmToShort(pcm, bits), sampleRate, channels, -1, -1);
    }

    /// <summary>Decodes a loose MP3 stream.</summary>
    public static DecodedSound DecodeMp3Stream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return DecodeMp3(stream, -1, -1);
    }

    private static DecodedSound DecodeWav(Sound sound)
    {
        if (sound.AudioFormat != Sound.WaveAudioFormat.PCM)
        {
            throw new NotSupportedException(
                $"WAV {sound.AudioFormat} decoding is not available on Linux (only PCM is supported).");
        }

        if (sound.Bits is not (8 or 16))
        {
            throw new NotSupportedException($"WAV {sound.Bits}-bit PCM decoding is not supported.");
        }

        var data = new byte[sound.StreamingDataSize];
        sound.ReadStreamingData(data);

        return new DecodedSound(PcmToShort(data, (int)sound.Bits), (int)sound.SampleRate, (int)sound.Channels, sound.LoopStart, sound.LoopEnd);
    }

    private static DecodedSound DecodeMp3(Stream stream, int loopStart, int loopEnd)
    {
        using var mpeg = new MpegFile(stream);

        var sampleRate = mpeg.SampleRate;
        var channels = mpeg.Channels;
        var expected = mpeg.Length > 0 ? (int)Math.Min(mpeg.Length, int.MaxValue) : 0;
        var samples = expected > 0 ? new List<short>(expected) : [];

        var buffer = new float[16384];
        int read;

        while ((read = mpeg.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                samples.Add((short)Math.Clamp(buffer[i] * 32767f, short.MinValue, short.MaxValue));
            }
        }

        return new DecodedSound([.. samples], sampleRate, channels, loopStart, loopEnd);
    }

    private static short[] PcmToShort(byte[] data, int bits)
    {
        if (bits == 16)
        {
            var samples = new short[data.Length / 2];

            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = (short)(data[i * 2] | (data[(i * 2) + 1] << 8));
            }

            return samples;
        }

        // 8-bit PCM is unsigned, centered on 128.
        var eight = new short[data.Length];

        for (var i = 0; i < eight.Length; i++)
        {
            eight[i] = (short)((data[i] - 128) << 8);
        }

        return eight;
    }
}
