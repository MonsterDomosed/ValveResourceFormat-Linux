using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using GUI.Platform;

namespace GUI.Linux.Platform;

/// <summary>
/// Linux file dialogs. Backed by Avalonia's <see cref="IStorageProvider"/>, which on Linux uses the
/// XDG Desktop Portal (org.freedesktop.portal.FileChooser) when available and falls back to a GTK or
/// managed dialog otherwise.
/// </summary>
internal sealed class LinuxFileDialogs : IFileDialogService
{
    // Remembered directories are part of the settings port and are not persisted yet.
    public string? PickFolder(string? title, FileDialogRemember remember = FileDialogRemember.None, bool updateRemembered = true)
    {
        return AvaloniaSync.Run(async () =>
        {
            var storage = RequireStorageProvider();

            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
            }).ConfigureAwait(true);

            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        });
    }

    public string? OpenFile(string? title, string? filter, FileDialogRemember remember = FileDialogRemember.OpenDirectory, bool updateRemembered = true)
    {
        var files = OpenFilesCore(title, filter, multiselect: false);
        return files is { Length: > 0 } ? files[0] : null;
    }

    public string[]? OpenFiles(string? title, string? filter, FileDialogRemember remember = FileDialogRemember.OpenDirectory, bool updateRemembered = true)
        => OpenFilesCore(title, filter, multiselect: true);

    private static string[]? OpenFilesCore(string? title, string? filter, bool multiselect)
    {
        return AvaloniaSync.Run(async () =>
        {
            var storage = RequireStorageProvider();

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = multiselect,
                FileTypeFilter = ParseFilters(filter),
            }).ConfigureAwait(true);

            var paths = files
                .Select(file => file.TryGetLocalPath())
                .OfType<string>()
                .ToArray();

            return paths.Length > 0 ? paths : null;
        });
    }

    public string? SaveFile(string title, string? defaultFileName, string? defaultExtension, string filter, FileDialogRemember remember = FileDialogRemember.SaveDirectory)
        => SaveFile(title, defaultFileName, defaultExtension, filter, out _, remember);

    public string? SaveFile(string title, string? defaultFileName, string? defaultExtension, string filter, out int selectedFilterIndex, FileDialogRemember remember = FileDialogRemember.SaveDirectory)
    {
        var filters = ParseFilters(filter);

        var path = AvaloniaSync.Run(async () =>
        {
            var storage = RequireStorageProvider();

            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = defaultFileName,
                DefaultExtension = NormalizeExtension(defaultExtension),
                FileTypeChoices = filters,
            }).ConfigureAwait(true);

            return file?.TryGetLocalPath();
        });

        selectedFilterIndex = path is not null ? MatchFilterIndex(filters, path) : 0;
        return path;
    }

    private static IStorageProvider RequireStorageProvider()
        => LinuxPlatform.MainWindow?.StorageProvider
            ?? throw new InvalidOperationException("File dialogs require the Avalonia shell to be running.");

    private static List<FilePickerFileType> ParseFilters(string? filter)
    {
        var result = new List<FilePickerFileType>();

        if (!string.IsNullOrWhiteSpace(filter))
        {
            // WinForms format: "Description|pattern;pattern|Description|pattern".
            var parts = filter.Split('|');

            for (var i = 0; i + 1 < parts.Length; i += 2)
            {
                var name = parts[i].Trim();

                if (name.Length == 0)
                {
                    continue;
                }

                var patterns = parts[i + 1]
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(pattern => pattern == "*.*" ? "*" : pattern)
                    .ToArray();

                if (patterns.Length > 0)
                {
                    result.Add(new FilePickerFileType(name) { Patterns = patterns });
                }
            }
        }

        return result.Count > 0 ? result : [FilePickerFileTypes.All];
    }

    private static string? NormalizeExtension(string? extension)
        => string.IsNullOrEmpty(extension) ? null : extension.TrimStart('.');

    private static int MatchFilterIndex(List<FilePickerFileType> filters, string path)
    {
        var extension = Path.GetExtension(path);

        for (var i = 0; i < filters.Count; i++)
        {
            foreach (var pattern in filters[i].Patterns ?? [])
            {
                if (pattern == "*"
                    || pattern.Equals("*" + extension, StringComparison.OrdinalIgnoreCase)
                    || pattern.Equals(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return i + 1;
                }
            }
        }

        return 1;
    }
}
