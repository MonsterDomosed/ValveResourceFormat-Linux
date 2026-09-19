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

        var messageRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new GUI.Linux.UI.SvgIcon(IconName(icon), 20),
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 440,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
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
                    messageRow,
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

    private static string IconName(MessageIcon icon) => icon switch
    {
        MessageIcon.Warning => "Warning",
        MessageIcon.Error => "Error",
        MessageIcon.Question => "Question",
        _ => "Info",
    };
}
