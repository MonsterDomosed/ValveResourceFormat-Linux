using System.Threading.Tasks;
using GUI.Linux.Types.GLViewers;

namespace Tests;

/// <summary>
/// The material editor queues changes and keeps the published values in step, so the controls never
/// snap back to an old value while the renderer has not applied the change yet.
/// </summary>
public class MaterialInspectorSessionTest
{
    [Test]
    public async Task PublishesParametersInOrder()
    {
        var session = new MaterialInspectorSession();
        session.Publish(
        [
            new MaterialParameter("g_flScale", MaterialParameterKind.Float, 2f, 0, default),
            new MaterialParameter("g_vColor", MaterialParameterKind.Vector, 0f, 0, new Vector4(1f, 0f, 0f, 1f)),
        ]);

        var parameters = session.GetParameters();

        await Assert.That(parameters.Length).IsEqualTo(2);
        await Assert.That(parameters[0].Name).IsEqualTo("g_flScale");
        await Assert.That(parameters[1].Vector.X).IsEqualTo(1f);
    }

    [Test]
    public async Task ChangesAreQueuedAndTheValueReadsBackImmediately()
    {
        var session = new MaterialInspectorSession();
        session.Publish([new MaterialParameter("g_flScale", MaterialParameterKind.Float, 2f, 0, default)]);

        session.Set(new MaterialParameterChange(MaterialParameterKind.Float, "g_flScale", 5f, 0, default));

        // The UI polls the published values, which must not lag behind the queued change
        await Assert.That(session.GetParameters()[0].Float).IsEqualTo(5f);

        var changes = session.ConsumeChanges();

        await Assert.That(changes.Length).IsEqualTo(1);
        await Assert.That(changes[0].Float).IsEqualTo(5f);

        await Assert.That(session.ConsumeChanges().Length).IsEqualTo(0);
    }

    [Test]
    public async Task ChangesWakeTheViewer()
    {
        var session = new MaterialInspectorSession();
        var wakes = 0;
        session.RequestFrame = () => wakes++;

        session.Set(new MaterialParameterChange(MaterialParameterKind.Int, "g_nMode", 0f, 3, default));
        session.Set(new MaterialParameterChange(MaterialParameterKind.Vector, "g_vTint", 0f, 0, Vector4.One));

        await Assert.That(wakes).IsEqualTo(2);
        await Assert.That(session.ConsumeChanges().Length).IsEqualTo(2);
    }
}
