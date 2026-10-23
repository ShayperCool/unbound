using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>Assets of the project's `Resources` folders, by path relative to such a folder and
    /// without extension.</summary>
    public partial class Resources
    {
        static readonly JsonSerializerOptions s_Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        sealed class Entry
        {
            public string Guid { get; set; }
            public string Path { get; set; }
        }

        /// <summary>Assets whose Resources path is `path` (several when only extensions differ),
        /// or, with `folder`, every asset below it.</summary>
        static List<Entry> Find(string path, bool folder)
        {
            path = (path ?? "").Replace('\\', '/').Trim('/');
            var text = Native.Call("resources-find", JsonSerializer.Serialize(new { path, folder }, s_Json));
            return JsonSerializer.Deserialize<List<Entry>>(text, s_Json) ?? new List<Entry>();
        }

        public static Object Load(string path) => Load(path, typeof(Object));

        public static T Load<T>(string path) where T : Object => (T)Load(path, typeof(T));

        public static Object Load(string path, Type systemTypeInstance)
        {
            if (systemTypeInstance == null) throw new NullReferenceException("The type is null.");
            foreach (var entry in Find(path, folder: false))
            {
                var obj = AssetDatabaseRuntime.LoadMain(entry.Guid, systemTypeInstance);
                if (obj != null) return obj;
                // A sub-asset of the requested type (a sprite of a texture).
                foreach (var sub in AssetDatabaseRuntime.LoadAll(entry.Guid))
                    if (systemTypeInstance.IsInstanceOfType(sub)) return sub;
            }
            return null;
        }

        public static ResourceRequest LoadAsync(string path) => LoadAsync(path, typeof(Object));

        public static ResourceRequest LoadAsync<T>(string path) where T : Object => LoadAsync(path, typeof(T));

        public static ResourceRequest LoadAsync(string path, Type type)
        {
            var request = new ResourceRequest { m_Path = path, m_Type = type };
            request.m_Asset = Load(path, type);
            AsyncOperations.CompleteNextFrame(request);
            return request;
        }

        public static Object[] LoadAll(string path, Type systemTypeInstance)
        {
            var result = new List<Object>();
            foreach (var entry in Find(path, folder: true))
                foreach (var obj in AssetDatabaseRuntime.LoadAll(entry.Guid))
                    if (systemTypeInstance.IsInstanceOfType(obj) && !(obj is Component)) result.Add(obj);
            return result.ToArray();
        }

        public static Object[] LoadAll(string path) => LoadAll(path, typeof(Object));

        public static T[] LoadAll<T>(string path) where T : Object
        {
            var all = LoadAll(path, typeof(T));
            var result = new T[all.Length];
            for (int i = 0; i < all.Length; i++) result[i] = (T)all[i];
            return result;
        }

        public static Object GetBuiltinResource(Type type, string path) => BuiltinResources.Get(type, path);

        public static T GetBuiltinResource<T>(string path) where T : Object => (T)GetBuiltinResource(typeof(T), path);

        public static void UnloadAsset(Object assetToUnload) { }

        public static AsyncOperation UnloadUnusedAssets()
        {
            UnloadUnusedAssetsInternal();
            return AsyncOperation.Completed();
        }

        internal static void UnloadUnusedAssetsInternal() => ObjectRegistry.Compact();

        public static Object[] FindObjectsOfTypeAll(Type type) => ObjectRegistry.All(type).ToArray();

        public static T[] FindObjectsOfTypeAll<T>() where T : Object
        {
            var all = ObjectRegistry.All(typeof(T));
            var result = new T[all.Count];
            for (int i = 0; i < all.Count; i++) result[i] = (T)all[i];
            return result;
        }

        public static Object InstanceIDToObject(int instanceID) => ObjectRegistry.FindById(instanceID);

        public static bool InstanceIDIsValid(int instanceId) => ObjectRegistry.FindById(instanceId) != null;

        [Obsolete("Use AssetDatabase.LoadAssetAtPath instead (UnityUpgradable) -> * [UnityEditor] UnityEditor.AssetDatabase.LoadAssetAtPath(*)", true)]
        public static Object LoadAssetAtPath(string assetPath, Type type) => null;
    }

    public partial class ResourceRequest : AsyncOperation
    {
        internal string m_Path;
        internal Type m_Type;
        internal Object m_Asset;

        public Object asset => m_Asset;
    }
}

namespace Unbound.Runtime
{
    /// <summary>Operations that finish on the next frame, as Unity's asynchronous loads do at best.</summary>
    internal static class AsyncOperations
    {
        static readonly List<UnityEngine.AsyncOperation> s_Pending = new List<UnityEngine.AsyncOperation>();

        static AsyncOperations()
        {
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.EarlyUpdate.UpdatePreloading), Run);
        }

        internal static void CompleteNextFrame(UnityEngine.AsyncOperation op)
        {
            op.m_Progress = 0.9f;
            s_Pending.Add(op);
        }

        static void Run()
        {
            if (s_Pending.Count == 0) return;
            var batch = s_Pending.ToArray();
            s_Pending.Clear();
            foreach (var op in batch) op.Complete();
        }
    }

    /// <summary>Unity's built-in resources (Resources.GetBuiltinResource, `{fileID, guid 0000...e000/f000}`).</summary>
    internal static class BuiltinResources
    {
        internal static readonly Dictionary<string, Func<UnityEngine.Object>> Factories = new Dictionary<string, Func<UnityEngine.Object>>();
        static readonly Dictionary<string, UnityEngine.Object> s_Cache = new Dictionary<string, UnityEngine.Object>();

        internal static UnityEngine.Object Get(Type type, string path)
        {
            var key = $"{type.Name}:{path}";
            if (s_Cache.TryGetValue(key, out var cached)) return cached;
            if (!Factories.TryGetValue(key, out var factory) && type == typeof(UnityEngine.Mesh) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                // The primitives: "Cube.fbx", "Sphere.fbx", ...
                var mesh = UnityEngine.BuiltinMeshes.Create(path.Substring(0, path.Length - 4));
                if (mesh != null)
                {
                    mesh.IsPersistentAsset = true;
                    return s_Cache[key] = mesh;
                }
            }
            if (factory == null)
            {
                UnityEngine.Debug.LogWarning($"Unbound: built-in resource {path} ({type.Name}) is not available.");
                return null;
            }
            return s_Cache[key] = factory();
        }
    }
}
