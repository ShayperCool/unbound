using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    public class UxmlSerializedDataTests
    {
        [Serializable]
        class Payload
        {
            public string text;
            public int count;
        }

        class CustomRef : IUxmlSerializedDataDeserializeReference
        {
            public string tag;
            public object DeserializeReference() => new CustomRef { tag = tag + ":deserialized" };
        }

        class TestElement : VisualElement
        {
            public int count { get; set; } = 42;
            public string title { get; set; } = "initial";
            public Payload payload { get; set; } = new Payload { text = "init", count = 9 };

            [Serializable]
            public new class UxmlSerializedData : VisualElement.UxmlSerializedData
            {
                [SerializeField] int count;
                [SerializeField] string title;
                [SerializeField] Payload payload;
                [SerializeField, UxmlIgnore, HideInInspector] UxmlAttributeFlags count_UxmlAttributeFlags;
                [SerializeField, UxmlIgnore, HideInInspector] UxmlAttributeFlags title_UxmlAttributeFlags;
                [SerializeField, UxmlIgnore, HideInInspector] UxmlAttributeFlags payload_UxmlAttributeFlags;

                public override object CreateInstance() => new TestElement();

                public override void Deserialize(object obj)
                {
                    base.Deserialize(obj);
                    var e = (TestElement)obj;
                    if (ShouldWriteAttributeValue(count_UxmlAttributeFlags)) e.count = count;
                    if (ShouldWriteAttributeValue(title_UxmlAttributeFlags)) e.title = title;
                    if (ShouldWriteAttributeValue(payload_UxmlAttributeFlags) && payload != null)
                        e.payload = UxmlSerializedDataUtility.CopySerialized<Payload>(payload);
                }
            }
        }

        class ThrowingData : UxmlSerializedData
        {
            public bool sawDefault;
            public bool sawOverridden;

            public override object CreateInstance() => new VisualElement();

            public override void Deserialize(object obj)
            {
                sawDefault = ShouldWriteAttributeValue(UxmlAttributeFlags.DefaultValue);
                sawOverridden = ShouldWriteAttributeValue(UxmlAttributeFlags.OverriddenInUxml);
                throw new InvalidOperationException("boom");
            }
        }

        static void SetField(object target, string name, object value)
        {
            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                var field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }
            throw new MissingFieldException(target.GetType().FullName, name);
        }

        [Fact]
        public void ShouldWriteAttributeValue_DefaultAndScopedFlags()
        {
            Assert.False(UxmlSerializedData.ShouldWriteAttributeValue(UxmlSerializedData.UxmlAttributeFlags.Ignore));
            Assert.True(UxmlSerializedData.ShouldWriteAttributeValue(UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml));
            Assert.False(UxmlSerializedData.ShouldWriteAttributeValue(UxmlSerializedData.UxmlAttributeFlags.DefaultValue));
            Assert.True(UxmlSerializedData.ShouldWriteAttributeValue(
                UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml | UxmlSerializedData.UxmlAttributeFlags.DefaultValue));

            var data = new TestElement.UxmlSerializedData();
            SetField(data, "count", 0);
            SetField(data, "count_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(data, "title", "default-flag");
            SetField(data, "title_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.DefaultValue);

            var normal = (TestElement)data.CreateInstance();
            data.Deserialize(normal);
            Assert.Equal(0, normal.count);
            Assert.Equal("initial", normal.title);

            var defaultOnly = (TestElement)data.CreateInstance();
            data.Deserialize(defaultOnly, UxmlSerializedData.UxmlAttributeFlags.DefaultValue);
            Assert.Equal(42, defaultOnly.count);
            Assert.Equal("default-flag", defaultOnly.title);

            var both = (TestElement)data.CreateInstance();
            data.Deserialize(both, UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml | UxmlSerializedData.UxmlAttributeFlags.DefaultValue);
            Assert.Equal(0, both.count);
            Assert.Equal("default-flag", both.title);

            Assert.True(UxmlSerializedData.ShouldWriteAttributeValue(UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml));
            Assert.False(UxmlSerializedData.ShouldWriteAttributeValue(UxmlSerializedData.UxmlAttributeFlags.DefaultValue));
        }

        [Fact]
        public void DeserializeWithFlags_RestoresMaskAfterException()
        {
            var throwing = new ThrowingData();
            Assert.Throws<InvalidOperationException>(() =>
                throwing.Deserialize(new VisualElement(), UxmlSerializedData.UxmlAttributeFlags.DefaultValue));
            Assert.True(throwing.sawDefault);
            Assert.False(throwing.sawOverridden);
            Assert.False(UxmlSerializedData.ShouldWriteAttributeValue(UxmlSerializedData.UxmlAttributeFlags.DefaultValue));
            Assert.True(UxmlSerializedData.ShouldWriteAttributeValue(UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml));
        }

        [Fact]
        public void CopySerialized_IsolatesInstancesAndSupportsCustomDeserializeReference()
        {
            Assert.Null(UxmlSerializedDataUtility.CopySerialized(null));

            var data = new TestElement.UxmlSerializedData();
            SetField(data, "payload", new Payload { text = "shared", count = 7 });
            SetField(data, "payload_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);

            var a = (TestElement)data.CreateInstance();
            var b = (TestElement)data.CreateInstance();
            data.Deserialize(a);
            data.Deserialize(b);
            Assert.NotSame(a.payload, b.payload);
            a.payload.text = "changed";
            Assert.Equal("shared", b.payload.text);

            var custom = new CustomRef { tag = "x" };
            var cloned = UxmlSerializedDataUtility.CopySerialized<CustomRef>(custom);
            Assert.Equal("x:deserialized", cloned.tag);
            var boxed = (CustomRef)UxmlSerializedDataUtility.CopySerialized(custom);
            Assert.Equal("x:deserialized", boxed.tag);
        }

        [Fact]
        public void VisualElementAndDataBindingSerializedData_ApplyOnlyFlaggedAttributesAndBindings()
        {
            var bindingData = new DataBinding.UxmlSerializedData();
            SetField(bindingData, "property", "tooltip");
            SetField(bindingData, "property_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(bindingData, "dataSourcePathString", "hero.title");
            SetField(bindingData, "dataSourcePathString_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(bindingData, "bindingMode", BindingMode.ToTarget);
            SetField(bindingData, "bindingMode_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);

            var veData = new VisualElement.UxmlSerializedData();
            SetField(veData, "name", "panel-root");
            SetField(veData, "name_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);
            SetField(veData, "bindings", new List<Binding.UxmlSerializedData> { bindingData });
            SetField(veData, "bindings_UxmlAttributeFlags", UxmlSerializedData.UxmlAttributeFlags.OverriddenInUxml);

            Assert.True(veData.HasBindingInternal("tooltip"));
            Assert.False(veData.HasBindingInternal("text"));

            var ve = (VisualElement)veData.CreateInstance();
            ve.tooltip = "keep-tooltip";
            veData.Deserialize(ve);

            Assert.Equal("panel-root", ve.name);
            Assert.Equal("keep-tooltip", ve.tooltip);
            var attached = Assert.IsType<DataBinding>(ve.GetBinding("tooltip"));
            Assert.Equal("hero.title", attached.dataSourcePath.ToString());
            Assert.Equal(BindingMode.ToTarget, attached.bindingMode);
        }

        [Fact]
        public void DescriptionCacheAndAttributeCompat_FollowMeasuredAndUnmeasuredFamilies()
        {
            using (TestRelease.Pin("6000.0.71f1"))
            {
                var names = new UxmlAttributeNames("field", "field", null, null);
                Assert.NotNull(names.obsoleteNames);
                Assert.Empty(names.obsoleteNames);

                var ex = Assert.Throws<ArgumentException>(() => new UxmlObjectReferenceAttribute("null"));
                Assert.Equal("UxmlObjectReferenceAttribute name cannot be \"null\".", ex.Message);
                var twoArg = new UxmlObjectReferenceAttribute("null", typeof(CustomBinding));
                Assert.Equal("null", twoArg.name);
                Assert.Single(twoArg.types);

                var register = typeof(VisualElement.UxmlSerializedData).GetMethod("Register", BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);
                Assert.NotNull(register);
                register.Invoke(null, null);
                Assert.True(UxmlDescriptionCache.TryGetCachedDescription(typeof(VisualElement.UxmlSerializedData), out var veAttrs));
                Assert.Equal(13, veAttrs.Length);
            }

            using (TestRelease.Pin(TestRelease.Unmeasured))
            {
                var names = new UxmlAttributeNames("field", "field", null, null);
                Assert.Null(names.obsoleteNames);

                var attr = new UxmlObjectReferenceAttribute("null");
                Assert.Equal("null", attr.name);
            }
        }
    }
}
