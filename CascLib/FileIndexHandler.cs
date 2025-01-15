using System.Collections.Generic;
using System.IO;

namespace CASCLib
{
    public class FileIndexHandler : IndexHandlerBase
    {
        private readonly HashSet<MD5Hash> fileIndexData = new HashSet<MD5Hash>(MD5HashComparer9.Instance);

        public FileIndexHandler(Stream stream)
        {
            ParseIndex(stream, 0);
        }

        public FileIndexHandler(CASCConfig cascConfig)
        {
            config = cascConfig;

            if (config.OnlineMode)
                DownloadIndexFile(config.FileIndex, -1);
            else
                OpenIndexFile(config.FileIndex, -1);
        }

        protected override void ParseIndexBlocks(BinaryReader br, int dataIndex)
        {
            Stream stream = br.BaseStream;

            stream.Seek(0, SeekOrigin.Begin);

            for (int i = 0; i < numBlocks; i++)
            {
                (MD5Hash key, _) = ParseIndexEntry(br, dataIndex);

                fileIndexData.Add(key);

                // each chunk is 4096 bytes, and zero padding at the end
                long remaining = CHUNK_SIZE - (stream.Position % CHUNK_SIZE);

                // skip padding
                if (remaining < keySizeBytes + sizeBytes + offsetBytes)
                {
                    stream.Position += remaining;
                }
            }
        }

        public bool GetFullEKey(in MD5Hash eKey, out MD5Hash fullEKey)
        {
#if NETSTANDARD2_0
            if (fileIndexData.Contains(eKey))
            {
                var comparer = MD5HashComparer9.Instance;
                foreach (MD5Hash hash in fileIndexData)
                {
                    if (comparer.Equals(hash, eKey))
                    {
                        fullEKey = hash;
                        return true;
                    }
                }
            }
            fullEKey = default;
            return false;
#else
            return fileIndexData.TryGetValue(eKey, out fullEKey);
#endif
        }
    }
}
