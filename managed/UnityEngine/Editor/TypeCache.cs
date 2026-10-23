using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;

namespace UnityEditor
{
    /// <summary>
    /// Types, methods and fields of the loaded assemblies (the game's and the engine's) by base
    /// type or attribute, found by reflection on first use of each query and kept.
    /// </summary>
    public static partial class TypeCache
    {
        static readonly Dictionary<(string, Type, string), Array> s_Cache = new Dictionary<(string, Type, string), Array>();
        /// <summary>The assembly list the cache was built from (a hot reload replaces it).</summary>
        static IReadOnlyList<Assembly> s_CachedFor;

        internal static ulong GetCurrentAge() => 0;

        static IEnumerable<Assembly> Assemblies(string assemblyName)
        {
            var seen = new HashSet<Assembly>();
            var all = new List<Assembly>(GameDomain.Assemblies()) { typeof(UnityEngine.Object).Assembly };
            foreach (var a in all)
            {
                if (a == null || !seen.Add(a)) continue;
                if (assemblyName != null && a.GetName().Name != assemblyName) continue;
                yield return a;
            }
        }

        static Type[] Types(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return Array.FindAll(e.Types, t => t != null); }
        }

        static T[] Query<T>(string kind, Type key, string assemblyName, Func<Assembly, IEnumerable<T>> find)
        {
            lock (s_Cache)
            {
                if (!ReferenceEquals(s_CachedFor, GameDomain.Assemblies()))
                {
                    s_Cache.Clear();
                    s_CachedFor = GameDomain.Assemblies();
                }
                if (s_Cache.TryGetValue((kind, key, assemblyName), out var cached)) return (T[])cached;
                var list = new List<T>();
                foreach (var a in Assemblies(assemblyName))
                {
                    try { list.AddRange(find(a)); }
                    catch (Exception) { }
                }
                var result = list.ToArray();
                s_Cache[(kind, key, assemblyName)] = result;
                return result;
            }
        }

        const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        static IEnumerable<Type> Derived(Assembly a, Type parent)
        {
            foreach (var t in Types(a))
            {
                if (t == parent) continue;
                if (parent.IsGenericTypeDefinition ? DerivesFromGeneric(t, parent) : parent.IsAssignableFrom(t))
                    yield return t;
            }
        }

        static bool DerivesFromGeneric(Type t, Type definition)
        {
            for (var b = t.BaseType; b != null; b = b.BaseType)
                if (b.IsGenericType && b.GetGenericTypeDefinition() == definition) return true;
            foreach (var i in t.GetInterfaces())
                if (i.IsGenericType && i.GetGenericTypeDefinition() == definition) return true;
            return false;
        }

        static IEnumerable<Type> WithAttribute(Assembly a, Type attribute)
        {
            foreach (var t in Types(a))
                if (t.IsDefined(attribute, attribute.GetCustomAttribute<AttributeUsageAttribute>()?.Inherited ?? true)) yield return t;
        }

        static IEnumerable<MethodInfo> MethodsWith(Assembly a, Type attribute)
        {
            foreach (var t in Types(a))
                foreach (var m in t.GetMethods(Members))
                    if (m.IsDefined(attribute, false)) yield return m;
        }

        static IEnumerable<FieldInfo> FieldsWith(Assembly a, Type attribute)
        {
            foreach (var t in Types(a))
                foreach (var f in t.GetFields(Members))
                    if (f.IsDefined(attribute, false)) yield return f;
        }

        public static TypeCollection GetTypesWithAttribute<T>() where T : Attribute => GetTypesWithAttribute(typeof(T));
        public static MethodCollection GetMethodsWithAttribute<T>() where T : Attribute => GetMethodsWithAttribute(typeof(T));
        public static FieldInfoCollection GetFieldsWithAttribute<T>() where T : Attribute => GetFieldsWithAttribute(typeof(T));
        public static TypeCollection GetTypesDerivedFrom<T>() => GetTypesDerivedFrom(typeof(T));
        public static TypeCollection GetTypesDerivedFrom(Type parentType) => GetTypesDerivedFrom(parentType, null);
        public static TypeCollection GetTypesWithAttribute(Type attrType) => GetTypesWithAttribute(attrType, null);
        public static MethodCollection GetMethodsWithAttribute(Type attrType) => GetMethodsWithAttribute(attrType, null);
        public static FieldInfoCollection GetFieldsWithAttribute(Type attrType) => GetFieldsWithAttribute(attrType, null);
        public static TypeCollection GetTypesWithAttribute<T>(string assemblyName) where T : Attribute => GetTypesWithAttribute(typeof(T), assemblyName);
        public static MethodCollection GetMethodsWithAttribute<T>(string assemblyName) where T : Attribute => GetMethodsWithAttribute(typeof(T), assemblyName);
        public static FieldInfoCollection GetFieldsWithAttribute<T>(string assemblyName) where T : Attribute => GetFieldsWithAttribute(typeof(T), assemblyName);
        public static TypeCollection GetTypesDerivedFrom<T>(string assemblyName) => GetTypesDerivedFrom(typeof(T), assemblyName);

