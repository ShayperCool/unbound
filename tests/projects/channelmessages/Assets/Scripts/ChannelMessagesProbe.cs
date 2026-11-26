using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.MPE;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $CHANNELMESSAGES_OUT (tests/value_probe.py rows): messages between the
/// Editor's channel service (UnityEditor.MPE.ChannelService) and channel clients of the same Editor
/// (ChannelClient): connecting, the client list, text and binary messages each way (broadcast and to
/// one connection), when handlers run (on a tick, on DispatchMessages or on a frame), the Editor's
/// own echo channels, closing a client. Ids are recorded only by how they relate.
/// </summary>
public class ChannelMessagesProbe : MonoBehaviour
{
    const string Variable = "CHANNELMESSAGES_OUT";
    const string Channel = "probe-channel";
    static StreamWriter output;
    readonly List<string> serverReceived = new List<string>();
    readonly List<string> clientReceived = new List<string>();
    int clientConnection = -1;

    static string Escape(string value)
    {
        var text = new StringBuilder();
        foreach (char c in value)
        {
            if (c == '\\' || c == '"') text.Append('\\').Append(c);
            else if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4"));
            else text.Append(c);
        }
        return text.ToString();
    }

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static string Try(Func<string> action)
    {
        try { return action(); }
        catch (Exception e) { return "throws " + e.GetType().Name + ": " + e.Message; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("ChannelMessagesProbe").AddComponent<ChannelMessagesProbe>();
    }

    static string Hex(byte[] data) => data == null ? "null" : string.Join(" ", data.Select(b => b.ToString("x2")));

    string Connection(int id) => id == clientConnection ? "client" : id < 0 ? id.ToString() : "other";

    /// <summary>Waits up to two seconds of wall clock, a frame at a time, until the condition holds.</summary>
    static IEnumerator Until(Func<bool> condition, Action<bool> done)
    {
        var start = DateTime.UtcNow;
        while (!condition() && (DateTime.UtcNow - start).TotalSeconds < 2) yield return null;
        done(condition());
    }

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        try { yield return Run(); }
        finally
        {
            output.Write("{\"k\":\"complete\",\"v\":true}\n");
            output.Dispose();
            EditorApplication.Exit(0);
        }
    }

