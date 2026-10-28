using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Logging;
using GUI.Linux.Utils;

namespace GUI.Linux;

internal static class Program
{
    /// <summary>Selects how much of the non-interactive startup check runs before exiting.</summary>
    internal enum SelfCheckMode
    {
        /// <summary>Run the interactive application.</summary>
        None,

        /// <summary>Start the shell, render one lightweight GL frame and exit.</summary>
        Smoke,

        /// <summary>Run the full content and viewer validation suite.</summary>
        Full,

        /// <summary>Run only the package browser validation.</summary>
        Browser,
    }

    /// <summary>How much of the non-interactive startup and platform-service check to run.</summary>
    internal static SelfCheckMode SelfCheck { get; private set; }

    /// <summary>
    /// Sections of the full self-check to run, from <c>--self-check-section</c>. Empty runs all of
    /// them, which takes minutes; naming one or two keeps the loop around a single change short.
    /// </summary>
    internal static HashSet<string> SelfCheckSections { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a named section of the full self-check should run.</summary>
    internal static bool WantsSelfCheckSection(string name)
        => SelfCheckSections.Count == 0 || SelfCheckSections.Contains(name);

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

        SelfCheck = ParseSelfCheck(args);

        foreach (var arg in args)
        {
            const string Prefix = "--self-check-section=";

            if (arg.StartsWith(Prefix, StringComparison.Ordinal))
            {
                foreach (var section in arg[Prefix.Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    SelfCheckSections.Add(section);
                }
            }
        }

        Args = args;
        FileArgs = [.. args.Where(static arg => !arg.StartsWith("--", StringComparison.Ordinal))];

        if (SelfCheck != SelfCheckMode.None)
        {
            // Surface Avalonia's own trace on stdout so headless runs can report why GL init failed.
            Trace.Listeners.Add(new TextWriterTraceListener(StdOut));
            Trace.AutoFlush = true;
        }

        SetAppInfo();
        LinuxPlatform.Initialize();

        return BuildAvaloniaApp(args).StartWithClassicDesktopLifetime(args);
    }

    private static SelfCheckMode ParseSelfCheck(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.Equals("--self-check=smoke", StringComparison.Ordinal))
            {
                return SelfCheckMode.Smoke;
            }

            if (arg.Equals("--self-check", StringComparison.Ordinal))
            {
                return SelfCheckMode.Full;
            }

            if (arg.Equals("--self-check=browser", StringComparison.Ordinal))
            {
                return SelfCheckMode.Browser;
            }
        }

        return SelfCheckMode.None;
    }

    public static AppBuilder BuildAvaloniaApp(string[] args)
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace(LogEventLevel.Information);

        if (OperatingSystem.IsLinux() && args.Contains("--wayland", StringComparer.Ordinal))
        {
            // Native Wayland is available on request, but it is not the default: a Wayland client cannot
            // move the pointer, so dragging a model stops where the pointer stops. The X11 backend runs
            // through Xwayland on a Wayland desktop, where the pointer warp that keeps a drag going is
            // emulated, which is also what other 3D applications rely on. UsePlatformDetect already
            // prefers X11 when an X server is reachable and falls back to Wayland when there is none.
            builder = builder.UseWayland();
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
#if CI_RELEASE_BUILD
        AppInfo.IsReleaseBuild = true;
#else
        AppInfo.IsReleaseBuild = false;
#endif
    }
}
