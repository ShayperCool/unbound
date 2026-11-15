using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    public class RectDimensionMessageTests
    {
        sealed class Listener : MonoBehaviour
        {
            internal List<string> events;
            internal Action onRect;
            void OnRectTransformDimensionsChange() { events?.Add(name+".rect"); onRect?.Invoke(); }
            void OnChildRectTransformDimensionsChange() { events?.Add(name+".child"); }
        }
        static RectTransform Rect(string name, Transform parent, List<string> events, bool stretch = false)
        {
            var owner = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)owner.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(100, 80);
            if (stretch) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero; }
            owner.AddComponent<Listener>().events = events;
            return rect;
        }

        [Fact]
        public void OnlyTheFlaggedDirectParentReceivesChildDimensionMessages()
        {
            var events = new List<string>();
            var grand = Rect("grand", null, events);
            var parent = Rect("parent", grand, events);
            var child = Rect("child", parent, events);
            GameObject clone = null;
            try
            {
                events.Clear();
                Assert.False(parent.sendChildDimensionsChange);
                grand.sendChildDimensionsChange = true;
                Assert.Empty(events);
                child.sizeDelta += Vector2.one;
                Assert.Equal(new[] { "child.rect" }, events);
                events.Clear();
                parent.sendChildDimensionsChange = true;
                parent.GetComponent<Listener>().enabled = false;
                child.sizeDelta += Vector2.one;
                Assert.Equal(new[] { "child.rect", "parent.child" }, events);
                parent.gameObject.SetActive(false);
                events.Clear();
                child.sizeDelta += Vector2.one;
                Assert.Equal(new[] { "child.rect", "parent.child" }, events);
                parent.gameObject.SetActive(true);
                child.sendChildDimensionsChange = true;
                clone = Object.Instantiate(child.gameObject);
                Assert.False(clone.GetComponent<RectTransform>().sendChildDimensionsChange);
            }
            finally { Object.DestroyImmediate(grand.gameObject); if (clone != null) Object.DestroyImmediate(clone); }
        }

        [Fact]
        public void DimensionListenersSeeUpdatedDescendantMatricesBeforeLaterSiblingTraversal()
        {
            var events = new List<string>();
            var parent = Rect("parent", null, events);
            var child = Rect("child", parent, events, true);
            var leaf = Rect("leaf", child, events);
            leaf.anchorMin = leaf.anchorMax = Vector2.one;
            var sibling = Rect("sibling", parent, events);
            sibling.anchorMin = sibling.anchorMax = Vector2.zero;
            Vector3 leafInCallback = default, siblingInCallback = default;
            bool called = false;
            try
            {
                Assert.Equal(new Vector3(50, 40, 0), leaf.localToWorldMatrix.MultiplyPoint3x4(Vector3.zero));
                Assert.Equal(new Vector3(-50, -40, 0), sibling.localToWorldMatrix.MultiplyPoint3x4(Vector3.zero));
                child.GetComponent<Listener>().onRect = () =>
                {
                    called = true;
                    leafInCallback = leaf.localToWorldMatrix.MultiplyPoint3x4(Vector3.zero);
                    siblingInCallback = sibling.localToWorldMatrix.MultiplyPoint3x4(Vector3.zero);
                };
                parent.sizeDelta += new Vector2(50, 20);
                Assert.True(called);
                Assert.Equal(new Vector3(75, 50, 0), leafInCallback);
                Assert.Equal(new Vector3(-50, -40, 0), siblingInCallback);
                Assert.Equal(new Vector3(-75, -50, 0), sibling.localToWorldMatrix.MultiplyPoint3x4(Vector3.zero));
            }
            finally { Object.DestroyImmediate(parent.gameObject); }
        }

        [Fact]
        public void ReparentingInsideTheRectCallbackNotifiesTheCurrentParent()
        {
            var events = new List<string>();
            var oldParent = Rect("old", null, events);
            var newParent = Rect("new", null, events);
            var child = Rect("child", oldParent, events);
            try
            {
                oldParent.sendChildDimensionsChange = newParent.sendChildDimensionsChange = true;
                events.Clear();
                child.GetComponent<Listener>().onRect = () => child.SetParent(newParent, false);
                child.sizeDelta += Vector2.one;
                Assert.Contains("new.child", events);
                Assert.DoesNotContain("old.child", events);
                events.Clear();
                child.GetComponent<Listener>().onRect = null;
                child.sizeDelta = Vector2.zero;
                child.sizeDelta = new Vector2(-10, -20);
                Assert.Equal(new[] { "child.rect", "new.child", "child.rect", "new.child" }, events);
            }
            finally { Object.DestroyImmediate(oldParent.gameObject); Object.DestroyImmediate(newParent.gameObject); }
        }
    }
}
