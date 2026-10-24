using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Properties.Internal;
using UnityEngine;

namespace Unity.Properties
{
    /// <summary>Property bags: registration, lookup and the dispatch of visitors to the
    /// specialized collection interfaces.</summary>
    public static partial class PropertyBag
    {
        public static void AcceptWithSpecializedVisitor<TContainer>(IPropertyBag<TContainer> properties, IPropertyBagVisitor visitor, ref TContainer container)
        {
            if (properties == null) throw new ArgumentNullException(nameof(properties));
            if (properties is IDictionaryPropertyBagAccept<TContainer> dictionary && visitor is IDictionaryPropertyBagVisitor dictionaryVisitor)
                dictionary.Accept(dictionaryVisitor, ref container);
            else if (properties is IListPropertyBagAccept<TContainer> list && visitor is IListPropertyBagVisitor listVisitor)
                list.Accept(listVisitor, ref container);
            else if (properties is ISetPropertyBagAccept<TContainer> set && visitor is ISetPropertyBagVisitor setVisitor)
                set.Accept(setVisitor, ref container);
            else if (properties is ICollectionPropertyBagAccept<TContainer> collection && visitor is ICollectionPropertyBagVisitor collectionVisitor)
                collection.Accept(collectionVisitor, ref container);
            else
                properties.Accept(visitor, ref container);
        }

        public static void Register<TContainer>(PropertyBag<TContainer> propertyBag) => PropertyBagStore.AddPropertyBag(propertyBag);

        public static void RegisterArray<TElement>() => PropertyBagStore.AddPropertyBag(new ArrayPropertyBag<TElement>());
        public static void RegisterArray<TContainer, TElement>() => RegisterArray<TElement>();
        public static void RegisterList<TElement>() => PropertyBagStore.AddPropertyBag(new ListPropertyBag<TElement>());
        public static void RegisterList<TContainer, TElement>() => RegisterList<TElement>();
        public static void RegisterHashSet<TElement>() => PropertyBagStore.AddPropertyBag(new HashSetPropertyBag<TElement>());
        public static void RegisterHashSet<TContainer, TElement>() => RegisterHashSet<TElement>();
        public static void RegisterDictionary<TKey, TValue>() => PropertyBagStore.AddPropertyBag(new DictionaryPropertyBag<TKey, TValue>());
        public static void RegisterDictionary<TContainer, TKey, TValue>() => RegisterDictionary<TKey, TValue>();
        public static void RegisterIList<TList, TElement>() where TList : IList<TElement> => PropertyBagStore.AddPropertyBag(new IndexedCollectionPropertyBag<TList, TElement>());
        public static void RegisterIList<TContainer, TList, TElement>() where TList : IList<TElement> => RegisterIList<TList, TElement>();
        public static void RegisterISet<TSet, TElement>() where TSet : ISet<TElement> => PropertyBagStore.AddPropertyBag(new SetPropertyBagBase<TSet, TElement>());
        public static void RegisterISet<TContainer, TSet, TElement>() where TSet : ISet<TElement> => RegisterISet<TSet, TElement>();

        public static void RegisterIDictionary<TDictionary, TKey, TValue>() where TDictionary : IDictionary<TKey, TValue>
        {
            PropertyBagStore.AddPropertyBag(new KeyValueCollectionPropertyBag<TDictionary, TKey, TValue>());
            PropertyBagStore.AddPropertyBag(new KeyValuePairPropertyBag<TKey, TValue>());
        }

        public static void RegisterIDictionary<TContainer, TDictionary, TKey, TValue>() where TDictionary : IDictionary<TKey, TValue> =>
            RegisterIDictionary<TDictionary, TKey, TValue>();

        public static TContainer CreateInstance<TContainer>()
        {
            var bag = PropertyBagStore.GetPropertyBag<TContainer>();
            if (bag == null) throw new MissingPropertyBagException(typeof(TContainer));
            return bag.CreateInstance();
        }

        public static IPropertyBag GetPropertyBag(Type type) => PropertyBagStore.GetPropertyBag(type);

        public static IPropertyBag<TContainer> GetPropertyBag<TContainer>() => PropertyBagStore.GetPropertyBag<TContainer>();

        public static bool TryGetPropertyBagForValue<TValue>(ref TValue value, out IPropertyBag propertyBag) =>
            PropertyBagStore.TryGetPropertyBagForValue(ref value, out propertyBag);

        public static bool Exists<TContainer>() => PropertyBagStore.Exists<TContainer>();

        public static bool Exists(Type type) => PropertyBagStore.Exists(type);

        public static IEnumerable<Type> GetAllTypesWithAPropertyBag() => PropertyBagStore.AllTypes;
    }

    /// <summary>The properties of one container type, and how to make an instance of it.</summary>
    public abstract partial class PropertyBag<TContainer> : IPropertyBag<TContainer>, IPropertyBagRegister, IConstructor<TContainer>
    {
        static PropertyBag()
        {
            if (!TypeTraits.IsContainer(typeof(TContainer)))
                throw new InvalidOperationException($"Failed to create a property bag for Type=[{typeof(TContainer)}]. The type is not a valid container type.");
        }

        protected PropertyBag() { }

        protected virtual InstantiationKind InstantiationKind { get; } = InstantiationKind.Activator;

        InstantiationKind IConstructor.InstantiationKind => InstantiationKind;

        void IPropertyBagRegister.Register() => PropertyBagStore.AddPropertyBag(this);

        public void Accept(ITypeVisitor visitor)
        {
            if (visitor == null) throw new ArgumentNullException(nameof(visitor));
            visitor.Visit<TContainer>();
        }

