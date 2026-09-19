namespace GUI.Linux.Utils;

// Shared between the Windows and Linux shells so that callers do not need to know which UI toolkit is in use.
public enum MessageIcon
{
    Info,
    Warning,
    Error,
    Question,
}

public enum ConfirmButtons
{
    OkCancel,
    YesNo,
}
