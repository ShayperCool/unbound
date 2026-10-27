using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    public sealed partial class PlayerSettings
    {
        /// <summary>The scripting runtime: .NET 4.x equivalent, the only one Unity 6 has.</summary>
        public static ScriptingRuntimeVersion scriptingRuntimeVersion
        {
            get => ScriptingRuntimeVersion.Latest;
            set { }
        }

        /// <summary>ProjectSettings.asset as editor code edits it through SerializedObject.</summary>
        internal static SerializedObject GetSerializedObject() => new SerializedObject(Project);

        public sealed partial class Android
        {
            public static AndroidSdkVersions minSdkVersion
            {
                get => (AndroidSdkVersions)ProjectSettingsText.Int("AndroidMinSdkVersion", 23);
                set { }
            }

            public static AndroidSdkVersions targetSdkVersion
            {
                get => (AndroidSdkVersions)ProjectSettingsText.Int("AndroidTargetSdkVersion", 0);
                set { }
            }

            public static AndroidSplashScreenScale splashScreenScale
            {
                get => (AndroidSplashScreenScale)ProjectSettingsText.Int("AndroidSplashScreenScale", 0);
                set { }
            }
        }

        public sealed partial class iOS
        {
            public static string targetOSVersionString
            {
                get => ProjectSettingsText.Text("iOSTargetOSVersionString", "13.0");
                set { }
            }
        }
    }

    /// <summary>Top-level values of ProjectSettings/ProjectSettings.asset, read once.</summary>
    internal static class ProjectSettingsText
    {
        static Dictionary<string, string> s_Values;

        /// <summary>The file changed (an applied SerializedObject): read it again when next asked.</summary>
        internal static void Invalidate() => s_Values = null;

        internal static string Text(string key, string fallback)
        {
            if (s_Values == null)
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    var file = Path.Combine(ProjectInfo.ProjectPath, "ProjectSettings", "ProjectSettings.asset");
                    if (File.Exists(file))
                        foreach (Match m in Regex.Matches(File.ReadAllText(file), @"^  (\w+): ?(.*?)\s*$", RegexOptions.Multiline))
                            values.TryAdd(m.Groups[1].Value, m.Groups[2].Value);
                }
                catch (IOException) { }
                s_Values = values;
            }
            return s_Values.TryGetValue(key, out var value) ? value : fallback;
        }

        internal static int Int(string key, int fallback) =>
            int.TryParse(Text(key, ""), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    public enum ScriptingRuntimeVersion
    {
        Legacy = 0,
        Latest = 1,
    }

    public enum AndroidSplashScreenScale
    {
        Center = 0,
        ScaleToFit = 1,
        ScaleToFill = 2,
    }

    public enum AndroidSdkVersions
    {
        AndroidApiLevelAuto = 0,
        [Obsolete("Minimum supported Android API level is 23 (Android 6.0 Marshmallow). Please use AndroidApiLevel23 or higher")] AndroidApiLevel16 = 16,
        [Obsolete("Minimum supported Android API level is 23 (Android 6.0 Marshmallow). Please use AndroidApiLevel23 or higher")] AndroidApiLevel17 = 17,
        [Obsolete("Minimum supported Android API level is 23 (Android 6.0 Marshmallow). Please use AndroidApiLevel23 or higher")] AndroidApiLevel18 = 18,
        [Obsolete("Minimum supported Android API level is 23 (Android 6.0 Marshmallow). Please use AndroidApiLevel23 or higher")] AndroidApiLevel19 = 19,
        [Obsolete("Minimum supported Android API level is 23 (Android 6.0 Marshmallow). Please use AndroidApiLevel23 or higher")] AndroidApiLevel21 = 21,
        [Obsolete("Minimum supported Android API level is 23 (Android 6.0 Marshmallow). Please use AndroidApiLevel23 or higher")] AndroidApiLevel22 = 22,
        AndroidApiLevel23 = 23,
        AndroidApiLevel24 = 24,
        AndroidApiLevel25 = 25,
        AndroidApiLevel26 = 26,
        AndroidApiLevel27 = 27,
        AndroidApiLevel28 = 28,
        AndroidApiLevel29 = 29,
        AndroidApiLevel30 = 30,
        AndroidApiLevel31 = 31,
        AndroidApiLevel32 = 32,
        AndroidApiLevel33 = 33,
        AndroidApiLevel34 = 34,
        AndroidApiLevel35 = 35,
        AndroidApiLevel36 = 36,
    }

    [Flags]
    public enum BuildOptions
    {
        None = 0,
        Development = 1,
        AutoRunPlayer = 4,
        ShowBuiltPlayer = 8,
        BuildAdditionalStreamedScenes = 16,
        AcceptExternalModificationsToPlayer = 32,
        InstallInBuildFolder = 64,
        CleanBuildCache = 128,
        ConnectWithProfiler = 256,
        AllowDebugging = 512,
        [Obsolete("BuildOptions.SymlinkLibraries is obsolete. Use BuildOptions.SymlinkSources instead (UnityUpgradable) -> [UnityEditor] BuildOptions.SymlinkSources")] SymlinkLibraries = 1024,
        SymlinkSources = 1024,
        UncompressedAssetBundle = 2048,
        [Obsolete("Use BuildOptions.Development instead")] StripDebugSymbols = 0,
        [Obsolete("Texture Compression is now always enabled")] CompressTextures = 0,
        ConnectToHost = 4096,
        CustomConnectionID = 8192,
        [Obsolete("Use StandaloneBuildSubtarget.Server instead")] EnableHeadlessMode = 16384,
        BuildScriptsOnly = 32768,
        PatchPackage = 65536,
        [Obsolete("BuildOptions.IL2CPP is deprecated and has no effect. Use PlayerSettings.SetScriptingBackend() instead.")] Il2CPP = 0,
        ForceEnableAssertions = 131072,
        CompressWithLz4 = 262144,
        CompressWithLz4HC = 524288,
        [Obsolete("Specify IL2CPP optimization level in Player Settings.")] ForceOptimizeScriptCompilation = 0,
        ComputeCRC = 1048576,
        StrictMode = 2097152,
        IncludeTestAssemblies = 4194304,
        NoUniqueIdentifier = 8388608,
        WaitForPlayerConnection = 33554432,
        EnableCodeCoverage = 67108864,
        EnableDeepProfilingSupport = 268435456,
        DetailedBuildReport = 536870912,
        [Obsolete("Shader LiveLink is no longer supported.")] ShaderLivelinkSupport = 0,
    }

    public enum StandaloneBuildSubtarget
    {
        Default = 0,
        Player = 2,
        Server = 1,
    }

    public partial class EditorUserBuildSettings
    {
        public static StandaloneBuildSubtarget standaloneBuildSubtarget
        {
            get => StandaloneBuildSubtarget.Player;
            set { }
        }
    }

    public partial class BuildPipeline
    {
        /// <summary>The installed platform support folder of the target
        /// (<c>Data/PlaybackEngines/&lt;Platform&gt;Player</c>).</summary>
        public static string GetPlaybackEngineDirectory(BuildTarget target, BuildOptions options) =>
            GetPlaybackEngineDirectory(target, options, true);

        public static string GetPlaybackEngineDirectory(BuildTarget target, BuildOptions options, bool assertUnsupportedPlatforms)
        {
            string folder = target switch
            {
                BuildTarget.Android => "AndroidPlayer",
                BuildTarget.iOS => "iOSSupport",
                BuildTarget.WebGL => "WebGLSupport",
                BuildTarget.StandaloneLinux64 => "LinuxStandaloneSupport",
                BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => "WindowsStandaloneSupport",
                BuildTarget.StandaloneOSX => "MacStandaloneSupport",
                _ => null,
            };
            var root = Path.GetFullPath(Path.Combine(UnityInstallInfo.DataPath, "PlaybackEngines"));
            var path = folder == null ? null : Path.Combine(root, folder);
            if (path != null && Directory.Exists(path)) return path;
            // The Linux Editor's own player support ships inside it.
            if (target == BuildTarget.StandaloneLinux64) return Path.Combine(root, "LinuxStandaloneSupport");
            if (assertUnsupportedPlatforms) Debug.LogError($"Unable to find the playback engine of {target}: its module is not installed.");
            return "";
        }
    }

    public sealed partial class PrefabUtility
    {
        /// <summary>The obsolete prefab type of an object: assets are prefabs, scene objects are
        /// not prefab instances (Unbound keeps no prefab connection in Play mode).</summary>
        [Obsolete("PrefabUtility.GetPrefabType is no longer supported. Use PrefabUtility.GetPrefabAssetType and PrefabUtility.GetPrefabInstanceStatus to get the full picture about Prefab types.")]
        public static PrefabType GetPrefabType(Object target)
        {
            if (target == null) return PrefabType.None;
            var go = target as GameObject ?? (target as Component)?.gameObject;
            if (go != null && go.m_IsPrefabAsset) return PrefabType.Prefab;
            return PrefabType.None;
        }

        [Obsolete("Use PrefabUtility.GetCorrespondingObjectFromSource.")]
        public static Object GetPrefabParent(Object obj) => null;
    }

    public enum PrefabType
    {
        None = 0,
        Prefab = 1,
        ModelPrefab = 2,
        PrefabInstance = 3,
        ModelPrefabInstance = 4,
        MissingPrefabInstance = 5,
        [Obsolete("PrefabType.DisconnectedPrefabInstance has been deprecated and is not used. Prefabs can not be in a disconnected state.")] DisconnectedPrefabInstance = 6,
        [Obsolete("PrefabType.DisconnectedModelPrefabInstance has been deprecated and is not used. Prefabs can not be in a disconnected state.")] DisconnectedModelPrefabInstance = 7,
    }

    /// <summary>A menu item; there is no menu in Unbound's Editor, so it is only an attribute.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed partial class MenuItem : Attribute
    {
        public string menuItem;
        public bool validate;
        public int priority;

        public MenuItem(string itemName) : this(itemName, false) { }

        public MenuItem(string itemName, bool isValidateFunction) : this(itemName, isValidateFunction, itemName.StartsWith("GameObject/Create Other") ? 10 : 1000) { }

        public MenuItem(string itemName, bool isValidateFunction, int priority)
        {
            menuItem = itemName;
            validate = isValidateFunction;
            this.priority = priority;
        }

        internal MenuItem(string itemName, bool isValidateFunction, int priority, bool internalMenu) : this(itemName, isValidateFunction, priority) { }

        internal MenuItem(string itemName, bool isValidateFunction, int priority, bool internalMenu, string[] editorModes) : this(itemName, isValidateFunction, priority) { }
    }

    /// <summary>A gizmo drawing method; the game view shows no gizmos.</summary>
    public sealed partial class DrawGizmo : Attribute
    {
        public Type drawnType;
        public GizmoType drawOptions;

        public DrawGizmo(GizmoType gizmo) => drawOptions = gizmo;

        public DrawGizmo(GizmoType gizmo, Type drawnGizmoType)
        {
            drawnType = drawnGizmoType;
            drawOptions = gizmo;
        }
    }

    [Flags]
    public enum GizmoType
    {
        Pickable = 1,
        NotInSelectionHierarchy = 2,
        NonSelected = 32,
        Selected = 4,
        Active = 8,
        InSelectionHierarchy = 16,
        [Obsolete("Use NotInSelectionHierarchy instead (UnityUpgradable) -> NotInSelectionHierarchy")] NotSelected = -127,
        [Obsolete("Use InSelectionHierarchy instead (UnityUpgradable) -> InSelectionHierarchy")] SelectedOrChild = -127,
    }

    public sealed partial class Tools : ScriptableObject
    {
        /// <summary>Every layer is visible in scene views.</summary>
        public static int visibleLayers
        {
            get => -1;
            set { }
        }
    }

    public sealed partial class EditorApplication
    {
        /// <summary>Asked for after settings that need a restart; Unbound's session goes on (restart
        /// it to apply them).</summary>
        internal static void RestartEditorAndRecompileScripts() =>
            Debug.LogWarning("[Unbound] The Editor was asked to restart and recompile scripts; restart the Unbound session to apply the change.");
    }

    public sealed partial class TextureImporter : AssetImporter
    {
        public TextureImporter() { }
    }

    public partial class TextScriptImporter : AssetImporter
    {
        public TextScriptImporter() { }
    }

    public partial class MonoImporter : AssetImporter
    {
        public MonoImporter() { }

        /// <summary>The scripts of the runtime (non-editor) assemblies.</summary>
        public static MonoScript[] GetAllRuntimeMonoScripts()
        {
            var scripts = new List<MonoScript>();
            foreach (var assembly in GameDomain.Descriptions)
            {
                if (assembly.Editor) continue;
                foreach (var (_, guid, _) in assembly.Scripts)
                    if (AssetDatabaseRuntime.LoadMain(guid, typeof(MonoScript)) is MonoScript script) scripts.Add(script);
            }
            return scripts.ToArray();
        }
    }
}

