using System;
using System.Collections.Generic;
using System.Globalization;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>An asset's importer, as the Editor exposes it: its path and settings.</summary>
    [global::UnityEngine.Scripting.@PreserveAttribute] public partial class AssetImporter : Object
    {
        internal string m_AssetPath = "";

        public AssetImporter()
        {
            if (Compat.UncreatedObjectWrappersMeasured && !ObjectConstruction.TakeBacking(this)) MarkUncreated();
        }

        static string NormalizeString(string value) => value ?? (Compat.UncreatedObjectWrappersMeasured ? "" : null);

        internal void CheckImporterState(string binding)
        {
            if (!Compat.UncreatedObjectWrappersMeasured) return;
            ThrowIfUncreated();
            MainThreadCheck.Check(binding);
        }

        public string assetPath { get { CheckImporterState("get_assetPath"); return NormalizeString(m_AssetPath); } }

        [Unbound.EditorFaithful("every asset has its .meta once the Editor imported it")]
        public bool importSettingsMissing { get { CheckImporterState("get_importSettingsMissing"); return false; } }

        string m_UserData = "", m_AssetBundleName = "", m_AssetBundleVariant = "";
        public string userData
        {
            get { CheckImporterState("get_userData"); return NormalizeString(m_UserData); }
            set { CheckImporterState("set_userData"); m_UserData = NormalizeString(value); }
        }

        public string assetBundleName
        {
            get { CheckImporterState("get_assetBundleName"); return NormalizeString(m_AssetBundleName); }
            set { CheckImporterState("set_assetBundleName"); m_AssetBundleName = NormalizeString(value); }
        }

        public string assetBundleVariant
        {
            get { CheckImporterState("get_assetBundleVariant"); return NormalizeString(m_AssetBundleVariant); }
            set { CheckImporterState("set_assetBundleVariant"); m_AssetBundleVariant = NormalizeString(value); }
        }

        /// <summary>The .meta's <c>externalObjects</c>: imported objects by class and name, remapped
        /// to project assets.</summary>
        readonly Dictionary<SourceAssetIdentifier, Object> m_ExternalObjects = new Dictionary<SourceAssetIdentifier, Object>();

        public Dictionary<SourceAssetIdentifier, Object> GetExternalObjectMap() => new Dictionary<SourceAssetIdentifier, Object>(m_ExternalObjects);

        public void AddRemap(SourceAssetIdentifier identifier, Object externalObject) => m_ExternalObjects[identifier] = externalObject;

        public bool RemoveRemap(SourceAssetIdentifier identifier) => m_ExternalObjects.Remove(identifier);

        /// <summary>Reads the .meta's <c>externalObjects</c> entries (<c>first: {type: "Namespace:Class",
        /// assembly, name}</c>, <c>second: {fileID, guid}</c>); an entry naming a class Unbound does not
        /// know is skipped.</summary>
        internal void ReadExternalObjects(YNode settings)
        {
            var entries = settings?.Get("externalObjects");
            if (entries == null || entries.Kind != YKind.Seq) return;
            foreach (var entry in entries.Items)
            {
                var first = entry.Get("first");
                var type = ExternalType(first?.Get("type")?.AsString());
                var name = first?.Get("name")?.AsString();
                if (type == null || string.IsNullOrEmpty(name)) continue;
                var target = entry.Ref("second");
                m_ExternalObjects[new SourceAssetIdentifier(type, name)] = target.IsNull ? null : AssetDatabaseRuntime.Load(target.Guid, target.FileId, type);
            }
        }

        static Type ExternalType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var full = name.Replace(':', '.');
            return typeof(Object).Assembly.GetType(full) ?? GameDomain.FindTypeByName(full);
        }

        public partial struct SourceAssetIdentifier
        {
            public Type type;
            public string name;

            public SourceAssetIdentifier(Object asset)
            {
                if (asset == null) throw new ArgumentNullException(nameof(asset));
                type = asset.GetType();
                name = asset.name;
            }

            public SourceAssetIdentifier(Type type, string name)
            {
                if (type == null) throw new ArgumentNullException(nameof(type));
                if (name == null) throw new ArgumentNullException(nameof(name));
                if (string.IsNullOrEmpty(name)) throw new ArgumentException("The name is empty", nameof(name));
                this.type = type;
                this.name = name;
            }
        }
    }
}

