using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Xml;
using Unbound.UIElements;

// UXML documents: parsed from their source by Unbound's importer into a tree of element
// descriptions, instantiated into visual elements (types by their UXML names, attributes onto the
// elements' properties, <Style> sheets onto their elements, templates as template containers).

namespace UnityEngine.UIElements
{
    public partial class VisualTreeAsset : ScriptableObject
    {
        internal UxmlNode m_UaRoot;
        internal Dictionary<string, VisualTreeAsset> m_UaTemplates = new Dictionary<string, VisualTreeAsset>();
        bool m_ImportedWithErrors;
        bool m_ImportedWithWarnings;
        int m_ContentHash;

        public VisualTreeAsset() { }

        public bool importedWithErrors { get => m_ImportedWithErrors; internal set => m_ImportedWithErrors = value; }
        public bool importedWithWarnings { get => m_ImportedWithWarnings; internal set => m_ImportedWithWarnings = value; }
        public int contentHash { get => m_ContentHash; set => m_ContentHash = value; }

        public IEnumerable<VisualTreeAsset> templateDependencies => m_UaTemplates.Values;

        public IEnumerable<StyleSheet> stylesheets
        {
            get
            {
                var list = new List<StyleSheet>();
                void Visit(UxmlNode n)
                {
                    foreach (var s in n.StyleSheets) if (s != null && !list.Contains(s)) list.Add(s);
                    foreach (var c in n.Children) Visit(c);
                }
                if (m_UaRoot != null) Visit(m_UaRoot);
                return list;
            }
        }

        public TemplateContainer Instantiate()
        {
            var container = new TemplateContainer(name, this);
            CloneTree(container);
            return container;
        }

        public TemplateContainer Instantiate(string bindingPath)
        {
            var container = Instantiate();
            container.bindingPath = bindingPath;
            return container;
        }

        public TemplateContainer CloneTree() => Instantiate();

        public TemplateContainer CloneTree(string bindingPath) => Instantiate(bindingPath);

        public void CloneTree(VisualElement target) => CloneTree(target, out _, out _);

        public void CloneTree(VisualElement target, out int firstElementIndex, out int elementAddedCount)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            firstElementIndex = target.childCount;
            elementAddedCount = 0;
            if (m_UaRoot == null) return;
            UxmlBuilder.ApplyClassesAndSheets(m_UaRoot, target);
            foreach (var child in m_UaRoot.Children)
            {
                var element = UxmlBuilder.Create(this, child, target as TemplateContainer);
                if (element == null) continue;
                element.visualTreeAssetSource = this;
                target.hierarchy.Add(element);
                elementAddedCount++;
            }
        }
    }

    public partial class TemplateContainer : BindableElement
    {
        VisualElement m_ContentContainer;
        VisualTreeAsset m_TemplateSource;

        public TemplateContainer() : this(null) { }

        public TemplateContainer(string templateId)
        {
            this.templateId = templateId;
            m_ContentContainer = this;
        }

        internal TemplateContainer(string templateId, VisualTreeAsset templateSource) : this(templateId)
        {
            m_TemplateSource = templateSource;
        }

        public string templateId { get; private set; }

        public VisualTreeAsset templateSource { get => m_TemplateSource; internal set => m_TemplateSource = value; }

        public override VisualElement contentContainer => m_ContentContainer;

        internal void SetContentContainer(VisualElement content) => m_ContentContainer = content;
    }

    public partial interface IBindable
    {
        IBinding binding { get; set; }
        string bindingPath { get; set; }
    }

    public partial interface IBinding
    {
        void PreUpdate();
        void Update();
        void Release();
    }

    public partial class BindableElement : VisualElement, IBindable
    {
        public BindableElement() { }
        public IBinding binding { get; set; }
        public string bindingPath { get; set; }
    }
}

