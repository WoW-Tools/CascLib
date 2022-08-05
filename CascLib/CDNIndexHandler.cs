using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Net;
//using System.Net.Http;
//using System.Net.Http.Headers;

namespace CASCLib
{
    public class IndexEntry
    {
        public int Index;
        public int Offset;
        public int Size;
    }

    public class CDNIndexHandler : IndexHandlerBase
    {
        private Dictionary<MD5Hash, IndexEntry> CDNIndexData = new Dictionary<MD5Hash, IndexEntry>(MD5HashComparer9.Instance);
        private BackgroundWorkerEx worker;

        public IReadOnlyDictionary<MD5Hash, IndexEntry> Data => CDNIndexData;
        public int Count => CDNIndexData.Count;

        private CDNIndexHandler(CASCConfig cascConfig, BackgroundWorkerEx worker)
        {
            config = cascConfig;
            this.worker = worker;
        }

        public static CDNIndexHandler Initialize(CASCConfig config, BackgroundWorkerEx worker)
        {
            var handler = new CDNIndexHandler(config, worker);

            worker?.ReportProgress(0, "Loading \"CDN indexes\"...");

            for (int i = 0; i < config.Archives.Count; i++)
            {
                string archive = config.Archives[i];

                if (config.OnlineMode)
                    handler.DownloadIndexFile(archive, i);
                else
                    handler.OpenIndexFile(archive, i);

                worker?.ReportProgress((int)((i + 1) / (float)config.Archives.Count * 100));
            }

            return handler;
        }

        protected override void ParseIndex(Stream stream, int dataIndex)
        {
            using (var br = new BinaryReader(stream))
            {
                stream.Seek(-20, SeekOrigin.End);

                byte version = br.ReadByte();

                if (version != 1)
                    throw new InvalidDataException($"Unsupported CDN index version: {version}. This client only supports versions <= {1}");

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

                if (offsetBytes != 4)
                    throw new InvalidDataException("ParseIndex -> offsetBytes");

                byte sizeBytes = br.ReadByte();

                if (sizeBytes != 4)
                    throw new InvalidDataException("ParseIndex -> sizeBytes");

                byte keySizeBytes = br.ReadByte();

                if (keySizeBytes != 16)
                    throw new InvalidDataException("ParseIndex -> keySizeBytes");

                byte hashSize = br.ReadByte();

                if (hashSize != 8)
                    throw new InvalidDataException("ParseIndex -> hashSize");

                int numElements = br.ReadInt32();

                if (numElements * (keySizeBytes + sizeBytes + offsetBytes) > stream.Length)
                    throw new Exception("ParseIndex failed");

                stream.Seek(0, SeekOrigin.Begin);

                for (int i = 0; i < numElements; i++)
                {
                    MD5Hash key = br.Read<MD5Hash>();

                    IndexEntry entry = new IndexEntry
                    {
                        Index = dataIndex,
                        Size = br.ReadInt32BE(),
                        Offset = br.ReadInt32BE()
                    };
                    CDNIndexData.Add(key, entry);

                    // each chunk is 4096 bytes, and zero padding at the end
                    long remaining = CHUNK_SIZE - (stream.Position % CHUNK_SIZE);

                    // skip padding
                    if (remaining < 16 + 4 + 4)
                    {
                        stream.Position += remaining;
                    }
                }
            }
        }

        public Stream OpenDataFile(IndexEntry entry)
        {
            string archive = config.Archives[entry.Index];

            string file = Utils.MakeCDNPath(config.CDNPath, "data", archive);

            MemoryMappedFile dataFile = CDNCache.Instance.OpenDataFile(file);

            if (dataFile != null)
            {
                var accessor = dataFile.CreateViewStream(entry.Offset, entry.Size, MemoryMappedFileAccess.Read);
                return accessor;
            }

            //using (HttpClient client = new HttpClient())
            //{
            //    client.DefaultRequestHeaders.Range = new RangeHeaderValue(entry.Offset, entry.Offset + entry.Size - 1);

            //    var resp = client.GetStreamAsync(url).Result;

            //    MemoryStream ms = new MemoryStream(entry.Size);
            //    resp.CopyBytes(ms, entry.Size);
            //    ms.Position = 0;
            //    return ms;
            //}

            string url = Utils.MakeCDNUrl(config.CDNHost, file);

            try
            {
                using (var resp = Utils.HttpWebResponseGetWithRange(url, entry.Offset, entry.Offset + entry.Size - 1))
                using (Stream rstream = resp.GetResponseStream())
                {
                    return rstream.CopyBytesToMemoryStream(entry.Size);
                }
            }
            catch (WebException exc)
            {
                var resp = (HttpWebResponse)exc.Response;
                Logger.WriteLine($"CDNIndexHandler: error while opening {url}: Status {exc.Status}, StatusCode {resp?.StatusCode}");
                return null;
            }
        }

        public Stream OpenDataFileDirect(in MD5Hash key)
        {
            var keyStr = key.ToHexString().ToLower();

            worker?.ReportProgress(0, string.Format("Downloading \"{0}\" file...", keyStr));

            string file = Utils.MakeCDNPath(config.CDNPath, "data", keyStr);

            Stream stream = CDNCache.Instance.OpenFile(file);

            if (stream != null)
                return stream;

            string url = Utils.MakeCDNUrl(config.CDNHost, file);

            return OpenFile(url);
        }

        public static Stream OpenConfigFileDirect(CASCConfig cfg, string key)
        {
            string file = Utils.MakeCDNPath(cfg.CDNPath, "config", key);

            Stream stream = CDNCache.Instance.OpenFile(file);

            if (stream != null)
                return stream;

            string url = Utils.MakeCDNUrl(cfg.CDNHost, file);

            return OpenFileDirect(url);
        }

        public static Stream OpenFileDirect(string url)
        {
            //using (HttpClient client = new HttpClient())
            //{
            //    var resp = client.GetStreamAsync(url).Result;

            //    MemoryStream ms = new MemoryStream();
            //    resp.CopyTo(ms);
            //    ms.Position = 0;
            //    return ms;
            //}

            using (var resp = Utils.HttpWebResponseGet(url))
            using (Stream stream = resp.GetResponseStream())
            {
                return stream.CopyToMemoryStream(resp.ContentLength);
            }
        }

        public IndexEntry GetIndexInfo(in MD5Hash eKey)
        {
            if (!CDNIndexData.TryGetValue(eKey, out IndexEntry result))
                Logger.WriteLine("CDNIndexHandler: missing EKey: {0}", eKey.ToHexString());

            return result;
        }

        public void Clear()
        {
            CDNIndexData.Clear();
            CDNIndexData = null;

            config = null;
            worker = null;
        }
    }
}
