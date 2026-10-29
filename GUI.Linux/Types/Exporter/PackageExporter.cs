using System.Collections.Frozen;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GUI.Linux.Shell;
using GUI.Linux.Types.Browser;
using GUI.Linux.Utils;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Utils;
using Resource = ValveResourceFormat.Resource;

namespace GUI.Linux.Types.Exporter;

/// <summary>
/// Decompiles or exports package entries and loose files to disk, reporting progress through an
/// <see cref="ExportProgressDialog"/>.
/// </summary>
internal sealed class PackageExporter
{
    private sealed class FileTypeToExtract
    {
        public string? OutputFormat;
        public int Count = 1;
    }

    // Small files are written in a couple of chunks anyway, so reserving space up front only helps for large ones
    private const int PreallocationThreshold = 1024 * 1024;

    // When decompiling, parents go first so that anything they pull in (and export themselves) is skipped later
    private static readonly FrozenDictionary<string, int> ExtractOrder = new List<ResourceType>
    {
        ResourceType.Map,
        ResourceType.World,
        ResourceType.WorldNode,
        ResourceType.Model,
        ResourceType.Mesh,
        ResourceType.AnimationGroup,
        ResourceType.Animation,
        ResourceType.Sequence,
        ResourceType.Morph,

        ResourceType.Material,
        ResourceType.Texture,
    }.Select(static (type, index) => (Key: type.GetExtension() + GameFileLoader.CompiledFileSuffix, Index: index))
        .ToFrozenDictionary(static x => x.Key, static x => x.Index);

    private readonly bool decompile;
    private readonly Package? package;
    private readonly Dictionary<string, FileTypeToExtract> fileTypesToExtract = [];
    private readonly List<PackageEntry> filesToExtract = [];
    private readonly HashSet<string> extractedFiles = [];
    private string? path;
    private string? lastCreatedDirectory;
    private int processed;
    private int filesFailedToExport;
    private ExportProgressDialog? progress;
    private GltfModelExporter? gltfExporter;

    private string OutputPath => path ?? throw new InvalidOperationException("Path must be set before extracting files");

    /// <summary>Creates an exporter, optionally for package entries and with a fixed output directory.</summary>
    public PackageExporter(Package? package, string? path, bool decompile)
    {
        this.package = package;
        this.path = path;
        this.decompile = decompile;
    }

    /// <summary>Queues a single package entry.</summary>
    public void QueueFiles(PackageEntry file)
    {
        if (fileTypesToExtract.TryGetValue(file.TypeName, out var fileType))
        {
            fileType.Count++;
        }
        else
        {
            fileTypesToExtract[file.TypeName] = new FileTypeToExtract(); // Type to be filled in later
        }

        filesToExtract.Add(file);
    }

    /// <summary>Queues every entry in a folder and all of its subfolders.</summary>
    public void QueueFiles(PackageTreeNode folder)
    {
        foreach (var node in folder.Folders.Values)
        {
            QueueFiles(node);
        }

        foreach (var file in folder.Files)
        {
            QueueFiles(file);
        }
    }

    /// <summary>Sets the output format for a queued type, as the output types dialog would.</summary>
    internal void SetOutputFormat(string typeName, string? format) => fileTypesToExtract[typeName].OutputFormat = format;

    /// <summary>
    /// Runs the queued files into <paramref name="folder"/>. A null <paramref name="owner"/> runs the
    /// extraction without the progress window, which is what the self-check drives.
    /// </summary>
    internal async Task ExecuteToFolderAsync(Window? owner, string folder)
    {
        path = folder;

        if (owner is null)
        {
            await ExtractQueuedFilesAsync(CancellationToken.None).ConfigureAwait(false);
            return;
        }

        await RunInDialogAsync(owner, ExtractQueuedFilesAsync, null).ConfigureAwait(true);
    }

    /// <summary>Asks for output types and a destination folder, then extracts all queued files.</summary>
    public async Task ExecuteMultipleFileExtractAsync(Window owner)
    {
        if (filesToExtract.Count == 0)
        {
            await AppMessageDialogs.ShowMessageAsync("There are no files to extract", "Failed to extract", MessageIcon.Warning).ConfigureAwait(true);
            return;
        }

        if (decompile && !await ShowTypesDialogAsync(owner).ConfigureAwait(true))
        {
            return;
        }

        var selectedPath = AppFileDialogs.PickFolder("Choose which folder to extract files to", AppFileDialogs.RememberIn.SaveDirectory);

        if (selectedPath is null)
        {
            return;
        }

        path = selectedPath;

        await RunInDialogAsync(owner, ExtractQueuedFilesAsync, null).ConfigureAwait(true);
    }

