using System;
using System.Threading;
using ValveResourceFormat.CompiledShader;

namespace GUI.Linux.Types.GLViewers;

/// <summary>
/// What the texture viewer is showing: a mip, a depth slice, a cube face, a channel mapping or a
/// sprite-sheet frame. A plain snapshot, because every value is absolute rather than a command.
/// </summary>
internal readonly record struct TextureInspectorState(
    int MipCount,
    int Mip,
    int DepthCount,
    int Depth,
    int CubeFaceCount,
    int CubeFace,
    uint Channels,
    int SplitMode,
    int Projection,
    int SpriteFrameCount,
    int SpriteFrame,
    bool SpriteCrop)
{
    /// <summary>Whether the texture has more than one mip level.</summary>
    public bool HasMips => MipCount > 1;

    /// <summary>Whether the texture has more than one depth slice.</summary>
    public bool HasDepth => DepthCount > 1;

    /// <summary>Whether the texture is a cubemap with selectable faces.</summary>
    public bool HasCubeFaces => CubeFaceCount > 1;

    /// <summary>Whether the texture carries a sprite sheet with more than one frame.</summary>
    public bool HasSpriteFrames => SpriteFrameCount > 1;
}

/// <summary>
/// Thread-safe bridge between the texture viewer's controls and its renderer. The controls set
/// absolute values, so the renderer only has to read the current state once per frame.
/// </summary>
internal sealed class TextureInspectorSession
{
    private readonly Lock sync = new();

    private TextureInspectorState state = new(
        MipCount: 1,
        Mip: 0,
        DepthCount: 1,
        Depth: 0,
        CubeFaceCount: 1,
        CubeFace: 0,
        Channels: ChannelMapping.RGBA.PackedValue,
        SplitMode: 0,
        Projection: 0,
        SpriteFrameCount: 0,
        SpriteFrame: -1,
        SpriteCrop: false);

    /// <summary>Called after a change so an idle viewer redraws.</summary>
    public Action? RequestFrame { get; set; }

    /// <summary>The current state, safe to read from either thread.</summary>
    public TextureInspectorState GetState()
    {
        lock (sync)
        {
            return state;
        }
    }

    /// <summary>Publishes what the loaded texture offers, clamped to one entry each.</summary>
    public void SetLimits(int mipCount, int depthCount, int cubeFaceCount, int spriteFrameCount)
    {
        Update(current =>
        {
            var mips = Math.Max(1, mipCount);
            var depth = Math.Max(1, depthCount);
            var faces = Math.Max(1, cubeFaceCount);
            var spriteFrames = Math.Max(0, spriteFrameCount);

            return current with
            {
                MipCount = mips,
                DepthCount = depth,
                CubeFaceCount = faces,
                SpriteFrameCount = spriteFrames,
                Mip = Math.Min(current.Mip, mips - 1),
                Depth = Math.Min(current.Depth, depth - 1),
                CubeFace = Math.Min(current.CubeFace, faces - 1),
                SpriteFrame = Math.Min(current.SpriteFrame, spriteFrames - 1),
            };
        });
    }

    /// <summary>Selects a mip level.</summary>
    public void SetMip(int mip) => Update(current => current with { Mip = Math.Clamp(mip, 0, current.MipCount - 1) });

    /// <summary>Selects a depth slice.</summary>
    public void SetDepth(int depth) => Update(current => current with { Depth = Math.Clamp(depth, 0, current.DepthCount - 1) });

    /// <summary>Selects a cubemap face.</summary>
    public void SetCubeFace(int face) => Update(current => current with { CubeFace = Math.Clamp(face, 0, current.CubeFaceCount - 1) });

    /// <summary>Sets the packed channel mapping.</summary>
    public void SetChannels(uint channels) => Update(current => current with { Channels = channels });

    /// <summary>Sets how many channel slices are shown: 0 (off), 2 or 4.</summary>
    public void SetSplitMode(int slices) => Update(current => current with { SplitMode = Math.Clamp(slices, 0, 2) });

    /// <summary>Sets the cubemap projection: 0 (single face), 1 (equirectangular) or 2 (cubic).</summary>
    public void SetProjection(int projection) => Update(current => current with { Projection = Math.Clamp(projection, 0, 2) });

    /// <summary>Selects a sprite-sheet frame, or <c>-1</c> to show the whole sheet.</summary>
    public void SetSpriteFrame(int frame)
        => Update(current => current with { SpriteFrame = Math.Clamp(frame, -1, current.SpriteFrameCount - 1) });

    /// <summary>Whether the sprite sheet is cropped to the selected frame.</summary>
    public void SetSpriteCrop(bool crop) => Update(current => current with { SpriteCrop = crop });

    private void Update(Func<TextureInspectorState, TextureInspectorState> change)
    {
        lock (sync)
        {
            state = change(state);
        }

        RequestFrame?.Invoke();
    }
}