namespace UnityEditor.AssetImporters
{
    /// <summary>A project importer written in C#; Unbound runs it when an asset it imports loads.</summary>
    [global::UnityEngine.Scripting.@PreserveAttribute] public abstract partial class ScriptedImporter : AssetImporter
    {
        protected ScriptedImporter() { }

        public abstract void OnImportAsset(AssetImportContext ctx);
    }

    /// <summary>What a ScriptedImporter fills while it imports: the asset's objects and main object.</summary>
    public partial class AssetImportContext
    {
        internal readonly List<(string Identifier, Object Object)> m_Objects = new List<(string, Object)>();
        Object m_MainObject;

        internal AssetImportContext(string assetPath) { this.assetPath = assetPath; }

        public string assetPath { get; internal set; }

        public BuildTarget selectedBuildTarget => EditorUserBuildSettingsInfo.ActiveBuildTarget;

        public Object mainObject => m_MainObject;

        public void SetMainObject(Object obj)
        {
            if (obj == null) return;
            if (!m_Objects.Exists(o => o.Object == obj))
            {
                Debug.LogError($"Main object '{obj.name}' must be added to the asset with AddObjectToAsset first ({assetPath}).");
                return;
            }
            m_MainObject = obj;
        }

        public void AddObjectToAsset(string identifier, Object obj) => AddObjectToAsset(identifier, obj, null);

        public void AddObjectToAsset(string identifier, Object obj, Texture2D thumbnail)
        {
            if (obj == null)
            {
                Debug.LogError($"Cannot add a null object to the asset {assetPath} ('{identifier}').");
                return;
            }
            if (string.IsNullOrEmpty(identifier))
            {
                Debug.LogError($"Cannot add an object to the asset {assetPath} without an identifier.");
                return;
            }
            if (m_Objects.Exists(o => o.Identifier == identifier))
            {
                Debug.LogError($"The identifier '{identifier}' is already used in the asset {assetPath}.");
                return;
            }
            m_Objects.Add((identifier, obj));
            obj.IsPersistentAsset = true;
            // As in Unity: the first object added is the main object until one is set.
            if (m_MainObject == null) m_MainObject = obj;
        }

        public void GetObjects(List<Object> objects)
        {
            if (objects == null) throw new ArgumentNullException(nameof(objects));
            objects.Clear();
            foreach (var (_, o) in m_Objects) objects.Add(o);
        }

        // Dependencies drive re-imports in the Editor; Unbound re-imports when the asset is reloaded.
        const string Reimported = "dependencies only schedule re-imports; Unbound imports the asset each time it loads, so it is always current";
        [Unbound.EditorFaithful(Reimported)] public void DependsOnSourceAsset(string path) { }
        [Unbound.EditorFaithful(Reimported)] public void DependsOnSourceAsset(GUID guid) { }
        [Unbound.EditorFaithful(Reimported)] public void DependsOnArtifact(GUID guid) { }
        [Unbound.EditorFaithful(Reimported)] public void DependsOnArtifact(string path) { }
        [Unbound.EditorFaithful(Reimported)] public void DependsOnCustomDependency(string dependency) { }

        public Object GetReferenceToAssetMainObject(string path) => AssetDatabase.LoadMainAssetAtPath(path);

        public string GetOutputArtifactFilePath(string fileName)
        {
            var dir = System.IO.Path.Combine(ProjectInfo.ProjectPath, "Library", "Unbound", "ImportArtifacts",
                AssetDatabase.AssetPathToGUID(assetPath));
            System.IO.Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, fileName);
        }

