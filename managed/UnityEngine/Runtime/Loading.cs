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
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
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
            /// <summary>A serialized file of an AssetBundle: its GameObjects are prefab assets, its
            /// scripts its MonoScripts, its shaders the project's of their names.</summary>
            Bundle,
        }

        /// <param name="created">Called once every object exists, before any data is read: assets
        /// referencing each other in a cycle resolve to these objects, filled in afterwards.</param>
        internal static LoadedFile Build(List<YDocument> documents, string path, string guid, Kind kind, SceneData scene, Action<LoadedFile> created = null)
        {
            var file = new LoadedFile { Path = path, Guid = guid };
            var gameObjects = new Dictionary<long, (GameObject Go, YDocument Doc)>();
            var components = new List<(Component Component, YDocument Doc)>();
            var assets = new List<(Object Asset, YDocument Doc)>();
            bool isAsset = kind != Kind.Scene;
            YDocument sceneRoots = null;
            var deleted = new Dictionary<long, Type>();
            // A built bundle keeps no prefab instances; nor do the scenes Play mode loads, whose
            // objects are plain objects (measured: tests/projects/editorcallbacks).
            var instances = kind == Kind.Bundle || kind == Kind.Scene && EditorSession.IsPlaying ? null : PrefabLinks.Instances(documents, isAsset);

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
                        m_IsPrefabAsset = kind == Kind.PrefabAsset || kind == Kind.Bundle,
                        IsPersistentAsset = isAsset,
                    };
                    go.isStatic = body.Long("m_StaticEditorFlags") != 0;
                    if (scene != null) go.m_Scene = scene.Handle;
                    if (instances != null) PrefabLinks.Link(go, body, instances);
                    gameObjects[doc.FileId] = (go, doc);
                    file.Objects[doc.FileId] = go;
                }

                foreach (var doc in documents)
                {
                    if (doc.Stripped || doc.ClassId == EngineTypes.GameObjectId) continue;
                    if (doc.ClassId == EngineTypes.PrefabInstanceId) continue;
                    if (doc.ClassId == 1660057539) { sceneRoots = doc; continue; } // SceneRoots
                    if (SceneSettings.IsSettingsClass(doc.ClassId)) continue;
                    // A bundle's own objects: its AssetBundle object, the scripts and preload data.
                    if (doc.ClassId == 142 || doc.ClassId == 115 || doc.ClassId == 150) continue;
                    var body = doc.Body;
                    if (doc.ClassId == 48 && kind == Kind.Bundle)
                    {
                        // A built shader: the project's (or Unity's) shader of its name.
                        var shader = Shader.Find(body.Str("m_Name"));
                        if (shader == null) Debug.LogWarning($"Unbound: the AssetBundle shader '{body.Str("m_Name")}' is not one Unbound has the source of; its materials draw with the error shader.");
                        file.Objects[doc.FileId] = shader != null ? shader : Shader.Find("Hidden/InternalErrorShader");
                        continue;
                    }
                    var owner = body.Ref("m_GameObject");
                    if (!owner.IsNull)
                    {
                        if (!gameObjects.TryGetValue(owner.FileId, out var entry)) continue;
                        var type = ComponentTypeOf(doc, guid);
                        if (type == null) continue;
                        if (kind == Kind.Scene && EditorSession.IsPlaying && DisabledModules.Contains(type))
                        {
                            deleted[doc.FileId] = type;
                            continue;
                        }
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
                        if (instances != null) PrefabLinks.Link(c, body, instances);
                        if (c is Transform t) entry.Go.m_Transform = t;
                        file.Objects[doc.FileId] = c;
                        components.Add((c, doc));
                        continue;
                    }
                    var asset = CreateAsset(doc, guid);
                    if (asset == null) continue;
                    asset.IsPersistentAsset = isAsset;
                    file.Objects[doc.FileId] = asset;
                    assets.Add((asset, doc));
                }
            }

            // Components of disabled modules, reported in the order each GameObject lists them.
            if (deleted.Count != 0)
            {
                foreach (var (_, doc) in gameObjects.Values)
                    foreach (var item in doc.Body.Get("m_Component")?.Seq() ?? Array.Empty<YNode>())
                        if (deleted.Remove(item.Ref("component").FileId, out var type)) LogDeleted(type);
                foreach (var type in deleted.Values) LogDeleted(type);
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
            if (created != null)
            {
                SetMainObject(file, kind, assets);
                created(file);
            }

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

            // Attachment precedes serialized fields and may precede the Transform itself.
            // Snapshot only after every local pose and parent is final, including the
            // flattened prefab overrides. An inactive body must not capture later caller edits.
            foreach (var (c, _) in components)
                if (c is Rigidbody rb) rb.CaptureInactivePose();

            SetMainObject(file, kind, assets);
            foreach (var (asset, _) in assets)
                if (asset is ScriptableObject so) so.RunCreationCallbacks();
            return file;
        }

        /// <summary>The main object: a prefab's root GameObject; an asset file's first object.</summary>
        static void SetMainObject(LoadedFile file, Kind kind, List<(Object Asset, YDocument Doc)> assets)
        {
            if (kind == Kind.PrefabAsset) file.MainObject = file.Roots.Count > 0 ? file.Roots[0] : null;
            else if (kind == Kind.Asset && assets.Count > 0) file.MainObject = assets[0].Asset;
        }

        /// <summary>The class a MonoBehaviour's `m_Script` names: a project script by GUID, or an
        /// AssetBundle's MonoScript by its class.</summary>
        static Type ScriptOf(ObjectRef script, string fileGuid) =>
            BundleFiles.IsBundleScript(script, fileGuid) ? BundleFiles.Script(script, fileGuid) : GameDomain.ResolveScript(script.Guid, script.FileId);

        static void LogDeleted(Type type) =>
            Debug.Log($"{type.Name} component deleted: Component belongs to a disabled built-in package.");

        static Type ComponentTypeOf(YDocument doc, string fileGuid)
        {
            if (doc.ClassId == EngineTypes.MonoBehaviourId)
            {
                var script = doc.Body.Ref("m_Script");
                var type = ScriptOf(script, fileGuid);
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

        static Object CreateAsset(YDocument doc, string fileGuid)
        {
            if (doc.ClassId == EngineTypes.MonoBehaviourId)
            {
                var script = doc.Body.Ref("m_Script");
                var type = ScriptOf(script, fileGuid);
                if (type == null)
                {
                    MissingScripts.Report(script, doc);
                    // As in Unity, the asset still exists: an object of the base class, so lists
                    // of assets keep their entries (its data cannot be read).
                    var placeholder = ScriptableObject.CreateInstanceWithoutCallbacks(typeof(ScriptableObject));
                    if (placeholder != null) placeholder.m_Name = doc.Body.Str("m_Name");
                    return placeholder;
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
            {
                c.ReadSerialized(body, file);
                Trace.NoteUnread(c.GetType().Name, body);
            }
        }

        static void ReadAsset(Object asset, YDocument doc, LoadedFile file)
        {
            if (asset is ScriptableObject)
                new YamlReader(file).ReadScriptObject(asset, doc.Body);
            else if (asset is ISerializedAsset serialized)
            {
                serialized.ReadSerialized(doc.Body, file);
                Trace.NoteUnread(asset.GetType().Name, doc.Body);
            }
            else
                // A class Unbound knows but cannot read: it would load with default settings.
                Stubs.Ignored(asset.GetType().FullName + " assets", "are not read yet: they load with default settings");
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
                documents = Native.LoadDocuments(scene.LoadKey ?? scene.Path);
            }
            catch (Exception e)
            {
                Debug.LogError($"Unbound: could not load scene {scene.Path}: {e.Message}");
                return;
            }
            var file = ObjectGraphBuilder.Build(documents, scene.Path, null, ObjectGraphBuilder.Kind.Scene, scene);
            foreach (var doc in documents)
                if (!doc.Stripped) SceneSettings.Apply(doc, file, scene);
            foreach (var root in file.Roots) scene.Roots.Add(root);
            foreach (var pair in file.Objects) scene.FileIds[pair.Value] = pair.Key;

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

        internal static void Apply(YDocument doc, IReferenceResolver resolver, SceneData scene = null)
        {
            if (!IsSettingsClass(doc.ClassId)) return;
            if (doc.ClassId == 104) RenderSettings.SceneLoaded(scene, doc.Body, resolver);
            if (doc.ClassId == 157)
            {
                // The first scene loaded (the others unloaded) replaces the baked lighting; an
                // additive one adds to it.
                bool first = scene == null || SceneRegistry.Loaded.TrueForAll(s => s == scene || SceneRegistry.IsDontDestroyOnLoad(s) || !s.IsLoaded);
                RenderSettings.ReadLightingData(doc.Body.Ref("m_LightingDataAsset"), resolver as LoadedFile, first, scene?.Handle.Value ?? 0);
            }
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
            if (guid.StartsWith("bundle:", StringComparison.Ordinal)) return BundleFiles.Load(guid, fileId);
            if (string.Equals(guid, "0000000000000000d000000000000000", StringComparison.OrdinalIgnoreCase))
                return Unbound.UIElements.UiResources.LoadObject(fileId);
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
            // The main object and the file's other assets (a model's meshes, materials, clips), not
            // the GameObjects and components under a prefab's root.
            var list = new List<Object>();
            if (file.MainObject != null) list.Add(file.MainObject);
            foreach (var o in file.Objects.Values)
                if (o != file.MainObject && !(o is Component) && !(o is GameObject)) list.Add(o);
            return list;
        }

        /// <summary>A model's hidden `__preview__&lt;take&gt;` clip: listed by
        /// AssetDatabase.LoadAllAssetsAtPath, not by Resources or LoadAllAssetRepresentationsAtPath
        /// (measured, tests/projects/modelclips).</summary>
        internal static bool IsTakePreview(Object o) =>
            o is AnimationClip && (o.hideFlags & HideFlags.HideInHierarchy) != 0;

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
                // Published as soon as its objects exist, so cyclic references between assets
                // resolve (to objects whose data is read right after).
                void Publish(LoadedFile f)
                {
                    if (info.Kind == "yaml" && info.MainFileId != 0 && f.Objects.TryGetValue(info.MainFileId, out var main)) f.MainObject = main;
                    f.Guid = guid;
                    f.Path = info.Path;
                    foreach (var pair in f.Objects) s_Objects[(guid, pair.Key)] = pair.Value;
                    s_Files[guid] = f;
                }
                switch (info.Kind)
                {
                    case "prefab":
                    case "model":
                        // A model is the prefab it imports to (the native side makes its documents).
                        file = ObjectGraphBuilder.Build(Native.LoadDocuments(guid), info.Path, guid, ObjectGraphBuilder.Kind.PrefabAsset, null, Publish);
                        if (info.Kind == "model") ModelMaterialImport.UsePipelineDefault(file);
                        break;
                    case "yaml":
                        file = ObjectGraphBuilder.Build(Native.LoadDocuments(guid), info.Path, guid, ObjectGraphBuilder.Kind.Asset, null, Publish);
                        if (info.MainFileId != 0 && file.Objects.TryGetValue(info.MainFileId, out var main)) file.MainObject = main;
                        // A sprite atlas v2 is referenced by the object its importer makes.
                        if (file.MainObject is UnityEngine.U2D.SpriteAtlas)
                            file.Objects[UnityEngine.U2D.SpriteAtlas.ImportedFileId] = file.MainObject;
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
                // As the Editor: a loaded asset's behaviours and scriptable objects get OnValidate.
                if (file != null && (info.Kind == "prefab" || info.Kind == "yaml"))
                    foreach (var o in new List<Object>(file.Objects.Values))
                        ScriptMessages.Validate(o);
                return file;
            }
            finally
            {
                s_Loading.Remove(guid);
            }
        }

        /// <summary>
        /// Resources.UnloadAsset of a loaded asset. A material alone in its file reads it again in
        /// place; a ScriptableObject alone in its file is destroyed and the file forgotten, so the
        /// next load reads it again (both measured, tests/projects/droppedparams unload.*). An
        /// imported Texture2D or AudioClip is imported again into its renderer or mixer handle, a
        /// mesh read from a document (a model's, a mesh asset's) takes that document's data again,
        /// and an imported TextAsset frees its bytes, read from its file at their next use: each
        /// stays the same object, as the material does (unmeasured for these kinds). False for
        /// what is not unloaded (other kinds, shared-file materials and ScriptableObjects).
        /// </summary>
        internal static bool Unload(Object asset)
        {
            string guid = null;
            long fileId = 0;
            foreach (var pair in s_Objects)
                if (ReferenceEquals(pair.Value, asset)) { (guid, fileId) = pair.Key; break; }
            if (guid == null || !s_Files.TryGetValue(guid, out var file) || file == null) return false;
            switch (asset)
            {
                case ScriptableObject when file.Objects.Count == 1:
                    Invalidate(guid);
                    Object.DestroyImmediate(asset, true);
                    return true;
                // Only a material alone in its file: reading a shared file again would build
                // and destroy fresh copies of its siblings (the reverted material would then
                // reference destroyed textures), so such a material is not unloaded (warns).
                case Material material when file.Objects.Count == 1:
                    var saved = ReadAgain(guid, fileId) as Material;
                    if (saved == null) return false;
                    material.RevertTo(saved);
                    return true;
                case Texture2D texture:
                    return texture.ReloadImported();
                case Mesh mesh:
                    return ReloadMesh(mesh, guid, fileId, file);
                case AudioClip clip:
                    return clip.ReloadImported();
                // Not a MonoScript (a TextAsset too): unloading scripts is not covered.
                case TextAsset text when text.GetType() == typeof(TextAsset):
                    return text.UnloadData();
                default:
                    return false;
            }
        }

        /// <summary>The documents of an asset file, read again (tests replace it).</summary>
        internal static Func<string, List<YDocument>> ReloadDocuments = guid => Native.LoadDocuments(guid);

        /// <summary>A mesh built from a document of its file (a model's mesh, a mesh asset) takes
        /// that document's data again. Its siblings are not read: a mesh references none.</summary>
        static bool ReloadMesh(Mesh mesh, string guid, long fileId, LoadedFile file)
        {
            if (file.UnknownFileIds) return false;
            YDocument document = null;
            try
            {
                foreach (var doc in ReloadDocuments(guid))
                    if (doc.FileId == fileId && !doc.Stripped) { document = doc; break; }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: reading mesh '{mesh.m_Name}' again: {e.Message}");
                return false;
            }
            if (document == null || !typeof(Mesh).IsAssignableFrom(EngineTypes.ForClassId(document.ClassId, document.TypeName))) return false;
            var saved = new Mesh { m_Name = document.Body.Str("m_Name") };
            try
            {
                ((ISerializedAsset)saved).ReadSerialized(document.Body, file);
                mesh.RevertTo(saved);
            }
            finally
            {
                Object.DestroyImmediate(saved);
            }
            return true;
        }

        /// <summary>
        /// Resources.UnloadUnusedAssets (Scripting API 6000.0): "An asset is deemed to be unused if it
        /// isn't reached after walking the whole game object hierarchy, including script
        /// components. Static variables are also examined", and "All assets other than
        /// ScriptableObjects are loaded back in the next time one of its properties or methods is
        /// used". Each loaded asset that <see cref="Reachability"/> does not reach, and whose hide
        /// flags do not include DontUnloadUnusedAsset, is unloaded when Unbound can load it back
        /// lazily, at its next use: an imported TextAsset frees its bytes and reads its file again
        /// when next asked for them. One already unloaded is not touched again. Every other kind is
        /// kept as it is, with what scripts changed: loading a material, mesh, texture or audio
        /// clip back at its next use would need hooks in their accessors and in the renderer and
        /// mixer, outside this mechanism, and reading them again now, on every call, would redo
        /// the work for nothing (<see cref="Unload(Object)"/>, the explicit Resources.UnloadAsset,
        /// still does that). ScriptableObjects are kept too: the engine does not see every
        /// reference to one (the stack, as in Unity; statics it cannot read safely), so destroying
        /// one could break an object still in use. Nothing reached only through what the engine
        /// cannot see is reverted: no kind kept here loses an edit. A failure unloading one asset
        /// is reported and the others are still unloaded. Returns how many assets were unloaded.
        /// </summary>
        internal static int UnloadUnused()
        {
            var reached = Reachability.Walk();
            var candidates = new List<Object>();
            var listed = new HashSet<Object>(ReferenceEqualityComparer.Instance);
            foreach (var obj in s_Objects.Values)
            {
                if (obj == null || obj.m_Destroyed || !obj.IsPersistentAsset || reached.Contains(obj)) continue;
                if ((obj.hideFlags & HideFlags.DontUnloadUnusedAsset) != 0) continue;
                if (listed.Add(obj)) candidates.Add(obj);
            }
            int unloaded = 0;
            foreach (var obj in candidates)
            {
                if (obj.m_Destroyed) continue;
                try
                {
                    if (UnloadUnusedOne(obj)) unloaded++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Unbound: Resources.UnloadUnusedAssets could not unload '{obj.m_Name}' ({obj.GetType().Name}): {e.Message}");
                }
            }
            return unloaded;
        }

        /// <summary>Unloads one unused asset of a kind loaded back lazily; false (nothing done) for
        /// the kinds kept and for an asset already unloaded.</summary>
        static bool UnloadUnusedOne(Object asset) => asset switch
        {
            // Not a MonoScript (a TextAsset too): unloading scripts is not covered.
            TextAsset text when text.GetType() == typeof(TextAsset) && text.DataLoaded => text.UnloadData(),
            _ => false,
        };

        /// <summary>
        /// The objects the engine can see in use, for UnloadUnusedAssets: everything reachable,
        /// through the fields of objects (any accessibility; arrays and collections through their
        /// storage), from every live object that is not an asset (the objects of loaded scenes,
        /// DontDestroyOnLoad ones, objects made at run time), from every object flagged
        /// DontUnloadUnusedAsset, from static fields of non-framework assemblies, and from the
        /// engine's own holders of scene state (<see cref="EngineRoots"/>).
        /// <para>Statics of a class whose only class initialization is its static field
        /// initializers (C# marks it beforefieldinit) are read: the runtime may run those
        /// initializers at any time before the first access to a static field (ECMA-335
        /// II.10.5.3.2; C# "Static field initialization"), so reading them here is within the
        /// language contract, though it may run initializers the game has not reached yet. Statics
        /// of a class with an explicit static constructor are not read: the constructor runs exactly
        /// at the class's first use, reading would run it, and whether it already ran cannot be
        /// told. Initializers are run this way only in assemblies built against the engine (the
        /// game's scripts, packages and plugins); elsewhere, and in the engine's own assembly,
        /// only classes without class initialization are read. The engine's scene-state holders
        /// are explicit roots instead, so no other engine initializer runs here.</para>
        /// Not seen: the stack and handles (as in Unity), statics of generic classes, statics of
        /// classes with an explicit static constructor, and statics with initializers in assemblies
        /// not built against the engine; assets held only there count as unused,
        /// which <see cref="UnloadUnused"/> makes safe by never reverting or destroying what it
        /// unloads. The asset database's own tables are not roots.
        /// </summary>
        internal static class Reachability
        {
            const System.Reflection.BindingFlags InstanceFields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            const System.Reflection.BindingFlags StaticFields = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            static readonly Dictionary<Type, System.Reflection.FieldInfo[]> s_Fields = new Dictionary<Type, System.Reflection.FieldInfo[]>();
            static readonly Dictionary<Type, bool> s_MayHoldReferences = new Dictionary<Type, bool>();
            static readonly Dictionary<System.Reflection.Assembly, System.Reflection.FieldInfo[]> s_Statics = new Dictionary<System.Reflection.Assembly, System.Reflection.FieldInfo[]>();

            /// <summary>The engine classes whose statics hold the loaded scenes' state, all of it a
            /// root: RenderSettings (skybox, halo and custom reflection, the settings kept for the
            /// other loaded scenes), LightmapSettings (lightmaps, light probes) and Shader (global
            /// textures and buffers).</summary>
            internal static readonly Type[] EngineRoots = { typeof(RenderSettings), typeof(LightmapSettings), typeof(Shader) };

            internal static HashSet<object> Walk()
            {
                var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
                var pending = new Stack<object>();
                void Push(object value)
                {
                    if (value == null || value is string || value is System.Reflection.MemberInfo || value is System.Reflection.Assembly || value is System.Reflection.Module || value is System.Reflection.Pointer) return;
                    if (seen.Add(value)) pending.Push(value);
                }
                foreach (var obj in ObjectRegistry.All(typeof(Object)))
                    if (!obj.IsPersistentAsset || (obj.hideFlags & HideFlags.DontUnloadUnusedAsset) != 0) Push(obj);
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    foreach (var field in StaticsOf(assembly))
                    {
                        object value;
                        try { value = field.GetValue(null); }
                        catch (Exception) { continue; }
                        Push(value);
                    }
                foreach (var type in EngineRoots)
                    foreach (var field in type.GetFields(StaticFields))
                    {
                        if (field.IsLiteral || !MayHoldReferences(field.FieldType)) continue;
                        object value;
                        try { value = field.GetValue(null); }
                        catch (Exception) { continue; }
                        Push(value);
                    }
                while (pending.Count > 0)
                {
                    var value = pending.Pop();
                    if (value is Array array)
                    {
                        if (MayHoldReferences(array.GetType().GetElementType()))
                            foreach (var item in array) Push(item);
                        continue;
                    }
                    foreach (var field in FieldsOf(value.GetType()))
                    {
                        object item;
                        try { item = field.GetValue(value); }
                        catch (Exception) { continue; }
                        Push(item);
                    }
                }
                return seen;
            }

            /// <summary>The instance fields of a type and its bases that may hold references.</summary>
            static System.Reflection.FieldInfo[] FieldsOf(Type type)
            {
                if (s_Fields.TryGetValue(type, out var cached)) return cached;
                var fields = new List<System.Reflection.FieldInfo>();
                for (var t = type; t != null; t = t.BaseType)
                    foreach (var field in t.GetFields(InstanceFields))
                        if (MayHoldReferences(field.FieldType)) fields.Add(field);
                return s_Fields[type] = fields.ToArray();
            }

            static bool MayHoldReferences(Type type)
            {
                if (type == null || type.IsPointer || type.IsFunctionPointer || type.IsByRef || type.IsPrimitive || type.IsEnum || type == typeof(string)) return false;
                if (!type.IsValueType) return true;
                if (s_MayHoldReferences.TryGetValue(type, out var known)) return known;
                s_MayHoldReferences[type] = false;
                bool result = false;
                foreach (var field in type.GetFields(InstanceFields))
                    if (MayHoldReferences(field.FieldType)) { result = true; break; }
                return s_MayHoldReferences[type] = result;
            }

            static System.Reflection.FieldInfo[] StaticsOf(System.Reflection.Assembly assembly)
            {
                if (s_Statics.TryGetValue(assembly, out var cached)) return cached;
                var fields = new List<System.Reflection.FieldInfo>();
                var name = assembly.GetName().Name ?? "";
                bool framework = assembly.IsDynamic || name == "System" || name.StartsWith("System.", StringComparison.Ordinal)
                    || name.StartsWith("Microsoft.", StringComparison.Ordinal) || name == "mscorlib" || name == "netstandard";
                if (!framework)
                {
                    Type[] types;
                    try { types = assembly.GetTypes(); }
                    catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
                    catch (Exception) { types = Array.Empty<Type>(); }
                    bool engine = assembly == typeof(AssetDatabaseRuntime).Assembly;
                    // Initializers run here only in code built against the engine (the game's
                    // scripts, packages and plugins), never in libraries or the host's.
                    bool script = !engine && Array.Exists(assembly.GetReferencedAssemblies(),
                        r => r.Name == "UnityEngine" || (r.Name != null && r.Name.StartsWith("UnityEngine.", StringComparison.Ordinal)));
                    foreach (var type in types)
                    {
                        if (type == null || type.ContainsGenericParameters) continue;
                        if (type.TypeInitializer != null && (!script || (type.Attributes & System.Reflection.TypeAttributes.BeforeFieldInit) == 0)) continue;
                        if (type == typeof(AssetDatabaseRuntime) || type == typeof(ObjectRegistry)) continue;
                        foreach (var field in type.GetFields(StaticFields))
                            if (!field.IsLiteral && MayHoldReferences(field.FieldType)) fields.Add(field);
                    }
                }
                return s_Statics[assembly] = fields.ToArray();
            }
        }

        /// <summary>A new copy of one object of a file, read from it again; the loaded file and its
        /// objects stay what they are. The copy's other objects are destroyed.</summary>
        static Object ReadAgain(string guid, long fileId)
        {
            var loaded = s_Files[guid];
            var objects = new Dictionary<(string, long), Object>();
            foreach (var pair in s_Objects) if (string.Equals(pair.Key.Item1, guid, StringComparison.OrdinalIgnoreCase)) objects[pair.Key] = pair.Value;
            s_Files.Remove(guid);
            LoadedFile fresh;
            try
            {
                fresh = LoadFile(guid);
            }
            finally
            {
                s_Files[guid] = loaded;
                foreach (var pair in objects) s_Objects[pair.Key] = pair.Value;
            }
            if (fresh == null) return null;
            fresh.Objects.TryGetValue(fileId, out var copy);
            foreach (var o in fresh.Objects.Values)
                if (!ReferenceEquals(o, copy) && !objects.ContainsValue(o)) Object.DestroyImmediate(o, true);
            return copy;
        }

        /// <summary>Forgets every loaded asset (a domain reload or a content change).</summary>
        internal static void Clear()
        {
            s_Files.Clear();
            s_Objects.Clear();
        }

        /// <summary>Makes an already built file and its objects loaded, as reading it would (tests).</summary>
        internal static void AddLoaded(LoadedFile file)
        {
            foreach (var pair in file.Objects) s_Objects[(file.Guid, pair.Key)] = pair.Value;
            s_Files[file.Guid] = file;
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