namespace UnityEditor.Compilation
{
    public static partial class CompilationPipeline
    {
        /// <summary>Scripts compile with debug information (Debug code optimization, as Unbound
        /// compiles for hot reload).</summary>
        public static bool IsScriptDebugInfoEnabled() => true;
    }
}

namespace UnityEditor.Android
{
    public static partial class AndroidExternalToolsSettings
    {
        /// <summary>The Android SDK the Editor's preferences name (the module's own when unset).</summary>
        public static string sdkRootPath
        {
            get
            {
                var custom = EditorPrefs.GetString("AndroidSdkRoot", "");
                if (!string.IsNullOrEmpty(custom)) return custom;
                var module = Path.Combine(UnityInstallInfo.DataPath, "PlaybackEngines", "AndroidPlayer", "SDK");
                return Directory.Exists(module) ? module : "";
            }
            set { }
        }
    }
}

namespace UnityEditor.PackageManager
{
    public sealed partial class PackageInfo
    {
        DependencyInfo[] m_Dependencies;

        /// <summary>The dependencies package.json declares.</summary>
        public DependencyInfo[] dependencies
        {
            get
            {
                if (m_Dependencies != null) return m_Dependencies;
                var list = new List<DependencyInfo>();
                try
                {
                    var file = Path.Combine(resolvedPath ?? "", "package.json");
                    if (File.Exists(file))
                    {
                        using var json = JsonDocument.Parse(File.ReadAllText(file));
                        if (json.RootElement.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Object)
                            foreach (var dep in deps.EnumerateObject())
                                list.Add(new DependencyInfo(dep.Name, dep.Value.GetString() ?? ""));
                    }
                }
                catch (Exception e) when (e is IOException || e is JsonException) { }
                return m_Dependencies = list.ToArray();
            }
        }
    }

    public partial struct DependencyInfo
    {
        internal DependencyInfo(string name, string version)
        {
            this.name = name;
            this.version = version;
        }

        public string name { get; }
        public string version { get; }
    }
}

namespace UnityEditor.PackageManager.UI
{
    public partial interface IPackageManagerExtension
    {
        void OnPackageSelectionChange(PackageInfo packageInfo);
        void OnPackageAddedOrUpdated(PackageInfo packageInfo);
        void OnPackageRemoved(PackageInfo packageInfo);
    }

    /// <summary>Package Manager window extensions: kept, though the window never opens.</summary>
    public static partial class PackageManagerExtensions
    {
        static readonly List<IPackageManagerExtension> s_Extensions = new List<IPackageManagerExtension>();

        public static void RegisterExtension(IPackageManagerExtension extension)
        {
            if (extension != null) s_Extensions.Add(extension);
        }
    }
}
