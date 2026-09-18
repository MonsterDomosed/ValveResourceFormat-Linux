using Avalonia.Controls;
using GUI.Linux.Platform;
using GUI.Platform;

namespace GUI.Linux;

/// <summary>
/// Entry point for the Linux GUI shell. Registers the Linux platform services and exposes the main
/// window that toolkit-backed services (file dialogs, clipboard, dialogs) need.
/// </summary>
public static class LinuxPlatform
{
    /// <summary>Registers the Linux implementation of <see cref="IPlatformServices"/>.</summary>
    public static void Initialize() => PlatformServices.Register(new LinuxPlatformServices());

    /// <summary>The application's main window, set once the Avalonia shell has created it.</summary>
    internal static Window? MainWindow { get; set; }
}
