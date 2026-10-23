using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine.Networking;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Collects a body through ReceiveData, as scripted handlers do.</summary>
    public class CountingHandler : DownloadHandlerScript
    {
        public int Chunks;
        public readonly List<byte> Received = new List<byte>();
        public bool Completed;

        public CountingHandler(byte[] buffer) : base(buffer) { }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            Chunks++;
            for (int i = 0; i < dataLength; i++) Received.Add(data[i]);
            return true;
        }

        protected override void CompleteContent() => Completed = true;
    }

    /// <summary>UnityWebRequest against a server on the loopback interface.</summary>
    public class UnityWebRequestTests : IDisposable
    {
        readonly HttpListener m_Listener = new HttpListener();
        readonly string m_Base;
        readonly Thread m_Thread;
        string m_LastBody = "";
        string m_LastContentType = "";

        public UnityWebRequestTests()
        {
            int port;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            m_Base = $"http://127.0.0.1:{port}/";
            m_Listener.Prefixes.Add(m_Base);
            m_Listener.Start();
            m_Thread = new Thread(Serve) { IsBackground = true };
            m_Thread.Start();
        }

        void Serve()
        {
            while (m_Listener.IsListening)
            {
                HttpListenerContext context;
                try { context = m_Listener.GetContext(); }
                catch (Exception) { return; }
                var request = context.Request;
                using (var reader = new System.IO.StreamReader(request.InputStream, Encoding.UTF8))
                    m_LastBody = reader.ReadToEnd();
                m_LastContentType = request.ContentType ?? "";
                var response = context.Response;
                byte[] body;
                switch (request.Url.AbsolutePath)
                {
                    case "/hello":
                        response.ContentType = "text/plain; charset=utf-8";
                        response.Headers["X-Test"] = "yes";
                        body = Encoding.UTF8.GetBytes("hello Ünity");
                        break;
                    case "/echo":
                        body = Encoding.UTF8.GetBytes(request.HttpMethod + " " + m_LastBody);
                        break;
                    case "/big":
                        body = new byte[10000];
                        for (int i = 0; i < body.Length; i++) body[i] = (byte)i;
                        break;
                    default:
                        response.StatusCode = 404;
                        body = Encoding.UTF8.GetBytes("missing");
                        break;
                }
                response.ContentLength64 = body.Length;
                response.OutputStream.Write(body, 0, body.Length);
                response.Close();
            }
        }

        public void Dispose()
        {
            m_Listener.Stop();
            m_Listener.Close();
        }

        static void Wait(UnityWebRequest request)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!request.isDone && DateTime.UtcNow < deadline) Thread.Sleep(5);
            Assert.True(request.isDone, "the request did not complete");
        }

        [Fact]
        public void GetReturnsTheBodyHeadersAndCode()
        {
            using var request = UnityWebRequest.Get(m_Base + "hello");
            var operation = request.SendWebRequest();
            Assert.Same(request, operation.webRequest);
            Wait(request);
            Assert.Equal(UnityWebRequest.Result.Success, request.result);
            Assert.Equal(200, request.responseCode);
            Assert.Equal("hello Ünity", request.downloadHandler.text);
            Assert.Equal("yes", request.GetResponseHeader("x-test"));
            Assert.True(operation.isDone);
            Assert.Equal(1f, request.downloadProgress);
            Assert.False(request.isModifiable);
            Assert.Throws<InvalidOperationException>(() => request.SendWebRequest());
        }

        [Fact]
        public void PostSendsFormsAndBodies()
        {
            var form = new WWWForm();
            form.AddField("name", "a b&c");
            form.AddField("count", 3);
            using (var request = UnityWebRequest.Post(m_Base + "echo", form))
            {
                request.SendWebRequest();
                Wait(request);
                Assert.Equal("POST name=a+b%26c&count=3", request.downloadHandler.text);
                Assert.StartsWith("application/x-www-form-urlencoded", m_LastContentType);
            }
            using (var request = UnityWebRequest.Post(m_Base + "echo", "{\"x\":1}", "application/json"))
            {
                request.SendWebRequest();
                Wait(request);
                Assert.Equal("POST {\"x\":1}", request.downloadHandler.text);
                Assert.StartsWith("application/json", m_LastContentType);
            }
        }

        [Fact]
        public void ErrorsAreProtocolOrConnectionErrors()
        {
            using (var request = UnityWebRequest.Get(m_Base + "nothing"))
            {
                request.SendWebRequest();
                Wait(request);
                Assert.Equal(UnityWebRequest.Result.ProtocolError, request.result);
                Assert.Equal(404, request.responseCode);
                Assert.Equal("HTTP/1.1 404 Not Found", request.error);
                Assert.Equal("missing", request.downloadHandler.text);
            }
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int closed = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            using (var request = UnityWebRequest.Get($"http://127.0.0.1:{closed}/"))
            {
                request.SendWebRequest();
                Wait(request);
                Assert.Equal(UnityWebRequest.Result.ConnectionError, request.result);
                Assert.Equal("Cannot connect to destination host", request.error);
            }
        }

        [Fact]
        public void ScriptHandlersGetTheBodyThroughTheirBuffer()
        {
            var handler = new CountingHandler(new byte[4096]);
            using var request = new UnityWebRequest(m_Base + "big", UnityWebRequest.kHttpVerbGET, handler, null);
            request.SendWebRequest();
            Wait(request);
            Assert.Equal(3, handler.Chunks);
            Assert.Equal(10000, handler.Received.Count);
            Assert.Equal((byte)(9999 % 256), handler.Received[9999]);
            Assert.True(handler.Completed);
        }

        [Fact]
        public void FileUrlsAndEscaping()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unbound-uwr-" + Guid.NewGuid().ToString("N") + ".txt");
            System.IO.File.WriteAllText(path, "from disk");
            try
            {
                using var request = UnityWebRequest.Get("file://" + path);
                request.SendWebRequest();
                Wait(request);
                Assert.Equal("from disk", request.downloadHandler.text);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
            Assert.Equal("a+b%2fc%3d", UnityWebRequest.EscapeURL("a b/c="));
            Assert.Equal("a b/c=", UnityWebRequest.UnEscapeURL("a+b%2fc%3d"));
            Assert.Equal(Encoding.UTF8.GetBytes("k=v+w"), UnityWebRequest.SerializeSimpleForm(new Dictionary<string, string> { ["k"] = "v w" }));
        }
    }
}
