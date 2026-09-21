using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GUI.Linux.GL;
using GUI.Linux.Viewers;

namespace GUI.Linux.Shell;

/// <summary>
/// Native Linux model inspection view: a GL viewport with a resizable left-hand sidebar holding the
/// camera reset action and, when the model has animations, the full animation controls. The sidebar
/// talks to the render thread exclusively through <see cref="ModelAnimationSession"/>.
/// </summary>
internal sealed class ModelViewerControl : UserControl, IDisposable
{
    private const string BindPoseItem = "(bind pose)";

    private readonly ModelAnimationSession session;
    private readonly AvaloniaGlViewport viewport;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    private readonly StackPanel animationGroup = new() { Spacing = 8, IsVisible = false };
    private readonly ComboBox animationSelector = new();
    private readonly Button playPauseButton = new() { Content = "Pause", Width = 84 };
    private readonly Button restartButton = new() { Content = "Restart", Width = 84 };
    private readonly Button resetViewButton = new() { Content = "Reset view", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Slider trackBar = new() { Minimum = 0, Maximum = 1 };
    private readonly TextBlock timeLabel = new() { FontFamily = new FontFamily("monospace") };
    private readonly Slider speedBar = new()
    {
        Minimum = ModelAnimationSession.MinSpeed,
        Maximum = ModelAnimationSession.MaxSpeed,
        Value = 1,
        TickFrequency = 0.1,
        IsSnapToTickEnabled = false,
    };
    private readonly TextBlock speedLabel = new();
    private readonly CheckBox loopCheckBox = new() { Content = "Loop" };

    private string[] populatedAnimations = [];
    private bool updatingFromSnapshot;
    private bool scrubbing;
    private bool wasPlayingBeforeScrub;
    private bool currentPlaying;
    private bool disposed;

    private string? lastAnimation;
    private bool lastPlaying;
    private bool lastLooping;
    private int lastFrame = -1;
    private int lastFrameCount = -1;
    private float lastSpeed = float.NaN;

    public ModelViewerControl(string fileName, ModelAnimationSession session)
    {
        this.session = session;

        viewport = new AvaloniaGlViewport
        {
            RendererFactory = () => new ModelGlRenderer(fileName, session),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(280)) { MinWidth = 220, MaxWidth = 560 });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        var sidebar = BuildSidebar();
        Grid.SetColumn(sidebar, 0);
        grid.Children.Add(sidebar);

        var splitter = new GridSplitter { Width = 4, ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        Grid.SetColumn(viewport, 2);
        grid.Children.Add(viewport);

        Content = grid;

        timer.Tick += (_, _) => UpdateFromSnapshot();
        timer.Start();
    }

    private ScrollViewer BuildSidebar()
    {
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(12) };

        panel.Children.Add(new TextBlock
        {
            Text = "View",
            FontWeight = FontWeight.SemiBold,
        });

        resetViewButton.Click += (_, _) => session.ResetView();
        panel.Children.Add(resetViewButton);

        animationGroup.Children.Add(new TextBlock { Text = "Animation", FontWeight = FontWeight.SemiBold });

        animationSelector.HorizontalAlignment = HorizontalAlignment.Stretch;
        animationSelector.SelectionChanged += OnAnimationSelected;
        animationGroup.Children.Add(animationSelector);

        playPauseButton.Click += (_, _) => session.SetPlaying(!currentPlaying);
        restartButton.Click += (_, _) => session.Restart();
        animationGroup.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { playPauseButton, restartButton },
        });

        trackBar.PointerPressed += (_, _) => BeginScrub();
        trackBar.PointerReleased += (_, _) => EndScrub();
        trackBar.PointerCaptureLost += (_, _) => EndScrub();
        trackBar.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && scrubbing)
            {
                session.ScrubTo((float)trackBar.Value);
            }
        };
        animationGroup.Children.Add(trackBar);

        animationGroup.Children.Add(timeLabel);

        speedLabel.Opacity = 0.8;
        animationGroup.Children.Add(speedLabel);

        speedBar.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !updatingFromSnapshot)
            {
                session.SetSpeed((float)speedBar.Value);
            }
        };
        animationGroup.Children.Add(speedBar);

        loopCheckBox.IsCheckedChanged += (_, _) => SetLooping(loopCheckBox.IsChecked == true);
        animationGroup.Children.Add(loopCheckBox);

        panel.Children.Add(animationGroup);

        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    private void SetLooping(bool value)
    {
        if (!updatingFromSnapshot)
        {
            session.SetLooping(value);
        }
    }

    private void BeginScrub()
    {
        if (scrubbing)
        {
            return;
        }

        scrubbing = true;
        wasPlayingBeforeScrub = session.GetSnapshot().Playing;

        if (wasPlayingBeforeScrub)
        {
            session.SetPlaying(false);
        }

        session.ScrubTo((float)trackBar.Value);
    }

    private void EndScrub()
    {
        if (!scrubbing)
        {
            return;
        }

        scrubbing = false;
        session.ScrubTo((float)trackBar.Value);

        if (wasPlayingBeforeScrub)
        {
            session.SetPlaying(true);
        }
    }

    private void OnAnimationSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (updatingFromSnapshot || animationSelector.SelectedItem is not string name)
        {
            return;
        }

        session.SelectAnimation(name == BindPoseItem ? null : name);
    }

    private void UpdateFromSnapshot()
    {
        var snapshot = session.GetSnapshot();

        if (!snapshot.HasAnimations)
        {
            animationGroup.IsVisible = false;
            return;
        }

        animationGroup.IsVisible = true;

        if (!NamesEqual(populatedAnimations, snapshot.Animations))
        {
            PopulateAnimations(snapshot.Animations);
        }

        updatingFromSnapshot = true;

        try
        {
            // Only mirror values that actually changed, so a pending user command is not overwritten
            // by the previous state before the render thread has applied it.
            if (!string.Equals(snapshot.ActiveAnimation, lastAnimation, StringComparison.Ordinal))
            {
                lastAnimation = snapshot.ActiveAnimation;
                animationSelector.SelectedItem = snapshot.ActiveAnimation ?? BindPoseItem;
            }

            if (snapshot.Playing != lastPlaying)
            {
                lastPlaying = snapshot.Playing;
                currentPlaying = snapshot.Playing;
                playPauseButton.Content = snapshot.Playing ? "Pause" : "Play";
            }

            if (snapshot.Looping != lastLooping)
            {
                lastLooping = snapshot.Looping;
                loopCheckBox.IsChecked = snapshot.Looping;
            }

            if (!scrubbing && (snapshot.Frame != lastFrame || snapshot.FrameCount != lastFrameCount))
            {
                lastFrame = snapshot.Frame;
                lastFrameCount = snapshot.FrameCount;
                trackBar.Value = CycleFraction(snapshot);
            }

            var frameNumber = snapshot.FrameCount > 0 ? snapshot.Frame + 1 : 0;
            timeLabel.Text = string.Create(CultureInfo.InvariantCulture,
                $"Frame {frameNumber} / {snapshot.FrameCount}\nTime {snapshot.Time:F2} / {snapshot.Duration:F2} s\nFPS {snapshot.Fps:F2}");

            if (snapshot.Speed != lastSpeed)
            {
                lastSpeed = snapshot.Speed;
                speedBar.Value = snapshot.Speed;
                speedLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Speed: {snapshot.Speed:F2}x");
            }
        }
        finally
        {
            updatingFromSnapshot = false;
        }
    }

    private static double CycleFraction(ModelAnimationSnapshot snapshot)
    {
        var cycleFrames = snapshot.FrameCount - 1;

        if (cycleFrames <= 0)
        {
            return 0;
        }

        return Math.Clamp((double)snapshot.Frame / cycleFrames, 0, 1);
    }

    private static bool NamesEqual(string[] left, string[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void PopulateAnimations(string[] animations)
    {
        populatedAnimations = animations;

        updatingFromSnapshot = true;

        try
        {
            animationSelector.Items.Clear();
            animationSelector.Items.Add(BindPoseItem);

            foreach (var name in animations)
            {
                animationSelector.Items.Add(name);
            }

            animationSelector.SelectedItem = BindPoseItem;
        }
        finally
        {
            updatingFromSnapshot = false;
        }
    }

    /// <summary>The animation selector, for the self-check to drive the real control.</summary>
    internal ComboBox AnimationSelector => animationSelector;

    /// <summary>The play/pause button, for the self-check to click the real control.</summary>
    internal Button PlayPauseButton => playPauseButton;

    /// <summary>The restart button, for the self-check to click the real control.</summary>
    internal Button RestartButton => restartButton;

    /// <summary>The reset-view button, for the self-check to click the real control.</summary>
    internal Button ResetViewButton => resetViewButton;

    /// <summary>The timeline slider, for the self-check to drive the real control.</summary>
    internal Slider Timeline => trackBar;

    /// <summary>The playback speed slider, for the self-check to drive the real control.</summary>
    internal Slider SpeedBar => speedBar;

    /// <summary>The loop checkbox, for the self-check to drive the real control.</summary>
    internal CheckBox LoopCheckBox => loopCheckBox;

    /// <summary>Whether the animation controls are shown for the current model.</summary>
    internal bool AnimationControlsVisible => animationGroup.IsVisible;

    /// <summary>The animation session backing this control.</summary>
    internal ModelAnimationSession Session => session;

    /// <summary>Starts a simulated timeline drag, pausing playback like a real pointer press does.</summary>
    internal void BeginTimelineDrag() => BeginScrub();

    /// <summary>Ends a simulated timeline drag, seeking and restoring playback like a real release.</summary>
    internal void EndTimelineDrag() => EndScrub();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        GC.SuppressFinalize(this);
    }
}
