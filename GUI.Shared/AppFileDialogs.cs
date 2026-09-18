using GUI.Platform;

namespace GUI.Utils;

// File and folder picker wrapper
public static class AppFileDialogs
{
    public enum RememberIn
    {
        None,
        OpenDirectory,
        SaveDirectory,
    }

    // updateRemembered: set to false when the caller validates the picked path first and
    // wants to remember the directory only when the pick is actually accepted.
    public static string? PickFolder(string? title, RememberIn remember = RememberIn.None, bool updateRemembered = true)
        => PlatformServices.Current.FileDialogs.PickFolder(title, Map(remember), updateRemembered);

    public static string? OpenFile(string? title, string? filter, RememberIn remember = RememberIn.OpenDirectory, bool updateRemembered = true)
        => PlatformServices.Current.FileDialogs.OpenFile(title, filter, Map(remember), updateRemembered);

    public static string[]? OpenFiles(string? title, string? filter, RememberIn remember = RememberIn.OpenDirectory, bool updateRemembered = true)
        => PlatformServices.Current.FileDialogs.OpenFiles(title, filter, Map(remember), updateRemembered);

    public static string? SaveFile(string title, string? defaultFileName, string? defaultExtension, string filter, RememberIn remember = RememberIn.SaveDirectory)
        => PlatformServices.Current.FileDialogs.SaveFile(title, defaultFileName, defaultExtension, filter, Map(remember));

    // selectedFilterIndex is 1 based, 0 means user canceled
    public static string? SaveFile(string title, string? defaultFileName, string? defaultExtension, string filter, out int selectedFilterIndex, RememberIn remember = RememberIn.SaveDirectory)
        => PlatformServices.Current.FileDialogs.SaveFile(title, defaultFileName, defaultExtension, filter, out selectedFilterIndex, Map(remember));

    private static FileDialogRemember Map(RememberIn remember) => remember switch
    {
        RememberIn.OpenDirectory => FileDialogRemember.OpenDirectory,
        RememberIn.SaveDirectory => FileDialogRemember.SaveDirectory,
        _ => FileDialogRemember.None,
    };
}
