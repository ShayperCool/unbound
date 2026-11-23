using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>The base of the project's classes that hear of asset changes through static
    /// methods (OnWillCreateAsset, ...).</summary>
    public partial class AssetModificationProcessor
    {
        public AssetModificationProcessor() { }
    }

    /// <summary>
    /// Calls the AssetModificationProcessor callbacks of the project's scripts. OnWillCreateAsset is
    /// called with the path of an asset about to be created and with the path of each .meta file
    /// about to be written for a new asset imported (as the Unity 6000.0.71f1 Editor does,
    /// tests/projects/projectwindowutil create.*), not yet for the .meta files of a refresh. The
    /// editability callbacks (IsOpenForEdit and CanOpenForEdit, each in a list and a single-path
    /// form, and MakeEditable) are called by the AssetDatabase queries as the Editor calls them
    /// (tests/projects/openforedithooks, openforeditorder). The other callbacks are not called yet.
    /// </summary>
    internal partial class AssetModificationProcessorInternal
    {
        public AssetModificationProcessorInternal() { }

        const BindingFlags CallbackFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Type[] s_ListQuery = { typeof(string[]), typeof(List<string>), typeof(StatusQueryOptions) };
        static readonly Type[] s_PathQuery = { typeof(string), typeof(string).MakeByRefType() };
        static readonly Type[] s_MakeEditable = { typeof(string[]), typeof(string), typeof(List<string>) };

        /// <summary>The processor classes a test sets in place of those TypeCache finds in the
        /// project (the test assembly is not a game assembly).</summary>
        internal static Type[] ProcessorsForTests;

        /// <summary>The project's processor classes, in TypeCache's order, which the Editor calls
        /// them in (Unity documents no order between processors). Asked each time: the classes
        /// change when scripts reload.</summary>
        static IEnumerable<Type> Processors =>
            (IEnumerable<Type>)ProcessorsForTests ?? TypeCache.GetTypesDerivedFrom<AssetModificationProcessor>();

        /// <summary>The processors' static bool callbacks of this name and these exact parameters
        /// (a list and a single-path form may be on one class).</summary>
        static IEnumerable<MethodInfo> Callbacks(string name, Type[] parameters)
        {
            foreach (var processor in Processors)
            {
                var method = processor.GetMethod(name, CallbackFlags, null, parameters, null);
                if (method != null && method.ReturnType == typeof(bool)) yield return method;
            }
        }

        /// <summary>Calls the callbacks until one returns false; whether none did.</summary>
        static bool AllAccept(string name, Type[] parameters, object[] arguments)
        {
            foreach (var method in Callbacks(name, parameters))
                if (!(bool)method.Invoke(null, arguments)) return false;
            return true;
        }

        /// <summary>An IsOpenForEdit or CanOpenForEdit query of versioned paths (neither blank, nor
        /// in an immutable package, nor outside the project's versioned folders): the list
        /// callbacks with all of them until one refuses; when all accept, each processor's
        /// single-path callback with every path, each refused path added to the list and its
        /// message kept (null as given), stopping after a processor that refused one.</summary>
        static bool Query(string name, string[] paths, List<string> outNotEditablePaths, StatusQueryOptions options, out string message)
        {
            message = "";
            if (!AllAccept(name, s_ListQuery, new object[] { paths, outNotEditablePaths, options })) return false;
            foreach (var method in Callbacks(name, s_PathQuery))
            {
                bool refused = false;
                foreach (string path in paths)
                {
                    var arguments = new object[] { path, null };
                    if ((bool)method.Invoke(null, arguments)) continue;
                    outNotEditablePaths.Add(path);
                    message = (string)arguments[1];
                    refused = true;
                }
                if (refused) return false;
            }
            return true;
        }

        internal static bool IsOpenForEdit(string[] assetOrMetaFilePaths, List<string> outNotEditablePaths, StatusQueryOptions statusQueryOptions) =>
            Query("IsOpenForEdit", assetOrMetaFilePaths, outNotEditablePaths, statusQueryOptions, out _);

        internal static bool IsOpenForEdit(string assetPath, out string message, StatusQueryOptions statusOptions) =>
            Query("IsOpenForEdit", new[] { assetPath }, new List<string>(), statusOptions, out message);

        internal static bool CanOpenForEdit(string[] assetOrMetaFilePaths, List<string> outNotEditablePaths, StatusQueryOptions statusQueryOptions) =>
            Query("CanOpenForEdit", assetOrMetaFilePaths, outNotEditablePaths, statusQueryOptions, out _);

        /// <summary>One path: open (by the IsOpenForEdit callbacks, their message dropped), or
        /// else as the CanOpenForEdit callbacks answer, with their message.</summary>
        internal static bool CanOpenForEdit(string assetPath, out string message, StatusQueryOptions statusOptions)
        {
            message = "";
            return IsOpenForEdit(assetPath, out _, statusOptions)
                || Query("CanOpenForEdit", new[] { assetPath }, new List<string>(), statusOptions, out message);
        }

        /// <summary>The MakeEditable callbacks with the paths, prompt and list as given, until one
        /// refuses.</summary>
        internal static bool MakeEditable(string[] paths, string prompt, List<string> outNotEditablePaths) =>
            AllAccept("MakeEditable", s_MakeEditable, new object[] { paths, prompt, outNotEditablePaths });

        /// <summary>Calls each processor's static OnWillCreateAsset(string); one with other
        /// parameters is skipped with a warning.</summary>
        internal static void OnWillCreateAsset(string path) => OnWillCreateAsset(path, Processors);

        internal static void OnWillCreateAsset(string path, System.Collections.Generic.IEnumerable<Type> processors)
        {
            foreach (var processor in processors)
            {
                var method = processor.GetMethod("OnWillCreateAsset", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null) continue;
                if (!ArgumentsMatch(new[] { typeof(string) }, method)) continue;
                method.Invoke(null, new object[] { path });
            }
        }

        static bool ArgumentsMatch(Type[] types, MethodInfo method)
        {
            var parameters = method.GetParameters();
            if (types.Length != parameters.Length)
            {
                Debug.LogWarning("Parameter count did not match. Expected: " + types.Length + " Got: " + parameters.Length + " in " + method.DeclaringType + "." + method.Name);
                return false;
            }
            for (int i = 0; i < types.Length; i++)
            {
                if (types[i] == parameters[i].ParameterType) continue;
                Debug.LogWarning("Parameter type mismatch at parameter " + i + ". Expected: " + types[i] + " Got: " + parameters[i].ParameterType + " in " + method.DeclaringType + "." + method.Name);
                return false;
            }
            return true;
        }
    }
}
