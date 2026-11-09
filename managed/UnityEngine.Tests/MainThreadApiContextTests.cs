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
        static bool s_Executing, s_RootStreamChecked, s_AnimationStreamChecked;
        static bool s_RootHumanStream, s_AnimationHumanStream;
        static Vector3 s_StreamPosition, s_ScenePosition;

        struct ContextJob : IAnimationJob
        {
            public TransformStreamHandle Stream;
            public TransformSceneHandle Scene;
            public void ProcessRootMotion(AnimationStream stream)
            {
                s_RootHumanStream = stream.isHumanStream;
                s_RootStreamChecked = true;
                try { _ = Time.time; } catch (Exception e) { s_RootFailure = e; }
            }
            public void ProcessAnimation(AnimationStream stream)
            {
                s_AnimationHumanStream = stream.isHumanStream;
                s_AnimationStreamChecked = true;
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
                s_Executing = s_RootStreamChecked = s_AnimationStreamChecked = false;
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
                Assert.True(s_RootStreamChecked && s_AnimationStreamChecked);
                Assert.False(s_RootHumanStream);
                Assert.False(s_AnimationHumanStream);
                Assert.IsType<UnityException>(s_RootFailure);
                Assert.IsType<UnityException>(s_AnimationFailure);
                Assert.True(float.IsFinite(s_StreamPosition.x) && float.IsFinite(s_StreamPosition.y) && float.IsFinite(s_StreamPosition.z));
                Assert.Equal(new Vector3(1, 2, 3), s_ScenePosition);
                Assert.False(JobsUtility.IsExecutingJob);
                _ = Time.time;
            }
            finally { graph.Destroy(); Object.DestroyImmediate(go); }
        }

        sealed class DefaultMaterialPipeline : UnityEngine.Rendering.RenderPipelineAsset
        {
            internal Material Material;
            public override Material defaultMaterial => Material;
            protected override UnityEngine.Rendering.RenderPipeline CreatePipeline() => null;
        }

        [Fact]
        public void NullMaterialSlotsUseOwnedCopiesOfThePipelineDefault()
        {
            var previous = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            var quality = QualitySettings.renderPipeline;
            var pipeline = ScriptableObject.CreateInstance<DefaultMaterialPipeline>();
            var shader = Shader.Create("Default material control", 2000);
            var material = new Material(shader) { name = "Pipeline default" };
            pipeline.Material = material;
            var go = new GameObject("Material controls");
            try
            {
                QualitySettings.renderPipeline = null;
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = pipeline;
                var renderer = go.AddComponent<MeshRenderer>();
                var other = go.AddComponent<MeshRenderer>();
                Assert.Null(renderer.sharedMaterial);
                Assert.NotSame(material, renderer.material);
                Assert.Equal("Pipeline default (Instance)", renderer.material.name);
                Assert.Same(renderer.material, renderer.sharedMaterial);
                Assert.NotSame(renderer.material, other.material);
                renderer.sharedMaterials = Array.Empty<Material>();
                Assert.Empty(renderer.materials);
                Assert.NotNull(renderer.material);
                Assert.Single(renderer.sharedMaterials);
            }
            finally
            {
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = previous;
                QualitySettings.renderPipeline = quality;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
                Object.DestroyImmediate(pipeline);
            }
        }

        [Fact]
        public void FollowupBindingsRejectWorkerAccessBeforeChangingState()
        {
            var go = new GameObject("Guard controls");
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                var animator = go.AddComponent<Animator>();
                var audio = go.AddComponent<AudioSource>();
                Assert.IsType<UnityException>(OnWorker(() => renderer.forceRenderingOff = true));
                Assert.False(renderer.forceRenderingOff);
                Assert.StartsWith("GetLightmapIndex can only", OnWorker(() => _ = renderer.lightmapIndex).Message);
                Assert.IsType<UnityException>(OnWorker(() => animator.fireEvents = false));
                Assert.True(animator.fireEvents);
                Assert.IsType<UnityException>(OnWorker(() => audio.priority = 1));
                Assert.Equal(128, audio.priority);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void ControllerlessInitializationFollowsHierarchyActivation()
        {
            var go = new GameObject("Initialization controls");
            go.SetActive(false);
            try
            {
                var animator = go.AddComponent<Animator>();
                Assert.False(animator.isInitialized);
                animator.enabled = false;
                animator.Rebind();
                Assert.False(animator.isInitialized);
                go.SetActive(true);
                Assert.True(animator.isInitialized);
                animator.Rebind();
                Assert.True(animator.isInitialized);
                animator.enabled = true;
                animator.enabled = false;
                Assert.True(animator.isInitialized);
                go.SetActive(false);
                Assert.False(animator.isInitialized);
                animator.enabled = true;
                go.SetActive(true);
                animator.keepAnimatorStateOnDisable = true;
                go.SetActive(false);
                Assert.False(animator.isInitialized);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Fact]
        public void CheckedFollowupPropertiesDoNotAllocate()
        {
            var go = new GameObject("Followup getter controls");
            try
            {
                var renderer = go.AddComponent<MeshRenderer>();
                var animator = go.AddComponent<Animator>();
                var audio = go.AddComponent<AudioSource>();
                int sum = 0;
                for (int i = 0; i < 10000; i++)
                    sum += renderer.rendererPriority + audio.priority + (animator.fireEvents ? 1 : 0);
                long before = GC.GetAllocatedBytesForCurrentThread();
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 0; i < 1000000; i++)
                    sum += renderer.rendererPriority + audio.priority + (animator.fireEvents ? 1 : 0);
                var ticks = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(0, allocated);
                Assert.Equal(1010000 * 129, sum);
                Console.WriteLine("Checked followup getter: " + (ticks * 1e9 / System.Diagnostics.Stopwatch.Frequency / 3000000) + " ns, " + allocated + " bytes");
            }
            finally { Object.DestroyImmediate(go); }
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
