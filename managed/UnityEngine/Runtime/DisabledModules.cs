using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Unbound.Runtime
{
    /// <summary>
    /// The engine modules whose built-in package (<c>com.unity.modules.*</c>) the project leaves
    /// out. A scene loaded in Play mode loses its components of those modules' classes: each is
    /// deleted with the log message "&lt;Class&gt; component deleted: Component belongs to a disabled
    /// built-in package." and the rest of the scene loads.
    /// </summary>
    internal static class DisabledModules
    {
        static readonly HashSet<string> s_Types = new HashSet<string>(StringComparer.Ordinal);

        /// <param name="moduleAssemblies">The modules' reference assemblies in the Unity
        /// installation (<c>Managed/UnityEngine/UnityEngine.PhysicsModule.dll</c>, ...): the classes
        /// they define are the modules' own.</param>
        internal static void Configure(IEnumerable<string> moduleAssemblies)
        {
            s_Types.Clear();
            foreach (var path in moduleAssemblies)
            {
                using var stream = File.OpenRead(path);
                using var pe = new PEReader(stream);
                var metadata = pe.GetMetadataReader();
                foreach (var handle in metadata.TypeDefinitions)
                {
                    var type = metadata.GetTypeDefinition(handle);
                    if (type.GetDeclaringType().IsNil)
                        s_Types.Add(metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name));
                }
            }
        }

        /// <summary>Whether <paramref name="type"/> is a class of a disabled module.</summary>
        internal static bool Contains(Type type) => type.Assembly == typeof(UnityEngine.Component).Assembly
            && s_Types.Count != 0 && type.FullName != null && s_Types.Contains(type.FullName);

        /// <summary>Removes disabled engine components before open Edit-mode scenes activate.</summary>
        internal static void PruneOpenScenes()
        {
            if (s_Types.Count == 0) return;
            foreach (var scene in SceneRegistry.Loaded.ToArray())
                foreach (var root in scene.Roots.ToArray())
                    if (root != null) Prune(root.transform);
        }

        internal static void Prune(UnityEngine.Transform transform)
        {
            var go = transform.gameObject;
            foreach (var component in go.m_Components.ToArray())
            {
                if (component == null || !Contains(component.GetType())) continue;
                UnityEngine.Debug.Log(component.GetType().Name + " component deleted: Component belongs to a disabled built-in package.");
                UnityEngine.Object.DestroyImmediate(component);
            }
            foreach (var child in transform.m_Children.ToArray())
                if (child != null) Prune(child);
        }
    }
}
