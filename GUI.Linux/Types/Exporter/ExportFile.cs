using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using GUI.Linux.Types.Browser;
using GUI.Linux.Utils;
using ValvePak;
using ValveResourceFormat.IO;
using Resource = ValveResourceFormat.Resource;

namespace GUI.Linux.Types.Exporter;

/// <summary>
/// Single file entry points for the export and decompile actions, for both package entries and loose
/// files opened from disk.
/// </summary>
internal static class ExportFile
{
    /// <summary>Exports or decompiles one package entry.</summary>
    public static async Task ExtractFromPackageEntryAsync(Window owner, Package package, PackageEntry file, bool decompile)
    {
        var stream = GameFileLoader.GetPackageEntryStream(package, file);
        await ExtractFromStreamAsync(owner, file.GetFullPath(), stream, decompile).ConfigureAwait(true);
    }

    /// <summary>Exports or decompiles one loose file.</summary>
    public static async Task ExtractFromPathAsync(Window owner, string path, bool decompile)
    {
        var stream = File.OpenRead(path);
        await ExtractFromStreamAsync(owner, Path.GetFileName(path), stream, decompile).ConfigureAwait(true);
    }

    /// <summary>Exports or decompiles a folder and everything below it.</summary>
    public static Task ExtractFromFolderAsync(Window owner, Package package, PackageTreeNode folder, bool decompile)
    {
        var exporter = new PackageExporter(package, null, decompile);
        exporter.QueueFiles(folder);
        return exporter.ExecuteMultipleFileExtractAsync(owner);
    }

    /// <summary>Exports or decompiles every selected package entry.</summary>
    public static Task ExtractFromEntriesAsync(Window owner, Package package, IEnumerable<PackageEntry> entries, bool decompile)
    {
        var exporter = new PackageExporter(package, null, decompile);

        foreach (var entry in entries)
        {
            exporter.QueueFiles(entry);
        }

        return exporter.ExecuteMultipleFileExtractAsync(owner);
    }

    private static async Task ExtractFromStreamAsync(Window owner, string fileName, Stream stream, bool decompile)
    {
        if (decompile && !await PreExportDisclaimerAsync(Path.GetExtension(fileName)).ConfigureAwait(true))
        {
            await stream.DisposeAsync().ConfigureAwait(true);
            return;
        }

        if (decompile && fileName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
        {
            await DecompileAsync(owner, fileName, stream).ConfigureAwait(true);
            return;
        }

        await SaveAsIsAsync(owner, fileName, stream, decompile).ConfigureAwait(true);
    }

    private static async Task DecompileAsync(Window owner, string fileName, Stream stream)
    {
        using var resource = new Resource
        {
            FileName = fileName,
        };

        try
        {
            resource.Read(stream);
        }
        catch (Exception e)
        {
            Log.Error(nameof(ExportFile), $"Failed to read '{fileName}': {e}");
            await stream.DisposeAsync().ConfigureAwait(true);
            return;
        }

        var extension = FileExtract.GetExtension(resource);

        if (string.IsNullOrEmpty(extension))
        {
            Log.Error(nameof(ExportFile), $"Export for \"{fileName}\" has no suitable extension");
            await stream.DisposeAsync().ConfigureAwait(true);
            return;
        }

        var filter = $"{extension} file|*.{extension}";

        if (GltfModelExporter.CanExport(resource))
        {
            const string gltfFilter = "glTF|*.gltf";
            const string glbFilter = "glTF Binary|*.glb";

            filter = $"{filter}|{gltfFilter}|{glbFilter}";
        }

        var fileNameForSave = Path.GetFileNameWithoutExtension(fileName);

        if (Path.GetExtension(fileName) == ".vmap_c")
        {
            // Suggest a suffix like de_dust2_d so recompiled maps do not collide with the originals
            fileNameForSave += "_d";
        }

        var pickedFileName = AppFileDialogs.SaveFile("Choose where to save the file", fileNameForSave, extension, filter);

        if (pickedFileName is null)
        {
            await stream.DisposeAsync().ConfigureAwait(true);
            return;
        }

        var exporter = new PackageExporter(null, Path.GetDirectoryName(pickedFileName) ?? string.Empty, true);

        // Cancelling closes the window while the extract is still winding down, so wait for it to stop
        // before the finally pulls the stream out from under it
        await exporter.ExecuteSingleFileExtractAsync(
            owner,
            resource,
            fileName,
            pickedFileName,
            $"Extracting {fileName} to \"{Path.GetFileName(pickedFileName)}\"").ConfigureAwait(true);

        await stream.DisposeAsync().ConfigureAwait(true);
    }

    private static async Task SaveAsIsAsync(Window owner, string fileName, Stream stream, bool decompile)
    {
        var outputName = fileName;

        if (decompile && FileExtract.TryExtractNonResource(stream, fileName, out var content))
        {
            if (content.Data is null)
            {
                // Content has no data to extract, only potentially subfiles
                content.Dispose();
                await stream.DisposeAsync().ConfigureAwait(true);
                Log.Info(nameof(ExportFile), $"File \"{fileName}\" has no extractable data");
                return;
            }

            outputName = Path.ChangeExtension(fileName, Path.GetExtension(content.FileName));
            await stream.DisposeAsync().ConfigureAwait(true);

            stream = new MemoryStream(content.Data);
            content.Dispose();
        }

        var saveFileName = AppFileDialogs.SaveFile("Choose where to save the file", Path.GetFileName(outputName), null, "All files (*.*)|*.*");

        if (saveFileName is not null)
        {
            Log.Info(nameof(ExportFile), $"Saved \"{Path.GetFileName(saveFileName)}\"");

            using var streamOutput = File.Create(saveFileName);
            await stream.CopyToAsync(streamOutput).ConfigureAwait(true);
        }

        await stream.DisposeAsync().ConfigureAwait(true);
    }

    private static async Task<bool> PreExportDisclaimerAsync(string fileExtension)
    {
        var messageString = "";

        switch (fileExtension)
        {
            case ".vmap_c":
                messageString =
                    """
                    Decompiling Source2 maps is a difficult process, as such the output will be messy and imperfect, and will not resemble how
                    real .vmap files are made!

                    - Models will be merged by material across the map.
                    - Parts of the skybox mesh might be missing.
                    - The collision of the map will be merged into one mesh using special materials.
                    - The map will lack lightmap resolution volumes.
                    - Hammer meshes will be triangulated.

                    It is NOT ADVISED to work on decompiled maps as your first map if you are new to mapping!
                    """;
                break;

            default:
                break;
        }

        if (!string.IsNullOrEmpty(messageString))
        {
            return await AppMessageDialogs.ConfirmAsync(messageString, "Decompile warning", MessageIcon.Warning).ConfigureAwait(true);
        }

        return true;
    }
}
