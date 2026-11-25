using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unbound.Runtime;
using UnityEditor.Build;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEditor
{
    /// <summary>
    /// What the Editor runs when it enters Play mode, before the game's own initialization: the
    /// editor domain loads while <c>isPlaying</c> is still false — <c>[InitializeOnLoad]</c> static
    /// constructors, <c>[InitializeOnEnterPlayMode]</c> methods, <c>[InitializeOnLoadMethod]</c>
    /// methods and <c>[DidReloadScripts]</c> methods by callback order, in that order (measured:
    /// tests/projects/editorcallbacks) — with <c>playModeStateChanged(ExitingEditMode)</c>, which the
    /// Editor sends from the domain before the reload, sent once the domain's handlers exist; after
    /// the first frame, <c>playModeStateChanged(EnteredPlayMode)</c>. Packages prepare their Play mode
    /// data here (Addressables builds its "Use Asset Database" locator, for one). The main window
    /// exists first, as the Editor's does. Each scene loaded in Play mode is processed by the
    /// <c>IProcessSceneWithReport</c>/<c>IProcessScene</c> implementations and
    /// <c>[PostProcessScene]</c> methods (<see cref="ProcessScene"/>).
    /// </summary>
    internal static class EditorInitialization
    {
        const string InitializeOnLoad = "UnityEditor.InitializeOnLoadAttribute";
        const string InitializeOnLoadMethod = "UnityEditor.InitializeOnLoadMethodAttribute";
        const string InitializeOnEnterPlayMode = "UnityEditor.InitializeOnEnterPlayModeAttribute";
        /// <summary>Set by the host for a session it relaunched for a script edit (crates/unbound).</summary>
        const string ScriptReloadVariable = "UNBOUND_SCRIPT_RELOAD";
        static bool? s_ContinuesScriptReload;

        /// <summary>Whether this session was relaunched for a script edit: read once, and the variable
        /// cleared from the managed environment, so that processes started from managed code do not
        /// inherit it (native code and the host still see it; nothing else reads it).</summary>
        internal static bool ContinuesScriptReload()
        {
            if (s_ContinuesScriptReload is bool known) return known;
            s_ContinuesScriptReload = Environment.GetEnvironmentVariable(ScriptReloadVariable) == "1";
            Environment.SetEnvironmentVariable(ScriptReloadVariable, null);
            return s_ContinuesScriptReload.Value;
        }

        internal static void BeforePlayMode()
        {
            // Enter Play Mode Options that keep the domain: the Editor's domain is the one it loaded
            // at its start, in Edit mode; entering Play mode then reloads nothing (matches the Unity
            // 6000.0.71f1 Editor, tests/projects/compileeventsnoreload start.load).
            if ((EditorSettings.ActiveEnterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0)
            {
                // Read (and cleared) here too: nothing reloads, but the variable must not outlive the start.
                ContinuesScriptReload();
                // The start's load runs in Edit mode: isPlaying and isPlayingOrWillChangePlaymode false.
                EditorSession.IsPlaying = false;
                LoadEditorDomain();
                EditorSession.BeginEnteringPlayMode();
                UnityEngine.U2D.SpriteAtlasPacking.PackV1ForPlayMode();
                try
                {
                    EditorApplication.Internal_PlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
                    // [InitializeOnEnterPlayMode] runs as Play mode is entered, as on the reload path.
                    PlayModeDomainLoading = true;
                    try { EnterPlayModeMethods(); }
                    finally { PlayModeDomainLoading = false; }
                }
                finally { EditorSession.EndEnteringPlayMode(); }
                return;
            }
            EditorSession.BeginEnteringPlayMode();
            // Play mode's atlases are packed before its domain loads.
            UnityEngine.U2D.SpriteAtlasPacking.PackV1ForPlayMode();
            try { LoadEditorDomain(enteringPlayMode: true); }
            finally { EditorSession.EndEnteringPlayMode(); }
        }

        /// <summary>The editor domain loads: the main window, then [InitializeOnLoad] classes,
        /// (entering Play mode) [InitializeOnEnterPlayMode] methods, [InitializeOnLoadMethod]
        /// methods, (entering Play mode) ExitingEditMode, [DidReloadScripts] methods, and
        /// afterAssemblyReload.</summary>
        internal static void LoadEditorDomain(bool enteringPlayMode = false)
        {
            MainView.Open();
            DomainLoading = true;
            try
            {
                PlayModeDomainLoading = enteringPlayMode;
                // [InitializeOnLoad] classes by assembly: the assemblies by name, each after those
                // its code references (matches the Unity 6000.0.71f1 Editor,
                // tests/projects/initializeorder order.initializers).
                foreach (var assembly in InDependencyOrder(GameDomain.Assemblies()))
                {
                    foreach (var type in TypesOf(assembly))
                    {
                        if (!Has(type, InitializeOnLoad)) continue;
                        try { RuntimeHelpers.RunClassConstructor(type.TypeHandle); }
                        catch (TypeInitializationException e) { Debug.LogException(e.InnerException ?? e); }
                        catch (Exception e) { Debug.LogException(e); }
                    }
                }
                if (enteringPlayMode) EnterPlayModeMethods();
                ForEachStaticMethod(InitializeOnLoadMethod, method => Invoke(method));
            }
            catch
            {
                DomainLoading = false;
                throw;
            }
            finally { PlayModeDomainLoading = false; }
            // The Editor sends it before the domain reloads, in Edit mode, then beforeAssemblyReload when
            // entering Play mode reloads the domain (tests/projects/compileevents start.load).
            if (enteringPlayMode)
            {
                // The Editor sends these before its reload, outside the domain load: their imports
                // run at once.
                DomainLoading = false;
                EditorApplication.Internal_PlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
                // A session restarted for a script edit continues that reload (its old session raised
                // beforeAssemblyReload). Options that keep the domain do not come here (BeforePlayMode).
                if (!ContinuesScriptReload()) UnityEditor.AssemblyReloadEvents.RaiseBefore();
            }
            try
            {
                PlayModeDomainLoading = enteringPlayMode;
                DomainLoading = true;
                DidReloadScriptsMethods();
            }
            finally
            {
                PlayModeDomainLoading = false;
                DomainLoading = false;
            }
            // Every load of the editor domain ends with afterAssemblyReload, after its load callbacks,
            // [DidReloadScripts] included (and so after Play mode entry's ExitingEditMode, which the
            // Editor sends before the reload): the Editor's start, its Play mode entry, and a session
            // an edit restarted (matches the Unity 6000.0.71f1 Editor, tests/projects/compileevents
            // start.load and round*.order).
            UnityEditor.AssemblyReloadEvents.RaiseAfter();
            // The import of the opened project runs the models' preprocessors (scenes expand models
            // natively), then every domain load calls OnPostprocessAllAssets (didDomainReload),
            // before the runtime initializers (tests/projects/assetpostprocessors timeline).
            AssetPostprocessing.PrepareImports();
            // Imports asked for while the domain loaded run now, after afterAssemblyReload, and
            // the domain load's OnPostprocessAllAssets lists the assets they imported (matches the
            // Unity 6000.0.71f1 Editor, tests/projects/initializeorder game.sequence).
            AssetPostprocessing.DomainLoaded(RunDeferredImports());
        }

        /// <summary>While the editor domain's load callbacks run (its initializers, on the reload path
        /// [InitializeOnEnterPlayMode], [InitializeOnLoadMethod] and [DidReloadScripts] methods; not
        /// ExitingEditMode or beforeAssemblyReload, which the Editor sends before its reload),
        /// imports wait until the load has ended.</summary>
        internal static bool DomainLoading { get; private set; }

        /// <summary>The work the domain load put off, in the order asked: imports, and the
        /// postprocessing of assets it loaded.</summary>
        static readonly List<Action> s_Deferred = new List<Action>();
        static readonly List<string> s_DeferredImported = new List<string>();

        /// <summary>Keeps an import asked for while the domain loads; false at any other time.</summary>
        internal static bool DeferImport(string path, ImportAssetOptions options)
        {
            if (!DomainLoading) return false;
            s_Deferred.Add(() =>
            {
                AssetDatabase.ImportAsset(path, options);
                if ((options & ImportAssetOptions.ForceUpdate) != 0 && !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)) && !s_DeferredImported.Contains(path))
                    s_DeferredImported.Add(path);
            });
            return true;
        }

        /// <summary>Keeps the postprocessing of an asset Unbound loads while the domain loads (its
        /// replay of the asset's import: the Editor loads imported assets without postprocessing),
        /// so its postprocessors see every initializer's state, where the Editor runs the imports a
        /// load asks for; false at any other time.</summary>
        internal static bool DeferPostprocess(Action postprocess)
        {
            if (!DomainLoading) return false;
            s_Deferred.Add(postprocess);
            return true;
        }

        /// <summary>Runs the work the domain load put off, in the order asked; the paths of the
        /// assets its imports imported (forced imports of existing assets).</summary>
        static string[] RunDeferredImports()
        {
            if (s_Deferred.Count == 0) return Array.Empty<string>();
            var work = s_Deferred.ToArray();
            s_Deferred.Clear();
            s_DeferredImported.Clear();
            foreach (var action in work)
            {
                try { action(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            var imported = s_DeferredImported.ToArray();
            s_DeferredImported.Clear();
            return imported;
        }

        /// <summary>The assemblies by name (ordinal), each moved after those it references among
        /// them (the references its compiled code keeps); a reference cycle is broken where it
        /// closes.</summary>
        internal static List<Assembly> InDependencyOrder(IReadOnlyList<Assembly> given)
        {
            var assemblies = new List<Assembly>(given);
            assemblies.Sort((a, b) => string.CompareOrdinal(a.GetName().Name, b.GetName().Name));
            var byName = new Dictionary<string, int>(assemblies.Count);
            for (int i = 0; i < assemblies.Count; i++) byName[assemblies[i].GetName().Name] = i;
            var state = new byte[assemblies.Count];
            var result = new List<Assembly>(assemblies.Count);
            void Visit(int index)
            {
                if (state[index] != 0) return;
                state[index] = 1;
                foreach (var reference in assemblies[index].GetReferencedAssemblies())
                    if (reference.Name != null && byName.TryGetValue(reference.Name, out var other)) Visit(other);
                state[index] = 2;
                result.Add(assemblies[index]);
            }
            for (int i = 0; i < assemblies.Count; i++) Visit(i);
            return result;
        }

        /// <summary>The editor domain's load callbacks run as Play mode is entered (in the Editor,
        /// in the domain reloaded for Play mode; not ExitingEditMode, which the Editor sends before
        /// the reload).</summary>
        internal static bool PlayModeDomainLoading { get; private set; }

        /// <summary>[InitializeOnEnterPlayMode] methods, as Play mode is entered.</summary>
        internal static void EnterPlayModeMethods()
        {
            ForEachStaticMethod(InitializeOnEnterPlayMode, method =>
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 0) Invoke(method);
                else if (parameters.Length == 1 && parameters[0].ParameterType.IsEnum)
                    Invoke(method, Enum.ToObject(parameters[0].ParameterType, (int)EditorSettings.ActiveEnterPlayModeOptions)); // the options Play mode is entered with
            });
        }

        /// <summary>[DidReloadScripts] methods after the domain's scripts loaded: by callback order,
        /// methods of equal order as the assemblies list them.</summary>
        static void DidReloadScriptsMethods()
        {
            var methods = new List<OrderedMethod>();
            ForEachType(type =>
            {
                foreach (var method in StaticMethods(type))
                    if (TryGetAttribute(method, out DidReloadScripts attribute)) methods.Add(new OrderedMethod(attribute.callbackOrder, method));
            });
            methods.Sort(CompareOrder);
            foreach (var m in methods) Invoke(m.Method);
        }

        /// <summary>Play mode counts as entered at the end of its first frame, after LateUpdate and
        /// before the Editor's own updates (measured in tests/projects/input).</summary>
        internal static void AfterFirstFrame() =>
            EditorApplication.Internal_PlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);

        interface IOrdered
        {
            int Order { get; }
        }

        readonly struct OrderedMethod : IOrdered
        {
            internal readonly MethodInfo Method;
            public int Order { get; }
            internal OrderedMethod(int order, MethodInfo method) { Order = order; Method = method; }
        }

        /// <summary>A scene processor: an IProcessSceneWithReport or IProcessScene instance, or a
        /// [PostProcessScene] method.</summary>
        readonly struct SceneProcessor : IOrdered
        {
            internal readonly object Instance;
            internal readonly MethodInfo Method;
            public int Order { get; }
            internal SceneProcessor(int order, object instance, MethodInfo method) { Order = order; Instance = instance; Method = method; }
        }

        static int CompareOrder<T>(T a, T b) where T : IOrdered => a.Order - b.Order;

        static List<SceneProcessor> s_SceneProcessors;

        /// <summary>
        /// A scene loaded in Play mode, after its objects' Awake and OnEnable and before
        /// activeSceneChanged and sceneLoaded: every IProcessSceneWithReport (with a null report) and
        /// IProcessScene implementation and [PostProcessScene] method, by callback order; of equal
        /// order, the implementations as the assemblies list them, then the methods. An exception is
        /// logged and the others still run (measured: tests/projects/editorcallbacks).
        /// </summary>
        internal static void ProcessScene(Scene scene)
        {
            s_SceneProcessors ??= SceneProcessors();
            foreach (var processor in s_SceneProcessors)
            {
                try
                {
                    if (processor.Instance is IProcessSceneWithReport withReport) withReport.OnProcessScene(scene, null);
#pragma warning disable 618
                    else if (processor.Instance is IProcessScene legacy) legacy.OnProcessScene(scene);
#pragma warning restore 618
                    else processor.Method.Invoke(null, null);
                }
                catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        static List<SceneProcessor> SceneProcessors()
        {
            var processors = new List<SceneProcessor>();
            ForEachType(type =>
            {
#pragma warning disable 618
                if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters
                    || !(typeof(IProcessSceneWithReport).IsAssignableFrom(type) || typeof(IProcessScene).IsAssignableFrom(type))) return;
#pragma warning restore 618
                try
                {
                    var instance = (IOrderedCallback)Activator.CreateInstance(type, nonPublic: true);
                    processors.Add(new SceneProcessor(instance.callbackOrder, instance, null));
                }
                catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
                catch (Exception e) { Debug.LogException(e); }
            });
            ForEachType(type =>
            {
                foreach (var method in StaticMethods(type))
                    if (TryGetAttribute(method, out PostProcessSceneAttribute attribute))
                        processors.Add(new SceneProcessor(attribute.callbackOrder, null, method));
            });
            processors.Sort(CompareOrder);
            return processors;
        }

        static bool TryGetAttribute<T>(MethodInfo method, out T attribute) where T : Attribute
        {
            attribute = null;
            if (method.ContainsGenericParameters) return false;
            try { attribute = method.GetCustomAttribute<T>(false); }
            catch (Exception)
            {
                // An attribute type that does not load: not one of ours.
            }
            return attribute != null;
        }

        static MethodInfo[] StaticMethods(Type type)
        {
            try { return type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
            catch (Exception) { return Array.Empty<MethodInfo>(); }
        }

        static void Invoke(MethodInfo method, params object[] arguments)
        {
            try { method.Invoke(null, arguments.Length == 0 ? null : arguments); }
            catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static bool Has(MemberInfo member, string attribute)
        {
            try
            {
                foreach (var data in member.CustomAttributes)
                    if (data.AttributeType.FullName == attribute) return true;
            }
            catch (Exception)
            {
                // An attribute type that does not load: not one of ours.
            }
            return false;
        }

        static void ForEachType(Action<Type> action)
        {
            foreach (var assembly in GameDomain.Assemblies())
                foreach (var type in TypesOf(assembly)) action(type);
        }

        static Type[] TypesOf(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return Array.FindAll(e.Types, t => t != null); }
        }

        static void ForEachStaticMethod(string attribute, Action<MethodInfo> action)
        {
            ForEachType(type =>
            {
                foreach (var method in StaticMethods(type))
                    if (!method.ContainsGenericParameters && Has(method, attribute)) action(method);
            });
        }
    }
}
