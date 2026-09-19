using System.Threading;
using System.Threading.Tasks;
using GUI.Linux.Utils;
using SkiaSharp;

namespace GUI.Linux.Platform;

/// <summary>Where a file dialog should start, and which remembered directory to update.</summary>
public enum FileDialogRemember
{
    None,
    OpenDirectory,
    SaveDirectory,
}

/// <summary>Open/save/folder pickers. Implemented per UI toolkit.</summary>
public interface IFileDialogService
{
    string? PickFolder(string? title, FileDialogRemember remember = FileDialogRemember.None, bool updateRemembered = true);
    string? OpenFile(string? title, string? filter, FileDialogRemember remember = FileDialogRemember.OpenDirectory, bool updateRemembered = true);
    string[]? OpenFiles(string? title, string? filter, FileDialogRemember remember = FileDialogRemember.OpenDirectory, bool updateRemembered = true);
    string? SaveFile(string title, string? defaultFileName, string? defaultExtension, string filter, FileDialogRemember remember = FileDialogRemember.SaveDirectory);
    string? SaveFile(string title, string? defaultFileName, string? defaultExtension, string filter, out int selectedFilterIndex, FileDialogRemember remember = FileDialogRemember.SaveDirectory);
}

/// <summary>Text and image clipboard access.</summary>
public interface IClipboardService
{
    void SetText(string text);
    string GetText();
    void SetImage(SKBitmap bitmap);
}

/// <summary>Modal user notifications and confirmations.</summary>
public interface IMessageDialogService
{
    Task ShowMessageAsync(string message, string title, MessageIcon icon = MessageIcon.Info);
    Task<bool> ConfirmAsync(string message, string title, MessageIcon icon = MessageIcon.Question, ConfirmButtons buttons = ConfirmButtons.OkCancel);
}

/// <summary>Handing paths and URLs to the desktop environment.</summary>
public interface IShellService
{
    void OpenUrl(Uri url);
    void OpenFile(string path);
    void RevealInFileManager(string path);
}

/// <summary>
/// The set of OS/UI operations the portable application code needs. A platform shell registers its
/// implementation once at startup; portable code only ever sees these interfaces.
/// </summary>
public interface IPlatformServices
{
    IFileDialogService FileDialogs { get; }
    IClipboardService Clipboard { get; }
    IMessageDialogService MessageDialogs { get; }
    IShellService Shell { get; }

    /// <summary>Directory where settings and other persistent application data are stored.</summary>
    string SettingsDirectory { get; }
}

/// <summary>Process-wide holder for the active <see cref="IPlatformServices"/> implementation.</summary>
public static class PlatformServices
{
    private static IPlatformServices? current;

    /// <summary>Whether a platform implementation has been registered.</summary>
    public static bool IsRegistered => current != null;

    /// <summary>The registered implementation.</summary>
    /// <exception cref="InvalidOperationException">No implementation has been registered.</exception>
    public static IPlatformServices Current => current
        ?? throw new InvalidOperationException("No platform services have been registered. A platform shell must call PlatformServices.Register at startup.");

    /// <summary>Registers the platform implementation. The first registration wins.</summary>
    public static void Register(IPlatformServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Interlocked.CompareExchange(ref current, services, null);
    }
}
