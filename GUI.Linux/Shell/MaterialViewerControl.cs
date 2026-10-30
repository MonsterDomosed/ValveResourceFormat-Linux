using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GUI.Linux.GL;
using GUI.Linux.Types.GLViewers;
using GUI.Linux.UI;

namespace GUI.Linux.Shell;

/// <summary>
/// Material inspection view: the shared scene sidebar and viewport with an editor for the material's
/// float, int and vector parameters. Values go through <see cref="MaterialInspectorSession"/> and are
/// written into the material by the renderer, which is what marks it for re-upload.
/// </summary>
internal sealed class MaterialViewerControl : UserControl, IDisposable
{
    private sealed record Row(MaterialParameter Parameter, Control[] Containers, Action<MaterialParameter> Apply);

    private readonly MaterialInspectorSession session;
    private readonly TextBox filterBox = new() { PlaceholderText = "Filter parameters..." };
    private readonly StackPanel parameterList = new() { Spacing = 6 };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    private readonly List<Row> rows = [];
    private string[] builtNames = [];
    private bool updatingFromState;
    private bool disposed;

    public MaterialViewerControl(Func<IGLViewportRenderer> createRenderer, MaterialInspectorSession session)
    {
        this.session = session;

        var viewer = new ViewportWithSidebar(createRenderer, new ViewerSidebar());
        session.RequestFrame = viewer.Viewport.RequestFrame;

        var header = new StackPanel { Spacing = 8, Margin = new Thickness(10) };
        header.Children.Add(new TextBlock { Text = "Parameters", FontWeight = FontWeight.SemiBold });
        header.Children.Add(filterBox);

        filterBox.TextChanged += (_, _) => ApplyFilter();

        var scroll = new ScrollViewer
        {
            Content = parameterList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(scroll);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(320)) { MinWidth = 220, MaxWidth = 520 });

        Grid.SetColumn(viewer, 0);
        grid.Children.Add(viewer);

        var splitter = new GridSplitter { Width = 4, ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        Grid.SetColumn(panel, 2);
        grid.Children.Add(panel);

        Content = grid;

        timer.Tick += (_, _) => UpdateFromState();
        timer.Start();
        UpdateFromState();
    }

    /// <summary>The parameter session, for the self-check.</summary>
    internal MaterialInspectorSession Session => session;

    private void UpdateFromState()
    {
        var parameters = session.GetParameters();
        var names = parameters.Select(static parameter => parameter.Name).ToArray();

        if (!names.SequenceEqual(builtNames, StringComparer.Ordinal))
        {
            BuildRows(parameters);
            builtNames = names;
        }

        updatingFromState = true;

        try
        {
            foreach (var row in rows)
            {
                row.Apply(row.Parameter);
            }
        }
        finally
        {
            updatingFromState = false;
        }
    }

    private void BuildRows(MaterialParameter[] parameters)
    {
        parameterList.Children.Clear();
        rows.Clear();

        foreach (var parameter in parameters)
        {
            rows.Add(parameter.Kind switch
            {
                MaterialParameterKind.Float => BuildScalarRow(parameter, isFloat: true),
                MaterialParameterKind.Int => BuildScalarRow(parameter, isFloat: false),
                _ => BuildVectorRow(parameter),
            });
        }

        ApplyFilter();
    }

    private Row BuildScalarRow(MaterialParameter parameter, bool isFloat)
    {
        var label = new TextBlock
        {
            Text = parameter.Name,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var input = new NumericUpDown
        {
            Increment = isFloat ? 0.1m : 1m,
            FormatString = isFloat ? "0.###" : "0",
            Width = 120,
        };

        input.ValueChanged += (_, e) =>
        {
            if (updatingFromState || e.NewValue is not { } value)
            {
                return;
            }

            session.Set(isFloat
                ? new MaterialParameterChange(MaterialParameterKind.Float, parameter.Name, (float)value, 0, default)
                : new MaterialParameterChange(MaterialParameterKind.Int, parameter.Name, 0f, (long)value, default));
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(label, 0);
        Grid.SetColumn(input, 1);
        row.Children.Add(label);
        row.Children.Add(input);
        parameterList.Children.Add(row);

        return new Row(parameter, [row], current =>
        {
            var value = isFloat ? (decimal)current.Float : current.Int;

            if (input.Value != value)
            {
                input.Value = value;
            }
        });
    }

    private Row BuildVectorRow(MaterialParameter parameter)
    {
        var label = new TextBlock
        {
            Text = parameter.Name,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var inputs = new NumericUpDown[4];
        var components = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

        for (var i = 0; i < inputs.Length; i++)
        {
            var component = i;
            var input = new NumericUpDown
            {
                Increment = 0.1m,
                FormatString = "0.###",
                Width = 64,
            };

            input.ValueChanged += (_, e) =>
            {
                if (updatingFromState)
                {
                    return;
                }

                var value = new Vector4(
                    (float)(inputs[0].Value ?? 0m),
                    (float)(inputs[1].Value ?? 0m),
                    (float)(inputs[2].Value ?? 0m),
                    (float)(inputs[3].Value ?? 0m));

                value = component switch
                {
                    0 => new Vector4((float)(e.NewValue ?? 0m), value.Y, value.Z, value.W),
                    1 => new Vector4(value.X, (float)(e.NewValue ?? 0m), value.Z, value.W),
                    2 => new Vector4(value.X, value.Y, (float)(e.NewValue ?? 0m), value.W),
                    _ => new Vector4(value.X, value.Y, value.Z, (float)(e.NewValue ?? 0m)),
                };

                session.Set(new MaterialParameterChange(MaterialParameterKind.Vector, parameter.Name, 0f, 0, value));
            };

            inputs[i] = input;
            components.Children.Add(input);
        }

        parameterList.Children.Add(label);
        parameterList.Children.Add(components);

        return new Row(parameter, [label, components], current =>
        {
            var value = current.Vector;

            Set(inputs[0], value.X);
            Set(inputs[1], value.Y);
            Set(inputs[2], value.Z);
            Set(inputs[3], value.W);

            static void Set(NumericUpDown input, float component)
            {
                var decimalValue = (decimal)component;

                if (input.Value != decimalValue)
                {
                    input.Value = decimalValue;
                }
            }
        });
    }

    private void ApplyFilter()
    {
        var query = filterBox.Text?.Trim() ?? string.Empty;

        foreach (var row in rows)
        {
            var visible = query.Length == 0
                || row.Parameter.Name.Contains(query, StringComparison.OrdinalIgnoreCase);

            foreach (var container in row.Containers)
            {
                container.IsVisible = visible;
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        session.RequestFrame = null;
        GC.SuppressFinalize(this);
    }
}