    /// <summary>
    /// Extracts a single already-read resource to <paramref name="outFilePath"/>, including any additional and
    /// sub files. Completes once the work really stopped, which cancelling makes later than the window closing.
    /// </summary>
    public async Task ExecuteSingleFileExtractAsync(Window owner, Resource resource, string inFilePath, string outFilePath, string initialText)
    {
        await RunInDialogAsync(owner, cancellationToken => ExtractFileAsync(resource, inFilePath, outFilePath, true, cancellationToken), initialText).ConfigureAwait(true);
    }

    private async Task RunInDialogAsync(Window owner, Func<CancellationToken, Task> work, string? initialText)
    {
        var dialog = new ExportProgressDialog { StayOpenOnCompletion = true };
        progress = dialog;

        if (initialText is not null)
        {
            dialog.SetProgress(initialText);
        }

        if (decompile)
        {
            // We need to know what files were handled by the glTF exporter
            gltfExporter = new GltfModelExporter(new TrackingFileLoader(LinuxGameContent.FileLoader))
            {
                ProgressReporter = dialog,
            };
        }

        dialog.OnProcess = async cancellationToken =>
        {
            await work(cancellationToken).ConfigureAwait(false);

            var completedText = $"Export completed in {ExportProgressDialog.FormatTime(dialog.Elapsed)}";

            if (filesFailedToExport > 0)
            {
                completedText += $", {filesFailedToExport} file{(filesFailedToExport == 1 ? "" : "s")} failed";
            }

            Log.Info(nameof(PackageExporter), completedText);
            dialog.SetProgress(completedText);
        };

        // RunAsync already waits for the work to really stop, so nothing it reads is disposed early
        await dialog.RunAsync(owner).ConfigureAwait(true);

        if (filesFailedToExport > 0)
        {
            await AppMessageDialogs.ShowMessageAsync(
                $"{filesFailedToExport} file{(filesFailedToExport == 1 ? "" : "s")} failed to extract, see the console for details.",
                "Export finished with errors",
                MessageIcon.Warning).ConfigureAwait(true);
        }
    }

    private async Task<bool> ShowTypesDialogAsync(Window owner)
    {
        var dialog = new ExtractOutputTypesDialog();
        dialog.SelectionChanged += OnTypesDialogSelectedValueChanged;

        var hasVmap = fileTypesToExtract.ContainsKey("vmap_c");

        foreach (var type in fileTypesToExtract.OrderByDescending(static x => x.Value.Count))
        {
            var resourceType = ResourceTypeExtensions.DetermineByFileExtension("." + type.Key);
            var firstType = resourceType != ResourceType.Unknown
                ? FileExtract.GetExtension(resourceType)
                : type.Key.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase) ? type.Key[..^2] : type.Key;

            var outputTypes = new List<string>
            {
                "* Do not export *",
                firstType,
            };

            if (firstType is "vmdl" or "vmesh" or "vmap" or "vwrld" or "vwnod" or "vnmclip" or "vnmskel")
            {
                outputTypes.Add("gltf");
                outputTypes.Add("glb");
            }
            else if (firstType == "vtex")
            {
                outputTypes.Insert(1, "image");
            }
            else if (firstType == "vsnd")
            {
                outputTypes.Insert(1, "sound");
            }

            // Select the first suggested type, the 0th item is always "do not export".
            // For folders containing a map, default to "do not export", except for the map itself.
            var selectedIndex = hasVmap
                ? firstType is "vmap" ? 1 : 0
                : 1;

            type.Value.OutputFormat = selectedIndex == 0 ? null : outputTypes[selectedIndex];
            dialog.AddTypeToTable(type.Key, type.Value.Count, outputTypes, selectedIndex);
        }

        await dialog.ShowDialog(owner).ConfigureAwait(true);
        dialog.SelectionChanged -= OnTypesDialogSelectedValueChanged;

