using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using GUI.Linux.GL;
using GUI.Linux.Shell;
using GUI.Linux.Types.GLViewers;
using GUI.Linux.Utils;

namespace GUI.Linux.UI;

/// <summary>
/// Hosts a GL viewport with a collapsible left-hand inspector sidebar. The sidebar attaches to the
/// scene core's session once the GL thread has created it.
/// </summary>
internal sealed class ViewportWithSidebar : UserControl, IDisposable
{
    private const double CollapsedWidth = 28;
    private const double MinExpandedWidth = 220;

    private readonly ViewerSidebar sidebar;
    private readonly Grid grid = new();
    private readonly ColumnDefinition sidebarColumn = new();
    private readonly DispatcherTimer attachTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly ToggleButton collapseButton = new();
    private bool collapsed;
    private bool disposed;

    public ViewportWithSidebar(Func<IGLViewportRenderer> rendererFactory, ViewerSidebar sidebar)
    {
        this.sidebar = sidebar;

        var viewport = new AvaloniaGlViewport
        {
            RendererFactory = rendererFactory,
        };

        var expanded = Math.Max(MinExpandedWidth, Settings.Config.ViewerSidebarWidth);
        sidebarColumn.Width = new GridLength(expanded);
        sidebarColumn.MinWidth = CollapsedWidth;
        grid.ColumnDefinitions.Add(sidebarColumn);
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        collapseButton.Classes.Add("icon");
        collapseButton.VerticalAlignment = VerticalAlignment.Top;
        collapseButton.HorizontalAlignment = HorizontalAlignment.Center;
        collapseButton.Margin = new Thickness(0, 8);
        collapseButton.Click += (_, _) => SetCollapsed(!collapsed);
        UpdateCollapseIcon();

        var collapseBar = new Border
        {
            Classes = { "sidebar" },
            Child = collapseButton,
        };

        var sidebarHost = new DockPanel();
        DockPanel.SetDock(collapseBar, Dock.Top);
        sidebarHost.Children.Add(collapseBar);
        sidebarHost.Children.Add(sidebar);
        sidebarHost.Classes.Add("sidebar");
        Grid.SetColumn(sidebarHost, 0);
        grid.Children.Add(sidebarHost);

        var splitter = new GridSplitter { Width = 4, ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        Grid.SetColumn(viewport, 2);
        grid.Children.Add(viewport);

        Content = grid;

        attachTimer.Tick += (_, _) => TryAttach(viewport);
        attachTimer.Start();
    }

    private void SetCollapsed(bool value)
    {
        collapsed = value;

        if (collapsed)
        {
            if (sidebarColumn.Width.Value > CollapsedWidth)
            {
                Settings.Config.ViewerSidebarWidth = sidebarColumn.Width.Value;
            }

            sidebarColumn.Width = new GridLength(CollapsedWidth);
        }
        else
        {
            sidebarColumn.Width = new GridLength(Math.Max(MinExpandedWidth, Settings.Config.ViewerSidebarWidth));
        }

        sidebar.IsVisible = !collapsed;
        UpdateCollapseIcon();
    }

    private void UpdateCollapseIcon()
        => collapseButton.Content = new SvgIcon(collapsed ? "NavigateForward" : "NavigateBack", 14);

    private void TryAttach(AvaloniaGlViewport viewport)
    {
        if (viewport.ViewportRenderer is SceneCoreGlRenderer { SceneCore: { } core })
        {
            sidebar.Attach(core.Sidebar);
            attachTimer.Stop();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        attachTimer.Stop();
        sidebar.Dispose();
        GC.SuppressFinalize(this);
    }
}
