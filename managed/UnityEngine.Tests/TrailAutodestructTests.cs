using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine.LowLevel;
using Xunit;
using PL = UnityEngine.PlayerLoop;

namespace UnityEngine.Tests
{
    public class TrailAutodestructProbe : MonoBehaviour
    {
        internal Action WhenDestroyed;
        internal readonly List<string> Log = new List<string>();

        void OnDestroy() => WhenDestroyed?.Invoke();

        /// <summary>Resumes at the next Update delayed-callback update and records the target.</summary>
        internal IEnumerator RecordAtResume(GameObject target)
        {
            yield return null;
            Log.Add(target == null ? "destroyed" : "alive");
        }
    }

    /// <summary>A trail's automatic destruction: requested only by the render's ageing, queued
    /// with the object alive, and run by the next fixed or dynamic delayed-callback update before
    /// that node's other work (Unity 6000.0.71f1, tests/projects/lines/TRAIL_AUTODESTRUCT_NATIVE.md).
    /// Steps the global clock and runs player loop nodes, so it shares the frame collection.</summary>
    [Collection("Frames")]
    public class TrailAutodestructTests
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        static readonly MethodInfo s_RunSystem = typeof(PlayerLoopDriver).GetMethod("RunSystem", Static);
        static readonly FieldInfo s_Engine = typeof(PlayerLoopDriver).GetField("s_Engine", Static);
        static readonly Type s_TrailSystem = typeof(TrailRenderer).GetNestedType("TrailSystem", BindingFlags.NonPublic);

        static readonly Type[] DrainNodes =
        {
            typeof(PL.FixedUpdate.ScriptRunDelayedFixedFrameRate),
            typeof(PL.Update.ScriptRunDelayedDynamicFrameRate),
            typeof(PL.PostLateUpdate.ScriptRunDelayedDynamicFrameRate),
        };

        struct RegistrationNode { }

        static void Frame(float seconds)
        {
            Time.s_FrameCount++;
            Time.s_Time += seconds;
        }

        /// <summary>Runs one player loop node's engine actions, as a frame does.</summary>
        static void RunNode(Type node) => s_RunSystem.Invoke(null, new object[] { new PlayerLoopSystem { type = node } });

        static Dictionary<Type, List<Action>> Engine() => (Dictionary<Type, List<Action>>)s_Engine.GetValue(null);

        static List<Action> Actions(Type node) => Engine().TryGetValue(node, out var list) ? list : new List<Action>();

        static int Requests(TrailRenderer trail)
        {
            int n = 0;
            foreach (var item in (IList)s_TrailSystem.GetField("s_Requests", Static).GetValue(null))
                if (ReferenceEquals(((ValueTuple<TrailRenderer, GameObject>)item).Item1, trail)) n++;
            return n;
        }

        static bool IsDrain(Action action) => action.Method.Name == "DrainAutodestruct" && action.Method.DeclaringType == s_TrailSystem;

        /// <summary>A still autodestruct trail whose two points have outlived its time, not yet aged.</summary>
        static TrailRenderer ExpiredTrail(string name)
        {
            var go = new GameObject(name);
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.5f;
            trail.autodestruct = true;
            Frame(0.1f);
            Assert.Equal(0, trail.positionCount);
            // The first move records where it was and where it is; its ageing sees them.
            go.transform.position = Vector3.right;
            Assert.Equal(2, trail.positionCount);
            Frame(1f);
            return trail;
        }

        static void Collect(TrailRenderer trail, Camera camera) =>
            trail.CollectDraws(FrameBuilder.ContextOf(camera), new List<FrameBuilder.DrawItem>());

        [Fact]
        public void AStillTrailRequestsOnceFromRenderCollectionAndStaysAlive()
        {
            var camera = new GameObject("camera").AddComponent<Camera>();
            var trail = ExpiredTrail("autodestruct.collect");
            var go = trail.gameObject;
            Assert.Equal(0, Requests(trail));
            Collect(trail, camera);
            Assert.Equal(0, trail.positionCount);
            Assert.Equal(1, Requests(trail));
            Assert.True(trail.m_AutodestructRequested);
            // The request leaves the object alive, enabled and not pending destruction.
            Assert.False(go.m_PendingDestroy);
            Assert.False(go.m_Destroyed);
            Assert.False(trail.m_PendingDestroy);
            Assert.True(trail.enabled);
            Assert.True(go.activeSelf);
            // Latched: further collections, and new points expiring again, do not request again.
            Collect(trail, camera);
            trail.AgeForRender();
            go.transform.position = new Vector3(2f, 0f, 0f);
            Assert.Equal(2, trail.positionCount);
            Frame(1f);
            Collect(trail, camera);
            Assert.Equal(0, trail.positionCount);
            Assert.Equal(1, Requests(trail));
            RunNode(typeof(PL.Update.ScriptRunDelayedDynamicFrameRate));
            Assert.True(go == null);
            Assert.Equal(0, Requests(trail));
            Assert.False(trail.m_AutodestructRequested);
            Object.DestroyImmediate(camera.gameObject);
        }

