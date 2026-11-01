using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;
using Xunit;
using PL = UnityEngine.PlayerLoop;

namespace UnityEngine.Tests
{
    /// <summary>Source-derived request/queue rules from TRAIL_AUTODESTRUCT_NATIVE.md. These
    /// implementation controls do not identify the consuming node in the public Native capture
    /// or establish general delayed-manager parity.</summary>
    [Collection("Frames")]
    public class TrailAutodestructQueueTests
    {
        static readonly Type TrailSystem = typeof(TrailRenderer).GetNestedType("TrailSystem", BindingFlags.NonPublic);
        const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        static T State<T>(string name) => (T)TrailSystem.GetField(name, StaticPrivate).GetValue(null);
        static void SetState(string name, bool value) => TrailSystem.GetField(name, StaticPrivate).SetValue(null, value);
        static Dictionary<Type, List<Action>> Engine =>
            (Dictionary<Type, List<Action>>)typeof(PlayerLoopDriver).GetField("s_Engine", StaticPrivate).GetValue(null);
        static Queue<GameObject> Requests => State<Queue<GameObject>>("s_DestroyRequests");
        static HashSet<GameObject> Targets => State<HashSet<GameObject>>("s_DestroyTargets");
        static void Run(Type node) => typeof(PlayerLoopDriver).GetMethod("RunSystem", StaticPrivate)
            .Invoke(null, new object[] { new PlayerLoopSystem { type = node } });
        static Type Node(int which) => which == 0 ? typeof(PL.FixedUpdate.ScriptRunDelayedFixedFrameRate) :
            which == 1 ? typeof(PL.Update.ScriptRunDelayedDynamicFrameRate) : typeof(PL.PostLateUpdate.ScriptRunDelayedDynamicFrameRate);

        static void Collect(TrailRenderer trail)
        {
            var draws = new List<FrameBuilder.DrawItem>();
            trail.CollectDraws(new FrameBuilder.CameraContext { Position = new Vector3(0f, 0f, -10f), Forward = Vector3.forward }, draws);
            Assert.Empty(draws);
        }

        // Registration is lazy and global. Isolate each control, including the first-activation
        // cases, without leaving test observers or resetting any production shared subsystem.
        sealed class Scope : IDisposable
        {
            readonly Dictionary<Type, List<Action>> m_Engine = new Dictionary<Type, List<Action>>();
            readonly TrailRenderer[] m_Trails = State<List<TrailRenderer>>("s_Trails").ToArray();
            readonly TrailRenderer[] m_Snapshot = State<List<TrailRenderer>>("s_Snapshot").ToArray();
            readonly GameObject[] m_Requests = Requests.ToArray();
            readonly GameObject[] m_Targets = new List<GameObject>(Targets).ToArray();
            readonly bool m_Registered = State<bool>("s_Registered"), m_Draining = State<bool>("s_Draining");
            readonly double m_Time = Time.s_Time;
            readonly bool m_Playing = EditorSession.IsPlaying;
            readonly bool m_InFixedTimeStep = Time.s_InFixedTimeStep;
            readonly List<Object> m_Owned = new List<Object>();

            internal Scope()
            {
                foreach (var pair in Engine) m_Engine.Add(pair.Key, new List<Action>(pair.Value));
                State<List<TrailRenderer>>("s_Trails").Clear();
                State<List<TrailRenderer>>("s_Snapshot").Clear();
                Requests.Clear();
                Targets.Clear();
                SetState("s_Registered", false);
                SetState("s_Draining", false);
                Time.s_Time = 100;
                Time.s_InFixedTimeStep = false;
                EditorSession.IsPlaying = true;
            }

            internal T Own<T>(T obj) where T : Object { m_Owned.Add(obj); return obj; }

            internal TrailRenderer Trail()
            {
                var go = Own(new GameObject("automatic trail"));
                var trail = go.AddComponent<TrailRenderer>();
                trail.time = 0.1f;
                trail.autodestruct = true;
                go.transform.position = Vector3.right;
                trail.UpdateTrail(); // Movement ages nonempty points and sets the nonempty latch.
                Assert.Equal(2, trail.positionCount);
                return trail;
            }

