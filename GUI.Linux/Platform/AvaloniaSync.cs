using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace GUI.Linux.Platform;

/// <summary>
/// Bridges Avalonia's asynchronous, UI-thread-affine APIs to the synchronous
/// <see cref="GUI.Platform.IPlatformServices"/> contract. Dialogs and clipboard operations are
/// modal by nature, so pumping a nested dispatcher frame is safe here.
/// </summary>
internal static class AvaloniaSync
{
    /// <summary>Runs an asynchronous operation on the UI thread and waits for it to complete.</summary>
    public static void Run(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        RunCore(operation).GetAwaiter().GetResult();
    }

    /// <summary>Runs an asynchronous operation on the UI thread and returns its result.</summary>
    public static T Run<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RunCore(operation).GetAwaiter().GetResult();
    }

    private static Task RunCore(Func<Task> operation)
    {
        var dispatcher = Dispatcher.UIThread;

        if (!dispatcher.CheckAccess())
        {
            return dispatcher.InvokeAsync(operation);
        }

        var task = operation();
        PumpUntilComplete(dispatcher, task);
        return task;
    }

    private static Task<T> RunCore<T>(Func<Task<T>> operation)
    {
        var dispatcher = Dispatcher.UIThread;

        if (!dispatcher.CheckAccess())
        {
            return dispatcher.InvokeAsync(operation);
        }

        var task = operation();
        PumpUntilComplete(dispatcher, task);
        return task;
    }

    private static void PumpUntilComplete(Dispatcher dispatcher, Task task)
    {
        if (task.IsCompleted)
        {
            return;
        }

        var frame = new DispatcherFrame();

        task.ContinueWith(
            _ => dispatcher.Post(() => frame.Continue = false),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        dispatcher.PushFrame(frame);
    }
}
