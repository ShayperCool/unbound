using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Properties.Internal;
using UnityEngine;

namespace Unity.Properties
{
    public static partial class TypeTraits
    {
        /// <summary>A type with properties: not a primitive, pointer, enum or string.</summary>
        public static bool IsContainer(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return !type.IsPrimitive && !type.IsPointer && !type.IsEnum && type != typeof(string);
        }
    }

    public static partial class TypeTraits<T>
    {
        static TypeTraits()
        {
            var type = typeof(T);
            IsValueType = type.IsValueType;
            IsPrimitive = type.IsPrimitive;
            IsInterface = type.IsInterface;
            IsAbstract = type.IsAbstract;
            IsArray = type.IsArray;
            IsEnum = type.IsEnum;
            IsEnumFlags = IsEnum && type.IsDefined(typeof(FlagsAttribute), false);
            IsNullable = Nullable.GetUnderlyingType(type) != null;
            IsMultidimensionalArray = IsArray && type.GetArrayRank() != 1;
            IsObject = type == typeof(object);
            IsString = type == typeof(string);
            IsContainer = TypeTraits.IsContainer(type);
            CanBeNull = !IsValueType || IsNullable;
            IsPrimitiveOrString = IsPrimitive || IsString;
            IsAbstractOrInterface = IsAbstract || IsInterface;
            IsLazyLoadReference = type.IsGenericType && type.GetGenericTypeDefinition().FullName == "UnityEngine.LazyLoadReference`1";
            IsUnityObject = typeof(UnityEngine.Object).IsAssignableFrom(type);
        }

        public static bool IsValueType { get; }
        public static bool IsPrimitive { get; }
        public static bool IsInterface { get; }
        public static bool IsAbstract { get; }
        public static bool IsArray { get; }
        public static bool IsMultidimensionalArray { get; }
        public static bool IsEnum { get; }
        public static bool IsEnumFlags { get; }
        public static bool IsNullable { get; }
        public static bool IsObject { get; }
        public static bool IsString { get; }
        public static bool IsContainer { get; }
        public static bool CanBeNull { get; }
        public static bool IsPrimitiveOrString { get; }
        public static bool IsAbstractOrInterface { get; }
        public static bool IsUnityObject { get; }
        public static bool IsLazyLoadReference { get; }
    }

    /// <summary>Type converters keyed by source and destination types.</summary>
    internal readonly partial struct ConversionRegistry : IEqualityComparer<ConversionRegistry>
    {
        readonly Dictionary<(Type Source, Type Destination), Delegate> m_Converters;

        ConversionRegistry(Dictionary<(Type, Type), Delegate> converters) => m_Converters = converters;

        public static ConversionRegistry Create() => new ConversionRegistry(new Dictionary<(Type, Type), Delegate>());

        public void Register(Type source, Type destination, Delegate converter) =>
            m_Converters[(source, destination)] = converter ?? throw new ArgumentException("converter");

        public Delegate GetConverter(Type source, Type destination) =>
            m_Converters.TryGetValue((source, destination), out var converter) ? converter : null;

        public bool TryGetConverter(Type source, Type destination, out Delegate converter)
        {
            converter = GetConverter(source, destination);
            return converter != null;
        }

        public bool Equals(ConversionRegistry x, ConversionRegistry y) => x.m_Converters == y.m_Converters;

        public int GetHashCode(ConversionRegistry obj) => obj.m_Converters?.GetHashCode() ?? 0;
    }

    /// <summary>
    /// Value conversions for properties: registered converters first, then the built-in ones
    /// (numbers, booleans and characters convert as C# casts do, strings parse and print,
    /// objects unbox only their exact type), then nullable, enum, UnityEngine.Object and
    /// assignment rules.
    /// </summary>
    public static partial class TypeConversion
    {
        static readonly ConversionRegistry s_Converters = ConversionRegistry.Create();

