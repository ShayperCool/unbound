using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Properties;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

// Property bags of element styles, so bindings and property paths reach them: `style.<name>` (the
// inline style, read and written as Style* values) and `resolvedStyle.<name>` (read-only resolved
// values), each property found under its C# name and its USS name (`style.background-color`);
// `value` and `keyword` of every Style* value and `value` and `unit` of a Length. The global
// converters of Unity's style properties are registered with the bags: a style value from its plain
// value or a StyleKeyword and back, a StyleLength from a float or int, a StyleFloat from an int, a
// StyleColor from a Color32, a StyleBackground from a Texture2D, Sprite, RenderTexture or
// VectorImage, a StyleFontDefinition from a Font or FontAsset, and the reverse of each. Documented
// (Unity 6000.0 Manual "Runtime data binding": "Bind to style properties", data-source-path to
// `style.<property>`); matches the Unity 6000.0.71f1 Editor, tests/projects/uitkbindings.
namespace Unbound.UIElements
{
    internal static class StylePropertyBags
    {
        static readonly MethodInfo s_RegisterStyleType = typeof(StylePropertyBags).GetMethod(nameof(RegisterStyleType), BindingFlags.Static | BindingFlags.NonPublic);

        /// <summary>Registers the style bags and the style converters (once, with the built-in
        /// property bags).</summary>
        internal static void Register()
        {
            var styleTypes = new HashSet<Type>();
            PropertyBag.Register(new InlineStylePropertyBag(styleTypes));
            PropertyBag.Register(new ResolvedStylePropertyBag());
            foreach (var styleType in styleTypes)
                s_RegisterStyleType.MakeGenericMethod(styleType, StyleValueType(styleType)).Invoke(null, null);
            PropertyBag.Register(new LengthPropertyBag());
        }

