using System;
using System.Threading;

namespace GUI.Linux.Types.GLViewers;

/// <summary>What the graph presenter offers, so the controls can populate themselves.</summary>
internal readonly record struct GraphInspectorState(bool Ready, int NodeCount, string[] Subtitles);

/// <summary>
/// Bridge between the graph viewer's search controls and its renderer. The renderer applies the
/// request when <see cref="Version"/> changes instead of re-reading it every frame.
/// </summary>
internal sealed class GraphInspectorSession
{
    private readonly Lock sync = new();

    private int requestVersion;
    private string search = string.Empty;
    private string[] filter = [];

    private bool ready;
    private int nodeCount;
    private string[] subtitles = [];

    /// <summary>Called after a change so an idle viewer redraws.</summary>
    public Action? RequestFrame { get; set; }

    /// <summary>Incremented whenever the search or filter changes.</summary>
    public int Version => Volatile.Read(ref requestVersion);

    /// <summary>The requested search text and subtitle filter, as one consistent pair.</summary>
    public (string Search, string[] Filter) GetRequest()
    {
        lock (sync)
        {
            return (search, filter);
        }
    }

    /// <summary>Publishes the loaded graph's node count and distinct subtitles.</summary>
    public void Publish(int nodeCount, string[] subtitles)
    {
        lock (sync)
        {
            ready = true;
            this.nodeCount = nodeCount;
            this.subtitles = subtitles;
        }
    }

    /// <summary>The last published graph metadata.</summary>
    public GraphInspectorState GetState()
    {
        lock (sync)
        {
            return new GraphInspectorState(ready, nodeCount, subtitles);
        }
    }

    /// <summary>Highlights nodes matching <paramref name="query"/>; empty clears the highlight.</summary>
    public void SetSearch(string query)
        => Update(() => search = query ?? string.Empty);

    /// <summary>Hides nodes whose subtitle is not in <paramref name="visibleSubtitles"/>; empty shows all.</summary>
    public void SetFilter(string[] visibleSubtitles)
        => Update(() => filter = visibleSubtitles);

    private void Update(Action change)
    {
        lock (sync)
        {
            change();
        }

        Interlocked.Increment(ref requestVersion);
        RequestFrame?.Invoke();
    }
}
