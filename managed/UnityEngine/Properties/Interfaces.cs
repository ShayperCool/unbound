using System;
using System.Collections.Generic;

namespace Unity.Properties
{
    public partial interface IProperty
    {
        string Name { get; }
        bool IsReadOnly { get; }
        Type DeclaredValueType();
        bool HasAttribute<TAttribute>() where TAttribute : Attribute;
        TAttribute GetAttribute<TAttribute>() where TAttribute : Attribute;
        IEnumerable<TAttribute> GetAttributes<TAttribute>() where TAttribute : Attribute;
        IEnumerable<Attribute> GetAttributes();
    }

    public partial interface IPropertyAccept<TContainer>
    {
        void Accept(IPropertyVisitor visitor, ref TContainer container);
    }

    public partial interface IProperty<TContainer> : IProperty, IPropertyAccept<TContainer>
    {
        object GetValue(ref TContainer container);
        void SetValue(ref TContainer container, object value);
    }

    public partial interface IPropertyBag
    {
        void Accept(ITypeVisitor visitor);
        void Accept(IPropertyBagVisitor visitor, ref object container);
    }

    public partial interface IPropertyBag<TContainer> : IPropertyBag
    {
        PropertyCollection<TContainer> GetProperties();
        PropertyCollection<TContainer> GetProperties(ref TContainer container);
        TContainer CreateInstance();
        bool TryCreateInstance(out TContainer instance);
        void Accept(IPropertyBagVisitor visitor, ref TContainer container);
    }

    public partial interface ITypeVisitor
    {
        void Visit<TContainer>();
    }

    public partial interface IPropertyBagVisitor
    {
        void Visit<TContainer>(IPropertyBag<TContainer> properties, ref TContainer container);
    }