        [Fact]
        public void MovementBoundsAndBakeMeshAgeingNeverRequest()
        {
            var camera = new GameObject("camera").AddComponent<Camera>();
            var trail = ExpiredTrail("autodestruct.other");
            var go = trail.gameObject;
            // The per-frame movement update, bounds, reads and BakeMesh of the expired still trail.
            trail.UpdateTrail();
            trail.GetLocalBounds();
            var mesh = new Mesh();
            trail.BakeMesh(mesh, camera);
            Assert.Equal(2, trail.positionCount);
            // Movement ages, and keeps the point it records.
            go.transform.position = new Vector3(5f, 0f, 0f);
            Assert.Equal(2, trail.positionCount);
            Assert.Equal(0, Requests(trail));
            Assert.False(trail.m_AutodestructRequested);
            foreach (var node in DrainNodes) RunNode(node);
            Assert.False(go.m_Destroyed);
            Assert.False(go.m_PendingDestroy);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(camera.gameObject);
        }

        /// <summary>The end-of-frame destruction and startup delayed calls leave the request
        /// queued; the next fixed or dynamic delayed-callback update destroys the object.</summary>
        [Theory]
        [InlineData(typeof(PL.FixedUpdate.ScriptRunDelayedFixedFrameRate))]
        [InlineData(typeof(PL.Update.ScriptRunDelayedDynamicFrameRate))]
        [InlineData(typeof(PL.PostLateUpdate.ScriptRunDelayedDynamicFrameRate))]
        public void TheRequestRunsAtTheNextDelayedCallbackUpdate(Type node)
        {
            var camera = new GameObject("camera").AddComponent<Camera>();
            var trail = ExpiredTrail("autodestruct.node");
            var go = trail.gameObject;
            Collect(trail, camera);
            Assert.Equal(1, Requests(trail));
            var ineligible = new[]
            {
                typeof(PL.PostLateUpdate.PlayerSendFrameStarted),
                typeof(PL.PostLateUpdate.TriggerEndOfFrameCallbacks),
                typeof(PL.PostLateUpdate.PlayerSendFrameComplete),
                typeof(PL.EarlyUpdate.ScriptRunDelayedStartupFrame),
            };
            foreach (var other in ineligible)
            {
                RunNode(other);
                Assert.False(go.m_Destroyed);
                Assert.False(go.m_PendingDestroy);
                Assert.Equal(1, Requests(trail));
            }
            RunNode(node);
            Assert.True(go == null);
            Assert.Equal(0, Requests(trail));
            Object.DestroyImmediate(camera.gameObject);
        }

        /// <summary>The drain is the first action of its nodes: before the node's coroutines
        /// (registered when the player loop starts) and before observers registered later.</summary>
        [Fact]
        public void TheDrainRunsBeforeTheNodesOtherWork()
        {
            var node = typeof(PL.Update.ScriptRunDelayedDynamicFrameRate);
            var camera = new GameObject("camera").AddComponent<Camera>();
            var probe = new GameObject("probe").AddComponent<TrailAutodestructProbe>();
            var trail = ExpiredTrail("autodestruct.order");
            var go = trail.gameObject;
            probe.StartCoroutine(probe.RecordAtResume(go));
            var seen = new List<string>();
            Action observer = () => seen.Add(go == null ? "destroyed" : "alive");
            PlayerLoopDriver.Register(node, observer);
            try
            {
                foreach (var drainNode in DrainNodes) Assert.True(IsDrain(Actions(drainNode)[0]));
                Collect(trail, camera);
                Frame(0.02f);
                RunNode(node);
                Assert.Equal(new[] { "destroyed" }, seen);
                Assert.Equal(new[] { "destroyed" }, probe.Log);
            }
            finally
            {
                Actions(node).Remove(observer);
            }
            Object.DestroyImmediate(probe.gameObject);
            Object.DestroyImmediate(camera.gameObject);
        }

