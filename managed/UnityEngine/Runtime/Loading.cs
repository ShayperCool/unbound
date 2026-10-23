using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>The objects of one loaded file (scene, prefab or asset), by file id. References inside
    /// the file resolve here, others go to the asset database.</summary>
    internal sealed class LoadedFile : IReferenceResolver
    {
        internal string Path;
        internal string Guid;
        internal readonly Dictionary<long, Object> Objects = new Dictionary<long, Object>();
        internal readonly List<GameObject> Roots = new List<GameObject>();
        internal Object MainObject;
        /// <summary>The file ids are Unbound's, not Unity's (a ScriptedImporter's objects): references
        /// resolve by the objects themselves (<see cref="AssetDatabaseRuntime.Load"/>).</summary>
        internal bool UnknownFileIds;
        /// <summary>The identifiers a ScriptedImporter gave its objects.</summary>
        internal readonly Dictionary<Object, string> Identifiers = new Dictionary<Object, string>();

        public Object Resolve(ObjectRef reference, Type expected)
        {
            if (reference.IsNull) return null;
            if (reference.IsLocal || string.Equals(reference.Guid, Guid, StringComparison.OrdinalIgnoreCase))
                return Objects.TryGetValue(reference.FileId, out var local) ? local : null;
            return AssetDatabaseRuntime.Load(reference.Guid, reference.FileId, expected);
        }
    }

    /// <summary>Engine classes by Unity class id, for the objects serialized data names.</summary>
    internal static class EngineTypes
    {
        static readonly Dictionary<int, Type> s_ByClassId = new Dictionary<int, Type>();
        static readonly HashSet<int> s_Warned = new HashSet<int>();

        internal const int GameObjectId = 1;
        internal const int TransformId = 4;
        internal const int RectTransformId = 224;
        internal const int MonoBehaviourId = 114;
        internal const int PrefabInstanceId = 1001;

        internal static void Register(int classId, Type type) => s_ByClassId[classId] = type;

        internal static Type ForClassId(int classId, string typeName)
        {
            if (s_ByClassId.TryGetValue(classId, out var type)) return type;
            lock (s_Warned)
            {
                if (s_Warned.Add(classId))
                    Debug.LogWarning($"Unbound: {typeName} (class {classId}) is not supported yet; such objects are skipped.");
            }
            return null;
        }

        static EngineTypes()
        {
            Register(TransformId, typeof(Transform));
            Register(RectTransformId, typeof(RectTransform));
            foreach (var type in typeof(Object).Assembly.GetTypes())
            {
                foreach (ClassIdAttribute attribute in type.GetCustomAttributes(typeof(ClassIdAttribute), false))
                    s_ByClassId[attribute.Id] = type;
            }
        }
    }

    /// <summary>Marks an engine class with the Unity class id its serialized documents carry.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    internal sealed class ClassIdAttribute : Attribute
    {
        internal readonly int Id;
        internal ClassIdAttribute(int id) { Id = id; }
    }

    /// <summary>Builds objects from the documents of a scene, prefab or asset file.</summary>
    internal static class ObjectGraphBuilder
    {
        internal enum Kind
        {
            Scene,
            PrefabAsset,
            Asset,
        }

        internal static LoadedFile Build(List<YDocument> documents, string path, string guid, Kind kind, SceneData scene)
        {
            var file = new LoadedFile { Path = path, Guid = guid };
            var gameObjects = new Dictionary<long, (GameObject Go, YDocument Doc)>();
            var components = new List<(Component Component, YDocument Doc)>();
            var assets = new List<(Object Asset, YDocument Doc)>();
            bool isAsset = kind != Kind.Scene;
            YDocument sceneRoots = null;

            using (isAsset ? ObjectRegistry.AsAssets() : default)
            {
                // GameObjects first: components attach to them.
                foreach (var doc in documents)
                {
                    if (doc.Stripped || doc.ClassId != EngineTypes.GameObjectId) continue;
                    var body = doc.Body;
                    var go = new GameObject(body.Str("m_Name"), bare: true)
                    {
                        m_IsActive = body.Bool("m_IsActive", true),
                        m_Layer = body.Int("m_Layer"),
                        m_Tag = body.Str("m_TagString", "Untagged"),
                        m_IsPrefabAsset = kind == Kind.PrefabAsset,
                        IsPersistentAsset = isAsset,
                    };
                    go.isStatic = body.Long("m_StaticEditorFlags") != 0;
                    if (scene != null) go.m_Scene = scene.Handle;
                    gameObjects[doc.FileId] = (go, doc);
                    file.Objects[doc.FileId] = go;
                }

                foreach (var doc in documents)
                {
                    if (doc.Stripped || doc.ClassId == EngineTypes.GameObjectId) continue;
                    if (doc.ClassId == EngineTypes.PrefabInstanceId) continue;
                    if (doc.ClassId == 1660057539) { sceneRoots = doc; continue; } // SceneRoots
                    if (SceneSettings.IsSettingsClass(doc.ClassId)) continue;
                    var body = doc.Body;
                    var owner = body.Ref("m_GameObject");
                    if (!owner.IsNull)
                    {
                        if (!gameObjects.TryGetValue(owner.FileId, out var entry)) continue;
                        var type = ComponentTypeOf(doc);
                        if (type == null) continue;
                        Component c;
                        try
                        {
                            c = ComponentFactory.Create(type, entry.Go);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                            continue;
                        }
                        c.IsPersistentAsset = isAsset;
                        if (c is Transform t) entry.Go.m_Transform = t;
                        file.Objects[doc.FileId] = c;
                        components.Add((c, doc));
                        continue;
                    }
                    var asset = CreateAsset(doc);
                    if (asset == null) continue;
                    asset.IsPersistentAsset = isAsset;
                    file.Objects[doc.FileId] = asset;
                    assets.Add((asset, doc));
                }
            }

            // Component order as each GameObject lists it, the Transform first.
            foreach (var (go, doc) in gameObjects.Values)
            {
                foreach (var item in doc.Body.Get("m_Component")?.Seq() ?? Array.Empty<YNode>())
                {
                    var reference = item.Ref("component");
                    if (file.Objects.TryGetValue(reference.FileId, out var o) && o is Component c && c.m_GameObject == go)
                        go.m_Components.Add(c);
                }
                // Components the list does not name (added by prefab overrides) keep document order.
                foreach (var (c, _) in components)
                    if (c.m_GameObject == go && !go.m_Components.Contains(c))
                        go.m_Components.Add(c);
                if (go.m_Transform == null)
                {
                    go.AddTransform(typeof(Transform));
                }
                else
                {
                    go.m_Components.Remove(go.m_Transform);
                    go.m_Components.Insert(0, go.m_Transform);
                }
            }

            BuildHierarchy(file, components, gameObjects, sceneRoots);

            // Data.
            foreach (var (c, doc) in components)
            {
                try
                {
                    ReadComponent(c, doc.Body, file);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Unbound: reading {doc.TypeName} of '{c.m_GameObject?.m_Name}' in {path}: {e}");
                }
            }
            foreach (var (asset, doc) in assets)
            {
                try
                {
                    ReadAsset(asset, doc, file);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Unbound: reading {doc.TypeName} {doc.FileId} in {path}: {e}");
                }
            }
            foreach (var (go, _) in gameObjects.Values)
                go.m_Transform.MarkChanged();

            // The main object: a prefab's root GameObject; an asset file's first object.
            if (kind == Kind.PrefabAsset) file.MainObject = file.Roots.Count > 0 ? file.Roots[0] : null;
            else if (kind == Kind.Asset && assets.Count > 0) file.MainObject = assets[0].Asset;
            foreach (var (asset, _) in assets)
                if (asset is ScriptableObject so) so.RunCreationCallbacks();
            return file;
        }

        static Type ComponentTypeOf(YDocument doc)
        {
            if (doc.ClassId == EngineTypes.MonoBehaviourId)
            {
                var script = doc.Body.Ref("m_Script");
                var type = GameDomain.ResolveScript(script.Guid, script.FileId);
                if (type == null)
                {
                    MissingScripts.Report(script, doc);
                    return null;
                }
                return typeof(Component).IsAssignableFrom(type) ? type : null;
            }
            var engine = EngineTypes.ForClassId(doc.ClassId, doc.TypeName);
            return engine != null && typeof(Component).IsAssignableFrom(engine) ? engine : null;
        }

        static Object CreateAsset(YDocument doc)
        {
            if (doc.ClassId == EngineTypes.MonoBehaviourId)
            {
                var script = doc.Body.Ref("m_Script");
                var type = GameDomain.ResolveScript(script.Guid, script.FileId);
                if (type == null)
                {
                    MissingScripts.Report(script, doc);
                    return null;
                }
                if (!typeof(ScriptableObject).IsAssignableFrom(type)) return null;
                var so = ScriptableObject.CreateInstanceWithoutCallbacks(type);
                if (so != null) so.m_Name = doc.Body.Str("m_Name");
                return so;
            }
            var engine = EngineTypes.ForClassId(doc.ClassId, doc.TypeName);
            if (engine == null || typeof(Component).IsAssignableFrom(engine)) return null;
            var asset = (Object)Activator.CreateInstance(engine, nonPublic: true);
            asset.m_Name = doc.Body.Str("m_Name");
            return asset;
        }

        static void ReadComponent(Component c, YNode body, LoadedFile file)
        {
            if (c is Behaviour b) b.m_Enabled = body.Bool("m_Enabled", true);
            if (c is MonoBehaviour)
                new YamlReader(file).ReadScriptObject(c, body);
            else
                c.ReadSerialized(body, file);
        }

        static void ReadAsset(Object asset, YDocument doc, LoadedFile file)
        {
            if (asset is ScriptableObject)
                new YamlReader(file).ReadScriptObject(asset, doc.Body);
            else if (asset is ISerializedAsset serialized)
                serialized.ReadSerialized(doc.Body, file);
        }

        static void BuildHierarchy(LoadedFile file, List<(Component Component, YDocument Doc)> components,
            Dictionary<long, (GameObject Go, YDocument Doc)> gameObjects, YDocument sceneRoots)
        {
            var transforms = new List<(Transform T, YNode Body)>();
            foreach (var (c, doc) in components)
                if (c is Transform t) transforms.Add((t, doc.Body));
            var bodies = new Dictionary<Transform, YNode>();
            foreach (var (t, body) in transforms)
            {
                bodies[t] = body;
                var father = body.Ref("m_Father");
                t.m_Parent = !father.IsNull && file.Objects.TryGetValue(father.FileId, out var p) ? p as Transform : null;
            }
            foreach (var (t, body) in transforms)
            {
                foreach (var item in body.Get("m_Children")?.Seq() ?? Array.Empty<YNode>())
                {
                    var r = item.AsRef();
                    if (file.Objects.TryGetValue(r.FileId, out var o) && o is Transform child && child.m_Parent == t && !t.m_Children.Contains(child))
                        t.m_Children.Add(child);
                }
            }
            // Children whose parent does not list them.
            foreach (var (t, _) in transforms)
                if (t.m_Parent != null && !t.m_Parent.m_Children.Contains(t))
                    t.m_Parent.m_Children.Add(t);

            var roots = new List<Transform>();
            if (sceneRoots != null)
            {
                foreach (var item in sceneRoots.Body.Get("m_Roots")?.Seq() ?? Array.Empty<YNode>())
                    if (file.Objects.TryGetValue(item.AsRef().FileId, out var o) && o is Transform root && root.m_Parent == null)
                        roots.Add(root);
            }
            var unlisted = new List<(Transform T, int Order)>();
            foreach (var (t, body) in transforms)
                if (t.m_Parent == null && !roots.Contains(t))
                    unlisted.Add((t, body.Int("m_RootOrder")));
            unlisted.Sort((a, b) => a.Order.CompareTo(b.Order));
            foreach (var (t, _) in unlisted) roots.Add(t);
            // GameObjects without a Transform document got one; they are roots.
            foreach (var (go, _) in gameObjects.Values)
                if (go.m_Transform != null && go.m_Transform.m_Parent == null && !roots.Contains(go.m_Transform))
                    roots.Add(go.m_Transform);
            foreach (var root in roots) file.Roots.Add(root.m_GameObject);
        }
    }

    /// <summary>Objects that are not components and read their own serialized data.</summary>
    internal interface ISerializedAsset
    {
        void ReadSerialized(YNode body, IReferenceResolver resolver);
    }

    internal static class MissingScripts
    {
        static readonly HashSet<string> s_Reported = new HashSet<string>();

        internal static void Report(ObjectRef script, YDocument doc)
        {
            var key = $"{script.Guid}:{script.FileId}";
            lock (s_Reported)
            {
                if (!s_Reported.Add(key)) return;
            }
            Debug.LogWarning($"The referenced script (guid {script.Guid}, fileID {script.FileId}) on this Behaviour is missing! " +
                             $"({doc.TypeName} {doc.FileId}{AssetDatabaseRuntime.DescribeGuid(script.Guid)})");
        }
    }

    /// <summary>Scenes: builds the scene's objects into the scene and wakes them.</summary>
    internal static class SceneLoader
    {
        internal static void LoadInto(SceneData scene)
        {
            List<YDocument> documents;
            try
            {
                documents = Native.LoadDocuments(scene.Path);
            }
            catch (Exception e)
            {
                Debug.LogError($"Unbound: could not load scene {scene.Path}: {e.Message}");
                return;
            }
            var file = ObjectGraphBuilder.Build(documents, scene.Path, null, ObjectGraphBuilder.Kind.Scene, scene);
            foreach (var doc in documents)
                if (!doc.Stripped) SceneSettings.Apply(doc);
            foreach (var root in file.Roots) scene.Roots.Add(root);

            var active = new List<Component>();
            foreach (var root in file.Roots) CollectActive(root.m_Transform, active);
            Lifecycle.Activate(active);
        }

        static void CollectActive(Transform t, List<Component> into)
        {
            var go = t.m_GameObject;
            if (!go.m_IsActive) return;
            foreach (var c in go.m_Components)
                if (!c.m_Destroyed) into.Add(c);
            foreach (var child in t.m_Children) CollectActive(child, into);
        }
    }

    /// <summary>Scene-level settings documents (RenderSettings, LightmapSettings, ...).</summary>
    internal static class SceneSettings
    {
        internal static event Action<YDocument> Loaded;

        /// <summary>OcclusionCullingSettings, RenderSettings, LightmapSettings, NavMeshSettings.</summary>
        internal static bool IsSettingsClass(int classId) => classId == 29 || classId == 104 || classId == 157 || classId == 196;

        internal static void Apply(YDocument doc)
        {
            if (IsSettingsClass(doc.ClassId))
                Loaded?.Invoke(doc);
        }
    }

    /// <summary>Assets by GUID: YAML assets and prefabs are built from their documents, imported
    /// assets (textures, audio, models, text) through their importers. Each object is created once.</summary>
    internal static class AssetDatabaseRuntime
    {
        static readonly Dictionary<string, LoadedFile> s_Files = new Dictionary<string, LoadedFile>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<(string, long), Object> s_Objects = new Dictionary<(string, long), Object>();
        static readonly HashSet<string> s_Loading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Loaders of imported asset kinds (textures, audio, ...), by kind name.</summary>
        internal static readonly Dictionary<string, Func<AssetInfo, LoadedFile>> Importers = new Dictionary<string, Func<AssetInfo, LoadedFile>>();

        internal sealed class AssetInfo
        {
            public string Guid { get; set; }
            public string Path { get; set; }
            /// <summary>The file on disk.</summary>
            public string File { get; set; }
            public string Kind { get; set; }
            public long MainFileId { get; set; }
            /// <summary>The script GUID of the asset's ScriptedImporter, for kind "scripted".</summary>
            public string Importer { get; set; }
        }

        static readonly JsonSerializerOptions s_Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        internal static AssetInfo Info(string guid)
        {
            var text = Native.Call("asset-info", JsonSerializer.Serialize(new { guid }, s_Json));
            return JsonSerializer.Deserialize<AssetInfo>(text, s_Json);
        }

        internal static AssetInfo InfoByPath(string path)
        {
            var text = Native.Call("asset-info", JsonSerializer.Serialize(new { path }, s_Json));
            return JsonSerializer.Deserialize<AssetInfo>(text, s_Json);
        }

        internal static string DescribeGuid(string guid)
        {
            if (guid == null || !Native.IsReady) return "";
            try { return ", script " + Info(guid)?.Path; }
            catch (Exception) { return ""; }
        }

        /// <summary>The object `{fileID, guid}` names; null when it does not exist.</summary>
        internal static Object Load(string guid, long fileId, Type expected)
        {
            if (guid == null) return null;
            if (guid.StartsWith("0000000000000000", StringComparison.Ordinal))
                return BuiltinShaders.Resolve(guid, fileId);
            if (s_Objects.TryGetValue((guid, fileId), out var cached) && !cached.m_Destroyed) return cached;
            var file = LoadFile(guid);
            if (file == null) return null;
            if (file.Objects.TryGetValue(fileId, out var obj)) return obj;
            if (file.UnknownFileIds && fileId != 0)
            {
                obj = ByProducedObjects(file, expected);
                if (obj != null) s_Objects[(guid, fileId)] = obj;
            }
            return obj;
        }

        /// <summary>The object a reference into a ScriptedImporter's asset means, when only one fits:
        /// the asset's only object, or its only object of the expected type.</summary>
        static Object ByProducedObjects(LoadedFile file, Type expected)
        {
            Object match = null;
            int count = 0;
            foreach (var o in file.Objects.Values)
            {
                if (expected != null && expected != typeof(Object) && !expected.IsInstanceOfType(o)) continue;
                match = o;
                count++;
            }
            if (count == 1) return match;
            if (file.Objects.Count == 1)
                foreach (var o in file.Objects.Values) return o;
            Debug.LogError($"Unbound: a reference into {file.Path} matches {count} of the objects its importer made " +
                $"({string.Join(", ", file.Identifiers.Values)}); Unbound cannot tell which one Unity's file id names.");
            return null;
        }

        /// <summary>The main object of an asset (Resources.Load, AssetDatabase.LoadAssetAtPath).</summary>
        internal static Object LoadMain(string guid, Type expected)
        {
            var file = LoadFile(guid);
            if (file == null) return null;
            if (file.MainObject != null && (expected == null || expected.IsInstanceOfType(file.MainObject)))
                return file.MainObject;
            if (expected != null)
            {
                if (expected == typeof(GameObject) || typeof(Component).IsAssignableFrom(expected))
                {
                    if (file.MainObject is GameObject root)
                        return expected == typeof(GameObject) ? root : root.GetComponent(expected);
                }
                foreach (var o in file.Objects.Values)
                    if (expected.IsInstanceOfType(o)) return o;
            }
            return null;
        }

        /// <summary>The objects of an asset file as Resources.LoadAll and AssetDatabase list them:
        /// the main object and its sub-assets; for a prefab, its root only.</summary>
        internal static IEnumerable<Object> LoadAll(string guid)
        {
            var file = LoadFile(guid);
            if (file == null) return Array.Empty<Object>();
            if (file.MainObject is GameObject root && root.m_IsPrefabAsset) return new Object[] { root };
            var list = new List<Object>();
            if (file.MainObject != null) list.Add(file.MainObject);
            foreach (var o in file.Objects.Values)
                if (o != file.MainObject && !(o is Component) && !(o is GameObject)) list.Add(o);
            return list;
        }

        /// <summary>Where a loaded asset object comes from.</summary>
        internal static bool TryGetLocation(Object obj, out string guid, out long fileId)
        {
            foreach (var pair in s_Objects)
            {
                if (pair.Value == obj)
                {
                    (guid, fileId) = pair.Key;
                    return true;
                }
            }
            guid = null;
            fileId = 0;
            return false;
        }

        internal static string PathOf(string guid) => s_Files.TryGetValue(guid, out var f) && f != null ? f.Path : null;

        static LoadedFile LoadFile(string guid)
        {
            if (s_Files.TryGetValue(guid, out var loaded)) return loaded;
            if (!s_Loading.Add(guid))
            {
                Debug.LogError($"Unbound: asset {guid} references itself while loading.");
                return null;
            }
            try
            {
                AssetInfo info;
                try
                {
                    info = Info(guid);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Unbound: asset {guid}: {e.Message}");
                    s_Files[guid] = null;
                    return null;
                }
                LoadedFile file = null;
                switch (info.Kind)
                {
                    case "prefab":
                        file = ObjectGraphBuilder.Build(Native.LoadDocuments(guid), info.Path, guid, ObjectGraphBuilder.Kind.PrefabAsset, null);
                        break;
                    case "yaml":
                        file = ObjectGraphBuilder.Build(Native.LoadDocuments(guid), info.Path, guid, ObjectGraphBuilder.Kind.Asset, null);
                        if (info.MainFileId != 0 && file.Objects.TryGetValue(info.MainFileId, out var main)) file.MainObject = main;
                        break;
                    default:
                        if (Importers.TryGetValue(info.Kind ?? "", out var importer))
                        {
                            try
                            {
                                file = importer(info);
                            }
                            catch (Exception e)
                            {
                                Debug.LogWarning($"Unbound: importing {info.Path}: {e.Message}");
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"Unbound: assets of kind '{info.Kind}' are not supported yet ({info.Path}).");
                        }
                        break;
                }
                if (file != null)
                {
                    file.Guid = guid;
                    file.Path = info.Path;
                    foreach (var pair in file.Objects)
                    {
                        s_Objects[(guid, pair.Key)] = pair.Value;
                        if (string.IsNullOrEmpty(pair.Value.m_Name) && pair.Value == file.MainObject)
                            pair.Value.m_Name = System.IO.Path.GetFileNameWithoutExtension(info.Path);
                    }
                }
                s_Files[guid] = file;
                return file;
            }
            finally
            {
                s_Loading.Remove(guid);
            }
        }

        /// <summary>Forgets every loaded asset (a domain reload or a content change).</summary>
        internal static void Clear()
        {
            s_Files.Clear();
            s_Objects.Clear();
        }

        /// <summary>Forgets one file so that the next load reads it again (hot reload of content).</summary>
        internal static void Invalidate(string guid)
        {
            if (s_Files.Remove(guid))
            {
                var keys = new List<(string, long)>();
                foreach (var key in s_Objects.Keys) if (string.Equals(key.Item1, guid, StringComparison.OrdinalIgnoreCase)) keys.Add(key);
                foreach (var key in keys) s_Objects.Remove(key);
            }
        }
    }
}
