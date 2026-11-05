using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Unbound.Tests;
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

        // [choice] A skinned renderer's culling bounds are approximate in Unbound, so it counts as
        // visible while enabled: a CullCompletely animator over only a SkinnedMeshRenderer keeps
        // animating (never frozen on screen), and the approximation is reported. Red at 8f6c1e01:
        // the skinned renderer is not visible (no camera), so the update is skipped
        // (normalizedTime stays 0) and nothing is reported.
        [Fact]
        public void CullCompletelyCountsAnApproximateSkinnedRendererAsVisible()
        {
            static int Hits() => Stubs.HitReport().FirstOrDefault(h => h.Key == "Animation culling with approximate renderer bounds").Value;
            var (animator, text, _) = Rig(AnimatorCullingMode.CullCompletely, withRenderer: false);
            var body = new GameObject("SkinnedBody");
            body.layer = kLayer;
            body.transform.SetParent(animator.transform, false);
            var skinned = body.AddComponent<SkinnedMeshRenderer>();
            Assert.False(skinned.isVisible);
            int before = Hits();
            animator.Tick(0f);
            animator.Tick(0.15f);
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).normalizedTime > 0f);
            Assert.True(Hits() > before);
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

        /// <summary>CullUpdateTransforms is not applied yet (the transform write is inside the
        /// player's evaluation): the documented half, the state machine keeps updating, holds; the
        /// missing half is reported. The transforms are deliberately not asserted.</summary>
        [Fact]
        public void CullUpdateTransformsKeepsTheStateMachineAndReportsTheMissingTransformCulling()
        {
            var (animator, _, renderer) = Rig(AnimatorCullingMode.CullUpdateTransforms);
            Assert.False(renderer.isVisible);
            animator.Tick(0f);
            animator.Tick(0.25f);
            Near.Equal(0.25f / 0.3f, animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1e-4f);
            Assert.Contains(Stubs.HitReport(), h => h.Key == "Animator.cullingMode CullUpdateTransforms" && h.Value > 0);
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

        [Fact]
        public void BasedOnRenderersResumesWithoutCatchingUpWhenARendererBecomesVisible()
        {
            var camera = MakeCamera();
            var (animation, receiver, renderer) = LegacyRig();
            for (int i = 0; i < 5; i++) animation.Step(0.1f);
            MakeVisible(camera, renderer);

            animation.Step(0.1f); // the state's first update samples its start (measured)
            Near.Equal(5f, animation.transform.localPosition.x, 1e-4f);
            animation.Step(0.2f);
            Near.Equal(0.2f, animation["loop"].time, 1e-4f);
            Near.Equal(5.2f, animation.transform.localPosition.x, 1e-4f);
            Assert.Empty(receiver.Calls);
            animation.Step(0.1f); // crosses the event at 0.25
            Assert.Equal(new[] { "OnHit(3)" }, receiver.Calls);
        }
    }
}
