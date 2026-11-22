using System;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    [CollectionDefinition("Scene visibility", DisableParallelization = true)]
    public sealed class SceneVisibilityCollection { }

    /// <summary>SceneVisibilityManager as the Unity 6000.0.71f1 Editor answers it in Play mode
    /// (tests/projects/scenevisibility): own and descendant states, isolation, picking, events.</summary>
    [Collection("Scene visibility")]
    public sealed class SceneVisibilityTests
    {
        static SceneVisibilityManager Manager => SceneVisibilityManager.instance;

        static (GameObject A, GameObject A1, GameObject A2, GameObject A2a) Hierarchy()
        {
            var a = new GameObject("A");
            var a1 = new GameObject("A1");
            var a2 = new GameObject("A2");
            var a2a = new GameObject("A2a");
            a1.transform.SetParent(a.transform, false);
            a2.transform.SetParent(a.transform, false);
            a2a.transform.SetParent(a2.transform, false);
            return (a, a1, a2, a2a);
        }

        static string Bits(GameObject go) =>
            (Manager.IsHidden(go) ? "1" : "0") + (Manager.IsHidden(go, true) ? "1" : "0")
            + (Manager.AreAllDescendantsHidden(go) ? "1" : "0") + (Manager.AreAllDescendantsVisible(go) ? "1" : "0");

        [Fact]
        public void DescendantAnswersLeaveTheObjectOut()
        {
            var (a, a1, a2, a2a) = Hierarchy();
            try
            {
                Manager.Hide(a, false);
                Assert.Equal("1001", Bits(a));
                Manager.Hide(a, true);
                Assert.Equal("1110", Bits(a));
                // A hidden object without children: no hidden descendants, all of them visible.
                Assert.Equal("1101", Bits(a1));
                Manager.Show(a1, false);
                Assert.Equal("1000", Bits(a));
                Manager.ToggleVisibility(a2, true);
                Assert.Equal("0001", Bits(a2));
                Assert.Equal("0001", Bits(a2a));
                Assert.Throws<ArgumentNullException>(() => Manager.Hide((GameObject)null, false));
                Assert.Throws<ArgumentNullException>(() => Manager.Hide((GameObject[])null, false));
            }
            finally
            {
                Manager.Show(a, true);
                Object.DestroyImmediate(a);
            }
        }

        [Fact]
        public void PickingIsKeptApartFromVisibility()
        {
            var (a, a1, a2, a2a) = Hierarchy();
            try
            {
                Manager.DisablePicking(a, true);
                Manager.EnablePicking(a2, false);
                Assert.True(Manager.IsPickingDisabled(a));
                Assert.False(Manager.IsPickingDisabled(a, true));
                Assert.True(Manager.IsPickingDisabledOnAllDescendants(a2));
                Assert.False(Manager.IsPickingEnabledOnAllDescendants(a));
                Assert.False(Manager.IsHidden(a));
                Assert.False(Manager.IsSelectable(a1));
                Manager.TogglePicking(a1, false);
                Assert.True(Manager.IsSelectable(a1));
            }
            finally
            {
                Manager.EnablePicking(a, true);
                Object.DestroyImmediate(a);
            }
        }

        [Fact]
        public void IsolationHidesTheRestUntilItEndsAndShowingASceneRaisesVisibilityChanged()
        {
            var (a, a1, a2, a2a) = Hierarchy();
            var b = new GameObject("B");
            int visibility = 0, picking = 0;
            var isolated = new System.Collections.Generic.List<bool>();
            Action onVisibility = () => visibility++, onPicking = () => picking++;
            Action<bool> onIsolated = isolated.Add;
            SceneVisibilityManager.visibilityChanged += onVisibility;
            SceneVisibilityManager.pickingChanged += onPicking;
            SceneVisibilityManager.currentStageIsIsolated += onIsolated;
            try
            {
                Manager.Hide(b, false);
                Manager.Isolate(a1, false);
                Assert.True(Manager.IsCurrentStageIsolated());
                Assert.True(Manager.IsHidden(a));
                Assert.False(Manager.IsHidden(a1));
                Assert.True(Manager.IsHidden(a2a));
                Manager.Show(b, false);
                Manager.ExitIsolation();
                // The visibility from before isolation: only B hidden.
                Assert.False(Manager.IsHidden(a));
                Assert.False(Manager.IsHidden(a2a));
                Assert.True(Manager.IsHidden(b));
                Assert.Equal(new[] { true, false }, isolated);
                Manager.HideAll();
                Manager.ShowAll();
                Assert.Equal(0, visibility);
                Manager.Hide(a.scene);
                Assert.True(Manager.IsHidden(a.scene));
                Assert.True(Manager.AreAnyDescendantsHidden(a.scene));
                Manager.Show(a.scene);
                Assert.Equal(1, visibility);
                Assert.False(Manager.AreAnyDescendantsHidden(a.scene));
                Manager.DisableAllPicking();
                Manager.EnableAllPicking();
                Assert.Equal(0, picking);
            }
            finally
            {
                SceneVisibilityManager.visibilityChanged -= onVisibility;
                SceneVisibilityManager.pickingChanged -= onPicking;
                SceneVisibilityManager.currentStageIsIsolated -= onIsolated;
                Manager.ExitIsolation();
                Manager.Show(new[] { a, b }, true);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }
    }
}
