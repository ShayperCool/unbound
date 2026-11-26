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

    /// <summary>A client connected to a channel (Unity's C#).</summary>
    [Serializable]
    public partial struct ChannelClientInfo : IEquatable<ChannelClientInfo>
    {
        public static ChannelClientInfo invalidClient = new ChannelClientInfo { m_ChannelClientId = -1, m_ConnectionId = -1 };

        internal static ChannelClientInfo Of(string name, int clientId, int connectionId) =>
            new ChannelClientInfo { m_ChannelName = name, m_ChannelClientId = clientId, m_ConnectionId = connectionId };

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
    /// Started, it serves WebSocket clients on 127.0.0.1 at a free port (<see cref="Unbound.Runtime.ChannelServer"/>):
    /// a client's messages reach its channel's handlers on <see cref="DispatchMessages"/> and each
    /// Editor update, as bytes (a text message's UTF-8); broadcasts and sends go to the channel's or
    /// the connection's client as text or binary messages; the <c>echo</c> channel answers each
    /// message (matches the Unity 6000.0.71f1 Editor with clients of the same Editor,
    /// tests/projects/channelmessages). Unmeasured: clients of other processes, the <c>echoAll</c>,
    /// <c>event</c> and <c>status</c> channels' messages, a handler that throws.
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

        /// <summary>Whether the Editor's answers of this family are measured; otherwise the member
        /// reports itself not implemented.</summary>
        static bool Measured(string member)
        {
            if (Compat.EditorChannelServiceMeasured) return true;
            Stubs.NotImplemented("UnityEditor.MPE.ChannelService." + member);
            return false;
        }

        public static Action GetOrCreateChannel(string channelName, Action<int, byte[]> handler)
        {
            if (!Measured(nameof(GetOrCreateChannel))) return null;
            if (Internal_GetOrCreateChannel(channelName) == -1)
                throw new Exception("Cannot create channel: " + channelName);
            return RegisterMessageHandler(channelName, handler);
        }

        public static Action RegisterMessageHandler(string channelName, Action<int, byte[]> handler)
        {
            if (!Measured(nameof(RegisterMessageHandler))) return null;
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
        public static string GetAddress() => Measured(nameof(GetAddress)) ? "127.0.0.1" : null;

        /// <summary>The service's port while it runs (0 stopped: unmeasured).</summary>
        public static int GetPort() => Measured(nameof(GetPort)) ? Unbound.Runtime.ChannelServer.Port : 0;

        /// <summary>Gives the messages clients sent to the handlers of their channels.</summary>
        public static void DispatchMessages()
        {
            if (!Measured(nameof(DispatchMessages))) return;
            Unbound.Runtime.ChannelServer.Dispatch(HandlersOf);
        }

        static List<Action<int, byte[]>> HandlersOf(string channelName)
        {
            if (!s_Open.Contains(channelName)) return null;
            return s_Handlers.TryGetValue(ChannelNameToId(channelName), out var handlers) ? handlers : null;
        }

        /// <summary>Each Editor update: the clients' messages to the handlers, then the auto-ticked
        /// clients' ticks.</summary>
        internal static void EditorUpdate()
        {
            if (s_Running) DispatchMessages();
            ChannelClient.TickAutoClients();
        }

        public static void Start()
        {
            if (!Measured(nameof(Start)) || s_Running) return;
            Unbound.Runtime.ChannelServer.Start();
            s_Running = true;
            foreach (var own in s_OwnChannels)
                if (!s_Open.Contains(own)) s_Open.Add(own);
        }

        public static void Stop()
        {
            if (!Measured(nameof(Stop))) return;
            s_Running = false;
            Unbound.Runtime.ChannelServer.Stop();
        }

        public static bool IsRunning() => Measured(nameof(IsRunning)) && s_Running;

        public static ChannelInfo[] GetChannelList()
        {
            if (!Measured(nameof(GetChannelList))) return Array.Empty<ChannelInfo>();
            var list = new ChannelInfo[s_Open.Count];
            for (int i = 0; i < list.Length; i++) list[i] = ChannelInfo.Of(s_Open[i], ChannelNameToId(s_Open[i]));
            return list;
        }

        /// <summary>The service's connected clients: an in-process client as its own
        /// <see cref="ChannelClient.GetChannelClientInfo()"/> (channelmessages client.list); another
        /// process's with no client id (-1, unmeasured).</summary>
        public static ChannelClientInfo[] GetChannelClientList()
        {
            if (!Measured(nameof(GetChannelClientList))) return Array.Empty<ChannelClientInfo>();
            var list = new List<ChannelClientInfo>();
            foreach (var (id, channel, port) in Unbound.Runtime.ChannelServer.Connections())
            {
                var own = ChannelClient.OfConnection(id);
                list.Add(own ?? ChannelClientInfo.Of(channel, -1, id));
            }
            return list.ToArray();
        }

        /// <summary>The channel's clients get the text as a text message.</summary>
        public static void Broadcast(int channelId, string data)
        {
            if (Measured(nameof(Broadcast)) && NameOf(channelId) is string channel)
                Unbound.Runtime.ChannelServer.Broadcast(channel, Encoding.UTF8.GetBytes(data ?? ""), text: true);
        }

        /// <summary>The channel's clients get the bytes as a binary message.</summary>
        public static void BroadcastBinary(int channelId, byte[] data)
        {
            if (Measured(nameof(BroadcastBinary)) && NameOf(channelId) is string channel)
                Unbound.Runtime.ChannelServer.Broadcast(channel, data, text: false);
        }

        public static void Send(int connectionId, string data)
        {
            if (Measured(nameof(Send))) Unbound.Runtime.ChannelServer.Send(connectionId, Encoding.UTF8.GetBytes(data ?? ""), text: true);
        }

        internal static void SendBinary(int connectionId, byte[] data)
        {
            if (Measured(nameof(SendBinary))) Unbound.Runtime.ChannelServer.Send(connectionId, data, text: false);
        }

        /// <summary>The name of an open channel, by its id.</summary>
        static string NameOf(int channelId)
        {
            foreach (var name in s_Open)
                if (ChannelNameToId(name) == channelId) return name;
            return null;
        }

        public static int ChannelNameToId(string channelName) => Measured(nameof(ChannelNameToId))
            ? unchecked((int)XxHash32.Hash(Encoding.UTF8.GetBytes(channelName ?? ""), ChannelIdSeed))
            : 0;

        internal static int Internal_GetOrCreateChannel(string channelName)
        {
            if (channelName == null) return -1;
            if (!s_Open.Contains(channelName)) s_Open.Add(channelName);
            return ChannelNameToId(channelName);
        }

        internal static void Internal_CloseChannel(string channelName) => s_Open.Remove(channelName);
    }

    /// <summary>Opens a channel with a handler for a scope (Unity's C#).</summary>
    public partial struct ChannelScope : IDisposable
    {
        bool m_CloseChannelOnExit;
        string m_ChannelName;
        Action m_Off;

        public ChannelScope(string channelName, Action<int, byte[]> handler, bool closeChannelOnExit = true)
        {
            m_CloseChannelOnExit = closeChannelOnExit;
            m_ChannelName = channelName;
            m_Off = ChannelService.GetOrCreateChannel(channelName, handler);
        }

        public void Dispose()
        {
            // Null where the service's answers are unmeasured (no channel was opened) and for a
            // default ChannelScope, whose Dispose throws in Unity's C# (Unbound returns).
            m_Off?.Invoke();
            if (m_CloseChannelOnExit)
                ChannelService.CloseChannel(m_ChannelName);
        }
    }
}