namespace Unbound.UIElements
{
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>An element of a UXML document: its tag (namespace and type), attributes in source
    /// order, child elements and the style sheets of its &lt;Style&gt; children.</summary>
    internal sealed class UxmlNode
    {
        public string Namespace;
        public string Type;
        public readonly List<KeyValuePair<string, string>> Attributes = new List<KeyValuePair<string, string>>();
        public readonly List<UxmlNode> Children = new List<UxmlNode>();
        public readonly List<StyleSheet> StyleSheets = new List<StyleSheet>();
        public StyleSheet InlineSheet;
        public string TemplateName;

        public string Attr(string name)
        {
            foreach (var a in Attributes) if (a.Key == name) return a.Value;
            return null;
        }
    }

    /// <summary>The UXML importer: reads the document and resolves its style sheets and templates.</summary>
    internal static class UxmlImporter
    {
        internal const long VisualTreeAssetFileId = 9197481963319205126;
        internal const long InlineStyleSheetFileId = 3466183405998542844;

        internal static LoadedFileHandle Import(string assetPath, string text, VisualTreeAsset asset)
        {
            var doc = new XmlDocument { PreserveWhitespace = false };
            doc.LoadXml(text);
            var root = doc.DocumentElement;
            var rootNode = new UxmlNode { Namespace = root.NamespaceURI, Type = root.LocalName };
            var errors = new List<string>();
            ReadChildren(asset, assetPath, root, rootNode, errors);
            asset.m_UaRoot = rootNode;
            foreach (var e in errors) Debug.LogWarning(assetPath + ": " + e);
            asset.importedWithWarnings = errors.Count > 0;
            asset.contentHash = text.GetHashCode();
            return default;
        }

        internal struct LoadedFileHandle { }

        static void ReadChildren(VisualTreeAsset asset, string assetPath, XmlElement xml, UxmlNode node, List<string> errors)
        {
            foreach (XmlAttribute a in xml.Attributes)
            {
                if (a.Prefix == "xmlns" || a.Name == "xmlns" || a.Name.StartsWith("xsi:", StringComparison.Ordinal) || a.Name == "editor-extension-mode") continue;
                node.Attributes.Add(new KeyValuePair<string, string>(a.Name, a.Value));
            }
            var style = node.Attr("style");
            if (!string.IsNullOrEmpty(style)) node.InlineSheet = InlineStyles.Parse(assetPath, style);
            foreach (XmlNode child in xml.ChildNodes)
            {
                if (!(child is XmlElement element)) continue;
                string local = element.LocalName;
                if (local == "Style")
                {
                    var src = element.GetAttribute("src");
                    if (string.IsNullOrEmpty(src)) src = element.GetAttribute("path");
                    var sheet = AssetPaths.LoadStyleSheet(assetPath, src);
                    if (sheet != null) node.StyleSheets.Add(sheet);
                    else errors.Add($"style sheet not found: {src}");
                    continue;
                }
                if (local == "Template")
                {
                    var name = element.GetAttribute("name");
                    var src = element.GetAttribute("src");
                    if (string.IsNullOrEmpty(src)) src = element.GetAttribute("path");
                    var template = AssetPaths.LoadAsset(assetPath, src, typeof(VisualTreeAsset)) as VisualTreeAsset;
                    if (template != null) asset.m_UaTemplates[name] = template;
                    else errors.Add($"template not found: {src}");
                    continue;
                }
                if (local == "AttributeOverrides") continue;
                var childNode = new UxmlNode { Namespace = element.NamespaceURI, Type = local };
                if (local == "Instance") childNode.TemplateName = element.GetAttribute("template");
                ReadChildren(asset, assetPath, element, childNode, errors);
                node.Children.Add(childNode);
            }
        }
    }

    /// <summary>Instantiation of UXML elements.</summary>
    internal static class UxmlBuilder
    {
        static readonly Dictionary<string, Type> s_Types = new Dictionary<string, Type>();

        internal static void ApplyClassesAndSheets(UxmlNode node, VisualElement element)
        {
            var classes = node.Attr("class");
            if (!string.IsNullOrEmpty(classes))
                foreach (var c in classes.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries)) element.AddToClassList(c);
            foreach (var sheet in node.StyleSheets) element.styleSheets.Add(sheet);
        }