        return dialog.ContinuePressed;
    }

    private void OnTypesDialogSelectedValueChanged(ComboBox control, string type)
    {
        fileTypesToExtract[type].OutputFormat = control.SelectedIndex == 0 ? null : control.SelectedItem as string;
    }

    private async Task ExtractQueuedFilesAsync(CancellationToken cancellationToken)
    {
        Log.Info(nameof(PackageExporter), $"Folder export started to \"{OutputPath}\"");

        processed = 0;
        progress?.SetBarMax(filesToExtract.Count);

        var outputPath = OutputPath;

        if (package is null)
        {
            Log.Error(nameof(PackageExporter), "CurrentPackage is null, cannot extract files");
            return;
        }

        // Within each type, read the package in storage order to avoid seeking back and forth between entries
        var files = filesToExtract
            .OrderBy(file => decompile ? ExtractOrder.GetValueOrDefault(file.TypeName, ExtractOrder.Count) : 0)
            .ThenBy(static file => file.ArchiveIndex)
            .ThenBy(static file => file.Offset);

        foreach (var packageFile in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.SetBarValue(++processed);

            string? outputFormat = null;

            if (decompile && fileTypesToExtract.TryGetValue(packageFile.TypeName, out var outputType))
            {
                if (outputType.OutputFormat is null)
                {
                    // Skip this file type
                    continue;
                }

                outputFormat = outputType.OutputFormat;
            }

            var fileFullName = packageFile.GetFullPath();

            if (extractedFiles.Contains(fileFullName))
            {
                continue;
            }

            progress?.SetProgress(fileFullName);

            var outFilePath = ResolveOutputPath(outputPath, fileFullName);

            if (outputFormat is not null)
            {
                outFilePath = Path.ChangeExtension(outFilePath, outputFormat);
            }

            EnsureDirectoryExists(outFilePath);

            var stream = GameFileLoader.GetPackageEntryStream(package, packageFile);

            if (!decompile || !packageFile.TypeName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
            {
                // Export as is
                using var rawStream = stream;

                try
                {
                    await WriteStreamAsync(outFilePath, rawStream, packageFile.TotalLength, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    // One unreadable or unwritable entry must not abandon the rest of the export
                    filesFailedToExport++;
                    Log.Error(nameof(PackageExporter), $"Failed to extract '{fileFullName}': {e}");
                }

                continue;
            }

            try
            {
                await ExtractCompiledFileAsync(stream, fileFullName, outFilePath, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                filesFailedToExport++;
                Log.Error(nameof(PackageExporter), $"Failed to extract '{fileFullName}': {e}");
            }
        }
    }

    /// <summary>Parses one compiled file and extracts it, owning the resource for that lifetime.</summary>
    private async Task ExtractCompiledFileAsync(Stream stream, string fileFullName, string outFilePath, CancellationToken cancellationToken)
    {
        using var resource = new Resource
        {
            FileName = fileFullName,
        };

        resource.Read(stream);
        await ExtractFileAsync(resource, fileFullName, outFilePath, false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a package-relative name inside the export folder and refuses anything that would land
    /// outside it, which a corrupt or hostile archive can otherwise do with ".." segments.
    /// </summary>
    private static string ResolveOutputPath(string outputPath, string relativePath)
    {
        var root = Path.GetFullPath(outputPath);
        var combined = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing to write '{relativePath}' outside the export folder.");
        }

        return combined;
    }

    /// <summary>Writes a file through a temporary name, so a cancel cannot leave a half-written result.</summary>
    private static async Task WriteStreamAsync(string outFilePath, Stream source, long expectedLength, CancellationToken cancellationToken)
    {
        var temporary = outFilePath + ".tmp";

        try
        {
            using (var outStream = new FileStream(temporary, new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Options = FileOptions.Asynchronous,
                PreallocationSize = expectedLength >= PreallocationThreshold ? expectedLength : 0,
            }))
            {
                await source.CopyToAsync(outStream, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, outFilePath, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: the temp file is only an artifact of a failed write
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task ExtractFileAsync(Resource resource, string inFilePath, string outFilePath, bool flatSubfiles, CancellationToken cancellationToken)
    {
        var outExtension = Path.GetExtension(outFilePath);

        if (GltfModelExporter.CanExport(resource) && outExtension is ".glb" or ".gltf")
        {
            if (gltfExporter is null)
            {
                Log.Error(nameof(PackageExporter), "gltfExporter is null, cannot export to glTF format");
                return;
            }

            try
            {
                gltfExporter.Export(resource, outFilePath, cancellationToken);

                if (gltfExporter.FileLoader is TrackingFileLoader trackingFileLoader)
                {
                    lock (trackingFileLoader.LoadedFilePaths)
                    {
                        extractedFiles.UnionWith(trackingFileLoader.LoadedFilePaths);
                        trackingFileLoader.LoadedFilePaths.Clear();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                filesFailedToExport++;
                Log.Error(nameof(PackageExporter), $"Failed to extract '{resource.FileName}': {e}");
            }

            return;
        }

        if (outExtension is ".sound" or ".image" || outExtension.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
        {
            outFilePath = Path.ChangeExtension(outFilePath, FileExtract.GetExtension(resource));
        }
        else if (outExtension == ".vmap")
        {
            flatSubfiles = false;
        }

        ContentFile? contentFile = null;

        try
        {
            contentFile = FileExtract.Extract(resource, LinuxGameContent.FileLoader, progress);

            if (contentFile.Data is not null)
            {
                await WriteFileAsync(outFilePath, contentFile.Data, cancellationToken).ConfigureAwait(false);
            }

            foreach (var additionalFile in contentFile.AdditionalFiles)
            {
                extractedFiles.Add(additionalFile.FileName + GameFileLoader.CompiledFileSuffix);
                var fileNameOut = additionalFile.FileName;
                var flattenThis = flatSubfiles && !additionalFile.KeepFullPath;

                if (additionalFile.Data is not null)
                {
                    if (flattenThis)
                    {
                        fileNameOut = Path.GetFileName(fileNameOut);
                    }

                    await WriteAssetFileAsync(fileNameOut, additionalFile.Data, cancellationToken).ConfigureAwait(false);
                }

                var contentRelativeFolder = flattenThis ? string.Empty : Path.GetDirectoryName(fileNameOut) ?? string.Empty;

                await ExtractSubfilesAsync(contentRelativeFolder, additionalFile, cancellationToken).ConfigureAwait(false);
            }

            extractedFiles.Add(inFilePath);

            var inFileContentRelativeFolder = flatSubfiles ? string.Empty : Path.GetDirectoryName(inFilePath) ?? string.Empty;

            await ExtractSubfilesAsync(inFileContentRelativeFolder, contentFile, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            filesFailedToExport++;
            Log.Error(nameof(PackageExporter), $"Failed to extract '{inFilePath}': {e}");
        }
        finally
        {
            contentFile?.Dispose();
        }
    }

    private async Task ExtractSubfilesAsync(string contentRelativeFolder, ContentFile contentFile, CancellationToken cancellationToken)
    {
        foreach (var contentSubFile in contentFile.SubFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            contentSubFile.FileName = Path.Combine(contentRelativeFolder, contentSubFile.FileName).Replace(Path.DirectorySeparatorChar, '/');

            if (extractedFiles.Contains(contentSubFile.FileName))
            {
                continue;
            }

            if (contentSubFile.Extract is null)
            {
                Log.Error(nameof(PackageExporter), $"Extract function is null for subfile '{contentSubFile.FileName}'");
                continue;
            }

            byte[] subFileData;

            try
            {
                subFileData = contentSubFile.Extract();
            }
            catch (Exception e)
            {
                filesFailedToExport++;
                Log.Error(nameof(PackageExporter), $"Failed to extract subfile '{contentSubFile.FileName}': {e}");
                continue;
            }

            // A zero length file is a placeholder the extractor could not produce, not something to write
            if (subFileData.Length > 0)
            {
                extractedFiles.Add(contentSubFile.FileName);
                await WriteAssetFileAsync(contentSubFile.FileName, subFileData, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private Task WriteAssetFileAsync(string assetName, byte[] data, CancellationToken cancellationToken)
    {
        progress?.SetProgress($"+ {assetName}");
        return WriteFileAsync(CombineAssetFolder(OutputPath, assetName), data, cancellationToken);
    }

    private async Task WriteFileAsync(string outFilePath, byte[] data, CancellationToken cancellationToken)
    {
        var resolved = ResolveOutputPath(OutputPath, Path.GetRelativePath(OutputPath, outFilePath));
        EnsureDirectoryExists(resolved);

        await WriteStreamAsync(resolved, new MemoryStream(data, writable: false), data.Length, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureDirectoryExists(string outFilePath)
    {
        var directory = Path.GetDirectoryName(outFilePath);

        // Consecutive files usually share a folder, so avoid hitting the filesystem for each one
        if (directory is null || directory == lastCreatedDirectory)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        lastCreatedDirectory = directory;
    }

    /// <summary>
    /// Joins an asset relative name to the chosen folder, chopping the longest run of leading asset
    /// folders the destination already ends with, so extracting "maps" to a folder named "maps" does
    /// not nest a second "maps" directory.
    /// </summary>
    internal static string CombineAssetFolder(string userFolder, string assetName)
    {
        var normalized = userFolder.Replace(Path.DirectorySeparatorChar, '/');

        for (var slash = assetName.LastIndexOf('/'); slash > 0; slash = assetName.LastIndexOf('/', slash - 1))
        {
            if (normalized.Length >= slash
                && normalized.AsSpan(normalized.Length - slash).SequenceEqual(assetName.AsSpan(0, slash))
                && (normalized.Length == slash || normalized[normalized.Length - slash - 1] == '/'))
            {
                return Path.Combine(userFolder, assetName[(slash + 1)..]);
            }
        }

        return Path.Combine(userFolder, assetName);
    }
}
