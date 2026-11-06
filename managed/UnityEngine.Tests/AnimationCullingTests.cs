using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Unbound.Tests;
using Unbound.Runtime.Playables;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Animator.cullingMode and Animation.cullingType in the player loop's update (Animator.Tick,
    /// Animation.Step). Documented (6000.0 AnimatorCullingMode / AnimationCullingType): culling
    /// applies "when renderers are not visible"; a culled Animation "won't update animation states,
    /// execute events or sample animations". Visibility comes from the engine's own camera culling
    /// (FrameBuilder.CullCamera marks the renderers a camera takes; a renderer no camera took is not
    /// visible). The rig sits far from every other test's camera, on its own layer, so that culling
    /// it and other tests' cameras do not see each other's renderers.
    /// </summary>
    [Collection("Animation")]
    public class AnimationCullingTests : System.IDisposable
    {
        const int kLayer = 31;
        static readonly Vector3 kFar = new Vector3(1000f, 0f, 0f);

        readonly List<Object> m_Objects = new List<Object>();

        public void Dispose()
        {
            foreach (var o in m_Objects) Object.DestroyImmediate(o);
        }

        static LoadedFile Load(string yaml) =>
            ObjectGraphBuilder.Build(UnityYaml.Parse(yaml), "test.asset", null, ObjectGraphBuilder.Kind.Asset, null);

        Camera MakeCamera()
        {
            var camera = new GameObject("CullingCamera").AddComponent<Camera>();
            m_Objects.Add(camera.gameObject);
            camera.transform.position = kFar;
            camera.fieldOfView = 60;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 100;
            camera.aspect = 16f / 9f;
            camera.cullingMask = 1 << kLayer;
            return camera;
        }

        /// <summary>A renderer on <paramref name="go"/>, in front of <see cref="MakeCamera"/>'s camera.</summary>
        static Renderer AddRenderer(GameObject go)
        {
            go.layer = kLayer;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.bounds = new Bounds(kFar + new Vector3(0f, 0f, 10f), Vector3.one);
            return renderer;
        }

        /// <summary>The popup controller's rig (a 0.3 s non-looping state that scales and moves
        /// ContinueText), with a renderer on a second child when <paramref name="withRenderer"/>.</summary>
        (Animator Animator, RectTransform Text, Renderer Renderer) Rig(AnimatorCullingMode mode, bool withRenderer = true)
        {
            var file = Load(AnimationFixtures.PopupButton);
            var controller = (RuntimeAnimatorController)file.Objects[9100000];
            var root = new GameObject("Button", typeof(RectTransform));
            m_Objects.Add(root);
            var text = new GameObject("ContinueText", typeof(RectTransform));
            text.transform.SetParent(root.transform, false);
            Renderer renderer = null;
            if (withRenderer)
            {
                var body = new GameObject("Body");
                body.transform.SetParent(root.transform, false);
                renderer = AddRenderer(body);
            }
            var animator = root.AddComponent<Animator>();
            animator.cullingMode = mode;
            animator.runtimeAnimatorController = controller;
            return (animator, (RectTransform)text.transform, renderer);
        }

        static void MakeVisible(Camera camera, Renderer renderer)
        {
            FrameBuilder.CullCamera(camera);
            Assert.True(renderer.isVisible);
        }

        [Fact]
        public void CullCompletelySkipsTheUpdateWhileNoRendererIsVisible()
        {
            var (animator, text, renderer) = Rig(AnimatorCullingMode.CullCompletely);
            Assert.False(renderer.isVisible); // no camera took it
            animator.Tick(0f);
            animator.Tick(0.25f);
            // No state advance, no transform write.
            Near.Equal(0f, animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
            Near.Equal(Vector3.one, text.localScale);
            Near.Equal(0f, text.anchoredPosition.y);
        }

        /// <summary>A skinned renderer no camera took culls a CullCompletely Animator like any other,
        /// whatever its updateWhenOffscreen (measured, tests/projects/animculling s/*).</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CullCompletelyCullsByASkinnedRendererVisibility(bool updateWhenOffscreen)
        {
            var (animator, _, _) = Rig(AnimatorCullingMode.CullCompletely, withRenderer: false);
            var body = new GameObject("SkinnedBody");
            body.layer = kLayer;
            body.transform.SetParent(animator.transform, false);
            var skinned = body.AddComponent<SkinnedMeshRenderer>();
            skinned.updateWhenOffscreen = updateWhenOffscreen;
            Assert.False(skinned.isVisible);
            animator.Tick(0f);
            animator.Tick(0.15f);
            Near.Equal(0f, animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
        }

        [Fact]
        public void CullCompletelyResumesWithoutCatchingUpWhenARendererBecomesVisible()
        {
            var camera = MakeCamera();
            var control = Rig(AnimatorCullingMode.AlwaysAnimate, withRenderer: false);
            control.Animator.Tick(0f);
            control.Animator.Tick(0.25f);

            var (animator, text, renderer) = Rig(AnimatorCullingMode.CullCompletely);
            animator.Tick(0f);
            animator.Tick(0.25f);
            animator.Tick(0.25f);
            MakeVisible(camera, renderer);
            animator.Tick(0f);
            animator.Tick(0.25f);

            // The culled updates left nothing behind: the animator is where the control is after
            // the same two visible updates (Unbound's reading of "completely disabled": no catch-up).
            Near.Equal(0.25f / 0.3f, animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1e-4f);
            Near.Equal(control.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime, animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1e-6f);
            Near.Equal(new Vector3(1.0884f, 1.0884f, 1.0884f), text.localScale, 1e-4f);
            Near.Equal(control.Text.anchoredPosition.y, text.anchoredPosition.y, 1e-5f);
        }

        [Fact]
        public void AlwaysAnimateAnimatesWithoutAVisibleRenderer()
        {
            var (animator, text, renderer) = Rig(AnimatorCullingMode.AlwaysAnimate);
            Assert.False(renderer.isVisible);
            animator.Tick(0f);
            Near.Equal(-69.3f, text.anchoredPosition.y, 1e-3f);
            animator.Tick(0.25f);
            Near.Equal(0.25f / 0.3f, animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1e-4f);
            Near.Equal(30f, text.anchoredPosition.y, 1e-3f);
            Near.Equal(new Vector3(1.0884f, 1.0884f, 1.0884f), text.localScale, 1e-4f);
        }

        /// <summary>CullUpdateTransforms keeps the state machine updating and skips transform writes while
        /// no renderer is visible; the renderer becoming visible in a camera's culling writes the
        /// current pose (measured, tests/projects/animculling a/transforms, h/transforms).</summary>
        [Fact]
        public void CullUpdateTransformsAdvancesWithoutWritingUntilARendererBecomesVisible()
        {
            var camera = MakeCamera();
            var (animator, text, renderer) = Rig(AnimatorCullingMode.CullUpdateTransforms);
            Assert.False(renderer.isVisible);
            animator.Tick(0f);
            animator.Tick(0.25f);
            Near.Equal(0.25f / 0.3f, animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1e-4f);
            Near.Equal(Vector3.one, text.localScale);
            Near.Equal(0f, text.anchoredPosition.y);
            MakeVisible(camera, renderer);
            Near.Equal(new Vector3(1.0884f, 1.0884f, 1.0884f), text.localScale, 1e-4f);
            Near.Equal(30f, text.anchoredPosition.y, 1e-3f);
        }

        /// <summary>CullUpdateTransforms leaves only the Transforms (and the humanoid pose) unwritten:
        /// a clip's parameter curve still drives its parameter while culled, so the state machine's
        /// conditions keep following it (documented: only "write of Transforms" is disabled).</summary>
        [Fact]
        public void CullUpdateTransformsStillWritesParameterCurves()
        {
            var file = Load(AnimationFixtures.Locomotion);
            var idle = (AnimationClip)file.Objects[7400001];
            idle.m_Curves.Add(new ClipCurve
            {
                Binding = new CurveBinding { Path = "", Attribute = "Speed", ClassId = 95 },
                Curve = AnimationCurve.Constant(0f, 1f, 0.8f),
            });
            idle.m_Version++;
            var root = new GameObject("Character");
            m_Objects.Add(root);
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var renderer = AddRenderer(body);
            var animator = root.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            animator.runtimeAnimatorController = (RuntimeAnimatorController)file.Objects[9100000];
            Assert.False(renderer.isVisible);
            Near.Equal(0.5f, animator.GetFloat("Speed"));
            animator.Tick(0f);
            animator.Tick(0.1f);
            Near.Equal(0.8f, animator.GetFloat("Speed"));
        }

        /// <summary>Channels bound after a culled update (here a clip bound to the Animator's
        /// bindings, as a playable output's would be) do not break the write on becoming visible:
        /// the poses are sampled again at their size.</summary>
        [Fact]
        public void BecomingVisibleAfterNewBindingsWritesThePose()
        {
            var logs = new List<string>();
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            logger.logHandler = new ExceptionCapture(logs);
            try
            {
                var camera = MakeCamera();
                var (animator, text, renderer) = Rig(AnimatorCullingMode.CullUpdateTransforms);
                animator.Tick(0f);
                animator.Tick(0.25f);
                var extra = new AnimationClip { legacy = true, name = "extra" };
                extra.SetCurve("Body", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 1f, 1f, 2f));
                animator.m_Player.Bindings.Bind(extra);
                MakeVisible(camera, renderer);
                Assert.Empty(logs);
                Near.Equal(new Vector3(1.0884f, 1.0884f, 1.0884f), text.localScale, 1e-4f);
            }
            finally
            {
                logger.logHandler = previous;
            }
        }

        sealed class ExceptionCapture : ILogHandler
        {
            readonly List<string> m_Logs;
            internal ExceptionCapture(List<string> logs) => m_Logs = logs;
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Error || type == LogType.Exception) m_Logs.Add(format);
            }
            public void LogException(System.Exception exception, Object context) => m_Logs.Add(exception.ToString());
        }

        static AnimationClip LegacyLoop()
        {
            var clip = new AnimationClip { legacy = true, wrapMode = WrapMode.Loop, name = "loop" };
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 5f, 1f, 6f));
            clip.AddEvent(new AnimationEvent { time = 0.25f, functionName = "OnHit", floatParameter = 3f });
            return clip;
        }

        (Animation Animation, AnimationEventReceiver Receiver, Renderer Renderer) LegacyRig()
        {
            var go = new GameObject("Legacy");
            m_Objects.Add(go);
            var renderer = AddRenderer(go);
            var receiver = go.AddComponent<AnimationEventReceiver>();
            var animation = go.AddComponent<Animation>();
            animation.cullingType = AnimationCullingType.BasedOnRenderers;
            animation.AddClip(LegacyLoop(), "loop");
            Assert.True(animation.Play("loop"));
            return (animation, receiver, renderer);
        }

        [Fact]
        public void BasedOnRenderersDoesNotAnimateWhileNoRendererIsVisible()
        {
            var (animation, receiver, renderer) = LegacyRig();
            Assert.False(renderer.isVisible);
            for (int i = 0; i < 5; i++) animation.Step(0.1f);
            // Documented: no state update, no events, no sampling.
            Assert.Equal(0f, animation["loop"].time);
            Assert.Empty(receiver.Calls);
            Assert.Equal(0f, animation.transform.localPosition.x);
        }

        /// <summary>The first update that runs again catches up the culled updates' time, and the
        /// skipped span's events fire then; the state's first update (sampling its start) is spent
        /// while culled (measured, tests/projects/animculling l/based/*).</summary>
        [Fact]
        public void BasedOnRenderersCatchesUpWhenARendererBecomesVisible()
        {
            var camera = MakeCamera();
            var (animation, receiver, renderer) = LegacyRig();
            for (int i = 0; i < 5; i++) animation.Step(0.1f);
            Assert.Equal(0f, animation["loop"].time);
            Assert.Empty(receiver.Calls);
            MakeVisible(camera, renderer);

            animation.Step(0.1f);
            Near.Equal(0.5f, animation["loop"].time, 1e-4f);
            Near.Equal(5.5f, animation.transform.localPosition.x, 1e-4f);
            Assert.Equal(new[] { "OnHit(3)" }, receiver.Calls);
        }
        // Automatic animation passes, using the same prepare/output/controller sequence as the
        // player loop, with an explicit delta so these tests do not depend on a running clock.
        static void PlayableStep(PlayableGraph graph, Animator animator, float dt)
        {
            var data = PlayableGraphData.Get(graph);
            data.PreparePass(dt, evaluate: false);
            data.FrameId++;
            foreach (var output in data.Outputs.ToArray()) OutputSystems.Process(output, dt, false);
            animator.Tick(dt);
            AnimationOutputs.RunPending(animator);
        }

        static PlayableGraph PlayableRig(Animator animator, out AnimationClipPlayable playable)
        {
            var clip = new AnimationClip { legacy = true };
            // Imported position curves carry the whole vector, as the authored probe clips do.
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0f, 1f, 0f));
            clip.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            clip.SetCurve("", typeof(Transform), "localPosition.z", AnimationCurve.Constant(0f, 1f, 0f));
            clip.SetCurve("ContinueText", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 2f));
            clip.legacy = false;
            var graph = PlayableGraph.Create("culled-output");
            var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            playable = AnimationClipPlayable.Create(graph, clip);
            output.SetSourcePlayable(playable);
            graph.Play();
            return graph;
        }

        [Theory]
        [InlineData(AnimatorCullingMode.CullCompletely)]
        [InlineData(AnimatorCullingMode.CullUpdateTransforms)]
        public void PlayableCullingControlsTimeAndWritesAndRestoresTheCombinedPose(AnimatorCullingMode mode)
        {
            var camera = MakeCamera();
            var (animator, text, renderer) = Rig(mode);
            animator.runtimeAnimatorController = null;
            var graph = PlayableRig(animator, out var playable);
            try
            {
                PlayableStep(graph, animator, 0.1f);
                PlayableStep(graph, animator, 0.1f);
                float time = mode == AnimatorCullingMode.CullCompletely ? 0f : 0.2f;
                Near.Equal(time, (float)playable.GetTime());
                Near.Equal(0f, text.localPosition.x);
                // The Animator's own root curves still write with CullUpdateTransforms.
                Near.Equal(time, animator.transform.localPosition.y);
                MakeVisible(camera, renderer);
                Near.Equal(time * 2f, text.localPosition.x);
                PlayableStep(graph, animator, 0.1f);
                Near.Equal(time + 0.1f, (float)playable.GetTime());
                Near.Equal((time + 0.1f) * 2f, text.localPosition.x);
            }
            finally { graph.Destroy(); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AnOutputReplaysItsLastVisibleMotionAfterSkippedUpdates(bool controller)
        {
            var camera = MakeCamera();
            // Root movement is measured on a generic Transform rig, as in animculling p/*.
            var root = new GameObject("PlayableActor");
            m_Objects.Add(root);
            var text = new GameObject("ContinueText", typeof(RectTransform)).transform;
            text.SetParent(root.transform, false);
            var renderer = AddRenderer(root);
            var animator = root.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.CullCompletely;
            if (controller) animator.runtimeAnimatorController = (RuntimeAnimatorController)Load(AnimationFixtures.PopupButton).Objects[9100000];
            animator.applyRootMotion = true;
            var graph = PlayableRig(animator, out var playable);
            try
            {
                PlayableStep(graph, animator, 0.1f);
                MakeVisible(camera, renderer);
                PlayableStep(graph, animator, 0.1f);
                Near.Equal(0.1f, animator.transform.localPosition.y);
                renderer.SetVisible(false);
                PlayableStep(graph, animator, 0.1f);
                PlayableStep(graph, animator, 0.1f);
                Near.Equal(0.1f, (float)playable.GetTime());
                Near.Equal(0.1f, animator.transform.localPosition.y);
                MakeVisible(camera, renderer);
                Near.Equal(0.2f, animator.transform.localPosition.y);
                Near.Equal(0.2f, text.localPosition.x);
                Near.Equal(0.1f, (float)playable.GetTime());
            }
            finally { graph.Destroy(); }
        }

        [Fact]
        public void AnUnrelatedOutputPreservesPartialControllerLayersAndTheirHeldValues()
        {
            var controller = (RuntimeAnimatorController)Load(AnimationFixtures.PopupButton).Objects[9100000];
            controller.m_Layers.Clear();
            var clip = new AnimationClip { legacy = true };
            clip.SetCurve("Held", typeof(Transform), "localPosition.y", AnimationCurve.Constant(0f, 1f, 4f));
            clip.legacy = false;
            var empty = new ControllerStateAsset { name = "Empty", WriteDefaults = false };
            var moving = new ControllerStateAsset { name = "Moving", Motion = clip };
            var holding = new ControllerStateAsset { name = "Hold", WriteDefaults = false };
            var basis = new ControllerStateMachineAsset { name = "Base", DefaultState = empty };
            basis.States.Add(empty);
            var partial = new ControllerStateMachineAsset { name = "Partial", DefaultState = moving };
            partial.States.Add(moving);
            partial.States.Add(holding);
            controller.m_Layers.Add(new ControllerLayerData { Name = "Base", StateMachine = basis });
            controller.m_Layers.Add(new ControllerLayerData { Name = "Partial", StateMachine = partial, DefaultWeight = 0.5f });
            foreach (var asset in new Object[] { controller, clip, empty, moving, holding, basis, partial }) m_Objects.Add(asset);

            (Animator Animator, Transform Held) Actor(string name)
            {
                var root = new GameObject(name);
                m_Objects.Add(root);
                var held = new GameObject("Held").transform;
                held.SetParent(root.transform, false);
                new GameObject("ContinueText").transform.SetParent(root.transform, false);
                var animator = root.AddComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.runtimeAnimatorController = controller;
                animator.Tick(0f);
                return (animator, held);
            }
            var control = Actor("ControllerOnly");
            var output = Actor("ControllerAndOutput");
            // This output adds bindings for another child after the controller already evaluated.
            var graph = PlayableRig(output.Animator, out _);
            try
            {
                for (int frame = 0; frame < 4; frame++)
                {
                    if (frame == 2)
                    {
                        control.Animator.Play("Hold", 1, 0f);
                        output.Animator.Play("Hold", 1, 0f);
                        // A no-defaults state retains the controller's value despite game writes.
                        control.Held.localPosition = output.Held.localPosition = new Vector3(0f, 9f, 0f);
                    }
                    control.Animator.Tick(0.1f);
                    PlayableStep(graph, output.Animator, 0.1f);
                    Near.Equal(2f, control.Held.localPosition.y);
                    Near.Equal(control.Held.localPosition.y, output.Held.localPosition.y);
                    Near.Equal(0.2f * (frame + 1), output.Animator.transform.Find("ContinueText").localPosition.x);
                }
            }
            finally { graph.Destroy(); }
        }

        public class BindDuringAnimationEvent : MonoBehaviour
        {
            public Transform Target;
            public int Calls;
            void BindNow()
            {
                Calls++;
                GetComponent<Animator>().BindStreamTransform(Target);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EventBindingsPreserveTheSampledPoseAndRootMotion(bool writeDefaults)
        {
            var controller = (RuntimeAnimatorController)Load(AnimationFixtures.PopupButton).Objects[9100000];
            controller.m_Layers.Clear();
            var clip = new AnimationClip { legacy = true };
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0f, 1f, 0f));
            clip.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            clip.SetCurve("", typeof(Transform), "localPosition.z", AnimationCurve.Constant(0f, 1f, 0f));
            clip.SetCurve("ContinueText", typeof(Transform), "localPosition.y", AnimationCurve.Constant(0f, 1f, 4f));
            clip.legacy = false;
            clip.AddEvent(new AnimationEvent { time = 0.05f, functionName = "BindNow" });
            var state = new ControllerStateAsset { name = "Moving", Motion = clip, WriteDefaults = writeDefaults };
            var machine = new ControllerStateMachineAsset { name = "Base", DefaultState = state };
            machine.States.Add(state);
            controller.m_Layers.Add(new ControllerLayerData { Name = "Base", StateMachine = machine });
            foreach (var asset in new Object[] { controller, clip, state, machine }) m_Objects.Add(asset);
            var root = new GameObject("EventBindings");
            m_Objects.Add(root);
            root.transform.localPosition = new Vector3(0f, 5f, 0f);
            var child = new GameObject("ContinueText").transform;
            child.SetParent(root.transform, false);
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = true;
            var receiver = root.AddComponent<BindDuringAnimationEvent>();
            receiver.Target = child;
            int before = animator.StreamBindings.FloatCount;
            var graph = PlayableRig(animator, out _);
            try
            {
                PlayableStep(graph, animator, 0.1f);
                Assert.Equal(1, receiver.Calls);
                Assert.True(animator.StreamBindings.FloatCount > before);
                Near.Equal(new Vector3(0.2f, 4f, 0f), child.localPosition);
                Near.Equal(5.1f, root.transform.localPosition.y);
                Near.Equal(0.1f, animator.deltaPosition.y);
                PlayableStep(graph, animator, 0.1f);
                Assert.Equal(1, receiver.Calls);
                Near.Equal(new Vector3(0.4f, 4f, 0f), child.localPosition);
                Near.Equal(5.2f, root.transform.localPosition.y);
            }
            finally { graph.Destroy(); }
        }

        public class PlayableMoveState : StateMachineBehaviour
        {
            public static int Calls;
            public override void OnStateMove(Animator animator, AnimatorStateInfo state, int layer) => Calls++;
        }

        [Theory]
        [InlineData(AnimatorCullingMode.AlwaysAnimate)]
        [InlineData(AnimatorCullingMode.CullUpdateTransforms)]
        public void PendingOutputsPreserveTheAttachedControllersStateMovementCallbacks(AnimatorCullingMode mode)
        {
            var file = Load(AnimationFixtures.PopupButton);
            var prototype = ScriptableObject.CreateInstance<PlayableMoveState>();
            m_Objects.Add(prototype);
            foreach (var state in file.Objects.Values.OfType<ControllerStateAsset>()) state.Behaviours.Add(prototype);
            var root = new GameObject("PlayableController");
            m_Objects.Add(root);
            new GameObject("ContinueText").transform.SetParent(root.transform, false);
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = (RuntimeAnimatorController)file.Objects[9100000];
            animator.cullingMode = mode;
            var graph = PlayableRig(animator, out _);
            try
            {
                PlayableMoveState.Calls = 0;
                PlayableStep(graph, animator, 0.1f);
                Assert.Equal(1, PlayableMoveState.Calls);
            }
            finally { graph.Destroy(); }
        }

        [Fact]
        public void ASharedPlayableStillAdvancesForAnUnculledOutput()
        {
            var (animator, _, _) = Rig(AnimatorCullingMode.CullCompletely);
            animator.runtimeAnimatorController = null;
            var graph = PlayableRig(animator, out var playable);
            try
            {
                var script = ScriptPlayableOutput.Create(graph, "Script");
                script.SetSourcePlayable(playable);
                PlayableStep(graph, animator, 0.1f);
                PlayableStep(graph, animator, 0.1f);
                Near.Equal(0.2f, (float)playable.GetTime());
            }
            finally { graph.Destroy(); }
        }

    }
}
