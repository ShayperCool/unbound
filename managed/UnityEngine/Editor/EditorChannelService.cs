using System;
using System.Collections.Generic;
using System.Text;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor.MPE
{
    /// <summary>A messaging channel of the Editor's channel service (Unity's C#).</summary>
    [Serializable]
    public partial struct ChannelInfo : IEquatable<ChannelInfo>
    {
        public static ChannelInfo invalidChannel = new ChannelInfo { m_ChannelId = -1 };

        string m_ChannelName;
        int m_ChannelId;

        public string name => m_ChannelName;

        public int id => m_ChannelId;

        internal static ChannelInfo Of(string name, int id) => new ChannelInfo { m_ChannelName = name, m_ChannelId = id };

        public bool Equals(ChannelInfo obj) => this == obj;

        public override bool Equals(object obj) => obj is ChannelInfo info && this == info;

        public override int GetHashCode() => name.GetHashCode() ^ id.GetHashCode();

        public static bool operator ==(ChannelInfo x, ChannelInfo y) => x.id == y.id && x.name == y.name;

        public static bool operator !=(ChannelInfo x, ChannelInfo y) => !(x == y);
    }

    /// <summary>A client connected to a channel (Unity's C#); none connects to Unbound.</summary>
    [Serializable]
    public partial struct ChannelClientInfo : IEquatable<ChannelClientInfo>
    {
        public static ChannelClientInfo invalidClient = new ChannelClientInfo { m_ChannelClientId = -1, m_ConnectionId = -1 };

        string m_ChannelName;
        int m_ChannelClientId;
        int m_ConnectionId;

        public string name => m_ChannelName;

        public int clientId => m_ChannelClientId;

        public int connectionId => m_ConnectionId;

        public bool Equals(ChannelClientInfo obj) => this == obj;

        public override bool Equals(object obj) => obj is ChannelClientInfo info && Equals(info);

        public override int GetHashCode() => name.GetHashCode() ^ clientId.GetHashCode() ^ connectionId.GetHashCode();

        public static bool operator ==(ChannelClientInfo x, ChannelClientInfo y) =>
            x.name == y.name && x.clientId == y.clientId && x.connectionId == y.connectionId;

        public static bool operator !=(ChannelClientInfo x, ChannelClientInfo y) => !(x == y);
    }

    /// <summary>
    /// The Editor's messaging channel service as the Unity 6000.0.71f1 Editor answers it in Play mode
    /// (tests/projects/editorchannels): not running until <see cref="Start"/> (in batch mode), then
    /// holding the Editor's own channels <c>echo</c>, <c>echoAll</c>, <c>event</c> and
    /// <c>status</c>; a channel's id is its name's 32-bit xxHash with the service's seed, whether
    /// the channel is open or not; channels open and close, handlers register as Unity's C# does.
    /// Unbound runs no channel server: no client connects, so broadcasts and sends reach nobody (the
    /// Editor's capture has no client either) and the port is reported as none, with a warning.
    /// </summary>
    public static partial class ChannelService
    {
        internal static Dictionary<int, List<Action<int, byte[]>>> s_Handlers = new Dictionary<int, List<Action<int, byte[]>>>();

        /// <summary>The seed of the service's channel ids (measured: tests/projects/editorchannels mpe.id.*).</summary>
        const uint ChannelIdSeed = 0x8F37154B;

        /// <summary>The channels the Editor's service opens itself as it starts.</summary>
        static readonly string[] s_OwnChannels = { "echo", "echoAll", "event", "status" };

        static readonly List<string> s_Open = new List<string>();
        static bool s_Running;

        public static Action GetOrCreateChannel(string channelName, Action<int, byte[]> handler)
        {
            if (Internal_GetOrCreateChannel(channelName) == -1)
                throw new Exception("Cannot create channel: " + channelName);
            return RegisterMessageHandler(channelName, handler);
        }

        public static Action RegisterMessageHandler(string channelName, Action<int, byte[]> handler)
        {
            var channel = GetChannelFromName(channelName);
            if (ChannelInfo.invalidChannel == channel)
                throw new Exception("Channel doesn't exists or is not open.");
            if (!s_Handlers.TryGetValue(channel.id, out var handlers))
                s_Handlers.Add(channel.id, new List<Action<int, byte[]>> { handler });
            else if (handlers.Contains(handler))
                throw new Exception("Cannot add already existing channel handler for channel: " + channelName);
            else
                handlers.Add(handler);
            return () => UnregisterMessageHandler(channelName, handler);
        }

        public static void UnregisterMessageHandler(string channelName, Action<int, byte[]> handler)
        {
            var channel = GetChannelFromName(channelName);
            if (ChannelInfo.invalidChannel == channel) return;
            if (s_Handlers.TryGetValue(channel.id, out var handlers))
            {
                handlers.Remove(handler);
                if (handlers.Count == 0) s_Handlers.Remove(channel.id);
            }
        }

        public static void CloseChannel(string channelName)
        {
            var channel = GetChannelFromName(channelName);
            if (ChannelInfo.invalidChannel == channel) return;
            s_Handlers.Remove(channel.id);
            Internal_CloseChannel(channelName);
        }

        public static void Broadcast(int channelId, byte[] data) => BroadcastBinary(channelId, data);

        public static void Send(int connectionId, byte[] data) => SendBinary(connectionId, data);

        internal static ChannelInfo GetChannelFromName(string channelName)
        {
            foreach (var channelInfo in GetChannelList())
                if (channelInfo.name == channelName) return channelInfo;
            return ChannelInfo.invalidChannel;
        }

        [Unbound.EditorFaithful("the service's local address (measured: tests/projects/editorchannels mpe.address)")]
        public static string GetAddress() => "127.0.0.1";

        public static int GetPort()
        {
            Stubs.Ignored("UnityEditor.MPE.ChannelService.GetPort", "Unbound runs no channel server: no client can connect, so there is no port (0)");
            return 0;
        }

        /// <summary>Messages come from clients, and none connect.</summary>
        [Unbound.EditorFaithful("no client connects to Unbound's channel service, as none does in the Editor's capture (tests/projects/editorchannels mpe.clients)")]
        public static void DispatchMessages() { }

        public static void Start()
        {
            if (s_Running) return;
            s_Running = true;
            foreach (var own in s_OwnChannels)
                if (!s_Open.Contains(own)) s_Open.Add(own);
        }

        public static void Stop() => s_Running = false;

        public static bool IsRunning() => s_Running;

        public static ChannelInfo[] GetChannelList()
        {
            var list = new ChannelInfo[s_Open.Count];
            for (int i = 0; i < list.Length; i++) list[i] = ChannelInfo.Of(s_Open[i], ChannelNameToId(s_Open[i]));
            return list;
        }

        [Unbound.EditorFaithful("no client connects to Unbound's channel service, as none does in the Editor's capture (tests/projects/editorchannels mpe.clients)")]
        public static ChannelClientInfo[] GetChannelClientList() => Array.Empty<ChannelClientInfo>();

        /// <summary>No client is connected: the message reaches nobody.</summary>
        [Unbound.EditorFaithful("no client connects to Unbound's channel service, as none does in the Editor's capture (tests/projects/editorchannels mpe.clients)")]
        public static void Broadcast(int channelId, string data) { }

        /// <summary>No client is connected: the message reaches nobody.</summary>
        [Unbound.EditorFaithful("no client connects to Unbound's channel service, as none does in the Editor's capture (tests/projects/editorchannels mpe.clients)")]
        public static void BroadcastBinary(int channelId, byte[] data) { }

        /// <summary>No client is connected: the message reaches nobody.</summary>
        [Unbound.EditorFaithful("no client connects to Unbound's channel service, as none does in the Editor's capture (tests/projects/editorchannels mpe.clients)")]
        public static void Send(int connectionId, string data) { }

        /// <summary>No client is connected: the message reaches nobody.</summary>
        [Unbound.EditorFaithful("no client connects to Unbound's channel service, as none does in the Editor's capture (tests/projects/editorchannels mpe.clients)")]
        internal static void SendBinary(int connectionId, byte[] data) { }

        public static int ChannelNameToId(string channelName) =>
            unchecked((int)XxHash32.Hash(Encoding.UTF8.GetBytes(channelName ?? ""), ChannelIdSeed));

        internal static int Internal_GetOrCreateChannel(string channelName)
        {
            if (channelName == null) return -1;
            if (!s_Open.Contains(channelName)) s_Open.Add(channelName);
            return ChannelNameToId(channelName);
        }

        internal static void Internal_CloseChannel(string channelName) => s_Open.Remove(channelName);
    }
}

