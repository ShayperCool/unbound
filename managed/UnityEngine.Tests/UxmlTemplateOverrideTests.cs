#pragma warning disable CS0618 // the deprecated UxmlFactory/UxmlTraits path is tested too
using System;
using UnityEngine.UIElements;
using Xunit;

// Template instances with attribute overrides and slots, measured on the Unity 6000.0.71f1 Editor in
// tests/projects/uxmltemplates (the outermost instance's override wins, then the first of an
// instance's; overrides reach every named element of the instanced documents but not template
// instances nor the instance's own children; a slot is reachable only from the instance whose
// documents define it). These tests apply the same rules to cases the probe does not take.
namespace UnityEngine.Tests
{
    /// <summary>A game element of the deprecated factory path for UxmlTemplateOverrideTests.</summary>
    public sealed class TemplateOverrideCounter : VisualElement
    {
        public new sealed class UxmlFactory : UxmlFactory<TemplateOverrideCounter, UxmlTraits> { }

        public new sealed class UxmlTraits : VisualElement.UxmlTraits
        {
            readonly UxmlIntAttributeDescription m_Count = new UxmlIntAttributeDescription { name = "count", obsoleteNames = new[] { "old-count" } };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ((TemplateOverrideCounter)ve).count = m_Count.GetValueFromBag(bag, cc);
            }
        }

        public int count;
    }

    [Collection("UXML custom controls")]
    public class UxmlTemplateOverrideTests
    {
        const string Ns = "xmlns:ui=\"UnityEngine.UIElements\"";

        static VisualTreeAsset Import(string name, string text)
        {
            var asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            asset.name = name;
            Unbound.UIElements.UxmlImporter.Import("Assets/UI/" + name + ".uxml", text, asset);
            return asset;
        }

        /// <summary>A document instancing <paramref name="template"/> under <paramref name="alias"/>
        /// (the template is registered directly: the test has no project to resolve `src` in).</summary>
        static VisualTreeAsset Host(string name, string alias, VisualTreeAsset template, string body)
        {
            var asset = Import(name, $"<ui:UXML {Ns}>{body}</ui:UXML>");
            asset.m_UaTemplates[alias] = template;
            return asset;
        }

        // An element nested inside another element of the template is overridden too.
        [Fact]
        public void NestedElementsOfTheTemplateAreOverridden()
        {
            var leaf = Import("DeepLeaf", $"<ui:UXML {Ns}><ui:VisualElement><ui:VisualElement><ui:Label name=\"deep\" text=\"own\" /></ui:VisualElement></ui:VisualElement></ui:UXML>");
            var host = Host("DeepHost", "Leaf", leaf,
                "<ui:Instance template=\"Leaf\"><AttributeOverrides element-name=\"deep\" text=\"set\" tooltip=\"tip\" /></ui:Instance>");
            var root = new VisualElement();
            host.CloneTree(root);
            var deep = root.Q<Label>("deep");
            Assert.Equal("set", deep.text);
            Assert.Equal("tip", deep.tooltip);
        }

        // Through a factory's traits an outer instance's override wins even under a former name,
        // over an inner instance's under the current one.
        [Fact]
        public void AnOuterOverrideUnderAFormerNameWinsThroughTraits()
        {
            var ns = typeof(TemplateOverrideCounter).Namespace;
            var leaf = Import("CounterLeaf", $"<ui:UXML {Ns} xmlns:t=\"{ns}\"><t:TemplateOverrideCounter name=\"c\" count=\"1\" /></ui:UXML>");
            var middle = Host("CounterMiddle", "Leaf", leaf,
                "<ui:Instance template=\"Leaf\"><AttributeOverrides element-name=\"c\" count=\"2\" /></ui:Instance>");
            var outer = Host("CounterOuter", "Middle", middle,
                "<ui:Instance template=\"Middle\"><AttributeOverrides element-name=\"c\" old-count=\"3\" /></ui:Instance>");
            var root = new VisualElement();
            middle.CloneTree(root);
            Assert.Equal(2, root.Q<TemplateOverrideCounter>("c").count);
            root = new VisualElement();
            outer.CloneTree(root);
            Assert.Equal(3, root.Q<TemplateOverrideCounter>("c").count);
        }

        // Each clone from code has its own slot table: a second clone fills its own slots.
        [Fact]
        public void EachCloneHasItsOwnSlots()
        {
            var leaf = Import("SlotLeaf", $"<ui:UXML {Ns}><ui:VisualElement name=\"s\" slot-name=\"s\" /></ui:UXML>");
            var host = Host("SlotHost", "Leaf", leaf,
                "<ui:Instance template=\"Leaf\"><ui:Label name=\"x\" slot=\"s\" /></ui:Instance>");
            for (int i = 0; i < 2; i++)
            {
                var root = new VisualElement();
                host.CloneTree(root);
                Assert.Same(root.Q("s"), root.Q<Label>("x").parent);
            }
        }

        // A template container written as an element names its template without cloning it.
        [Fact]
        public void ATemplateContainerElementNamesItsTemplate()
        {
            var asset = Import("ContainerElement", $"<ui:UXML {Ns}><ui:TemplateContainer template=\"Missing\" name=\"t\" /></ui:UXML>");
            var root = new VisualElement();
            asset.CloneTree(root);
            var container = root.Q<TemplateContainer>("t");
            Assert.Equal("Missing", container.templateId);
            Assert.Equal(0, container.childCount);
        }

        // The deprecated traits of a template container throw under any enclosing override, and a
        // factory's exception leaves the clone.
        [Fact]
        public void TemplateContainerTraitsThrowUnderOverrides()
        {
            var panel = Import("ThrowPanel", $"<ui:UXML {Ns}><ui:Label name=\"h\" /></ui:UXML>");
            var cc = new CreationContext(Unbound.UIElements.UxmlCloneScope.Root(), panel, null);
            var traits = new TemplateContainer.UxmlTraits();
            var container = new TemplateContainer();
            var bag = new Bag("template", "Self");
            panel.m_UaTemplates["Self"] = panel;
            traits.Init(container, bag, cc);
            Assert.Equal(1, container.childCount);
            var overridden = Unbound.UIElements.UxmlCloneScope.Root().Enter(new System.Collections.Generic.List<Unbound.UIElements.UxmlAttributeOverride>
            {
                new Unbound.UIElements.UxmlAttributeOverride("nobody", "text", "x"),
            });
            Assert.Throws<NullReferenceException>(() => traits.Init(new TemplateContainer(), bag, new CreationContext(overridden, panel, null)));
        }

        sealed class Bag : IUxmlAttributes
        {
            readonly string m_Name, m_Value;
            public Bag(string name, string value) { m_Name = name; m_Value = value; }
            public bool TryGetAttributeValue(string attributeName, out string value)
            {
                value = attributeName == m_Name ? m_Value : null;
                return value != null;
            }
        }
    }
}
