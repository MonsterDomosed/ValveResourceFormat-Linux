using ValveResourceFormat.NavMesh;

namespace GUI.Linux.GL;

/// <summary>
/// Linux navigation mesh renderer: supplies the shared scene core with a parsed nav mesh, then reuses
/// <see cref="SceneCoreGlRenderer"/> for all hosting and frame driving.
/// </summary>
internal sealed class NavMeshGlRenderer : SceneCoreGlRenderer
{
    public NavMeshGlRenderer(string fileName)
        : base("navmesh", (context, rendererContext, host) => new NavMeshSceneCore(context, rendererContext, host, ReadNavMesh(fileName)))
    {
    }

    private static NavMeshFile ReadNavMesh(string fileName)
    {
        var navMeshFile = new NavMeshFile();
        navMeshFile.Read(fileName);
        return navMeshFile;
    }
}
