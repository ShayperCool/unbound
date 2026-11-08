using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbound.Runtime;
using Xunit;
using PL = UnityEngine.PlayerLoop;

namespace UnityEngine.Tests
{
    /// <summary>Manual zero-delay Object.Destroy, as tests/projects/destroycontract records it for
    /// awakened MonoBehaviours (Unity 6000.0.71f1): the request disables the scripts before it
    /// returns, parents first, while the objects stay alive, listed, active and findable; OnDestroy
    /// follows at the delayed scripting points. These tests run only the driver's, Lifecycle's and
    /// Destruction's actions at the selected nodes, without rendering, clocks or async schedulers.</summary>
    [Collection("Frames")]
    public partial class ManualDestroyTests
    {
        const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly List<string> Calls = new List<string>();

        public class Recorder : MonoBehaviour
        {
            internal bool Reenter;
            bool m_Reentered;
            void Awake() => Calls.Add(name + ".Awake");
            void OnEnable() => Calls.Add(name + ".OnEnable");
            void Start() => Calls.Add(name + ".Start");
            void Update() => Calls.Add(name + ".Update");
            void LateUpdate() => Calls.Add(name + ".LateUpdate");

            void OnDisable()
            {
                Calls.Add(name + ".OnDisable");
                if (!Reenter || m_Reentered) return;
                m_Reentered = true;
                Calls.Add(name + ".request2");
                Destroy(gameObject);
                Calls.Add(name + ".returned2");
            }

            void OnDestroy() => Calls.Add(name + ".OnDestroy");
        }

        public class AssetRecorder : ScriptableObject
        {
            void OnDestroy() => Calls.Add(name + ".OnDestroy");
        }

        public class Needed : MonoBehaviour
        {
            void OnDisable() => Calls.Add(name + ".Needed.OnDisable");
        }

        [RequireComponent(typeof(Needed))]
        public class Needing : MonoBehaviour
        {
        }

        static Dictionary<Type, List<Action>> Engine =>
            (Dictionary<Type, List<Action>>)typeof(PlayerLoopDriver).GetField("s_Engine", PrivateStatic).GetValue(null);

        static void RunNode(Type node)
        {
            if (!Engine.TryGetValue(node, out var actions)) return;
            foreach (var action in actions.ToArray())
            {
                var owner = action.Method.DeclaringType;
                if (owner == typeof(Lifecycle) || owner == typeof(Destruction) || owner == typeof(PlayerLoopDriver) ||
                    owner?.DeclaringType == typeof(PlayerLoopDriver))
                    action();
            }
        }

        static readonly Type FixedDelayed = typeof(PL.FixedUpdate.ScriptRunDelayedFixedFrameRate);
        static readonly Type UpdateDelayed = typeof(PL.Update.ScriptRunDelayedDynamicFrameRate);
        static readonly Type PostLateDelayed = typeof(PL.PostLateUpdate.ScriptRunDelayedDynamicFrameRate);

        static Type Delivery(int which) => which == 0 ? FixedDelayed : which == 1 ? UpdateDelayed : PostLateDelayed;

        /// <summary>Saves and empties the shared dispatch and destruction collections, restoring the
        /// same collections and contents after destroying the test's own objects.</summary>
        sealed class Scope : IDisposable
        {
            sealed class Saved
            {
                readonly object m_Collection;
                readonly object[] m_Items;
                readonly MethodInfo m_Clear, m_Add;

                internal Saved(object collection)
                {
                    m_Collection = collection;
                    m_Items = ((IEnumerable)collection).Cast<object>().ToArray();
                    m_Clear = collection.GetType().GetMethod("Clear", Type.EmptyTypes);
                    m_Add = collection.GetType().GetMethod("Add");
                    m_Clear.Invoke(collection, null);
                }

                internal void Restore()
                {
                    m_Clear.Invoke(m_Collection, null);
                    foreach (var item in m_Items) m_Add.Invoke(m_Collection, new[] { item });
                }
            }

