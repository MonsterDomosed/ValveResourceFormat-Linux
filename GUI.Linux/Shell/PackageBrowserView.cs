using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GUI.Linux.Types.Browser;
using GUI.Linux.Types.Exporter;
using GUI.Linux.Utils;
using ValvePak;
using ValveResourceFormat.IO;

namespace GUI.Linux.Shell;

/// <summary>
/// Avalonia package browser for a single VPK: a lazily-built folder tree, a file list that shows
/// folders and files together as an icon grid (or as a sortable list), and double-click-to-open
/// routed through the shell. Files show a thumbnail preview when one can be decoded. Navigation and
/// search come from the shared <see cref="PackageTree"/>; only the presentation is Avalonia.
/// </summary>
internal sealed class PackageBrowserView : UserControl, IDisposable
{
    private sealed class PackageListEntry
    {
        public required string Name { get; init; }
        public PackageTreeNode? Folder { get; init; }
        public PackageEntry? Entry { get; init; }
        public string Type { get; init; } = string.Empty;
        public string Size { get; init; } = string.Empty;
        public bool IsParent { get; init; }
        public bool IsFolder => Folder is not null;
        public bool ThumbnailRequested { get; set; }
        public Bitmap? Thumbnail { get; set; }
        public Image? ThumbnailImage { get; set; }
    }

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
    private readonly ToggleButton iconsToggle = new() { Content = "Icons", IsChecked = true };
    private readonly ToggleButton listToggle = new() { Content = "List" };
    private readonly SemaphoreSlim thumbnailGate = new(3);
    private readonly DispatcherTimer filterTimer;
    private readonly DispatcherTimer visibleCheckTimer;
    private ScrollViewer? fileScrollViewer;

    /// <summary>
    /// How many entries the list shows at once. A package holds over a hundred thousand entries, and
    /// the list is not virtualized in the icons view, so showing all of them would build a visual
    /// tree that takes a minute to create.
    /// </summary>
    private const int MaxDisplayedResults = 1000;

    /// <summary>How long the filter waits for the typing to settle before it searches.</summary>
    private static readonly TimeSpan FilterDelay = TimeSpan.FromMilliseconds(200);

    private PackageTreeNode currentFolder;
    private List<PackageListEntry> currentEntries = [];
    private CancellationTokenSource? thumbnailCancellation;
    private int totalMatches;
    private int refreshCount;
    private bool iconsMode = true;
    private bool suppressTreeRefresh;
    private bool disposed;

    public PackageBrowserView(string vpkPath, MainWindow window)
        : this(vpkPath, window, Prepare(vpkPath))
    {
    }

    private PackageBrowserView(string vpkPath, MainWindow window, (Package Package, PackageTreeNode Root) prepared)
        : this(vpkPath, window, prepared.Package, prepared.Root)
    {
    }

    internal PackageBrowserView(string vpkPath, MainWindow window, Package package, PackageTreeNode root)
    {
        this.vpkPath = vpkPath;
        this.window = window;

        this.package = package;
        this.root = root;
        currentFolder = root;

        // The search runs ahead of the preview work, so it is not stuck behind it.
        filterTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = FilterDelay };
        filterTimer.Tick += (_, _) =>
        {
            filterTimer.Stop();
            RefreshFileList();
        };

        visibleCheckTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        visibleCheckTimer.Tick += OnVisibleCheckTick;

        Content = BuildLayout();
        PopulateFolderTree();
        RefreshFileList();

