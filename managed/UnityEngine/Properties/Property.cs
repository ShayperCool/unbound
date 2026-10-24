using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Properties.Internal;

namespace Unity.Properties
{
    /// <summary>
    /// A property of a container: reads and writes one value of type <typeparamref name="TValue"/>
    /// on a <typeparamref name="TContainer"/>, carries the attributes of its member, and dispatches
    /// visitors to itself with its exact types.
    /// </summary>
    public abstract partial class Property<TContainer, TValue> : IProperty<TContainer>, IAttributes
    {
        List<Attribute> m_Attributes;

        protected Property() { }

        public abstract string Name { get; }

        public abstract bool IsReadOnly { get; }

        public Type DeclaredValueType() => typeof(TValue);

        public void Accept(IPropertyVisitor visitor, ref TContainer container) => visitor.Visit(this, ref container);

        public abstract TValue GetValue(ref TContainer container);

        public abstract void SetValue(ref TContainer container, TValue value);

        object IProperty<TContainer>.GetValue(ref TContainer container) => GetValue(ref container);

        void IProperty<TContainer>.SetValue(ref TContainer container, object value) =>
            SetValue(ref container, TypeConversion.Convert<object, TValue>(ref value));

        // Attributes: those of the member, without [CreateProperty] itself.

        List<Attribute> IAttributes.Attributes
        {
            get => m_Attributes;
            set => m_Attributes = value;
        }

        protected void AddAttribute(Attribute attribute) => ((IAttributes)this).AddAttribute(attribute);

        protected void AddAttributes(IEnumerable<Attribute> attributes) => ((IAttributes)this).AddAttributes(attributes);

        void IAttributes.AddAttribute(Attribute attribute)
        {
            if (attribute == null || attribute.GetType() == typeof(CreatePropertyAttribute)) return;
            (m_Attributes ??= new List<Attribute>()).Add(attribute);
        }

        void IAttributes.AddAttributes(IEnumerable<Attribute> attributes)
        {
            m_Attributes ??= new List<Attribute>();
            foreach (var attribute in attributes)
                if (attribute != null) m_Attributes.Add(attribute);
        }

        AttributesScope IAttributes.CreateAttributesScope(IAttributes attributes) => new AttributesScope(this, attributes?.Attributes);

        public bool HasAttribute<TAttribute>() where TAttribute : Attribute => GetAttribute<TAttribute>() != null;

        public TAttribute GetAttribute<TAttribute>() where TAttribute : Attribute
        {
            if (m_Attributes == null) return null;
            foreach (var attribute in m_Attributes)
                if (attribute is TAttribute typed) return typed;
            return null;
        }

        public IEnumerable<TAttribute> GetAttributes<TAttribute>() where TAttribute : Attribute
        {
            if (m_Attributes == null) yield break;
            for (int i = 0; i < m_Attributes.Count; i++)
                if (m_Attributes[i] is TAttribute typed) yield return typed;
        }

        public IEnumerable<Attribute> GetAttributes()
        {
            if (m_Attributes == null) yield break;
            for (int i = 0; i < m_Attributes.Count; i++) yield return m_Attributes[i];
        }
    }

    /// <summary>Lends a property the attributes of another while it is visited in its place (the
    /// element properties of collections carry their collection's attributes).</summary>
    public readonly partial struct AttributesScope : IDisposable
    {
        readonly IAttributes m_Target;
        readonly List<Attribute> m_Previous;

        public AttributesScope(IProperty target, IProperty source)
        {
            m_Target = target as IAttributes;
            m_Previous = m_Target?.Attributes;
            if (m_Target != null) m_Target.Attributes = (source as IAttributes)?.Attributes;
        }

        internal AttributesScope(IAttributes target, List<Attribute> attributes)
        {
            m_Target = target;
            m_Previous = target.Attributes;
            target.Attributes = attributes;
        }

        public void Dispose()
        {
            if (m_Target != null) m_Target.Attributes = m_Previous;
        }
    }