        /// <summary>The T of a Style* type's IStyleValue&lt;T&gt;.</summary>
        internal static Type StyleValueType(Type styleType)
        {
            foreach (var i in styleType.GetInterfaces())
                if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStyleValue<>)) return i.GetGenericArguments()[0];
            throw new InvalidOperationException(styleType + " is not a style value");
        }

        static void RegisterStyleType<TStyle, TValue>() where TStyle : struct, IStyleValue<TValue>
        {
            PropertyBag.Register(new StyleValuePropertyBag<TStyle, TValue>());
            ConverterGroups.RegisterGlobal((ref TStyle sv) => sv.value);
            ConverterGroups.RegisterGlobal((ref TValue v) => new TStyle { value = v });
            ConverterGroups.RegisterGlobal((ref TStyle sv) => sv.keyword);
            ConverterGroups.RegisterGlobal((ref StyleKeyword kw) => new TStyle { keyword = kw });
            if (typeof(TStyle) == typeof(StyleColor))
            {
                ConverterGroups.RegisterGlobal((ref Color32 v) => new StyleColor(v));
                ConverterGroups.RegisterGlobal((ref StyleColor sv) => (Color32)sv.value);
            }
            else if (typeof(TStyle) == typeof(StyleBackground))
            {
                ConverterGroups.RegisterGlobal((ref Texture2D v) => new StyleBackground(v));
                ConverterGroups.RegisterGlobal((ref Sprite v) => new StyleBackground(v));
                ConverterGroups.RegisterGlobal((ref VectorImage v) => new StyleBackground(v));
                ConverterGroups.RegisterGlobal((ref RenderTexture v) => new StyleBackground(Background.FromRenderTexture(v)));
                ConverterGroups.RegisterGlobal((ref StyleBackground sv) => sv.value.texture);
                ConverterGroups.RegisterGlobal((ref StyleBackground sv) => sv.value.sprite);
                ConverterGroups.RegisterGlobal((ref StyleBackground sv) => sv.value.renderTexture);
                ConverterGroups.RegisterGlobal((ref StyleBackground sv) => sv.value.vectorImage);
            }
            else if (typeof(TStyle) == typeof(StyleLength))
            {
                ConverterGroups.RegisterGlobal((ref float v) => new StyleLength(v));
                ConverterGroups.RegisterGlobal((ref int v) => new StyleLength(v));
                ConverterGroups.RegisterGlobal((ref StyleLength sv) => sv.value.value);
                ConverterGroups.RegisterGlobal((ref StyleLength sv) => (int)sv.value.value);
            }
            else if (typeof(TStyle) == typeof(StyleFloat))
            {
                ConverterGroups.RegisterGlobal((ref int v) => new StyleFloat(v));
                ConverterGroups.RegisterGlobal((ref StyleFloat sv) => (int)sv.value);
            }
            else if (typeof(TStyle) == typeof(StyleFontDefinition))
            {
                ConverterGroups.RegisterGlobal((ref Font v) => new StyleFontDefinition(v));
                ConverterGroups.RegisterGlobal((ref FontAsset v) => new StyleFontDefinition(v));
                ConverterGroups.RegisterGlobal((ref StyleFontDefinition sv) => sv.value.font);
                ConverterGroups.RegisterGlobal((ref StyleFontDefinition sv) => sv.value.fontAsset);
            }
        }

        static Dictionary<string, string> s_UssNames;

        /// <summary>The USS name of a style property, by its C# name.</summary>
        internal static string UssName(string propertyName)
        {
            if (s_UssNames == null)
            {
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var entry in StyleProperties.NameToId)
                {
                    var id = entry.Value.ToString();
                    names[char.ToLowerInvariant(id[0]) + id.Substring(1)] = entry.Key;
                }
                s_UssNames = names;
            }
            return s_UssNames.TryGetValue(propertyName, out var uss) ? uss : propertyName;
        }

        /// <summary>The non-obsolete properties of a style interface, as Unity's style bags list them.</summary>
        internal static IEnumerable<PropertyInfo> Properties(Type styleInterface)
        {
            foreach (var p in styleInterface.GetProperties())
                if (!p.IsDefined(typeof(ObsoleteAttribute), false)) yield return p;
        }
    }

    /// <summary>A bag whose properties are also found under their USS names.</summary>
    internal abstract class StyleAccessPropertyBag<TContainer> : PropertyBag<TContainer>, INamedProperties<TContainer>
    {
        readonly List<IProperty<TContainer>> m_List = new List<IProperty<TContainer>>();
        readonly Dictionary<string, IProperty<TContainer>> m_ByName = new Dictionary<string, IProperty<TContainer>>(StringComparer.Ordinal);

        protected void Add(IProperty<TContainer> property)
        {
            m_List.Add(property);
            m_ByName.Add(property.Name, property);
            var uss = StylePropertyBags.UssName(property.Name);
            if (uss != property.Name) m_ByName.Add(uss, property);
        }

        public override PropertyCollection<TContainer> GetProperties() => new PropertyCollection<TContainer>(m_List);

        public override PropertyCollection<TContainer> GetProperties(ref TContainer container) => new PropertyCollection<TContainer>(m_List);

        public bool TryGetProperty(ref TContainer container, string name, out IProperty<TContainer> property) =>
            m_ByName.TryGetValue(name, out property);
    }

    /// <summary>`style`: the inline style values, read and written through IStyle.</summary>
    internal sealed class InlineStylePropertyBag : StyleAccessPropertyBag<InlineStyle>
    {
        internal InlineStylePropertyBag(HashSet<Type> styleTypes)
        {
            foreach (var p in StylePropertyBags.Properties(typeof(IStyle)))
            {
                styleTypes.Add(p.PropertyType);
                var type = typeof(InlineStyleProperty<>).MakeGenericType(p.PropertyType);
                Add((IProperty<InlineStyle>)Activator.CreateInstance(type, p));
            }
        }

        sealed class InlineStyleProperty<TStyle> : Property<InlineStyle, TStyle>
        {
            readonly string m_Name;
            readonly Func<IStyle, TStyle> m_Get;
            readonly Action<IStyle, TStyle> m_Set;

            public InlineStyleProperty(PropertyInfo property)
            {
                m_Name = property.Name;
                m_Get = (Func<IStyle, TStyle>)Delegate.CreateDelegate(typeof(Func<IStyle, TStyle>), property.GetGetMethod());
                m_Set = (Action<IStyle, TStyle>)Delegate.CreateDelegate(typeof(Action<IStyle, TStyle>), property.GetSetMethod());
            }

            public override string Name => m_Name;
            public override bool IsReadOnly => false;
            public override TStyle GetValue(ref InlineStyle container) => m_Get(container);
            public override void SetValue(ref InlineStyle container, TStyle value) => m_Set(container, value);
        }
    }

    /// <summary>`resolvedStyle`: the resolved values, read-only.</summary>
    internal sealed class ResolvedStylePropertyBag : StyleAccessPropertyBag<ResolvedStyle>
    {
        internal ResolvedStylePropertyBag()
        {
            foreach (var p in StylePropertyBags.Properties(typeof(IResolvedStyle)))
            {
                var type = typeof(ResolvedStyleProperty<>).MakeGenericType(p.PropertyType);
                Add((IProperty<ResolvedStyle>)Activator.CreateInstance(type, p));
            }
        }

        sealed class ResolvedStyleProperty<TValue> : Property<ResolvedStyle, TValue>
        {
            readonly string m_Name;
            readonly Func<IResolvedStyle, TValue> m_Get;

            public ResolvedStyleProperty(PropertyInfo property)
            {
                m_Name = property.Name;
                m_Get = (Func<IResolvedStyle, TValue>)Delegate.CreateDelegate(typeof(Func<IResolvedStyle, TValue>), property.GetGetMethod());
            }

            public override string Name => m_Name;
            public override bool IsReadOnly => true;
            public override TValue GetValue(ref ResolvedStyle container) => m_Get(container);
            public override void SetValue(ref ResolvedStyle container, TValue value) => throw new InvalidOperationException();
        }
    }

    /// <summary>A Style* value: its `value` and `keyword`.</summary>
    internal sealed class StyleValuePropertyBag<TStyle, TValue> : ContainerPropertyBag<TStyle> where TStyle : IStyleValue<TValue>
    {
        public StyleValuePropertyBag()
        {
            AddProperty(new ValueProperty());
            AddProperty(new KeywordProperty());
        }

        sealed class ValueProperty : Property<TStyle, TValue>
        {
            public override string Name => "value";
            public override bool IsReadOnly => false;
            public override TValue GetValue(ref TStyle container) => container.value;
            public override void SetValue(ref TStyle container, TValue value) => container.value = value;
        }

        sealed class KeywordProperty : Property<TStyle, StyleKeyword>
        {
            public override string Name => "keyword";
            public override bool IsReadOnly => false;
            public override StyleKeyword GetValue(ref TStyle container) => container.keyword;
            public override void SetValue(ref TStyle container, StyleKeyword value) => container.keyword = value;
        }
    }

    /// <summary>A Length: its `value` and `unit`.</summary>
    internal sealed class LengthPropertyBag : ContainerPropertyBag<Length>
    {
        public LengthPropertyBag()
        {
            AddProperty(new ValueProperty());
            AddProperty(new UnitProperty());
        }

        sealed class ValueProperty : Property<Length, float>
        {
            public override string Name => "value";
            public override bool IsReadOnly => false;
            public override float GetValue(ref Length container) => container.value;
            public override void SetValue(ref Length container, float value) => container.value = value;
        }

        sealed class UnitProperty : Property<Length, LengthUnit>
        {
            public override string Name => "unit";
            public override bool IsReadOnly => false;
            public override LengthUnit GetValue(ref Length container) => container.unit;
            public override void SetValue(ref Length container, LengthUnit value) => container.unit = value;
        }
    }
}
