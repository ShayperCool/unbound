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
            MainThreadCheck.Check("Load");
            if (systemTypeInstance == null) throw new NullReferenceException("The type is null.");
            foreach (var entry in Find(path, folder: false))
            {
                var obj = AssetDatabaseRuntime.LoadMain(entry.Guid, systemTypeInstance);
                if (obj != null) return obj;
                // A sub-asset of the requested type (a sprite of a texture).
                foreach (var sub in AssetDatabaseRuntime.LoadAll(entry.Guid))
                    if (systemTypeInstance.IsInstanceOfType(sub) && !AssetDatabaseRuntime.IsTakePreview(sub)) return sub;
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
                    if (systemTypeInstance.IsInstanceOfType(obj) && !(obj is Component) && !AssetDatabaseRuntime.IsTakePreview(obj)) result.Add(obj);
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

        /// <summary>
        /// As the Editor (measured, tests/projects/droppedparams unload.*): GameObjects, components
        /// and asset bundles are refused with an error, as are objects that are not assets, and
        /// null is ignored. An unloaded material reads its saved state again where it is referenced
        /// (what was set from script is gone); an unloaded ScriptableObject is destroyed and the
        /// next load reads a new one from its file. On 6000.0.71f1 the reloaded material has a new
        /// wrapper of the same object, with shared values (tests/projects/materialalias); Compat
        /// retains the previous unmeasured wrapper behavior for other releases. An unloaded custom render
        /// texture asset stops updating and frees its GPU data (tests/projects/customrt).
        /// Not measured, done as for materials (Scripting API 6000.0 UnloadUnusedAssets: assets other
        /// than ScriptableObjects "are loaded back the next time one of its properties or methods
        /// is used"): an imported Texture2D or AudioClip is imported again, a mesh reads its
        /// document again and an imported TextAsset reads its file again at its next use, each
        /// staying the same object with what scripts changed gone
        /// (<see cref="AssetDatabaseRuntime.Unload"/>). Other kinds, and materials and
        /// ScriptableObjects sharing their file, are not unloaded (warns).
        /// </summary>
        public static void UnloadAsset(Object assetToUnload)
        {
            if (assetToUnload == null) return;
            if (assetToUnload is GameObject || assetToUnload is Component || assetToUnload is AssetBundle)
            {
                Debug.LogError("UnloadAsset may only be used on individual assets and can not be used on GameObject's / Components / AssetBundles or GameManagers");
                return;
            }
            if (!assetToUnload.IsPersistentAsset)
            {
                Debug.LogError("UnloadAsset can only be used on assets;");
                return;
            }
            // A custom render texture asset stops updating (its unload event fires) and frees its
            // GPU data. Runtime ones were refused above, by the Editor's generic non-asset check.
            if (assetToUnload is CustomRenderTexture crt) { crt.UnloadAsset(); return; }
            if (!Unbound.Runtime.AssetDatabaseRuntime.Unload(assetToUnload))
                Unbound.Runtime.Stubs.Ignored("Resources.UnloadAsset(" + assetToUnload.GetType().Name + ")",
                    "does not unload this kind of asset yet (materials and ScriptableObjects alone in their file, imported textures, audio clips and text assets, and meshes are)");
        }

        /// <summary>
        /// Unloads the loaded assets the engine does not see in use
        /// (<see cref="AssetDatabaseRuntime.UnloadUnused"/>: references from scene, DontDestroyOnLoad
        /// and run-time objects, the engine's scene settings (skybox, lightmaps, shader globals) and
        /// the statics it can read; not the stack, nor statics of generic classes or of classes with
        /// class initialization, which reading could run early), where it can load them back lazily
        /// at their next use,
        /// as the Scripting API 6000.0 says: imported text assets free their bytes. Materials,
        /// meshes, textures, audio clips and ScriptableObjects are kept, with what scripts changed,
        /// and nothing already unloaded is processed again. The operation is complete when it
        /// returns, as before.
        /// Scene loads and EditorUtility.UnloadUnusedAssetsImmediate still only compact the object
        /// registry (<see cref="UnloadUnusedAssetsInternal"/>).
        /// </summary>
        public static AsyncOperation UnloadUnusedAssets()
        {
            UnloadUnusedAssetsInternal();
            AssetDatabaseRuntime.UnloadUnused();
            return AsyncOperation.Completed();
        }

        internal static void UnloadUnusedAssetsInternal() => ObjectRegistry.Compact();

        public static Object[] FindObjectsOfTypeAll(Type type) => ObjectRegistry.TypedArray(type, ObjectRegistry.All(type));

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
        [Unbound.EditorFaithful("Unity returns null too (obsolete as an error)")]
        public static Object LoadAssetAtPath(string assetPath, Type type) => null;
    }

    public partial class ResourceRequest : AsyncOperation
    {
        internal string m_Path;
        internal Type m_Type;
        internal Object m_Asset;

        public Object asset => GetResult();

        protected virtual Object GetResult() => m_Asset;
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

    /// <summary>
    /// Unity's built-in resources by the path each object of the install's resource files is
    /// registered under: Resources.GetBuiltinResource looks in `unity default resources`,
    /// AssetDatabase.GetBuiltinExtraResource in `unity_builtin_extra` (the release profile's
    /// tables, filled by the host). As the Unity 6000.0.71f1 Editor (tests/projects/builtinresources):
    /// a path matches case-sensitively, and only together with the object's own class (the
    /// requested type's engine class: a base class such as Object finds nothing, a script class
    /// finds its MonoBehaviour object when that object is an instance of it); a path that is not
    /// registered with that class logs an assertion and an error and answers null; a null or empty
    /// path throws; "Arial.ttf" is looked up as "LegacyRuntime.ttf" and then throws. Every call
    /// answers the same object.
    /// </summary>
    internal static class BuiltinResources
    {
        /// <summary>(path, class id) → file id in `unity default resources`.</summary>
        internal static readonly Dictionary<(string, int), long> DefaultPaths = new Dictionary<(string, int), long>();
        /// <summary>(path, class id) → file id in `unity_builtin_extra`.</summary>
        internal static readonly Dictionary<(string, int), long> ExtraPaths = new Dictionary<(string, int), long>();

        const string RemovedFont = "Arial.ttf";
        const string RemovedFontReplacement = "LegacyRuntime.ttf";

        internal static UnityEngine.Object Get(Type type, string path) => Lookup(type, path, extra: false);

        internal static UnityEngine.Object GetExtra(Type type, string path) => Lookup(type, path, extra: true);

        static UnityEngine.Object Lookup(Type type, string path, bool extra)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Invalid path");
            bool removedFont = path == RemovedFont;
            if (removedFont) path = RemovedFontReplacement;
            var found = Find(type, path, extra);
            if (removedFont)
                throw new ArgumentException($"{RemovedFont} is no longer a valid built in font. Please use {RemovedFontReplacement}");
            return found;
        }

        static UnityEngine.Object Find(Type type, string path, bool extra)
        {
            var paths = extra ? ExtraPaths : DefaultPaths;
            int classId = type == null ? -1 : EngineTypes.EngineClassIdOf(type);
            if (!paths.TryGetValue((path, classId), out var fileId))
            {
                UnityEngine.Debug.LogAssertion($"Failed to find {path}");
                UnityEngine.Debug.LogError($"The resource {path} could not be loaded from the resource file!");
                return null;
            }
            var found = BuiltinShaders.Resolve(extra ? BuiltinShaders.ExtraGuid : BuiltinShaders.DefaultGuid, fileId);
            if (found == null)
            {
                UnityEngine.Debug.LogError($"The resource {path} could not be loaded from the resource file!");
                return null;
            }
            // A script class's MonoBehaviour object answers only a type it is an instance of.
            return type.IsInstanceOfType(found) ? found : null;
        }
    }
}