            public void Dispose()
            {
                foreach (var obj in m_Owned) if (obj != null) Object.DestroyImmediate(obj);
                Engine.Clear();
                foreach (var pair in m_Engine) Engine.Add(pair.Key, pair.Value);
                var trails = State<List<TrailRenderer>>("s_Trails");
                trails.Clear(); trails.AddRange(m_Trails);
                var snapshot = State<List<TrailRenderer>>("s_Snapshot");
                snapshot.Clear(); snapshot.AddRange(m_Snapshot);
                Requests.Clear();
                foreach (var target in m_Requests) Requests.Enqueue(target);
                Targets.Clear(); Targets.UnionWith(m_Targets);
                SetState("s_Registered", m_Registered);
                SetState("s_Draining", m_Draining);
                Time.s_Time = m_Time;
                Time.s_InFixedTimeStep = m_InFixedTimeStep;
                EditorSession.IsPlaying = m_Playing;
            }
        }

        [Fact]
        public void StationaryExpiryRequestsDuringCollectionBeforeTheEmptyStripReturn()
        {
            using var scope = new Scope();
            var trail = scope.Trail();
            var go = trail.gameObject;
            Time.s_Time += 1;
            trail.UpdateTrail();
            Assert.Equal(2, trail.positionCount);
            Assert.Empty(Requests);
            Collect(trail);
            Assert.Equal(0, trail.positionCount);
            Assert.Same(go, Assert.Single(Requests));
            Assert.True(go && trail.enabled && go.activeInHierarchy);
            Assert.False(go.m_PendingDestroy);
            for (int i = 0; i < 20; i++) Collect(trail);
            Assert.Single(Requests);
            Assert.Single(Targets);
        }

        [Theory]
        [InlineData("movement")]
        [InlineData("getter")]
        [InlineData("bounds")]
        [InlineData("bake")]
        public void NonRenderAgeingCannotRequestDestruction(string caller)
        {
            using var scope = new Scope();
            var trail = scope.Trail();
            var go = trail.gameObject;
            Time.s_Time += 1;
            trail.time = -1f; // Even the newly recorded movement expires.
            go.transform.position = 2f * Vector3.right;
            if (caller == "movement") trail.UpdateTrail();
            else if (caller == "getter") _ = trail.positionCount;
            else if (caller == "bounds") _ = trail.GetLocalBounds();
            else trail.BakeMesh(scope.Own(new Mesh()), scope.Own(new GameObject("camera")).AddComponent<Camera>());
            Assert.Equal(0, trail.positionCount);
            Assert.Empty(Requests);
            Run(Node(1));
            Destruction.FlushEndOfFrame();
            Assert.True(go);
            Collect(trail);
            Assert.Same(go, Assert.Single(Requests));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void AConsumerRunsBeforeAnExistingObserverAtTheSameNode(int node)
        {
            using var scope = new Scope();
            GameObject target = null;
            bool observed = false, sawDestroyed = false;
            PlayerLoopDriver.Register(Node(node), () => { observed = true; sawDestroyed = target.m_Destroyed; });
            var trail = scope.Trail();
            target = trail.gameObject;
            Time.s_Time += 1;
            Collect(trail);
            Run(Node(node));
            // RunSystem logs action exceptions, so also assert outside its catch boundary.
            Assert.True(observed);
            Assert.True(sawDestroyed);
            Assert.True(target.m_Destroyed);
            Assert.Empty(Requests);
            Assert.Empty(Targets);
        }

        [Fact]
        public void StartupAndEofDoNotConsumeAnAutomaticRequest()
        {
            using var scope = new Scope();
            var trail = scope.Trail();
            var go = trail.gameObject;
            Time.s_Time += 1;
            Collect(trail);
            foreach (var node in new[] { typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame),
                typeof(PL.PostLateUpdate.PlayerSendFrameStarted), typeof(PL.PostLateUpdate.PlayerSendFrameComplete),
                typeof(PL.PostLateUpdate.TriggerEndOfFrameCallbacks) }) Run(node);
            Destruction.RunDelayed();
            Destruction.FlushEndOfFrame();
            Assert.True(go && trail.enabled);
            Assert.False(go.m_PendingDestroy);
            Assert.Single(Requests);
        }