namespace UnityEditor
{
    /// <summary>The Editor's interface language settings as the Unity 6000.0.71f1 Editor answers them
    /// (tests/projects/editorchannels localization.*): English, localization enabled.</summary>
    internal partial class LocalizationDatabase
    {
        static SystemLanguage s_EditorLanguage = SystemLanguage.English;
        static bool s_EnableEditorLocalization = true;

        public static SystemLanguage currentEditorLanguage { get => s_EditorLanguage; set => s_EditorLanguage = value; }

        public static bool enableEditorLocalization { get => s_EnableEditorLocalization; set => s_EnableEditorLocalization = value; }
    }
}

namespace UnityEditor.Collaboration
{
    /// <summary>The collaboration service object: one instance, as the Unity 6000.0.71f1 Editor
    /// answers (tests/projects/editorchannels collab.instance); its other members are not implemented.</summary>
    internal partial class Collab
    {
        static readonly Collab s_Instance = new Collab();

        Collab() { }

        public static Collab instance => s_Instance;
    }
}

namespace UnityEditor.Toolbars
{
    /// <summary>The play mode toolbar: Unbound shows no toolbar, so the event is never raised (as in
    /// the batch-mode Editor); handlers are added and removed as on any event.</summary>
    /// <summary>Names a toolbar element (Unity's C#).</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed partial class EditorToolbarElementAttribute : Attribute
    {
        public EditorToolbarElementAttribute(string id, params Type[] targetWindows)
        {
            this.id = id;
            this.targetWindows = targetWindows;
        }

        public string id { get; }

        public Type[] targetWindows { get; }
    }

    [EditorToolbarElement("Editor Utility/Play Mode")]
    internal sealed partial class PlayModeButtons : UnityEngine.UIElements.VisualElement
    {
#pragma warning disable CS0067 // never raised: no toolbar
        internal static event Action<UnityEngine.UIElements.VisualElement> onPlayModeButtonsCreated;
#pragma warning restore CS0067
    }
}
