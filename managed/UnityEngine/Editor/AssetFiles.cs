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
        /// <summary>Whether a file holding <paramref name="existing"/> would be left as it is when
        /// <paramref name="written"/> is saved over it: only when the text is the same, line endings
        /// included (a CRLF file is written with LF; matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/spriteatlaseditor v1wholewritecrlf).</summary>
        internal static bool Unchanged(string existing, string written) => string.Equals(existing, written, StringComparison.Ordinal);

        /// <summary>Writes the files of the dirty asset objects (<paramref name="only"/>: of that
        /// object only, when it is dirty); with <paramref name="unchangedStays"/>, a file whose text
        /// would not change is left untouched.</summary>
        internal static void SaveDirty(Object only = null, bool unchangedStays = false, ICollection<string> onlyFiles = null, bool willSave = false)
        {
            Undo.FlushDirtiness();
            // The assets CreateAsset made are written from their objects (and so no longer dirty);
            // with willSave, once the processors have returned their paths.
            var created = willSave ? AssetDatabase.CreatedWithChanges(only, onlyFiles) : null;
            if (!willSave) AssetDatabase.SaveCreated(only, onlyFiles);
            var dirty = new List<int>(EditorUtility.DirtyIds);
            var byGuid = new Dictionary<string, List<Object>>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in dirty)
            {
                var o = ObjectRegistry.FindById(id);
                if (!Saved(o, out var guid)) continue;
                if (only is not null && !ReferenceEquals(o, only)) continue;
                if (onlyFiles != null && !onlyFiles.Contains(guid)) continue;
                if (!byGuid.TryGetValue(guid, out var list)) byGuid[guid] = list = new List<Object>();
                list.Add(o);
            }
            // AssetDatabase.SaveAssets first hands the processors' OnWillSaveAssets the paths of the
            // dirty assets, those CreateAsset made included (in path order; the Editor's order for
            // several is unmeasured; matches the Unity 6000.0.71f1 Editor, tests/projects/vfxwrites
            // material save assets) and saves the paths they return; a processor may have saved some
            // itself (the VFX package writes its graphs).
            try
            {
                if (willSave)
                {
                    // A package the project does not hold is not the project's to save: its paths
                    // are not offered, and Save leaves them as before.
                    var offered = new List<string>(byGuid.Keys);
                    offered.AddRange(created);
                    var paths = new List<string>();
                    foreach (var guid in offered)
                        if (!AssetDatabase.IsImmutablePackagePath(AssetDatabaseRuntime.PathOf(guid)) && !paths.Contains(AssetDatabaseRuntime.PathOf(guid))) paths.Add(AssetDatabaseRuntime.PathOf(guid));
                    paths.Sort(StringComparer.Ordinal);
                    VisualEffectWrites.RefusedInSave = new HashSet<UnityEditor.VFX.VisualEffectResource>(ReferenceEqualityComparer.Instance);
                    var save = new HashSet<string>(AssetModificationProcessorInternal.OnWillSaveAssets(paths.ToArray()), StringComparer.Ordinal);
                    created.RemoveAll(guid => !save.Contains(AssetDatabaseRuntime.PathOf(guid)) && !AssetDatabase.IsImmutablePackagePath(AssetDatabaseRuntime.PathOf(guid)));
                    if (created.Count > 0) AssetDatabase.SaveCreated(only, new HashSet<string>(created, StringComparer.OrdinalIgnoreCase));
                    foreach (var guid in new List<string>(byGuid.Keys))
                    {
                        var path = AssetDatabaseRuntime.PathOf(guid);
                        byGuid[guid].RemoveAll(o => o == null || !EditorUtility.IsDirty(o));
                        if (byGuid[guid].Count == 0 || (!save.Contains(path) && !AssetDatabase.IsImmutablePackagePath(path))) byGuid.Remove(guid);
                    }
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
            finally
            {
                VisualEffectWrites.RefusedInSave = null;
            }
        }

        /// <summary>Whether a dirty object is one a save writes, and its file's GUID.</summary>
        static bool Saved(Object o, out string guid)
        {
            guid = null;
            if (o == null || !o.IsPersistentAsset) return false;
            // An asset CreateAsset made is written by SaveCreated, which reports one it cannot write.
            if (AssetDatabase.CreatedThisSession(o)) return false;
            // A V2 sprite atlas is an imported object (SpriteAtlasImporter); SaveAssets writes
            // neither the .spriteatlasv2 nor its .meta and leaves the object dirty (matches the
            // Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor v2include).
            if (o is UnityEngine.U2D.SpriteAtlas sa && sa.IsFromImporter) return false;
            return AssetDatabaseRuntime.TryGetLocation(o, out guid, out _) && !guid.StartsWith("0000000000000000", StringComparison.Ordinal);
        }

        /// <summary>AssetDatabase.SaveAssetIfDirty of a file: when the file has something to save
        /// (any of its objects dirty, not only the one asked about), the processors'
        /// OnWillSaveAssets get its path, and the file is saved when they return it; with nothing
        /// to save they are not called (matches the Unity 6000.0.71f1 Editor, tests/projects/vfxwrites
        /// model save if dirty asset: a graph saved through its asset for its dirty model;
        /// material save if dirty: a material CreateAsset made).</summary>
        internal static void SaveFileIfDirty(string guid)
        {
            Undo.FlushDirtiness();
            if (!HasChanges(guid)) return;
            var path = AssetDatabaseRuntime.PathOf(guid);
            try
            {
                VisualEffectWrites.RefusedInSave = new HashSet<UnityEditor.VFX.VisualEffectResource>(ReferenceEqualityComparer.Instance);
                // A package the project does not hold is not offered (as in SaveAssets).
                if (!AssetDatabase.IsImmutablePackagePath(path)
                    && Array.IndexOf(AssetModificationProcessorInternal.OnWillSaveAssets(new[] { path }), path) < 0) return;
                SaveDirty(onlyFiles: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { guid });
            }
            finally
            {
                VisualEffectWrites.RefusedInSave = null;
            }
        }

        static bool HasChanges(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return false;
            if (AssetDatabase.CreatedHasChanges(guid)) return true;
            foreach (var id in EditorUtility.DirtyIds)
                if (Saved(ObjectRegistry.FindById(id), out var g) && string.Equals(g, guid, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
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
            // A save writes a graph file from its resource's objects (VisualEffectResource.WriteAsset):
            // AssetDatabase.SaveAssets, SaveAssetIfDirty and the Editor's exit alike
            // (tests/projects/vfxwrites).
            if (info.Kind == "vfx")
            {
                // A package's own graphs (its templates) are read only: they stay as they are.
                if (AssetDatabase.IsImmutablePackagePath(info.Path)) return;
                foreach (var o in loaded.Objects.Values)
                    if (o is UnityEditor.VFX.VisualEffectResource resource)
                    {
                        // Refused while the package's OnWillSaveAssets wrote it: said once already.
                        if (VisualEffectWrites.RefusedInSave?.Contains(resource) != true) VisualEffectWrites.Write(resource);
                        return;
                    }
            }
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
            Written(loaded.Objects.Values);
        }

        /// <summary>What the objects of a file just written become: a material no longer has the
        /// names set from script that its shader does not declare
        /// (<see cref="Material.ForgetScriptNamesOutsideShader"/>).</summary>
        internal static void Written(IEnumerable<Object> objects)
        {
            foreach (var o in objects)
                if (o is Material material && material != null) material.ForgetScriptNamesOutsideShader();
        }
    }
}
