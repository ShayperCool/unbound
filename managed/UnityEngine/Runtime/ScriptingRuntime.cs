using System.Collections.Generic;

namespace UnityEngine
{
    internal partial class ScriptingRuntime
    {
        /// <summary>The file names of the project's script assemblies, Editor ones included
        /// (measured: tests/projects/runtimeapis scripting_runtime.user_assemblies).</summary>
        public static string[] GetAllUserAssemblies()
        {
            var names = new List<string>();
            foreach (var assembly in Unbound.Runtime.GameDomain.Descriptions)
                if (assembly.PluginGuid == null) names.Add(assembly.Name + ".dll");
            return names.ToArray();
        }
    }
}
