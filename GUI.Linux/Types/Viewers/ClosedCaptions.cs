using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace GUI.Linux.Types.Viewers
{
    public sealed class ClosedCaptions(IViewerContext vrfGuiContext) : IViewer, IDisposable
    {
        private ValveResourceFormat.ClosedCaptions.ClosedCaptions? captions;

        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.ClosedCaptions.ClosedCaptions.MAGIC;
        }

        public async Task LoadAsync(Stream? stream)
        {
            captions = new ValveResourceFormat.ClosedCaptions.ClosedCaptions();

            if (stream != null)
            {
                captions.Read(vrfGuiContext.FileName, stream);
            }
            else
            {
                captions.Read(vrfGuiContext.FileName);
            }
        }

        public ViewerContent GetContent()
        {
            Debug.Assert(captions is not null);

            return new ViewerContent.Tabs(
            [
                new("Captions", new ViewerContent.Grid(captions.Captions)),
                new("Text", new ViewerContent.Text(captions.ToString())),
            ]);
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
