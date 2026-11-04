using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using LogicSim.Mcp;
using NUnit.Framework;

namespace LogicSim.Tests
{
    public class McpHttpTransportTests
    {
        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static HttpStatusCode Post(HttpClient client, string endpoint, string token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                request.Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", Encoding.UTF8, "application/json");
                if (token != null)
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                    return response.StatusCode;
            }
        }

        [Test]
        public void Token_IsRequiredWhenSet()
        {
            using (var transport = new StreamableHttpTransport(FreePort(), body => Task.FromResult("{}")) { Token = "s3cret" })
            using (var client = new HttpClient())
            {
                transport.Start();
                Assert.That(Post(client, transport.Endpoint, null), Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(Post(client, transport.Endpoint, "wrong"), Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(Post(client, transport.Endpoint, "s3cret"), Is.EqualTo(HttpStatusCode.OK));
            }
        }

        [Test]
        public void WithoutToken_LocalClientsAreAccepted()
        {
            using (var transport = new StreamableHttpTransport(FreePort(), body => Task.FromResult("{}")))
            using (var client = new HttpClient())
            {
                transport.Start();
                Assert.That(Post(client, transport.Endpoint, null), Is.EqualTo(HttpStatusCode.OK));
            }
        }
    }
}
