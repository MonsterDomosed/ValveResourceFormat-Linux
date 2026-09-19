using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GUI.Linux.Platform;
using GUI.Linux.Utils;

namespace GUI.Linux.Platform;

/// <summary>
/// Linux message and confirmation dialogs. Avalonia has no built-in message box, so this builds a
/// small modal window at runtime.
/// </summary>
internal sealed class LinuxMessageDialogs : IMessageDialogService
{
    public Task ShowMessageAsync(string message, string title, MessageIcon icon = MessageIcon.Info)
        => Dispatcher.UIThread.InvokeAsync(() => ShowCoreAsync(message, title, icon, buttons: null));

    public Task<bool> ConfirmAsync(string message, string title, MessageIcon icon = MessageIcon.Question, ConfirmButtons buttons = ConfirmButtons.OkCancel)
        => Dispatcher.UIThread.InvokeAsync(() => ShowCoreAsync(message, title, icon, buttons));

    private static async Task<bool> ShowCoreAsync(string message, string title, MessageIcon icon, ConfirmButtons? buttons)
    {
        var owner = LinuxPlatform.MainWindow;
        var accepted = false;

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = owner is { IsVisible: true }
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{IconLabel(icon)} {message}",
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 480,
                    },
                    buttonPanel,
                },
            },
        };

        var acceptButton = new Button
        {
            Content = buttons == ConfirmButtons.YesNo ? "Yes" : "OK",
            IsDefault = true,
            MinWidth = 88,
        };

        acceptButton.Click += (_, _) =>
        {
            accepted = true;
            window.Close();
        };

        buttonPanel.Children.Add(acceptButton);

        if (buttons is not null)
        {
            var rejectButton = new Button
            {
                Content = buttons == ConfirmButtons.YesNo ? "No" : "Cancel",
                IsCancel = true,
                MinWidth = 88,
            };

            rejectButton.Click += (_, _) => window.Close();
            buttonPanel.Children.Add(rejectButton);
        }

        var completion = new TaskCompletionSource<bool>();
        window.Closed += (_, _) => completion.TrySetResult(accepted);

        if (owner is { IsVisible: true })
        {
            await window.ShowDialog(owner).ConfigureAwait(true);
        }
        else
        {
            window.Show();
        }

        return await completion.Task.ConfigureAwait(true);
    }

    private static string IconLabel(MessageIcon icon) => icon switch
    {
        MessageIcon.Warning => "(!)",
        MessageIcon.Error => "(x)",
        MessageIcon.Question => "(?)",
        _ => "(i)",
    };
}
