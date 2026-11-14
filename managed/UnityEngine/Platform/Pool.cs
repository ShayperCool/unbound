using System;
using System.Collections.Generic;

namespace UnityEngine.Pool
{
    // Both pool layouts check object identity, independently of user equality and counters.
    sealed class PoolReferences<T> where T : class
    {
        static readonly object s_NullItem = new object();
        readonly Dictionary<object, int> m_Counts = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        internal bool Contains(T item) => m_Counts.ContainsKey((object)item ?? s_NullItem);
        internal void Add(T item)
        {
            object key = (object)item ?? s_NullItem;
            m_Counts.TryGetValue(key, out int count);
            m_Counts[key] = count + 1;
        }
        internal void Remove(T item)
        {
            object key = (object)item ?? s_NullItem;
            int count = m_Counts[key];
            if (count == 1) m_Counts.Remove(key);
            else m_Counts[key] = count - 1;
        }
        internal void Clear() => m_Counts.Clear();
    }

    internal partial interface IPool
    {
        void Clear();
        int CountInactive { get; }
    }

    public partial interface IObjectPool<T> where T : class
    {
        int CountInactive { get; }
        T Get();
        PooledObject<T> Get(out T v);
        void Release(T element);
        void Clear();
    }

    public partial struct PooledObject<T> : IDisposable where T : class
    {
        readonly T m_ToReturn;
        readonly IObjectPool<T> m_Pool;

        public PooledObject(T value, IObjectPool<T> pool)
        {
            m_ToReturn = value;
            m_Pool = pool;
        }

        void IDisposable.Dispose() => m_Pool.Release(m_ToReturn);
    }

    /// <summary>A stack-based pool with create/get/release/destroy callbacks and one extra slot: an
    /// object released while the slot is empty waits there, further releases push onto the stack;
    /// Get takes the slot first, then the stack top; Clear destroys the stack bottom to top, then
    /// the slot.
    /// The order matches the Unity 6000.0.71f1 Editor (tests/projects/uitkcollections, the order
    /// in which a list view's rebuild destroys its rows).</summary>
    public partial class ObjectPool<T> : IDisposable, IObjectPool<T>, IPool where T : class
    {
        internal readonly List<T> m_List;
        readonly Func<T> m_CreateFunc;
        readonly Action<T> m_ActionOnGet;
        readonly Action<T> m_ActionOnRelease;
        readonly Action<T> m_ActionOnDestroy;
        readonly int m_MaxSize;
        internal bool m_CollectionCheck;
        T m_FreshlyReleased;
        PoolReferences<T> m_References;

        public int CountAll { get; private set; }
        public int CountActive => CountAll - CountInactive;
        public int CountInactive => m_List.Count + (m_FreshlyReleased != null ? 1 : 0);

        public ObjectPool(Func<T> createFunc, Action<T> actionOnGet = null, Action<T> actionOnRelease = null, Action<T> actionOnDestroy = null, bool collectionCheck = true, int defaultCapacity = 10, int maxSize = 10000)
        {
            if (createFunc == null) throw new ArgumentNullException(nameof(createFunc));
            if (maxSize <= 0) throw new ArgumentException("Max Size must be greater than 0", nameof(maxSize));
            m_List = new List<T>(defaultCapacity);
            m_CreateFunc = createFunc;
            m_MaxSize = maxSize;
            m_ActionOnGet = actionOnGet;
            m_ActionOnRelease = actionOnRelease;
            m_ActionOnDestroy = actionOnDestroy;
            m_CollectionCheck = collectionCheck;
            if (collectionCheck) m_References = new PoolReferences<T>();
        }

        public T Get()
        {
            T element;
            if (m_FreshlyReleased != null)
            {
                element = m_FreshlyReleased;
                m_FreshlyReleased = null;
                m_References?.Remove(element);
            }
            else if (m_List.Count == 0)
            {
                element = m_CreateFunc();
                CountAll++;
            }
            else
            {
                int index = m_List.Count - 1;
                element = m_List[index];
                m_List.RemoveAt(index);
                m_References?.Remove(element);
            }
            m_ActionOnGet?.Invoke(element);
            return element;
        }

        public PooledObject<T> Get(out T v) => new PooledObject<T>(v = Get(), this);

        public void Release(T element)
        {
            if (m_CollectionCheck)
            {
                // Internal consumers may enable checks after constructing an unchecked pool.
                if (m_References == null)
                {
                    m_References = new PoolReferences<T>();
                    if (m_FreshlyReleased != null) m_References.Add(m_FreshlyReleased);
                    foreach (var stored in m_List) m_References.Add(stored);
                }
                if (CountInactive > 0 && (ReferenceEquals(element, m_FreshlyReleased) || m_References.Contains(element)))
                    throw new InvalidOperationException("Trying to release an object that has already been released to the pool.");
            }
            m_ActionOnRelease?.Invoke(element);
            if (m_FreshlyReleased == null)
            {
                m_FreshlyReleased = element;
                if (element != null) m_References?.Add(element);
                return;
            }
            if (CountInactive < m_MaxSize)
            {
                m_List.Add(element);
                m_References?.Add(element);
            }
            else
            {
                CountAll--;
                m_ActionOnDestroy?.Invoke(element);
            }
        }