        public void LogImportError(string msg, Object obj = null) => Debug.LogError($"{assetPath}: {msg}", obj);

        public void LogImportWarning(string msg, Object obj = null) => Debug.LogWarning($"{assetPath}: {msg}", obj);
    }
}

namespace UnityEditor
{
    /// <summary>The active build target of the Play mode session (the --target the game runs for).</summary>
    internal static class EditorUserBuildSettingsInfo
    {
        internal static BuildTarget ActiveBuildTarget = BuildTarget.StandaloneLinux64;
        internal static BuildTargetGroup ActiveGroup = BuildTargetGroup.Standalone;

        internal static void SetTargetGroup(string group)
        {
            switch (group)
            {
                case "Android": ActiveBuildTarget = BuildTarget.Android; ActiveGroup = BuildTargetGroup.Android; break;
                case "iOS":
                case "iPhone": ActiveBuildTarget = BuildTarget.iOS; ActiveGroup = BuildTargetGroup.iOS; break;
                case "WebGL": ActiveBuildTarget = BuildTarget.WebGL; ActiveGroup = BuildTargetGroup.WebGL; break;
                default: ActiveBuildTarget = BuildTarget.StandaloneLinux64; ActiveGroup = BuildTargetGroup.Standalone; break;
            }
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEditor.AssetImporters;

    /// <summary>
    /// Imports assets whose .meta names a ScriptedImporter by running the project's importer, as the
    /// Editor does. Each object's file id is Unity's: an added GameObject's hierarchy (its own
    /// GameObject, components and children) by <see cref="HierarchyFileIds"/>; another object the one
    /// the .meta's name table lists for its identifier and class (tests/projects/shadergraph), else
    /// xxHash64 of <c>"Type:" + class + "->" + identifier + "0"</c> (measured in tests/projects/input,
    /// tests/projects/uxmlattributes). A reference no object answers to is empty.
    /// </summary>
    internal static class ScriptedImports
    {
        /// <summary>True while a project's importer runs: the engine's own import, not the game,
        /// is asking (members that warn when a game relies on them stay quiet then).</summary>
        [ThreadStatic] static int s_Importing;
        internal static bool Importing => s_Importing > 0;

        /// <summary>Marks the code inside as a project importer's (objects it makes belong to the asset).</summary>
        internal static ImportScope Scope()
        {
            s_Importing++;
            return default;
        }

        internal readonly struct ImportScope : IDisposable
        {
            public void Dispose() => s_Importing--;
        }

        /// <summary>An instance of a ScriptedImporter class with the settings its .meta stores.</summary>
        internal static ScriptedImporter CreateImporter(string script, long scriptFileId, string path, string guid, YNode settings)
        {
            var type = string.IsNullOrEmpty(script) ? null : GameDomain.ResolveScript(script, scriptFileId);
            if (type == null || !typeof(ScriptedImporter).IsAssignableFrom(type) || type.IsAbstract)
                throw new InvalidOperationException($"its ScriptedImporter (script {script}) is not a loaded importer class");
            ScriptedImporter importer;
            using (ObjectRegistry.AsAssets())
                importer = (ScriptedImporter)ObjectConstruction.CreateWithBacking(type);
            importer.m_AssetPath = path;
            importer.ReadExternalObjects(settings);
            if (settings != null)
            {
                new YamlReader(new LoadedFile { Guid = guid, Path = path }, metaNames: true).ReadScriptObject(importer, settings);
                importer.userData = settings.Get("userData")?.Scalar ?? "";
                importer.assetBundleName = settings.Get("assetBundleName")?.Scalar ?? "";
                importer.assetBundleVariant = settings.Get("assetBundleVariant")?.Scalar ?? "";
            }
            return importer;
        }

        internal static LoadedFile Import(AssetDatabaseRuntime.AssetInfo info)
        {
            var settings = Native.LoadDocuments("meta:" + info.Guid);
            var importer = CreateImporter(info.Importer, info.ImporterFileId, info.Path, info.Guid, settings.Count > 0 ? settings[0].Body : null);
            var ctx = new AssetImportContext(info.Path);
            using (Scope())
            using (ObjectRegistry.AsAssets())
                importer.OnImportAsset(ctx);
            var file = new LoadedFile { MainObject = ctx.mainObject, UnknownFileIds = true };
            var table = settings.Count > 0 ? NameTable(settings[0].Body) : null;
            var repeated = new List<(string Key, string Type)>();
            var inHierarchy = HierarchyFileIds(ctx.m_Objects, file.Objects, repeated);
            foreach (var (key, type) in repeated)
                ctx.LogImportWarning($"Identifier uniqueness violation: 'Name:{key}, Type:{type}'. Multiple Objects with the same name/type are generated by this Importer. There is no guarantee that subsequent imports of this asset will properly re-link to these targets.");
            foreach (var (identifier, obj) in ctx.m_Objects)
                if (!inHierarchy.Contains(obj)) file.Objects[FileId(identifier, obj, table)] = obj;
            // The main object takes the asset's file name, whatever the importer named it
            // (measured: tests/projects/svgsprites, a GameObject the importer names "transform"), after
            // its hierarchy's file ids are derived from the importer's names. A Shader keeps the name
            // its source declares, which Shader.Find looks up (tests/projects/shadergraph).
            if (ctx.mainObject != null && !(ctx.mainObject is Shader))
                ctx.mainObject.m_Name = System.IO.Path.GetFileNameWithoutExtension(info.Path);
            return file;
        }

        /// <summary>
        /// File ids of the hierarchies of the added GameObjects, into <paramref name="into"/>; returns
        /// the objects given one. Each added GameObject without an added ancestor roots a hierarchy
        /// whose path is its identifier and name (<c>"Top/Tree"</c>); a child's path adds its name, a
        /// component's its class name (<c>"Top/Tree/LOD0/MeshRenderer"</c>; a script component's
        /// <c>"MonoBehaviour-"</c> and its script class, <c>"Top/Holder/MonoBehaviour-Probe.Names.NamedMarker"</c>). An object's id is xxHash64
        /// of <c>"Type:" + class + "->" + path + index</c>, the index the first one (from 0) not taken
        /// yet, in depth-first order: the GameObject, its components, then its children. Names are
        /// those when the importer returns (before the main object takes the asset's name); a child
        /// added under its own identifier keeps its hierarchy id and the name table does not apply.
        /// Each path and class that more than one object shares goes once into
        /// <paramref name="repeated"/> (the Editor warns about it). Matches the Unity 6000.0.71f1
        /// Editor (tests/projects/scriptedfileids HierarchyControl).
        /// </summary>
        internal static HashSet<Object> HierarchyFileIds(List<(string Identifier, Object Obj)> added, Dictionary<long, Object> into,
            List<(string Key, string Type)> repeated = null)
        {
            var given = new HashSet<Object>(ReferenceEqualityComparer.Instance);
            var addedObjects = new HashSet<Object>(ReferenceEqualityComparer.Instance);
            foreach (var (_, obj) in added) addedObjects.Add(obj);
            foreach (var (identifier, obj) in added)
            {
                if (!(obj is GameObject go) || given.Contains(go) || HasAddedAncestor(go.transform, addedObjects)) continue;
                Walk(go.transform, identifier + "/" + go.m_Name, into, given, repeated);
            }
            return given;
        }

        static bool HasAddedAncestor(Transform t, HashSet<Object> added)
        {
            for (var p = t.parent; p != null; p = p.parent)
                if (added.Contains(p.gameObject)) return true;
            return false;
        }

        static void Walk(Transform t, string path, Dictionary<long, Object> into, HashSet<Object> given, List<(string, string)> repeated)
        {
            var go = t.gameObject;
            Assign(go, "GameObject", path, into, given, repeated);
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue;
                var type = NativeClassName(component);
                // A script names its class with its namespace: "MonoBehaviour-Probe.Names.NamedMarker".
                var segment = IsScriptObject(component) ? type + "-" + component.GetType().FullName : type;
                Assign(component, type, path + "/" + segment, into, given, repeated);
            }
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                Walk(child, path + "/" + child.gameObject.m_Name, into, given, repeated);
            }
        }

