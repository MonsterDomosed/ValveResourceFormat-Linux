using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using GUI.Linux.GL;
using GUI.Linux.Types.GLViewers;
using GUI.Linux.UI;
using ValveResourceFormat.Renderer.SceneNodes;

namespace GUI.Linux.Shell;

/// <summary>
/// Particle inspection view: the shared scene sidebar and viewport with a playback toolbar above it
/// (play/pause, restart, loop, and what part of the effect is played).
/// </summary>
internal sealed class ParticleViewerControl : UserControl, IDisposable
{
    private readonly ParticleSession session;
    private readonly Button playPauseButton = new() { Content = "Pause", Width = 84 };
    private readonly Button restartButton = new() { Content = "Restart", Width = 84 };
    private readonly CheckBox loopCheckBox = new() { Content = "Loop" };
    private readonly ComboBox modeSelector = new() { Width = 190 };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private static readonly (string Label, ParticlePlaybackMode Mode)[] Modes =
    [
        ("Normal + endcap", ParticlePlaybackMode.NormalWithEndCap),
        ("Normal", ParticlePlaybackMode.Normal),
        ("Endcap only", ParticlePlaybackMode.EndCapOnly),
    ];

    private bool updatingFromState;
    private bool disposed;

    public ParticleViewerControl(Func<IGLViewportRenderer> createRenderer, ParticleSession session)
    {
        this.session = session;

        playPauseButton.Click += (_, _) =>
        {
            var playing = session.GetState().Playing;
            session.SetPlaying(!playing);
            UpdateFromState();
        };

        restartButton.Click += (_, _) => session.Restart();

        loopCheckBox.IsCheckedChanged += (_, _) =>
        {
            if (!updatingFromState)
            {
                session.SetLooping(loopCheckBox.IsChecked == true);
            }
        };

        modeSelector.ItemsSource = Array.ConvertAll(Modes, static mode => mode.Label);
        modeSelector.SelectedIndex = 0;
        modeSelector.SelectionChanged += (_, _) =>
        {
            var index = modeSelector.SelectedIndex;

            if (!updatingFromState && index >= 0 && index < Modes.Length)
            {
                session.SetMode(Modes[index].Mode);
            }
        };

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(8, 6),
            Children = { playPauseButton, restartButton, loopCheckBox, modeSelector },
        };

        var viewer = new ViewportWithSidebar(createRenderer, new ViewerSidebar());
        session.RequestFrame = viewer.Viewport.RequestFrame;

        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        layout.Children.Add(viewer);
        Content = layout;

        timer.Tick += (_, _) => UpdateFromState();
        timer.Start();
        UpdateFromState();
    }

    /// <summary>The playback session, for the self-check.</summary>
    internal ParticleSession Session => session;

    private void UpdateFromState()
    {
        var state = session.GetState();

        updatingFromState = true;

        try
        {
            playPauseButton.Content = state.Playing ? "Pause" : "Play";

            if (loopCheckBox.IsChecked != state.Looping)
            {
                loopCheckBox.IsChecked = state.Looping;
            }

            var modeIndex = Array.FindIndex(Modes, mode => mode.Mode == state.Mode);

            if (modeIndex >= 0 && modeSelector.SelectedIndex != modeIndex)
            {
                modeSelector.SelectedIndex = modeIndex;
            }
        }
        finally
        {
            updatingFromState = false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        session.RequestFrame = null;
        GC.SuppressFinalize(this);
    }
}
