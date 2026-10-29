using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// An AssetBundle the Editor built (UnityFS: uncompressed, LZ4 or LZMA): its archive stays open on
    /// the native side; its AssetBundle object lists its assets (by lowercase path) or scenes. The
    /// objects of its serialized files are made when an asset is first loaded, so references into
    /// bundles loaded after it (its dependencies) resolve, as in Unity.
    /// </summary>
    public partial class AssetBundle : Object
    {
        internal ulong m_NativeId;
        internal readonly List<string> m_Files = new List<string>();
        internal readonly List<(string Name, int PreloadIndex, int PreloadSize, long Asset)> m_Container = new List<(string, int, int, long)>();
        internal readonly List<long> m_PreloadTable = new List<long>();
        internal string m_AssetFile;
        internal bool m_IsStreamedScene;
        internal readonly List<(string Path, string Key)> m_Scenes = new List<(string, string)>();
        static readonly List<AssetBundle> s_Loaded = new List<AssetBundle>();

        public AssetBundle() { }

        public bool isStreamedSceneAssetBundle => m_IsStreamedScene;

        public static uint memoryBudgetKB { get; set; }

        public static IEnumerable<AssetBundle> GetAllLoadedAssetBundles() => s_Loaded.ToArray();

        internal static AssetBundle[] GetAllLoadedAssetBundles_Native() => s_Loaded.ToArray();

        public static void UnloadAllAssetBundles(bool unloadAllObjects)
        {
            foreach (var bundle in s_Loaded.ToArray()) bundle.Unload(unloadAllObjects);
        }

        // ------------------------------------------------------------ loading

        public static AssetBundle LoadFromFile(string path) => LoadFromFile(path, 0, 0);
        public static AssetBundle LoadFromFile(string path, uint crc) => LoadFromFile(path, crc, 0);
        public static AssetBundle LoadFromFile(string path, uint crc, ulong offset) => LoadFromFile_Internal(path, crc, offset);

        internal static AssetBundle LoadFromFile_Internal(string path, uint crc, ulong offset)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogError($"Unable to open archive file: {path}");
                return null;
            }
            return Open(JsonSerializer.Serialize(new { path = Path.GetFullPath(path), offset }), path);
        }

        public static AssetBundle LoadFromMemory(byte[] binary) => LoadFromMemory(binary, 0);
        public static AssetBundle LoadFromMemory(byte[] binary, uint crc) => LoadFromMemory_Internal(binary, crc);

        internal static AssetBundle LoadFromMemory_Internal(byte[] binary, uint crc)
        {
            if (binary == null) throw new NullReferenceException();
            return Open(JsonSerializer.Serialize(new { data = Convert.ToBase64String(binary) }), "<memory>");
        }

        public static AssetBundle LoadFromStream(Stream stream) => LoadFromStream(stream, 0, 0);
        public static AssetBundle LoadFromStream(Stream stream, uint crc) => LoadFromStream(stream, crc, 0);
        public static AssetBundle LoadFromStream(Stream stream, uint crc, uint managedReadBufferSize) => LoadFromStreamInternal(stream, crc, managedReadBufferSize);

        internal static void ValidateLoadFromStream(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream), "ManagedStream object must be non-null");
            if (!stream.CanRead) throw new ArgumentException("ManagedStream object must be readable (stream.CanRead must return true)");
            if (!stream.CanSeek) throw new ArgumentException("ManagedStream object must be seekable (stream.CanSeek must return true)");
        }

        internal static AssetBundle LoadFromStreamInternal(Stream stream, uint crc, uint managedReadBufferSize)
        {
            ValidateLoadFromStream(stream);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return LoadFromMemory(memory.ToArray(), crc);
        }

        public static AssetBundleCreateRequest LoadFromFileAsync(string path) => LoadFromFileAsync(path, 0, 0);
        public static AssetBundleCreateRequest LoadFromFileAsync(string path, uint crc) => LoadFromFileAsync(path, crc, 0);
        public static AssetBundleCreateRequest LoadFromFileAsync(string path, uint crc, ulong offset) => LoadFromFileAsync_Internal(path, crc, offset);
        internal static AssetBundleCreateRequest LoadFromFileAsync_Internal(string path, uint crc, ulong offset) => Request(LoadFromFile(path, crc, offset));

        public static AssetBundleCreateRequest LoadFromMemoryAsync(byte[] binary) => LoadFromMemoryAsync(binary, 0);
        public static AssetBundleCreateRequest LoadFromMemoryAsync(byte[] binary, uint crc) => LoadFromMemoryAsync_Internal(binary, crc);
        internal static AssetBundleCreateRequest LoadFromMemoryAsync_Internal(byte[] binary, uint crc) => Request(LoadFromMemory(binary, crc));

        public static AssetBundleCreateRequest LoadFromStreamAsync(Stream stream) => LoadFromStreamAsync(stream, 0, 0);
        public static AssetBundleCreateRequest LoadFromStreamAsync(Stream stream, uint crc) => LoadFromStreamAsync(stream, crc, 0);
        public static AssetBundleCreateRequest LoadFromStreamAsync(Stream stream, uint crc, uint managedReadBufferSize) => LoadFromStreamAsyncInternal(stream, crc, managedReadBufferSize);
        internal static AssetBundleCreateRequest LoadFromStreamAsyncInternal(Stream stream, uint crc, uint managedReadBufferSize) => Request(LoadFromStream(stream, crc, managedReadBufferSize));

        static AssetBundleCreateRequest Request(AssetBundle bundle)
        {
            var request = new AssetBundleCreateRequest { m_AssetBundle = bundle };
            AsyncOperations.CompleteNextFrame(request);
            return request;
        }

        [Obsolete("Method CreateFromFile has been renamed to LoadFromFile (UnityUpgradable) -> LoadFromFile(*)")]
        public static AssetBundle CreateFromFile(string path) => LoadFromFile(path);

        [Obsolete("Method CreateFromMemory has been renamed to LoadFromMemoryAsync (UnityUpgradable) -> LoadFromMemoryAsync(*)")]
        public static AssetBundleCreateRequest CreateFromMemory(byte[] binary) => LoadFromMemoryAsync(binary);

        [Obsolete("Method CreateFromMemoryImmediate has been renamed to LoadFromMemory (UnityUpgradable) -> LoadFromMemory(*)")]
        public static AssetBundle CreateFromMemoryImmediate(byte[] binary) => LoadFromMemory(binary);

        /// <summary>Opens an archive on the native side and reads its AssetBundle object.</summary>
        static AssetBundle Open(string request, string description)
        {
            JsonElement info;
            try
            {
                info = JsonDocument.Parse(Native.Call("bundle-open", request)).RootElement;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to read data for the AssetBundle '{description}'. ({e.Message})");
                return null;
            }
            ulong id = info.GetProperty("id").GetUInt64();
            var bundle = new AssetBundle { m_NativeId = id };
            foreach (var f in info.GetProperty("files").EnumerateArray())
                if (f.GetProperty("serialized").GetBoolean()) bundle.m_Files.Add(f.GetProperty("path").GetString());
            // Another bundle of the same files is loaded: Unity refuses.
            foreach (var other in s_Loaded)
                if (other.m_Files.Intersect(bundle.m_Files).Any())
                {
                    Native.Call("bundle-close", JsonSerializer.Serialize(new { id }));
                    Debug.LogError($"The AssetBundle '{description}' can't be loaded because another AssetBundle with the same files is already loaded.");
                    return null;
                }
            try
            {
                bundle.ReadContents();
            }
            catch (Exception e)
            {
                Native.Call("bundle-close", JsonSerializer.Serialize(new { id }));
                Debug.LogError($"Failed to read the AssetBundle '{description}': {e.Message}");
                return null;
            }
            s_Loaded.Add(bundle);
            return bundle;
        }

        /// <summary>The AssetBundle object (the files' class 142 document): the container, the
        /// preload table, whether it holds scenes; the scripts the files name.</summary>
        void ReadContents()
        {
            foreach (var file in m_Files)
            {
                // A built scene's documents load with the scene (its scripts are its shared assets').
                bool isScene = !file.EndsWith(".sharedAssets", StringComparison.Ordinal) && file.StartsWith("BuildPlayer-", StringComparison.Ordinal);
                if (isScene) continue;
                var documents = BundleFiles.Documents(this, file);
                BundleFiles.RegisterScripts(file, documents);
                BundleFiles.Register(this, file);
                foreach (var doc in documents)
                {
                    if (doc.ClassId != 142) continue;
                    var body = doc.Body;
                    m_Name = body.Str("m_Name");
                    if (string.IsNullOrEmpty(m_Name)) m_Name = body.Str("m_AssetBundleName");
                    m_IsStreamedScene = body.Bool("m_IsStreamedSceneAssetBundle");
                    m_AssetFile = file;
                    foreach (var p in body.Get("m_PreloadTable")?.Seq() ?? Array.Empty<YNode>())
                        m_PreloadTable.Add(p.Long("fileID"));
                    foreach (var entry in body.Get("m_Container")?.Seq() ?? Array.Empty<YNode>())
                    {
                        var second = entry.Get("second");
                        m_Container.Add((entry.Str("first"), second?.Int("preloadIndex") ?? 0, second?.Int("preloadSize") ?? 0, second?.Get("asset")?.Long("fileID") ?? 0));
                    }
                }
            }
            if (m_IsStreamedScene)
            {
                foreach (var file in m_Files)
                {
                    if (!file.StartsWith("BuildPlayer-", StringComparison.Ordinal) || file.EndsWith(".sharedAssets", StringComparison.Ordinal)) continue;
                    string sceneName = file.Substring("BuildPlayer-".Length);
                    string path = m_Container.Select(c => c.Name).FirstOrDefault(n => string.Equals(Path.GetFileNameWithoutExtension(n), sceneName, StringComparison.OrdinalIgnoreCase)) ?? sceneName;
                    m_Scenes.Add((path, $"bundle:{m_NativeId}/{file}"));
                }
            }
        }

        // ------------------------------------------------------------ assets

        Object Resolve(long fileId) => m_AssetFile == null || fileId == 0 ? null : BundleFiles.Load("bundle:" + m_AssetFile, fileId);

        static bool Matches(string key, string name)
        {
            var lower = name.ToLowerInvariant();
            if (key == lower || key.EndsWith("/" + lower, StringComparison.Ordinal)) return true;
            return string.Equals(Path.GetFileNameWithoutExtension(key), name, StringComparison.OrdinalIgnoreCase);
        }

        static Object Typed(Object o, Type type)
        {
            if (o == null) return null;
            if (type == null || type.IsInstanceOfType(o)) return o;
            if (o is GameObject go && typeof(Component).IsAssignableFrom(type)) return go.GetComponent(type);
            return null;
        }

        void ThrowIfUnloaded()
        {
            if (m_NativeId == 0) throw new NullReferenceException("This AssetBundle has been unloaded.");
        }

        static void CheckName(string name)
        {
            if (name == null) throw new NullReferenceException("The input asset name cannot be null.");
            if (name.Length == 0) throw new ArgumentException("The input asset name cannot be empty.");
        }

        public bool Contains(string name) => m_Container.Any(c => Matches(c.Name, name));

        public Object LoadAsset(string name) => LoadAsset(name, typeof(Object));
        public T LoadAsset<T>(string name) where T : Object => (T)LoadAsset(name, typeof(T));

        public Object LoadAsset(string name, Type type)
        {
            CheckName(name);
            if (type == null) throw new NullReferenceException("The input type cannot be null.");
            ThrowIfUnloaded();
            foreach (var entry in m_Container)
            {
                if (!Matches(entry.Name, name)) continue;
                var found = Typed(Resolve(entry.Asset), type);
                if (found != null) return found;
                // A sub-asset of the entry (a texture's sprite).
                for (int i = entry.PreloadIndex; i < entry.PreloadIndex + entry.PreloadSize && i < m_PreloadTable.Count; i++)
                {
                    found = Typed(Resolve(m_PreloadTable[i]), type);
                    if (found != null && found.m_Name == Resolve(entry.Asset)?.m_Name) return found;
                }
            }
            return null;
        }

        public Object[] LoadAssetWithSubAssets(string name) => LoadAssetWithSubAssets(name, typeof(Object));
        public T[] LoadAssetWithSubAssets<T>(string name) where T : Object => ConvertObjects<T>(LoadAssetWithSubAssets(name, typeof(T)));

        public Object[] LoadAssetWithSubAssets(string name, Type type) => LoadAssetWithSubAssets_Internal(name, type);

        internal Object[] LoadAssetWithSubAssets_Internal(string name, Type type)
        {
            CheckName(name);
            ThrowIfUnloaded();
            var result = new List<Object>();
            foreach (var entry in m_Container)
            {
                if (!Matches(entry.Name, name)) continue;
                var o = Typed(Resolve(entry.Asset), type);
                if (o != null && !result.Contains(o)) result.Add(o);
            }
            return result.ToArray();
        }

        internal static T[] ConvertObjects<T>(Object[] rawObjects) where T : Object
        {
            if (rawObjects == null) return null;
            var typed = new T[rawObjects.Length];
            for (int i = 0; i < typed.Length; i++) typed[i] = (T)rawObjects[i];
            return typed;
        }

        public Object[] LoadAllAssets() => LoadAllAssets(typeof(Object));
        public T[] LoadAllAssets<T>() where T : Object => ConvertObjects<T>(LoadAllAssets(typeof(T)));

        public Object[] LoadAllAssets(Type type)
        {
            if (type == null) throw new NullReferenceException("The input type cannot be null.");
            ThrowIfUnloaded();
            var result = new List<Object>();
            foreach (var entry in m_Container)
            {
                var o = Typed(Resolve(entry.Asset), type);
                if (o != null && !result.Contains(o)) result.Add(o);
            }
            return result.ToArray();
        }

        public AssetBundleRequest LoadAssetAsync(string name) => LoadAssetAsync(name, typeof(Object));
        public AssetBundleRequest LoadAssetAsync<T>(string name) => LoadAssetAsync(name, typeof(T));
        public AssetBundleRequest LoadAssetAsync(string name, Type type) => AssetRequest(() => new[] { LoadAsset(name, type) });

        public AssetBundleRequest LoadAssetWithSubAssetsAsync(string name) => LoadAssetWithSubAssetsAsync(name, typeof(Object));
        public AssetBundleRequest LoadAssetWithSubAssetsAsync<T>(string name) => LoadAssetWithSubAssetsAsync(name, typeof(T));
        public AssetBundleRequest LoadAssetWithSubAssetsAsync(string name, Type type) => AssetRequest(() => LoadAssetWithSubAssets(name, type));

        public AssetBundleRequest LoadAllAssetsAsync() => LoadAllAssetsAsync(typeof(Object));
        public AssetBundleRequest LoadAllAssetsAsync<T>() => LoadAllAssetsAsync(typeof(T));
        public AssetBundleRequest LoadAllAssetsAsync(Type type) => AssetRequest(() => LoadAllAssets(type));

        static AssetBundleRequest AssetRequest(Func<Object[]> load)
        {
            var objects = load() ?? Array.Empty<Object>();
            var request = new AssetBundleRequest { m_AllAssets = objects, m_Asset = objects.Length > 0 ? objects[0] : null };
            AsyncOperations.CompleteNextFrame(request);
            return request;
        }

        [Obsolete("Method Load has been deprecated. Script updater cannot update it as the loading behaviour has changed. Please use LoadAsset instead and check the documentation for details.")]
        [Unbound.EditorFaithful("obsolete: Unity's body answers null (bundles since 5.0 have no main asset)")]
        public Object Load(string name) => null;

        [Obsolete("Method Load has been deprecated. Script updater cannot update it as the loading behaviour has changed. Please use LoadAsset instead and check the documentation for details.")]
        [Unbound.EditorFaithful("obsolete: Unity's body answers null (bundles since 5.0 have no main asset)")]
        public Object Load<T>(string name) => null;

        [Obsolete("Method LoadAll has been deprecated. Script updater cannot update it as the loading behaviour has changed. Please use LoadAllAssets instead and check the documentation for details.")]
        [Unbound.EditorFaithful("obsolete: Unity's body answers null (bundles since 5.0 have no main asset)")]
        public T[] LoadAll<T>() where T : Object => null;

        [Obsolete("Method LoadAll has been deprecated. Script updater cannot update it as the loading behaviour has changed. Please use LoadAllAssets instead and check the documentation for details.")]
        [Unbound.EditorFaithful("obsolete: Unity's body answers null (bundles since 5.0 have no main asset)")]
        public Object[] LoadAll() => null;

        [Obsolete("mainAsset has been made obsolete. Please use the new AssetBundle build system introduced in 5.0 and check BuildAssetBundles documentation for details.")]
        [Unbound.EditorFaithful("obsolete: Unity's body answers null (bundles since 5.0 have no main asset)")]
        public Object mainAsset => null;

        internal static Object returnMainAsset(AssetBundle bundle) => null;

        [Obsolete("This method is deprecated.Use GetAllAssetNames() instead.")]
        public string[] AllAssetNames() => GetAllAssetNames();

        public string[] GetAllAssetNames() => m_IsStreamedScene ? Array.Empty<string>() : m_Container.Select(c => c.Name).Distinct().ToArray();

        public string[] GetAllScenePaths() => m_IsStreamedScene ? m_Scenes.Select(s => s.Path).ToArray() : Array.Empty<string>();

        /// <summary>A loaded bundle's scene by name or path, as LoadScene accepts it: its path and the
        /// key its documents load by.</summary>
        internal static bool FindScene(string sceneName, out string path, out string key)
        {
            foreach (var bundle in s_Loaded)
                foreach (var (p, k) in bundle.m_Scenes)
                {
                    var withoutExtension = p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ? p.Substring(0, p.Length - 6) : p;
                    if (string.Equals(p, sceneName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(withoutExtension, sceneName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(Path.GetFileNameWithoutExtension(p), sceneName, StringComparison.OrdinalIgnoreCase)
                        || withoutExtension.EndsWith("/" + sceneName, StringComparison.OrdinalIgnoreCase))
                    {
                        path = p;
                        key = k;
                        return true;
                    }
                }
            path = key = null;
            return false;
        }

        // ------------------------------------------------------------ unloading

        public void Unload(bool unloadAllLoadedObjects)
        {
            if (m_NativeId == 0) return;
            s_Loaded.Remove(this);
            foreach (var file in m_Files) BundleFiles.Unregister(file, unloadAllLoadedObjects);
            try { Native.Call("bundle-close", JsonSerializer.Serialize(new { id = m_NativeId })); }
            catch (Exception) { }
            m_NativeId = 0;
            DestroyImmediate(this);
        }

        public AssetBundleUnloadOperation UnloadAsync(bool unloadAllLoadedObjects)
        {
            Unload(unloadAllLoadedObjects);
            var op = new AssetBundleUnloadOperation();
            AsyncOperations.CompleteNextFrame(op);
            return op;
        }

        public static AssetBundleRecompressOperation RecompressAssetBundleAsync(string inputPath, string outputPath, BuildCompression method, uint expectedCRC = 0, ThreadPriority priority = ThreadPriority.Low) =>
            RecompressAssetBundleAsync_Internal(inputPath, outputPath, method, expectedCRC, priority);

        internal static AssetBundleRecompressOperation RecompressAssetBundleAsync_Internal(string inputPath, string outputPath, BuildCompression method, uint expectedCRC, ThreadPriority priority)
        {
            Debug.LogWarning("Unbound: AssetBundle.RecompressAssetBundleAsync is not supported; the bundle is left as it is.");
            var op = new AssetBundleRecompressOperation { m_InputPath = inputPath, m_OutputPath = outputPath, m_Result = AssetBundleLoadResult.FailedWrite };
            AsyncOperations.CompleteNextFrame(op);
            return op;
        }
    }

    public partial class AssetBundleCreateRequest : AsyncOperation
    {
        internal AssetBundle m_AssetBundle;

        public AssetBundleCreateRequest() { }

        public AssetBundle assetBundle => m_AssetBundle;

        internal void DisableCompatibilityChecks() { }
    }

    public partial class AssetBundleRequest : ResourceRequest
    {
        internal Object[] m_AllAssets = Array.Empty<Object>();

        public AssetBundleRequest() { }

        protected override Object GetResult() => m_Asset;

        public new Object asset => GetResult();

        public Object[] allAssets => m_AllAssets;
    }

    public partial class AssetBundleUnloadOperation : AsyncOperation
    {
        public AssetBundleUnloadOperation() { }

        public void WaitForCompletion() => Complete();
    }

    public partial class AssetBundleRecompressOperation : AsyncOperation
    {
        internal string m_InputPath, m_OutputPath;
        internal AssetBundleLoadResult m_Result;

        public AssetBundleRecompressOperation() { }

        public string humanReadableResult => m_Result.ToString();
        public string inputPath => m_InputPath;
        public string outputPath => m_OutputPath;
        public AssetBundleLoadResult result => m_Result;
        public bool success => m_Result == AssetBundleLoadResult.Success;
    }

    /// <summary>A bundle build's manifest: the bundles, their hashes and dependencies.</summary>
    [ClassId(290)]
    public partial class AssetBundleManifest : Object, ISerializedAsset
    {
        readonly SortedDictionary<int, string> m_Names = new SortedDictionary<int, string>();
        readonly Dictionary<int, (Hash128 Hash, int[] Dependencies)> m_Infos = new Dictionary<int, (Hash128, int[])>();
        readonly List<string> m_WithVariant = new List<string>();

        internal AssetBundleManifest() { }

        int IndexOf(string name)
        {
            foreach (var pair in m_Names)
                if (pair.Value == name) return pair.Key;
            return -1;
        }

        public string[] GetAllAssetBundles() => m_Names.Values.ToArray();

        public string[] GetAllAssetBundlesWithVariant() => m_WithVariant.ToArray();

        public Hash128 GetAssetBundleHash(string assetBundleName) =>
            m_Infos.TryGetValue(IndexOf(assetBundleName), out var info) ? info.Hash : default;

        public string[] GetDirectDependencies(string assetBundleName) =>
            m_Infos.TryGetValue(IndexOf(assetBundleName), out var info) ? info.Dependencies.Select(d => m_Names.TryGetValue(d, out var n) ? n : "").ToArray() : Array.Empty<string>();

        public string[] GetAllDependencies(string assetBundleName)
        {
            var result = new List<string>();
            var pending = new Queue<int>();
            if (m_Infos.TryGetValue(IndexOf(assetBundleName), out var start))
                foreach (var d in start.Dependencies) pending.Enqueue(d);
            while (pending.Count > 0)
            {
                int d = pending.Dequeue();
                if (!m_Names.TryGetValue(d, out var name) || result.Contains(name) || name == assetBundleName) continue;
                result.Add(name);
                if (m_Infos.TryGetValue(d, out var info))
                    foreach (var next in info.Dependencies) pending.Enqueue(next);
            }
            return result.ToArray();
        }

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            foreach (var pair in body.Get("AssetBundleNames")?.Seq() ?? Array.Empty<YNode>())
                m_Names[pair.Int("first")] = pair.Str("second");
            foreach (var name in body.Get("AssetBundlesWithVariant")?.Seq() ?? Array.Empty<YNode>())
                m_WithVariant.Add(name.AsString());
            foreach (var pair in body.Get("AssetBundleInfos")?.Seq() ?? Array.Empty<YNode>())
            {
                var info = pair.Get("second");
                var bytes = new byte[16];
                var hash = info?.Get("AssetBundleHash");
                for (int i = 0; i < 16; i++) bytes[i] = (byte)(hash?.Int($"bytes[{i}]") ?? 0);
                var dependencies = new List<int>();
                foreach (var d in info?.Get("AssetBundleDependencies")?.Seq() ?? Array.Empty<YNode>()) dependencies.Add(d.AsInt());
                m_Infos[pair.Int("first")] = (new Hash128(BitConverter.ToUInt32(bytes, 0), BitConverter.ToUInt32(bytes, 4), BitConverter.ToUInt32(bytes, 8), BitConverter.ToUInt32(bytes, 12)), dependencies.ToArray());
            }
        }
    }
}

