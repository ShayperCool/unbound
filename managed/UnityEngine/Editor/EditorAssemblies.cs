using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbound.Runtime;

namespace UnityEditor
{
    /// <summary>The assemblies the Editor has loaded: Unbound's engine and the project's code.</summary>
    internal static partial class EditorAssemblies
    {
        internal static Assembly[] loadedAssemblies
        {
            get
            {
                var all = new List<Assembly> { typeof(UnityEngine.Object).Assembly };
                all.AddRange(GameDomain.Assemblies());
                return all.Distinct().ToArray();
            }
        }

        internal static IEnumerable<Type> loadedTypes => loadedAssemblies.SelectMany(Types);

        static Type[] Types(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); }
        }

        internal static IEnumerable<MethodInfo> GetAllMethodsWithAttribute<T>(BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) where T : Attribute =>
            loadedTypes.SelectMany(t =>
            {
                try { return t.GetMethods(bindingFlags | BindingFlags.DeclaredOnly); }
                catch (Exception) { return Array.Empty<MethodInfo>(); }
            }).Where(m =>
            {
                try { return m.IsDefined(typeof(T), false); }
                catch (Exception) { return false; }
            });
    }

    public sealed partial class EditorApplication
    {
        /// <summary>The interaction mode (how often the Editor updates when idle): Unbound updates every frame.</summary>
        internal static void UpdateInteractionModeSettings() { }

        internal static void UpdateTooltipsInPlayModeSettings() { }
    }
}
