#pragma warning disable CS0618 // UxmlFactory and UxmlTraits are deprecated in Unity 6 and still supported
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Unbound.Runtime;
using Unbound.UIElements;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UxmlControlProbes;
using Xunit;
using Object = UnityEngine.Object;

// Custom controls read from UXML. Each test names its source:
//   [doc]    a guarantee or example of the Unity 6000.0 Manual / Scripting API pages "Create a
//            custom control", "Configure the custom control name", "Define UXML attributes for
//            built-in types", "Define custom control attributes for complex data types", "Customize
//            UXML attributes", "Migrate custom controls from an earlier version to Unity 6", and
//            UxmlElementAttribute, UxmlAttributeAttribute, UxmlAttributeConverter<T>,
//            UxmlFactory<T0,T1>, BaseUxmlFactory<T0,T1>, UxmlAttributeDescription, CreationContext;
//   [choice] Unbound's choice where Unity's pages say nothing (text forms, precedence, how a
//            failure is reported). These check Unbound's implementation, not Unity behaviour.
// No reference capture covers custom controls; nothing here is a measured Unity value.

namespace UnityEngine.Tests
{
    [Collection("UXML custom controls")]
    public class UxmlCustomControlAttributeTests
    {
        const string Path = "Assets/UI/UxmlControlProbe.uxml";

        static (VisualElement Root, VisualTreeAsset Asset) Clone(string body)
        {
            var asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            asset.name = "UxmlControlProbe";
            UxmlImporter.Import(Path, "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" + body + "</ui:UXML>", asset);
            var root = new VisualElement();
            asset.CloneTree(root);
            return (root, asset);
        }

