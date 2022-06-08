using System;
using System.IO;

namespace CASCLib
{
    public abstract class IndexHandlerBase
    {
        protected const int CHUNK_SIZE = 4096;
        protected CASCConfig config;

        protected abstract void ParseIndex(Stream stream, int dataIndex);

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
