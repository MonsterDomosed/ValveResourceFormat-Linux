using System.IO;
using GUI.Platform;

namespace GUI.Linux.Platform;

/// <summary>Linux implementation of <see cref="IPlatformServices"/>.</summary>
internal sealed class LinuxPlatformServices : IPlatformServices
{
    public IFileDialogService FileDialogs { get; } = new LinuxFileDialogs();
    public IClipboardService Clipboard { get; } = new LinuxClipboard();
    public IMessageDialogService MessageDialogs { get; } = new LinuxMessageDialogs();
    public IShellService Shell { get; } = new LinuxShell();

    public string SettingsDirectory { get; } = Path.Combine(XdgPaths.DataHome, "Source2Viewer");
}
