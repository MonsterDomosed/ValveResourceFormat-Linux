using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using GUI.Linux.Types.GLViewers;

namespace GUI.Linux.Shell;

/// <summary>
/// The scene inspector sidebar. It builds the common View/Display/Render/Layers/Camera/Debug sections
/// and talks to the render thread exclusively through a <see cref="SceneSidebarSession"/>.
/// </summary>
internal sealed class ViewerSidebar : UserControl, IDisposable
{
    private static readonly string[] PerfModes = ["Off", "Stats", "Timings", "Allocations"];

    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly ComboBox renderModeBox = new();
    private readonly CheckBox wireframeCheck = new() { Content = "Wireframe" };
    private readonly CheckBox baseGridCheck = new() { Content = "Base grid" };
    private readonly CheckBox lightBackgroundCheck = new() { Content = "Light background" };
    private readonly CheckBox solidBackgroundCheck = new() { Content = "Solid background" };
    private readonly ComboBox perfModeBox = new();
    private readonly CheckBox staticOctreeCheck = new() { Content = "Static octree" };
    private readonly CheckBox dynamicOctreeCheck = new() { Content = "Dynamic octree" };
    private readonly CheckBox visDebugCheck = new() { Content = "Visibility debug" };
    private readonly CheckBox physicsTracesCheck = new() { Content = "Physics traces" };
    private readonly CheckBox speedCheck = new() { Content = "Move speed" };
    private readonly StackPanel layersPanel = new() { Spacing = 4 };
    private readonly StackPanel layersSection = new() { Spacing = 8 };
    private readonly TextBox cameraNameBox = new() { PlaceholderText = "Camera name" };
    private readonly ComboBox savedCamerasBox = new();
    private readonly StackPanel cameraSection = new() { Spacing = 8 };

    private SceneSidebarSession? session;
    private bool updating;
    private bool disposed;

    private string[] lastRenderModes = [];
    private string? lastRenderMode;
    private bool? lastWireframe;
    private bool? lastBaseGrid;
    private bool? lastLightBackground;
    private bool? lastSolidBackground;
    private bool? lastStaticOctree;
    private bool? lastDynamicOctree;
    private bool? lastVisDebug;
    private bool? lastPhysicsTraces;
    private bool? lastSpeed;
    private int lastPerfMode = -1;
    private string[] lastLayers = [];
    private string[] lastEnabledLayers = [];
    private string[] lastCameras = [];

    public ViewerSidebar()
    {
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(12) };

        var resetView = new Button { Content = "Reset view", Classes = { "tool" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        resetView.Click += (_, _) => session?.ResetCamera();
        panel.Children.Add(Section("View", resetView));

        baseGridCheck.IsCheckedChanged += (_, _) => Send(ref updating, () => session?.SetBaseGrid(baseGridCheck.IsChecked == true));
        lightBackgroundCheck.IsCheckedChanged += (_, _) => Send(ref updating, () => session?.SetLightBackground(lightBackgroundCheck.IsChecked == true));
        solidBackgroundCheck.IsCheckedChanged += (_, _) => Send(ref updating, () => session?.SetSolidBackground(solidBackgroundCheck.IsChecked == true));
        panel.Children.Add(Section("Display", baseGridCheck, lightBackgroundCheck, solidBackgroundCheck));

        renderModeBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        renderModeBox.SelectionChanged += (_, _) => Send(ref updating, () => { if (renderModeBox.SelectedItem is string mode) { session?.SetRenderMode(mode); } });
        wireframeCheck.IsCheckedChanged += (_, _) => Send(ref updating, () => session?.SetWireframe(wireframeCheck.IsChecked == true));
        panel.Children.Add(Section("Render", renderModeBox, wireframeCheck));

        layersSection.Children.Add(layersPanel);
        layersSection.IsVisible = false;
        panel.Children.Add(Section("Layers", layersSection));

        SaveCameraButton = new Button { Content = "Save", Classes = { "tool" } };
        SaveCameraButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(cameraNameBox.Text))
            {
                session?.SaveCamera(cameraNameBox.Text.Trim());
            }
        };

