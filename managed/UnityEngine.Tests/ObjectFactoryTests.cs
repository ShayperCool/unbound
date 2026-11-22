using System;
using System.Collections.Generic;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    [RequireComponent(typeof(Rigidbody))]
    public class FactoryNeedsBody : MonoBehaviour { }

    [DisallowMultipleComponent]
    public class FactoryOnlyOne : MonoBehaviour { }

    /// <summary>ObjectFactory, one-of-a-kind engine components and undo group names as the Unity
    /// 6000.0.71f1 Editor has them in Play mode (tests/projects/objectfactory,
    /// tests/projects/uniquecomponents, tests/projects/editor).</summary>
    [Collection("Frames")]
    public sealed class ObjectFactoryTests
    {
        [Fact]
        public void AddedComponentsRaiseTheEventRequiredOnesFirstAndTakeTheHideFlags()
        {
            var added = new List<string>();
            Action<Component> onAdded = c => added.Add(c.GetType().Name);
            ObjectFactory.componentWasAdded += onAdded;
            var holder = new GameObject("Scene probe");
            GameObject go = null;
            try
            {
                go = ObjectFactory.CreateGameObject(holder.scene, HideFlags.DontSave, "C", typeof(Light));
                Assert.Equal(new[] { "Transform", "Light" }, added);
                added.Clear();
                var body = ObjectFactory.AddComponent<FactoryNeedsBody>(go);
                Assert.Equal(new[] { "Rigidbody", "FactoryNeedsBody" }, added);
                Assert.Equal(HideFlags.DontSave, body.hideFlags);
                Assert.Equal(HideFlags.DontSave, go.GetComponent<Rigidbody>().hideFlags);
            }
            finally
            {
                ObjectFactory.componentWasAdded -= onAdded;
                if (go != null) Object.DestroyImmediate(go);
                Object.DestroyImmediate(holder);
            }
        }

        [Fact]
        public void ASecondComponentOfAOneOfAKindTypeThrowsWithoutALog()
        {
            var go = new GameObject("A");
            try
            {
                var logged = new List<string>();
                Application.LogCallback onLog = (message, stack, type) => logged.Add(type + ": " + message);
                Application.logMessageReceived += onLog;
                try
                {
                ObjectFactory.AddComponent<Rigidbody>(go);
                var native = Assert.Throws<ArgumentException>(() => ObjectFactory.AddComponent<Rigidbody>(go));
                Assert.Equal("Can't add component 'Rigidbody' to A because such a component is already added to the game object!", native.Message);
                ObjectFactory.AddComponent<FactoryOnlyOne>(go);
                var script = Assert.Throws<ArgumentException>(() => ObjectFactory.AddComponent<FactoryOnlyOne>(go));
                Assert.Equal("Can't add '<NULL>' to A because a 'FactoryOnlyOne' is already added to the game object!", script.Message);
                }
                finally { Application.logMessageReceived -= onLog; }
                Assert.Empty(logged);
                // Colliders are not one of a kind.
                ObjectFactory.AddComponent<BoxCollider>(go);
                ObjectFactory.AddComponent<BoxCollider>(go);
                Assert.Equal(2, go.GetComponents<BoxCollider>().Length);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void GameObjectAddComponentRefusesASecondOneOfAKindComponent()
        {
            var go = new GameObject("U");
            try
            {
                var logged = new List<string>();
                Application.LogCallback onLog = (message, stack, type) => logged.Add(type + ": " + message);
                Application.logMessageReceived += onLog;
                try
                {
                    Assert.NotNull(go.AddComponent<Camera>());
                    Assert.Null(go.AddComponent<Camera>());
                }
                finally { Application.logMessageReceived -= onLog; }
                Assert.Single(go.GetComponents<Camera>());
                Assert.Equal(new[] { "Log: Can't add component 'Camera' to U because such a component is already added to the game object!" }, logged);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void SiblingNamesTakeTheLowestFreeNumberFromTheirOwn()
        {
            var made = new List<GameObject>();
            GameObject Make(string name) { var go = new GameObject(name); made.Add(go); return go; }
            try
            {
                Make("Gap"); Make("Gap (2)"); Make("Item (5)"); Make("Big (99999999999)");
                // tests/projects/objectfactory unique.*: a gap is filled, a numbered name counts on.
                Assert.Equal("Gap (1)", GameObjectUtility.GetUniqueNameForSibling(null, "Gap"));
                Assert.Equal("Item (6)", GameObjectUtility.GetUniqueNameForSibling(null, "Item (5)"));
                Assert.Equal("Fresh", GameObjectUtility.GetUniqueNameForSibling(null, "Fresh"));
                // A number too long to read is part of the name (Unbound's choice; unmeasured).
                Assert.Equal("Big (99999999999) (1)", GameObjectUtility.GetUniqueNameForSibling(null, "Big (99999999999)"));
                // An object does not clash with itself.
                var solo = Make("Solo");
                GameObjectUtility.EnsureUniqueNameForSibling(solo);
                Assert.Equal("Solo", solo.name);
                var parent = Make("Parent of children");
                var first = Make("Child"); first.transform.SetParent(parent.transform, false);
                var second = Make("Child"); second.transform.SetParent(parent.transform, false);
                GameObjectUtility.EnsureUniqueNameForSibling(second);
                Assert.Equal("Child (1)", second.name);
            }
            finally
            {
                foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
            }
        }

        [Fact]
        public void PlacingUnderAUiParentTakesItsLayerAndARectTransformAndResetsTheTransform()
        {
            var parent = new GameObject("UiParent", typeof(RectTransform)) { layer = 5 };
            try
            {
                var child = ObjectFactory.CreateGameObject("UiChild");
                child.transform.localScale = new Vector3(2, 3, 4);
                ObjectFactory.PlaceGameObject(child, parent);
                Assert.Same(parent.transform, child.transform.parent);
                Assert.IsType<RectTransform>(child.transform);
                Assert.Equal(5, child.layer);
                Assert.Equal(Vector3.one, child.transform.localScale);
                Assert.Same(child, Selection.activeGameObject);
                // A second RectTransform is one too many (objectfactory placed.rectTwice).
                var twice = ObjectFactory.CreateGameObject("UiTwice", typeof(RectTransform));
                var refused = Assert.Throws<ArgumentException>(() => ObjectFactory.PlaceGameObject(twice, parent));
                Assert.Equal("Can't add component 'RectTransform' to UiTwice because such a component is already added to the game object!", refused.Message);
                // It throws after reparenting (objectfactory placed.rectTwice.components).
                Assert.Same(parent.transform, twice.transform.parent);
            }
            finally { Object.DestroyImmediate(parent); }
        }
    }
}
