using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    public class CertificateAsset : ScriptableObject
    {
        [SerializeField] string data;
        public string Data => data;
    }

    [Serializable]
    public struct InnerData
    {
        public int a;
        public Vector3 v;
    }

    [Serializable]
    public struct OuterData
    {
        public InnerData inner;
        public float f;
    }

    [Serializable]
    public class ItemData
    {
        public string label;
        public int count;
        [NonSerialized] public int runtimeOnly = 42;
    }

    public enum SampleMode { First = 1, Second = 5, ThirdOne = 9 }

    public interface IShapeData { }

    [Serializable]
    public class CircleData : IShapeData
    {
        public float radius;
    }

    public class SerializedSample : ScriptableObject
    {
        public int number = 5;
        [SerializeField] List<int> numbers = new List<int> { 1, 2, 3 };
        public ItemData[] items = { new ItemData { label = "a", count = 1 } };
        public OuterData outer;
        public SerializedSample other;
        public SampleMode mode = SampleMode.Second;
        [Tooltip("hint")] public Color tint = Color.red;
        [HideInInspector] public bool hidden;
        [SerializeReference] public IShapeData shape;
        public long big;
        public double precise;
        public Bounds box;

        public List<int> Numbers => numbers;

        public int validated;
        void OnValidate() => validated++;
    }

    public class SmallSample : ScriptableObject
    {
        public int a;
        public Vector2 v;
        public List<int> list = new List<int> { 7 };
    }

    public class EnabledSample : MonoBehaviour
    {
        public float speed = 2f;
    }

    public class SerializedObjectTests
    {
        static T Create<T>() where T : ScriptableObject => ScriptableObject.CreateInstance<T>();

        [Fact]
        public void FindsAPrivateSerializeFieldAndAppliesABufferedWrite()
        {
            var certificate = Create<CertificateAsset>();
            var serialized = new SerializedObject(certificate);
            var property = serialized.FindProperty("data");
            Assert.NotNull(property);
            Assert.Equal("data", property.name);
            Assert.Equal("Data", property.displayName);
            Assert.Equal("data", property.propertyPath);
            Assert.Equal(SerializedPropertyType.String, property.propertyType);
            Assert.Equal("string", property.type);
            Assert.Equal("", property.stringValue);

            property.stringValue = "-----BEGIN CERTIFICATE-----";
            // Reads after a write see the buffered value; the object is unchanged until Apply.
            Assert.Equal("-----BEGIN CERTIFICATE-----", property.stringValue);
            Assert.Equal("-----BEGIN CERTIFICATE-----", serialized.FindProperty("data").stringValue);
            Assert.Null(certificate.Data);
            Assert.True(serialized.hasModifiedProperties);

            Assert.True(serialized.ApplyModifiedProperties());
            Assert.Equal("-----BEGIN CERTIFICATE-----", certificate.Data);
            Assert.False(serialized.hasModifiedProperties);
            Assert.False(serialized.ApplyModifiedProperties());
        }

        [Fact]
        public void UpdateDropsUnappliedChangesAndRereads()
        {
            var sample = Create<SerializedSample>();
            var serialized = new SerializedObject(sample);
            var number = serialized.FindProperty("number");
            number.intValue = 11;
            sample.number = 20;
            serialized.Update();
            Assert.False(serialized.hasModifiedProperties);
            Assert.Equal(20, number.intValue);
            Assert.False(serialized.ApplyModifiedProperties());
            Assert.Equal(20, sample.number);
        }

        [Fact]
        public void WritingTheSameValueIsNotAModification()
        {
            var sample = Create<SerializedSample>();
            var serialized = new SerializedObject(sample);
            serialized.FindProperty("number").intValue = 5;
            Assert.False(serialized.hasModifiedProperties);
        }

        [Fact]
        public void ArraysExposeSizeAndDataPaths()
        {
            var sample = Create<SerializedSample>();
            var serialized = new SerializedObject(sample);
            var numbers = serialized.FindProperty("numbers");
            Assert.True(numbers.isArray);
            Assert.Equal("vector", numbers.type);
            Assert.Equal("int", numbers.arrayElementType);
            Assert.Equal(SerializedPropertyType.Generic, numbers.propertyType);
            Assert.Equal(3, numbers.arraySize);

            var size = serialized.FindProperty("numbers.Array.size");
            Assert.Equal(SerializedPropertyType.ArraySize, size.propertyType);
            Assert.Equal(3, size.intValue);
            Assert.Equal("Size", size.displayName);

            var second = numbers.GetArrayElementAtIndex(1);
            Assert.Equal("numbers.Array.data[1]", second.propertyPath);
            Assert.Equal("data", second.name);
            Assert.Equal("Element 1", second.displayName);
            Assert.Equal(2, second.intValue);
            Assert.Equal(2, second.depth);
            Assert.Equal(3, serialized.FindProperty("numbers.Array.data[2]").intValue);
            Assert.Equal(1, numbers.FindPropertyRelative("Array.data[0]").intValue);
            Assert.Null(numbers.GetArrayElementAtIndex(3));
            Assert.Null(serialized.FindProperty("numbers.Array.data[3]"));
        }

        [Fact]
        public void ArrayElementsAreInsertedDeletedAndMovedThenApplied()
        {
            var sample = Create<SerializedSample>();
            var serialized = new SerializedObject(sample);
            var numbers = serialized.FindProperty("numbers");

            // Insert duplicates the element at the index.
            numbers.InsertArrayElementAtIndex(1);
            Assert.Equal(4, numbers.arraySize);
            Assert.Equal(new[] { 1, 2, 2, 3 }, Values(numbers));
            numbers.GetArrayElementAtIndex(1).intValue = 9;
            numbers.DeleteArrayElementAtIndex(0);
            Assert.Equal(new[] { 9, 2, 3 }, Values(numbers));
            Assert.True(numbers.MoveArrayElement(2, 0));
            Assert.Equal(new[] { 3, 9, 2 }, Values(numbers));
            // Growing duplicates the last element.
            numbers.arraySize = 5;
            Assert.Equal(new[] { 3, 9, 2, 2, 2 }, Values(numbers));
            serialized.FindProperty("numbers.Array.size").intValue = 4;
            Assert.Equal(new[] { 1, 2, 3 }, sample.Numbers);

            Assert.True(serialized.ApplyModifiedProperties());
            Assert.Equal(new[] { 3, 9, 2, 2 }, sample.Numbers);

            numbers.ClearArray();
            serialized.ApplyModifiedProperties();
            Assert.Empty(sample.Numbers);
            numbers.InsertArrayElementAtIndex(0);
            serialized.ApplyModifiedProperties();
            Assert.Equal(new[] { 0 }, sample.Numbers);
        }

        static int[] Values(SerializedProperty array)
        {
            var values = new int[array.arraySize];
            for (int i = 0; i < values.Length; i++) values[i] = array.GetArrayElementAtIndex(i).intValue;
            return values;
        }

        [Fact]
        public void ArraysOfClassesKeepTheirInstancesAndDuplicateIntoNewOnes()
        {
            var sample = Create<SerializedSample>();
            var original = sample.items[0];
            original.runtimeOnly = 7;
            var serialized = new SerializedObject(sample);
            serialized.FindProperty("items.Array.data[0].label").stringValue = "z";
            serialized.ApplyModifiedProperties();
            Assert.Same(original, sample.items[0]);
            Assert.Equal("z", original.label);

            var items = serialized.FindProperty("items");
            items.InsertArrayElementAtIndex(1);
            items.GetArrayElementAtIndex(1).FindPropertyRelative("count").intValue = 3;
            serialized.ApplyModifiedProperties();
            Assert.Equal(2, sample.items.Length);
            Assert.Same(original, sample.items[0]);
            Assert.Equal(7, sample.items[0].runtimeOnly);
            Assert.NotSame(original, sample.items[1]);
            Assert.Equal("z", sample.items[1].label);
            Assert.Equal(3, sample.items[1].count);
            Assert.Equal(1, sample.items[0].count);
            // A new instance is constructed: non-serialized fields get their initializers.
            Assert.Equal(42, sample.items[1].runtimeOnly);
        }

        [Fact]
        public void NestedStructWritesAreWrittenBackUpTheChain()
        {
            var sample = Create<SerializedSample>();
            sample.outer.f = 1.5f;
            var serialized = new SerializedObject(sample);
            var y = serialized.FindProperty("outer.inner.v.y");
            Assert.Equal(SerializedPropertyType.Float, y.propertyType);
            Assert.Equal("outer.inner.v.y", y.propertyPath);
            Assert.Equal(3, y.depth);
            y.floatValue = 3f;
            serialized.FindProperty("outer.inner.a").intValue = 4;
            Assert.Equal(new Vector3(0, 3, 0), serialized.FindProperty("outer.inner.v").vector3Value);
            Assert.Equal(0, sample.outer.inner.a);

            serialized.ApplyModifiedProperties();
            Assert.Equal(4, sample.outer.inner.a);
            Assert.Equal(new Vector3(0, 3, 0), sample.outer.inner.v);
            Assert.Equal(1.5f, sample.outer.f);

            serialized.FindProperty("box.m_Extent.x").floatValue = 2f;
            serialized.ApplyModifiedProperties();
            Assert.Equal(new Vector3(4, 0, 0), sample.box.size);
        }

        [Fact]
        public void IteratesInUnitysOrder()
        {
            var small = Create<SmallSample>();
            var serialized = new SerializedObject(small);

            var all = new List<string>();
            var it = serialized.GetIterator();
            Assert.Equal(-1, it.depth);
            while (it.Next(true)) all.Add(it.propertyPath);
            Assert.Equal(new[]
            {
                "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject",
                "m_Enabled", "m_EditorHideFlags", "m_Script", "m_Name", "m_EditorClassIdentifier",
                "a", "v", "v.x", "v.y", "list", "list.Array", "list.Array.size", "list.Array.data[0]",
            }, all);

            var top = new List<string>();
            it = serialized.GetIterator();
            while (it.Next(false)) top.Add(it.propertyPath);
            Assert.Equal(new[]
            {
                "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject",
                "m_Enabled", "m_EditorHideFlags", "m_Script", "m_Name", "m_EditorClassIdentifier", "a", "v", "list",
            }, top);

            var visible = new List<string>();
            it = serialized.GetIterator();
            while (it.NextVisible(true)) visible.Add(it.propertyPath);
            Assert.Equal(new[] { "m_Script", "a", "v", "v.x", "v.y", "list", "list.Array.size", "list.Array.data[0]" }, visible);

            // The inspector's loop: enter the object once, then walk its top-level properties.
            var inspector = new List<string>();
            it = serialized.GetIterator();
            for (bool enter = true; it.NextVisible(enter); enter = false) inspector.Add(it.propertyPath);
            Assert.Equal(new[] { "m_Script", "a", "v", "list" }, inspector);

            Assert.False(it.NextVisible(false));
            Assert.Throws<InvalidOperationException>(() => it.propertyPath);
        }

        [Fact]
        public void ChildrenEnumerateUpToTheEndProperty()
        {
            var serialized = new SerializedObject(Create<SmallSample>());
            var v = serialized.FindProperty("v");
            Assert.True(v.hasChildren);
            Assert.True(v.hasVisibleChildren);
            var names = new List<string>();
            foreach (SerializedProperty child in v.Copy()) names.Add(child.name);
            Assert.Equal(new[] { "x", "y" }, names);

            var end = v.GetEndProperty();
            Assert.Equal("list", end.propertyPath);
            var x = v.Copy();
            x.NextVisible(true);
            Assert.False(SerializedProperty.EqualContents(x, end));
            x.NextVisible(false);
            x.NextVisible(false);
            Assert.True(SerializedProperty.EqualContents(x, end));

            var list = serialized.FindProperty("list");
            var elements = new List<int>();
            foreach (SerializedProperty element in list) elements.Add(element.intValue);
            Assert.Equal(new[] { 7 }, elements);
        }

        [Fact]
        public void ObjectReferencesAndScriptProperties()
        {
            var sample = Create<SerializedSample>();
            var other = Create<SerializedSample>();
            sample.name = "first";
            var serialized = new SerializedObject(sample);

            var reference = serialized.FindProperty("other");
            Assert.Equal(SerializedPropertyType.ObjectReference, reference.propertyType);
            Assert.Equal("PPtr<$SerializedSample>", reference.type);
            Assert.Null(reference.objectReferenceValue);
            reference.objectReferenceValue = other;
            Assert.Same(other, reference.objectReferenceValue);
            Assert.Equal(other.GetInstanceID(), reference.objectReferenceInstanceIDValue);
            // A reference of another type is not assigned.
            reference.objectReferenceValue = Create<SmallSample>();
            Assert.Same(other, reference.objectReferenceValue);

            var script = serialized.FindProperty("m_Script");
            Assert.Equal(SerializedPropertyType.ObjectReference, script.propertyType);
            Assert.Equal("PPtr<MonoScript>", script.type);

            var name = serialized.FindProperty("m_Name");
            Assert.Equal("first", name.stringValue);
            name.stringValue = "renamed";

            serialized.ApplyModifiedProperties();
            Assert.Same(other, sample.other);
            Assert.Equal("renamed", sample.name);
        }

        [Fact]
        public void ValueAccessorsFollowThePropertyType()
        {
            var sample = Create<SerializedSample>();
            var serialized = new SerializedObject(sample);

            var mode = serialized.FindProperty("mode");
            Assert.Equal(SerializedPropertyType.Enum, mode.propertyType);
            Assert.Equal(new[] { "First", "Second", "ThirdOne" }, mode.enumNames);
            Assert.Equal(new[] { "First", "Second", "Third One" }, mode.enumDisplayNames);
            Assert.Equal(1, mode.enumValueIndex);
            Assert.Equal(5, mode.intValue);
            mode.enumValueIndex = 2;
            Assert.Equal(9, mode.enumValueFlag);

            var tint = serialized.FindProperty("tint");
            Assert.Equal(SerializedPropertyType.Color, tint.propertyType);
            Assert.Equal("hint", tint.tooltip);
            Assert.Equal(Color.red, tint.colorValue);
            serialized.FindProperty("tint.g").floatValue = 1f;
            Assert.Equal(new Color(1, 1, 0, 1), tint.colorValue);

            var big = serialized.FindProperty("big");
            Assert.Equal(SerializedPropertyNumericType.Int64, big.numericType);
            big.longValue = long.MaxValue;
            var precise = serialized.FindProperty("precise");
            precise.doubleValue = 0.1;

            var outer = serialized.FindProperty("outer");
            var boxed = (OuterData)outer.boxedValue;
            boxed.f = 8f;
            boxed.inner.a = 6;
            outer.boxedValue = boxed;
            Assert.Equal(6, serialized.FindProperty("outer.inner.a").intValue);

            serialized.ApplyModifiedProperties();
            Assert.Equal(SampleMode.ThirdOne, sample.mode);
            Assert.Equal(new Color(1, 1, 0, 1), sample.tint);
            Assert.Equal(long.MaxValue, sample.big);
            Assert.Equal(0.1, sample.precise);
            Assert.Equal(8f, sample.outer.f);
            Assert.Equal(6, sample.outer.inner.a);
            Assert.Equal(1, sample.validated);
        }

        [Fact]
        public void HiddenFieldsAreSkippedByVisibleIteration()
        {
            var serialized = new SerializedObject(Create<SerializedSample>());
            var it = serialized.GetIterator();
            var visible = new List<string>();
            for (bool enter = true; it.NextVisible(enter); enter = false) visible.Add(it.name);
            Assert.DoesNotContain("hidden", visible);
            Assert.NotNull(serialized.FindProperty("hidden"));
        }

        [Fact]
        public void ManagedReferences()
        {
            var sample = Create<SerializedSample>();
            var serialized = new SerializedObject(sample);
            var shape = serialized.FindProperty("shape");
            Assert.Equal(SerializedPropertyType.ManagedReference, shape.propertyType);
            Assert.Null(shape.managedReferenceValue);
            Assert.Equal(-2, shape.managedReferenceId);

            var circle = new CircleData { radius = 1f };
            shape.managedReferenceValue = circle;
            Assert.EndsWith(" UnityEngine.Tests.CircleData", shape.managedReferenceFullTypename);
            var radius = serialized.FindProperty("shape.radius");
            Assert.NotNull(radius);
            radius.floatValue = 2.5f;
            serialized.ApplyModifiedProperties();
            Assert.Same(circle, sample.shape);
            Assert.Equal(2.5f, circle.radius);
        }

        [Fact]
        public void EditingMultipleObjectsWritesToAll()
        {
            var a = Create<SerializedSample>();
            var b = Create<SerializedSample>();
            b.number = 6;
            var serialized = new SerializedObject(new Object[] { a, b });
            Assert.True(serialized.isEditingMultipleObjects);
            Assert.Same(a, serialized.targetObject);
            var number = serialized.FindProperty("number");
            Assert.Equal(5, number.intValue);
            Assert.True(number.hasMultipleDifferentValues);
            Assert.False(serialized.FindProperty("mode").hasMultipleDifferentValues);

            number.intValue = 12;
            serialized.FindProperty("items.Array.data[0].count").intValue = 4;
            serialized.ApplyModifiedProperties();
            Assert.Equal(12, a.number);
            Assert.Equal(12, b.number);
            Assert.Equal(4, a.items[0].count);
            Assert.Equal(4, b.items[0].count);
            Assert.NotSame(a.items[0], b.items[0]);
        }

        [Fact]
        public void BuiltInPropertiesOfComponents()
        {
            var go = new GameObject("holder");
            var behaviour = go.AddComponent<EnabledSample>();
            var serialized = new SerializedObject(behaviour);
            Assert.Same(go, serialized.FindProperty("m_GameObject").objectReferenceValue);
            var enabled = serialized.FindProperty("m_Enabled");
            Assert.True(enabled.boolValue);
            enabled.boolValue = false;
            serialized.FindProperty("speed").floatValue = 4f;
            serialized.ApplyModifiedProperties();
            Assert.False(behaviour.enabled);
            Assert.Equal(4f, behaviour.speed);

            var transform = new SerializedObject(go.transform);
            transform.FindProperty("m_LocalPosition.y").floatValue = 2f;
            transform.ApplyModifiedProperties();
            Assert.Equal(new Vector3(0, 2, 0), go.transform.localPosition);

            var gameObject = new SerializedObject(go);
            Assert.Equal("holder", gameObject.FindProperty("m_Name").stringValue);
            Object.DestroyImmediate(go);
        }

        [Fact]
        public void CopiesPropertiesBetweenObjects()
        {
            var a = Create<SerializedSample>();
            var b = Create<SerializedSample>();
            a.items[0].label = "copied";
            var source = new SerializedObject(a);
            var target = new SerializedObject(b);
            Assert.False(SerializedProperty.DataEquals(source.FindProperty("items"), target.FindProperty("items")));
            Assert.True(target.CopyFromSerializedPropertyIfDifferent(source.FindProperty("items")));
            Assert.False(target.CopyFromSerializedPropertyIfDifferent(source.FindProperty("items")));
            target.ApplyModifiedProperties();
            Assert.Equal("copied", b.items[0].label);
            Assert.NotSame(a.items[0], b.items[0]);
        }

        [Theory]
        [InlineData("m_MyVariable", "My Variable")]
        [InlineData("_privateThing", "Private Thing")]
        [InlineData("kConstantValue", "Constant Value")]
        [InlineData("myHTMLParser", "My HTML Parser")]
        [InlineData("value2", "Value 2")]
        [InlineData("x", "X")]
        [InlineData("isExpanded", "Is Expanded")]
        public void NicifiesVariableNames(string name, string expected)
        {
            Assert.Equal(expected, ObjectNames.NicifyVariableName(name));
        }
    }
}