namespace Unbound.Runtime
{
    /// <summary>The serialized files of the loaded bundles by their pseudo GUID (`bundle:&lt;file&gt;`,
    /// what references into them name): their documents, their objects once made, the script
    /// classes their MonoScripts name.</summary>
    internal static class BundleFiles
    {
        sealed class Entry
        {
            internal UnityEngine.AssetBundle Bundle;
            internal string File;
            internal List<YDocument> Documents;
            internal LoadedFile Loaded;
        }

        static readonly Dictionary<string, Entry> s_Files = new Dictionary<string, Entry>(StringComparer.Ordinal);
        static readonly Dictionary<(string File, long Id), Type> s_Scripts = new Dictionary<(string, long), Type>();
        /// <summary>The parsed documents of bundle files, held weakly: a bundle's whole tree is
        /// needed to read its header and to build its objects, not afterwards (thousands of loaded
        /// bundles would otherwise keep their YAML alive, hundreds of MB); a collected tree is
        /// parsed again when asked for.</summary>
        static readonly Dictionary<(UnityEngine.AssetBundle, string), WeakReference<List<YDocument>>> s_Documents = new Dictionary<(UnityEngine.AssetBundle, string), WeakReference<List<YDocument>>>();

        internal static List<YDocument> Documents(UnityEngine.AssetBundle bundle, string file)
        {
            if (s_Documents.TryGetValue((bundle, file), out var weak) && weak.TryGetTarget(out var docs)) return docs;
            docs = Native.LoadDocuments($"bundle:{bundle.m_NativeId}/{file}");
            s_Documents[(bundle, file)] = new WeakReference<List<YDocument>>(docs);
            return docs;
        }

