using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// Writes an open scene to its file as the Unity 6000.0.71f1 Editor saves it
    /// (tests/projects/scenesave): every object is one document, ordered by file id; the settings
    /// documents come first (file ids 1 to 4) and SceneRoots last. An object read from the scene
    /// keeps its file id, and its document starts from the one the file has, so whatever Unbound does
    /// not model stays as written and only the fields Unbound writes change (its GameObject's
    /// components, names, flags, its Transform, the serialized fields of its scripts, the fields of
    /// the engine components that declare a writer). A new object gets a new file id (the Editor's
    /// are random per session; a GameObject's components follow it) that it keeps from then on, and
    /// starts from the document the release writes for a new object of its class
    /// (tests/projects/scenesave defaults.unity). Documents of the file Unbound made no object of
    /// (a missing script, a class Unbound does not load) are kept while their GameObject lives.
    /// Prefab instances keep their PrefabInstance document: the instance's engine components and
    /// GameObjects record each written field that differs from their source prefab's as a
    /// modification (as the Editor's own setters record them), its scripts only after
    /// PrefabUtility.RecordPrefabInstancePropertyModifications (measured: a script change alone is
    /// not saved); components added to an instance are listed in m_AddedComponents with a stripped
    /// document of their GameObject.
    /// </summary>
    internal static class SceneFiles
    {
        const int OcclusionCullingSettingsId = 29, RenderSettingsId = 104, LightmapSettingsId = 157, NavMeshSettingsId = 196;
        const int MonoBehaviourId = 114, SceneRootsId = 1660057539;
        /// <summary>SceneRoots' file id: the largest, so it is the last document.</summary>
        const long SceneRootsFileId = long.MaxValue;

        /// <summary>Whether a path names an asset file of the project: relative, under Assets/ or
        /// a package folder inside the project's Packages/ (an embedded package), no <c>..</c>,
        /// with the extension.</summary>
        internal static bool IsAssetPath(string path, string extension)
        {
            var p = ProjectRelative(path).Replace('\\', '/');
            var parts = p.Split('/');
            if (Array.IndexOf(parts, "..") >= 0 || !p.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return false;
            if (p.StartsWith("Assets/", StringComparison.Ordinal)) return true;
            return parts.Length > 2 && parts[0] == "Packages" && Directory.Exists(Path.Combine(ProjectInfo.ProjectPath, "Packages", parts[1]));
        }

        /// <summary>A path inside the project made relative to it (others are returned as given).</summary>
        internal static string ProjectRelative(string path)
        {
            if (!Path.IsPathRooted(path)) return path;
            var project = Path.GetFullPath(ProjectInfo.ProjectPath).TrimEnd('/') + "/";
            var full = Path.GetFullPath(path);
            return full.StartsWith(project, StringComparison.Ordinal) ? full.Substring(project.Length) : path;
        }

        /// <summary>Replaces a file's text whole: written beside it, then moved over it, so a failed
        /// write leaves the file as it was.</summary>
        internal static void WriteFile(string file, string text)
        {
            // A linked file is written where it links to; the temporary file's leading dot keeps the
            // Editor's asset database from importing it.
            if (File.Exists(file) && new FileInfo(file).ResolveLinkTarget(true) is FileSystemInfo target) file = target.FullName;
            var temporary = Path.Combine(Path.GetDirectoryName(file), "." + Path.GetFileName(file) + ".unbound-save");
            try
            {
                File.WriteAllText(temporary, text);
                File.Move(temporary, file, overwrite: true);
            }
            catch
            {
                try { File.Delete(temporary); } catch (IOException) { }
                throw;
            }
        }

        /// <summary>The scene's file text; null (with an error logged) when a part of it cannot be written.</summary>
        internal static string Write(SceneData scene) => new SceneWriter(scene).Write();

        /// <summary>Whether the scene can be saved to the path at all (it is loaded, the path names a
        /// scene asset); the reason is logged when not.</summary>
        internal static bool CanSave(SceneData scene, string path)
        {
            if (!scene.IsLoaded)
            {
                Debug.LogError($"Unbound: {path} is not saved: the scene is not loaded.");
                return false;
            }
            if (!IsAssetPath(path, ".unity"))
            {
                Debug.LogError($"Unbound: {path} is not saved: a scene is saved under Assets/ (or an embedded package) with the .unity extension.");
                return false;
            }
            return true;
        }

        /// <summary>Writes the scene to <paramref name="path"/>, with a new .meta when the file is
        /// new; false, with the reason logged, when it cannot be written.</summary>
        internal static bool Save(SceneData scene, string path)
        {
            if (!CanSave(scene, path)) return false;
            path = ProjectRelative(path);
            string text;
            try { text = Write(scene); }
            catch (NotSupportedException e)
            {
                Debug.LogError($"Unbound: {path} is not saved: {e.Message}");
                return false;
            }
            if (text == null) return false;
            try
            {
                var file = Path.Combine(ProjectInfo.ProjectPath, path);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                WriteFile(file, text);
                NewAssetMeta.Write(path, NewAssetMeta.DefaultImporter);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"Unbound: {path} is not saved: {e.Message}");
                return false;
            }
            return true;
        }

        /// <summary>The release's documents for new objects: a new scene's settings, and each class's
        /// document as a new component of a GameObject named after the class.</summary>
        internal static class Defaults
        {
            static List<YTextDocument> s_Documents;
            static Dictionary<string, YTextDocument> s_ByClass;

            static void Load()
            {
                if (s_Documents != null) return;
                if (!Compat.NewObjectDocumentsMeasured)
                    throw new NotSupportedException("Unbound has no measured documents of new objects for this Unity release, so it cannot save its scenes");
                using var stream = typeof(SceneFiles).Assembly.GetManifestResourceStream("Unbound.default-documents-6000.0.unity")
                    ?? throw new InvalidOperationException("the measured documents of new objects are missing from the engine");
                using var reader = new StreamReader(stream);
                s_Documents = UnityYamlText.Parse(reader.ReadToEnd());
                s_ByClass = new Dictionary<string, YTextDocument>(StringComparer.Ordinal);
                var byId = new Dictionary<long, YTextDocument>();
                foreach (var d in s_Documents) byId[d.FileId] = d;
                // A class's document is the one on the GameObject named after it (others there were
                // added as its requirements).
                foreach (var d in s_Documents)
                {
                    if (d.ClassId != EngineTypes.GameObjectId) continue;
                    var name = d.Body.Get("m_Name")?.Text ?? "";
                    var components = d.Body.Get("m_Component");
                    if (name == "UnityEngine.GameObject") s_ByClass["GameObject"] = d;
                    if (components == null) continue;
                    foreach (var item in components.Items)
                    {
                        var id = ParseFileId(item.Get("component"));
                        if (!byId.TryGetValue(id, out var c)) continue;
                        if (name.EndsWith("." + c.ClassName, StringComparison.Ordinal) || !s_ByClass.ContainsKey(c.ClassName))
                            s_ByClass[c.ClassName] = c;
                    }
                }
            }

            /// <summary>A copy of the new-object document of the class (null: none measured).</summary>
            internal static YText Body(string className)
            {
                Load();
                return s_ByClass.TryGetValue(className, out var d) ? d.Body.Clone() : null;
            }

            /// <summary>A new scene's settings document of the class.</summary>
            internal static YTextDocument Settings(int classId)
            {
                Load();
                foreach (var d in s_Documents)
                    if (d.ClassId == classId) return new YTextDocument { Header = d.Header, ClassId = d.ClassId, FileId = d.FileId, ClassName = d.ClassName, Body = d.Body.Clone() };
                throw new NotSupportedException($"no measured {classId} document");
            }
        }

        internal static long ParseFileId(YText reference)
        {
            if (reference == null) return 0;
            var id = reference.Get("fileID");
            return id != null && long.TryParse(id.Text, out var v) ? v : 0;
        }

        static YText Raw(string raw) => YText.Scalar(raw);

        /// <summary>A flow map as the writer gives it (references, vectors).</summary>
        internal static YText FlowValue(string raw)
        {
            var doc = UnityYamlText.Parse("--- !u!1 &1\nX:\n  v: " + raw + "\n");
            return doc[0].Body.Get("v");
        }

        /// <summary>The body a writer gives, as a YAML tree.</summary>
        internal static YText Body(Action<UnityYamlWriter.Writer> write, Dictionary<Object, long> ids, AssetDatabaseRuntime.Locations locations,
            Func<Object, string> references = null)
        {
            var sb = new StringBuilder("--- !u!1 &1\nX:\n");
            int start = sb.Length;
            write(new UnityYamlWriter.Writer(sb, ids, null, references, locations));
            if (sb.Length == start) return YText.Map();
            return UnityYamlText.Parse(sb.ToString())[0].Body;
        }

        static bool IsUnlocated(YText v) => v.Kind == YKind.Map && v.Flow && v.Raw == UnityYamlWriter.Writer.UnlocatedReference;

        static bool IsReference(YText v) => v != null && v.Kind == YKind.Map && v.Flow && v.Get("fileID") != null;

        /// <summary>A written value whose references to assets Unbound cannot name (built-in or
        /// imported objects it has no location of) are taken from the file's value at the same
        /// place, else written as none; <paramref name="unlocated"/> counts the latter.</summary>
        internal static YText Resolve(YText written, YText old, ref int unlocated)
        {
            if (IsUnlocated(written))
            {
                if (IsReference(old) && !IsUnlocated(old)) return old;
                unlocated++;
                return FlowValue("{fileID: 0}");
            }
            if (written.Kind == YKind.Seq)
                for (int i = 0; i < written.Items.Count; i++)
                    written.Items[i] = Resolve(written.Items[i], OldItem(written.Items[i], old, i), ref unlocated);
            else if (written.Kind == YKind.Map && !written.Flow)
                for (int i = 0; i < written.Keys.Count; i++)
                    written.Values[i] = Resolve(written.Values[i], old?.Kind == YKind.Map ? old.Get(written.Keys[i]) : null, ref unlocated);
            return written;
        }

        /// <summary>The file's item standing where a written one does: of a list of one-entry maps (a
        /// material's property lists) the one of the same key, else the one at the same index.</summary>
        static YText OldItem(YText item, YText old, int index)
        {
            if (old == null || old.Kind != YKind.Seq) return null;
            if (item.Kind == YKind.Map && !item.Flow && item.Keys.Count == 1)
            {
                foreach (var candidate in old.Items)
                    if (candidate.Kind == YKind.Map && candidate.Keys.Count == 1 && candidate.Keys[0] == item.Keys[0]) return candidate;
                return null;
            }
            return index < old.Items.Count ? old.Items[index] : null;
        }

        /// <summary>Puts <paramref name="written"/>'s entries into <paramref name="body"/>: a block map
        /// both have is merged, any other value replaced; a key the body lacks goes after the
        /// previous written key.</summary>
        internal static void Overlay(YText body, YText written)
        {
            int unlocated = 0;
            Overlay(body, written, ref unlocated);
        }

        internal static void Overlay(YText body, YText written, ref int unlocated)
        {
            int insertAt = 0;
            for (int i = 0; i < written.Keys.Count; i++)
            {
                var key = written.Keys[i];
                var value = written.Values[i];
                int at = body.Keys.IndexOf(key);
                if (at < 0)
                {
                    body.Keys.Insert(insertAt, key);
                    body.Values.Insert(insertAt, Resolve(value, null, ref unlocated));
                    insertAt++;
                    continue;
                }
                var old = body.Values[at];
                if (old.Kind == YKind.Map && !old.Flow && value.Kind == YKind.Map && !value.Flow && value.Keys.Count > 0) Overlay(old, value, ref unlocated);
                else body.Values[at] = Resolve(value, old, ref unlocated);
                insertAt = at + 1;
            }
        }

        /// <summary>Whether the class itself declares its writer (a derived class reads fields its
        /// base's writer does not write).</summary>
        internal static bool Writable(Type type) => ImportedPrefabDocuments.Writable(type);

        /// <summary>The fields of a GameObject's document after its header.</summary>
        internal static void WriteGameObject(UnityYamlWriter.Writer w, GameObject go, IEnumerable<long> components)
        {
            w.Int("serializedVersion", 6);
            w.Key("m_Component", 1);
            int count = 0;
            foreach (var id in components)
            {
                w.Item("component", "{fileID: " + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", 1);
                count++;
            }
            if (count == 0) w.Line(1, "m_Component", "[]");
            w.Int("m_Layer", go.m_Layer);
            w.String("m_Name", go.m_Name);
            w.String("m_TagString", go.m_Tag);
            w.Line(1, "m_Icon", "{fileID: 0}");
            w.Int("m_NavMeshLayer", go.m_NavMeshLayer);
            w.Int("m_StaticEditorFlags", go.m_StaticEditorFlags);
            w.Bool("m_IsActive", go.m_IsActive);
        }

        /// <summary>The enabled state a component's document stores (null: it has none).</summary>
        static bool? Enabled(Component c) => c switch
        {
            Behaviour b => b.m_Enabled,
            Renderer r => r.m_Enabled,
            Collider col => col.m_Enabled,
            _ => null,
        };

        sealed class SceneWriter
        {
            readonly SceneData m_Scene;
            SortedDictionary<long, YTextDocument> m_Out = new SortedDictionary<long, YTextDocument>();
            readonly Dictionary<long, YTextDocument> m_Source = new Dictionary<long, YTextDocument>();
            /// <summary>The file ids of every object written (Unbound's and the file's).</summary>
            readonly Dictionary<Object, long> m_Ids = new Dictionary<Object, long>(ReferenceEqualityComparer.Instance);
            readonly HashSet<long> m_Used = new HashSet<long>();
            /// <summary>The file ids the scene's objects had when they were read or last saved.</summary>
            readonly HashSet<long> m_Known = new HashSet<long>();
            readonly Dictionary<long, Object> m_KnownObjects = new Dictionary<long, Object>();
            /// <summary>The documents of embedded objects written this pass.</summary>
            readonly HashSet<long> m_Embedded = new HashSet<long>();
            readonly Dictionary<long, string> m_KeptEmbeddedClasses = new Dictionary<long, string>();
            readonly HashSet<long> m_LiveGameObjects = new HashSet<long>();
            readonly Dictionary<PrefabInstanceData, InstanceState> m_Instances = new Dictionary<PrefabInstanceData, InstanceState>();
            readonly SortedSet<string> m_KeptAsRead = new SortedSet<string>(StringComparer.Ordinal);
            readonly SortedSet<string> m_Unwritten = new SortedSet<string>(StringComparer.Ordinal);
            readonly AssetDatabaseRuntime.Locations m_Locations = new AssetDatabaseRuntime.Locations();
            int m_UnlocatedCount;
            /// <summary>Ids for the first pass of objects whose ids come after it.</summary>
            long m_Provisional;

            sealed class InstanceState
            {
                internal YTextDocument Document;
                internal readonly List<(long Target, string Path, YText Value, YText Reference)> Changes = new List<(long, string, YText, YText)>();
                internal readonly List<(long SourceGameObject, long Added)> AddedComponents = new List<(long, long)>();
            }

            internal SceneWriter(SceneData scene)
            {
                m_Scene = scene;
            }

            internal string Write()
            {
                ReadSource();
                foreach (var pair in m_Scene.FileIds)
                {
                    if (pair.Key is null) continue;
                    m_Known.Add(pair.Value);
                    m_KnownObjects[pair.Value] = pair.Key;
                    // An object no longer saved (DontSave) is referenced as none, as a new one is; an
                    // object outside the hierarchy (an embedded Mesh) is the file's only while the
                    // file holds its document (a save that dropped it, nothing referencing it, made
                    // it one made by script again).
                    bool ofFile = pair.Key is GameObject || pair.Key is Component || m_Source.ContainsKey(pair.Value);
                    if (pair.Key != null && Saved(pair.Key) && ofFile) { m_Ids[pair.Key] = pair.Value; m_Used.Add(pair.Value); }
                }
                foreach (var id in m_Source.Keys) m_Used.Add(id);
                foreach (var id in m_Scene.StrippedIds.Values) m_Used.Add(id);
                m_Used.Add(SceneRootsFileId);
                for (long id = 1; id <= 4; id++) m_Used.Add(id);

                var roots = new List<GameObject>();
                foreach (var root in m_Scene.Roots)
                    if (root != null && Saved(root)) roots.Add(root);
                // File ids first: references between objects need them all. The objects of prefab
                // instances that the written documents reference are known once they are written:
                // a first pass finds them, their stand-ins get ids, the second pass writes.
                foreach (var root in roots) AssignIds(root.transform);
                m_Recording = true;
                Pass(roots);
                m_Recording = false;
                foreach (var root in roots) InstanceIds(root.transform);
                Pass(roots);
                if (m_Unwritten.Count > 0)
                {
                    Debug.LogError($"Unbound: {DisplayPath} is not saved: Unbound cannot write {string.Join(", ", m_Unwritten)} yet.");
                    return null;
                }
                if (m_UnlocatedCount > 0)
                    Debug.LogWarning($"Unbound: {DisplayPath} is saved with {m_UnlocatedCount} references to assets Unbound cannot name (built-in or imported objects it has no location of) written as none.");
                if (m_KeptAsRead.Count > 0)
                    Debug.LogWarning($"Unbound: {DisplayPath} is saved with its {string.Join(", ", m_KeptAsRead)} objects as they were read: Unbound does not write their fields yet, so changes to them are not saved.");
                // The ids the objects have now are theirs from now on (the Editor's saved copy and
                // later saves agree, tests/projects/scenesave BaseCopy).
                foreach (var pair in m_Ids)
                    if (pair.Key != null) m_Scene.FileIds[pair.Key] = pair.Value;
                var docs = new List<YTextDocument>(m_Out.Values);
                return UnityYamlText.Write(docs);
            }

            /// <summary>Writes every document once (the state of a pass starts empty).</summary>
            void Pass(List<GameObject> roots)
            {
                m_Out = new SortedDictionary<long, YTextDocument>();
                m_LiveGameObjects.Clear();
                m_Instances.Clear();
                m_KeptAsRead.Clear();
                m_Unwritten.Clear();
                m_Embedded.Clear();
                m_KeptEmbeddedClasses.Clear();
                m_UnlocatedCount = 0;
                foreach (var root in roots) WriteHierarchy(root.transform);
                Settings();
                Instances();
                KeepUnclaimed();
                SceneRoots(roots);
                StrippedForReferences();
                PruneUnreferenced();
            }

            /// <summary>Drops the stripped documents nothing written references (as an instance's
            /// stand-in for an added component goes once the component is applied to the prefab,
            /// tests/projects/scenesave BaseApplied), and so the embedded objects (unmeasured: the
            /// Editor saves what the scene's objects reference); one referenced by another kept one
            /// stays.</summary>
            void PruneUnreferenced()
            {
                while (true)
                {
                    var referenced = new HashSet<long>();
                    void Walk(YText v)
                    {
                        switch (v.Kind)
                        {
                            case YKind.Map when v.Flow:
                                if (v.Get("guid") == null) referenced.Add(ParseFileId(v));
                                break;
                            case YKind.Map:
                                foreach (var child in v.Values) Walk(child);
                                break;
                            case YKind.Seq:
                                foreach (var child in v.Items) Walk(child);
                                break;
                        }
                    }
                    foreach (var d in m_Out.Values) Walk(d.Body);
                    var unused = new List<long>();
                    foreach (var d in m_Out.Values)
                        if ((d.Stripped() || m_Embedded.Contains(d.FileId)) && !referenced.Contains(d.FileId)) unused.Add(d.FileId);
                    if (unused.Count == 0) break;
                    foreach (var id in unused)
                    {
                        m_Out.Remove(id);
                    }
                }
                foreach (var pair in m_KeptEmbeddedClasses)
                    if (m_Out.ContainsKey(pair.Key)) m_KeptAsRead.Add(pair.Value);
            }

            /// <summary>While the first pass runs: the objects of prefab instances written
            /// documents reference.</summary>
            bool m_Recording;
            readonly HashSet<Object> m_Referenced = new HashSet<Object>(ReferenceEqualityComparer.Instance);

            string Record(Object o)
            {
                if (m_Recording && Link(o) != null) m_Referenced.Add(o);
                // An object made by script (a Mesh, a Material) that a scene object references is
                // embedded in the scene by the Editor; Unbound does not embed new objects yet.
                // Objects marked DontSave are written as none, as the Editor writes them.
                if (!o.IsPersistentAsset && !m_Ids.ContainsKey(o) && o is not GameObject && o is not Component && Saved(o))
                    m_Unwritten.Add($"{o.GetType().Name} objects made by script that the scene references (the Editor embeds them in the scene)");
                return null;
            }

            /// <summary>A written body, references noted while recording.</summary>
            YText Written(Action<UnityYamlWriter.Writer> write) => Body(write, m_Ids, m_Locations, Record);

            string DisplayPath => m_Scene.Path.Length > 0 ? m_Scene.Path : "the untitled scene";

            void ReadSource()
            {
                if (m_Scene.Path.Length == 0) return;
                var file = Path.Combine(ProjectInfo.ProjectPath, m_Scene.Path);
                if (!File.Exists(file)) return;
                foreach (var d in UnityYamlText.Parse(File.ReadAllText(file)))
                    m_Source[d.FileId] = d;
            }

            // ------------------------------------------------------------ ids

            static readonly System.Random s_Random = new System.Random();

            /// <summary>A new GameObject's file id and the ids after it for its components (the
            /// Editor's are random below 2^31).</summary>
            long NewIds(int count)
            {
                while (true)
                {
                    long first = s_Random.NextInt64(1, int.MaxValue - count - 1);
                    bool free = true;
                    for (int i = 0; i <= count && free; i++) free = !m_Used.Contains(first + i);
                    if (!free) continue;
                    for (int i = 0; i <= count; i++) m_Used.Add(first + i);
                    return first;
                }
            }

            /// <summary>The first free file id after <paramref name="id"/>.</summary>
            long After(long id)
            {
                while (m_Used.Contains(++id)) { }
                m_Used.Add(id);
                return id;
            }

            /// <summary>The file's stripped document of the instance's object (0: none).</summary>
            long FileStripped(PrefabLink link)
            {
                foreach (var d in m_Source.Values)
                    if (d.Stripped()
                        && ParseFileId(d.Body.Get("m_PrefabInstance")) == link.Instance.FileId
                        && ParseFileId(d.Body.Get("m_CorrespondingSourceObject")) == link.SourceFileId)
                        return d.FileId;
                return 0;
            }

            static PrefabLink Link(Object o) => PrefabLinks.Of(o) is PrefabLink link && link.Instance != null ? link : null;

            void AssignIds(Transform t)
            {
                var go = t.gameObject;
                var link = Link(go);
                var components = LiveComponents(go);
                if (link == null)
                {
                    int fresh = 0;
                    foreach (var c in components) if (!m_Ids.ContainsKey(c)) fresh++;
                    if (!m_Ids.ContainsKey(go))
                    {
                        long first = NewIds(fresh);
                        m_Ids[go] = first;
                        foreach (var c in components) if (!m_Ids.ContainsKey(c)) m_Ids[c] = ++first;
                    }
                    else if (fresh > 0)
                    {
                        long first = NewIds(fresh - 1);
                        foreach (var c in components) if (!m_Ids.ContainsKey(c)) m_Ids[c] = first++;
                    }
                }
                else
                {
                    // A component added to an instance's GameObject is the scene's own object; a new
                    // one gets its id after the first pass (InstanceIds).
                    foreach (var c in components)
                        if (Link(c) == null && !m_Ids.ContainsKey(c)) m_Ids[c] = --m_Provisional;
                }
                for (int i = 0; i < t.childCount; i++)
                    if (Saved(t.GetChild(i).gameObject)) AssignIds(t.GetChild(i));
            }

            /// <summary>
            /// The ids of an instance's objects that need documents: a stripped stand-in for each one
            /// written documents reference, for the GameObject of a component added to it and for
            /// the GameObject of a referenced script; an added component's own. The file's stand-ins
            /// keep theirs; new ones take the ids after the PrefabInstance's, in hierarchy order (a
            /// GameObject, its components, then its children; measured for one instance:
            /// tests/projects/scenesave, its root and script referenced, a component added to a child).
            /// </summary>
            void InstanceIds(Transform t)
            {
                var go = t.gameObject;
                var link = Link(go);
                if (link != null)
                {
                    var components = LiveComponents(go);
                    bool needed = m_Referenced.Contains(go);
                    foreach (var c in components)
                        if (Link(c) == null || (c is MonoBehaviour && m_Referenced.Contains(c))) needed = true;
                    if (needed) Stand(go, link);
                    foreach (var c in components)
                    {
                        var componentLink = Link(c);
                        if (componentLink == null)
                        {
                            if (m_Ids[c] < 0) m_Ids[c] = After(link.Instance.FileId);
                        }
                        else if (m_Referenced.Contains(c)) Stand(c, componentLink);
                    }
                }
                for (int i = 0; i < t.childCount; i++)
                    if (Saved(t.GetChild(i).gameObject)) InstanceIds(t.GetChild(i));
            }

            /// <summary>The id of the stripped document standing for an instance's object.</summary>
            void Stand(Object o, PrefabLink link)
            {
                long id = FileStripped(link);
                if (id == 0 && !m_Scene.StrippedIds.TryGetValue(o, out id)) id = After(link.Instance.FileId);
                m_Scene.StrippedIds[o] = id;
                m_Used.Add(id);
                m_Ids[o] = id;
                m_Stands.Add(o);
            }

            /// <summary>The instance objects that have stripped documents this save.</summary>
            readonly HashSet<Object> m_Stands = new HashSet<Object>(ReferenceEqualityComparer.Instance);

            /// <summary>The GameObject's components that are saved (live, not HideFlags.DontSave).</summary>
            static List<Component> LiveComponents(GameObject go)
            {
                var list = new List<Component>(go.m_Components.Count);
                foreach (var c in go.m_Components) if (c != null && !c.m_Destroyed && Saved(c)) list.Add(c);
                return list;
            }

            // ------------------------------------------------------------ objects

            YTextDocument Document(Object o, long id, int classId, string className, YText body)
            {
                var doc = new YTextDocument
                {
                    Header = $"--- !u!{classId} &{id}",
                    ClassId = classId,
                    FileId = id,
                    ClassName = className,
                    Body = body,
                };
                m_Out[id] = doc;
                return doc;
            }

            /// <summary>The document a written object starts from: the file's (when it is the same
            /// object's) or a new object's of its class.</summary>
            YText Base(long id, int classId, string className)
            {
                if (m_Known.Contains(id) && m_Source.TryGetValue(id, out var source) && source.ClassId == classId && !source.Stripped())
                    return source.Body.Clone();
                return className == null ? null : Defaults.Body(className);
            }

            void WriteHierarchy(Transform t)
            {
                var go = t.gameObject;
                var link = Link(go);
                var components = LiveComponents(go);
                if (link == null)
                {
                    long goId = m_Ids[go];
                    m_LiveGameObjects.Add(goId);
                    var body = Base(goId, EngineTypes.GameObjectId, "GameObject") ?? throw new InvalidOperationException("no GameObject document");
                    Overlay(body, Written(w => WriteGameObject(w, go, ComponentIds(goId, components))), ref m_UnlocatedCount);
                    Document(go, goId, EngineTypes.GameObjectId, "GameObject", body);
                    foreach (var c in components) WriteComponent(c, go);
                }
                else
                {
                    var state = Instance(link.Instance);
                    if (state != null)
                    {
                        Modifications(state, go, link);
                        foreach (var c in components)
                        {
                            var componentLink = Link(c);
                            if (componentLink == null) AddedComponent(state, c, go, link);
                            else Modifications(state, c, componentLink);
                        }
                    }
                }
                for (int i = 0; i < t.childCount; i++)
                {
                    var child = t.GetChild(i);
                    if (!Saved(child.gameObject)) continue;
                    if (link != null && Link(child.gameObject) == null)
                        m_Unwritten.Add("GameObjects added to prefab instances");
                    WriteHierarchy(child);
                }
            }

            /// <summary>The GameObject's m_Component list: its components in order, with the file's
            /// entries for components Unbound did not load kept after the one before them.</summary>
            IEnumerable<long> ComponentIds(long goId, List<Component> components)
            {
                var ids = new List<long>(components.Count);
                foreach (var c in components) ids.Add(m_Ids[c]);
                if (!m_Known.Contains(goId) || !m_Source.TryGetValue(goId, out var source)) return ids;
                long previous = 0;
                foreach (var item in source.Body.Get("m_Component")?.Items ?? new List<YText>())
                {
                    long id = ParseFileId(item.Get("component"));
                    if (!m_Known.Contains(id) && m_Source.ContainsKey(id))
                    {
                        int at = previous == 0 ? 0 : ids.IndexOf(previous) + 1;
                        ids.Insert(Math.Max(0, at), id);
                    }
                    previous = id;
                }
                return ids;
            }

            void WriteComponent(Component c, GameObject go)
            {
                long id = m_Ids[c];
                var type = c.GetType();
                if (c is MonoBehaviour)
                {
                    var written = Resolve(Written(w => w.ScriptBody(c)), Base(id, MonoBehaviourId, null), ref m_UnlocatedCount);
                    Document(c, id, MonoBehaviourId, "MonoBehaviour", written);
                    return;
                }
                int classId = EngineTypes.EngineClassIdOf(type);
                var className = type.Name;
                var body = Base(id, classId, className);
                if (body == null)
                {
                    m_Unwritten.Add($"new {className} components");
                    return;
                }
                body.Set("m_GameObject", FlowValue("{fileID: " + m_Ids[go].ToString(System.Globalization.CultureInfo.InvariantCulture) + "}"));
                if (Enabled(c) is bool enabled && body.Get("m_Enabled") != null) body.Set("m_Enabled", Raw(enabled ? "1" : "0"));
                if (Writable(type)) Overlay(body, Written(c.WriteSerialized), ref m_UnlocatedCount);
                else if (HasFields(body))
                {
                    // A new one would be saved with the release's defaults whatever was set on it.
                    if (!m_Known.Contains(id))
                    {
                        m_Unwritten.Add($"new {className} components");
                        return;
                    }
                    m_KeptAsRead.Add(className);
                }
                Document(c, id, classId, className, body);
            }

            /// <summary>The document fields every component has (no state of its class).</summary>
            static readonly HashSet<string> s_ComponentHeader = new HashSet<string>(StringComparer.Ordinal)
            {
                "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject", "m_Enabled",
            };

            static bool HasFields(YText body)
            {
                foreach (var key in body.Keys) if (!s_ComponentHeader.Contains(key)) return true;
                return false;
            }

            // ------------------------------------------------------------ prefab instances

            InstanceState Instance(PrefabInstanceData data)
            {
                if (m_Instances.TryGetValue(data, out var state)) return state;
                state = null;
                if (data.FileId != 0 && m_Source.TryGetValue(data.FileId, out var doc) && doc.ClassId == EngineTypes.PrefabInstanceId)
                    state = new InstanceState { Document = Clone(doc) };
                else m_Unwritten.Add("new prefab instances");
                m_Instances[data] = state;
                return state;
            }

            static YTextDocument Clone(YTextDocument d) =>
                new YTextDocument { Header = d.Header, ClassId = d.ClassId, FileId = d.FileId, ClassName = d.ClassName, Body = d.Body.Clone() };

            /// <summary>Fields no modification holds: the hierarchy and the document's links.</summary>
            static readonly HashSet<string> s_Structural = new HashSet<string>(StringComparer.Ordinal)
            {
                "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject",
                "m_Component", "m_Children", "m_Father", "serializedVersion", "m_Script", "m_EditorHideFlags", "m_EditorClassIdentifier",
            };

            /// <summary>Records the written fields of an instance's object that differ from its
            /// source's as modifications (scripts only once their changes were recorded).</summary>
            void Modifications(InstanceState state, Object o, PrefabLink link)
            {
                bool script = o is MonoBehaviour;
                if (script && !PrefabRecords.IsRecorded(o)) return;
                if (o is Component c && !script && !Writable(c.GetType()) && Enabled(c) == null) return;
                var source = PrefabLinks.Source(o);
                if (source == null) return;
                var current = Fields(o);
                var original = Fields(source);
                var before = new Dictionary<string, YText>(StringComparer.Ordinal);
                foreach (var (path, value) in Flatten(original)) before[path] = value;
                foreach (var (path, value) in Flatten(current))
                {
                    if (before.TryGetValue(path, out var was) && Same(was, value)) continue;
                    state.Changes.Add((link.SourceFileId, path, value.Kind == YKind.Map ? null : value, value.Kind == YKind.Map ? value : null));
                }
            }

            static bool Same(YText a, YText b) =>
                a.Kind == b.Kind && (a.Kind == YKind.Scalar ? a.Raw == b.Raw : UnityYamlText.FlowText(a) == UnityYamlText.FlowText(b));

            /// <summary>The fields Unbound writes of an object, references to an instance's objects
            /// written as the source objects they stand for (so an instance and its source compare
            /// alike).</summary>
            YText Fields(Object o)
            {
                string Sourced(Object target)
                {
                    var l = Link(target);
                    if (l != null) return $"{{fileID: {l.SourceFileId}, guid: {l.SourceGuid}, type: 3}}";
                    return null;
                }
                switch (o)
                {
                    case GameObject go:
                        return Body(w => WriteGameObject(w, go, Array.Empty<long>()), m_Ids, m_Locations, Sourced);
                    case MonoBehaviour mb:
                        return Body(w => { w.Header(mb); w.Fields(mb, mb.GetType(), 1); }, m_Ids, m_Locations, Sourced);
                    case Component c:
                        return Body(w =>
                        {
                            if (Enabled(c) is bool e) w.Bool("m_Enabled", e);
                            if (Writable(c.GetType())) c.WriteSerialized(w);
                        }, m_Ids, m_Locations, Sourced);
                }
                return YText.Map();
            }

            /// <summary>The values of a body by property path (<c>m_LocalPosition.x</c>,
            /// <c>list.Array.size</c>, <c>list.Array.data[0]</c>); references stay whole maps.</summary>
            static List<(string, YText)> Flatten(YText body)
            {
                var list = new List<(string, YText)>();
                void Walk(string prefix, YText v)
                {
                    switch (v.Kind)
                    {
                        case YKind.Map when v.Flow && v.Get("fileID") != null:
                            list.Add((prefix, v));
                            return;
                        case YKind.Map:
                            for (int i = 0; i < v.Keys.Count; i++)
                            {
                                if (prefix.Length == 0 && s_Structural.Contains(v.Keys[i])) continue;
                                Walk(prefix.Length == 0 ? v.Keys[i] : prefix + "." + v.Keys[i], v.Values[i]);
                            }
                            return;
                        case YKind.Seq:
                            list.Add((prefix + ".Array.size", YText.Scalar(v.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))));
                            for (int i = 0; i < v.Items.Count; i++) Walk(prefix + ".Array.data[" + i + "]", v.Items[i]);
                            return;
                        default:
                            list.Add((prefix, v));
                            return;
                    }
                }
                Walk("", body);
                return list;
            }

            void AddedComponent(InstanceState state, Component c, GameObject go, PrefabLink goLink)
            {
                long id = m_Ids[c];
                state.AddedComponents.Add((goLink.SourceFileId, id));
                if (c is MonoBehaviour)
                {
                    var written = Resolve(Written(w => w.ScriptBody(c)), Base(id, MonoBehaviourId, null), ref m_UnlocatedCount);
                    written.Set("m_GameObject", FlowValue("{fileID: " + m_Ids[go] + "}"));
                    Document(c, id, MonoBehaviourId, "MonoBehaviour", written);
                    return;
                }
                m_Unwritten.Add($"{c.GetType().Name} components added to prefab instances");
            }

            /// <summary>The stripped document standing for an instance's object: the file's, else a
            /// new one (a script's names its script and the stand-in of its GameObject, as Unity's
            /// stripped MonoBehaviour documents do).</summary>
            void StandDocument(Object o)
            {
                long id = m_Ids[o];
                if (m_Out.ContainsKey(id)) return;
                if (m_Source.TryGetValue(id, out var file) && file.Stripped())
                {
                    m_Out[id] = Clone(file);
                    return;
                }
                var link = Link(o);
                int classId = o is GameObject ? EngineTypes.GameObjectId : o is MonoBehaviour ? MonoBehaviourId : EngineTypes.EngineClassIdOf(o.GetType());
                var body = YText.Map();
                body.Add("m_CorrespondingSourceObject", FlowValue($"{{fileID: {link.SourceFileId}, guid: {link.SourceGuid}, type: 3}}"));
                body.Add("m_PrefabInstance", FlowValue("{fileID: " + link.Instance.FileId + "}"));
                body.Add("m_PrefabAsset", FlowValue("{fileID: 0}"));
                if (o is MonoBehaviour mb)
                {
                    var go = mb.gameObject;
                    body.Add("m_GameObject", FlowValue("{fileID: " + (m_Stands.Contains(go) ? m_Ids[go] : 0) + "}"));
                    body.Add("m_Enabled", Raw("1"));
                    body.Add("m_EditorHideFlags", Raw("0"));
                    body.Add("m_Script", FlowValue(new UnityYamlWriter.Writer(new StringBuilder(), m_Ids, null, null, m_Locations).ReferenceText(MonoScript.FromType(mb.GetType()))));
                    body.Add("m_Name", Raw(""));
                    body.Add("m_EditorClassIdentifier", Raw(""));
                }
                var className = o is GameObject ? "GameObject" : o is MonoBehaviour ? "MonoBehaviour" : o.GetType().Name;
                m_Out[id] = new YTextDocument { Header = $"--- !u!{classId} &{id} stripped", ClassId = classId, FileId = id, ClassName = className, Body = body };
            }

            void Instances()
            {
                foreach (var state in m_Instances.Values)
                {
                    if (state == null) continue;
                    var modification = state.Document.Body.Get("m_Modification");
                    var list = modification?.Get("m_Modifications");
                    if (list == null) continue;
                    foreach (var change in state.Changes) SetModification(list, change);
                    var added = YText.Seq();
                    foreach (var (sourceGo, component) in state.AddedComponents)
                    {
                        var entry = YText.Map();
                        entry.Add("targetCorrespondingSourceObject", FlowValue($"{{fileID: {sourceGo}, guid: {SourceGuid(state)}, type: 3}}"));
                        entry.Add("insertIndex", Raw("-1"));
                        entry.Add("addedObject", FlowValue("{fileID: " + component + "}"));
                        added.Items.Add(entry);
                    }
                    var existing = modification.Get("m_AddedComponents");
                    if (existing != null && existing.Items.Count > 0 && state.AddedComponents.Count == 0)
                        m_Unwritten.Add("components removed from prefab instances");
                    if (state.AddedComponents.Count == 0) added.Flow = true;
                    modification.Set("m_AddedComponents", added);
                    m_Out[state.Document.FileId] = state.Document;
                }
            }

            static string SourceGuid(InstanceState state) => state.Document.Body.Get("m_SourcePrefab")?.Get("guid")?.Text ?? "";

            /// <summary>Sets a modification's value, else inserts it after the last one of the same or
            /// a smaller target file id (the Editor keeps them ordered by target).</summary>
            void SetModification(YText list, (long Target, string Path, YText Value, YText Reference) change)
            {
                int insertAt = list.Items.Count;
                for (int i = 0; i < list.Items.Count; i++)
                {
                    var m = list.Items[i];
                    long target = ParseFileId(m.Get("target"));
                    if (target == change.Target && m.Get("propertyPath")?.Text == change.Path)
                    {
                        Fill(m, change);
                        return;
                    }
                    if (target > change.Target && insertAt == list.Items.Count) insertAt = i;
                }
                var guid = list.Items.Count > 0 ? list.Items[0].Get("target")?.Get("guid")?.Text : null;
                var entry = YText.Map();
                entry.Add("target", FlowValue($"{{fileID: {change.Target}, guid: {guid}, type: 3}}"));
                entry.Add("propertyPath", Raw(UnityYamlWriter.Scalar(change.Path)));
                entry.Add("value", Raw(""));
                entry.Add("objectReference", FlowValue("{fileID: 0}"));
                Fill(entry, change);
                list.Items.Insert(insertAt, entry);
                list.Flow = false;
            }

            static void Fill(YText m, (long Target, string Path, YText Value, YText Reference) change)
            {
                if (change.Reference != null)
                {
                    m.Set("value", Raw(""));
                    m.Set("objectReference", change.Reference);
                }
                else
                {
                    m.Set("value", change.Value);
                    m.Set("objectReference", FlowValue("{fileID: 0}"));
                }
            }

            // ------------------------------------------------------------ the rest

            void Settings()
            {
                foreach (var classId in new[] { OcclusionCullingSettingsId, RenderSettingsId, LightmapSettingsId, NavMeshSettingsId })
                {
                    YTextDocument doc = null;
                    foreach (var d in m_Source.Values)
                        if (d.ClassId == classId) { doc = Clone(d); break; }
                    doc ??= Defaults.Settings(classId);
                    if (classId == RenderSettingsId && RenderSettings.InEffectFor(m_Scene))
                        Overlay(doc.Body, Written(RenderSettings.WriteSerialized), ref m_UnlocatedCount);
                    if (classId == LightmapSettingsId) LightingSettingsReference(doc.Body);
                    m_Out[doc.FileId] = doc;
                }
            }

            /// <summary>The scene's Lighting Settings as it has them now (Lightmapping.lightingSettings
            /// assigns them): written when they are an asset or the scene's own object, else the save
            /// fails rather than drop the change.</summary>
            void LightingSettingsReference(YText body)
            {
                var current = m_Scene.LightingSettings;
                // None now may be a reference the file names that did not load: kept.
                if (current == null) return;
                var written = FlowValue(current == null ? "{fileID: 0}" : new UnityYamlWriter.Writer(new StringBuilder(), m_Ids, null, null, m_Locations).ReferenceText(current));
                if (written.Raw == UnityYamlWriter.Writer.UnlocatedReference || ParseFileId(written) == 0)
                {
                    m_Unwritten.Add("Lighting Settings that are no asset of the project");
                    return;
                }
                body.Set("m_LightingSettings", written);
            }

            /// <summary>The file's documents Unbound made no object of: kept while their GameObject is.</summary>
            void KeepUnclaimed()
            {
                // Instances whose objects Unbound made none of (a missing prefab) stay as read.
                foreach (var d in m_Source.Values)
                    if (d.ClassId == EngineTypes.PrefabInstanceId && !m_Out.ContainsKey(d.FileId) && !InstanceGone(d.FileId))
                        m_Out[d.FileId] = Clone(d);
                foreach (var d in m_Source.Values)
                {
                    if (m_Out.ContainsKey(d.FileId)) continue;
                    if (d.Stripped())
                    {
                        // A prefab instance's stand-in (references and other instances' modifications
                        // name it): kept with its instance.
                        if (m_Out.TryGetValue(ParseFileId(d.Body.Get("m_PrefabInstance")), out var instance) && instance.ClassId == EngineTypes.PrefabInstanceId)
                            m_Out[d.FileId] = Clone(d);
                        continue;
                    }
                    if (m_KnownObjects.TryGetValue(d.FileId, out var known))
                    {
                        // An object of the file outside the hierarchy (an embedded Mesh, Material or
                        // Lighting Settings) is written while it lives: its fields when Unbound has a
                        // writer for its class, else as the file has it. One nothing written
                        // references goes (PruneUnreferenced).
                        if (known == null || known is GameObject || known is Component || !Saved(known)) continue;
                        var doc = Clone(d);
                        // One kept as read is named once it survives pruning: a scene save writes
                        // its objects as they are, dirty or not.
                        if (!NativeDocuments.Write(doc, known, m_Ids, m_Locations, ref m_UnlocatedCount, Record))
                            m_KeptEmbeddedClasses[d.FileId] = known.GetType().Name;
                        m_Out[d.FileId] = doc;
                        m_Embedded.Add(d.FileId);
                        continue;
                    }
                    if (d.ClassId == SceneRootsId || d.ClassId == EngineTypes.PrefabInstanceId || SceneSettings.IsSettingsClass(d.ClassId)) continue;
                    if (d.ClassId == EngineTypes.GameObjectId) continue;
                    var owner = d.Body.Get("m_GameObject");
                    if (owner != null && !m_LiveGameObjects.Contains(ParseFileId(owner)) && !IsStrippedKept(ParseFileId(owner))) continue;
                    m_Out[d.FileId] = Clone(d);
                }
            }

            /// <summary>The stand-ins of this save's instance objects; a reference to an object of an
            /// instance Unbound does not write fails the save.</summary>
            void StrippedForReferences()
            {
                foreach (var o in m_Stands)
                {
                    if (o == null) continue;
                    if (!m_Instances.TryGetValue(Link(o).Instance, out var state) || state == null)
                    {
                        m_Unwritten.Add("references to objects of prefab instances it does not write");
                        continue;
                    }
                    StandDocument(o);
                }
            }

            bool IsStrippedKept(long id) => m_Out.TryGetValue(id, out var d) && d.Stripped();

            /// <summary>Whether objects of the instance were loaded (so it was written if any of
            /// them still is): an instance of a missing prefab has none and stays as read.</summary>
            bool InstanceGone(long instanceId)
            {
                foreach (var pair in m_Scene.FileIds)
                    if (pair.Key is not null && Link(pair.Key) is PrefabLink link && link.Instance.FileId == instanceId) return true;
                return false;
            }

            void SceneRoots(List<GameObject> roots)
            {
                var body = m_Source.TryGetValue(SceneRootsFileId, out var source) ? source.Body.Clone() : null;
                if (body == null)
                {
                    body = YText.Map();
                    body.Add("m_ObjectHideFlags", Raw("0"));
                }
                var list = YText.Seq();
                foreach (var root in roots)
                {
                    var link = Link(root);
                    long id = link != null ? link.Instance.FileId : m_Ids[root.transform];
                    list.Items.Add(FlowValue("{fileID: " + id + "}"));
                }
                if (list.Items.Count == 0) list.Flow = true;
                body.Set("m_Roots", list);
                m_Out[SceneRootsFileId] = new YTextDocument
                {
                    Header = $"--- !u!{SceneRootsId} &{SceneRootsFileId}",
                    ClassId = SceneRootsId,
                    FileId = SceneRootsFileId,
                    ClassName = "SceneRoots",
                    Body = body,
                };
            }
        }

        /// <summary>Whether the object is saved with its scene (not HideFlags.DontSaveInEditor).</summary>
        static bool Saved(Object o) => (o.HideFlagsNoThrow & HideFlags.DontSaveInEditor) == 0;

        static bool Stripped(this YTextDocument d) => d.Header.EndsWith(" stripped", StringComparison.Ordinal);
    }

    /// <summary>Objects of prefab instances whose script changes were recorded as overrides
    /// (PrefabUtility.RecordPrefabInstancePropertyModifications).</summary>
    internal static class PrefabRecords
    {
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Object, object> s_Recorded =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Object, object>();

        internal static void Record(Object o)
        {
            if (o is null) return;
            s_Recorded.AddOrUpdate(o, null);
        }

        internal static bool IsRecorded(Object o) => o is not null && s_Recorded.TryGetValue(o, out _);
    }

    /// <summary>
    /// The .meta file of an asset Unbound writes for the first time, as the Editor's import makes
    /// it: a new random GUID and the importer's settings (measured for scenes:
    /// tests/projects/scenesave, Fresh.unity.meta). An asset that has one keeps it.
    /// </summary>
    internal static class NewAssetMeta
    {
        internal const string DefaultImporter = "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";

        internal static void Write(string assetPath, string importer)
        {
            var meta = Path.Combine(ProjectInfo.ProjectPath, assetPath + ".meta");
            if (File.Exists(meta)) return;
            File.WriteAllText(meta, "fileFormatVersion: 2\nguid: " + Guid.NewGuid().ToString("N") + "\n" + importer);
        }
    }

    /// <summary>The document of an object Unbound writes whole or in part outside a scene's
    /// hierarchy (an asset file's object, an object embedded in a scene).</summary>
    internal static class NativeDocuments
    {
        /// <summary>Writes the object's fields into its document: a script object's whole body, a
        /// material's modeled fields over the file's; false (the document kept as read) for other
        /// classes.</summary>
        /// <param name="references">Overrides how references are written (a scene's checks).</param>
        internal static bool Write(YTextDocument doc, Object o, Dictionary<Object, long> ids, AssetDatabaseRuntime.Locations locations, ref int unlocated,
            Func<Object, string> references = null)
        {
            if (SerializedTree.IsScript(o))
            {
                doc.Body = SceneFiles.Resolve(SceneFiles.Body(w => w.ScriptBody(o), ids, locations, references), doc.Body, ref unlocated);
                return true;
            }
            if (o is Material material)
            {
                var writer = new UnityYamlWriter.Writer(new StringBuilder(), ids, null, references, locations);
                SceneFiles.Overlay(doc.Body, MaterialTypeTree.FileBody(material, r => SceneFiles.FlowValue(writer.ReferenceText(r))), ref unlocated);
                return true;
            }
            return false;
        }
    }
}