        void IPropertyBag.Accept(IPropertyBagVisitor visitor, ref object container)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            if (!(container is TContainer typed))
                throw new ArgumentException($"The given ContainerType=[{container.GetType()}] does not match the PropertyBagType=[{typeof(TContainer)}]");
            PropertyBag.AcceptWithSpecializedVisitor(this, visitor, ref typed);
            container = typed;
        }

        void IPropertyBag<TContainer>.Accept(IPropertyBagVisitor visitor, ref TContainer container) => visitor.Visit(this, ref container);

        PropertyCollection<TContainer> IPropertyBag<TContainer>.GetProperties() => GetProperties();

        PropertyCollection<TContainer> IPropertyBag<TContainer>.GetProperties(ref TContainer container) => GetProperties(ref container);

        TContainer IConstructor<TContainer>.Instantiate() => Instantiate();

        public abstract PropertyCollection<TContainer> GetProperties();

        public abstract PropertyCollection<TContainer> GetProperties(ref TContainer container);

        protected virtual TContainer Instantiate() => default;

        public TContainer CreateInstance() => TypeUtility.Instantiate<TContainer>();

        public bool TryCreateInstance(out TContainer instance) => TypeUtility.TryInstantiate(out instance);
    }

    /// <summary>A bag of named properties, in the order they were added.</summary>
    public abstract partial class ContainerPropertyBag<TContainer> : PropertyBag<TContainer>, INamedProperties<TContainer>
    {
        readonly List<IProperty<TContainer>> m_List = new List<IProperty<TContainer>>();
        readonly Dictionary<string, IProperty<TContainer>> m_ByName = new Dictionary<string, IProperty<TContainer>>();

        static ContainerPropertyBag()
        {
            if (!TypeTraits.IsContainer(typeof(TContainer)))
                throw new InvalidOperationException($"Failed to create a property bag for Type=[{typeof(TContainer)}]. The type is not a valid container type.");
        }

        protected ContainerPropertyBag() { }

        protected void AddProperty<TValue>(Property<TContainer, TValue> property)
        {
            m_List.Add(property);
            m_ByName.Add(property.Name, property);
        }

        public override PropertyCollection<TContainer> GetProperties() => new PropertyCollection<TContainer>(m_List);

        public override PropertyCollection<TContainer> GetProperties(ref TContainer container) => new PropertyCollection<TContainer>(m_List);

        public bool TryGetProperty(ref TContainer container, string name, out IProperty<TContainer> property) =>
            m_ByName.TryGetValue(name, out property);
    }

    /// <summary>The properties a bag gives: a list, any enumerable, or a collection's shared
    /// element property moved over its indices.</summary>
    public readonly partial struct PropertyCollection<TContainer> : IEnumerable<IProperty<TContainer>>
    {
        enum Source { Empty, Enumerable, List, Indexed }

        readonly Source m_Source;
        readonly IEnumerable<IProperty<TContainer>> m_Enumerable;
        readonly List<IProperty<TContainer>> m_List;
        readonly IndexedCollectionPropertyBagEnumerable<TContainer> m_Indexed;

        public static PropertyCollection<TContainer> Empty { get; } = default;

        public PropertyCollection(IEnumerable<IProperty<TContainer>> enumerable)
        {
            m_Source = Source.Enumerable;
            m_Enumerable = enumerable;
            m_List = null;
            m_Indexed = default;
        }

        public PropertyCollection(List<IProperty<TContainer>> properties)
        {
            m_Source = Source.List;
            m_Enumerable = null;
            m_List = properties;
            m_Indexed = default;
        }

        internal PropertyCollection(IndexedCollectionPropertyBagEnumerable<TContainer> enumerable)
        {
            m_Source = Source.Indexed;
            m_Enumerable = null;
            m_List = null;
            m_Indexed = enumerable;
        }

        public Enumerator GetEnumerator() => m_Source switch
        {
            Source.Enumerable => new Enumerator(m_Enumerable.GetEnumerator()),
            Source.List => new Enumerator(m_List.GetEnumerator()),
            Source.Indexed => new Enumerator(m_Indexed.GetEnumerator()),
            _ => default,
        };

        IEnumerator<IProperty<TContainer>> IEnumerable<IProperty<TContainer>>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public partial struct Enumerator : IEnumerator<IProperty<TContainer>>
        {
            readonly Source m_Source;
            readonly IEnumerator<IProperty<TContainer>> m_Enumerator;
            List<IProperty<TContainer>>.Enumerator m_List;
            IndexedCollectionPropertyBagEnumerator<TContainer> m_Indexed;

            internal Enumerator(IEnumerator<IProperty<TContainer>> enumerator)
            {
                m_Source = Source.Enumerable;
                m_Enumerator = enumerator;
                m_List = default;
                m_Indexed = default;
            }

            internal Enumerator(List<IProperty<TContainer>>.Enumerator properties)
            {
                m_Source = Source.List;
                m_Enumerator = null;
                m_List = properties;
                m_Indexed = default;
            }

            internal Enumerator(IndexedCollectionPropertyBagEnumerator<TContainer> enumerator)
            {
                m_Source = Source.Indexed;
                m_Enumerator = null;
                m_List = default;
                m_Indexed = enumerator;
            }

            public IProperty<TContainer> Current => m_Source switch
            {
                Source.Enumerable => m_Enumerator.Current,
                Source.List => m_List.Current,
                Source.Indexed => m_Indexed.Current,
                _ => null,
            };

            object IEnumerator.Current => Current;

            public bool MoveNext() => m_Source switch
            {
                Source.Enumerable => m_Enumerator.MoveNext(),
                Source.List => m_List.MoveNext(),
                Source.Indexed => m_Indexed.MoveNext(),
                _ => false,
            };

            public void Reset()
            {
                switch (m_Source)
                {
                    case Source.Enumerable: m_Enumerator.Reset(); break;
                    case Source.List: ((IEnumerator)m_List).Reset(); break;
                    case Source.Indexed: m_Indexed.Reset(); break;
                }
            }

            public void Dispose()
            {
                switch (m_Source)
                {
                    case Source.Enumerable: m_Enumerator.Dispose(); break;
                    case Source.Indexed: m_Indexed.Dispose(); break;
                }
            }
        }
    }

    internal readonly partial struct IndexedCollectionPropertyBagEnumerable<TContainer>
    {
        readonly IIndexedCollectionPropertyBagEnumerator<TContainer> m_Impl;
        readonly TContainer m_Container;

        public IndexedCollectionPropertyBagEnumerable(IIndexedCollectionPropertyBagEnumerator<TContainer> impl, TContainer container)
        {
            m_Impl = impl;
            m_Container = container;
        }

        public IndexedCollectionPropertyBagEnumerator<TContainer> GetEnumerator() => new IndexedCollectionPropertyBagEnumerator<TContainer>(m_Impl, m_Container);
    }

    /// <summary>Moves a collection bag's one element property over the indices; the property's
    /// state is restored when done.</summary>
    internal partial struct IndexedCollectionPropertyBagEnumerator<TContainer> : IEnumerator<IProperty<TContainer>>
    {
        readonly IIndexedCollectionPropertyBagEnumerator<TContainer> m_Impl;
        readonly IndexedCollectionSharedPropertyState m_Previous;
        TContainer m_Container;
        int m_Position;

        internal IndexedCollectionPropertyBagEnumerator(IIndexedCollectionPropertyBagEnumerator<TContainer> impl, TContainer container)
        {
            m_Impl = impl;
            m_Container = container;
            m_Previous = impl.GetSharedPropertyState();
            m_Position = -1;
        }

        public IProperty<TContainer> Current => m_Impl.GetSharedProperty();

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            m_Position++;
            if (m_Position < m_Impl.GetCount(ref m_Container))
            {
                m_Impl.SetSharedPropertyState(new IndexedCollectionSharedPropertyState { Index = m_Position, IsReadOnly = false });
                return true;
            }
            m_Impl.SetSharedPropertyState(m_Previous);
            return false;
        }

        public void Reset()
        {
            m_Position = -1;
            m_Impl.SetSharedPropertyState(m_Previous);
        }

        public void Dispose() { }
    }

    // ------------------------------------------------------------------ collections

    /// <summary>The elements of a list, by index.</summary>
    public partial class IndexedCollectionPropertyBag<TList, TElement> : PropertyBag<TList>, IListPropertyBag<TList, TElement>, IConstructorWithCount<TList>, IIndexedCollectionPropertyBagEnumerator<TList>
        where TList : IList<TElement>
    {
        sealed class ListElementProperty : Property<TList, TElement>, IListElementProperty
        {
            internal int m_Index;
            internal bool m_IsReadOnly;

            public int Index => m_Index;
            public override string Name => m_Index.ToString();
            public override bool IsReadOnly => m_IsReadOnly;
            public override TElement GetValue(ref TList container) => container[m_Index];
            public override void SetValue(ref TList container, TElement value) => container[m_Index] = value;
        }

        readonly ListElementProperty m_Shared = new ListElementProperty();

        public IndexedCollectionPropertyBag() { }

        public override PropertyCollection<TList> GetProperties() => PropertyCollection<TList>.Empty;

        public override PropertyCollection<TList> GetProperties(ref TList container) =>
            new PropertyCollection<TList>(new IndexedCollectionPropertyBagEnumerable<TList>(this, container));

        public bool TryGetProperty(ref TList container, int index, out IProperty<TList> property)
        {
            if ((uint)index >= (uint)container.Count)
            {
                property = null;
                return false;
            }
            property = new ListElementProperty { m_Index = index };
            return true;
        }

        void ICollectionPropertyBagAccept<TList>.Accept(ICollectionPropertyBagVisitor visitor, ref TList container) => visitor.Visit(this, ref container);

        void IListPropertyBagAccept<TList>.Accept(IListPropertyBagVisitor visitor, ref TList container) => visitor.Visit(this, ref container);

        void IListPropertyAccept<TList>.Accept<TContainer>(IListPropertyVisitor visitor, Property<TContainer, TList> property, ref TContainer container, ref TList list)
        {
            using (new AttributesScope(m_Shared, property))
                visitor.Visit<TContainer, TList, TElement>(property, ref container, ref list);
        }

        TList IConstructorWithCount<TList>.InstantiateWithCount(int count) => InstantiateWithCount(count);

        protected virtual TList InstantiateWithCount(int count) => default;

        int IIndexedCollectionPropertyBagEnumerator<TList>.GetCount(ref TList container) => container.Count;

        IProperty<TList> IIndexedCollectionPropertyBagEnumerator<TList>.GetSharedProperty() => m_Shared;

        IndexedCollectionSharedPropertyState IIndexedCollectionPropertyBagEnumerator<TList>.GetSharedPropertyState() =>
            new IndexedCollectionSharedPropertyState { Index = m_Shared.m_Index, IsReadOnly = m_Shared.m_IsReadOnly };

        void IIndexedCollectionPropertyBagEnumerator<TList>.SetSharedPropertyState(IndexedCollectionSharedPropertyState state)
        {
            m_Shared.m_Index = state.Index;
            m_Shared.m_IsReadOnly = state.IsReadOnly;
        }
    }

    public sealed partial class ArrayPropertyBag<TElement> : IndexedCollectionPropertyBag<TElement[], TElement>
    {
        public ArrayPropertyBag() { }

        protected override InstantiationKind InstantiationKind => InstantiationKind.PropertyBagOverride;

        protected override TElement[] InstantiateWithCount(int count) => new TElement[count];

        protected override TElement[] Instantiate() => Array.Empty<TElement>();
    }

    public partial class ListPropertyBag<TElement> : IndexedCollectionPropertyBag<List<TElement>, TElement>
    {
        public ListPropertyBag() { }

        protected override InstantiationKind InstantiationKind => InstantiationKind.PropertyBagOverride;

        protected override List<TElement> InstantiateWithCount(int count) => new List<TElement>(count);

        protected override List<TElement> Instantiate() => new List<TElement>();
    }

    /// <summary>The elements of a set: read-only properties named by their values.</summary>
    public partial class SetPropertyBagBase<TSet, TElement> : PropertyBag<TSet>, ISetPropertyBag<TSet, TElement> where TSet : ISet<TElement>
    {
        sealed class SetElementProperty : Property<TSet, TElement>, ISetElementProperty<TElement>
        {
            internal TElement m_Value;

            public override string Name => m_Value.ToString();
            public override bool IsReadOnly => true;
            public TElement Key => m_Value;
            public object ObjectKey => m_Value;
            public override TElement GetValue(ref TSet container) => m_Value;
            public override void SetValue(ref TSet container, TElement value) => throw new InvalidOperationException("Property is ReadOnly.");
        }

        readonly SetElementProperty m_Shared = new SetElementProperty();

        public SetPropertyBagBase() { }

        public override PropertyCollection<TSet> GetProperties() => PropertyCollection<TSet>.Empty;

        public override PropertyCollection<TSet> GetProperties(ref TSet container) => new PropertyCollection<TSet>(Elements(container));

        IEnumerable<IProperty<TSet>> Elements(TSet container)
        {
            foreach (var element in container)
            {
                m_Shared.m_Value = element;
                yield return m_Shared;
            }
        }

        void ICollectionPropertyBagAccept<TSet>.Accept(ICollectionPropertyBagVisitor visitor, ref TSet container) => visitor.Visit(this, ref container);

        void ISetPropertyBagAccept<TSet>.Accept(ISetPropertyBagVisitor visitor, ref TSet container) => visitor.Visit(this, ref container);

        void ISetPropertyAccept<TSet>.Accept<TContainer>(ISetPropertyVisitor visitor, Property<TContainer, TSet> property, ref TContainer container, ref TSet set)
        {
            using (new AttributesScope(m_Shared, property))
                visitor.Visit<TContainer, TSet, TElement>(property, ref container, ref set);
        }

        public bool TryGetProperty(ref TSet container, object key, out IProperty<TSet> property)
        {
            var element = (TElement)key;
            if (container.Contains(element))
            {
                property = new SetElementProperty { m_Value = element };
                return true;
            }
            property = null;
            return false;
        }
    }

    public partial class HashSetPropertyBag<TElement> : SetPropertyBagBase<HashSet<TElement>, TElement>
    {
        public HashSetPropertyBag() { }

        protected override InstantiationKind InstantiationKind => InstantiationKind.PropertyBagOverride;

        protected override HashSet<TElement> Instantiate() => new HashSet<TElement>();
    }

    /// <summary>The entries of a dictionary: key/value pairs named by their keys.</summary>
    public partial class KeyValueCollectionPropertyBag<TDictionary, TKey, TValue> : PropertyBag<TDictionary>, IDictionaryPropertyBag<TDictionary, TKey, TValue>
        where TDictionary : IDictionary<TKey, TValue>
    {
        sealed class KeyValuePairProperty : Property<TDictionary, KeyValuePair<TKey, TValue>>, IDictionaryElementProperty<TKey>
        {
            public TKey Key { get; internal set; }
            public object ObjectKey => Key;
            public override string Name => Key.ToString();
            public override bool IsReadOnly => false;
            public override KeyValuePair<TKey, TValue> GetValue(ref TDictionary container) => new KeyValuePair<TKey, TValue>(Key, container[Key]);
            public override void SetValue(ref TDictionary container, KeyValuePair<TKey, TValue> value) => container[value.Key] = value.Value;
        }

        readonly KeyValuePairProperty m_Shared = new KeyValuePairProperty();

        public KeyValueCollectionPropertyBag() { }

        public override PropertyCollection<TDictionary> GetProperties() => PropertyCollection<TDictionary>.Empty;

        public override PropertyCollection<TDictionary> GetProperties(ref TDictionary container) => new PropertyCollection<TDictionary>(Entries(container));

        IEnumerable<IProperty<TDictionary>> Entries(TDictionary container)
        {
            // The keys are taken first: visitors may change the values.
            var keys = new List<TKey>(container.Keys);
            var previous = m_Shared.Key;
            try
            {
                foreach (var key in keys)
                {
                    m_Shared.Key = key;
                    yield return m_Shared;
                }
            }
            finally
            {
                m_Shared.Key = previous;
            }
        }

        void ICollectionPropertyBagAccept<TDictionary>.Accept(ICollectionPropertyBagVisitor visitor, ref TDictionary container) => visitor.Visit(this, ref container);

        void IDictionaryPropertyBagAccept<TDictionary>.Accept(IDictionaryPropertyBagVisitor visitor, ref TDictionary container) => visitor.Visit(this, ref container);

        void IDictionaryPropertyAccept<TDictionary>.Accept<TContainer>(IDictionaryPropertyVisitor visitor, Property<TContainer, TDictionary> property, ref TContainer container, ref TDictionary dictionary)
        {
            using (new AttributesScope(m_Shared, property))
                visitor.Visit<TContainer, TDictionary, TKey, TValue>(property, ref container, ref dictionary);
        }

        bool IKeyedProperties<TDictionary, object>.TryGetProperty(ref TDictionary container, object key, out IProperty<TDictionary> property)
        {
            var typed = (TKey)key;
            if (container.ContainsKey(typed))
            {
                property = new KeyValuePairProperty { Key = typed };
                return true;
            }
            property = null;
            return false;
        }
    }

    public partial class DictionaryPropertyBag<TKey, TValue> : KeyValueCollectionPropertyBag<Dictionary<TKey, TValue>, TKey, TValue>
    {
        public DictionaryPropertyBag() { }

        protected override InstantiationKind InstantiationKind => InstantiationKind.PropertyBagOverride;

        protected override Dictionary<TKey, TValue> Instantiate() => new Dictionary<TKey, TValue>();
    }

    /// <summary>A key/value pair: its read-only Key and Value.</summary>
    public partial class KeyValuePairPropertyBag<TKey, TValue> : PropertyBag<KeyValuePair<TKey, TValue>>, INamedProperties<KeyValuePair<TKey, TValue>>
    {
        static readonly DelegateProperty<KeyValuePair<TKey, TValue>, TKey> s_Key =
            new DelegateProperty<KeyValuePair<TKey, TValue>, TKey>("Key", (ref KeyValuePair<TKey, TValue> pair) => pair.Key);

        static readonly DelegateProperty<KeyValuePair<TKey, TValue>, TValue> s_Value =
            new DelegateProperty<KeyValuePair<TKey, TValue>, TValue>("Value", (ref KeyValuePair<TKey, TValue> pair) => pair.Value);

        public KeyValuePairPropertyBag() { }

        static IEnumerable<IProperty<KeyValuePair<TKey, TValue>>> Both()
        {
            yield return s_Key;
            yield return s_Value;
        }

        public override PropertyCollection<KeyValuePair<TKey, TValue>> GetProperties() => new PropertyCollection<KeyValuePair<TKey, TValue>>(Both());

        public override PropertyCollection<KeyValuePair<TKey, TValue>> GetProperties(ref KeyValuePair<TKey, TValue> container) => GetProperties();

        public bool TryGetProperty(ref KeyValuePair<TKey, TValue> container, string name, out IProperty<KeyValuePair<TKey, TValue>> property)
        {
            property = name == "Key" ? s_Key : name == "Value" ? s_Value : null;
            return property != null;
        }
    }

    /// <summary>Visits a property bag's container type with its concrete type.</summary>
    public abstract partial class ConcreteTypeVisitor : IPropertyBagVisitor
    {
        protected ConcreteTypeVisitor() { }

        protected abstract void VisitContainer<TContainer>(ref TContainer container);

        void IPropertyBagVisitor.Visit<TContainer>(IPropertyBag<TContainer> properties, ref TContainer container) => VisitContainer(ref container);
    }
}

