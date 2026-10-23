using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>The project's build settings as the Editor exposes them: the scene list and the
    /// config objects packages register (Addressables, Localization, ...). Changes stay in memory:
    /// Unbound does not write ProjectSettings.</summary>
    public partial class EditorBuildSettings : Object
    {
        static EditorBuildSettingsScene[] s_Scenes = Array.Empty<EditorBuildSettingsScene>();
        static readonly Dictionary<string, (string Guid, long FileId)> s_ConfigRefs = new Dictionary<string, (string, long)>();
        static readonly Dictionary<string, Object> s_ConfigObjects = new Dictionary<string, Object>();

        public static event Action sceneListChanged;

        internal static void Configure(IEnumerable<EditorBuildSettingsScene> scenes, Dictionary<string, (string Guid, long FileId)> configObjects)
        {
            s_Scenes = new List<EditorBuildSettingsScene>(scenes).ToArray();
            s_ConfigRefs.Clear();
            s_ConfigObjects.Clear();
            foreach (var pair in configObjects) s_ConfigRefs[pair.Key] = pair.Value;
        }

        public static EditorBuildSettingsScene[] scenes
        {
            get => (EditorBuildSettingsScene[])s_Scenes.Clone();
            set
            {
                s_Scenes = value ?? Array.Empty<EditorBuildSettingsScene>();
                sceneListChanged?.Invoke();
            }
        }

        public static bool TryGetConfigObject<T>(string name, out T result) where T : Object
        {
            result = GetConfigObject(name) as T;
            return result != null;
        }

        public static string[] GetConfigObjectNames()
        {
            var names = new SortedSet<string>(s_ConfigRefs.Keys, StringComparer.Ordinal);
            names.UnionWith(s_ConfigObjects.Keys);
            return new List<string>(names).ToArray();
        }

        public static void AddConfigObject(string name, Object obj, bool overwrite)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name cannot be null or empty");
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (!overwrite && GetConfigObject(name) != null)
                throw new Exception($"Config object with name '{name}' already exists.");
            s_ConfigObjects[name] = obj;
            s_ConfigRefs.Remove(name);
        }

        public static bool RemoveConfigObject(string name)
        {
            bool removed = s_ConfigObjects.Remove(name);
            return s_ConfigRefs.Remove(name) || removed;
        }

        static Object GetConfigObject(string name)
        {
            if (name == null) return null;
            if (s_ConfigObjects.TryGetValue(name, out var obj) && obj != null) return obj;
            if (!s_ConfigRefs.TryGetValue(name, out var reference)) return null;
            obj = AssetDatabaseRuntime.Load(reference.Guid, reference.FileId, typeof(Object));
            if (obj != null) s_ConfigObjects[name] = obj;
            return obj;
        }
    }

    /// <summary>A scene of the build settings' list.</summary>
    public partial class EditorBuildSettingsScene : IComparable
    {
        public EditorBuildSettingsScene() { }

        public EditorBuildSettingsScene(string path, bool enabled)
        {
            this.path = path;
            this.enabled = enabled;
            guid = new GUID(AssetDatabase.AssetPathToGUID(path));
        }

        public EditorBuildSettingsScene(GUID guid, bool enabled)
        {
            this.guid = guid;
            this.enabled = enabled;
            path = AssetDatabase.GUIDToAssetPath(guid);
        }

        public bool enabled { get; set; }
        public string path { get; set; }
        public GUID guid { get; set; }

        public static string[] GetActiveSceneList(EditorBuildSettingsScene[] scenes)
        {
            var list = new List<string>();
            if (scenes != null)
                foreach (var s in scenes)
                    if (s != null && s.enabled) list.Add(s.path);
            return list.ToArray();
        }

        public int CompareTo(object obj) =>
            obj is EditorBuildSettingsScene other ? string.CompareOrdinal(path, other.path) : 1;
    }
}
