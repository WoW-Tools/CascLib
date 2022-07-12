using MimeKit;
using System;
using System.IO;
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
        private readonly TcpClient client = new TcpClient();

        public RibbitClient(string region, int version = 1)
        {
            client = new TcpClient(region + ribbitHost, 1119);
            this.version = version;
        }

        public string Get(string request)
        {
            using (NetworkStream stream = client.GetStream())
            {
                byte[] req = Encoding.ASCII.GetBytes(request + "\r\n");

                stream.Write(req, 0, req.Length);

                var message = MimeMessage.Load(stream);

                return message.TextBody;
            }
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
            client.Dispose();
        }
    }
}
