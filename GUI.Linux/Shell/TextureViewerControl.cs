using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GUI.Linux.GL;
using GUI.Linux.Types.GLViewers;
using ValveResourceFormat.CompiledShader;

namespace GUI.Linux.Shell;

/// <summary>
/// Texture inspection view: a GL viewport with a sidebar that selects the mip, depth slice, cube face
/// and projection, the channel mapping, the channel split and a sprite-sheet frame. Everything goes
/// through <see cref="TextureInspectorSession"/>, so the render thread only reads a snapshot.
/// </summary>
internal sealed class TextureViewerControl : UserControl, IDisposable
{
    private readonly TextureInspectorSession session;
    private readonly AvaloniaGlViewport viewport;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private readonly ComboBox mipSelector = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox depthSelector = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox faceSelector = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox projectionSelector = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox channelSelector = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox splitSelector = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox spriteSelector = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox spriteCrop = new() { Content = "Crop to frame" };

    private readonly StackPanel mipGroup = new() { Spacing = 6 };
    private readonly StackPanel depthGroup = new() { Spacing = 6 };
    private readonly StackPanel faceGroup = new() { Spacing = 6 };
    private readonly StackPanel projectionGroup = new() { Spacing = 6 };
    private readonly StackPanel spriteGroup = new() { Spacing = 6 };

    private static readonly (string Label, uint Packed)[] ChannelPresets =
    [
        ("RGBA", ChannelMapping.RGBA.PackedValue),
        ("RGB", ChannelMapping.RGB.PackedValue),
        ("RG", ChannelMapping.RG.PackedValue),
        ("AG", ChannelMapping.AG.PackedValue),
        ("R", ChannelMapping.R.PackedValue),
        ("G", ChannelMapping.G.PackedValue),
        ("B", ChannelMapping.B.PackedValue),
        ("A", ChannelMapping.A.PackedValue),
    ];

    private static readonly string[] FaceNames = ["+X", "-X", "+Y", "-Y", "+Z", "-Z"];
    private static readonly string[] ProjectionNames = ["Face", "Equirectangular", "Cubemap"];

    private int populatedMipCount = -1;
    private int populatedDepthCount = -1;
    private int populatedSpriteCount = -1;
    private bool updatingFromState;
    private bool disposed;

