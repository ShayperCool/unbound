using Unity.Properties;
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
    [global::UnityEngine.@HelpURLAttribute("UIE-VisualTree-landing")] [global::System.Serializable]
    public partial class VisualTreeAsset : ScriptableObject
    {
        internal UxmlNode m_UaRoot;
        internal Dictionary<string, VisualTreeAsset> m_UaTemplates = new Dictionary<string, VisualTreeAsset>();
        /// <summary>The document's asset path: asset urls in its attributes resolve against it.</summary>
        internal string m_UaAssetPath;
        [global::UnityEngine.@SerializeField] bool m_ImportedWithErrors;
        [global::UnityEngine.@SerializeField] bool m_ImportedWithWarnings;
        [global::UnityEngine.@SerializeField] int m_ContentHash;

        public VisualTreeAsset() { }

        public bool importedWithErrors { get => m_ImportedWithErrors; internal set => m_ImportedWithErrors = value; }
        public bool importedWithWarnings { get => m_ImportedWithWarnings; internal set => m_ImportedWithWarnings = value; }
        public int contentHash { get => m_ContentHash; set => m_ContentHash = value; }

        public IEnumerable<VisualTreeAsset> templateDependencies => m_UaTemplates.Values;

        /// <summary>The template this document imports under <paramref name="templateName"/>, or null.</summary>
        internal VisualTreeAsset ResolveTemplate(string templateName) =>
            templateName != null && m_UaTemplates.TryGetValue(templateName, out var template) ? template : null;

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

        public void CloneTree(VisualElement target, out int firstElementIndex, out int elementAddedCount) =>
            CloneTree(target, UxmlCloneScope.Root(), out firstElementIndex, out elementAddedCount);

        /// <summary>Clones the document into <paramref name="target"/> with the overrides and slots of
        /// the template instances it is cloned for.</summary>
        internal void CloneTree(VisualElement target, UxmlCloneScope scope, out int firstElementIndex, out int elementAddedCount)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            firstElementIndex = target.childCount;
            elementAddedCount = 0;
            if (m_UaRoot == null) return;
            UxmlBuilder.ApplyClassesAndSheets(m_UaRoot, target);
            foreach (var child in m_UaRoot.Children)
            {
                if (UxmlBuilder.IsBindings(child)) continue;
                var element = UxmlBuilder.Create(this, child, target as TemplateContainer, target, scope);
                if (element == null) continue;
                element.visualTreeAssetSource = this;
                target.hierarchy.Add(element);
                elementAddedCount++;
            }
        }
    }

    [global::UnityEngine.@HideInInspector] [global::UnityEngine.UIElements.@UxmlElementAttribute("Instance")] public partial class TemplateContainer : BindableElement
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

        [CreateProperty(ReadOnly = true)]
        public string templateId { get; private set; }

        internal void SetTemplateId(string id) => templateId = id;

        [CreateProperty(ReadOnly = true)]
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
        List<KeyValuePair<string, string>> m_Attributes = new List<KeyValuePair<string, string>>();
        public List<KeyValuePair<string, string>> Attributes => m_Attributes;
        public readonly List<UxmlNode> Children = new List<UxmlNode>();
        public readonly List<StyleSheet> StyleSheets = new List<StyleSheet>();
        public StyleSheet InlineSheet;
        public string TemplateName;
        /// <summary>Where the element's tag starts in the document (1-based line and column of its
        /// name), as Unity's importer warnings name it.</summary>
        public int Line, Column;
        /// <summary>Attributes whose import-time warnings were logged (once per document element).</summary>
        public HashSet<string> ReportedUrls;
        /// <summary>The element's position among the document's elements (depth first).</summary>
        public int Index;
        /// <summary>A template instance's attribute overrides, in document order (null without any).</summary>
        public List<UxmlAttributeOverride> Overrides;
        /// <summary>For an element description with overrides applied: the document each overridden
        /// attribute was written in (its asset references resolve against it) and the override's
        /// precedence (lower wins: outer instances first, then document order).</summary>
        public Dictionary<string, (VisualTreeAsset Source, int Rank)> OverrideSources;
        /// <summary>The slot this element defines in its document (`slot-name`), or null.</summary>
        public string SlotName;
        /// <summary>The slot of the enclosing template instance's document this element goes into
        /// (`slot`), or null.</summary>
        public string SlotUsage;

        /// <summary>A copy of the element description with other attributes (the same children,
        /// sheets and position).</summary>
        public UxmlNode WithAttributes(List<KeyValuePair<string, string>> attributes, Dictionary<string, (VisualTreeAsset Source, int Rank)> sources)
        {
            // The copy reports its import-time warnings once per document element, as the original.
            ReportedUrls ??= new HashSet<string>();
            var copy = (UxmlNode)MemberwiseClone();
            copy.m_Attributes = attributes;
            copy.OverrideSources = sources;
            return copy;
        }

        /// <summary>The document an attribute's asset references resolve against: the one that
        /// overrode it, else <paramref name="document"/>.</summary>
        public VisualTreeAsset SourceOf(string attribute, VisualTreeAsset document) =>
            OverrideSources != null && OverrideSources.TryGetValue(attribute, out var o) ? o.Source : document;

        public string Attr(string name)
        {
            foreach (var a in Attributes) if (a.Key == name) return a.Value;
            return null;
        }
    }

    /// <summary>One attribute value a template instance gives the named elements of its template.</summary>
    internal readonly struct UxmlAttributeOverride
    {
        public readonly string ElementName;
        public readonly string AttributeName;
        public readonly string Value;
        /// <summary>The document the override is written in.</summary>
        public readonly VisualTreeAsset Source;

        public UxmlAttributeOverride(string elementName, string attributeName, string value, VisualTreeAsset source)
        {
            ElementName = elementName;
            AttributeName = attributeName;
            Value = value;
            Source = source;
        }
    }

    /// <summary>
    /// What cloning a document carries into the templates it instances: the attribute overrides of
    /// the enclosing template instances (outermost first) and the slots their documents defined.
    /// Matches the Unity 6000.0.71f1 Editor (tests/projects/uxmltemplates): an override applies to
    /// every element of the instanced document, nested instances' documents included, whose name
    /// it names (not to template instances themselves); an outer instance's override wins over an
    /// inner one's, and within one instance the first override of an attribute wins. A child of an
    /// instance that names a slot (`slot`) goes into the element of that name (`slot-name`) defined so
    /// far by the instance's document or the documents it instances; the table is emptied once the
    /// instance's children are placed, so an outer instance reaches only its own document's slots.
    /// </summary>
    internal sealed class UxmlCloneScope
    {
        static readonly List<List<UxmlAttributeOverride>> s_NoRanges = new List<List<UxmlAttributeOverride>>();

        /// <summary>The overrides of the enclosing instances, outermost first.</summary>
        public readonly List<List<UxmlAttributeOverride>> Ranges;
        /// <summary>The slots defined so far by the instances' documents (one table for the whole
        /// clone, emptied once an instance's children are placed).</summary>
        public readonly Dictionary<string, VisualElement> Slots;

        /// <summary>Whether this is the clone TemplateContainer's deprecated traits make: it cannot
        /// clone a template instance, nor, under overrides, an element of the UXML serialization path
        /// (built-in and [UxmlElement] elements); the Unity 6000.0.71f1 Editor throws a
        /// NullReferenceException out of the clone there (tests/projects/uxmltemplates NestedOld,
        /// OldTemplateHost, OldTemplateOther; OldTemplateBare and OldFactoryHost clone).</summary>
        public readonly bool FromDeprecatedTraits;

        UxmlCloneScope(List<List<UxmlAttributeOverride>> ranges, Dictionary<string, VisualElement> slots, bool fromDeprecatedTraits = false)
        {
            Ranges = ranges;
            Slots = slots;
            FromDeprecatedTraits = fromDeprecatedTraits;
        }

        /// <summary>The scope of the template TemplateContainer's deprecated traits clone.</summary>
        public UxmlCloneScope ForDeprecatedTraits() => new UxmlCloneScope(Ranges, Slots, true);

        /// <summary>Fails as the deprecated traits' clone does before creating
        /// <paramref name="node"/> (<paramref name="serializationPath"/>: a built-in or [UxmlElement]
        /// element).</summary>
        public void CheckDeprecatedTraitsClone(UxmlNode node, bool serializationPath)
        {
            if (FromDeprecatedTraits && (node.TemplateName != null || serializationPath && Ranges.Count > 0))
                throw new NullReferenceException("Object reference not set to an instance of an object");
        }

        /// <summary>The scope of a document cloned from code: no overrides, its own slot table.</summary>
        public static UxmlCloneScope Root() => new UxmlCloneScope(s_NoRanges, new Dictionary<string, VisualElement>());

        /// <summary>The scope of a template cloned for an instance with <paramref name="overrides"/>.</summary>
        public UxmlCloneScope Enter(List<UxmlAttributeOverride> overrides)
        {
            if (overrides == null || overrides.Count == 0) return this;
            var ranges = new List<List<UxmlAttributeOverride>>(Ranges.Count + 1);
            ranges.AddRange(Ranges);
            ranges.Add(overrides);
            return new UxmlCloneScope(ranges, Slots, FromDeprecatedTraits);
        }

        /// <summary>The value an override gives attribute <paramref name="attribute"/> (or one of
        /// <paramref name="obsoleteNames"/>) of the element named <paramref name="elementName"/>.</summary>
        public bool TryGetOverride(string elementName, string attribute, IEnumerable<string> obsoleteNames, out string value, out VisualTreeAsset source)
        {
            value = null;
            source = null;
            if (string.IsNullOrEmpty(elementName)) return false;
            foreach (var range in Ranges)
            {
                foreach (var o in range)
                {
                    if (o.ElementName != elementName) continue;
                    if (o.AttributeName == attribute || IsObsoleteName(o.AttributeName, obsoleteNames))
                    {
                        value = o.Value;
                        source = o.Source;
                        return true;
                    }
                }
            }
            return false;
        }

        static bool IsObsoleteName(string name, IEnumerable<string> obsoleteNames)
        {
            if (obsoleteNames == null) return false;
            foreach (var obsolete in obsoleteNames)
                if (!string.IsNullOrEmpty(obsolete) && obsolete == name) return true;
            return false;
        }

        /// <summary>The element description with the overrides of its name applied (attributes it has
        /// get the override's value, others are added in the order they are first overridden), or
        /// <paramref name="node"/> itself when none apply. Template instances take none.</summary>
        public UxmlNode Apply(UxmlNode node)
        {
            if (Ranges.Count == 0 || node.TemplateName != null) return node;
            var name = node.Attr("name");
            if (string.IsNullOrEmpty(name)) return node;
            List<KeyValuePair<string, string>> attributes = null;
            Dictionary<string, (VisualTreeAsset Source, int Rank)> sources = null;
            int rank = 0;
            foreach (var range in Ranges)
            {
                foreach (var o in range)
                {
                    if (o.ElementName != name) continue;
                    // An outer instance's value (seen first) and the first of an instance's values win.
                    sources ??= new Dictionary<string, (VisualTreeAsset Source, int Rank)>();
                    if (!sources.TryAdd(o.AttributeName, (o.Source, rank++))) continue;
                    attributes ??= new List<KeyValuePair<string, string>>(node.Attributes);
                    var entry = new KeyValuePair<string, string>(o.AttributeName, o.Value);
                    int index = attributes.FindIndex(a => a.Key == o.AttributeName);
                    if (index >= 0) attributes[index] = entry;
                    else attributes.Add(entry);
                }
            }
            return attributes == null ? node : node.WithAttributes(attributes, sources);
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
            asset.importedWithWarnings = false;
            asset.importedWithErrors = false;
            var errors = new List<string>();
            ReadChildren(asset, assetPath, root, rootNode, null, errors, positions, new HashSet<string>(), ref next);
            asset.m_UaRoot = rootNode;
            foreach (var e in errors) Debug.LogWarning(assetPath + ": " + e);
            if (errors.Count > 0) asset.importedWithWarnings = true;
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
            node.Index = next;
            (node.Line, node.Column) = positions[next++];
        }

        /// <summary>Skips the positions of an element that makes no node, and of its descendants.</summary>
        static void Skip(XmlElement xml, ref int next)
        {
            next++;
            foreach (XmlNode child in xml.ChildNodes)
                if (child is XmlElement element) Skip(element, ref next);
        }

        const string k_SlotWarning = "Syntax - Slot are an experimental feature. Syntax and semantic may change in the future.";

        static bool IsDeclaration(XmlAttribute a) =>
            a.Prefix == "xmlns" || a.Name == "xmlns" || a.Name.StartsWith("xsi:", StringComparison.Ordinal);

        /// <summary>Reads an element's attributes, children and their descriptions. Slots and
        /// attribute overrides are reported as the Unity 6000.0.71f1 Editor's importer reports them
        /// (tests/projects/uxmltemplates): each `slot-name` and `slot` as experimental, a slot name
        /// defined twice in the document as an error (the first element keeps it), and an override of
        /// `name`, `class` or `style` as one it cannot make (the override is dropped).</summary>
        static void ReadChildren(VisualTreeAsset asset, string assetPath, XmlElement xml, UxmlNode node, UxmlNode parent, List<string> errors,
            List<(int Line, int Column)> positions, HashSet<string> slots, ref int next)
        {
            foreach (XmlAttribute a in xml.Attributes)
            {
                if (IsDeclaration(a) || a.Name == "editor-extension-mode") continue;
                if (a.Name == "slot-name" || a.Name == "slot")
                {
                    Debug.LogWarning($" ({node.Line},{node.Column}): {k_SlotWarning}");
                    asset.importedWithWarnings = true;
                    if (a.Name == "slot")
                    {
                        if (parent?.TemplateName != null) node.SlotUsage = a.Value;
                        else
                        {
                            Debug.LogError($" ({node.Line},{node.Column}): Semantic - Element has an assigned slot, but its parent '({(parent == null ? "" : UxmlBuilder.TypeName(parent))})({parent?.Index ?? 0})' is not a template reference");
                            asset.importedWithErrors = true;
                        }
                    }
                    else if (slots.Add(a.Value)) node.SlotName = a.Value;
                    else
                    {
                        Debug.LogError($" ({node.Line},{node.Column}): Semantic - Slot definition '{a.Value}' is defined more than once");
                        asset.importedWithErrors = true;
                    }
                    continue;
                }
                node.Attributes.Add(new KeyValuePair<string, string>(a.Name, a.Value));
            }
            var style = node.Attr("style");
            if (!string.IsNullOrEmpty(style)) node.InlineSheet = InlineStyles.Parse(assetPath, style);
            foreach (XmlNode child in xml.ChildNodes)
            {
                if (!(child is XmlElement element)) continue;
                string local = element.LocalName;
                // Overrides belong to template instances; elsewhere the tag is an element like any other.
                bool overrides = local == "AttributeOverrides" && node.TemplateName != null;
                if (overrides) ReadOverrides(asset, element, node, next < positions.Count ? positions[next] : default);
                if (local == "Style" || local == "Template" || overrides) Skip(element, ref next);
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
                if (overrides) continue;
                var childNode = new UxmlNode { Namespace = element.NamespaceURI, Type = local };
                Locate(childNode, positions, ref next);
                // Measured (Unity 6000.0.71f1, tests/projects/uitkbindings): a tag in <Bindings> that
                // names no UXML object type is reported by the importer, and only that binding is
                // dropped.
                if (UxmlBuilder.IsBindings(node) && !UxmlBuilder.IsUxmlObjectTag(childNode))
                {
                    Debug.LogError($" ({childNode.Line},{childNode.Column}): Semantic - Uxml object has an invalid child element: {UxmlBuilder.TypeName(childNode)}");
                    asset.importedWithErrors = true;
                }
                if (local == "Instance") childNode.TemplateName = element.GetAttribute("template");
                ReadChildren(asset, assetPath, element, childNode, node, errors, positions, slots, ref next);
                node.Children.Add(childNode);
            }
        }

        /// <summary>An `AttributeOverrides` element: each attribute but `element-name` overrides that
        /// attribute of the template's elements of that name.</summary>
        static void ReadOverrides(VisualTreeAsset asset, XmlElement xml, UxmlNode instance, (int Line, int Column) position)
        {
            if (!xml.HasAttribute("element-name"))
            {
                Debug.LogWarning($" ({position.Line},{position.Column}): Semantic - AttributeOverrides node missing 'element-name' attribute.");
                asset.importedWithWarnings = true;
                return;
            }
            var elementName = xml.GetAttribute("element-name");
            foreach (XmlAttribute a in xml.Attributes)
            {
                if (IsDeclaration(a) || a.Name == "element-name") continue;
                if (a.Name == "name" || a.Name == "class" || a.Name == "style")
                {
                    Debug.LogWarning($" ({position.Line},{position.Column}): Semantic - AttributeOverrides node cannot override attribute '{a.Name}'.");
                    asset.importedWithWarnings = true;
                    continue;
                }
                (instance.Overrides ??= new List<UxmlAttributeOverride>()).Add(new UxmlAttributeOverride(elementName, a.Name, a.Value, asset));
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

        static string FullName(UxmlNode node)
        {
            if (node.Type.Contains(".")) return node.Type;
            string ns = node.Namespace ?? "";
            return string.IsNullOrEmpty(ns) ? node.Type : ns + "." + node.Type;
        }

        internal static Type ResolveType(UxmlNode node)
        {
            string full = FullName(node);
            if (s_Types.TryGetValue(full, out var cached)) return cached;
            Type found = FindType(full);
            if (found != null && !typeof(VisualElement).IsAssignableFrom(found)) found = null;
            s_Types[full] = found;
            return found;
        }

        static readonly Dictionary<string, Type> s_ObjectTypes = new Dictionary<string, Type>(StringComparer.Ordinal);

        /// <summary>The UXML object type a tag names (a class with UXML serialized data, or marked
        /// [UxmlObject], that is not an element), else null.</summary>
        internal static Type UxmlObjectType(UxmlNode node)
        {
            string full = FullName(node);
            if (s_ObjectTypes.TryGetValue(full, out var cached)) return cached;
            var type = FindType(full);
            var result = IsUxmlObject(type) ? type : null;
            s_ObjectTypes[full] = result;
            return result;
        }

        static bool IsUxmlObject(Type type) =>
            type != null && !typeof(VisualElement).IsAssignableFrom(type)
            && (type.GetNestedType("UxmlSerializedData", BindingFlags.Public | BindingFlags.NonPublic) != null
                || HasAttributeNamed(type, "UxmlObjectAttribute"));

        static bool HasAttributeNamed(Type type, string name)
        {
            foreach (var data in type.CustomAttributes)
                if (data.AttributeType.Name == name) return true;
            return false;
        }

        /// <summary>An element's `<Bindings>` child: the bindings declared on the element (Unity's
        /// UI Builder writes the tag without a namespace).</summary>
        internal static bool IsBindings(UxmlNode node) => node.Type == "Bindings" && string.IsNullOrEmpty(node.Namespace) && node.TemplateName == null;

        /// <summary>Whether a tag names a UXML object type (a binding, a column, ...).</summary>
        internal static bool IsUxmlObjectTag(UxmlNode node) => UxmlObjectType(node) != null;

        /// <summary>The UXML object Binding type a tag names (DataBinding, a game's [UxmlObject]
        /// binding), else null.</summary>
        internal static Type BindingType(UxmlNode node)
        {
            var type = UxmlObjectType(node);
            return type != null && typeof(Binding).IsAssignableFrom(type) ? type : null;
        }

        /// <summary>A tag's namespace-qualified type name, as the importer reports it.</summary>
        internal static string TypeName(UxmlNode node) => FullName(node);

        // A type by its full name, in the engine or any loaded assembly.
        static Type FindType(string full)
        {
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

        internal static VisualElement Create(VisualTreeAsset asset, UxmlNode node, TemplateContainer target, VisualElement cloneTarget, UxmlCloneScope scope)
        {
            VisualElement element;
            var source = ElementSource.BuiltIn;
            Type type = null;
            if (node.TemplateName != null)
            {
                scope.CheckDeprecatedTraitsClone(node, false);
                if (!asset.m_UaTemplates.TryGetValue(node.TemplateName, out var template))
                {
                    Debug.LogError($"Template '{node.TemplateName}' not found in {asset.name}");
                    return null;
                }
                var container = new TemplateContainer(node.TemplateName, template);
                template.CloneTree(container, scope.Enter(node.Overrides), out _, out _);
                element = container;
            }
            else
            {
                source = Resolve(node, out type, out var factories, out var hint);
                if (source == ElementSource.Unknown && UxmlObjectType(node) is Type objectType)
                {
                    // UXML objects (a multi-column view's <Columns> and <SortColumnDescriptions>)
                    // describe their parent's data, not elements: not read yet.
                    Unbound.Runtime.Stubs.Ignored($"UXML object <{node.Type}> ({objectType.FullName})", "is not read yet: its parent is created without it");
                    return null;
                }
                if (source == ElementSource.Unknown)
                {
                    // The Unity 6000.0.71f1 Editor's message (tests/projects/uxmltemplates Odd), with
                    // Unbound's hint when it has one.
                    Debug.LogError($"Element '{(string.IsNullOrEmpty(node.Namespace) ? "" : node.Namespace + ".")}{node.Type}' is missing a UxmlElementAttribute and has no registered factory method. Please ensure that you have the correct namespace imported."
                        + (hint != null ? " " + hint : ""));
                    var unknown = new Label($"Unknown type: '{(string.IsNullOrEmpty(node.Namespace) ? "" : node.Namespace + ".")}{node.Type}'");
                    unknown.style.color = Color.red;
                    return unknown;
                }
                scope.CheckDeprecatedTraitsClone(node, source != ElementSource.Factory);
                if (source == ElementSource.Factory)
                {
                    // A factory's traits read the overrides through their attribute descriptions.
                    element = CreateWithFactory(asset, node, factories, cloneTarget, scope);
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
            if (source == ElementSource.UxmlElement) ApplyUxmlAttributes(asset, element, scope.Apply(node), type);
            else if (source == ElementSource.BuiltIn) ApplyAttributes(asset, element, scope.Apply(node));
            // A slot name the table holds already (from another document) fails the clone.
            if (node.SlotName != null) scope.Slots.Add(node.SlotName, element);
            ApplyClassesAndSheets(node, element);
            if (node.InlineSheet != null)
            {
                element.uaInlineSheet = node.InlineSheet;
                element.uaInlineRule = node.InlineSheet.rules.Length > 0 ? node.InlineSheet.rules[0] : null;
                element.UaStyleChanged();
            }
            foreach (var child in node.Children)
            {
                if (IsBindings(child))
                {
                    UxmlBindings.Apply(asset, element, child);
                    continue;
                }
                var childElement = Create(asset, child, target, cloneTarget, scope);
                if (childElement == null) continue;
                childElement.visualTreeAssetSource = asset;
                if (node.TemplateName != null && child.SlotUsage != null) AddToSlot(element, childElement, child.SlotUsage, scope);
                else element.Add(childElement);
            }
            // An instance's slots are filled: the slots defined so far are not reachable further out.
            if (node.TemplateName != null) scope.Slots.Clear();
            if (node.Attr("content-container") != null && target != null) target.SetContentContainer(element);
            return element;
        }

        /// <summary>A child of a template instance into the slot it names, or (reported) into the
        /// instance when no such slot is defined.</summary>
        static void AddToSlot(VisualElement instance, VisualElement child, string slot, UxmlCloneScope scope)
        {
            if (scope.Slots.TryGetValue(slot, out var slotElement))
            {
                slotElement.Add(child);
                return;
            }
            Debug.LogErrorFormat("Slot '{0}' was not found. Existing slots: {1}", slot, string.Join(", ", scope.Slots.Keys));
            instance.Add(child);
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
            List<KeyValuePair<string, string>> enumAttributes = null, componentAttributes = null;
            foreach (var a in Ordered(node))
            {
                if (!Gather(element, a, ref enumAttributes, ref componentAttributes)) ApplyAttribute(element, a.Key, a.Value, asset: node.SourceOf(a.Key, asset), node: node);
            }
            ApplyEnumFieldAttributes(asset, node, element, enumAttributes);
            ApplyComponentAttributes(element, componentAttributes);
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

        internal static string Describe(VisualTreeAsset asset, UxmlNode node) =>
            $"{(string.IsNullOrEmpty(asset?.m_UaAssetPath) ? asset?.name : asset.m_UaAssetPath)}: <{QualifiedName(node)}>";

        /// <summary>The attributes of a [UxmlElement]: its [UxmlAttribute] members (by name or obsolete
        /// name), the others as on built-in elements (the attributes of its built-in base classes).</summary>
        static void ApplyUxmlAttributes(VisualTreeAsset asset, VisualElement element, UxmlNode node, Type type)
        {
            var members = UxmlTypeIndex.MembersOf(type);
            List<KeyValuePair<string, string>> enumAttributes = null, componentAttributes = null;
            foreach (var a in Ordered(node))
            {
                UxmlMember member = null;
                if (!IsStructural(a.Key) && (members.ByName.TryGetValue(a.Key, out member) || members.ByObsoleteName.TryGetValue(a.Key, out member)))
                {
                    // One of a member's names is read: see PreferredName.
                    if (PreferredName(node, member) != a.Key) continue;
                }
                if (member == null)
                {
                    if (!Gather(element, a, ref enumAttributes, ref componentAttributes)) ApplyAttribute(element, a.Key, a.Value, engineDeclaredOnly: true, asset: node.SourceOf(a.Key, asset), node: node);
                    continue;
                }
                if (member.Problem != null) continue;
                ApplyMember(node.SourceOf(a.Key, asset), node, element, member, a.Key, a.Value);
            }
            ApplyEnumFieldAttributes(asset, node, element, enumAttributes);
            ApplyComponentAttributes(element, componentAttributes);
        }

        /// <summary>One [UxmlAttribute] member of a custom control or UXML object read from its
        /// attribute text and set on <paramref name="target"/>.</summary>
        internal static void ApplyMember(VisualTreeAsset asset, UxmlNode node, object target, UxmlMember member, string key, string text)
        {
            object value;
            if (typeof(Object).IsAssignableFrom(member.ValueType) && UxmlTypeIndex.ConverterFor(member.ValueType) == null)
            {
                if (!TryReadObjectReference(target as VisualElement, key, text, member.ValueType, member.Info.Name, asset, node, out var referenced)) return;
                value = referenced;
            }
            else if (UxmlAttributeValues.IsUnreadBackground(member.ValueType))
            {
                // Measured (Unity 6000.0.71f1, tests/projects/uxmlattributes): a Background or
                // List<Background> member is not read from UXML without a game converter (it
                // keeps its value whatever the reference), and its url is not checked.
                return;
            }
            else if (member.ValueType == typeof(Type))
            {
                value = UxmlAttributeValues.ReadTypeReference(node, key, text,
                    member.Info.GetCustomAttribute<UxmlTypeReferenceAttribute>(false)?.baseType);
            }
            else if (!UxmlAttributeValues.TryReadLenient(text, member.ValueType, member.Info, asset, out value, out var error))
            {
                Debug.LogError($"{Describe(asset, node)} attribute '{key}': {error}; '{member.Info.Name}' keeps its value.");
                return;
            }
            try
            {
                member.Set(target, value);
            }
            catch (TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e);
            }
            catch (ArgumentException e)
            {
                Debug.LogError($"{Describe(asset, node)} attribute '{key}': {e.Message}");
            }
        }

        /// <summary>The name a member is read under when several of its names are present: the
        /// override of best precedence under any of them (it wins over the element's own attribute),
        /// else the current name, else (Unbound's choice) the first obsolete name in declaration order.</summary>
        static string PreferredName(UxmlNode node, UxmlMember member)
        {
            if (node.OverrideSources != null)
            {
                string best = null;
                int bestRank = int.MaxValue;
                void Consider(string candidate)
                {
                    if (!string.IsNullOrEmpty(candidate) && node.OverrideSources.TryGetValue(candidate, out var o) && o.Rank < bestRank)
                        (best, bestRank) = (candidate, o.Rank);
                }
                Consider(member.UxmlName);
                foreach (var obsolete in member.ObsoleteNames) Consider(obsolete);
                if (best != null) return best;
            }
            if (node.Attr(member.UxmlName) != null) return member.UxmlName;
            foreach (var obsolete in member.ObsoleteNames)
                if (!string.IsNullOrEmpty(obsolete) && node.Attr(obsolete) != null) return obsolete;
            return null;
        }

        /// <summary>An element of a game UxmlFactory: the first factory of the tag that accepts the
        /// attributes creates it, its traits reading them.</summary>
        static VisualElement CreateWithFactory(VisualTreeAsset asset, UxmlNode node, List<IUxmlFactory> factories, VisualElement cloneTarget, UxmlCloneScope scope)
        {
            var bag = new UxmlNodeAttributes(node);
            var cc = new CreationContext(scope, asset, cloneTarget);
            IUxmlFactory chosen = null;
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
            // A factory's exception leaves the clone, as in the Unity 6000.0.71f1 Editor
            // (tests/projects/uxmltemplates OldTemplateHost).
            var element = chosen.Create(bag, cc);
            if (element == null)
            {
                Debug.LogError($"{Describe(asset, node)}: {chosen.GetType().FullName}.Create returned no element.");
                return null;
            }
            return element;
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
                    return;
                case "template":
                    // A template container written as an element names its template without cloning it
                    // (Unity 6000.0.71f1, tests/projects/uxmltemplates).
                    if (element is TemplateContainer container) container.SetTemplateId(value);
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
            switch (name)
            {
                case "data-source":
                    if (TryReadObjectReference(element, name, value, typeof(Object), nameof(VisualElement.dataSource), asset, node, out var source))
                        element.dataSource = source;
                    return;
                case "data-source-path":
                    element.dataSourcePath = new PropertyPath(value);
                    return;
                case "data-source-type":
                    element.dataSourceType = UxmlAttributeValues.ReadTypeReference(node, name, value, typeof(object));
                    return;
            }
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
        // will represent an icon within a Button element" / "Sets the icon for the Tab's header";
        // Background.FromTexture2D, FromRenderTexture, FromSprite and FromVectorImage). Measured (Unity
        // 6000.0.71f1, tests/projects/uxmlattributes, on Button, Tab and subclasses of both with a
        // preset icon): the url names an object as an Object member's does (project url, path or relative;
        // a texture file's main asset is its texture, a fileID picks its sprite), and the property is
        // always replaced by the Background made from it: an empty or unresolved url logs the
        // importer's warnings and clears the icon, an object no Background can show (a style sheet, a
        // UXML document, a Cubemap) clears it silently. Unbound's choice: a file the asset database
        // cannot load logs ONE error and keeps the value.
        static void ApplyImageAttribute(VisualElement element, PropertyInfo property, string name, string value, VisualTreeAsset asset, UxmlNode node)
        {
            if (!TryReadObjectReference(element, name, value, typeof(Object), property.Name, asset, node, out var referenced)) return;
            try
            {
                property.SetValue(element, Background.FromObject(referenced));
            }
            catch (TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e);
            }
        }

        // ListView and TreeView `item-template`, a UXML template (VisualTreeAsset) reference. Documented
        // (6000.0 Manual "ListView" and "TreeView", UXML attribute `item-template` of type
        // UIElements.VisualTreeAsset; Scripting API ListView.itemTemplate and TreeView.itemTemplate:
        // "A UXML template that constructs each recycled and rebound element"). Measured (Unity
        // 6000.0.71f1, tests/projects/uxmlattributes): read as a [UxmlAttribute] VisualTreeAsset member
        // is (TryReadObjectReference). Only the attribute's documented name is read, on the property
        // ListView or TreeView declares. The itemTemplate setters make the rows from the template.
        static bool IsItemTemplate(PropertyInfo property, string name) =>
            name == "item-template" && property.PropertyType == typeof(VisualTreeAsset)
            && (property.DeclaringType == typeof(ListView) || property.DeclaringType == typeof(TreeView));

        static void ApplyItemTemplateAttribute(VisualElement element, PropertyInfo property, string name, string value, VisualTreeAsset asset, UxmlNode node)
        {
            if (!TryReadObjectReference(element, name, value, typeof(VisualTreeAsset), property.Name, asset, node, out var template)) return;
            try
            {
                property.SetValue(element, template);
            }
            catch (TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e);
            }
        }

        // An asset url read into an object member of `type`. Measured (Unity 6000.0.71f1,
        // tests/projects/uxmlattributes, [UxmlAttribute] object members and `item-template`): an empty
        // or unresolved url logs the importer's warnings and gives null; an object of another type
        // fails on assignment (the ArgumentException is logged) and the member keeps its value.
        // Unbound's choice: a file the asset database cannot load logs ONE error, value kept.
        internal static bool TryReadObjectReference(VisualElement element, string attribute, string text, Type type, string memberName, VisualTreeAsset asset, UxmlNode node, out Object result)
        {
            switch (UxmlAttributeValues.ResolveReference(text, type, asset, out result, out var failure))
            {
                case UxmlAttributeValues.Reference.Unreadable:
                    Debug.LogError($"{Where(element, asset, node)} attribute '{attribute}': {failure}; '{memberName}' keeps its value.");
                    return false;
                case UxmlAttributeValues.Reference.Empty:
                case UxmlAttributeValues.Reference.Missing:
                    UxmlAttributeValues.ReportUrl(node, attribute, text, asset);
                    result = null;
                    return true;
                case UxmlAttributeValues.Reference.OtherType:
                    Debug.LogException(new ArgumentException($"Object of type '{result.GetType().FullName}' cannot be converted to type '{type.FullName}'."));
                    return false;
                default:
                    return true;
            }
        }

        static string Where(VisualElement element, VisualTreeAsset asset, UxmlNode node) =>
            node != null ? Describe(asset, node) : $"{(string.IsNullOrEmpty(asset?.m_UaAssetPath) ? asset?.name : asset.m_UaAssetPath)}: <{element.GetType().FullName}>";

        static readonly Dictionary<(Type, string, bool), PropertyInfo> s_Properties = new Dictionary<(Type, string, bool), PropertyInfo>();

        /// <summary>Attributes of engine elements whose UXML name is not their property's name in
        /// kebab case (matches the Unity 6000.0.71f1 Editor, tests/projects/numericfields): the text
        /// inputs read "readonly", "password" and "mask-character" (also under its former name
        /// "maskCharacter") but not their property names, and
        /// the numeric text fields take no "format-string"; text elements read their internal selection
        /// options (tests/projects/uxmllegacy). A null property ignores the attribute.</summary>
        static readonly (Type declaringType, string attribute, string property)[] s_AttributeNames =
        {
            (typeof(TextInputBaseField<>), "readonly", "isReadOnly"),
            (typeof(TextInputBaseField<>), "is-read-only", null),
            (typeof(TextInputBaseField<>), "password", "isPasswordField"),
            (typeof(TextInputBaseField<>), "is-password-field", null),
            (typeof(TextInputBaseField<>), "mask-character", "maskChar"),
            (typeof(TextInputBaseField<>), "maskCharacter", "maskChar"),
            (typeof(TextInputBaseField<>), "mask-char", null),
            (typeof(TextValueField<>), "format-string", null),
            // Text inputs' placeholder (internal properties) and selection options.
            (typeof(TextInputBaseField<>), "placeholder-text", "placeholderText"),
            (typeof(TextInputBaseField<>), "hide-placeholder-on-focus", "hidePlaceholderOnFocus"),
            (typeof(TextInputBaseField<>), "select-word-by-double-click", "doubleClickSelectsWord"),
            (typeof(TextInputBaseField<>), "select-line-by-triple-click", "tripleClickSelectsLine"),
            // Text elements' selection options (internal properties) under their names and former names.
            (typeof(TextElement), "selectable", "isSelectable"),
            (typeof(TextElement), "double-click-selects-word", "doubleClickSelectsWord"),
            (typeof(TextElement), "selectWordByDoubleClick", "doubleClickSelectsWord"),
            (typeof(TextElement), "select-word-by-double-click", "doubleClickSelectsWord"),
            (typeof(TextElement), "triple-click-selects-line", "tripleClickSelectsLine"),
            (typeof(TextElement), "selectLineByTripleClick", "tripleClickSelectsLine"),
            (typeof(TextElement), "select-line-by-triple-click", "tripleClickSelectsLine"),
        };

        /// <summary>The property an attribute names on <paramref name="type"/>: its entry in
        /// <see cref="s_AttributeNames"/> when the type derives from the entry's declaring type (such
        /// a property may be internal, <paramref name="listed"/>), else the attribute in camel case.</summary>
        static string PropertyNameFor(Type type, string attribute, out bool listed)
        {
            listed = true;
            foreach (var (declaringType, name, property) in s_AttributeNames)
            {
                if (name != attribute) continue;
                for (var t = type; t != null; t = t.BaseType)
                    if (t == declaringType || t.IsGenericType && t.GetGenericTypeDefinition() == declaringType) return property;
            }
            listed = false;
            return KebabToCamel(attribute);
        }

        static PropertyInfo FindProperty(Type type, string attribute, bool engineDeclaredOnly)
        {
            if (s_Properties.TryGetValue((type, attribute, engineDeclaredOnly), out var cached)) return cached;
            string camel = PropertyNameFor(type, attribute, out bool listed);
            if (camel == null)
            {
                s_Properties[(type, attribute, engineDeclaredOnly)] = null;
                return null;
            }
            PropertyInfo found = null;
            for (var t = type; t != null && found == null; t = t.BaseType)
            {
                if (engineDeclaredOnly && !UxmlTypeIndex.IsEngineType(t)) continue;
                var visibility = listed ? BindingFlags.Public | BindingFlags.NonPublic : BindingFlags.Public;
                foreach (var p in t.GetProperties(visibility | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (string.Equals(p.Name, camel, StringComparison.OrdinalIgnoreCase) && p.CanWrite && p.GetSetMethod(listed) != null && p.GetIndexParameters().Length == 0)
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
            if (type == typeof(char))
            {
                // A character attribute takes the text's first character.
                if (string.IsNullOrEmpty(value)) return false;
                result = value[0];
                return true;
            }
            if (type == typeof(ulong))
            {
                if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ul)) { result = ul; return true; }
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
            // Vectors, rects, bounds and hashes read as the UXML attributes of game elements do.
            if (IsCompositeValue(type) || type == typeof(Hash128))
                return UxmlAttributeValues.TryRead(value, type, null, null, out result, out _);
            return false;
        }

        /// <summary>Attributes applied together once the element's others are applied: EnumField's
        /// initialization, and a composite field's per-component values.</summary>
        static bool Gather(VisualElement element, KeyValuePair<string, string> a, ref List<KeyValuePair<string, string>> enumAttributes,
            ref List<KeyValuePair<string, string>> componentAttributes)
        {
            if (IsEnumFieldAttribute(element, a.Key)) (enumAttributes ??= new List<KeyValuePair<string, string>>()).Add(a);
            else if (ComponentFieldOf(element.GetType()) is { } field && Array.IndexOf(field.names, a.Key) >= 0) (componentAttributes ??= new List<KeyValuePair<string, string>>()).Add(a);
            else return false;
            return true;
        }

        /// <summary>The per-component attributes the composite fields also read, an older UXML form
        /// (matches the Unity 6000.0.71f1 Editor, tests/projects/compositefields): the components
        /// given make the whole value, the others 0, replacing a `value` attribute; bounds take a
        /// center and a size. Fields deriving from these read them too; other fields holding the same
        /// value types (MinMaxSlider) do not.</summary>
        static readonly (Type field, Type value, string[] names)[] s_ComponentFields =
        {
            (typeof(Vector2Field), typeof(Vector2), new[] { "x", "y" }),
            (typeof(Vector3Field), typeof(Vector3), new[] { "x", "y", "z" }),
            (typeof(Vector4Field), typeof(Vector4), new[] { "x", "y", "z", "w" }),
            (typeof(RectField), typeof(Rect), new[] { "x", "y", "w", "h" }),
            (typeof(BoundsField), typeof(Bounds), new[] { "cx", "cy", "cz", "ex", "ey", "ez" }),
            (typeof(Vector2IntField), typeof(Vector2Int), new[] { "x", "y" }),
            (typeof(Vector3IntField), typeof(Vector3Int), new[] { "x", "y", "z" }),
            (typeof(RectIntField), typeof(RectInt), new[] { "x", "y", "w", "h" }),
            (typeof(BoundsIntField), typeof(BoundsInt), new[] { "px", "py", "pz", "sx", "sy", "sz" }),
        };

        static readonly Dictionary<Type, int> s_ComponentFieldIndex = new Dictionary<Type, int>();

        /// <summary>The composite field an element type is (or derives from); null for other types.</summary>
        static (Type field, Type value, string[] names)? ComponentFieldOf(Type type)
        {
            if (!s_ComponentFieldIndex.TryGetValue(type, out int index))
            {
                index = Array.FindIndex(s_ComponentFields, entry => entry.field.IsAssignableFrom(type));
                s_ComponentFieldIndex[type] = index;
            }
            return index < 0 ? null : s_ComponentFields[index];
        }

        /// <summary>Whether a value type is one the composite fields hold (read as UXML text by the
        /// shared attribute reader).</summary>
        static bool IsCompositeValue(Type type) => Array.Exists(s_ComponentFields, entry => entry.value == type);

        static void ApplyComponentAttributes(VisualElement element, List<KeyValuePair<string, string>> attributes)
        {
            if (attributes == null) return;
            var (_, valueType, names) = ComponentFieldOf(element.GetType()).Value;
            bool integral = valueType == typeof(Vector2Int) || valueType == typeof(Vector3Int) || valueType == typeof(RectInt) || valueType == typeof(BoundsInt);
            var c = new double[names.Length];
            foreach (var a in attributes)
            {
                // A component that does not read stays 0.
                int i = Array.IndexOf(names, a.Key);
                if (integral) c[i] = int.TryParse(a.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
                else c[i] = float.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0;
            }
            object value;
            float F(int i) => (float)c[i];
            int I(int i) => (int)c[i];
            if (valueType == typeof(Vector2)) value = new Vector2(F(0), F(1));
            else if (valueType == typeof(Vector3)) value = new Vector3(F(0), F(1), F(2));
            else if (valueType == typeof(Vector4)) value = new Vector4(F(0), F(1), F(2), F(3));
            else if (valueType == typeof(Rect)) value = new Rect(F(0), F(1), F(2), F(3));
            else if (valueType == typeof(Bounds)) value = new Bounds(new Vector3(F(0), F(1), F(2)), new Vector3(F(3), F(4), F(5)));
            else if (valueType == typeof(Vector2Int)) value = new Vector2Int(I(0), I(1));
            else if (valueType == typeof(Vector3Int)) value = new Vector3Int(I(0), I(1), I(2));
            else if (valueType == typeof(RectInt)) value = new RectInt(I(0), I(1), I(2), I(3));
            else value = new BoundsInt(I(0), I(1), I(2), I(3), I(4), I(5));
            element.GetType().GetProperty("value", BindingFlags.Public | BindingFlags.Instance, null, valueType, Type.EmptyTypes, null)?.SetValue(element, value);
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
