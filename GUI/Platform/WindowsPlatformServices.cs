using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Utils;
using SkiaSharp;

#pragma warning disable RS0030 // Banned API: this is the Windows implementation of the platform service abstractions

namespace GUI.Platform;

/// <summary>
/// WinForms implementation of <see cref="IPlatformServices"/>. This is the only place the Windows
/// shell is allowed to touch MessageBox/Clipboard/file dialogs directly.
/// </summary>
internal sealed class WindowsPlatformServices : IPlatformServices
{
    [ModuleInitializer]
    internal static void EnsureRegistered() => PlatformServices.Register(new WindowsPlatformServices());

    public IFileDialogService FileDialogs { get; } = new WindowsFileDialogs();
    public IClipboardService Clipboard { get; } = new WindowsClipboard();
    public IMessageDialogService MessageDialogs { get; } = new WindowsMessageDialogs();
    public IShellService Shell { get; } = new WindowsShell();

    public string SettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Source2Viewer");
}

internal sealed class WindowsFileDialogs : IFileDialogService
{
    public string? PickFolder(string? title, FileDialogRemember remember = FileDialogRemember.None, bool updateRemembered = true)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = title ?? string.Empty,
            UseDescriptionForTitle = title != null,
            SelectedPath = GetRememberedDirectory(remember),
            AddToRecent = true,
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return null;
        }

        if (updateRemembered)
        {
            SetRememberedDirectory(remember, dialog.SelectedPath);
        }

        return dialog.SelectedPath;
    }

    public string? OpenFile(string? title, string? filter, FileDialogRemember remember = FileDialogRemember.OpenDirectory, bool updateRemembered = true)
    {
        var files = OpenFilesCore(title, filter, multiselect: false, remember, updateRemembered);
        return files is { Length: > 0 } ? files[0] : null;
    }

    public string[]? OpenFiles(string? title, string? filter, FileDialogRemember remember = FileDialogRemember.OpenDirectory, bool updateRemembered = true)
    {
        return OpenFilesCore(title, filter, multiselect: true, remember, updateRemembered);
    }

    private static string[]? OpenFilesCore(string? title, string? filter, bool multiselect, FileDialogRemember remember, bool updateRemembered)
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter ?? string.Empty,
            InitialDirectory = GetRememberedDirectory(remember),
            Multiselect = multiselect,
            AddToRecent = true,
        };

        if (dialog.ShowDialog() != DialogResult.OK || dialog.FileNames.Length < 1)
        {
            return null;
        }

        if (updateRemembered && Path.GetDirectoryName(dialog.FileNames[0]) is { Length: > 0 } directory)
        {
            SetRememberedDirectory(remember, directory);
        }

        return dialog.FileNames;
    }

    public string? SaveFile(string title, string? defaultFileName, string? defaultExtension, string filter, FileDialogRemember remember = FileDialogRemember.SaveDirectory)
    {
        return SaveFile(title, defaultFileName, defaultExtension, filter, out _, remember);
    }

    public string? SaveFile(string title, string? defaultFileName, string? defaultExtension, string filter, out int selectedFilterIndex, FileDialogRemember remember = FileDialogRemember.SaveDirectory)
    {
        using var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            DefaultExt = defaultExtension,
            Filter = filter,
            InitialDirectory = GetRememberedDirectory(remember),
            AddToRecent = true,
        };

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            selectedFilterIndex = 0;
            return null;
        }

        selectedFilterIndex = dialog.FilterIndex;

        if (Path.GetDirectoryName(dialog.FileName) is { Length: > 0 } directory)
        {
            SetRememberedDirectory(remember, directory);
        }

        return dialog.FileName;
    }

    private static string GetRememberedDirectory(FileDialogRemember remember) => remember switch
    {
        FileDialogRemember.OpenDirectory => Settings.Config.OpenDirectory,
        FileDialogRemember.SaveDirectory => Settings.Config.SaveDirectory,
        _ => string.Empty,
    };

    private static void SetRememberedDirectory(FileDialogRemember remember, string path)
    {
        switch (remember)
        {
            case FileDialogRemember.OpenDirectory:
                Settings.Config.OpenDirectory = path;
                break;
            case FileDialogRemember.SaveDirectory:
                Settings.Config.SaveDirectory = path;
                break;
            case FileDialogRemember.None:
                break;
        }
    }
}

internal sealed class WindowsClipboard : IClipboardService
{
    public void SetText(string text) => Clipboard.SetText(text);

    public string GetText() => Clipboard.GetText();

    public void SetImage(SKBitmap bitmap)
    {
        var data = new DataObject();

        using var bitmapWindows = bitmap.ToBitmap();
        data.SetData(DataFormats.Bitmap, true, bitmapWindows);

        using var pngStream = new MemoryStream();
        using var pixels = bitmap.PeekPixels();
        pixels.Encode(pngStream, new SKPngEncoderOptions(SKPngEncoderFilterFlags.Sub, zLibLevel: 1));

        data.SetData("PNG", false, pngStream);

        Clipboard.SetDataObject(data, copy: true);
    }
}

internal sealed class WindowsMessageDialogs : IMessageDialogService
{
    public Task ShowMessageAsync(string message, string title, MessageIcon icon = MessageIcon.Info)
    {
        MessageBox.Show(message, title, MessageBoxButtons.OK, ToWinForms(icon));
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string message, string title, MessageIcon icon = MessageIcon.Question, ConfirmButtons buttons = ConfirmButtons.OkCancel)
    {
        var winButtons = buttons == ConfirmButtons.YesNo ? MessageBoxButtons.YesNo : MessageBoxButtons.OKCancel;
        var result = MessageBox.Show(message, title, winButtons, ToWinForms(icon));
        return Task.FromResult(result is DialogResult.OK or DialogResult.Yes);
    }

    private static MessageBoxIcon ToWinForms(MessageIcon icon) => icon switch
    {
        MessageIcon.Info => MessageBoxIcon.Information,
        MessageIcon.Warning => MessageBoxIcon.Warning,
        MessageIcon.Error => MessageBoxIcon.Error,
        MessageIcon.Question => MessageBoxIcon.Question,
        _ => MessageBoxIcon.None,
    };
}

internal sealed class WindowsShell : IShellService
{
    public void OpenUrl(Uri url) => Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });

    public void OpenFile(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public void RevealInFileManager(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = @$"/select, ""{path}""",
            });
        }
        else if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path + Path.DirectorySeparatorChar,
                UseShellExecute = true,
                Verb = "open",
            });
        }
    }
}

#pragma warning restore RS0030