    /// <summary>A property made of a getter and an optional setter (read-only without one).</summary>
    public partial class DelegateProperty<TContainer, TValue> : Property<TContainer, TValue>
    {
        readonly PropertyGetter<TContainer, TValue> m_Getter;
        readonly PropertySetter<TContainer, TValue> m_Setter;

        public DelegateProperty(string name, PropertyGetter<TContainer, TValue> getter, PropertySetter<TContainer, TValue> setter = null)
        {
            Name = name;
            m_Getter = getter ?? throw new ArgumentException(nameof(getter));
            m_Setter = setter;
        }

        public override string Name { get; }

        public override bool IsReadOnly => m_Setter == null;

        public override TValue GetValue(ref TContainer container) => m_Getter(ref container);

        public override void SetValue(ref TContainer container, TValue value)
        {
            if (IsReadOnly) throw new InvalidOperationException("Property is ReadOnly.");
            m_Setter(ref container, value);
        }
    }

    /// <summary>
    /// A property over a field or C# property found by reflection. Fields are read and written
    /// through compiled accessors (structs by reference); read-only fields, properties without a
    /// setter and [CreateProperty(ReadOnly = true)] members are read-only.
    /// </summary>
    public partial class ReflectedMemberProperty<TContainer, TValue> : Property<TContainer, TValue>
    {
        delegate TValue StructGetter(ref TContainer container);
        delegate void StructSetter(ref TContainer container, TValue value);
        delegate TValue ClassGetter(TContainer container);
        delegate void ClassSetter(TContainer container, TValue value);

        readonly IMemberInfo m_Info;
        readonly bool m_IsStruct;
        StructGetter m_StructGet;
        StructSetter m_StructSet;
        ClassGetter m_ClassGet;
        ClassSetter m_ClassSet;

        public ReflectedMemberProperty(FieldInfo info, string name) : this(new FieldMember(info), name) { }

        public ReflectedMemberProperty(PropertyInfo info, string name) : this(new PropertyMember(info), name) { }

        internal ReflectedMemberProperty(IMemberInfo info, string name)
        {
            Name = name;
            m_Info = info;
            m_IsStruct = typeof(TContainer).IsValueType;
            AddAttributes(info.GetCustomAttributes());
            bool readOnly = info.IsReadOnly;
            var create = GetAttribute<CreatePropertyAttribute>();
            if (create != null) readOnly |= create.ReadOnly;
            IsReadOnly = readOnly;
            try { CompileAccessors(readOnly); }
            catch (Exception)
            {
                // Reflection reads and writes instead.
                m_StructGet = null; m_StructSet = null; m_ClassGet = null; m_ClassSet = null;
            }
        }

        void CompileAccessors(bool readOnly)
        {
            if (m_Info is FieldMember field)
            {
                var f = field.m_FieldInfo;
                var owner = m_IsStruct ? f.ReflectedType.MakeByRefType() : f.ReflectedType;
                var get = new System.Reflection.Emit.DynamicMethod("get_" + f.Name, f.FieldType, new[] { owner }, typeof(ReflectedMemberProperty<TContainer, TValue>).Module, true);
                var il = get.GetILGenerator();
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Ldfld, f);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
                if (m_IsStruct) m_StructGet = (StructGetter)get.CreateDelegate(typeof(StructGetter));
                else m_ClassGet = (ClassGetter)get.CreateDelegate(typeof(ClassGetter));
                if (readOnly || f.IsInitOnly) return;
                var set = new System.Reflection.Emit.DynamicMethod("set_" + f.Name, typeof(void), new[] { owner, f.FieldType }, typeof(ReflectedMemberProperty<TContainer, TValue>).Module, true);
                il = set.GetILGenerator();
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
                il.Emit(System.Reflection.Emit.OpCodes.Stfld, f);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
                if (m_IsStruct) m_StructSet = (StructSetter)set.CreateDelegate(typeof(StructSetter));
                else m_ClassSet = (ClassSetter)set.CreateDelegate(typeof(ClassSetter));
            }
            else if (m_Info is PropertyMember property)
            {
                var p = property.m_PropertyInfo;
                var getter = p.GetGetMethod(true);
                var setter = readOnly ? null : p.GetSetMethod(true);
                if (m_IsStruct)
                {
                    m_StructGet = (StructGetter)Delegate.CreateDelegate(typeof(StructGetter), getter);
                    if (setter != null) m_StructSet = (StructSetter)Delegate.CreateDelegate(typeof(StructSetter), setter);
                }
                else
                {
                    m_ClassGet = (ClassGetter)Delegate.CreateDelegate(typeof(ClassGetter), getter);
                    if (setter != null) m_ClassSet = (ClassSetter)Delegate.CreateDelegate(typeof(ClassSetter), setter);
                }
            }
        }

