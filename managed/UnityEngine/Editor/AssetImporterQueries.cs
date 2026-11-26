using System;
using Unbound.Runtime;
using UnityEditor.AssetImporters;

namespace UnityEditor
{
    /// <summary>
    /// Which importer imports an asset: its extension's, whatever its `.meta` names (a project's
    /// ScriptedImporter declaring the extension, else the extension's native importer, else the
    /// DefaultImporter, whose class is AssetImporter), as the Unity 6000.0.71f1 Editor answers
    /// (tests/projects/importerpick type.*, default.*, available.*, override.*); an override importer
    /// for its extension (overrideFileExtensions) its `.meta` names imports it instead and is its
    /// override (SetImporterOverride itself is not supported; overrides unmeasured).
    /// </summary>
    public sealed partial class AssetDatabase
    {
        public static Type GetImporterType(string assetPath) => AssetImporter.GetAtPath(assetPath)?.GetType();

        public static Type GetImporterType(GUID guid) => GetImporterType(GUIDToAssetPath(guid));

        public static Type[] GetImporterTypes(string[] paths)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            var types = new Type[paths.Length];
            for (int i = 0; i < paths.Length; i++) types[i] = GetImporterType(paths[i]);
            return types;
        }

        public static Type[] GetImporterTypes(ReadOnlySpan<GUID> guids)
        {
            var types = new Type[guids.Length];
            for (int i = 0; i < guids.Length; i++) types[i] = GetImporterType(guids[i]);
            return types;
        }

        /// <summary>The importer the asset's extension has: a project ScriptedImporter declaring it,
        /// else its native importer; null for an extension none declares (the DefaultImporter).</summary>
        public static Type GetDefaultImporter(string path)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(AssetPathToGUID(path))) return null;
            if (ScriptedImports.ForExtension(path) is { } scripted) return scripted;
            var name = ExtensionImporter(path).Importer;
            if (string.IsNullOrEmpty(name) || name == "DefaultImporter") return null;
            return typeof(AssetImporter).Assembly.GetType("UnityEditor." + name)
                ?? typeof(AssetImporter).Assembly.GetType("UnityEditor.AssetImporters." + name);
        }

        public static Type[] GetAvailableImporters(string path)
        {
            var available = new System.Collections.Generic.List<Type>();
            if (GetDefaultImporter(path) is { } importer) available.Add(importer);
            if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(AssetPathToGUID(path)))
                foreach (var type in ScriptedImports.OverridesFor(path))
                    if (!available.Contains(type)) available.Add(type);
            return available.ToArray();
        }

        [Obsolete("GetAvailableImporterTypes() has been deprecated. Use GetAvailableImporters() instead (UnityUpgradable) -> GetAvailableImporters(*)")]
        public static Type[] GetAvailableImporterTypes(string path) => GetAvailableImporters(path);

        /// <summary>The override importer its `.meta` names, or null.</summary>
        public static Type GetImporterOverride(string path)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(AssetPathToGUID(path))) return null;
            var type = GetImporterType(path);
            foreach (var candidate in ScriptedImports.OverridesFor(path))
                if (candidate == type) return type;
            return null;
        }

        /// <summary>The importer an asset's extension has natively and that importer's section of a
        /// new asset ("" where not measured): native <c>extension-importer</c>.</summary>
        internal static (string Importer, string Section) ExtensionImporter(string path)
        {
            using var reply = System.Text.Json.JsonDocument.Parse(Native.Call("extension-importer", System.Text.Json.JsonSerializer.Serialize(new { path })));
            return (reply.RootElement.GetProperty("importer").GetString() ?? "", reply.RootElement.GetProperty("section").GetString() ?? "");
        }

        /// <summary>The section a `.meta` of <paramref name="path"/> whose section is not its importer's
        /// (another importer's, or none) is written with when it is written again: its ScriptedImporter's
        /// at its defaults, else its extension's native importer's (importerpick labels.*); null when
        /// the file's section is its importer's, or the section is not known.</summary>
        internal static string OwnImporterSection(string path, string text)
        {
            var section = NamedScript(text);
            if (AssetImporter.GetAtPath(path) is ScriptedImporter importer && GameDomain.ScriptOf(importer.GetType()) is { } own)
            {
                if (section != null && string.Equals(section.Value.Guid, own.Guid, StringComparison.OrdinalIgnoreCase)
                    && section.Value.FileId == own.FileId) return null;
                return ScriptedImports.FreshSection(importer);
            }
            // A native importer's file whose `.meta` names a project's ScriptedImporter: written again
            // with that importer's section (importerpick labels.Plain.txt, labels.Unclaimed.qux); one
            // holding its GUID alone only for the DefaultImporter (labels.Guid.xyz: the other
            // importers' sections of such a file are the label-meta-section's, where measured).
            bool project = section != null && !string.Equals(section.Value.Guid, BuiltinScriptGuid, StringComparison.OrdinalIgnoreCase);
            var (name, native) = ExtensionImporter(path);
            if (!project && !(AssetPaths.HoldsGuidAlone(text) && name == "DefaultImporter")) return null;
            return native.Length > 0 ? native : null;
        }

        const string BuiltinScriptGuid = "0000000000000000e000000000000000";

        /// <summary>The script a `.meta`'s ScriptedImporter section names, or null.</summary>
        static (string Guid, long FileId)? NamedScript(string text)
        {
            var script = UnityYamlText.ParseTopLevel(text).Get("ScriptedImporter")?.Get("script");
            if (script == null) return null;
            var guid = script.Get("guid")?.Text;
            if (string.IsNullOrEmpty(guid) || !long.TryParse(script.Get("fileID")?.Text, out var fileId)) return null;
            return (guid, fileId);
        }
    }
}
