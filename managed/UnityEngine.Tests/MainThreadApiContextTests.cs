using System;
using System.Reflection;
using System.Threading;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Frames")]
    public sealed class MainThreadApiContextTests : IDisposable
    {
        static readonly FieldInfo MainThread = typeof(PlayerLoopDriver).GetField("s_MainThreadId", BindingFlags.Static | BindingFlags.NonPublic);
        readonly object m_MainThread = MainThread.GetValue(null);
        readonly string m_Release = ProjectInfo.UnityVersion;

        public MainThreadApiContextTests()
        {
            MainThread.SetValue(null, Environment.CurrentManagedThreadId);
            ProjectInfo.UnityVersion = "6000.0.71f1";
        }

        public void Dispose()
        {
            MainThread.SetValue(null, m_MainThread);
            ProjectInfo.UnityVersion = m_Release;
        }

        static Exception OnWorker(Action call)
        {
            Exception failure = null;
            var thread = new Thread(() => { try { call(); } catch (Exception e) { failure = e; } });
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
            return failure;
        }

        static void Rejects(string binding, Action call)
        {
            var failure = Assert.IsType<UnityException>(OnWorker(call));
            Assert.StartsWith(binding + " can only be called from the main thread.", failure.Message);
        }

        [Fact]
        public void PhysicsQueriesKeepManagedEarlyReturnsAndRejectOtherWorkerCalls()
        {
            var go = new GameObject("Thread API");
            try
            {
                var body = go.AddComponent<Rigidbody>();
                var collider = go.AddComponent<BoxCollider>();
                Rejects("get_mass", () => _ = body.mass);
                Rejects("set_mass", () => body.mass = 2);
                Rejects("Raycast", () => collider.Raycast(new Ray(Vector3.zero, Vector3.zero), out _, 1));
                Rejects("Internal_RaycastTest", () => Physics.Raycast(Vector3.zero, Vector3.up, 1));
                Rejects("Internal_Raycast", () => Physics.Raycast(Vector3.zero, Vector3.up, out _, 1, ~0));
                Rejects("Internal_RaycastNonAlloc", () => Physics.RaycastNonAlloc(Vector3.zero, Vector3.up, null, 1));
                Assert.Null(OnWorker(() =>
                {
                    Assert.False(Physics.Raycast(Vector3.zero, Vector3.zero, 1));
                    _ = Physics.gravity;
                    _ = Physics.defaultPhysicsScene;
                    _ = Animator.StringToHash("state");
                }));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void ConstructorsRejectApiButCreationCallbacksAndTheirCallerCanUseIt()
        {
            var go = new GameObject("Constructed");
            ConstructorAssetReader asset = null;
            try
            {
                var component = go.AddComponent<ConstructorTimeReader>();
                Assert.Contains("MonoBehaviour constructor", component.Field);
                Assert.Contains("on game object 'Constructed'", component.Body);
                Assert.True(component.AwakeReadTime);
                asset = ScriptableObject.CreateInstance<ConstructorAssetReader>();
                Assert.Contains("ScriptableObject constructor", asset.Field);
                Assert.True(asset.EnabledReadTime);
                Assert.Throws<UnityException>(() => go.AddComponent<ThrowingApiConstructor>());
                _ = Time.time;
                Assert.False(JobsUtility.IsExecutingJob);
            }
            finally
            {
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(go);
            }
        }

        static Exception s_RootFailure, s_AnimationFailure;
        static bool s_Executing;
        static Vector3 s_StreamPosition, s_ScenePosition;

        struct ContextJob : IAnimationJob
        {
            public TransformStreamHandle Stream;
            public TransformSceneHandle Scene;
            public void ProcessRootMotion(AnimationStream stream)
            {
                try { _ = Time.time; } catch (Exception e) { s_RootFailure = e; }
            }
            public void ProcessAnimation(AnimationStream stream)
            {
                s_Executing = JobsUtility.IsExecutingJob;
                try { Physics.Raycast(Vector3.zero, Vector3.up, 1); } catch (Exception e) { s_AnimationFailure = e; }
                s_StreamPosition = Stream.GetLocalPosition(stream);
                s_ScenePosition = Scene.GetLocalPosition(stream);
            }
        }

        [Fact]
        public void AnimationCallbacksAreJobsWhileTheirStreamHandlesKeepWorking()
        {
            var go = new GameObject("Animation context");
            var child = new GameObject("Child");
            child.transform.SetParent(go.transform, false);
            child.transform.localPosition = new Vector3(1, 2, 3);
            var animator = go.AddComponent<Animator>();
            var graph = PlayableGraph.Create("Thread API context");
            try
            {
                s_RootFailure = s_AnimationFailure = null;
                s_Executing = false;
                var playable = AnimationScriptPlayable.Create(graph, new ContextJob
                {
                    Stream = animator.BindStreamTransform(child.transform),
                    Scene = animator.BindSceneTransform(child.transform),
                });
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
                output.SetSourcePlayable(playable);
                graph.Play();
                graph.Evaluate(0.02f);
                Assert.True(s_Executing);
                Assert.IsType<UnityException>(s_RootFailure);
                Assert.IsType<UnityException>(s_AnimationFailure);
                Assert.True(float.IsFinite(s_StreamPosition.x) && float.IsFinite(s_StreamPosition.y) && float.IsFinite(s_StreamPosition.z));
                Assert.Equal(new Vector3(1, 2, 3), s_ScenePosition);
                Assert.False(JobsUtility.IsExecutingJob);
                _ = Time.time;
            }
            finally { graph.Destroy(); Object.DestroyImmediate(go); }
        }

        [Fact]
        public void SuccessfulCheckedPropertyReadsDoNotAllocate()
        {
            var go = new GameObject("Checked getter");
            try
            {
                var body = go.AddComponent<Rigidbody>();
                float sum = 0;
                for (int i = 0; i < 10000; i++) sum += body.mass;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 10000; i++) sum += body.mass;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(0, allocated);
                Assert.Equal(20000f, sum);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }

    public sealed class ConstructorTimeReader : MonoBehaviour
    {
        public string Field = Read();
        public string Body;
        public bool AwakeReadTime;
        public ConstructorTimeReader() { Body = Read(); }
        internal static string Read()
        {
            try { _ = Time.time; return "allowed"; }
            catch (UnityException e) { return e.Message; }
        }
        void Awake() { _ = Time.time; AwakeReadTime = true; }
    }

    public sealed class ConstructorAssetReader : ScriptableObject
    {
        public string Field = ConstructorTimeReader.Read();
        public bool EnabledReadTime;
        void OnEnable() { _ = Time.time; EnabledReadTime = true; }
    }

    public sealed class ThrowingApiConstructor : MonoBehaviour
    {
        public ThrowingApiConstructor() { _ = Time.time; }
    }
}