        public override string Name { get; }

        public override bool IsReadOnly { get; }

        public override TValue GetValue(ref TContainer container)
        {
            if (m_IsStruct) return m_StructGet != null ? m_StructGet(ref container) : (TValue)m_Info.GetValue(container);
            return m_ClassGet != null ? m_ClassGet(container) : (TValue)m_Info.GetValue(container);
        }

        public override void SetValue(ref TContainer container, TValue value)
        {
            if (IsReadOnly) throw new InvalidOperationException("Property is ReadOnly.");
            if (m_IsStruct)
            {
                if (m_StructSet != null)
                {
                    m_StructSet(ref container, value);
                    return;
                }
                // A boxed copy is written, then copied back.
                object boxed = container;
                m_Info.SetValue(boxed, value);
                container = (TContainer)boxed;
                return;
            }
            if (m_ClassSet != null) m_ClassSet(container, value);
            else m_Info.SetValue(container, value);
        }
    }

    internal readonly partial struct FieldMember : IMemberInfo
    {
        internal readonly FieldInfo m_FieldInfo;

        public FieldMember(FieldInfo fieldInfo)
        {
            m_FieldInfo = fieldInfo;
            Name = ReflectionUtilities.SanitizeMemberName(fieldInfo);
        }

        public string Name { get; }

        public bool IsReadOnly => m_FieldInfo.IsInitOnly;

        public Type ValueType => m_FieldInfo.FieldType;

        public object GetValue(object obj) => m_FieldInfo.GetValue(obj);

        public void SetValue(object obj, object value) => m_FieldInfo.SetValue(obj, value);

        public IEnumerable<Attribute> GetCustomAttributes() => m_FieldInfo.GetCustomAttributes();
    }

    internal readonly partial struct PropertyMember : IMemberInfo
    {
        internal readonly PropertyInfo m_PropertyInfo;

        public PropertyMember(PropertyInfo propertyInfo)
        {
            m_PropertyInfo = propertyInfo;
            Name = ReflectionUtilities.SanitizeMemberName(propertyInfo);
        }

        public string Name { get; }

        public bool IsReadOnly => !m_PropertyInfo.CanWrite;

        public Type ValueType => m_PropertyInfo.PropertyType;

        public object GetValue(object obj) => m_PropertyInfo.GetValue(obj);

        public void SetValue(object obj, object value) => m_PropertyInfo.SetValue(obj, value);

        public IEnumerable<Attribute> GetCustomAttributes() => m_PropertyInfo.GetCustomAttributes();
    }
}

namespace Unity.Properties.Internal
{
    internal static partial class ReflectionUtilities
    {
        /// <summary>A member name usable as a property name: compiler-generated characters made
        /// underscores (`&lt;Value&gt;k__BackingField` -> `_Value_k__BackingField`).</summary>
        public static string SanitizeMemberName(MemberInfo info) =>
            info.Name.Replace(".", "_").Replace("<", "_").Replace(">", "_").Replace("+", "_");
    }
}
