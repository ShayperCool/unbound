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
            if (!Measured(nameof(Start)) || s_Running) return;
            s_Running = true;
            foreach (var own in s_OwnChannels)
                if (!s_Open.Contains(own)) s_Open.Add(own);
        }

        public static void Stop()
        {
            if (Measured(nameof(Stop))) s_Running = false;
        }

        public static bool IsRunning() => Measured(nameof(IsRunning)) && s_Running;

        public static ChannelInfo[] GetChannelList()
        {
            if (!Measured(nameof(GetChannelList))) return Array.Empty<ChannelInfo>();
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
    /// enabled when the machine's EditorPrefs say so (`Editor.kEnableEditorLocalization`: absent or 0
    /// gives false, 1 true, windowed and batch alike, measured with private preferences); Unbound
    /// answers the value without the key (false; true on macOS, its batch Editor's answer) and does
    /// not read the preference, which packages write themselves (the tutorial framework turns it on).
    /// Installed language packs (.po files in the installation's Localization folder) are not read: a
    /// notice says so. Unmeasured: macOS's languages, null strings, the setters' effects beyond the
    /// value read back, context groups set by code, GetLocalizedStringWithGroupNameInLang.</summary>
    internal partial class LocalizationDatabase
    {
        static SystemLanguage s_EditorLanguage = SystemLanguage.English;
        static bool s_EnableEditorLocalization = OperatingSystem.IsMacOS();

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

        static bool s_PacksChecked, s_PreferenceChecked, s_EnableSet;

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
                // The Editor answers the machine's preference, which Unbound does not follow: say so
                // when the preferences hold another value (measured: tests/projects/editorlocalization).
                if (!s_PreferenceChecked && !s_EnableSet)
                {
                    s_PreferenceChecked = true;
                    if (EditorPrefs.HasKey(PreferenceKey) && (EditorPrefs.GetInt(PreferenceKey, 0) != 0) != s_EnableEditorLocalization)
                        Unbound.Runtime.Stubs.Ignored("UnityEditor.LocalizationDatabase.enableEditorLocalization",
                            "answers " + s_EnableEditorLocalization + " where the Editor follows EditorPrefs " + PreferenceKey + " (" + EditorPrefs.GetInt(PreferenceKey, 0) + ")");
                }
                return s_EnableEditorLocalization;
            }
            set
            {
                if (!Measured(nameof(enableEditorLocalization))) return;
                s_EnableEditorLocalization = value;
                s_EnableSet = true;
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
    /// answers (tests/projects/editorchannels collab.instance); its other members are not implemented.</summary>
    internal partial class Collab
    {
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
