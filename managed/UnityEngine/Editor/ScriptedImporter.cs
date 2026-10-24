using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>An asset's importer, as the Editor exposes it: its path and settings.</summary>
    public partial class AssetImporter : Object
    {
        internal string m_AssetPath = "";

        public AssetImporter() { }

        public string assetPath => m_AssetPath;

        public bool importSettingsMissing => false;

        public string userData { get; set; } = "";

        public string assetBundleName { get; set; } = "";

        public string assetBundleVariant { get; set; } = "";
    }
}

namespace UnityEditor.AssetImporters
{
    /// <summary>A project importer written in C#; Unbound runs it when an asset it imports loads.</summary>
    public abstract partial class ScriptedImporter : AssetImporter
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
        public void DependsOnSourceAsset(string path) { }
        public void DependsOnSourceAsset(GUID guid) { }
        public void DependsOnArtifact(GUID guid) { }
        public void DependsOnArtifact(string path) { }
        public void DependsOnCustomDependency(string dependency) { }

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
    /// Editor does. Each object's file id is Unity's: xxHash64 of <c>"Type:" + class + "->" +
    /// identifier + "0"</c> (measured in tests/projects/input); a reference no object answers to
    /// still resolves by the objects the import produced.
    /// </summary>
    internal static class ScriptedImports
    {
        /// <summary>An instance of a ScriptedImporter class with the settings its .meta stores.</summary>
        internal static ScriptedImporter CreateImporter(string script, string path, string guid, YNode settings)
        {
            var type = string.IsNullOrEmpty(script) ? null : GameDomain.ResolveScript(script, 11500000);
            if (type == null || !typeof(ScriptedImporter).IsAssignableFrom(type) || type.IsAbstract)
                throw new InvalidOperationException($"its ScriptedImporter (script {script}) is not a loaded importer class");
            ScriptedImporter importer;
            using (ObjectRegistry.AsAssets())
                importer = (ScriptedImporter)Activator.CreateInstance(type, nonPublic: true);
            importer.m_AssetPath = path;
            if (settings != null)
            {
                new YamlReader(new LoadedFile { Guid = guid, Path = path }).ReadScriptObject(importer, settings);
                importer.userData = settings.Get("userData")?.Scalar ?? "";
                importer.assetBundleName = settings.Get("assetBundleName")?.Scalar ?? "";
                importer.assetBundleVariant = settings.Get("assetBundleVariant")?.Scalar ?? "";
            }
            return importer;
        }

        internal static LoadedFile Import(AssetDatabaseRuntime.AssetInfo info)
        {
            var settings = Native.LoadDocuments("meta:" + info.Guid);
            var importer = CreateImporter(info.Importer, info.Path, info.Guid, settings.Count > 0 ? settings[0].Body : null);
            var ctx = new AssetImportContext(info.Path);
            using (ObjectRegistry.AsAssets())
                importer.OnImportAsset(ctx);
            var file = new LoadedFile { MainObject = ctx.mainObject, UnknownFileIds = true };
            foreach (var (identifier, obj) in ctx.m_Objects)
            {
                if (string.IsNullOrEmpty(obj.m_Name) && obj != ctx.mainObject) obj.m_Name = identifier;
                file.Objects[FileId(identifier, obj)] = obj;
                file.Identifiers[obj] = identifier;
            }
            return file;
        }

        /// <summary>The file id Unity gives an object a ScriptedImporter adds under an identifier.</summary>
        internal static long FileId(string identifier, Object obj)
        {
            // The native class name: scripts' objects are MonoBehaviours to the serializer.
            var type = obj is MonoBehaviour || obj is ScriptableObject ? "MonoBehaviour" : obj.GetType().Name;
            return (long)XxHash64(System.Text.Encoding.UTF8.GetBytes("Type:" + type + "->" + identifier + "0"));
        }

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
