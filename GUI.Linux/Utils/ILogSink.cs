namespace GUI.Linux.Utils;

/// <summary>
/// Destination for application log output. Each UI shell provides an implementation that renders the
/// console/log area; portable code only talks to <see cref="Log"/>.
/// </summary>
public interface ILogSink
{
    /// <summary>Appends a single log line.</summary>
    void WriteLine(Log.Category category, string component, string message);

    /// <summary>Clears all buffered log output.</summary>
    void Clear();
}
