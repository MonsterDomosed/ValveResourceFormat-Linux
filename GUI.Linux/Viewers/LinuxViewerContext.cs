using GUI.Types.Viewers;

namespace GUI.Linux.Viewers;

/// <summary>Linux implementation of the portable <see cref="IViewerContext"/>.</summary>
internal sealed class LinuxViewerContext(string fileName) : IViewerContext
{
    public string FileName { get; } = fileName;
}
