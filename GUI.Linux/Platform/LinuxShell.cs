using System.Diagnostics;
using System.IO;
using GUI.Platform;

namespace GUI.Linux.Platform;

/// <summary>
/// Linux <see cref="IShellService"/>. Uses the XDG <c>xdg-open</c> helper, which delegates to the
/// desktop environment's configured handlers on both Wayland and X11.
/// </summary>
internal sealed class LinuxShell : IShellService
{
    public void OpenUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        Launch(url.AbsoluteUri);
    }

    public void OpenFile(string path)
    {
        if (File.Exists(path))
        {
            Launch(path);
        }
    }

    public void RevealInFileManager(string path)
    {
        // Linux desktop environments have no portable equivalent of "select this file"; opening the
        // containing directory is the closest behaviour.
        var directory = File.Exists(path) ? Path.GetDirectoryName(path) : path;

        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            Launch(directory);
        }
    }

    private static void Launch(string target)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "xdg-open",
            UseShellExecute = false,
            ArgumentList = { target },
        });
    }
}