        /// <summary>The classes the file's MonoScripts name (by assembly, namespace and class).</summary>
        internal static void RegisterScripts(string file, List<YDocument> documents)
        {
            foreach (var doc in documents)
            {
                if (doc.ClassId != 115) continue;
                var body = doc.Body;
                var type = ScriptType(body.Str("m_AssemblyName"), body.Str("m_Namespace"), body.Str("m_ClassName"));
                if (type != null) s_Scripts[("bundle:" + file, doc.FileId)] = type;
            }
        }

        static Type ScriptType(string assembly, string ns, string name)
        {
            var full = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
            if (assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) assembly = assembly.Substring(0, assembly.Length - 4);
            var found = GameDomain.Find(assembly)?.GetType(full);
            if (found != null) return found;
            found = typeof(UnityEngine.Object).Assembly.GetType(full);
            return found ?? GameDomain.FindTypeByName(full);
        }

        /// <summary>The class a MonoBehaviour's `m_Script` names in a bundle file; null when the
        /// reference is not into a bundle.</summary>
        internal static Type Script(ObjectRef script, string fileGuid)
        {
            string file = script.IsLocal ? fileGuid : script.Guid;
            if (file == null || !file.StartsWith("bundle:", StringComparison.Ordinal)) return null;
            return s_Scripts.TryGetValue((file, script.FileId), out var type) ? type : null;
        }

