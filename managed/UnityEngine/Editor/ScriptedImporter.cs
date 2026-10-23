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
    /// Editor does. Unity derives each object's file id from its identifier with a hash Unbound does
    /// not reproduce, so references into these assets resolve by the objects the import produced.
    /// </summary>
    internal static class ScriptedImports
    {
        internal static LoadedFile Import(AssetDatabaseRuntime.AssetInfo info)
        {
            var type = string.IsNullOrEmpty(info.Importer) ? null : GameDomain.ResolveScript(info.Importer, 11500000);
            if (type == null || !typeof(ScriptedImporter).IsAssignableFrom(type) || type.IsAbstract)
                throw new InvalidOperationException($"its ScriptedImporter (script {info.Importer}) is not a loaded importer class ({type?.AssemblyQualifiedName ?? "no class"}; base {type?.BaseType?.BaseType?.AssemblyQualifiedName})");
            ScriptedImporter importer;
            using (ObjectRegistry.AsAssets())
                importer = (ScriptedImporter)Activator.CreateInstance(type, nonPublic: true);
            importer.m_AssetPath = info.Path;
            var settings = Native.LoadDocuments("meta:" + info.Guid);
            if (settings.Count > 0)
            {
                var body = settings[0].Body;
                new YamlReader(new LoadedFile { Guid = info.Guid, Path = info.Path }).ReadScriptObject(importer, body);
                importer.userData = body.Get("userData")?.Scalar ?? "";
                importer.assetBundleName = body.Get("assetBundleName")?.Scalar ?? "";
                importer.assetBundleVariant = body.Get("assetBundleVariant")?.Scalar ?? "";
            }
            var ctx = new AssetImportContext(info.Path);
            using (ObjectRegistry.AsAssets())
                importer.OnImportAsset(ctx);
            var file = new LoadedFile { MainObject = ctx.mainObject, UnknownFileIds = true };
            long id = 1;
            foreach (var (identifier, obj) in ctx.m_Objects)
            {
                if (string.IsNullOrEmpty(obj.m_Name) && obj != ctx.mainObject) obj.m_Name = identifier;
                file.Objects[-(id++)] = obj;
                file.Identifiers[obj] = identifier;
            }
            return file;
        }
    }
}