namespace Unity.Properties.Internal
{
    /// <summary>The property bags known: registered ones (generated code, built-in types) and
    /// those the reflection provider makes on first use.</summary>
    internal static partial class PropertyBagStore
    {
        internal partial struct TypedStore<TContainer>
        {
            public static IPropertyBag<TContainer> PropertyBag;
        }

        static readonly ConcurrentDictionary<Type, IPropertyBag> s_Bags = new ConcurrentDictionary<Type, IPropertyBag>();
        static readonly List<Type> s_Types = new List<Type>();
        static ReflectedPropertyBagProvider s_Provider;
        static int s_Initialized;

        static ReflectedPropertyBagProvider Provider => s_Provider ??= new ReflectedPropertyBagProvider();

        internal static List<Type> AllTypes
        {
            get
            {
                EnsureInitialized();
                return s_Types;
            }
        }

        /// <summary>The built-in bags register before anything is looked up (the engine does it at
        /// startup).</summary>
        static void EnsureInitialized()
        {
            if (System.Threading.Interlocked.Exchange(ref s_Initialized, 1) == 0) PropertiesInitialization.InitializeProperties();
        }

        internal static void CreatePropertyBagProvider() => s_Provider = new ReflectedPropertyBagProvider();

        internal static void AddPropertyBag<TContainer>(IPropertyBag<TContainer> propertyBag)
        {
            EnsureInitialized();
            if (!TypeTraits<TContainer>.IsContainer)
                throw new Exception($"PropertyBagStore Type=[{typeof(TContainer)}] is not a valid container type. Type can not be primitive, enum or string.");
            if (TypeTraits<TContainer>.IsAbstractOrInterface)
                throw new Exception($"PropertyBagStore Type=[{typeof(TContainer)}] is not a valid container type. Type can not be abstract or interface.");
            var existing = TypedStore<TContainer>.PropertyBag;
            if (existing != null)
            {
                // A bag from the container's own assembly stays; so does any other when the new one
                // is generated code from another assembly.
                var containerAssembly = typeof(TContainer).Assembly;
                if (existing.GetType().Assembly == containerAssembly
                    || (propertyBag.GetType().IsDefined(typeof(CompilerGeneratedAttribute), true) && propertyBag.GetType().Assembly != containerAssembly))
                    return;
            }
            TypedStore<TContainer>.PropertyBag = propertyBag;
            lock (s_Types)
                if (!s_Bags.ContainsKey(typeof(TContainer))) s_Types.Add(typeof(TContainer));
            s_Bags[typeof(TContainer)] = propertyBag;
        }

