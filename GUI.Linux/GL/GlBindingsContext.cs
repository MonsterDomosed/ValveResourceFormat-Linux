using System;
using Avalonia.OpenGL;

namespace GUI.Linux.GL;

/// <summary>Feeds Avalonia's GL proc loader to OpenTK so the Renderer's bindings resolve.</summary>
internal sealed class GlBindingsContext(GlInterface gl) : OpenTK.IBindingsContext
{
    public IntPtr GetProcAddress(string procName) => gl.GetProcAddress(procName);
}
