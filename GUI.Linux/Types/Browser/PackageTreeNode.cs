using ValvePak;

namespace GUI.Linux.Types.Browser;

/// <summary>
/// A folder in a package's virtual directory tree. Mirrors the Windows browser's virtual node model
/// without its WinForms tree-node realization cache, so both shells can share the navigation data.
/// </summary>
internal sealed class PackageTreeNode(string name, long size, PackageTreeNode? parent)
{
    /// <summary>Folder name (a single path segment).</summary>
    public string Name { get; } = name;

    /// <summary>Summed size of every file in this folder, recursively.</summary>
    public long TotalSize { get; set; } = size;

    /// <summary>The containing folder, or null for the root.</summary>
    public PackageTreeNode? Parent { get; } = parent;

    /// <summary>Child folders keyed by name.</summary>
    public Dictionary<string, PackageTreeNode> Folders { get; } = [];

    /// <summary>Files directly in this folder.</summary>
    public List<PackageEntry> Files { get; } = [];

    /// <summary>Number of files in this folder and all descendants.</summary>
    public int TotalFileCount { get; set; }
}
