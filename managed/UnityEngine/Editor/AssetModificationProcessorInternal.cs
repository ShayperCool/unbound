using System;
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
    /// about to be written for a new asset (as the Unity 6000.0.71f1 Editor does,
    /// tests/projects/projectwindowutil create.*). The other callbacks are not called yet.
    /// </summary>
    internal partial class AssetModificationProcessorInternal
    {
        public AssetModificationProcessorInternal() { }

        /// <summary>Calls each processor's static OnWillCreateAsset(string); one with other
        /// parameters is skipped with a warning.</summary>
        internal static void OnWillCreateAsset(string path)
        {
            // Asked each time (asset creation is rare): the classes change when scripts reload.
            foreach (var processor in TypeCache.GetTypesDerivedFrom<AssetModificationProcessor>())
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