            readonly List<Object> m_Objects = new List<Object>();
            readonly List<Saved> m_Saved = new List<Saved>();
            readonly List<(object List, FieldInfo Field, object Value)> m_Removed = new List<(object, FieldInfo, object)>();
            readonly FieldInfo m_Sequence = typeof(Lifecycle).GetField("s_Sequence", PrivateStatic);
            readonly object m_OldSequence;
            readonly FieldInfo m_Loop = typeof(PlayerLoopDriver).GetField("s_Current", PrivateStatic);
            readonly object m_OldLoop;
            readonly bool m_Playing = EditorSession.IsPlaying;
            readonly string m_Version = ProjectInfo.UnityVersion;
            readonly string[] m_Calls = Calls.ToArray();

            internal Scope(bool measured = false)
            {
                if (measured) ProjectInfo.UnityVersion = "6000.0.71f1";
                _ = Engine; // Complete the driver's registrations before isolating its queues.
                m_OldSequence = m_Sequence.GetValue(null);
                m_OldLoop = m_Loop.GetValue(null);
                PlayerLoopDriver.CurrentLoop = PlayerLoopDriver.DefaultLoop();
                foreach (string name in new[] { "s_Update", "s_LateUpdate", "s_FixedUpdate" })
                {
                    var list = typeof(Lifecycle).GetField(name, PrivateStatic).GetValue(null);
                    foreach (string field in new[] { "m_Items", "m_Members" })
                        m_Saved.Add(new Saved(list.GetType().GetField(field, PrivateInstance).GetValue(list)));
                    var removed = list.GetType().GetField("m_Removed", PrivateInstance);
                    m_Removed.Add((list, removed, removed.GetValue(list)));
                    removed.SetValue(list, 0);
                }
                foreach (string name in new[] { "s_PendingStart", "s_OnGUI", "s_OnRenderObject" })
                    Save(typeof(Lifecycle), name);
                Save(typeof(Coroutines), "s_Running");
                Save(typeof(Invokes), "s_Entries");
                Save(typeof(Destruction), "s_Delayed");
                Save(typeof(Destruction), "s_EndOfFrame");
                // Absent from a Core without the request list: a red run then fails on behaviour.
                var requested = typeof(Destruction).GetField("s_Requested", PrivateStatic);
                if (requested != null) m_Saved.Add(new Saved(requested.GetValue(null)));
                EditorSession.IsPlaying = true;
                Calls.Clear();
            }

            void Save(Type type, string field) => m_Saved.Add(new Saved(type.GetField(field, PrivateStatic).GetValue(null)));

            internal T Own<T>(T obj) where T : Object
            {
                m_Objects.Add(obj);
                return obj;
            }

            internal Recorder Subject(string name, Transform parent = null)
            {
                var go = Own(new GameObject(name));
                if (parent != null) go.transform.SetParent(parent, false);
                return go.AddComponent<Recorder>();
            }

            public void Dispose()
            {
                try
                {
                    foreach (var obj in m_Objects) if (obj != null) Object.DestroyImmediate(obj);
                }
                finally
                {
                    foreach (var saved in m_Saved) saved.Restore();
                    foreach (var r in m_Removed) r.Field.SetValue(r.List, r.Value);
                    m_Sequence.SetValue(null, m_OldSequence);
                    m_Loop.SetValue(null, m_OldLoop);
                    EditorSession.IsPlaying = m_Playing;
                    ProjectInfo.UnityVersion = m_Version;
                    Calls.Clear();
                    Calls.AddRange(m_Calls);
                }
            }
        }

        static bool Listed(Recorder r) =>
            Object.FindObjectsByType<Recorder>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(o => ReferenceEquals(o, r));