        static TypeConversion() => s_Converters.Register(typeof(string), typeof(Guid), (TypeConverter<string, Guid>)((ref string g) => new Guid(g)));

        public static void Register<TSource, TDestination>(TypeConverter<TSource, TDestination> converter) =>
            s_Converters.Register(typeof(TSource), typeof(TDestination), converter);

        public static TDestination Convert<TSource, TDestination>(ref TSource value)
        {
            if (!TryConvert(ref value, out TDestination destination))
                throw new InvalidOperationException($"TypeConversion no converter has been registered for SrcType=[{typeof(TSource)}] to DstType=[{typeof(TDestination)}]");
            return destination;
        }

        public static bool TryConvert<TSource, TDestination>(ref TSource source, out TDestination destination)
        {
            if (s_Converters.TryGetConverter(typeof(TSource), typeof(TDestination), out var converter))
            {
                destination = ((TypeConverter<TSource, TDestination>)converter)(ref source);
                return true;
            }
            if (typeof(TSource).IsValueType && typeof(TSource) == typeof(TDestination))
            {
                destination = UnsafeUtility.As<TSource, TDestination>(ref source);
                return true;
            }
            if (BuiltIn.TryConvert(source, out destination)) return true;
            if (TypeTraits<TDestination>.IsNullable)
            {
                if (TypeTraits<TSource>.IsNullable && Nullable.GetUnderlyingType(typeof(TDestination)) != Nullable.GetUnderlyingType(typeof(TSource)))
                {
                    destination = default;
                    return false;
                }
                var underlying = Nullable.GetUnderlyingType(typeof(TDestination));
                if (underlying.IsEnum)
                {
                    var number = System.Convert.ChangeType(source, Enum.GetUnderlyingType(underlying));
                    destination = (TDestination)Enum.ToObject(underlying, number);
                    return true;
                }
                if (source == null)
                {
                    destination = default;
                    return true;
                }
                destination = (TDestination)System.Convert.ChangeType(source, underlying);
                return true;
            }
            if (TypeTraits<TSource>.IsNullable && typeof(TDestination) == Nullable.GetUnderlyingType(typeof(TSource)))
            {
                if (source == null)
                {
                    destination = default;
                    return false;
                }
                destination = (TDestination)(object)source;
                return true;
            }
            if (TypeTraits<TDestination>.IsUnityObject && ToUnityObject(source, out destination)) return true;
            if (TypeTraits<TDestination>.IsEnum)
            {
                if (typeof(TSource) == typeof(string))
                {
                    try
                    {
                        destination = (TDestination)Enum.Parse(typeof(TDestination), (string)(object)source);
                        return true;
                    }
                    catch (ArgumentException)
                    {
                        destination = default;
                        return false;
                    }
                }
                if (IsNumeric(typeof(TSource)))
                {
                    destination = UnsafeUtility.As<TSource, TDestination>(ref source);
                    return true;
                }
            }
            if (source is TDestination direct)
            {
                destination = direct;
                return true;
            }
            if (typeof(TDestination).IsAssignableFrom(typeof(TSource)))
            {
                destination = (TDestination)(object)source;
                return true;
            }
            destination = default;
            return false;
        }

        static bool ToUnityObject<TSource, TDestination>(TSource source, out TDestination destination)
        {
            destination = default;
            if (!typeof(UnityEngine.Object).IsAssignableFrom(typeof(TDestination))) return false;
            if (typeof(UnityEngine.Object).IsAssignableFrom(typeof(TSource)) || source is UnityEngine.Object)
            {
                if (source == null) return true;
                if (typeof(TDestination) == typeof(UnityEngine.Object))
                {
                    destination = (TDestination)(object)source;
                    return true;
                }
            }
            if (s_Converters.TryGetConverter(typeof(TSource), typeof(UnityEngine.Object), out var converter))
            {
                var obj = ((TypeConverter<TSource, UnityEngine.Object>)converter)(ref source);
                destination = (TDestination)(object)obj;
                return obj;
            }
            return false;
        }