        /// <summary>Warnings and errors logged while <paramref name="action"/> runs that mention
        /// <paramref name="fragment"/> (other tests may log at the same time).</summary>
        static List<string> Logged(Action action, string fragment)
        {
            var logs = new List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type != LogType.Log && message != null && message.Contains(fragment)) lock (logs) logs.Add(message);
            };
            Application.logMessageReceived += capture;
            try { action(); }
            finally { Application.logMessageReceived -= capture; }
            return logs;
        }

        sealed class Bag : IUxmlAttributes
        {
            readonly Dictionary<string, string> m_Values;
            internal Bag(params (string Name, string Value)[] values) => m_Values = values.ToDictionary(v => v.Name, v => v.Value);
            public bool TryGetAttributeValue(string attributeName, out string value) => m_Values.TryGetValue(attributeName, out value);
        }

        // ------------------------------------------------------------------ [UxmlElement] naming

        // [doc] UxmlAttributeAttribute: "the field or property name splits into lowercase words
        // connected by hyphens"; fields and properties are both allowed targets.
        // Red on b988: the builder only sets public properties, so the four fields stay at their
        // defaults (the first Assert.Equal fails with null).
        [Fact]
        public void DefaultNamesSplitFieldNamesAtUppercaseLetters()
        {
            var (root, _) = Clone("<UxmlControlProbes.FieldProbe my-string-value=\"Hello World\" my-int-value=\"123\" my-float-value=\"3.14\" my-pascal=\"P\" />");
            var probe = Assert.IsType<FieldProbe>(root[0]);
            Assert.Equal("Hello World", probe.myStringValue);
            Assert.Equal(123, probe.myIntValue);
            Assert.Equal(3.14f, probe.myFloatValue);
            Assert.Equal("P", probe.MyPascal);
        }

        // [doc] "Customize UXML attributes": [UxmlAttribute("character-name")] names the attribute.
        // Red on b988: "character-name" maps to no property named characterName; the values stay null/0.
        [Fact]
        public void CustomNamesAreRead()
        {
            var (root, _) = Clone("<UxmlControlProbes.CustomNameProbe character-name=\"Karl\" credits=\"1.23\" level=\"1\" usage=\"DynamicColor\" />");
            var probe = Assert.IsType<CustomNameProbe>(root[0]);
            Assert.Equal("Karl", probe.myStringValue);
            Assert.Equal(1.23f, probe.myFloatValue);
            Assert.Equal(1, probe.myIntValue);
            Assert.Equal(UsageHints.DynamicColor, probe.myEnumValue);
        }

        // [doc] UxmlAttributeAttribute obsoleteNames: UXML with the old names still loads (the
        // documented CharacterDetails example, its members renamed so that no property is named
        // after an old attribute).
        // Red on b988: npc-name and health map to no property; characterLabel stays null.
        [Fact]
        public void ObsoleteNamesAreRead()
        {
            var (root, _) = Clone("<UxmlControlProbes.CharacterDetails npc-name=\"Haydee\" health=\"100\" />");
            var probe = Assert.IsType<CharacterDetails>(root[0]);
            Assert.Equal("Haydee", probe.characterLabel);
            Assert.Equal(100f, probe.hitPoints);
        }

        // [choice] With both names present the current name wins (Unity's pages do not say).
        // Red on b988: neither name is read; characterLabel stays null.
        [Fact]
        public void CurrentNameWinsOverObsoleteName()
        {
            var (root, _) = Clone("<UxmlControlProbes.CharacterDetails npc-name=\"Old\" character-name=\"New\" />");
            Assert.Equal("New", Assert.IsType<CharacterDetails>(root[0]).characterLabel);
        }

        // [doc] "When an attribute shares the same UXML name as an attribute from a child class, it
        // takes precedence and overrides that field" (example: an internal property over "value").
        // Red on b988: the internal property and the base field are not found; Overridden stays 0.
        [Fact]
        public void RedeclaredAttributeTakesPrecedence()
        {
            var (root, _) = Clone("<UxmlControlProbes.DerivedValueProbe value=\"7\" />");
            var probe = Assert.IsType<DerivedValueProbe>(root[0]);
            Assert.Equal(7, probe.Overridden);
            Assert.Equal(0, probe.value);
        }

        // [doc] UxmlElementAttribute: with a custom name the element is found under the custom name
        // and under its class name, in its namespace.
        // Red on b988: the custom tag resolves to no type and becomes the red "Unknown type" Label.
        [Fact]
        public void CustomElementNameAndClassNameBothCreateTheElement()
        {
            var (root, _) = Clone("<UxmlControlProbes.UxmlProbeCustomTag name=\"a\" /><UxmlControlProbes.CustomTagElement name=\"b\" />");
            Assert.Equal(2, root.childCount);
            Assert.IsType<CustomTagElement>(root[0]);
            Assert.Equal("a", root[0].name);
            Assert.IsType<CustomTagElement>(root[1]);
        }

        // [doc] "The tag name must be unique". [choice] A duplicated name is reported and creates
        // nothing of either type (the Unknown element, as for unknown tags).
        // Red on b988: no error mentions the duplicate (the tag is reported as unknown only).
        [Fact]
        public void DuplicatedCustomElementNamesFailLoudly()
        {
            VisualElement root = null;
            var logs = Logged(() => root = Clone("<UxmlControlProbes.UxmlProbeDuplicate />").Root, "UxmlProbeDuplicate");
            Assert.Contains(logs, l => l.Contains("more than one [UxmlElement]"));
            Assert.IsType<Label>(root[0]);
        }

        // [doc] Custom controls are declared with [UxmlElement] (or, without it, created by a
        // UxmlFactory). [choice] Other game types are treated as unknown tags, with a hint.
        // Red on b988: the undeclared type is created by reflection, not reported.
        [Fact]
        public void UndeclaredGameTypesAreNotCreated()
        {
            VisualElement root = null;
            var logs = Logged(() => root = Clone("<UxmlControlProbes.UndeclaredProbe />").Root, "UndeclaredProbe");
            Assert.IsType<Label>(root[0]);
            Assert.Contains(logs, l => l.Contains("has no registered factory method") && l.Contains("[UxmlElement]"));
        }

        // ------------------------------------------------------------------ value types

        // [doc] The UxmlElementAttribute example: List<int> "1,2,3", Color "#FF0000FF", Type by
        // assembly-qualified name restricted by UxmlTypeReference(typeof(VisualElement)).
        // Red on b988: fields are never set; the list stays null.
        [Fact]
        public void DocumentedBuiltInValueForms()
        {
            var (root, _) = Clone("<UxmlControlProbes.ValueProbe my-list-of-ints=\"1,2,3\" my-color=\"#FF0000FF\""
                + " my-type=\"UnityEngine.UIElements.Button, UnityEngine.UIElementsModule\" />");
            var probe = Assert.IsType<ValueProbe>(root[0]);
            Assert.Equal(new List<int> { 1, 2, 3 }, probe.myListOfInts);
            Assert.Equal(Color.red, probe.myColor);
            Assert.Equal(typeof(Button), probe.myType);
        }

        // [doc] bool, char, byte, short, ulong, double, enum and Hash128 are built-in types.
        // [choice] Their text forms: invariant numbers, true/false, one character, enum names,
        // 32 hex digits; vectors, rects and bounds as comma-separated components.
        // Red on b988: none of the fields is set (myBool stays false).
        [Fact]
        public void OtherBuiltInValueTypes()
        {
            var (root, _) = Clone("<UxmlControlProbes.ValueProbe my-bool=\"true\" my-char=\"x\" my-byte=\"200\" my-short=\"-5\""
                + " my-ulong=\"18446744073709551615\" my-double=\"0.25\" my-hints=\"MaskContainer\""
                + " my-hash=\"0123456789abcdef0123456789abcdef\" my-vector=\"1,2.5,-3\" my-rect-int=\"1,2,3,4\" my-bounds=\"0,0,0,2,4,6\" />");
            var probe = Assert.IsType<ValueProbe>(root[0]);
            Assert.True(probe.myBool);
            Assert.Equal('x', probe.myChar);
            Assert.Equal((byte)200, probe.myByte);
            Assert.Equal((short)-5, probe.myShort);
            Assert.Equal(ulong.MaxValue, probe.myUlong);
            Assert.Equal(0.25, probe.myDouble);
            Assert.Equal(UsageHints.MaskContainer, probe.myHints);
            Assert.Equal(Hash128.Parse("0123456789abcdef0123456789abcdef"), probe.myHash);
            Assert.Equal(new Vector3(1, 2.5f, -3), probe.myVector);
            Assert.Equal(new RectInt(1, 2, 3, 4), probe.myRectInt);
            Assert.Equal(new Bounds(Vector3.zero, new Vector3(2, 4, 6)), probe.myBounds);
        }

        // [doc] UxmlTypeReference restricts the allowed base type. Measured
        // (tests/projects/uxmlattributes): a type outside it logs Unity's error and gives null.
        [Fact]
        public void TypeOutsideTheReferenceBaseFailsLoudly()
        {
            VisualElement root = null;
            var logs = Logged(() => root = Clone("<UxmlControlProbes.ValueProbe my-type=\"System.String\" />").Root, "Invalid type");
            Assert.Null(Assert.IsType<ValueProbe>(root[0]).myType);
            Assert.Contains(logs, l => l.StartsWith("Type: Invalid type \"System.String\". Type must derive from", StringComparison.Ordinal));
        }

        // Measured (tests/projects/uxmlattributes): a value that does not convert silently gives the
        // type's default (0 here, not the field's -1), an item of a list its default, a colour white.
        [Fact]
        public void UnreadableValuesSilentlyTakeTheTypesDefault()
        {
            VisualElement root = null;
            var logs = Logged(() => root = Clone("<UxmlControlProbes.FieldProbe my-int-value=\"abc\" />"
                + "<UxmlControlProbes.ValueProbe my-list-of-ints=\"1,x,3\" my-color=\"rgb(0,128,255)\" />").Root, "my-");
            Assert.Equal(0, Assert.IsType<FieldProbe>(root[0]).myIntValue);
            var probe = Assert.IsType<ValueProbe>(root[1]);
            Assert.Equal(new List<int> { 1, 0, 3 }, probe.myListOfInts);
            Assert.Equal(Color.white, probe.myColor);
            Assert.Empty(logs);
        }

        // Measured: colours take HTML forms, including short hex and names.
        [Fact]
        public void ColoursTakeHtmlForms()
        {
            var (root, _) = Clone("<UxmlControlProbes.ValueProbe my-color=\"#F00\" /><UxmlControlProbes.ValueProbe my-color=\"blue\" />");
            Assert.Equal(Color.red, Assert.IsType<ValueProbe>(root[0]).myColor);
            Assert.Equal(Color.blue, Assert.IsType<ValueProbe>(root[1]).myColor);
        }

        // [doc] UxmlAttributeConverter<T> example: "Michael Scott:58:USA" and a comma-separated list
        // of converted items.
        // Red on b988: converters are never consulted; manager stays null.
        [Fact]
        public void ConvertersReadCustomTypesAndListsOfThem()
        {
            var (root, _) = Clone("<UxmlControlProbes.Department manager=\"Michael Scott:58:USA\" employees=\"Dwight Schrute:53:USA,Jim Halpert:44:USA\" />");
            var department = Assert.IsType<Department>(root[0]);
            Assert.Equal("Michael Scott", department.manager.name);
            Assert.Equal(58, department.manager.age);
            Assert.Equal(new[] { "Dwight Schrute", "Jim Halpert" }, department.employees.Select(p => p.name));
            Assert.Equal(44, department.employees[1].age);
        }

        // [doc] "You can also use generic attribute converters, but you must declare the attribute
        // with the generic type."
        // Red on b988: the field stays null.
        [Fact]
        public void GenericConvertersServeTheirConstructedTypes()
        {
            var (root, _) = Clone("<UxmlControlProbes.PairProbe numbers=\"3|4\" />");
            var probe = Assert.IsType<PairProbe>(root[0]);
            Assert.Equal(3, probe.numbers.First);
            Assert.Equal(4, probe.numbers.Second);
        }

        // [doc] Without a converter: "[UxmlElement] 'Department' has a [UxmlAttribute] 'manager' of
        // an unknown type 'Person'. To fix this error, define a custom UxmlAttributeConverter<Person>."
        // The control's other attributes are still applied.
        // Red on b988: no error is logged and known is not set.
        [Fact]
        public void TypesWithoutConverterReportTheDocumentedError()
        {
            VisualElement root = null;
            var logs = Logged(() => root = Clone("<UxmlControlProbes.NoConverterProbe data=\"x\" known=\"5\" />").Root, "NoConverterProbe");
            Assert.Contains(logs, l => l.Contains("[UxmlElement] 'NoConverterProbe' has a [UxmlAttribute] 'data' of an unknown type 'Unconverted'. To fix this error, define a custom UxmlAttributeConverter<Unconverted>."));
            Assert.Equal(5, Assert.IsType<NoConverterProbe>(root[0]).known);
        }

        // [doc] AnimationCurve is a built-in attribute type. [choice] Unbound does not read its UXML
        // text yet and says so instead of leaving it silently unset.
        // Red on b988: nothing is reported.
        [Fact]
        public void BuiltInTypesNotReadYetFailLoudly()
        {
            var logs = Logged(() => Clone("<UxmlControlProbes.CurveProbe curve=\"x\" />"), "CurveProbe");
            Assert.Contains(logs, l => l.Contains("'curve'") && l.Contains("does not read from UXML yet"));
        }

        // [doc] Unity objects are attributes holding a reference to the asset file. [choice] The
        // `project://database/...?fileID=..&guid=..` form resolves through Unbound's asset urls without
        // a project; an asset that cannot be found is reported.
        // Red on b988: the texture field is never set.
        [Fact]
        public void AssetReferencesResolveByUrl()
        {
            var texture = new Texture2D(2, 2) { name = "probe-texture", IsPersistentAsset = true };
            var file = new LoadedFile { Guid = "4c1f0e5e8f5d4a2b9c7d6e5f4a3b2c1d", Path = "Assets/UI/ProbeTexture.png", MainObject = texture };
            file.Objects[2800000] = texture;
            AssetDatabaseRuntime.AddLoaded(file);
            try
            {
                var (root, _) = Clone("<UxmlControlProbes.AssetProbe my-texture=\"project://database/Assets/UI/ProbeTexture.png?fileID=2800000&amp;guid=4c1f0e5e8f5d4a2b9c7d6e5f4a3b2c1d&amp;type=3#ProbeTexture\" />");
                Assert.Same(texture, Assert.IsType<AssetProbe>(root[0]).myTexture);
                VisualElement missing = null;
                // Measured: an unresolved reference logs the importer's warnings and gives null.
                var logs = Logged(() => missing = Clone("<UxmlControlProbes.AssetProbe my-texture=\"project://database/Assets/UI/Missing.png?fileID=2800000&amp;guid=9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f&amp;type=3\" />").Root, "Semantic");
                Assert.Null(Assert.IsType<AssetProbe>(missing[0]).myTexture);
                Assert.Contains(logs, l => l.EndsWith("Invalid asset path hint \"Assets/UI/Missing.png\" for referenced asset GUID \"9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f\"", StringComparison.Ordinal));
                // A reference to an object of another type throws on assignment (logged) and the
                // member keeps its value.
                var sheet = ScriptableObject.CreateInstance<StyleSheet>();
                sheet.name = "probe-sheet";
                var sheetFile = new LoadedFile { Guid = "5d2a1f6e9a6e5b3cad8e7f6a5b4c3d2e", Path = "Assets/UI/Probe.uss", MainObject = sheet };
                sheetFile.Objects[7433441132597879392] = sheet;
                AssetDatabaseRuntime.AddLoaded(sheetFile);
                try
                {
                    var thrown = Logged(() => Clone("<UxmlControlProbes.AssetProbe my-texture=\"project://database/Assets/UI/Probe.uss?fileID=7433441132597879392&amp;guid=5d2a1f6e9a6e5b3cad8e7f6a5b4c3d2e&amp;type=3\" />"), "cannot be converted");
                    Assert.Contains(thrown, l => l.Contains("Object of type 'UnityEngine.UIElements.StyleSheet' cannot be converted to type 'UnityEngine.Texture2D'."));
                }
                finally
                {
                    AssetDatabaseRuntime.Invalidate(sheetFile.Guid);
                    Object.DestroyImmediate(sheet, true);
                }
            }
            finally
            {
                AssetDatabaseRuntime.Invalidate(file.Guid);
                Object.DestroyImmediate(texture, true);
            }
        }

        // ------------------------------------------------------------------ UxmlFactory / UxmlTraits

        // [doc] Migration guide example: the traits' Init reads the descriptions with their defaults
        // (high-value defaults to 100), UxmlFactory.Create calls Init, base.Init applies the element
        // name; CreationContext gives the cloned asset and the target.
        // Red on b988: the factory is never used, so the absent high-value stays 0 and Init never runs.
        [Fact]
        public void LegacyFactoriesCreateTheElementThroughTheirTraits()
        {
            LegacyBar.Contexts.Clear();
            var (root, asset) = Clone("<UxmlControlProbes.LegacyBar name=\"bar\" title=\"My Progress bar\" low-value=\"0\" high-value=\"1\" value=\"0.5\" />"
                + "<UxmlControlProbes.LegacyBar old-title=\"renamed\" />");
            var bar = Assert.IsType<LegacyBar>(root[0]);
            Assert.Equal("bar", bar.name);
            Assert.Equal("My Progress bar", bar.title);
            Assert.Equal(1f, bar.highValue);
            Assert.Equal(0.5f, bar.value);
            var defaults = Assert.IsType<LegacyBar>(root[1]);
            Assert.Equal(100f, defaults.highValue);
            Assert.Equal("renamed", defaults.title);
            Assert.Equal(2, LegacyBar.Contexts.Count);
            Assert.Same(asset, LegacyBar.Contexts[0].visualTreeAsset);
            Assert.Same(root, LegacyBar.Contexts[0].target);
        }

        // [choice] Traits deriving from a built-in control's traits whose Init Unbound has not
        // written: the attributes the game's traits do not describe are applied as on built-in
        // elements, so the base's `text` still takes effect.
        // Red on b988: the factory is never used; extra stays null.
        [Fact]
        public void LegacyTraitsOverUnwrittenBuiltInTraitsKeepTheBuiltInAttributes()
        {
            var (root, _) = Clone("<UxmlControlProbes.LegacyButton text=\"Hi\" extra=\"E\" />");
            var button = Assert.IsType<LegacyButton>(root[0]);
            Assert.Equal("E", button.extra);
            Assert.Equal("Hi", button.text);
        }

        // [doc] UxmlAttributeAttribute (6000.0): "Declares that a field or property is associated
        // with a UXML attribute." Unbound reads this declaration model as: a game's own public
        // property without [UxmlAttribute] is not a UXML attribute of the control, so the
        // attribute leaves it untouched, for an [UxmlElement] and for a UxmlFactory over unwritten
        // built-in traits alike; built-in base attributes (`name`, `text`) still apply.
        // Red at 7e25f621: the undeclared-attribute fallback matches Card's own public `title` and
        // sets it to "x" (the second Assert.Equal fails with "x").
        // Red on b988: the same (every matching public property was set).
        [Fact]
        public void UndeclaredAttributesLeaveGamePropertiesUntouched()
        {
            var (root, _) = Clone("<UxmlControlProbes.Card name=\"c\" title=\"x\" />"
                + "<UxmlControlProbes.CardButton text=\"Hi\" caption=\"x\" />");
            var card = Assert.IsType<Card>(root[0]);
            Assert.Equal("c", card.name);
            Assert.Equal("initial", card.title);
            var button = Assert.IsType<CardButton>(root[1]);
            Assert.Equal("Hi", button.text);
            Assert.Equal("initial", button.caption);
        }

        // [doc] UxmlAttributeDescription.GetValueFromBag returns the default when the attribute is
        // absent; TryGetValueFromBag returns whether it was found; obsoleteNames are names it had.
        // Red on b988: the descriptions are stubs returning default(int).
        [Fact]
        public void TypedDescriptionsReadTheirBag()
        {
            var description = new UxmlIntAttributeDescription { name = "count", defaultValue = 9, obsoleteNames = new[] { "old-count" } };
            Assert.Equal(9, description.GetValueFromBag(new Bag(), CreationContext.Default));
            Assert.Equal(4, description.GetValueFromBag(new Bag(("count", "4")), CreationContext.Default));
            Assert.Equal(6, description.GetValueFromBag(new Bag(("old-count", "6")), CreationContext.Default));
            int value = 1;
            Assert.False(description.TryGetValueFromBag(new Bag(), CreationContext.Default, ref value));
            Assert.True(description.TryGetValueFromBag(new Bag(("count", "12")), CreationContext.Default, ref value));
            Assert.Equal(12, value);
            var hints = new UxmlEnumAttributeDescription<UsageHints> { name = "hints" };
            Assert.Equal(UsageHints.DynamicTransform, hints.GetValueFromBag(new Bag(("hints", "DynamicTransform")), CreationContext.Default));
            var text = new UxmlStringAttributeDescription { name = "label" };
            Assert.Equal("read", text.GetValueFromBag(new Bag(("label", "read")), CreationContext.Default));
        }

        // Measured: tests/projects/uxmlbaseapi, Unity 6000.0.71f1. Factories expose traits'
        // metadata, and derived elements substitute for VisualElement, whose own factory has no substitution.
        [Fact]
        public void FactoryAndTraitsMetadata()
        {
            var factory = new LegacyBar.UxmlFactory();
            Assert.Equal(nameof(LegacyBar), factory.uxmlName);
            Assert.Equal("UxmlControlProbes", factory.uxmlNamespace);
            Assert.Equal("UxmlControlProbes.LegacyBar", factory.uxmlQualifiedName);
            Assert.Equal(typeof(LegacyBar), factory.uxmlType);
            Assert.True(factory.AcceptsAttributeBag(new Bag(), CreationContext.Default));
            Assert.Equal("VisualElement", factory.substituteForTypeName);
            Assert.Equal("UnityEngine.UIElements", factory.substituteForTypeNamespace);
            Assert.Equal("UnityEngine.UIElements.VisualElement", factory.substituteForTypeQualifiedName);
            var exact = new VisualElement.UxmlFactory();
            Assert.Equal("", exact.substituteForTypeName);
            Assert.Equal("", exact.substituteForTypeNamespace);
            Assert.Equal("", exact.substituteForTypeQualifiedName);
            var child = Assert.Single(new VisualElement.UxmlTraits().uxmlChildElementsDescription);
            Assert.Equal("VisualElement", child.elementName);
            Assert.Equal("UnityEngine.UIElements", child.elementNamespace);
            Assert.Equal(default(CreationContext), CreationContext.Default);
        }

        public sealed class MetadataTraits : UnityEngine.UIElements.UxmlTraits
        {
            readonly UxmlStringAttributeDescription value = new UxmlStringAttributeDescription { name = "probe-value" };
            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get { yield return new UxmlChildElementDescription(typeof(Label)); }
            }
            public override void Init(VisualElement element, IUxmlAttributes bag, CreationContext context)
            {
                base.Init(element, bag, context);
                element.name = value.GetValueFromBag(bag, context);
            }
        }

        public sealed class EmptyMetadataTraits : UnityEngine.UIElements.UxmlTraits { }

        sealed class SelectingFactory : UxmlFactory<VisualElement, MetadataTraits>
        {
            public override bool AcceptsAttributeBag(IUxmlAttributes bag, CreationContext context) =>
                bag != null && bag.TryGetAttributeValue("probe-value", out var text) && text == "accepted";
        }

        [Fact]
        public void FactoriesExposeTheirCustomTraitsSchemaAndInitialization()
        {
            var factory = new UxmlFactory<VisualElement, MetadataTraits>();
            Assert.Equal("probe-value", Assert.Single(factory.uxmlAttributesDescription).name);
            var child = Assert.Single(factory.uxmlChildElementsDescription);
            Assert.Equal("Label", child.elementName);
            Assert.Equal("UnityEngine.UIElements", child.elementNamespace);
            Assert.Equal("accepted", factory.Create(new Bag(("probe-value", "accepted")), default).name);
            Assert.NotEmpty(new VisualElement.UxmlFactory().uxmlAttributesDescription);
            Assert.Single(new VisualElement.UxmlFactory().uxmlChildElementsDescription);
        }

        [Fact]
        public void BaseDefaultsLeaveElementsUntouchedAndDoNotBypassFactoryOverrides()
        {
            var factory = new UxmlFactory<VisualElement, EmptyMetadataTraits>();
            Assert.True(factory.AcceptsAttributeBag(null, default));
            Assert.True(factory.AcceptsAttributeBag(new Bag(), default));
            Assert.Empty(factory.uxmlAttributesDescription);
            Assert.Empty(factory.uxmlChildElementsDescription);
            var element = new VisualElement { name = "keep", tooltip = "keep-tooltip" };
            new EmptyMetadataTraits().Init(element, new Bag(("name", "ignored")), default);
            Assert.Equal("keep", element.name);
            Assert.Equal("keep-tooltip", element.tooltip);
            Assert.Equal(0, element.childCount);
            new EmptyMetadataTraits().Init(null, null, default);
            var selecting = new SelectingFactory();
            Assert.True(selecting.AcceptsAttributeBag(new Bag(("probe-value", "accepted")), default));
            Assert.False(selecting.AcceptsAttributeBag(new Bag(("probe-value", "rejected")), default));
        }
    }
}

