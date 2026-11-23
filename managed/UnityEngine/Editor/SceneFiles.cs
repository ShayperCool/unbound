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
    /// not saved); components and GameObjects added to an instance are listed in m_AddedComponents
    /// and m_AddedGameObjects (the file's entries kept, a new one after the prefab's objects) and
    /// written on stripped documents of their GameObject or parent Transform. In a prefab file new
    /// stand-ins and added components take random ids (prefab-nested).
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
        internal static string Write(SceneData scene) => new SceneWriter(WriteTarget.Of(scene)).Write();

        /// <summary>A prefab asset's file text (its hierarchy from <paramref name="target"/>'s root);
        /// null (with an error logged) when a part of it cannot be written.</summary>
        internal static string Write(WriteTarget target) => new SceneWriter(target).Write();

        /// <summary>What a hierarchy is written as: a scene's file, or a prefab asset's (one root).</summary>
        internal sealed class WriteTarget
        {
            /// <summary>The project path of the file the hierarchy was read from ("" for none).</summary>
            internal string Path;
            /// <summary>The file ids of the objects (those of the file, and those given by saves).</summary>
            internal Dictionary<Object, long> FileIds;
            internal Dictionary<Object, long> StrippedIds;
            internal List<GameObject> Roots;
            /// <summary>The scene written; null for a prefab.</summary>
            internal SceneData Scene;
            internal bool Prefab => Scene == null;

            internal static WriteTarget Of(SceneData scene) => new WriteTarget
            {
                Path = scene.Path, FileIds = scene.FileIds, StrippedIds = scene.StrippedIds, Roots = scene.Roots, Scene = scene,
            };
        }

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
                UnityEditor.AssetDatabase.ImportWritten(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"Unbound: {path} is not saved: {e.Message}");
                return false;
            }
            // A save takes the instances' Transform changes (scenesave modification-timing save.after).
            PrefabRecordTiming.SceneSaved();
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
        internal static bool? Enabled(Component c) => c switch
        {
            Behaviour b => b.m_Enabled,
            Renderer r => r.m_Enabled,
            Collider col => col.m_Enabled,
            _ => null,
        };

        sealed class SceneWriter
        {
            readonly WriteTarget m_Target;
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
                internal PrefabInstanceData Data;
                /// <summary>Its topmost GameObject.</summary>
                internal GameObject Root;
                /// <summary>Its modification list is written from the stored modifications.</summary>
                internal bool Rebuild;
                internal readonly List<(long Target, string Path, YText Value, YText Reference)> Changes = new List<(long, string, YText, YText)>();
                /// <summary>Components added to the instance's GameObjects: the source GameObject, the
                /// component's id and whether it comes after every component of the prefab's.</summary>
                internal readonly List<(long SourceGameObject, long Added, bool Last)> AddedComponents = new List<(long, long, bool)>();
                /// <summary>GameObjects added under the instance's objects: the source Transform of
                /// their parent, their Transform's id and whether they come after every child of the
                /// prefab's.</summary>
                internal readonly List<(long SourceTransform, long Added, bool Last)> AddedGameObjects = new List<(long, long, bool)>();
            }

            internal SceneWriter(WriteTarget target)
            {
                m_Target = target;
            }

            internal string Write()
            {
                ReadSource();
                foreach (var pair in m_Target.FileIds)
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
                foreach (var id in m_Target.StrippedIds.Values) m_Used.Add(id);
                if (!m_Target.Prefab)
                {
                    m_Used.Add(SceneRootsFileId);
                    for (long id = 1; id <= 4; id++) m_Used.Add(id);
                }

                var roots = new List<GameObject>();
                foreach (var root in m_Target.Roots)
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
                    if (pair.Key != null) m_Target.FileIds[pair.Key] = pair.Value;
                foreach (var pair in m_NewInstances) pair.Key.FileId = pair.Value;
                var docs = m_Target.Prefab ? PrefabOrder(m_Out.Values) : new List<YTextDocument>(m_Out.Values);
                return UnityYamlText.Write(docs);
            }

            /// <summary>A prefab's documents in the Editor's order: its GameObjects by file id, each
            /// followed by its components in its component order, then its PrefabInstance documents
            /// by file id, then the rest by file id, a stripped GameObject followed by the documents
            /// on it (tests/projects/scenesave, prefab-nested Outer.prefab, RichVariant.prefab).</summary>
            static List<YTextDocument> PrefabOrder(IEnumerable<YTextDocument> written)
            {
                var byId = new SortedDictionary<long, YTextDocument>();
                foreach (var d in written) byId[d.FileId] = d;
                var ordered = new List<YTextDocument>(byId.Count);
                var placed = new HashSet<long>();
                foreach (var d in byId.Values)
                {
                    if (d.ClassId != EngineTypes.GameObjectId || d.Stripped()) continue;
                    ordered.Add(d);
                    placed.Add(d.FileId);
                    foreach (var item in d.Body.Get("m_Component")?.Items ?? new List<YText>())
                    {
                        long id = ParseFileId(item.Get("component"));
                        if (byId.TryGetValue(id, out var c) && placed.Add(id)) ordered.Add(c);
                    }
                }
                foreach (var d in byId.Values)
                    if (d.ClassId == EngineTypes.PrefabInstanceId && placed.Add(d.FileId)) ordered.Add(d);
                var onStripped = new Dictionary<long, List<YTextDocument>>();
                foreach (var d in byId.Values)
                {
                    if (placed.Contains(d.FileId)) continue;
                    long owner = ParseFileId(d.Body.Get("m_GameObject"));
                    if (owner != 0 && byId.TryGetValue(owner, out var go) && go.ClassId == EngineTypes.GameObjectId && go.Stripped())
                    {
                        if (!onStripped.TryGetValue(owner, out var list)) onStripped[owner] = list = new List<YTextDocument>();
                        list.Add(d);
                        placed.Add(d.FileId);
                    }
                }
                foreach (var d in byId.Values)
                {
                    if (!placed.Add(d.FileId)) continue;
                    ordered.Add(d);
                    if (onStripped.TryGetValue(d.FileId, out var on)) ordered.AddRange(on);
                }
                return ordered;
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
                if (!m_Target.Prefab) Settings();
                Instances();
                KeepUnclaimed();
                if (!m_Target.Prefab) SceneRoots(roots);
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

            string DisplayPath => m_Target.Path.Length > 0 ? m_Target.Path : "the untitled scene";

            void ReadSource()
            {
                if (m_Target.Path.Length == 0) return;
                var file = Path.Combine(ProjectInfo.ProjectPath, m_Target.Path);
                if (!File.Exists(file)) return;
                foreach (var d in UnityYamlText.Parse(File.ReadAllText(file)))
                    m_Source[d.FileId] = d;
            }

            // ------------------------------------------------------------ ids

            static readonly System.Random s_Random = new System.Random();

            /// <summary>A new GameObject's file id and the ids after it for its components (the
            /// Editor's are random below 2^31 in a scene; in a prefab each object's is random,
            /// tests/projects/scenesave Made.prefab).</summary>
            long NewIds(int count)
            {
                if (m_Target.Prefab)
                {
                    if (count != 0) throw new InvalidOperationException("a prefab's new objects get one id each");
                    while (true)
                    {
                        long id = s_Random.NextInt64(1, long.MaxValue);
                        if (m_Used.Add(id)) return id;
                    }
                }
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
                        && ParseFileId(d.Body.Get("m_PrefabInstance")) == InstanceFileId(link.Instance)
                        && ParseFileId(d.Body.Get("m_CorrespondingSourceObject")) == link.SourceFileId)
                        return d.FileId;
                return 0;
            }

            static PrefabLink Link(Object o) => PrefabLinks.Of(o) is PrefabLink link && link.Instance != null ? link : null;

            /// <summary>The instances that get their first PrefabInstance document this save, with its id.</summary>
            readonly Dictionary<PrefabInstanceData, long> m_NewInstances = new Dictionary<PrefabInstanceData, long>();

            long InstanceFileId(PrefabInstanceData data) =>
                data.FileId != 0 ? data.FileId : m_NewInstances.TryGetValue(data, out var id) ? id : 0;

            void AssignIds(Transform t)
            {
                var go = t.gameObject;
                var link = Link(go);
                var components = LiveComponents(go);
                if (link == null && m_Target.Prefab)
                {
                    if (!m_Ids.ContainsKey(go)) m_Ids[go] = NewIds(0);
                    foreach (var c in components) if (!m_Ids.ContainsKey(c)) m_Ids[c] = NewIds(0);
                }
                else if (link == null)
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
                    // A new instance gets its PrefabInstance's file id now (its stand-ins' ids follow it).
                    if (InstanceFileId(link.Instance) == 0) m_NewInstances[link.Instance] = NewIds(0);
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
                            if (m_Ids[c] < 0) m_Ids[c] = InstanceObjectId(link);
                        }
                        else if (m_Referenced.Contains(c)) Stand(c, componentLink);
                    }
                    // A GameObject added under the object hangs from the stand-in of its Transform.
                    for (int i = 0; i < t.childCount; i++)
                        if (Saved(t.GetChild(i).gameObject) && Link(t.GetChild(i).gameObject) == null && Link(t) is PrefabLink transformLink)
                        {
                            Stand(t, transformLink);
                            break;
                        }
                }
                for (int i = 0; i < t.childCount; i++)
                    if (Saved(t.GetChild(i).gameObject)) InstanceIds(t.GetChild(i));
            }

            /// <summary>The id of the stripped document standing for an instance's object.</summary>
            void Stand(Object o, PrefabLink link)
            {
                long id = FileStripped(link);
                if (id == 0 && !m_Target.StrippedIds.TryGetValue(o, out id)) id = InstanceObjectId(link);
                m_Target.StrippedIds[o] = id;
                m_Used.Add(id);
                m_Ids[o] = id;
                m_Stands.Add(o);
            }

            /// <summary>A new id for an instance's stand-in or added component: in a scene the ids after
            /// the PrefabInstance's (tests/projects/scenesave Base), in a prefab a random one each
            /// (prefab-nested Outer.prefab).</summary>
            long InstanceObjectId(PrefabLink link) => m_Target.Prefab ? NewIds(0) : After(InstanceFileId(link.Instance));

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
                    var state = Instance(link.Instance, go);
                    if (state != null)
                    {
                        Modifications(state, go, link);
                        for (int i = 0; i < components.Count; i++)
                        {
                            var c = components[i];
                            var componentLink = Link(c);
                            if (componentLink == null)
                            {
                                bool last = true;
                                for (int j = i + 1; j < components.Count; j++)
                                    if (Link(components[j]) != null) last = false;
                                AddedComponent(state, c, go, link, last);
                            }
                            else Modifications(state, c, componentLink);
                        }
                    }
                }
                for (int i = 0; i < t.childCount; i++)
                {
                    var child = t.GetChild(i);
                    if (!Saved(child.gameObject)) continue;
                    // A GameObject added under an instance's object: listed by the instance under its
                    // parent's source Transform (matches the Unity 6000.0.71f1 Editor,
                    // tests/projects/scenesave prefab-nested Outer.prefab, RichVariant.prefab).
                    if (link != null && Link(child.gameObject) == null && Link(t) is PrefabLink transformLink && Instance(link.Instance, go) is InstanceState owner)
                    {
                        bool last = true;
                        for (int j = i + 1; j < t.childCount; j++)
                            if (Link(t.GetChild(j).gameObject)?.Instance == link.Instance) last = false;
                        owner.AddedGameObjects.Add((transformLink.SourceFileId, m_Ids[child], last));
                    }
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
                // An instance's GameObject gets its stand-in's id after the first pass.
                long goId = m_Ids.TryGetValue(go, out var known) ? known : 0;
                body.Set("m_GameObject", FlowValue("{fileID: " + goId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}"));
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

            InstanceState Instance(PrefabInstanceData data, GameObject root)
            {
                if (m_Instances.TryGetValue(data, out var state)) return state;
                long id = InstanceFileId(data);
                if (id != 0 && m_Source.TryGetValue(id, out var doc) && doc.ClassId == EngineTypes.PrefabInstanceId)
                    state = new InstanceState { Document = Clone(doc), Rebuild = data.Edited };
                else
                    state = new InstanceState { Document = NewInstanceDocument(data, id, root), Rebuild = true };
                state.Data = data;
                state.Root = root;
                m_Instances[data] = state;
                return state;
            }

            /// <summary>A new instance's PrefabInstance document, as the Editor writes one
            /// (tests/projects/scenesave ApiConverted, WritesFinal): its parent, no removed or added
            /// objects, its prefab.</summary>
            YTextDocument NewInstanceDocument(PrefabInstanceData data, long id, GameObject root)
            {
                var parent = root.transform.parent;
                var modification = YText.Map();
                modification.Add("serializedVersion", Raw("3"));
                modification.Add("m_TransformParent", FlowValue("{fileID: " + (parent != null && m_Ids.TryGetValue(parent, out var parentId) ? parentId : 0) + "}"));
                modification.Add("m_Modifications", YText.Seq(flow: true));
                foreach (var key in new[] { "m_RemovedComponents", "m_RemovedGameObjects", "m_AddedGameObjects", "m_AddedComponents" })
                    modification.Add(key, YText.Seq(flow: true));
                var body = YText.Map();
                body.Add("m_ObjectHideFlags", Raw("0"));
                body.Add("serializedVersion", Raw("2"));
                body.Add("m_Modification", modification);
                body.Add("m_SourcePrefab", FlowValue($"{{fileID: 100100000, guid: {data.SourceGuid}, type: 3}}"));
                return new YTextDocument
                {
                    Header = $"--- !u!{EngineTypes.PrefabInstanceId} &{id}", ClassId = EngineTypes.PrefabInstanceId, FileId = id,
                    ClassName = "PrefabInstance", Body = body,
                };
            }

            /// <summary>The modification list of an instance whose stored modifications changed (or a
            /// new one): the stored ones in their order, each as the file had it when it did.</summary>
            void RebuildModifications(InstanceState state, YText list)
            {
                var data = state.Data;
                var file = new Dictionary<(long, string), YText>();
                foreach (var m in list.Items)
                    file.TryAdd((ParseFileId(m.Get("target")), m.Get("propertyPath")?.Text ?? ""), m);
                list.Items.Clear();
                foreach (var stored in data.Stored)
                {
                    if (file.TryGetValue((stored.Target.FileId, stored.Path), out var kept))
                    {
                        list.Items.Add(kept.Clone());
                        continue;
                    }
                    var entry = YText.Map();
                    entry.Add("target", FlowValue(UnityYamlWriter.Writer.ReferenceText(stored.Target.Guid == null
                        ? new ObjectRef(stored.Target.FileId, data.SourceGuid, 3) : stored.Target)));
                    entry.Add("propertyPath", Raw(UnityYamlWriter.Scalar(stored.Path)));
                    entry.Add("value", Raw(UnityYamlWriter.Scalar(stored.Value ?? "")));
                    YText reference;
                    if (data.ReferenceObjects.TryGetValue((stored.Target.FileId, stored.Path), out var referenced))
                        reference = FlowValue(new UnityYamlWriter.Writer(new StringBuilder(), m_Ids, null, Record, m_Locations).ReferenceText(referenced));
                    else reference = FlowValue(UnityYamlWriter.Writer.ReferenceText(stored.Reference));
                    if (IsUnlocated(reference)) reference = FlowValue("{fileID: 0}");
                    entry.Add("objectReference", reference);
                    list.Items.Add(entry);
                }
                list.Flow = list.Items.Count == 0;
            }

            static YTextDocument Clone(YTextDocument d) =>
                new YTextDocument { Header = d.Header, ClassId = d.ClassId, FileId = d.FileId, ClassName = d.ClassName, Body = d.Body.Clone() };

            /// <summary>Records the written fields of an instance's object that differ from its
            /// source's as modifications (scripts only once their changes were recorded).</summary>
            void Modifications(InstanceState state, Object o, PrefabLink link)
            {
                bool script = o is MonoBehaviour;
                // A script's values count as the queries count them (PrefabInstances.Considered).
                if (script && !PrefabInstances.Considered(o)) return;
                if (o is Component c && !script && !Writable(c.GetType()) && Enabled(c) == null) return;
                var source = PrefabLinks.Source(o);
                if (source == null) return;
                var current = Fields(o);
                var original = Fields(source);
                var before = new Dictionary<string, YText>(StringComparer.Ordinal);
                foreach (var (path, value) in PrefabValues.Flatten(original)) before[path] = value;
                // A stored modification takes the value the object has now, also one back at its
                // source's (measured: tests/projects/scenesave prefab-writes sceneSave, the root's
                // position after RevertPrefabInstance).
                var stored = new HashSet<string>(StringComparer.Ordinal);
                foreach (var m in state.Data.Stored)
                    if (!m.Target.IsNull && m.Target.FileId == link.SourceFileId) stored.Add(m.Path);
                foreach (var (path, value) in PrefabValues.Flatten(current))
                {
                    if (!stored.Contains(path) && before.TryGetValue(path, out var was) && PrefabValues.Same(was, value)) continue;
                    state.Changes.Add((link.SourceFileId, path, value.Kind == YKind.Map ? null : value, value.Kind == YKind.Map ? value : null));
                }
            }

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
                return PrefabValues.Fields(o, Sourced, m_Ids, m_Locations);
            }

            void AddedComponent(InstanceState state, Component c, GameObject go, PrefabLink goLink, bool last)
            {
                state.AddedComponents.Add((goLink.SourceFileId, m_Ids[c], last));
                // Written as any component, on the stand-in of its GameObject (engine components:
                // prefab-nested Outer.prefab, RichVariant.prefab).
                WriteComponent(c, go);
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
                body.Add("m_PrefabInstance", FlowValue("{fileID: " + InstanceFileId(link.Instance) + "}"));
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
                    if (state.Rebuild) RebuildModifications(state, list);
                    foreach (var change in state.Changes) SetModification(list, change, state.Data.SourceGuid);
                    modification.Set("m_AddedComponents", AddedList(state, modification.Get("m_AddedComponents"), state.AddedComponents, "components", false));
                    modification.Set("m_AddedGameObjects", AddedList(state, modification.Get("m_AddedGameObjects"), state.AddedGameObjects, "GameObjects", true));
                    RemovedComponents(state, modification);
                    m_Out[state.Document.FileId] = state.Document;
                }
            }

            /// <summary>The source components the instance's GameObjects lack: the file's entries
            /// for those still missing, then each newly removed one (unmeasured: no measured save
            /// has one), as references to the prefab's components.</summary>
            void RemovedComponents(InstanceState state, YText modification)
            {
                var data = state.Data;
                var removed = new List<(long FileId, string Guid)>();
                foreach (var o in PrefabInstances.ObjectsOf(data, state.Root))
                {
                    if (o is not GameObject go || PrefabLinks.Source(go) is not GameObject sourceGo) continue;
                    foreach (var sc in sourceGo.m_Components)
                        if (sc != null && !sc.m_Destroyed && PrefabInstances.Counterpart(go, sc, data) == null
                            && m_Locations.TryGet(sc, out var guid, out var fileId, out _))
                            removed.Add((fileId, guid));
                }
                var list = YText.Seq();
                var listed = new HashSet<long>();
                foreach (var item in modification.Get("m_RemovedComponents")?.Items ?? new List<YText>())
                {
                    long id = ParseFileId(item);
                    // An entry of a component Unbound does not load stays; a loaded one while it is removed.
                    bool loaded = AssetDatabaseRuntime.Load(item.Get("guid")?.Text ?? data.SourceGuid, id, typeof(Component)) != null;
                    if (loaded && !removed.Exists(r => r.FileId == id)) continue;
                    list.Items.Add(item.Clone());
                    listed.Add(id);
                }
                foreach (var (fileId, guid) in removed)
                    if (listed.Add(fileId)) list.Items.Add(FlowValue($"{{fileID: {fileId}, guid: {guid}, type: 3}}"));
                list.Flow = list.Items.Count == 0;
                modification.Set("m_RemovedComponents", list);
            }

            /// <summary>
            /// An instance's list of added components or GameObjects: the file's entries in their order,
            /// then one for each newly added object. An entry stays as written for an object Unbound did
            /// not load, for an object still added with the same target (when listed after the
            /// prefab's objects, insertIndex -1, it must still come after them) and, for GameObjects,
            /// for another instance's object still saved (an instance placed under this one's
            /// object, while it is still there); one for an object applied to the prefab or gone is
            /// dropped (tests/projects/scenesave BaseApplied). A new object is listed after the
            /// prefab's objects (insertIndex -1, measured: prefab-nested, Base). The entries must give
            /// the order the objects have now when placed as a load places them (each at its
            /// insertIndex, else after the others, in the list's order); an added object moved to
            /// another target, among the prefab's objects or out of that order, and another instance
            /// moved away, are not measured and fail the save.
            /// </summary>
            YText AddedList(InstanceState state, YText fileList, List<(long Target, long Added, bool Last)> current, string what, bool gameObjects)
            {
                var list = YText.Seq();
                var now = new Dictionary<long, (long Target, bool Last)>();
                foreach (var c in current) now[c.Added] = (c.Target, c.Last);
                var listed = new HashSet<long>();
                foreach (var item in fileList?.Items ?? new List<YText>())
                {
                    long id = ParseFileId(item.Get("addedObject"));
                    if (now.TryGetValue(id, out var added))
                    {
                        bool moved = ParseFileId(item.Get("targetCorrespondingSourceObject")) != added.Target
                            || (item.Get("insertIndex")?.Text == "-1" && !added.Last);
                        if (moved) m_Unwritten.Add($"{what} added to a prefab instance and moved (their new place is not measured)");
                        list.Items.Add(item.Clone());
                        listed.Add(id);
                    }
                    else if (!m_Known.Contains(id))
                    {
                        if (m_Source.ContainsKey(id)) list.Items.Add(item.Clone());
                    }
                    else if (gameObjects && m_KnownObjects.TryGetValue(id, out var o) && o != null && Saved(o) && Link(o) is PrefabLink other && other.Instance != state.Data)
                    {
                        // Kept only while that instance is still under this one's object the entry names.
                        var parent = (o as Transform ?? (o as GameObject)?.transform)?.parent;
                        if (parent == null || Link(parent) is not PrefabLink at || at.Instance != state.Data
                            || at.SourceFileId != ParseFileId(item.Get("targetCorrespondingSourceObject")))
                            m_Unwritten.Add("an instance placed under a prefab instance's object and moved (its new place is not measured)");
                        list.Items.Add(item.Clone());
                    }
                }
                foreach (var c in current)
                {
                    if (listed.Contains(c.Added)) continue;
                    if (!c.Last) m_Unwritten.Add($"{what} added among a prefab instance's own (their place is not measured)");
                    var entry = YText.Map();
                    entry.Add("targetCorrespondingSourceObject", FlowValue($"{{fileID: {c.Target}, guid: {SourceGuid(state)}, type: 3}}"));
                    entry.Add("insertIndex", Raw("-1"));
                    entry.Add("addedObject", FlowValue("{fileID: " + c.Added + "}"));
                    list.Items.Add(entry);
                }
                if (!InOrder(list, gameObjects)) m_Unwritten.Add($"{what} added to a prefab instance and reordered (their new place is not measured)");
                list.Flow = list.Items.Count == 0;
                return list;
            }

            /// <summary>Whether the entries, placed as a load places them (each, in the list's order,
            /// at its insertIndex among the target's objects, else after them), give the order the
            /// targets' children or components have now.</summary>
            bool InOrder(YText list, bool gameObjects)
            {
                var byTarget = new Dictionary<Object, List<(Object Added, int Index)>>(ReferenceEqualityComparer.Instance);
                foreach (var item in list.Items)
                {
                    if (!ObjectOf(ParseFileId(item.Get("addedObject")), out var added)) continue;
                    Object container = gameObjects ? (added as Transform ?? (added as GameObject)?.transform)?.parent : (added as Component)?.gameObject;
                    if (container == null) continue;
                    if (!byTarget.TryGetValue(container, out var entries)) byTarget[container] = entries = new List<(Object, int)>();
                    int.TryParse(item.Get("insertIndex")?.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int index);
                    entries.Add((added is GameObject g ? g.transform : added, index));
                }
                foreach (var (container, entries) in byTarget)
                {
                    var current = new List<Object>();
                    if (container is Transform parent)
                    {
                        for (int i = 0; i < parent.childCount; i++)
                            if (Saved(parent.GetChild(i).gameObject)) current.Add(parent.GetChild(i));
                    }
                    else if (container is GameObject go)
                        foreach (var c in go.m_Components) if (c != null && !c.m_Destroyed) current.Add(c);
                    var placed = new List<Object>();
                    foreach (var o in current)
                        if (!entries.Exists(e => ReferenceEquals(e.Added, o))) placed.Add(o);
                    foreach (var (added, index) in entries)
                    {
                        if (index >= 0 && index <= placed.Count) placed.Insert(index, added);
                        else placed.Add(added);
                    }
                    if (placed.Count != current.Count) return false;
                    for (int i = 0; i < placed.Count; i++)
                        if (!ReferenceEquals(placed[i], current[i])) return false;
                }
                return true;
            }

            Dictionary<long, Object> m_ObjectsById;

            /// <summary>The object this save writes under a file id.</summary>
            bool ObjectOf(long id, out Object o)
            {
                if (m_ObjectsById == null)
                {
                    m_ObjectsById = new Dictionary<long, Object>();
                    foreach (var pair in m_Ids) m_ObjectsById[pair.Value] = pair.Key;
                }
                return m_ObjectsById.TryGetValue(id, out o) && o != null;
            }

            static string SourceGuid(InstanceState state) => state.Document.Body.Get("m_SourcePrefab")?.Get("guid")?.Text ?? "";

            /// <summary>Sets a modification's value, else inserts it after the last one of the same or
            /// a smaller target file id (the Editor keeps them ordered by target).</summary>
            void SetModification(YText list, (long Target, string Path, YText Value, YText Reference) change, string sourceGuid)
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
                var guid = list.Items.Count > 0 ? list.Items[0].Get("target")?.Get("guid")?.Text : sourceGuid;
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
                    if (classId == RenderSettingsId && RenderSettings.InEffectFor(m_Target.Scene))
                        Overlay(doc.Body, Written(RenderSettings.WriteSerialized), ref m_UnlocatedCount);
                    // A scene file CreateSceneAsset writes has no skybox, unlike a new scene saved.
                    if (classId == RenderSettingsId && m_Target.Scene.IsNewSceneAsset)
                        doc.Body.Set("m_SkyboxMaterial", FlowValue("{fileID: 0}"));
                    if (classId == LightmapSettingsId) LightingSettingsReference(doc.Body);
                    // A bake that wrote the scene's lighting data names it (as the Editor's
                    // saved scene does: tests/projects/bakeenvironment EnvBakedNoSky.unity).
                    if (classId == LightmapSettingsId && m_Target.Scene.LightingDataWritten)
                    {
                        var data = m_Target.Scene.LightingDataRef;
                        doc.Body.Set("m_LightingDataAsset", FlowValue(FormattableString.Invariant($"{{fileID: {data.FileId}, guid: {data.Guid}, type: {data.Type}}}")));
                    }
                    m_Out[doc.FileId] = doc;
                }
            }

            /// <summary>The scene's Lighting Settings as it has them now (Lightmapping.lightingSettings
            /// assigns them): written when they are an asset or the scene's own object, else the save
            /// fails rather than drop the change.</summary>
            void LightingSettingsReference(YText body)
            {
                var current = m_Target.Scene.LightingSettings;
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
                foreach (var pair in m_Target.FileIds)
                    if (pair.Key is not null && Link(pair.Key) is PrefabLink link && InstanceFileId(link.Instance) == instanceId) return true;
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
                    long id = link != null ? InstanceFileId(link.Instance) : m_Ids[root.transform];
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

    /// <summary>The document of an object Unbound writes whole or in part outside a scene's
    /// hierarchy (an asset file's object, an object embedded in a scene).</summary>
    internal static class NativeDocuments
    {
        /// <summary>Writes the object's fields into its document: a script object's whole body, a
        /// material's modeled fields over the file's, a V1 sprite atlas's changed settings, an edited
        /// mesh's, texture's or physics material's data over the file's; false (the document kept
        /// as read) for other classes and for those it cannot write.</summary>
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
            if (o is UnityEngine.U2D.SpriteAtlas atlas && !atlas.IsFromImporter)
            {
                WriteSpriteAtlas(doc.Body, atlas);
                return true;
            }
            // An edited native asset read from its file. Not edited, or a document in another
            // serialization than the release's (an older version, keys it does not hold) is kept as
            // read; so are unreadable, compressed and block-compressed ones (unmeasured).
            if (ReadNativeKeys(o) is string[] keys && EditorUtility.IsDirty(o) && Compat.ReadAssetSavesMeasured)
            {
                if (!UnityYamlWriter.WritesNew(o)) return false;
                var text = UnityYamlWriter.Write(new[] { o }, new Dictionary<Object, long>(ReferenceEqualityComparer.Instance) { [o] = doc.FileId });
                var written = UnityYamlText.Parse(text)[0].Body;
                if (!SameSerialization(doc.Body, written, keys)) return false;
                var kept = YText.Map();
                foreach (var key in keys) kept.Set(key, written.Get(key));
                SceneFiles.Overlay(doc.Body, kept, ref unlocated);
                return true;
            }
            return false;
        }

        /// <summary>Whether a file's document is in the serialization the writer writes: the same
        /// serialized version, of the document and of each written map that has one, and every
        /// written key present.</summary>
        static bool SameSerialization(YText file, YText written, string[] keys)
        {
            if (file.Get("serializedVersion")?.Raw != written.Get("serializedVersion")?.Raw) return false;
            foreach (var key in keys)
            {
                var value = written.Get(key);
                var held = file.Get(key);
                if (value == null || held == null) return false;
                if (value.Kind == YKind.Map && value.Get("serializedVersion") is YText version && held.Get("serializedVersion")?.Raw != version.Raw) return false;
            }
            return true;
        }

        /// <summary>The keys of a native asset read from its file that SaveAssets writes from the
        /// object, the rest of its document kept as the file has it: a mesh's geometry, a texture's
        /// size, format and data, a physics material's values (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/scenesave native-saves; the keys are those its API changes and Unbound
        /// reads back whole). Null for other types.</summary>
        static string[] ReadNativeKeys(Object o) => o switch
        {
            Mesh => s_MeshKeys,
            Texture2D => s_Texture2DKeys,
            PhysicsMaterial => s_PhysicsMaterialKeys,
            _ => null,
        };

        static readonly string[] s_MeshKeys =
        {
            "m_SubMeshes", "m_Shapes", "m_BindPose", "m_BoneNameHashes", "m_RootBoneNameHash", "m_BonesAABB",
            "m_IndexFormat", "m_IndexBuffer", "m_VertexData", "m_LocalAABB",
        };

        static readonly string[] s_Texture2DKeys =
        {
            "m_Width", "m_Height", "m_CompleteImageSize", "m_TextureFormat", "m_MipCount", "image data", "_typelessdata",
        };

        static readonly string[] s_PhysicsMaterialKeys =
        {
            "m_DynamicFriction", "m_StaticFriction", "m_Bounciness", "m_FrictionCombine", "m_BounceCombine",
        };

        /// <summary>The keys of a V1 sprite atlas document in the order the Editor writes them.</summary>
        static readonly string[] s_SpriteAtlasKeyOrder =
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_Name",
            "m_EditorData", "m_MasterAtlas", "m_PackedSprites", "m_PackedSpriteNamesToIndex", "m_RenderDataMap", "m_Tag", "m_IsVariant",
        };

        /// <summary>Writes a V1 sprite atlas into its `.spriteatlas` document: its Editor settings
        /// whole, and the packables, master, tag, variant flag and packed sprites that changed, each
        /// key in the Editor's place (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/spriteatlaseditor and tests/projects/spriteatlaspack).</summary>
        static void WriteSpriteAtlas(YText body, UnityEngine.U2D.SpriteAtlas atlas)
        {
            var editorData = body.Get("m_EditorData");
            if (editorData == null || editorData.Kind != YKind.Map)
            {
                editorData = YText.Map();
                body.SetInOrder("m_EditorData", editorData, s_SpriteAtlasKeyOrder);
            }
            var order = UnityEditor.U2D.SpriteAtlasEditorSettings.KeyOrder;
            atlas.Settings.Write(editorData, prefixed: true);
            if (atlas.PackablesModified)
                editorData.SetInOrder("packables", atlas.ToPackablesYText(), order);
            if (atlas.MasterAtlasModified)
                body.SetInOrder("m_MasterAtlas", SceneFiles.FlowValue(UnityYamlWriter.Writer.ReferenceText(atlas.MasterAtlasRef)), s_SpriteAtlasKeyOrder);
            if (atlas.TagModified)
                body.SetInOrder("m_Tag", YText.Scalar(UnityYamlWriter.Scalar(atlas.RawTag ?? "")), s_SpriteAtlasKeyOrder);
            if (atlas.IsVariantModified)
                body.SetInOrder("m_IsVariant", YText.Scalar(atlas.isVariant ? "1" : "0"), s_SpriteAtlasKeyOrder);
            if (atlas.PackedDataModified)
            {
                editorData.SetInOrder("packedSpriteRenderDataKeys", atlas.ToPackedRenderDataKeysYText(), order);
                body.SetInOrder("m_PackedSprites", atlas.ToPackedSpritesYText(), s_SpriteAtlasKeyOrder);
                body.SetInOrder("m_PackedSpriteNamesToIndex", atlas.ToPackedSpriteNamesYText(), s_SpriteAtlasKeyOrder);
                atlas.ClearPackedDataModified();
            }
        }
    }
}
