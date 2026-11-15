using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    public class BuiltinStructHolder : ScriptableObject
    {
        public RenderingLayerMask mask;
        public SphericalHarmonicsL2 sh;
        public RenderingLayerMask[] masks = new RenderingLayerMask[0];
        public List<SphericalHarmonicsL2> shs = new List<SphericalHarmonicsL2>();
    }

    [Serializable]
    public class BuiltinStructWrapper
    {
        public RenderingLayerMask mask;
        public SphericalHarmonicsL2 sh;
    }

    /// <summary>RenderingLayerMask and SphericalHarmonicsL2 are serialized as built-in values, not
    /// as CLR serializable structs (Unity 6000.0.71f1, tests/projects/builtinserial; the text below
    /// is the Editor's own for these values).</summary>
    public class BuiltinStructSerializationTests
    {
        const string EditorText = @"  mask:
    serializedVersion: 0
    m_Bits: 5
  sh:
    'sh[ 0]': 1.5
    'sh[ 1]': 1.75
    'sh[ 2]': 2
    'sh[ 3]': 2.25
    'sh[ 4]': 2.5
    'sh[ 5]': 2.75
    'sh[ 6]': 3
    'sh[ 7]': 3.25
    'sh[ 8]': 3.5
    'sh[ 9]': 3.75
    'sh[10]': 4
    'sh[11]': 4.25
    'sh[12]': 4.5
    'sh[13]': 4.75
    'sh[14]': 5
    'sh[15]': 5.25
    'sh[16]': 5.5
    'sh[17]': 5.75
    'sh[18]': 6
    'sh[19]': 6.25
    'sh[20]': 6.5
    'sh[21]': 6.75
    'sh[22]': 7
    'sh[23]': 7.25
    'sh[24]': 7.5
    'sh[25]': 7.75
    'sh[26]': 8
  masks:
  - serializedVersion: 0
    m_Bits: 20
  - serializedVersion: 0
    m_Bits: 4294967295
  shs:
  - 'sh[ 0]': 201.5
    'sh[ 1]': 201.75
    'sh[ 2]': 202
    'sh[ 3]': 202.25
    'sh[ 4]': 202.5
    'sh[ 5]': 202.75
    'sh[ 6]': 203
    'sh[ 7]': 203.25
    'sh[ 8]': 203.5
    'sh[ 9]': 203.75
    'sh[10]': 204
    'sh[11]': 204.25
    'sh[12]': 204.5
    'sh[13]': 204.75
    'sh[14]': 205
    'sh[15]': 205.25
    'sh[16]': 205.5
    'sh[17]': 205.75
    'sh[18]': 206
    'sh[19]': 206.25
    'sh[20]': 206.5
    'sh[21]': 206.75
    'sh[22]': 207
    'sh[23]': 207.25
    'sh[24]': 207.5
    'sh[25]': 207.75
    'sh[26]': 208";

        // Distinct coefficients: seed + index / 4.
        static SphericalHarmonicsL2 Sh(float seed)
        {
            var sh = new SphericalHarmonicsL2();
            for (int i = 0; i < 27; i++) sh[i / 9, i % 9] = seed + i * 0.25f;
            return sh;
        }

        static BuiltinStructHolder Holder()
        {
            var holder = ScriptableObject.CreateInstance<BuiltinStructHolder>();
            holder.mask = 5u;
            holder.sh = Sh(1.5f);
            holder.masks = new RenderingLayerMask[] { 20u, 0xFFFFFFFFu };
            holder.shs = new List<SphericalHarmonicsL2> { Sh(201.5f) };
            return holder;
        }

        [Fact]
        public void TheStructsAreNotClrSerializable()
        {
            Assert.False(typeof(RenderingLayerMask).IsSerializable);
            Assert.False(typeof(SphericalHarmonicsL2).IsSerializable);
            Assert.Equal(Shape.RenderingLayerMask, SerializationRules.Classify(typeof(RenderingLayerMask), 0));
            Assert.Equal(Shape.SphericalHarmonicsL2, SerializationRules.Classify(typeof(SphericalHarmonicsL2), 0));
        }

        [Fact]
        public void TheTextWriterWritesTheEditorsText()
        {
            var holder = Holder();
            try
            {
                var text = UnityYamlWriter.Write(new Object[] { holder });
                Assert.Contains("m_EditorClassIdentifier: \n" + EditorText.Replace("\r", "") + "\n", text);
            }
            finally { Object.DestroyImmediate(holder); }
        }

        [Fact]
        public void TheReaderReadsTheEditorsTextAndKeepsUnnamedMembers()
        {
            var holder = ScriptableObject.CreateInstance<BuiltinStructHolder>();
            try
            {
                new YamlReader(null).ReadScriptObject(holder, TestYaml.DocumentBody("MonoBehaviour:\n" + EditorText));
                Assert.Equal(5u, holder.mask.value);
                Assert.Equal(Sh(1.5f), holder.sh);
                Assert.Equal(new uint[] { 20u, 0xFFFFFFFFu }, Array.ConvertAll(holder.masks, m => m.value));
                Assert.Equal(Sh(201.5f), Assert.Single(holder.shs));

                // Data naming some members only: the others keep their value.
                holder.mask = 9u;
                holder.sh = Sh(50);
                new YamlReader(null).ReadScriptObject(holder, TestYaml.DocumentBody("MonoBehaviour:\n  mask:\n    serializedVersion: 0\n  sh:\n    'sh[ 3]': 9\n"));
                Assert.Equal(9u, holder.mask.value);
                var expected = Sh(50);
                expected[0, 3] = 9;
                Assert.Equal(expected, holder.sh);
            }
            finally { Object.DestroyImmediate(holder); }
        }

        [Fact]
        public void JsonUsesTheBuiltInPayloadAndKeepsUnnamedMembers()
        {
            var wrapper = new BuiltinStructWrapper { mask = 0x80000000u, sh = Sh(-3) };
            var json = JsonUtility.ToJson(wrapper);
            Assert.StartsWith("{\"mask\":{\"serializedVersion\":\"0\",\"m_Bits\":2147483648},\"sh\":{\"sh[ 0]\":-3.0,\"sh[ 1]\":-2.75,", json);
            Assert.EndsWith("\"sh[ 9]\":-0.75,\"sh[10]\":-0.5,\"sh[11]\":-0.25,\"sh[12]\":0.0,\"sh[13]\":0.25,\"sh[14]\":0.5,\"sh[15]\":0.75,\"sh[16]\":1.0,\"sh[17]\":1.25,\"sh[18]\":1.5,\"sh[19]\":1.75,\"sh[20]\":2.0,\"sh[21]\":2.25,\"sh[22]\":2.5,\"sh[23]\":2.75,\"sh[24]\":3.0,\"sh[25]\":3.25,\"sh[26]\":3.5}}", json);
            var back = JsonUtility.FromJson<BuiltinStructWrapper>(json);
            Assert.Equal(0x80000000u, back.mask.value);
            Assert.Equal(Sh(-3), back.sh);

            var target = new BuiltinStructWrapper { mask = 1u << 20, sh = Sh(50) };
            JsonUtility.FromJsonOverwrite("{\"mask\":{},\"sh\":{\"sh[ 3]\":9,\"sh[26]\":-9}}", target);
            Assert.Equal(1u << 20, target.mask.value);
            var expected = Sh(50);
            expected[0, 3] = 9;
            expected[2, 8] = -9;
            Assert.Equal(expected, target.sh);
            // Not a serializable type of its own (a value outside a field writes nothing).
            Assert.Equal("{}", JsonUtility.ToJson(wrapper.mask));
        }

        [Fact]
        public void PropertiesExposeTheMaskAsAnIntegerAndTheCoefficientsAsFloats()
        {
            var holder = Holder();
            try
            {
                var so = new SerializedObject(holder);
                var mask = so.FindProperty("mask");
                Assert.Equal(SerializedPropertyType.RenderingLayerMask, mask.propertyType);
                Assert.Equal("RenderingLayerMask", mask.type);
                Assert.True(mask.hasChildren);
                Assert.False(mask.hasVisibleChildren);
                var bits = so.FindProperty("mask.m_Bits");
                Assert.Equal("uint", bits.type);
                var full = so.FindProperty("masks.Array.data[1]");
                Assert.Equal(-1, full.intValue);
                Assert.Equal(4294967295u, full.uintValue);
                Assert.Equal(4294967295L, full.longValue);
                var sh = so.FindProperty("sh");
                Assert.Equal(SerializedPropertyType.Generic, sh.propertyType);
                Assert.Equal("SphericalHarmonicsL2", sh.type);
                Assert.True(sh.hasVisibleChildren);
                mask.uintValue = 0x40000000u;
                so.FindProperty("sh.sh[26]").floatValue = 99;
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.Equal(0x40000000u, holder.mask.value);
                Assert.Equal(99f, holder.sh[2, 8]);
            }
            finally { Object.DestroyImmediate(holder); }
        }
    }
}
