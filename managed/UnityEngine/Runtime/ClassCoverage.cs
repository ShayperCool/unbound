using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>How the loader treats the documents of one Unity class.</summary>
    internal enum ClassReading
    {
        /// <summary>A registered engine class reads its data (a component's ReadSerialized, an
        /// <see cref="ISerializedAsset"/>, a ScriptableObject's fields).</summary>
        Read,
        /// <summary>The loader itself uses it (GameObject, prefab instances, scene roots,
        /// MonoBehaviour by its script, the scene settings it applies).</summary>
        Loader,
        /// <summary>Knowingly not loaded, with a reason (<see cref="ClassCoverage.Ignored"/>).</summary>
        Ignored,
        /// <summary>A registered engine class that does not read the document's data: it loads
        /// with default settings.</summary>
        Unread,
        /// <summary>No engine class: its objects are skipped (references to them are null).</summary>
        Unknown,
        /// <summary>A reader exists, but the project's documents contain a representation it
        /// rejects or a feature Unbound does not draw.</summary>
        Unsupported,
    }

    /// <summary>A Behaviour whose complete serialized runtime state is the enabled flag read
    /// by ObjectGraphBuilder.ReadComponent. Classes with other data must supply a reader.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed class EnabledOnlySerializedStateAttribute : Attribute { }

    /// <summary>
    /// Which Unity classes the loader reads (Unity's class ids, Manual "YAML Class ID Reference"),
    /// so that a class without a reader is loud: <see cref="Note"/> warns once per class at load,
    /// naming the class and the file, and <c>unbound doctor</c> lists every file of the project
    /// that holds one (<see cref="Report"/>). Unity's own class table of the release comes from
    /// tests/projects/classids.
    /// </summary>
    internal static class ClassCoverage
    {
        /// <summary>Keys every object or component document has; data beyond these means there is
        /// something to read.</summary>
        static readonly HashSet<string> s_CommonKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset",
            "m_PrefabInternal", "m_PrefabParentObject", "m_GameObject", "m_Enabled", "m_Name",
            "serializedVersion", "m_EditorHideFlags", "m_EditorClassIdentifier",
        };

        /// <summary>The scene settings objects the loader applies (<see cref="SceneSettings"/>),
        /// the bundle objects it uses (<see cref="ObjectGraphBuilder"/>).</summary>
        static readonly Dictionary<int, string> s_Loader = new Dictionary<int, string>
        {
            [EngineTypes.GameObjectId] = "GameObject",
            [EngineTypes.MonoBehaviourId] = "MonoBehaviour: read by its script's class",
            [EngineTypes.PrefabInstanceId] = "PrefabInstance: flattened by the native side",
            [1660057539] = "SceneRoots: the scene's root order",
            [104] = "RenderSettings: applied by the scene",
            [157] = "LightmapSettings: the scene's baked lighting",
            [196] = "NavMeshSettings: the scene's baked NavMesh (m_NavMeshData)",
            [142] = "AssetBundle: a bundle's own object",
            [115] = "MonoScript: a bundle's scripts",
            [150] = "PreloadData: a bundle's own object",
        };

        /// <summary>Classes Unbound knowingly does not load, with the reason. A class listed here
        /// does not warn. Only classes whose objects cannot change what Play mode does belong here
        /// (as <see cref="EditorFaithfulAttribute"/> does for members); one that would only be
        /// approximated stays unread, and loud.</summary>
        static readonly Dictionary<int, string> s_Ignored = new Dictionary<int, string>
        {
            [1002] = "EditorExtensionImpl: editor bookkeeping of old prefab data, no runtime object",
        };

        /// <summary>Scene settings Unbound does not read, ignored while they hold only the
        /// Editor's bake inputs; unread once they reference baked data (the key named).</summary>
        static readonly Dictionary<int, (string Key, string Data)> s_IgnoredWithoutData = new Dictionary<int, (string, string)>
        {
            [29] = ("m_OcclusionCullingData", "the scene's baked occlusion data"),
        };

        internal const string DoctorHint = " `unbound doctor --all --json` reports unsupported serialized content in the source project, including the source of AssetBundle scenes.";

        static readonly HashSet<int> s_Warned = new HashSet<int>();

        /// <summary>The loader's handling of a document of a class, given its top-level keys
        /// (null: the class alone decides).</summary>
        internal static (ClassReading Reading, string Detail) Classify(int classId, Func<string, bool> hasData, string release = null)
        {
            if (classId == 92 && Compat.RemovesLegacyGuiLayer(release ?? ProjectInfo.UnityVersion))
                return (ClassReading.Ignored, "GUILayer: obsolete serialized component removed on load (tests/projects/classids)");
            if (classId == 181963792)
                return (ClassReading.Unsupported, "Preset: UnityEditor.Presets.Preset serialized properties and ApplyTo are not supported; references to this asset are null");
            if (s_Loader.TryGetValue(classId, out var loader)) return (ClassReading.Loader, loader);
            if (s_Ignored.TryGetValue(classId, out var reason)) return (ClassReading.Ignored, reason);
            if (s_IgnoredWithoutData.TryGetValue(classId, out var data))
                return hasData != null && hasData(data.Key)
                    ? (ClassReading.Unread, data.Data + " (" + data.Key + ") is not loaded")
                    : (ClassReading.Ignored, "only the Editor's bake inputs, no baked data");
            var type = EngineTypes.ForClassId(classId, null, warn: false);
            if (type == null) return (ClassReading.Unknown, "no engine class: its objects are skipped");
            if (type == typeof(AudioMixerEffectController))
                return (ClassReading.Read, type.FullName + " (Chorus, Flange, Distortion, Normalize and Pitch Shifter support only Unity 6000.0.71f1; Normalize and Pitch Shifter only 1024-frame stereo at 48000/44100 Hz and no live changes, Pitch Shifter only FFT size 1024, Overlap 4, Max channels 0; static wet mixing on those five effects supports 1024-frame stereo at 48000/44100 Hz through +30 dB with no live effect changes; SFX Reverb wet mixing retains its 0 dB maximum; other slot wet mixing is unsupported; Send/Receive/Duck Volume require Unity 6000.0.71f1; sends to another mixer and unscaled timed sidechain snapshots are unmeasured and fail; scaled-time Send and Duck Volume snapshots support linear, Smoothstep, Squared, SquareRoot, BrickwallStart and BrickwallEnd transitions; weighted snapshot arrays use linear transitions; other sidechain transition overrides are unmeasured and fail)");
            // These registered Behaviour classes declare no serialized data beyond the
            // enabled flag that ReadComponent consumes for every Behaviour.
            if (type == typeof(FlareLayer))
                return (ClassReading.Read, type.FullName + " (Behaviour enabled flag; requires Camera on the same GameObject; " + (Compat.FlareCameraRepairMeasuredFor(release ?? ProjectInfo.UnityVersion) ? "creates a missing Camera on load with a warning" : "missing-Camera load repair is unmeasured for this release and rejected") + "; disabled cameras do not render automatically)");
            if (type.IsDefined(typeof(EnabledOnlySerializedStateAttribute), inherit: false))
                return (ClassReading.Read, type.FullName + " (Behaviour enabled flag)");
            if (Reads(type)) return (ClassReading.Read, type.FullName);
            // Nothing beyond the common keys: the defaults are what the document says.
            if (hasData != null && !hasData(null)) return (ClassReading.Read, type.FullName + " (no data to read)");
            return (ClassReading.Unread, type.FullName + " does not read its data: it loads with default settings");
        }

        static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> s_Reads = new System.Collections.Concurrent.ConcurrentDictionary<Type, bool>();

        /// <summary>Whether a registered class reads its documents' data.</summary>
        static bool Reads(Type type) => s_Reads.GetOrAdd(type, ReadsUncached);

        static bool ReadsUncached(Type type)
        {
            if (typeof(ScriptableObject).IsAssignableFrom(type) || typeof(ISerializedAsset).IsAssignableFrom(type)) return true;
            if (!typeof(Component).IsAssignableFrom(type)) return false;
            var method = type.GetMethod("ReadSerialized", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(YNode), typeof(IReferenceResolver) }, null);
            return method != null && method.DeclaringType != typeof(Component);
        }

        /// <summary>Whether a document has data beyond the common keys (key null), or that key
        /// referencing an object.</summary>
        static Func<string, bool> DataOf(YNode body) => key =>
        {
            if (key != null) return body?.Ref(key).IsNull == false;
            if (body?.Keys == null) return false;
            foreach (var k in body.Keys)
                if (!s_CommonKeys.Contains(k)) return true;
            return false;
        };

        /// <summary>Warns once per class when a loaded document's class is unread or unknown.</summary>
        internal static void Note(YDocument doc, string path)
        {
            if (doc.Stripped) return;
            var (reading, detail) = Classify(doc.ClassId, DataOf(doc.Body));
            if (reading != ClassReading.Unread && reading != ClassReading.Unknown && reading != ClassReading.Unsupported) return;
            lock (s_Warned)
            {
                if (!s_Warned.Add(doc.ClassId)) return;
            }
            Debug.LogWarning($"Unbound: {doc.TypeName} (class {doc.ClassId}) in {path} is not read: {detail}." +
                DoctorHint);
        }

        /// <summary>One class found in the project's files (<c>unbound doctor</c>).</summary>
        internal sealed class Found
        {
            public int ClassId { get; set; }
            public string TypeName { get; set; }
            /// <summary>The top-level keys its documents have.</summary>
            public List<string> Keys { get; set; }
            /// <summary>The keys of <see cref="s_IgnoredWithoutData"/> that reference an object in
            /// any of its documents.</summary>
            public List<string> Referenced { get; set; }
            /// <summary>The documents of the classes checked value by value, by file: Sprite
            /// (213) for the same layout checks used at load (the doctor does not resolve
            /// textures or evaluate their imported dimensions). Lighting Settings are checked with
            /// their scene references by the field audit.</summary>
            public Dictionary<string, string> Documents { get; set; }
        }

        internal sealed class Entry
        {
            public int ClassId { get; set; }
            public string TypeName { get; set; }
            public string Reading { get; set; }
            public string Detail { get; set; }
            /// <summary>Unity's name of the class in the release's table; null when the release
            /// has no such class.</summary>
            public string UnityName { get; set; }
            /// <summary>A loader or registered object type exists, including default-only types.</summary>
            public bool InspectFields { get; set; }
        }

        internal sealed class Coverage
        {
            public string Release { get; set; }
            /// <summary>The release's class table was found.</summary>
            public bool HasTable { get; set; }
            public List<Entry> Classes { get; set; } = new List<Entry>();
            /// <summary>Unity's concrete runtime classes (not abstract, not editor-only, not project
            /// settings or serialization placeholders) that the loader neither reads nor handles nor
            /// ignores, whatever the project holds.</summary>
            public List<Entry> UnreadInRelease { get; set; } = new List<Entry>();
        }

        /// <summary>How the loader treats each class found in a project, and the release's
        /// classes without a reader.</summary>
        internal static Coverage Report(string release, IEnumerable<Found> found)
        {
            var table = UnityTable(release);
            var report = new Coverage { Release = release, HasTable = table != null };
            foreach (var f in found)
            {
                var keys = new HashSet<string>(f.Keys ?? new List<string>(), StringComparer.Ordinal);
                var referenced = new HashSet<string>(f.Referenced ?? new List<string>(), StringComparer.Ordinal);
                bool HasData(string key)
                {
                    if (key != null) return referenced.Contains(key);
                    foreach (var k in keys) if (!s_CommonKeys.Contains(k)) return true;
                    return false;
                }
                var (reading, detail) = Classify(f.ClassId, HasData, release);
                Func<YNode, string> check = f.ClassId == 213 ? SpriteProblem : null;
                if (check != null && f.Documents != null)
                {
                    // Each problem once, with the objects that have it.
                    var problems = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    var order = new List<string>();
                    void Add(string problem, string where)
                    {
                        if (!problems.TryGetValue(problem, out var list))
                        {
                            problems[problem] = list = new List<string>();
                            order.Add(problem);
                        }
                        list.Add(where);
                    }
                    foreach (var pair in f.Documents)
                    {
                        try
                        {
                            foreach (var doc in UnityYamlText.Parse(pair.Value))
                            {
                                string problem;
                                try { problem = check(doc.Body.ToNode()); }
                                catch (Exception e) { problem = e.Message; }
                                if (problem != null) Add(problem, $"{pair.Key} fileID {doc.FileId}");
                            }
                        }
                        catch (Exception e) { Add(e.Message, pair.Key); }
                    }
                    if (order.Count != 0)
                        (reading, detail) = (ClassReading.Unsupported, string.Join("; ", order.ConvertAll(p => $"{string.Join(", ", problems[p])}: {p}")));
                }
                report.Classes.Add(new Entry
                {
                    ClassId = f.ClassId, TypeName = f.TypeName, Reading = reading.ToString(), Detail = detail,
                    UnityName = table != null && table.TryGetValue(f.ClassId, out var c) ? c.Name : null,
                    InspectFields = reading != ClassReading.Ignored && (reading == ClassReading.Loader || EngineTypes.ForClassId(f.ClassId, null, warn: false) != null),
                });
            }
            if (table != null)
            {
                foreach (var pair in table)
                {
                    var c = pair.Value;
                    if (c.Abstract || c.EditorOnly || !CanBeContent(table, c)) continue;
                    var (reading, detail) = Classify(pair.Key, null, release);
                    if (reading == ClassReading.Unread || reading == ClassReading.Unknown)
                        report.UnreadInRelease.Add(new Entry { ClassId = pair.Key, TypeName = c.Name, UnityName = c.Name, Reading = reading.ToString(), Detail = detail });
                }
            }
            return report;
        }

        static string SpriteProblem(YNode body)
        {
            Sprite.ValidateSerialized(body);
            return null;
        }

        /// <summary>Whether objects of a class can be in scenes, prefabs or assets: not a
        /// serialization placeholder (no base: `int`, `Vector3f`), not a project settings manager
        /// (a GlobalGameManager, read from ProjectSettings by the settings assets).</summary>
        static bool CanBeContent(SortedDictionary<int, UnityClass> table, UnityClass c)
        {
            if (string.IsNullOrEmpty(c.Base) && c.Name != "Object") return false;
            var byName = new Dictionary<string, UnityClass>(StringComparer.Ordinal);
            foreach (var other in table.Values) byName[other.Name] = other;
            for (var b = c.Base; !string.IsNullOrEmpty(b) && byName.TryGetValue(b, out var parent); b = parent.Base)
                if (b == "GlobalGameManager") return false;
            return true;
        }

        /// <summary>Orders release names by their numbers (6000.0.9f1 before 6000.0.71f1).</summary>
        static int CompareVersions(string a, string b)
        {
            var x = System.Text.RegularExpressions.Regex.Matches(a, "[0-9]+");
            var y = System.Text.RegularExpressions.Regex.Matches(b, "[0-9]+");
            for (int i = 0; i < Math.Min(x.Count, y.Count); i++)
            {
                int c = long.Parse(x[i].Value).CompareTo(long.Parse(y[i].Value));
                if (c != 0) return c;
            }
            return x.Count.CompareTo(y.Count);
        }

        internal sealed class UnityClass
        {
            public string Name;
            public string Base;
            public bool Abstract;
            public bool EditorOnly;
        }

        /// <summary>The release's class table (tests/projects/classids/Reference), the exact release's
        /// or else the latest one of its family; null when there is none.</summary>
        internal static SortedDictionary<int, UnityClass> UnityTable(string release)
        {
            var assembly = typeof(ClassCoverage).Assembly;
            const string prefix = "Unbound.class-ids-unity-";
            string best = null;
            var family = release != null && release.Split('.') is var parts && parts.Length >= 2 ? parts[0] + "." + parts[1] + "." : null;
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var version = name.Substring(prefix.Length, name.Length - prefix.Length - ".jsonl".Length);
                if (version == release) { best = name; break; }
                if (family != null && version.StartsWith(family, StringComparison.Ordinal) && (best == null || CompareVersions(version, best.Substring(prefix.Length)) > 0)) best = name;
            }
            if (best == null) return null;
            var table = new SortedDictionary<int, UnityClass>();
            using var reader = new StreamReader(assembly.GetManifestResourceStream(best));
            for (string line; (line = reader.ReadLine()) != null;)
            {
                if (line.Length == 0) continue;
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("id", out var id)) continue;
                table[id.GetInt32()] = new UnityClass
                {
                    Name = root.GetProperty("name").GetString(),
                    Base = root.GetProperty("base").GetString(),
                    Abstract = root.GetProperty("abstract").GetBoolean(),
                    EditorOnly = root.GetProperty("editorOnly").GetBoolean(),
                };
            }
            return table;
        }
    }
}
