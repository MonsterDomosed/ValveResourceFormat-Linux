using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.Linux.Platform;
using GUI.Linux.Utils;
using ValveResourceFormat.Renderer;

namespace GUI.Linux.Shell;

/// <summary>
/// The About dialog: build information, graphics details and project links.
/// </summary>
internal static class AboutDialog
{
    private const string ForkUrl = "https://github.com/MonsterDomosed/ValveResourceFormat-Linux";
    private const string OriginalUrl = "https://github.com/ValveResourceFormat/ValveResourceFormat";
    private const string WebsiteUrl = "https://s2v.app";

    public static async Task ShowAsync(Window owner)
    {
        var info = new StackPanel { Spacing = 6 };

        AddRow(info, "Version", AppInfo.DisplayVersion);
        AddRow(info, "Channel", Settings.BuildChannel.ToString());
        AddRow(info, "Runtime", Environment.Version.ToString());

        if (GLEnvironment.GpuRendererAndDriver is { Length: > 0 } gpu)
        {
            AddRow(info, "GPU", gpu);
        }

        var links = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                Link("Website", WebsiteUrl),
                Link("Fork", ForkUrl),
                Link("Original", OriginalUrl),
            },
        };

        var copyButton = new Button { Content = "Copy version", Classes = { "tool" } };
        copyButton.Click += (_, _) => PlatformServices.Current.Clipboard.SetText(BuildVersionText());

        Window? dialog = null;

        var closeButton = new Button { Content = "Close", IsDefault = true, IsCancel = true, MinWidth = 88 };
        closeButton.Click += (_, _) => dialog?.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { copyButton, closeButton },
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new UI.SvgIcon("Logo", 40),
                new StackPanel
                {
                    Spacing = 2,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "Source 2 Viewer", Classes = { "title" } },
                        new TextBlock { Text = "Native Linux shell built on Avalonia and OpenGL.", Classes = { "hint" } },
                    },
                },
            },
        };

        var window = new Window
        {
            Title = "About Source 2 Viewer",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children = { header, info, links, buttons },
            },
        };

        dialog = window;
        await window.ShowDialog(owner).ConfigureAwait(true);
    }

    private static Button Link(string label, string url)
    {
        var button = new Button { Content = label, Classes = { "tool" } };
        button.Click += (_, _) => PlatformServices.Current.Shell.OpenUrl(new Uri(url));
        return button;
    }

    private static void AddRow(Panel parent, string label, string value)
    {
        parent.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = label, Width = 80, Classes = { "hint" } },
                new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap },
            },
        });
    }

    private static string BuildVersionText()
    {
        var text = $"Source 2 Viewer {AppInfo.DisplayVersion} ({Settings.BuildChannel}, {Environment.Version})";

        if (GLEnvironment.GpuRendererAndDriver is { Length: > 0 } gpu)
        {
            text += $"\n{gpu}";
        }

        return text;
    }
}
