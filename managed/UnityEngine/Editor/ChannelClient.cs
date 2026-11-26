using System;
using System.Collections.Generic;
using System.Text;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor.MPE
{
    /// <summary>
    /// A client of a channel of the channel service, one per channel name (its id the channel's id),
    /// as the Unity 6000.0.71f1
    /// Editor's clients of its own service behave (tests/projects/channelmessages): Start connects at
    /// once; what it sends goes out, and what the service sent reaches its handlers, on Tick (each
    /// Editor update for an auto-ticked client); each message reaches both kinds of handlers, the
    /// text ones first (the bytes as UTF-8) and then the binary ones, whether it was sent as text or
    /// binary; registering a handler twice throws; Close disconnects it, drops its handlers, and
    /// the next GetOrCreateClient makes another. A handler's exception goes to the standard output,
    /// as Unity's C# writes it. The service is the one whose port the command line's
    /// <c>-ump-channel-service-port</c> names (how a process Multiplayer Play Mode starts reaches
    /// the main one, which also runs its own service: unmeasured), else this process's.
    /// Other release families: not implemented, noticed.
    /// </summary>
    public partial class ChannelClient
    {
        internal static Dictionary<int, ChannelClient> s_Clients = new Dictionary<int, ChannelClient>();
        static readonly List<ChannelClient> s_Ticking = new List<ChannelClient>();

        List<Action<string>> m_TextHandlers = new List<Action<string>>();
        List<Action<byte[]>> m_BinaryHandlers = new List<Action<byte[]>>();
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

        /// <summary>Whether this family's answers are measured; otherwise the member reports itself
        /// not implemented.</summary>
        static bool Measured(string member)
        {
            if (Compat.EditorChannelServiceMeasured) return true;
            Stubs.NotImplemented("UnityEditor.MPE.ChannelClient." + member);
            return false;
        }

        public static ChannelClient GetOrCreateClient(string channelName)
        {
            if (!Measured(nameof(GetOrCreateClient))) return null;
            int id = ChannelService.ChannelNameToId(channelName);
            if (!s_Clients.TryGetValue(id, out var client)) s_Clients.Add(id, client = new ChannelClient(channelName, id));
            return client;
        }

        public bool IsConnected() => m_Socket != null && m_Socket.Connected;

        /// <summary>Connects to the service (unless connected), sets whether each Editor update ticks
        /// the client, and ticks it, as Unity's C# does.</summary>
        public void Start(bool autoTick)
        {
            if (!Measured(nameof(Start))) return;
            isAutoTick = autoTick;
            if (!IsConnected()) Connect();
            Tick();
        }

        void Connect()
        {
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

        /// <summary>The port of the service this process's clients connect to: the command line's,
        /// else this process's service's.</summary>
        static int ServicePort()
        {
            var args = Unbound.EditorProcess.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-ump-channel-service-port" && int.TryParse(args[i + 1], out int port)) return port;
            return ChannelServer.Running ? ChannelServer.Port : 0;
        }

        public void Stop()
        {
            m_Socket?.Close();
            m_Socket = null;
        }

        /// <summary>Drops the handlers, then closes the client of this channel (Unity's C#: the one the
        /// channel's id names now, which a later GetOrCreateClient may have made).</summary>
        public void Close()
        {
            m_TextHandlers = new List<Action<string>>();
            m_BinaryHandlers = new List<Action<byte[]>>();
            Close(channelName);
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
            catch (Exception e) { Console.WriteLine(e); }
        }

        public void Send(string data) => m_Outgoing.Add(new ChannelMessage(Encoding.UTF8.GetBytes(data ?? ""), true));

        public void Send(byte[] data) => m_Outgoing.Add(new ChannelMessage(data ?? Array.Empty<byte>(), false));

        public Action RegisterMessageHandler(Action<string> handler)
        {
            if (m_TextHandlers.Contains(handler)) throw new Exception("Channel Client Handler already registered");
            m_TextHandlers.Add(handler);
            return () => UnregisterMessageHandler(handler);
        }

        public void UnregisterMessageHandler(Action<string> handler) => m_TextHandlers.Remove(handler);

        public Action RegisterMessageHandler(Action<byte[]> handler)
        {
            if (m_BinaryHandlers.Contains(handler)) throw new Exception("Channel Client Handler already registered");
            m_BinaryHandlers.Add(handler);
            return () => UnregisterMessageHandler(handler);
        }

        public void UnregisterMessageHandler(Action<byte[]> handler) => m_BinaryHandlers.Remove(handler);

        public int NewRequestId() => ++m_RequestId;

        /// <summary>The client's name, id and its connection's id at this process's service (-1 when
        /// not connected to it).</summary>
        public ChannelClientInfo GetChannelClientInfo() =>
            ChannelClientInfo.Of(channelName, clientId, IsConnected() && ChannelServer.Running ? ChannelServer.ConnectionOfPort(m_Socket.LocalPort) : -1);

        public static ChannelClientInfo GetChannelClientInfo(string channelName) =>
            s_Clients.TryGetValue(ChannelService.ChannelNameToId(channelName), out var client) ? client.GetChannelClientInfo() : ChannelClientInfo.invalidClient;

        public static ChannelClientInfo[] GetChannelClientList()
        {
            var list = new List<ChannelClientInfo>();
            foreach (var client in s_Clients.Values) list.Add(client.GetChannelClientInfo());
            return list.ToArray();
        }

        public static void Close(string channelName)
        {
            int id = ChannelService.ChannelNameToId(channelName);
            if (!s_Clients.Remove(id, out var client)) return;
            client.Stop();
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
            s_Ticking.Clear();
            foreach (var client in s_Clients.Values)
                if (client.isAutoTick) s_Ticking.Add(client);
            foreach (var client in s_Ticking) client.Tick();
            s_Ticking.Clear();
        }
    }
}
