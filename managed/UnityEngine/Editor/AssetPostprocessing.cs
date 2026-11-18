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

        /// <summary>The callbacks Unbound does not run yet, by the kind of asset they postprocess: a
        /// postprocessor declaring one is reported (once, <see cref="Stubs.Ignored"/>) when an asset
        /// of that kind loads. A model's are its import's (Unbound's model prefabs, which scenes
        /// expand natively, do not run managed postprocessors).</summary>
        static readonly (string Kind, string Callback)[] s_Unsupported =
        {
            ("model", "OnPostprocessMeshHierarchy"), ("model", "OnAssignMaterialModel"), ("model", "OnPostprocessMaterial"),
            ("model", "OnPostprocessAnimation"), ("model", "OnPostprocessModel"), ("model", "OnPostprocessGameObjectWithUserProperties"),
            ("model", "OnPostprocessGameObjectWithAnimatedUserProperties"), ("model", "OnPreprocessCameraDescription"),
            ("model", "OnPreprocessLightDescription"), ("prefab", "OnPostprocessPrefab"),
            ("texture3d", "OnPostprocessTexture3D"), ("texture2darray", "OnPostprocessTexture2DArray"),
        };

        static readonly HashSet<string> s_ReportedKinds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Reports the postprocessors of an asset kind that loaded which Unbound does not run.</summary>
        internal static void ReportUnsupported(string kind)
        {
            if (kind == null || !s_ReportedKinds.Add(kind)) return;
            foreach (var (k, callback) in s_Unsupported)
            {
                if (k != kind) continue;
                foreach (var type in TypesDeclaring(callback))
                    Stubs.Ignored("AssetPostprocessor." + callback, $"is not run yet ({type.FullName}): the {kind} assets it would change load without it");
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
            methods.Sort((a, b) => string.CompareOrdinal(a.DeclaringType.Name, b.DeclaringType.Name));
            foreach (var method in methods) Call(method, Array.Empty<string>(), true);
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
            // GetPostprocessOrder does not order these (Scripting API 6000.0); the Editor runs them
            // by class name (tests/projects/assetpostprocessors allAssets.*).
            callbacks.Sort((a, b) => string.CompareOrdinal(a.DeclaringType.Name, b.DeclaringType.Name));
            return callbacks.ToArray();
        }

        // ------------------------------------------------------------------ the import pass

        static readonly HashSet<string> s_Preprocessed = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>As the editor domain loads: the preprocessors of every model, whose prefab the
        /// native side expands in scenes before any script asks for it. Textures and audio clips
        /// are preprocessed when they first load (<see cref="EnsurePreprocessed"/>).</summary>
        internal static void PrepareImports()
        {
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

        static IEnumerable<Type> TypesDeclaring(string name)
        {
            foreach (var assembly in GameDomain.Assemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract || !typeof(AssetPostprocessor).IsAssignableFrom(type)) continue;
                    if (type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) != null)
                        yield return type;
                }
            }
        }

        /// <summary>An asset's preprocessors: OnPreprocessAsset, then its type's (a model's
        /// OnPreprocessModel, then OnPreprocessAnimation), on the asset's importer, which
        /// <see cref="AssetImporter.GetAtPath"/> also gives. Settings they changed are handed to the
        /// native import for this session.</summary>
        internal static void Preprocess(string path, string kind)
        {
            var importer = AssetImporter.GetAtPath(path);
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
            var guid = AssetDatabase.AssetPathToGUID(path);
            Native.Call("import-settings", "{\"guid\":" + JsonSerializer.Serialize(guid) + ",\"importer\":" + JsonSerializer.Serialize(importer.m_SettingsType)
                + ",\"settings\":" + MetaSettings.ToJson(importer.m_Settings) + "}");
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
