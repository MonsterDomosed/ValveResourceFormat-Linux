namespace GUI.Utils;

/// <summary>
/// Application logging. Output is forwarded to the shell's <see cref="ILogSink"/> when one is
/// registered, otherwise it falls back to the standard output stream.
/// </summary>
public static class Log
{
    public enum Category
    {
        DEBUG,
        INFO,
        WARN,
        ERROR,
    }

    private static ILogSink? sink;

    /// <summary>Registers the UI shell's log destination.</summary>
    public static void SetSink(ILogSink value)
    {
        ArgumentNullException.ThrowIfNull(value);
        sink = value;
    }

    private static void WriteToConsole(string component, string message)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{component}] {message}");
    }

    public static void Debug(string component, string message)
    {
        if (sink == null)
        {
            WriteToConsole(component, message);
            return;
        }

#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[{component}] {message}");
#endif

        sink.WriteLine(Category.DEBUG, component, message);
    }

    public static void Info(string component, string message)
    {
        if (sink == null)
        {
            WriteToConsole(component, message);
            return;
        }

        sink.WriteLine(Category.INFO, component, message);
    }

    public static void Warn(string component, string message)
    {
        if (sink == null)
        {
            WriteToConsole(component, message);
            return;
        }

        sink.WriteLine(Category.WARN, component, message);
    }

    public static void Error(string component, string message)
    {
        if (sink == null)
        {
            WriteToConsole(component, message);
            return;
        }

        sink.WriteLine(Category.ERROR, component, message);
    }

    public static void ClearConsole() => sink?.Clear();
}
