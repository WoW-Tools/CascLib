using System.Collections.Generic;
using System.IO;

namespace CASCLib
{
    public class DummyRootHandler : RootHandlerBase
    {
        public DummyRootHandler(BinaryReader stream, BackgroundWorkerEx worker)
        {
            worker?.ReportProgress(0, "Loading \"root\"...");

            // root file is executable, skip

            worker?.ReportProgress(100);
        }

        public override IEnumerable<KeyValuePair<ulong, RootEntry>> GetAllEntries()
        {
            yield break;
        }

        public override IEnumerable<RootEntry> GetAllEntries(ulong hash)
        {
            yield break;
        }

        // Returns only entries that match current locale and content flags
        public override IEnumerable<RootEntry> GetEntries(ulong hash)
        {
            yield break;
        }

        public override void LoadListFile(string path, BackgroundWorkerEx worker = null)
        {

        }

        protected override CASCFolder CreateStorageTree()
        {
            var root = new CASCFolder("root");

            CountSelect = 0;

            // Cleanup fake names for unknown files
            CountUnknown = 0;

            Logger.WriteLine("HSRootHandler: {0} file names missing for locale {1}", CountUnknown, Locale);

            return root;
        }

        public override void Clear()
        {
            Root.Files.Clear();
            Root.Folders.Clear();
            CASCFile.Files.Clear();
        }

        public override void Dump(EncodingHandler encodingHandler = null)
        {

        }
    }
}