namespace UxmlControlProbes
{
    [UxmlElement]
    public partial class FieldProbe : VisualElement
    {
        [UxmlAttribute] public string myStringValue;
        [UxmlAttribute] public int myIntValue = -1;
        [UxmlAttribute] public float myFloatValue;
        [UxmlAttribute] public string MyPascal;
    }

    [UxmlElement]
    public partial class CustomNameProbe : VisualElement
    {
        [UxmlAttribute("character-name")] public string myStringValue { get; set; }
        [UxmlAttribute("credits")] public float myFloatValue { get; set; }
        [UxmlAttribute("level")] public int myIntValue { get; set; }
        [UxmlAttribute("usage")] public UsageHints myEnumValue { get; set; }
    }

    [UxmlElement]
    public partial class CharacterDetails : VisualElement
    {
        [UxmlAttribute("character-name", "npc-name")] public string characterLabel { get; set; }
        [UxmlAttribute("character-health", "health")] public float hitPoints { get; set; }
    }

    [UxmlElement]
    public partial class BaseValueProbe : VisualElement
    {
        [UxmlAttribute] public int value;
    }

    [UxmlElement]
    public partial class DerivedValueProbe : BaseValueProbe
    {
        public int Overridden;

        [UxmlAttribute("value")]
        internal int valueOverride
        {
            get => Overridden;
            set => Overridden = value;
        }
    }

