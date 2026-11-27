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

        /// <summary>The prefabs that were loaded when a prefab their files' nesting reaches was
        /// written in this session (its variants, the prefabs nesting it, and theirs, through every
        /// file of the chain, loaded or not), with that prefab's path: Unbound does not write them
        /// again in the session (<see cref="RefreshDependents"/>, <see cref="Refusal"/>). A prefab
        /// loaded after the write is expanded from the files as they are then and is not marked.</summary>
        static readonly ConditionalWeakTable<LoadedFile, StaleMark> s_StaleSince = new ConditionalWeakTable<LoadedFile, StaleMark>();

        sealed class StaleMark
        {
            /// <summary>The path of the prefab whose write made it stale (the first one).</summary>
            internal string Source;
            /// <summary>Its refresh could not give all of what the Editor's load from its file
            /// gives (<see cref="Refresh"/> said what it kept).</summary>
            internal bool Incomplete;
        }

        /// <summary>The prefabs written in the session whose nesting prefabs the engine could not
        /// be told to expand again: every prefab whose chain reaches one is stale, loaded before or
        /// after.</summary>
        static readonly HashSet<string> s_NotExpandedAgain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
            if (ProjectInfo.ReadOnly)
            {
                Debug.LogError($"Unbound: {file?.Path} is not saved: the project is read-only.");
                return false;
            }
            if (file?.MainObject is not GameObject root || root == null)
            {
                Debug.LogError($"Unbound: {file?.Path} is not saved: the prefab has no root GameObject.");
                return false;
            }
            // A prefab its nesting reaches was written in the session after it was loaded: the Editor
            // loads it again from its file then; Unbound does not, so it does not write it (its
            // objects could hold that prefab's values from before, written as overrides the Editor
            // does not write).
            if (Refused(file)) return false;
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
            // The file holds its objects now: they are no longer dirty (prefab-dirty-saves *.saved;
            // after SavePrefabAsset and applies, unmeasured).
            foreach (var pair in file.Objects)
                if (pair.Value is not null && !pair.Value.m_Destroyed) EditorUtility.ClearDirty(pair.Value);
            foreach (var pair in ids)
                if (pair.Key is not null && !pair.Key.m_Destroyed) EditorUtility.ClearDirty(pair.Key);
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
                catch (Exception e)
                {
                    Debug.LogWarning($"Unbound: {file.Path}: the prefabs nesting it keep their earlier expansion: {e.Message}");
                    if (file.Guid != null) s_NotExpandedAgain.Add(file.Guid);
                }
            }
            if (file.Guid != null) s_Nesting.Remove(file.Guid);
            RefreshDependents(file);
            return true;
        }

        /// <summary>Why a loaded prefab is not written in this session (null: it is): a prefab its
        /// nesting reaches was written after it was loaded (the Editor loads it again from its file;
        /// what Unbound holds of it may not be that), or a prefab of its chain is held as it could
        /// not be refreshed in full (<see cref="Refresh"/>): its instances' overrides are taken
        /// against that prefab's objects. Every prefab write checks it before it changes anything:
        /// SaveAssets, SaveAssetIfDirty, SavePrefabAsset, applies and their undo, a prefab's
        /// contents saved, a hierarchy saved over a prefab.</summary>
        internal static string Refusal(LoadedFile file)
        {
            if (file == null) return null;
            // A prefab a hierarchy is being written over was judged before its objects were
            // replaced (<see cref="ReplaceRefusal"/>): what it holds then is the hierarchy's.
            if (s_Replacing.Contains(file)) return null;
            if (s_StaleSince.TryGetValue(file, out var mark))
                return $"{mark.Source}, which it holds instances of, was saved in this session";
            return file.Guid == null ? null : ChainRefusal(Chain(file.Guid));
        }

        /// <summary>The prefabs a hierarchy is being written over (<see cref="PrefabContents.Replace"/>).</summary>
        static readonly HashSet<LoadedFile> s_Replacing = new HashSet<LoadedFile>(ReferenceEqualityComparer.Instance);

        /// <summary>Why a prefab holding instances of the prefabs <paramref name="chain"/> names is
        /// not written (null: it is).</summary>
        static string ChainRefusal(ICollection<string> chain)
        {
            foreach (var guid in chain)
            {
                if (s_NotExpandedAgain.Contains(guid))
                    return $"{AssetDatabase.GUIDToAssetPath(guid)}, which it holds instances of, was saved in this session and the engine still expands it as it was";
                if (AssetDatabaseRuntime.LoadedOrNull(guid) is LoadedFile held && s_StaleSince.TryGetValue(held, out var heldMark) && heldMark.Incomplete)
                    return $"{held.Path}, which it holds instances of, is held as it was before {heldMark.Source} was saved in this session";
            }
            return null;
        }

        /// <summary>Why a new prefab file holding <paramref name="source"/>'s instances is not
        /// written (null: it is): as <see cref="Refusal"/> for the prefabs they come from.</summary>
        internal static string CreateRefusal(GameObject source)
        {
            var chain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in source.GetComponentsInChildren<Transform>(true))
                if (PrefabLinks.Of(t.m_GameObject)?.Instance is PrefabInstanceData data && data.SourceGuid != null && chain.Add(data.SourceGuid))
                    chain.UnionWith(Chain(data.SourceGuid));
            return ChainRefusal(chain);
        }

        /// <summary>Why a hierarchy is not written over a prefab (null: it is): as
        /// <see cref="CreateRefusal"/> for the prefabs the hierarchy holds and for the prefab the
        /// target's root comes from (a variant's root keeps its base), which is what the prefab holds
        /// once its objects are the hierarchy's; its file's nesting is the one it is leaving.</summary>
        internal static string ReplaceRefusal(GameObject hierarchy, LoadedFile file)
        {
            if (CreateRefusal(hierarchy) is string reason) return reason;
            if (file.MainObject is GameObject root && PrefabLinks.Of(root)?.Instance?.SourceGuid is string source)
            {
                var chain = Chain(source);
                chain.Add(source);
                return ChainRefusal(chain);
            }
            return null;
        }

        /// <summary>Writes a prefab whose objects a hierarchy replaced (<see cref="PrefabContents.Replace"/>),
        /// judged before by <see cref="ReplaceRefusal"/>: it holds nothing from before, so it is no
        /// longer stale, and its save is not judged again by the file it is leaving.</summary>
        internal static bool CommitReplaced(Change change)
        {
            s_StaleSince.Remove(change.File);
            s_Replacing.Add(change.File);
            try { return change.Commit(() => s_Replacing.Remove(change.File)); }
            finally { s_Replacing.Remove(change.File); }
        }

        /// <summary>Says a write is refused (<see cref="Refusal"/>); true when it is.</summary>
        internal static bool Refused(LoadedFile file, string path = null)
        {
            if (Refusal(file) is not string reason) return false;
            Debug.LogWarning($"Unbound: {path ?? file.Path} is not saved: {reason}, and Unbound does not write a prefab after a prefab it holds was saved (the Editor loads it again); its changes since are not saved either.");
            return true;
        }

        /// <summary>
        /// The loaded prefabs whose nesting reaches <paramref name="written"/> through the files (its
        /// variants, the prefabs nesting it, then theirs, by every path, the prefabs between loaded or
        /// not) take it as it was written for what is read of them, as the Editor loads them again
        /// (matches the Unity 6000.0.71f1 Editor, tests/projects/scenesave prefab-dirty-saves
        /// dependent.*, dependentOwn.*, nesting.*, chain.*, diamond.*, own6.*): each of their
        /// instances that holds it takes the modifications its file has now and its source's values
        /// (a prefab between them not loaded is loaded from the new files), their unsaved changes to
        /// those instances dropped, and the prefab is no longer dirty; their scene instances follow.
        /// What the refresh cannot give as the Editor's load from the file would is kept and said:
        /// an instance whose objects no longer match its source keeps its objects; objects added
        /// and not saved stay; unsaved changes elsewhere in the prefab stay. None of them is written
        /// again in the session (<see cref="Save"/>, with a warning): the Editor writes them
        /// unchanged after such a load (dependent.file, nesting.file, chain2.file, middle5.file).
        /// </summary>
        static void RefreshDependents(LoadedFile written)
        {
            if (written.Guid == null) return;
            var chains = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> ChainOf(string guid)
            {
                if (!chains.TryGetValue(guid, out var chain)) chains[guid] = chain = Chain(guid);
                return chain;
            }
            var affected = new List<(string Guid, string Path)>();
            var byGuid = new Dictionary<string, LoadedFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var dependent in AssetDatabaseRuntime.LoadedFiles())
            {
                if (dependent?.Guid == null || string.Equals(dependent.Guid, written.Guid, StringComparison.OrdinalIgnoreCase)) continue;
                if (dependent.MainObject is not GameObject root || root == null) continue;
                if (dependent.Path == null || !dependent.Path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                if (!ChainOf(dependent.Guid).Contains(written.Guid)) continue;
                if (!s_StaleSince.TryGetValue(dependent, out _)) s_StaleSince.AddOrUpdate(dependent, new StaleMark { Source = written.Path });
                affected.Add((dependent.Guid, dependent.Path));
                byGuid[dependent.Guid] = dependent;
            }
            // The prefabs a dependent holds are refreshed before it, so it merges with them as they
            // now are.
            foreach (var guid in SourcesFirst(affected, ChainOf))
                Refresh(byGuid[guid], written, ChainOf);
        }

        /// <summary>One dependent's refresh (<see cref="RefreshDependents"/>).</summary>
        static void Refresh(LoadedFile dependent, LoadedFile written, Func<string, HashSet<string>> chainOf)
        {
            var root = (GameObject)dependent.MainObject;
            var holding = new List<(PrefabInstanceData Data, GameObject Root)>();
            foreach (var (data, instanceRoot) in InstancesIn(root))
                if (string.Equals(data.SourceGuid, written.Guid, StringComparison.OrdinalIgnoreCase) || chainOf(data.SourceGuid).Contains(written.Guid))
                    holding.Add((data, instanceRoot));
            var saved = new HashSet<Object>(ReferenceEqualityComparer.Instance);
            foreach (var o in dependent.Objects.Values) if (o is not null) saved.Add(o);
            var fileInstances = FileInstances(dependent.Guid);
            // What the Editor's load from the file drops and a refresh would keep: objects added and
            // not saved (not those an instance's merge made, nor those the import's postprocessors
            // added, which are never saved), instances not saved, unsaved changes outside the
            // instances it merges.
            int unsavedObjects = 0;
            bool unsavedChanges = false;
            var merged = new HashSet<Object>(ReferenceEqualityComparer.Instance);
            var unsavedInstances = new List<string>();
            var kept = new List<string>();
            foreach (var (data, instanceRoot) in holding)
            {
                if (data.FileId == 0 || fileInstances == null || !fileInstances.ContainsKey(data.FileId)) unsavedInstances.Add(instanceRoot.name);
                else if (fileInstances[data.FileId].RemovedGameObjects > 0) kept.Add(instanceRoot.name);
                else foreach (var o in PrefabInstances.ObjectsOf(data, instanceRoot)) merged.Add(o);
            }
            foreach (var o in HierarchyObjects(root))
            {
                if ((o.HideFlagsNoThrow & HideFlags.DontSaveInEditor) != 0) continue;
                if (!saved.Contains(o))
                {
                    if (PrefabLinks.Of(o)?.Instance is not PrefabInstanceData linked || linked.FileId == 0) unsavedObjects++;
                }
                else if (!merged.Contains(o) && EditorUtility.IsDirty(o)) unsavedChanges = true;
            }
            var change = new Change(dependent);
            var refs = new ValueReferences();
            foreach (var (data, instanceRoot) in holding)
            {
                if (data.FileId == 0 || fileInstances == null || !fileInstances.TryGetValue(data.FileId, out var fromFile) || fromFile.RemovedGameObjects > 0) continue;
                // The modifications its file has now (an override written earlier in the session
                // included), as the Editor loads them; its unsaved ones are dropped.
                data.Stored.Clear();
                data.Stored.AddRange(fromFile.Stored);
                data.ReferenceObjects.Clear();
                data.Edited = false;
                data.RemovedGameObjects = fromFile.RemovedGameObjects;
                var snapshot = PrefabInstances.Capture(data, refs, liveDifferences: false, root: instanceRoot);
                // The components it removes are those its file lists (a component the saved prefab
                // gained is made, one removed and not saved comes back).
                snapshot.Removed.RemoveWhere(c => !fromFile.RemovesComponent(c));
                PrefabInstances.Merge(data, snapshot, instanceRoot);
            }
            foreach (var o in HierarchyObjects(root)) EditorUtility.ClearDirty(o);
            foreach (var o in dependent.Objects.Values)
                if (o is not null && !o.m_Destroyed) EditorUtility.ClearDirty(o);
            // Its scene instances follow it as it is held, what the refresh kept included (the Editor's
            // follow the prefab as loaded from its file: unmeasured, said in the warnings below).
            change.MergeInstances();
            if (kept.Count > 0 || unsavedInstances.Count > 0 || unsavedObjects > 0 || unsavedChanges)
                if (s_StaleSince.TryGetValue(dependent, out var mark)) mark.Incomplete = true;
            if (kept.Count > 0)
                Debug.LogWarning($"Unbound: {dependent.Path} keeps the objects of its instance{(kept.Count > 1 ? "s" : "")} {string.Join(", ", kept)} as they are after {written.Path} was saved: their file removes GameObjects from them, which Unbound does not take again, so what is read of them does not take the saved prefab, as the Editor's load of {dependent.Path} from its file does.");
            if (unsavedInstances.Count > 0)
                Debug.LogWarning($"Unbound: {dependent.Path} keeps its instance{(unsavedInstances.Count > 1 ? "s" : "")} {string.Join(", ", unsavedInstances)}, not saved, as {(unsavedInstances.Count > 1 ? "they are" : "it is")} after {written.Path} was saved; the Editor loads {dependent.Path} again from its file, without {(unsavedInstances.Count > 1 ? "them" : "it")}.");
            if (unsavedObjects > 0)
                Debug.LogWarning($"Unbound: {dependent.Path} keeps {unsavedObjects} object{(unsavedObjects > 1 ? "s" : "")} added to it and not saved after {written.Path} was saved; the Editor loads {dependent.Path} again from its file, without them.");
            if (unsavedChanges)
                Debug.LogWarning($"Unbound: {dependent.Path} keeps its unsaved changes for what is read after {written.Path} was saved; the Editor loads {dependent.Path} again from its file, without them.");
        }

        /// <summary>The GameObjects and components of a prefab asset's hierarchy.</summary>
        static List<Object> HierarchyObjects(GameObject root)
        {
            var list = new List<Object>();
            void Walk(Transform t)
            {
                var go = t.m_GameObject;
                list.Add(go);
                foreach (var c in go.m_Components) if (c is not null && !c.m_Destroyed) list.Add(c);
                foreach (var child in t.m_Children) Walk(child);
            }
            Walk(root.transform);
            return list;
        }

        /// <summary>The prefabs whose instances a loaded prefab holds (its base, the prefabs it
        /// nests), by GUID, as its objects link them now (instances added in the session too).</summary>
        internal static HashSet<string> SourcesHeld(LoadedFile file)
        {
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (file?.MainObject is not GameObject root || root == null) return sources;
            foreach (var (data, _) in InstancesIn(root)) sources.Add(data.SourceGuid);
            return sources;
        }

        /// <summary>The prefabs a prefab holds instances of directly: those its file's
        /// PrefabInstance documents name (the nesting the engine expands), and, while it is loaded,
        /// those its objects link (<see cref="SourcesHeld"/>).</summary>
        internal static HashSet<string> HeldSources(string guid)
        {
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (FileInstances(guid) is { } instances)
                foreach (var data in instances.Values)
                    if (data.SourceGuid != null) sources.Add(data.SourceGuid);
            if (AssetDatabaseRuntime.LoadedOrNull(guid) is LoadedFile loaded) sources.UnionWith(SourcesHeld(loaded));
            sources.Remove(guid);
            return sources;
        }

        /// <summary>Every prefab a prefab's nesting reaches (<see cref="HeldSources"/>, then theirs),
        /// through files whether or not they are loaded.</summary>
        internal static HashSet<string> Chain(string guid)
        {
            var chain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>();
            pending.Push(guid);
            while (pending.Count > 0)
                foreach (var held in HeldSources(pending.Pop()))
                    if (!string.Equals(held, guid, StringComparison.OrdinalIgnoreCase) && chain.Add(held)) pending.Push(held);
            return chain;
        }

        /// <summary>A PrefabInstance document of a prefab's file: its source, modifications and the
        /// source components it removes.</summary>
        internal sealed class FileInstance
        {
            internal readonly PrefabInstanceData Data;
            readonly HashSet<(string Guid, long FileId)> m_RemovedComponents = new HashSet<(string, long)>();

            internal FileInstance(PrefabInstanceData data, YText body)
            {
                Data = data;
                foreach (var r in body.Get("m_Modification")?.Get("m_RemovedComponents")?.Items ?? new List<YText>())
                    m_RemovedComponents.Add(((r.Get("guid")?.Text ?? data.SourceGuid ?? "").ToLowerInvariant(), SceneFiles.ParseFileId(r)));
            }

            internal long FileId { get => Data.FileId; set => Data.FileId = value; }
            internal string SourceGuid => Data.SourceGuid;
            internal int RemovedGameObjects => Data.RemovedGameObjects;
            internal List<(ObjectRef Target, string Path, string Value, ObjectRef Reference)> Stored => Data.Stored;

            /// <summary>Whether the document lists a source component as removed.</summary>
            internal bool RemovesComponent(Object component) =>
                AssetDatabaseRuntime.TryGetLocation(component, out var guid, out var fileId) && m_RemovedComponents.Contains((guid.ToLowerInvariant(), fileId));
        }

        sealed class FileNesting
        {
            internal string File;
            internal long Length;
            internal DateTime Written;
            internal Dictionary<long, FileInstance> Instances;
        }

        static readonly Dictionary<string, FileNesting> s_Nesting = new Dictionary<string, FileNesting>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The PrefabInstance documents of a prefab's file as it is on disk, by file id
        /// (null: no prefab file, or not in the text serialization); kept while the file's length
        /// and modification time stay.</summary>
        internal static Dictionary<long, FileInstance> FileInstances(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            // The file found before, while it is there (finding it can ask the engine).
            var path = s_Nesting.TryGetValue(guid, out var known) && File.Exists(known.File) ? known.File : FilePathOf(guid);
            if (path == null) return null;
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            if (s_Nesting.TryGetValue(guid, out var cached) && cached.File == path && cached.Length == info.Length && cached.Written == info.LastWriteTimeUtc)
                return cached.Instances;
            Dictionary<long, FileInstance> instances = null;
            try
            {
                var text = File.ReadAllText(path);
                if (text.StartsWith(UnityYamlText.Preamble, StringComparison.Ordinal) || text.StartsWith(UnityYamlText.Preamble.Replace("\n", "\r\n"), StringComparison.Ordinal))
                {
                    instances = new Dictionary<long, FileInstance>();
                    foreach (var doc in UnityYamlText.Parse(text))
                    {
                        if (doc.ClassId != EngineTypes.PrefabInstanceId || doc.Header.EndsWith(" stripped", StringComparison.Ordinal)) continue;
                        var data = new FileInstance(PrefabLinks.ReadInstance(doc.Body.ToNode(), inAsset: true), doc.Body);
                        data.FileId = doc.FileId;
                        instances[doc.FileId] = data;
                    }
                }
            }
            catch (IOException) { instances = null; }
            s_Nesting[guid] = new FileNesting { File = path, Length = info.Length, Written = info.LastWriteTimeUtc, Instances = instances };
            return instances;
        }

        /// <summary>The file on disk of a prefab asset's GUID (null: none known, or not a prefab).</summary>
        static string FilePathOf(string guid)
        {
            var loaded = AssetDatabaseRuntime.LoadedOrNull(guid);
            var assetPath = loaded?.Path;
            if (assetPath == null)
            {
                try { assetPath = AssetDatabase.GUIDToAssetPath(guid); }
                catch (InvalidOperationException) { assetPath = null; }
            }
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return null;
            if (assetPath.StartsWith("Assets/", StringComparison.Ordinal)) return Path.Combine(ProjectInfo.ProjectPath, assetPath);
            if (Native.IsReady)
            {
                try { return AssetDatabaseRuntime.Info(guid)?.File; }
                catch (Exception e) when (e is InvalidOperationException || e is System.Text.Json.JsonException) { return null; }
            }
            return Path.Combine(ProjectInfo.ProjectPath, assetPath);
        }

        /// <summary>The files to write in the order that writes a prefab before the prefabs holding
        /// instances of it, through every prefab between them (in the save or not), else in path
        /// order (<paramref name="chain"/>: every prefab a file's nesting reaches).</summary>
        internal static List<string> SourcesFirst(IEnumerable<(string Guid, string Path)> files, Func<string, ICollection<string>> chain)
        {
            var byPath = new List<(string Guid, string Path)>(files);
            byPath.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in byPath) pending.Add(f.Guid);
            var order = new List<string>();
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Visit(string guid)
            {
                if (!pending.Contains(guid) || !visiting.Add(guid)) return;
                foreach (var held in chain(guid) ?? Array.Empty<string>())
                    if (!string.Equals(held, guid, StringComparison.OrdinalIgnoreCase)) Visit(held);
                pending.Remove(guid);
                order.Add(guid);
            }
            foreach (var f in byPath) Visit(f.Guid);
            return order;
        }

        /// <summary>Marks a loaded prefab as one that could not take a prefab it holds instances of.</summary>
        internal static void MarkStaleForTests(LoadedFile file, string sourcePath, bool incomplete = false) => s_StaleSince.AddOrUpdate(file, new StaleMark { Source = sourcePath, Incomplete = incomplete });

        /// <summary>The instances inside a prefab asset's hierarchy, with their roots, in hierarchy
        /// order.</summary>
        static List<(PrefabInstanceData Data, GameObject Root)> InstancesIn(GameObject root)
        {
            var found = new List<(PrefabInstanceData, GameObject)>();
            var seen = new HashSet<PrefabInstanceData>();
            void Walk(Transform t)
            {
                if (PrefabLinks.Of(t.m_GameObject)?.Instance is PrefabInstanceData data && data.InAsset && data.SourceGuid != null && seen.Add(data))
                    found.Add((data, t.m_GameObject));
                foreach (var child in t.m_Children) Walk(child);
            }
            Walk(root.transform);
            return found;
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
            if (CreateRefusal(source) is string reason)
            {
                Debug.LogWarning($"Unbound: {path} is not saved: {reason}, and Unbound does not write a prefab after a prefab it holds was saved (the Editor loads it again).");
                return null;
            }
            var text = NewPrefabText(path, source);
            return text == null ? null : Imported(path, text);
        }

        /// <summary>The text of <see cref="Create"/>'s file; null with the reason logged.</summary>
        internal static string NewPrefabText(string path, GameObject source)
        {
            var instances = new List<(PrefabInstanceData Data, long FileId)>();
            foreach (var t in source.GetComponentsInChildren<Transform>(true))
            {
                if (PrefabLinks.Of(t.m_GameObject)?.Instance is not PrefabInstanceData data) continue;
                // Not measured, refused: an instance placed under another instance's object (how the
                // outer one lists it), one whose file lists GameObjects removed from it (kept nowhere else).
                var parent = t.parent == null ? null : PrefabLinks.Of(t.parent.m_GameObject)?.Instance;
                if (parent != null && parent != data && t != source.transform)
                    throw new NotSupportedException($"Unbound does not make new prefabs holding an instance under another instance's object yet ({t.name} in {path})");
                if (data.RemovedGameObjects > 0)
                    throw new NotSupportedException($"Unbound does not make new prefabs of instances with removed GameObjects yet ({t.name} in {path})");
                if (!instances.Exists(i => i.Data == data)) instances.Add((data, data.FileId));
            }
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
            if (guid != null) s_Nesting.Remove(guid);
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
                MergeInstances();
                return true;
            }

            /// <summary>The scene instances take the asset as it is now, keeping their overrides,
            /// and are told.</summary>
            internal void MergeInstances()
            {
                var updated = new List<GameObject>();
                foreach (var (data, root, snapshot) in m_Instances)
                {
                    if (root == null) continue;
                    PrefabInstances.Merge(data, snapshot);
                    updated.Add(root);
                }
                Updated(updated);
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

        internal static PrefabContents Of(GameObject root)
        {
            foreach (var c in s_Loaded) if (ReferenceEquals(c.Root, root)) return c;
            return null;
        }

        /// <summary>The contents of the prefab at a project path, in a preview scene named after
        /// the prefab whose path is the prefab's (measured: tests/projects/scenesave
        /// scene-api-rest contents.scene).</summary>
        internal static GameObject Load(LoadedFile file, string assetPath)
        {
            var assetRoot = (GameObject)file.MainObject;
            var scene = SceneRegistry.CreatePreview(System.IO.Path.GetFileNameWithoutExtension(assetPath), assetPath);
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

        /// <summary>A preview scene is being closed: contents loaded into it are no longer
        /// contents (unloading them then throws, measured: tests/projects/scenesave scene-api-rest
        /// contents.unloadAfterClose).</summary>
        internal static void SceneClosing(SceneData scene) => s_Loaded.RemoveAll(c => c.Scene == scene);

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
            // Refused before the asset takes the contents.
            if (PrefabAssets.Refused(contents.File)) return false;
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
            // Its objects are replaced by the hierarchy's (the root and its Transform take their
            // values): what it held before a save of a prefab it held does not matter, the prefabs
            // the hierarchy holds do.
            if (PrefabAssets.ReplaceRefusal(root, file) is string reason)
            {
                Debug.LogWarning($"Unbound: {file.Path} is not saved: {reason}, and Unbound does not write a prefab after a prefab it holds was saved (the Editor loads it again).");
                return false;
            }
            var change = new PrefabAssets.Change(file);
            Sync(root, assetRoot, map, change.Refs);
            return PrefabAssets.CommitReplaced(change);
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