        internal static IPropertyBag<TContainer> GetPropertyBag<TContainer>()
        {
            EnsureInitialized();
            var typed = TypedStore<TContainer>.PropertyBag;
            if (typed != null) return typed;
            var bag = GetPropertyBag(typeof(TContainer));
            if (bag == null) return null;
            return bag as IPropertyBag<TContainer> ?? throw new InvalidOperationException("PropertyBag type container type mismatch.");
        }

        internal static IPropertyBag GetPropertyBag(Type type)
        {
            EnsureInitialized();
            if (s_Bags.TryGetValue(type, out var bag)) return bag;
            if (!TypeTraits.IsContainer(type) || (type.IsArray && type.GetArrayRank() != 1) || type.IsInterface || type.IsAbstract || type == typeof(object))
                return null;
            bag = Provider.CreatePropertyBag(type);
            if (bag == null)
            {
                s_Bags.TryAdd(type, null);
                return null;
            }
            (bag as IPropertyBagRegister)?.Register();
            return bag;
        }

        internal static bool Exists<TContainer>()
        {
            EnsureInitialized();
            return TypedStore<TContainer>.PropertyBag != null;
        }

        internal static bool Exists(Type type)
        {
            EnsureInitialized();
            return s_Bags.ContainsKey(type);
        }

