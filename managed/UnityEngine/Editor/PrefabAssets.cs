using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// Prefab assets written from the objects Unbound loaded them into, as the Unity 6000.0.71f1
    /// Editor writes them (tests/projects/scenesave prefab-writes, edit, prefab-api): the root is
    /// named after the file, an object keeps its file id, a new one gets a random one, a destroyed
    /// one's document goes, and the documents are in a prefab's order. The scenes and prefabs nesting
    /// it expand it again, and the loaded instances take the change (<see cref="Change"/>).
    /// </summary>
    internal static class PrefabAssets
    {
        static readonly ConditionalWeakTable<LoadedFile, Dictionary<Object, long>> s_Stripped = new ConditionalWeakTable<LoadedFile, Dictionary<Object, long>>();

        /// <summary>The loaded prefab of an asset path (null: none).</summary>
        internal static LoadedFile FileAt(string assetPath)
        {
            var guid = AssetDatabaseRuntime.LoadedGuidAt(assetPath) ?? AssetDatabase.AssetPathToGUID(assetPath);
            return string.IsNullOrEmpty(guid) ? null : FileOf(guid);
        }

        internal static LoadedFile FileOf(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            var file = AssetDatabaseRuntime.LoadFile(guid);
            return file?.MainObject is GameObject ? file : null;
        }

        /// <summary>Writes the prefab's hierarchy to its file; false, with the reason logged, when
        /// it cannot be written.</summary>
        internal static bool Save(LoadedFile file)
        {
            if (file?.MainObject is not GameObject root || root == null)
            {
                Debug.LogError($"Unbound: {file?.Path} is not saved: the prefab has no root GameObject.");
                return false;
            }
            root.m_Name = Path.GetFileNameWithoutExtension(file.Path);
            var known = new HashSet<Object>(ReferenceEqualityComparer.Instance);
            var ids = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance);
            foreach (var pair in file.Objects)
                if (pair.Value is not null)
                {
                    known.Add(pair.Value);
                    ids.TryAdd(pair.Value, pair.Key);
                }
            var target = new SceneFiles.WriteTarget
            {
                Path = file.Path, FileIds = ids, StrippedIds = s_Stripped.GetValue(file, _ => new Dictionary<Object, long>(ReferenceEqualityComparer.Instance)),
                Roots = new List<GameObject> { root },
            };
            string text;
            try { text = SceneFiles.Write(target); }
            catch (NotSupportedException e)
            {
                Debug.LogError($"Unbound: {file.Path} is not saved: {e.Message}");
                return false;
            }
            if (text == null) return false;
            try { SceneFiles.WriteFile(Path.Combine(ProjectInfo.ProjectPath, file.Path), text); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"Unbound: {file.Path} is not saved: {e.Message}");
                return false;
            }
            // New objects are the asset's under the ids they were written with; destroyed ones are gone.
            var removed = new List<long>();
            foreach (var pair in new List<KeyValuePair<long, Object>>(file.Objects))
                if (pair.Value is null || pair.Value.m_Destroyed)
                {
                    file.Objects.Remove(pair.Key);
                    removed.Add(pair.Key);
                }
            foreach (var pair in ids)
                if (pair.Key is not null && !pair.Key.m_Destroyed && !known.Contains(pair.Key)) file.Objects[pair.Value] = pair.Key;
            AssetDatabaseRuntime.RefreshObjects(file, removed);
            if (Native.IsReady)
            {
                try { Native.Call("prefab-written", System.Text.Json.JsonSerializer.Serialize(new { guid = file.Guid })); }
                catch (Exception e) { Debug.LogWarning($"Unbound: {file.Path}: the prefabs nesting it keep their earlier expansion: {e.Message}"); }
            }
            return true;
        }

        /// <summary>
        /// A new prefab asset made of a scene's hierarchy, written as it is: a plain hierarchy or a
        /// prefab's contents (a copy, its root named after the file, a random file id per object),
        /// one holding prefab instances (each written as a new instance of the file: its
        /// modifications, added and removed objects) or an instance's root (a variant of its prefab:
        /// its PrefabInstance, the root named after the file); references to scene objects outside
        /// it are none. The file is then imported (the PrefabImporter's <c>.meta</c>) and loaded
        /// from it; the hierarchy keeps its name and its instances their documents' ids (matches
        /// the Unity 6000.0.71f1 Editor, tests/projects/scenesave edit Made.prefab, prefab-api
        /// Created.prefab, prefab-writes ThingCopy.prefab, ThingVariant.prefab, prefab-nested
        /// Outer.prefab, RichVariant.prefab, VariantOfVariant.prefab). Null, with the reason
        /// logged, when it cannot be written.
        /// </summary>
        internal static LoadedFile Create(string path, GameObject source)
        {
            var text = NewPrefabText(path, source);
            return text == null ? null : Imported(path, text);
        }

        /// <summary>The text of <see cref="Create"/>'s file; null with the reason logged.</summary>
        internal static string NewPrefabText(string path, GameObject source)
        {
            var instances = new List<(PrefabInstanceData Data, long FileId)>();
            foreach (var t in source.GetComponentsInChildren<Transform>(true))
                if (PrefabLinks.Of(t.m_GameObject)?.Instance is PrefabInstanceData data && !instances.Exists(i => i.Data == data))
                    instances.Add((data, data.FileId));
            var name = source.m_Name;
            source.m_Name = Path.GetFileNameWithoutExtension(path);
            foreach (var (data, _) in instances) data.FileId = 0;
            try
            {
                return WriteNew(path, source);
            }
            finally
            {
                source.m_Name = name;
                foreach (var (data, fileId) in instances) data.FileId = fileId;
            }
        }

        /// <summary>A new prefab file's text with <paramref name="root"/>'s hierarchy; null with
        /// the reason logged.</summary>
        static string WriteNew(string path, GameObject root)
        {
            var target = new SceneFiles.WriteTarget
            {
                Path = path,
                FileIds = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance),
                StrippedIds = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance),
                Roots = new List<GameObject> { root },
            };
            try { return SceneFiles.Write(target); }
            catch (NotSupportedException e)
            {
                Debug.LogError($"Unbound: {path} is not saved: {e.Message}");
                return null;
            }
        }

        /// <summary>Writes a new prefab file, imports it and loads it; null with the reason logged.</summary>
        static LoadedFile Imported(string path, string text)
        {
            try { SceneFiles.WriteFile(Path.Combine(ProjectInfo.ProjectPath, path), text); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"Unbound: {path} is not saved: {e.Message}");
                return null;
            }
            var guid = AssetDatabase.ImportOne(path);
            var file = FileOf(guid);
            if (file == null) Debug.LogError($"Unbound: {path} is written but could not be loaded as a prefab.");
            return file;
        }

        /// <summary>The values a prefab's objects have in its file (for an object or path the file
        /// lacks, those it has now): what its instances were merged from before a script changed
        /// the asset itself.</summary>
        internal static Func<Object, Dictionary<string, YText>> FileValues(LoadedFile file)
        {
            Dictionary<long, YTextDocument> docs = null;
            return source =>
            {
                docs ??= ReadDocuments(file);
                var refs = new ValueReferences();
                var now = PrefabValues.ByPath(PrefabValues.Flatten(PrefabValues.Fields(source, null, refs)));
                long id = IdOf(file, source);
                if (id == 0 || !docs.TryGetValue(id, out var doc)) return now;
                var body = doc.Body.Clone();
                Rename(body, file, refs);
                foreach (var (path, value) in PrefabValues.Flatten(body))
                    if (now.ContainsKey(path)) now[path] = value;
                return now;
            };
        }

        static Dictionary<long, YTextDocument> ReadDocuments(LoadedFile file)
        {
            var docs = new Dictionary<long, YTextDocument>();
            try
            {
                foreach (var d in UnityYamlText.Parse(File.ReadAllText(Path.Combine(ProjectInfo.ProjectPath, file.Path))))
                    docs[d.FileId] = d;
            }
            catch (IOException) { }
            return docs;
        }

        /// <summary>A document's references as <paramref name="refs"/> names the objects they are.</summary>
        static void Rename(YText v, LoadedFile file, ValueReferences refs)
        {
            switch (v.Kind)
            {
                case YKind.Map when v.Flow && v.Get("fileID") != null:
                {
                    long id = SceneFiles.ParseFileId(v);
                    var guid = v.Get("guid")?.Text;
                    var o = id == 0 ? null : file.Resolve(new ObjectRef(id, guid, 0), typeof(Object));
                    var named = SceneFiles.FlowValue(refs.Text(o) ?? "{fileID: 0}");
                    v.Keys.Clear();
                    v.Values.Clear();
                    v.Keys.AddRange(named.Keys);
                    v.Values.AddRange(named.Values);
                    v.Raw = named.Raw;
                    return;
                }
                case YKind.Map:
                    foreach (var child in v.Values) Rename(child, file, refs);
                    return;
                case YKind.Seq:
                    foreach (var child in v.Items) Rename(child, file, refs);
                    return;
            }
        }

        /// <summary>The file id a saved prefab gave one of its objects (0: none).</summary>
        internal static long IdOf(LoadedFile file, Object o)
        {
            foreach (var pair in file.Objects)
                if (ReferenceEquals(pair.Value, o)) return pair.Key;
            return 0;
        }

        /// <summary>
        /// One change of a prefab asset: the overrides of its loaded instances are taken before it
        /// changes, then it is written and each instance merged with it, keeping its overrides;
        /// prefabInstanceUpdated is called for each instance and their update published (measured:
        /// tests/projects/scenesave prefab-writes *.updated, *.event).
        /// </summary>
        internal sealed class Change
        {
            internal readonly LoadedFile File;
            internal readonly ValueReferences Refs = new ValueReferences();
            readonly List<(PrefabInstanceData Data, GameObject Root, PrefabInstances.Snapshot Snapshot)> m_Instances =
                new List<(PrefabInstanceData, GameObject, PrefabInstances.Snapshot)>();

            /// <param name="baseline">A source object's values the instances were merged from, when
            /// the asset was changed before this (a script's edit saved by SavePrefabAsset).</param>
            internal Change(LoadedFile file, Func<Object, Dictionary<string, YText>> baseline = null)
            {
                File = file;
                foreach (var (data, root) in PrefabInstances.InstancesOf(file.Guid))
                    m_Instances.Add((data, root, PrefabInstances.Capture(data, Refs, baseline)));
            }

            /// <summary>The overrides an instance is merged with: those given (an undo's) instead of
            /// those it had when the change began.</summary>
            internal void Replace(PrefabInstanceData data, PrefabInstances.Snapshot snapshot)
            {
                for (int i = 0; i < m_Instances.Count; i++)
                    if (m_Instances[i].Data == data) m_Instances[i] = (data, m_Instances[i].Root, snapshot);
            }

            /// <summary>The overrides an instance had when the change began.</summary>
            internal PrefabInstances.Snapshot SnapshotOf(PrefabInstanceData data)
            {
                foreach (var entry in m_Instances) if (entry.Data == data) return entry.Snapshot;
                return null;
            }

            /// <summary>Writes the asset, then merges and tells the instances; false when the asset
            /// could not be written (the instances are left as they are).</summary>
            internal bool Commit(Action saved = null)
            {
                if (!Save(File)) return false;
                saved?.Invoke();
                var updated = new List<GameObject>();
                foreach (var (data, root, snapshot) in m_Instances)
                {
                    if (root == null) continue;
                    PrefabInstances.Merge(data, snapshot);
                    updated.Add(root);
                }
                Updated(updated);
                return true;
            }
        }

        /// <summary>prefabInstanceUpdated for each updated instance root, then one
        /// UpdatePrefabInstances change per scene listing them.</summary>
        internal static void Updated(List<GameObject> roots)
        {
            foreach (var root in roots)
            {
                var callback = PrefabUtility.prefabInstanceUpdated;
                if (callback == null || root == null) continue;
                foreach (PrefabUtility.PrefabInstanceUpdated handler in callback.GetInvocationList())
                {
                    try { handler(root); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
            if (!ObjectChangeEvents.Listening) return;
            var byScene = new List<(Scene Scene, List<int> Ids)>();
            foreach (var root in roots)
            {
                if (root == null) continue;
                var scene = root.scene;
                var entry = byScene.Find(e => e.Scene == scene);
                if (entry.Ids == null) byScene.Add(entry = (scene, new List<int>()));
                entry.Ids.Add(root.GetInstanceID());
            }
            foreach (var (scene, ids) in byScene) ObjectChangeEvents.NoteUpdatedInstances(scene, ids.ToArray());
        }
    }
}

namespace Unbound.Runtime
{
    /// <summary>
    /// Prefab contents loaded for editing (PrefabUtility.LoadPrefabContents): a copy of a prefab's
    /// hierarchy in a preview scene of its own, not an instance and not the asset (measured:
    /// tests/projects/scenesave prefab-writes contentsCopy.*), each object knowing its asset's.
    /// Saving them to their prefab makes the asset as they are: its objects take their values, the
    /// contents' new objects become the asset's, the asset's objects the contents lost go.
    /// </summary>
    internal sealed class PrefabContents
    {
        internal LoadedFile File;
        internal GameObject Root;
        internal SceneData Scene;
        /// <summary>Contents objects to the asset's.</summary>
        internal readonly Dictionary<Object, Object> ToAsset = new Dictionary<Object, Object>(ReferenceEqualityComparer.Instance);

        static readonly List<PrefabContents> s_Loaded = new List<PrefabContents>();

        internal static int LoadedCount => s_Loaded.Count;

        internal static PrefabContents Of(GameObject root)
        {
            foreach (var c in s_Loaded) if (ReferenceEquals(c.Root, root)) return c;
            return null;
        }

        internal static GameObject Load(LoadedFile file)
        {
            var assetRoot = (GameObject)file.MainObject;
            var scene = SceneRegistry.Create("", "", -1);
            scene.IsPreview = true;
            scene.IsLoaded = true;
            scene.LoadingState = UnityEngine.SceneManagement.Scene.LoadingState.Loaded;
            var clone = Instantiation.Prepare(assetRoot, assetRoot, scene.Handle, "");
            Instantiation.Place(clone, null, scene.Handle, false, false, null, null);
            var contents = new PrefabContents { File = file, Root = clone.RootGo, Scene = scene };
            foreach (var pair in clone.Map)
            {
                contents.ToAsset[pair.Value] = pair.Key;
                // The prefab's nested instances stay instances in its contents.
                if (PrefabLinks.Of(pair.Key) is PrefabLink link) PrefabLinks.Set(pair.Value, link);
            }
            s_Loaded.Add(contents);
            Instantiation.Activate(new[] { clone });
            return contents.Root;
        }

        internal static bool Unload(GameObject root)
        {
            var contents = root == null ? null : Of(root);
            if (contents == null) return false;
            s_Loaded.Remove(contents);
            Object.DestroyImmediate(contents.Root);
            SceneRegistry.Forget(contents.Scene);
            return true;
        }

        /// <summary>Writes the contents to their prefab.</summary>
        internal static bool Save(PrefabContents contents)
        {
            var change = new PrefabAssets.Change(contents.File);
            Sync(contents.Root, (GameObject)contents.File.MainObject, contents.ToAsset, change.Refs);
            return change.Commit();
        }

        /// <summary>A plain hierarchy written over a prefab: its root and the root's Transform keep
        /// their ids, every other object of the prefab is replaced (measured for ReplacePrefab:
        /// tests/projects/scenesave replace.prefab).</summary>
        internal static bool Replace(GameObject root, LoadedFile file)
        {
            var assetRoot = (GameObject)file.MainObject;
            var map = new Dictionary<Object, Object>(ReferenceEqualityComparer.Instance)
            {
                [root] = assetRoot,
                [root.transform] = assetRoot.transform,
            };
            var change = new PrefabAssets.Change(file);
            Sync(root, assetRoot, map, change.Refs);
            return change.Commit();
        }

        /// <summary>A prefab asset's new GameObject with its transform (a <paramref name="transformType"/>).</summary>
        static GameObject NewAssetGameObject(string name, Type transformType)
        {
            GameObject made;
            using (ObjectRegistry.AsAssets()) made = new GameObject(name, bare: true) { m_IsPrefabAsset = true, IsPersistentAsset = true };
            Transform t;
            using (ObjectRegistry.AsAssets()) t = (Transform)ComponentFactory.Create(transformType, made, deserializing: true);
            t.IsPersistentAsset = true;
            made.m_Transform = t;
            made.m_Components.Add(t);
            return made;
        }

        /// <summary>Makes an asset hierarchy as another one is, objects matched by
        /// <paramref name="map"/> (which takes the new asset objects): matched objects take the
        /// values, unmatched ones are made in the asset, the asset's unmatched objects are destroyed.</summary>
        internal static void Sync(GameObject from, GameObject asset, Dictionary<Object, Object> map, ValueReferences refs)
        {
            void Structure(GameObject source, GameObject target)
            {
                map[source] = target;
                var kept = new HashSet<Object>(ReferenceEqualityComparer.Instance);
                var order = new List<Component>();
                foreach (var c in source.m_Components)
                {
                    if (c == null || c.m_Destroyed) continue;
                    if (!map.TryGetValue(c, out var made) || made == null || ReferenceEquals(((Component)made).m_GameObject, target) == false)
                    {
                        if (c is Transform) made = target.m_Transform;
                        else
                        {
                            Component created;
                            using (ObjectRegistry.AsAssets()) created = ComponentFactory.Create(c.GetType(), target, deserializing: true);
                            created.IsPersistentAsset = true;
                            target.m_Components.Add(created);
                            made = created;
                        }
                        map[c] = made;
                    }
                    kept.Add(made);
                    order.Add((Component)made);
                }
                foreach (var c in target.m_Components.ToArray())
                    if (c != null && !kept.Contains(c)) Object.DestroyImmediate(c, true);
                target.m_Components.Clear();
                target.m_Components.AddRange(order);
                var children = new List<Transform>();
                foreach (var child in source.transform.m_Children)
                {
                    var childGo = child.m_GameObject;
                    if (!map.TryGetValue(childGo, out var existing) || existing == null)
                    {
                        var made = NewAssetGameObject(childGo.m_Name, child.GetType());
                        existing = made;
                        map[child] = made.m_Transform;
                    }
                    var targetChild = (GameObject)existing;
                    var parent = targetChild.m_Transform.m_Parent;
                    if (parent != null && !ReferenceEquals(parent, target.m_Transform)) parent.m_Children.Remove(targetChild.m_Transform);
                    targetChild.m_Transform.m_Parent = target.m_Transform;
                    children.Add(targetChild.m_Transform);
                    Structure(childGo, targetChild);
                }
                foreach (var child in target.m_Transform.m_Children.ToArray())
                    if (!children.Contains(child)) Object.DestroyImmediate(child.m_GameObject, true);
                target.m_Transform.m_Children.Clear();
                target.m_Transform.m_Children.AddRange(children);
            }
            Structure(from, asset);
            Object ToAsset(Object x)
            {
                if (x is null) return null;
                if (map.TryGetValue(x, out var mapped)) return mapped;
                // An object of a scene cannot be referenced by an asset.
                return !x.IsPersistentAsset && (x is GameObject || x is Component) ? null : x;
            }
            foreach (var pair in map)
                if (pair.Key != null && pair.Value != null)
                    PrefabValues.Write(pair.Value, PrefabValues.Flatten(PrefabValues.Fields(pair.Key, ToAsset, refs)), refs);
        }
    }
}
