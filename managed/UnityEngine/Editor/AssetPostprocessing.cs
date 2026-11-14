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

        /// <summary>Calls <c>OnPostprocessAllAssets</c> of the postprocessor classes of an
        /// assembly with <paramref name="imported"/> as the imported assets (nothing deleted or
        /// moved, no domain reload), by increasing postprocess order.</summary>
        internal static void ImportedAssets(Assembly assembly, string[] imported)
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
                int order = 0;
                try { order = ((AssetPostprocessor)Activator.CreateInstance(type, nonPublic: true)).GetPostprocessOrder(); }
                catch (Exception e) { Debug.LogException(e); }
                callbacks.Add((order, method));
            }
            // A stable order among equal postprocess orders: the assembly's declaration order.
            for (int i = 1; i < callbacks.Count; i++)
                for (int j = i; j > 0 && callbacks[j - 1].Order > callbacks[j].Order; j--)
                    (callbacks[j - 1], callbacks[j]) = (callbacks[j], callbacks[j - 1]);
            var none = Array.Empty<string>();
            foreach (var (_, method) in callbacks)
            {
                var parameters = method.GetParameters();
                object[] arguments;
                if (parameters.Length == 4) arguments = new object[] { imported, none, none, none };
                else if (parameters.Length == 5 && parameters[4].ParameterType == typeof(bool)) arguments = new object[] { imported, none, none, none, false };
                else continue;
                try { method.Invoke(null, arguments); }
                catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
