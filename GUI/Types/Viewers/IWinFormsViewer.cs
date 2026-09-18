using System.Windows.Forms;

namespace GUI.Types.Viewers;

/// <summary>
/// Windows-only viewer that builds its own WinForms UI instead of describing content with
/// <see cref="ViewerContent"/>. The portable <see cref="IViewer"/> contract stays WinForms-free.
/// </summary>
internal interface IWinFormsViewer : IViewer
{
    void Create(TabPage containerTabPage);
}
