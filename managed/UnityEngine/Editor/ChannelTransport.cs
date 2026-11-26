using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>One message of a channel connection: its bytes and whether it was sent as text.</summary>
    internal readonly struct ChannelMessage
    {
        internal readonly byte[] Data;
        internal readonly bool Text;

        internal ChannelMessage(byte[] data, bool text)
        {
            Data = data;
            Text = text;
        }
    }

    /// <summary>
    /// The channel service's server: WebSocket connections on 127.0.0.1 at a free port, a client
    /// naming its channel by the request's path (<c>ws://127.0.0.1:port/channel</c>). Messages
    /// arrive on background threads and are queued; <see cref="Dispatch"/> gives them to the
    /// channel's handlers on the main thread, as the Editor's service does on DispatchMessages and
    /// each Editor update (tests/projects/channelmessages to_server.*).
    /// </summary>
    internal static class ChannelServer
    {
        sealed class Connection
        {
            internal int Id;
            internal string Channel;
            internal WebSocket Socket;
            internal int RemotePort;
            internal readonly object SendLock = new object();
        }

        static readonly object s_Lock = new object();
        static TcpListener s_Listener;
        static int s_Port;
        static int s_NextConnection;
        static readonly Dictionary<int, Connection> s_Connections = new Dictionary<int, Connection>();
        static readonly ConcurrentQueue<(int Connection, ChannelMessage Message)> s_Incoming = new ConcurrentQueue<(int, ChannelMessage)>();

        internal static bool Running { get { lock (s_Lock) return s_Listener != null; } }

        internal static int Port { get { lock (s_Lock) return s_Listener != null ? s_Port : 0; } }

        internal static void Start()
        {
            lock (s_Lock)
            {
                if (s_Listener != null) return;
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                s_Listener = listener;
                s_Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                new Thread(() => AcceptLoop(listener)) { IsBackground = true, Name = "Unbound channel service" }.Start();
            }
        }

        internal static void Stop()
        {
            List<Connection> connections;
            lock (s_Lock)
            {
                if (s_Listener == null) return;
                s_Listener.Stop();
                s_Listener = null;
                connections = new List<Connection>(s_Connections.Values);
                s_Connections.Clear();
            }
            foreach (var connection in connections) connection.Socket.Abort();
            while (s_Incoming.TryDequeue(out _)) { }
        }

        static void AcceptLoop(TcpListener listener)
        {
            while (true)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (Exception e) when (e is SocketException || e is ObjectDisposedException || e is InvalidOperationException) { return; }
                new Thread(() => Serve(client)) { IsBackground = true, Name = "Unbound channel connection" }.Start();
            }
        }

        static void Serve(TcpClient client)
        {
            Connection connection = null;
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var (channel, accept) = Handshake(stream);
                if (channel == null) { client.Close(); return; }
                var socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = Timeout.InfiniteTimeSpan });
                // Known before the client hears the answer: an in-process client finds its connection.
                lock (s_Lock)
                {
                    connection = new Connection
                    {
                        Id = ++s_NextConnection,
                        Channel = channel,
                        Socket = socket,
                        RemotePort = ((IPEndPoint)client.Client.RemoteEndPoint).Port,
                    };
                    s_Connections.Add(connection.Id, connection);
                }
                var answer = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
                lock (connection.SendLock) stream.Write(answer, 0, answer.Length);
                foreach (var message in ChannelSockets.Receive(socket))
                    s_Incoming.Enqueue((connection.Id, message));
            }
            // A stopped service aborts its sockets: a pending receive ends with OperationCanceledException.
            catch (Exception e) when (e is IOException || e is WebSocketException || e is SocketException || e is ObjectDisposedException
                                      || e is OperationCanceledException || e is InvalidOperationException) { }
            finally
            {
                if (connection != null)
                    lock (s_Lock) s_Connections.Remove(connection.Id);
                client.Close();
            }
        }

        /// <summary>Reads a WebSocket upgrade request: the channel its path names and the accept key
        /// of the answer, or no channel (answered 400) for another request.</summary>
        static (string Channel, string Accept) Handshake(Stream stream)
        {
            var request = new StringBuilder();
            var buffer = new byte[1];
            while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (stream.Read(buffer, 0, 1) != 1 || request.Length > 16384) return (null, null);
                request.Append((char)buffer[0]);
            }
            var lines = request.ToString().Split("\r\n");
            var start = lines[0].Split(' ');
            string key = null;
            foreach (var line in lines)
            {
                int colon = line.IndexOf(':');
                if (colon > 0 && line.Substring(0, colon).Trim().Equals("Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase))
                    key = line.Substring(colon + 1).Trim();
            }
            if (start.Length < 2 || start[0] != "GET" || key == null)
            {
                var refusal = Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n");
                stream.Write(refusal, 0, refusal.Length);
                return (null, null);
            }
            return (Uri.UnescapeDataString(start[1].TrimStart('/')), AcceptKey(key));
        }

        internal static string AcceptKey(string key) =>
            Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));

        /// <summary>Gives the queued messages to their channels' handlers (main thread): each one's
        /// bytes, a text message's as UTF-8. The Editor's own <c>echo</c> channel sends each message
        /// back to its sender, as it was sent (channelmessages echo.*).</summary>
        internal static void Dispatch(Func<string, List<Action<int, byte[]>>> handlersOf)
        {
            while (s_Incoming.TryDequeue(out var item))
            {
                string channel;
                lock (s_Lock) channel = s_Connections.TryGetValue(item.Connection, out var c) ? c.Channel : null;
                if (channel == null) continue;
                if (channel == "echo") Send(item.Connection, item.Message.Data, item.Message.Text);
                var handlers = handlersOf(channel);
                if (handlers == null) continue;
                foreach (var handler in handlers.ToArray())
                {
                    try { handler(item.Connection, item.Message.Data); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
        }

        /// <summary>The connections of a channel, by id.</summary>
        internal static List<int> ConnectionsOf(string channel)
        {
            var ids = new List<int>();
            lock (s_Lock)
                foreach (var connection in s_Connections.Values)
                    if (connection.Channel == channel) ids.Add(connection.Id);
            ids.Sort();
            return ids;
        }

        /// <summary>Every connection: its id, channel and the client's local port.</summary>
        internal static List<(int Id, string Channel, int RemotePort)> Connections()
        {
            var list = new List<(int, string, int)>();
            lock (s_Lock)
                foreach (var connection in s_Connections.Values) list.Add((connection.Id, connection.Channel, connection.RemotePort));
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return list;
        }

        /// <summary>The connection of an in-process client, by the client's local port; -1 for none.</summary>
        internal static int ConnectionOfPort(int port)
        {
            lock (s_Lock)
                foreach (var connection in s_Connections.Values)
                    if (connection.RemotePort == port) return connection.Id;
            return -1;
        }

        internal static void Send(int connectionId, byte[] data, bool text)
        {
            Connection connection;
            lock (s_Lock)
                if (!s_Connections.TryGetValue(connectionId, out connection)) return;
            lock (connection.SendLock) ChannelSockets.Send(connection.Socket, data, text);
        }

        internal static void Broadcast(string channel, byte[] data, bool text)
        {
            foreach (int id in ConnectionsOf(channel)) Send(id, data, text);
        }
    }

    /// <summary>Whole messages over a WebSocket.</summary>
    internal static class ChannelSockets
    {
        /// <summary>The messages of a socket until it closes (blocking).</summary>
        internal static IEnumerable<ChannelMessage> Receive(WebSocket socket)
        {
            var buffer = new byte[65536];
            var message = new MemoryStream();
            while (socket.State == WebSocketState.Open)
            {
                var result = socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).GetAwaiter().GetResult();
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // Answer the other end's close.
                    try { socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).GetAwaiter().GetResult(); }
                    catch (Exception e) when (e is WebSocketException || e is IOException || e is ObjectDisposedException || e is OperationCanceledException) { }
                    yield break;
                }
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                yield return new ChannelMessage(message.ToArray(), result.MessageType == WebSocketMessageType.Text);
                message.SetLength(0);
            }
        }

        /// <summary>Sends one message; a closed socket drops it.</summary>
        internal static void Send(WebSocket socket, byte[] data, bool text)
        {
            if (socket.State != WebSocketState.Open) return;
            try
            {
                socket.SendAsync(new ArraySegment<byte>(data ?? Array.Empty<byte>()), text ? WebSocketMessageType.Text : WebSocketMessageType.Binary, true, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch (Exception e) when (e is WebSocketException || e is IOException || e is ObjectDisposedException
                                      || e is OperationCanceledException || e is InvalidOperationException) { }
        }
    }

    /// <summary>A channel client's connection to a channel service.</summary>
    internal sealed class ChannelClientSocket
    {
        readonly TcpClient m_Tcp;
        readonly WebSocket m_Socket;
        readonly ConcurrentQueue<ChannelMessage> m_Incoming = new ConcurrentQueue<ChannelMessage>();
        readonly object m_SendLock = new object();

        ChannelClientSocket(TcpClient tcp, WebSocket socket)
        {
            m_Tcp = tcp;
            m_Socket = socket;
            LocalPort = ((IPEndPoint)tcp.Client.LocalEndPoint).Port;
        }

        /// <summary>The port of this end: an in-process service knows the connection by it.</summary>
        internal int LocalPort { get; }

        internal bool Connected => m_Socket.State == WebSocketState.Open;

        /// <summary>Connects to the service at the port on 127.0.0.1 (blocking, at most five seconds);
        /// null when it cannot.</summary>
        internal static ChannelClientSocket Connect(int port, string channel)
        {
            var tcp = new TcpClient { NoDelay = true, ReceiveTimeout = 5000, SendTimeout = 5000 };
            try
            {
                if (!tcp.ConnectAsync(IPAddress.Loopback, port).Wait(TimeSpan.FromSeconds(5))) throw new IOException("timed out");
                var stream = tcp.GetStream();
                var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
                var request = Encoding.ASCII.GetBytes("GET /" + Uri.EscapeDataString(channel) + " HTTP/1.1\r\nHost: 127.0.0.1:" + port
                    + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: " + key + "\r\nSec-WebSocket-Version: 13\r\n\r\n");
                stream.Write(request, 0, request.Length);
                var answer = new StringBuilder();
                var buffer = new byte[1];
                while (!answer.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    if (stream.Read(buffer, 0, 1) != 1 || answer.Length > 16384) throw new IOException("no answer");
                    answer.Append((char)buffer[0]);
                }
                var text = answer.ToString();
                if (!text.StartsWith("HTTP/1.1 101", StringComparison.Ordinal) || text.IndexOf(ChannelServer.AcceptKey(key), StringComparison.Ordinal) < 0)
                    throw new IOException("refused");
                tcp.ReceiveTimeout = 0;
                tcp.SendTimeout = 0;
                var socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = false, KeepAliveInterval = Timeout.InfiniteTimeSpan });
                var client = new ChannelClientSocket(tcp, socket);
                new Thread(() =>
                {
                    try
                    {
                        foreach (var message in ChannelSockets.Receive(socket)) client.m_Incoming.Enqueue(message);
                    }
                    catch (Exception e) when (e is WebSocketException || e is IOException || e is ObjectDisposedException
                                              || e is OperationCanceledException || e is InvalidOperationException) { }
                }) { IsBackground = true, Name = "Unbound channel client" }.Start();
                return client;
            }
            catch (Exception e) when (e is IOException || e is SocketException || e is AggregateException || e is ObjectDisposedException)
            {
                tcp.Dispose();
                return null;
            }
        }

        internal void Send(byte[] data, bool text)
        {
            lock (m_SendLock) ChannelSockets.Send(m_Socket, data, text);
        }

        internal bool TryReceive(out ChannelMessage message) => m_Incoming.TryDequeue(out message);

        internal void Close()
        {
            try
            {
                // The receiving thread sees the service's answer; nothing waits for it here.
                if (m_Socket.State == WebSocketState.Open)
                    lock (m_SendLock)
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        m_Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", timeout.Token).GetAwaiter().GetResult();
                    }
            }
            catch (Exception e) when (e is WebSocketException || e is OperationCanceledException || e is IOException || e is ObjectDisposedException) { }
            m_Socket.Abort();
            m_Socket.Dispose();
            m_Tcp.Dispose();
        }
    }
}
