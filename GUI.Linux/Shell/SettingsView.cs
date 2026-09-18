using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.Platform;
using GUI.Utils;
using ValveResourceFormat.IO;

namespace GUI.Linux.Shell;

/// <summary>Editable application settings for the Linux shell, backed by the shared settings model.</summary>
internal static partial class SettingsView
{
    private static readonly int[] AntiAliasingSamples = [0, 2, 4, 8, 16];
    private static readonly int[] ShadowResolutions = [512, 1024, 2048, 4096];
    private static readonly string[] ShadowNames = ["Low", "Medium", "High", "Very High"];

    public static Control Create()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock { Text = "Settings", FontSize = 22, FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    Text = "Changes are saved immediately. Rendering options apply to views opened after the change.",
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap,
                },
                BuildGameSection(),
                BuildRenderingSection(),
                BuildInterfaceSection(),
                BuildAudioSection(),
                BuildPathsSection(),
            },
        };

        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static StackPanel Section(string title) => new()
    {
        Spacing = 8,
        Children =
        {
            new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold },
        },
    };

    private static void Add(StackPanel section, string label, Control control)
    {
        var block = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = label },
                control,
            },
        };

        section.Children.Add(block);
    }

    private static void AddNote(StackPanel section, string text) => section.Children.Add(new TextBlock
    {
        Text = text,
        Opacity = 0.7,
        TextWrapping = TextWrapping.Wrap,
    });

    private static StackPanel BuildGameSection()
    {
        var section = Section("Game content");
        var games = GameContentLocator.DiscoverInstalledGames();

        var gameCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 320 };
        gameCombo.Items.Add("Auto (prefer Deadlock)");

        foreach (var game in games)
        {
            gameCombo.Items.Add($"{game.Name} ({game.AppId})");
        }

        gameCombo.SelectedIndex = 0;

        for (var i = 0; i < games.Count; i++)
        {
            if (string.Equals(games[i].Name, Settings.Config.SelectedGame, StringComparison.OrdinalIgnoreCase))
            {
                gameCombo.SelectedIndex = i + 1;
                break;
            }
        }

        gameCombo.SelectionChanged += (_, _) =>
        {
            Settings.Config.SelectedGame = gameCombo.SelectedIndex <= 0 ? string.Empty : games[gameCombo.SelectedIndex - 1].Name;
            Settings.Save();
            LinuxGameContent.Reset();
        };

        Add(section, "Active game", gameCombo);
        AddNote(section, "The active game supplies the content root used to open resources and render worlds.");

        var paths = new ListBox { Height = 120, Width = 520, HorizontalAlignment = HorizontalAlignment.Left };

        void RefreshPaths()
        {
            paths.Items.Clear();

            foreach (var path in Settings.Config.GameSearchPaths)
            {
                paths.Items.Add(path);
            }
        }

        RefreshPaths();

        var addFile = new Button { Content = "Add .vpk / gameinfo.gi" };
        addFile.Click += (_, _) =>
        {
            var path = AppFileDialogs.OpenFile(
                "Add game search path",
                "Valve Pak (*.vpk) or gameinfo.gi|*.vpk;gameinfo.gi|All files (*.*)|*.*",
                updateRemembered: false);

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            path = NormalizeVpkNumberArchive(path);

            if (Settings.Config.GameSearchPaths.Contains(path))
            {
                return;
            }

            Settings.Config.GameSearchPaths.Add(path);
            Settings.Save();
            LinuxGameContent.Reset();
            RefreshPaths();
        };

        var addFolder = new Button { Content = "Add folder" };
        addFolder.Click += (_, _) =>
        {
            var path = AppFileDialogs.PickFolder(null, AppFileDialogs.RememberIn.OpenDirectory, updateRemembered: false);

            if (string.IsNullOrEmpty(path) || Settings.Config.GameSearchPaths.Contains(path))
            {
                return;
            }

            Settings.Config.GameSearchPaths.Add(path);
            Settings.Save();
            LinuxGameContent.Reset();
            RefreshPaths();
        };

        var remove = new Button { Content = "Remove selected" };
        remove.Click += (_, _) =>
        {
            if (paths.SelectedItem is not string selected)
            {
                return;
            }

            Settings.Config.GameSearchPaths.Remove(selected);
            Settings.Save();
            LinuxGameContent.Reset();
            RefreshPaths();
        };

        section.Children.Add(new TextBlock { Text = "Game search paths" });
        section.Children.Add(paths);
        section.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { addFile, addFolder, remove },
        });
        AddNote(section, "Configured search paths take precedence over the selected installed game.");

        return section;
    }

    private static StackPanel BuildRenderingSection()
    {
        var section = Section("Rendering");

        var antiAliasing = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 120 };
        var selectedSamples = 0;

        for (var i = 0; i < AntiAliasingSamples.Length; i++)
        {
            antiAliasing.Items.Add($"{AntiAliasingSamples[i]}x");

            if (Settings.Config.AntiAliasingSamples >= AntiAliasingSamples[i])
            {
                selectedSamples = i;
            }
        }

        antiAliasing.SelectedIndex = selectedSamples;
        antiAliasing.SelectionChanged += (_, _) =>
        {
            if (antiAliasing.SelectedIndex >= 0)
            {
                Settings.Config.AntiAliasingSamples = AntiAliasingSamples[antiAliasing.SelectedIndex];
                Settings.Save();
            }
        };
        Add(section, "Anti-aliasing", antiAliasing);

        var shadows = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 200 };

        foreach (var name in ShadowNames)
        {
            shadows.Items.Add(name);
        }

        var shadowIndex = ShadowResolutions.Length - 1;

        for (var i = 0; i < ShadowResolutions.Length; i++)
        {
            if (Settings.Config.ShadowResolution <= ShadowResolutions[i])
            {
                shadowIndex = i;
                break;
            }
        }

        shadows.SelectedIndex = shadowIndex;
        shadows.SelectionChanged += (_, _) =>
        {
            if (shadows.SelectedIndex >= 0)
            {
                Settings.Config.ShadowResolution = ShadowResolutions[shadows.SelectedIndex];
                Settings.Save();
            }
        };
        Add(section, "Shadow quality", shadows);

        Add(section, "Maximum texture size", CreateNumber(Settings.Config.MaxTextureSize, 128, 10240, 128, value =>
        {
            Settings.Config.MaxTextureSize = (int)value;
        }));

        Add(section, "Field of view", CreateNumber((decimal)Settings.Config.FieldOfView, 1, 170, 1, value =>
        {
            Settings.Config.FieldOfView = (float)value;
        }));

        Add(section, "Mouse sensitivity", CreateNumber((decimal)Settings.Config.MouseSensitivity, 0.1m, 10m, 0.1m, value =>
        {
            Settings.Config.MouseSensitivity = (float)value;
        }));

        Add(section, "Smooth camera", CreateCheck(Settings.Config.SmoothCameraEnabled, value =>
        {
            Settings.Config.SmoothCameraEnabled = value;
        }));

        Add(section, "V-sync", CreateCheck(Settings.Config.Vsync != 0, value =>
        {
            Settings.Config.Vsync = value ? 1 : 0;
        }));

        Add(section, "Display FPS", CreateCheck(Settings.Config.DisplayFps != 0, value =>
        {
            Settings.Config.DisplayFps = value ? 1 : 0;
        }));

        return section;
    }

    private static StackPanel BuildInterfaceSection()
    {
        var section = Section("Interface");

        var theme = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 160 };

        foreach (var name in new[] { "System", "Light", "Dark" })
        {
            theme.Items.Add(name);
        }

        theme.SelectedIndex = Math.Clamp(Settings.Config.Theme, 0, 2);
        theme.SelectionChanged += (_, _) =>
        {
            if (theme.SelectedIndex < 0)
            {
                return;
            }

            Settings.Config.Theme = theme.SelectedIndex;
            Settings.Save();
            App.ApplyTheme();
        };
        Add(section, "Theme", theme);

        Add(section, "Text viewer font size", CreateNumber(Settings.Config.TextViewerFontSize, 8, 24, 1, value =>
        {
            Settings.Config.TextViewerFontSize = (int)value;
        }));

        Add(section, "Quick file preview", CreateCheck(
            ((Settings.QuickPreviewFlags)Settings.Config.QuickFilePreview & Settings.QuickPreviewFlags.Enabled) != 0,
            value => UpdateQuickPreview(flags => value ? flags | Settings.QuickPreviewFlags.Enabled : flags & ~Settings.QuickPreviewFlags.Enabled)));

        Add(section, "Auto-play sound previews", CreateCheck(
            ((Settings.QuickPreviewFlags)Settings.Config.QuickFilePreview & Settings.QuickPreviewFlags.AutoPlaySounds) != 0,
            value => UpdateQuickPreview(flags => value ? flags | Settings.QuickPreviewFlags.AutoPlaySounds : flags & ~Settings.QuickPreviewFlags.AutoPlaySounds)));

        return section;
    }

    private static StackPanel BuildAudioSection()
    {
        var section = Section("Audio");
        var volume = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Width = 320,
            HorizontalAlignment = HorizontalAlignment.Left,
            Value = Math.Clamp(Settings.Config.Volume * 100f, 0, 100),
        };

        volume.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                Settings.Config.Volume = (float)(volume.Value / 100.0);
                Settings.Save();
            }
        };

        Add(section, "Volume", volume);
        return section;
    }

    private static StackPanel BuildPathsSection()
    {
        var section = Section("Files");
        section.Children.Add(new TextBlock { Text = "Settings file" });
        section.Children.Add(new TextBlock
        {
            Text = System.IO.Path.Combine(PlatformServices.Current.SettingsDirectory, "settings.vdf"),
            FontFamily = new FontFamily("monospace"),
            TextWrapping = TextWrapping.Wrap,
        });

        return section;
    }

    private static NumericUpDown CreateNumber(decimal value, decimal minimum, decimal maximum, decimal increment, Action<decimal> onChanged)
    {
        var number = new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            Value = value,
            Width = 160,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        number.PropertyChanged += (_, e) =>
        {
            if (e.Property == NumericUpDown.ValueProperty && number.Value is { } current)
            {
                onChanged(current);
                Settings.Save();
            }
        };

        return number;
    }

    private static CheckBox CreateCheck(bool value, Action<bool> onChanged)
    {
        var check = new CheckBox
        {
            IsChecked = value,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        check.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
            {
                onChanged(check.IsChecked == true);
                Settings.Save();
            }
        };

        return check;
    }

    private static void UpdateQuickPreview(Func<Settings.QuickPreviewFlags, Settings.QuickPreviewFlags> update)
    {
        var flags = update((Settings.QuickPreviewFlags)Settings.Config.QuickFilePreview);
        Settings.Config.QuickFilePreview = (int)flags;
    }

    private static string NormalizeVpkNumberArchive(string path)
    {
        var fileName = System.IO.Path.GetFileName(path);

        if (VpkNumberArchiveRegex().IsMatch(fileName))
        {
            return string.Concat(path.AsSpan(0, path.Length - 8), "_dir.vpk");
        }

        return path;
    }

    [GeneratedRegex(@"_[0-9]{3}\.vpk$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VpkNumberArchiveRegex();
}
