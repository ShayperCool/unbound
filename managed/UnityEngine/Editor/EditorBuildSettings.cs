using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>The project's build settings as the Editor exposes them: the scene list and the
    /// config objects packages register (Addressables, Localization, ...) of
    /// ProjectSettings/EditorBuildSettings.asset. A new scene list and added or
    /// removed config objects are saved into that file as the Editor saves settings
    /// (SettingsAssets.Edit: by SaveAssets and at the Editor's exit in Edit mode; the config objects
    /// as a map by name in ordinal order, matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/scenesave settings-writes and build-scenes).</summary>
    public partial class EditorBuildSettings : Object
    {
        static EditorBuildSettingsScene[] s_Scenes;
        static readonly Dictionary<string, (string Guid, long FileId)> s_ConfigRefs = new Dictionary<string, (string, long)>();
        static readonly Dictionary<string, Object> s_ConfigObjects = new Dictionary<string, Object>();
        static bool s_Read;
        static bool? s_ParallelAssetBundles;

        public static event Action sceneListChanged;

        /// <summary>The scene list and config objects the host read from the project's file.</summary>
        internal static void Configure(IEnumerable<EditorBuildSettingsScene> scenes, Dictionary<string, (string Guid, long FileId)> configObjects)
        {
            s_Scenes = new List<EditorBuildSettingsScene>(scenes).ToArray();
            s_ConfigRefs.Clear();
            s_ConfigObjects.Clear();
            foreach (var pair in configObjects) s_ConfigRefs[pair.Key] = pair.Value;
            s_Read = true;
        }

        /// <summary>Without the host's list (unit tests), the project's file, read once.</summary>
        static void Read()
        {
            if (s_Read) return;
            s_Read = true;
            var scenes = new List<EditorBuildSettingsScene>();
            var body = SettingsAssets.ReadBody("EditorBuildSettings");
            if (body?.Get("m_Scenes") is YText list && list.Kind == YKind.Seq)
                foreach (var item in list.Items)
                {
                    if (item.Kind != YKind.Map) continue;
                    scenes.Add(new EditorBuildSettingsScene
                    {
                        enabled = item.Get("enabled")?.Text == "1",
                        path = item.Get("path")?.Text ?? "",
                        guid = new GUID(item.Get("guid")?.Text ?? ""),
                    });
                }
            s_Scenes ??= scenes.ToArray();
            if (body?.Get("m_configObjects") is YText map && map.Kind == YKind.Map)
                for (int i = 0; i < map.Keys.Count; i++)
                {
                    var reference = map.Values[i];
                    if (reference.Kind != YKind.Map || !long.TryParse(reference.Get("fileID")?.Text, out var fileId)) continue;
                    if (!s_ConfigRefs.ContainsKey(map.Keys[i]) && !s_ConfigObjects.ContainsKey(map.Keys[i]))
                        s_ConfigRefs[map.Keys[i]] = (reference.Get("guid")?.Text ?? "", fileId);
                }
        }

        /// <summary>Forgets what was read (another project; tests).</summary>
        internal static void Reset()
        {
            s_Read = false;
            s_Scenes = null;
            s_ParallelAssetBundles = null;
            s_ConfigRefs.Clear();
            s_ConfigObjects.Clear();
        }

        /// <summary>The list, each scene a copy (the Editor's getter makes new objects; matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/scenesave build-scenes set.sameInstance). Setting
        /// it copies the scenes given (a later change to them changes nothing), takes a scene's path
        /// from its GUID when the project has that asset (a stale path, or none), keeps a missing
        /// one's as given, treats null as an empty list and refuses a null entry
        /// (NullReferenceException, nothing changed); sceneListChanged follows only a change of
        /// the list; the scenes Play mode loads by build index are its enabled ones (build-scenes
        /// set.*, again.*, nullEntry.*, nullList.*, empty.*).</summary>
        public static EditorBuildSettingsScene[] scenes
        {
            get => Copies();
            set
            {
                if (SetScenes(value)) sceneListChanged?.Invoke();
            }
        }

        /// <summary>The list ignoring build profiles (Unbound has none: the same list); setting it
        /// raises no sceneListChanged (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/scenesave build-scenes global.*).</summary>
        public static EditorBuildSettingsScene[] globalScenes
        {
            get => Copies();
            set => SetScenes(value);
        }

        static EditorBuildSettingsScene[] Copies()
        {
            Read();
            var copies = new EditorBuildSettingsScene[s_Scenes.Length];
            for (int i = 0; i < copies.Length; i++) copies[i] = Copy(s_Scenes[i]);
            return copies;
        }

        static EditorBuildSettingsScene Copy(EditorBuildSettingsScene s) =>
            new EditorBuildSettingsScene { enabled = s.enabled, path = s.path, guid = s.guid };

        /// <summary>Takes a new list; whether it differs from the old one.</summary>
        static bool SetScenes(EditorBuildSettingsScene[] value)
        {
            Read();
            value ??= Array.Empty<EditorBuildSettingsScene>();
            foreach (var scene in value)
                if (scene == null) throw new NullReferenceException("(null)");
            var next = new EditorBuildSettingsScene[value.Length];
            for (int i = 0; i < value.Length; i++)
            {
                var scene = Copy(value[i]);
                if (!scene.guid.Empty() && AssetDatabase.GUIDToAssetPath(scene.guid) is string known && known.Length > 0) scene.path = known;
                next[i] = scene;
            }
            bool changed = next.Length != s_Scenes.Length;
            for (int i = 0; !changed && i < next.Length; i++)
                changed = next[i].enabled != s_Scenes[i].enabled || next[i].path != s_Scenes[i].path || !next[i].guid.Equals(s_Scenes[i].guid);
            if (!changed) return false;
            s_Scenes = next;
            UnityEngine.ProjectInfo.BuildScenes.Clear();
            foreach (var scene in next)
                if (scene.enabled && !string.IsNullOrEmpty(scene.path)) UnityEngine.ProjectInfo.BuildScenes.Add((scene.path, scene.guid.ToString()));
            SettingsAssets.Edit("EditorBuildSettings", body =>
            {
                var list = YText.Seq();
                foreach (var scene in next)
                {
                    var entry = YText.Map();
                    entry.Add("enabled", YText.Scalar(scene.enabled ? "1" : "0"));
                    entry.Add("path", YText.Scalar(UnityYamlWriter.Scalar(scene.path ?? "")));
                    entry.Add("guid", YText.Scalar(scene.guid.ToString()));
                    list.Items.Add(entry);
                }
                list.Flow = list.Items.Count == 0;
                body.Set("m_Scenes", list);
            });
            return true;
        }

        /// <summary>Whether asset bundles build in parallel, saved as m_UseUCBPForAssetBundles
        /// (matches the Unity 6000.0.71f1 Editor, tests/projects/scenesave build-scenes parallel.*).</summary>
        public static bool UseParallelAssetBundleBuilding
        {
            get
            {
                if (s_ParallelAssetBundles is bool known) return known;
                var value = SettingsAssets.ReadBody("EditorBuildSettings")?.Get("m_UseUCBPForAssetBundles")?.Text == "1";
                s_ParallelAssetBundles = value;
                return value;
            }
            set
            {
                s_ParallelAssetBundles = value;
                SettingsAssets.Edit("EditorBuildSettings", body => body.Set("m_UseUCBPForAssetBundles", YText.Scalar(value ? "1" : "0")));
            }
        }

        public static bool TryGetConfigObject<T>(string name, out T result) where T : Object
        {
            result = GetConfigObject(name) as T;
            return result != null;
        }

        public static string[] GetConfigObjectNames()
        {
            Read();
            var names = new SortedSet<string>(s_ConfigRefs.Keys, StringComparer.Ordinal);
            names.UnionWith(s_ConfigObjects.Keys);
            return new List<string>(names).ToArray();
        }

        public static void AddConfigObject(string name, Object obj, bool overwrite)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name cannot be null or empty");
            // Null and objects that are no assets are refused, nothing changed (matches the Unity
            // 6000.0.71f1 Editor, tests/projects/scenesave settings-writes config.null, config.notPersisted).
            if (obj == null) throw new Exception($"Cannot add null config object with name '{name}'.");
            if (!obj.IsPersistentAsset || !AssetDatabaseRuntime.TryGetLocation(obj, out _, out _))
                throw new Exception($"Cannot add non-persisted config object with name '{name}'.");
            Read();
            if (!overwrite && GetConfigObject(name) != null)
                throw new Exception($"Config object with name '{name}' already exists.");
            s_ConfigObjects[name] = obj;
            s_ConfigRefs.Remove(name);
            var reference = SceneFiles.FlowValue(new UnityYamlWriter.Writer(new System.Text.StringBuilder(), new Dictionary<Object, long>()).ReferenceText(obj));
            EditConfigObjects(map => SettingsAssets.SetInOrdinalOrder(map, name, reference));
        }

        public static bool RemoveConfigObject(string name)
        {
            Read();
            bool removed = s_ConfigObjects.Remove(name);
            removed = s_ConfigRefs.Remove(name) || removed;
            if (removed)
                EditConfigObjects(map =>
                {
                    int at = map.Keys.IndexOf(name);
                    if (at < 0) return;
                    map.Keys.RemoveAt(at);
                    map.Values.RemoveAt(at);
                });
            return removed;
        }

        /// <summary>The file's config object map changed (a block map, or <c>{}</c> when empty).</summary>
        static void EditConfigObjects(Action<YText> change) => SettingsAssets.Edit("EditorBuildSettings", body =>
        {
            var map = body.Get("m_configObjects");
            if (map == null || map.Kind != YKind.Map) map = YText.Map();
            change(map);
            map.Flow = map.Keys.Count == 0;
            map.Raw = null;
            body.Set("m_configObjects", map);
        });

        static Object GetConfigObject(string name)
        {
            if (name == null) return null;
            Read();
            if (s_ConfigObjects.TryGetValue(name, out var obj) && obj != null) return obj;
            if (!s_ConfigRefs.TryGetValue(name, out var reference)) return null;
            obj = AssetDatabaseRuntime.Load(reference.Guid, reference.FileId, typeof(Object));
            if (obj != null) s_ConfigObjects[name] = obj;
            return obj;
        }
    }

    /// <summary>A scene of the build settings' list, as Unity's own C# makes it (6000.0,
    /// Editor/Mono/EditorBuildSettings.bindings.cs): a path with back slashes made forward ones
    /// (null throws), the GUID of the asset at that path, the path of the asset of a GUID.</summary>
    public partial class EditorBuildSettingsScene : IComparable
    {
        string m_Path;

        public EditorBuildSettingsScene() { }

        public EditorBuildSettingsScene(string path, bool enabled)
        {
            m_Path = path.Replace("\\", "/");
            this.enabled = enabled;
            guid = new GUID(AssetDatabase.AssetPathToGUID(path));
        }

        public EditorBuildSettingsScene(GUID guid, bool enabled)
        {
            this.guid = guid;
            this.enabled = enabled;
            m_Path = AssetDatabase.GUIDToAssetPath(guid.ToString());
        }

        public bool enabled { get; set; }
        public string path { get => m_Path; set => m_Path = value.Replace("\\", "/"); }
        public GUID guid { get; set; }

        public static string[] GetActiveSceneList(EditorBuildSettingsScene[] scenes) =>
            System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(
                System.Linq.Enumerable.Where(scenes, scene => scene.enabled && !string.IsNullOrEmpty(scene.path)), scene => scene.path));

        public int CompareTo(object obj)
        {
            if (obj is EditorBuildSettingsScene temp) return temp.path.CompareTo(path);
            throw new ArgumentException("object is not a EditorBuildSettingsScene");
        }
    }
}
