using System.Threading.Tasks;
using GUI.Platform;

namespace GUI.Utils;

public static class AppMessageDialogs
{
    public static Task ShowMessageAsync(string message, string title, MessageIcon icon = MessageIcon.Info)
        => PlatformServices.Current.MessageDialogs.ShowMessageAsync(message, title, icon);

    public static Task<bool> ConfirmAsync(string message, string title, MessageIcon icon = MessageIcon.Question, ConfirmButtons buttons = ConfirmButtons.OkCancel)
        => PlatformServices.Current.MessageDialogs.ConfirmAsync(message, title, icon, buttons);
}