        static void AssertAliveAndListed(Recorder r, bool active)
        {
            var go = r.gameObject;
            Assert.True(go != null);
            Assert.True(r != null);
            Assert.Equal(active, go.activeSelf);
            Assert.Equal(active, go.activeInHierarchy);
            Assert.Same(active ? go : null, GameObject.Find(go.name));
            Assert.Same(r, go.GetComponent<Recorder>());
            Assert.True(Listed(r));
            Assert.False(r.enabled);
            Assert.False(r.isActiveAndEnabled);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void RequestDisablesBeforeReturningAndADelayedScriptPointDestroys(int node)
        {
            using var scope = new Scope();
            var subject = scope.Subject("destroy.subject");
            var go = subject.gameObject;
            RunNode(typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame));
            Calls.Clear();

            Object.Destroy(go);
            Assert.Equal(new[] { "destroy.subject.OnDisable" }, Calls);
            AssertAliveAndListed(subject, active: true);

            // The disabled script leaves the update lists at once.
            RunNode(typeof(PL.Update.ScriptRunBehaviourUpdate));
            RunNode(typeof(PL.PreLateUpdate.ScriptRunBehaviourLateUpdate));
            Assert.Equal(new[] { "destroy.subject.OnDisable" }, Calls);

            RunNode(Delivery(node));
            Assert.Equal(new[] { "destroy.subject.OnDisable", "destroy.subject.OnDestroy" }, Calls);
            Assert.True(go == null);
            Assert.True(subject == null);
            Assert.False(Listed(subject));
        }

