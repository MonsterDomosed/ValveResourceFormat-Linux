using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace GUI.Types.Viewers
{
    public sealed class BinaryKeyValues2(IViewerContext vrfGuiContext) : IViewer, IDisposable
    {
        public const int MAGIC = 757932348; // "<!--"

        private string? text;

        public static bool IsAccepted(uint magic, string fileName)
        {
            return magic == MAGIC && (fileName.EndsWith(".dmx", StringComparison.OrdinalIgnoreCase) ||
                                      fileName.EndsWith(".vmap", StringComparison.OrdinalIgnoreCase));
        }

        public async Task LoadAsync(Stream? stream)
        {
            Stream dmStream;
            Datamodel.Datamodel dm;

            if (stream != null)
            {
                dmStream = stream;
            }
            else
            {
                dmStream = File.OpenRead(vrfGuiContext.FileName!);
            }

            try
            {
                dm = Datamodel.Datamodel.Load(dmStream, Datamodel.Codecs.DeferredMode.Disabled);
            }
            finally
            {
                dmStream.Close();
            }

            using var ms = new MemoryStream();
            using var reader = new StreamReader(ms);

            dm.Save(ms, "keyvalues2", 4);

            ms.Seek(0, SeekOrigin.Begin);

            text = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        public ViewerContent GetContent()
        {
            Debug.Assert(text is not null);

            var content = new ViewerContent.Text(text);

            text = null;

            return content;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
