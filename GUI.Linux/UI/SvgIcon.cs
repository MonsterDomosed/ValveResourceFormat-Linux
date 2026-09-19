using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace GUI.Linux.UI;

/// <summary>
/// A small control that draws a rasterized SVG icon and re-resolves it when the theme variant
/// changes, so light/dark icon variants stay in sync without rebuilding the UI.
/// </summary>
internal sealed class SvgIcon : Control
{
    private readonly string name;
    private readonly int size;
    private bool subscribed;

    public SvgIcon(string name, int size = 16)
    {
        this.name = name;
        this.size = size;
        Width = size;
        Height = size;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (!subscribed && Application.Current is IThemeVariantHost host)
        {
            host.ActualThemeVariantChanged += OnThemeVariantChanged;
            subscribed = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (subscribed && Application.Current is IThemeVariantHost host)
        {
            host.ActualThemeVariantChanged -= OnThemeVariantChanged;
            subscribed = false;
        }
    }

    private void OnThemeVariantChanged(object? sender, EventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        // The bitmap is owned by the IconFactory cache and must not be disposed here.
#pragma warning disable CA2000
        var bitmap = IconFactory.Get(name, size);
#pragma warning restore CA2000

        if (bitmap is not null)
        {
            context.DrawImage(bitmap, new Rect(0, 0, size, size));
        }
    }
}
