using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// The project's native YAML assets (ScriptableObjects, materials) written back as the Unity
    /// 6000.0.71f1 Editor saves them (tests/projects/scenesave): AssetDatabase.SaveAssets writes the
    /// file of every dirty asset object, in Edit and in Play mode, and the Editor's exit writes those
    /// still dirty (an edit made in Play mode stays once Play mode ends). An object is dirty after
    /// EditorUtility.SetDirty or a change through a material's API; a script's field set by script
    /// alone dirties nothing and is not saved. A saved object is no longer dirty. Each document of the
    /// file starts from the file's: a script object's is written whole, a material's fields Unbound
    /// models are replaced, the others stay as written.
    /// </summary>
    internal static class AssetFiles
    {
        /// <summary>Writes the files of the dirty asset objects (<paramref name="only"/>: of that
        /// object only, when it is dirty).</summary>
        internal static void SaveDirty(Object only = null)
        {
            Undo.FlushDirtiness();
            var dirty = new List<int>(EditorUtility.DirtyIds);
            var byGuid = new Dictionary<string, List<Object>>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in dirty)
            {
                var o = ObjectRegistry.FindById(id);
                if (o == null || !o.IsPersistentAsset) continue;
                if (only is not null && !ReferenceEquals(o, only)) continue;
                if (!AssetDatabaseRuntime.TryGetLocation(o, out var guid, out _) || guid.StartsWith("0000000000000000", StringComparison.Ordinal)) continue;
                if (!byGuid.TryGetValue(guid, out var list)) byGuid[guid] = list = new List<Object>();
                list.Add(o);
            }
            foreach (var pair in byGuid)
            {
                try { Save(pair.Key, pair.Value); }
                catch (Exception e) when (e is IOException || e is NotSupportedException || e is UnauthorizedAccessException)
                {
                    Debug.LogError($"Unbound: {AssetDatabaseRuntime.PathOf(pair.Key)} is not saved: {e.Message}");
                }
            }
        }

        /// <summary>The documents of an asset file in the text serialization holding a document for
        /// each dirty object; null for a binary serialized file or one that does not hold them (it is
        /// then left as it is).</summary>
        internal static List<YTextDocument> TextDocuments(string text, Dictionary<long, Object> objects, List<Object> dirty)
        {
            if (!text.StartsWith(UnityYamlText.Preamble, StringComparison.Ordinal)) return null;
            var docs = UnityYamlText.Parse(text);
            foreach (var o in dirty)
                if (!docs.Exists(d => objects.TryGetValue(d.FileId, out var x) && ReferenceEquals(x, o))) return null;
            return docs;
        }

        static void Save(string guid, List<Object> dirty)
        {
            var info = AssetDatabaseRuntime.Info(guid);
            var loaded = AssetDatabaseRuntime.LoadFile(guid);
            if (info == null || loaded == null || string.IsNullOrEmpty(info.File)) return;
            if (info.Kind != "yaml")
            {
                // An imported asset's objects come from its source file, which the Editor does not
                // write back; a prefab's are written by prefab saves.
                if (info.Kind == "prefab")
                    Debug.LogWarning($"Unbound: {info.Path} is not saved: Unbound does not write prefab assets from AssetDatabase.SaveAssets yet.");
                foreach (var o in dirty) EditorUtility.ClearDirty(o);
                return;
            }
            var docs = TextDocuments(File.ReadAllText(info.File), loaded.Objects, dirty);
            if (docs == null)
            {
                Debug.LogError($"Unbound: {info.Path} is not saved: Unbound writes Unity's text serialization only, and this file is not in it.");
                return;
            }
            var ids = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance);
            foreach (var p in loaded.Objects)
                if (p.Value is not null) ids[p.Value] = p.Key;
            var locations = new AssetDatabaseRuntime.Locations();
            var keptAsRead = new SortedSet<string>(StringComparer.Ordinal);
            int unlocated = 0;
            foreach (var doc in docs)
            {
                if (!loaded.Objects.TryGetValue(doc.FileId, out var o) || o == null) continue;
                if (!NativeDocuments.Write(doc, o, ids, locations, ref unlocated) && dirty.Contains(o)) keptAsRead.Add(o.GetType().Name);
            }
            if (keptAsRead.Count > 0)
                Debug.LogWarning($"Unbound: {info.Path} is saved with its {string.Join(", ", keptAsRead)} objects as they were read: Unbound does not write their fields yet, so changes to them are not saved.");
            if (unlocated > 0)
                Debug.LogWarning($"Unbound: {info.Path} is saved with {unlocated} references to assets Unbound cannot name written as none.");
            SceneFiles.WriteFile(info.File, UnityYamlText.Write(docs));
            foreach (var o in loaded.Objects.Values)
                if (o is not null) EditorUtility.ClearDirty(o);
        }
    }
}
