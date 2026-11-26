using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $UNITYTLS_OUT (tests/value_probe.py rows): what the Unity TLS binding
/// (Unity.TLS.LowLevel.Binding, internal, called by reflection as Unity Transport calls it)
/// answers. A configuration's defaults; then a server and a client over in-memory pipes (stream
/// and datagram): each handshake call's result, state and handshake state, the cipher suites, an
/// echoed message, and the failures of a wrong CA, a wrong host name and a garbled certificate.
/// </summary>
public unsafe class UnityTlsProbe : MonoBehaviour
{
    const string Variable = "UNITYTLS_OUT";
    static StreamWriter output;

    const string CaPem = @"-----BEGIN CERTIFICATE-----
MIIBbTCCARKgAwIBAgIULh+DNVc/aU1z88X76aL8/Rsp3K8wCgYIKoZIzj0EAwIw
GzEZMBcGA1UEAwwQVW5ib3VuZCBQcm9iZSBDQTAgFw0yNjExMjYxNzM0MzVaGA8y
MTI2MTEwMjE3MzQzNVowGzEZMBcGA1UEAwwQVW5ib3VuZCBQcm9iZSBDQTBZMBMG
ByqGSM49AgEGCCqGSM49AwEHA0IABC9clXzGP2SFtRGQ0bC7tU2xXyMkm9ArWJ5l
affez+H4AkXph6Z4SwwO65pTNUVCkkD8fMy5IWgg8VLdNrvzWJmjMjAwMB0GA1Ud
DgQWBBSqRyHpYzfC60yKMjz08XMRBH//WzAPBgNVHRMBAf8EBTADAQH/MAoGCCqG
SM49BAMCA0kAMEYCIQD0g9zMRrkLMgOh/XnPMOZ14W/1Zea6nVicAiqVuj8HXgIh
AOsAvJhp1LnewQ3WCFyi7ImD933AwNydgB8rexl4xB9/
-----END CERTIFICATE-----";
    const string ServerPem = @"-----BEGIN CERTIFICATE-----
MIIBVjCB/KADAgECAhQJLYKAcgYxOhBiz4Tkn2HjWQchdDAKBggqhkjOPQQDAjAb
MRkwFwYDVQQDDBBVbmJvdW5kIFByb2JlIENBMCAXDTI2MTEyNjE3MzQzNVoYDzIx
MjYxMTAyMTczNDM1WjAUMRIwEAYDVQQDDAlsb2NhbGhvc3QwWTATBgcqhkjOPQIB
BggqhkjOPQMBBwNCAAT9ryvhwQLxMOi81HIbg+RLdYo/uK9+WRL6+hUTnvOA87vS
nSjQ9i77NgMHGXcrrndevfXcK2mvhYNqjN4MXe6WoyMwITAUBgNVHREEDTALggls
b2NhbGhvc3QwCQYDVR0TBAIwADAKBggqhkjOPQQDAgNJADBGAiEAx2FU2VuROU1V
nxsRcvOs3uJN+7bN9eN9kkc2oDz7slkCIQDHVyd5CPCCk2wAjPRDzKIG4TdqngUK
MZr2pXR7BFM9Cw==
-----END CERTIFICATE-----";
    const string ServerKey = @"-----BEGIN EC PRIVATE KEY-----
MHcCAQEEIPCWk9/l867Z5HhMfd68/qQ/OeZQALJnCZqXa1VSOSxJoAoGCCqGSM49
AwEHoUQDQgAE/a8r4cEC8TDovNRyG4PkS3WKP7ivflkS+voVE57zgPO70p0o0PYu
+zYDBxl3K653Xr313Ctpr4WDaozeDF3ulg==
-----END EC PRIVATE KEY-----";
    const string OtherCaPem = @"-----BEGIN CERTIFICATE-----
MIIBXDCCAQKgAwIBAgIUZSbCoMSuFHS9I3mRn6KdB2gi1Y0wCgYIKoZIzj0EAwIw
EzERMA8GA1UEAwwIT3RoZXIgQ0EwIBcNMjYxMTI2MTczNDM1WhgPMjEyNjExMDIx
NzM0MzVaMBMxETAPBgNVBAMMCE90aGVyIENBMFkwEwYHKoZIzj0CAQYIKoZIzj0D
AQcDQgAEdAeCLTkTyPf0CmNy6zlK0jCZRFemiyptZM9VdsxNFgtN14btMyt5DIy6
sIlBe4Jllu94KvxQfxcrM+jIYF7w5aMyMDAwHQYDVR0OBBYEFH+l20CfpIU96WNO
rgMiFhQPNKDNMA8GA1UdEwEB/wQFMAMBAf8wCgYIKoZIzj0EAwIDSAAwRQIhAJKj
Yw9/lfUAAH9G6yFFq+MFu8nHlfYVqEI0zigLYuz7AiBnnIeyviQS8FEZzYeAi6gB
5MDaH1S8yAr9uKkiD5uxtw==
-----END CERTIFICATE-----";

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value)
    {
        output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value ?? "null") + "\"}\n");
        output.Flush();
    }

    static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("UnityTlsProbe").AddComponent<UnityTlsProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        try { Run(); }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + (e.InnerException ?? e).Message); }
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    // The binding, by reflection.
    static Type B;
    static Type ConfigType, ClientType, DataRefType;
    static MethodInfo M(string name) => B.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    static object P(void* p, Type t) => (IntPtr)p;
    static object Ptr(void* p) => (IntPtr)p;

    // Each binding function through a thunk whose pointer parameters are IntPtr (reflection does
    // not pass pointers to it on every runtime).
    static readonly Dictionary<string, System.Reflection.Emit.DynamicMethod> s_Thunks = new Dictionary<string, System.Reflection.Emit.DynamicMethod>();

    static object Call(string name, params object[] args)
    {
        if (!s_Thunks.TryGetValue(name, out var thunk))
        {
            var m = M(name);
            var parameters = m.GetParameters().Select(p => p.ParameterType.IsPointer ? typeof(IntPtr) : p.ParameterType).ToArray();
            var ret = m.ReturnType.IsPointer ? typeof(IntPtr) : m.ReturnType;
            thunk = new System.Reflection.Emit.DynamicMethod("thunk_" + name, ret, parameters, typeof(UnityTlsProbe).Module, true);
            var il = thunk.GetILGenerator();
            for (short i = 0; i < parameters.Length; i++) il.Emit(System.Reflection.Emit.OpCodes.Ldarg, i);
            il.Emit(System.Reflection.Emit.OpCodes.Call, m);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
            s_Thunks[name] = thunk;
        }
        return thunk.Invoke(null, args);
    }
    static uint U(object o) => Convert.ToUInt32(o);

    // In-memory pipes: each side's inbound bytes (stream) or packets (datagram).
    const int WantRead = -0x6900;
    static readonly Dictionary<long, Queue<byte[]>> s_Inbound = new Dictionary<long, Queue<byte[]>>();
    static readonly Dictionary<long, bool> s_Datagram = new Dictionary<long, bool>();
    static readonly Dictionary<long, int> s_Sent = new Dictionary<long, int>();
    static long Peer(long side) => side ^ 1;

    static bool s_NullUser;
    static readonly Dictionary<long, byte[]> s_FirstSend = new Dictionary<long, byte[]>();

    static int Send(IntPtr user, byte* data, UIntPtr length, uint status)
    {
        long side = (long)user;
        var bytes = new byte[(int)length.ToUInt32()];
        Marshal.Copy((IntPtr)data, bytes, 0, bytes.Length);
        if (!s_FirstSend.ContainsKey(side)) s_FirstSend[side] = bytes;
        s_Inbound[Peer(side)].Enqueue(bytes);
        s_Sent[side] = s_Sent.TryGetValue(side, out var n) ? n + 1 : 1;
        return bytes.Length;
    }

    static int Receive(IntPtr user, byte* data, UIntPtr length, uint status)
    {
        long side = (long)user;
        var queue = s_Inbound[side];
        if (queue.Count == 0) return WantRead;
        var packet = queue.Peek();
        int take = Math.Min(packet.Length, (int)length.ToUInt32());
        Marshal.Copy(packet, 0, (IntPtr)data, take);
        if (s_Datagram[side] || take == packet.Length) queue.Dequeue();
        else
        {
            queue.Dequeue();
            var rest = new byte[packet.Length - take];
            Array.Copy(packet, take, rest, 0, rest.Length);
            var items = queue.ToArray();
            queue.Clear();
            queue.Enqueue(rest);
            foreach (var item in items) queue.Enqueue(item);
        }
        return take;
    }

    static readonly List<object> s_Keep = new List<object>();
    static readonly List<GCHandle> s_Pins = new List<GCHandle>();

    static IntPtr Callback(string delegateName, string method)
    {
        var type = B.GetNestedType(delegateName, BindingFlags.Public | BindingFlags.NonPublic);
        var d = Delegate.CreateDelegate(type, typeof(UnityTlsProbe).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic));
        s_Keep.Add(d);
        return Marshal.GetFunctionPointerForDelegate(d);
    }

    static IntPtr Pinned(byte[] bytes)
    {
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        s_Pins.Add(handle);
        return handle.AddrOfPinnedObject();
    }

    static IntPtr Text(string text, bool terminated) => Pinned(Encoding.ASCII.GetBytes(terminated ? text + "\0" : text));

    static int Offset(string field) => (int)Marshal.OffsetOf(ConfigType, field);

    static void SetRef(IntPtr config, string field, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        int at = Offset(field);
        Marshal.WriteIntPtr(config, at + (int)Marshal.OffsetOf(DataRefType, "dataPtr"), Pinned(bytes));
        Marshal.WriteIntPtr(config, at + (int)Marshal.OffsetOf(DataRefType, "dataLen"), (IntPtr)bytes.Length);
    }

    static string Field(IntPtr config, FieldInfo f)
    {
        int at = (int)Marshal.OffsetOf(ConfigType, f.Name);
        var t = f.FieldType;
        if (t == typeof(uint)) return I((uint)Marshal.ReadInt32(config, at));
        if (t == typeof(int)) return I(Marshal.ReadInt32(config, at));
        if (t == typeof(ushort)) return I((ushort)Marshal.ReadInt16(config, at));
        if (t == typeof(IntPtr) || t.IsPointer) return Marshal.ReadIntPtr(config, at) == IntPtr.Zero ? "null" : "set";
        if (t == DataRefType)
            return (Marshal.ReadIntPtr(config, at) == IntPtr.Zero ? "null" : "set") + "/" + I(Marshal.ReadIntPtr(config, at + IntPtr.Size).ToInt64());
        return "?" + t.Name;
    }

    static IntPtr NewConfig(string key)
    {
        var config = Marshal.AllocHGlobal(Marshal.SizeOf(ConfigType));
        // The defaults' row starts from a filled block (what init_config leaves shows), a
        // working configuration from zeros.
        for (int i = 0; i < Marshal.SizeOf(ConfigType); i++) Marshal.WriteByte(config, i, key != null ? (byte)0xCD : (byte)0);
        Call("unitytls_client_init_config", P((void*)config, ConfigType));
        if (key != null)
            foreach (var f in ConfigType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(f => (long)Marshal.OffsetOf(ConfigType, f.Name)))
                Row(key + "." + f.Name, Field(config, f));
        return config;
    }

    /// <summary>A ClientHello record's version, cipher suites and extensions (with the curves and
    /// signature algorithms they list); the random, session id and key shares are left out.</summary>
    static string Hello(byte[] b, bool datagram)
    {
        if (b == null) return "none";
        int i = datagram ? 13 + 12 : 5 + 4;
        if (b.Length < i + 34) return "short " + I(b.Length);
        string version = ((b[i] << 8) | b[i + 1]).ToString("x4");
        i += 2 + 32;
        i += 1 + b[i];
        if (datagram) i += 1 + b[i];
        int cs = (b[i] << 8) | b[i + 1];
        i += 2;
        var suites = new List<string>();
        for (int k = 0; k < cs; k += 2) suites.Add(((b[i + k] << 8) | b[i + k + 1]).ToString("x4"));
        i += cs;
        i += 1 + b[i];
        int end = i + 2 + ((b[i] << 8) | b[i + 1]);
        i += 2;
        var exts = new List<string>();
        while (i + 4 <= end && i + 4 <= b.Length)
        {
            int t = (b[i] << 8) | b[i + 1];
            int l = (b[i + 2] << 8) | b[i + 3];
            string detail = "";
            if (t == 10 || t == 13)
            {
                var v = new List<string>();
                for (int k = 2; k + 1 < l; k += 2) v.Add(((b[i + 4 + k] << 8) | b[i + 4 + k + 1]).ToString("x4"));
                detail = "[" + string.Join(" ", v.ToArray()) + "]";
            }
            exts.Add(I(t) + detail);
            i += 4 + l;
        }
        return "version " + version + " suites " + string.Join(" ", suites.ToArray()) + " | extensions " + string.Join(",", exts.ToArray());
    }

    struct Side { public IntPtr Client; public long Id; }

    static string Fields(IntPtr client) =>
        I((uint)Marshal.ReadInt32(client, (int)Marshal.OffsetOf(ClientType, "role"))) + ","
        + I((uint)Marshal.ReadInt32(client, (int)Marshal.OffsetOf(ClientType, "state"))) + ","
        + (Marshal.ReadIntPtr(client, (int)Marshal.OffsetOf(ClientType, "ctx")) == IntPtr.Zero ? "noctx" : "ctx");

    static Side Make(string key, uint role, bool datagram, long id, Action<IntPtr> configure, bool steps)
    {
        var config = NewConfig(null);
        Marshal.WriteInt32(config, Offset("transportProtocol"), datagram ? 1 : 0);
        Marshal.WriteIntPtr(config, Offset("dataSendCB"), Callback("unitytls_client_data_send_callback", nameof(Send)));
        Marshal.WriteIntPtr(config, Offset("dataReceiveCB"), Callback("unitytls_client_data_receive_callback", nameof(Receive)));
        Marshal.WriteIntPtr(config, Offset("transportUserData"), (IntPtr)id);
        Marshal.WriteInt32(config, Offset("handshakeReturnsOnStep"), steps ? 1 : 0);
        configure(config);
        if (s_NullUser && role == 2) Marshal.WriteIntPtr(config, Offset("transportUserData"), IntPtr.Zero);
        s_Inbound[id] = new Queue<byte[]>();
        s_Datagram[id] = datagram;
        s_Sent[id] = 0;
        var client = (IntPtr)Call("unitytls_client_create", role, P((void*)config, ConfigType));
        Row(key + ".created", client == IntPtr.Zero ? "null" : "set");
        if (client == IntPtr.Zero) return new Side { Client = client, Id = id };
        // The client's own fields (role, state, handshake state) before and after init; the
        // binding's queries read its context, which init makes.
        Row(key + ".fields.created", Fields(client));
        Row(key + ".init", I(Convert.ToInt64(Call("unitytls_client_init", P((void*)client, ClientType)))));
        Row(key + ".fields.init", Fields(client));
        Row(key + ".role", I(U(Call("unitytls_client_get_role", P((void*)client, ClientType)))));
        Row(key + ".state.init", I(U(Call("unitytls_client_get_state", P((void*)client, ClientType)))));
        int count = Convert.ToInt32(Call("unitytls_client_get_ciphersuite_cnt", P((void*)client, ClientType)));
        var suites = new List<string>();
        for (int i = 0; i < count; i++) suites.Add(I(U(Call("unitytls_client_get_ciphersuite", P((void*)client, ClientType), i))));
        Row(key + ".ciphersuites", I(count) + ":" + string.Join(",", suites.ToArray()));
        return new Side { Client = client, Id = id };
    }

    // The sides that have made a handshake call: before one, the handshake state is whatever the
    // client's memory held (the binding leaves it unset).
    static readonly HashSet<long> s_Shook = new HashSet<long>();

    static string Status(Side s) =>
        I(U(Call("unitytls_client_get_state", P((void*)s.Client, ClientType)))) + ":"
        + (s_Shook.Contains(s.Id) ? I(U(Call("unitytls_client_get_handshake_state", P((void*)s.Client, ClientType)))) : "-");

    static string Errors(Side s)
    {
        ulong reserved = 0;
        return I(U(Call("unitytls_client_get_errorsState", P((void*)s.Client, ClientType), Ptr(&reserved)))) + "/" + I((long)reserved);
    }

    /// <summary>Handshake calls in turns (client first) until neither changes or 400 calls.</summary>
    static void Handshake(string key, Side client, Side server)
    {
        var c = new List<string>();
        var sv = new List<string>();
        for (int i = 0; i < 200; i++)
        {
            foreach (var (side, log) in new[] { (client, c), (server, sv) })
            {
                uint state = U(Call("unitytls_client_get_state", P((void*)side.Client, ClientType)));
                if (state != 1 && state != 2) continue;
                uint r = U(Call("unitytls_client_handshake", P((void*)side.Client, ClientType)));
                s_Shook.Add(side.Id);
                log.Add(I(r) + "@" + Status(side));
            }
            bool cDone = U(Call("unitytls_client_get_state", P((void*)client.Client, ClientType))) >= 3;
            bool sDone = U(Call("unitytls_client_get_state", P((void*)server.Client, ClientType))) >= 3;
            if (cDone && sDone) break;
        }
        Row(key + ".client.steps", string.Join(" ", c.ToArray()));
        Row(key + ".server.steps", string.Join(" ", sv.ToArray()));
        Row(key + ".client.final", Status(client) + " errors " + Errors(client));
        Row(key + ".server.final", Status(server) + " errors " + Errors(server));
        Row(key + ".packets", I(s_Sent[client.Id]) + "," + I(s_Sent[server.Id]));
    }

    static void Echo(string key, Side from, Side to, string message)
    {
        var bytes = Encoding.ASCII.GetBytes(message);
        fixed (byte* p = bytes)
            Row(key + ".send", I(U(Call("unitytls_client_send_data", P((void*)from.Client, ClientType), Ptr(p), (UIntPtr)bytes.Length))));
        var buffer = new byte[256];
        UIntPtr read = UIntPtr.Zero;
        uint r;
        fixed (byte* p = buffer)
            r = U(Call("unitytls_client_read_data", P((void*)to.Client, ClientType), Ptr(p), (UIntPtr)buffer.Length, Ptr(&read)));
        Row(key + ".read", I(r) + "/" + I((long)read.ToUInt64()) + "/" + Encoding.ASCII.GetString(buffer, 0, (int)read.ToUInt64()));
        fixed (byte* p = buffer)
            r = U(Call("unitytls_client_read_data", P((void*)to.Client, ClientType), Ptr(p), (UIntPtr)buffer.Length, Ptr(&read)));
        Row(key + ".readAgain", I(r) + "/" + I((long)read.ToUInt64()));
    }

    static void Destroy(Side s)
    {
        if (s.Client != IntPtr.Zero) Call("unitytls_client_destroy", P((void*)s.Client, ClientType));
    }

    static void Scenario(string key, bool datagram, bool steps, Action<IntPtr> server, Action<IntPtr> client, bool echo)
    {
        var sv = Make(key + ".server", 1, datagram, 2, server, steps);
        var cl = Make(key + ".client", 2, datagram, 3, client, steps);
        if (sv.Client != IntPtr.Zero && cl.Client != IntPtr.Zero)
        {
            Handshake(key, cl, sv);
            if (key.EndsWith(".cert"))
                Row(key + ".client.hello", Hello(s_FirstSend.TryGetValue(cl.Id, out var hello) ? hello : null, datagram));
            if (echo)
            {
                Echo(key + ".toServer", cl, sv, "hello server");
                Echo(key + ".toClient", sv, cl, "hello client");
            }
        }
        Destroy(cl);
        Destroy(sv);
        s_Shook.Clear();
        s_FirstSend.Clear();
    }

    static void Run()
    {
        B = Type.GetType("Unity.TLS.LowLevel.Binding, UnityEngine.TLSModule");
        Row("binding", B == null ? "missing" : "found");
        ConfigType = B.GetNestedType("unitytls_client_config", BindingFlags.Public | BindingFlags.NonPublic);
        ClientType = B.GetNestedType("unitytls_client", BindingFlags.Public | BindingFlags.NonPublic);
        DataRefType = B.GetNestedType("unitytls_dataRef", BindingFlags.Public | BindingFlags.NonPublic);
        Row("sizes", I(Marshal.SizeOf(ConfigType)) + "," + I(Marshal.SizeOf(ClientType)));
        NewConfig("config.default");

        Action<IntPtr> serverCert = c => { SetRef(c, "serverPEM", ServerPem); SetRef(c, "privateKeyPEM", ServerKey); };
        Action<IntPtr> clientCa = c => { SetRef(c, "caPEM", CaPem); Marshal.WriteIntPtr(c, Offset("hostname"), Text("localhost", true)); };
        foreach (var datagram in new[] { false, true })
        {
            var p = datagram ? "dtls" : "tls";
            Scenario(p + ".cert", datagram, false, serverCert, clientCa, true);
            Scenario(p + ".steps", datagram, true, serverCert, clientCa, false);
            Scenario(p + ".wrongCa", datagram, false, serverCert, c => { SetRef(c, "caPEM", OtherCaPem); Marshal.WriteIntPtr(c, Offset("hostname"), Text("localhost", true)); }, false);
            Scenario(p + ".wrongHost", datagram, false, serverCert, c => { SetRef(c, "caPEM", CaPem); Marshal.WriteIntPtr(c, Offset("hostname"), Text("example.com", true)); }, false);
            Scenario(p + ".noCa", datagram, false, serverCert, c => Marshal.WriteIntPtr(c, Offset("hostname"), Text("localhost", true)), false);
            Scenario(p + ".garbledCert", datagram, false, c => { SetRef(c, "serverPEM", ServerPem.Replace("MII", "XXX")); SetRef(c, "privateKeyPEM", ServerKey); }, clientCa, false);
            Scenario(p + ".clientAuthRequired", datagram, false, c => { serverCert(c); SetRef(c, "caPEM", CaPem); Marshal.WriteInt32(c, Offset("clientAuth"), 2); }, clientCa, false);
            // A client without transport user data (its callbacks then get none).
            Scenario(p + ".nullUser", datagram, false, serverCert, c => { clientCa(c); s_NullUser = true; }, false);
            s_NullUser = false;
            // The client's own clientAuth: none, optional and required, without a CA and with the
            // wrong one (whether the client then verifies the server). A client without a host
            // name is left out: the Editor's answer varies between runs.
            foreach (var (name, auth) in new[] { ("None", 0), ("Optional", 1), ("Required", 2) })
            {
                int a = auth;
                Scenario(p + ".clientAuth" + name + ".noCa", datagram, false, serverCert, c => { Marshal.WriteInt32(c, Offset("clientAuth"), a); Marshal.WriteIntPtr(c, Offset("hostname"), Text("localhost", true)); }, true);
                Scenario(p + ".clientAuth" + name + ".wrongCa", datagram, false, serverCert, c => { Marshal.WriteInt32(c, Offset("clientAuth"), a); SetRef(c, "caPEM", OtherCaPem); Marshal.WriteIntPtr(c, Offset("hostname"), Text("localhost", true)); }, false);
            }
        }
        var psk = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        Action<IntPtr> pskConfig = c =>
        {
            int at = Offset("psk");
            Marshal.WriteIntPtr(c, at + (int)Marshal.OffsetOf(DataRefType, "dataPtr"), Pinned(psk));
            Marshal.WriteIntPtr(c, at + (int)Marshal.OffsetOf(DataRefType, "dataLen"), (IntPtr)psk.Length);
            SetRef(c, "pskIdentity", "probe-identity");
        };
        Scenario("dtls.psk", true, false, pskConfig, pskConfig, true);
        foreach (var pin in s_Pins) pin.Free();
    }
}