        internal static bool Exists<TContainer>(ref TContainer value)
        {
            if (!TypeTraits<TContainer>.CanBeNull) return GetPropertyBag<TContainer>() != null;
            if (EqualityComparer<TContainer>.Default.Equals(value, default)) return false;
            return GetPropertyBag(value.GetType()) != null;
        }

        internal static bool TryGetPropertyBagForValue<TValue>(ref TValue value, out IPropertyBag propertyBag)
        {
            if (!TypeTraits<TValue>.IsContainer)
            {
                propertyBag = null;
                return false;
            }
            if (TypeTraits<TValue>.IsValueType || (TypeTraits<TValue>.CanBeNull && EqualityComparer<TValue>.Default.Equals(value, default)))
                propertyBag = GetPropertyBag<TValue>();
            else
                propertyBag = GetPropertyBag(value.GetType());
            return propertyBag != null;
        }
    }

    internal static partial class PropertiesInitialization
    {
        public static void InitializeProperties()
        {
            PropertyBagStore.CreatePropertyBagProvider();
            PropertyBag.Register(new ColorPropertyBag());
            PropertyBag.Register(new Vector2PropertyBag());
            PropertyBag.Register(new Vector3PropertyBag());
            PropertyBag.Register(new Vector4PropertyBag());
            PropertyBag.Register(new Vector2IntPropertyBag());
            PropertyBag.Register(new Vector3IntPropertyBag());
            PropertyBag.Register(new RectPropertyBag());
            PropertyBag.Register(new RectIntPropertyBag());
            PropertyBag.Register(new BoundsPropertyBag());
            PropertyBag.Register(new BoundsIntPropertyBag());
            PropertyBag.Register(new SystemVersionPropertyBag());
        }
    }

