using System;
using System.Collections.Generic;
using Unity.Properties.Internal;

namespace Unity.Properties
{
    /// <summary>
    /// The base of visitors that walk containers property by property. For each property, the
    /// adapters added may exclude it or take over its visit (the first adapter whose types fit
    /// wins, and may continue with the following ones); otherwise collections go to
    /// VisitList/VisitSet/VisitDictionary/VisitCollection and other values to VisitProperty, which
    /// visits the value's own properties. A writable property gets the (possibly changed) value
    /// back.
    /// </summary>
    public abstract partial class PropertyVisitor : IPropertyBagVisitor, IListPropertyBagVisitor, IDictionaryPropertyBagVisitor,
        IPropertyVisitor, ICollectionPropertyVisitor, IListPropertyVisitor, ISetPropertyVisitor, IDictionaryPropertyVisitor
    {
        readonly List<IPropertyVisitorAdapter> m_Adapters = new List<IPropertyVisitorAdapter>();

        protected PropertyVisitor() { }

        public void AddAdapter(IPropertyVisitorAdapter adapter) => m_Adapters.Add(adapter);

        public void RemoveAdapter(IPropertyVisitorAdapter adapter) => m_Adapters.Remove(adapter);

        void IPropertyBagVisitor.Visit<TContainer>(IPropertyBag<TContainer> properties, ref TContainer container)
        {
            foreach (var property in properties.GetProperties(ref container)) property.Accept(this, ref container);
        }

        void IListPropertyBagVisitor.Visit<TList, TElement>(IListPropertyBag<TList, TElement> properties, ref TList container)
        {
            foreach (var property in properties.GetProperties(ref container)) property.Accept(this, ref container);
        }

        void IDictionaryPropertyBagVisitor.Visit<TDictionary, TKey, TValue>(IDictionaryPropertyBag<TDictionary, TKey, TValue> properties, ref TDictionary container)
        {
            foreach (var property in properties.GetProperties(ref container)) property.Accept(this, ref container);
        }

        void IPropertyVisitor.Visit<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container)
        {
            var value = property.GetValue(ref container);
            if (ExcludedByAdapters(property, ref container, ref value) || IsExcluded(property, ref container, ref value)) return;
            ContinueVisitation(property, new ReadOnlyAdapterCollection(m_Adapters).GetEnumerator(), ref container, ref value);
            if (!property.IsReadOnly) property.SetValue(ref container, value);
        }

