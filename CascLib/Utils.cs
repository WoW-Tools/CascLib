using System;
using System.IO;
using System.Net;

namespace CASCLib
{
    internal static class Utils
    {
        public static string MakeCDNPath(string cdnPath, string folder, string fileName)
        {
            return $"{cdnPath}/{folder}/{fileName.Substring(0, 2)}/{fileName.Substring(2, 2)}/{fileName}";
        }

        public static string MakeCDNPath(string cdnPath, string fileName)
        {
            return $"{cdnPath}/{fileName.Substring(0, 2)}/{fileName.Substring(2, 2)}/{fileName}";
        }

        public static string MakeCDNUrl(string cdnHost, string cdnPath)
        {
            return $"http://{cdnHost}/{cdnPath}";
        }

        private static HttpWebResponse HttpWebResponse(string url, string method = "GET", int? from = null, int? to = null, int numRetries = 0)
        {
            if (numRetries >= 5)
            {
                string message = $"Utils: HttpWebResponse for {url} failed after 5 tries";
                Logger.WriteLine(message);
                throw new Exception(message);
            }

            HttpWebRequest req = WebRequest.CreateHttp(url);
            req.Method = method;

            if (method == "GET")
            {
                req.ReadWriteTimeout = 15000;

                if (from.HasValue && to.HasValue)
                    req.AddRange(from.Value, to.Value);
            }

            HttpWebResponse resp;

            try
            {
                return (HttpWebResponse)req.GetResponse();
            }
            catch (WebException exc)
            {
                using (resp = (HttpWebResponse)exc.Response)
                {
                    if (exc.Status == WebExceptionStatus.ProtocolError && (resp.StatusCode == HttpStatusCode.NotFound || resp.StatusCode == (HttpStatusCode)429))
                    {
                        return HttpWebResponse(url, method, from, to, numRetries + 1);
                    }
                    else
                    {
                        string message = $"Utils: error at HttpWebResponse {url}: Status {exc.Status}, StatusCode {resp.StatusCode}";
                        Logger.WriteLine(message);
                        throw new Exception(message);
                    }
                }
            }
        }

        public static HttpWebResponse HttpWebResponseHead(string url)
        {
            return HttpWebResponse(url, "HEAD");
        }

        public static HttpWebResponse HttpWebResponseGet(string url)
        {
            return HttpWebResponse(url, "GET");
        }

        public static HttpWebResponse HttpWebResponseGetWithRange(string url, int from, int to)
        {
            return HttpWebResponse(url, "GET", from, to);
        }

        // copies whole stream
        public static Stream CopyToMemoryStream(this Stream src, long length, BackgroundWorkerEx worker = null)
        {
            MemoryStream ms = new MemoryStream();
            src.CopyToStream(ms, length, worker);
            ms.Position = 0;
            return ms;
        }

        // copies whole stream
        public static MemoryStream CopyToMemoryStream(this Stream src)
        {
            MemoryStream ms = new MemoryStream();
            src.CopyTo(ms);
            ms.Position = 0;
            return ms;
        }

        // copies only numBytes bytes
        public static Stream CopyBytesToMemoryStream(this Stream src, int numBytes)
        {
            MemoryStream ms = new MemoryStream(numBytes);
            src.CopyBytes(ms, numBytes);
            ms.Position = 0;
            return ms;
        }

        public static long GetFileSize(string url)
        {
            using (var resp = Utils.HttpWebResponseHead(url))
            {
                return resp.ContentLength;
            }
        }
    }
}
