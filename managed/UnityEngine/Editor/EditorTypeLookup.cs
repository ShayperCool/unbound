using System;
using System.Collections.Generic;
using System.Threading;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>Names and persistent identifiers from the release's measured class table.</summary>
    internal sealed partial class UnityType
    {
        sealed class Registry
        {
            internal readonly string Release;
            internal readonly Dictionary<string, UnityType> ByName = new Dictionary<string, UnityType>(StringComparer.Ordinal);
            internal Registry(string release)
            {
                Release = release;
                foreach (var entry in Compat.EditorClassTable(release))
                    ByName.Add(entry.Value.Name, new UnityType(entry.Value.Name, entry.Key));
            }
        }
        static Registry s_Registry;
        public UnityType() { }
        UnityType(string typeName, int classId) { name = typeName; persistentTypeID = classId; }
        public string name { get; private set; }
        public int persistentTypeID { get; private set; }
        public static UnityType FindTypeByName(string name)
        {
            if (name == null) throw new ArgumentNullException("key");
            string release = ProjectInfo.UnityVersion;
            var registry = Volatile.Read(ref s_Registry);
            if (registry == null || registry.Release != release)
            {
                registry = new Registry(release);
                Volatile.Write(ref s_Registry, registry);
            }
            return registry.ByName.TryGetValue(name, out var value) ? value : null;
        }
    }
}
