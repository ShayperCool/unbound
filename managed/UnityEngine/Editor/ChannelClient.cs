using System;
using System.Collections.Generic;
using System.Text;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor.MPE
{
    /// <summary>
    /// A client of a channel of the channel service, one per channel name, as the Unity 6000.0.71f1
    /// Editor's clients of its own service behave (tests/projects/channelmessages): Start connects at
    /// once; what it sends goes out, and what the service sent reaches its handlers, on Tick (each
    /// Editor update for an auto-ticked client); each message reaches both kinds of handlers, the
    /// text ones first (the bytes as UTF-8) and then the binary ones, whether it was sent as text or
    /// binary; Close disconnects it and the next GetOrCreateClient makes another. The service is
    /// this process's, or the one whose port the command line's <c>-ump-channel-service-port</c>
    /// names (how a process MPPM starts reaches the main one; unmeasured).
    /// </summary>
    public partial class ChannelClient
    {
        internal static Dictionary<int, ChannelClient> s_Clients = new Dictionary<int, ChannelClient>();
        static int s_NextClientId;

        readonly List<Action<string>> m_TextHandlers = new List<Action<string>>();
        readonly List<Action<byte[]>> m_BinaryHandlers = new List<Action<byte[]>>();
        readonly List<ChannelMessage> m_Outgoing = new List<ChannelMessage>();
        ChannelClientSocket m_Socket;
        int m_RequestId;

        ChannelClient(string channelName, int clientId)
        {
            this.channelName = channelName;
            this.clientId = clientId;
        }

        public int clientId { get; }

        public string channelName { get; }

        public bool isAutoTick { get; private set; }

        public static ChannelClient GetOrCreateClient(string channelName)
        {
            foreach (var client in s_Clients.Values)
                if (client.channelName == channelName) return client;
            var created = new ChannelClient(channelName, ++s_NextClientId);
            s_Clients.Add(created.clientId, created);
            return created;
        }

        public bool IsConnected() => m_Socket != null && m_Socket.Connected;

        /// <summary>Connects to the service (no-op when connected) and sets whether each Editor update
        /// ticks the client.</summary>
        public void Start(bool autoTick)
        {
            isAutoTick = autoTick;
            if (IsConnected()) return;
            int port = ServicePort();
            if (port <= 0)
            {
                Debug.LogWarning($"Unbound: the channel client '{channelName}' cannot connect: no channel service runs in this process and the command line names none (-ump-channel-service-port)");
                return;
            }
            m_Socket = ChannelClientSocket.Connect(port, channelName);
            if (m_Socket == null)
                Debug.LogWarning($"Unbound: the channel client '{channelName}' could not connect to the channel service at 127.0.0.1:{port}");
        }

        /// <summary>The port of the service this process's clients connect to.</summary>
        static int ServicePort()
        {
            if (ChannelServer.Running) return ChannelServer.Port;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-ump-channel-service-port" && int.TryParse(args[i + 1], out int port)) return port;
            return 0;
        }

        public void Stop()
        {
            m_Socket?.Close();
            m_Socket = null;
        }

        public void Close()
        {
            Stop();
            s_Clients.Remove(clientId);
        }

        /// <summary>Sends what was queued, then gives the received messages to the handlers.</summary>
        public void Tick()
        {
            if (m_Socket == null) return;
            foreach (var message in m_Outgoing) m_Socket.Send(message.Data, message.Text);
            m_Outgoing.Clear();
            while (m_Socket != null && m_Socket.TryReceive(out var message))
            {
                if (m_TextHandlers.Count > 0)
                {
                    string text = Encoding.UTF8.GetString(message.Data);
                    foreach (var handler in m_TextHandlers.ToArray()) Call(() => handler(text));
                }
                foreach (var handler in m_BinaryHandlers.ToArray()) Call(() => handler(message.Data));
            }
        }

        static void Call(Action action)
        {
            try { action(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public void Send(string data) => m_Outgoing.Add(new ChannelMessage(Encoding.UTF8.GetBytes(data ?? ""), true));

        public void Send(byte[] data) => m_Outgoing.Add(new ChannelMessage(data ?? Array.Empty<byte>(), false));

        public Action RegisterMessageHandler(Action<string> handler)
        {
            m_TextHandlers.Add(handler);
            return () => UnregisterMessageHandler(handler);
        }

        public void UnregisterMessageHandler(Action<string> handler) => m_TextHandlers.Remove(handler);

        public Action RegisterMessageHandler(Action<byte[]> handler)
        {
            m_BinaryHandlers.Add(handler);
            return () => UnregisterMessageHandler(handler);
        }

        public void UnregisterMessageHandler(Action<byte[]> handler) => m_BinaryHandlers.Remove(handler);

        public int NewRequestId() => ++m_RequestId;

        /// <summary>The client's name, id and its connection's id at this process's service (-1 when
        /// not connected to it).</summary>
        public ChannelClientInfo GetChannelClientInfo() =>
            ChannelClientInfo.Of(channelName, clientId, IsConnected() && ChannelServer.Running ? ChannelServer.ConnectionOfPort(m_Socket.LocalPort) : -1);

        public static ChannelClientInfo GetChannelClientInfo(string channelName)
        {
            foreach (var client in s_Clients.Values)
                if (client.channelName == channelName) return client.GetChannelClientInfo();
            return ChannelClientInfo.invalidClient;
        }

        public static ChannelClientInfo[] GetChannelClientList()
        {
            var list = new List<ChannelClientInfo>();
            foreach (var client in s_Clients.Values) list.Add(client.GetChannelClientInfo());
            return list.ToArray();
        }

        public static void Close(string channelName)
        {
            foreach (var client in new List<ChannelClient>(s_Clients.Values))
                if (client.channelName == channelName) client.Close();
        }

        public static void Shutdown()
        {
            foreach (var client in new List<ChannelClient>(s_Clients.Values)) client.Close();
        }

        /// <summary>The in-process client of a service connection, as its info; null for another process's.</summary>
        internal static ChannelClientInfo? OfConnection(int connectionId)
        {
            foreach (var client in s_Clients.Values)
                if (client.IsConnected() && ChannelServer.ConnectionOfPort(client.m_Socket.LocalPort) == connectionId)
                    return client.GetChannelClientInfo();
            return null;
        }

        /// <summary>Ticks the auto-ticked clients (each Editor update).</summary>
        internal static void TickAutoClients()
        {
            if (s_Clients.Count == 0) return;
            foreach (var client in new List<ChannelClient>(s_Clients.Values))
                if (client.isAutoTick) client.Tick();
        }
    }
}
