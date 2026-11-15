using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using Connection = UnityEngine.Networking.PlayerConnection.PlayerConnection;
using Message = UnityEngine.Networking.PlayerConnection.MessageEventArgs;
using UnityEngine.Events;

public class EditorApiProbe : MonoBehaviour
{
    public static Action<int> finished;
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static StreamWriter writer;
    static readonly Dictionary<string, int> hits = new Dictionary<string, int>();
    static int diagnostics;
    sealed class Capture : ILogHandler
    {
        readonly ILogHandler previous;
        public Capture(ILogHandler handler) { previous = handler; }
        public void LogException(Exception exception, Object context) => previous.LogException(exception, context);
        public void LogFormat(LogType type, Object context, string format, params object[] args)
        {
            if (string.Format(format, args).Contains("is not implemented yet")) diagnostics++;
            previous.LogFormat(type, context, format, args);
        }
    }
    readonly List<Action> removals = new List<Action>();
    [Serializable] class Row { public string key; public string value; }
    static void Value(string key, object value) => writer.WriteLine(JsonUtility.ToJson(new Row { key = key, value = value == null ? "<null>" : value.ToString() }));
    static void Check(string key, Func<object> action)
    {
        try { Value(key, action()); }
        catch (Exception e) { while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
            if (key.StartsWith("connection.unregister", StringComparison.Ordinal))
                File.AppendAllText(Path.Combine(Environment.GetEnvironmentVariable("EDITORAPI_PROBE_OUT"), "connection-exceptions.txt"), key + "\n" + e + "\n");
            if (e is ArgumentNullException nullError) Value(key, "ArgumentNullException:" + nullError.ParamName);
            else if (e is ArgumentException argument && argument.ParamName != null)
            {
                string message = argument.Message.Replace("\r", "");
                int end = message.IndexOf("\nParameter name:", StringComparison.Ordinal);
                if (end < 0) end = message.IndexOf(" (Parameter '", StringComparison.Ordinal);
                if (end >= 0) message = message.Substring(0, end);
                Value(key, "ArgumentException:" + message + ":" + argument.ParamName);
            }
            else if (e is NullReferenceException) Value(key, "NullReferenceException");
            else Value(key, e.GetType().Name + ":" + e.Message.Replace("\r", "")); }
    }
    static Type TypeOf(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
    static object Call(string type, string method, params object[] args) => TypeOf(type).GetMethods(All).Single(m => m.Name == method && m.GetParameters().Length == args.Length).Invoke(null, args);
    static void Hit(string key) { hits[key]++; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string output = Environment.GetEnvironmentVariable("EDITORAPI_PROBE_OUT");
        if (output == null) return;
        Directory.CreateDirectory(output);
        writer = new StreamWriter(Path.Combine(output, "values.jsonl")) { AutoFlush = true };
        Value("editorConnection.persistent.beforePlay", File.ReadAllText(Path.Combine(output, "persistent-before-play.txt")));
        new GameObject("EditorApiProbe").AddComponent<EditorApiProbe>();
    }
    IEnumerator Start()
    {
        int code = 0;
        for (int i = 0; i < 30; i++) yield return null;
        Debug.unityLogger.logHandler = new Capture(Debug.unityLogger.logHandler);
        try { Queries(); ThreadQueries(); Check("connection.setup", () => { ConnectionControls(); ConnectionThreadControls(); return "ok"; }); ExtraQueries(); RegistryControls.Run(Check); MenuControls.Run(Check); EditorConnectionControls.Run(Check); SubscribeEvents(); SetChecked("Edit/Play", false); }
        catch (Exception e) { Value("fatal", e.GetType().Name + ":" + e.Message); code = 1; }
        for (int i = 0; i < 30; i++) yield return null;
        foreach (var pair in hits) Value("event.idle." + pair.Key, pair.Value);
        GameObject changed = new GameObject("Undo event control");
        int beforeUndo = hits["ObjectChangeEvents.changesPublished"];
        Check("event.undo.change", () => {
            Undo.RecordObject(changed.transform, "event control");
            changed.transform.localPosition = new Vector3(1, 2, 3);
            Undo.FlushUndoRecordObjects();
            return changed.transform.localPosition.x;
        });
        for (int i = 0; i < 30; i++) yield return null;
        Value("event.undo.published", hits["ObjectChangeEvents.changesPublished"] - beforeUndo);
        foreach (var remove in removals) remove();
        Object.DestroyImmediate(changed);

        Value("complete", code == 0); writer.Dispose();
        if (finished != null) finished(code); else EditorApplication.Exit(code);
    }
    void SubscribeEvents()
    {
        string[] specs = {
            "UnityEditor.SceneManagement.PrefabStage|prefabStageOpened,prefabStageClosing",
            "UnityEditor.PrefabUtility|prefabInstanceReverting",
            "UnityEditor.Lightmapping|bakeStarted,bakeCompleted,bakeCancelled,lightingDataCleared",
            "UnityEditor.ObjectChangeEvents|changesPublished",
            "UnityEditor.EditorTools.ToolManager|activeToolChanged",
            "UnityEditor.Menu|menuChanged",
            "UnityEditor.ClipboardUtility|pastedGameObjects,duplicatedGameObjects",
            "UnityEditor.PackageManager.Events|registeredPackages",
            "UnityEditor.AI.NavMeshEditorHelpers|agentTypeSettingsClicked,areaSettingsClicked,agentRejectedDebugInfoRequestsCountChanged,agentDebugRequestsPending,agentDebugRequestsProcessed",
            "Unity.Profiling.Memory.MemoryProfiler|CreatingMetadata"
        };
        foreach (string spec in specs)
        {
            string[] parts = spec.Split('|'); Type type = TypeOf(parts[0]);
            foreach (string name in parts[1].Split(','))
            {
                string key = type.Name + "." + name;
                Check("event.subscribe." + key, () => {
                    EventInfo evt = type.GetEvent(name, All);
                    MethodInfo add = evt.GetAddMethod(true), remove = evt.GetRemoveMethod(true);
                    var pars = evt.EventHandlerType.GetMethod("Invoke").GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
                    var handler = Expression.Lambda(evt.EventHandlerType, Expression.Call(typeof(EditorApiProbe).GetMethod("Hit", All), Expression.Constant(key)), pars).Compile();
                    hits[key] = 0;
                    int before = diagnostics;
                    add.Invoke(null, new object[] { handler }); add.Invoke(null, new object[] { handler });
                    remove.Invoke(null, new object[] { handler });
                    removals.Add(() => remove.Invoke(null, new object[] { handler }));
                    return diagnostics == before ? "ok" : "stub diagnostics";
                });
            }
        }
    }
    static void ThreadQueries()
    {
        var thread = new System.Threading.Thread(() => {
            Check("thread.asset.empty", () => { bool yes = AssetDatabase.IsOpenForEdit("", out var message); return yes + ":" + message; });
            Check("thread.asset.scene", () => { bool yes = AssetDatabase.IsOpenForEdit("Assets/Scenes/Probe.unity", out var message); return yes + ":" + message; });
            Check("thread.menu.get", () => GetChecked("Edit/Play"));
            Check("thread.menu.set", () => { SetChecked("Edit/Play", false); return "ok"; });
            Check("thread.unsupported.developer", () => Unsupported.IsDeveloperMode());
            Check("thread.unitytype", () => Call("UnityEditor.UnityType", "FindTypeByName", "GameObject") != null);
            Check("thread.inspector.repaint", () => { Call("UnityEditor.InspectorWindow", "RepaintAllInspectors"); return "ok"; });
        });
        thread.Start(); thread.Join();
        Value("menu.afterWorker", GetChecked("Edit/Play"));
    }
    static void ConnectionControls()
    {
        var connection = Connection.instance;
        Value("connection.singleton", ReferenceEquals(connection, Connection.instance));
        Value("connection.flags", connection.hideFlags);
        Value("connection.connected.initial", connection.isConnected);
        if (connection.isConnected) throw new InvalidOperationException("Connection controls require no attached peer");
        Guid id = new Guid("5bb88277-b5bc-40a4-91d2-c86e47903e16");
        UnityAction<Message> callback = message => Hit("connection.message");
        UnityAction<int> state = player => Hit("connection.state");
        hits["connection.message"] = hits["connection.state"] = 0;
        Guid ordinary = new Guid("bd3c24c0-2037-4213-a18b-3c21cf55dfc6");
        Check("connection.ordinary", () => { connection.Register(ordinary, callback); connection.Register(ordinary, callback); connection.Unregister(ordinary, callback); connection.Unregister(ordinary, callback); return "ok"; });
        Check("connection.register", () => { connection.Register(id, callback); connection.Register(id, callback); return "ok"; });
        Check("connection.register.empty", () => { connection.Register(Guid.Empty, callback); return "ok"; });
        Check("connection.register.null", () => { connection.Register(id, null); return "ok"; });
        Check("connection.unregister", () => { connection.Unregister(id, callback); connection.Unregister(id, callback); return "ok"; });
        Check("connection.unregister.unknown", () => { connection.Unregister(new Guid("a52d7730-9994-4e9e-811a-3638b89011eb"), callback); return "ok"; });
        Check("connection.unregister.null", () => { connection.Unregister(id, null); return "ok"; });
        Check("connection.send", () => { connection.Send(id, new byte[] { 1, 2 }); return "ok"; });
        Check("connection.send.null", () => { connection.Send(id, null); return "ok"; });
        Check("connection.send.empty", () => { connection.Send(Guid.Empty, null); return "ok"; });
        Check("connection.trySend", () => connection.TrySend(id, new byte[] { 3 }));
        Check("connection.trySend.empty", () => connection.TrySend(Guid.Empty, null));
        Check("connection.subscribe", () => { connection.RegisterConnection(state); connection.RegisterDisconnection(state); return "ok"; });
        Check("connection.disconnect", () => { connection.DisconnectAll(); return connection.isConnected; });
        Check("connection.unsubscribe", () => { connection.UnregisterConnection(state); connection.UnregisterDisconnection(state); return "ok"; });
    }
    static void ConnectionThreadControls()
    {
        var connection = Connection.instance;
        var known = new Guid("b17ede28-ac2c-4c74-9c0c-382b6e555970");
        var cold = new Guid("a666990b-a63f-4d36-8cc0-866859bd7443");
        UnityAction<Message> handler = message => { };
        connection.Register(known, handler);
        var thread = new System.Threading.Thread(() => {
            Check("thread.connection.singleton", () => ReferenceEquals(connection, Connection.instance));
            Check("thread.connection.connected", () => connection.isConnected);
            Check("thread.connection.register.new", () => { connection.Register(cold, handler); return "ok"; });
            Check("thread.connection.register.known", () => { connection.Register(known, null); return "ok"; });
            Check("thread.connection.send", () => { connection.Send(known, null); return "ok"; });
            Check("thread.connection.trySend", () => connection.TrySend(known, null));
            Check("thread.connection.unregister.unknown", () => { connection.Unregister(cold, handler); return "ok"; });
            Check("thread.connection.disconnect", () => { connection.DisconnectAll(); return "ok"; });
        });
        thread.Start(); thread.Join();
        Check("thread.connection.register.stateAfter", () => { connection.Unregister(known, handler); return "ok"; });
        Check("connection.unregisterConnection.emptyNull", () => { connection.UnregisterConnection(null); return "ok"; });
        Check("event.remove.emptyNull", () => { new UnityEvent<int>().RemoveListener(null); return "ok"; });
    }
    static void TupleControls()
    {
        Type factory = TypeOf("UnityEditor.Tuple");
        for (int length = 1; length <= 8; length++)
        {
            int n = length;
            Check("tuple." + n + ".values", () => {
                MethodInfo create = factory.GetMethods(All).Single(m => m.Name == "Create" && m.GetGenericArguments().Length == n);
                Type[] types = Enumerable.Repeat(typeof(int), n).ToArray();
                object[] values = Enumerable.Range(1, n).Select(i => (object)i).ToArray();
                object a = create.MakeGenericMethod(types).Invoke(null, values);
                object b = create.MakeGenericMethod(types).Invoke(null, values);
                values[n-1] = 100;
                object c = create.MakeGenericMethod(types).Invoke(null, values);
                Value("tuple." + n + ".hash", a.GetHashCode());
                Type structural = a.GetType().GetInterfaces().Single(t => t.FullName == "UnityEditor.IStructuralEquatable");
                Type comparable = a.GetType().GetInterfaces().Single(t => t.FullName == "UnityEditor.IStructuralComparable");
                Type tuple = a.GetType().GetInterfaces().Single(t => t.FullName == "UnityEditor.ITuple");
                var comparer = new AllEqualComparer();
                Value("tuple." + n + ".customEquality", structural.GetMethod("Equals").Invoke(a, new object[] { c, comparer }));
                Value("tuple." + n + ".customHash", structural.GetMethod("GetHashCode").Invoke(a, new object[] { comparer }));
                Value("tuple." + n + ".customCompare", comparable.GetMethod("CompareTo").Invoke(a, new object[] { c, comparer }));
                Value("tuple." + n + ".length", tuple.GetProperty("Length").GetValue(a));
                Check("tuple." + n + ".wrongType", () => ((IComparable)a).CompareTo("wrong"));
                return a + ":" + a.Equals(b) + ":" + a.Equals(c) + ":" + (a.GetHashCode() == b.GetHashCode()) + ":" + ((IComparable)a).CompareTo(c);
            });
        }
    }
    sealed class AllEqualComparer : System.Collections.IEqualityComparer, System.Collections.IComparer
    {
        public new bool Equals(object a, object b) => true;
        public int GetHashCode(object a) => 7;
        public int Compare(object a, object b) => 0;
    }
    static void NestedTuples()
    {
        Type factory = TypeOf("UnityEditor.Tuple");
        Type open = TypeOf("UnityEditor.Tuple`8");
        foreach (int count in new[] { 2, 3, 8 })
        {
            int length = count;
            Check("tuple.rest." + length, () => {
                MethodInfo create = factory.GetMethods(All).Single(m => m.Name == "Create" && m.GetGenericArguments().Length == length);
                object rest = create.MakeGenericMethod(Enumerable.Repeat(typeof(int), length).ToArray()).Invoke(null, Enumerable.Range(8, length).Select(i => (object)i).ToArray());
                Type type = open.MakeGenericType(Enumerable.Repeat(typeof(int), 7).Concat(new[] { rest.GetType() }).ToArray());
                object value = Activator.CreateInstance(type, Enumerable.Range(1, 7).Select(i => (object)i).Concat(new[] { rest }).ToArray());
                Type tuple = type.GetInterfaces().Single(t => t.FullName == "UnityEditor.ITuple");
                return value + ":" + value.GetHashCode() + ":" + tuple.GetProperty("Length").GetValue(value) + ":" + tuple.GetProperty("Item").GetValue(value, new object[] { length + 6 });
            });
        }
        Check("tuple.rest.invalid", () => Activator.CreateInstance(open.MakeGenericType(Enumerable.Repeat(typeof(int), 7).Concat(new[] { typeof(string) }).ToArray()), new object[] { 1, 2, 3, 4, 5, 6, 7, "invalid" }));
    }
    static int customMenuCalls;
    [MenuItem("Tools/Unbound API Probe Toggle _g")]
    static void CustomMenu() { customMenuCalls++; }
    static void ExtraQueries()
    {
        string path = "Packages/com.unbound.api-probe-local/package.json";
        Check("asset.open.localPackage", () => { bool yes = AssetDatabase.IsOpenForEdit(path, out var message); return yes + ":" + message; });
        Check("asset.open.packageObject", () => {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Packages/com.unity.modules.audio/package.json");
            bool yes = AssetDatabase.IsOpenForEdit(asset, out var message);
            return (asset != null) + ":" + yes + ":" + message;
        });
        foreach (string item in new[] { "Packages/com.unity.modules.audio", "Packages/com.unity.modules.audio/package.json.meta", "/tmp/unbound-probe-missing" })
        {
            string query = item;
            Check("asset.open.extra." + query, () => { bool yes = AssetDatabase.IsOpenForEdit(query, out var message); return yes + ":" + message; });
        }
        var list = new List<string> { "stale" };
        Check("asset.open.bulk", () => {
            AssetDatabase.IsOpenForEdit(new[] { "Assets/Scenes/Probe.unity", "Packages/com.unity.modules.audio/package.json", path, "Assets/missing.asset", "Library/missing" }, list);
            return string.Join("|", list);
        });
        Check("asset.open.bulk.empty", () => { list.Add("stale"); AssetDatabase.IsOpenForEdit(new string[0], list); return list.Count; });
        Check("asset.open.bulk.nullPaths", () => { AssetDatabase.IsOpenForEdit(null, list); return "ok"; });
        Check("asset.open.bulk.nullList", () => { AssetDatabase.IsOpenForEdit(new string[0], null); return "ok"; });
        Check("asset.open.bulk.bothNull", () => { AssetDatabase.IsOpenForEdit(null, null); return "ok"; });
        const string menu = "Tools/Unbound API Probe Toggle";
        SetChecked(menu, false); Check("menu.custom.false", () => GetChecked(menu));
        SetChecked(menu, true); Check("menu.custom.true", () => GetChecked(menu));
        SetChecked(menu, false); Check("menu.custom.reset", () => GetChecked(menu));
        Check("menu.custom.execute", () => EditorApplication.ExecuteMenuItem(menu) + ":" + customMenuCalls);
        foreach (string name in new[] { "Edit/Pause", "Edit/Step", "", null })
        {
            string item = name;
            Check("menu.boundary." + (item ?? "null"), () => { SetChecked(item, true); bool state = GetChecked(item); SetChecked(item, false); return state; });
        }
        Check("event.stream.size", () => {
            var method = new System.Reflection.Emit.DynamicMethod("StreamSize", typeof(int), Type.EmptyTypes, typeof(EditorApiProbe), true);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Sizeof, TypeOf("UnityEditor.ObjectChangeEventStream"));
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
            return ((Func<int>)method.CreateDelegate(typeof(Func<int>)))();
        });
        Check("memoryMetadata.description", () => {
            Type type = TypeOf("Unity.Profiling.Memory.MemorySnapshotMetadata");
            object value = Activator.CreateInstance(type);
            var property = type.GetProperty("Description", All);
            string initial = property.GetValue(value) == null ? "<null>" : property.GetValue(value).ToString();
            property.SetValue(value, "probe-description");
            return initial + ":" + property.GetValue(value);
        });
    }
    static bool GetChecked(string name) => (bool)Call("UnityEditor.Menu", "GetChecked", name);
    static void SetChecked(string name, bool value) => Call("UnityEditor.Menu", "SetChecked", name, value);
    static void Queries()
    {
        Check("unsupported.developerMode", () => Unsupported.IsDeveloperMode());
        Check("unsupported.developerBuild", () => Unsupported.IsDeveloperBuild());
        foreach (string path in new[] { "Assets/Scenes/Probe.unity", "Packages/com.unity.modules.audio/package.json", "Assets/missing.asset", "ProjectSettings/ProjectSettings.asset", "Packages/manifest.json", "Library/missing", "", null })
        {
            string key = path ?? "null";
            Check("asset.open." + key, () => { bool result = AssetDatabase.IsOpenForEdit(path, out var message); return result + ":" + (message ?? "<null>"); });
        }
        Check("asset.open.readOnly", () => {
            string file = "Assets/Scenes/Probe.unity";
            var attributes = File.GetAttributes(file);
            try { File.SetAttributes(file, attributes | FileAttributes.ReadOnly); bool yes = AssetDatabase.IsOpenForEdit(file, out var message); return yes + ":" + message; }
            finally { File.SetAttributes(file, attributes); }
        });
        Check("asset.open.nullObject", () => { bool result = AssetDatabase.IsOpenForEdit((Object)null, out var message); return result + ":" + (message ?? "<null>"); });
        foreach (string name in new[] { "GameObject", "Transform", "Texture2D", "MonoBehaviour", "Object", "Component", "Behaviour", "MonoScript", "Camera", "Mesh", "Sprite", "UnityEngine.GameObject", "Missing", "", null })
            Check("unitytype." + (name ?? "null"), () => {
                object value = Call("UnityEditor.UnityType", "FindTypeByName", new object[] { name });
                if (value == null) return null;
                Type type = value.GetType();
                return type.GetProperty("name", All).GetValue(value) + ":" + type.GetProperty("persistentTypeID", All).GetValue(value);
            });
        Check("menu.unknown.initial", () => GetChecked("UnboundProbe/Missing"));
        SetChecked("UnboundProbe/Missing", true);
        Check("menu.unknown.set", () => GetChecked("UnboundProbe/Missing"));
        SetChecked("UnboundProbe/Missing", false);
        Check("menu.known.initial", () => GetChecked("Edit/Play"));
        SetChecked("Edit/Play", false); Check("menu.known.false", () => GetChecked("Edit/Play"));
        SetChecked("Edit/Play", true); Check("menu.known.true", () => GetChecked("Edit/Play"));
        Check("inspector.repaint", () => { Call("UnityEditor.InspectorWindow", "RepaintAllInspectors"); return "ok"; });
        foreach (string name in new[] { "PrefabImporter", "ShaderImporter", "ShaderIncludeImporter" })
            Check("constructor." + name, () => {
                object value = Activator.CreateInstance(TypeOf("UnityEditor." + name), true);
                Object obj = value as Object;
                return value.GetType().Name + ":" + (obj != null);
            });
        Check("constructor.ProjectSettingsBase", () => {
            object value = Activator.CreateInstance(TypeOf("UnityEditor.AudioManager"), true);
            return value.GetType().Name + ":" + ((Object)value != null);
        });
        Type toolbar = TypeOf("UnityEditor.Toolbar");
        Check("toolbar.root.field", () => toolbar.GetField("m_Root", All)?.FieldType.FullName);
        Check("toolbar.root.zones", () => {
            Object[] bars = Resources.FindObjectsOfTypeAll(toolbar);
            if (bars.Length == 0) return "no-toolbar";
            var root = (UnityEngine.UIElements.VisualElement)toolbar.GetField("m_Root", All).GetValue(bars[0]);
            return (root != null) + ":" + (root.Q("ToolbarZoneLeftAlign") != null) + ":" + (root.Q("ToolbarZoneRightAlign") != null);
        });
        Type registry = TypeOf("UnityEditor.CustomEditorAttributes");
        Check("customEditors.registry.members", () => string.Join(",", registry.GetFields(All).Select(f => f.Name).OrderBy(s => s)));
        Check("customEditors.clientShape", () => {
            Type entry = registry.GetNestedType("MonoEditorType", All);
            Type storage = registry.GetNestedType("MonoEditorTypeStorage", All);
            Type cache = registry.GetNestedType("CustomEditorCache", All);
            return registry.GetProperty("instance", All) != null && registry.GetMethod("Rebuild", All) != null && registry.GetField("m_Cache", All) != null
                && storage.GetField("customEditors", All) != null && storage.GetField("customEditorsMultiEdition", All) != null
                && cache.GetField("m_CustomEditorCache", All).FieldType == typeof(Dictionary<,>).MakeGenericType(typeof(Type), storage)
                && entry.GetField("inspectorType", All) != null && entry.GetField("editorForChildClasses", All) != null && entry.GetField("isFallback", All) != null
                && typeof(CustomEditor).GetField("m_EditorForChildClasses", All) != null;
        });
        Check("customEditors.entry.members", () => string.Join(",", registry.GetNestedType("MonoEditorType", All).GetFields(All).Select(f => f.Name).OrderBy(s => s)));
        Type sprite = TypeOf("UnityEditor.SpriteUtilityWindow");
        Func<Object, bool> action = o => o == null;
        Check("sprite.callback", () => { sprite.GetMethod("SetShowSpriteEditorWindowWithObject", All).Invoke(null, new object[] { action }); return sprite.GetMethod("ShowSpriteEditorWindow", All).Invoke(null, new object[] { null }); });
        Check("sprite.callback.clear", () => { sprite.GetMethod("SetShowSpriteEditorWindowWithObject", All).Invoke(null, new object[] { null }); return sprite.GetMethod("ShowSpriteEditorWindow", All).Invoke(null, new object[] { null }); });
    }
}
