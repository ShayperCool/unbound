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
    /// editability callbacks (IsOpenForEdit in its list and single-path forms, CanOpenForEdit,
    /// MakeEditable) are called by the AssetDatabase queries as tests/projects/openforedithooks
    /// measures: each kind processor after processor until one refuses. The other callbacks are
    /// not called yet.
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

        /// <summary>The processors' static bool callbacks of this name and these exact parameters
        /// (both IsOpenForEdit forms may be on one class), in TypeCache's order as the Editor calls
        /// them (Unity documents no order between processors). Asked each time, as
        /// OnWillCreateAsset: the classes change when scripts reload.</summary>
        static IEnumerable<MethodInfo> Callbacks(string name, Type[] parameters)
        {
            foreach (var processor in (IEnumerable<Type>)ProcessorsForTests ?? TypeCache.GetTypesDerivedFrom<AssetModificationProcessor>())
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

        /// <summary>The list IsOpenForEdit callbacks for paths that are neither blank nor in an
        /// immutable package; they add the paths not open to the list.</summary>
        internal static bool IsOpenForEdit(string[] assetOrMetaFilePaths, List<string> outNotEditablePaths, StatusQueryOptions statusQueryOptions) =>
            AllAccept("IsOpenForEdit", s_ListQuery, new object[] { assetOrMetaFilePaths, outNotEditablePaths, statusQueryOptions });

        /// <summary>One path's IsOpenForEdit callbacks: the list ones with it and a new list, then
        /// the single-path ones, whose message is the answer's.</summary>
        internal static bool IsOpenForEdit(string assetPath, out string message, StatusQueryOptions statusOptions)
        {
            message = "";
            if (!IsOpenForEdit(new[] { assetPath }, new List<string>(), statusOptions)) return false;
            foreach (var method in Callbacks("IsOpenForEdit", s_PathQuery))
            {
                var arguments = new object[] { assetPath, null };
                if ((bool)method.Invoke(null, arguments)) continue;
                message = (string)arguments[1] ?? "";
                return false;
            }
            return true;
        }

        /// <summary>The CanOpenForEdit callbacks for the paths that are not open.</summary>
        internal static bool CanOpenForEdit(string[] assetOrMetaFilePaths, List<string> outNotEditablePaths, StatusQueryOptions statusQueryOptions) =>
            AllAccept("CanOpenForEdit", s_ListQuery, new object[] { assetOrMetaFilePaths, outNotEditablePaths, statusQueryOptions });

        /// <summary>One path: open (by the IsOpenForEdit callbacks), or else accepted by the
        /// CanOpenForEdit callbacks. The message stays empty.</summary>
        internal static bool CanOpenForEdit(string assetPath, out string message, StatusQueryOptions statusOptions)
        {
            message = "";
            return IsOpenForEdit(assetPath, out _, statusOptions)
                || CanOpenForEdit(new[] { assetPath }, new List<string>(), statusOptions);
        }

        /// <summary>The MakeEditable callbacks with the paths, prompt and list as given.</summary>
        internal static bool MakeEditable(string[] paths, string prompt, List<string> outNotEditablePaths) =>
            AllAccept("MakeEditable", s_MakeEditable, new object[] { paths, prompt, outNotEditablePaths });

        /// <summary>Calls each processor's static OnWillCreateAsset(string); one with other
        /// parameters is skipped with a warning.</summary>
        internal static void OnWillCreateAsset(string path) =>
            // Asked each time (asset creation is rare): the classes change when scripts reload.
            OnWillCreateAsset(path, TypeCache.GetTypesDerivedFrom<AssetModificationProcessor>());

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
