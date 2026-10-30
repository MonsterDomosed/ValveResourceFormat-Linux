using System;
using System.Globalization;
using System.Linq;
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
    private const string NoHitboxSetItem = "(none)";

    private readonly ModelAnimationSession session;
    private readonly AvaloniaGlViewport viewport;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    private readonly StackPanel animationGroup = new() { Spacing = 8, IsVisible = false };
    private readonly StackPanel hitboxGroup = new() { Spacing = 8, IsVisible = false };
    private readonly StackPanel skeletonGroup = new() { Spacing = 8, IsVisible = false };
    private readonly ComboBox animationSelector = new();
    private readonly ComboBox hitboxSelector = new();
    private readonly CheckBox showBonesCheckBox = new() { Content = "Show bones" };
    private readonly CheckBox showAttachmentsCheckBox = new() { Content = "Show attachments" };
    private readonly CheckBox rootMotionCheckBox = new() { Content = "Walk with root motion", IsEnabled = false };
    private readonly ListBox attachmentList = new() { SelectionMode = SelectionMode.Multiple, IsVisible = false, MaxHeight = 160 };
    private readonly Button playPauseButton = new() { Content = "Pause", Width = 84 };
    private readonly Button restartButton = new() { Content = "Restart", Width = 84 };
    private readonly Button resetViewButton = new() { Content = "Reset view", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox lodSelector = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel lodGroup = new() { Spacing = 6 };
    private readonly ComboBox materialGroupSelector = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel materialGroupGroup = new() { Spacing = 6 };
    private readonly StackPanel meshGroupGroup = new() { Spacing = 6 };
    private readonly StackPanel meshGroupPanel = new() { Spacing = 2 };
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
    private string[] populatedHitboxSets = [];
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
    private string? lastHitboxSet;
    private bool hitboxSetsPopulated;
    private string[] populatedAttachments = [];
    private bool attachmentsPopulated;
    private int[] populatedLods = [];
    private string[] populatedMeshGroups = [];
    private string[] populatedMaterialGroups = [];

    public ModelViewerControl(string fileName, ModelAnimationSession session, Action<string>? openResource = null)
    {
        this.session = session;

        viewport = new AvaloniaGlViewport
        {
            RendererFactory = () => new ModelGlRenderer(fileName, session, openResource),
        };

        // Idle viewers stop drawing, so a command from these controls has to ask for a frame.
        session.RequestFrame = viewport.RequestFrame;

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

        lodGroup.Children.Add(new TextBlock { Text = "Level of detail", Classes = { "hint" } });
        lodSelector.SelectionChanged += (_, _) => SetLodFromSelection();
        lodGroup.Children.Add(lodSelector);
        panel.Children.Add(lodGroup);

        meshGroupGroup.Children.Add(new TextBlock { Text = "Mesh groups", Classes = { "hint" } });
        meshGroupGroup.Children.Add(meshGroupPanel);
        panel.Children.Add(meshGroupGroup);

        materialGroupGroup.Children.Add(new TextBlock { Text = "Skin", Classes = { "hint" } });
        materialGroupSelector.SelectionChanged += (_, _) =>
        {
            if (updatingFromSnapshot || materialGroupSelector.SelectedIndex < 0)
            {
                return;
            }

            session.SetMaterialGroup(populatedMaterialGroups[materialGroupSelector.SelectedIndex]);
        };
        materialGroupGroup.Children.Add(materialGroupSelector);
        panel.Children.Add(materialGroupGroup);

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

        // Root motion only exists while an animation that carries it is playing.
        rootMotionCheckBox.IsCheckedChanged += (_, _) => SetWalkWithRootMotion(rootMotionCheckBox.IsChecked == true);
        animationGroup.Children.Add(rootMotionCheckBox);

        panel.Children.Add(animationGroup);

        hitboxGroup.Children.Add(new TextBlock { Text = "Hitboxes", FontWeight = FontWeight.SemiBold });

        hitboxSelector.HorizontalAlignment = HorizontalAlignment.Stretch;
        hitboxSelector.SelectionChanged += OnHitboxSetSelected;
        hitboxGroup.Children.Add(hitboxSelector);

        panel.Children.Add(hitboxGroup);

        showBonesCheckBox.IsCheckedChanged += (_, _) => SetBonesVisible(showBonesCheckBox.IsChecked == true);
        skeletonGroup.Children.Add(showBonesCheckBox);

        showAttachmentsCheckBox.IsCheckedChanged += (_, _) => SetAttachmentsVisible(showAttachmentsCheckBox.IsChecked == true);
        skeletonGroup.Children.Add(showAttachmentsCheckBox);

        attachmentList.SelectionChanged += OnAttachmentsSelected;
        skeletonGroup.Children.Add(attachmentList);

        panel.Children.Add(skeletonGroup);

        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    private void SetLodFromSelection()
    {
        if (updatingFromSnapshot || lodSelector.SelectedIndex < 0)
        {
            return;
        }

        // Index 0 is automatic, the rest map to the model's populated levels
        var index = lodSelector.SelectedIndex - 1;
        session.SetLod(index >= 0 && index < populatedLods.Length ? populatedLods[index] : null);
    }

    private void SetLooping(bool value)
    {
        if (!updatingFromSnapshot)
        {
            session.SetLooping(value);
        }
    }

    private void SetBonesVisible(bool value)
    {
        if (!updatingFromSnapshot)
        {
            session.SetShowBones(value);
        }
    }

    private void SetWalkWithRootMotion(bool value)
    {
        if (!updatingFromSnapshot)
        {
            session.SetWalkWithRootMotion(value);
        }
    }

    private void SetAttachmentsVisible(bool value)
    {
        attachmentList.IsVisible = value;

        if (!updatingFromSnapshot)
        {
            session.SetShowAttachments(value);
        }
    }

    private void OnAttachmentsSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (updatingFromSnapshot || !attachmentList.IsVisible)
        {
            return;
        }

        var selected = attachmentList.SelectedItems is { } items
            ? items.OfType<string>().ToArray()
            : [];

        session.SelectAttachments(selected);
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

    private void OnHitboxSetSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (updatingFromSnapshot || hitboxSelector.SelectedItem is not string name)
        {
            return;
        }

        session.SelectHitboxSet(name == NoHitboxSetItem ? null : name);
    }

    private void UpdateFromSnapshot()
    {
        var snapshot = session.GetSnapshot();

        animationGroup.IsVisible = snapshot.HasAnimations;
        hitboxGroup.IsVisible = snapshot.HasHitboxSets;
        skeletonGroup.IsVisible = snapshot.HasBones || snapshot.HasAttachments;

        if (snapshot.HasAnimations && !NamesEqual(populatedAnimations, snapshot.Animations))
        {
            PopulateAnimations(snapshot.Animations);
        }

        lodGroup.IsVisible = snapshot.HasMultipleLods;
        meshGroupGroup.IsVisible = snapshot.HasMeshGroups;
        materialGroupGroup.IsVisible = snapshot.HasMaterialGroups;

        if (snapshot.HasMaterialGroups)
        {
            if (!populatedMaterialGroups.SequenceEqual(snapshot.MaterialGroups))
            {
                populatedMaterialGroups = snapshot.MaterialGroups;
                materialGroupSelector.ItemsSource = populatedMaterialGroups;
            }

            var groupIndex = Array.IndexOf(populatedMaterialGroups, snapshot.ActiveMaterialGroup);

            if (groupIndex >= 0 && materialGroupSelector.SelectedIndex != groupIndex)
            {
                materialGroupSelector.SelectedIndex = groupIndex;
            }
        }

        if (snapshot.HasMultipleLods && !populatedLods.SequenceEqual(snapshot.LodLevels))
        {
            populatedLods = snapshot.LodLevels;

            var items = new string[populatedLods.Length + 1];
            items[0] = "Auto";

            for (var i = 0; i < populatedLods.Length; i++)
            {
                items[i + 1] = $"LOD {populatedLods[i]}";
            }

            lodSelector.ItemsSource = items;
        }

        if (snapshot.HasMultipleLods)
        {
            var lodIndex = snapshot.AutoLod ? 0 : Array.IndexOf(populatedLods, snapshot.ActiveLod) + 1;

            if (lodIndex > 0 && lodSelector.SelectedIndex != lodIndex)
            {
                lodSelector.SelectedIndex = lodIndex;
            }
        }

        if (snapshot.HasMeshGroups && !NamesEqual(populatedMeshGroups, snapshot.MeshGroups))
        {
            populatedMeshGroups = snapshot.MeshGroups;
            meshGroupPanel.Children.Clear();

            foreach (var name in populatedMeshGroups)
            {
                var check = new CheckBox
                {
                    Content = name,
                    IsChecked = snapshot.ActiveMeshGroups.Contains(name),
                };

                check.IsCheckedChanged += (_, _) =>
                {
                    if (updatingFromSnapshot)
                    {
                        return;
                    }

                    session.SetMeshGroups([.. meshGroupPanel.Children.OfType<CheckBox>()
                        .Where(static box => box.IsChecked == true)
                        .Select(static box => (string)box.Content!)]);
                };

                meshGroupPanel.Children.Add(check);
            }
        }
        else if (snapshot.HasMeshGroups)
        {
            foreach (var check in meshGroupPanel.Children.OfType<CheckBox>())
            {
                var active = snapshot.ActiveMeshGroups.Contains((string)check.Content!);

                if (check.IsChecked != active)
                {
                    check.IsChecked = active;
                }
            }
        }

        if (snapshot.HasHitboxSets && (!hitboxSetsPopulated || !NamesEqual(populatedHitboxSets, snapshot.HitboxSets)))
        {
            PopulateHitboxSets(snapshot.HitboxSets);
        }

        if (snapshot.HasAttachments && (!attachmentsPopulated || !NamesEqual(populatedAttachments, snapshot.Attachments)))
        {
            PopulateAttachments(snapshot.Attachments);
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

            if (!string.Equals(snapshot.ActiveHitboxSet, lastHitboxSet, StringComparison.Ordinal))
            {
                lastHitboxSet = snapshot.ActiveHitboxSet;
                hitboxSelector.SelectedItem = snapshot.ActiveHitboxSet ?? NoHitboxSetItem;
            }

            if (rootMotionCheckBox.IsEnabled != snapshot.HasRootMotion)
            {
                rootMotionCheckBox.IsEnabled = snapshot.HasRootMotion;

                if (!snapshot.HasRootMotion)
                {
                    rootMotionCheckBox.IsChecked = false;
                }
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

    private void PopulateHitboxSets(string[] sets)
    {
        populatedHitboxSets = sets;
        hitboxSetsPopulated = true;

        updatingFromSnapshot = true;

        try
        {
            hitboxSelector.Items.Clear();
            hitboxSelector.Items.Add(NoHitboxSetItem);

            foreach (var name in sets)
            {
                hitboxSelector.Items.Add(name);
            }

            hitboxSelector.SelectedItem = NoHitboxSetItem;
        }
        finally
        {
            updatingFromSnapshot = false;
        }
    }

    private void PopulateAttachments(string[] attachments)
    {
        populatedAttachments = attachments;
        attachmentsPopulated = true;

        updatingFromSnapshot = true;

        try
        {
            attachmentList.Items.Clear();
            attachmentList.SelectedItems?.Clear();

            foreach (var name in attachments)
            {
                attachmentList.Items.Add(name);
            }
        }
        finally
        {
            updatingFromSnapshot = false;
        }
    }

    /// <summary>The animation selector, for the self-check to drive the real control.</summary>
    internal ComboBox AnimationSelector => animationSelector;

    /// <summary>The hitbox set selector, for the self-check to drive the real control.</summary>
    internal ComboBox HitboxSelector => hitboxSelector;

    /// <summary>Whether the hitbox controls are shown for the current model.</summary>
    internal bool HitboxControlsVisible => hitboxGroup.IsVisible;

    /// <summary>The bone overlay checkbox, for the self-check to drive the real control.</summary>
    internal CheckBox ShowBonesCheckBox => showBonesCheckBox;

    /// <summary>The attachment overlay checkbox, for the self-check to drive the real control.</summary>
    internal CheckBox ShowAttachmentsCheckBox => showAttachmentsCheckBox;

    /// <summary>The attachment name list, for the self-check to inspect the real control.</summary>
    internal ListBox AttachmentList => attachmentList;

    /// <summary>The root motion checkbox, for the self-check to drive the real control.</summary>
    internal CheckBox RootMotionCheckBox => rootMotionCheckBox;

    /// <summary>Whether the skeleton overlay controls are shown for the current model.</summary>
    internal bool SkeletonControlsVisible => skeletonGroup.IsVisible;

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
