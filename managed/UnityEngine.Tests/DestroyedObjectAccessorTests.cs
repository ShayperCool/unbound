using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Reading state from destroyed objects. Measured in Unity 6000.0.71f1 by
    /// tests/projects/destroycontract after a manual Destroy is delivered: GameObject.activeSelf,
    /// activeInHierarchy and GetComponent&lt;T&gt;() of a destroyed GameObject, and Behaviour.enabled
    /// and isActiveAndEnabled of a destroyed script, throw MissingReferenceException. Between the
    /// request and its delivery the objects still answer (the script reads enabled=false), and the
    /// reentrant request's OnDisable still reaches its gameObject. A live GameObject whose script
    /// alone was destroyed answers GetComponent with null.
    /// DestroyImmediate reaches the same destroyed state without running the delivery points.
    /// Not covered by the reference, and kept as before: TryGetComponent, and reads inside
    /// OnDestroy (the object is destroyed only after it).</summary>
    [Collection("Frames")]
    public class DestroyedObjectAccessorTests
    {
        public class Probe : MonoBehaviour
        {
            internal readonly List<string> Seen = new List<string>();
            internal bool ReadOnDisable, ReadOnDestroy;

            internal string Snapshot()
            {
                var go = gameObject;
                return $"activeSelf={go.activeSelf} activeInHierarchy={go.activeInHierarchy} enabled={enabled} " +
                       $"isActiveAndEnabled={isActiveAndEnabled} same={ReferenceEquals(go.GetComponent<Probe>(), this)}";
            }

            void OnDisable()
            {
                if (ReadOnDisable) Seen.Add("OnDisable " + Snapshot());
            }

            void OnDestroy()
            {
                if (ReadOnDestroy) Seen.Add("OnDestroy " + Snapshot());
            }
        }

        public class Absent : MonoBehaviour
        {
        }

        sealed class Owned : IDisposable
        {
            readonly List<GameObject> m_Objects = new List<GameObject>();

            internal GameObject Make(string name, Transform parent = null)
            {
                var go = new GameObject(name);
                if (parent != null) go.transform.SetParent(parent, false);
                m_Objects.Add(go);
                return go;
            }

            public void Dispose()
            {
                foreach (var go in m_Objects)
                    if (go != null) Object.DestroyImmediate(go);
            }
        }

        static void Missing(Func<object> read) => Assert.Throws<MissingReferenceException>(() => read());

        // -------------------------------------------------- destroyed wrappers (fail on the old Core)

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DestroyedGameObjectStateAndLookupsThrow(bool active)
        {
            using var owned = new Owned();
            var go = owned.Make("destroyed.go");
            var probe = go.AddComponent<Probe>();
            go.SetActive(active);
            Object.DestroyImmediate(go);

            Assert.True(go == null);
            Assert.False(go is null);
            Missing(() => go.activeSelf);
            Missing(() => go.activeInHierarchy);
            Missing(() => go.GetComponent<Probe>());
            Missing(() => go.GetComponent(typeof(Probe)));
            Missing(() => go.GetComponent(nameof(Probe)));
            Missing(() => probe.enabled);
            Missing(() => probe.isActiveAndEnabled);
        }

        [Fact]
        public void DestroyedScriptOnALiveObjectThrowsWhileTheObjectAnswers()
        {
            using var owned = new Owned();
            var go = owned.Make("destroyed.component");
            var probe = go.AddComponent<Probe>();
            Object.DestroyImmediate(probe);

            Assert.True(probe == null);
            Missing(() => probe.enabled);
            Missing(() => probe.isActiveAndEnabled);
            Assert.True(go != null);
            Assert.True(go.activeSelf);
            Assert.True(go.activeInHierarchy);
            Assert.Null(go.GetComponent<Probe>());
            Assert.Null(go.GetComponent(typeof(Probe)));
            Assert.Null(go.GetComponent(nameof(Probe)));
        }

        [Fact]
        public void DestroyedChildThrowsWhileItsLiveParentAnswers()
        {
            using var owned = new Owned();
            var parent = owned.Make("destroyed.parent");
            var child = owned.Make("destroyed.child", parent.transform);
            Object.DestroyImmediate(child);

            Missing(() => child.activeInHierarchy);
            Assert.True(parent.activeInHierarchy);
            Assert.Equal(0, parent.transform.childCount);
        }

        // -------------------------------------------------- preservation controls (pass on both)

        [Fact]
        public void ManagedNullIsANullReferenceNotAMissingObject()
        {
            GameObject go = null;
            Behaviour behaviour = null;
            Assert.Throws<NullReferenceException>(() => go.activeSelf);
            Assert.Throws<NullReferenceException>(() => go.activeInHierarchy);
            Assert.Throws<NullReferenceException>(() => go.GetComponent<Probe>());
            Assert.Throws<NullReferenceException>(() => behaviour.enabled);
            Assert.Throws<NullReferenceException>(() => behaviour.isActiveAndEnabled);
        }

        [Fact]
        public void LiveInactiveAndDisabledObjectsAnswer()
        {
            using var owned = new Owned();
            var parent = owned.Make("live.parent");
            var child = owned.Make("live.child", parent.transform);
            var probe = child.AddComponent<Probe>();
            Assert.True(child.activeSelf && child.activeInHierarchy && probe.enabled && probe.isActiveAndEnabled);

            probe.enabled = false;
            Assert.False(probe.enabled);
            Assert.False(probe.isActiveAndEnabled);
            probe.enabled = true;

            parent.SetActive(false);
            Assert.False(parent.activeSelf);
            Assert.True(child.activeSelf);
            Assert.False(child.activeInHierarchy);
            Assert.True(probe.enabled);
            Assert.False(probe.isActiveAndEnabled);
            Assert.Same(probe, child.GetComponent<Probe>());
        }

        [Fact]
        public void LiveObjectWithoutTheComponentGivesNull()
        {
            using var owned = new Owned();
            var go = owned.Make("live.missing");
            Assert.Null(go.GetComponent<Absent>());
            Assert.Null(go.GetComponent(typeof(Absent)));
            Assert.Null(go.GetComponent(nameof(Absent)));
            Assert.Null(go.GetComponent((Type)null));
            Assert.False(go.TryGetComponent<Absent>(out var found));
            Assert.Null(found);
        }

        [Fact]
        public void TryGetComponentOnADestroyedObjectStillAnswersFalse()
        {
            using var owned = new Owned();
            var go = owned.Make("destroyed.try");
            go.AddComponent<Probe>();
            Object.DestroyImmediate(go);

            Assert.False(go.TryGetComponent<Probe>(out var generic));
            Assert.Null(generic);
            Assert.False(go.TryGetComponent(typeof(Probe), out var typed));
            Assert.Null(typed);
        }

        [Fact]
        public void PendingRequestAndItsOnDisableStillAnswer()
        {
            using var owned = new Owned();
            var go = owned.Make("pending.go");
            var probe = go.AddComponent<Probe>();
            probe.ReadOnDisable = true;

            Object.Destroy(go);

            Assert.True(go != null && probe != null);
            Assert.True(go.activeSelf);
            Assert.True(go.activeInHierarchy);
            Assert.Same(probe, go.GetComponent<Probe>());
            // The request's own OnDisable read every member without an exception (its values
            // inside the callback are not measured).
            Assert.Single(probe.Seen);
            Assert.StartsWith("OnDisable activeSelf=", probe.Seen[0]);
            Assert.False(probe.enabled);
            Assert.False(probe.isActiveAndEnabled);
        }

        [Fact]
        public void OnDestroyStillReadsItsObjects()
        {
            using var owned = new Owned();
            var go = owned.Make("callback.go");
            var probe = go.AddComponent<Probe>();
            probe.ReadOnDestroy = true;
            var seen = probe.Seen;

            Object.DestroyImmediate(go);

            // Read without an exception: the object is destroyed after its OnDestroy. The values
            // inside OnDestroy are not measured.
            Assert.Single(seen);
            Assert.StartsWith("OnDestroy activeSelf=", seen[0]);
        }
    }
}