        public void Clear()
        {
            if (m_ActionOnDestroy != null)
            {
                foreach (var item in m_List) m_ActionOnDestroy(item);
                if (m_FreshlyReleased != null) m_ActionOnDestroy(m_FreshlyReleased);
            }
            m_FreshlyReleased = null;
            m_List.Clear();
            m_References?.Clear();
            CountAll = 0;
        }

        public void Dispose() => Clear();
    }

    public partial class LinkedPool<T> : IDisposable, IObjectPool<T>, IPool where T : class
    {
        readonly Func<T> m_CreateFunc;
        readonly Action<T> m_ActionOnGet, m_ActionOnRelease, m_ActionOnDestroy;
        readonly int m_Limit;
        readonly PoolReferences<T> m_Inactive;
        internal LinkedPoolItem m_PoolFirst, m_NextAvailableListItem;

        internal partial class LinkedPoolItem
        {
            internal LinkedPoolItem poolNext;
            internal T value;
            public LinkedPoolItem() { }
        }

        public LinkedPool(Func<T> createFunc, Action<T> actionOnGet = null, Action<T> actionOnRelease = null, Action<T> actionOnDestroy = null, bool collectionCheck = true, int maxSize = 10000)
        {
            if (createFunc == null) throw new ArgumentNullException(nameof(createFunc));
            if (maxSize <= 0) throw new ArgumentException(nameof(maxSize), "Max size must be greater than 0");
            m_CreateFunc = createFunc;
            m_ActionOnGet = actionOnGet;
            m_ActionOnRelease = actionOnRelease;
            m_ActionOnDestroy = actionOnDestroy;
            if (collectionCheck) m_Inactive = new PoolReferences<T>();
            m_Limit = maxSize;
        }

        public int CountInactive { get; private set; }

        public T Get()
        {
            T result;
            if (m_PoolFirst == null) result = m_CreateFunc();
            else
            {
                var node = m_PoolFirst;
                m_PoolFirst = node.poolNext;
                result = node.value;
                m_Inactive?.Remove(result);
                node.value = null;
                node.poolNext = m_NextAvailableListItem;
                m_NextAvailableListItem = node;
                CountInactive = unchecked(CountInactive - 1);
            }
            m_ActionOnGet?.Invoke(result);
            return result;
        }

        public PooledObject<T> Get(out T v) => new PooledObject<T>(v = Get(), this);

        public void Release(T item)
        {
            if (m_Inactive != null && m_Inactive.Contains(item))
                throw new InvalidOperationException("Trying to release an object that has already been released to the pool.");
            m_ActionOnRelease?.Invoke(item);
            if (CountInactive >= m_Limit) { m_ActionOnDestroy?.Invoke(item); return; }
            // Reuse empty nodes so a warmed pool does not allocate during Get/Release.
            var available = m_NextAvailableListItem;
            if (available == null) available = new LinkedPoolItem();
            else m_NextAvailableListItem = available.poolNext;
            m_Inactive?.Add(item);
            available.value = item;
            available.poolNext = m_PoolFirst;
            m_PoolFirst = available;
            CountInactive = unchecked(CountInactive + 1);
        }

        public void Clear()
        {
            if (m_ActionOnDestroy != null)
                for (var node = m_PoolFirst; node != null; node = node.poolNext)
                    m_ActionOnDestroy(node.value);
            m_Inactive?.Clear();
            m_PoolFirst = null;
            m_NextAvailableListItem = null;
            CountInactive = 0;
        }

        public void Dispose() => Clear();
    }

    public partial class GenericPool<T> where T : class, new()
    {
        internal static readonly ObjectPool<T> s_Pool = new ObjectPool<T>(() => new T());
        public static T Get() => s_Pool.Get();
        public static PooledObject<T> Get(out T value) => s_Pool.Get(out value);
        public static void Release(T toRelease) => s_Pool.Release(toRelease);
    }

    public static partial class UnsafeGenericPool<T> where T : class, new()
    {
        internal static readonly ObjectPool<T> s_Pool = new ObjectPool<T>(() => new T(), null, null, null, false);
        public static T Get() => s_Pool.Get();
        public static PooledObject<T> Get(out T value) => s_Pool.Get(out value);
        public static void Release(T toRelease) => s_Pool.Release(toRelease);
    }

    public partial class CollectionPool<TCollection, TItem> where TCollection : class, ICollection<TItem>, new()
    {
        internal static readonly ObjectPool<TCollection> s_Pool = new ObjectPool<TCollection>(() => new TCollection(), null, l => l.Clear());
        public static TCollection Get() => s_Pool.Get();
        public static PooledObject<TCollection> Get(out TCollection value) => s_Pool.Get(out value);
        public static void Release(TCollection toRelease) => s_Pool.Release(toRelease);
    }

    public partial class ListPool<T> : CollectionPool<List<T>, T> { }

    public partial class HashSetPool<T> : CollectionPool<HashSet<T>, T> { }

    public partial class DictionaryPool<TKey, TValue> : CollectionPool<Dictionary<TKey, TValue>, KeyValuePair<TKey, TValue>> { }
}
