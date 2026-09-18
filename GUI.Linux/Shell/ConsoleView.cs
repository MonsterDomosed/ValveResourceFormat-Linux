using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GUI.Utils;

namespace GUI.Linux.Shell;

/// <summary>
/// Avalonia console/log area. Implements <see cref="ILogSink"/> so portable code can log through
/// <see cref="Log"/> without knowing about the UI toolkit.
/// </summary>
internal sealed class ConsoleView : UserControl, ILogSink
{
    private const int MaxLines = 5000;

    private readonly record struct LogLine(DateTime Time, Log.Category Category, string Component, string Message);

    private readonly TextBox textBox;
    private readonly ConcurrentQueue<LogLine> queue = new();
    private readonly List<string> lines = [];
    private int drainScheduled;

    public ConsoleView()
    {
        textBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("monospace"),
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
            Foreground = new SolidColorBrush(Color.FromRgb(235, 235, 235)),
            BorderThickness = new Avalonia.Thickness(0),
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };

        Content = textBox;

#pragma warning disable CA2000 // Console.SetOut/SetError retain these writers for the process lifetime
        Console.SetOut(new ConsoleWriter(this, Log.Category.INFO));
        Console.SetError(new ConsoleWriter(this, Log.Category.ERROR));
#pragma warning restore CA2000

        Log.SetSink(this);
        WriteLine(Log.Category.INFO, "Console", $"- Welcome to Source 2 Viewer {AppInfo.ProductVersion}");
    }

    public void WriteLine(Log.Category category, string component, string message)
    {
        queue.Enqueue(new LogLine(DateTime.Now, category, component, message));
        ScheduleDrain();
    }

    public void Clear() => Dispatcher.UIThread.Post(() =>
    {
        queue.Clear();
        lines.Clear();
        textBox.Text = string.Empty;
    });

    private void ScheduleDrain()
    {
        if (Interlocked.CompareExchange(ref drainScheduled, 1, 0) == 0)
        {
            Dispatcher.UIThread.Post(Drain);
        }
    }

    private void Drain()
    {
        Interlocked.Exchange(ref drainScheduled, 0);

        while (queue.TryDequeue(out var line))
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"[{line.Time:HH:mm:ss.fff}] [{line.Component}] {line.Message}"));
        }

        if (lines.Count > MaxLines)
        {
            lines.RemoveRange(0, lines.Count - MaxLines);
        }

        textBox.Text = string.Join(Environment.NewLine, lines);
        textBox.CaretIndex = textBox.Text?.Length ?? 0;
    }

    private sealed class ConsoleWriter(ILogSink sink, Log.Category category) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value)
        {
            if (value is not null)
            {
                sink.WriteLine(category, "Console", value);
            }
        }
    }
}
