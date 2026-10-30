using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace GUI.Linux.Shell;

/// <summary>
/// Progress window shown while resources are exported or decompiled. Runs the work on a background
/// task, coalesces progress updates onto a slow timer and can be cancelled by the user.
/// </summary>
internal sealed class ExportProgressDialog : Window, IProgress<string>
{
    private readonly TextBlock statusLabel = new()
    {
        Text = "Calculating...",
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly ProgressBar progressBar = new() { IsIndeterminate = true, Minimum = 0, Maximum = 100 };
    private readonly Button cancelButton = new() { Content = "Cancel", Classes = { "tool" }, MinWidth = 88 };

    private CancellationToken cancellationToken;

    private string? pendingText;
    private int pendingBarValue = -1;
    private int pendingBarMax = -1;
    private string? baseTitle;
    private long startTimestamp;
    private bool completed;
    private Task? workCompletion;

    /// <summary>The work to run once the window is open.</summary>
    public Func<CancellationToken, Task>? OnProcess { get; set; }

    /// <summary>Whether the window stays open after the work finishes, so the result can be read.</summary>
    public bool StayOpenOnCompletion { get; init; }

    /// <summary>Time since the work started.</summary>
    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(startTimestamp);

    /// <summary>Completes when the work really stopped, which cancelling makes later than the window closing.</summary>
    public Task WorkCompletion => workCompletion ?? Task.CompletedTask;

    /// <inheritdoc/>
    public void Report(string value) => SetProgress(value);

    public ExportProgressDialog()
    {
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 704;
        Title = "Extracting files...";

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
            Margin = new Thickness(23, 17, 23, 17),
        };

        layout.Children.Add(progressBar);
        Grid.SetRow(statusLabel, 1);
        layout.Children.Add(statusLabel);
        Grid.SetRow(cancelButton, 2);
        layout.Children.Add(cancelButton);

        Content = layout;

        cancelButton.Click += (_, _) => Close();
    }

    /// <summary>Starts the work, shows the window, and returns once the window has closed.</summary>
    public async Task RunAsync(Window owner)
    {
        baseTitle = Title;
        startTimestamp = Stopwatch.GetTimestamp();

        // Show anything reported before the window existed
        ApplyPendingUpdate();

        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => ApplyPendingUpdate();
        timer.Start();

        var cancellationTokenSource = new CancellationTokenSource();
        cancellationToken = cancellationTokenSource.Token;

        workCompletion = Task.Run(async () =>
        {
            try
            {
                if (OnProcess is { } work)
                {
                    await work(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                Dispatcher.UIThread.Post(OnWorkFinished, DispatcherPriority.Background);
            }
        });

        Closed += (_, _) =>
        {
            timer.Stop();

            // Cancelling closes the window while the work winds down, so nothing may keep going
            cancellationTokenSource.Cancel();
        };

        await ShowDialog(owner).ConfigureAwait(true);

        // The window is gone, but the work may still be winding down, and its inputs must outlive it
        await WorkCompletion.ConfigureAwait(true);
        cancellationTokenSource.Dispose();
    }

    /// <summary>Stores the latest status text. Safe to call from the worker thread.</summary>
    public void SetProgress(string text) => Interlocked.Exchange(ref pendingText, text);

    /// <summary>Stores the latest progress value. Safe to call from the worker thread.</summary>
    public void SetBarValue(int value) => Interlocked.Exchange(ref pendingBarValue, value);

    /// <summary>Switches the bar to determinate. A single unit of work has no progression to show.</summary>
    public void SetBarMax(int count) => Interlocked.Exchange(ref pendingBarMax, count);

    private void ApplyPendingUpdate()
    {
        var text = Interlocked.Exchange(ref pendingText, null);

        if (text is not null)
        {
            statusLabel.Text = text;
        }

        var barMax = Interlocked.Exchange(ref pendingBarMax, -1);

        if (barMax > 1)
        {
            progressBar.IsIndeterminate = false;
            progressBar.Maximum = barMax;
        }

        var barValue = Interlocked.Exchange(ref pendingBarValue, -1);

        if (barValue >= 0)
        {
            progressBar.Value = Math.Min(barValue, progressBar.Maximum);
        }

        UpdateTitle();
    }

    private void OnWorkFinished()
    {
        // A cancelled run has nothing worth showing, it closed the window already
        if (!StayOpenOnCompletion || cancellationToken.IsCancellationRequested)
        {
            Close();
            return;
        }

        completed = true;
        ApplyPendingUpdate();
        progressBar.IsIndeterminate = false;
        progressBar.Value = progressBar.Maximum;
        cancelButton.Content = "Close";
    }

    private void UpdateTitle()
    {
        if (baseTitle is null)
        {
            return;
        }

        var elapsed = Elapsed;

        if (completed)
        {
            Title = $"{baseTitle} (completed in {FormatTime(elapsed)})";
            return;
        }

        if (progressBar.IsIndeterminate || progressBar.Value <= 0 || progressBar.Maximum <= 0)
        {
            Title = $"{baseTitle} ({FormatTime(elapsed)} elapsed)";
            return;
        }

        var remaining = elapsed * (progressBar.Maximum - progressBar.Value) / progressBar.Value;
        Title = $"{baseTitle} {progressBar.Value * 100 / progressBar.Maximum}% ({FormatTime(elapsed)} elapsed, ~{FormatTime(remaining)} left)";
    }

    /// <summary>Formats a duration the way the progress window shows it.</summary>
    public static string FormatTime(TimeSpan time) => time.TotalHours >= 1
        ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
        : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
}