        static void Assign(Object obj, string type, string key, Dictionary<long, Object> into, HashSet<Object> given, List<(string, string)> repeated)
        {
            for (int index = 0; ; index++)
            {
                long id = (long)XxHash64(System.Text.Encoding.UTF8.GetBytes("Type:" + type + "->" + key + index.ToString(CultureInfo.InvariantCulture)));
                if (into.ContainsKey(id)) continue;
                into[id] = obj;
                given.Add(obj);
                if (index == 1) repeated?.Add((key, type));
                return;
            }
        }

        /// <summary>The derived leaf-asset file id for an import identifier; hierarchy ids remain separate.</summary>
        internal static long FileId(string identifier, Object obj) =>
            (long)XxHash64(System.Text.Encoding.UTF8.GetBytes("Type:" + NativeClassName(obj) + "->" + identifier + "0"));

        /// <summary>The native class name: scripts' objects are MonoBehaviours to the serializer.</summary>
        static string NativeClassName(Object obj) => IsScriptObject(obj) ? "MonoBehaviour" : obj.GetType().Name;

        /// <summary>
        /// The file id of an object added under <paramref name="identifier"/> when the asset's .meta
        /// lists names: an entry for the identifier and the object's class gives its file id, which
        /// replaces the derived one (a reference by the derived id is then empty); an entry for
        /// another class is ignored. Matches the Unity 6000.0.71f1 Editor (tests/projects/shadergraph:
        /// a graph whose .meta maps <c>4800000: MainAsset</c>, as older Shader Graph assets do).
        /// </summary>
        internal static long FileId(string identifier, Object obj, List<(int ClassId, long FileId, string Name)> table)
        {
            if (table != null)
                foreach (var (classId, fileId, name) in table)
                    if (name == identifier && IsOfClass(obj, classId)) return fileId;
            return FileId(identifier, obj);
        }

