using MimeKit;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace CASCLib
{
    public sealed class RibbitClient : IDisposable
    {
        private const string ribbitHost = ".version.battle.net";

        private readonly TcpClient client = new TcpClient();

        public RibbitClient(string region)
        {
            client = new TcpClient(region + ribbitHost, 1119);
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

        public Stream GetAsStream(string request)
        {
            return new MemoryStream(Encoding.ASCII.GetBytes(Get(request)));
        }

        public void Dispose()
        {
            client.Dispose();
        }
    }
}
