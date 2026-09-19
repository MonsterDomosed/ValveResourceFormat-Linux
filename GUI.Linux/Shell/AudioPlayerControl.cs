using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GUI.Linux.Audio;
using GUI.Linux.Types.Audio;
using GUI.Linux.Utils;

namespace GUI.Linux.Shell;

/// <summary>
/// Avalonia audio playback panel: waveform with a clickable playhead, play/pause, rewind, loop,
/// volume and time, plus the resource metadata rows. Playback uses the native
/// <see cref="PulseAudioPlayer"/>; decoding already happened in the shared portable decoder.
/// </summary>
internal sealed class AudioPlayerControl : UserControl, IDisposable
{
    private readonly DecodedSound? sound;
    private readonly PulseAudioPlayer? player;
    private readonly WaveformView? waveform;
    private readonly Button playButton = new() { Width = 72 };
    private readonly Button rewindButton = new() { Width = 40 };
    private readonly ToggleButton loopButton = new() { IsEnabled = false };
    private readonly Slider volumeSlider = new() { Minimum = 0, Maximum = 1, Width = 140 };
    private readonly TextBlock timeLabel = new();
    private readonly DispatcherTimer timer = new() { Interval = System.TimeSpan.FromMilliseconds(100) };

    private bool disposed;
    private bool? lastPlayingState;

    /// <summary>The playback backend, for the self-check.</summary>
    internal PulseAudioPlayer? Player => player;

    /// <summary>Number of metadata rows shown.</summary>
    internal int MetadataRowCount { get; }

    /// <summary>Decoded frame count, for the self-check.</summary>
    internal int DecodedFrameCount => sound?.FrameCount ?? 0;

    /// <summary>Whether a working playback backend was created.</summary>
    internal bool PlaybackAvailable => player is { Available: true };

    /// <summary>Whether a waveform was computed.</summary>
    internal bool HasWaveform => sound is not null && sound.FrameCount > 0;

    public AudioPlayerControl(DecodedSound? decoded, string? unsupportedReason, IReadOnlyList<(string Label, string Value)> metadata)
    {
        sound = decoded;
        MetadataRowCount = metadata.Count;

        var layout = new StackPanel { Spacing = 8, Margin = new Thickness(12) };

        if (sound is not null)
        {
            player = new PulseAudioPlayer(sound);

            waveform = new WaveformView(sound)
            {
                Height = 96,
                SeekRequested = fraction => SeekTo(fraction),
            };
            layout.Children.Add(waveform);

            playButton.Click += (_, _) => TogglePlayPause();
            rewindButton.Click += (_, _) => SeekTo(0);

            playButton.Content = new GUI.Linux.UI.SvgIcon("AudioPlay", 16);
            rewindButton.Content = new GUI.Linux.UI.SvgIcon("AudioRewindLeft", 16);
            loopButton.Content = new GUI.Linux.UI.SvgIcon("AudioRepeat", 16);

            volumeSlider.Value = Math.Clamp(Settings.Config.Volume, 0, 1);
            volumeSlider.PropertyChanged += (_, e) =>
            {
                if (e.Property == Slider.ValueProperty && player is not null)
                {
                    player.Volume = (float)volumeSlider.Value;
                    Settings.Config.Volume = (float)volumeSlider.Value;
                }
            };
            player.Volume = (float)volumeSlider.Value;

            if (sound.HasLoop)
            {
                loopButton.IsEnabled = true;
                loopButton.IsChecked = true;
                player.Looping = true;
            }

            loopButton.IsCheckedChanged += (_, _) =>
            {
                if (player is not null)
                {
                    player.Looping = loopButton.IsChecked == true;
                }
            };

            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { rewindButton, playButton, loopButton, volumeSlider, timeLabel },
            };
            layout.Children.Add(controls);

            if (!player.Available)
            {
                var error = new TextBlock
                {
                    Text = player.ErrorMessage ?? "Playback is unavailable.",
                    TextWrapping = TextWrapping.Wrap,
                };
                GUI.Linux.UI.ThemeResources.Bind(error, TextBlock.ForegroundProperty, "Danger");
                layout.Children.Add(error);
            }

            timer.Tick += (_, _) => UpdatePlaybackState();
            timer.Start();
            UpdatePlaybackState();
        }
        else
        {
            var error = new TextBlock
            {
                Text = unsupportedReason ?? "This sound could not be decoded on Linux.",
                TextWrapping = TextWrapping.Wrap,
            };
            GUI.Linux.UI.ThemeResources.Bind(error, TextBlock.ForegroundProperty, "Danger");
            layout.Children.Add(error);
        }

        layout.Children.Add(BuildMetadata(metadata));

        Content = new ScrollViewer
        {
            Content = layout,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
    }

