using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using GUI.Linux.GL;
using GUI.Linux.Types.GLViewers;
using GUI.Linux.Types.Graphs.Core;

namespace GUI.Linux.Shell;

/// <summary>
/// Graph inspection view: a GL viewport with a toolbar that highlights matching nodes and hides
/// nodes by subtitle. Everything goes through <see cref="GraphInspectorSession"/>.
/// </summary>
internal sealed class GraphViewerControl : UserControl, IDisposable
{
    private readonly GraphInspectorSession session;
    private readonly AvaloniaGlViewport viewport;
    private readonly TextBox searchBox = new() { PlaceholderText = "Find nodes..." };
    private readonly ComboBox filterSelector = new() { Width = 200 };
    private readonly TextBlock summary = new() { Classes = { "hint" } };
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    private string[] populatedSubtitles = [];
    private bool updatingFromState;
    private bool disposed;

    public GraphViewerControl(Func<GraphView, IDisposable?> build, GraphInspectorSession session)
    {
        this.session = session;

        viewport = new AvaloniaGlViewport
        {
            RendererFactory = () => new GraphGlRenderer(build, session),
        };

        session.RequestFrame = viewport.RequestFrame;

        searchBox.TextChanged += (_, _) =>
        {
            if (!updatingFromState)
            {
                // Each search re-rasterizes the graph, so wait for the typing to settle
                searchTimer.Stop();
                searchTimer.Start();
            }
        };

        searchTimer.Tick += (_, _) =>
        {
            searchTimer.Stop();
            session.SetSearch(searchBox.Text ?? string.Empty);
        };

        filterSelector.SelectionChanged += (_, _) =>
        {
            if (updatingFromState || filterSelector.SelectedIndex < 0)
            {
                return;
            }

            // Index 0 shows everything, the rest filter to one subtitle
            var index = filterSelector.SelectedIndex - 1;
            session.SetFilter(index < 0 || index >= populatedSubtitles.Length ? [] : [populatedSubtitles[index]]);
        };

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(8, 6),
            Children =
            {
                searchBox,
                new TextBlock { Text = "Show", VerticalAlignment = VerticalAlignment.Center, Classes = { "hint" } },
                filterSelector,
                summary,
            },
        };

        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        layout.Children.Add(viewport);
        Content = layout;

        refreshTimer.Tick += (_, _) => UpdateFromState();
        refreshTimer.Start();
        UpdateFromState();
    }

    /// <summary>The controls' session, for the self-check.</summary>
    internal GraphInspectorSession Session => session;

    private void UpdateFromState()
    {
        var state = session.GetState();

        updatingFromState = true;

        try
        {
            if (!NamesEqual(populatedSubtitles, state.Subtitles))
            {
                populatedSubtitles = state.Subtitles;

                var items = new string[state.Subtitles.Length + 1];
                items[0] = "Everything";
                Array.Copy(state.Subtitles, 0, items, 1, state.Subtitles.Length);

                filterSelector.ItemsSource = items;
                filterSelector.SelectedIndex = 0;
            }

            summary.Text = state.Ready ? $"{state.NodeCount} nodes" : "Loading...";
        }
        finally
        {
            updatingFromState = false;
        }
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

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        searchTimer.Stop();
        refreshTimer.Stop();
        session.RequestFrame = null;
        GC.SuppressFinalize(this);
    }
}
