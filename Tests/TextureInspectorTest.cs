using System.Threading.Tasks;
using GUI.Linux.Types.GLViewers;
using ValveResourceFormat.CompiledShader;

namespace Tests;

/// <summary>
/// The texture inspector's values are absolute and clamped to what the loaded texture offers, so the
/// renderer can read them every frame without missing a queue.
/// </summary>
public class TextureInspectorTest
{
    [Test]
    public async Task DefaultsToEveryChannelOfMipZero()
    {
        var session = new TextureInspectorSession();
        var state = session.GetState();

        await Assert.That(state.Mip).IsEqualTo(0);
        await Assert.That(state.Depth).IsEqualTo(0);
        await Assert.That(state.CubeFace).IsEqualTo(0);
        await Assert.That(state.Channels).IsEqualTo(ChannelMapping.RGBA.PackedValue);
        await Assert.That(state.SplitMode).IsEqualTo(0);
        await Assert.That(state.SpriteFrame).IsEqualTo(-1);
    }

    [Test]
    public async Task SelectionIsClampedToTheLoadedTexture()
    {
        var session = new TextureInspectorSession();
        session.SetLimits(mipCount: 8, depthCount: 4, cubeFaceCount: 6, spriteFrameCount: 10);

        session.SetMip(99);
        session.SetDepth(99);
        session.SetCubeFace(99);
        session.SetSplitMode(99);
        session.SetProjection(99);
        session.SetSpriteFrame(99);

        var state = session.GetState();

        await Assert.That(state.Mip).IsEqualTo(7);
        await Assert.That(state.Depth).IsEqualTo(3);
        await Assert.That(state.CubeFace).IsEqualTo(5);
        await Assert.That(state.SplitMode).IsEqualTo(2);
        await Assert.That(state.Projection).IsEqualTo(2);
        await Assert.That(state.SpriteFrame).IsEqualTo(9);
    }

    [Test]
    public async Task LimitsThatShrinkPullTheSelectionBack()
    {
        var session = new TextureInspectorSession();
        session.SetLimits(mipCount: 8, depthCount: 8, cubeFaceCount: 6, spriteFrameCount: 8);
        session.SetMip(6);
        session.SetSpriteFrame(6);

        session.SetLimits(mipCount: 2, depthCount: 1, cubeFaceCount: 1, spriteFrameCount: 0);

        var state = session.GetState();

        await Assert.That(state.Mip).IsEqualTo(1);
        await Assert.That(state.Depth).IsEqualTo(0);
        await Assert.That(state.CubeFace).IsEqualTo(0);
        await Assert.That(state.SpriteFrame).IsEqualTo(-1);
        await Assert.That(state.HasMips).IsTrue();
        await Assert.That(state.HasDepth).IsFalse();
        await Assert.That(state.HasSpriteFrames).IsFalse();
    }

    [Test]
    public async Task EveryChangeWakesTheViewer()
    {
        var session = new TextureInspectorSession();
        var wakes = 0;
        session.RequestFrame = () => wakes++;

        session.SetLimits(4, 2, 6, 4);
        session.SetMip(2);
        session.SetChannels(ChannelMapping.R.PackedValue);
        session.SetSpriteCrop(true);

        await Assert.That(wakes).IsEqualTo(4);
        await Assert.That(session.GetState().Channels).IsEqualTo(ChannelMapping.R.PackedValue);
        await Assert.That(session.GetState().SpriteCrop).IsTrue();
    }
}
