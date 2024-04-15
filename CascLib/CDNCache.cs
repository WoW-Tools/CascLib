using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Net;

namespace CASCLib
{
    public class CacheMetaData
    {
        public long Size { get; }
        public DateTime LastModified { get; }

        public CacheMetaData(long size, DateTime lastModified)
        {
            Size = size;
            LastModified = lastModified;
        }
    }

    public class CDNCache
    {
        public static bool Enabled { get; set; } = true;
        public static bool CacheData { get; set; } = false;
        public static bool Validate { get; set; } = true;
        public static bool ValidateFast { get; set; } = true;
        public static string CachePath { get; set; } = "cache";

        private readonly Dictionary<string, MemoryMappedFile> _dataStreams = new Dictionary<string, MemoryMappedFile>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CacheMetaData> _metaData;
        private readonly CASCConfig _config;
        private static CDNCache _instance;

        public static CDNCache Instance => _instance;

        private CDNCache(CASCConfig config)
        {
            if (Enabled)
            {
                _config = config;

                string metaFile = Path.Combine(CachePath, "cache.meta");

                _metaData = new Dictionary<string, CacheMetaData>(StringComparer.OrdinalIgnoreCase);

                if (File.Exists(metaFile))
                {
                    var lines = File.ReadLines(metaFile);

                    foreach (var line in lines)
                    {
                        string[] tokens = line.Split(new[] { ' ' }, 3);
                        _metaData[tokens[0]] = new CacheMetaData(Convert.ToInt64(tokens[1]), DateTime.Parse(tokens[2]));
                    }
                }
            }
        }

        public static void Init(CASCConfig config)
        {
            _instance = new CDNCache(config);
        }

        public Stream OpenFile(string cdnPath)
        {
            if (!Enabled)
                return null;

            string file = Path.Combine(CachePath, cdnPath);

            Logger.WriteLine($"CDNCache: {file} opening...");

            Stream stream = GetFileStream(file, cdnPath);

            if (stream != null)
            {
                Logger.WriteLine($"CDNCache: {file} has been opened");
                CDNCacheStats.numFilesOpened++;
            }

            return stream;
        }

        public MemoryMappedFile OpenDataFile(string cdnPath)
        {
            if (!Enabled)
                return null;

            if (!CacheData)
                return null;

            string file = Path.Combine(CachePath, cdnPath);

            Logger.WriteLine($"CDNCache: {file} opening...");

            MemoryMappedFile stream = GetDataFile(file, cdnPath);

            if (stream != null)
            {
                Logger.WriteLine($"CDNCache: {file} has been opened");
                CDNCacheStats.numFilesOpened++;
            }

            return stream;
        }

        private Stream GetFileStream(string file, string cdnPath)
        {
            if (ValidateMeta(file, cdnPath))
                return File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            else
                return GetFileStream(file, cdnPath);
        }