        internal static bool IsBundleScript(ObjectRef script, string fileGuid)
        {
            string file = script.IsLocal ? fileGuid : script.Guid;
            return file != null && file.StartsWith("bundle:", StringComparison.Ordinal);
        }

        /// <summary>The native id of the bundle a file (`bundle:CAB-…`) belongs to; 0 when it is not a
        /// loaded bundle's.</summary>
        internal static ulong NativeId(string guid) =>
            guid != null && s_Files.TryGetValue(guid, out var entry) ? entry.Bundle.m_NativeId : 0;

        internal static void Register(UnityEngine.AssetBundle bundle, string file) =>
            s_Files["bundle:" + file] = new Entry { Bundle = bundle, File = file };

        /// <summary>An object of a bundle file, its objects made on first use.</summary>
        internal static UnityEngine.Object Load(string guid, long fileId)
        {
            if (!s_Files.TryGetValue(guid, out var entry)) return null;
            if (entry.Loaded == null)
            {
                var docs = Documents(entry.Bundle, entry.File);
                entry.Loaded = ObjectGraphBuilder.Build(docs, entry.File, guid, ObjectGraphBuilder.Kind.Bundle, null, f => entry.Loaded = f);
            }
            return entry.Loaded.Objects.TryGetValue(fileId, out var obj) ? obj : null;
        }