    [UxmlElement("UxmlProbeCustomTag")]
    public partial class CustomTagElement : VisualElement { }

    [UxmlElement("UxmlProbeDuplicate")]
    public partial class DuplicateA : VisualElement { }

    [UxmlElement("UxmlProbeDuplicate")]
    public partial class DuplicateB : VisualElement { }

    public class UndeclaredProbe : VisualElement { }

    [UxmlElement]
    public partial class ValueProbe : VisualElement
    {
        [UxmlAttribute] public List<int> myListOfInts;
        [UxmlAttribute] public Color myColor;
        [UxmlAttribute, UxmlTypeReference(typeof(VisualElement))] public Type myType;
        [UxmlAttribute] public bool myBool;
        [UxmlAttribute] public char myChar;
        [UxmlAttribute] public byte myByte;
        [UxmlAttribute] public short myShort;
        [UxmlAttribute] public ulong myUlong;
        [UxmlAttribute] public double myDouble;
        [UxmlAttribute] public UsageHints myHints;
        [UxmlAttribute] public Hash128 myHash;
        [UxmlAttribute] public Vector3 myVector;
        [UxmlAttribute] public RectInt myRectInt;
        [UxmlAttribute] public Bounds myBounds;
    }

    [Serializable]
    public class Person
    {
        public string name;
        public int age;
        public string nationality;
    }

