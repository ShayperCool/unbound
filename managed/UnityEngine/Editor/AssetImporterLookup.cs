using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using UnityEditor.AssetImporters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    public partial class AssetImporter
    {
        static readonly Dictionary<string, AssetImporter> s_ByPath = new Dictionary<string, AssetImporter>(StringComparer.Ordinal);

        /// <summary>The importer of an asset, with its settings from the .meta (one instance per
        /// path, as in the Editor). Changes made to it stay in memory: Unbound does not write .meta files.</summary>
        public static AssetImporter GetAtPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            path = path.Replace('\\', '/');
            if (s_ByPath.TryGetValue(path, out var cached)) return cached;
            var guid = AssetDatabase.AssetPathToGUID(path);
            AssetImporter importer = null;
            if (!string.IsNullOrEmpty(guid))
            {
                try { importer = Create(path, guid); }
                catch (Exception e) { Debug.LogWarning($"Unbound: the importer of {path}: {e.Message}"); }
            }
            s_ByPath[path] = importer;
            return importer;
        }

        static AssetImporter Create(string path, string guid)
        {
            var settings = Native.LoadDocuments("meta:" + guid);
            if (settings.Count == 0) return null;
            var doc = settings[0];
            AssetImporter importer;
            using (ObjectRegistry.AsAssets())
            {
                if (doc.TypeName == "ScriptedImporter")
                {
                    importer = ScriptedImports.CreateImporter(doc.Body.Ref("script").Guid, path, guid, doc.Body);
                }
                else
                {
                    var type = typeof(AssetImporter).Assembly.GetType("UnityEditor." + doc.TypeName)
                        ?? typeof(AssetImporter).Assembly.GetType("UnityEditor.AssetImporters." + doc.TypeName);
                    importer = type != null && typeof(AssetImporter).IsAssignableFrom(type) && !type.IsAbstract
                        ? (AssetImporter)Activator.CreateInstance(type, nonPublic: true)
                        : new AssetImporter();
                }
            }
            if (importer == null) return null;
            importer.m_AssetPath = path;
            importer.userData = doc.Body.Str("userData");
            importer.assetBundleName = doc.Body.Str("assetBundleName");
            importer.assetBundleVariant = doc.Body.Str("assetBundleVariant");
            importer.ReadSettings(doc.Body);
            return importer;
        }

        /// <summary>Reads the importer's own settings from its section of the .meta.</summary>
        internal virtual void ReadSettings(YNode body) { }
    }

    /// <summary>A precompiled plugin's settings: on which platforms it is enabled, and with what.</summary>
    public sealed partial class PluginImporter : AssetImporter
    {
        sealed class Platform
        {
            internal bool Enabled;
            internal readonly Dictionary<string, string> Settings = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        readonly Dictionary<string, Platform> m_Platforms = new Dictionary<string, Platform>(StringComparer.OrdinalIgnoreCase);
        bool m_IsPreloaded;
        bool m_IsExplicitlyReferenced;
        string[] m_DefineConstraints = Array.Empty<string>();

        public PluginImporter() { }

        internal override void ReadSettings(YNode body)
        {
            m_IsPreloaded = body.Bool("isPreloaded");
            m_IsExplicitlyReferenced = body.Bool("isExplicitlyReferenced");
            var constraints = new List<string>();
            foreach (var item in body.Get("defineConstraints")?.Seq() ?? Array.Empty<YNode>()) constraints.Add(item.AsString());
            m_DefineConstraints = constraints.ToArray();
            var data = body.Get("platformData");
            if (data == null) return;
            if (data.Kind == YKind.Seq)
            {
                // - first: {Android: Android}  second: {enabled: 1, settings: {...}}
                foreach (var item in data.Items)
                {
                    var first = item.Get("first");
                    var second = item.Get("second");
                    if (first == null || first.Kind != YKind.Map || first.Keys.Length == 0) continue;
                    var group = first.Keys[0];
                    var name = first.Values[0].AsString();
                    var platformName = !string.IsNullOrEmpty(name) ? name : group;
                    if (string.IsNullOrEmpty(platformName)) continue;
                    Fill(platformName, second);
                }
            }
            else if (data.Kind == YKind.Map)
            {
                // Older layout: platformData: {Any: {enabled: 1, settings: {...}}, Editor: {...}}
                for (int i = 0; i < data.Keys.Length; i++) Fill(data.Keys[i], data.Values[i]);
            }
        }

        void Fill(string name, YNode entry)
        {
            var platform = PlatformOf(name, create: true);
            if (entry == null) return;
            platform.Enabled = entry.Bool("enabled");
            var settings = entry.Get("settings");
            if (settings?.Kind != YKind.Map) return;
            for (int i = 0; i < settings.Keys.Length; i++) platform.Settings[settings.Keys[i]] = settings.Values[i].AsString();
        }

        Platform PlatformOf(string name, bool create)
        {
            if (m_Platforms.TryGetValue(name, out var platform) || !create) return platform;
            platform = new Platform();
            m_Platforms[name] = platform;
            return platform;
        }

        /// <summary>Unity's platform names for build targets (BuildPipeline.GetBuildTargetName).</summary>
        static string NameOf(BuildTarget target) => target switch
        {
            BuildTarget.StandaloneWindows => "Win",
            BuildTarget.StandaloneWindows64 => "Win64",
            BuildTarget.StandaloneOSX => "OSXUniversal",
            BuildTarget.StandaloneLinux64 => "Linux64",
            BuildTarget.iOS => "iOS",
            BuildTarget.Android => "Android",
            BuildTarget.WebGL => "WebGL",
            _ => target.ToString(),
        };

        public bool GetCompatibleWithAnyPlatform() => PlatformOf("Any", false)?.Enabled ?? false;

        public void SetCompatibleWithAnyPlatform(bool enable) => PlatformOf("Any", true).Enabled = enable;

        public bool GetExcludeFromAnyPlatform(string platformName) =>
            PlatformOf("Any", false)?.Settings.TryGetValue("Exclude " + platformName, out var v) == true && v == "1";

        public bool GetExcludeFromAnyPlatform(BuildTarget platform) => GetExcludeFromAnyPlatform(NameOf(platform));

        public void SetExcludeFromAnyPlatform(string platformName, bool excludedFromAny) =>
            PlatformOf("Any", true).Settings["Exclude " + platformName] = excludedFromAny ? "1" : "0";

        public void SetExcludeFromAnyPlatform(BuildTarget platform, bool excludedFromAny) => SetExcludeFromAnyPlatform(NameOf(platform), excludedFromAny);

        public bool GetExcludeEditorFromAnyPlatform() => GetExcludeFromAnyPlatform("Editor");

        public void SetExcludeEditorFromAnyPlatform(bool excludedFromAny) => SetExcludeFromAnyPlatform("Editor", excludedFromAny);

        /// <summary>With "Any Platform" on, unless excluded; otherwise when enabled for it.</summary>
        public bool GetCompatibleWithPlatform(string platformName) =>
            GetCompatibleWithAnyPlatform() ? !GetExcludeFromAnyPlatform(platformName) : PlatformOf(platformName, false)?.Enabled ?? false;

        public bool GetCompatibleWithPlatform(BuildTarget platform) => GetCompatibleWithPlatform(NameOf(platform));

        public void SetCompatibleWithPlatform(string platformName, bool enable) => PlatformOf(platformName, true).Enabled = enable;

        public void SetCompatibleWithPlatform(BuildTarget platform, bool enable) => SetCompatibleWithPlatform(NameOf(platform), enable);

        public bool GetCompatibleWithEditor() => GetCompatibleWithPlatform("Editor");

        public bool GetCompatibleWithEditor(string buildTarget) => GetCompatibleWithEditor();

        public bool GetCompatibleWithEditor(string buildTargetGroup, string buildTarget) => GetCompatibleWithEditor();

        public void SetCompatibleWithEditor(bool enable) => SetCompatibleWithPlatform("Editor", enable);

        public string GetPlatformData(string platformName, string key) =>
            PlatformOf(platformName, false)?.Settings.TryGetValue(key, out var v) == true ? v : "";

        public string GetPlatformData(BuildTarget platform, string key) => GetPlatformData(NameOf(platform), key);

        public void SetPlatformData(string platformName, string key, string value) => PlatformOf(platformName, true).Settings[key] = value;

        public void SetPlatformData(BuildTarget platform, string key, string value) => SetPlatformData(NameOf(platform), key, value);

        public string GetEditorData(string key) => GetPlatformData("Editor", key);

        public void SetEditorData(string key, string value) => SetPlatformData("Editor", key, value);

        public void ClearSettings() => m_Platforms.Clear();

        public bool GetIsOverridable() => false;

        public bool ShouldIncludeInBuild() => true;

        public bool isPreloaded { get => m_IsPreloaded; set => m_IsPreloaded = value; }

        public string[] DefineConstraints { get => m_DefineConstraints; set => m_DefineConstraints = value ?? Array.Empty<string>(); }

        /// <summary>Not a .NET assembly: a native library (.so, .a, .aar, a native .dll, ...).</summary>
        public bool isNativePlugin
        {
            get
            {
                if (!assetPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return true;
                try
                {
                    System.Reflection.AssemblyName.GetAssemblyName(Path.GetFullPath(assetPath));
                    return false;
                }
                catch (Exception)
                {
                    return true;
                }
            }
        }

        static readonly string[] s_PluginExtensions = { ".dll", ".so", ".a", ".aar", ".jar", ".bundle", ".dylib", ".jslib", ".jspre", ".framework", ".xcframework", ".winmd" };

        public static PluginImporter[] GetAllImporters()
        {
            var importers = new List<PluginImporter>();
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                var extension = Path.GetExtension(path);
                if (Array.IndexOf(s_PluginExtensions, extension.ToLowerInvariant()) < 0) continue;
                if (GetAtPath(path) is PluginImporter importer) importers.Add(importer);
            }
            return importers.ToArray();
        }

        public static PluginImporter[] GetImporters(string platformName) =>
            Array.FindAll(GetAllImporters(), i => i.GetCompatibleWithPlatform(platformName));

        public static PluginImporter[] GetImporters(BuildTarget platform) => GetImporters(NameOf(platform));

        public static PluginImporter[] GetImporters(string buildTargetGroup, string buildTarget) => GetImporters(buildTarget);

        public static PluginImporter[] GetImporters(BuildTargetGroup buildTargetGroup, BuildTarget buildTarget) => GetImporters(buildTarget);
    }
}