        [Fact]
        public void CollectionRequiresTheNonemptyLatchAndPlayMode()
        {
            using var scope = new Scope();
            var empty = scope.Own(new GameObject("never populated")).AddComponent<TrailRenderer>();
            empty.autodestruct = true;
            Collect(empty);
            Assert.Empty(Requests);
            var trail = scope.Trail();
            Time.s_Time += 1;
            EditorSession.IsPlaying = false;
            Collect(trail);
            Assert.Equal(0, trail.positionCount);
            Assert.Empty(Requests);
            EditorSession.IsPlaying = true;
            Collect(trail);
            Assert.Same(trail.gameObject, Assert.Single(Requests));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void FirstRegistrationInsideAnActiveConsumerNodeWaitsForTheNextInvocation(int node)
        {
            using var scope = new Scope();
            GameObject target = null;
            bool created = false, tailRan = false;
            PlayerLoopDriver.Register(Node(node), () =>
            {
                if (created) return;
                created = true;
                var trail = scope.Trail();
                target = trail.gameObject;
                Time.s_Time += 1;
                Collect(trail);
            });
            PlayerLoopDriver.Register(Node(node), () => tailRan = true);
            var oldActions = Engine[Node(node)];
            int oldCount = oldActions.Count;
            Run(Node(node));
            Assert.True(created && tailRan);
            Assert.True(target != null);
            Assert.Single(Requests);
            Assert.NotSame(oldActions, Engine[Node(node)]);
            Assert.Equal(oldCount, oldActions.Count);
            Run(Node(node));
            Assert.True(target.m_Destroyed);
            Assert.Empty(Requests);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void ADestructionCallbackRequestWaitsForTheNextMatchingNode(int node)
        {
            using var scope = new Scope();
            var first = scope.Trail();
            var second = scope.Trail();
            var firstTarget = first.gameObject;
            var secondTarget = second.gameObject;
            bool callbackRan = false;
            firstTarget.AddComponent<DestroyCallback>().Callback = () =>
            {
                callbackRan = true;
                Collect(second);
                Run(Node((node + 1) % 3)); // A nested matching node must not drain fresh requests.
            };
            Time.s_Time += 1;
            Collect(first);
            Run(Node(node));
            Assert.True(callbackRan && firstTarget.m_Destroyed);
            Assert.True(secondTarget != null);
            Assert.Same(secondTarget, Assert.Single(Requests));
            Run(Node((node + 1) % 3));
            Assert.True(secondTarget.m_Destroyed);
            Assert.Empty(Requests);
        }

        [Theory]
        [InlineData("disabled")]
        [InlineData("removed")]
        [InlineData("flag")]
        [InlineData("inactive")]
        public void ATargetRequestSurvivesLaterComponentAndObjectChanges(string change)
        {
            using var scope = new Scope();
            var trail = scope.Trail();
            var go = trail.gameObject;
            Time.s_Time += 1;
            Collect(trail);
            if (change == "disabled") trail.enabled = false;
            else if (change == "removed") Object.DestroyImmediate(trail);
            else if (change == "flag") trail.autodestruct = false;
            else go.SetActive(false);
            Assert.True(go);
            Assert.Single(Requests);
            Run(Node(1));
            Assert.True(go.m_Destroyed);
            Assert.Empty(Requests);
        }

        [Fact]
        public void StaleRequestsAfterSceneUnloadAndPlayModeResetCannotDestroyReplacementObjects()
        {
            using var scope = new Scope();
            var trail = scope.Trail();
            var go = trail.gameObject;
            Time.s_Time += 1;
            Collect(trail);
            var scene = SceneManager.CreateScene("queued trail unload");
            SceneManager.MoveGameObjectToScene(go, scene);
            SceneManager.UnloadSceneAsync(scene);
            Assert.True(go.m_Destroyed);
            // Play-mode exit likewise destroys roots before resetting the scene registry. A stale
            // target reference must be harmless across clock resets and replacement allocations.
            Time.s_Time = 0;
            var replacement = scope.Trail();
            Run(Node(0));
            Assert.True(replacement != null);
            Assert.Empty(Requests);
            Assert.Empty(Targets);
        }

        public sealed class DestroyCallback : MonoBehaviour
        {
            internal Action Callback;
            void OnDestroy()
            {
                var callback = Callback;
                Callback = null;
                callback?.Invoke();
            }
        }
    }
}
