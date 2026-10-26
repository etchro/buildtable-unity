using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BuildtableSdk.Tests
{
    /// <summary>
    /// A local stand-in for the Buildtable API, so delivery tests run the real
    /// UnityWebRequest path against a server whose answers they choose.
    /// </summary>
    public sealed class FakeBuildtableServer : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private int _bugRequests;

        /// <summary>Status code returned for <c>POST /api/v1/bugs</c>.</summary>
        public volatile int BugStatus = 201;

        public string Url { get; }
        public int BugRequests => Volatile.Read(ref _bugRequests);

        public FakeBuildtableServer()
        {
            Url = $"http://127.0.0.1:{FreePort()}";
            _listener.Prefixes.Add(Url + "/");
            _listener.Start();
            Task.Run(Serve);
        }

        public void Dispose()
        {
            try { _listener.Stop(); _listener.Close(); }
            catch (ObjectDisposedException) { }
        }

        private async Task Serve()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }

                var path = context.Request.Url.AbsolutePath;
                int status;
                string body;

                if (path.EndsWith("/api/v1/bugs"))
                {
                    Interlocked.Increment(ref _bugRequests);
                    status = BugStatus;
                    body = status < 300
                        ? "{\"data\":{\"id\":\"bug_test\",\"status\":\"open\"}}"
                        : "{\"error\":{\"code\":\"test\",\"message\":\"Fake server said " + status + "\"}}";
                }
                else if (path.EndsWith("/api/v1/events"))
                {
                    status = 200;
                    body = "{\"data\":{\"accepted\":0}}";
                }
                else
                {
                    status = 404;
                    body = "{}";
                }

                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                context.Response.Close();
            }
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }
    }
}
