using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>The loaded AssetPostprocessor classes that declare an instance hook.</summary>
    internal static class AssetPostprocessorHooks
    {
        /// <summary>The postprocessor types (<paramref name="first"/>, then the game's assemblies')
        /// declaring the instance method <paramref name="name"/> with these parameters, by
        /// increasing <c>GetPostprocessOrder</c> (ties by type name). Unbound's stand-ins for
        /// Editor classes it does not implement are left out; a postprocessor that cannot be made
        /// is logged and left out.</summary>
        internal static List<(int Order, Type Type, MethodInfo Method)> Find(string name, Type[] parameters, params Type[] first)
        {
            var found = new List<(int, Type, MethodInfo)>();
            void Consider(Type type)
            {
                if (type.IsAbstract || !typeof(AssetPostprocessor).IsAssignableFrom(type)) return;
                if (type.IsDefined(typeof(StubAttribute), false)) return;
                var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parameters, null);
                if (method == null) return;
                try
                {
                    var instance = (AssetPostprocessor)Activator.CreateInstance(type, nonPublic: true);
                    found.Add((instance.GetPostprocessOrder(), type, method));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            foreach (var type in first) Consider(type);
            foreach (var assembly in GameDomain.Assemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
                foreach (var type in types)
                    if (Array.IndexOf(first, type) < 0) Consider(type);
            }
            found.Sort((a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1)
                : string.CompareOrdinal(a.Item2.FullName, b.Item2.FullName));
            return found;
        }

        /// <summary>Runs each hook on a new postprocessor of its type with this asset path and
        /// context; a hook's exception is logged and the next one runs.</summary>
        internal static void Run(List<(int Order, Type Type, MethodInfo Method)> hooks, string assetPath,
            UnityEditor.AssetImporters.AssetImportContext context, object[] arguments)
        {
            foreach (var (_, type, method) in hooks)
            {
                try
                {
                    var postprocessor = (AssetPostprocessor)Activator.CreateInstance(type, nonPublic: true);
                    postprocessor.assetPath = assetPath;
                    postprocessor.context = context;
                    method.Invoke(postprocessor, arguments);
                }
                catch (TargetInvocationException e)
                {
                    Debug.LogException(e.InnerException ?? e);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }
}
