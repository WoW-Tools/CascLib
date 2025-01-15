using System;
using System.Collections.Generic;
using System.IO;

namespace CASCLib
{
    public abstract class IndexHandlerBase
    {
        protected const int CHUNK_SIZE = 4096;
        protected CASCConfig config;

        protected Dictionary<MD5Hash, IndexEntry> indexData = new Dictionary<MD5Hash, IndexEntry>(MD5HashComparer9.Instance);

        // Index footer fields
        protected byte version;
        protected byte offsetBytes;
        protected byte sizeBytes;
        protected byte keySizeBytes;
        protected int numBlocks;

        public IReadOnlyDictionary<MD5Hash, IndexEntry> Data => indexData;
        public int Count => indexData.Count;

        protected void ParseIndex(Stream stream, int dataIndex)
        {
            using (var br = new BinaryReader(stream))
            {
                ParseIndexFooter(br);
                ParseIndexBlocks(br, dataIndex);
            }
        }

        protected void ParseIndexFooter(BinaryReader br)
        {
            Stream stream = br.BaseStream;

            stream.Seek(-20, SeekOrigin.End);

            version = br.ReadByte();

            if (version != 1)
                throw new InvalidDataException($"Unsupported CDN index version: {version}. This client only supports versions <= {1}");

            byte unk1 = br.ReadByte();

            if (unk1 != 0)
                throw new InvalidDataException("ParseIndexFooter -> unk1");

            byte unk2 = br.ReadByte();

            if (unk2 != 0)
                throw new InvalidDataException("ParseIndexFooter -> unk2");

            byte blockSizeKb = br.ReadByte();

            if (blockSizeKb != 4)
                throw new InvalidDataException("ParseIndexFooter -> blockSizeKb");

            offsetBytes = br.ReadByte();

            if (offsetBytes != 0 && offsetBytes != 4 && offsetBytes != 5 && offsetBytes != 6)
                throw new InvalidDataException("ParseIndexFooter -> offsetBytes");

            sizeBytes = br.ReadByte();

            if (sizeBytes != 4)
                throw new InvalidDataException("ParseIndexFooter -> sizeBytes");

            keySizeBytes = br.ReadByte();

            if (keySizeBytes != 16)
                throw new InvalidDataException("ParseIndexFooter -> keySizeBytes");

            byte hashSize = br.ReadByte();

            if (hashSize != 8)
                throw new InvalidDataException("ParseIndexFooter -> hashSize");

            numBlocks = br.ReadInt32();

            if (numBlocks * (keySizeBytes + sizeBytes + offsetBytes) > stream.Length)
                throw new Exception("ParseIndexFooter -> not enough data");
        }

        protected virtual void ParseIndexBlocks(BinaryReader br, int dataIndex)
        {
            Stream stream = br.BaseStream;

            stream.Seek(0, SeekOrigin.Begin);

            for (int i = 0; i < numBlocks; i++)
            {
                (MD5Hash key, IndexEntry entry) = ParseIndexEntry(br, dataIndex);

                indexData.Add(key, entry);

                // each chunk is 4096 bytes, and zero padding at the end
                long remaining = CHUNK_SIZE - (stream.Position % CHUNK_SIZE);

                // skip padding
                if (remaining < keySizeBytes + sizeBytes + offsetBytes)
                {
                    stream.Position += remaining;
                }
            }
        }

        protected (MD5Hash, IndexEntry) ParseIndexEntry(BinaryReader br, int dataIndex)
        {
            IndexEntry entry = new IndexEntry();

            MD5Hash key;

            switch (keySizeBytes)
            {
                case 16:
                    key = br.Read<MD5Hash>();
                    break;
                default:
                    throw new Exception($"ParseIndex -> unhandled keySizeBytes {keySizeBytes}");
            }

            switch (sizeBytes)
            {
                case 4:
                    entry.Size = br.ReadInt32BE();
                    break;
                default:
                    throw new Exception($"ParseIndex -> unhandled sizeBytes {sizeBytes}");
            }

            switch (offsetBytes)
            {
                case 0: // file-index
                    break;
                case 4:
                    entry.Index = dataIndex;
                    entry.Offset = br.ReadInt32BE();
                    break;
                case 5:
                    entry.Index = br.ReadByte();
                    entry.Offset = br.ReadInt32BE();
                    break;
                case 6:
                    entry.Index = br.ReadInt16BE();
                    entry.Offset = br.ReadInt32BE();
                    break;
                default:
                    throw new Exception($"ParseIndex -> unhandled offsetBytes {offsetBytes}");
            }

            return (key, entry);
        }

        protected void DownloadIndexFile(string archive, int dataIndex)
        {
            try
            {
                string file = Utils.MakeCDNPath(config.CDNPath, "data", archive + ".index");

                Stream stream = CDNCache.Instance.OpenFile(file);

                if (stream != null)
                {
                    using (stream)
                        ParseIndex(stream, dataIndex);
                }
                else
                {
                    string url = Utils.MakeCDNUrl(config.CDNHost, file);

                    using (var fs = OpenFile(url))
                        ParseIndex(fs, dataIndex);
                }
            }
            catch (Exception exc)
            {
                throw new Exception($"DownloadIndexFile failed: {archive} - {exc}");
            }
        }

        protected void OpenIndexFile(string archive, int dataIndex)
        {
            try
            {
                string dataFolder = CASCGame.GetDataFolder(config.GameType);

                string path = Path.Combine(config.BasePath, dataFolder, "indices", archive + ".index");

                if (File.Exists(path))
                {
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        ParseIndex(fs, dataIndex);
                }
                else
                {
                    DownloadIndexFile(archive, dataIndex);
                }
            }
            catch (Exception exc)
            {
                throw new Exception($"OpenIndexFile failed: {archive} - {exc}");
            }
        }

        protected Stream OpenFile(string url)
        {
            using (var resp = Utils.HttpWebResponseGet(url))
            using (Stream stream = resp.GetResponseStream())
            {
                return stream.CopyToMemoryStream(resp.ContentLength);
            }
        }
    }
}