    public TextureViewerControl(string fileName, TextureInspectorSession session)
    {
        this.session = session;

        viewport = new AvaloniaGlViewport
        {
            RendererFactory = () => new TextureGlRenderer(fileName, session),
        };

        session.RequestFrame = viewport.RequestFrame;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(240)) { MinWidth = 200, MaxWidth = 420 });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        var sidebar = BuildSidebar();
        Grid.SetColumn(sidebar, 0);
        grid.Children.Add(sidebar);

        var splitter = new GridSplitter { Width = 4, ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        Grid.SetColumn(viewport, 2);
        grid.Children.Add(viewport);

        Content = grid;

        timer.Tick += (_, _) => UpdateFromState();
        timer.Start();
        UpdateFromState();
    }

    /// <summary>The controls' session, for the self-check.</summary>
    internal TextureInspectorSession Session => session;

    private ScrollViewer BuildSidebar()
    {
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(12) };

        panel.Children.Add(new TextBlock { Text = "Texture", FontWeight = FontWeight.SemiBold });

        mipGroup.Children.Add(new TextBlock { Text = "Mip", Classes = { "hint" } });
        mipSelector.SelectionChanged += (_, _) =>
        {
            if (!updatingFromState && mipSelector.SelectedIndex >= 0)
            {
                session.SetMip(mipSelector.SelectedIndex);
            }
        };
        mipGroup.Children.Add(mipSelector);
        panel.Children.Add(mipGroup);

        depthGroup.Children.Add(new TextBlock { Text = "Depth slice", Classes = { "hint" } });
        depthSelector.SelectionChanged += (_, _) =>
        {
            if (!updatingFromState && depthSelector.SelectedIndex >= 0)
            {
                session.SetDepth(depthSelector.SelectedIndex);
            }
        };
        depthGroup.Children.Add(depthSelector);
        panel.Children.Add(depthGroup);

        faceGroup.Children.Add(new TextBlock { Text = "Cube face", Classes = { "hint" } });
        faceSelector.ItemsSource = FaceNames;
        faceSelector.SelectionChanged += (_, _) =>
        {
            if (!updatingFromState && faceSelector.SelectedIndex >= 0)
            {
                session.SetCubeFace(faceSelector.SelectedIndex);
            }
        };
        faceGroup.Children.Add(faceSelector);
        panel.Children.Add(faceGroup);

        projectionGroup.Children.Add(new TextBlock { Text = "Projection", Classes = { "hint" } });
        projectionSelector.ItemsSource = ProjectionNames;
        projectionSelector.SelectionChanged += (_, _) =>
        {
            if (!updatingFromState && projectionSelector.SelectedIndex >= 0)
            {
                session.SetProjection(projectionSelector.SelectedIndex);
            }
        };
        projectionGroup.Children.Add(projectionSelector);
        panel.Children.Add(projectionGroup);

        var channelGroup = new StackPanel { Spacing = 6 };
        channelGroup.Children.Add(new TextBlock { Text = "Channels", Classes = { "hint" } });
        channelSelector.ItemsSource = Array.ConvertAll(ChannelPresets, static preset => preset.Label);
        channelSelector.SelectedIndex = 0;
        channelSelector.SelectionChanged += (_, _) =>
        {
            var index = channelSelector.SelectedIndex;

            if (!updatingFromState && index >= 0 && index < ChannelPresets.Length)
            {
                session.SetChannels(ChannelPresets[index].Packed);
            }
        };
        channelGroup.Children.Add(channelSelector);

        channelGroup.Children.Add(new TextBlock { Text = "Split", Classes = { "hint" } });
        splitSelector.ItemsSource = new[] { "Off", "2 slices", "4 slices" };
        splitSelector.SelectedIndex = 0;
        splitSelector.SelectionChanged += (_, _) =>
        {
            if (!updatingFromState && splitSelector.SelectedIndex >= 0)
            {
                session.SetSplitMode(splitSelector.SelectedIndex);
            }
        };
        channelGroup.Children.Add(splitSelector);
        panel.Children.Add(channelGroup);

        spriteGroup.Children.Add(new TextBlock { Text = "Sprite frame", Classes = { "hint" } });
        spriteSelector.SelectionChanged += (_, _) =>
        {
            if (!updatingFromState)
            {
                // Index 0 is the whole sheet, so the frame number is one less
                session.SetSpriteFrame(spriteSelector.SelectedIndex - 1);
            }
        };
        spriteGroup.Children.Add(spriteSelector);
        spriteCrop.IsCheckedChanged += (_, _) =>
        {
            if (!updatingFromState)
            {
                session.SetSpriteCrop(spriteCrop.IsChecked == true);
            }
        };
        spriteGroup.Children.Add(spriteCrop);
        panel.Children.Add(spriteGroup);

        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    private void UpdateFromState()
    {
        var state = session.GetState();

        updatingFromState = true;

        try
        {
            mipGroup.IsVisible = state.HasMips;
            depthGroup.IsVisible = state.HasDepth;
            faceGroup.IsVisible = state.HasCubeFaces;
            projectionGroup.IsVisible = state.HasCubeFaces;
            spriteGroup.IsVisible = state.HasSpriteFrames;

            if (populatedMipCount != state.MipCount)
            {
                populatedMipCount = state.MipCount;
                mipSelector.ItemsSource = BuildLabels("Mip", state.MipCount);
                mipSelector.SelectedIndex = state.Mip;
            }
            else if (mipSelector.SelectedIndex != state.Mip)
            {
                mipSelector.SelectedIndex = state.Mip;
            }

            if (populatedDepthCount != state.DepthCount)
            {
                populatedDepthCount = state.DepthCount;
                depthSelector.ItemsSource = BuildLabels("Slice", state.DepthCount);
                depthSelector.SelectedIndex = state.Depth;
            }
            else if (depthSelector.SelectedIndex != state.Depth)
            {
                depthSelector.SelectedIndex = state.Depth;
            }

            if (faceSelector.SelectedIndex != state.CubeFace)
            {
                faceSelector.SelectedIndex = state.CubeFace;
            }

            if (projectionSelector.SelectedIndex != state.Projection)
            {
                projectionSelector.SelectedIndex = state.Projection;
            }

            if (channelSelector.SelectedIndex < 0)
            {
                channelSelector.SelectedIndex = 0;
            }

            if (splitSelector.SelectedIndex != state.SplitMode)
            {
                splitSelector.SelectedIndex = state.SplitMode;
            }

            if (populatedSpriteCount != state.SpriteFrameCount)
            {
                populatedSpriteCount = state.SpriteFrameCount;

                var labels = new string[state.SpriteFrameCount + 1];
                labels[0] = "Whole sheet";

                for (var i = 0; i < state.SpriteFrameCount; i++)
                {
                    labels[i + 1] = $"Frame {i}";
                }

                spriteSelector.ItemsSource = labels;
                spriteSelector.SelectedIndex = state.SpriteFrame + 1;
            }
            else if (spriteSelector.SelectedIndex != state.SpriteFrame + 1)
            {
                spriteSelector.SelectedIndex = state.SpriteFrame + 1;
            }

            if (spriteCrop.IsChecked != state.SpriteCrop)
            {
                spriteCrop.IsChecked = state.SpriteCrop;
            }
        }
        finally
        {
            updatingFromState = false;
        }
    }

    private static string[] BuildLabels(string prefix, int count)
    {
        var labels = new string[count];

        for (var i = 0; i < count; i++)
        {
            labels[i] = $"{prefix} {i}";
        }

        return labels;
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
