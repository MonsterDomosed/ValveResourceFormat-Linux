using System;
using System.Collections.Generic;
using System.Threading;

namespace GUI.Linux.Types.GLViewers;

/// <summary>Which table of a render material a parameter belongs to.</summary>
internal enum MaterialParameterKind
{
    Float,
    Int,
    Vector,
}

/// <summary>One editable material parameter and its current value.</summary>
internal readonly record struct MaterialParameter(
    string Name,
    MaterialParameterKind Kind,
    float Float,
    long Int,
    Vector4 Vector);

/// <summary>A value the user changed, to be written into the material by the renderer.</summary>
internal readonly record struct MaterialParameterChange(
    MaterialParameterKind Kind,
    string Name,
    float Float,
    long Int,
    Vector4 Vector);

/// <summary>
/// Bridge between the material parameter editor and the renderer. The renderer publishes the
/// parameter tables once, the UI queues changes, and the renderer writes them into the material on
/// its next frame, which is what marks the material for re-upload.
/// </summary>
internal sealed class MaterialInspectorSession
{
    private readonly Lock sync = new();
    private readonly List<MaterialParameterChange> changes = [];

    private MaterialParameter[] parameters = [];

    /// <summary>Called after a change so an idle viewer redraws.</summary>
    public Action? RequestFrame { get; set; }

    /// <summary>The parameters and their values, sorted by name.</summary>
    public MaterialParameter[] GetParameters()
    {
        lock (sync)
        {
            return parameters;
        }
    }

    /// <summary>Publishes the loaded material's parameter tables.</summary>
    public void Publish(MaterialParameter[] values)
    {
        lock (sync)
        {
            parameters = values;
        }
    }

    /// <summary>Queues a changed value and updates what the UI reads back.</summary>
    public void Set(MaterialParameterChange change)
    {
        lock (sync)
        {
            changes.Add(change);

            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].Kind == change.Kind && string.Equals(parameters[i].Name, change.Name, StringComparison.Ordinal))
                {
                    parameters[i] = new MaterialParameter(change.Name, change.Kind, change.Float, change.Int, change.Vector);
                    break;
                }
            }
        }

        RequestFrame?.Invoke();
    }

    /// <summary>Takes the queued changes. Called on the render thread each frame.</summary>
    public MaterialParameterChange[] ConsumeChanges()
    {
        lock (sync)
        {
            if (changes.Count == 0)
            {
                return [];
            }

            var consumed = changes.ToArray();
            changes.Clear();

            return consumed;
        }
    }
}