        header.Text = $"{vpkPath}    {root.TotalFileCount:N0} entries, {CountFolders(root):N0} folders, "
            + $"{HumanReadableByteSizeFormatter.Format(root.TotalSize)}";
    }

    /// <summary>Total entries in the package, for the self-check.</summary>
    /// <summary>
    /// Reads a package and builds its tree. Does no UI work, so the shell can run it off the UI
    /// thread before constructing the view.
    /// </summary>
    public static (Package Package, PackageTreeNode Root) Prepare(string vpkPath)
    {
        var package = new Package();
        package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
        package.Read(vpkPath);

        return (package, PackageTree.Build(package));
    }

    /// <summary>The package file this browser shows.</summary>
    internal string VpkPath => vpkPath;

    internal int EntryCount => root.TotalFileCount;

    /// <summary>The opened package, for the self-check.</summary>
    internal Package Package => package;

    /// <summary>Total folders in the package, for the self-check.</summary>
    internal int FolderCount => CountFolders(root);

    /// <summary>Folders currently shown in the list (parent navigation excluded), for the self-check.</summary>
    internal int ListFolderCount => currentEntries.Count(static entry => entry.IsFolder && !entry.IsParent);

    /// <summary>Files currently shown in the list, for the self-check.</summary>
    internal int ListFileCount => currentEntries.Count(static entry => entry.Entry is not null);

    /// <summary>Entries with a decoded thumbnail in the current list, for the self-check.</summary>
    internal int ThumbnailCount => currentEntries.Count(static entry => entry.Thumbnail is not null);

    /// <summary>Runs the same search the UI uses, for the self-check.</summary>
    internal List<PackageEntry> Search(string query, PackageSearchMode mode) => PackageTree.Search(root, query, mode);

    /// <summary>Navigates to the folder that contains <paramref name="entry"/>. Used by the self-check.</summary>
    internal bool NavigateToFolderOf(PackageEntry entry)
    {
        var node = root;

        foreach (var part in entry.DirectoryName.Split(ValvePak.Package.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!node.Folders.TryGetValue(part, out var child))
            {
                return false;
            }

            node = child;
        }

        NavigateTo(node);
        return true;
    }

    /// <summary>Waits until at least <paramref name="minimum"/> thumbnails are decoded. For the self-check.</summary>
    internal async Task<int> WaitForThumbnailsAsync(int minimum, int timeoutMs, string? suffix = null)
    {
        for (var waited = 0; waited < timeoutMs; waited += 100)
        {
            var count = CountThumbnails(suffix);

            if (count >= minimum)
            {
                return count;
            }

            await Task.Delay(100).ConfigureAwait(true);
        }

        return CountThumbnails(suffix);
    }

    private int CountThumbnails(string? suffix)
        => suffix is null
            ? ThumbnailCount
            : currentEntries.Count(entry => entry.Thumbnail is not null
                && entry.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    private DockPanel BuildLayout()
    {
        header.TextWrapping = TextWrapping.Wrap;
        header.Margin = new Thickness(8, 6, 8, 2);
        header.FontWeight = FontWeight.SemiBold;

        filterBox.PlaceholderText = "Filter by name or path...";
        filterBox.Width = 320;

        // Searching a whole package matches tens of thousands of entries, so the search waits until
        // the typing settles instead of running again for every keystroke.
        filterBox.TextChanged += (_, _) => ScheduleRefresh();

        searchAllFolders.IsCheckedChanged += (_, _) => RefreshFileList();

        countLabel.VerticalAlignment = VerticalAlignment.Center;
        countLabel.Opacity = 0.7;
        countLabel.Margin = new Thickness(8, 0);

        iconsToggle.Classes.Add("tool");
        listToggle.Classes.Add("tool");
        iconsToggle.IsCheckedChanged += (_, _) => SetViewMode(icons: iconsToggle.IsChecked == true);
        listToggle.IsCheckedChanged += (_, _) => SetViewMode(icons: listToggle.IsChecked != true);

        var viewToggles = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { iconsToggle, listToggle },
        };

        var filterRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(8, 4),
            Children = { filterBox, searchAllFolders, viewToggles, countLabel },
        };

        folderTree.Width = 320;
        folderTree.SelectionChanged += (_, _) => OnFolderSelected();
        folderTree.ContextMenu = BuildFolderContextMenu();

        fileList.SelectionMode = SelectionMode.Multiple;
        fileList.DoubleTapped += (_, _) => OpenSelected();
        fileList.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Back)
            {
                OpenSelected();
                e.Handled = true;
            }
        };
        fileList.ContextMenu = BuildFileContextMenu();
        fileList.PointerPressed += OnFileListPointerPressed;

        ApplyViewMode();

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

    private void SetViewMode(bool icons)
    {
        if (iconsMode == icons)
        {
            return;
        }

        iconsMode = icons;
        iconsToggle.IsChecked = icons;
        listToggle.IsChecked = !icons;
        ApplyViewMode();
    }

    private void ApplyViewMode()
    {
        if (iconsMode)
        {
            fileList.ItemsPanel = new FuncTemplate<Panel?>(static () => new WrapPanel
            {
                Orientation = Orientation.Horizontal,
            });
            fileList.ItemTemplate = new FuncDataTemplate<PackageListEntry>((entry, _) => BuildGridTile(entry), supportsRecycling: false);
        }
        else
        {
            // The list view virtualizes, so only the rows on screen exist as controls. The icons view
            // has no virtualizing wrap panel to fall back on, which is why the number of results is
            // capped instead.
            fileList.ItemsPanel = new FuncTemplate<Panel?>(static () => new VirtualizingStackPanel());
            fileList.ItemTemplate = new FuncDataTemplate<PackageListEntry>((entry, _) => BuildListRow(entry), supportsRecycling: false);
        }
    }

    private static StackPanel BuildGridTile(PackageListEntry entry)
    {
        var preview = new Image
        {
            Width = 96,
            Height = 96,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        preview.Source = ResolvePreviewSource(entry, 72);
        preview.Tag = entry;
        entry.ThumbnailImage = preview;

        var name = new TextBlock
        {
            Text = entry.Name,
            Width = 112,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 36,
        };

        var sub = new TextBlock
        {
            Text = entry.IsFolder ? entry.Size : entry.Type,
            Classes = { "hint" },
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var tile = new StackPanel
        {
            Width = 124,
            Spacing = 2,
            Margin = new Thickness(4),
            Children = { preview, name, sub },
        };

        ToolTip.SetTip(tile, entry.Entry?.GetFullPath() ?? entry.Name);
        return tile;
    }

    private static Grid BuildListRow(PackageListEntry entry)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,130,90"),
        };

        var icon = new Image
        {
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        icon.Source = ResolvePreviewSource(entry, 16);
        icon.Tag = entry;
        entry.ThumbnailImage = icon;

        var name = new TextBlock { Text = entry.Name, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1), VerticalAlignment = VerticalAlignment.Center };
        var type = new TextBlock { Text = entry.Type, Opacity = 0.7, Margin = new Thickness(0, 1), VerticalAlignment = VerticalAlignment.Center };
        var size = new TextBlock { Text = entry.Size, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 1), VerticalAlignment = VerticalAlignment.Center };

        Grid.SetColumn(icon, 0);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(type, 2);
        Grid.SetColumn(size, 3);
        grid.Children.Add(icon);
        grid.Children.Add(name);
        grid.Children.Add(type);
        grid.Children.Add(size);
        return grid;
    }

    private static Bitmap? ResolvePreviewSource(PackageListEntry entry, int iconSize)
    {
        if (entry.IsFolder)
        {
            return UI.IconFactory.Get(entry.IsParent ? "FolderUp" : "Folder", iconSize);
        }

        if (entry.Thumbnail is not null)
        {
            return entry.Thumbnail;
        }

        var icon = entry.Entry is null ? null : MainWindow.IconForFile(entry.Entry.GetFileName());
        return UI.IconFactory.Get(icon ?? "File", iconSize);
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

    private void EnsureChildren(TreeViewItem item, PackageTreeNode node)
    {
        if (item.Items.Count == 1 && item.Items[0] is TreeViewItem { Tag: null })
        {
            item.Items.Clear();

            foreach (var folder in SortedFolders(node))
            {
                item.Items.Add(CreateFolderItem(folder));
            }
        }
    }

    private void OnFolderExpanded(object? sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem item || item.Tag is not PackageTreeNode node)
        {
            return;
        }

        // Only handle the node that actually expanded, not the ancestors the event bubbles through.
        e.Handled = true;
        EnsureChildren(item, node);
    }

    private void OnFolderSelected()
    {
        if (suppressTreeRefresh)
        {
            return;
        }

        if (folderTree.SelectedItem is TreeViewItem { Tag: PackageTreeNode node })
        {
            NavigateTo(node);
        }
    }

    private void NavigateTo(PackageTreeNode node)
    {
        currentFolder = node;
        RefreshFileList();
        SyncFolderTreeSelection(node);
    }

    // Expands the tree down to the given folder and selects it, so list navigation keeps the tree in
    // sync. The tree is built lazily, so missing levels are materialized on the way.
    private void SyncFolderTreeSelection(PackageTreeNode node)
    {
        if (node == root || folderTree.Items.Count == 0 || folderTree.Items[0] is not TreeViewItem rootItem)
        {
            return;
        }

        var chain = new List<PackageTreeNode>();
        var current = node;

        while (current is not null && current != root)
        {
            chain.Add(current);
            current = current.Parent;
        }

        chain.Reverse();

        suppressTreeRefresh = true;

        try
        {
            var item = rootItem;
            var parentNode = root;

            foreach (var child in chain)
            {
                item.IsExpanded = true;
                EnsureChildren(item, parentNode);

                var next = item.Items
                    .OfType<TreeViewItem>()
                    .FirstOrDefault(candidate => ReferenceEquals(candidate.Tag, child));

                if (next is null)
                {
                    return;
                }

                item = next;
                parentNode = child;
            }

            folderTree.SelectedItem = item;
        }
        finally
        {
            suppressTreeRefresh = false;
        }
    }

    private void RefreshFileList()
    {
        refreshCount++;

        CancelThumbnails();

        var query = filterBox.Text?.Trim() ?? string.Empty;
        var entries = new List<PackageListEntry>();

        if (query.Length == 0)
        {
            if (currentFolder.Parent is { } parent)
            {
                entries.Add(new PackageListEntry
                {
                    Name = parent.Parent is null ? ".." : $".. {parent.Name}",
                    Folder = parent,
                    IsParent = true,
                    Size = HumanReadableByteSizeFormatter.Format(parent.TotalSize),
                });
            }

            foreach (var folder in SortedFolders(currentFolder))
            {
                entries.Add(new PackageListEntry
                {
                    Name = folder.Name,
                    Folder = folder,
                    Size = HumanReadableByteSizeFormatter.Format(folder.TotalSize),
                });
            }

            totalMatches = currentFolder.Files.Count;

            foreach (var file in SortedFiles(currentFolder.Files.Take(MaxDisplayedResults)))
            {
                entries.Add(CreateFileEntry(file));
            }
        }
        else
        {
            var matches = searchAllFolders.IsChecked == true
                ? PackageTree.Search(root, query, PackageSearchMode.FileNamePartialMatch, MaxDisplayedResults, out totalMatches)
                : PackageTree.Search(currentFolder, query, PackageSearchMode.FileNamePartialMatch, MaxDisplayedResults, out totalMatches);

            foreach (var file in SortedFiles(matches))
            {
                entries.Add(CreateFileEntry(file));
            }
        }

        currentEntries = entries;
        fileList.ItemsSource = currentEntries;

        var folders = ListFolderCount;
        var files = ListFileCount;

        if (query.Length > 0)
        {
            countLabel.Text = totalMatches > files
                ? $"showing first {files:N0} of {totalMatches:N0} matches, refine the search"
                : $"{files:N0} file{(files == 1 ? string.Empty : "s")}";
        }
        else
        {
            countLabel.Text = totalMatches > files
                ? $"{folders:N0} folder{(folders == 1 ? string.Empty : "s")}, showing first {files:N0} of {totalMatches:N0} files"
                : $"{folders:N0} folder{(folders == 1 ? string.Empty : "s")}, {files:N0} file{(files == 1 ? string.Empty : "s")}";
        }

        QueueThumbnails();
        ScheduleVisibleThumbnailCheck();
    }

    /// <summary>Waits for the typing to settle, then refreshes the list.</summary>
    private void ScheduleRefresh()
    {
        filterTimer.Stop();
        filterTimer.Start();
    }

    /// <summary>
    /// Runs a search over the whole package and reports what the list ended up showing and how long
    /// it took, for the self-check.
    /// </summary>
    internal (int Shown, int Total, long ElapsedMilliseconds, int Models, int Textures) SearchAllFolders(string query)
    {
        searchAllFolders.IsChecked = true;
        filterBox.Text = query;

        var watch = Stopwatch.StartNew();
        RefreshFileList();

        var models = currentEntries.Count(static e => e.Entry is not null && e.Entry.GetFileName().EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase));
        var textures = currentEntries.Count(static e => e.Entry is not null && e.Entry.GetFileName().EndsWith(".vtex_c", StringComparison.OrdinalIgnoreCase));

        return (currentEntries.Count, totalMatches, watch.ElapsedMilliseconds, models, textures);
    }

    /// <summary>
    /// Types a query the way a person would, one character at a time, and reports how often the list
    /// was rebuilt and what it ended up showing, for the self-check.
    /// </summary>
    internal async Task<(int Shown, int Total, int Rebuilds)> TypeSearchAsync(string query)
    {
        searchAllFolders.IsChecked = true;
        filterBox.Text = string.Empty;
        await Task.Delay(FilterDelay * 2).ConfigureAwait(true);

        var rebuildsBefore = refreshCount;

        foreach (var character in query)
        {
            filterBox.Text += character;
            await Task.Delay(20).ConfigureAwait(true);
        }

        await Task.Delay(FilterDelay * 3).ConfigureAwait(true);

        return (currentEntries.Count, totalMatches, refreshCount - rebuildsBefore);
    }

    private static PackageListEntry CreateFileEntry(PackageEntry file) => new()
    {
        Name = file.GetFileName(),
        Entry = file,
        Type = file.TypeName,
        Size = HumanReadableByteSizeFormatter.Format(file.TotalLength),
    };

    private static IEnumerable<PackageEntry> SortedFiles(IEnumerable<PackageEntry> files)
        => files.OrderBy(static file => file.GetFileName(), StringComparer.OrdinalIgnoreCase);

    private void QueueThumbnails()
    {
        // Model thumbnails render on the UI thread through an offscreen GL context, so they are only
        // produced while this browser tab is actually visible. Work resumes when the tab is shown.
        if (disposed || !IsEffectivelyVisible)
        {
            return;
        }

        thumbnailCancellation = new CancellationTokenSource();
    }

    /// <summary>
    /// Asks for the previews of the entries that are on screen. A search can match a hundred thousand
    /// entries, and producing a preview for each of them would keep the user interface busy long
    /// after the list itself is ready.
    /// </summary>
    private void RequestVisibleThumbnails()
    {
        if (disposed || thumbnailCancellation is null)
        {
            return;
        }

        var scroll = fileList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

        if (scroll is null)
        {
            return;
        }

        foreach (var entry in currentEntries)
        {
            if (entry.Thumbnail is not null || entry.ThumbnailRequested || entry.ThumbnailImage is not { } image)
            {
                continue;
            }

            if (IsInsideViewport(image, scroll))
            {
                RequestThumbnail(entry);
            }
        }
    }

    /// <summary>Whether the image lies within the visible part of the list.</summary>
    private static bool IsInsideViewport(Control image, ScrollViewer scroll)
    {
        if (image.TransformToVisual(scroll) is not { } transform)
        {
            return false;
        }

        var topLeft = transform.Transform(new Point(image.Bounds.Left, image.Bounds.Top));
        var bottomRight = transform.Transform(new Point(image.Bounds.Right, image.Bounds.Bottom));
        var bounds = new Rect(topLeft, bottomRight);

        return bounds.Intersects(scroll.Bounds);
    }

    /// <summary>
    /// Asks for the preview of a single entry. The list only creates the controls for the entries it
    /// shows, so this runs for the visible entries rather than for everything the search matched.
    /// </summary>
    private void RequestThumbnail(PackageListEntry entry)
    {
        if (disposed || entry.Entry is not { } file || entry.Thumbnail is not null || entry.ThumbnailRequested)
        {
            return;
        }

        if (!PackageThumbnails.CanThumbnail(file) || thumbnailCancellation is null)
        {
            return;
        }

        entry.ThumbnailRequested = true;
        _ = LoadThumbnailAsync(entry, file, thumbnailCancellation.Token);
    }

    private async Task LoadThumbnailAsync(PackageListEntry entry, PackageEntry file, CancellationToken cancellationToken)
    {
        try
        {
            await thumbnailGate.WaitAsync(cancellationToken).ConfigureAwait(true);

            try
            {
                var bitmap = await Task.Run(() => PackageThumbnails.Get(package, file, cancellationToken), cancellationToken).ConfigureAwait(true);

                if (bitmap is null || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (disposed || cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    entry.Thumbnail = bitmap;

                    // The list recycles rows, so the image that was showing this entry may now show a
                    // different one, and updating it would put the wrong preview there.
                    if (entry.ThumbnailImage is { } image && ReferenceEquals(image.Tag, entry))
                    {
                        image.Source = bitmap;
                    }
                });
            }
            finally
            {
                try
                {
                    thumbnailGate.Release();
                }
                catch (ObjectDisposedException)
                {
                    // The tab was closed while the thumbnail was in flight.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Folder changed or the tab was closed; the entries are discarded.
        }
        catch (ObjectDisposedException)
        {
            // The tab was closed while the thumbnail was in flight.
        }
        catch (Exception e)
        {
            Log.Warn(nameof(PackageBrowserView), $"Thumbnail failed for {file.GetFullPath()}: {e.Message}");
        }
    }

    private void CancelThumbnails()
    {
        var cancellation = thumbnailCancellation;

        if (cancellation is null)
        {
            return;
        }

        thumbnailCancellation = null;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void ResumeThumbnails()
    {
        QueueThumbnails();

        // The rows that are already on screen keep their controls, so their previews have to be
        // asked for again. Rows that were never created have no image yet and ask when they appear.
        foreach (var entry in currentEntries)
        {
            if (entry.Thumbnail is null && entry.ThumbnailImage is not null)
            {
                entry.ThumbnailRequested = false;
            }
        }

        RequestVisibleThumbnails();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (IsEffectivelyVisible)
        {
            ResumeThumbnails();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != IsVisibleProperty)
        {
            return;
        }

        if (IsEffectivelyVisible)
        {
            ResumeThumbnails();
        }
        else
        {
            CancelThumbnails();
        }
    }

    /// <summary>
    /// Looks for the previews of the entries that came into view. The check runs on a timer, so a
    /// scroll does not turn into a walk over every entry of the list.
    /// </summary>
    private void ScheduleVisibleThumbnailCheck()
    {
        if (disposed)
        {
            return;
        }

        visibleCheckTimer.Stop();
        visibleCheckTimer.Start();
    }

    private void OnVisibleCheckTick(object? sender, EventArgs e)
    {
        visibleCheckTimer.Stop();

        if (disposed)
        {
            return;
        }

        if (!IsEffectivelyVisible)
        {
            // The tab is hidden, so the previews wait until it is shown again.
            visibleCheckTimer.Start();
            return;
        }

        if (thumbnailCancellation is null)
        {
            QueueThumbnails();
        }

        RequestVisibleThumbnails();
    }

    /// <summary>Notices that the list scrolled, so the previews of the new entries can be asked for.</summary>
    private void OnFileListScrolled(object? sender, ScrollChangedEventArgs e)
    {
        ScheduleVisibleThumbnailCheck();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // The list creates its scroll viewer with its template, so it is only there once loaded.
        if (fileScrollViewer is null)
        {
            fileScrollViewer = fileList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

            if (fileScrollViewer is not null)
            {
                fileScrollViewer.ScrollChanged += OnFileListScrolled;
            }
        }

        ScheduleVisibleThumbnailCheck();
    }

    private void OpenSelected()
    {
        var selected = SelectedListEntries();

        // Selecting a folder in the list navigates into it, even as part of a larger selection
        if (selected.FirstOrDefault(static item => item.Folder is not null) is { } folderEntry)
        {
            NavigateTo(folderEntry.Folder!);
            return;
        }

        // Opening a multi selection opens every selected file, like a double click on it
        foreach (var item in selected)
        {
            if (item.Entry is { } file)
            {
                _ = window.OpenPackageEntryAsync(package, vpkPath, file);
            }
        }
    }

    private List<PackageListEntry> SelectedListEntries() => fileList.SelectedItems?
        .OfType<PackageListEntry>()
        .ToList() ?? [];

    /// <summary>Package entries behind the current file list selection. Folder rows select nothing.</summary>
    private List<PackageEntry> SelectedEntries()
        => SelectedListEntries().Where(static item => item.Entry is not null).Select(static item => item.Entry!).ToList();

    private ContextMenu BuildFileContextMenu()
    {
        var open = new MenuItem { Header = "Open" };
        open.Click += (_, _) => OpenSelected();

        var copyPath = new MenuItem { Header = "Copy path" };
        copyPath.Click += (_, _) =>
        {
            foreach (var entry in SelectedEntries())
            {
                AppClipboard.SetText(entry.GetFullPath());
                break;
            }
        };

        var export = new MenuItem { Header = "Export as is", Icon = new GUI.Linux.UI.SvgIcon("Export", 16) };
        export.Click += (_, _) => ExportSelected(decompile: false);

        var decompile = new MenuItem { Header = "Decompile & export", Icon = new GUI.Linux.UI.SvgIcon("Decompile", 16) };
        decompile.Click += (_, _) => ExportSelected(decompile: true);

        var menu = new ContextMenu();
        menu.Items.Add(open);
        menu.Items.Add(copyPath);
        menu.Items.Add(export);
        menu.Items.Add(decompile);

        menu.Opening += (_, _) =>
        {
            var selection = SelectedEntries();
            var singleFile = selection.Count == 1;

            copyPath.IsEnabled = singleFile;
            export.IsEnabled = selection.Count > 0;
            decompile.IsEnabled = selection.Count > 0;
        };

        return menu;
    }

    /// <summary>Context menu for the folder tree, which exports the selected folder's contents.</summary>
    private ContextMenu BuildFolderContextMenu()
    {
        var export = new MenuItem { Header = "Export as is", Icon = new GUI.Linux.UI.SvgIcon("Export", 16) };
        export.Click += (_, _) => ExportFolder(decompile: false);

        var decompile = new MenuItem { Header = "Decompile & export", Icon = new GUI.Linux.UI.SvgIcon("Decompile", 16) };
        decompile.Click += (_, _) => ExportFolder(decompile: true);

        var menu = new ContextMenu();
        menu.Items.Add(export);
        menu.Items.Add(decompile);

        return menu;
    }

    private void ExportFolder(bool decompile)
    {
        if (folderTree.SelectedItem is not TreeViewItem { Tag: PackageTreeNode node })
        {
            return;
        }

        _ = ExportFile.ExtractFromFolderAsync(window, package, node, decompile);
    }

    private void ExportSelected(bool decompile)
    {
        var entries = SelectedEntries();

        if (entries.Count == 0)
        {
            return;
        }

        // A single file can be written wherever the user wants, several go into a chosen folder
        if (entries.Count == 1)
        {
            _ = ExportFile.ExtractFromPackageEntryAsync(window, package, entries[0], decompile);
            return;
        }

        _ = ExportFile.ExtractFromEntriesAsync(window, package, entries, decompile);
    }

    private void OnFileListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(fileList).Properties.IsRightButtonPressed
            && (e.Source as Control)?.DataContext is PackageListEntry entry)
        {
            fileList.SelectedItem = entry;
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
        filterTimer.Stop();
        visibleCheckTimer.Stop();
        CancelThumbnails();
        thumbnailGate.Dispose();
        package.Dispose();
        GC.SuppressFinalize(this);
    }
}