namespace UnityEditor
{
    /// <summary>The Editor's interface language settings as the Unity 6000.0.71f1 Editor answers them
    /// in Play mode (tests/projects/editorlocalization, the windowed Linux Editor with clean
    /// preferences; tests/projects/editorchannels in batch mode): English, the only language
    /// installed; every string its own translation; each language's culture code. Localization is
    /// enabled when the EditorPrefs say so (`Editor.kEnableEditorLocalization`: absent or 0 gives
    /// false, 1 true, windowed and batch alike: tests/projects/editorlocalization profiles), read
    /// once: a later write of the preference does not change it, and the setter changes it without
    /// writing the preference (afterPrefWrite, afterSet). Without the key macOS answers true (the
    /// macOS batch Editor's answer with its own preferences, tests/projects/editorchannels metal;
    /// without the key, and the key on macOS: unmeasured). Packages write the preference
    /// themselves (the tutorial framework turns it on). Unmeasured: the preference written in Play
    /// mode before the first read (Unbound reads it then; the Editor presumably at its start).
    /// Installed language packs (.po files in the installation's Localization folder) are not read: a
    /// notice says so. Unmeasured: macOS's languages, null strings, the setters' effects beyond the
    /// value read back, context groups set by code, GetLocalizedStringWithGroupNameInLang.</summary>
    internal partial class LocalizationDatabase
    {
        static SystemLanguage s_EditorLanguage = SystemLanguage.English;
        static bool s_EnableEditorLocalization;

