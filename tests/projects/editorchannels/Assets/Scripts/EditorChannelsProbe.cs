using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.MPE;
using UnityEngine;

/// <summary>
/// Records, in Play mode, what the Editor's messaging channel service (UnityEditor.MPE.ChannelService),
/// the Editor localization settings, the collaboration service and the play mode toolbar event
/// answer: the members packages call while a game runs.
/// </summary>
static class EditorChannelsProbe
{
    static void OnButtons(object buttons) { }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("EDITOR_CHANNELS_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var output = new StringBuilder();
        void Put(string key, string value) => output.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}\n");
        string Try(Func<string> read) { try { return read(); } catch (Exception e) { return "throws " + e.GetType().Name + ": " + e.Message; } }
        string Channels() => string.Join(",", ChannelService.GetChannelList().Select(c => c.name).OrderBy(n => n, StringComparer.Ordinal));

        const string name = "probe.channel";
        // The batch-mode Editor does not run the service (asking a stopped one for its channels
        // ends the Editor); a windowed Editor does: the rest is measured once it runs.
        Put("mpe.running.start", Try(() => ChannelService.IsRunning().ToString()));
        Put("mpe.process.start", Try(() => ProcessService.IsChannelServiceStarted().ToString()));
        Put("mpe.start", Try(() => { if (!ChannelService.IsRunning()) ChannelService.Start(); return ChannelService.IsRunning().ToString(); }));
        Put("mpe.process.started", Try(() => ProcessService.IsChannelServiceStarted().ToString()));
        Put("mpe.channels.start", Try(Channels));
        Put("mpe.id.before", Try(() => ChannelService.ChannelNameToId(name).ToString()));
        foreach (var other in new[] { "", "a", "b", "ab", "abc", "echo", "status", "A" })
            Put("mpe.id.name." + other, Try(() => ChannelService.ChannelNameToId(other).ToString()));
        Action<int, byte[]> handler = (client, data) => { };
        Action close = null;
        Put("mpe.create", Try(() => { close = ChannelService.GetOrCreateChannel(name, handler); return close == null ? "null" : "an action"; }));
        Put("mpe.running.created", Try(() => ChannelService.IsRunning().ToString()));
        Put("mpe.id.created", Try(() => ChannelService.ChannelNameToId(name).ToString()));
        Put("mpe.channels.created", Try(Channels));
        Put("mpe.register.again", Try(() => { ChannelService.RegisterMessageHandler(name, handler); return "registered"; }));
        Put("mpe.register.unknown", Try(() => { ChannelService.RegisterMessageHandler("probe.unknown", handler); return "registered"; }));
        Put("mpe.broadcast", Try(() => { ChannelService.BroadcastBinary(ChannelService.ChannelNameToId(name), new byte[] { 1, 2, 3 }); return "sent"; }));
        Put("mpe.broadcast.string", Try(() => { ChannelService.Broadcast(ChannelService.ChannelNameToId(name), "text"); return "sent"; }));
        Put("mpe.clients", Try(() => ChannelService.GetChannelClientList().Length.ToString()));
        Put("mpe.port", Try(() => ChannelService.GetPort() > 0 ? "positive" : ChannelService.GetPort().ToString()));
        Put("mpe.address", Try(ChannelService.GetAddress));
        Put("mpe.unregister", Try(() => { close(); return "done"; }));
        Put("mpe.close", Try(() => { ChannelService.CloseChannel(name); return "closed"; }));
        Put("mpe.channels.closed", Try(Channels));
        Put("mpe.id.closed", Try(() => ChannelService.ChannelNameToId(name).ToString()));
        Put("mpe.stop", Try(() => { ChannelService.Stop(); return ChannelService.IsRunning().ToString(); }));
        Put("mpe.process.stopped", Try(() => ProcessService.IsChannelServiceStarted().ToString()));

        var editor = typeof(EditorApplication).Assembly;
        var localization = editor.GetType("UnityEditor.LocalizationDatabase");
        Put("localization.type", localization == null ? "null" : "found");
        Put("localization.currentEditorLanguage", Try(() => localization.GetProperty("currentEditorLanguage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null).ToString()));
        Put("localization.enableEditorLocalization", Try(() => localization.GetProperty("enableEditorLocalization", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null).ToString()));
        var collab = editor.GetType("UnityEditor.Collaboration.Collab");
        Put("collab.type", collab == null ? "null" : "found");
        Put("collab.instance", Try(() => { var i = collab.GetProperty("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null); return i == null ? "null" : i.GetType().FullName; }));
        var buttons = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.Toolbars.PlayModeButtons")).FirstOrDefault(t => t != null);
        Put("toolbar.type", buttons == null ? "null" : "found");
        Put("toolbar.event", Try(() =>
        {
            var e = buttons.GetEvent("onPlayModeButtonsCreated", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var h = Delegate.CreateDelegate(e.EventHandlerType, typeof(EditorChannelsProbe).GetMethod(nameof(OnButtons), BindingFlags.NonPublic | BindingFlags.Static));
            e.AddMethod.Invoke(null, new object[] { h });
            e.RemoveMethod.Invoke(null, new object[] { h });
            return "added and removed";
        }));
        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }
}
