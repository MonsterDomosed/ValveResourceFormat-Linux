using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GUI.Linux.Types.Browser;
using GUI.Linux.Utils;
using ValvePak;
using ValveResourceFormat.IO;

namespace GUI.Linux.Shell;

/// <summary>
/// Avalonia package browser for a single VPK: a lazily-built folder tree, a filtered file list with
/// name/type/size, and double-click-to-open routed through the shell. Navigation and search come from
/// the shared <see cref="PackageTree"/>; only the presentation is Avalonia.
/// </summary>
internal sealed class PackageBrowserView : UserControl, IDisposable
{
    private sealed record FileRow(string Name, string Type, string Size, PackageEntry Entry);

    private readonly string vpkPath;
    private readonly MainWindow window;
    private readonly Package package;
    private readonly PackageTreeNode root;
    private readonly TreeView folderTree = new();
    private readonly ListBox fileList = new();
    private readonly TextBox filterBox = new();
    private readonly CheckBox searchAllFolders = new() { Content = "Search all folders" };
    private readonly TextBlock header = new();
    private readonly TextBlock countLabel = new();

    private PackageTreeNode currentFolder;
    private bool disposed;

    public PackageBrowserView(string vpkPath, MainWindow window)
    {
        this.vpkPath = vpkPath;
        this.window = window;

        package = new Package();
        package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
        package.Read(vpkPath);
        root = PackageTree.Build(package);
        currentFolder = root;

        Content = BuildLayout();
        PopulateFolderTree();
        RefreshFileList();

        header.Text = $"{vpkPath}    {root.TotalFileCount:N0} entries, {CountFolders(root):N0} folders, "
            + $"{HumanReadableByteSizeFormatter.Format(root.TotalSize)}";
    }

    /// <summary>Total entries in the package, for the self-check.</summary>
    internal int EntryCount => root.TotalFileCount;

    /// <summary>The opened package, for the self-check.</summary>
    internal Package Package => package;

    /// <summary>Total folders in the package, for the self-check.</summary>
    internal int FolderCount => CountFolders(root);

    /// <summary>Runs the same search the UI uses, for the self-check.</summary>
    internal List<PackageEntry> Search(string query, PackageSearchMode mode) => PackageTree.Search(root, query, mode);

    private DockPanel BuildLayout()
    {
        header.TextWrapping = TextWrapping.Wrap;
        header.Margin = new Thickness(8, 6, 8, 2);
        header.FontWeight = FontWeight.SemiBold;

        filterBox.PlaceholderText = "Filter by name or path...";
        filterBox.Width = 320;
        filterBox.TextChanged += (_, _) => RefreshFileList();

        searchAllFolders.IsCheckedChanged += (_, _) => RefreshFileList();

        countLabel.VerticalAlignment = VerticalAlignment.Center;
        countLabel.Opacity = 0.7;
        countLabel.Margin = new Thickness(8, 0);

        var filterRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(8, 4),
            Children = { filterBox, searchAllFolders, countLabel },
        };

        folderTree.Width = 320;
        folderTree.SelectionChanged += (_, _) => OnFolderSelected();