        [Fact]
        public void StartupPointAndFrameStartedFlushDoNotDeliverARequest()
        {
            // Measured: the EarlyUpdate startup point did not deliver the request made after
            // TriggerEndOfFrameCallbacks, and PlayerSendFrameStarted did not deliver LateUpdate's.
            using var scope = new Scope();
            var subject = scope.Subject("destroy.kept");
            RunNode(typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame));
            Object.Destroy(subject.gameObject);
            Calls.Clear();
            RunNode(typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame));
            RunNode(typeof(PL.PostLateUpdate.PlayerSendFrameStarted));
            RunNode(typeof(PL.PostLateUpdate.PlayerSendFrameComplete));
            Assert.Empty(Calls);
            AssertAliveAndListed(subject, active: true);
            Assert.True(subject.gameObject.m_PendingDestroy);
            RunNode(FixedDelayed);
            Assert.Equal(new[] { "destroy.kept.OnDestroy" }, Calls);
        }

        [Fact]
        public void HierarchyRequestVisitsParentBeforeChildForBothCallbacks()
        {
            // Scoped to the measured single-child request (destroycontract), not a general
            // hierarchy rule. Other instantiateasync paths have child-first OnDisable.
            using var scope = new Scope();
            var parent = scope.Subject("destroy.parent");
            var child = scope.Subject("destroy.child", parent.transform);
            RunNode(typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame));
            Calls.Clear();

            Object.Destroy(parent.gameObject);
            Assert.Equal(new[] { "destroy.parent.OnDisable", "destroy.child.OnDisable" }, Calls);
            AssertAliveAndListed(parent, active: true);
            AssertAliveAndListed(child, active: true);
            Assert.Same(parent.transform, child.transform.parent);
            Assert.Equal(1, parent.transform.childCount);

            RunNode(UpdateDelayed);
            Assert.Equal(new[] { "destroy.parent.OnDisable", "destroy.child.OnDisable",
                "destroy.parent.OnDestroy", "destroy.child.OnDestroy" }, Calls);
            Assert.True(parent == null && child == null);
        }

        [Fact]
        public void ComponentRequestKeepsItsGameObject()
        {
            using var scope = new Scope();
            var subject = scope.Subject("destroy.component");
            var go = subject.gameObject;
            RunNode(typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame));
            Calls.Clear();

            Object.Destroy(subject);
            Assert.Equal(new[] { "destroy.component.OnDisable" }, Calls);
            AssertAliveAndListed(subject, active: true);
            Assert.False(go.m_PendingDestroy);

            RunNode(UpdateDelayed);
            Assert.Equal(new[] { "destroy.component.OnDisable", "destroy.component.OnDestroy" }, Calls);
            Assert.True(subject == null);
            Assert.True(go != null && go.activeInHierarchy);
            Assert.Same(go, GameObject.Find("destroy.component"));
            Assert.Null(go.GetComponent<Recorder>());
        }

        [Fact]
        public void DisabledAndInactiveScriptsGetNoSecondOnDisable()
        {
            using var scope = new Scope();
            var disabled = scope.Subject("destroy.disabled");
            disabled.enabled = false;
            var inactive = scope.Subject("destroy.inactive");
            inactive.gameObject.SetActive(false);
            Assert.Equal(new[] { "destroy.disabled.Awake", "destroy.disabled.OnEnable", "destroy.disabled.OnDisable",
                "destroy.inactive.Awake", "destroy.inactive.OnEnable", "destroy.inactive.OnDisable" }, Calls);
            Calls.Clear();

            Object.Destroy(disabled.gameObject);
            Object.Destroy(inactive.gameObject);
            Assert.Empty(Calls);
            AssertAliveAndListed(disabled, active: true);
            AssertAliveAndListed(inactive, active: false);

            RunNode(UpdateDelayed);
            Assert.Equal(new[] { "destroy.disabled.OnDestroy", "destroy.inactive.OnDestroy" }, Calls);
        }

        [Fact]
        public void DestroyFromItsOwnOnDisableReturnsWithoutASecondRequest()
        {
            using var scope = new Scope();
            var subject = scope.Subject("destroy.reenter");
            subject.Reenter = true;
            RunNode(typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame));
            Calls.Clear();

            Object.Destroy(subject.gameObject);
            Assert.Equal(new[] { "destroy.reenter.OnDisable", "destroy.reenter.request2", "destroy.reenter.returned2" }, Calls);
            AssertAliveAndListed(subject, active: true);

            RunNode(UpdateDelayed);
            RunNode(PostLateDelayed);
            Assert.Equal(new[] { "destroy.reenter.OnDisable", "destroy.reenter.request2", "destroy.reenter.returned2",
                "destroy.reenter.OnDestroy" }, Calls);
        }

        [Fact]
        public void PendingStartsAtTheUpdatePointRunBeforeTheDelivery()
        {
            // Measured within the Update delayed phase: instantiateasync reference lines 745-750
            // place the pending Start before destruction. This is not a claim about every leaf.
            using var scope = new Scope();
            var doomed = scope.Subject("destroy.doomed");
            RunNode(typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame));
            var fresh = scope.Subject("destroy.fresh");
            Object.Destroy(doomed.gameObject);
            Calls.Clear();

            RunNode(UpdateDelayed);
            Assert.Equal(new[] { "destroy.fresh.Start", "destroy.doomed.OnDestroy" }, Calls);
            Assert.True(fresh.didStart);
        }

        static IEnumerator Forever()
        {
            while (true) yield return null;
        }

        [Fact]
        public void CoroutinesKeepRunningUntilTheDelivery()
        {
            // Preservation control for the unmeasured Destroy-request window. The Coroutines
            // manual guarantees that enabled=false alone does not stop a coroutine, but does
            // not specify whether Destroy stops it at request time or actual destruction.
            using var scope = new Scope();
            var subject = scope.Subject("destroy.coroutine");
            RunNode(typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame));
            var routine = subject.StartCoroutine(Forever());

            Object.Destroy(subject.gameObject);
            Assert.False(routine.Done);
            RunNode(UpdateDelayed);
            Assert.True(routine.Done);
        }

        static UnityEngine.LowLevel.PlayerLoopSystem WithoutDelayedPoints(
            UnityEngine.LowLevel.PlayerLoopSystem system, bool all)
        {
            if (system.subSystemList != null)
                system.subSystemList = system.subSystemList
                    .Where(s => s.type != UpdateDelayed && (!all || s.type != FixedDelayed && s.type != PostLateDelayed))
                    .Select(s => WithoutDelayedPoints(s, all)).ToArray();
            return system;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReplacingTheLoopKeepsAnOrderedEndOfFrameDrain(bool removeAll)
        {
            // Preservation control, not a Native claim about custom loops. Queue under the
            // default loop, then remove one or all delivery points. A FixedUpdate-only pump
            // must not be required to run when the game is paused or has no fixed step due.
            using var scope = new Scope();
            var first = scope.Subject("destroy.custom.first");
            var asset = scope.Own(ScriptableObject.CreateInstance<AssetRecorder>());
            asset.name = "destroy.custom.asset";
            var last = scope.Subject("destroy.custom.last");
            RunNode(typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame));
            Object.Destroy(first.gameObject);
            Object.Destroy(asset);
            Object.Destroy(last.gameObject);
            Calls.Clear();

            PlayerLoopDriver.CurrentLoop = WithoutDelayedPoints(PlayerLoopDriver.CurrentLoop, removeAll);
            // Simulate any remaining delayed point. Partial pumps keep the same fallback.
            if (!removeAll) RunNode(PostLateDelayed);
            Assert.True(first != null && asset != null && last != null);
            Assert.Empty(Calls);
            RunNode(typeof(PL.PostLateUpdate.PlayerSendFrameStarted));
            Assert.Equal(new[] { "destroy.custom.first.OnDestroy", "destroy.custom.asset.OnDestroy",
                "destroy.custom.last.OnDestroy" }, Calls.Where(c => c.EndsWith(".OnDestroy")));
            Assert.True(first == null && asset == null && last == null);
            Calls.Clear();
            RunNode(typeof(PL.PostLateUpdate.PlayerSendFrameComplete));
            Assert.Empty(Calls);
        }

        [Fact]
        public void AutomaticParticleDestructionKeepsItsEndOfFrameTiming()
        {
            // Preservation control, not a Native claim about the stop-action's exact timing.
            using var scope = new Scope();
            var subject = scope.Subject("destroy.automatic");
            var ps = subject.gameObject.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.stopAction = ParticleSystemStopAction.Destroy;
            RunNode(typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame));
            Calls.Clear();

            ps.RunStopAction();
            Assert.True(subject.enabled && subject.gameObject.m_PendingDestroy);
            Assert.Empty(Calls);
            foreach (var node in new[] { FixedDelayed, UpdateDelayed, PostLateDelayed }) RunNode(node);
            Assert.True(subject != null && subject.enabled);
            Assert.Empty(Calls);
            RunNode(typeof(PL.PostLateUpdate.PlayerSendFrameStarted));
            Assert.Equal(new[] { "destroy.automatic.OnDisable", "destroy.automatic.OnDestroy" }, Calls);
            Assert.True(subject == null && ps == null);
        }

        [Fact]
        public void PositiveDelaysAssetsAndRequiredScriptsKeepTheEndOfFramePath()
        {
            // Preservation control, not a Native claim: these paths are outside destroycontract.
            using var scope = new Scope();
            var delayed = scope.Subject("destroy.delayed");
            var needing = scope.Own(new GameObject("destroy.needing")).AddComponent<Needing>();
            var needed = needing.GetComponent<Needed>();
            var mesh = scope.Own(new Mesh());
            RunNode(typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame));
            Calls.Clear();

            Object.Destroy(delayed.gameObject, 1f);
            Object.Destroy(needed);
            Object.Destroy(mesh);
            Assert.Empty(Calls);
            Assert.True(delayed.enabled && needed.enabled);
            Assert.False(delayed.gameObject.m_PendingDestroy);
            Assert.True(needed.m_PendingDestroy && mesh.m_PendingDestroy);

            foreach (var node in new[] { FixedDelayed, UpdateDelayed, PostLateDelayed }) RunNode(node);
            Assert.Empty(Calls);
            Assert.True(mesh != null && needed != null && delayed != null);

            // PlayerSendFrameStarted flushes the end-of-frame list: the asset goes, the required
            // script is refused (Unity's message text; timing unmeasured) and stays enabled;
            // the delay is not due.
            RunNode(typeof(PL.PostLateUpdate.PlayerSendFrameStarted));
            Assert.True(mesh == null);
            Assert.True(needed != null && needed.enabled);
            Assert.False(needed.m_PendingDestroy);
            Assert.True(delayed != null && delayed.enabled);
            Assert.Empty(Calls);
        }
    }
}
