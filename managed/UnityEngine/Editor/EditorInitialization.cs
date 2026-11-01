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

        internal static void BeforePlayMode()
        {
            EditorSession.BeginEnteringPlayMode();
            try { LoadEditorDomain(enteringPlayMode: true); }
            finally { EditorSession.EndEnteringPlayMode(); }
        }

        /// <summary>The editor domain loads: the main window, then [InitializeOnLoad] classes,
        /// (entering Play mode) [InitializeOnEnterPlayMode] methods, [InitializeOnLoadMethod]
        /// methods, (entering Play mode) ExitingEditMode, and [DidReloadScripts] methods.</summary>
        internal static void LoadEditorDomain(bool enteringPlayMode = false)
        {
            MainView.Open();
            ForEachType(type =>
            {
                if (!Has(type, InitializeOnLoad)) return;
                try { RuntimeHelpers.RunClassConstructor(type.TypeHandle); }
                catch (TypeInitializationException e) { Debug.LogException(e.InnerException ?? e); }
                catch (Exception e) { Debug.LogException(e); }
            });
            if (enteringPlayMode) EnterPlayModeMethods();
            ForEachStaticMethod(InitializeOnLoadMethod, method => Invoke(method));
            if (enteringPlayMode) EditorApplication.Internal_PlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
            DidReloadScriptsMethods();
        }

        /// <summary>[InitializeOnEnterPlayMode] methods, as Play mode is entered.</summary>
        internal static void EnterPlayModeMethods()
        {
            ForEachStaticMethod(InitializeOnEnterPlayMode, method =>
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 0) Invoke(method);
                else if (parameters.Length == 1 && parameters[0].ParameterType.IsEnum)
                    Invoke(method, Enum.ToObject(parameters[0].ParameterType, 0)); // EnterPlayModeOptions.None
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
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
                foreach (var type in types) action(type);
            }
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
