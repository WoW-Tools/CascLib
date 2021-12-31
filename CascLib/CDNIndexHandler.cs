using System;
using System.Collections.Generic;
using System.IO;
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

    public class CDNIndexHandler
    {
        private const int CHUNK_SIZE = 4096;
        private Dictionary<MD5Hash, IndexEntry> CDNIndexData = new Dictionary<MD5Hash, IndexEntry>(MD5HashComparer.Instance);
        private CASCConfig config;
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

        private void ParseIndex(Stream stream, int dataIndex)
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

                if (offsetBytes != 4)
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

        private void DownloadIndexFile(string archive, int i)
        {
            try
            {
                string file = config.CDNPath + "/data/" + archive.Substring(0, 2) + "/" + archive.Substring(2, 2) + "/" + archive + ".index";

                Stream stream = CDNCache.Instance.OpenFile(file, false);

                if (stream != null)
                {
                    ParseIndex(stream, i);
                }
                else
                {
                    string url = "http://" + config.CDNHost + "/" + file;

                    using (var fs = OpenFile(url))
                        ParseIndex(fs, i);
                }
            }
            catch (Exception exc)
            {
                throw new Exception($"DownloadFile failed: {archive} - {exc}");
            }
        }

        private void OpenIndexFile(string archive, int i)
        {
            try
            {
                string dataFolder = CASCGame.GetDataFolder(config.GameType);

                string path = Path.Combine(config.BasePath, dataFolder, "indices", archive + ".index");

                if (File.Exists(path))
                {
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        ParseIndex(fs, i);
                }
                else
                {
                    DownloadIndexFile(archive, i);
                }
            }
            catch (Exception exc)
            {
                throw new Exception($"OpenFile failed: {archive} - {exc}");
            }
        }

        public Stream OpenDataFile(IndexEntry entry, int numRetries = 0)
        {
            var archive = config.Archives[entry.Index];

            if (numRetries >= 5)
            {
                Logger.WriteLine("CDNIndexHandler: too many tries to download file {0}", archive);
                return null;
            }

            string file = config.CDNPath + "/data/" + archive.Substring(0, 2) + "/" + archive.Substring(2, 2) + "/" + archive;

            Stream stream = CDNCache.Instance.OpenFile(file, true);

            if (stream != null)
            {
                stream.Position = entry.Offset;
                MemoryStream ms = new MemoryStream(entry.Size);
                stream.CopyBytes(ms, entry.Size);
                ms.Position = 0;
                return ms;
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

            string url = "http://" + config.CDNHost + "/" + file;

            HttpWebRequest req = WebRequest.CreateHttp(url);
            req.ReadWriteTimeout = 15000;
            //req.Headers[HttpRequestHeader.Range] = string.Format("bytes={0}-{1}", entry.Offset, entry.Offset + entry.Size - 1);
            req.AddRange(entry.Offset, entry.Offset + entry.Size - 1);

            HttpWebResponse resp;

            try
            {
                using (resp = (HttpWebResponse)req.GetResponse())
                using (Stream rstream = resp.GetResponseStream())
                {
                    MemoryStream ms = new MemoryStream(entry.Size);
                    rstream.CopyBytes(ms, entry.Size);
                    ms.Position = 0;
                    return ms;
                }
            }
            catch (WebException exc)
            {
                resp = (HttpWebResponse)exc.Response;

                if (exc.Status == WebExceptionStatus.ProtocolError && (resp.StatusCode == HttpStatusCode.NotFound || resp.StatusCode == (HttpStatusCode)429))
                {
                    return OpenDataFile(entry, numRetries + 1);
                }
                else
                {
                    Logger.WriteLine($"CDNIndexHandler: error while opening {url}: Status {exc.Status}, StatusCode {resp?.StatusCode}");
                    return null;
                }
            }
        }

        public Stream OpenDataFileDirect(MD5Hash key)
        {
            var keyStr = key.ToHexString().ToLower();

            worker?.ReportProgress(0, string.Format("Downloading \"{0}\" file...", keyStr));

            string file = config.CDNPath + "/data/" + keyStr.Substring(0, 2) + "/" + keyStr.Substring(2, 2) + "/" + keyStr;

            Stream stream = CDNCache.Instance.OpenFile(file, false);

            if (stream != null)
            {
                stream.Position = 0;
                MemoryStream ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                return ms;
            }

            string url = "http://" + config.CDNHost + "/" + file;

            return OpenFile(url);
        }

        public static Stream OpenConfigFileDirect(CASCConfig cfg, string key)
        {
            string file = cfg.CDNPath + "/config/" + key.Substring(0, 2) + "/" + key.Substring(2, 2) + "/" + key;

            Stream stream = CDNCache.Instance.OpenFile(file, false);

            if (stream != null)
                return stream;

            string url = "http://" + cfg.CDNHost + "/" + file;

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

            HttpWebRequest req = WebRequest.CreateHttp(url);
            //long fileSize = GetFileSize(url);
            //req.AddRange(0, fileSize - 1);
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream stream = resp.GetResponseStream())
            {
                MemoryStream ms = new MemoryStream();
                stream.CopyToStream(ms, resp.ContentLength);
                ms.Position = 0;
                return ms;
            }
        }

        private Stream OpenFile(string url)
        {
            HttpWebRequest req = WebRequest.CreateHttp(url);
            req.ReadWriteTimeout = 15000;
            //long fileSize = GetFileSize(url);
            //req.AddRange(0, fileSize - 1);
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream stream = resp.GetResponseStream())
            {
                MemoryStream ms = new MemoryStream();
                stream.CopyToStream(ms, resp.ContentLength, worker);
                ms.Position = 0;
                return ms;
            }
        }

        private static long GetFileSize(string url)
        {
            HttpWebRequest request = WebRequest.CreateHttp(url);
            request.Method = "HEAD";

            using (HttpWebResponse resp = (HttpWebResponse)request.GetResponse())
            {
                return resp.ContentLength;
            }
        }

        public IndexEntry GetIndexInfo(MD5Hash key)
        {
            if (!CDNIndexData.TryGetValue(key, out IndexEntry result))
                Logger.WriteLine("CDNIndexHandler: missing index: {0}", key.ToHexString());

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