        internal static void Unregister(string file, bool destroyObjects)
        {
            var guid = "bundle:" + file;
            if (!s_Files.TryGetValue(guid, out var entry)) return;
            s_Files.Remove(guid);
            s_Documents.Remove((entry.Bundle, file));
            var keys = s_Scripts.Keys.Where(k => k.File == guid).ToList();
            foreach (var k in keys) s_Scripts.Remove(k);
            if (destroyObjects && entry.Loaded != null)
                foreach (var obj in entry.Loaded.Objects.Values)
                    if (obj != null && !(obj is UnityEngine.Shader) && !(obj is UnityEngine.Component)) UnityEngine.Object.DestroyImmediate(obj, true);
        }
    }
}

namespace UnityEngine
{
    public enum AssetBundleLoadResult
    {
        Success = 0,
        Cancelled = 1,
        NotMatchingCrc = 2,
        FailedCache = 3,
        NotValidAssetBundle = 4,
        NoSerializedData = 5,
        NotCompatible = 6,
        AlreadyLoaded = 7,
        FailedRead = 8,
        FailedDecompression = 9,
        FailedWrite = 10,
        FailedDeleteRecompressionTarget = 11,
        RecompressionTargetIsLoaded = 12,
        RecompressionTargetExistsButNotArchive = 13,
    }