        internal static Type ResolveType(UxmlNode node)
        {
            string ns = node.Namespace ?? "";
            string full = string.IsNullOrEmpty(ns) ? node.Type : ns + "." + node.Type;
            if (node.Type.Contains(".")) full = node.Type;
            if (s_Types.TryGetValue(full, out var cached)) return cached;
            Type found = typeof(VisualElement).Assembly.GetType(full);
            if (found == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        found = assembly.GetType(full);
                    }
                    catch (Exception)
                    {
                        found = null;
                    }
                    if (found != null) break;
                }
            }
            if (found != null && !typeof(VisualElement).IsAssignableFrom(found)) found = null;
            s_Types[full] = found;
            return found;
        }

        internal static VisualElement Create(VisualTreeAsset asset, UxmlNode node, TemplateContainer target)
        {
            VisualElement element;
            if (node.TemplateName != null)
            {
                if (!asset.m_UaTemplates.TryGetValue(node.TemplateName, out var template))
                {
                    Debug.LogError($"Template '{node.TemplateName}' not found in {asset.name}");
                    return null;
                }
                var container = new TemplateContainer(node.TemplateName, template);
                template.CloneTree(container);
                element = container;
            }
            else
            {
                var type = ResolveType(node);
                if (type == null)
                {
                    Debug.LogError($"Element '{(string.IsNullOrEmpty(node.Namespace) ? "" : node.Namespace + ".")}{node.Type}' has no registered factory method.");
                    var unknown = new Label($"Unknown type: '{node.Type}'");
                    unknown.style.color = Color.red;
                    return unknown;
                }
                try
                {
                    element = (VisualElement)Activator.CreateInstance(type, true);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    return null;
                }
            }
            ApplyAttributes(element, node);
            ApplyClassesAndSheets(node, element);
            if (node.InlineSheet != null)
            {
                element.uaInlineSheet = node.InlineSheet;
                element.uaInlineRule = node.InlineSheet.rules.Length > 0 ? node.InlineSheet.rules[0] : null;
                element.UaStyleChanged();
            }
            foreach (var child in node.Children)
            {
                var childElement = Create(asset, child, target);
                if (childElement == null) continue;
                childElement.visualTreeAssetSource = asset;
                element.Add(childElement);
            }
            if (node.Attr("content-container") != null && target != null) target.SetContentContainer(element);
            return element;
        }

        static readonly string[] s_FirstAttributes = { "choices", "low-value", "high-value", "max-length" };

        static void ApplyAttributes(VisualElement element, UxmlNode node)
        {
            var ordered = new List<KeyValuePair<string, string>>();
            foreach (var first in s_FirstAttributes)
                foreach (var a in node.Attributes) if (a.Key == first) ordered.Add(a);
            foreach (var a in node.Attributes)
                if (Array.IndexOf(s_FirstAttributes, a.Key) < 0) ordered.Add(a);
            foreach (var a in ordered) ApplyAttribute(element, a.Key, a.Value);
        }

        internal static void ApplyAttribute(VisualElement element, string name, string value)
        {
            switch (name)
            {
                case "name":
                    element.name = value;
                    return;
                case "class":
                case "style":
                case "content-container":
                case "template":
                    return;
                case "picking-mode":
                    if (TryEnum(typeof(PickingMode), value, out var picking)) element.pickingMode = (PickingMode)picking;
                    return;
                case "tooltip":
                    element.tooltip = value;
                    return;
                case "view-data-key":
                    element.viewDataKey = value;
                    return;
                case "focusable":
                    element.focusable = ParseBool(value);
                    return;
                case "tabindex":
                case "tab-index":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tab)) element.tabIndex = tab;
                    return;
                case "binding-path":
                    if (element is IBindable bindable) bindable.bindingPath = value;
                    return;
                case "enabled":
                    element.SetEnabled(ParseBool(value));
                    return;
                case "usage-hints":
                    return;
            }
            if (name.StartsWith("data-source", StringComparison.Ordinal)) return;
            var property = FindProperty(element.GetType(), name);
            if (property == null)
            {
                // Unknown attributes are ignored, as Unity's factories ignore them.
                return;
            }
            try
            {
                if (TryConvert(value, property.PropertyType, out var converted)) property.SetValue(element, converted);
            }
            catch (TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e);
            }
        }

        static readonly Dictionary<(Type, string), PropertyInfo> s_Properties = new Dictionary<(Type, string), PropertyInfo>();

        static PropertyInfo FindProperty(Type type, string attribute)
        {
            if (s_Properties.TryGetValue((type, attribute), out var cached)) return cached;
            string camel = KebabToCamel(attribute);
            PropertyInfo found = null;
            for (var t = type; t != null && found == null; t = t.BaseType)
            {
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (string.Equals(p.Name, camel, StringComparison.OrdinalIgnoreCase) && p.CanWrite && p.GetSetMethod() != null && p.GetIndexParameters().Length == 0)
                    {
                        found = p;
                        break;
                    }
                }
            }
            s_Properties[(type, attribute)] = found;
            return found;
        }

        static string KebabToCamel(string s)
        {
            var parts = s.Split('-');
            var b = new System.Text.StringBuilder(parts[0]);
            for (int i = 1; i < parts.Length; i++)
                if (parts[i].Length > 0) b.Append(char.ToUpperInvariant(parts[i][0])).Append(parts[i], 1, parts[i].Length - 1);
            return b.ToString();
        }

        static bool ParseBool(string value) => string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

        static bool TryEnum(Type type, string value, out int result)
        {
            result = 0;
            if (string.IsNullOrEmpty(value)) return false;
            var compact = value.Replace("-", "");
            foreach (var n in Enum.GetNames(type))
            {
                if (string.Equals(n, compact, StringComparison.OrdinalIgnoreCase))
                {
                    result = Convert.ToInt32(Enum.Parse(type, n), CultureInfo.InvariantCulture);
                    return true;
                }
            }
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        internal static bool TryConvert(string value, Type type, out object result)
        {
            result = null;
            if (type == typeof(string)) { result = value; return true; }
            if (type == typeof(bool)) { result = ParseBool(value); return true; }
            if (type == typeof(int))
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) { result = i; return true; }
                return false;
            }
            if (type == typeof(long))
            {
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) { result = l; return true; }
                return false;
            }
            if (type == typeof(float))
            {
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) { result = f; return true; }
                return false;
            }
            if (type == typeof(double))
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) { result = d; return true; }
                return false;
            }
            if (type == typeof(uint))
            {
                if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u)) { result = u; return true; }
                return false;
            }
            if (type.IsEnum)
            {
                if (TryEnum(type, value, out var e)) { result = Enum.ToObject(type, e); return true; }
                return false;
            }
            if (type == typeof(List<string>) || type == typeof(IList<string>) || type == typeof(IEnumerable<string>))
            {
                var list = new List<string>();
                foreach (var part in value.Split(',')) list.Add(part.Trim());
                result = list;
                return true;
            }
            if (type == typeof(Color))
            {
                var hex = value.Trim().TrimStart('#');
                if ((hex.Length == 6 || hex.Length == 8) && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgba))
                {
                    if (hex.Length == 6) rgba = (rgba << 8) | 0xFF;
                    result = (Color)new Color32((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
                    return true;
                }
                return false;
            }
            if (type == typeof(Vector2))
            {
                var p = value.Split(',');
                if (p.Length == 2 && float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                {
                    result = new Vector2(x, y);
                    return true;
                }
                return false;
            }
            return false;
        }
    }

    /// <summary>Inline styles of UXML elements: a declaration list read as one rule.</summary>
    internal static class InlineStyles
    {
        internal static StyleSheet Parse(string assetPath, string declarations)
        {
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            var importer = new UssImporter(assetPath + " (inline style)")
            {
                resolveAsset = AssetPaths.LoadUrl,
            };
            importer.Import(sheet, "* {" + declarations + "}");
            return sheet;
        }
    }
}