    [UxmlElement]
    public partial class Department : VisualElement
    {
        [UxmlAttribute] public Person manager;
        [UxmlAttribute] public List<Person> employees;
    }

    public class PersonConverter : UxmlAttributeConverter<Person>
    {
        const char k_Separator = ':';

        public override string ToString(Person value) => $"{value.name}{k_Separator}{value.age}{k_Separator}{value.nationality}";

        public override Person FromString(string value)
        {
            var person = new Person();
            var split = value.Split(k_Separator);
            if (split.Length == 3)
            {
                person.name = split[0];
                person.age = int.Parse(split[1], CultureInfo.InvariantCulture);
                person.nationality = split[2];
            }
            return person;
        }
    }

    public class Pair<TFirst, TSecond>
    {
        public TFirst First;
        public TSecond Second;
    }

    public class PairConverter<TFirst, TSecond> : UxmlAttributeConverter<Pair<TFirst, TSecond>>
    {
        public override Pair<TFirst, TSecond> FromString(string value)
        {
            var parts = value.Split('|');
            return new Pair<TFirst, TSecond>
            {
                First = (TFirst)Convert.ChangeType(parts[0], typeof(TFirst), CultureInfo.InvariantCulture),
                Second = (TSecond)Convert.ChangeType(parts[1], typeof(TSecond), CultureInfo.InvariantCulture),
            };
        }
    }

