using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.Linux.Types.Browser;
using GUI.Linux.Utils;

namespace GUI.Linux.Shell;

/// <summary>
/// The merged Browser tab: a quick-start header for opening files, then the recently opened files
/// and every installed Steam game with its VPKs. Double-clicking a VPK opens a
/// <see cref="PackageBrowserView"/>; double-clicking a file opens it through the viewer factory.
/// Source enumeration comes from the shared <see cref="GameBrowser"/>.
/// </summary>
internal sealed class BrowserView : UserControl
{
    private readonly MainWindow window;
    private readonly TreeView sourcesTree = new();
    private readonly TextBlock status = new();
    private readonly Button refreshButton = new();
    private readonly Button openButton = new();

    public BrowserView(MainWindow window)
    {
        this.window = window;

        Content = BuildLayout();
        _ = LoadSourcesAsync();
    }

    private DockPanel BuildLayout()
    {
        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(16, 16, 16, 4),
            Children =
            {
                new UI.SvgIcon("Logo", 36),
                new StackPanel
                {
                    Spacing = 2,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "Source 2 Viewer", Classes = { "title" } },
                        new TextBlock { Text = $"Version {AppInfo.DisplayVersion}", Classes = { "hint" } },
                    },
                },
            },
        };

        openButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new UI.SvgIcon("FileAdd", 16),
                new TextBlock { Text = "Open file...", VerticalAlignment = VerticalAlignment.Center },
            },
        };
        openButton.Classes.Add("tool");
        openButton.Click += (_, _) => window.OpenFilesFromDialog();

        var actions = new DockPanel { Margin = new Thickness(16, 4, 16, 12) };
        DockPanel.SetDock(openButton, Dock.Right);
        actions.Children.Add(openButton);
        actions.Children.Add(new TextBlock
        {
            Text = "Browse installed Steam games and their VPKs, or open a file.",
            Classes = { "hint" },
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });

        refreshButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new UI.SvgIcon("History", 16),
                new TextBlock { Text = "Refresh", VerticalAlignment = VerticalAlignment.Center },
            },
        };
        refreshButton.Classes.Add("tool");
        refreshButton.Click += (_, _) => _ = LoadSourcesAsync();

        status.VerticalAlignment = VerticalAlignment.Center;
        status.Margin = new Thickness(8, 0);
        status.Classes.Add("hint");

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(12, 0, 12, 6),
            Children = { refreshButton, status },
        };

        sourcesTree.SelectionMode = SelectionMode.Single;
        sourcesTree.DoubleTapped += (_, _) => OpenSelected();
        sourcesTree.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                OpenSelected();
                e.Handled = true;
            }
        };

        var header = new StackPanel { Children = { titleRow, actions, new Border { Classes = { "separator" }, Margin = new Thickness(12, 0, 12, 0) }, toolbar } };

        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(sourcesTree);
        return dock;
    }

    private async Task LoadSourcesAsync()
    {
        refreshButton.IsEnabled = false;
        status.Text = "Scanning Steam libraries...";

        try
        {
            var recent = GameBrowser.BuildRecentFiles(Settings.Config.RecentFiles);
            var games = await Task.Run(GameBrowser.BuildGameSources).ConfigureAwait(true);

            sourcesTree.Items.Clear();
            sourcesTree.Items.Add(BuildNodeItem(recent, expand: true));

            foreach (var game in games)
            {
                sourcesTree.Items.Add(BuildNodeItem(game, expand: false));
            }

            status.Text = $"{games.Count} game(s), {games.Sum(static game => game.Children.Count)} VPK(s)";
        }
        catch (Exception e)
        {
            status.Text = $"Scan failed: {e.Message}";
            Log.Warn(nameof(BrowserView), $"Source scan failed: {e}");
        }
        finally
        {
            refreshButton.IsEnabled = true;
        }
    }

    private static TreeViewItem BuildNodeItem(BrowserSourceNode node, bool expand)
    {
        var item = new TreeViewItem
        {
            Header = node.Name,
            Tag = node,
            IsExpanded = expand,
        };

        foreach (var child in node.Children)
        {
            item.Items.Add(BuildNodeItem(child, expand: false));
        }

        return item;
    }

    private void OpenSelected()
    {
        if (sourcesTree.SelectedItem is not TreeViewItem { Tag: BrowserSourceNode node })
        {
            return;
        }

        switch (node.Kind)
        {
            case BrowserSourceKind.Vpk or BrowserSourceKind.MapVpk:
                window.OpenPackage(node.Path);
                break;

            case BrowserSourceKind.File:
                _ = window.OpenFileAsync(node.Path);
                break;
        }
    }
}