    IEnumerator Run()
    {
        Row("service.start", Try(() => { if (!ChannelService.IsRunning()) ChannelService.Start(); return ChannelService.IsRunning().ToString(); }));
        Row("service.port", Try(() => ChannelService.GetPort() > 0 ? "positive" : ChannelService.GetPort().ToString()));
        int channelId = ChannelService.ChannelNameToId(Channel);
        Row("server.open", Try(() =>
        {
            ChannelService.GetOrCreateChannel(Channel, (connection, data) =>
                serverReceived.Add(Connection(connection) + ":" + Hex(data)));
            return "ok";
        }));
        Row("clients.before", Try(() => ChannelService.GetChannelClientList().Count(c => c.name == Channel).ToString()));

        // A client of the same Editor, ticked by hand.
        ChannelClient client = null;
        Row("client.create", Try(() =>
        {
            client = ChannelClient.GetOrCreateClient(Channel);
            return client.channelName + " connected " + client.IsConnected() + " autoTick " + client.isAutoTick;
        }));
        if (client == null) yield break;
        client.RegisterMessageHandler((string text) => clientReceived.Add("text:" + text));
        client.RegisterMessageHandler((byte[] data) => clientReceived.Add("binary:" + Hex(data)));
        Row("client.start", Try(() => { client.Start(false); return "connected " + client.IsConnected() + " autoTick " + client.isAutoTick; }));
        bool connected = false;
        yield return Until(() => client.IsConnected(), c => connected = c);
        Row("client.connected", connected.ToString());
        Row("client.list", Try(() =>
        {
            var mine = ChannelService.GetChannelClientList().Where(c => c.name == Channel).ToArray();
            var info = client.GetChannelClientInfo();
            clientConnection = info.connectionId;
            return mine.Length + " entries; own entry " + mine.Any(c => c == info) + "; clientId matches " + (info.clientId == client.clientId)
                + "; connection positive " + (info.connectionId > 0) + "; name " + info.name;
        }));
        Row("client.info_by_name", Try(() => (ChannelClient.GetChannelClientInfo(Channel) == client.GetChannelClientInfo()).ToString()));
        Row("client.id_is_channel_id", Try(() => (client.clientId == channelId).ToString()));
        Row("client.duplicate_handler", Try(() => { Action<byte[]> h = _ => { }; client.RegisterMessageHandler(h); client.RegisterMessageHandler(h); return "ok"; }));

        // Client to server: text, then binary.
        yield return ToServer(client, "text", () => client.Send("hello server"));
        yield return ToServer(client, "binary", () => client.Send(new byte[] { 1, 2, 3 }));

        // Server to client: broadcasts and sends to the client's connection, text and binary.
        yield return ToClient(client, "broadcast_text", () => ChannelService.Broadcast(channelId, "to all"));
        yield return ToClient(client, "broadcast_binary", () => ChannelService.BroadcastBinary(channelId, new byte[] { 4, 5 }));
        yield return ToClient(client, "send_text", () => ChannelService.Send(clientConnection, "to one"));
        yield return ToClient(client, "send_binary", () => ChannelService.Send(clientConnection, new byte[] { 6 }));
        yield return ToClient(client, "broadcast_high_bytes", () => ChannelService.BroadcastBinary(channelId, new byte[] { 0xc3, 0xa9, 0xff, 0x41 }));
        yield return ToClient(client, "broadcast_non_ascii_text", () => ChannelService.Broadcast(channelId, "caf\u00e9"));

        // A client of another channel, its binary handler registered first: what it gets of a
        // broadcast on its channel and of this channel's.
        ChannelClient second = null;
        var secondReceived = new List<string>();
        const string Other = "probe-other";
        Row("second.create", Try(() =>
        {
            ChannelService.GetOrCreateChannel(Other, (connection, data) => serverReceived.Add("other:" + Hex(data)));
            second = ChannelClient.GetOrCreateClient(Other);
            second.RegisterMessageHandler((byte[] data) => secondReceived.Add("binary:" + Hex(data)));
            second.RegisterMessageHandler((string text) => secondReceived.Add("text:" + text));
            second.Start(false);
            return "same as first " + ReferenceEquals(second, client) + "; connected " + second.IsConnected();
        }));
        if (second != null)
        {
            clientReceived.Clear();
            ChannelService.Broadcast(ChannelService.ChannelNameToId(Other), "other channel");
            ChannelService.Broadcast(channelId, "first channel");
            Thread.Sleep(500);
            second.Tick();
            client.Tick();
            Row("second.received", string.Join(" | ", secondReceived));
            Row("second.first_client_received", string.Join(" | ", clientReceived));
            ChannelClient.Close(Other);
            ChannelService.CloseChannel(Other);
        }

        // The Editor's own echo channel.
        ChannelClient echo = null;
        var echoed = new List<string>();
        Row("echo.create", Try(() =>
        {
            echo = ChannelClient.GetOrCreateClient("echo");
            echo.RegisterMessageHandler((string text) => echoed.Add("text:" + text));
            echo.RegisterMessageHandler((byte[] data) => echoed.Add("binary:" + Hex(data)));
            echo.Start(true);
            return "autoTick " + echo.isAutoTick;
        }));
        if (echo != null)
        {
            bool echoConnected = false;
            yield return Until(() => echo.IsConnected(), c => echoConnected = c);
            Row("echo.connected", echoConnected.ToString());
            echo.Send("ping");
            echo.Send(new byte[] { 7 });
            bool answered = false;
            yield return Until(() => echoed.Count >= 2, c => answered = c);
            Row("echo.received", string.Join(" | ", echoed));
            Row("echo.server_handler", string.Join(" | ", serverReceived));
            serverReceived.Clear();
            echo.Close();
        }

        // Closing the client.
        Row("client.close", Try(() => { ChannelClient.Close(Channel); return "connected " + client.IsConnected(); }));
        int left = -1;
        yield return Until(() => (left = ChannelService.GetChannelClientList().Count(c => c.name == Channel)) == 0, _ => { });
        Row("client.list_after_close", left.ToString());
        Row("client.same_after_close", Try(() => ReferenceEquals(ChannelClient.GetOrCreateClient(Channel), client).ToString()));
        ChannelClient.Close(Channel);
    }

    /// <summary>A message to the server: what its handler got after half a second without a frame,
    /// after DispatchMessages, after two frames; then the same after the client's Tick.</summary>
    IEnumerator ToServer(ChannelClient client, string key, Action send)
    {
        serverReceived.Clear();
        Row("to_server." + key + ".send", Try(() => { send(); return "ok"; }));
        Thread.Sleep(500);
        Row("to_server." + key + ".before_dispatch", string.Join(" | ", serverReceived));
        ChannelService.DispatchMessages();
        Row("to_server." + key + ".after_dispatch", string.Join(" | ", serverReceived));
        yield return null;
        yield return null;
        Row("to_server." + key + ".after_frames", string.Join(" | ", serverReceived));
        client.Tick();
        Thread.Sleep(500);
        Row("to_server." + key + ".after_client_tick", string.Join(" | ", serverReceived));
        ChannelService.DispatchMessages();
        Row("to_server." + key + ".after_client_tick_dispatch", string.Join(" | ", serverReceived));
        yield return null;
        yield return null;
        Row("to_server." + key + ".after_client_tick_frames", string.Join(" | ", serverReceived));
    }

    /// <summary>A message to the client: what its handlers got after half a second without a frame,
    /// after frames, then after Tick.</summary>
    IEnumerator ToClient(ChannelClient client, string key, Action send)
    {
        clientReceived.Clear();
        Row("to_client." + key + ".send", Try(() => { send(); return "ok"; }));
        Thread.Sleep(500);
        Row("to_client." + key + ".before_tick", string.Join(" | ", clientReceived));
        yield return null;
        yield return null;
        Row("to_client." + key + ".after_frames", string.Join(" | ", clientReceived));
        client.Tick();
        Row("to_client." + key + ".after_tick", string.Join(" | ", clientReceived));
    }
}
