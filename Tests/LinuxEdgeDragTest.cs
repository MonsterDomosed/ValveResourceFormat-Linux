using System.Threading.Tasks;
using Avalonia;
using GUI.Linux.GL;

namespace Tests;

/// <summary>
/// A drag that reaches the edge of the screen has nowhere left for the pointer to go. The viewer keeps
/// the drag moving in the direction it was going, so a long drag does not simply stop at the edge.
/// </summary>
public class EdgeDragTest
{
    private static readonly PixelRect Screen = new(0, 0, 1920, 1080);

    [Test]
    public async Task PointerInsideTheScreenIsNotAtAnEdge()
    {
        await Assert.That(EdgeDrag.EdgesAt(new PixelPoint(960, 540), Screen)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task PointerAgainstEachEdgeReportsIt()
    {
        await Assert.That(EdgeDrag.EdgesAt(new PixelPoint(0, 540), Screen)).IsEqualTo((-1, 0));
        await Assert.That(EdgeDrag.EdgesAt(new PixelPoint(1919, 540), Screen)).IsEqualTo((1, 0));
        await Assert.That(EdgeDrag.EdgesAt(new PixelPoint(960, 0), Screen)).IsEqualTo((0, -1));
        await Assert.That(EdgeDrag.EdgesAt(new PixelPoint(960, 1079), Screen)).IsEqualTo((0, 1));
    }

    [Test]
    public async Task CornerReportsBothEdges()
    {
        await Assert.That(EdgeDrag.EdgesAt(new PixelPoint(1919, 1079), Screen)).IsEqualTo((1, 1));
        await Assert.That(EdgeDrag.EdgesAt(new PixelPoint(0, 0), Screen)).IsEqualTo((-1, -1));
    }

    [Test]
    public async Task OffsetScreenEdgesAreRelativeToTheScreen()
    {
        var second = new PixelRect(1920, 0, 1920, 1080);

        await Assert.That(EdgeDrag.EdgesAt(new PixelPoint(1919, 540), Screen)).IsEqualTo((1, 0));
        await Assert.That(EdgeDrag.EdgesAt(new PixelPoint(1921, 540), second)).IsEqualTo((-1, 0));
        await Assert.That(EdgeDrag.EdgesAt(new PixelPoint(2880, 540), second)).IsEqualTo((0, 0));
    }

    [Test]
    public async Task OnlyTheEdgeTheDragPushesIntoKeepsMoving()
    {
        // Dragging right into the right edge keeps going
        await Assert.That(EdgeDrag.PushedInto((1, 0), (1, 0))).IsEqualTo((1, 0));

        // Sliding down along that edge must not also push right
        await Assert.That(EdgeDrag.PushedInto((1, 0), (0, 1))).IsEqualTo((0, 0));

        // Moving back out of the edge stops it
        await Assert.That(EdgeDrag.PushedInto((1, 0), (-1, 0))).IsEqualTo((0, 0));
    }

    [Test]
    public async Task StepFollowsTheDragSpeed()
    {
        var step = EdgeDrag.Step((1, 1), 1000f, 0.016f);

        await Assert.That(step.X).IsEqualTo(16f).Within(1e-4f);
        await Assert.That(step.Y).IsEqualTo(16f).Within(1e-4f);
    }

    [Test]
    public async Task StepIsCappedSoAFlickCannotSpinTheView()
    {
        var step = EdgeDrag.Step((1, 0), 100000f, 1f);

        await Assert.That(step.X).IsEqualTo(EdgeDrag.MaximumStep).Within(1e-4f);
    }
}
