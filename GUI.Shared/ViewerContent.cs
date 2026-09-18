using System.Collections;
using GUI.Types.GLViewers;
using GUI.Utils;
using ValveKeyValue;

namespace GUI.Types.Viewers;

// UI agnostic description of what a viewer wants to display.
// The WinForms rendering lives in GUI.Controls.ViewerContentPresenter, the Avalonia one will live in GUI.Linux.
public abstract record ViewerContent
{
    // Plain or syntax highlighted text
    public sealed record Text(string Content, HighlightLanguage Language = HighlightLanguage.Default, IReadOnlyList<KvSourceSpan>? SourceMap = null) : ViewerContent;

    // Text that is produced on demand, rendering the exception text if producing it fails
    public sealed record LazyText(Func<string> GetContent, HighlightLanguage Language = HighlightLanguage.Default) : ViewerContent;

    // Raw bytes displayed as a hex dump
    public sealed record HexDump(byte[] Bytes) : ViewerContent;

    // A raster image encoded as bytes (PNG/JPEG/GIF, or a rasterized vector) for the shell to decode
    public sealed record EncodedImage(byte[] Bytes) : ViewerContent;

    // A table of objects, one row per item, one column per public property
    public sealed record Grid(IList Rows) : ViewerContent;

    // A GPU rendered viewport. The shell hosts a platform GL control and builds the renderer through
    // the factory once a GL context exists; the scene logic lives in GLSceneViewerCore.
    public sealed record GlViewport(Func<IGLViewportRenderer> CreateRenderer) : ViewerContent;

    // A platform-native control produced by the shell. The factory returns the shell's own control
    // type (WinForms Control / Avalonia Control); the shared model only carries it as object so this
    // project stays toolkit-agnostic. Used for interactive views such as audio playback.
    public sealed record CustomControl(Func<object> CreateControl) : ViewerContent;

    // Multiple named tabs of content
    public sealed record Tabs(IReadOnlyList<ViewerTab> Items) : ViewerContent;
}

public record ViewerTab(string Name, ViewerContent Content, bool Select = false);