    /// <summary>
    /// Makes property bags by reflection: arrays, lists, sets and dictionaries get their collection
    /// bags; other containers a bag of their members, derived type's first, each type's in
    /// declaration order: fields Unity serializes (public, [SerializeField], [SerializeReference];
    /// not [NonSerialized]) and members marked [CreateProperty], never [DontCreateProperty] ones.
    /// </summary>
    internal partial class ReflectedPropertyBagProvider
    {
        public ReflectedPropertyBagProvider() { }

        static readonly MethodInfo s_CreateGeneric = typeof(ReflectedPropertyBagProvider).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .First(m => m.Name == nameof(CreatePropertyBag) && m.IsGenericMethodDefinition);

        public IPropertyBag CreatePropertyBag(Type type)
        {
            if (type.IsGenericTypeDefinition) return null;
            try
            {
                return (IPropertyBag)s_CreateGeneric.MakeGenericMethod(type).Invoke(this, null);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        public IPropertyBag<TContainer> CreatePropertyBag<TContainer>()
        {
            var type = typeof(TContainer);
            if (!TypeTraits<TContainer>.IsContainer || TypeTraits<TContainer>.IsObject)
                throw new InvalidOperationException("Invalid container type.");
            if (type.IsArray)
            {
                if (type.GetArrayRank() != 1) throw new InvalidOperationException("Properties does not support multidimensional arrays.");
                return Make<TContainer>(typeof(ArrayPropertyBag<>), type.GetElementType());
            }
            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                var args = type.GetGenericArguments();
                if (definition == typeof(List<>)) return Make<TContainer>(typeof(ListPropertyBag<>), args[0]);
                if (definition == typeof(HashSet<>)) return Make<TContainer>(typeof(HashSetPropertyBag<>), args[0]);
                if (definition == typeof(Dictionary<,>)) return Make<TContainer>(typeof(DictionaryPropertyBag<,>), args[0], args[1]);
                if (definition == typeof(IList<>)) return Make<TContainer>(typeof(IndexedCollectionPropertyBag<,>), type, args[0]);
                if (definition == typeof(ISet<>)) return Make<TContainer>(typeof(SetPropertyBagBase<,>), type, args[0]);
                if (definition == typeof(IDictionary<,>)) return Make<TContainer>(typeof(KeyValueCollectionPropertyBag<,,>), type, args[0], args[1]);
                if (definition == typeof(KeyValuePair<,>)) return Make<TContainer>(typeof(KeyValuePairPropertyBag<,>), args[0], args[1]);
            }
            var bag = new ReflectedPropertyBag<TContainer>();
            foreach (var member in Members(type))
            {
                IMemberInfo info = member is FieldInfo field ? new FieldMember(field) : new PropertyMember((PropertyInfo)member);
                if (info.ValueType.IsPointer) continue;
                var property = (IProperty<TContainer>)Activator.CreateInstance(
                    typeof(ReflectedMemberProperty<,>).MakeGenericType(type, info.ValueType),
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new object[] { info, info.Name }, null);
                bag.AddPropertyObject(property);
            }
            return bag;
        }

        static IPropertyBag<TContainer> Make<TContainer>(Type bag, params Type[] arguments) =>
            (IPropertyBag<TContainer>)Activator.CreateInstance(bag.MakeGenericType(arguments));

        static IEnumerable<MemberInfo> Members(Type type)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var members = t.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(m => m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property)
                    .OrderBy(m => m.MetadataToken);
                foreach (var member in members)
                {
                    if (!IsValid(member) || member.IsDefined(typeof(DontCreatePropertyAttribute))) continue;
                    if (member.IsDefined(typeof(CreatePropertyAttribute))) yield return member;
                    else if (!member.IsDefined(typeof(NonSerializedAttribute))
                             && (member.IsDefined(typeof(SerializeField)) || member.IsDefined(typeof(SerializeReference))
                                 || (member is FieldInfo field && field.IsPublic)))
                        yield return member;
                }
            }
        }