        private MemoryMappedFile GetDataFile(string file, string cdnPath)
        {
            string fileName = Path.GetFileName(file);

            if (_dataStreams.TryGetValue(fileName, out MemoryMappedFile mmFile))
                return mmFile;

            if (ValidateMeta(file, cdnPath))
            {
                FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                mmFile = MemoryMappedFile.CreateFromFile(fs, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
                _dataStreams.Add(fileName, mmFile);
                return mmFile;
            }
            else
                return GetDataFile(file, cdnPath);
        }

        private bool ValidateMeta(string file, string cdnPath)
        {
            string fileName = Path.GetFileName(file);

            FileInfo fi = new FileInfo(file);

            if (!fi.Exists && !DownloadFile(cdnPath, file))
                return false;

            if (!fi.Exists)
                fi.Refresh();

            if (Validate || ValidateFast)
            {
                if (!_metaData.TryGetValue(fileName, out CacheMetaData meta))
                    meta = GetMetaData(cdnPath, fileName);

                if (meta == null)
                    throw new InvalidDataException($"unable to validate file {file}");

                bool sizeOk = fi.Length == meta.Size;
                bool dateOk = ValidateFast || fi.CreationTime == meta.LastModified;

                if (sizeOk && dateOk)
                {
                    Logger.WriteLine($"CDNCache: {file} validated, sizeOk {sizeOk}, dateOk {dateOk}, size {fi.Length}, expected size {meta.Size}");

                    return true;
                }
                else
                {
                    Logger.WriteLine($"CDNCache: {file} not validated, sizeOk {sizeOk}, dateOk {dateOk}, size {fi.Length}, expected size {meta.Size}");

                    _metaData.Remove(fileName);
                    fi.Delete();
                    return ValidateMeta(file, cdnPath);
                }
            }
            return true;
        }

        private CacheMetaData CacheFile(HttpWebResponse resp, string fileName)
        {
            var lastModifiedStr = resp.Headers[HttpResponseHeader.LastModified];

            DateTime lastModified = lastModifiedStr != null ? DateTime.Parse(lastModifiedStr) : DateTime.MinValue;
            CacheMetaData meta = new CacheMetaData(resp.ContentLength, lastModified);
            _metaData[fileName] = meta;

            using (var sw = File.AppendText(Path.Combine(CachePath, "cache.meta")))
            {
                sw.WriteLine($"{fileName} {resp.ContentLength} {lastModified}");
            }

            return meta;
        }

        public void InvalidateFile(string fileName)
        {
            fileName = fileName.ToLower();
            _metaData.Remove(fileName);

            if (_dataStreams.TryGetValue(fileName, out MemoryMappedFile stream))
                stream.Dispose();

            _dataStreams.Remove(fileName);

            string file = Utils.MakeCDNPath(_config.CDNPath, "data", fileName);

            string filePath = Path.Combine(CachePath, file);

            if (File.Exists(filePath))
                File.Delete(filePath);

            using (var sw = File.AppendText(Path.Combine(CachePath, "cache.meta")))
            {
                foreach (var meta in _metaData)
                {
                    sw.WriteLine($"{meta.Key} {meta.Value.Size} {meta.Value.LastModified}");
                }
            }
        }

        private bool DownloadFile(string cdnPath, string path)
        {
            string url = Utils.MakeCDNUrl(_config.CDNHost, cdnPath);

            Logger.WriteLine($"CDNCache: downloading file {url} to {path}");

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            //using (var client = new HttpClient())
            //{
            //    var msg = client.GetAsync(url).Result;

            //    using (Stream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            //    {
            //        //CacheMetaData.AddToCache(resp, path);
            //        //CopyToStream(stream, fs, resp.ContentLength);

            //        msg.Content.CopyToAsync(fs).Wait();
            //    }
            //}

            DateTime startTime = DateTime.Now;

            try
            {
                using (var resp = Utils.HttpWebResponseGet(url))
                {
                    CacheMetaData meta;
                    using (Stream stream = resp.GetResponseStream())
                    using (Stream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                    {
                        stream.CopyToStream(fs, resp.ContentLength);
                        meta = CacheFile(resp, Path.GetFileName(path));
                    }
                    FileInfo fileInfo = new FileInfo(path);
                    fileInfo.CreationTime = meta.LastModified;
                }
            }
            catch (WebException exc)
            {
                var resp = (HttpWebResponse)exc.Response;
                Logger.WriteLine($"CDNCache: error while downloading {url}: Status {exc.Status}, StatusCode {resp?.StatusCode}");
                return false;
            }

            TimeSpan timeSpent = DateTime.Now - startTime;
            CDNCacheStats.timeSpentDownloading += timeSpent;
            CDNCacheStats.numFilesDownloaded++;

            Logger.WriteLine($"CDNCache: {url} has been downloaded, spent {timeSpent}");

            return true;
        }

        private CacheMetaData GetMetaData(string cdnPath, string fileName)
        {
            string url = Utils.MakeCDNUrl(_config.CDNHost, cdnPath);

            try
            {
                using (var resp = Utils.HttpWebResponseHead(url))
                {
                    return CacheFile(resp, fileName);
                }
            }
            catch (WebException exc)
            {
                var resp = (HttpWebResponse)exc.Response;
                Logger.WriteLine($"CDNCache: error at GetMetaData {url}: Status {exc.Status}, StatusCode {resp.StatusCode}");
                return null;
            }
        }
    }
}
