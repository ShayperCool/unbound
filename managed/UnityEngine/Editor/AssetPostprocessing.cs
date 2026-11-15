using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// The Editor's post-import pass: after assets import, every <see cref="AssetPostprocessor"/>
    /// class's static <c>OnPostprocessAllAssets</c> is told their paths. Unbound reads assets as
    /// they load and has no such pass in general; an importer whose package finishes its import
    /// in that callback asks for it, for that package's postprocessors.
    /// </summary>
    internal static class AssetPostprocessing
    {
        const string Callback = "OnPostprocessAllAssets";

        /// <summary>Per assembly: its postprocessors' callbacks in calling order, found once and
        /// forgotten with the assembly (a reloaded game domain's assemblies are new ones).</summary>
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Assembly, MethodInfo[]> s_Callbacks =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Assembly, MethodInfo[]>();

        /// <summary>Calls <c>OnPostprocessAllAssets</c> of the postprocessor classes of an
        /// assembly with <paramref name="imported"/> as the imported assets (nothing deleted or
        /// moved, no domain reload), by increasing postprocess order.</summary>
        internal static void ImportedAssets(Assembly assembly, string[] imported)
        {
            var methods = s_Callbacks.GetValue(assembly, Callbacks);
            var none = Array.Empty<string>();
            foreach (var method in methods)
            {
                var arguments = method.GetParameters().Length == 4
                    ? new object[] { imported, none, none, none }
                    : new object[] { imported, none, none, none, false };
                try { method.Invoke(null, arguments); }
                catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        static MethodInfo[] Callbacks(Assembly assembly)
        {
            var callbacks = new List<(int Order, MethodInfo Method)>();
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            foreach (var type in types)
            {
                if (type == null || type.IsAbstract || !typeof(AssetPostprocessor).IsAssignableFrom(type)) continue;
                var method = type.GetMethod(Callback, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method == null) continue;
                var parameters = method.GetParameters();
                if (parameters.Length != 4 && !(parameters.Length == 5 && parameters[4].ParameterType == typeof(bool))) continue;
                int order = 0;
                try { order = ((AssetPostprocessor)Activator.CreateInstance(type, nonPublic: true)).GetPostprocessOrder(); }
                catch (Exception e) { Debug.LogException(e); }
                callbacks.Add((order, method));
            }
            // A stable order among equal postprocess orders: the assembly's declaration order.
            for (int i = 1; i < callbacks.Count; i++)
                for (int j = i; j > 0 && callbacks[j - 1].Order > callbacks[j].Order; j--)
                    (callbacks[j - 1], callbacks[j]) = (callbacks[j], callbacks[j - 1]);
            var ordered = new MethodInfo[callbacks.Count];
            for (int i = 0; i < ordered.Length; i++) ordered[i] = callbacks[i].Method;
            return ordered;
        }
    }
}
