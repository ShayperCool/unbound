using System;
using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using UnityEditor.AssetImporters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    [global::UnityEngine.@ExcludeFromObjectFactoryAttribute] public partial class AssetImporter
    {
        static readonly Dictionary<string, AssetImporter> s_ByPath = new Dictionary<string, AssetImporter>(StringComparer.Ordinal);

        /// <summary>The importers importing their assets now, by path.</summary>
        static readonly Dictionary<string, AssetImporter> s_Importing = new Dictionary<string, AssetImporter>(StringComparer.Ordinal);

        /// <summary>True while any importer imports its asset.</summary>
        internal static bool AnyImporting => s_Importing.Count > 0;

        /// <summary>While the scope lasts, <see cref="GetAtPath"/> gives this importer for its asset:
        /// during an import the Editor gives the importing instance, afterwards another one with
        /// the .meta's settings (matches the Unity 6000.0.71f1 Editor, tests/projects/toolapis
        /// importer.during and importer.after).</summary>
        internal static ImportingScope Importing(string path, AssetImporter importer)
        {
            path = path.Replace('\\', '/');
            s_Importing.TryGetValue(path, out var outer);
            s_Importing[path] = importer;
            return new ImportingScope(path, outer);
        }

        internal readonly struct ImportingScope : IDisposable
        {
            readonly string m_Path;
            readonly AssetImporter m_Outer;

            internal ImportingScope(string path, AssetImporter outer)
            {
                m_Path = path;
                m_Outer = outer;
            }

            public void Dispose()
            {
                if (m_Outer != null) s_Importing[m_Path] = m_Outer;
                else s_Importing.Remove(m_Path);
            }
        }

        /// <summary>The importer of an asset, with its settings from the .meta (one instance per
        /// path, as in the Editor). Changes are saved into the .meta as the Editor saves them
        /// (ImporterMeta: SaveAndReimport, WriteImportSettingsIfDirty, an import's preprocessors).</summary>
        public static AssetImporter GetAtPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            path = path.Replace('\\', '/');
            if (s_Importing.TryGetValue(path, out var importing)) return importing;
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

        internal static AssetImporter TryGetCached(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            path = path.Replace('\\', '/');
            if (s_Importing.TryGetValue(path, out var importing)) return importing;
            return s_ByPath.TryGetValue(path, out var cached) ? cached : null;
        }

        internal static void Evict(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            s_ByPath.Remove(path.Replace('\\', '/'));
        }

        static AssetImporter Create(string path, string guid)
        {
            // A project settings file has no .meta: it is a native-format file of default settings
            // (tests/projects/defaultasset native.settings).
            if (SettingsAssets.AtPath(path) != null)
                return FromMeta(path, guid, "NativeFormatImporter", new YNode { Kind = YKind.Map, Keys = Array.Empty<string>(), Values = Array.Empty<YNode>() });
            var settings = Native.LoadDocuments("meta:" + guid);
            if (settings.Count == 0) return null;
            return FromMeta(path, guid, settings[0].TypeName, settings[0].Body);
        }

        /// <summary>The importer a .meta's importer section (<paramref name="typeName"/>:
        /// <paramref name="body"/>) describes.</summary>
        internal static AssetImporter FromMeta(string path, string guid, string typeName, YNode body)
        {
            AssetImporter importer;
            using (ObjectRegistry.AsAssets())
            {
                if (typeName == "ScriptedImporter")
                {
                    var script = body.Ref("script");
                    importer = ScriptedImports.CreateImporter(script.Guid, script.FileId, path, guid, body);
                }
                else
                {
                    var type = typeof(AssetImporter).Assembly.GetType("UnityEditor." + typeName)
                        ?? typeof(AssetImporter).Assembly.GetType("UnityEditor.AssetImporters." + typeName)
                        ?? typeof(AssetImporter).Assembly.GetType("UnityEditor.U2D." + typeName)
                        ?? typeof(AssetImporter).Assembly.GetType("UnityEditor.VFX." + typeName);
                    importer = type != null && typeof(AssetImporter).IsAssignableFrom(type) && !type.IsAbstract
                        ? (AssetImporter)ObjectConstruction.CreateWithBacking(type)
                        : (AssetImporter)ObjectConstruction.CreateWithBacking(typeof(AssetImporter));
                    importer.ReadExternalObjects(body);
                }
            }
            if (importer == null) return null;
            importer.m_Settings = MetaSettings.Clone(body);
            importer.m_SettingsType = typeName;
            importer.m_ImportedSettings = MetaSettings.ToJson(importer.m_Settings);
            importer.m_AssetPath = path;
            importer.m_Guid = guid;
            importer.userData = body.Str("userData");
            importer.assetBundleName = body.Str("assetBundleName");
            importer.assetBundleVariant = body.Str("assetBundleVariant");
            importer.ReadSettings(body);
            importer.m_Loaded = true;
            EditorUtility.ClearDirty(importer);
            return importer;
        }

        /// <summary>Reads the importer's own settings from its section of the .meta.</summary>
        internal virtual void ReadSettings(YNode body) { }

        /// <summary>The asset was imported again with this importer's settings.</summary>
        internal virtual void Reimported() { }

        /// <summary>The settings of the asset's .meta read into this importer again.</summary>
        internal void ReloadSettings()
        {
            if (string.IsNullOrEmpty(m_Guid)) return;
            var documents = Native.LoadDocuments("meta:" + m_Guid);
            if (documents.Count == 0) return;
            var body = documents[0].Body;
            m_Settings = MetaSettings.Clone(body);
            m_ImportedSettings = MetaSettings.ToJson(m_Settings);
            m_UserData = body.Str("userData") ?? "";
            m_AssetBundleName = body.Str("assetBundleName") ?? "";
            m_AssetBundleVariant = body.Str("assetBundleVariant") ?? "";
            ReadExternalObjects(body);
            ReadSettings(body);
            if (this is AssetImporters.ScriptedImporter)
                new YamlReader(new LoadedFile { Guid = m_Guid, Path = m_AssetPath }, metaNames: true).ReadScriptObject(this, body);
            ClearModifiedState();
        }

        /// <summary>Whether <see cref="AssetDatabase.WriteImportSettingsIfDirty"/> checks
        /// <see cref="EditorUtility.IsDirty"/> and <see cref="WriteCustomMeta"/>.</summary>
        internal virtual bool UsesDirtyFlagForWrite => false;

        /// <summary>Writes importer-specific sections into its `.meta` YAML map when the `.meta` is
        /// written for a change; returns true when it updated <paramref name="section"/>.</summary>
        internal virtual bool WriteCustomMeta(YText section) => false;

        /// <summary>Clears dirty and pending-write tracking after writing `.meta`.</summary>
        internal virtual void ClearModifiedState()
        {
            m_SettingsChanged = false;
            EditorUtility.ClearDirty(this);
        }
    }

    /// <summary>A precompiled plugin's settings: on which platforms it is enabled, and with what. A
    /// plug-in whose .meta has none has those the Editor gives it by its kind and folders (Unbound's
    /// .meta reader fills them in; matches the Unity 6000.0.71f1 Editor, tests/projects/plugindefaults).</summary>
    [global::UnityEngine.@ExcludeFromPresetAttribute] public sealed partial class PluginImporter : AssetImporter
    {
        sealed class Platform
        {
            internal bool Enabled;
            internal readonly Dictionary<string, string> Settings = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        readonly Dictionary<string, Platform> m_Platforms = new Dictionary<string, Platform>(StringComparer.OrdinalIgnoreCase);
        bool m_IsPreloaded;
        bool m_IsOverridable;
        bool m_IsExplicitlyReferenced;
        string[] m_DefineConstraints = Array.Empty<string>();

        public PluginImporter() { }

        internal override void ReadSettings(YNode body)
        {
            m_IsPreloaded = body.Bool("isPreloaded");
            m_IsOverridable = body.Bool("isOverridable");
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

        /// <summary>Whether the platform's own setting enables the plug-in ("Any" and "Editor" included):
        /// "Any Platform" is not taken into account (tests/projects/plugindefaults `*.compatible`,
        /// `*.flags` editor).</summary>
        bool Enabled(string platformName) => PlatformOf(platformName, false)?.Enabled ?? false;

        public bool GetCompatibleWithAnyPlatform() => Enabled("Any");

        public void SetCompatibleWithAnyPlatform(bool enable) => PlatformOf("Any", true).Enabled = enable;

        public bool GetExcludeFromAnyPlatform(string platformName) =>
            PlatformOf("Any", false)?.Settings.TryGetValue("Exclude " + platformName, out var v) == true && v == "1";

        public bool GetExcludeFromAnyPlatform(BuildTarget platform) => GetExcludeFromAnyPlatform(NameOf(platform));

        public void SetExcludeFromAnyPlatform(string platformName, bool excludedFromAny) =>
            PlatformOf("Any", true).Settings["Exclude " + platformName] = excludedFromAny ? "1" : "0";

        public void SetExcludeFromAnyPlatform(BuildTarget platform, bool excludedFromAny) => SetExcludeFromAnyPlatform(NameOf(platform), excludedFromAny);

        public bool GetExcludeEditorFromAnyPlatform() => GetExcludeFromAnyPlatform("Editor");

        public void SetExcludeEditorFromAnyPlatform(bool excludedFromAny) => SetExcludeFromAnyPlatform("Editor", excludedFromAny);

        public bool GetCompatibleWithPlatform(string platformName) => Enabled(platformName);

        public bool GetCompatibleWithPlatform(BuildTarget platform) => GetCompatibleWithPlatform(NameOf(platform));

        public void SetCompatibleWithPlatform(string platformName, bool enable) => PlatformOf(platformName, true).Enabled = enable;

        public void SetCompatibleWithPlatform(BuildTarget platform, bool enable) => SetCompatibleWithPlatform(NameOf(platform), enable);

        public bool GetCompatibleWithEditor() => Enabled("Editor");

        public bool GetCompatibleWithEditor(string buildTarget) => GetCompatibleWithEditor();

        public bool GetCompatibleWithEditor(string buildTargetGroup, string buildTarget) => GetCompatibleWithEditor();

        public void SetCompatibleWithEditor(bool enable) => SetCompatibleWithPlatform("Editor", enable);

        /// <summary>A build target's setting; "Any" and "Editor" are no build targets and give ""
        /// (their settings are read with GetEditorData and GetExcludeFromAnyPlatform; tests/projects/plugindefaults
        /// `*.data`).</summary>
        public string GetPlatformData(string platformName, string key) =>
            platformName == "Any" || platformName == "Editor" ? "" : Setting(platformName, key);

        string Setting(string platformName, string key) =>
            PlatformOf(platformName, false)?.Settings.TryGetValue(key, out var v) == true ? v : "";

        public string GetPlatformData(BuildTarget platform, string key) => GetPlatformData(NameOf(platform), key);

        public void SetPlatformData(string platformName, string key, string value) => PlatformOf(platformName, true).Settings[key] = value;

        public void SetPlatformData(BuildTarget platform, string key, string value) => SetPlatformData(NameOf(platform), key, value);

        public string GetEditorData(string key) => Setting("Editor", key);

        public void SetEditorData(string key, string value) => SetPlatformData("Editor", key, value);

        public void ClearSettings() => m_Platforms.Clear();

        /// <summary>A package's plug-in, which one of the same name in the project overrides (its
        /// .meta's setting; tests/projects/plugindefaults `*.flags` overridable).</summary>
        public bool GetIsOverridable() => m_IsOverridable;

        /// <summary>A delegate set with SetIncludeInBuildDelegate may leave the plugin out;
        /// otherwise it is included (its define constraints and platform settings are not checked
        /// yet).</summary>
        public bool ShouldIncludeInBuild()
        {
            if (m_IncludeInBuildDelegate != null && !m_IncludeInBuildDelegate(assetPath)) return false;
            Stubs.Ignored("PluginImporter.ShouldIncludeInBuild", "is not honoured yet: define constraints and platform settings are not checked");
            return true;
        }

        public bool isPreloaded
        {
            get { CheckImporterState("get_isPreloaded"); return m_IsPreloaded; }
            set { CheckImporterState("set_isPreloaded"); m_IsPreloaded = value; }
        }

        public string[] DefineConstraints
        {
            get { CheckImporterState("get_DefineConstraints"); return m_DefineConstraints; }
            set { CheckImporterState("set_DefineConstraints"); m_DefineConstraints = value ?? Array.Empty<string>(); }
        }

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

        /// <summary>The extensions the PluginImporter imports (crates/unbound-project/src/plugin_defaults.rs
        /// PLUGIN_FILE_EXTENSIONS and FOLDER_PLUGIN_EXTENSIONS).</summary>
        static readonly string[] s_PluginExtensions =
        {
            ".dll", ".so", ".dylib", ".a", ".jar", ".aar", ".c", ".cc", ".cpp", ".h", ".m", ".mm", ".java", ".kt", ".swift", ".jslib", ".jspre",
            ".bundle", ".framework", ".xcframework", ".plugin", ".androidlib",
        };

        /// <summary>Whether the PluginImporter imports the file or folder at the path, by its extension.</summary>
        internal static bool IsPluginPath(string path) =>
            Array.IndexOf(s_PluginExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;

        public static PluginImporter[] GetAllImporters()
        {
            var importers = new List<PluginImporter>();
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (IsPluginPath(path) && GetAtPath(path) is PluginImporter importer) importers.Add(importer);
            }
            return importers.ToArray();
        }

        /// <summary>With "Any Platform" on, unless excluded from it; otherwise when enabled for it
        /// (tests/projects/plugindefaults `importers.*`; crates/unbound-project/src/plugins.rs
        /// `compatible_with` is the same rule).</summary>
        internal bool GetCompatibleWithPlatformOrAnyPlatformBuildTarget(string buildTarget) =>
            GetCompatibleWithAnyPlatform() ? !GetExcludeFromAnyPlatform(buildTarget) : Enabled(buildTarget);

        public static PluginImporter[] GetImporters(string platformName) =>
            Array.FindAll(GetAllImporters(), i => i.GetCompatibleWithPlatformOrAnyPlatformBuildTarget(platformName));

        public static PluginImporter[] GetImporters(BuildTarget platform) => GetImporters(NameOf(platform));

        public static PluginImporter[] GetImporters(string buildTargetGroup, string buildTarget) => GetImporters(buildTarget);

        public static PluginImporter[] GetImporters(BuildTargetGroup buildTargetGroup, BuildTarget buildTarget) => GetImporters(buildTarget);
    }
}