        fileList.SelectionMode = SelectionMode.Single;
        fileList.ItemTemplate = new FuncDataTemplate<FileRow>((row, _) => BuildFileRow(row), supportsRecycling: true);
        fileList.DoubleTapped += (_, _) => OpenSelected();
        fileList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                OpenSelected();
                e.Handled = true;
            }
        };
        fileList.ContextMenu = BuildFileContextMenu();
        fileList.PointerPressed += OnFileListPointerPressed;

        var panes = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,4,*"),
        };

        var splitter = new GridSplitter { Width = 4, ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter, 1);

        var filesPane = new DockPanel();
        DockPanel.SetDock(filterRow, Dock.Top);
        filesPane.Children.Add(filterRow);
        filesPane.Children.Add(fileList);

        Grid.SetColumn(folderTree, 0);
        Grid.SetColumn(filesPane, 2);
        panes.Children.Add(folderTree);
        panes.Children.Add(splitter);
        panes.Children.Add(filesPane);

        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(panes);
        return dock;
    }

    private static Grid BuildFileRow(FileRow row)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,130,90"),
        };

        var name = new TextBlock { Text = row.Name, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1) };
        var type = new TextBlock { Text = row.Type, Opacity = 0.7, Margin = new Thickness(0, 1) };
        var size = new TextBlock { Text = row.Size, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 1) };

        Grid.SetColumn(name, 0);
        Grid.SetColumn(type, 1);
        Grid.SetColumn(size, 2);
        grid.Children.Add(name);
        grid.Children.Add(type);
        grid.Children.Add(size);
        return grid;
    }

    private void PopulateFolderTree()
    {
        folderTree.Items.Clear();

        var rootItem = new TreeViewItem
        {
            Header = Path.GetFileName(vpkPath),
            Tag = root,
            IsExpanded = true,
        };

        foreach (var folder in SortedFolders(root))
        {
            rootItem.Items.Add(CreateFolderItem(folder));
        }

        folderTree.Items.Add(rootItem);
        folderTree.SelectedItem = rootItem;
    }

    private TreeViewItem CreateFolderItem(PackageTreeNode node)
    {
        var item = new TreeViewItem { Header = node.Name, Tag = node };

        if (node.Folders.Count > 0)
        {
            // Placeholder so the expander shows; replaced on first expand.
            item.Items.Add(new TreeViewItem { Header = "..." });
        }

        item.Expanded += OnFolderExpanded;
        return item;
    }

    private void OnFolderExpanded(object? sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem item || item.Tag is not PackageTreeNode node)
        {
            return;
        }

        // Only handle the node that actually expanded, not the ancestors the event bubbles through.
        e.Handled = true;

        if (item.Items.Count == 1 && item.Items[0] is TreeViewItem { Tag: null })
        {
            item.Items.Clear();

            foreach (var folder in SortedFolders(node))
            {
                item.Items.Add(CreateFolderItem(folder));
            }
        }
    }

    private void OnFolderSelected()
    {
        if (folderTree.SelectedItem is TreeViewItem { Tag: PackageTreeNode node })
        {
            currentFolder = node;
            RefreshFileList();
        }
    }

    private void RefreshFileList()
    {
        var query = filterBox.Text?.Trim() ?? string.Empty;
        List<PackageEntry> entries;

        if (query.Length == 0)
        {
            entries = [.. currentFolder.Files];
        }
        else if (searchAllFolders.IsChecked == true)
        {
            entries = PackageTree.Search(root, query, PackageSearchMode.FileNamePartialMatch);
        }
        else
        {
            entries = PackageTree.Search(currentFolder, query, PackageSearchMode.FileNamePartialMatch);
        }

        var rows = entries
            .Select(entry => new FileRow(
                entry.GetFileName(),
                entry.TypeName,
                HumanReadableByteSizeFormatter.Format(entry.TotalLength),
                entry))
            .OrderBy(static row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        fileList.ItemsSource = rows;
        countLabel.Text = $"{rows.Count:N0} file{(rows.Count == 1 ? string.Empty : "s")}";
    }

    private void OpenSelected()
    {
        if (fileList.SelectedItem is FileRow row)
        {
            _ = window.OpenPackageEntryAsync(package, vpkPath, row.Entry);
        }
    }

    private ContextMenu BuildFileContextMenu()
    {
        var open = new MenuItem { Header = "Open" };
        open.Click += (_, _) => OpenSelected();

        var copyPath = new MenuItem { Header = "Copy path" };
        copyPath.Click += (_, _) =>
        {
            if (fileList.SelectedItem is FileRow row)
            {
                AppClipboard.SetText(row.Entry.GetFullPath());
            }
        };

        var export = new MenuItem { Header = "Export..." };
        export.Click += (_, _) => ExportSelected();

        var menu = new ContextMenu();
        menu.Items.Add(open);
        menu.Items.Add(copyPath);
        menu.Items.Add(export);
        return menu;
    }

    private void ExportSelected()
    {
        if (fileList.SelectedItem is not FileRow row)
        {
            return;
        }

        var dest = AppFileDialogs.SaveFile(
            "Export file",
            row.Name,
            Path.GetExtension(row.Name).TrimStart('.'),
            "All files (*.*)|*.*");

        if (string.IsNullOrEmpty(dest))
        {
            return;
        }

        try
        {
            using var stream = GameFileLoader.GetPackageEntryStream(package, row.Entry);
            using var file = File.Create(dest);
            stream.CopyTo(file);
            Log.Info(nameof(PackageBrowserView), $"Exported {row.Entry.GetFullPath()} to {dest}");
        }
        catch (Exception e)
        {
            Log.Error(nameof(PackageBrowserView), e.ToString());
        }
    }

    private void OnFileListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(fileList).Properties.IsRightButtonPressed
            && (e.Source as Control)?.DataContext is FileRow row)
        {
            fileList.SelectedItem = row;
        }
    }

    private static IEnumerable<PackageTreeNode> SortedFolders(PackageTreeNode node)
        => node.Folders.Values.OrderBy(static folder => folder.Name, StringComparer.OrdinalIgnoreCase);

    private static int CountFolders(PackageTreeNode node)
    {
        var count = node.Folders.Count;

        foreach (var folder in node.Folders.Values)
        {
            count += CountFolders(folder);
        }

        return count;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        package.Dispose();
        GC.SuppressFinalize(this);
    }
}
