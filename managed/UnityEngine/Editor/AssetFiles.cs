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
    /// models are replaced, the others stay as written. An asset CreateAsset made is written whole
    /// from its objects, also when AddObjectToAsset added one.
    /// </summary>
    internal static class AssetFiles
    {
        /// <summary>Writes the files of the dirty asset objects (<paramref name="only"/>: of that
        /// object only, when it is dirty); with <paramref name="unchangedStays"/>, a file whose text
        /// would not change is left untouched.</summary>
        /// <summary>Whether a file holding <paramref name="existing"/> would be left as it is when
        /// <paramref name="written"/> is saved over it: only when the text is the same, line endings
        /// included (a CRLF file is written with LF; matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/spriteatlaseditor v1wholewritecrlf).</summary>
        internal static bool Unchanged(string existing, string written) => string.Equals(existing, written, StringComparison.Ordinal);

        internal static void SaveDirty(Object only = null, bool unchangedStays = false)
        {
            Undo.FlushDirtiness();
            // The assets CreateAsset made are written from their objects (and so no longer dirty).
            AssetDatabase.SaveCreated(only);
            var dirty = new List<int>(EditorUtility.DirtyIds);
            var byGuid = new Dictionary<string, List<Object>>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in dirty)
            {
                var o = ObjectRegistry.FindById(id);
                if (o == null || !o.IsPersistentAsset) continue;
                // An asset CreateAsset made is written by SaveCreated, which reports one it cannot write.
                if (AssetDatabase.CreatedThisSession(o)) continue;
                // A V2 sprite atlas is an imported object (SpriteAtlasImporter); SaveAssets writes
                // neither the .spriteatlasv2 nor its .meta and leaves the object dirty (matches the
                // Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor v2include).
                if (o is UnityEngine.U2D.SpriteAtlas sa && sa.IsFromImporter) continue;
                if (only is not null && !ReferenceEquals(o, only)) continue;
                if (!AssetDatabaseRuntime.TryGetLocation(o, out var guid, out _) || guid.StartsWith("0000000000000000", StringComparison.Ordinal)) continue;
                if (!byGuid.TryGetValue(guid, out var list)) byGuid[guid] = list = new List<Object>();
                list.Add(o);
            }
            foreach (var pair in byGuid)
            {
                try { Save(pair.Key, pair.Value, unchangedStays); }
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
            // A file with CRLF line endings is text serialization too (Parse reads it as LF).
            if (!text.StartsWith(UnityYamlText.Preamble, StringComparison.Ordinal)
                && !text.StartsWith(UnityYamlText.Preamble.Replace("\n", "\r\n"), StringComparison.Ordinal)) return null;
            var docs = UnityYamlText.Parse(text);
            foreach (var o in dirty)
                if (!docs.Exists(d => objects.TryGetValue(d.FileId, out var x) && ReferenceEquals(x, o))) return null;
            return docs;
        }

        static void Save(string guid, List<Object> dirty, bool unchangedStays)
        {
            var info = AssetDatabaseRuntime.Info(guid);
            var loaded = AssetDatabaseRuntime.LoadFile(guid);
            if (info == null || loaded == null || string.IsNullOrEmpty(info.File)) return;
            if (info.Kind != "yaml")
            {
                // An imported asset's objects come from its source file, which the Editor does not
                // write back: they stay dirty, with no message (matches the Unity 6000.0.71f1 Editor,
                // tests/projects/scenesave native-saves model.dirtyAfter, a model's mesh). A prefab's
                // are written by prefab saves.
                if (info.Kind == "prefab")
                    Debug.LogWarning($"Unbound: {info.Path} is not saved: Unbound does not write prefab assets from AssetDatabase.SaveAssets yet.");
                if (info.Kind == "prefab" || !Compat.ReadAssetSavesMeasured)
                    foreach (var o in dirty) EditorUtility.ClearDirty(o);
                return;
            }
            // A package the project does not hold (registry, built-in, Git) is shared, not the
            // project's: nothing is written into it, and its objects stay dirty (unmeasured).
            if (AssetDatabase.IsImmutablePackagePath(info.Path))
            {
                Debug.LogWarning($"Unbound: {info.Path} is not saved: it is in a package the project does not hold.");
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
            var written = UnityYamlText.Write(docs);
            if (!unchangedStays || !File.Exists(info.File) || !Unchanged(File.ReadAllText(info.File), written))
                SceneFiles.WriteFile(info.File, written);
            foreach (var o in loaded.Objects.Values)
                if (o is not null) EditorUtility.ClearDirty(o);
        }
    }
}
