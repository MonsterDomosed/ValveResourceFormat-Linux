using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using GUI.Linux.Types.Exporter;

namespace GUI.Linux.Shell;

/// <summary>
/// Asks what a single-file glTF export should contain. A model with every animation and material can
/// be much larger than what the user wanted to look at.
/// </summary>
internal sealed class GltfExportOptionsDialog : Window
{
    private readonly CheckBox animations = new() { Content = "Animations", IsChecked = true };
    private readonly CheckBox materials = new() { Content = "Materials and textures", IsChecked = true };
    private readonly CheckBox convertTextures = new() { Content = "Convert textures to image files", IsChecked = true };

    /// <summary>The chosen options, or null when the dialog was cancelled.</summary>
    public GltfExportOptions? Options { get; private set; }

    public GltfExportOptionsDialog()
    {
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        Title = "glTF export options";

        materials.IsCheckedChanged += (_, _) =>
        {
            // Converting textures only applies when materials are exported at all
            convertTextures.IsEnabled = materials.IsChecked == true;
        };

        var export = new Button { Content = "Export", Classes = { "tool" }, MinWidth = 85 };
        var cancel = new Button { Content = "Cancel", Classes = { "tool" }, MinWidth = 85 };

        export.Click += (_, _) =>
        {
            Options = new GltfExportOptions(
                Animations: animations.IsChecked == true,
                Materials: materials.IsChecked == true,
                ConvertTextures: convertTextures.IsChecked == true);

            Close();
        };

        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(23),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = "Choose what the glTF file should contain.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                animations,
                materials,
                convertTextures,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, export },
                },
            },
        };
    }

    /// <summary>Shows the dialog and returns the chosen options, or null when it was cancelled.</summary>
    public static async Task<GltfExportOptions?> ShowAsync(Window owner)
    {
        var dialog = new GltfExportOptionsDialog();
        await dialog.ShowDialog(owner).ConfigureAwait(true);

        return dialog.Options;
    }
}