        var cameraButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };

        ApplyCameraButton = new Button { Content = "Apply", Classes = { "tool" } };
        DeleteCameraButton = new Button { Content = "Delete", Classes = { "tool" } };
        cameraButtons.Children.Add(ApplyCameraButton);
        cameraButtons.Children.Add(DeleteCameraButton);

        ApplyCameraButton.Click += (_, _) => { if (savedCamerasBox.SelectedItem is string name) { session?.ApplyCamera(name); } };
        DeleteCameraButton.Click += (_, _) => { if (savedCamerasBox.SelectedItem is string name) { session?.DeleteCamera(name); } };

        savedCamerasBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        cameraSection.Children.Add(savedCamerasBox);
        cameraSection.Children.Add(cameraButtons);
        cameraSection.Children.Add(cameraNameBox);
        cameraSection.Children.Add(SaveCameraButton);
        panel.Children.Add(Section("Camera", cameraSection));

        perfModeBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        perfModeBox.ItemsSource = PerfModes;
        perfModeBox.SelectionChanged += (_, _) => Send(ref updating, () => session?.SetPerfMode(perfModeBox.SelectedIndex));
        staticOctreeCheck.IsCheckedChanged += (_, _) => Send(ref updating, () => session?.SetStaticOctree(staticOctreeCheck.IsChecked == true));
        dynamicOctreeCheck.IsCheckedChanged += (_, _) => Send(ref updating, () => session?.SetDynamicOctree(dynamicOctreeCheck.IsChecked == true));
        visDebugCheck.IsCheckedChanged += (_, _) => Send(ref updating, () => session?.SetVisDebug(visDebugCheck.IsChecked == true));
        physicsTracesCheck.IsCheckedChanged += (_, _) => Send(ref updating, () => session?.SetPhysicsTraces(physicsTracesCheck.IsChecked == true));
        speedCheck.IsCheckedChanged += (_, _) => Send(ref updating, () => session?.SetShowSpeed(speedCheck.IsChecked == true));
        panel.Children.Add(Section("Debug", perfModeBox, staticOctreeCheck, dynamicOctreeCheck, visDebugCheck, physicsTracesCheck, speedCheck));

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };

        timer.Tick += (_, _) => UpdateFromSnapshot();
        timer.Start();
    }

    /// <summary>Binds the sidebar to a viewer session once its scene core exists.</summary>
    public void Attach(SceneSidebarSession sidebarSession)
    {
        session = sidebarSession;
    }

    /// <summary>Whether the sidebar has been bound to a scene session.</summary>
    internal bool Attached => session is not null;

    /// <summary>The render mode selector, for the self-check to drive the real control.</summary>
    internal ComboBox RenderModeSelector => renderModeBox;

    /// <summary>The wireframe checkbox, for the self-check to drive the real control.</summary>
    internal CheckBox WireframeCheckBox => wireframeCheck;

    /// <summary>The base grid checkbox, for the self-check to drive the real control.</summary>
    internal CheckBox BaseGridCheckBox => baseGridCheck;

    /// <summary>The light background checkbox, for the self-check to drive the real control.</summary>
    internal CheckBox LightBackgroundCheckBox => lightBackgroundCheck;

    /// <summary>The performance overlay selector, for the self-check to drive the real control.</summary>
    internal ComboBox PerfModeSelector => perfModeBox;

    /// <summary>The saved camera name box, for the self-check to drive the real control.</summary>
    internal TextBox CameraNameBox => cameraNameBox;

    /// <summary>The saved camera list, for the self-check to drive the real control.</summary>
    internal ComboBox SavedCamerasSelector => savedCamerasBox;

    /// <summary>The saved camera Save button, for the self-check to click the real control.</summary>
    internal Button SaveCameraButton { get; private set; } = null!;

    /// <summary>The saved camera Apply button, for the self-check to click the real control.</summary>
    internal Button ApplyCameraButton { get; private set; } = null!;

    /// <summary>The saved camera Delete button, for the self-check to click the real control.</summary>
    internal Button DeleteCameraButton { get; private set; } = null!;

    private static void Send(ref bool updating, Action action)
    {
        if (!updating)
        {
            action();
        }
    }

    private static StackPanel Section(string title, params Control[] children)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = title, Classes = { "section" } });

        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }

    private void UpdateFromSnapshot()
    {
        if (session is not { } current)
        {
            return;
        }

        var snapshot = current.GetSnapshot();

        if (!snapshot.Ready)
        {
            return;
        }

        updating = true;

        try
        {
            if (!SequenceEqual(lastRenderModes, snapshot.RenderModes))
            {
                lastRenderModes = snapshot.RenderModes;
                renderModeBox.ItemsSource = snapshot.RenderModes;
            }

            if (!string.Equals(lastRenderMode, snapshot.CurrentRenderMode, StringComparison.Ordinal))
            {
                lastRenderMode = snapshot.CurrentRenderMode;
                renderModeBox.SelectedItem = snapshot.CurrentRenderMode;
            }

            SetChecked(wireframeCheck, ref lastWireframe, snapshot.Wireframe);
            SetChecked(baseGridCheck, ref lastBaseGrid, snapshot.BaseGrid);
            SetChecked(lightBackgroundCheck, ref lastLightBackground, snapshot.LightBackground);
            SetChecked(solidBackgroundCheck, ref lastSolidBackground, snapshot.SolidBackground);
            SetChecked(staticOctreeCheck, ref lastStaticOctree, snapshot.StaticOctree);
            SetChecked(dynamicOctreeCheck, ref lastDynamicOctree, snapshot.DynamicOctree);
            SetChecked(visDebugCheck, ref lastVisDebug, snapshot.VisDebug);
            SetChecked(physicsTracesCheck, ref lastPhysicsTraces, snapshot.PhysicsTraces);
            SetChecked(speedCheck, ref lastSpeed, snapshot.ShowSpeed);

            if (lastPerfMode != snapshot.PerfMode)
            {
                lastPerfMode = snapshot.PerfMode;
                perfModeBox.SelectedIndex = Math.Clamp(snapshot.PerfMode, 0, PerfModes.Length - 1);
            }

            if (!SequenceEqual(lastLayers, snapshot.LayerNames) || !SequenceEqual(lastEnabledLayers, snapshot.EnabledLayers))
            {
                RebuildLayers(snapshot);
            }

            if (!SequenceEqual(lastCameras, snapshot.SavedCameras))
            {
                lastCameras = snapshot.SavedCameras;
                savedCamerasBox.ItemsSource = snapshot.SavedCameras;
            }
        }
        finally
        {
            updating = false;
        }
    }

    private void RebuildLayers(SceneSidebarSnapshot snapshot)
    {
        lastLayers = snapshot.LayerNames;
        lastEnabledLayers = snapshot.EnabledLayers;

        layersSection.IsVisible = snapshot.LayerNames.Length > 0;

        if (!layersSection.IsVisible)
        {
            return;
        }

        layersPanel.Children.Clear();

        foreach (var layer in snapshot.LayerNames)
        {
            var check = new CheckBox { Content = layer, IsChecked = snapshot.EnabledLayers.Contains(layer, StringComparer.Ordinal) };
            check.IsCheckedChanged += (_, _) =>
            {
                if (updating)
                {
                    return;
                }

                var enabled = layersPanel.Children.OfType<CheckBox>()
                    .Where(static c => c.IsChecked == true)
                    .Select(static c => (string)c.Content!)
                    .ToArray();
                session?.SetLayers(enabled);
            };
            layersPanel.Children.Add(check);
        }
    }

    private static void SetChecked(CheckBox box, ref bool? last, bool value)
    {
        if (last == value)
        {
            return;
        }

        last = value;
        box.IsChecked = value;
    }

    private static bool SequenceEqual(string[] left, string[] right)
        => left.AsSpan().SequenceEqual(right);

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
