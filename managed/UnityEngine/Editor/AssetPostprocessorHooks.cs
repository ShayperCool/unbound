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
        /// increasing <c>GetPostprocessOrder</c> (ties by class name). Unbound's stand-ins for
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
            found.Sort((a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : ByClassName(a.Item2, b.Item2));
            return found;
        }

        /// <summary>Postprocessors of an equal order run by class name (matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/assetpostprocessors), then full name (unmeasured).</summary>
        internal static int ByClassName(Type a, Type b)
        {
            int byName = string.CompareOrdinal(a.Name, b.Name);
            return byName != 0 ? byName : string.CompareOrdinal(a.FullName, b.FullName);
        }

        /// <summary>Runs each hook on a new postprocessor of its type with this asset path, context
        /// and importer (outside an asset's import: <see cref="UnityEditor.TextureGenerator"/>).</summary>
        internal static object Run(List<(int Order, Type Type, MethodInfo Method)> hooks, string assetPath,
            UnityEditor.AssetImporters.AssetImportContext context, object[] arguments, AssetImporter importer = null) =>
            Run(hooks, new ImportSession(assetPath, context, importer), arguments);

        /// <summary>Runs each hook on the import's postprocessor of its type; a hook's exception is
        /// logged and the next one runs. Returns the last hook's result; with
        /// <paramref name="untilResult"/> the first one that is not null ends the calls (the Editor's
        /// OnAssignMaterialModel: tests/projects/assetpostprocessors PPInstance).</summary>
        internal static object Run(List<(int Order, Type Type, MethodInfo Method)> hooks, ImportSession import, object[] arguments,
            bool untilResult = false)
        {
            object result = null;
            foreach (var (_, type, method) in hooks)
            {
                try
                {
                    result = method.Invoke(import.Instance(type), arguments);
                }
                catch (TargetInvocationException e)
                {
                    Debug.LogException(e.InnerException ?? e);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                if (untilResult && result is UnityEngine.Object o && o != null) break;
            }
            return result;
        }
    }

    /// <summary>One asset's import: the postprocessor of each class every callback of the import
    /// is called on (preprocessors included), with one context (matches the Unity 6000.0.71f1
    /// Editor, tests/projects/assetpostprocessors PPInstance: one object counts every callback of
    /// an import, a later import of the asset makes new ones).</summary>
    internal sealed class ImportSession
    {
        internal readonly string Path;
        internal readonly UnityEditor.AssetImporters.AssetImportContext Context;
        internal readonly AssetImporter Importer;
        readonly Dictionary<Type, AssetPostprocessor> m_Postprocessors = new Dictionary<Type, AssetPostprocessor>();

        internal ImportSession(string path, UnityEditor.AssetImporters.AssetImportContext context, AssetImporter importer)
        {
            Path = path;
            Context = context;
            Importer = importer;
        }

        internal AssetPostprocessor Instance(Type type)
        {
            if (m_Postprocessors.TryGetValue(type, out var postprocessor)) return postprocessor;
            postprocessor = (AssetPostprocessor)Activator.CreateInstance(type, nonPublic: true);
            postprocessor.assetPath = Path;
            postprocessor.context = Context;
            postprocessor.m_AssetImporter = Importer;
            m_Postprocessors[type] = postprocessor;
            return postprocessor;
        }
    }
}
