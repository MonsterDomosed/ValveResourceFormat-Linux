using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GUI.Linux.Shell;
using GUI.Types.Audio;
using GUI.Types.Viewers;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.Viewers;

/// <summary>
/// Linux compiled-sound viewer: an interactive AUDIO tab (waveform, playback controls, metadata) plus
/// the portable resource data tabs. Decoding happens on the loading thread through the shared
/// <see cref="SoundDecoder"/>; playback uses the native PulseAudio backend.
/// </summary>
internal sealed class AudioViewerView : IViewer
{
    private readonly ResourceDataViewer dataViewer;
    private readonly DecodedSound? decoded;
    private readonly string? unsupportedReason;
    private readonly IReadOnlyList<(string Label, string Value)> metadata;
    private AudioPlayerControl? control;

    public AudioViewerView(ResourceDataViewer dataViewer)
    {
        this.dataViewer = dataViewer;

        if (dataViewer.Resource?.DataBlock is not Sound sound)
        {
            unsupportedReason = "Resource has no sound data block.";
            metadata = [];
            return;
        }

        metadata = sound.GetInfoRows();

        try
        {
            decoded = SoundDecoder.Decode(sound);
        }
        catch (System.Exception e)
        {
            unsupportedReason = e.Message;
        }
    }

    public Task LoadAsync(Stream? stream) => dataViewer.LoadAsync(stream);

    public ViewerContent GetContent()
    {
        List<ViewerTab> tabs =
        [
            new ViewerTab("AUDIO", new ViewerContent.CustomControl(() => control = new AudioPlayerControl(decoded, unsupportedReason, metadata)), Select: true),
        ];

        if (dataViewer.GetContent() is ViewerContent.Tabs data)
        {
            foreach (var tab in data.Items)
            {
                tabs.Add(new ViewerTab(tab.Name, tab.Content));
            }
        }

        return new ViewerContent.Tabs(tabs);
    }

    public void Dispose()
    {
        control?.Dispose();
        control = null;
        dataViewer.Dispose();
        GC.SuppressFinalize(this);
    }
}
