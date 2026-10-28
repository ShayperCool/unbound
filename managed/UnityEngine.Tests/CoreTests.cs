using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unbound.Runtime;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    public class Recorder : MonoBehaviour
    {
        public static readonly List<string> Calls = new List<string>();
        public int value = 3;
        public Recorder other;
        [SerializeField] List<int> list = new List<int> { 1, 2 };
        int notSerialized = 7;

        public List<int> List => list;
        public int NotSerialized { get => notSerialized; set => notSerialized = value; }

        void Awake() => Calls.Add($"{name}.Awake");
        void OnEnable() => Calls.Add($"{name}.OnEnable");
        void Start() => Calls.Add($"{name}.Start");
        void Update() => Calls.Add($"{name}.Update");
        void OnDisable() => Calls.Add($"{name}.OnDisable");
        void OnDestroy() => Calls.Add($"{name}.OnDestroy");
    }

    public class CoreTests
    {
        [Fact]
        public void Md4MatchesTheRfcVectors()
        {
            Assert.Equal("a448017aaf21d8525fc10ae87aa6729d", Hex(Md4.Hash(Encoding.ASCII.GetBytes("abc"))));
            Assert.Equal("31d6cfe0d16ae931b73c59d7e0c089c0", Hex(Md4.Hash(new byte[0])));
            Assert.Equal("d79e1c308aa5bbcdeea8ed63df412da9", Hex(Md4.Hash(Encoding.ASCII.GetBytes("abcdefghijklmnopqrstuvwxyz"))));
        }

        static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder();
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>Queries by type return arrays of that type, as the 6000.0 Editor does (I2
        /// Localization casts them); Resources.LoadAll returns Object[].</summary>
        [Fact]
        public void QueriesByTypeReturnArraysOfThatType()
        {
            var go = new GameObject("typed", typeof(Recorder));
            try
            {
                Assert.IsType<Recorder[]>(Object.FindObjectsOfType(typeof(Recorder)));
                Assert.IsType<Recorder[]>(Object.FindObjectsOfType(typeof(Recorder), true));
                Assert.IsType<Recorder[]>(Object.FindObjectsByType(typeof(Recorder), FindObjectsSortMode.None));
                Assert.IsType<Recorder[]>(Resources.FindObjectsOfTypeAll(typeof(Recorder)));
                Assert.IsType<Rigidbody[]>(Resources.FindObjectsOfTypeAll(typeof(Rigidbody)));
                Assert.NotEmpty((Recorder[])Resources.FindObjectsOfTypeAll(typeof(Recorder)));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void InstantiatingASpriteCopiesItOverTheSameTexture()
        {
            var texture = new Texture2D(8, 8);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 8), new Vector2(0.5f, 0.5f));
            sprite.name = "icon";
            var copy = Object.Instantiate(sprite);
            Assert.NotSame(sprite, copy);
            Assert.Equal("icon(Clone)", copy.name);
            Assert.Same(texture, copy.texture);
            Assert.Equal(sprite.rect, copy.rect);
        }

        [Fact]
        public void LifecycleRunsInUnitysOrder()
        {
            Recorder.Calls.Clear();
            var go = new GameObject("a");
            var r = go.AddComponent<Recorder>();
            Assert.Equal(new[] { "a.Awake", "a.OnEnable" }, Recorder.Calls);
            Lifecycle.RunPendingStarts();
            Lifecycle.RunUpdate();
            Assert.Equal(new[] { "a.Awake", "a.OnEnable", "a.Start", "a.Update" }, Recorder.Calls);
            Recorder.Calls.Clear();
            go.SetActive(false);
            Assert.Equal(new[] { "a.OnDisable" }, Recorder.Calls);
            Recorder.Calls.Clear();
            Object.DestroyImmediate(go);
            Assert.Equal(new[] { "a.OnDestroy" }, Recorder.Calls);
            Assert.True(r == null);
            Assert.False(ReferenceEquals(r, null));
        }

        [Fact]
        public void InactiveObjectsWaitForAwake()
        {
            Recorder.Calls.Clear();
            var go = new GameObject("b");
            go.SetActive(false);
            go.AddComponent<Recorder>();
            Assert.Empty(Recorder.Calls);
            go.SetActive(true);
            Assert.Equal(new[] { "b.Awake", "b.OnEnable" }, Recorder.Calls);
            Object.DestroyImmediate(go);
        }

        [Fact]
        public void ChildQueriesStartFromTheObjectAsked()
        {
            // Unity always searches the object asked; below it, objects inactive by their own flag
            // hide their subtrees. A deactivated root still finds its active children's components.
            var root = new GameObject("root");
            var child = new GameObject("child");
            var hidden = new GameObject("hidden");
            child.transform.SetParent(root.transform);
            hidden.transform.SetParent(root.transform);
            var shown = child.AddComponent<BoxCollider>();
            hidden.AddComponent<SphereCollider>();
            hidden.SetActive(false);
            root.SetActive(false);
            Assert.Same(shown, root.GetComponentInChildren<BoxCollider>());
            Assert.Null(root.GetComponentInChildren<SphereCollider>());
            Assert.NotNull(root.GetComponentInChildren<SphereCollider>(true));
            Assert.Single(root.GetComponentsInChildren<Collider>());
            Assert.Equal(2, root.GetComponentsInChildren<Collider>(true).Length);
            Object.DestroyImmediate(root);
        }

        [Fact]
        public void InstantiateCopiesSerializedStateAndRemapsReferences()
        {
            Recorder.Calls.Clear();
            var root = new GameObject("root");
            var child = new GameObject("child");
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = new Vector3(1, 2, 3);
            var a = root.AddComponent<Recorder>();
            var b = child.AddComponent<Recorder>();
            a.value = 42;
            a.other = b;
            a.List.Add(3);
            a.NotSerialized = 99;

            var clone = Object.Instantiate(root);
            Assert.Equal("root(Clone)", clone.name);
            var ca = clone.GetComponent<Recorder>();
            var cb = clone.transform.GetChild(0).GetComponent<Recorder>();
            Assert.Equal(42, ca.value);
            Assert.Same(cb, ca.other);
            Assert.Equal(new[] { 1, 2, 3 }, ca.List);
            Assert.NotSame(a.List, ca.List);
            Assert.Equal(7, ca.NotSerialized);
            Assert.Equal(new Vector3(1, 2, 3), cb.transform.localPosition);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(clone);
        }

        [Fact]
        public void WorldTransformsFollowParents()
        {
            var parent = new GameObject("p").transform;
            var child = new GameObject("c").transform;
            child.SetParent(parent, false);
            parent.position = new Vector3(10, 0, 0);
            parent.rotation = Quaternion.Euler(0, 90, 0);
            child.localPosition = new Vector3(0, 0, 1);
            var p = child.position;
            Assert.True((p - new Vector3(11, 0, 0)).magnitude < 1e-4f, p.ToString());
            child.SetParent(null, true);
            Assert.True((child.position - new Vector3(11, 0, 0)).magnitude < 1e-4f);
            Object.DestroyImmediate(parent.gameObject);
            Object.DestroyImmediate(child.gameObject);
        }

        [Fact]
        public void RectTransformPlacesItsRectFromAnchors()
        {
            var parent = new GameObject("p").AddComponent<RectTransform>();
            parent.sizeDelta = new Vector2(100, 100);
            var child = new GameObject("c").AddComponent<RectTransform>();
            child.SetParent(parent, false);
            child.anchorMin = Vector2.zero;
            child.anchorMax = Vector2.one;
            child.sizeDelta = new Vector2(-20, -20);
            Assert.Equal(new Rect(-40, -40, 80, 80), child.rect);
            child.anchorMin = child.anchorMax = new Vector2(0.5f, 0.5f);
            child.sizeDelta = new Vector2(10, 10);
            child.anchoredPosition = new Vector2(5, 0);
            Assert.Equal(new Vector3(5, 0, 0), child.localPosition);
            child.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 10, 30);
            Assert.Equal(new Vector2(25, 0), new Vector2(child.anchoredPosition.x, 0));
            Object.DestroyImmediate(parent.gameObject);
        }
    }
}