        static readonly Dictionary<SystemLanguage, string> s_Cultures = new Dictionary<SystemLanguage, string>
        {
            [SystemLanguage.Afrikaans] = "af",
            [SystemLanguage.Arabic] = "ar",
            [SystemLanguage.Basque] = "eu",
            [SystemLanguage.Belarusian] = "be",
            [SystemLanguage.Bulgarian] = "bg",
            [SystemLanguage.Catalan] = "ca",
            [SystemLanguage.Chinese] = "zh",
            [SystemLanguage.Czech] = "cs",
            [SystemLanguage.Danish] = "da",
            [SystemLanguage.Dutch] = "nl",
            [SystemLanguage.English] = "en",
            [SystemLanguage.Estonian] = "et",
            [SystemLanguage.Faroese] = "fo",
            [SystemLanguage.Finnish] = "fi",
            [SystemLanguage.French] = "fr",
            [SystemLanguage.German] = "de",
            [SystemLanguage.Greek] = "el",
            [SystemLanguage.Hebrew] = "he",
            [SystemLanguage.Hungarian] = "hu",
            [SystemLanguage.Icelandic] = "is",
            [SystemLanguage.Indonesian] = "id",
            [SystemLanguage.Italian] = "it",
            [SystemLanguage.Japanese] = "ja",
            [SystemLanguage.Korean] = "ko",
            [SystemLanguage.Latvian] = "lv",
            [SystemLanguage.Lithuanian] = "lt",
            [SystemLanguage.Norwegian] = "no",
            [SystemLanguage.Polish] = "pl",
            [SystemLanguage.Portuguese] = "pt",
            [SystemLanguage.Romanian] = "ro",
            [SystemLanguage.Russian] = "ru",
            [SystemLanguage.SerboCroatian] = "hr",
            [SystemLanguage.Slovak] = "sk",
            [SystemLanguage.Slovenian] = "sl",
            [SystemLanguage.Spanish] = "es",
            [SystemLanguage.Swedish] = "sv",
            [SystemLanguage.Thai] = "th",
            [SystemLanguage.Turkish] = "tr",
            [SystemLanguage.Ukrainian] = "uk",
            [SystemLanguage.Vietnamese] = "vi",
            [SystemLanguage.ChineseSimplified] = "zh-hans",
            [SystemLanguage.ChineseTraditional] = "zh-hant",
            [SystemLanguage.Hindi] = "hi",
        };

        static bool s_PacksChecked, s_EnableKnown;

        /// <summary>Gives a notice once when the installation holds language packs (`.po` files in
        /// its Localization folder), which Unbound does not read; L10n's translations check too.</summary>
        internal static void CheckLanguagePacks()
        {
            if (s_PacksChecked) return;
            s_PacksChecked = true;
            var folder = System.IO.Path.Combine(UnityInstallInfo.DataPath ?? "", "Localization");
            if (!System.IO.Directory.Exists(folder)) return;
            using var packs = System.IO.Directory.EnumerateFiles(folder, "*.po", System.IO.SearchOption.AllDirectories).GetEnumerator();
            if (packs.MoveNext())
                Unbound.Runtime.Stubs.Ignored("UnityEditor.LocalizationDatabase language packs",
                    "are not read: the Editor's interface stays English and strings untranslated (" + folder + ")");
        }

        static bool Measured(string member)
        {
            if (Unbound.Runtime.Compat.EditorChannelServiceMeasured)
            {
                CheckLanguagePacks();
                return true;
            }
            Unbound.Runtime.Stubs.NotImplemented("UnityEditor.LocalizationDatabase." + member);
            return false;
        }

        public static SystemLanguage currentEditorLanguage
        {
            get => Measured(nameof(currentEditorLanguage)) ? s_EditorLanguage : default;
            set { if (Measured(nameof(currentEditorLanguage))) s_EditorLanguage = value; }
        }

        public static bool enableEditorLocalization
        {
            get
            {
                if (!Measured(nameof(enableEditorLocalization))) return false;
                if (!s_EnableKnown)
                {
                    s_EnableEditorLocalization = EditorPrefs.HasKey(PreferenceKey) ? EditorPrefs.GetInt(PreferenceKey, 0) != 0 : OperatingSystem.IsMacOS();
                    s_EnableKnown = true;
                }
                return s_EnableEditorLocalization;
            }
            set
            {
                if (!Measured(nameof(enableEditorLocalization))) return;
                s_EnableEditorLocalization = value;
                s_EnableKnown = true;
            }
        }

