using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GUI.Linux.Shell;

/// <summary>
/// Asks which output format each compiled file type should be decompiled as, so a whole folder can be
/// exported in one go. Rows are added by <see cref="AddTypeToTable"/> before the window is shown.
/// </summary>
internal sealed class ExtractOutputTypesDialog : Window
{
    private readonly StackPanel rows = new() { Spacing = 4 };
    private readonly Dictionary<ComboBox, string> comboTypes = [];

    /// <summary>Raised when any output selection changes, so the caller can record the choice.</summary>
    public event Action<ComboBox, string>? SelectionChanged;

    /// <summary>Whether the user pressed Continue rather than closing the window.</summary>
    public bool ContinuePressed { get; private set; }

    public ExtractOutputTypesDialog()
    {
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 493;
        SizeToContent = SizeToContent.Height;
        Title = "Select output file types...";

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(3, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(4, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(3, GridUnitType.Star)),
            },
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                HeaderLabel("Input"),
                HeaderLabel("Output"),
                HeaderLabel("Count"),
            },
        };

        Grid.SetColumn(header.Children[1], 1);
        Grid.SetColumn(header.Children[2], 2);

        var scroll = new ScrollViewer
        {
            MaxHeight = 421,
            Content = rows,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var continueButton = new Button { Content = "Continue", Classes = { "tool" }, MinWidth = 75, HorizontalAlignment = HorizontalAlignment.Right };
        continueButton.Click += (_, _) =>
        {
            ContinuePressed = true;
            Close();
        };

        Content = new StackPanel
        {
            Margin = new Thickness(23),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "Select which output types you would like each compiled file to export as.",
                    TextWrapping = TextWrapping.Wrap,
                },
                header,
                scroll,
                continueButton,
            },
        };
    }

    private static TextBlock HeaderLabel(string text) => new()
    {
        Text = text,
        Classes = { "section" },
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Adds one input type row with a combo box of the output types it can be exported as.</summary>
    public void AddTypeToTable(string type, int count, List<string> outputTypes, int defaultIndex)
    {
        var combo = new ComboBox { ItemsSource = outputTypes, SelectedIndex = defaultIndex, MinWidth = 160 };
        comboTypes[combo] = type;
        combo.SelectionChanged += (_, _) => SelectionChanged?.Invoke(combo, type);

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(3, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(4, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(3, GridUnitType.Star)),
            },
            Margin = new Thickness(0, 0, 0, 4),
            Children =
            {
                new TextBlock { Text = type, VerticalAlignment = VerticalAlignment.Center },
                combo,
                new TextBlock
                {
                    Text = $"{count} file{(count == 1 ? "" : "s")}",
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };

        Grid.SetColumn(row.Children[1], 1);
        Grid.SetColumn(row.Children[2], 2);

        rows.Children.Add(row);
    }
}
