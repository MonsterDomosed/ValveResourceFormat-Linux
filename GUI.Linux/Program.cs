using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Logging;
using GUI.Utils;

namespace GUI.Linux;

internal static class Program
{
    /// <summary>Whether to run the non-interactive startup and platform-service check and exit.</summary>
    internal static bool SelfCheck { get; private set; }

    /// <summary>Command line arguments passed to the shell.</summary>
    internal static string[] Args { get; private set; } = [];

    /// <summary>Command line arguments that are file paths (shell flags such as --wayland are removed).</summary>
    internal static string[] FileArgs { get; private set; } = [];

    /// <summary>The real standard output, captured before the console sink redirects it.</summary>
    internal static TextWriter StdOut { get; private set; } = Console.Out;

    [STAThread]
    public static int Main(string[] args)
    {
        StdOut = Console.Out;

        // Set invariant culture so we have consistent localization (e.g. dots do not get encoded as commas)
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        SelfCheck = args.Contains("--self-check", StringComparer.Ordinal);
        Args = args;
        FileArgs = [.. args.Where(static arg => !arg.StartsWith("--", StringComparison.Ordinal))];

        SetAppInfo();
        LinuxPlatform.Initialize();

        return BuildAvaloniaApp(args).StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp(string[] args)
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace(LogEventLevel.Information);

        if (OperatingSystem.IsLinux())
        {
            if (args.Contains("--wayland", StringComparer.Ordinal))
            {
                builder = builder.UseWayland();
            }
            else if (!args.Contains("--x11", StringComparer.Ordinal))
            {
                // Use Wayland when a compositor is available, otherwise the X11 backend selected above.
                builder = builder.UseWaylandWithFallback();
            }
        }

        return builder;
    }

    private static void SetAppInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        AppInfo.ProductVersion = informational;
        AppInfo.DisplayVersion = informational;
        AppInfo.IsReleaseBuild = false;
    }
}