        /// <summary>The EditorPrefs key the Editor's answer follows.</summary>
        const string PreferenceKey = "Editor.kEnableEditorLocalization";

        public static SystemLanguage GetDefaultEditorLanguage() => Measured(nameof(GetDefaultEditorLanguage)) ? SystemLanguage.English : default;

        public static SystemLanguage[] GetAvailableEditorLanguages() =>
            Measured(nameof(GetAvailableEditorLanguages)) ? new[] { SystemLanguage.English } : Array.Empty<SystemLanguage>();

        public static string GetLocalizedString(string original) => Measured(nameof(GetLocalizedString)) ? original : default;

        public static string GetLocalizedStringInLang(SystemLanguage lang, string original) => Measured(nameof(GetLocalizedStringInLang)) ? original : default;

        public static string GetLocalizedStringWithGroupName(string original, string groupName) =>
            Measured(nameof(GetLocalizedStringWithGroupName)) ? original : default;

        public static string GetLocalizedStringWithGroupNameInLang(SystemLanguage lang, string original, string groupName) =>
            Measured(nameof(GetLocalizedStringWithGroupNameInLang)) ? original : default;

        /// <summary>The installation's Localization folder.</summary>
        public static string GetLocalizationResourceFolder() =>
            Measured(nameof(GetLocalizationResourceFolder)) ? UnityInstallInfo.DataPath.TrimEnd('/') + "/Localization" : default;

        public static string GetCulture(SystemLanguage lang) =>
            Measured(nameof(GetCulture)) ? (s_Cultures.TryGetValue(lang, out var culture) ? culture : "") : default;

        public static string GetContextGroupName() => Measured(nameof(GetContextGroupName)) ? "" : default;

        public static string MarkForTranslation(string value) => Measured(nameof(MarkForTranslation)) ? value : default;

        public static string noLocalizationGroupName => Measured(nameof(noLocalizationGroupName)) ? "." : default;
    }
}

namespace UnityEditor.Collaboration
{
    /// <summary>The collaboration service object: one instance, as the Unity 6000.0.71f1 Editor
    /// answers (tests/projects/editorchannels collab.instance); Collaborate is not enabled for the
    /// project (tests/projects/uvmetrics collab.*); its other members are not implemented.</summary>
    internal partial class Collab
    {
        /// <summary>False, as Unity's C# answers for any project (6000.0
        /// Editor/Mono/Collab/CollabToUVCSBridge.cs: Collaborate was retired) and the Unity
        /// 6000.0.71f1 Editor answers (tests/projects/uvmetrics collab.IsCollabEnabledForCurrentProject).</summary>
        public bool IsCollabEnabledForCurrentProject()
        {
            if (Unbound.Runtime.Compat.EditorChannelServiceMeasured) return false;
            Unbound.Runtime.Stubs.NotImplemented("UnityEditor.Collaboration.Collab.IsCollabEnabledForCurrentProject");
            return default;
        }

        static readonly Collab s_Instance = new Collab();

        Collab() { }

        public static Collab instance
        {
            get
            {
                if (Unbound.Runtime.Compat.EditorChannelServiceMeasured) return s_Instance;
                Unbound.Runtime.Stubs.NotImplemented("UnityEditor.Collaboration.Collab.instance");
                return null;
            }
        }
    }
}

namespace UnityEditor.Toolbars
{
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

    /// <summary>The play mode toolbar: Unbound shows no toolbar, so the event is never raised (as in
    /// the batch-mode Editor); handlers are added and removed as on any event.</summary>
    [EditorToolbarElement("Editor Utility/Play Mode")]
    internal sealed partial class PlayModeButtons : UnityEngine.UIElements.VisualElement
    {
        /// <summary>Unity's constructor builds the Play, Pause and Step buttons and raises
        /// <see cref="onPlayModeButtonsCreated"/>; Unbound has no toolbar buttons.</summary>
        public PlayModeButtons() { Unbound.Runtime.Stubs.NotImplemented("UnityEditor.Toolbars.PlayModeButtons.ctor"); }

#pragma warning disable CS0067 // never raised: no toolbar
        internal static event Action<UnityEngine.UIElements.VisualElement> onPlayModeButtonsCreated;
#pragma warning restore CS0067
    }
}