        public static TypeCollection GetTypesDerivedFrom(Type parentType, string assemblyName) =>
            new TypeCollection(parentType == null ? null : Query("derived", parentType, assemblyName, a => Derived(a, parentType)));

        public static TypeCollection GetTypesWithAttribute(Type attrType, string assemblyName) =>
            new TypeCollection(attrType == null ? null : Query("types", attrType, assemblyName, a => WithAttribute(a, attrType)));

        public static MethodCollection GetMethodsWithAttribute(Type attrType, string assemblyName) =>
            new MethodCollection(attrType == null ? null : Query("methods", attrType, assemblyName, a => MethodsWith(a, attrType)));

        public static FieldInfoCollection GetFieldsWithAttribute(Type attrType, string assemblyName) =>
            new FieldInfoCollection(attrType == null ? null : Query("fields", attrType, assemblyName, a => FieldsWith(a, attrType)));

        public partial struct TypeCollection : IList<Type>, ICollection<Type>, IEnumerable<Type>, IEnumerable, IList, ICollection
        {
            readonly Type[] m_Items;

            internal TypeCollection(Type[] types) => m_Items = types ?? Array.Empty<Type>();

            Type[] Items => m_Items ?? Array.Empty<Type>();

            public int Count => Items.Length;
            public bool IsReadOnly => true;
            public bool IsFixedSize => true;
            public bool IsSynchronized => true;
            object ICollection.SyncRoot => null;

            public Type this[int index]
            {
                get => Items[index];
                set => throw new NotSupportedException();
            }

            object IList.this[int index]
            {
                get => Items[index];
                set => throw new NotSupportedException();
            }

            public bool Contains(Type item) => Array.IndexOf(Items, item) >= 0;
            public bool Contains(object item) => item is Type t && Contains(t);
            public Enumerator GetEnumerator() => new Enumerator(ref this);
            public void CopyTo(Type[] array, int arrayIndex) => Items.CopyTo(array, arrayIndex);
            public void CopyTo(Array array, int arrayIndex) => Items.CopyTo(array, arrayIndex);
            public int IndexOf(Type item) => Array.IndexOf(Items, item);
            public int IndexOf(object item) => item is Type t ? IndexOf(t) : -1;

            IEnumerator<Type> IEnumerable<Type>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            void ICollection<Type>.Add(Type item) => throw new NotSupportedException();
            void ICollection<Type>.Clear() => throw new NotSupportedException();
            bool ICollection<Type>.Remove(Type item) => throw new NotSupportedException();
            void IList<Type>.Insert(int index, Type item) => throw new NotSupportedException();
            void IList<Type>.RemoveAt(int index) => throw new NotSupportedException();
            int IList.Add(object value) => throw new NotSupportedException();
            void IList.Clear() => throw new NotSupportedException();
            void IList.Insert(int index, object value) => throw new NotSupportedException();
            void IList.Remove(object value) => throw new NotSupportedException();
            void IList.RemoveAt(int index) => throw new NotSupportedException();

            public partial struct Enumerator : IEnumerator<Type>, IEnumerator, IDisposable
            {
                readonly Type[] m_Items;
                int m_Index;

                internal Enumerator(ref TypeCollection collection)
                {
                    m_Items = collection.Items;
                    m_Index = -1;
                }

                public void Dispose() { }
                public bool MoveNext() => ++m_Index < m_Items.Length;
                void IEnumerator.Reset() => m_Index = -1;
                object IEnumerator.Current => Current;
                public Type Current => m_Items[m_Index];
            }
        }

        public partial struct MethodCollection : IList<MethodInfo>, ICollection<MethodInfo>, IEnumerable<MethodInfo>, IEnumerable, IList, ICollection
        {
            readonly MethodInfo[] m_Items;

            internal MethodCollection(MethodInfo[] methods) => m_Items = methods ?? Array.Empty<MethodInfo>();

            MethodInfo[] Items => m_Items ?? Array.Empty<MethodInfo>();

            public int Count => Items.Length;
            public bool IsReadOnly => true;
            public bool IsFixedSize => true;
            public bool IsSynchronized => true;
            object ICollection.SyncRoot => null;

            public MethodInfo this[int index]
            {
                get => Items[index];
                set => throw new NotSupportedException();
            }

            object IList.this[int index]
            {
                get => Items[index];
                set => throw new NotSupportedException();
            }