        /// <summary>A drain destroys the requests queued when it started; one made while it runs
        /// (here by a render collection from the destroyed object's OnDestroy) waits for the next.</summary>
        [Fact]
        public void ARequestMadeDuringADrainWaitsForTheNextDrain()
        {
            var camera = new GameObject("camera").AddComponent<Camera>();
            var first = ExpiredTrail("autodestruct.first");
            var second = ExpiredTrail("autodestruct.second");
            var firstGo = first.gameObject;
            var secondGo = second.gameObject;
            firstGo.AddComponent<TrailAutodestructProbe>().WhenDestroyed = () => Collect(second, camera);
            Collect(first, camera);
            Assert.Equal(1, Requests(first));
            Assert.Equal(0, Requests(second));
            RunNode(typeof(PL.Update.ScriptRunDelayedDynamicFrameRate));
            Assert.True(firstGo == null);
            Assert.False(secondGo.m_Destroyed);
            Assert.False(secondGo.m_PendingDestroy);
            Assert.Equal(1, Requests(second));
            RunNode(typeof(PL.PostLateUpdate.ScriptRunDelayedDynamicFrameRate));
            Assert.True(secondGo == null);
            Assert.Equal(0, Requests(second));
            Object.DestroyImmediate(camera.gameObject);
        }

        /// <summary>An action registered first while its node runs does not disturb that run and
        /// first runs at the node's next run.</summary>
        [Fact]
        public void RegisteringFirstDuringTheNodeRunsFromItsNextRun()
        {
            var node = typeof(RegistrationNode);
            var log = new List<string>();
            bool registered = false;
            Action first = () => log.Add("first");
            PlayerLoopDriver.Register(node, () =>
            {
                log.Add("registering");
                if (registered) return;
                registered = true;
                PlayerLoopDriver.RegisterFirst(node, first);
            });
            PlayerLoopDriver.Register(node, () => log.Add("after"));
            try
            {
                RunNode(node);
                Assert.Equal(new[] { "registering", "after" }, log);
                log.Clear();
                RunNode(node);
                Assert.Equal(new[] { "first", "registering", "after" }, log);
            }
            finally
            {
                Engine().Remove(node);
            }
        }

        /// <summary>The trail system's first registration made by a trail enabled while a drain
        /// node runs (a delayed coroutine) does not disturb that run; a request made then is
        /// drained by the node's next run, first.</summary>
        [Fact]
        public void ATrailFirstEnabledDuringADrainNodeRegistersForItsNextRun()
        {
            var node = typeof(PL.Update.ScriptRunDelayedDynamicFrameRate);
            var camera = new GameObject("camera").AddComponent<Camera>();
            var expired = ExpiredTrail("autodestruct.waiting");
            var expiredGo = expired.gameObject;
            // Start over as though no trail had been enabled yet.
            foreach (var drainNode in DrainNodes) Actions(drainNode).RemoveAll(IsDrain);
            Actions(typeof(PL.PostLateUpdate.UpdateAllRenderers)).RemoveAll(a => a.Method.DeclaringType == s_TrailSystem);
            s_TrailSystem.GetField("s_Registered", Static).SetValue(null, false);
            GameObject enabled = null;
            bool done = false;
            Action observer = () =>
            {
                if (done) return;
                done = true;
                enabled = new GameObject("autodestruct.enabled");
                enabled.AddComponent<TrailRenderer>();
                Collect(expired, camera);
            };
            PlayerLoopDriver.Register(node, observer);
            try
            {
                RunNode(node);
                Assert.True((bool)s_TrailSystem.GetField("s_Registered", Static).GetValue(null));
                foreach (var drainNode in DrainNodes) Assert.True(IsDrain(Actions(drainNode)[0]));
                Assert.False(expiredGo.m_Destroyed);
                Assert.Equal(1, Requests(expired));
                RunNode(node);
                Assert.True(expiredGo == null);
                Assert.Equal(0, Requests(expired));
            }
            finally
            {
                Actions(node).Remove(observer);
            }
            Object.DestroyImmediate(enabled);
            Object.DestroyImmediate(camera.gameObject);
        }
    }
}
