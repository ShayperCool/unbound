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
            var typeName = settings[0].TypeName;
            // A ScriptedImporter that does not import the file: the extension's own importer is
            // the asset's (tests/projects/importselfload asset.Mismatch.json).
            if (typeName == "ScriptedImporter" && settings[0].Body.Ref("script") is var script
                && !ScriptedImports.ImportsAsset(script.Guid, script.FileId, path, AssetDatabaseRuntime.Info(guid).ImporterOverride))
            {
                // The extension's importer; an extension no importer knows is the DefaultImporter's
                // (asset.Override.ovr2: the Editor's AssetImporter).
                var own = AssetDatabase.ImporterOf(path);
                typeName = own == null || own == "ScriptedImporter" ? "DefaultImporter" : own;
            }
            return FromMeta(path, guid, typeName, settings[0].Body);
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
            // As the .meta holds them, in lower case: reading writes nothing.
            importer.ReadBundle(body);
            importer.ReadSettings(body);
            importer.m_Loaded = true;
            EditorUtility.ClearDirty(importer);
            return importer;
        }

        /// <summary>Reads the importer's own settings from its section of the .meta.</summary>
        internal virtual void ReadSettings(YNode body) { }

        /// <summary>The settings as the .meta would hold them were nothing changed: without what the
        /// importer added itself on loading them (<see cref="TextureImporter"/>'s platforms).</summary>
        internal virtual YNode SavedSettings() => m_Settings;

        /// <summary>The settings a save writes: the importer's, but for what it reads differently
        /// from the file without a change since (<see cref="TextureImporter"/>'s version-1 sampler
        /// settings), which keeps the file's.</summary>
        internal virtual YNode WrittenSettings() => m_Settings;

        /// <summary>The settings were written into the .meta whole.</summary>
        internal virtual void SettingsWritten() { }

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
            ReadBundle(body);
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

        /// <summary>The "Exclude &lt;platform&gt;" setting of "Any Platform", read as a .meta flag
        /// (1 or true; crates/unbound-project/src/plugins.rs `compatible_with` reads it so too).</summary>
        public bool GetExcludeFromAnyPlatform(string platformName) =>
            PlatformOf("Any", false)?.Settings.TryGetValue("Exclude " + platformName, out var v) == true && (v == "1" || v == "true");

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
            // A Windows metadata file: its .meta names the PluginImporter (not measured).
            ".winmd",
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

        /// <summary>The plug-ins of a platform. As Unity's own C# (UnityCsReference 6000.0
        /// PluginImporter.GetImporters): with an installed platform module's importer extension, of
        /// the plug-ins that go to one place in a build (the extension's final path) a package's
        /// overridable one is left out beside one of the project (tests/projects/plugindefaults
        /// `importers.Android`: a static library of one name in a package and in the project).</summary>
        public static PluginImporter[] GetImporters(string platformName)
        {
            var all = Array.FindAll(GetAllImporters(), i => i.GetCompatibleWithPlatformOrAnyPlatformBuildTarget(platformName));
            var finalPath = FinalPathRule(platformName);
            return finalPath == null ? all : LeaveOutOverridden(all, finalPath);
        }

        /// <summary>Unity's own loop (PluginImporter.GetImporters): of plug-ins with one final path
        /// the first stays, a later one that is not overridable takes an overridable one's place,
        /// a later overridable one is left out; a plug-in without a final path always stays.</summary>
        internal static PluginImporter[] LeaveOutOverridden(PluginImporter[] all, Func<PluginImporter, string> finalPath)
        {
            var importers = new List<PluginImporter>();
            var byFinalPath = new Dictionary<string, PluginImporter>(StringComparer.Ordinal);
            foreach (var importer in all)
            {
                var path = finalPath(importer);
                if (!string.IsNullOrEmpty(path))
                {
                    if (!byFinalPath.TryGetValue(path, out var first)) byFinalPath.Add(path, importer);
                    else if (first.GetIsOverridable() && !importer.GetIsOverridable())
                    {
                        byFinalPath[path] = importer;
                        importers.Remove(first);
                    }
                    else if (importer.GetIsOverridable()) continue;
                }
                importers.Add(importer);
            }
            return importers.ToArray();
        }

        /// <summary>Where a platform's importer extension puts a plug-in in a build (its final path),
        /// or null when the platform has none here: the Editor, a platform whose module is not
        /// installed (Unity's C# leaves out nothing without an extension), one Unbound does not know
        /// (tvOS, visionOS, Windows Store and others, whose modules may leave plug-ins out: Unbound
        /// leaves out nothing, unmeasured). The standalone players' rule is Unity's own C#
        /// (DesktopPluginImporterExtension); Android's is the base rule for a static library without
        /// a CPU (its file name; tests/projects/plugindefaults `importers.Android`). Where the rule is
        /// not known (Android's other plug-ins, which its module may place by kind, folder or CPU;
        /// iOS, WebGL) the plug-in is compared with no other, with a notice for an overridable one,
        /// which the platform's module might leave out.</summary>
        static Func<PluginImporter, string> FinalPathRule(string platformName)
        {
            BuildTarget target;
            switch (platformName)
            {
                case "Win": target = BuildTarget.StandaloneWindows; break;
                case "Win64": target = BuildTarget.StandaloneWindows64; break;
                case "OSXUniversal": target = BuildTarget.StandaloneOSX; break;
                case "Linux64": target = BuildTarget.StandaloneLinux64; break;
                case "Android": target = BuildTarget.Android; break;
                case "iOS": target = BuildTarget.iOS; break;
                case "WebGL": target = BuildTarget.WebGL; break;
                default: return null;
            }
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Unknown, target)) return null;
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneOSX:
                case BuildTarget.StandaloneLinux64:
                    return importer => DesktopFinalPath(importer, platformName, target);
                case BuildTarget.Android:
                    return importer => AndroidFinalPath(importer, platformName);
                default:
                    return importer => UnknownFinalPath(importer, platformName);
            }
        }

        /// <summary>Android's final path where it is measured: a static library without a CPU by its
        /// file name (tests/projects/plugindefaults `importers.Android`); any other plug-in has none
        /// known.</summary>
        internal static string AndroidFinalPath(PluginImporter importer, string platformName) =>
            IsStaticLibrary(importer.assetPath) && string.IsNullOrEmpty(importer.GetPlatformData(platformName, "CPU"))
                ? Path.GetFileName(importer.assetPath)
                : UnknownFinalPath(importer, platformName);

        static string UnknownFinalPath(PluginImporter importer, string platformName)
        {
            if (importer.GetIsOverridable())
                Stubs.Ignored("PluginImporter.GetImporters(" + platformName + ")",
                    $"keeps {importer.assetPath}: whether the platform's module leaves out an overridable plug-in going to the same place in a build as another is not known");
            return "";
        }

        /// <summary>The base rule (DefaultPluginImporterExtension.CalculateFinalPluginPath): a CPU's
        /// folder, then the file name.</summary>
        static string BaseFinalPath(PluginImporter importer, string platformName)
        {
            var cpu = importer.GetPlatformData(platformName, "CPU");
            var name = Path.GetFileName(importer.assetPath);
            return !string.IsNullOrEmpty(cpu) && !string.Equals(cpu, "AnyCPU", StringComparison.OrdinalIgnoreCase) && !string.Equals(cpu, "None", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(cpu, name)
                : name;
        }

        static readonly System.Text.RegularExpressions.Regex s_LinuxLibrary =
            new System.Text.RegularExpressions.Regex(@"\.so(\.[0-9]+)*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        static bool IsStaticLibrary(string path) =>
            string.Equals(Path.GetExtension(path), ".a", StringComparison.OrdinalIgnoreCase);

        static bool IsCppSource(string path) =>
            Path.GetExtension(path).ToLowerInvariant() is ".cpp" or ".c" or ".h" or ".mm" or ".m";

        /// <summary>DesktopPluginImporterExtension.CalculateFinalPluginPath (Unity's own C#): "" for a
        /// native plug-in the player cannot use or a CPU of None; Windows by its CPU folder, macOS by
        /// the base rule, Linux by the file name.</summary>
        internal static string DesktopFinalPath(PluginImporter importer, string platformName, BuildTarget target)
        {
            var path = importer.assetPath;
            bool native = importer.isNativePlugin;
            var extension = Path.GetExtension(path).ToLowerInvariant();
            bool usable = target switch
            {
                BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => !native || extension == ".dll" || IsCppSource(path),
                BuildTarget.StandaloneOSX => !native || extension is ".bundle" or ".dylib" or ".xcprivacy" || s_LinuxLibrary.IsMatch(path) || IsCppSource(path),
                _ => !native || s_LinuxLibrary.IsMatch(path) || IsCppSource(path),
            };
            if (!usable) return "";
            var cpu = importer.GetPlatformData(platformName, "CPU");
            if (string.Equals(cpu, "None", StringComparison.OrdinalIgnoreCase)) return "";
            var name = Path.GetFileName(path);
            if (target == BuildTarget.StandaloneWindows || target == BuildTarget.StandaloneWindows64)
            {
                if (string.Equals(cpu, "ARM64", StringComparison.OrdinalIgnoreCase)) return Path.Combine(cpu, name);
                return Path.Combine(target == BuildTarget.StandaloneWindows ? "x86" : "x86_64", name);
            }
            return target == BuildTarget.StandaloneOSX ? BaseFinalPath(importer, platformName) : name;
        }

        public static PluginImporter[] GetImporters(BuildTarget platform) => GetImporters(NameOf(platform));

        /// <summary>With "Any Platform" on unless excluded, or enabled for the target; no plug-in is
        /// left out (Unity's own C#).</summary>
        public static PluginImporter[] GetImporters(string buildTargetGroup, string buildTarget) =>
            Array.FindAll(GetAllImporters(), i => i.GetCompatibleWithPlatformOrAnyPlatformBuildTarget(buildTarget));

        public static PluginImporter[] GetImporters(BuildTargetGroup buildTargetGroup, BuildTarget buildTarget) =>
            GetImporters(buildTargetGroup.ToString(), NameOf(buildTarget));
    }
}
