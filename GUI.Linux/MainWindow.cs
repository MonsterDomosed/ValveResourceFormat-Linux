using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using GUI.Linux.GL;
using GUI.Linux.Platform;
using GUI.Linux.Shell;
using GUI.Linux.Types.Viewers;
using GUI.Linux.Utils;
using GUI.Linux.Viewers;
using ValvePak;
using ValveResourceFormat.IO;

namespace GUI.Linux;

/// <summary>
/// The Avalonia application shell: menus, a closable tab strip, the console tab and the status bar.
/// Viewers are loaded through the portable <see cref="IViewer"/> contract and rendered by
/// <see cref="AvaloniaViewerContentPresenter"/>.
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly string[] args;
    private readonly TabControl mainTabs = new();
    private readonly Grid contentHost = new();
    private readonly Dictionary<TabItem, Control> tabContents = [];
    private readonly ConsoleView consoleView = new();
    private readonly TextBlock statusText = new();
    private readonly MenuItem recentFilesMenu = new() { Header = "_Open Recent" };
    private readonly MenuItem bookmarksMenu = new() { Header = "_Bookmarks" };
    private readonly Dictionary<TabItem, string> tabPaths = [];
    private TabItem consoleTab = null!;

    /// <summary>Number of open tabs, used by the self-check.</summary>
    internal int TabCount => mainTabs.Items.Count;

    /// <summary>The open tabs, for the self-check.</summary>
    internal IReadOnlyList<TabItem> Tabs => [.. mainTabs.Items.OfType<TabItem>()];

    /// <summary>Title of the selected tab, used by the self-check.</summary>
    internal string SelectedTabTitle => GetTabTitle(mainTabs.SelectedItem as TabItem);

    public MainWindow(string[] args)
    {
        this.args = args;

        Title = "Source 2 Viewer";
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Source2Viewer/Assets/source2viewer.png")));
        Width = 1100;
        Height = 720;
        MinWidth = 640;
        MinHeight = 400;

        RestoreWindowGeometry();

        Content = BuildLayout();
        RefreshRecentFiles();
        RefreshBookmarks();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        KeyDown += OnKeyDown;
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private DockPanel BuildLayout()
    {
        var menu = new Menu();
        menu.Items.Add(BuildFileMenu());
        menu.Items.Add(BuildViewMenu());
        menu.Items.Add(BuildHelpMenu());

        mainTabs.SelectionChanged += (_, _) =>
        {
            UpdateContentVisibility();
            UpdateStatus();
            RefreshBookmarks();
        };

        // The tab strip and the content are separated so tab contents stay mounted. This keeps GL
        // viewports alive (and their cameras independent) when another tab is selected, which the
        // stock TabControl does not do: it detaches the previous tab's content.
        mainTabs.Template = new FuncControlTemplate<TabControl>((_, scope) =>
        {
            var presenter = new ItemsPresenter
            {
                // Lay the tabs out left to right; a bare ItemsPresenter stacks them vertically.
                ItemsPanel = new FuncTemplate<Panel?>(static () => new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                }),
            };
            scope.Register("PART_ItemsPresenter", presenter);
            return presenter;
        });

        consoleTab = CreateTab("Console", consoleView, close: null, select: false, iconName: "Log");
        mainTabs.Items.Add(consoleTab);
        mainTabs.SelectedItem = consoleTab;
        UpdateContentVisibility();

        statusText.VerticalAlignment = VerticalAlignment.Center;
        statusText.Margin = new Thickness(8, 0);

        var statusBar = new Border
        {
            Classes = { "statusBar" },
            Child = statusText,
            Height = 26,
        };

        var dock = new DockPanel();
        DockPanel.SetDock(menu, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        DockPanel.SetDock(mainTabs, Dock.Top);
        dock.Children.Add(menu);
        dock.Children.Add(statusBar);
        dock.Children.Add(mainTabs);
        dock.Children.Add(contentHost);

        return dock;
    }

    private MenuItem BuildFileMenu()
    {
        var open = new MenuItem { Header = "_Open...", Icon = new GUI.Linux.UI.SvgIcon("Open", 16) };
        open.Click += (_, _) => OpenFilesFromDialog();

        recentFilesMenu.Icon = new GUI.Linux.UI.SvgIcon("History", 16);
        bookmarksMenu.Icon = new GUI.Linux.UI.SvgIcon("Bookmarks", 16);

        var exit = new MenuItem { Header = "E_xit" };
        exit.Click += (_, _) => Close();

        var fileMenu = new MenuItem { Header = "_File" };
        fileMenu.Items.Add(open);
        fileMenu.Items.Add(recentFilesMenu);
        fileMenu.Items.Add(bookmarksMenu);
        fileMenu.Items.Add(new Separator());
        fileMenu.Items.Add(exit);

        return fileMenu;
    }

    private MenuItem BuildViewMenu()
    {
        var browser = new MenuItem { Header = "_Browser", Icon = new GUI.Linux.UI.SvgIcon("Explorer", 16) };
        browser.Click += (_, _) => OpenBrowser();

        var console = new MenuItem { Header = "_Console", Icon = new GUI.Linux.UI.SvgIcon("Log", 16) };
        console.Click += (_, _) => SelectTab(consoleTab);

        var settings = new MenuItem { Header = "_Settings", Icon = new GUI.Linux.UI.SvgIcon("Settings", 16) };
        settings.Click += (_, _) => OpenSettings();

        var viewMenu = new MenuItem { Header = "_View" };
        viewMenu.Items.Add(browser);
        viewMenu.Items.Add(console);
        viewMenu.Items.Add(settings);

        return viewMenu;
    }

    private MenuItem BuildHelpMenu()
    {
        var about = new MenuItem { Header = "_About", Icon = new GUI.Linux.UI.SvgIcon("About", 16) };
        about.Click += (_, _) => ShowAbout();

        var helpMenu = new MenuItem { Header = "_Help" };
        helpMenu.Items.Add(about);

        return helpMenu;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Log.Info(nameof(MainWindow), "Shell opened");

        if (args.Length > 0)
        {
            foreach (var file in args)
            {
                await OpenFileAsync(file).ConfigureAwait(true);
            }
        }
        else
        {
            OpenBrowser();
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        SaveWindowGeometry();
        Settings.Save();
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is not { } files)
        {
            return;
        }

        foreach (var item in files)
        {
            if (item.TryGetLocalPath() is { Length: > 0 } path)
            {
                _ = OpenFileAsync(path);
            }
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (!control)
        {
            if (e.Key == Key.F5 && mainTabs.SelectedItem is TabItem active)
            {
                ReloadTab(active);
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.O:
                OpenFilesFromDialog();
                e.Handled = true;
                break;
            case Key.W:
                CloseCurrentTab();
                e.Handled = true;
                break;
            case Key.Q:
                CloseAllTabs();
                e.Handled = true;
                break;
            case Key.E:
                if (mainTabs.SelectedItem is TabItem right)
                {
                    CloseTabsRightOf(right);
                }

                e.Handled = true;
                break;
            case Key.R:
                if (mainTabs.SelectedItem is TabItem reload)
                {
                    ReloadTab(reload);
                }

                e.Handled = true;
                break;
            case Key.Tab:
                CycleTab(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                e.Handled = true;
                break;
        }
    }

    private void CycleTab(int direction)
    {
        if (mainTabs.Items.Count < 2 || mainTabs.SelectedItem is not TabItem current)
        {
            return;
        }

        var index = mainTabs.Items.IndexOf(current);
        var next = (index + direction + mainTabs.Items.Count) % mainTabs.Items.Count;
        mainTabs.SelectedItem = mainTabs.Items[next];
    }

    internal void OpenFilesFromDialog()
    {
        var files = AppFileDialogs.OpenFiles("Open files", "Valve Resource Format (*.*_c, *.vpk, *.vcs)|*.*_c;*.vpk;*.vcs|All files (*.*)|*.*");

        if (files is null)
        {
            return;
        }

        foreach (var file in files)
        {
            _ = OpenFileAsync(file);
        }
    }

    /// <summary>Opens a file in a new tab, loading it off the UI thread.</summary>
    /// <param name="path">Path to open.</param>
    /// <param name="trackRecent">Whether to record the path in the recent files list.</param>
    public async Task OpenFileAsync(string path, bool trackRecent = true)
    {
        // VPKs are containers, not resources; they open in the package browser.
        if (string.Equals(Path.GetExtension(path), ".vpk", StringComparison.OrdinalIgnoreCase))
        {
            OpenPackage(path);
            return;
        }

        if (!File.Exists(path))
        {
            Log.Error(nameof(MainWindow), $"File '{path}' does not exist.");
            return;
        }

        path = Path.GetFullPath(path);
        Log.Info(nameof(MainWindow), $"Opening {path}");

        var loading = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new GUI.Linux.UI.SvgIcon(IconForFile(path) ?? "File", 48),
                new TextBlock { Text = Path.GetFileName(path), Classes = { "section" }, HorizontalAlignment = HorizontalAlignment.Center },
                new ProgressBar { IsIndeterminate = true, Width = 240 },
                new TextBlock { Text = "Loading...", Classes = { "hint" }, HorizontalAlignment = HorizontalAlignment.Center },
            },
        };

        var tab = CreateTab(Path.GetFileName(path), loading, CloseTab, select: true, iconName: IconForFile(path));
        tab.Tag = path;
        tabPaths[tab] = path;
        mainTabs.Items.Add(tab);
        mainTabs.SelectedItem = tab;

        try
        {
            var viewer = await Task.Run(() => LinuxViewerFactory.CreateAndLoadAsync(path)).ConfigureAwait(true);

            if (!mainTabs.Items.Contains(tab))
            {
                // Closed while loading.
                viewer.Dispose();
                return;
            }

            var content = viewer.GetContent();
            SetTabContent(tab, content is null
                ? new TextBlock { Text = "The viewer produced no content.", Margin = new Thickness(16) }
                : AvaloniaViewerContentPresenter.Present(content));
            tab.Tag = viewer;
            viewer.NotifyVisible();
        }
        catch (Exception ex)
        {
            Log.Error(nameof(MainWindow), ex.ToString());
            SetTabContent(tab, CreateErrorView(path, ex));
        }

        Settings.TrackRecentFile(path);
        RefreshRecentFiles();
        RefreshBookmarks();
        UpdateStatus();
    }

    private static StackPanel CreateErrorView(string path, Exception exception)
    {
        var details = new TextBox
        {
            Text = exception.ToString(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("monospace"),
            Height = 280,
            IsVisible = false,
        };

        GUI.Linux.UI.ThemeResources.Bind(details, TextBox.BackgroundProperty, "InputBackground");
        GUI.Linux.UI.ThemeResources.Bind(details, TextBox.ForegroundProperty, "TextPrimary");

        var toggle = new Button { Content = "Show details", Classes = { "tool" } };
        toggle.Click += (_, _) =>
        {
            details.IsVisible = !details.IsVisible;
            toggle.Content = details.IsVisible ? "Hide details" : "Show details";
        };

        var copy = new Button { Content = "Copy details", Classes = { "tool" } };
        copy.Click += (_, _) => AppClipboard.SetText(exception.ToString());

        var heading = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                new GUI.Linux.UI.SvgIcon("Error", 20),
                new TextBlock
                {
                    Text = $"Could not open {Path.GetFileName(path)}",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                },
            },
        };

        var message = new TextBlock
        {
            Text = $"{exception.GetType().Name}: {exception.Message}",
            TextWrapping = TextWrapping.Wrap,
        };
        GUI.Linux.UI.ThemeResources.Bind(message, TextBlock.ForegroundProperty, "TextSecondary");

        return new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 10,
            Children =
            {
                heading,
                message,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { toggle, copy },
                },
                details,
            },
        };
    }

    /// <summary>Opens (or focuses) the source browser tab.</summary>
    internal BrowserView OpenBrowser()
    {
        if (FindTab("Browser") is { } existing)
        {
            SelectTab(existing);
            return (BrowserView)tabContents[existing];
        }

        var browser = new BrowserView(this);
        var tab = CreateTab("Browser", browser, CloseTab, select: true, iconName: "Explorer");
        mainTabs.Items.Add(tab);
        mainTabs.SelectedItem = tab;
        return browser;
    }

    /// <summary>Opens a VPK in a package browser tab and returns its view, or null when it is missing.</summary>
    internal PackageBrowserView? OpenPackage(string vpkPath)
    {
        if (!File.Exists(vpkPath))
        {
            Log.Error(nameof(MainWindow), $"Package '{vpkPath}' does not exist.");
            return null;
        }

        var view = new PackageBrowserView(vpkPath, this);
        var tab = CreateTab(Path.GetFileName(vpkPath), view, CloseTab, select: true, iconName: "vpk");
        tab.Tag = view;
        mainTabs.Items.Add(tab);
        mainTabs.SelectedItem = tab;
        UpdateStatus();
        return view;
    }

    /// <summary>
    /// Opens a resource stored inside a VPK: adds the package to the search paths so its own
    /// references resolve, extracts the entry to a session temp path, and opens it normally.
    /// </summary>
    internal async Task OpenPackageEntryAsync(Package package, string vpkPath, PackageEntry entry)
    {
        LinuxGameContent.AddSearchPackage(vpkPath);

        var entryRelative = entry.GetFullPath().Replace('/', Path.DirectorySeparatorChar);
        var packageKey = StablePathKey(vpkPath);
        var destRoot = Path.Combine(Path.GetTempPath(), "s2v-browser", $"{Path.GetFileName(vpkPath)}-{packageKey}");
        var dest = Path.Combine(destRoot, entryRelative);

        if (!File.Exists(dest))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            using var stream = GameFileLoader.GetPackageEntryStream(package, entry);
            using var file = File.Create(dest);
            await stream.CopyToAsync(file).ConfigureAwait(true);
        }

        await OpenFileAsync(dest, trackRecent: false).ConfigureAwait(true);
    }

    // Stable, collision-resistant key for a physical path without pulling in a cryptographic hash.
    private static string StablePathKey(string path)
    {
        var hash = 14695981039346656037UL;

        foreach (var value in Encoding.UTF8.GetBytes(path))
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Closes the first tab whose title matches. Used by the self-check.</summary>
    internal bool CloseTabByTitle(string title)
    {
        if (FindTab(title) is { } tab)
        {
            CloseTab(tab);
            return true;
        }

        return false;
    }

    private void RefreshRecentFiles()
    {
        recentFilesMenu.Items.Clear();

        var recent = Settings.Config.RecentFiles;

        if (recent.Count == 0)
        {
            recentFilesMenu.Items.Add(new MenuItem { Header = "(no recent files)", IsEnabled = false });
            return;
        }

        for (var i = recent.Count - 1; i >= 0 && i >= recent.Count - 10; i--)
        {
            var path = recent[i];
            var item = new MenuItem { Header = Path.GetFileName(path) };
            ToolTip.SetTip(item, path);
            item.Click += (_, _) => _ = OpenFileAsync(path);
            recentFilesMenu.Items.Add(item);
        }

        recentFilesMenu.Items.Add(new Separator());

        var clear = new MenuItem { Header = "Clear recent files" };
        clear.Click += (_, _) =>
        {
            Settings.Config.RecentFiles.Clear();
            Settings.Save();
            RefreshRecentFiles();
        };
        recentFilesMenu.Items.Add(clear);
    }

    private void RefreshBookmarks()
    {
        bookmarksMenu.Items.Clear();

        var bookmarks = Settings.Config.BookmarkedFiles;
        var currentPath = GetSelectedFilePath();

        var bookmarkCurrent = new MenuItem
        {
            Header = "Bookmark current file",
            IsEnabled = currentPath is not null && !bookmarks.Contains(currentPath, StringComparer.OrdinalIgnoreCase),
        };
        bookmarkCurrent.Click += (_, _) =>
        {
            if (GetSelectedFilePath() is { } path && !bookmarks.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                bookmarks.Add(path);
                Settings.Save();
                RefreshBookmarks();
            }
        };
        bookmarksMenu.Items.Add(bookmarkCurrent);

        var removeCurrent = new MenuItem
        {
            Header = "Remove bookmark for current file",
            IsEnabled = currentPath is not null && bookmarks.Contains(currentPath, StringComparer.OrdinalIgnoreCase),
        };
        removeCurrent.Click += (_, _) =>
        {
            if (GetSelectedFilePath() is { } path)
            {
                bookmarks.RemoveAll(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase));
                Settings.Save();
                RefreshBookmarks();
            }
        };
        bookmarksMenu.Items.Add(removeCurrent);

        if (bookmarks.Count == 0)
        {
            bookmarksMenu.Items.Add(new MenuItem { Header = "(no bookmarks)", IsEnabled = false });
            return;
        }

        bookmarksMenu.Items.Add(new Separator());

        foreach (var path in bookmarks)
        {
            var item = new MenuItem { Header = Path.GetFileName(path) };
            ToolTip.SetTip(item, path);
            item.Click += (_, _) => _ = OpenFileAsync(path);
            bookmarksMenu.Items.Add(item);
        }

        bookmarksMenu.Items.Add(new Separator());

        var clear = new MenuItem { Header = "Clear bookmarks" };
        clear.Click += (_, _) =>
        {
            bookmarks.Clear();
            Settings.Save();
            RefreshBookmarks();
        };
        bookmarksMenu.Items.Add(clear);
    }

    private string? GetSelectedFilePath()
        => mainTabs.SelectedItem is TabItem tab && tabPaths.TryGetValue(tab, out var path) ? path : null;

    internal void OpenSettings()
    {
        if (FindTab("Settings") is { } existing)
        {
            SelectTab(existing);
            return;
        }

        var tab = CreateTab("Settings", SettingsView.Create(), CloseTab, select: true, iconName: "Settings");
        mainTabs.Items.Add(tab);
        mainTabs.SelectedItem = tab;
    }

    private void ShowAbout()
    {
        _ = AboutDialog.ShowAsync(this);
    }

    private TabItem CreateTab(string header, Control content, Action<TabItem>? close, bool select, string? iconName = null)
    {
        var title = new TextBlock
        {
            Text = header,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (iconName is not null)
        {
            headerPanel.Children.Add(new GUI.Linux.UI.SvgIcon(iconName, 14));
        }

        headerPanel.Children.Add(title);

        var tab = new TabItem
        {
            Header = headerPanel,
        };

        ToolTip.SetTip(headerPanel, header);

        if (close is not null)
        {
            var closeButton = new Button
            {
                Content = new GUI.Linux.UI.SvgIcon("CloseTab", 12),
                Classes = { "icon" },
                VerticalAlignment = VerticalAlignment.Center,
            };

            closeButton.Click += (_, _) => close(tab);
            headerPanel.Children.Add(closeButton);
        }

        // Middle-click closes, and right-click opens the tab actions.
        headerPanel.PointerPressed += (_, e) =>
        {
            var properties = e.GetCurrentPoint(headerPanel).Properties;

            if (properties.IsMiddleButtonPressed && tab != consoleTab)
            {
                CloseTab(tab);
                e.Handled = true;
            }
        };

        headerPanel.ContextMenu = BuildTabContextMenu(tab);

        SetTabContent(tab, content);

        if (select)
        {
            mainTabs.SelectedItem = tab;
        }

        return tab;
    }

    private ContextMenu BuildTabContextMenu(TabItem tab)
    {
        var menu = new ContextMenu();
        List<Control> items = [];

        var reload = new MenuItem { Header = "Reload", InputGesture = new KeyGesture(Key.R, KeyModifiers.Control) };
        reload.Click += (_, _) => ReloadTab(tab);
        reload.IsEnabled = tabPaths.ContainsKey(tab);
        items.Add(reload);

        items.Add(new Separator());

        var close = new MenuItem { Header = "Close", InputGesture = new KeyGesture(Key.W, KeyModifiers.Control) };
        close.Click += (_, _) => CloseTab(tab);
        items.Add(close);

        var closeOthers = new MenuItem { Header = "Close others" };
        closeOthers.Click += (_, _) => CloseOtherTabs(tab);
        items.Add(closeOthers);

        var closeRight = new MenuItem { Header = "Close to the right", InputGesture = new KeyGesture(Key.E, KeyModifiers.Control) };
        closeRight.Click += (_, _) => CloseTabsRightOf(tab);
        items.Add(closeRight);

        var closeLeft = new MenuItem { Header = "Close to the left" };
        closeLeft.Click += (_, _) => CloseTabsLeftOf(tab);
        items.Add(closeLeft);

        menu.ItemsSource = items;
        return menu;
    }

    private void ReloadTab(TabItem tab)
    {
        if (!tabPaths.TryGetValue(tab, out var path))
        {
            return;
        }

        var index = mainTabs.Items.IndexOf(tab);
        CloseTab(tab);
        _ = OpenFileAsync(path);

        // OpenFileAsync appends the new tab; move it back to the original position.
        if (index >= 0 && mainTabs.Items.Count > index && mainTabs.Items[index] is TabItem reopened)
        {
            mainTabs.Items.Remove(reopened);
            mainTabs.Items.Insert(Math.Min(index, mainTabs.Items.Count), reopened);
            mainTabs.SelectedItem = reopened;
        }
    }

    private void CloseOtherTabs(TabItem keep)
    {
        foreach (var item in mainTabs.Items.OfType<TabItem>().ToList())
        {
            if (item != consoleTab && item != keep)
            {
                CloseTab(item);
            }
        }

        SelectTab(keep);
    }

    private void CloseTabsRightOf(TabItem reference)
    {
        var index = mainTabs.Items.IndexOf(reference);

        foreach (var item in mainTabs.Items.OfType<TabItem>().Skip(index + 1).ToList())
        {
            if (item != consoleTab)
            {
                CloseTab(item);
            }
        }

        SelectTab(reference);
    }

    private void CloseTabsLeftOf(TabItem reference)
    {
        var index = mainTabs.Items.IndexOf(reference);

        foreach (var item in mainTabs.Items.OfType<TabItem>().Take(index).ToList())
        {
            if (item != consoleTab)
            {
                CloseTab(item);
            }
        }

        SelectTab(reference);
    }

    /// <summary>Maps a file path to the asset-type icon shown on its tab.</summary>
    internal static string? IconForFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".vmdl_c" => "mdl",
            ".vmesh_c" => "mesh",
            ".vmat_c" => "mat",
            ".vtex_c" => "tex",
            ".vcompmat_c" => "compmat",
            ".vwrld_c" or ".vmap_c" => "map",
            ".vwnod_c" => "wnod",
            ".vpcf_c" => "pcf",
            ".vsnd_c" or ".vsndstck_c" => "snd",
            ".vsvg_c" => "svg",
            ".vphys_c" => "phys",
            ".vnav_c" => "nav",
            ".vanmgrph_c" => "anmgrph",
            ".vpulse_c" => "pulse",
            ".vents_c" => "ents",
            ".vsnap_c" => "snap",
            ".vvis_c" => "vis",
            ".vpost_c" => "post",
            ".vsmart_c" => "vsmart",
            ".vnmclip_c" or ".vnmskel_c" or ".vnmgraph_c" => "anim",
            ".vpk" => "vpk",
            ".kv3" => "kv3",
            ".kv2" => "kv2",
            ".txt" => "txt",
            ".csv" or ".vdata_c" => "vdata",
            ".wav" => "wav",
            ".png" or ".jpg" or ".jpeg" => "png",
            ".json" => "json",
            ".xml" => "xml",
            ".css" => "css",
            ".js" => "js",
            _ => null,
        };
    }

    private void CloseTab(TabItem tab)
    {
        if (tab == consoleTab)
        {
            return;
        }

        var index = mainTabs.Items.IndexOf(tab);

        if (tab.Tag is IViewer viewer)
        {
            viewer.Dispose();
            tab.Tag = null;
        }
        else if (tab.Tag is IDisposable disposable)
        {
            disposable.Dispose();
            tab.Tag = null;
        }

        RemoveTabContent(tab);
        tabPaths.Remove(tab);
        mainTabs.Items.Remove(tab);

        if (mainTabs.Items.Count > 0)
        {
            var next = Math.Clamp(index, 0, mainTabs.Items.Count - 1);
            mainTabs.SelectedItem = mainTabs.Items[next];
        }

        UpdateStatus();
    }

    private void CloseCurrentTab()
    {
        if (mainTabs.SelectedItem is TabItem tab)
        {
            CloseTab(tab);
        }
    }

    private void CloseAllTabs()
    {
        mainTabs.SelectedItem = consoleTab;

        for (var i = mainTabs.Items.Count - 1; i >= 0; i--)
        {
            if (mainTabs.Items[i] is TabItem tab && tab != consoleTab)
            {
                CloseTab(tab);
            }
        }
    }

    /// <summary>Selects the tab hosting the given GL viewport. Used by the self-check.</summary>
    internal bool SelectTabContaining(AvaloniaGlViewport viewport)
    {
        foreach (var (tab, content) in tabContents)
        {
            if (ContentContains(content, viewport))
            {
                SelectTab(tab);
                return true;
            }
        }

        return false;
    }

    private void SelectTab(TabItem tab) => mainTabs.SelectedItem = tab;

    private static bool ContentContains(Control content, AvaloniaGlViewport viewport)
        => content.GetVisualDescendants().OfType<AvaloniaGlViewport>().Contains(viewport);

    private void SetTabContent(TabItem tab, Control content)
    {
        if (tabContents.Remove(tab, out var existing))
        {
            contentHost.Children.Remove(existing);
            (existing as IDisposable)?.Dispose();
        }

        tabContents[tab] = content;
        content.IsVisible = mainTabs.SelectedItem is null || ReferenceEquals(mainTabs.SelectedItem, tab);
        contentHost.Children.Add(content);
    }

    private void RemoveTabContent(TabItem tab)
    {
        if (tabContents.Remove(tab, out var content))
        {
            contentHost.Children.Remove(content);

            // Viewer controls own timers and sessions; dispose them with the tab.
            (content as IDisposable)?.Dispose();
        }
    }

    private void UpdateContentVisibility()
    {
        foreach (var (tab, content) in tabContents)
        {
            content.IsVisible = ReferenceEquals(mainTabs.SelectedItem, tab);
        }
    }

    /// <summary>All live GL viewports across open tabs, in tab order. Used by the self-check.</summary>
    internal IReadOnlyList<AvaloniaGlViewport> GetGlViewports()
    {
        var viewports = new List<AvaloniaGlViewport>();

        foreach (var content in tabContents.Values)
        {
            viewports.AddRange(content.GetVisualDescendants().OfType<AvaloniaGlViewport>());
        }

        return viewports;
    }

    /// <summary>All live audio players across open tabs. Used by the self-check.</summary>
    internal IReadOnlyList<AudioPlayerControl> GetAudioPlayers()
    {
        var players = new List<AudioPlayerControl>();

        foreach (var content in tabContents.Values)
        {
            players.AddRange(content.GetVisualDescendants().OfType<AudioPlayerControl>());
        }

        return players;
    }

    /// <summary>Closes the tab hosting the given GL viewport. Used by the self-check.</summary>
    internal bool CloseTabContaining(AvaloniaGlViewport viewport)
    {
        foreach (var (tab, content) in tabContents)
        {
            if (ContentContains(content, viewport))
            {
                CloseTab(tab);
                return true;
            }
        }

        return false;
    }

    private TabItem? FindTab(string header)
    {
        foreach (var item in mainTabs.Items)
        {
            if (item is TabItem tab
                && tab.Header is StackPanel panel
                && panel.Children.Count > 0
                && panel.Children[0] is TextBlock text
                && text.Text == header)
            {
                return tab;
            }
        }

        return null;
    }

    private void UpdateStatus()
    {
        var title = GetTabTitle(mainTabs.SelectedItem as TabItem);
        statusText.Text = $"{AppInfo.DisplayVersion}    {title}";
        Title = string.IsNullOrEmpty(title) ? "Source 2 Viewer" : $"Source 2 Viewer - {title}";
    }

    private static string GetTabTitle(TabItem? tab)
    {
        if (tab?.Header is StackPanel panel && panel.Children.Count > 0 && panel.Children[0] is TextBlock text)
        {
            return text.Text ?? string.Empty;
        }

        return string.Empty;
    }

    private void RestoreWindowGeometry()
    {
        if (Settings.Config.WindowWidth > 0 && Settings.Config.WindowHeight > 0)
        {
            Width = Settings.Config.WindowWidth;
            Height = Settings.Config.WindowHeight;
        }

        if (Settings.Config.WindowLeft != 0 || Settings.Config.WindowTop != 0)
        {
            Position = new PixelPoint(Settings.Config.WindowLeft, Settings.Config.WindowTop);
        }

        if (Settings.Config.WindowState == (int)WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveWindowGeometry()
    {
        Settings.Config.WindowWidth = (int)Width;
        Settings.Config.WindowHeight = (int)Height;
        Settings.Config.WindowLeft = Position.X;
        Settings.Config.WindowTop = Position.Y;
        Settings.Config.WindowState = WindowState == WindowState.Maximized ? (int)WindowState.Maximized : (int)WindowState.Normal;
    }
}