        static bool IsValid(MemberInfo member) => member switch
        {
            FieldInfo field => !field.IsStatic && IsValidType(field.FieldType),
            PropertyInfo property => property.GetMethod != null && !property.GetMethod.IsStatic && IsValidType(property.PropertyType),
            _ => false,
        };

        static bool IsValidType(Type type) => !type.IsPointer && (!type.IsGenericType || type.GetGenericArguments().All(IsValidType));
    }

    [ReflectedPropertyBag]
    internal partial class ReflectedPropertyBag<TContainer> : ContainerPropertyBag<TContainer>
    {
        public ReflectedPropertyBag() { }

        internal new void AddProperty<TValue>(Property<TContainer, TValue> property)
        {
            // A derived type's member hides its base type's of the same name.
            TContainer container = default;
            if (TryGetProperty(ref container, property.Name, out var existing))
            {
                if (existing.DeclaredValueType() != typeof(TValue))
                    Debug.LogWarning($"Detected multiple return types for PropertyBag=[{TypeUtility.GetTypeDisplayName(typeof(TContainer))}] Property=[{property.Name}]. The property will use the most derived Type=[{TypeUtility.GetTypeDisplayName(existing.DeclaredValueType())}] and IgnoreType=[{TypeUtility.GetTypeDisplayName(property.DeclaredValueType())}].");
                return;
            }
            base.AddProperty(property);
        }

        static readonly MethodInfo s_AddProperty = typeof(ReflectedPropertyBag<TContainer>).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == nameof(AddProperty) && m.IsGenericMethodDefinition && m.DeclaringType == typeof(ReflectedPropertyBag<TContainer>));

        internal void AddPropertyObject(IProperty<TContainer> property) =>
            s_AddProperty.MakeGenericMethod(property.DeclaredValueType()).Invoke(this, new object[] { property });
    }

    // Built-in Unity types.

    sealed class FieldProperty<TContainer, TValue> : Property<TContainer, TValue>
    {
        readonly PropertyGetter<TContainer, TValue> m_Get;
        readonly PropertySetter<TContainer, TValue> m_Set;
        readonly bool m_ReadOnly;

        internal FieldProperty(string name, PropertyGetter<TContainer, TValue> get, PropertySetter<TContainer, TValue> set, bool readOnly = false, Attribute attribute = null)
        {
            Name = name;
            m_Get = get;
            m_Set = set;
            m_ReadOnly = readOnly;
            if (attribute != null) AddAttribute(attribute);
        }

        public override string Name { get; }
        public override bool IsReadOnly => m_ReadOnly;
        public override TValue GetValue(ref TContainer container) => m_Get(ref container);
        public override void SetValue(ref TContainer container, TValue value)
        {
            if (!m_ReadOnly) m_Set(ref container, value);
        }
    }

    internal partial class ColorPropertyBag : ContainerPropertyBag<Color>
    {
        public ColorPropertyBag()
        {
            AddProperty(new FieldProperty<Color, float>("r", (ref Color c) => c.r, (ref Color c, float v) => c.r = v));
            AddProperty(new FieldProperty<Color, float>("g", (ref Color c) => c.g, (ref Color c, float v) => c.g = v));
            AddProperty(new FieldProperty<Color, float>("b", (ref Color c) => c.b, (ref Color c, float v) => c.b = v));
            AddProperty(new FieldProperty<Color, float>("a", (ref Color c) => c.a, (ref Color c, float v) => c.a = v));
        }
    }

    internal partial class Vector2PropertyBag : ContainerPropertyBag<Vector2>
    {
        public Vector2PropertyBag()
        {
            AddProperty(new FieldProperty<Vector2, float>("x", (ref Vector2 c) => c.x, (ref Vector2 c, float v) => c.x = v));
            AddProperty(new FieldProperty<Vector2, float>("y", (ref Vector2 c) => c.y, (ref Vector2 c, float v) => c.y = v));
        }
    }

    internal partial class Vector3PropertyBag : ContainerPropertyBag<Vector3>
    {
        public Vector3PropertyBag()
        {
            AddProperty(new FieldProperty<Vector3, float>("x", (ref Vector3 c) => c.x, (ref Vector3 c, float v) => c.x = v));
            AddProperty(new FieldProperty<Vector3, float>("y", (ref Vector3 c) => c.y, (ref Vector3 c, float v) => c.y = v));
            AddProperty(new FieldProperty<Vector3, float>("z", (ref Vector3 c) => c.z, (ref Vector3 c, float v) => c.z = v));
        }
    }

    internal partial class Vector4PropertyBag : ContainerPropertyBag<Vector4>
    {
        public Vector4PropertyBag()
        {
            AddProperty(new FieldProperty<Vector4, float>("x", (ref Vector4 c) => c.x, (ref Vector4 c, float v) => c.x = v));
            AddProperty(new FieldProperty<Vector4, float>("y", (ref Vector4 c) => c.y, (ref Vector4 c, float v) => c.y = v));
            AddProperty(new FieldProperty<Vector4, float>("z", (ref Vector4 c) => c.z, (ref Vector4 c, float v) => c.z = v));
            AddProperty(new FieldProperty<Vector4, float>("w", (ref Vector4 c) => c.w, (ref Vector4 c, float v) => c.w = v));
        }
    }

    internal partial class Vector2IntPropertyBag : ContainerPropertyBag<Vector2Int>
    {
        public Vector2IntPropertyBag()
        {
            AddProperty(new FieldProperty<Vector2Int, int>("x", (ref Vector2Int c) => c.x, (ref Vector2Int c, int v) => c.x = v));
            AddProperty(new FieldProperty<Vector2Int, int>("y", (ref Vector2Int c) => c.y, (ref Vector2Int c, int v) => c.y = v));
        }
    }

    internal partial class Vector3IntPropertyBag : ContainerPropertyBag<Vector3Int>
    {
        public Vector3IntPropertyBag()
        {
            AddProperty(new FieldProperty<Vector3Int, int>("x", (ref Vector3Int c) => c.x, (ref Vector3Int c, int v) => c.x = v));
            AddProperty(new FieldProperty<Vector3Int, int>("y", (ref Vector3Int c) => c.y, (ref Vector3Int c, int v) => c.y = v));
            AddProperty(new FieldProperty<Vector3Int, int>("z", (ref Vector3Int c) => c.z, (ref Vector3Int c, int v) => c.z = v));
        }
    }

    internal partial class RectPropertyBag : ContainerPropertyBag<Rect>
    {
        public RectPropertyBag()
        {
            AddProperty(new FieldProperty<Rect, float>("x", (ref Rect c) => c.x, (ref Rect c, float v) => c.x = v));
            AddProperty(new FieldProperty<Rect, float>("y", (ref Rect c) => c.y, (ref Rect c, float v) => c.y = v));
            AddProperty(new FieldProperty<Rect, float>("width", (ref Rect c) => c.width, (ref Rect c, float v) => c.width = v));
            AddProperty(new FieldProperty<Rect, float>("height", (ref Rect c) => c.height, (ref Rect c, float v) => c.height = v));
        }
    }

    internal partial class RectIntPropertyBag : ContainerPropertyBag<RectInt>
    {
        public RectIntPropertyBag()
        {
            AddProperty(new FieldProperty<RectInt, int>("x", (ref RectInt c) => c.x, (ref RectInt c, int v) => c.x = v));
            AddProperty(new FieldProperty<RectInt, int>("y", (ref RectInt c) => c.y, (ref RectInt c, int v) => c.y = v));
            AddProperty(new FieldProperty<RectInt, int>("width", (ref RectInt c) => c.width, (ref RectInt c, int v) => c.width = v));
            AddProperty(new FieldProperty<RectInt, int>("height", (ref RectInt c) => c.height, (ref RectInt c, int v) => c.height = v));
        }
    }

    internal partial class BoundsPropertyBag : ContainerPropertyBag<Bounds>
    {
        public BoundsPropertyBag()
        {
            AddProperty(new FieldProperty<Bounds, Vector3>("center", (ref Bounds c) => c.center, (ref Bounds c, Vector3 v) => c.center = v));
            AddProperty(new FieldProperty<Bounds, Vector3>("extents", (ref Bounds c) => c.extents, (ref Bounds c, Vector3 v) => c.extents = v));
        }
    }

    internal partial class BoundsIntPropertyBag : ContainerPropertyBag<BoundsInt>
    {
        public BoundsIntPropertyBag()
        {
            AddProperty(new FieldProperty<BoundsInt, Vector3Int>("position", (ref BoundsInt c) => c.position, (ref BoundsInt c, Vector3Int v) => c.position = v));
            AddProperty(new FieldProperty<BoundsInt, Vector3Int>("size", (ref BoundsInt c) => c.size, (ref BoundsInt c, Vector3Int v) => c.size = v));
        }
    }

    /// <summary>System.Version: its read-only Major, Minor, Build and Revision.</summary>
    internal partial class SystemVersionPropertyBag : ContainerPropertyBag<Version>
    {
        public SystemVersionPropertyBag()
        {
            AddProperty(new FieldProperty<Version, int>("Major", (ref Version c) => c.Major, null, true, new MinAttribute(0f)));
            AddProperty(new FieldProperty<Version, int>("Minor", (ref Version c) => c.Minor, null, true, new MinAttribute(0f)));
            AddProperty(new FieldProperty<Version, int>("Build", (ref Version c) => c.Build, null, true, new MinAttribute(0f)));
            AddProperty(new FieldProperty<Version, int>("Revision", (ref Version c) => c.Revision, null, true, new MinAttribute(0f)));
        }
    }
}
