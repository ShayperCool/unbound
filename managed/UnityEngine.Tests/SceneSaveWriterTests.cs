using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public enum SavedMood { Calm, Busy = 5, Late = -3 }

    [Serializable]
    public struct SavedPair
    {
        public int key;
        public float value;
    }

    [Serializable]
    public class SavedNested
    {
        public string title;
        public List<SavedPair> pairs = new List<SavedPair>();
    }

    /// <summary>The fields of tests/projects/scenesave's Kinds, in its order.</summary>
    public class SavedKinds : ScriptableObject
    {
        public List<float> floats = new List<float>();
        public bool[] flags = Array.Empty<bool>();
        public List<SavedMood> moods = new List<SavedMood>();
        public long[] longs = Array.Empty<long>();
        public byte[] bytes = Array.Empty<byte>();
        public List<double> doubles = new List<double>();
        public List<Vector3> points = new List<Vector3>();
        public List<SavedPair> pairs = new List<SavedPair>();
        public SavedNested nested = new SavedNested();
        public List<SavedNested> nestedList = new List<SavedNested>();
        public SavedMood mood;
        public uint unsigned;
        public short small;
        public char letter = 'a';
        public Matrix4x4 matrix = Matrix4x4.identity;
        public AnimationCurve curve = new AnimationCurve();
        public Gradient gradient = new Gradient();
        public RectOffset padding = new RectOffset();
        public Rect area;
        public LayerMask layers;
        public Bounds bounds;
        public Quaternion turn = Quaternion.identity;
        public Vector2Int cell;
        public Hash128 hash;
        public Color32 shade;
        public string multiline;
        public string empty = "";
        public List<string> texts = new List<string>();
    }

    [Serializable]
    public class SavedShape
    {
        public string name;
    }

    [Serializable]
    public class SavedCircle : SavedShape
    {
        public float radius;
    }

    [Serializable]
    public class SavedBox : SavedShape
    {
        public Vector2 size;
        [SerializeReference] public SavedShape inner;
    }

    /// <summary>tests/projects/scenesave's Graph: [SerializeReference] fields.</summary>
    public class SavedGraph : ScriptableObject
    {
        [SerializeReference] public SavedShape main;
        [SerializeReference] public List<SavedShape> shapes = new List<SavedShape>();
        [SerializeReference] public SavedShape empty;
        public int after = 1;
    }

    /// <summary>
    /// Unity's text serialization as the Unity 6000.0.71f1 Editor writes it when it saves a scene or
    /// an asset (tests/projects/scenesave; the text below is the Editor's own for the probe's Kinds):
    /// number layouts, string quoting, hex arrays of integers, booleans and enums, the block layouts
    /// of built-in values, and [SerializeReference] graphs with their references block.
    /// </summary>
    public class SceneSaveWriterTests
    {
        const string EditorKinds = """
  floats:
  - 0.5
  - -0.00001
  - 3.4028235e+38
  - 100000
  - 1000000
  - 10000000
  - 100000000
  - 1e+9
  - 1e+15
  - 1e+16
  - 1e+17
  - 123456790
  - 1234567.9
  - 16777216
  - 0.0001
  - 0.000001
  - 0.00000001
  - 0.000000001
  - 1e-10
  - 0.000000015
  - 1e-15
  - 1e-16
  - 1e-30
  - 1.1754944e-38
  - 1e-45
  - Infinity
  - -Infinity
  - NaN
  - 3.4028235e+38
  - 1e-45
  flags: 010001
  moods: 05000000fdffffff
  longs: ffffffffffffffff0000000000010000
  bytes: 000fff
  doubles:
  - 0.1
  - 1e-300
  - 100000
  - 10000000000
  - 1000000000000000
  - 10000000000000000
  - 1e+17
  - 1e+20
  - 1e+21
  - 1e+22
  - 1.2345678901234568e+17
  - 0.00001
  - 0.0000001
  - 0.00000001
  - 0.0000000001
  - 0.0000000000000001
  - 0.00000000000000001
  - 0.30000000000000004
  - 1.7976931348623157e+308
  - 5e-324
  - NaN
  - -Infinity
  points:
  - {x: 1, y: 2, z: 3}
  pairs:
  - key: 1
    value: 0.25
  - key: 2
    value: -4
  nested:
    title: inner
    pairs:
    - key: 3
      value: 1
  nestedList:
  - title: first
    pairs: []
  - title: 
    pairs: []
  mood: -3
  unsigned: 4000000000
  small: -7
  letter: 90
  matrix:
    e00: 0.8434933
    e01: -0.83682406
    e02: 0.16841204
    e03: 1
    e10: 0.49240386
    e11: 1.7057371
    e12: -0.08682408
    e13: 2
    e20: -0.21461019
    e21: 0.62464905
    e22: 0.4627083
    e23: 3
    e30: 0
    e31: 0
    e32: 0
    e33: 1
  curve:
    serializedVersion: 2
    m_Curve:
    - serializedVersion: 3
      time: 0
      value: 0
      inSlope: 0
      outSlope: 1
      tangentMode: 0
      weightedMode: 0
      inWeight: 0
      outWeight: 0
    - serializedVersion: 3
      time: 1
      value: 2
      inSlope: 0.5
      outSlope: 0.5
      tangentMode: 0
      weightedMode: 3
      inWeight: 0.2
      outWeight: 0.4
    m_PreInfinity: 2
    m_PostInfinity: 1
    m_RotationOrder: 4
  gradient:
    serializedVersion: 2
    key0: {r: 1, g: 0, b: 0, a: 1}
    key1: {r: 0, g: 0, b: 1, a: 0.5}
    key2: {r: 0, g: 0, b: 0, a: 0}
    key3: {r: 0, g: 0, b: 0, a: 0}
    key4: {r: 0, g: 0, b: 0, a: 0}
    key5: {r: 0, g: 0, b: 0, a: 0}
    key6: {r: 0, g: 0, b: 0, a: 0}
    key7: {r: 0, g: 0, b: 0, a: 0}
    ctime0: 0
    ctime1: 49151
    ctime2: 0
    ctime3: 0
    ctime4: 0
    ctime5: 0
    ctime6: 0
    ctime7: 0
    atime0: 0
    atime1: 65535
    atime2: 0
    atime3: 0
    atime4: 0
    atime5: 0
    atime6: 0
    atime7: 0
    m_Mode: 0
    m_ColorSpace: -1
    m_NumColorKeys: 2
    m_NumAlphaKeys: 2
  padding:
    m_Left: 1
    m_Right: 2
    m_Top: 3
    m_Bottom: 4
  area:
    serializedVersion: 2
    x: 0.5
    y: 1
    width: 10
    height: 20
  layers:
    serializedVersion: 2
    m_Bits: 33
  bounds:
    m_Center: {x: 1, y: 1, z: 1}
    m_Extent: {x: 1, y: 2, z: 3}
  turn: {x: 0, y: 0.70710677, z: 0, w: 0.70710677}
  cell: {x: -3, y: 4}
  hash:
    serializedVersion: 2
    Hash: d92598d9617fba07c8a8b1a37c69350a
  shade:
    serializedVersion: 2
    rgba: 673059850
  multiline: 'line one

    line two: "quoted"'
  empty: 
  texts:
  - "tab\there"
  - ' lead'
  - 'trail '
  - '#hash'
  - '- dash'
  - -dash
  - '{brace}'
  - '[x]'
  - yes
  - no
  - null
  - ~
  - 123
  - 1.5
  - true
  - a:b
  - 'a: b'
  - a #b
  - a#b
  - it's
  - '"dq"'
  - '@at'
  - '`tick'
  - '%pct'
  - '&amp'
  - '*star'
  - '!bang'
  - '|pipe'
  - '>gt'
  - ?q
  - ',comma'
  - 'end:'
  - "\xE9t\xE9"
  - "\u4E2D"
  - "cr\rlf"
  - 'two


    lines'
  - 
  - "x\x01y"
  - back\slash
  - 'trailing

'

""";

        static SavedKinds Kinds()
        {
            var kinds = ScriptableObject.CreateInstance<SavedKinds>();
            kinds.floats = new List<float> { 0.5f, -1e-5f, 3.4028235e38f, 1e5f, 1e6f, 1e7f, 1e8f, 1e9f, 1e15f, 1e16f, 1e17f, 123456789f, 1234567.9f, 16777216f, 1e-4f, 1e-6f, 1e-8f, 1e-9f, 1e-10f, 1.5e-8f, 1e-15f, 1e-16f, 1e-30f, 1.17549435e-38f, 1e-45f, float.PositiveInfinity, float.NegativeInfinity, float.NaN, float.MaxValue, float.Epsilon };
            kinds.flags = new[] { true, false, true };
            kinds.moods = new List<SavedMood> { SavedMood.Busy, SavedMood.Late };
            kinds.longs = new[] { -1L, 1L << 40 };
            kinds.bytes = new byte[] { 0, 15, 255 };
            kinds.doubles = new List<double> { 0.1, 1e-300, 1e5, 1e10, 1e15, 1e16, 1e17, 1e20, 1e21, 1e22, 123456789012345678, 1e-5, 1e-7, 1e-8, 1e-10, 1e-16, 1e-17, 0.1 + 0.2, double.MaxValue, double.Epsilon, double.NaN, double.NegativeInfinity };
            kinds.points = new List<Vector3> { new Vector3(1f, 2f, 3f) };
            kinds.pairs = new List<SavedPair> { new SavedPair { key = 1, value = 0.25f }, new SavedPair { key = 2, value = -4f } };
            kinds.nested = new SavedNested { title = "inner", pairs = new List<SavedPair> { new SavedPair { key = 3, value = 1f } } };
            kinds.nestedList = new List<SavedNested> { new SavedNested { title = "first" }, new SavedNested() };
            kinds.mood = SavedMood.Late;
            kinds.unsigned = 4000000000u;
            kinds.small = -7;
            kinds.letter = 'Z';
            // The Editor's TRS and Euler results, so the test is about their text, not the arithmetic.
            var m = new Matrix4x4();
            float[] e = { 0.8434933f, -0.83682406f, 0.16841204f, 1f, 0.49240386f, 1.7057371f, -0.08682408f, 2f, -0.21461019f, 0.62464905f, 0.4627083f, 3f, 0f, 0f, 0f, 1f };
            for (int i = 0; i < 16; i++) m[i / 4, i % 4] = e[i];
            kinds.matrix = m;
            kinds.curve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 1f), new Keyframe(1f, 2f, 0.5f, 0.5f) { weightedMode = WeightedMode.Both, inWeight = 0.2f, outWeight = 0.4f });
            kinds.curve.postWrapMode = WrapMode.Loop;
            kinds.gradient = new Gradient();
            kinds.gradient.SetKeys(new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 0.75f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 1f) });
            kinds.padding = new RectOffset(1, 2, 3, 4);
            kinds.area = new Rect(0.5f, 1f, 10f, 20f);
            kinds.layers = 1 << 5 | 1;
            kinds.bounds = new Bounds(new Vector3(1f, 1f, 1f), new Vector3(2f, 4f, 6f));
            kinds.turn = new Quaternion(0f, 0.70710677f, 0f, 0.70710677f);
            kinds.cell = new Vector2Int(-3, 4);
            kinds.hash = Hash128.Parse("d92598d9617fba07c8a8b1a37c69350a");
            kinds.shade = new Color32(10, 20, 30, 40);
            kinds.multiline = "line one\nline two: \"quoted\"";
            kinds.texts = new List<string> { "tab\there", " lead", "trail ", "#hash", "- dash", "-dash", "{brace}", "[x]", "yes", "no", "null", "~", "123", "1.5", "true", "a:b", "a: b", "a #b", "a#b", "it's", "\"dq\"", "@at", "`tick", "%pct", "&amp", "*star", "!bang", "|pipe", ">gt", "?q", ",comma", "end:", "\u00e9t\u00e9", "\u4e2d", "cr\rlf", "two\n\nlines", "", "x\u0001y", "back\\slash", "trailing\n" };
            return kinds;
        }

        static string Fields(string document)
        {
            int start = document.IndexOf("m_EditorClassIdentifier: \n", StringComparison.Ordinal);
            return document.Substring(start + "m_EditorClassIdentifier: \n".Length);
        }

        [Fact]
        public void ValuesAreWrittenAsTheEditorWritesThem()
        {
            var kinds = Kinds();
            try
            {
                Assert.Equal(EditorKinds, Fields(UnityYamlWriter.Write(new Object[] { kinds })));
            }
            finally { Object.DestroyImmediate(kinds); }
        }

        [Fact]
        public void WrittenValuesReadBackAsThemselves()
        {
            var kinds = Kinds();
            var copy = ScriptableObject.CreateInstance<SavedKinds>();
            try
            {
                var text = UnityYamlWriter.Write(new Object[] { kinds });
                new YamlReader(null).ReadScriptObject(copy, UnityYamlText.Parse(text)[0].Body.ToNode());
                Assert.Equal(kinds.floats, copy.floats);
                Assert.Equal(kinds.doubles, copy.doubles);
                Assert.Equal(kinds.flags, copy.flags);
                Assert.Equal(kinds.moods, copy.moods);
                Assert.Equal(kinds.longs, copy.longs);
                Assert.Equal(kinds.texts, copy.texts);
                Assert.Equal(kinds.multiline, copy.multiline);
                Assert.Equal(kinds.matrix, copy.matrix);
                Assert.Equal(kinds.curve.keys.Length, copy.curve.keys.Length);
                Assert.Equal(WrapMode.Loop, copy.curve.postWrapMode);
                Assert.Equal(kinds.gradient.colorKeys[1].time, copy.gradient.colorKeys[1].time, 4);
                Assert.Equal(kinds.hash, copy.hash);
                Assert.Equal(kinds.shade, copy.shade);
                Assert.Equal(kinds.bounds, copy.bounds);
            }
            finally
            {
                Object.DestroyImmediate(kinds);
                Object.DestroyImmediate(copy);
            }
        }

        [Theory]
        [InlineData(1e8f, "100000000")]
        [InlineData(1e9f, "1e+9")]
        [InlineData(123456789f, "123456790")]
        [InlineData(1e-9f, "0.000000001")]
        [InlineData(1e-10f, "1e-10")]
        [InlineData(1.5e-8f, "0.000000015")]
        [InlineData(-0f, "-0")]
        [InlineData(-2.5f, "-2.5")]
        [InlineData(3.4028235e38f, "3.4028235e+38")]
        [InlineData(1.17549435e-38f, "1.1754944e-38")]
        public void FloatsUseAnExponentOutsideTheirPrecision(float value, string text) => Assert.Equal(text, FloatText.Format(value));

        [Theory]
        [InlineData(1e16, "10000000000000000")]
        [InlineData(1e17, "1e+17")]
        [InlineData(1e-17, "0.00000000000000001")]
        [InlineData(1e-300, "1e-300")]
        [InlineData(123456789012345678.0, "1.2345678901234568e+17")]
        [InlineData(5e-324, "5e-324")]
        public void DoublesUseAnExponentOutsideTheirPrecision(double value, string text) => Assert.Equal(text, FloatText.Format(value));

        [Fact]
        public void LongPlainStringsContinueAtTheIndentation()
        {
            var words = string.Join(" ", new string[20]).Replace(" ", "word ").Trim();
            var text = UnityYamlWriter.Scalar(words, 8, 4);
            Assert.Contains("\n    word", text);
            Assert.DoesNotContain("\n", UnityYamlWriter.Scalar("short words only", 8, 4));
            // Where a value cannot span lines (a flow map) a line break is escaped.
            Assert.Equal("\"a\\nb\"", UnityYamlWriter.Scalar("a\nb"));
        }

        static string Rids(string text) => Regex.Replace(text, @"rid: (\d+)", m => "rid: R");

        static SavedGraph Graph()
        {
            var graph = ScriptableObject.CreateInstance<SavedGraph>();
            var shared = new SavedCircle { name = "shared", radius = 0.5f };
            graph.main = new SavedBox { name = "box", size = new Vector2(1f, 2f), inner = shared };
            graph.shapes = new List<SavedShape> { shared, shared, null, new SavedBox { name = "b2" } };
            return graph;
        }

        [Fact]
        public void ManagedReferencesAreWrittenWithTheirReferencesBlock()
        {
            var graph = Graph();
            try
            {
                var text = Fields(UnityYamlWriter.Write(new Object[] { graph }));
                var asm = typeof(SavedGraph).Assembly.GetName().Name;
                Assert.Equal($@"  main:
    rid: R
  shapes:
  - rid: R
  - rid: R
  - rid: -2
  - rid: R
  empty:
    rid: -2
  after: 1
  references:
    version: 2
    RefIds:
    - rid: -2
      type: {{class: , ns: , asm: }}
    - rid: R
      type: {{class: SavedBox, ns: UnityEngine.Tests, asm: {asm}}}
      data:
        name: box
        size: {{x: 1, y: 2}}
        inner:
          rid: R
    - rid: R
      type: {{class: SavedCircle, ns: UnityEngine.Tests, asm: {asm}}}
      data:
        name: shared
        radius: 0.5
    - rid: R
      type: {{class: SavedBox, ns: UnityEngine.Tests, asm: {asm}}}
      data:
        name: b2
        size: {{x: 0, y: 0}}
        inner:
          rid: -2
".Replace("\r", ""), Rids(text));
                // The shared node has one id, the others theirs, in the order they are first written.
                var ids = Regex.Matches(text, @"rid: (\d+)");
                Assert.Equal(ids[1].Groups[1].Value, ids[2].Groups[1].Value);
                Assert.True(long.Parse(ids[0].Groups[1].Value) < long.Parse(ids[1].Groups[1].Value));
                Assert.True(long.Parse(ids[1].Groups[1].Value) < long.Parse(ids[3].Groups[1].Value));
            }
            finally { Object.DestroyImmediate(graph); }
        }

        [Fact]
        public void ReadManagedReferencesKeepTheirIdsWhenWrittenAgain()
        {
            var graph = Graph();
            var copy = ScriptableObject.CreateInstance<SavedGraph>();
            try
            {
                var text = UnityYamlWriter.Write(new Object[] { graph });
                new YamlReader(null).ReadScriptObject(copy, UnityYamlText.Parse(text)[0].Body.ToNode());
                Assert.Same(copy.shapes[0], copy.shapes[1]);
                Assert.Same(((SavedBox)copy.main).inner, copy.shapes[0]);
                Assert.Null(copy.shapes[2]);
                Assert.Null(copy.empty);
                copy.name = graph.name;
                Assert.Equal(text, UnityYamlWriter.Write(new Object[] { copy }));
                // A node added since gets a new id, after the others.
                copy.shapes.Add(new SavedCircle { name = "new" });
                var again = UnityYamlWriter.Write(new Object[] { copy });
                var ids = Regex.Matches(again, @"- rid: (\d+)\n      type");
                Assert.Equal(4, ids.Count);
            }
            finally
            {
                Object.DestroyImmediate(graph);
                Object.DestroyImmediate(copy);
            }
        }

        [Fact]
        public void QuotedScalarsOverSeveralLinesAreOneValue()
        {
            const string text = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &1\nMonoBehaviour:\n  multiline: 'line one\n\n    line two: \"quoted\"'\n  texts:\n  - 'two\n\n\n    lines'\n  - 'trailing\n\n'\n  after: 1\n";
            var docs = UnityYamlText.Parse(text);
            var body = docs[0].Body;
            Assert.Equal(new[] { "multiline", "texts", "after" }, body.Keys);
            Assert.Equal("line one\nline two: \"quoted\"", body.Get("multiline").Text);
            Assert.Equal(2, body.Get("texts").Items.Count);
            Assert.Equal(text, UnityYamlText.Write(docs));
        }

        [Fact]
        public void AnOverlayKeepsWhatItDoesNotWrite()
        {
            var body = UnityYamlText.Parse("--- !u!108 &1\nLight:\n  m_Enabled: 1\n  m_Shadows:\n    m_Type: 2\n    m_Bias: 0.05\n  m_Cookie: {fileID: 7, guid: 0000000000000000e000000000000000, type: 0}\n  m_Unknown: 3\n")[0].Body;
            var written = UnityYamlText.Parse("--- !u!108 &1\nLight:\n  m_Shadows:\n    m_Type: 1\n  m_Cookie: " + UnityYamlWriter.Writer.UnlocatedReference + "\n  m_Added: 4\n")[0].Body;
            SceneFiles.Overlay(body, written);
            Assert.Equal(new[] { "m_Enabled", "m_Shadows", "m_Cookie", "m_Added", "m_Unknown" }, body.Keys);
            Assert.Equal("1", body.Get("m_Shadows").Get("m_Type").Raw);
            Assert.Equal("0.05", body.Get("m_Shadows").Get("m_Bias").Raw);
            // A reference Unbound cannot name keeps the file's.
            Assert.Equal("7", body.Get("m_Cookie").Get("fileID").Text);
            // With no file value to keep, it is written as none.
            var fresh = UnityYamlText.Parse("--- !u!108 &1\nLight:\n  m_Enabled: 1\n")[0].Body;
            int unlocated = 0;
            SceneFiles.Overlay(fresh, written, ref unlocated);
            Assert.Equal(1, unlocated);
            Assert.Equal("{fileID: 0}", UnityYamlText.FlowText(fresh.Get("m_Cookie")));
        }
    }
}