    /// <summary>How a bundle is compressed (the recompression API's argument).</summary>
    public partial struct BuildCompression
    {
        CompressionType m_Compression;
        CompressionLevel m_Level;
        uint m_BlockSize;

        BuildCompression(CompressionType compression, CompressionLevel level, uint blockSize)
        {
            m_Compression = compression;
            m_Level = level;
            m_BlockSize = blockSize;
        }

        public static readonly BuildCompression Uncompressed = new BuildCompression(CompressionType.None, CompressionLevel.Maximum, 128 * 1024);
        public static readonly BuildCompression LZ4 = new BuildCompression(CompressionType.Lz4HC, CompressionLevel.Maximum, 128 * 1024);
        public static readonly BuildCompression LZMA = new BuildCompression(CompressionType.Lzma, CompressionLevel.Maximum, 128 * 1024);
        public static readonly BuildCompression UncompressedRuntime = Uncompressed;
        public static readonly BuildCompression LZ4Runtime = new BuildCompression(CompressionType.Lz4, CompressionLevel.Maximum, 128 * 1024);

        public CompressionType compression => m_Compression;
        public CompressionLevel level => m_Level;
        public uint blockSize => m_BlockSize;
    }
}

namespace UnityEngine
{
    public enum CompressionType
    {
        None = 0,
        Lzma = 1,
        Lz4 = 2,
        Lz4HC = 3,
    }

    public enum CompressionLevel
    {
        None = 0,
        Fastest = 1,
        Fast = 2,
        Normal = 3,
        High = 4,
        Maximum = 5,
    }
}

namespace UnityEngine
{
    /// <summary>The cache of downloaded AssetBundles. The Editor's is set up before any script runs.</summary>
    public sealed partial class Caching
    {
        [Unbound.EditorFaithful("the Editor's cache is ready before any script runs")]
        public static bool ready => true;
    }
}
