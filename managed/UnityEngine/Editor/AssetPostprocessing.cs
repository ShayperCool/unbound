using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Unbound.Runtime;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// The Editor's import pass for <see cref="AssetPostprocessor"/>s (matches the Unity
    /// 6000.0.71f1 Editor, tests/projects/assetpostprocessors). Unbound has no import of the opened
    /// project: the textures, models and audio clips it imports get their preprocessors
    /// (OnPreprocessAsset, then OnPreprocessTexture, OnPreprocessModel and OnPreprocessAnimation,
    /// or OnPreprocessAudio) once per session before their import (models as the editor domain
    /// loads, the others as they first load), and the settings they set are what the asset imports
    /// with (the Editor writes them into the .meta; Unbound keeps them in memory, part of the import
    /// cache's key). Assets of other kinds, which Unbound does not import, get no OnPreprocessAsset.
    /// The postprocessors of a texture, its sprites, a cubemap and an audio clip run when the asset
    /// loads, on its objects. Postprocessors
    /// run by increasing <see cref="AssetPostprocessor.GetPostprocessOrder"/>, then class name;
    /// OnPostprocessAllAssets runs by class name alone, and every load of the editor domain calls
    /// its five-argument form with no assets and <c>didDomainReload</c> true.
    /// </summary>
    internal static class AssetPostprocessing
    {
        const string AllAssets = "OnPostprocessAllAssets";

        /// <summary>The callbacks Unbound does not run yet (<see cref="ScriptMessages.NotSent"/>,
        /// which <c>unbound doctor</c> lists too): model postprocessors (Unbound's model prefabs, which
        /// scenes expand natively, run no managed postprocessor), OnPostprocessPrefab, the 2D array
        /// and 3D texture postprocessors, the SpeedTree 7/8 ones and asset bundle name changes. Each postprocessor declaring one is reported once as the
        /// editor domain loads.</summary>
        internal static readonly string[] Unsupported =
        {
            "OnPostprocessMeshHierarchy", "OnAssignMaterialModel", "OnPostprocessMaterial", "OnPostprocessAnimation",
            "OnPostprocessModel", "OnPostprocessGameObjectWithUserProperties", "OnPostprocessGameObjectWithAnimatedUserProperties",
            "OnPreprocessCameraDescription", "OnPreprocessLightDescription", "OnPostprocessPrefab",
            "OnPostprocessTexture2DArray", "OnPostprocessTexture3D", "OnPreprocessSpeedTree", "OnPostprocessSpeedTree",
            "OnPostprocessAssetbundleNameChanged",
        };

        static void ReportUnsupported()
        {
            foreach (var assembly in GameDomain.Assemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                foreach (var type in types)
                    if (type != null && !type.IsAbstract && typeof(AssetPostprocessor).IsAssignableFrom(type) && !type.IsDefined(typeof(Unbound.StubAttribute), false))
                        ScriptMessages.WarnNotSent(type);
            }
        }

        static readonly Dictionary<string, List<(int Order, Type Type, MethodInfo Method)>> s_Hooks =
            new Dictionary<string, List<(int, Type, MethodInfo)>>(StringComparer.Ordinal);

        /// <summary>The instance callback <paramref name="name"/> of every loaded postprocessor, in
        /// calling order (found once per session).</summary>
        internal static List<(int Order, Type Type, MethodInfo Method)> Hooks(string name, params Type[] parameters)
        {
            if (!s_Hooks.TryGetValue(name, out var hooks))
                s_Hooks[name] = hooks = AssetPostprocessorHooks.Find(name, parameters);
            return hooks;
        }

        // ------------------------------------------------------------------ OnPostprocessAllAssets

        /// <summary>Per assembly: its postprocessors' OnPostprocessAllAssets, by class name, found once
        /// and forgotten with the assembly (a reloaded game domain's assemblies are new ones).</summary>
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Assembly, MethodInfo[]> s_Callbacks =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Assembly, MethodInfo[]>();

        /// <summary>Calls <c>OnPostprocessAllAssets</c> of the postprocessor classes of an
        /// assembly with <paramref name="imported"/> as the imported assets (nothing deleted or
        /// moved, no domain reload).</summary>
        internal static void ImportedAssets(Assembly assembly, string[] imported)
        {
            foreach (var method in s_Callbacks.GetValue(assembly, Callbacks)) Call(method, imported, false);
        }

        /// <summary>The call every editor domain load makes: the five-argument callbacks of every
        /// loaded postprocessor, by class name, with no assets and <c>didDomainReload</c> true (the
        /// four-argument ones are not called; tests/projects/assetpostprocessors allAssets.*).</summary>
        internal static void DomainLoaded()
        {
            var methods = new List<MethodInfo>();
            foreach (var assembly in GameDomain.Assemblies())
                foreach (var method in s_Callbacks.GetValue(assembly, Callbacks))
                    if (method.GetParameters().Length == 5) methods.Add(method);
            foreach (var method in Ordered(methods)) Call(method, Array.Empty<string>(), true);
        }

        static void Call(MethodInfo method, string[] imported, bool didDomainReload)
        {
            var none = Array.Empty<string>();
            var arguments = method.GetParameters().Length == 4
                ? new object[] { imported, none, none, none }
                : new object[] { imported, none, none, none, didDomainReload };
            try { method.Invoke(null, arguments); }
            catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static MethodInfo[] Callbacks(Assembly assembly)
        {
            var callbacks = new List<MethodInfo>();
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            foreach (var type in types)
            {
                if (type == null || type.IsAbstract || !typeof(AssetPostprocessor).IsAssignableFrom(type)) continue;
                if (type.IsDefined(typeof(Unbound.StubAttribute), false)) continue;
                var method = type.GetMethod(AllAssets, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method == null) continue;
                var parameters = method.GetParameters();
                if (parameters.Length != 4 && !(parameters.Length == 5 && parameters[4].ParameterType == typeof(bool))) continue;
                callbacks.Add(method);
            }
            return Ordered(callbacks).ToArray();
        }

        /// <summary>OnPostprocessAllAssets callbacks in calling order: GetPostprocessOrder does not
        /// order them (Scripting API 6000.0); the Editor runs them by class name
        /// (tests/projects/assetpostprocessors allAssets.*), moved as their RunAfterClass,
        /// RunBeforeClass, RunAfterAssembly, RunBeforeAssembly, RunAfterPackage and RunBeforePackage
        /// attributes ask (each callback after every one it must follow, the others keeping their
        /// order; a cycle keeps the name order, with a warning).</summary>
        internal static List<MethodInfo> Ordered(List<MethodInfo> callbacks)
        {
            callbacks.Sort((a, b) =>
            {
                int byName = string.CompareOrdinal(a.DeclaringType.Name, b.DeclaringType.Name);
                return byName != 0 ? byName : string.CompareOrdinal(a.DeclaringType.FullName, b.DeclaringType.FullName);
            });
            int n = callbacks.Count;
            var after = new List<int>[n];
            for (int i = 0; i < n; i++) after[i] = new List<int>();
            for (int i = 0; i < n; i++)
                foreach (var (relation, target) in OrderAttributes(callbacks[i]))
                    for (int j = 0; j < n; j++)
                    {
                        if (j == i || !Matches(callbacks[j], relation, target)) continue;
                        if (relation.StartsWith("RunAfter", StringComparison.Ordinal)) after[i].Add(j);
                        else after[j].Add(i);
                    }
            var done = new bool[n];
            var ordered = new List<MethodInfo>(n);
            while (ordered.Count < n)
            {
                int next = -1;
                for (int i = 0; i < n && next < 0; i++)
                    if (!done[i] && after[i].TrueForAll(j => done[j])) next = i;
                if (next < 0)
                {
                    Debug.LogWarning("Unbound: the RunAfter/RunBefore attributes of OnPostprocessAllAssets callbacks form a cycle; the rest run by class name");
                    for (int i = 0; i < n; i++) if (!done[i]) { done[i] = true; ordered.Add(callbacks[i]); }
                    break;
                }
                done[next] = true;
                ordered.Add(callbacks[next]);
            }
            return ordered;
        }

        /// <summary>A callback's ordering attributes (read without making them: Unbound has no
        /// classes of its own for them), as (attribute name, its class, assembly or package).</summary>
        static IEnumerable<(string Relation, object Target)> OrderAttributes(MethodInfo method)
        {
            IList<CustomAttributeData> attributes;
            try { attributes = method.GetCustomAttributesData(); }
            catch (Exception) { yield break; }
            foreach (var attribute in attributes)
            {
                var type = attribute.AttributeType;
                if (type.Namespace != "UnityEditor.Callbacks" || attribute.ConstructorArguments.Count != 1) continue;
                var name = type.Name.EndsWith("Attribute", StringComparison.Ordinal) ? type.Name.Substring(0, type.Name.Length - "Attribute".Length) : type.Name;
                if (!name.StartsWith("RunAfter", StringComparison.Ordinal) && !name.StartsWith("RunBefore", StringComparison.Ordinal)) continue;
                var value = attribute.ConstructorArguments[0].Value;
                if (name.EndsWith("Class", StringComparison.Ordinal) && value is string qualified) value = Type.GetType(qualified, throwOnError: false);
                if (value != null) yield return (name, value);
            }
        }

        static bool Matches(MethodInfo method, string relation, object target)
        {
            var type = method.DeclaringType;
            if (relation.EndsWith("Class", StringComparison.Ordinal)) return target is Type t && t == type;
            if (relation.EndsWith("Assembly", StringComparison.Ordinal)) return target is string a && a == type.Assembly.GetName().Name;
            return target is string package && PackageOf(type.Assembly) == package;
        }

        /// <summary>The package a script assembly's sources are in (null for the project's).</summary>
        static string PackageOf(Assembly assembly)
        {
            var name = assembly.GetName().Name;
            foreach (var description in GameDomain.Descriptions)
            {
                if (description.Name != name) continue;
                foreach (var source in description.Sources)
                    if (source.StartsWith("Packages/", StringComparison.Ordinal))
                    {
                        int slash = source.IndexOf('/', "Packages/".Length);
                        return slash < 0 ? null : source.Substring("Packages/".Length, slash - "Packages/".Length);
                    }
                return null;
            }
            return null;
        }

        // ------------------------------------------------------------------ the import pass

        static readonly HashSet<string> s_Preprocessed = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>As the editor domain loads: the preprocessors of every model, whose prefab the
        /// native side expands in scenes before any script asks for it. Textures and audio clips
        /// are preprocessed when they first load (<see cref="EnsurePreprocessed"/>).</summary>
        internal static void PrepareImports()
        {
            ReportUnsupported();
            if (Hooks("OnPreprocessAsset").Count == 0 && Hooks("OnPreprocessModel").Count == 0 && Hooks("OnPreprocessAnimation").Count == 0)
                return;
            foreach (var path in AssetDatabase.GetAllAssetPaths())
                if (AssetDatabase.Kind(path) == "model") EnsurePreprocessed(path, "model");
        }

        /// <summary>Runs an asset's preprocessors once per session, before its import.</summary>
        internal static void EnsurePreprocessed(string path, string kind)
        {
            if (path == null || !s_Preprocessed.Add(path)) return;
            string own = kind switch { "texture" => "OnPreprocessTexture", "model" => "OnPreprocessModel", "audio" => "OnPreprocessAudio", _ => null };
            if (own == null) return;
            if (Hooks("OnPreprocessAsset").Count == 0 && Hooks(own).Count == 0 && (kind != "model" || Hooks("OnPreprocessAnimation").Count == 0))
                return;
            Preprocess(path, kind);
        }

        /// <summary>An asset's preprocessors: OnPreprocessAsset, then its type's (a model's
        /// OnPreprocessModel, then OnPreprocessAnimation), on the asset's importer, which
        /// <see cref="AssetImporter.GetAtPath"/> also gives. Settings they changed are handed to the
        /// native import for this session.</summary>
        internal static void Preprocess(string path, string kind)
        {
            var importer = AssetImporter.GetAtPath(path);
            // What scripts changed before (without SaveAndReimport) is not the preprocessors'.
            if (importer != null && importer.m_SettingsChanged)
            {
                importer.m_SettingsChanged = false;
                importer.m_ImportedSettings = MetaSettings.ToJson(importer.m_Settings);
            }
            var context = new AssetImportContext(path);
            AssetPostprocessorHooks.Run(Hooks("OnPreprocessAsset"), path, context, Array.Empty<object>(), importer);
            switch (kind)
            {
                case "texture":
                    AssetPostprocessorHooks.Run(Hooks("OnPreprocessTexture"), path, context, Array.Empty<object>(), importer);
                    break;
                case "model":
                    AssetPostprocessorHooks.Run(Hooks("OnPreprocessModel"), path, context, Array.Empty<object>(), importer);
                    AssetPostprocessorHooks.Run(Hooks("OnPreprocessAnimation"), path, context, Array.Empty<object>(), importer);
                    break;
                case "audio":
                    AssetPostprocessorHooks.Run(Hooks("OnPreprocessAudio"), path, context, Array.Empty<object>(), importer);
                    break;
            }
            if (importer == null || !importer.m_SettingsChanged || importer.m_Settings == null) return;
            importer.m_SettingsChanged = false;
            // Settings set to the values they had are the .meta's: the import stays the file's.
            var settings = MetaSettings.ToJson(importer.m_Settings);
            if (settings == importer.m_ImportedSettings) return;
            importer.m_ImportedSettings = settings;
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (AssetDatabaseRuntime.IsMainLoaded(guid))
                Debug.LogWarning($"Unbound: {path} loaded before its preprocessors ran; it keeps the import of its .meta's settings for this session");
            try
            {
                Native.Call("import-settings", "{\"guid\":" + JsonSerializer.Serialize(guid) + ",\"importer\":" + JsonSerializer.Serialize(importer.m_SettingsType)
                    + ",\"settings\":" + settings + "}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: {path} imports with its .meta's settings: the settings its preprocessors set were refused ({e.Message})");
            }
        }

        // ------------------------------------------------------------------ postprocessing at load

        /// <summary>A texture's postprocessors as it loads: OnPostprocessTexture with the texture
        /// readable (pixels it sets are uploaded), then OnPostprocessSprites with its sprites.</summary>
        internal static void PostprocessTexture(string path, Texture2D texture, Sprite[] sprites)
        {
            var hooks = Hooks("OnPostprocessTexture", typeof(Texture2D));
            var spriteHooks = Hooks("OnPostprocessSprites", typeof(Texture2D), typeof(Sprite[]));
            if (hooks.Count == 0 && (spriteHooks.Count == 0 || sprites.Length == 0)) return;
            var importer = AssetImporter.GetAtPath(path);
            var context = new AssetImportContext(path);
            if (hooks.Count > 0)
            {
                bool readable = texture.m_IsReadable;
                texture.m_IsReadable = true;
                try { AssetPostprocessorHooks.Run(hooks, path, context, new object[] { texture }, importer); }
                finally
                {
                    // The pixels a postprocessor read or set are the import's: uploaded, then kept
                    // on the CPU only for a readable texture.
                    if (texture.m_Pixels != null || texture.m_Hdr != null)
                    {
                        try { texture.Apply(false, !readable); }
                        catch (Exception e) { Debug.LogException(e, texture); }
                    }
                    texture.m_IsReadable = readable;
                }
            }
            if (sprites.Length > 0)
                AssetPostprocessorHooks.Run(spriteHooks, path, context, new object[] { texture, sprites }, importer);
        }

        /// <summary>A cubemap's postprocessors as it loads: OnPostprocessCubemap with the cubemap
        /// (pixels it sets are not uploaded: unmeasured).</summary>
        internal static void PostprocessCubemap(string path, Cubemap cubemap)
        {
            var hooks = Hooks("OnPostprocessCubemap", typeof(Cubemap));
            if (hooks.Count == 0) return;
            AssetPostprocessorHooks.Run(hooks, path, new AssetImportContext(path), new object[] { cubemap }, AssetImporter.GetAtPath(path));
        }

        /// <summary>An audio clip's postprocessors as it loads: OnPostprocessAudio with the clip, which
        /// has no name yet (the Editor names it after the callback).</summary>
        internal static void PostprocessAudio(string path, AudioClip clip)
        {
            var hooks = Hooks("OnPostprocessAudio", typeof(AudioClip));
            if (hooks.Count == 0) return;
            var name = clip.name;
            clip.name = "";
            try { AssetPostprocessorHooks.Run(hooks, path, new AssetImportContext(path), new object[] { clip }, AssetImporter.GetAtPath(path)); }
            finally { clip.name = name; }
        }
    }
}
