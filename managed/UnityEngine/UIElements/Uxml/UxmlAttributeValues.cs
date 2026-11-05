using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

// How UXML attribute text becomes the values of custom controls, and how the builder finds those
// controls: [UxmlElement] types by their class name or custom name, UxmlFactory types by the name
// their factory reports, and UxmlAttributeConverter types for values of the game's own types.
//
// Documented (Unity 6000.0 Manual "Create a custom control", "Define UXML attributes for built-in
// types", "Define custom control attributes for complex data types", "Customize UXML attributes";
// Scripting API UxmlElementAttribute, UxmlAttributeAttribute, UxmlAttributeConverter<T>):
//   - an attribute's default name splits the member name at its uppercase characters into
//     lowercase words joined by hyphens (myIntValue -> my-int-value);
//   - [UxmlAttribute("name", "old-name")] gives a custom name and obsolete names still read;
//   - an attribute declared again under the same UXML name in a derived class takes precedence;
//   - [UxmlElement("Name")] elements are found under the custom name and under the class name, in
//     the class's namespace;
//   - built-in value types (bool, byte, sbyte, char, string, short, ushort, int, uint, long, ulong,
//     enums, float, double, Type with UxmlTypeReference, Color, Hash128, Length, Bounds, BoundsInt,
//     Rect, RectInt, Vector2/2Int/3/3Int/4, Gradient, AnimationCurve, ToggleButtonGroupState),
//     assets by url, List<T> with comma-separated items, and other types through a
//     UxmlAttributeConverter<T>; without one the documented error is reported.
// Unbound's choices (not Unity guarantees): the text forms of vectors, rects and bounds
// (comma-separated components), the accepted colour forms (#RRGGBB, #RRGGBBAA), arrays read like
// lists, items not trimmed, the current name winning over an obsolete one, and errors logged when a
// value cannot be read (the member keeps its value). Length, Gradient, AnimationCurve and
// ToggleButtonGroupState values are not read yet and fail loudly. Background values (without a game
// converter) and Texture values are asset urls read as UxmlImageReferences reads them.

namespace Unbound.UIElements
{
    /// <summary>The attribute values of one UXML element, as a UxmlTraits class reads them.</summary>
    internal sealed class UxmlNodeAttributes : IUxmlAttributes
    {
        readonly UxmlNode m_Node;

        internal UxmlNodeAttributes(UxmlNode node) => m_Node = node;

        public bool TryGetAttributeValue(string attributeName, out string value)
        {
            value = attributeName == null ? null : m_Node.Attr(attributeName);
            return value != null;
        }
    }

    /// <summary>A [UxmlAttribute] field or property of a custom control.</summary>
    internal sealed class UxmlMember
    {
        internal MemberInfo Info;
        internal Type ValueType;
        internal string UxmlName;
        internal string[] ObsoleteNames;
        /// <summary>Why the member cannot be read from UXML, or null.</summary>
        internal string Problem;

        internal void Set(object target, object value)
        {
            if (Info is FieldInfo field) field.SetValue(target, value);
            else ((PropertyInfo)Info).SetValue(target, value);
        }
    }

    /// <summary>The [UxmlAttribute] members of a custom control type, by UXML name.</summary>
    internal sealed class UxmlMembers
    {
        internal readonly Dictionary<string, UxmlMember> ByName = new Dictionary<string, UxmlMember>(StringComparer.Ordinal);
        internal readonly Dictionary<string, UxmlMember> ByObsoleteName = new Dictionary<string, UxmlMember>(StringComparer.Ordinal);