    private static Grid BuildMetadata(IReadOnlyList<(string Label, string Value)> metadata)
    {
        var table = new Grid { Margin = new Thickness(0, 8, 0, 0), RowSpacing = 2, ColumnSpacing = 16 };
        table.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        table.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var row = 0;

        foreach (var (label, value) in metadata)
        {
            table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var labelBlock = new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, Opacity = 0.8 };
            var valueBlock = new TextBlock { Text = value };

            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            Grid.SetRow(valueBlock, row);
            Grid.SetColumn(valueBlock, 1);
            table.Children.Add(labelBlock);
            table.Children.Add(valueBlock);
            row++;
        }

        return table;
    }

    private void TogglePlayPause()
    {
        if (player is null || !player.Available)
        {
            return;
        }

        if (player.IsPlaying)
        {
            player.Pause();
        }
        else
        {
            if (player.PositionFrame >= Math.Max(1, sound!.FrameCount) && !player.HasLoop)
            {
                player.Seek(0);
            }

            player.Play();
        }

        UpdatePlaybackState();
    }

    private void SeekTo(float fraction)
    {
        if (player is null || sound is null)
        {
            return;
        }

        player.Seek((int)Math.Clamp(fraction * sound.FrameCount, 0, sound.FrameCount));
        UpdatePlaybackState();
    }

    private void UpdatePlaybackState()
    {
        if (sound is null || player is null)
        {
            return;
        }

        var position = System.TimeSpan.FromSeconds(sound.SampleRate <= 0 ? 0 : (double)player.PositionFrame / sound.SampleRate);
        timeLabel.Text = $"{FormatTime(position)} / {FormatTime(sound.Duration)}";

        if (lastPlayingState != player.IsPlaying)
        {
            lastPlayingState = player.IsPlaying;
            playButton.Content = new GUI.Linux.UI.SvgIcon(player.IsPlaying ? "AudioPause" : "AudioPlay", 16);
        }

        waveform!.Position = sound.FrameCount <= 0 ? 0f : (float)player.PositionFrame / sound.FrameCount;
        waveform!.InvalidateVisual();
    }

    private static string FormatTime(System.TimeSpan time)
        => $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds / 10:00}";

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        player?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Draws the decoded waveform with a playhead and reports clicks as a seek fraction.</summary>
    private sealed class WaveformView(DecodedSound sound) : Control
    {
        private SoundWaveform.Peak[] peaks = [];
        private int peakWidth;
        private float peakMax = 1f;

        /// <summary>Playhead position in [0, 1].</summary>
        public float Position { get; set; }

        /// <summary>Raised with the clicked fraction in [0, 1].</summary>
        public Action<float>? SeekRequested { get; init; }

        public override void Render(DrawingContext context)
        {
            var width = (int)Bounds.Width;
            var height = Bounds.Height;

            var playedBrush = GUI.Linux.UI.ThemeResources.Brush("Accent");
            var unplayedBrush = GUI.Linux.UI.ThemeResources.Brush("BorderSubtle");
            var playheadBrush = GUI.Linux.UI.ThemeResources.Brush("TextPrimary");
            var playheadPen = new Pen(playheadBrush, 1.5);

            context.FillRectangle(GUI.Linux.UI.ThemeResources.Brush("InputBackground"), new Rect(0, 0, Bounds.Width, height));

            if (width <= 0 || height <= 0 || sound.FrameCount == 0)
            {
                return;
            }

            if (peaks.Length == 0 || peakWidth != width)
            {
                peaks = SoundWaveform.Compute(sound, width);
                peakWidth = width;

                var max = 0.0001f;

                foreach (var peak in peaks)
                {
                    max = Math.Max(max, Math.Max(Math.Abs(peak.Min), Math.Abs(peak.Max)));
                }

                peakMax = max;
            }

            var mid = height / 2f;
            var playedX = Position * width;

            for (var x = 0; x < peaks.Length; x++)
            {
                var peak = peaks[x];
                var top = mid - (peak.Max / peakMax * mid);
                var bottom = mid - (peak.Min / peakMax * mid);

                if (bottom - top < 1)
                {
                    bottom = top + 1;
                }

                context.DrawLine(new Pen(x <= playedX ? playedBrush : unplayedBrush, 1), new Point(x + 0.5, top), new Point(x + 0.5, bottom));
            }

            if (Position > 0)
            {
                context.DrawLine(playheadPen, new Point(playedX, 0), new Point(playedX, height));
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (Bounds.Width <= 0)
            {
                return;
            }

            var fraction = (float)Math.Clamp(e.GetPosition(this).X / Bounds.Width, 0, 1);
            SeekRequested?.Invoke(fraction);
        }
    }
}