    public partial interface IPropertyVisitor
    {
        void Visit<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container);
    }

    public partial interface INamedProperties<TContainer>
    {
        bool TryGetProperty(ref TContainer container, string name, out IProperty<TContainer> property);
    }

    public partial interface IIndexedProperties<TContainer>
    {
        bool TryGetProperty(ref TContainer container, int index, out IProperty<TContainer> property);
    }

    public partial interface IKeyedProperties<TContainer, TKey>
    {
        bool TryGetProperty(ref TContainer container, TKey key, out IProperty<TContainer> property);
    }

    // Collections.

    public partial interface ICollectionElementProperty
    {
    }

    public partial interface IListElementProperty : ICollectionElementProperty
    {
        int Index { get; }
    }

    public partial interface ISetElementProperty : ICollectionElementProperty
    {
        object ObjectKey { get; }
    }

    public partial interface ISetElementProperty<out TKey> : ISetElementProperty
    {
        TKey Key { get; }
    }

    public partial interface IDictionaryElementProperty : ICollectionElementProperty
    {
        object ObjectKey { get; }
    }

    public partial interface IDictionaryElementProperty<out TKey> : IDictionaryElementProperty
    {
        TKey Key { get; }
    }

    public partial interface ICollectionPropertyBagAccept<TContainer>
    {
        void Accept(ICollectionPropertyBagVisitor visitor, ref TContainer container);
    }

    public partial interface IListPropertyBagAccept<TContainer>
    {
        void Accept(IListPropertyBagVisitor visitor, ref TContainer container);
    }

    public partial interface ISetPropertyBagAccept<TContainer>
    {
        void Accept(ISetPropertyBagVisitor visitor, ref TContainer container);
    }

    public partial interface IDictionaryPropertyBagAccept<TContainer>
    {
        void Accept(IDictionaryPropertyBagVisitor visitor, ref TContainer container);
    }

    public partial interface ICollectionPropertyAccept<TCollection>
    {
        void Accept<TContainer>(ICollectionPropertyVisitor visitor, Property<TContainer, TCollection> property, ref TContainer container, ref TCollection collection);
    }

    public partial interface IListPropertyAccept<TList>
    {
        void Accept<TContainer>(IListPropertyVisitor visitor, Property<TContainer, TList> property, ref TContainer container, ref TList list);
    }

    public partial interface ISetPropertyAccept<TSet>
    {
        void Accept<TContainer>(ISetPropertyVisitor visitor, Property<TContainer, TSet> property, ref TContainer container, ref TSet set);
    }

    public partial interface IDictionaryPropertyAccept<TDictionary>
    {
        void Accept<TContainer>(IDictionaryPropertyVisitor visitor, Property<TContainer, TDictionary> property, ref TContainer container, ref TDictionary dictionary);
    }

    public partial interface ICollectionPropertyBag<TCollection, TElement> : IPropertyBag<TCollection>, ICollectionPropertyBagAccept<TCollection>
        where TCollection : ICollection<TElement>
    {
    }

    public partial interface IListPropertyBag<TList, TElement> : ICollectionPropertyBag<TList, TElement>, IListPropertyBagAccept<TList>, IListPropertyAccept<TList>, IIndexedProperties<TList>
        where TList : IList<TElement>
    {
    }

    public partial interface ISetPropertyBag<TSet, TElement> : ICollectionPropertyBag<TSet, TElement>, ISetPropertyBagAccept<TSet>, ISetPropertyAccept<TSet>, IKeyedProperties<TSet, object>
        where TSet : ISet<TElement>
    {
    }

    public partial interface IDictionaryPropertyBag<TDictionary, TKey, TValue> : ICollectionPropertyBag<TDictionary, KeyValuePair<TKey, TValue>>, IDictionaryPropertyBagAccept<TDictionary>, IDictionaryPropertyAccept<TDictionary>, IKeyedProperties<TDictionary, object>
        where TDictionary : IDictionary<TKey, TValue>
    {
    }

    public partial interface ICollectionPropertyBagVisitor
    {
        void Visit<TCollection, TElement>(ICollectionPropertyBag<TCollection, TElement> properties, ref TCollection container) where TCollection : ICollection<TElement>;
    }

    public partial interface IListPropertyBagVisitor
    {
        void Visit<TList, TElement>(IListPropertyBag<TList, TElement> properties, ref TList container) where TList : IList<TElement>;
    }

    public partial interface ISetPropertyBagVisitor
    {
        void Visit<TSet, TValue>(ISetPropertyBag<TSet, TValue> properties, ref TSet container) where TSet : ISet<TValue>;
    }

    public partial interface IDictionaryPropertyBagVisitor
    {
        void Visit<TDictionary, TKey, TValue>(IDictionaryPropertyBag<TDictionary, TKey, TValue> properties, ref TDictionary container) where TDictionary : IDictionary<TKey, TValue>;
    }

    public partial interface ICollectionPropertyVisitor
    {
        void Visit<TContainer, TCollection, TElement>(Property<TContainer, TCollection> property, ref TContainer container, ref TCollection collection) where TCollection : ICollection<TElement>;
    }

    public partial interface IListPropertyVisitor
    {
        void Visit<TContainer, TList, TElement>(Property<TContainer, TList> property, ref TContainer container, ref TList list) where TList : IList<TElement>;
    }

    public partial interface ISetPropertyVisitor
    {
        void Visit<TContainer, TSet, TValue>(Property<TContainer, TSet> property, ref TContainer container, ref TSet set) where TSet : ISet<TValue>;
    }

    public partial interface IDictionaryPropertyVisitor
    {
        void Visit<TContainer, TDictionary, TKey, TValue>(Property<TContainer, TDictionary> property, ref TContainer container, ref TDictionary dictionary) where TDictionary : IDictionary<TKey, TValue>;
    }

    // Visitor adapters.

    public partial interface IPropertyVisitorAdapter
    {
    }

    public partial interface IVisitPropertyAdapter : IPropertyVisitorAdapter
    {
        void Visit<TContainer, TValue>(in VisitContext<TContainer, TValue> context, ref TContainer container, ref TValue value);
    }

    public partial interface IVisitPropertyAdapter<TValue> : IPropertyVisitorAdapter
    {
        void Visit<TContainer>(in VisitContext<TContainer, TValue> context, ref TContainer container, ref TValue value);
    }

    public partial interface IVisitPropertyAdapter<TContainer, TValue> : IPropertyVisitorAdapter
    {
        void Visit(in VisitContext<TContainer, TValue> context, ref TContainer container, ref TValue value);
    }

    public partial interface IVisitContravariantPropertyAdapter<in TValue> : IPropertyVisitorAdapter
    {
        void Visit<TContainer>(in VisitContext<TContainer> context, ref TContainer container, TValue value);
    }

    public partial interface IVisitContravariantPropertyAdapter<TContainer, in TValue> : IPropertyVisitorAdapter
    {
        void Visit(in VisitContext<TContainer> context, ref TContainer container, TValue value);
    }

    public partial interface IVisitPrimitivesPropertyAdapter :
        IVisitPropertyAdapter<sbyte>, IVisitPropertyAdapter<short>, IVisitPropertyAdapter<int>, IVisitPropertyAdapter<long>,
        IVisitPropertyAdapter<byte>, IVisitPropertyAdapter<ushort>, IVisitPropertyAdapter<uint>, IVisitPropertyAdapter<ulong>,
        IVisitPropertyAdapter<float>, IVisitPropertyAdapter<double>, IVisitPropertyAdapter<bool>, IVisitPropertyAdapter<char>
    {
    }

    public partial interface IExcludePropertyAdapter : IPropertyVisitorAdapter
    {
        bool IsExcluded<TContainer, TValue>(in ExcludeContext<TContainer, TValue> context, ref TContainer container, ref TValue value);
    }

    public partial interface IExcludePropertyAdapter<TValue> : IPropertyVisitorAdapter
    {
        bool IsExcluded<TContainer>(in ExcludeContext<TContainer, TValue> context, ref TContainer container, ref TValue value);
    }

    public partial interface IExcludePropertyAdapter<TContainer, TValue> : IPropertyVisitorAdapter
    {
        bool IsExcluded(in ExcludeContext<TContainer, TValue> context, ref TContainer container, ref TValue value);
    }

    public partial interface IExcludeContravariantPropertyAdapter<in TValue> : IPropertyVisitorAdapter
    {
        bool IsExcluded<TContainer>(in ExcludeContext<TContainer> context, ref TContainer container, TValue value);
    }

    public partial interface IExcludeContravariantPropertyAdapter<TContainer, in TValue> : IPropertyVisitorAdapter
    {
        bool IsExcluded(in ExcludeContext<TContainer> context, ref TContainer container, TValue value);
    }

    // Construction.

    internal partial interface IConstructor
    {
        InstantiationKind InstantiationKind { get; }
    }

    internal partial interface IConstructor<out T> : IConstructor
    {
        T Instantiate();
    }

    internal partial interface IConstructorWithCount<out T> : IConstructor
    {
        T InstantiateWithCount(int count);
    }

    internal partial interface IIndexedCollectionPropertyBagEnumerator<TContainer>
    {
        int GetCount(ref TContainer container);
        IProperty<TContainer> GetSharedProperty();
        IndexedCollectionSharedPropertyState GetSharedPropertyState();
        void SetSharedPropertyState(IndexedCollectionSharedPropertyState state);
    }

    internal partial struct IndexedCollectionSharedPropertyState
    {
        public int Index;
        public bool IsReadOnly;
    }

    internal partial interface IMemberInfo
    {
        string Name { get; }
        bool IsReadOnly { get; }
        Type ValueType { get; }
        object GetValue(object obj);
        void SetValue(object obj, object value);
        IEnumerable<Attribute> GetCustomAttributes();
    }
}

namespace Unity.Properties.Internal
{
    internal partial interface IAttributes
    {
        List<Attribute> Attributes { get; set; }
        void AddAttribute(Attribute attribute);
        void AddAttributes(IEnumerable<Attribute> attributes);
        AttributesScope CreateAttributesScope(IAttributes attributes);
    }

    internal partial interface IPropertyBagRegister
    {
        void Register();
    }
}
