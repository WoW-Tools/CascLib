using System.IO;
using System.IO.MemoryMappedFiles;
using System.Net;
//using System.Net.Http;
//using System.Net.Http.Headers;

namespace CASCLib
{
    public struct IndexEntry
    {
        public int Index;
        public int Offset;
        public int Size;

        public bool IsValid => Size != 0;
    }

    public class CDNIndexHandler : IndexHandlerBase
    {
        private BackgroundWorkerEx worker;

        private CDNIndexHandler(CASCConfig cascConfig, BackgroundWorkerEx worker)
        {
            config = cascConfig;
            this.worker = worker;
        }

        public static CDNIndexHandler Initialize(CASCConfig config, BackgroundWorkerEx worker)
        {
            var handler = new CDNIndexHandler(config, worker);

            if (!config.OnlineMode && !string.IsNullOrEmpty(config.ArchiveGroup))
            {
                string groupArchivePath = Path.Combine(config.BasePath, CASCGame.GetDataFolder(config.GameType), "indices", config.ArchiveGroup + ".index");
                if (File.Exists(groupArchivePath))
                {
                    worker?.ReportProgress(0, "Loading \"CDN group index\"...");
                    handler.OpenIndexFile(config.ArchiveGroup, -1);
                    return handler;
                }
            }

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
            if (!indexData.TryGetValue(eKey, out IndexEntry result))
                Logger.WriteLine("CDNIndexHandler: missing EKey: {0}", eKey.ToHexString());

            return result;
        }

        public void Clear()
        {
            indexData.Clear();
            indexData = null;

            config = null;
            worker = null;
        }
    }
}
