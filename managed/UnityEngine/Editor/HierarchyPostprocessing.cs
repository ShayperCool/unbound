using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Unbound.Runtime;
using UnityEditor.AssetImporters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// The postprocessors of a model's and a prefab's import, as the Editor runs them (matches the
    /// Unity 6000.0.71f1 Editor, tests/projects/assetpostprocessors). Unbound imports such an asset
    /// as it first loads or a scene first expands it, when the project's postprocessors change that
    /// kind of import: the native side then gives the asset's documents without their changes
    /// (<c>raw:</c>), the managed engine builds its objects and runs the callbacks on them, and
    /// hands the native expansion what changed (<see cref="Answer"/>), so scene and prefab
    /// instances get the postprocessed hierarchy too.
    ///
    /// A model: per top-level node in the file's order, depth first, each node's renderer slots
    /// (a "Standard (Legacy)" material: OnPostprocessMaterial of the file's material, then
    /// OnAssignMaterialModel, and when that gave no material, OnPostprocessMaterial once more the
    /// first time the import makes that material; "Import via MaterialDescription": the
    /// description preprocessors around OnAssignMaterialModel the same way; a material the model
    /// does not make, such as the default one: OnAssignMaterialModel alone), then the node's
    /// OnPostprocessGameObjectWithUserProperties, then its children; OnPostprocessMeshHierarchy
    /// after each top-level node. The top-level nodes are roots meanwhile (a single top-level node
    /// is the root, under its name in the file), the children in the file's order. Then
    /// OnPostprocessAnimation per clip and OnPostprocessModel. The first material
    /// OnAssignMaterialModel returns ends that slot's calls; a material only such slots use is not
    /// made (unmeasured). Afterwards the hierarchy is sorted by name (with
    /// <c>sortHierarchyByName</c>) and its objects take the fileIDs of their final paths.
    /// OnPreprocessAnimation runs with the model's preprocessors, before Unbound's import (the
    /// Editor calls it before OnPostprocessAnimation; the settings it sets are needed earlier).
    ///
    /// A prefab: OnPreprocessAsset, then OnPostprocessPrefab with its root; the prefabs it nests
    /// (and a variant's base) are imported with their own changes first. An object a prefab's
    /// postprocessor adds gets a fileID derived from the asset and its place (the Editor's rule is
    /// unmeasured).
    /// </summary>
    internal static class HierarchyPostprocessing
    {
        static List<(int, Type, MethodInfo)> MeshHierarchyHooks => AssetPostprocessing.Hooks("OnPostprocessMeshHierarchy", typeof(GameObject));
        static List<(int, Type, MethodInfo)> AssignHooks => AssetPostprocessing.Hooks("OnAssignMaterialModel", typeof(Material), typeof(Renderer));
        static List<(int, Type, MethodInfo)> MaterialHooks => AssetPostprocessing.Hooks("OnPostprocessMaterial", typeof(Material));
        static List<(int, Type, MethodInfo)> UserPropertyHooks => AssetPostprocessing.Hooks("OnPostprocessGameObjectWithUserProperties", typeof(GameObject), typeof(string[]), typeof(object[]));
        static List<(int, Type, MethodInfo)> AnimationHooks => AssetPostprocessing.Hooks("OnPostprocessAnimation", typeof(GameObject), typeof(AnimationClip));
        static List<(int, Type, MethodInfo)> ModelHooks => AssetPostprocessing.Hooks("OnPostprocessModel", typeof(GameObject));
        static List<(int, Type, MethodInfo)> PrefabHooks => AssetPostprocessing.Hooks("OnPostprocessPrefab", typeof(GameObject));

        /// <summary>Whether the postprocessors change model imports.</summary>
        internal static bool Models =>
            MeshHierarchyHooks.Count > 0 || AssignHooks.Count > 0 || MaterialHooks.Count > 0 || UserPropertyHooks.Count > 0
            || AnimationHooks.Count > 0 || ModelHooks.Count > 0;

        /// <summary>Whether the postprocessors change prefab imports.</summary>
        internal static bool Prefabs => PrefabHooks.Count > 0;

        /// <summary>Whether a prefab's import calls a postprocessor (OnPreprocessAsset, which every
        /// asset's import starts with, or OnPostprocessPrefab).</summary>
        static bool PrefabImportsCalled => Prefabs || AssetPostprocessing.Hooks("OnPreprocessAsset").Count > 0;

        /// <summary>Tells the native expansion which imports the postprocessors are called on.</summary>
        internal static void Announce()
        {
            try
            {
                Native.Call("postprocessors", "{\"models\":" + (Models ? "true" : "false") + ",\"prefabs\":" + (PrefabImportsCalled ? "true" : "false") + "}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: scenes expand models and prefabs without their postprocessors' changes ({e.Message})");
            }
        }

        /// <summary>What the postprocessors of the model or prefab <paramref name="guid"/> changed,
        /// for the native expansion (<c>imported_prefabs.rs</c>): header lines of the import's
        /// objects outside the hierarchy (instances reference the loaded ones), of the objects the
        /// postprocessors removed and of those whose fileIDs changed, then the documents of the
        /// changed and added objects.</summary>
        internal static string Answer(string guid)
        {
            // Importing makes objects, which only the main thread may.
            if (!PlayerLoopDriver.IsMainThread)
                throw new InvalidOperationException("a model's or prefab's postprocessors run on the main thread only");
            // A prefab whose import only OnPreprocessAsset is called on: nothing changes.
            var info = AssetDatabaseRuntime.Info(guid);
            if (info?.Kind == "prefab" && !Prefabs)
            {
                AssetPostprocessing.EnsurePreprocessed(info.Path, "prefab");
                AssetPostprocessing.EndSession(info.Path);
                return "";
            }
            var file = AssetDatabaseRuntime.LoadFile(guid) ?? throw new InvalidOperationException("the asset did not import");
            return file.PostprocessAnswer ?? "";
        }

        /// <summary>The import's callbacks on the loaded <paramref name="file"/> (read from its
        /// documents without postprocessing), and what they changed.</summary>
        internal static void Run(LoadedFile file, bool model)
        {
            var session = AssetPostprocessing.Session(file.Path);
            using var callbacks = AssetDatabaseRuntime.ImportCallbacks(file.Guid);
            try
            {
                if (!(file.MainObject is GameObject root)) return;
                var (oldIds, assets) = ImportedPrefabDocuments.References(file, root);
                var before = new Dictionary<Object, string>(ReferenceEqualityComparer.Instance);
                // Other files' objects the documents reference, found once for the pass.
                var located = new Dictionary<Object, string>(ReferenceEqualityComparer.Instance);
                // Components Unbound cannot write: their fields, to tell whether a postprocessor
                // changed them.
                var unwritable = new Dictionary<Object, string>(ReferenceEqualityComparer.Instance);
                foreach (var o in HierarchyObjects(root))
                {
                    before[o] = ImportedPrefabDocuments.Document(o, oldIds[o], oldIds, assets, located);
                    if (before[o] == null) unwritable[o] = Fields(o);
                }
                List<Material> dropped;
                using (ScriptedImports.Scope())
                    dropped = model ? RunModel(file, root, session) : RunPrefab(file, root, session);
                file.PostprocessAnswer = Changes(file, root, model, oldIds, assets, before, dropped, located);
                foreach (var (o, fields) in unwritable)
                    if (o != null && Fields(o) != fields && s_Unkept.Add(o.GetType()))
                        Debug.LogWarning($"Unbound: {file.Path}: a postprocessor changed its {o.GetType().Name} '{o.name}', which cannot be written as a document yet: scene and prefab instances of the asset keep the import's (reported once per class)");
            }
            catch (Exception e)
            {
                // Scene and prefab instances then expand without the postprocessors' changes.
                Debug.LogError($"Unbound: {file.Path}: running its postprocessors failed; scenes expand it without their changes ({e.GetType().Name}: {e.Message})");
            }
            finally
            {
                // A material whose description waited for the pass is made even when it failed.
                foreach (var o in file.Objects.Values)
                    if (o is Material m && ModelMaterialImport.TakeDeferred(m) is MaterialDescription description)
                        ModelMaterialImport.Describe(session, description, m);
                AssetPostprocessing.EndSession(file.Path);
            }
        }

        static readonly HashSet<Type> s_Unkept = new HashSet<Type>();

        /// <summary>The values of an object's fields (and of the arrays and lists they hold), to
        /// compare an object before and after the callbacks: references by identity.</summary>
        internal static string Fields(Object o)
        {
            var sb = new StringBuilder();
            for (var type = o.GetType(); type != null && type != typeof(Object); type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var value = field.GetValue(o);
                    Append(sb, value);
                    if (value is System.Collections.IEnumerable items && !(value is string))
                        foreach (var item in items) Append(sb, item);
                    sb.Append(';');
                }
            return sb.ToString();
        }

        static void Append(StringBuilder sb, object value)
        {
            if (value == null) sb.Append("null,");
            else if (value.GetType().IsValueType || value is string) sb.Append(value).Append(',');
            else sb.Append('#').Append(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value)).Append(',');
        }

        static List<Material> RunPrefab(LoadedFile file, GameObject root, ImportSession session)
        {
            AssetPostprocessing.EnsurePreprocessed(file.Path, "prefab");
            AssetPostprocessorHooks.Run(PrefabHooks, session, new object[] { root });
            return new List<Material>();
        }

        // ------------------------------------------------------------------ models

        sealed class ModelPass
        {
            internal ImportSession Session;
            internal bool Legacy;
            internal readonly Dictionary<GameObject, (string[], object[])> UserProperties =
                new Dictionary<GameObject, (string[], object[])>(ReferenceEqualityComparer.Instance);
            internal readonly Dictionary<Transform, List<Transform>> FileChildren =
                new Dictionary<Transform, List<Transform>>(ReferenceEqualityComparer.Instance);
            /// <summary>The model's own materials as read (copies), the slots' temporaries start
            /// from them; with their descriptions for "Import via MaterialDescription".</summary>
            internal readonly Dictionary<Material, (Material Source, MaterialDescription Description)> Own =
                new Dictionary<Material, (Material, MaterialDescription)>(ReferenceEqualityComparer.Instance);
            internal readonly HashSet<Material> Made = new HashSet<Material>(ReferenceEqualityComparer.Instance);
            internal readonly HashSet<Material> Used = new HashSet<Material>(ReferenceEqualityComparer.Instance);
        }

        static List<Material> RunModel(LoadedFile file, GameObject root, ImportSession session)
        {
            var pass = new ModelPass
            {
                Session = session,
                Legacy = session.Importer is ModelImporter importer && importer.materialImportMode == ModelImporterMaterialImportMode.ImportStandard,
            };
            using var info = JsonDocument.Parse(Native.Call("model-postprocess", "{\"guid\":" + JsonSerializer.Serialize(file.Guid) + "}"));
            var json = info.RootElement;
            GameObject ById(string id) =>
                long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out long fileId) && file.Objects.TryGetValue(fileId, out var o) ? o as GameObject : null;
            foreach (var node in json.GetProperty("nodes").EnumerateArray())
            {
                var go = ById(node.GetProperty("gameObject").GetString());
                if (go == null) continue;
                var names = new List<string>();
                var values = new List<object>();
                foreach (var p in node.GetProperty("properties").EnumerateArray())
                {
                    names.Add(p.GetProperty("name").GetString());
                    values.Add(UserValue(p.GetProperty("type").GetString(), p.GetProperty("value")));
                }
                pass.UserProperties[go] = (names.ToArray(), values.ToArray());
            }
            // The file's order of the nodes: the children's order while the callbacks run.
            var rank = new Dictionary<Transform, int>(ReferenceEqualityComparer.Instance);
            foreach (var id in json.GetProperty("fileOrder").EnumerateArray())
                if (ById(id.GetString()) is GameObject go) rank[go.transform] = rank.Count;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var children = new List<Transform>(t.m_Children);
                children.Sort((a, b) => Rank(rank, a).CompareTo(Rank(rank, b)));
                pass.FileChildren[t] = children;
                Reorder(t, children);
            }
            foreach (var o in file.Objects.Values)
            {
                if (!(o is Material m)) continue;
                var description = ModelMaterialImport.TakeDeferred(m);
                pass.Own[m] = (new Material(m), description);
            }

            string rootNodeName = json.TryGetProperty("rootNodeName", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            string fileName = root.name;
            var top = rootNodeName != null ? new List<Transform> { root.transform } : new List<Transform>(pass.FileChildren[root.transform]);
            if (rootNodeName != null) root.name = rootNodeName;
            else foreach (var t in top) t.SetParentRaw(null);
            try
            {
                foreach (var t in top)
                {
                    if (t == null) continue;
                    Visit(pass, t);
                    if (t != null) AssetPostprocessorHooks.Run(MeshHierarchyHooks, session, new object[] { t.gameObject });
                }
            }
            finally
            {
                if (rootNodeName != null) root.name = fileName;
                else
                {
                    var added = new List<Transform>(root.transform.m_Children);
                    foreach (var t in added) t.SetParentRaw(null);
                    foreach (var t in top) if (t != null) t.SetParentRaw(root.transform);
                    foreach (var t in added) t.SetParentRaw(root.transform);
                }
            }
            foreach (var o in new List<Object>(file.Objects.Values))
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                    AssetPostprocessorHooks.Run(AnimationHooks, session, new object[] { root, clip });
            AssetPostprocessorHooks.Run(ModelHooks, session, new object[] { root });

            // A model material no slot uses is made as reading it without postprocessors makes it.
            foreach (var (m, (source, description)) in pass.Own)
            {
                if (!pass.Used.Contains(m) && description != null) ModelMaterialImport.Describe(session, description, m);
                Object.DestroyImmediate(source, true);
            }
            if (json.TryGetProperty("sorted", out var sorted) && sorted.GetBoolean())
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) SortByName(t);
            // A material only slots that took another one used is not made.
            var dropped = new List<Material>();
            foreach (var m in pass.Used) if (!pass.Made.Contains(m)) dropped.Add(m);
            return dropped;
        }

        static int Rank(Dictionary<Transform, int> rank, Transform t) => rank.TryGetValue(t, out int r) ? r : int.MaxValue;

        static void Reorder(Transform t, List<Transform> children)
        {
            t.m_Children.Clear();
            t.m_Children.AddRange(children);
        }

        static void Visit(ModelPass pass, Transform t)
        {
            var go = t.gameObject;
            if (go.GetComponent<Renderer>() is Renderer renderer && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = Slot(pass, materials[i], renderer);
                renderer.sharedMaterials = materials;
            }
            if (go != null && pass.UserProperties.TryGetValue(go, out var properties))
                AssetPostprocessorHooks.Run(UserPropertyHooks, pass.Session, new object[] { go, properties.Item1, properties.Item2 });
            if (pass.FileChildren.TryGetValue(t, out var children))
                foreach (var child in children)
                    if (child != null) Visit(pass, child);
        }

        /// <summary>A renderer slot's material after the import's callbacks.</summary>
        static Material Slot(ModelPass pass, Material material, Renderer renderer)
        {
            if (material == null || !pass.Own.TryGetValue(material, out var own))
            {
                var given = AssetPostprocessorHooks.Run(AssignHooks, pass.Session, new object[] { material, renderer }, untilResult: true) as Material;
                return given != null ? given : material;
            }
            pass.Used.Add(material);
            var temporary = new Material(own.Source);
            if (pass.Legacy || own.Description == null)
                AssetPostprocessorHooks.Run(MaterialHooks, pass.Session, new object[] { temporary });
            else
                ModelMaterialImport.Describe(pass.Session, own.Description, temporary);
            var assigned = AssetPostprocessorHooks.Run(AssignHooks, pass.Session, new object[] { temporary, renderer }, untilResult: true) as Material;
            bool keep = assigned != null && assigned != temporary;
            Object.DestroyImmediate(temporary, true);
            if (keep) return assigned;
            if (pass.Made.Add(material))
            {
                if (pass.Legacy || own.Description == null)
                    AssetPostprocessorHooks.Run(MaterialHooks, pass.Session, new object[] { material });
                else
                    ModelMaterialImport.Describe(pass.Session, own.Description, material);
            }
            return material;
        }

        /// <summary>A user property's value as the callback gets it.</summary>
        static object UserValue(string type, JsonElement value)
        {
            float F(int i) => value[i].GetSingle();
            return type switch
            {
                "bool" => value.GetBoolean(),
                "int" => value.GetInt32(),
                "float" => value.GetSingle(),
                "string" => value.GetString(),
                "vector" => new Vector4(F(0), F(1), F(2), F(3)),
                "color" => new Color(F(0), F(1), F(2), F(3)),
                _ => null,
            };
        }

        /// <summary>A node's children sorted by name as the model importer sorts them.</summary>
        static void SortByName(Transform t)
        {
            if (t.m_Children.Count < 2) return;
            var names = new string[t.m_Children.Count];
            for (int i = 0; i < names.Length; i++) names[i] = t.m_Children[i].name;
            using var answer = JsonDocument.Parse(Native.Call("model-child-order", "{\"names\":" + JsonSerializer.Serialize(names) + "}"));
            var children = new List<Transform>(names.Length);
            foreach (var index in answer.RootElement.GetProperty("order").EnumerateArray()) children.Add(t.m_Children[index.GetInt32()]);
            Reorder(t, children);
        }

        // ------------------------------------------------------------------ what changed

        /// <summary>The GameObjects and components of a hierarchy, each GameObject before its
        /// components, depth first.</summary>
        static List<Object> HierarchyObjects(GameObject root)
        {
            var objects = new List<Object>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                objects.Add(t.gameObject);
                foreach (var c in t.gameObject.GetComponents<Component>())
                    if (c != null) objects.Add(c);
            }
            return objects;
        }

        static string Changes(LoadedFile file, GameObject root, bool model, Dictionary<Object, long> oldIds,
            Dictionary<Object, (string, long)> assets, Dictionary<Object, string> before, List<Material> dropped,
            Dictionary<Object, string> located)
        {
            var objects = HierarchyObjects(root);
            var newIds = model ? ModelIds(file, root, objects) : PrefabIds(file, root, objects, oldIds);
            var present = new HashSet<Object>(objects, ReferenceEqualityComparer.Instance);
            var removed = new List<long>();
            foreach (var (o, id) in oldIds) if (!present.Contains(o)) removed.Add(id);
            foreach (var m in dropped)
                foreach (var (id, o) in file.Objects)
                    if (ReferenceEquals(o, m)) { removed.Add(id); assets.Remove(m); break; }
            var remap = new List<(long, long)>();
            var docs = new StringBuilder();
            var ids = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < objects.Count; i++) ids[objects[i]] = newIds[i];
            for (int i = 0; i < objects.Count; i++)
            {
                var o = objects[i];
                long id = newIds[i];
                if (oldIds.TryGetValue(o, out long old))
                {
                    if (old != id) remap.Add((old, id));
                    var now = ImportedPrefabDocuments.Document(o, old, oldIds, assets, located);
                    if (now == null || now == before[o]) continue;
                    docs.Append(WithoutPrefabLinks(ImportedPrefabDocuments.Document(o, id, ids, assets, located)));
                    continue;
                }
                var added = ImportedPrefabDocuments.Document(o, id, ids, assets, located);
                if (added == null)
                {
                    Debug.LogError($"Unbound: {file.Path}: the {o.GetType().Name} a postprocessor added to '{o.name}' cannot be written as a document yet, so scene and prefab instances of the asset do not get it");
                    continue;
                }
                docs.Append(added);
            }

            // The loaded file holds its objects by their new fileIDs.
            foreach (var id in removed) file.Objects.Remove(id);
            foreach (var (old, _) in remap) file.Objects.Remove(old);
            for (int i = 0; i < objects.Count; i++) file.Objects[newIds[i]] = objects[i];
            foreach (var m in dropped) Object.DestroyImmediate(m, true);
            var forgotten = new List<long>(removed);
            foreach (var (old, _) in remap) forgotten.Add(old);
            AssetDatabaseRuntime.RefreshObjects(file, forgotten);

            // The import's other objects (meshes, materials, clips) are the loaded ones, which the
            // postprocessors may have changed: instances reference them rather than copies.
            var external = new List<long>();
            foreach (var (id, o) in file.Objects)
                if (!present.Contains(o) && !(o is GameObject) && !(o is Component)) external.Add(id);
            var answer = new StringBuilder();
            if (external.Count > 0) answer.Append("# external: ").Append(string.Join(" ", external)).Append('\n');
            if (removed.Count > 0) answer.Append("# removed: ").Append(string.Join(" ", removed)).Append('\n');
            if (remap.Count > 0)
            {
                answer.Append("# remap:");
                foreach (var (old, id) in remap) answer.Append(' ').Append(old).Append('=').Append(id);
                answer.Append('\n');
            }
            if (docs.Length > 0) answer.Append(ImportedPrefabDocuments.YamlHeader).Append(docs);
            return answer.ToString();
        }

        /// <summary>A changed document without its prefab links: the native side keeps those of the
        /// document it replaces (a nested prefab's object stays linked to its source).</summary>
        static string WithoutPrefabLinks(string document)
        {
            var sb = new StringBuilder(document.Length);
            foreach (var line in document.Split('\n'))
            {
                if (line.StartsWith("  m_CorrespondingSourceObject:", StringComparison.Ordinal)
                    || line.StartsWith("  m_PrefabInstance:", StringComparison.Ordinal)
                    || line.StartsWith("  m_PrefabAsset:", StringComparison.Ordinal))
                    continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line);
            }
            return sb.ToString();
        }

        const int GameObjectClassId = 1;

        /// <summary>A model's hierarchy objects' fileIDs by their final paths (the native import's
        /// rule, with the .meta's table).</summary>
        static long[] ModelIds(LoadedFile file, GameObject root, List<Object> objects)
        {
            var request = new StringBuilder("{\"guid\":").Append(JsonSerializer.Serialize(file.Guid)).Append(",\"objects\":[");
            for (int i = 0; i < objects.Count; i++)
            {
                var o = objects[i];
                var go = o as GameObject ?? ((Component)o).gameObject;
                bool behaviour = o is MonoBehaviour;
                if (i > 0) request.Append(',');
                request.Append("{\"classId\":").Append(o is GameObject ? GameObjectClassId : EngineTypes.EngineClassIdOf(o.GetType()))
                    .Append(",\"className\":").Append(JsonSerializer.Serialize(behaviour ? "MonoBehaviour" : o.GetType().Name))
                    .Append(",\"path\":").Append(JsonSerializer.Serialize(PathBelow(root.transform, go.transform))).Append('}');
            }
            request.Append("]}");
            using var answer = JsonDocument.Parse(Native.Call("model-file-ids", request.ToString()));
            var ids = new long[objects.Count];
            int n = 0;
            foreach (var id in answer.RootElement.GetProperty("ids").EnumerateArray())
                ids[n++] = long.Parse(id.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture);
            return ids;
        }

        /// <summary>A prefab's objects keep their fileIDs; an added one gets a hash of the asset's
        /// GUID, its path, its class and an index, unused by the prefab's other objects.</summary>
        static long[] PrefabIds(LoadedFile file, GameObject root, List<Object> objects, Dictionary<Object, long> oldIds)
        {
            var used = new HashSet<long>(file.Objects.Keys);
            var ids = new long[objects.Count];
            for (int i = 0; i < objects.Count; i++)
            {
                var o = objects[i];
                if (oldIds.TryGetValue(o, out long old)) { ids[i] = old; continue; }
                var go = o as GameObject ?? ((Component)o).gameObject;
                string key = file.Guid + "/" + PathBelow(root.transform, go.transform) + "/" + o.GetType().Name;
                long id;
                for (int index = 0; ; index++)
                {
                    id = Fnv(key + "/" + index.ToString(CultureInfo.InvariantCulture));
                    if (id != 0 && used.Add(id)) break;
                }
                ids[i] = id;
            }
            return ids;
        }

        /// <summary>FNV-1a over the UTF-8 bytes, as a positive 63-bit number.</summary>
        static long Fnv(string text)
        {
            ulong h = 14695981039346656037UL;
            foreach (var b in Encoding.UTF8.GetBytes(text)) { h ^= b; h *= 1099511628211UL; }
            return (long)(h & 0x7FFFFFFFFFFFFFFFUL);
        }

        static string PathBelow(Transform root, Transform t)
        {
            if (t == root) return "";
            var names = new List<string>();
            for (var at = t; at != null && at != root; at = at.m_Parent) names.Add(at.name);
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
