using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace LogicSim.Mcp
{
    /// <summary>
    /// MCP "Streamable HTTP" transport without server-initiated streams: POST /mcp carries JSON-RPC messages
    /// and gets a JSON response. Listens on the loopback interface only and rejects foreign browser origins.
    /// </summary>
    public sealed class StreamableHttpTransport : IDisposable
    {
        private readonly Func<string, Task<string>> _handler;
        private HttpListener _listener;

        public int Port { get; }
        public string Path { get; }
        public bool IsListening => _listener != null && _listener.IsListening;
        public string Endpoint => $"http://127.0.0.1:{Port}{Path}";

        public event Action<string> Log;

        /// <param name="handler">Receives the request body and returns the response body, or null for 202 Accepted.</param>
        public StreamableHttpTransport(int port, Func<string, Task<string>> handler, string path = "/mcp")
        {
            Port = port;
            Path = path;
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void Start()
        {
            if (IsListening)
                return;
            _listener = CreateListener(includeIpPrefix: true);
            try
            {
                _listener.Start();
            }
            catch (HttpListenerException)
            {
                // Some platforms only allow the "localhost" prefix without extra permissions.
                _listener.Close();
                _listener = CreateListener(includeIpPrefix: false);
                _listener.Start();
            }
            Log?.Invoke($"MCP server listening on {Endpoint}");
            _ = AcceptLoop(_listener);
        }

        private HttpListener CreateListener(bool includeIpPrefix)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{Port}/");
            if (includeIpPrefix)
                listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            return listener;
        }

        private async Task AcceptLoop(HttpListener listener)
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception) when (!listener.IsListening)
                {
                    return;
                }
                catch (Exception exception)
                {
                    Log?.Invoke("MCP accept failed: " + exception.Message);
                    continue;
                }
                _ = Task.Run(() => ProcessAsync(context));
            }
        }

        private async Task ProcessAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            try
            {
                if (!IsAllowedOrigin(request.Headers["Origin"]))
                {
                    await Respond(response, 403, "Forbidden origin").ConfigureAwait(false);
                    return;
                }
                if (!string.Equals(request.Url.AbsolutePath.TrimEnd('/'), Path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                {
                    await Respond(response, 404, "Not found; the MCP endpoint is " + Path).ConfigureAwait(false);
                    return;
                }

                switch (request.HttpMethod)
                {
                    case "POST":
                        break;
                    case "DELETE":
                        await Respond(response, 200, null).ConfigureAwait(false);
                        return;
                    case "OPTIONS":
                        response.AddHeader("Allow", "POST, DELETE, OPTIONS");
                        await Respond(response, 204, null).ConfigureAwait(false);
                        return;
                    default:
                        // No server-initiated SSE stream.
                        response.AddHeader("Allow", "POST, DELETE, OPTIONS");
                        await Respond(response, 405, "Method not allowed").ConfigureAwait(false);
                        return;
                }

                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(body))
                {
                    await Respond(response, 400, "Empty body").ConfigureAwait(false);
                    return;
                }

                bool initialize = McpServer.IsInitializeRequest(body);
                var result = await _handler(body).ConfigureAwait(false);
                if (result == null)
                {
                    await Respond(response, 202, null).ConfigureAwait(false);
                    return;
                }
                if (initialize)
                    response.AddHeader("Mcp-Session-Id", Guid.NewGuid().ToString("N"));
                response.ContentType = "application/json";
                await Respond(response, 200, result).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Log?.Invoke("MCP request failed: " + exception.Message);
                try
                {
                    await Respond(response, 500, exception.Message).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The client went away.
                }
            }
        }

        private static bool IsAllowedOrigin(string origin)
        {
            if (string.IsNullOrEmpty(origin) || origin == "null")
                return true;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                return false;
            return uri.Host == "localhost" || uri.Host == "127.0.0.1" || uri.Host == "[::1]" || uri.Host == "::1";
        }

        private static async Task Respond(HttpListenerResponse response, int status, string body)
        {
            response.StatusCode = status;
            if (body != null)
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                if (response.ContentType == null)
                    response.ContentType = "text/plain; charset=utf-8";
                response.ContentLength64 = bytes.Length;
                await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }
            response.Close();
        }

        public void Stop()
        {
            if (_listener == null)
                return;
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch (ObjectDisposedException)
            {
            }
            _listener = null;
        }

        public void Dispose() => Stop();
    }
}
