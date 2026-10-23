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
    }
}
