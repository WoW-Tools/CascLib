using MimeKit;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace CASCLib
{
    public enum ProductInfoType
    {
        Versions, Cdns, Bgdl
    }

    public sealed class RibbitClient : IDisposable
    {
        private const string ribbitHost = ".version.battle.net";
        private readonly int version;
        private readonly string region;
        private readonly TcpClient client;

        public RibbitClient(string region, int version = 2)
        {
            this.region = region ?? throw new ArgumentNullException(nameof(region));
            this.version = version;

            if (version == 1)
                client = new TcpClient(region + ribbitHost, 1119);
        }

        public string Get(string request)
        {
            switch (version)
            {
                case 1:
                    using (var stream = client.GetStream())
                    {
                        byte[] req = Encoding.ASCII.GetBytes(request + "\r\n");

                        stream.Write(req, 0, req.Length);

                        var message = MimeMessage.Load(stream);

                        return message.TextBody;
                    }
                case 2:
                    return GetV2Https(request);
                default:
                    throw new InvalidOperationException($"unsupported protocol version {version}");
            }
        }

        private string GetV2Https(string request)
        {
            // v2 lives at: https://{region}.version.battle.net/{request}
            var url = $"https://{region}{ribbitHost}/{request}";

            using var handler = new HttpClientHandler();
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using var http = new HttpClient(handler);
            http.Timeout = TimeSpan.FromSeconds(30);

            http.DefaultRequestHeaders.UserAgent.ParseAdd("CASCLib.RibbitClient/2");

            var resp = http.GetAsync(url).GetAwaiter().GetResult();
            resp.EnsureSuccessStatusCode();
            return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        private string MakeRequestString(string product, ProductInfoType infoType)
        {
            return infoType switch
            {
                ProductInfoType.Versions => $"v{version}/products/{product}/versions",
                ProductInfoType.Cdns => $"v{version}/products/{product}/cdns",
                ProductInfoType.Bgdl => $"v{version}/products/{product}/bgdl",
                _ => throw new InvalidOperationException()
            };
        }

        public string GetProductInfo(string product, ProductInfoType infoType)
        {
            return Get(MakeRequestString(product, infoType));
        }

        public Stream GetAsStream(string request)
        {
            return new MemoryStream(Encoding.ASCII.GetBytes(Get(request)));
        }

        public Stream GetProductInfoStream(string product, ProductInfoType infoType)
        {
            return GetAsStream(MakeRequestString(product, infoType));
        }

        public void Dispose()
        {
            client?.Dispose();
        }
    }
}
