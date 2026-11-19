using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Unbound.Runtime;
using UnityEditor.AssetImporters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// The Editor's import pass for <see cref="AssetPostprocessor"/>s (matches the Unity
    /// 6000.0.71f1 Editor, tests/projects/assetpostprocessors). Unbound has no import of the opened
    /// project: the textures, models and audio clips it imports get their preprocessors
    /// (OnPreprocessAsset, then OnPreprocessTexture, OnPreprocessModel and OnPreprocessAnimation,
    /// or OnPreprocessAudio) once per session before their import (models as the editor domain
    /// loads, the others as they first load), and the settings they set are what the asset imports
    /// with (the Editor writes them into the .meta; Unbound writes no authored file and keeps them in
    /// memory, part of the import cache's key). Prefabs are imported when postprocessors change
    /// prefab imports (OnPostprocessPrefab): each gets OnPreprocessAsset then. Assets of other
    /// kinds, which Unbound does not import, get no OnPreprocessAsset. The postprocessors of a
    /// texture, its sprites, a cubemap and an audio clip run when the asset loads, on its objects;
    /// a model's and a prefab's as it loads or a scene first expands it
    /// (<see cref="HierarchyPostprocessing"/>). Every callback of an asset's import is called on
    /// one postprocessor per class (<see cref="ImportSession"/>). Postprocessors run by increasing
    /// <see cref="AssetPostprocessor.GetPostprocessOrder"/>, then class name;
    /// OnPostprocessAllAssets runs by class name alone, and every load of the editor domain calls
    /// its five-argument form with no assets and <c>didDomainReload</c> true.
    /// </summary>
    internal static class AssetPostprocessing
    {
        const string AllAssets = "OnPostprocessAllAssets";

        /// <summary>The callbacks Unbound does not run yet (<see cref="ScriptMessages.NotSent"/>,
        /// which <c>unbound doctor</c> lists too): animated user properties and the camera and light
        /// descriptions of models (Unbound imports neither), the 2D array and 3D texture
        /// postprocessors, the SpeedTree 7/8 ones and asset bundle name changes. Each postprocessor
        /// declaring one is reported once as the editor domain loads.</summary>
        internal static readonly string[] Unsupported =
        {
            "OnPostprocessGameObjectWithAnimatedUserProperties", "OnPreprocessCameraDescription", "OnPreprocessLightDescription",
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

        /// <summary>Makes <paramref name="hooks"/> the postprocessors of <paramref name="name"/>
        /// until the scope ends (tests: the domain's own stay unseen).</summary>
        internal static IDisposable HooksForTests(string name, List<(int Order, Type Type, MethodInfo Method)> hooks)
        {
            s_Hooks.TryGetValue(name, out var previous);
            s_Hooks[name] = hooks;
            return new Restore(() => { if (previous == null) s_Hooks.Remove(name); else s_Hooks[name] = previous; });
        }

        sealed class Restore : IDisposable
        {
            readonly Action m_Action;
            internal Restore(Action action) => m_Action = action;
            public void Dispose() => m_Action();
        }

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
            callbacks.Sort((a, b) => AssetPostprocessorHooks.ByClassName(a.DeclaringType, b.DeclaringType));
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
        static readonly Dictionary<string, ImportSession> s_Sessions = new Dictionary<string, ImportSession>(StringComparer.Ordinal);

        /// <summary>The asset's import in progress (its preprocessors ran, its postprocessors have
        /// not finished), else a new one.</summary>
        internal static ImportSession Session(string path)
        {
            if (!s_Sessions.TryGetValue(path, out var session))
                s_Sessions[path] = session = new ImportSession(path, new AssetImportContext(path), AssetImporter.GetAtPath(path));
            return session;
        }

        /// <summary>The asset's import in progress, if any.</summary>
        internal static ImportSession ExistingSession(string path) => s_Sessions.TryGetValue(path, out var session) ? session : null;

        /// <summary>The asset's import is done: a later one makes new postprocessors.</summary>
        internal static void EndSession(string path) => s_Sessions.Remove(path);

        /// <summary>As the editor domain loads: the preprocessors of every model, whose prefab the
        /// native side expands in scenes before any script asks for it (textures and audio clips
        /// are preprocessed when they first load, <see cref="EnsurePreprocessed"/>), and the kinds
        /// of imports the postprocessors change, for the native expansion.</summary>
        internal static void PrepareImports()
        {
            ReportUnsupported();
            HierarchyPostprocessing.Announce();
            if (Hooks("OnPreprocessAsset").Count == 0 && Hooks("OnPreprocessModel").Count == 0 && Hooks("OnPreprocessAnimation").Count == 0)
                return;
            foreach (var path in AssetDatabase.GetAllAssetPaths())
                if (AssetDatabase.Kind(path) == "model") EnsurePreprocessed(path, "model");
        }

        /// <summary>The asset is imported again: its preprocessors run again before that import.</summary>
        internal static void ForgetPreprocessed(string path) => s_Preprocessed.Remove(path);

        /// <summary>Runs an asset's preprocessors once per session, before its import.</summary>
        internal static void EnsurePreprocessed(string path, string kind, string guid = null)
        {
            if (path == null || !s_Preprocessed.Add(path)) return;
            // A prefab has no preprocessor of its own: OnPreprocessAsset alone.
            string own = kind switch { "texture" => "OnPreprocessTexture", "model" => "OnPreprocessModel", "audio" => "OnPreprocessAudio", _ => null };
            if (own == null && kind != "prefab") return;
            if (Hooks("OnPreprocessAsset").Count == 0 && (own == null || Hooks(own).Count == 0) && (kind != "model" || Hooks("OnPreprocessAnimation").Count == 0))
                return;
            Preprocess(path, kind, guid);
        }

        /// <summary>An asset's preprocessors: OnPreprocessAsset, then its type's (a model's
        /// OnPreprocessModel, then OnPreprocessAnimation), on the asset's importer, which
        /// <see cref="AssetImporter.GetAtPath"/> also gives. Settings they changed are handed to the
        /// native import for this session.</summary>
        internal static void Preprocess(string path, string kind, string guid = null)
        {
            // The import takes the importer's settings as the preprocessors leave them: with what a
            // script changed on it before (Unbound's choice, unmeasured). Without a preprocessor of
            // the asset's kind none of this runs: such edits stay in memory, as SaveAndReimport
            // (a stub) does not import them.
            var session = Session(path);
            var importer = session.Importer;
            using var callbacks = AssetDatabaseRuntime.ImportCallbacks(guid ?? AssetDatabase.AssetPathToGUID(path));
            AssetPostprocessorHooks.Run(Hooks("OnPreprocessAsset"), session, Array.Empty<object>());
            switch (kind)
            {
                case "texture":
                    AssetPostprocessorHooks.Run(Hooks("OnPreprocessTexture"), session, Array.Empty<object>());
                    break;
                case "model":
                    // OnPreprocessAnimation follows (the Editor calls it after the hierarchy's
                    // callbacks): the settings it sets must be known before Unbound's import.
                    AssetPostprocessorHooks.Run(Hooks("OnPreprocessModel"), session, Array.Empty<object>());
                    AssetPostprocessorHooks.Run(Hooks("OnPreprocessAnimation"), session, Array.Empty<object>());
                    break;
                case "audio":
                    AssetPostprocessorHooks.Run(Hooks("OnPreprocessAudio"), session, Array.Empty<object>());
                    break;
            }
            if (importer == null || !importer.m_SettingsChanged || importer.m_Settings == null) return;
            // Settings set to the values they had are the .meta's: the import stays the file's.
            var settings = MetaSettings.ToJson(importer.m_Settings);
            if (settings == importer.m_ImportedSettings) return;
            // The Editor saves the settings the preprocessors changed into the .meta
            // (tests/projects/assetpostprocessors meta.*, tests/projects/projectwrites 10.preprocessor).
            ImporterMeta.Write(importer);
            importer.m_ImportedSettings = settings;
            guid ??= AssetDatabase.AssetPathToGUID(path);
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
            using var callbacks = AssetDatabaseRuntime.ImportCallbacks(AssetDatabase.AssetPathToGUID(path));
            var hooks = Hooks("OnPostprocessTexture", typeof(Texture2D));
            var spriteHooks = Hooks("OnPostprocessSprites", typeof(Texture2D), typeof(Sprite[]));
            if (hooks.Count == 0 && (spriteHooks.Count == 0 || sprites.Length == 0))
            {
                EndSession(path);
                return;
            }
            var session = Session(path);
            if (hooks.Count > 0 && PostprocessBeforeCompression(session, hooks, texture)) { }
            else if (hooks.Count > 0)
            {
                bool readable = texture.m_IsReadable;
                texture.m_IsReadable = true;
                try { AssetPostprocessorHooks.Run(hooks, session, new object[] { texture }); }
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
                AssetPostprocessorHooks.Run(spriteHooks, session, new object[] { texture, sprites });
            EndSession(path);
        }

        /// <summary>OnPostprocessTexture of a block-compressed import: the Editor gives the texture
        /// before its compression (readable RGB24 without alpha, else RGBA32, with its mips:
        /// tests/projects/assetpostprocessors Plain.png and Sprite.png), then compresses what the
        /// postprocessors left. Unbound's import is compressed already: the postprocessors get a
        /// texture of the pixels before compression, and pixels they change are compressed into
        /// the import's format. Returns false, having done nothing, for an import that is not of an
        /// 8-bit block format Unbound compresses (a crunched or HDR one: those get the import as it
        /// is, unmeasured).</summary>
        static bool PostprocessBeforeCompression(ImportSession session, List<(int, Type, MethodInfo)> hooks, Texture2D texture)
        {
            if (!BeforeCompressionApplies(texture.format)) return false;
            var levels = new List<Color32[]>();
            bool alpha = true;
            try
            {
                for (int mip = 0; mip < texture.mipmapCount; mip++)
                {
                    using var json = JsonDocument.Parse(Native.Call("texture-pixels",
                        JsonSerializer.Serialize(new { guid = texture.m_AssetGuid, mip, beforeCompression = true })));
                    var root = json.RootElement;
                    // Float pixels (an HDR source of an 8-bit block format) are not given so.
                    if (root.GetProperty("format").GetInt32() != 0) return false;
                    alpha = root.GetProperty("hasAlpha").GetBoolean();
                    var rgba = Convert.FromBase64String(root.GetProperty("rgba").GetString());
                    var level = new Color32[rgba.Length / 4];
                    for (int i = 0; i < level.Length; i++) level[i] = new Color32(rgba[4 * i], rgba[4 * i + 1], rgba[4 * i + 2], rgba[4 * i + 3]);
                    levels.Add(level);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: {session.Path}: its OnPostprocessTexture gets the compressed import ({e.Message})");
                return false;
            }
            var subject = new Texture2D(texture.width, texture.height, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, levels.Count, !texture.isDataSRGB)
            {
                name = texture.name,
                filterMode = texture.filterMode,
                wrapModeU = texture.wrapModeU,
                wrapModeV = texture.wrapModeV,
                wrapModeW = texture.wrapModeW,
                anisoLevel = texture.anisoLevel,
                mipMapBias = texture.mipMapBias,
            };
            try
            {
                for (int mip = 0; mip < levels.Count; mip++) subject.SetPixels32(levels[mip], mip);
                AssetPostprocessorHooks.Run(hooks, session, new object[] { subject });
                // The sampler settings a postprocessor set are the import's.
                texture.filterMode = subject.filterMode;
                texture.wrapModeU = subject.wrapModeU;
                texture.wrapModeV = subject.wrapModeV;
                texture.wrapModeW = subject.wrapModeW;
                texture.anisoLevel = subject.anisoLevel;
                texture.mipMapBias = subject.mipMapBias;
                if (subject.width != texture.width || subject.height != texture.height || subject.mipmapCount != levels.Count)
                {
                    Debug.LogWarning($"Unbound: {session.Path}: a size or mip map count its OnPostprocessTexture gave the texture is not kept (unsupported); the import keeps its own");
                    return true;
                }
                if (Changed(subject, levels)) Recompress(session.Path, texture, subject);
            }
            catch (Exception e)
            {
                Debug.LogException(e, texture);
            }
            finally
            {
                Object.DestroyImmediate(subject);
            }
            return true;
        }

        /// <summary>Whether a texture of this imported format gets its pixels before compression:
        /// an 8-bit block format Unbound compresses (not crunched, not HDR).</summary>
        internal static bool BeforeCompressionApplies(TextureFormat format) =>
            BlockFormats.BlockBytes(format) > 0 && !BlockFormats.IsCrunch(format) && format != TextureFormat.BC6H && !BlockFormats.IsAstcHdr(format);

        static bool Changed(Texture2D subject, List<Color32[]> levels)
        {
            for (int mip = 0; mip < levels.Count; mip++)
            {
                var now = subject.GetPixels32(mip);
                if (now.Length != levels[mip].Length) return true;
                for (int i = 0; i < now.Length; i++)
                    if (!now[i].Equals(levels[mip][i])) return true;
            }
            return false;
        }

        /// <summary>The pixels a postprocessor changed, compressed into the import's format; a
        /// failure leaves the import as it was, with a warning.</summary>
        static void Recompress(string path, Texture2D texture, Texture2D pixels)
        {
            var blocks = new List<byte>();
            try
            {
                for (int mip = 0; mip < texture.mipmapCount; mip++)
                {
                    var level = pixels.GetPixels32(mip);
                    var rgba = new byte[level.Length * 4];
                    for (int i = 0; i < level.Length; i++)
                    {
                        rgba[4 * i] = level[i].r;
                        rgba[4 * i + 1] = level[i].g;
                        rgba[4 * i + 2] = level[i].b;
                        rgba[4 * i + 3] = level[i].a;
                    }
                    using var json = JsonDocument.Parse(Native.Call("texture-compress-blocks", JsonSerializer.Serialize(new
                    {
                        format = (int)texture.format,
                        width = Math.Max(1, texture.width >> mip),
                        height = Math.Max(1, texture.height >> mip),
                        import = true,
                        data = Convert.ToBase64String(rgba),
                    })));
                    blocks.AddRange(Convert.FromBase64String(json.RootElement.GetProperty("blocks").GetString()));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: {path}: the pixels its OnPostprocessTexture set are not kept: compressing them to {texture.format} failed ({e.Message})");
                return;
            }
            bool readable = texture.m_IsReadable;
            texture.m_IsReadable = true;
            try
            {
                texture.LoadRawTextureData(blocks.ToArray());
                texture.Apply(false, !readable);
            }
            catch (Exception e)
            {
                Debug.LogException(e, texture);
            }
            finally
            {
                texture.m_IsReadable = readable;
            }
        }

        /// <summary>A cubemap's postprocessors as it loads: OnPostprocessCubemap with the cubemap
        /// (pixels it sets are not uploaded: unmeasured).</summary>
        internal static void PostprocessCubemap(string path, Cubemap cubemap)
        {
            using var callbacks = AssetDatabaseRuntime.ImportCallbacks(AssetDatabase.AssetPathToGUID(path));
            var hooks = Hooks("OnPostprocessCubemap", typeof(Cubemap));
            if (hooks.Count > 0) AssetPostprocessorHooks.Run(hooks, Session(path), new object[] { cubemap });
            EndSession(path);
        }

        /// <summary>An audio clip's postprocessors as it loads: OnPostprocessAudio with the clip, which
        /// has no name yet (the Editor names it after the callback).</summary>
        internal static void PostprocessAudio(string path, AudioClip clip)
        {
            using var callbacks = AssetDatabaseRuntime.ImportCallbacks(AssetDatabase.AssetPathToGUID(path));
            var hooks = Hooks("OnPostprocessAudio", typeof(AudioClip));
            if (hooks.Count == 0)
            {
                EndSession(path);
                return;
            }
            var name = clip.name;
            clip.name = "";
            try { AssetPostprocessorHooks.Run(hooks, Session(path), new object[] { clip }); }
            finally
            {
                clip.name = name;
                EndSession(path);
            }
        }
    }
}