    [UxmlElement]
    public partial class PairProbe : VisualElement
    {
        [UxmlAttribute] public Pair<int, int> numbers;
    }

    public class Unconverted
    {
        public string text;
    }

    [UxmlElement]
    public partial class NoConverterProbe : VisualElement
    {
        [UxmlAttribute] public Unconverted data;
        [UxmlAttribute] public int known;
    }

    [UxmlElement]
    public partial class CurveProbe : VisualElement
    {
        [UxmlAttribute] public AnimationCurve curve;
    }

    [UxmlElement]
    public partial class AssetProbe : VisualElement
    {
        [UxmlAttribute] public Texture2D myTexture;
    }

    public class LegacyBar : VisualElement
    {
        internal static readonly List<CreationContext> Contexts = new List<CreationContext>();

        public new class UxmlFactory : UxmlFactory<LegacyBar, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlFloatAttributeDescription m_LowValue = new UxmlFloatAttributeDescription { name = "low-value", defaultValue = 0 };
            UxmlFloatAttributeDescription m_HighValue = new UxmlFloatAttributeDescription { name = "high-value", defaultValue = 100 };
            UxmlFloatAttributeDescription m_Value = new UxmlFloatAttributeDescription { name = "value", defaultValue = 0 };
            UxmlStringAttributeDescription m_Title = new UxmlStringAttributeDescription { name = "title", defaultValue = string.Empty, obsoleteNames = new[] { "old-title" } };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var bar = (LegacyBar)ve;
                bar.lowValue = m_LowValue.GetValueFromBag(bag, cc);
                bar.highValue = m_HighValue.GetValueFromBag(bag, cc);
                bar.value = m_Value.GetValueFromBag(bag, cc);
                bar.title = m_Title.GetValueFromBag(bag, cc);
                Contexts.Add(cc);
            }
        }

        public string title { get; set; }
        public float lowValue { get; set; }
        public float highValue { get; set; }
        public float value { get; set; }
    }

    public class LegacyButton : Button
    {
        public string extra;

        public new class UxmlFactory : UxmlFactory<LegacyButton, Traits> { }

        public class Traits : Button.UxmlTraits
        {
            UxmlStringAttributeDescription m_Extra = new UxmlStringAttributeDescription { name = "extra" };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((LegacyButton)ve).extra = m_Extra.GetValueFromBag(bag, cc);
            }
        }
    }

    [UxmlElement]
    public partial class Card : VisualElement
    {
        public string title { get; set; } = "initial";
    }

    public class CardButton : Button
    {
        public string caption { get; set; } = "initial";

        public new class UxmlFactory : UxmlFactory<CardButton, Button.UxmlTraits> { }
    }
}
