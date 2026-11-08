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
        /// <summary>The document's asset path: asset urls in its attributes resolve against it.</summary>
        internal string m_UaAssetPath;
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
                var element = UxmlBuilder.Create(this, child, target as TemplateContainer, target);
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
        /// <summary>Where the element's tag starts in the document (1-based line and column of its
        /// name), as Unity's importer warnings name it.</summary>
        public int Line, Column;
        /// <summary>Attributes whose import-time warnings were logged (once per document element).</summary>
        public HashSet<string> ReportedUrls;

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
            var positions = ElementPositions(text);
            int next = 0;
            var rootNode = new UxmlNode { Namespace = root.NamespaceURI, Type = root.LocalName };
            Locate(rootNode, positions, ref next);
            asset.m_UaAssetPath = assetPath;
            var errors = new List<string>();
            ReadChildren(asset, assetPath, root, rootNode, errors, positions, ref next);
            asset.m_UaRoot = rootNode;
            foreach (var e in errors) Debug.LogWarning(assetPath + ": " + e);
            asset.importedWithWarnings = errors.Count > 0;
            asset.contentHash = text.GetHashCode();
            return default;
        }

        internal struct LoadedFileHandle { }

        /// <summary>The line and column of every element's name, in document order (the order the
        /// document's elements are visited in, depth first).</summary>
        static List<(int Line, int Column)> ElementPositions(string text)
        {
            var positions = new List<(int, int)>();
            using var reader = XmlReader.Create(new System.IO.StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            var info = (IXmlLineInfo)reader;
            while (reader.Read())
                if (reader.NodeType == XmlNodeType.Element) positions.Add((info.LineNumber, info.LinePosition));
            return positions;
        }

        static void Locate(UxmlNode node, List<(int Line, int Column)> positions, ref int next)
        {
            if (next >= positions.Count) return;
            (node.Line, node.Column) = positions[next++];
        }

        /// <summary>Skips the positions of an element that makes no node, and of its descendants.</summary>
        static void Skip(XmlElement xml, ref int next)
        {
            next++;
            foreach (XmlNode child in xml.ChildNodes)
                if (child is XmlElement element) Skip(element, ref next);
        }

        static void ReadChildren(VisualTreeAsset asset, string assetPath, XmlElement xml, UxmlNode node, List<string> errors,
            List<(int Line, int Column)> positions, ref int next)
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
                if (local == "Style" || local == "Template" || local == "AttributeOverrides") Skip(element, ref next);
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
                Locate(childNode, positions, ref next);
                if (local == "Instance") childNode.TemplateName = element.GetAttribute("template");
                ReadChildren(asset, assetPath, element, childNode, errors, positions, ref next);
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

        /// <summary>Where the element of a UXML tag comes from.</summary>
        enum ElementSource
        {
            Unknown,
            /// <summary>One of Unbound's own element types, its attributes applied onto its properties.</summary>
            BuiltIn,
            /// <summary>A game type declared with [UxmlElement], its [UxmlAttribute] members applied.</summary>
            UxmlElement,
            /// <summary>A game UxmlFactory, which creates the element and reads its attributes.</summary>
            Factory,
        }

        /// <summary>The tag's qualified name: its namespace and local name, or a dotted local name.</summary>
        static string QualifiedName(UxmlNode node)
        {
            if (node.Type.Contains(".")) return node.Type;
            return UxmlTypeIndex.Qualified(node.Namespace, node.Type);
        }

        static ElementSource Resolve(UxmlNode node, out Type type, out List<IUxmlFactory> factories, out string hint)
        {
            factories = null;
            hint = null;
            type = ResolveType(node);
            if (type != null && UxmlTypeIndex.IsEngineType(type)) return ElementSource.BuiltIn;
            // A [UxmlElement] is found under its class name and under its custom name.
            if (type != null && type.IsDefined(typeof(UxmlElementAttribute), false)) return ElementSource.UxmlElement;
            var qualified = QualifiedName(node);
            var custom = UxmlTypeIndex.FindCustomName(qualified, out var duplicate);
            if (duplicate != null)
            {
                hint = duplicate;
                type = null;
                return ElementSource.Unknown;
            }
            if (custom != null)
            {
                type = custom;
                return ElementSource.UxmlElement;
            }
            // Without [UxmlElement], an element is created by its UxmlFactory.
            factories = UxmlTypeIndex.FindFactories(qualified);
            if (factories != null && factories.Count > 0) return ElementSource.Factory;
            if (type != null)
                hint = $"'{type.FullName}' is neither declared with [UxmlElement] (as a partial class) nor created by a UxmlFactory.";
            type = null;
            return ElementSource.Unknown;
        }

        internal static VisualElement Create(VisualTreeAsset asset, UxmlNode node, TemplateContainer target, VisualElement cloneTarget)
        {
            VisualElement element;
            var source = ElementSource.BuiltIn;
            Type type = null;
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
                source = Resolve(node, out type, out var factories, out var hint);
                if (source == ElementSource.Unknown)
                {
                    Debug.LogError($"Element '{(string.IsNullOrEmpty(node.Namespace) ? "" : node.Namespace + ".")}{node.Type}' has no registered factory method."
                        + (hint != null ? " " + hint : ""));
                    var unknown = new Label($"Unknown type: '{node.Type}'");
                    unknown.style.color = Color.red;
                    return unknown;
                }
                if (source == ElementSource.Factory)
                {
                    element = CreateWithFactory(asset, node, factories, cloneTarget);
                    if (element == null) return null;
                }
                else
                {
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
            }
            if (source == ElementSource.UxmlElement) ApplyUxmlAttributes(asset, element, node, type);
            else if (source == ElementSource.BuiltIn) ApplyAttributes(asset, element, node);
            ApplyClassesAndSheets(node, element);
            if (node.InlineSheet != null)
            {
                element.uaInlineSheet = node.InlineSheet;
                element.uaInlineRule = node.InlineSheet.rules.Length > 0 ? node.InlineSheet.rules[0] : null;
                element.UaStyleChanged();
            }
            foreach (var child in node.Children)
            {
                var childElement = Create(asset, child, target, cloneTarget);
                if (childElement == null) continue;
                childElement.visualTreeAssetSource = asset;
                element.Add(childElement);
            }
            if (node.Attr("content-container") != null && target != null) target.SetContentContainer(element);
            return element;
        }

        static readonly string[] s_FirstAttributes = { "choices", "low-value", "high-value", "max-length" };

        static List<KeyValuePair<string, string>> Ordered(UxmlNode node)
        {
            var ordered = new List<KeyValuePair<string, string>>();
            foreach (var first in s_FirstAttributes)
                foreach (var a in node.Attributes) if (a.Key == first) ordered.Add(a);
            foreach (var a in node.Attributes)
                if (Array.IndexOf(s_FirstAttributes, a.Key) < 0) ordered.Add(a);
            return ordered;
        }

        static void ApplyAttributes(VisualTreeAsset asset, VisualElement element, UxmlNode node)
        {
            List<KeyValuePair<string, string>> enumAttributes = null;
            foreach (var a in Ordered(node))
            {
                if (IsEnumFieldAttribute(element, a.Key)) (enumAttributes ??= new List<KeyValuePair<string, string>>()).Add(a);
                else ApplyAttribute(element, a.Key, a.Value, asset: asset, node: node);
            }
            ApplyEnumFieldAttributes(asset, node, element, enumAttributes);
        }

        // EnumField's own attributes `type`, `value` and `include-obsolete-values` (the attribute
        // descriptions the 6000.0 API metadata lists for EnumField) together initialize the field. So
        // they are gathered from the element's attributes and applied once, whatever their order in
        // the file. Documented (6000.0 Manual "EnumField", Scripting API EnumField and EnumField.Init):
        // `value` is "The value associated with the field"; the Manual's example sets
        // include-obsolete-values without a type and calls Init from C#; Init "Initializes the
        // EnumField with a default value, and initializes its underlying type". Unbound's choices
        // (unmeasured; the pages do not describe these):
        //   - `type` is read like other UXML type attributes (UxmlAttributeValues.TryReadTypeName): an
        //     assembly-qualified name ("Namespace.Type, Assembly"), else the type of that full name in
        //     any loaded assembly. It must name an enum;
        //   - `value` is read as the builder reads other enum attributes (TryConvert): a declared name
        //     in any case, dashes ignored, or an Int32 integer (which may name no declared value);
        //     surrounding spaces ignored. A name whose value lies outside Int32 (a long, ulong or uint
        //     member), or any other form that reader refuses, is read as Enum.TryParse reads it (names
        //     in any case, comma-separated flags, integers of the underlying type). Without `value`,
        //     the field takes the type's zero value;
        //   - a `value` without a `type` is read against the type the field already has (one its
        //     constructor gave it, e.g. in a subclass), keeping `include-obsolete-values`;
        //   - an empty `type` or `value` counts as absent; `include-obsolete-values` is true only for
        //     "true" (any case), as the builder reads other boolean attributes;
        //   - each of these logs ONE error naming the attribute and the element: a `type` that is not
        //     loaded or not an enum (the field stays untyped), a `value` the type cannot read (the
        //     field still takes the type, with its zero value), a `value` without a `type` on a field
        //     that has no type (it stays untyped), and a `value` its existing type cannot read (the
        //     field keeps its value);
        //   - without a `type`, `include-obsolete-values` is kept until Init sets it.
        static readonly string[] s_EnumFieldAttributes = { "type", "value", "include-obsolete-values" };

        static bool IsEnumFieldAttribute(VisualElement element, string name) =>
            element is EnumField && Array.IndexOf(s_EnumFieldAttributes, name) >= 0;

        static void ApplyEnumFieldAttributes(VisualTreeAsset asset, UxmlNode node, VisualElement element, List<KeyValuePair<string, string>> attributes)
        {
            if (!(element is EnumField field) || attributes == null) return;
            string typeText = null, valueText = null, obsoleteText = null;
            foreach (var a in attributes)
            {
                if (a.Key == "type") typeText = a.Value;
                else if (a.Key == "value") valueText = a.Value;
                else if (a.Key == "include-obsolete-values") obsoleteText = a.Value;
            }
            bool includeObsolete = obsoleteText != null ? ParseBool(obsoleteText) : field.includeObsoleteValues;
            bool hasValue = !string.IsNullOrWhiteSpace(valueText);
            if (string.IsNullOrWhiteSpace(typeText))
            {
                if (hasValue && field.type == null)
                    Debug.LogError($"{Describe(asset, node)} attribute 'value': '{valueText}' needs a 'type' attribute naming its enum type; the field stays untyped.");
                else if (hasValue)
                {
                    if (TryEnumFieldValue(valueText.Trim(), field.type, out var typed))
                    {
                        field.Init((Enum)typed, includeObsolete);
                        return;
                    }
                    Debug.LogError($"{Describe(asset, node)} attribute 'value': '{valueText}' is not a value of {field.type.FullName}; the field keeps its value.");
                }
                field.includeObsoleteValues = includeObsolete;
                return;
            }
            if (!UxmlAttributeValues.TryReadTypeName(typeText, typeof(Enum), out var read, out var error) || !(read is Type enumType) || !enumType.IsEnum)
            {
                Debug.LogError($"{Describe(asset, node)} attribute 'type': {error ?? $"'{typeText}' is not an enum type"}; the field stays untyped"
                    + (hasValue ? " and its 'value' is not applied." : "."));
                field.includeObsoleteValues = includeObsolete;
                return;
            }
            var value = (Enum)Enum.ToObject(enumType, 0);
            if (hasValue)
            {
                if (TryEnumFieldValue(valueText.Trim(), enumType, out var parsed)) value = (Enum)parsed;
                else Debug.LogError($"{Describe(asset, node)} attribute 'value': '{valueText}' is not a value of {enumType.FullName}; the field takes the type's zero value.");
            }
            field.Init(value, includeObsolete);
        }

        /// <summary>An EnumField `value` of <paramref name="enumType"/>: as the builder reads other enum
        /// attributes, else (that reader refuses the text, or its Int32 conversion overflows for a
        /// member outside Int32) as Enum.TryParse reads it. Never throws for a well-formed type.</summary>
        static bool TryEnumFieldValue(string text, Type enumType, out object value)
        {
            try
            {
                if (TryConvert(text, enumType, out value)) return true;
            }
            catch (OverflowException)
            {
            }
            return Enum.TryParse(enumType, text, true, out value);
        }

        /// <summary>Attributes the builder itself reads (classes, inline style, template content).</summary>
        static bool IsStructural(string name) => name == "class" || name == "style" || name == "content-container" || name == "template";

        static string Describe(VisualTreeAsset asset, UxmlNode node) =>
            $"{(string.IsNullOrEmpty(asset?.m_UaAssetPath) ? asset?.name : asset.m_UaAssetPath)}: <{QualifiedName(node)}>";

        /// <summary>The attributes of a [UxmlElement]: its [UxmlAttribute] members (by name or obsolete
        /// name), the others as on built-in elements (the attributes of its built-in base classes).</summary>
        static void ApplyUxmlAttributes(VisualTreeAsset asset, VisualElement element, UxmlNode node, Type type)
        {
            var members = UxmlTypeIndex.MembersOf(type);
            List<KeyValuePair<string, string>> enumAttributes = null;
            foreach (var a in Ordered(node))
            {
                UxmlMember member = null;
                if (!IsStructural(a.Key) && !members.ByName.TryGetValue(a.Key, out member) && members.ByObsoleteName.TryGetValue(a.Key, out member))
                {
                    // Unbound's choice when several of a member's names are present: the current
                    // name, else the first obsolete name in declaration order.
                    if (PreferredName(node, member) != a.Key) continue;
                }
                if (member == null)
                {
                    if (IsEnumFieldAttribute(element, a.Key)) (enumAttributes ??= new List<KeyValuePair<string, string>>()).Add(a);
                    else ApplyAttribute(element, a.Key, a.Value, engineDeclaredOnly: true, asset: asset, node: node);
                    continue;
                }
                if (member.Problem != null) continue;
                object value;
                if (typeof(Object).IsAssignableFrom(member.ValueType) && UxmlTypeIndex.ConverterFor(member.ValueType) == null)
                {
                    // Measured: an unresolved url warns and gives null; an object of another type
                    // fails on assignment below.
                    var found = UxmlAttributeValues.ResolveReference(a.Value, member.ValueType, asset, out var referenced);
                    if (found == UxmlAttributeValues.Reference.Empty || found == UxmlAttributeValues.Reference.Missing)
                        UxmlAttributeValues.ReportUrl(node, a.Key, a.Value, asset);
                    if (found == UxmlAttributeValues.Reference.OtherType)
                    {
                        Debug.LogException(new ArgumentException($"Object of type '{referenced.GetType().FullName}' cannot be converted to type '{member.ValueType.FullName}'."));
                        continue;
                    }
                    value = referenced;
                }
                else if (member.ValueType == typeof(Background) && UxmlTypeIndex.ConverterFor(member.ValueType) == null
                    && UxmlAttributeValues.ResolveReference(a.Value, typeof(Object), asset, out _) is var image
                    && (image == UxmlAttributeValues.Reference.Empty || image == UxmlAttributeValues.Reference.Missing))
                {
                    // As for object members: the importer's warnings and no image.
                    UxmlAttributeValues.ReportUrl(node, a.Key, a.Value, asset);
                    value = default(Background);
                }
                else if (member.ValueType == typeof(Type))
                {
                    value = UxmlAttributeValues.ReadTypeReference(node, a.Key, a.Value,
                        member.Info.GetCustomAttribute<UxmlTypeReferenceAttribute>(false)?.baseType);
                }
                else if (!UxmlAttributeValues.TryReadLenient(a.Value, member.ValueType, member.Info, asset, out value, out var error))
                {
                    Debug.LogError($"{Describe(asset, node)} attribute '{a.Key}': {error}; '{member.Info.Name}' keeps its value.");
                    continue;
                }
                try
                {
                    member.Set(element, value);
                }
                catch (TargetInvocationException e)
                {
                    Debug.LogException(e.InnerException ?? e);
                }
                catch (ArgumentException e)
                {
                    Debug.LogError($"{Describe(asset, node)} attribute '{a.Key}': {e.Message}");
                }
            }
            ApplyEnumFieldAttributes(asset, node, element, enumAttributes);
        }

        static string PreferredName(UxmlNode node, UxmlMember member)
        {
            if (node.Attr(member.UxmlName) != null) return member.UxmlName;
            foreach (var obsolete in member.ObsoleteNames)
                if (!string.IsNullOrEmpty(obsolete) && node.Attr(obsolete) != null) return obsolete;
            return null;
        }

        /// <summary>An element of a game UxmlFactory: the first factory of the tag that accepts the
        /// attributes creates it, its traits reading them.</summary>
        static VisualElement CreateWithFactory(VisualTreeAsset asset, UxmlNode node, List<IUxmlFactory> factories, VisualElement cloneTarget)
        {
            var bag = new UxmlNodeAttributes(node);
            var cc = new CreationContext(new Dictionary<string, VisualElement>(), asset, cloneTarget);
            IUxmlFactory chosen = null;
            VisualElement element;
            try
            {
                foreach (var factory in factories)
                {
                    if (factory.AcceptsAttributeBag(bag, cc))
                    {
                        chosen = factory;
                        break;
                    }
                }
                if (chosen == null)
                {
                    Debug.LogError($"{Describe(asset, node)}: no UxmlFactory of this element accepts its attributes.");
                    return null;
                }
                element = chosen.Create(bag, cc);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }
            if (element == null)
            {
                Debug.LogError($"{Describe(asset, node)}: {chosen.GetType().FullName}.Create returned no element.");
                return null;
            }
            ApplyBuiltInTraitAttributes(asset, chosen, element, node);
            return element;
        }

        /// <summary>A game traits class may derive from the traits of a built-in control whose Init
        /// Unbound has not written (a generated stub, which warns when called and reads nothing). The
        /// attributes the game's own traits do not describe are then applied as on built-in elements,
        /// so the built-in base's attributes (`text`, `value`, ...) still take effect.</summary>
        static void ApplyBuiltInTraitAttributes(VisualTreeAsset asset, IUxmlFactory factory, VisualElement element, UxmlNode node)
        {
            UxmlTraits traits = null;
            for (var t = factory.GetType(); t != null && traits == null; t = t.BaseType)
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(BaseUxmlFactory<,>))
                    traits = t.GetField("m_Traits", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(factory) as UxmlTraits;
            }
            if (traits == null) return;
            Type stub = null;
            var initParameters = new[] { typeof(VisualElement), typeof(IUxmlAttributes), typeof(CreationContext) };
            for (var t = traits.GetType(); t != null && t != typeof(UxmlTraits) && stub == null; t = t.BaseType)
            {
                var init = t.GetMethod("Init", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, initParameters, null);
                if (init != null && init.IsDefined(typeof(global::Unbound.StubAttribute), false)) stub = t;
            }
            if (stub == null) return;
            var described = new HashSet<string>(StringComparer.Ordinal);
            foreach (var description in UxmlAttributeValues.DescriptionsOf(traits, stub))
            {
                if (!string.IsNullOrEmpty(description.name)) described.Add(description.name);
                if (description.obsoleteNames != null)
                    foreach (var obsolete in description.obsoleteNames)
                        if (!string.IsNullOrEmpty(obsolete)) described.Add(obsolete);
            }
            List<KeyValuePair<string, string>> enumAttributes = null;
            foreach (var a in Ordered(node))
            {
                if (described.Contains(a.Key)) continue;
                if (IsEnumFieldAttribute(element, a.Key)) (enumAttributes ??= new List<KeyValuePair<string, string>>()).Add(a);
                else ApplyAttribute(element, a.Key, a.Value, engineDeclaredOnly: true, asset: asset, node: node);
            }
            ApplyEnumFieldAttributes(asset, node, element, enumAttributes);
        }

        /// <summary>Applies one attribute as on built-in elements. With <paramref name="engineDeclaredOnly"/>
        /// (a game element's undeclared attributes), only properties declared on engine types are
        /// written: a game's own public property is not a UXML attribute without [UxmlAttribute].
        /// <paramref name="asset"/> and <paramref name="node"/> (the document and its element) resolve
        /// asset references and name the element in a diagnostic.</summary>
        internal static void ApplyAttribute(VisualElement element, string name, string value, bool engineDeclaredOnly = false,
            VisualTreeAsset asset = null, UxmlNode node = null)
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
            var property = FindProperty(element.GetType(), name, engineDeclaredOnly);
            if (property == null)
            {
                // Unknown attributes are ignored, as Unity's factories ignore them.
                return;
            }
            if (property.PropertyType == typeof(Background))
            {
                ApplyImageAttribute(element, property, name, value, asset, node);
                return;
            }
            if (IsItemTemplate(property, name))
            {
                ApplyItemTemplateAttribute(element, property, name, value, asset, node);
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

        // An engine element's Background property (Button.iconImage and Tab.iconImage, the only ones
        // Unbound's elements declare) read from an asset reference. Documented (6000.0 Manual "Button"
        // and "Tab", attribute `icon-image` of type Object: "The Texture, Sprite, or VectorImage that
        // will represent an icon within a Button element" / "Sets the icon for the Tab's header",
        // written as icon-image="/path/to/image-file.png"). Measured (Unity 6000.0.71f1,
        // tests/projects/uxmlattributes): textures and sprites by project url or path; an empty or
        // unresolved url logs the importer's warnings and keeps the value. Unbound's choices
        // (unmeasured): the value is converted as UxmlImageReferences reads it, and an asset of a class
        // a Background cannot take logs ONE error naming the document, the element and the attribute,
        // the property keeping its value. Other attributes Unbound cannot convert stay silent as before.
        static void ApplyImageAttribute(VisualElement element, PropertyInfo property, string name, string value, VisualTreeAsset asset, UxmlNode node)
        {
            // An empty or unresolved url warns as Unity's importer does and leaves the image as it
            // is (measured, tests/projects/uxmlattributes).
            var found = UxmlAttributeValues.ResolveReference(value, typeof(Object), asset, out _);
            if (found == UxmlAttributeValues.Reference.Empty || found == UxmlAttributeValues.Reference.Missing)
            {
                UxmlAttributeValues.ReportUrl(node, name, value, asset);
                return;
            }
            if (!UxmlImageReferences.TryRead(value, typeof(Background), asset, out var image, out var error))
            {
                var where = node != null ? Describe(asset, node) : $"{(string.IsNullOrEmpty(asset?.m_UaAssetPath) ? asset?.name : asset.m_UaAssetPath)}: <{element.GetType().FullName}>";
                Debug.LogError($"{where} attribute '{name}': {error}; '{property.Name}' keeps its value.");
                return;
            }
            try
            {
                property.SetValue(element, image);
            }
            catch (TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e);
            }
        }

        // ListView and TreeView `item-template`, a UXML template (VisualTreeAsset) reference. Documented
        // (6000.0 Manual "ListView" and "TreeView", UXML attribute `item-template` of type
        // UIElements.VisualTreeAsset, and
        // Scripting API ListView.itemTemplate: "A UXML template that constructs each recycled and
        // rebound element within the list. This template is designed to replace the ListView.makeItem
        // definition", TreeView.itemTemplate: the same text for "the tree"; Manual "Reference other
        // files from UXML": absolute paths begin with `/` or `project://database/`, relative paths start
        // from the current file). Unbound's choices (unmeasured; the pages do not describe them): the
        // reference is resolved, checked and reported as the `icon-image` references are
        // (UxmlAttributeValues.TryReadTemplate), and one that does not resolve to a VisualTreeAsset
        // logs ONE error naming the document, the element and the attribute, itemTemplate keeping its
        // value; only the attribute's documented name is read, on the property ListView or TreeView
        // declares (other elements' VisualTreeAsset properties are not mapped here). The itemTemplate
        // setters make the rows from the template.
        static bool IsItemTemplate(PropertyInfo property, string name) =>
            name == "item-template" && property.PropertyType == typeof(VisualTreeAsset)
            && (property.DeclaringType == typeof(ListView) || property.DeclaringType == typeof(TreeView));

        static void ApplyItemTemplateAttribute(VisualElement element, PropertyInfo property, string name, string value, VisualTreeAsset asset, UxmlNode node)
        {
            if (!UxmlAttributeValues.TryReadTemplate(value, asset, out var template, out var error))
            {
                var where = node != null ? Describe(asset, node) : $"{(string.IsNullOrEmpty(asset?.m_UaAssetPath) ? asset?.name : asset.m_UaAssetPath)}: <{element.GetType().FullName}>";
                Debug.LogError($"{where} attribute '{name}': {error}; '{property.Name}' keeps its value.");
                return;
            }
            try
            {
                property.SetValue(element, template);
            }
            catch (TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e);
            }
        }

        static readonly Dictionary<(Type, string, bool), PropertyInfo> s_Properties = new Dictionary<(Type, string, bool), PropertyInfo>();

        static PropertyInfo FindProperty(Type type, string attribute, bool engineDeclaredOnly)
        {
            if (s_Properties.TryGetValue((type, attribute, engineDeclaredOnly), out var cached)) return cached;
            string camel = KebabToCamel(attribute);
            PropertyInfo found = null;
            for (var t = type; t != null && found == null; t = t.BaseType)
            {
                if (engineDeclaredOnly && !UxmlTypeIndex.IsEngineType(t)) continue;
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (string.Equals(p.Name, camel, StringComparison.OrdinalIgnoreCase) && p.CanWrite && p.GetSetMethod() != null && p.GetIndexParameters().Length == 0)
                    {
                        found = p;
                        break;
                    }
                }
            }
            s_Properties[(type, attribute, engineDeclaredOnly)] = found;
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

    // Documented (6000.0): Button and Tab `icon-image` take a Texture, Sprite or VectorImage asset
    // (the Button Manual's text also names a Render Texture); Background.GetSelectedImage "can be of a
    // type of Texture, Sprite, RenderTexture or VectorImage" and Background.FromTexture2D,
    // FromRenderTexture, FromSprite and FromVectorImage create one; UxmlAttributeAttribute: "Unity
    // objects can also be UxmlAttributes and will contain a reference to the asset file"; the Manual
    // "Define UXML attributes for built-in types" writes a Texture2D as
    // my-texture="project://database/Assets/MyTexture.png".
    // Unbound's choices (unmeasured; the pages do not describe them):
    //   - references are resolved by the asset urls of USS and UXML (AssetPaths.LoadAsset:
    //     `project://database/...?fileID=..&guid=..#name`, `/Assets/...`, or relative to the document);
    //     a reference to a file without a fileID or a #name means the file's main asset (an image
    //     file's texture);
    //   - a Background is made by the factory of the asset's type; a Texture2D, RenderTexture, Sprite or
    //     VectorImage only, never another texture type (Cubemap, Texture3D, ...) nor an image derived
    //     from the asset (no sprite's texture, no texture's sprite);
    //   - a [UxmlAttribute] of a Texture type takes an asset of that type only;
    //   - an empty value is no image (an empty Background, a null texture);
    //   - each of these is an error the caller reports once, the value kept: no asset at the
    //     reference, an asset of another class, a texture type a Background cannot show, and a #name
    //     (without a fileID that resolves) naming more than one asset the value could take.
    /// <summary>UXML asset references read as images: Background values (Button and Tab `icon-image`, a
    /// [UxmlAttribute] of type Background) and Texture values (a [UxmlAttribute] of type Texture or a
    /// subclass).</summary>
    internal static class UxmlImageReferences
    {
        const string BackgroundTypes = "Texture2D, RenderTexture, Sprite or VectorImage";

        /// <summary>Whether values of <paramref name="type"/> are read here.</summary>
        internal static bool Reads(Type type) => type == typeof(Background) || typeof(Texture).IsAssignableFrom(type);

        static bool IsBackgroundImage(UnityEngine.Object o) => o is Texture2D || o is RenderTexture || o is Sprite || o is VectorImage;

        /// <summary>Reads <paramref name="text"/>, an asset reference of <paramref name="document"/>, as
        /// a value of <paramref name="type"/> (Background, or Texture or a subclass).</summary>
        internal static bool TryRead(string text, Type type, VisualTreeAsset document, out object result, out string error)
        {
            bool background = type == typeof(Background);
            result = background ? (object)default(Background) : null;
            error = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            var url = text.Trim();
            var referrer = document?.m_UaAssetPath;
            Func<UnityEngine.Object, bool> accepts = background ? IsBackgroundImage : (o => type.IsInstanceOfType(o));
            var expected = background ? BackgroundTypes : type.Name;
            var ambiguous = Ambiguous(referrer, url, accepts);
            if (ambiguous != null)
            {
                error = $"'{url}' names more than one asset that could be a {expected} ({ambiguous}); give the reference the fileID of one";
                return false;
            }
            UnityEngine.Object asset;
            try
            {
                asset = AssetPaths.LoadAsset(referrer, url, background ? null : type);
                // What the reference names when it is no asset of the type: only to report it.
                if (asset == null && !background) asset = AssetPaths.LoadAsset(referrer, url, null);
            }
            catch (Exception e)
            {
                error = $"the asset at '{url}' could not be loaded ({e.GetType().Name}: {e.Message})";
                return false;
            }
            if (asset == null)
            {
                error = $"no {(background ? "" : type.Name + " ")}asset at '{url}'";
                return false;
            }
            if (!accepts(asset))
            {
                error = background && asset is Texture
                    ? $"the asset at '{url}' is a {asset.GetType().Name}, a texture type a Background cannot show (a {BackgroundTypes})"
                    : $"the asset at '{url}' is a {asset.GetType().Name}, not a {expected}";
                return false;
            }
            if (!background)
            {
                result = asset;
                return true;
            }
            result = asset switch
            {
                Texture2D texture => Background.FromTexture2D(texture),
                RenderTexture renderTexture => Background.FromRenderTexture(renderTexture),
                Sprite sprite => Background.FromSprite(sprite),
                _ => Background.FromVectorImage((VectorImage)asset),
            };
            return true;
        }

        /// <summary>The types of the assets a reference's #name names when it names more than one that
        /// <paramref name="accepts"/> takes and no fileID of it resolves; otherwise null.</summary>
        static string Ambiguous(string referrer, string url, Func<UnityEngine.Object, bool> accepts)
        {
            var path = AssetPaths.Resolve(referrer, url, out var guid, out var fileId, out var subAsset);
            if (string.IsNullOrEmpty(subAsset)) return null;
            try
            {
                if (guid != null && fileId != 0 && global::Unbound.Runtime.AssetDatabaseRuntime.Load(guid, fileId, null) != null) return null;
                // The file the resolver looks the name up in: the path's, else the reference's guid.
                string fileGuid = null;
                if (!string.IsNullOrEmpty(path))
                {
                    try
                    {
                        fileGuid = global::Unbound.Runtime.AssetDatabaseRuntime.InfoByPath(path)?.Guid;
                    }
                    catch (Exception)
                    {
                        fileGuid = null;
                    }
                }
                if (string.IsNullOrEmpty(fileGuid)) fileGuid = guid;
                if (string.IsNullOrEmpty(fileGuid)) return null;
                var matches = new List<string>();
                foreach (var o in global::Unbound.Runtime.AssetDatabaseRuntime.LoadAll(fileGuid))
                    if (o != null && o.name == subAsset && accepts(o)) matches.Add(o.GetType().Name);
                return matches.Count > 1 ? string.Join(", ", matches) : null;
            }
            catch (Exception)
            {
                // The resolver reports what it cannot load.
                return null;
            }
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
