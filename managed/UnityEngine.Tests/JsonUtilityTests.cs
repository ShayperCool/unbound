using System;
using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    [Serializable]
    public class JsonEntry
    {
        public string key;
        public string value;
    }

    [Serializable]
    public class JsonTable
    {
        [SerializeField] public List<JsonEntry> elements = new List<JsonEntry>();
    }

    [Serializable]
    public class JsonState : ISerializationCallbackReceiver
    {
        public JsonTable configs;
        public JsonEntry[] data = new JsonEntry[2];
        public int count = 5;
        public float ratio = 0.1f;
        public bool flag;
        public Vector3 position;
        public Color tint = Color.white;
        public LayerMask mask;
        public DayOfWeek day = DayOfWeek.Monday;
        private int hidden = 9;
        [NonSerialized] public int skipped = 3;
        public int afterDeserialize;

        public int Hidden => hidden;
        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() { afterDeserialize++; }
    }

    public class JsonBehaviour : MonoBehaviour
    {
        public int speed = 2;
        public string title = "x";
    }

    public class JsonUtilityTests
    {
        [Fact]
        public void WritesFloatsThroughDoublePrecisionAsUnityDoes()
        {
            var json = JsonUtility.ToJson(new Vector3(0.1f, 0f, 1f));
            Assert.Equal("{\"x\":0.10000000149011612,\"y\":0.0,\"z\":1.0}", json);
        }

        [Fact]
        public void WritesSerializedFieldsOnly()
        {
            var json = JsonUtility.ToJson(new JsonState());
            Assert.Contains("\"count\":5", json);
            Assert.Contains("\"flag\":false", json);
            Assert.Contains("\"day\":1", json);
            Assert.Contains("\"mask\":{\"serializedVersion\":\"2\",\"m_Bits\":0}", json);
            Assert.DoesNotContain("hidden", json);
            Assert.DoesNotContain("skipped", json);
            // A null serializable class is written as a default instance; null arrays elements too.
            Assert.Contains("\"configs\":{\"elements\":[]}", json);
            Assert.Contains("\"data\":[{\"key\":\"\",\"value\":\"\"},{\"key\":\"\",\"value\":\"\"}]", json);
        }

        [Fact]
        public void ReadsWhatItWrites()
        {
            var state = new JsonState { count = 42, flag = true, position = new Vector3(1, 2, 3), tint = Color.red };
            state.configs = new JsonTable();
            state.configs.elements.Add(new JsonEntry { key = "a", value = "[{\"q\":1}]" });
            var back = JsonUtility.FromJson<JsonState>(JsonUtility.ToJson(state, true));
            Assert.Equal(42, back.count);
            Assert.True(back.flag);
            Assert.Equal(new Vector3(1, 2, 3), back.position);
            Assert.Equal(Color.red, back.tint);
            Assert.Equal("[{\"q\":1}]", back.configs.elements[0].value);
            Assert.Equal(1, back.afterDeserialize);
        }

        [Fact]
        public void ReadsTheRemoteConfigShape()
        {
            const string json = "{\"configs\":{\"elements\":[{\"key\":\"BannerEnabled\",\"value\":\"0\"}]},\"data\":[{\"key\":\"None\",\"value\":\"Base\"}]}";
            var state = JsonUtility.FromJson<JsonState>(json);
            Assert.NotNull(state);
            Assert.Equal("BannerEnabled", state.configs.elements[0].key);
            Assert.Single(state.data);
            Assert.Equal(5, state.count); // missing fields keep their initial values
        }

        [Fact]
        public void PrettyPrintsWithFourSpaces()
        {
            var json = JsonUtility.ToJson(new Vector2(1, 2), true);
            Assert.Equal("{\n    \"x\": 1.0,\n    \"y\": 2.0\n}", json);
        }

        [Fact]
        public void EmptyInputGivesNullAndEngineTypesCannotBeCreated()
        {
            Assert.Null(JsonUtility.FromJson<JsonState>(""));
            Assert.Throws<ArgumentException>(() => JsonUtility.FromJson<JsonBehaviour>("{}"));
            Assert.Throws<ArgumentException>(() => JsonUtility.FromJson<JsonState>("{\"count\":"));
        }

        [Fact]
        public void OverwritesBehaviours()
        {
            var go = new GameObject("json");
            var b = go.AddComponent<JsonBehaviour>();
            JsonUtility.FromJsonOverwrite("{\"speed\":7}", b);
            Assert.Equal(7, b.speed);
            Assert.Equal("x", b.title);
            Assert.Equal("{\"speed\":7,\"title\":\"x\"}", JsonUtility.ToJson(b));
            Object.DestroyImmediate(go);
        }

        [Serializable]
        class CurveGradient
        {
            public Gradient gradient = new Gradient();
            public AnimationCurve curve = new AnimationCurve();
            public int after;
        }

        /// <summary>The Editor's text for the holder below (tests/projects/builtinserial `json.curveGradient`).</summary>
        const string CurveGradientText =
                "{\"gradient\":{\"serializedVersion\":\"2\",\"key0\":{\"r\":1.0,\"g\":0.5,\"b\":0.25,\"a\":1.0},\"key1\":{\"r\":0.0,\"g\":0.0,\"b\":0.0,\"a\":0.5},\"ke" +
                "y2\":{\"r\":0.20000000298023225,\"g\":0.4000000059604645,\"b\":0.6000000238418579,\"a\":0.0},\"key3\":{\"r\":0.0,\"g\":0.0,\"b\":0.0,\"a\":0.0},\"key4" +
                "\":{\"r\":0.0,\"g\":0.0,\"b\":0.0,\"a\":0.0},\"key5\":{\"r\":0.0,\"g\":0.0,\"b\":0.0,\"a\":0.0},\"key6\":{\"r\":0.0,\"g\":0.0,\"b\":0.0,\"a\":0.0},\"k" +
                "ey7\":{\"r\":0.0,\"g\":0.0,\"b\":0.0,\"a\":0.0},\"ctime0\":16384,\"ctime1\":49151,\"ctime2\":65535,\"ctime3\":0,\"ctime4\":0,\"ctime5\":0,\"ctime6\":0" +
                ",\"ctime7\":0,\"atime0\":0,\"atime1\":65535,\"atime2\":0,\"atime3\":0,\"atime4\":0,\"atime5\":0,\"atime6\":0,\"atime7\":0,\"m_Mode\":1,\"m_ColorSpace" +
                "\":1,\"m_NumColorKeys\":3,\"m_NumAlphaKeys\":2},\"curve\":{\"serializedVersion\":\"2\",\"m_Curve\":[{\"serializedVersion\":\"3\",\"time\":0.0,\"value\"" +
                ":0.0,\"inSlope\":0.0,\"outSlope\":1.0,\"tangentMode\":0,\"weightedMode\":0,\"inWeight\":0.0,\"outWeight\":0.0},{\"serializedVersion\":\"3\",\"time\":1" +
                ".0,\"value\":2.0,\"inSlope\":0.5,\"outSlope\":0.5,\"tangentMode\":0,\"weightedMode\":3,\"inWeight\":0.25,\"outWeight\":0.75}],\"m_PreInfinity\":1,\"m_" +
                "PostInfinity\":0,\"m_RotationOrder\":4},\"after\":4}";

        static CurveGradient MakeCurveGradient()
        {
            var g = new Gradient { mode = GradientMode.Fixed, colorSpace = ColorSpace.Linear };
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.5f, 0.25f), 0.25f), new GradientColorKey(Color.black, 0.75f), new GradientColorKey(new Color(0.2f, 0.4f, 0.6f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 1f) });
            var c = new AnimationCurve(new Keyframe(0f, 0f, 0f, 1f), new Keyframe(1f, 2f, 0.5f, 0.5f, 0.25f, 0.75f) { weightedMode = WeightedMode.Both })
            {
                preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.PingPong,
            };
            return new CurveGradient { gradient = g, curve = c, after = 4 };
        }

        [Fact]
        public void GradientAndCurveFieldsAreWrittenAsTheEditorWritesThem() =>
            Assert.Equal(CurveGradientText, JsonUtility.ToJson(MakeCurveGradient()));

        [Fact]
        public void GradientAndCurveFieldsAreReadBack()
        {
            var read = JsonUtility.FromJson<CurveGradient>(CurveGradientText);
            Assert.Equal(GradientMode.Fixed, read.gradient.mode);
            Assert.Equal(ColorSpace.Linear, read.gradient.colorSpace);
            var colors = read.gradient.colorKeys;
            Assert.Equal(3, colors.Length);
            // Times are stored in 1/65535: 0.25 comes back as 16384/65535.
            Assert.Equal(16384f / 65535f, colors[0].time);
            Assert.Equal(new Color(0.2f, 0.4f, 0.6f, 1f), colors[2].color);
            var alphas = read.gradient.alphaKeys;
            Assert.Equal(new[] { 1f, 0.5f }, new[] { alphas[0].alpha, alphas[1].alpha });
            Assert.Equal(WrapMode.Loop, read.curve.preWrapMode);
            Assert.Equal(WrapMode.PingPong, read.curve.postWrapMode);
            var keys = read.curve.keys;
            Assert.Equal(2, keys.Length);
            Assert.Equal((1f, 2f, 0.5f, 0.5f, 0.25f, 0.75f, WeightedMode.Both),
                (keys[1].time, keys[1].value, keys[1].inTangent, keys[1].outTangent, keys[1].inWeight, keys[1].outWeight, keys[1].weightedMode));
            Assert.Equal(4, read.after);
            // Outside a field neither is written.
            Assert.Equal("{}", JsonUtility.ToJson(read.gradient));
            Assert.Equal("{}", JsonUtility.ToJson(read.curve));
        }
    }
}
