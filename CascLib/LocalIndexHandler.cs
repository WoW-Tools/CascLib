using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CASCLib
{
    public class LocalIndexHandler
    {
        private Dictionary<MD5Hash, IndexEntry> LocalIndexData = new Dictionary<MD5Hash, IndexEntry>(MD5HashComparer9.Instance);

        public int Count => LocalIndexData.Count;

        private LocalIndexHandler()
        {

        }

        public static LocalIndexHandler Initialize(CASCConfig config, BackgroundWorkerEx worker)
        {
            var handler = new LocalIndexHandler();

            var idxFiles = GetIdxFiles(config);

            if (idxFiles.Count == 0)
                throw new FileNotFoundException("idx files are missing!");

            worker?.ReportProgress(0, "Loading \"local indexes\"...");

            int idxIndex = 0;

            foreach (var idx in idxFiles)
            {
                handler.ParseIndex(idx);

                worker?.ReportProgress((int)(++idxIndex / (float)idxFiles.Count * 100));
            }

            Logger.WriteLine("LocalIndexHandler: loaded {0} indexes", handler.Count);

            return handler;
        }

        private void ParseIndex(string idx)
        {
            using (var fs = new FileStream(idx, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs))
            {
                int HeaderHashSize = br.ReadInt32();
                int HeaderHash = br.ReadInt32();
                byte[] h2 = br.ReadBytes(HeaderHashSize);

                long padPos = (8 + HeaderHashSize + 0x0F) & 0xFFFFFFF0;
                fs.Position = padPos;

                int EntriesSize = br.ReadInt32();
                int EntriesHash = br.ReadInt32();

                int numBlocks = EntriesSize / 18;

                for (int i = 0; i < numBlocks; i++)
                {
                    IndexEntry info = new IndexEntry();
                    byte[] keyBytes = br.ReadBytes(9);
                    Array.Resize(ref keyBytes, 16);

                    MD5Hash key = keyBytes.ToMD5();

                    byte indexHigh = br.ReadByte();
                    int indexLow = br.ReadInt32BE();

                    info.Index = (indexHigh << 2 | (byte)((indexLow & 0xC0000000) >> 30));
                    info.Offset = (indexLow & 0x3FFFFFFF);

                    info.Size = br.ReadInt32();

                    if (!LocalIndexData.ContainsKey(key)) // use first key
                        LocalIndexData.Add(key, info);
                }

                padPos = (EntriesSize + 0x0FFF) & 0xFFFFF000;
                fs.Position = padPos;

                //if (fs.Position != fs.Length)
                //    throw new Exception("idx file under read");
            }
        }

        private static List<string> GetIdxFiles(CASCConfig config)
        {
            List<string> latestIdx = new List<string>();

            string dataFolder = CASCGame.GetDataFolder(config.GameType);
            string dataPath = Path.Combine(dataFolder, "data");

            for (int i = 0; i < 0x10; i++)
            {
                var files = Directory.EnumerateFiles(Path.Combine(config.BasePath, dataPath), string.Format($"{i:x2}*.idx"));

                if (files.Any())
                    latestIdx.Add(files.Last());
            }

            return latestIdx;
        }

        public IndexEntry GetIndexInfo(in MD5Hash eKey)
        {
            if (!LocalIndexData.TryGetValue(eKey, out IndexEntry result))
                Logger.WriteLine("LocalIndexHandler: missing EKey: {0}", eKey.ToHexString());

            return result;
        }

        public void Clear()
        {
            LocalIndexData.Clear();
            LocalIndexData = null;
        }
    }
}