            public bool Contains(MethodInfo item) => Array.IndexOf(Items, item) >= 0;
            public bool Contains(object item) => item is MethodInfo t && Contains(t);
            public Enumerator GetEnumerator() => new Enumerator(ref this);
            public void CopyTo(MethodInfo[] array, int arrayIndex) => Items.CopyTo(array, arrayIndex);
            public void CopyTo(Array array, int arrayIndex) => Items.CopyTo(array, arrayIndex);
            public int IndexOf(MethodInfo item) => Array.IndexOf(Items, item);
            public int IndexOf(object item) => item is MethodInfo t ? IndexOf(t) : -1;

            IEnumerator<MethodInfo> IEnumerable<MethodInfo>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            void ICollection<MethodInfo>.Add(MethodInfo item) => throw new NotSupportedException();
            void ICollection<MethodInfo>.Clear() => throw new NotSupportedException();
            bool ICollection<MethodInfo>.Remove(MethodInfo item) => throw new NotSupportedException();
            void IList<MethodInfo>.Insert(int index, MethodInfo item) => throw new NotSupportedException();
            void IList<MethodInfo>.RemoveAt(int index) => throw new NotSupportedException();
            int IList.Add(object value) => throw new NotSupportedException();
            void IList.Clear() => throw new NotSupportedException();
            void IList.Insert(int index, object value) => throw new NotSupportedException();
            void IList.Remove(object value) => throw new NotSupportedException();
            void IList.RemoveAt(int index) => throw new NotSupportedException();

            public partial struct Enumerator : IEnumerator<MethodInfo>, IEnumerator, IDisposable
            {
                readonly MethodInfo[] m_Items;
                int m_Index;

                internal Enumerator(ref MethodCollection collection)
                {
                    m_Items = collection.Items;
                    m_Index = -1;
                }

                public void Dispose() { }
                public bool MoveNext() => ++m_Index < m_Items.Length;
                void IEnumerator.Reset() => m_Index = -1;
                object IEnumerator.Current => Current;
                public MethodInfo Current => m_Items[m_Index];
            }
        }

        public partial struct FieldInfoCollection : IList<FieldInfo>, ICollection<FieldInfo>, IEnumerable<FieldInfo>, IEnumerable, IList, ICollection
        {
            readonly FieldInfo[] m_Items;

            internal FieldInfoCollection(FieldInfo[] fields) => m_Items = fields ?? Array.Empty<FieldInfo>();

            FieldInfo[] Items => m_Items ?? Array.Empty<FieldInfo>();

            public int Count => Items.Length;
            public bool IsReadOnly => true;
            public bool IsFixedSize => true;
            public bool IsSynchronized => true;
            object ICollection.SyncRoot => null;

            public FieldInfo this[int index]
            {
                get => Items[index];
                set => throw new NotSupportedException();
            }

            object IList.this[int index]
            {
                get => Items[index];
                set => throw new NotSupportedException();
            }

            public bool Contains(FieldInfo item) => Array.IndexOf(Items, item) >= 0;
            public bool Contains(object item) => item is FieldInfo t && Contains(t);
            public Enumerator GetEnumerator() => new Enumerator(ref this);
            public void CopyTo(FieldInfo[] array, int arrayIndex) => Items.CopyTo(array, arrayIndex);
            public void CopyTo(Array array, int arrayIndex) => Items.CopyTo(array, arrayIndex);
            public int IndexOf(FieldInfo item) => Array.IndexOf(Items, item);
            public int IndexOf(object item) => item is FieldInfo t ? IndexOf(t) : -1;

            IEnumerator<FieldInfo> IEnumerable<FieldInfo>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            void ICollection<FieldInfo>.Add(FieldInfo item) => throw new NotSupportedException();
            void ICollection<FieldInfo>.Clear() => throw new NotSupportedException();
            bool ICollection<FieldInfo>.Remove(FieldInfo item) => throw new NotSupportedException();
            void IList<FieldInfo>.Insert(int index, FieldInfo item) => throw new NotSupportedException();
            void IList<FieldInfo>.RemoveAt(int index) => throw new NotSupportedException();
            int IList.Add(object value) => throw new NotSupportedException();
            void IList.Clear() => throw new NotSupportedException();
            void IList.Insert(int index, object value) => throw new NotSupportedException();
            void IList.Remove(object value) => throw new NotSupportedException();
            void IList.RemoveAt(int index) => throw new NotSupportedException();

            public partial struct Enumerator : IEnumerator<FieldInfo>, IEnumerator, IDisposable
            {
                readonly FieldInfo[] m_Items;
                int m_Index;

                internal Enumerator(ref FieldInfoCollection collection)
                {
                    m_Items = collection.Items;
                    m_Index = -1;
                }

                public void Dispose() { }
                public bool MoveNext() => ++m_Index < m_Items.Length;
                void IEnumerator.Reset() => m_Index = -1;
                object IEnumerator.Current => Current;
                public FieldInfo Current => m_Items[m_Index];
            }
        }
    }
}
