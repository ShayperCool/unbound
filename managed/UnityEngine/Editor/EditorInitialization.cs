using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// What the Editor runs when it enters Play mode, before the game's own initialization:
    /// <c>[InitializeOnLoad]</c> static constructors and <c>[InitializeOnLoadMethod]</c> methods
    /// (the editor domain loading), <c>playModeStateChanged(ExitingEditMode)</c>, and
    /// <c>[InitializeOnEnterPlayMode]</c> methods; after the first scene,
    /// <c>playModeStateChanged(EnteredPlayMode)</c>. Packages prepare their Play mode data here
    /// (Addressables builds its "Use Asset Database" locator, for one).
    /// </summary>
    internal static class EditorInitialization
    {
        const string InitializeOnLoad = "UnityEditor.InitializeOnLoadAttribute";
        const string InitializeOnLoadMethod = "UnityEditor.InitializeOnLoadMethodAttribute";
        const string InitializeOnEnterPlayMode = "UnityEditor.InitializeOnEnterPlayModeAttribute";

        internal static void BeforePlayMode()
        {
            // The editor domain loads: classes first, then methods, assembly by assembly.
            ForEachType(type =>
            {
                if (!Has(type, InitializeOnLoad)) return;
                try { RuntimeHelpers.RunClassConstructor(type.TypeHandle); }
                catch (TypeInitializationException e) { Debug.LogException(e.InnerException ?? e); }
                catch (Exception e) { Debug.LogException(e); }
            });
            ForEachStaticMethod(InitializeOnLoadMethod, method => Invoke(method));
            EditorApplication.Internal_PlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
            ForEachStaticMethod(InitializeOnEnterPlayMode, method =>
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 0) Invoke(method);
                else if (parameters.Length == 1 && parameters[0].ParameterType.IsEnum)
                    Invoke(method, Enum.ToObject(parameters[0].ParameterType, 0)); // EnterPlayModeOptions.None
            });
        }

        internal static void AfterFirstScene() =>
            EditorApplication.Internal_PlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);

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
                MethodInfo[] methods;
                try { methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
                catch (Exception) { return; }
                foreach (var method in methods)
                    if (!method.ContainsGenericParameters && Has(method, attribute)) action(method);
            });
        }
    }
}