        internal static UxmlMembers Build(Type type)
        {
            var members = new UxmlMembers();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            // Most derived first: a redeclared UXML name takes precedence over the base's.
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var member in t.GetMembers(flags))
                {
                    if (!(member is FieldInfo) && !(member is PropertyInfo)) continue;
                    var attribute = member.GetCustomAttribute<UxmlAttributeAttribute>(false);
                    if (attribute == null) continue;
                    var uxmlName = string.IsNullOrEmpty(attribute.name) ? UxmlAttributeValues.DefaultAttributeName(member.Name) : attribute.name;
                    if (members.ByName.ContainsKey(uxmlName)) continue;
                    var entry = new UxmlMember
                    {
                        Info = member,
                        ValueType = member is FieldInfo f ? f.FieldType : ((PropertyInfo)member).PropertyType,
                        UxmlName = uxmlName,
                        ObsoleteNames = attribute.obsoleteNames ?? Array.Empty<string>(),
                    };
                    if (member is PropertyInfo p && (p.GetSetMethod(true) == null || p.GetIndexParameters().Length > 0))
                        entry.Problem = $"[UxmlElement] '{type.Name}' has a [UxmlAttribute] '{member.Name}' without a set accessor; Unbound cannot apply it.";
                    else if (member is FieldInfo { IsInitOnly: true })
                        entry.Problem = $"[UxmlElement] '{type.Name}' has a [UxmlAttribute] '{member.Name}' that is readonly; Unbound cannot apply it.";
                    else
                        entry.Problem = UxmlAttributeValues.Problem(entry.ValueType, type.Name, member.Name);
                    if (entry.Problem != null) Debug.LogError(entry.Problem);
                    members.ByName[uxmlName] = entry;
                    members.ByObsoleteName.Remove(uxmlName);
                    foreach (var obsolete in entry.ObsoleteNames)
                        if (!string.IsNullOrEmpty(obsolete) && !members.ByName.ContainsKey(obsolete) && !members.ByObsoleteName.ContainsKey(obsolete))
                            members.ByObsoleteName[obsolete] = entry;
                }
            }
            return members;
        }
    }

    /// <summary>The custom controls, factories and converters of the loaded game assemblies (those
    /// other than Unbound's own), indexed on first use and again after another assembly loads.</summary>
    internal static class UxmlTypeIndex
    {
        static int s_Loads;
        static int s_IndexedLoads = -1;
        static Dictionary<string, List<Type>> s_ElementNames;
        static Dictionary<string, List<IUxmlFactory>> s_Factories;
        static Dictionary<Type, Type> s_Converters;
        static Dictionary<Type, Type> s_GenericConverters;
        static readonly Dictionary<Type, UxmlMembers> s_Members = new Dictionary<Type, UxmlMembers>();
        static readonly Dictionary<Type, object> s_ConverterInstances = new Dictionary<Type, object>();

        static UxmlTypeIndex()
        {
            // Only assemblies the index scans make it stale.
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
            {
                if (IsGameAssembly(e.LoadedAssembly)) Interlocked.Increment(ref s_Loads);
            };
        }

        internal static bool IsEngineType(Type type) => type.Assembly == typeof(VisualElement).Assembly;

        static void Refresh()
        {
            int loads = Volatile.Read(ref s_Loads);
            if (loads == s_IndexedLoads) return;
            s_IndexedLoads = loads;
            s_ElementNames = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
            s_Factories = new Dictionary<string, List<IUxmlFactory>>(StringComparer.Ordinal);
            s_Converters = new Dictionary<Type, Type>();
            s_GenericConverters = new Dictionary<Type, Type>();
            s_Members.Clear();
            s_ConverterInstances.Clear();
            var factoryTypes = new List<Type>();
            foreach (var type in GameTypes())
            {
                // A type that cannot be inspected (an attribute or base type that does not load) is
                // reported and skipped; the rest of the index is still built.
                try
                {
                    IndexType(type, factoryTypes);
                }
                catch (Exception e)
                {
                    Debug.LogError($"UXML: Unbound skips the game type '{type.FullName}' when indexing custom controls: {e.Message}");
                }
            }
            // Factories are constructed only after the scan: their constructors run game code.
            foreach (var type in factoryTypes)
            {
                if (type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) == null) continue;
                try
                {
                    var factory = (IUxmlFactory)Activator.CreateInstance(type, true);
                    var name = factory.uxmlQualifiedName;
                    if (!string.IsNullOrEmpty(name)) Add(s_Factories, name, factory);
                }
                catch (Exception e)
                {
                    Debug.LogException(e is TargetInvocationException { InnerException: { } inner } ? inner : e);
                }
            }
        }

        static void IndexType(Type type, List<Type> factoryTypes)
        {
            if (!type.IsClass) return;
            if (typeof(VisualElement).IsAssignableFrom(type))
            {
                var element = type.GetCustomAttribute<UxmlElementAttribute>(false);
                if (element != null && !string.IsNullOrEmpty(element.name))
                    Add(s_ElementNames, Qualified(type.Namespace, element.name), type);
            }
            if (type.IsAbstract || type.ContainsGenericParameters && !type.IsGenericTypeDefinition) return;
            if (!type.IsGenericTypeDefinition && typeof(IUxmlFactory).IsAssignableFrom(type)) factoryTypes.Add(type);
            var converterBase = ConverterBase(type);
            if (converterBase != null) AddConverter(type, converterBase.GetGenericArguments()[0]);
        }

        static void Add<T>(Dictionary<string, List<T>> map, string key, T value)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<T>();
            list.Add(value);
        }

        internal static string Qualified(string ns, string name) => string.IsNullOrEmpty(ns) ? name : ns + "." + name;

        /// <summary>The assemblies the index scans: those other than Unbound's own, the framework's and
        /// dynamic ones.</summary>
        static bool IsGameAssembly(Assembly assembly)
        {
            if (assembly == null || assembly == typeof(VisualElement).Assembly || assembly.IsDynamic) return false;
            var name = assembly.GetName().Name ?? "";
            return !(name.StartsWith("System.", StringComparison.Ordinal) || name.StartsWith("Microsoft.", StringComparison.Ordinal)
                || name == "System" || name == "mscorlib" || name == "netstandard");
        }

        static IEnumerable<Type> GameTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!IsGameAssembly(assembly)) continue;
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    Debug.LogWarning($"UXML: {e.LoaderExceptions?.Length ?? 0} type(s) of '{assembly.GetName().Name}' do not load; Unbound indexes its other custom controls only.");
                    types = e.Types;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"UXML: Unbound cannot read the types of '{assembly.GetName().Name}' and indexes none of its custom controls: {e.Message}");
                    continue;
                }
                foreach (var type in types)
                    if (type != null) yield return type;
            }
        }

        static Type ConverterBase(Type type)
        {
            for (var t = type.BaseType; t != null; t = t.BaseType)
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(UxmlAttributeConverter<>)) return t;
            return null;
        }

        static void AddConverter(Type converter, Type target)
        {
            if (converter.IsGenericTypeDefinition)
            {
                // A generic converter serves the constructed types of its generic target when its
                // type parameters are the target's type arguments, in order.
                if (!target.IsGenericType) return;
                var parameters = converter.GetGenericArguments();
                var arguments = target.GetGenericArguments();
                if (parameters.Length != arguments.Length) return;
                for (int i = 0; i < parameters.Length; i++)
                    if (arguments[i] != parameters[i]) return;
                var definition = target.GetGenericTypeDefinition();
                if (!s_GenericConverters.TryAdd(definition, converter))
                    Debug.LogWarning($"UXML: both {s_GenericConverters[definition].FullName} and {converter.FullName} convert {definition.Name}; Unbound uses the first.");
                return;
            }
            if (target.ContainsGenericParameters) return;
            if (!s_Converters.TryAdd(target, converter))
                Debug.LogWarning($"UXML: both {s_Converters[target].FullName} and {converter.FullName} convert {target.Name}; Unbound uses the first.");
        }

        /// <summary>The game type whose [UxmlElement] custom name is <paramref name="qualifiedName"/>.</summary>
        internal static Type FindCustomName(string qualifiedName, out string error)
        {
            error = null;
            Refresh();
            if (!s_ElementNames.TryGetValue(qualifiedName, out var types)) return null;
            if (types.Count == 1) return types[0];
            var names = new List<string>();
            foreach (var t in types) names.Add(t.AssemblyQualifiedName);
            error = $"UXML element name '{qualifiedName}' is declared by more than one [UxmlElement] ({string.Join("; ", names)}); element names must be unique.";
            return null;
        }

        internal static List<IUxmlFactory> FindFactories(string qualifiedName)
        {
            Refresh();
            return s_Factories.TryGetValue(qualifiedName, out var list) ? list : null;
        }

        internal static UxmlMembers MembersOf(Type type)
        {
            Refresh();
            if (!s_Members.TryGetValue(type, out var members)) s_Members[type] = members = UxmlMembers.Build(type);
            return members;
        }

        /// <summary>The game's converter for values of <paramref name="target"/>, or null.</summary>
        internal static Type ConverterFor(Type target)
        {
            Refresh();
            if (s_Converters.TryGetValue(target, out var converter)) return converter;
            if (target.IsGenericType && !target.ContainsGenericParameters
                && s_GenericConverters.TryGetValue(target.GetGenericTypeDefinition(), out var generic))
            {
                try
                {
                    return generic.MakeGenericType(target.GetGenericArguments());
                }
                catch (ArgumentException)
                {
                    // The target's arguments break the converter's constraints.
                }
            }
            return null;
        }

        internal static bool TryConvert(Type converterType, string text, out object result, out string error)
        {
            result = null;
            error = null;
            try
            {
                if (!s_ConverterInstances.TryGetValue(converterType, out var instance))
                    s_ConverterInstances[converterType] = instance = Activator.CreateInstance(converterType, true);
                var method = converterType.GetMethod("FromString", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
                result = method.Invoke(instance, new object[] { text });
                return true;
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException { InnerException: { } x } ? x : e;
                error = $"{converterType.Name}.FromString threw {inner.GetType().Name}: {inner.Message}";
                return false;
            }
        }
    }

    /// <summary>Reading UXML attribute text as typed values.</summary>
    internal static class UxmlAttributeValues
    {
        /// <summary>The documented default: the member name split at its uppercase characters into
        /// lowercase words joined by hyphens. Other characters are kept as they are (Unbound's
        /// reading of names with digits, underscores or runs of capitals).</summary>
        internal static string DefaultAttributeName(string memberName)
        {
            var b = new StringBuilder(memberName.Length + 4);
            for (int i = 0; i < memberName.Length; i++)
            {
                char c = memberName[i];
                if (char.IsUpper(c))
                {
                    if (i > 0) b.Append('-');
                    b.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    b.Append(c);
                }
            }
            return b.ToString();
        }

        static bool IsList(Type type, out Type element)
        {
            element = null;
            if (type.IsArray && type.GetArrayRank() == 1) element = type.GetElementType();
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) element = type.GetGenericArguments()[0];
            return element != null;
        }

        static readonly HashSet<Type> s_Scalars = new HashSet<Type>
        {
            typeof(string), typeof(bool), typeof(char), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(Type),
            typeof(Color), typeof(Hash128), typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Vector2Int),
            typeof(Vector3Int), typeof(Rect), typeof(RectInt), typeof(Bounds), typeof(BoundsInt),
        };

        /// <summary>Documented built-in types whose UXML text Unbound does not read yet.</summary>
        static bool NotReadYet(Type type) =>
            type == typeof(Length) || type == typeof(Gradient) || type == typeof(AnimationCurve)
            || type.FullName == "UnityEngine.UIElements.ToggleButtonGroupState";

        /// <summary>Why values of <paramref name="type"/> cannot be read from UXML, or null.</summary>
        internal static string Problem(Type type, string elementName, string memberName)
        {
            if (UxmlTypeIndex.ConverterFor(type) != null) return null;
            if (s_Scalars.Contains(type) || type.IsEnum || typeof(Object).IsAssignableFrom(type) || UxmlImageReferences.Reads(type)) return null;
            if (NotReadYet(type))
                return $"[UxmlElement] '{elementName}' has a [UxmlAttribute] '{memberName}' of type '{type.Name}', which Unbound does not read from UXML yet.";
            if (IsList(type, out var element))
            {
                if (IsList(element, out _))
                    return $"[UxmlElement] '{elementName}' has a [UxmlAttribute] '{memberName}' of type '{type.Name}': Unbound does not read lists of lists from UXML.";
                return Problem(element, elementName, memberName);
            }
            return $"[UxmlElement] '{elementName}' has a [UxmlAttribute] '{memberName}' of an unknown type '{type.Name}'. To fix this error, define a custom UxmlAttributeConverter<{type.Name}>.";
        }

        /// <summary>Reads <paramref name="text"/> as a value of <paramref name="type"/>: through the
        /// game's converter if it has one, else as a built-in type, an asset url or a list.</summary>
        internal static bool TryRead(string text, Type type, MemberInfo member, VisualTreeAsset document, out object result, out string error)
        {
            result = null;
            error = null;
            text ??= "";
            var converter = UxmlTypeIndex.ConverterFor(type);
            if (converter != null) return UxmlTypeIndex.TryConvert(converter, text, out result, out error);
            if (type == typeof(string))
            {
                result = text;
                return true;
            }
            if (type.IsEnum)
            {
                if (TryEnum(type, text, out result)) return true;
                return Fail(out error, text, $"a {type.Name} value");
            }
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                    if (bool.TryParse(text, out var b)) { result = b; return true; }
                    return Fail(out error, text, "true or false");
                case TypeCode.Char:
                    if (text.Length == 1) { result = text[0]; return true; }
                    return Fail(out error, text, "a single character");
                case TypeCode.Byte:
                    if (byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u8)) { result = u8; return true; }
                    return Fail(out error, text, "a byte");
                case TypeCode.SByte:
                    if (sbyte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i8)) { result = i8; return true; }
                    return Fail(out error, text, "an sbyte");
                case TypeCode.Int16:
                    if (short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i16)) { result = i16; return true; }
                    return Fail(out error, text, "a short");
                case TypeCode.UInt16:
                    if (ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u16)) { result = u16; return true; }
                    return Fail(out error, text, "a ushort");
                case TypeCode.Int32:
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i32)) { result = i32; return true; }
                    return Fail(out error, text, "an int");
                case TypeCode.UInt32:
                    if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u32)) { result = u32; return true; }
                    return Fail(out error, text, "a uint");
                case TypeCode.Int64:
                    if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i64)) { result = i64; return true; }
                    return Fail(out error, text, "a long");
                case TypeCode.UInt64:
                    if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u64)) { result = u64; return true; }
                    return Fail(out error, text, "a ulong");
                case TypeCode.Single:
                    if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f32)) { result = f32; return true; }
                    return Fail(out error, text, "a float");
                case TypeCode.Double:
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f64)) { result = f64; return true; }
                    return Fail(out error, text, "a double");
            }
            if (type == typeof(Type))
            {
                var baseType = member?.GetCustomAttribute<UxmlTypeReferenceAttribute>(false)?.baseType;
                return TryReadTypeName(text, baseType, out result, out error);
            }
            if (type == typeof(Color))
            {
                if (UxmlBuilder.TryConvert(text, typeof(Color), out result)) return true;
                return Fail(out error, text, "a #RRGGBB or #RRGGBBAA colour");
            }
            if (type == typeof(Hash128))
            {
                var hex = text.Trim();
                if (hex.Length == 32 && IsHex(hex)) { result = Hash128.Parse(hex); return true; }
                return Fail(out error, text, "a Hash128 of 32 hexadecimal digits");
            }
            if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Rect) || type == typeof(Bounds))
            {
                int n = type == typeof(Vector2) ? 2 : type == typeof(Vector3) ? 3 : type == typeof(Bounds) ? 6 : 4;
                if (!TryFloats(text, n, out var v)) return Fail(out error, text, $"{n} comma-separated numbers");
                if (type == typeof(Vector2)) result = new Vector2(v[0], v[1]);
                else if (type == typeof(Vector3)) result = new Vector3(v[0], v[1], v[2]);
                else if (type == typeof(Vector4)) result = new Vector4(v[0], v[1], v[2], v[3]);
                else if (type == typeof(Rect)) result = new Rect(v[0], v[1], v[2], v[3]);
                else result = new Bounds(new Vector3(v[0], v[1], v[2]), new Vector3(v[3], v[4], v[5]));
                return true;
            }
            if (type == typeof(Vector2Int) || type == typeof(Vector3Int) || type == typeof(RectInt) || type == typeof(BoundsInt))
            {
                int n = type == typeof(Vector2Int) ? 2 : type == typeof(Vector3Int) ? 3 : type == typeof(RectInt) ? 4 : 6;
                if (!TryInts(text, n, out var v)) return Fail(out error, text, $"{n} comma-separated integers");
                if (type == typeof(Vector2Int)) result = new Vector2Int(v[0], v[1]);
                else if (type == typeof(Vector3Int)) result = new Vector3Int(v[0], v[1], v[2]);
                else if (type == typeof(RectInt)) result = new RectInt(v[0], v[1], v[2], v[3]);
                else result = new BoundsInt(v[0], v[1], v[2], v[3], v[4], v[5]);
                return true;
            }
            // Background and Texture values: asset references read as images (VisualTreeAsset.cs).
            if (UxmlImageReferences.Reads(type))
                return UxmlImageReferences.TryRead(text, type, document, out result, out error);
            if (typeof(Object).IsAssignableFrom(type))
                return TryReadAsset(text, type, document, out result, out error);
            if (IsList(type, out var element) && !IsList(element, out _))
            {
                var items = new List<object>();
                if (text.Length > 0)
                {
                    var parts = text.Split(',');
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (!TryRead(parts[i], element, member, document, out var item, out var itemError))
                        {
                            error = $"item {i}: {itemError}";
                            return false;
                        }
                        items.Add(item);
                    }
                }
                if (type.IsArray)
                {
                    var array = Array.CreateInstance(element, items.Count);
                    for (int i = 0; i < items.Count; i++) array.SetValue(items[i], i);
                    result = array;
                }
                else
                {
                    var list = (IList)Activator.CreateInstance(type);
                    foreach (var item in items) list.Add(item);
                    result = list;
                }
                return true;
            }
            error = Problem(type, member?.DeclaringType?.Name ?? "?", member?.Name ?? "?") ?? $"Unbound cannot read values of type '{type.Name}' from UXML.";
            return false;
        }

        static bool Fail(out string error, string text, string expected)
        {
            error = $"'{text}' is not {expected}";
            return false;
        }

        static bool IsHex(string s)
        {
            foreach (var c in s)
                if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        static bool TryFloats(string text, int count, out float[] values)
        {
            values = new float[count];
            var parts = text.Split(',');
            if (parts.Length != count) return false;
            for (int i = 0; i < count; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return false;
            return true;
        }

        static bool TryInts(string text, int count, out int[] values)
        {
            values = new int[count];
            var parts = text.Split(',');
            if (parts.Length != count) return false;
            for (int i = 0; i < count; i++)
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i])) return false;
            return true;
        }

        /// <summary>An enum value by name (case and hyphens ignored, as the builder reads built-in
        /// enum attributes), a combination of names, or a number.</summary>
        static bool TryEnum(Type type, string text, out object result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var compact = text.Trim().Replace("-", "");
            foreach (var name in Enum.GetNames(type))
            {
                if (string.Equals(name, compact, StringComparison.OrdinalIgnoreCase))
                {
                    result = Enum.Parse(type, name);
                    return true;
                }
            }
            if (Enum.TryParse(type, text, true, out var parsed))
            {
                result = parsed;
                return true;
            }
            return false;
        }

        /// <summary>A type by its assembly-qualified name ("Namespace.Type, Assembly"). Unbound's
        /// implementation lives in fewer assemblies than Unity's modules, so a type that is not in the
        /// named assembly is also looked for in the loaded ones.</summary>
        internal static bool TryReadTypeName(string text, Type baseType, out object result, out string error)
        {
            result = null;
            error = null;
            var name = text.Trim();
            if (name.Length == 0) return true;
            Type found = null;
            try
            {
                found = Type.GetType(name, false);
            }
            catch (Exception)
            {
                found = null;
            }
            if (found == null)
            {
                int comma = name.IndexOf(',');
                var typeName = (comma >= 0 ? name.Substring(0, comma) : name).Trim();
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        found = assembly.GetType(typeName, false);
                    }
                    catch (Exception)
                    {
                        found = null;
                    }
                    if (found != null) break;
                }
            }
            if (found == null)
            {
                error = $"no type '{name}' is loaded";
                return false;
            }
            if (baseType != null && !baseType.IsAssignableFrom(found))
            {
                error = $"type '{found.FullName}' does not derive from '{baseType.FullName}'";
                return false;
            }
            result = found;
            return true;
        }

        /// <summary>A UXML template (VisualTreeAsset) reference of <paramref name="document"/>, such as
        /// ListView or TreeView `item-template`: resolved, checked and reported exactly as UxmlImageReferences reads
        /// the asset references of `icon-image` (the same url forms, the same handling of a #name naming
        /// several candidates, an error text for a missing asset or one of another class). An empty
        /// value is no template (null).</summary>
        internal static bool TryReadTemplate(string text, VisualTreeAsset document, out VisualTreeAsset template, out string error)
        {
            template = null;
            if (!UxmlImageReferences.TryRead(text, typeof(VisualTreeAsset), document, out var value, out error)) return false;
            template = value as VisualTreeAsset;
            return true;
        }

        /// <summary>An asset by its UXML url, relative urls resolved against the document.</summary>
        static bool TryReadAsset(string text, Type type, VisualTreeAsset document, out object result, out string error)
        {
            result = null;
            error = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            Object asset;
            try
            {
                asset = AssetPaths.LoadAsset(document?.m_UaAssetPath, text.Trim(), type);
            }
            catch (Exception e)
            {
                error = $"the asset at '{text}' could not be loaded ({e.GetType().Name}: {e.Message})";
                return false;
            }
            if (asset == null || !type.IsInstanceOfType(asset))
            {
                error = $"no {type.Name} asset at '{text}'";
                return false;
            }
            result = asset;
            return true;
        }

        // ------------------------------------------------------------------ typed descriptions

        static string Where(UxmlAttributeDescription description) => $"UXML attribute '{description?.name}'";

        internal static T ReadTyped<T>(UxmlAttributeDescription description, string text, T fallback)
        {
            if (TryRead(text, typeof(T), null, null, out var value, out var error)) return (T)value;
            Debug.LogWarning($"{Where(description)}: {error}; the default value is used.");
            return fallback;
        }

        internal static Type ReadTypeName(UxmlAttributeDescription description, string text, Type baseType, Type fallback)
        {
            if (TryReadTypeName(text, baseType, out var value, out var error)) return (Type)value;
            Debug.LogWarning($"{Where(description)}: {error}; the default value is used.");
            return fallback;
        }

        internal static Object ReadAsset(UxmlAttributeDescription description, string text, VisualTreeAsset document, Type type)
        {
            if (TryReadAsset(text, type, document, out var value, out var error)) return (Object)value;
            Debug.LogError($"{Where(description)}: {error}.");
            return null;
        }

        /// <summary>The attribute descriptions held by a traits object's fields and properties,
        /// declared on its class and on base classes up to (excluding) <paramref name="stopAt"/>.</summary>
        internal static IEnumerable<UxmlAttributeDescription> DescriptionsOf(object traits, Type stopAt)
        {
            var seen = new HashSet<UxmlAttributeDescription>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var t = traits.GetType(); t != null && t != stopAt && t != typeof(object); t = t.BaseType)
            {
                foreach (var field in t.GetFields(flags))
                {
                    if (!typeof(UxmlAttributeDescription).IsAssignableFrom(field.FieldType)) continue;
                    if (field.GetValue(traits) is UxmlAttributeDescription d && seen.Add(d)) yield return d;
                }
                foreach (var property in t.GetProperties(flags))
                {
                    if (!typeof(UxmlAttributeDescription).IsAssignableFrom(property.PropertyType) || property.GetIndexParameters().Length > 0) continue;
                    var getter = property.GetGetMethod(true);
                    if (getter == null) continue;
                    if (getter.Invoke(traits, null) is UxmlAttributeDescription d && seen.Add(d)) yield return d;
                }
            }
        }
    }
}
