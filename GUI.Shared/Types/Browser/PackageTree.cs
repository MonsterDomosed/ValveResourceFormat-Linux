using System.Text.RegularExpressions;
using ValvePak;

namespace GUI.Types.Browser;

/// <summary>
/// How a package search query is matched against entries, mirroring the Windows browser's search
/// modes for the parts that do not need to scan archive bytes.
/// </summary>
internal enum PackageSearchMode
{
    FileNameExactMatch,
    FileNamePartialMatch,
    FullPath,
    Regex,
}

/// <summary>
/// Builds and searches the virtual directory tree of a <see cref="Package"/>. Portable counterpart of
/// the Windows browser's tree building and search, so both shells navigate packages the same way.
/// </summary>
internal static class PackageTree
{
    /// <summary>Builds the folder/file tree for every entry in the package.</summary>
    public static PackageTreeNode Build(Package package)
    {
        var root = new PackageTreeNode("root", 0, null);

        if (package.Entries != null)
        {
            foreach (var fileType in package.Entries)
            {
                foreach (var file in fileType.Value)
                {
                    AddFileNode(root, file);
                }
            }
        }

        ComputeTotals(root);
        return root;
    }

    /// <summary>Finds or creates the folder chain for <paramref name="directory"/> under <paramref name="current"/>.</summary>
    public static PackageTreeNode AddFolderNode(PackageTreeNode current, string directory, long size)
    {
        foreach (var subPath in directory.Split(Package.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!current.Folders.TryGetValue(subPath, out var node))
            {
                node = new PackageTreeNode(subPath, size, current);
                current.Folders.Add(subPath, node);
            }
            else
            {
                node.TotalSize += size;
            }

            current = node;
        }

        return current;
    }

    /// <summary>Adds <paramref name="file"/> to the folder implied by its directory name.</summary>
    public static PackageTreeNode AddFileNode(PackageTreeNode current, PackageEntry file)
    {
        if (!string.IsNullOrWhiteSpace(file.DirectoryName))
        {
            current = AddFolderNode(current, file.DirectoryName, file.TotalLength);
        }

        current.Files.Add(file);
        return current;
    }

    private static long ComputeTotals(PackageTreeNode node)
    {
        var total = 0L;
        var files = node.Files.Count;

        foreach (var file in node.Files)
        {
            total += file.TotalLength;
        }

        foreach (var folder in node.Folders.Values)
        {
            total += ComputeTotals(folder);
            files += folder.TotalFileCount;
        }

        node.TotalSize = total;
        node.TotalFileCount = files;
        return total;
    }

    /// <summary>
    /// Searches a subtree the way the Windows browser does: partial file-name matching, full path
    /// matching (also selected automatically when the query contains a directory separator), exact
    /// file names, or a regular expression against file names.
    /// </summary>
    public static List<PackageEntry> Search(PackageTreeNode root, string query, PackageSearchMode mode)
    {
        var results = new List<PackageEntry>();

        if (mode is PackageSearchMode.FileNamePartialMatch or PackageSearchMode.FullPath)
        {
            query = query.Replace('\\', Package.DirectorySeparatorChar);
        }

        if (mode == PackageSearchMode.FileNamePartialMatch && query.Contains(Package.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            mode = PackageSearchMode.FullPath;
        }

        Func<PackageEntry, bool> match = mode switch
        {
            PackageSearchMode.FileNameExactMatch => entry => entry.GetFileName().Equals(query, StringComparison.OrdinalIgnoreCase),
            PackageSearchMode.FileNamePartialMatch => entry => entry.GetFileName().Contains(query, StringComparison.OrdinalIgnoreCase),
            PackageSearchMode.FullPath => entry => entry.GetFullPath().Contains(query, StringComparison.OrdinalIgnoreCase),
            PackageSearchMode.Regex => CreateRegexMatcher(query),
            _ => static _ => false,
        };

        Collect(root, results, match);
        return results;
    }

    private static Func<PackageEntry, bool> CreateRegexMatcher(string query)
    {
        var regex = new Regex(query, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return entry => regex.IsMatch(entry.GetFileName());
    }

    private static void Collect(PackageTreeNode node, List<PackageEntry> results, Func<PackageEntry, bool> match)
    {
        foreach (var file in node.Files)
        {
            if (match(file))
            {
                results.Add(file);
            }
        }

        foreach (var folder in node.Folders.Values)
        {
            Collect(folder, results, match);
        }
    }
}
