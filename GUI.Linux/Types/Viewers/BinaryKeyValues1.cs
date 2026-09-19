using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Linux.Types.Viewers
{
    public sealed class BinaryKeyValues1(IViewerContext vrfGuiContext) : IViewer, IDisposable
    {
        private string? text;
        private IReadOnlyList<KvSourceSpan>? sourceMap;

        public static bool IsAccepted(uint magic)
        {
            return magic == BinaryKV1.MAGIC;
        }

        public async Task LoadAsync(Stream? stream)
        {
            Stream kvStream;
            KVDocument kv;

            if (stream != null)
            {
                kvStream = stream;
            }
            else
            {
                kvStream = File.OpenRead(vrfGuiContext.FileName!);
            }

            try
            {
                kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Binary).Deserialize(kvStream);
            }
            finally
            {
                kvStream.Close();
            }

            (text, sourceMap) = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).SerializeWithSourceMap(kv);
        }

        public ViewerContent GetContent()
        {
            Debug.Assert(text is not null);
            Debug.Assert(sourceMap is not null);

            var content = new ViewerContent.Text(text, SourceMap: sourceMap);

            text = null;
            sourceMap = null;

            return content;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