        static bool IsNumeric(Type t)
        {
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.Byte: case TypeCode.SByte: case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64:
                case TypeCode.Int16: case TypeCode.Int32: case TypeCode.Int64: case TypeCode.Decimal: case TypeCode.Double: case TypeCode.Single:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The conversions Unity registers for primitives, strings and object.</summary>
        static class BuiltIn
        {
            static readonly HashSet<Type> s_Primitive = new HashSet<Type>
            {
                typeof(sbyte), typeof(short), typeof(int), typeof(long), typeof(byte), typeof(ushort), typeof(uint), typeof(ulong),
                typeof(float), typeof(double), typeof(bool), typeof(char),
            };

            internal static bool TryConvert<TSource, TDestination>(TSource source, out TDestination destination)
            {
                destination = default;
                var from = typeof(TSource);
                var to = typeof(TDestination);
                if (from == to) return false;
                object result;
                if (s_Primitive.Contains(from) && s_Primitive.Contains(to)) result = Cast(source, to);
                else if (s_Primitive.Contains(from) && to == typeof(object)) result = source;
                else if (s_Primitive.Contains(from) && to == typeof(string)) result = ToText(source);
                else if (from == typeof(string) && s_Primitive.Contains(to)) result = Parse((string)(object)source, to);
                else if (from == typeof(object) && s_Primitive.Contains(to)) result = source != null && source.GetType() == to ? source : Activator.CreateInstance(to);
                else return false;
                destination = (TDestination)result;
                return true;
            }

            static string ToText(object value) => value switch
            {
                float f => f.ToString(CultureInfo.InvariantCulture),
                double d => d.ToString(CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };

            static object Parse(string text, Type to)
            {
                if (to == typeof(char)) return !string.IsNullOrEmpty(text) ? text[0] : '\0';
                if (to == typeof(bool))
                {
                    if (bool.TryParse(text, out var b)) return b;
                    return double.TryParse(text, out var number) && Math.Abs(number) > double.Epsilon;
                }
                if (to == typeof(double)) return double.TryParse(text, out var d) ? d : 0.0;
                object parsed = Type.GetTypeCode(to) switch
                {
                    TypeCode.SByte => sbyte.TryParse(text, out var v) ? v : null,
                    TypeCode.Int16 => short.TryParse(text, out var v) ? v : null,
                    TypeCode.Int32 => int.TryParse(text, out var v) ? v : null,
                    TypeCode.Int64 => long.TryParse(text, out var v) ? v : null,
                    TypeCode.Byte => byte.TryParse(text, out var v) ? v : null,
                    TypeCode.UInt16 => ushort.TryParse(text, out var v) ? v : null,
                    TypeCode.UInt32 => uint.TryParse(text, out var v) ? v : null,
                    TypeCode.UInt64 => ulong.TryParse(text, out var v) ? v : null,
                    TypeCode.Single => float.TryParse(text, out var v) ? v : null,
                    _ => null,
                };
                if (parsed != null) return parsed;
                return double.TryParse(text, out var fallback) ? Cast(fallback, to) : Activator.CreateInstance(to);
            }

            /// <summary>Between numbers, booleans and characters, as C# casts (unchecked).</summary>
            static object Cast(object value, Type to)
            {
                if (to == typeof(bool))
                {
                    return value switch
                    {
                        float f => Math.Abs(f) > float.Epsilon,
                        double d => Math.Abs(d) > double.Epsilon,
                        char c => c != '\0',
                        _ => System.Convert.ToDecimal(value is bool bb ? (bb ? 1 : 0) : value, CultureInfo.InvariantCulture) != 0m,
                    };
                }
                if (value is bool b) return CastInteger(b ? 1L : 0L, to);
                if (value is char ch) return to == typeof(float) ? (float)(int)ch : to == typeof(double) ? (double)(int)ch : CastInteger(ch, to);
                switch (value)
                {
                    case float f: return CastFloating(f, to);
                    case double d: return CastFloating(d, to);
                    case ulong u: return CastUnsigned(u, to);
                    default: return CastInteger(System.Convert.ToInt64(value, CultureInfo.InvariantCulture), to);
                }
            }

            static object CastInteger(long v, Type to)
            {
                unchecked
                {
                    switch (Type.GetTypeCode(to))
                    {
                        case TypeCode.SByte: return (sbyte)v;
                        case TypeCode.Int16: return (short)v;
                        case TypeCode.Int32: return (int)v;
                        case TypeCode.Int64: return v;
                        case TypeCode.Byte: return (byte)v;
                        case TypeCode.UInt16: return (ushort)v;
                        case TypeCode.UInt32: return (uint)v;
                        case TypeCode.UInt64: return (ulong)v;
                        case TypeCode.Single: return (float)v;
                        case TypeCode.Double: return (double)v;
                        case TypeCode.Char: return (char)v;
                        default: throw new InvalidCastException();
                    }
                }
            }

            static object CastUnsigned(ulong v, Type to)
            {
                unchecked
                {
                    switch (Type.GetTypeCode(to))
                    {
                        case TypeCode.Single: return (float)v;
                        case TypeCode.Double: return (double)v;
                        default: return CastInteger((long)v, to);
                    }
                }
            }

            /// <summary>Floating point to integers as Mono's casts do: truncated to a 64-bit
            /// integer, then wrapped (.NET saturates instead: (uint)-1.5f is 0 there, 4294967295 in
            /// Unity).</summary>
            static object CastFloating(double v, Type to)
            {
                unchecked
                {
                    switch (Type.GetTypeCode(to))
                    {
                        case TypeCode.Single: return (float)v;
                        case TypeCode.Double: return v;
                        case TypeCode.UInt64 when v >= 9223372036854775808.0: return (ulong)v;
                        case TypeCode.Int32: return (int)v;
                        default: return CastInteger((long)v, to);
                    }
                }
            }
        }
    }

    /// <summary>Making instances of types: an explicit method set for the type, else its
    /// property bag's own, else its default constructor (ScriptableObject.CreateInstance for
    /// ScriptableObjects; structs need none).</summary>
    public static partial class TypeUtility
    {
        interface IConstructorOf
        {
            bool CanBeInstantiated { get; }
            object InstantiateObject();
        }

        sealed class ConstructorOf<T> : IConstructorOf
        {
            Func<T> m_Explicit;
            readonly Func<T> m_Implicit;
            readonly IConstructor<T> m_Bag;

            internal ConstructorOf()
            {
                m_Bag = PropertyBagStore.GetPropertyBag<T>() as IConstructor<T>;
                var type = typeof(T);
                if (type.IsValueType) m_Implicit = () => default;
                else if (!type.IsAbstract)
                {
                    if (typeof(ScriptableObject).IsAssignableFrom(type)) m_Implicit = () => (T)(object)ScriptableObject.CreateInstance(type);
                    else if (type.GetConstructor(Type.EmptyTypes) != null) m_Implicit = Activator.CreateInstance<T>;
                }
            }

            internal void SetExplicit(Func<T> constructor) => m_Explicit = constructor;

            public bool CanBeInstantiated
            {
                get
                {
                    if (m_Explicit != null) return true;
                    if (m_Bag != null)
                    {
                        if (m_Bag.InstantiationKind == InstantiationKind.NotInstantiatable) return false;
                        if (m_Bag.InstantiationKind == InstantiationKind.PropertyBagOverride) return true;
                    }
                    return m_Implicit != null;
                }
            }

            internal T Instantiate()
            {
                if (m_Explicit != null) return m_Explicit();
                if (m_Bag != null)
                {
                    if (m_Bag.InstantiationKind == InstantiationKind.NotInstantiatable)
                        throw new InvalidOperationException("The type '" + typeof(T).Name + "' is not constructable.");
                    if (m_Bag.InstantiationKind == InstantiationKind.PropertyBagOverride) return m_Bag.Instantiate();
                }
                if (m_Implicit != null) return m_Implicit();
                throw new InvalidOperationException("The type '" + typeof(T).Name + "' is not constructable.");
            }

            public object InstantiateObject() => Instantiate();
        }

        sealed class NotConstructable : IConstructorOf
        {
            public bool CanBeInstantiated => false;
            public object InstantiateObject() => throw new InvalidOperationException("The type is not instantiatable.");
        }

        static class Cache<T>
        {
            internal static ConstructorOf<T> Constructor;
        }

        static readonly ConcurrentDictionary<Type, IConstructorOf> s_Constructors = new ConcurrentDictionary<Type, IConstructorOf>();
        static readonly ConcurrentDictionary<Type, string> s_DisplayNames = new ConcurrentDictionary<Type, string>();
        static readonly MethodInfo s_ConstructorOfGeneric = typeof(TypeUtility).GetMethod(nameof(For), BindingFlags.Static | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

        static TypeUtility() => SetExplicitInstantiationMethod(() => string.Empty);

        static ConstructorOf<T> For<T>()
        {
            if (Cache<T>.Constructor != null) return Cache<T>.Constructor;
            var constructor = new ConstructorOf<T>();
            Cache<T>.Constructor = constructor;
            s_Constructors[typeof(T)] = constructor;
            return constructor;
        }

        static IConstructorOf For(Type type)
        {
            if (s_Constructors.TryGetValue(type, out var constructor)) return constructor;
            if (type.ContainsGenericParameters)
            {
                constructor = new NotConstructable();
                s_Constructors[type] = constructor;
                return constructor;
            }
            return (IConstructorOf)s_ConstructorOfGeneric.MakeGenericMethod(type).Invoke(null, null);
        }

        public static string GetTypeDisplayName(Type type)
        {
            if (s_DisplayNames.TryGetValue(type, out var name)) return name;
            int argument = 0;
            name = DisplayName(type, type.GetGenericArguments(), ref argument);
            s_DisplayNames[type] = name;
            return name;
        }

        static string DisplayName(Type type, IReadOnlyList<Type> arguments, ref int argument)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Int32 when type == typeof(int): return "int";
                case TypeCode.UInt32 when type == typeof(uint): return "uint";
                case TypeCode.Int16 when type == typeof(short): return "short";
                case TypeCode.UInt16 when type == typeof(ushort): return "ushort";
                case TypeCode.Byte when type == typeof(byte): return "byte";
                case TypeCode.Char when type == typeof(char): return "char";
                case TypeCode.Boolean when type == typeof(bool): return "bool";
                case TypeCode.Int64 when type == typeof(long): return "long";
                case TypeCode.UInt64 when type == typeof(ulong): return "ulong";
                case TypeCode.Single when type == typeof(float): return "float";
                case TypeCode.Double when type == typeof(double): return "double";
                case TypeCode.String when type == typeof(string): return "string";
            }
            var name = type.Name;
            if (type.IsGenericParameter) return name;
            if (type.IsNested) name = DisplayName(type.DeclaringType, arguments, ref argument) + "." + name;
            if (!type.IsGenericType) return name;
            int tick = name.IndexOf('`');
            int count = type.GetGenericArguments().Length;
            if (tick > -1)
            {
                count = int.Parse(name.Substring(tick + 1), CultureInfo.InvariantCulture);
                name = name.Remove(tick);
            }
            var builder = new StringBuilder();
            for (int i = 0; i < count && argument < arguments.Count; i++, argument++)
            {
                if (i != 0) builder.Append(", ");
                builder.Append(GetTypeDisplayName(arguments[argument]));
            }
            return builder.Length > 0 ? $"{name}<{builder}>" : name;
        }

        public static Type GetRootType(this Type type)
        {
            if (type.IsInterface) return null;
            var root = type.IsValueType ? typeof(ValueType) : typeof(object);
            while (root != type.BaseType) type = type.BaseType;
            return type;
        }

        public static bool CanBeInstantiated(Type type) => For(type).CanBeInstantiated;

        public static bool CanBeInstantiated<T>() => For<T>().CanBeInstantiated;

        public static void SetExplicitInstantiationMethod<T>(Func<T> constructor) => For<T>().SetExplicit(constructor);

        public static T Instantiate<T>()
        {
            var constructor = For<T>();
            if (!constructor.CanBeInstantiated)
                throw new InvalidOperationException($"Type `{typeof(T).Name}` could not be instantiated. A parameter-less constructor or an explicit construction method is required.");
            return constructor.Instantiate();
        }

        public static bool TryInstantiate<T>(out T instance)
        {
            var constructor = For<T>();
            if (constructor.CanBeInstantiated)
            {
                instance = constructor.Instantiate();
                return true;
            }
            instance = default;
            return false;
        }

        public static T Instantiate<T>(Type derivedType)
        {
            var constructor = For(derivedType);
            if (!typeof(T).IsAssignableFrom(derivedType))
                throw new ArgumentException($"Could not create instance of type `{derivedType.Name}` and convert to `{typeof(T).Name}`: The given type is not assignable to target type.");
            if (!constructor.CanBeInstantiated)
                throw new InvalidOperationException($"Type `{derivedType.Name}` could not be instantiated. A parameter-less constructor or an explicit construction method is required.");
            return (T)constructor.InstantiateObject();
        }

        public static bool TryInstantiate<T>(Type derivedType, out T value)
        {
            if (!typeof(T).IsAssignableFrom(derivedType))
            {
                value = default;
                return false;
            }
            var constructor = For(derivedType);
            if (!constructor.CanBeInstantiated)
            {
                value = default;
                return false;
            }
            value = (T)constructor.InstantiateObject();
            return true;
        }

        public static TArray InstantiateArray<TArray>(int count = 0)
        {
            if (count < 0) throw new ArgumentException($"{nameof(TypeUtility)}: Cannot construct an array with {nameof(count)}={count}");
            if (PropertyBagStore.GetPropertyBag<TArray>() is IConstructorWithCount<TArray> withCount) return withCount.InstantiateWithCount(count);
            return NewArray<TArray>(count, "construct");
        }

        public static bool TryInstantiateArray<TArray>(int count, out TArray instance)
        {
            instance = default;
            if (count < 0) return false;
            if (PropertyBagStore.GetPropertyBag<TArray>() is IConstructorWithCount<TArray> withCount)
            {
                try
                {
                    instance = withCount.InstantiateWithCount(count);
                    return true;
                }
                catch (Exception) { }
            }
            var type = typeof(TArray);
            if (!type.IsArray || type.GetElementType() == null) return false;
            instance = (TArray)(object)Array.CreateInstance(type.GetElementType(), count);
            return true;
        }

        public static TArray InstantiateArray<TArray>(Type derivedType, int count = 0)
        {
            if (count < 0) throw new ArgumentException($"{nameof(TypeUtility)}: Cannot instantiate an array with {nameof(count)}={count}");
            if (PropertyBagStore.GetPropertyBag(derivedType) is IConstructorWithCount<TArray> withCount) return withCount.InstantiateWithCount(count);
            return NewArray<TArray>(count, "instantiate");
        }

        static TArray NewArray<TArray>(int count, string verb)
        {
            var type = typeof(TArray);
            if (!type.IsArray) throw new ArgumentException($"TypeUtility: Cannot {verb} an array, since {type.Name} is not an array type.");
            var element = type.GetElementType();
            if (element == null) throw new ArgumentException($"TypeUtility: Cannot {verb} an array, since {type.Name}.GetElementType() returned null.");
            return (TArray)(object)Array.CreateInstance(element, count);
        }
    }
}
