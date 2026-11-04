using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
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
            readonly List<(IList Collection, object[] Items)> m_Collections = new();
            readonly bool m_Playing = EditorSession.IsPlaying;
            readonly FieldInfo m_Sequence = typeof(Lifecycle).GetField("s_Sequence", BindingFlags.Static | BindingFlags.NonPublic);
            readonly object m_OldSequence;

            internal Owned()
            {
                m_OldSequence = m_Sequence.GetValue(null);
                Save(typeof(Destruction), "s_Requested");
                Save(typeof(Destruction), "s_EndOfFrame");
                Save(typeof(Lifecycle), "s_PendingStart");
                Save(typeof(Coroutines), "s_Running");
                Save(typeof(Invokes), "s_Entries");
                EditorSession.IsPlaying = true;
            }

            void Save(Type owner, string field)
            {
                var collection = (IList)owner.GetField(field, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                var items = new object[collection.Count];
                collection.CopyTo(items, 0);
                m_Collections.Add((collection, items));
                collection.Clear();
            }

            internal GameObject Make(string name, Transform parent = null)
            {
                var go = new GameObject(name);
                if (parent != null) go.transform.SetParent(parent, false);
                m_Objects.Add(go);
                return go;
            }

            public void Dispose()
            {
                try
                {
                    foreach (var go in m_Objects)
                        if (go != null) Object.DestroyImmediate(go);
                }
                finally
                {
                    foreach (var (collection, items) in m_Collections)
                    {
                        collection.Clear();
                        foreach (var item in items) collection.Add(item);
                    }
                    m_Sequence.SetValue(null, m_OldSequence);
                    EditorSession.IsPlaying = m_Playing;
                }
            }
        }

        static void Missing(Func<object> read) => Assert.Throws<MissingReferenceException>(() => read());

        // -------------------------------------------------- destroyed wrappers (fail on the old Core)

        // Each getter gets its own red result on the old Core. Type/string are unmeasured
        // overload-consistency checks, not independent Native observations.
        [Theory]
        [InlineData(true, "activeSelf")]
        [InlineData(true, "activeInHierarchy")]
        [InlineData(true, "generic")]
        [InlineData(true, "type")]
        [InlineData(true, "string")]
        [InlineData(true, "enabled")]
        [InlineData(true, "isActiveAndEnabled")]
        [InlineData(false, "activeSelf")]
        [InlineData(false, "activeInHierarchy")]
        [InlineData(false, "generic")]
        [InlineData(false, "type")]
        [InlineData(false, "string")]
        [InlineData(false, "enabled")]
        [InlineData(false, "isActiveAndEnabled")]
        public void DestroyedGameObjectStateAndLookupsThrow(bool active, string member)
        {
            using var owned = new Owned();
            var go = owned.Make("destroyed.go");
            var probe = go.AddComponent<Probe>();
            go.SetActive(active);
            Object.DestroyImmediate(go);

            Assert.True(go == null);
            Assert.False(go is null);
            Missing(() => member switch
            {
                "activeSelf" => go.activeSelf,
                "activeInHierarchy" => go.activeInHierarchy,
                "generic" => go.GetComponent<Probe>(),
                "type" => go.GetComponent(typeof(Probe)),
                "string" => go.GetComponent(nameof(Probe)),
                "enabled" => probe.enabled,
                "isActiveAndEnabled" => probe.isActiveAndEnabled,
                _ => throw new ArgumentOutOfRangeException(nameof(member))
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DestroyedScriptOnALiveObjectThrowsWhileTheObjectAnswers(bool activeAndEnabled)
        {
            using var owned = new Owned();
            var go = owned.Make("destroyed.component");
            var probe = go.AddComponent<Probe>();
            Object.DestroyImmediate(probe);

            Assert.True(probe == null);
            Missing(() => activeAndEnabled ? probe.isActiveAndEnabled : probe.enabled);
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
            // Unmeasured preservation control: retain the current null-Type answer.
            Assert.Null(go.GetComponent((Type)null));
            Assert.False(go.TryGetComponent<Absent>(out var found));
            Assert.Null(found);
        }

        // Unmeasured preservation control; this does not assert a Unity contract.
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

            // Unmeasured preservation control: the previous implementation marks after OnDestroy.
            // Neither accessibility nor the values inside OnDestroy are a Native claim.
            Assert.Single(seen);
            Assert.StartsWith("OnDestroy activeSelf=", seen[0]);
        }
        public class RemovesReceiver : MonoBehaviour
        {
            // Intentionally not an iterator method: DestroyImmediate happens before the
            // returned IEnumerator is inspected by the message/invoke pump.
            public IEnumerator RemoveAndReturn()
            {
                Object.DestroyImmediate(gameObject);
                return Empty();
            }

            public void RemoveNow() => Object.DestroyImmediate(gameObject);

            static IEnumerator Empty()
            {
                yield break;
            }
        }

        public class RemovesOnParentChange : MonoBehaviour
        {
            void OnTransformParentChanged() => Object.DestroyImmediate(gameObject);
        }

        // Internal preservation controls, not claims about Unity permitting destruction
        // in every callback. They protect the caller paths from newly throwing public getters.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AReturnedEnumeratorCanRemoveItsMessageOrInvokeReceiver(bool invoke)
        {
            using var owned = new Owned();
            var go = owned.Make("message.remove");
            var receiver = go.AddComponent<RemovesReceiver>();

            var exception = Record.Exception(() =>
            {
                if (invoke)
                {
                    receiver.Invoke(nameof(RemovesReceiver.RemoveAndReturn), 0f);
                    Invokes.Run();
                }
                else
                    go.SendMessage(nameof(RemovesReceiver.RemoveAndReturn), SendMessageOptions.RequireReceiver);
            });

            Assert.Null(exception);
            Assert.True(go == null);
        }

        [Fact]
        public void BroadcastCanContinueItsSnapshotAfterTheRootIsRemoved()
        {
            using var owned = new Owned();
            var root = owned.Make("broadcast.root");
            var child = owned.Make("broadcast.child", root.transform);
            root.AddComponent<RemovesReceiver>();

            var exception = Record.Exception(() =>
                root.BroadcastMessage(nameof(RemovesReceiver.RemoveNow), SendMessageOptions.RequireReceiver));

            Assert.Null(exception);
            Assert.True(root == null);
            Assert.True(child == null);
        }

        [Fact]
        public void SetParentCanFinishAfterTheParentChangedCallbackRemovesTheObject()
        {
            using var owned = new Owned();
            var parent = owned.Make("reparent.parent");
            var moved = owned.Make("reparent.moved");
            moved.AddComponent<RemovesOnParentChange>();
            var transform = moved.transform;

            var exception = Record.Exception(() => transform.SetParent(parent.transform, false));

            Assert.Null(exception);
            Assert.True(moved == null);
            Assert.True(parent != null);
        }
    }
}
