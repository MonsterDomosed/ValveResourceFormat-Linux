using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using GUI.Linux.Shell;
using GUI.Linux.Types.Audio;
using GUI.Linux.Types.Viewers;

namespace GUI.Linux.Viewers;

/// <summary>
/// Linux viewer for loose <c>.wav</c>/<c>.mp3</c> files, decoded through the shared
/// <see cref="SoundDecoder"/> and played through the native PulseAudio backend.
/// </summary>
internal sealed class LooseAudioViewer : IViewer
{
    private readonly DecodedSound? decoded;
    private readonly string? unsupportedReason;
    private readonly IReadOnlyList<(string Label, string Value)> metadata;
    private AudioPlayerControl? control;

    public LooseAudioViewer(string fileName, bool isWav)
    {
        DecodedSound sound;

        try
        {
            using var stream = File.OpenRead(fileName);
            sound = isWav ? SoundDecoder.DecodeWavStream(stream) : SoundDecoder.DecodeMp3Stream(stream);
        }
        catch (System.Exception e)
        {
            unsupportedReason = e.Message;
            metadata = [];
            return;
        }

        decoded = sound;
        metadata =
        [
            ("Duration", sound.Duration.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture)),
            ("Format", isWav ? "WAV PCM" : "MP3"),
            ("Channels", sound.Channels.ToString(CultureInfo.InvariantCulture)),
            ("Sample rate", $"{sound.SampleRate.ToString(CultureInfo.InvariantCulture)} Hz"),
        ];
    }

    public Task LoadAsync(Stream? stream) => Task.CompletedTask;

    public ViewerContent GetContent()
        => new ViewerContent.CustomControl(() => control = new AudioPlayerControl(decoded, unsupportedReason, metadata));

    public void Dispose()
    {
        control?.Dispose();
        control = null;
        GC.SuppressFinalize(this);
    }
}
