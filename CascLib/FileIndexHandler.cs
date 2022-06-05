using System;
using System.Collections.Generic;
using System.IO;

namespace CASCLib
{
    public class FileIndexHandler
    {
        private const int CHUNK_SIZE = 4096;
        private HashSet<MD5Hash> FileIndexData = new HashSet<MD5Hash>(MD5HashComparer9.Instance);
        private CASCConfig config;

        public ISet<MD5Hash> Data => FileIndexData;
        public int Count => FileIndexData.Count;

        public FileIndexHandler(CASCConfig cascConfig)
        {
            config = cascConfig;

            if (config.OnlineMode)
                DownloadIndexFile(config.FileIndex);
            else
                OpenIndexFile(config.FileIndex);
        }

        private void ParseIndex(Stream stream)
        {
            using (var br = new BinaryReader(stream))
            {
                stream.Seek(-20, SeekOrigin.End);

                byte version = br.ReadByte();

                if (version != 1)
                    throw new InvalidDataException("ParseIndex -> version");

                byte unk1 = br.ReadByte();

                if (unk1 != 0)
                    throw new InvalidDataException("ParseIndex -> unk1");

                byte unk2 = br.ReadByte();

                if (unk2 != 0)
                    throw new InvalidDataException("ParseIndex -> unk2");

                byte blockSizeKb = br.ReadByte();

                if (blockSizeKb != 4)
                    throw new InvalidDataException("ParseIndex -> blockSizeKb");

                byte offsetBytes = br.ReadByte();

                if (offsetBytes != 0)
                    throw new InvalidDataException("ParseIndex -> offsetBytes");

                byte sizeBytes = br.ReadByte();

                if (sizeBytes != 4)
                    throw new InvalidDataException("ParseIndex -> sizeBytes");

                byte keySizeBytes = br.ReadByte();

                if (keySizeBytes != 16)
                    throw new InvalidDataException("ParseIndex -> keySizeBytes");

                byte checksumSize = br.ReadByte();

                if (checksumSize != 8)
                    throw new InvalidDataException("ParseIndex -> checksumSize");

                int numElements = br.ReadInt32();

                if (numElements * (keySizeBytes + sizeBytes + offsetBytes) > stream.Length)
                    throw new Exception("ParseIndex failed");

                stream.Seek(0, SeekOrigin.Begin);

                for (int i = 0; i < numElements; i++)
                {
                    MD5Hash key = br.Read<MD5Hash>();

                    FileIndexData.Add(key);

                    // each chunk is 4096 bytes, and zero padding at the end
                    long remaining = CHUNK_SIZE - (stream.Position % CHUNK_SIZE);

                    // skip padding
                    if (remaining < 16 + 4)
                    {
                        stream.Position += remaining;
                    }
                }
            }
        }

        private void DownloadIndexFile(string archive)
        {
            try
            {
                string file = Utils.MakeCDNPath(config.CDNPath, "data", archive + ".index");

                Stream stream = CDNCache.Instance.OpenFile(file);

                if (stream != null)
                {
                    using (stream)
                        ParseIndex(stream);
                }
                else
                {
                    string url = Utils.MakeCDNUrl(config.CDNHost, file);

                    using (var fs = OpenFile(url))
                        ParseIndex(fs);
                }
            }
            catch (Exception exc)
            {
                throw new Exception($"DownloadIndexFile failed: {archive} - {exc}");
            }
        }

        private void OpenIndexFile(string archive)
        {
            try
            {
                string dataFolder = CASCGame.GetDataFolder(config.GameType);

                string path = Path.Combine(config.BasePath, dataFolder, "indices", archive + ".index");

                if (File.Exists(path))
                {
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        ParseIndex(fs);
                }
                else
                {
                    DownloadIndexFile(archive);
                }
            }
            catch (Exception exc)
            {
                throw new Exception($"OpenIndexFile failed: {archive} - {exc}");
            }
        }

        private Stream OpenFile(string url)
        {
            using (var resp = Utils.HttpWebResponseGet(url))
            using (Stream stream = resp.GetResponseStream())
            {
                return stream.CopyToMemoryStream(resp.ContentLength);
            }
        }

        public bool GetFullEKey(in MD5Hash eKey, out MD5Hash fullEKey)
        {
#if NETSTANDARD2_0
            if (FileIndexData.Contains(eKey))
            {
                var comparer = MD5HashComparer9.Instance;
                foreach (MD5Hash hash in FileIndexData)
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
            return FileIndexData.TryGetValue(eKey, out fullEKey);
#endif
        }
    }
}