        /// <summary>The visit of a value after the adapters: collections to their kind's method,
        /// others to VisitProperty.</summary>
        internal void ContinueVisitation<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container, ref TValue value)
        {
            if (PropertyBagStore.TryGetPropertyBagForValue(ref value, out var bag))
            {
                switch (bag)
                {
                    case IDictionaryPropertyAccept<TValue> dictionary:
                        dictionary.Accept(this, property, ref container, ref value);
                        return;
                    case IListPropertyAccept<TValue> list:
                        list.Accept(this, property, ref container, ref value);
                        return;
                    case ISetPropertyAccept<TValue> set:
                        set.Accept(this, property, ref container, ref value);
                        return;
                    case ICollectionPropertyAccept<TValue> collection:
                        collection.Accept(this, property, ref container, ref value);
                        return;
                }
            }
            VisitProperty(property, ref container, ref value);
        }

        /// <summary>Hands the value to the next adapter whose types fit (from where the enumerator
        /// stands); without one, visits it as usual.</summary>
        internal void ContinueVisitation<TContainer, TValue>(Property<TContainer, TValue> property, ReadOnlyAdapterCollection.Enumerator enumerator, ref TContainer container, ref TValue value)
        {
            while (enumerator.MoveNext())
            {
                switch (enumerator.Current)
                {
                    case IVisitPropertyAdapter<TContainer, TValue> exact:
                        exact.Visit(VisitContext<TContainer, TValue>.FromProperty(this, enumerator, property), ref container, ref value);
                        return;
                    case IVisitContravariantPropertyAdapter<TContainer, TValue> contravariantExact:
                        contravariantExact.Visit(VisitContext<TContainer>.FromProperty(this, enumerator, property), ref container, value);
                        value = property.GetValue(ref container);
                        return;
                    case IVisitPropertyAdapter<TValue> byValue:
                        byValue.Visit(VisitContext<TContainer, TValue>.FromProperty(this, enumerator, property), ref container, ref value);
                        return;
                    case IVisitContravariantPropertyAdapter<TValue> contravariant:
                        contravariant.Visit(VisitContext<TContainer>.FromProperty(this, enumerator, property), ref container, value);
                        value = property.GetValue(ref container);
                        return;
                    case IVisitPropertyAdapter any:
                        any.Visit(VisitContext<TContainer, TValue>.FromProperty(this, enumerator, property), ref container, ref value);
                        return;
                }
            }
            ContinueVisitationWithoutAdapters(property, enumerator, ref container, ref value);
        }

        internal void ContinueVisitationWithoutAdapters<TContainer, TValue>(Property<TContainer, TValue> property, ReadOnlyAdapterCollection.Enumerator enumerator, ref TContainer container, ref TValue value) =>
            ContinueVisitation(property, ref container, ref value);

        bool ExcludedByAdapters<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container, ref TValue value)
        {
            var enumerator = new ReadOnlyAdapterCollection(m_Adapters).GetEnumerator();
            while (enumerator.MoveNext())
            {
                switch (enumerator.Current)
                {
                    case IExcludePropertyAdapter<TContainer, TValue> exact:
                        if (exact.IsExcluded(ExcludeContext<TContainer, TValue>.FromProperty(this, property), ref container, ref value)) return true;
                        break;
                    case IExcludeContravariantPropertyAdapter<TContainer, TValue> contravariantExact:
                    {
                        bool excluded = contravariantExact.IsExcluded(ExcludeContext<TContainer>.FromProperty(this, property), ref container, value);
                        value = property.GetValue(ref container);
                        if (excluded) return true;
                        break;
                    }
                    case IExcludePropertyAdapter<TValue> byValue:
                        if (byValue.IsExcluded(ExcludeContext<TContainer, TValue>.FromProperty(this, property), ref container, ref value)) return true;
                        break;
                    case IExcludeContravariantPropertyAdapter<TValue> contravariant:
                    {
                        bool excluded = contravariant.IsExcluded(ExcludeContext<TContainer>.FromProperty(this, property), ref container, value);
                        value = property.GetValue(ref container);
                        if (excluded) return true;
                        break;
                    }
                    case IExcludePropertyAdapter any:
                        if (any.IsExcluded(ExcludeContext<TContainer, TValue>.FromProperty(this, property), ref container, ref value)) return true;
                        break;
                }
            }
            return false;
        }

        void ICollectionPropertyVisitor.Visit<TContainer, TCollection, TElement>(Property<TContainer, TCollection> property, ref TContainer container, ref TCollection collection) =>
            VisitCollection<TContainer, TCollection, TElement>(property, ref container, ref collection);

        void IListPropertyVisitor.Visit<TContainer, TList, TElement>(Property<TContainer, TList> property, ref TContainer container, ref TList list) =>
            VisitList<TContainer, TList, TElement>(property, ref container, ref list);

        void ISetPropertyVisitor.Visit<TContainer, TSet, TValue>(Property<TContainer, TSet> property, ref TContainer container, ref TSet set) =>
            VisitSet<TContainer, TSet, TValue>(property, ref container, ref set);

        void IDictionaryPropertyVisitor.Visit<TContainer, TDictionary, TKey, TValue>(Property<TContainer, TDictionary> property, ref TContainer container, ref TDictionary dictionary) =>
            VisitDictionary<TContainer, TDictionary, TKey, TValue>(property, ref container, ref dictionary);

        protected virtual bool IsExcluded<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container, ref TValue value) => false;

        protected virtual void VisitProperty<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container, ref TValue value) =>
            PropertyContainer.TryAccept(this, ref value);

        protected virtual void VisitCollection<TContainer, TCollection, TElement>(Property<TContainer, TCollection> property, ref TContainer container, ref TCollection value)
            where TCollection : ICollection<TElement> =>
            VisitProperty(property, ref container, ref value);

        protected virtual void VisitList<TContainer, TList, TElement>(Property<TContainer, TList> property, ref TContainer container, ref TList value)
            where TList : IList<TElement> =>
            VisitCollection<TContainer, TList, TElement>(property, ref container, ref value);

        protected virtual void VisitSet<TContainer, TSet, TValue>(Property<TContainer, TSet> property, ref TContainer container, ref TSet value)
            where TSet : ISet<TValue> =>
            VisitCollection<TContainer, TSet, TValue>(property, ref container, ref value);

        protected virtual void VisitDictionary<TContainer, TDictionary, TKey, TValue>(Property<TContainer, TDictionary> property, ref TContainer container, ref TDictionary value)
            where TDictionary : IDictionary<TKey, TValue> =>
            VisitCollection<TContainer, TDictionary, KeyValuePair<TKey, TValue>>(property, ref container, ref value);
    }

    /// <summary>What an adapter's visit gets: the property and how to go on with the visit.</summary>
    public readonly partial struct VisitContext<TContainer, TValue>
    {
        readonly PropertyVisitor m_Visitor;
        readonly ReadOnlyAdapterCollection.Enumerator m_Enumerator;

        VisitContext(PropertyVisitor visitor, ReadOnlyAdapterCollection.Enumerator enumerator, Property<TContainer, TValue> property)
        {
            m_Visitor = visitor;
            m_Enumerator = enumerator;
            Property = property;
        }

        internal static VisitContext<TContainer, TValue> FromProperty(PropertyVisitor visitor, ReadOnlyAdapterCollection.Enumerator enumerator, Property<TContainer, TValue> property) =>
            new VisitContext<TContainer, TValue>(visitor, enumerator, property);

        public Property<TContainer, TValue> Property { get; }

        public void ContinueVisitation(ref TContainer container, ref TValue value) =>
            m_Visitor.ContinueVisitation(Property, m_Enumerator, ref container, ref value);

        public void ContinueVisitationWithoutAdapters(ref TContainer container, ref TValue value) =>
            m_Visitor.ContinueVisitationWithoutAdapters(Property, m_Enumerator, ref container, ref value);
    }

    /// <summary>What a contravariant adapter's visit gets: the property without its value type.</summary>
    public readonly partial struct VisitContext<TContainer>
    {
        delegate void Continue(PropertyVisitor visitor, ReadOnlyAdapterCollection.Enumerator enumerator, IProperty<TContainer> property, ref TContainer container);
        delegate void ContinueWithout(PropertyVisitor visitor, IProperty<TContainer> property, ref TContainer container);

        readonly PropertyVisitor m_Visitor;
        readonly ReadOnlyAdapterCollection.Enumerator m_Enumerator;
        readonly Continue m_Continue;
        readonly ContinueWithout m_ContinueWithout;

        VisitContext(PropertyVisitor visitor, ReadOnlyAdapterCollection.Enumerator enumerator, IProperty<TContainer> property, Continue next, ContinueWithout without)
        {
            m_Visitor = visitor;
            m_Enumerator = enumerator;
            Property = property;
            m_Continue = next;
            m_ContinueWithout = without;
        }

        internal static VisitContext<TContainer> FromProperty<TValue>(PropertyVisitor visitor, ReadOnlyAdapterCollection.Enumerator enumerator, Property<TContainer, TValue> property) =>
            new VisitContext<TContainer>(visitor, enumerator, property,
                (PropertyVisitor v, ReadOnlyAdapterCollection.Enumerator e, IProperty<TContainer> p, ref TContainer c) =>
                {
                    var typed = (Property<TContainer, TValue>)p;
                    var value = typed.GetValue(ref c);
                    v.ContinueVisitation(typed, e, ref c, ref value);
                },
                (PropertyVisitor v, IProperty<TContainer> p, ref TContainer c) =>
                {
                    var typed = (Property<TContainer, TValue>)p;
                    var value = typed.GetValue(ref c);
                    v.ContinueVisitation(typed, ref c, ref value);
                });

        public IProperty<TContainer> Property { get; }

        public void ContinueVisitation(ref TContainer container) => m_Continue(m_Visitor, m_Enumerator, Property, ref container);

        public void ContinueVisitationWithoutAdapters(ref TContainer container) => m_ContinueWithout(m_Visitor, Property, ref container);
    }

    public readonly partial struct ExcludeContext<TContainer, TValue>
    {
        readonly PropertyVisitor m_Visitor;

        ExcludeContext(PropertyVisitor visitor, Property<TContainer, TValue> property)
        {
            m_Visitor = visitor;
            Property = property;
        }

        internal static ExcludeContext<TContainer, TValue> FromProperty(PropertyVisitor visitor, Property<TContainer, TValue> property) =>
            new ExcludeContext<TContainer, TValue>(visitor, property);

        public Property<TContainer, TValue> Property { get; }
    }

    public readonly partial struct ExcludeContext<TContainer>
    {
        readonly PropertyVisitor m_Visitor;

        ExcludeContext(PropertyVisitor visitor, IProperty<TContainer> property)
        {
            m_Visitor = visitor;
            Property = property;
        }

        internal static ExcludeContext<TContainer> FromProperty<TValue>(PropertyVisitor visitor, Property<TContainer, TValue> property) =>
            new ExcludeContext<TContainer>(visitor, property);

        public IProperty<TContainer> Property { get; }
    }

    /// <summary>
    /// A visitor that follows a <see cref="PropertyPath"/> from the container to the property it
    /// names, writing values back on the way out unless the visit is read-only, and gives that
    /// property to <see cref="VisitPath"/>. <see cref="ReturnCode"/> says why a path was not
    /// followed.
    /// </summary>
    public abstract partial class PathVisitor : IPropertyBagVisitor, IPropertyVisitor
    {
        int m_PathIndex;
        IProperty m_Parent;

        protected PathVisitor() { }

        public PropertyPath Path { get; set; }

        public bool ReadonlyVisit { get; set; }

        public VisitReturnCode ReturnCode { get; protected set; }

        public virtual void Reset()
        {
            m_PathIndex = 0;
            Path = default;
            ReturnCode = VisitReturnCode.Ok;
            ReadonlyVisit = false;
        }

        void IPropertyBagVisitor.Visit<TContainer>(IPropertyBag<TContainer> properties, ref TContainer container)
        {
            var part = Path[m_PathIndex++];
            IProperty<TContainer> property;
            switch (part.Kind)
            {
                case PropertyPathPartKind.Name:
                    if (properties is INamedProperties<TContainer> named && named.TryGetProperty(ref container, part.Name, out property))
                        property.Accept(this, ref container);
                    else
                        ReturnCode = VisitReturnCode.InvalidPath;
                    return;
                case PropertyPathPartKind.Index:
                    if (!(properties is IIndexedProperties<TContainer> indexed))
                    {
                        ReturnCode = VisitReturnCode.InvalidPath;
                        return;
                    }
                    if (properties is IIndexedCollectionPropertyBagEnumerator<TContainer> shared && part.Index < shared.GetCount(ref container))
                    {
                        // The collection's shared element property, moved to the index for the visit.
                        var previous = shared.GetSharedPropertyState();
                        shared.SetSharedPropertyState(new IndexedCollectionSharedPropertyState { Index = part.Index, IsReadOnly = false });
                        var element = shared.GetSharedProperty();
                        using ((element as IAttributes)?.CreateAttributesScope(m_Parent as IAttributes))
                            element.Accept(this, ref container);
                        shared.SetSharedPropertyState(previous);
                        return;
                    }
                    if (indexed.TryGetProperty(ref container, part.Index, out property))
                    {
                        using ((property as IAttributes)?.CreateAttributesScope(m_Parent as IAttributes))
                            property.Accept(this, ref container);
                        return;
                    }
                    ReturnCode = VisitReturnCode.InvalidPath;
                    return;
                case PropertyPathPartKind.Key:
                    if (properties is IKeyedProperties<TContainer, object> keyed && keyed.TryGetProperty(ref container, part.Key, out property))
                    {
                        using ((property as IAttributes)?.CreateAttributesScope(m_Parent as IAttributes))
                            property.Accept(this, ref container);
                        return;
                    }
                    ReturnCode = VisitReturnCode.InvalidPath;
                    return;
                default:
                    ReturnCode = VisitReturnCode.InvalidPath;
                    return;
            }
        }

        void IPropertyVisitor.Visit<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container)
        {
            var value = property.GetValue(ref container);
            if (m_PathIndex >= Path.Length)
            {
                VisitPath(property, ref container, ref value);
                return;
            }
            if (!PropertyBag.TryGetPropertyBagForValue(ref value, out _))
            {
                ReturnCode = VisitReturnCode.InvalidPath;
                return;
            }
            if (TypeTraits<TValue>.CanBeNull && EqualityComparer<TValue>.Default.Equals(value, default))
            {
                ReturnCode = VisitReturnCode.InvalidPath;
                return;
            }
            var parent = m_Parent;
            m_Parent = property;
            try { PropertyContainer.Accept(this, ref value); }
            finally { m_Parent = parent; }
            if (!property.IsReadOnly && !ReadonlyVisit) property.SetValue(ref container, value);
        }

        protected virtual void VisitPath<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container, ref TValue value) { }
    }
}

namespace Unity.Properties.Internal
{
    internal readonly partial struct ReadOnlyAdapterCollection
    {
        readonly List<IPropertyVisitorAdapter> m_Adapters;

        public ReadOnlyAdapterCollection(List<IPropertyVisitorAdapter> adapters) => m_Adapters = adapters;

        public Enumerator GetEnumerator() => new Enumerator(this);

        public partial struct Enumerator
        {
            readonly List<IPropertyVisitorAdapter> m_Adapters;
            int m_Index;

            public Enumerator(ReadOnlyAdapterCollection collection)
            {
                m_Adapters = collection.m_Adapters;
                m_Index = 0;
                Current = null;
            }

            public IPropertyVisitorAdapter Current { get; private set; }

            public bool MoveNext()
            {
                if (m_Adapters == null || m_Index >= m_Adapters.Count) return false;
                Current = m_Adapters[m_Index++];
                return true;
            }
        }
    }
}
