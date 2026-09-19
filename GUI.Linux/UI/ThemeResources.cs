using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

namespace GUI.Linux.UI;

/// <summary>
/// Accessors for the semantic design tokens defined in <c>Styles/Tokens.axaml</c>. Code-built
/// controls bind properties through <see cref="Bind"/> so they track theme changes, while drawing
/// code can resolve the current value with the typed accessors.
/// </summary>
internal static class ThemeResources
{
    private static ThemeVariant? CurrentVariant
        => (Application.Current as IThemeVariantHost)?.ActualThemeVariant;

    /// <summary>Whether the application is currently in the light theme variant.</summary>
    public static bool IsLightTheme => CurrentVariant == ThemeVariant.Light;

    /// <summary>Binds a control property to a token so it follows the active theme variant.</summary>
    public static void Bind(AvaloniaObject target, AvaloniaProperty property, string token)
        => target.Bind(property, new DynamicResourceExtension(token));

    /// <summary>Resolves a token to a brush for the active theme, falling back to transparent.</summary>
    public static IBrush Brush(string token)
        => Application.Current?.TryGetResource(token, CurrentVariant, out var value) == true && value is IBrush brush
            ? brush
            : Brushes.Transparent;

    /// <summary>Resolves a token to a double for the active theme, falling back to zero.</summary>
    public static double Double(string token)
        => Application.Current?.TryGetResource(token, CurrentVariant, out var value) == true && value is double number
            ? number
            : 0d;

    /// <summary>Resolves a token to a corner radius for the active theme, falling back to zero.</summary>
    public static CornerRadius CornerRadius(string token)
        => Application.Current?.TryGetResource(token, CurrentVariant, out var value) == true && value is CornerRadius radius
            ? radius
            : default;
}
