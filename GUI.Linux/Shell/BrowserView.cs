using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.Types.Browser;
using GUI.Utils;

namespace GUI.Linux.Shell;

/// <summary>
/// Avalonia source browser: recently opened files and every installed Steam game with its VPKs.
/// Double-clicking a VPK opens a <see cref="PackageBrowserView"/>; double-clicking a file opens it
/// through the normal viewer factory. Source enumeration comes from the shared <see cref="GameBrowser"/>.
/// </summary>
internal sealed class BrowserView : UserControl
{
    private readonly MainWindow window;
    private readonly TreeView sourcesTree = new();
    private readonly TextBlock status = new();
    private readonly Button refreshButton = new() { Content = "Refresh" };

    public BrowserView(MainWindow window)
    {
        this.window = window;

        Content = BuildLayout();
        _ = LoadSourcesAsync();
    }

    private DockPanel BuildLayout()
    {
        var intro = new TextBlock
        {
            Text = "Steam games and their VPKs. Double-click a VPK to browse it, or a recent file to open it.",
            Margin = new Thickness(8, 6),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        };

        refreshButton.Margin = new Thickness(8, 4);
        refreshButton.Click += (_, _) => _ = LoadSourcesAsync();

        status.VerticalAlignment = VerticalAlignment.Center;
        status.Margin = new Thickness(8, 0);
        status.Opacity = 0.7;

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
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

        var dock = new DockPanel();
        DockPanel.SetDock(intro, Dock.Top);
        DockPanel.SetDock(toolbar, Dock.Top);
        dock.Children.Add(intro);
        dock.Children.Add(toolbar);
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
