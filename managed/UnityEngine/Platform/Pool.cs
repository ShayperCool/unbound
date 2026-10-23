using System;
using System.Collections.Generic;

namespace UnityEngine.Pool
{
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

    /// <summary>A stack-based pool with create/get/release/destroy callbacks.</summary>
    public partial class ObjectPool<T> : IDisposable, IObjectPool<T> where T : class
    {
        internal readonly List<T> m_List;
        readonly Func<T> m_CreateFunc;
        readonly Action<T> m_ActionOnGet;
        readonly Action<T> m_ActionOnRelease;
        readonly Action<T> m_ActionOnDestroy;
        readonly int m_MaxSize;
        internal bool m_CollectionCheck;

        public int CountAll { get; private set; }
        public int CountActive => CountAll - CountInactive;
        public int CountInactive => m_List.Count;

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
        }

        public T Get()
        {
            T element;
            if (m_List.Count == 0)
            {
                element = m_CreateFunc();
                CountAll++;
            }
            else
            {
                int index = m_List.Count - 1;
                element = m_List[index];
                m_List.RemoveAt(index);
            }
            m_ActionOnGet?.Invoke(element);
            return element;
        }

        public PooledObject<T> Get(out T v) => new PooledObject<T>(v = Get(), this);

        public void Release(T element)
        {
            if (m_CollectionCheck && m_List.Count > 0 && m_List.Contains(element))
                throw new InvalidOperationException("Trying to release an object that has already been released to the pool.");
            m_ActionOnRelease?.Invoke(element);
            if (CountInactive < m_MaxSize) m_List.Add(element);
            else
            {
                CountAll--;
                m_ActionOnDestroy?.Invoke(element);
            }
        }

        public void Clear()
        {
            if (m_ActionOnDestroy != null)
                foreach (var item in m_List) m_ActionOnDestroy(item);
            m_List.Clear();
            CountAll = 0;
        }

        public void Dispose() => Clear();
    }

    public partial class LinkedPool<T> : IDisposable, IObjectPool<T> where T : class
    {
        readonly ObjectPool<T> m_Pool;

        public LinkedPool(Func<T> createFunc, Action<T> actionOnGet = null, Action<T> actionOnRelease = null, Action<T> actionOnDestroy = null, bool collectionCheck = true, int maxSize = 10000)
        {
            m_Pool = new ObjectPool<T>(createFunc, actionOnGet, actionOnRelease, actionOnDestroy, collectionCheck, 10, maxSize);
        }

        public int CountInactive => m_Pool.CountInactive;
        public T Get() => m_Pool.Get();
        public PooledObject<T> Get(out T v) => new PooledObject<T>(v = Get(), this);
        public void Release(T item) => m_Pool.Release(item);
        public void Clear() => m_Pool.Clear();
        public void Dispose() => m_Pool.Dispose();
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
