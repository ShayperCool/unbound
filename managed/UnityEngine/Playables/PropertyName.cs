using System;
using System.Collections.Generic;
using System.Text;

namespace UnityEngine
{
    /// <summary>An interned name compared by id (exposed references, notification ids).</summary>
    public partial struct PropertyName : IEquatable<PropertyName>
    {
        internal int id;
        internal int conflictIndex;

        public PropertyName(string name) : this(PropertyNameUtils.PropertyNameFromString(name)) { }

        public PropertyName(PropertyName other)
        {
            id = other.id;
            conflictIndex = other.conflictIndex;
        }

        public PropertyName(int id)
        {
            this.id = id;
            conflictIndex = 0;
        }

        public static bool IsNullOrEmpty(PropertyName prop) => prop.id == 0;

        public static bool operator ==(PropertyName lhs, PropertyName rhs) => lhs.id == rhs.id;

        public static bool operator !=(PropertyName lhs, PropertyName rhs) => lhs.id != rhs.id;

        public override int GetHashCode() => id;

        public override bool Equals(object other) => other is PropertyName p && Equals(p);

        public bool Equals(PropertyName other) => this == other;

        public static implicit operator PropertyName(string name) => new PropertyName(name);

        public static implicit operator PropertyName(int id) => new PropertyName(id);

        public override string ToString()
        {
            int conflicts = PropertyNameUtils.ConflictCountForID(id);
            string text = $"{PropertyNameUtils.StringFromPropertyName(this)}:{id}";
            if (conflicts > 0)
            {
                var b = new StringBuilder(text);
                b.Append(" conflicts with ");
                for (int i = 0; i < conflicts; i++)
                {
                    if (i == conflictIndex) continue;
                    b.AppendFormat("\"{0}\"", PropertyNameUtils.StringFromPropertyName(new PropertyName(id) { conflictIndex = i }));
                }
                text = b.ToString();
            }
            return text;
        }
    }

    /// <summary>The table of interned names: an id is the name's hash, names with the same hash
    /// are told apart by their conflict index.</summary>
    internal partial class PropertyNameUtils
    {
        static readonly Dictionary<string, PropertyName> s_ByName = new Dictionary<string, PropertyName>();
        static readonly Dictionary<int, List<string>> s_ById = new Dictionary<int, List<string>>();

        public static PropertyName PropertyNameFromString(string name)
        {
            if (string.IsNullOrEmpty(name)) return default;
            lock (s_ByName)
            {
                if (s_ByName.TryGetValue(name, out var found)) return found;
                int id = Animator.StringToHash(name);
                if (id == 0) id = 1;
                if (!s_ById.TryGetValue(id, out var names)) s_ById[id] = names = new List<string>();
                var p = new PropertyName(id) { conflictIndex = names.Count };
                names.Add(name);
                s_ByName[name] = p;
                return p;
            }
        }

        public static string StringFromPropertyName(PropertyName propertyName)
        {
            lock (s_ByName)
            {
                if (s_ById.TryGetValue(propertyName.id, out var names) && propertyName.conflictIndex < names.Count)
                    return names[propertyName.conflictIndex];
            }
            return "";
        }

        public static int ConflictCountForID(int id)
        {
            lock (s_ByName)
            {
                return s_ById.TryGetValue(id, out var names) && names.Count > 1 ? names.Count : 0;
            }
        }
    }

    /// <summary>A reference to a scene object that an asset holds by name, resolved through a
    /// table (the PlayableDirector) at runtime.</summary>
    [Serializable]
    public partial struct ExposedReference<T> where T : Object
    {
        [SerializeField] public PropertyName exposedName;
        [SerializeField] public Object defaultValue;

        public T Resolve(IExposedPropertyTable resolver)
        {
            if (resolver != null)
            {
                var value = resolver.GetReferenceValue(exposedName, out bool idValid);
                if (idValid) return value as T;
            }
            return defaultValue as T;
        }
    }
}