        /// <summary>The .meta's names of an importer's objects: <c>fileIDToRecycleName</c>
        /// (<c>fileID: name</c>, the class being <c>fileID / 100000</c>) and
        /// <c>internalIDToNameTable</c> (<c>- first: {classID: fileID}, second: name</c>).
        /// Matches the measured schema precedence in tests/projects/scriptedidentity (Unity 6000.0.71f1).</summary>
        internal static List<(int ClassId, long FileId, string Name)> NameTable(YNode settings)
        {
            List<(int, long, string)> table = null;
            var named = settings?.Get("internalIDToNameTable");
            if (named != null && named.Kind == YKind.Seq)
                foreach (var entry in named.Items)
                {
                    var first = entry.Get("first");
                    if (first == null || first.Kind != YKind.Map) continue;
                    var name = entry.Get("second")?.AsString() ?? "";
                    for (int i = 0; i < first.Keys.Length; i++)
                        if (int.TryParse(first.Keys[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var classId)
                            && first.Values[i].Kind == YKind.Scalar
                            && long.TryParse(first.Values[i].Scalar, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileId))
                            (table ??= new List<(int, long, string)>()).Add((classId, fileId, name));
                }
            // Current entries take precedence. Old schemas fill missing class/name pairs in
            // numeric file-id order (tests/projects/scriptedidentity, Unity 6000.0.71f1).
            var legacy = settings?.Get("fileIDToRecycleName");
            var version = settings?.Get("serializedVersion")?.AsInt() ?? 1;
            if (version <= 1 && legacy != null && legacy.Kind == YKind.Map)
            {
                var ordered = new SortedDictionary<long, string>();
                for (int i = 0; i < legacy.Keys.Length; i++)
                    if (long.TryParse(legacy.Keys[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                        ordered[id] = legacy.Values[i].AsString();
                foreach (var pair in ordered)
                    (table ??= new List<(int, long, string)>()).Add(((int)(pair.Key / LegacyClassStride), pair.Key, pair.Value));
            }
            return table;
        }

        /// <summary>Legacy file ids are <c>classID * 100000</c> (+2 for each further object of the class).</summary>
        const long LegacyClassStride = 100000;

        static bool IsScriptObject(Object obj) => obj is MonoBehaviour || obj is ScriptableObject;

        static bool IsOfClass(Object obj, int classId)
        {
            if (IsScriptObject(obj)) return classId == EngineTypes.MonoBehaviourId;
            var type = EngineTypes.ForClassId(classId, "", warn: false);
            if (type != null) return obj.GetType() == type;
            // A class can have a public managed type before its serialized fields are supported.
            // Leaf asset identity uses the release's existing class catalog independently of loaders.
            var concrete = obj.GetType();
            if (obj is GameObject || obj is Component || concrete.Assembly != typeof(Object).Assembly) return false;
            lock (s_ClassTableLock)
            {
                var release = ProjectInfo.UnityVersion;
                if (!s_ClassTableLoaded || s_ClassTableRelease != release)
                {
                    s_ClassTable = ClassCoverage.UnityTable(release);
                    s_ClassTableRelease = release;
                    s_ClassTableLoaded = true;
                }
                return s_ClassTable != null && s_ClassTable.TryGetValue(classId, out var entry) && entry.Name == concrete.Name;
            }
        }

        static readonly object s_ClassTableLock = new object();
        static bool s_ClassTableLoaded;
        static string s_ClassTableRelease;
        static SortedDictionary<int, ClassCoverage.UnityClass> s_ClassTable;

        /// <summary>xxHash64 with seed 0.</summary>
        internal static ulong XxHash64(byte[] data)
        {
            const ulong P1 = 11400714785074694791UL, P2 = 14029467366897019727UL, P3 = 1609587929392839161UL,
                P4 = 9650029242287828579UL, P5 = 2870177450012600261UL;
            static ulong Rotl(ulong x, int r) => System.Numerics.BitOperations.RotateLeft(x, r);
            static ulong Round(ulong acc, ulong input) => Rotl(acc + input * P2, 31) * P1;
            static ulong Merge(ulong acc, ulong value) => (acc ^ Round(0, value)) * P1 + P4;
            int n = data.Length, i = 0;
            ulong h;
            if (n >= 32)
            {
                ulong v1 = unchecked(P1 + P2), v2 = P2, v3 = 0, v4 = unchecked(0 - P1);
                for (; i + 32 <= n; i += 32)
                {
                    v1 = Round(v1, BitConverter.ToUInt64(data, i));
                    v2 = Round(v2, BitConverter.ToUInt64(data, i + 8));
                    v3 = Round(v3, BitConverter.ToUInt64(data, i + 16));
                    v4 = Round(v4, BitConverter.ToUInt64(data, i + 24));
                }
                h = Rotl(v1, 1) + Rotl(v2, 7) + Rotl(v3, 12) + Rotl(v4, 18);
                h = Merge(Merge(Merge(Merge(h, v1), v2), v3), v4);
            }
            else
            {
                h = P5;
            }
            h += (ulong)n;
            for (; i + 8 <= n; i += 8)
                h = Rotl(h ^ Round(0, BitConverter.ToUInt64(data, i)), 27) * P1 + P4;
            if (i + 4 <= n)
            {
                h = Rotl(h ^ BitConverter.ToUInt32(data, i) * P1, 23) * P2 + P3;
                i += 4;
            }
            for (; i < n; i++)
                h = Rotl(h ^ data[i] * P5, 11) * P1;
            h ^= h >> 33;
            h *= P2;
            h ^= h >> 29;
            h *= P3;
            h ^= h >> 32;
            return h;
        }
    }
}
