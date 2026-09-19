using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.Linux.Types.Viewers;
using GUI.Linux.Utils;

namespace GUI.Linux.Shell;

/// <summary>
/// Renders the UI-agnostic <see cref="ViewerContent"/> model into native Avalonia controls.
/// Presents the portable viewer content model in the Avalonia shell.
/// </summary>
internal static class AvaloniaViewerContentPresenter
{
    private static readonly FontFamily MonospaceFont = new("monospace");

    /// <summary>Creates a control for the given content.</summary>
    public static Control Present(ViewerContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return content switch
        {
            ViewerContent.Text text => CreateText(text.Content, text.Language),
            ViewerContent.LazyText lazy => CreateLazyText(lazy),
            ViewerContent.HexDump hex => CreateText(FormatHexDump(hex.Bytes), HighlightLanguage.None),
            ViewerContent.EncodedImage image => CreateImage(image.Bytes),
            ViewerContent.Grid grid => CreateGrid(grid),
            ViewerContent.GlViewport gl => CreateGlViewport(gl),
            ViewerContent.CustomControl custom => (Control)custom.CreateControl(),
            ViewerContent.Tabs tabs => CreateTabs(tabs),
            _ => throw new NotSupportedException($"Unknown content type {content.GetType().Name}"),
        };
    }

    /// <summary>Adds a tab for the given content and optionally selects it.</summary>
    public static TabItem AddContentTab(TabControl tabControl, ViewerTab tab)
    {
        ArgumentNullException.ThrowIfNull(tabControl);
        ArgumentNullException.ThrowIfNull(tab);

        var item = new TabItem
        {
            Header = tab.Name,
            Content = Present(tab.Content),
        };

        tabControl.Items.Add(item);

        if (tab.Select)
        {
            tabControl.SelectedItem = item;
        }

        return item;
    }

    private static ScrollViewer CreateLazyText(ViewerContent.LazyText lazy)
    {
        string producedText;

        try
        {
            producedText = lazy.GetContent();
        }
        catch (Exception e)
        {
            producedText = e.ToString();
        }

        return CreateText(producedText, lazy.Language);
    }

    private static ScrollViewer CreateText(string text, HighlightLanguage language)
    {
        // Syntax highlighting for the custom language definitions is not ported yet; the text is
        // shown verbatim in a selectable monospace view.
        _ = language;

        var block = new SelectableTextBlock
        {
            Text = text,
            FontFamily = MonospaceFont,
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(8),
        };

        return new ScrollViewer
        {
            Content = block,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
    }

    private static ScrollViewer CreateImage(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);

        var image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12),
        };

        return new ScrollViewer
        {
            Content = image,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
    }

    private static TabControl CreateTabs(ViewerContent.Tabs tabs)
    {
        var tabControl = new TabControl();

        foreach (var tab in tabs.Items)
        {
            AddContentTab(tabControl, tab);
        }

        return tabControl;
    }

    private static GUI.Linux.GL.AvaloniaGlViewport CreateGlViewport(ViewerContent.GlViewport content)
    {
        return new GUI.Linux.GL.AvaloniaGlViewport
        {
            RendererFactory = content.CreateRenderer,
        };
    }

    private static Control CreateGrid(ViewerContent.Grid grid)
    {
        var rows = grid.Rows;

        if (rows.Count == 0)
        {
            return new TextBlock { Text = "(no rows)", Margin = new Thickness(8) };
        }

        var properties = rows[0]?.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.GetIndexParameters().Length == 0)
            .ToArray() ?? [];

        if (properties.Length == 0)
        {
            return new TextBlock { Text = "(no columns)", Margin = new Thickness(8) };
        }

        var table = new Grid();

        for (var c = 0; c < properties.Length; c++)
        {
            table.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var c = 0; c < properties.Length; c++)
        {
            var header = new TextBlock
            {
                Text = properties[c].Name,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(8, 4),
            };

            Grid.SetColumn(header, c);
            Grid.SetRow(header, 0);
            table.Children.Add(header);
        }

        for (var r = 0; r < rows.Count; r++)
        {
            table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var item = rows[r];

            for (var c = 0; c < properties.Length; c++)
            {
                var value = ReadCell(item, properties[c]);

                var cell = new TextBlock
                {
                    Text = value,
                    TextWrapping = TextWrapping.NoWrap,
                    Margin = new Thickness(8, 2),
                    VerticalAlignment = VerticalAlignment.Top,
                };

                Grid.SetColumn(cell, c);
                Grid.SetRow(cell, r + 1);
                table.Children.Add(cell);
            }
        }

        return new ScrollViewer
        {
            Content = table,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
    }

    private static string ReadCell(object? item, PropertyInfo property)
    {
        if (item is null)
        {
            return string.Empty;
        }

        try
        {
            return property.GetValue(item)?.ToString() ?? string.Empty;
        }
        catch (Exception)
        {
            return "<unavailable>";
        }
    }

    /// <summary>Formats bytes as a classic offset/hex/ascii dump.</summary>
    public static string FormatHexDump(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        const int bytesPerLine = 16;
        var builder = new System.Text.StringBuilder(bytes.Length * 4 + 64);
        var ascii = new char[bytesPerLine];

        for (var offset = 0; offset < bytes.Length; offset += bytesPerLine)
        {
            var count = Math.Min(bytesPerLine, bytes.Length - offset);
            builder.Append(CultureInfo.InvariantCulture, $"{offset:X8}  ");

            for (var i = 0; i < bytesPerLine; i++)
            {
                if (i < count)
                {
                    var value = bytes[offset + i];
                    builder.Append(CultureInfo.InvariantCulture, $"{value:X2} ");
                    ascii[i] = value is >= 32 and < 127 ? (char)value : '.';
                }
                else
                {
                    builder.Append("   ");
                    ascii[i] = ' ';
                }

                if (i == 7)
                {
                    builder.Append(' ');
                }
            }

            builder.Append(" |");
            builder.Append(ascii, 0, count);
            builder.Append("|\n");
        }

        return builder.ToString();
    }
}
