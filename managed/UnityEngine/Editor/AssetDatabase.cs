using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// The Editor's asset database, as game code uses it in Play mode (under UNITY_EDITOR): paths and
    /// GUIDs, loading assets by path, FindAssets with type, label and name filters. Unbound reads the
    /// project's assets directly, so these answer as the Editor would.
    /// </summary>
    public partial class AssetDatabase
    {
        sealed class Entry
        {
            public string Guid { get; set; }
            public string Path { get; set; }
            public string Kind { get; set; }
            public string[] Labels { get; set; }
        }

        static List<Entry> s_Entries;
        static Dictionary<string, Entry> s_ByGuid;
        static Dictionary<string, Entry> s_ByPath;
        static readonly JsonSerializerOptions s_Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        static List<Entry> Entries
        {
            get
            {
                if (s_Entries != null) return s_Entries;
                s_Entries = JsonSerializer.Deserialize<List<Entry>>(Native.Call("asset-list", "{}"), s_Json) ?? new List<Entry>();
                s_ByGuid = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                s_ByPath = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in s_Entries)
                {
                    s_ByGuid[e.Guid] = e;
                    s_ByPath[e.Path] = e;
                }
                return s_Entries;
            }
        }

        internal static void Invalidate() => s_Entries = null;

        public static string GUIDToAssetPath(string guid)
        {
            _ = Entries;
            return guid != null && s_ByGuid.TryGetValue(guid, out var e) ? e.Path : "";
        }

        public static string GUIDToAssetPath(GUID guid) => GUIDToAssetPath(guid.ToString());

        public static string AssetPathToGUID(string path)
        {
            _ = Entries;
            return path != null && s_ByPath.TryGetValue(path.Replace('\\', '/'), out var e) ? e.Guid : "";
        }

        public static string AssetPathToGUID(string path, AssetPathToGUIDOptions options) => AssetPathToGUID(path);

        public static GUID GUIDFromAssetPath(string path) => new GUID(AssetPathToGUID(path));

        public static bool IsValidFolder(string path)
        {
            _ = Entries;
            path = path?.TrimEnd('/');
            if (path == "Assets" || path == "Packages") return true;
            return path != null && s_ByPath.TryGetValue(path, out var e) && e.Kind == "folder";
        }

        public static string[] GetSubFolders(string path)
        {
            path = path.TrimEnd('/') + "/";
            return Entries.Where(e => e.Kind == "folder" && e.Path.StartsWith(path, StringComparison.OrdinalIgnoreCase) && e.Path.IndexOf('/', path.Length) < 0)
                .Select(e => e.Path).ToArray();
        }

        public static string[] GetAllAssetPaths() => Entries.Select(e => e.Path).ToArray();

        public static Object LoadAssetAtPath(string assetPath, Type type)
        {
            var guid = AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) return null;
            var main = AssetDatabaseRuntime.LoadMain(guid, type ?? typeof(Object));
            if (main != null) return main;
            if (type == null) return null;
            foreach (var o in AssetDatabaseRuntime.LoadAll(guid))
                if (type.IsInstanceOfType(o)) return o;
            return null;
        }

        public static T LoadAssetAtPath<T>(string assetPath) where T : Object => LoadAssetAtPath(assetPath, typeof(T)) as T;

        public static Object LoadMainAssetAtPath(string assetPath) => LoadAssetAtPath(assetPath, typeof(Object));

        public static Object[] LoadAllAssetsAtPath(string assetPath)
        {
            var guid = AssetPathToGUID(assetPath);
            return string.IsNullOrEmpty(guid) ? Array.Empty<Object>() : AssetDatabaseRuntime.LoadAll(guid).ToArray();
        }

        public static Object[] LoadAllAssetRepresentationsAtPath(string assetPath)
        {
            var guid = AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) return Array.Empty<Object>();
            var main = AssetDatabaseRuntime.LoadMain(guid, null);
            return AssetDatabaseRuntime.LoadAll(guid).Where(o => o != main).ToArray();
        }

        public static Type GetMainAssetTypeAtPath(string assetPath) => LoadMainAssetAtPath(assetPath)?.GetType();

        public static string GetAssetPath(Object assetObject)
        {
            if (assetObject == null) return "";
            if (AssetDatabaseRuntime.TryGetLocation(assetObject, out var guid, out _)) return GUIDToAssetPath(guid);
            if (assetObject is Component c && c.m_GameObject != null && c.m_GameObject.m_IsPrefabAsset)
                return GetAssetPath(c.m_GameObject.transform.root.gameObject);
            if (assetObject is GameObject go && go.m_IsPrefabAsset && go.transform.parent != null)
                return GetAssetPath(go.transform.root.gameObject);
            return "";
        }

        public static string GetAssetPath(int instanceID) => GetAssetPath(ObjectRegistry.FindById(instanceID));

        public static bool Contains(Object obj) => obj != null && obj.IsPersistentAsset;

        public static bool Contains(int instanceID) => Contains(ObjectRegistry.FindById(instanceID));

        public static bool IsMainAsset(Object obj) => obj != null && AssetDatabaseRuntime.TryGetLocation(obj, out var guid, out _) && AssetDatabaseRuntime.LoadMain(guid, null) == obj;

        public static bool IsSubAsset(Object obj) => obj != null && obj.IsPersistentAsset && !IsMainAsset(obj);

        public static bool IsNativeAsset(Object obj)
        {
            var path = GetAssetPath(obj);
            return path.EndsWith(".asset") || path.EndsWith(".prefab") || path.EndsWith(".mat") || path.EndsWith(".unity");
        }

        public static bool IsForeignAsset(Object obj) => obj != null && obj.IsPersistentAsset && !IsNativeAsset(obj);

        public static bool TryGetGUIDAndLocalFileIdentifier(Object obj, out string guid, out long localId)
        {
            if (obj != null && AssetDatabaseRuntime.TryGetLocation(obj, out guid, out localId)) return true;
            guid = "";
            localId = 0;
            return false;
        }

        public static bool TryGetGUIDAndLocalFileIdentifier(int instanceID, out string guid, out long localId) =>
            TryGetGUIDAndLocalFileIdentifier(ObjectRegistry.FindById(instanceID), out guid, out localId);

        public static string[] FindAssets(string filter) => FindAssets(filter, null);

        /// <summary>`t:Type` (by asset type or script class, base classes included), `l:label`, and name
        /// words (all must appear in the file name), within the given folders.</summary>
        public static string[] FindAssets(string filter, string[] searchInFolders)
        {
            var types = new List<string>();
            var names = new List<string>();
            var labels = new List<string>();
            foreach (var token in (filter ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith("t:", StringComparison.OrdinalIgnoreCase)) types.Add(token.Substring(2));
                else if (token.StartsWith("l:", StringComparison.OrdinalIgnoreCase)) labels.Add(token.Substring(2));
                else if (token.StartsWith("ref:", StringComparison.OrdinalIgnoreCase) || token.StartsWith("glob:", StringComparison.OrdinalIgnoreCase)) { }
                else names.Add(token);
            }
            var folders = searchInFolders?.Select(f => f.TrimEnd('/') + "/").ToArray();
            var result = new List<string>();
            foreach (var e in Entries)
            {
                if (folders != null && !folders.Any(f => e.Path.StartsWith(f, StringComparison.OrdinalIgnoreCase))) continue;
                var fileName = System.IO.Path.GetFileNameWithoutExtension(e.Path);
                if (names.Any(n => fileName.IndexOf(n, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                if (labels.Count > 0 && (e.Labels == null || !labels.Any(l => e.Labels.Contains(l, StringComparer.OrdinalIgnoreCase)))) continue;
                if (types.Count > 0 && !types.Any(t => MatchesType(e, t))) continue;
                result.Add(e.Guid);
            }
            return result.ToArray();
        }

        static bool MatchesType(Entry e, string type)
        {
            var ext = System.IO.Path.GetExtension(e.Path).ToLowerInvariant();
            switch (type.ToLowerInvariant())
            {
                case "prefab": return ext == ".prefab";
                case "gameobject": return ext == ".prefab" || e.Kind == "model";
                case "scene": case "sceneasset": return ext == ".unity";
                case "texture": case "texture2d": return e.Kind == "texture";
                case "sprite": return e.Kind == "texture";
                case "material": return ext == ".mat";
                case "audioclip": return e.Kind == "audio";
                case "animationclip": return ext == ".anim";
                case "animatorcontroller": case "runtimeanimatorcontroller": return ext == ".controller";
                case "textasset": return e.Kind == "text";
                case "shader": return e.Kind == "shader";
                case "font": return e.Kind == "font";
                case "model": case "mesh": return e.Kind == "model";
                case "monoscript": case "script": return e.Kind == "script";
                case "defaultasset": return e.Kind == "folder" || e.Kind == "unknown";
                case "folder": return e.Kind == "folder";
                case "object": return true;
            }
            if (ext != ".asset") return false;
            // A ScriptableObject asset: compare with its script class and base classes.
            if (!s_ScriptOfAsset.TryGetValue(e.Guid, out var scriptGuid))
            {
                scriptGuid = JsonDocument.Parse(Native.Call("asset-script", JsonSerializer.Serialize(new { guid = e.Guid }))).RootElement.GetProperty("script").GetString();
                s_ScriptOfAsset[e.Guid] = scriptGuid;
            }
            var scriptType = string.IsNullOrEmpty(scriptGuid) ? null : GameDomain.ResolveScript(scriptGuid, 11500000);
            if (type.Equals("ScriptableObject", StringComparison.OrdinalIgnoreCase)) return scriptType != null;
            for (var t = scriptType; t != null; t = t.BaseType)
                if (t.Name.Equals(type, StringComparison.OrdinalIgnoreCase) || (t.FullName ?? "").Equals(type, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string[] GetDependencies(string pathName) => GetDependencies(pathName, true);

        public static string[] GetDependencies(string pathName, bool recursive) => new[] { pathName };

        public static string[] GetDependencies(string[] pathNames) => pathNames;

        public static string[] GetDependencies(string[] pathNames, bool recursive) => pathNames;

        static readonly Dictionary<string, string> s_ScriptOfAsset = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static Entry EntryOf(Object obj)
        {
            var path = obj != null ? GetAssetPath(obj) : null;
            if (string.IsNullOrEmpty(path)) return null;
            _ = Entries;
            return s_ByPath.TryGetValue(path, out var e) ? e : null;
        }

        /// <summary>The labels the asset's .meta lists.</summary>
        public static string[] GetLabels(Object obj) => (string[])(EntryOf(obj)?.Labels ?? Array.Empty<string>()).Clone();

        /// <summary>Changes the labels for this session; Unbound does not write .meta files.</summary>
        public static void SetLabels(Object obj, string[] labels)
        {
            var e = EntryOf(obj);
            if (e != null) e.Labels = labels?.Distinct().ToArray() ?? Array.Empty<string>();
        }

        public static void ClearLabels(Object obj) => SetLabels(obj, Array.Empty<string>());

        // Refresh imports what changed on disk; the asset list follows the project's own scan
        // (Unbound's watcher), so there is nothing to drop here: Unity's Refresh is cheap when
        // nothing changed, and editor code calls it often.
        public static void Refresh() { }

        public static void Refresh(ImportAssetOptions options) { }

        public static void SaveAssets() { }

        public static void SaveAssetIfDirty(Object obj) { }

        public static void SaveAssetIfDirty(GUID guid) { }

        public static void StartAssetEditing() { }

        public static void StopAssetEditing() { }

        public static void ImportAsset(string path) { }

        public static void ImportAsset(string path, ImportAssetOptions options) { }

        public static void ReleaseCachedFileHandles() { }

        public static bool IsOpenForEdit(Object assetObject) => true;

        public static bool IsOpenForEdit(string assetOrMetaFilePath) => true;
    }

    /// <summary>A script asset: its source text and the class it declares.</summary>
    public partial class MonoScript : TextAsset
    {
        internal string m_Guid;

        public MonoScript() { }

        internal MonoScript(byte[] bytes, string name, string guid) : base(bytes, name)
        {
            m_Guid = guid;
        }

        public Type GetClass() => m_Guid != null ? GameDomain.ResolveScript(m_Guid, 11500000) : null;

        public static MonoScript FromMonoBehaviour(MonoBehaviour behaviour) => FromType(behaviour?.GetType());

        public static MonoScript FromScriptableObject(ScriptableObject scriptableObject) => FromType(scriptableObject?.GetType());

        internal static MonoScript FromType(Type type)
        {
            var guid = type != null ? GameDomain.GuidOfScript(type) : null;
            return guid == null ? null : AssetDatabaseRuntime.LoadMain(guid, typeof(MonoScript)) as MonoScript;
        }
    }

    public partial struct GUID : IEquatable<GUID>, IComparable, IComparable<GUID>
    {
        readonly string m_Value;

        public GUID(string hexRepresentation)
        {
            m_Value = (hexRepresentation ?? "").ToLowerInvariant();
        }

        public bool Empty() => string.IsNullOrEmpty(m_Value) || m_Value.Trim('0').Length == 0;

        public bool IsEmpty() => Empty();

        public override string ToString() => string.IsNullOrEmpty(m_Value) ? "00000000000000000000000000000000" : m_Value;

        public override bool Equals(object obj) => obj is GUID other && Equals(other);

        public bool Equals(GUID obj) => ToString() == obj.ToString();

        public override int GetHashCode() => ToString().GetHashCode();

        public int CompareTo(object obj) => obj is GUID other ? CompareTo(other) : 1;

        public int CompareTo(GUID rhs) => string.CompareOrdinal(ToString(), rhs.ToString());

        public static bool operator ==(GUID x, GUID y) => x.Equals(y);

        public static bool operator !=(GUID x, GUID y) => !x.Equals(y);

        public static bool TryParse(string hex, out GUID result)
        {
            result = new GUID(hex);
            return hex != null && hex.Length == 32;
        }

        public static GUID Generate() => new GUID(Guid.NewGuid().ToString("N"));
    }

    public sealed partial class EditorSettings : Object
    {
        public static bool enterPlayModeOptionsEnabled { get; set; }
        public static EnterPlayModeOptions enterPlayModeOptions { get; set; }
    }

    [Flags]
    public enum EnterPlayModeOptions
    {
        None = 0,
        DisableDomainReload = 1,
        DisableSceneReload = 2,
        DisableSceneBackupUnlessDirty = 4,
    }
}
