using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    public class AnimationEventReceiver : MonoBehaviour
    {
        public readonly List<string> Calls = new List<string>();

        void OnHit(float value) => Calls.Add($"OnHit({value})");
        void OnPulse(AnimationEvent e) => Calls.Add($"OnPulse({e.stringParameter},{e.floatParameter},{e.intParameter},{e.time})");
        void OnStep() => Calls.Add("OnStep");
    }

    public class StateRecorder : StateMachineBehaviour
    {
        public static readonly List<string> Calls = new List<string>();

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) =>
            Calls.Add($"enter {Name(stateInfo)} {layerIndex}");

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) =>
            Calls.Add($"update {Name(stateInfo)}");

        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) =>
            Calls.Add($"exit {Name(stateInfo)}");

        static string Name(AnimatorStateInfo info) => info.IsName("Rise") ? "Rise" : info.IsName("Fall") ? "Fall" : "?";
    }

    /// <summary>Samples another clip on its parent when it is enabled (game code run by an animation write).</summary>
    public class SampleOnEnable : MonoBehaviour
    {
        public static AnimationClip Clip;

        void OnEnable()
        {
            if (Clip != null) Clip.SampleAnimation(transform.parent.gameObject, 0f);
        }
    }

    /// <summary>A script with a serialized color, like uGUI's Graphic.</summary>
    public class FakeGraphic : MonoBehaviour
    {
        [SerializeField] Color m_Color = Color.white;
        [SerializeField] bool m_Flag = false;
        public int applied;

        public Color Color => m_Color;
        public bool Flag => m_Flag;

        void OnDidApplyAnimationProperties() => applied++;
    }

    [CollectionDefinition("Animation", DisableParallelization = true)]
    public class AnimationCollection
    {
    }

    /// <summary>Runs alone so that the warnings and errors logged during a test are its own: any
    /// unexpected one fails it.</summary>
    [Collection("Animation")]
    public class AnimationTests : System.IDisposable
    {
        readonly List<string> m_Logs = new List<string>();
        readonly List<string> m_Expected = new List<string>();

        public AnimationTests()
        {
            Application.logMessageReceived += OnLog;
        }

        void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Log) m_Logs.Add($"{type}: {message}");
        }

        void Expect(string fragment) => m_Expected.Add(fragment);

        public void Dispose()
        {
            Application.logMessageReceived -= OnLog;
            var unexpected = m_Logs.FindAll(l => !m_Expected.Exists(e => l.Contains(e)));
            Assert.True(unexpected.Count == 0, string.Join("\n", unexpected));
        }

        static LoadedFile Load(string yaml) =>
            ObjectGraphBuilder.Build(UnityYaml.Parse(yaml), "test.asset", null, ObjectGraphBuilder.Kind.Asset, null);

        static GameObject Hierarchy(string root, params string[] children)
        {
            var go = new GameObject(root);
            foreach (var name in children)
            {
                var child = new GameObject(name);
                child.transform.SetParent(go.transform, false);
            }
            return go;
        }

        [Fact]
        public void StringToHashMatchesTheHashesUnityWrote()
        {
            // genericBindings of the project's clips store path and attribute hashes.
            Assert.Equal(965236745, Animator.StringToHash("ContinueText"));
            Assert.Equal(538195251, Animator.StringToHash("m_AnchoredPosition.y"));
            Assert.Equal(unchecked((int)3218135022u), Animator.StringToHash("Circle 1"));
            Assert.Equal(651789908, Animator.StringToHash("Circle 2"));
            Assert.Equal(1373550274, Animator.StringToHash("Circle 3"));
            Assert.Equal(304273561, Animator.StringToHash("m_Color.a"));
            Assert.Equal(0, Animator.StringToHash(""));
        }

        [Fact]
        public void LegacyClipSamplesTransformCurves()
        {
            var clip = new AnimationClip { legacy = true };
            clip.SetCurve("Child", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 10f));
            clip.SetCurve("", typeof(Transform), "localScale.y", AnimationCurve.Linear(0f, 1f, 2f, 3f));
            Assert.Equal(2f, clip.length);
            Assert.False(clip.empty);
            var root = Hierarchy("Root", "Child");
            var child = root.transform.Find("Child");
            child.localPosition = new Vector3(0f, 4f, 0f);

            clip.SampleAnimation(root, 0.5f);
            Near.Equal(new Vector3(5f, 4f, 0f), child.localPosition);
            Near.Equal(new Vector3(1f, 1.5f, 1f), root.transform.localScale);

            clip.SampleAnimation(root, 5f); // clamped to the end
            Near.Equal(10f, child.localPosition.x);
            Object.DestroyImmediate(root);
        }

        [Fact]
        public void ClipFromYamlAnimatesSpriteRendererColorAndScale()
        {
            var file = Load(AnimationFixtures.CircleIdle);
            var clip = (AnimationClip)file.Objects[7400000];
            Assert.Equal("Idle", clip.name);
            Assert.Equal(1.5f, clip.length);
            Assert.True(clip.isLooping);
            Assert.False(clip.legacy);
            Assert.Equal(60f, clip.frameRate);
            var events = clip.events;
            Assert.Single(events);
            Assert.Equal("OnPulse", events[0].functionName);
            Assert.Equal("half", events[0].stringParameter);
            Assert.Equal(7, events[0].intParameter);

            var root = Hierarchy("Badge", "Circle 1");
            var circle = root.transform.Find("Circle 1");
            var sr = circle.gameObject.AddComponent<SpriteRenderer>();
            sr.color = new Color(1f, 0.5f, 0.25f, 1f);

            clip.SampleAnimation(root, 1.1666666f);
            Near.Equal(new Color(1f, 0.5f, 0.25f, 0.1882353f), sr.color, 1e-4f);
            Near.Equal(new Vector3(1.04f, 1.04f, 1.04f), circle.localScale, 1e-4f);

            clip.SampleAnimation(root, 0.3f);
            Near.Equal(0f, sr.color.a, 1e-5f);
            Object.DestroyImmediate(root);
        }

        [Fact]
        public void ScriptFieldsAreAnimatedAndNotified()
        {
            var clip = new AnimationClip { legacy = true };
            clip.SetCurve("", typeof(FakeGraphic), "m_Color.a", AnimationCurve.Linear(0f, 1f, 1f, 0f));
            clip.SetCurve("", typeof(FakeGraphic), "m_Flag", AnimationCurve.Constant(0f, 1f, 1f));
            var go = new GameObject("Graphic");
            var graphic = go.AddComponent<FakeGraphic>();
            clip.SampleAnimation(go, 0.25f);
            Near.Equal(0.75f, graphic.Color.a);
            Near.Equal(1f, graphic.Color.r);
            Assert.True(graphic.Flag);
            Assert.Equal(1, graphic.applied);
            clip.SampleAnimation(go, 0.25f); // unchanged values: no notification
            Assert.Equal(1, graphic.applied);
            Object.DestroyImmediate(go);
        }

        [Fact]
        public void GameObjectActivityAndEnabledFlagsAreAnimated()
        {
            var clip = new AnimationClip { legacy = true };
            clip.SetCurve("Child", typeof(GameObject), "m_IsActive", new AnimationCurve(new Keyframe(0f, 1f, float.PositiveInfinity, float.PositiveInfinity), new Keyframe(0.5f, 0f, float.PositiveInfinity, float.PositiveInfinity)));
            clip.SetCurve("", typeof(SpriteRenderer), "m_Enabled", AnimationCurve.Constant(0f, 1f, 0f));
            var root = Hierarchy("Root", "Child");
            var sr = root.AddComponent<SpriteRenderer>();
            var child = root.transform.Find("Child").gameObject;
            clip.SampleAnimation(root, 0.25f);
            Assert.True(child.activeSelf);
            Assert.False(sr.enabled);
            clip.SampleAnimation(root, 0.75f);
            Assert.False(child.activeSelf);
            Object.DestroyImmediate(root);
        }

        static AnimationClip LoopingClip()
        {
            var clip = new AnimationClip { legacy = true, wrapMode = WrapMode.Loop, name = "loop" };
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            clip.AddEvent(new AnimationEvent { time = 0.25f, functionName = "OnHit", floatParameter = 3f });
            return clip;
        }

        [Fact]
        public void LegacyAnimationLoopsAndFiresEventsOncePerLoop()
        {
            var go = new GameObject("Legacy");
            var receiver = go.AddComponent<AnimationEventReceiver>();
            var animation = go.AddComponent<Animation>();
            animation.AddClip(LoopingClip(), "loop");
            Assert.Equal(1, animation.GetClipCount());
            Assert.True(animation.Play("loop"));
            Assert.True(animation.isPlaying);
            Assert.True(animation.IsPlaying("loop"));

            animation.Step(0.1f); // a state's first update samples its start (measured)
            for (int i = 0; i < 20; i++) animation.Step(0.1f); // two loops
            Assert.Equal(new[] { "OnHit(3)", "OnHit(3)" }, receiver.Calls);
            Near.Equal(0f, Mathf.Repeat(go.transform.localPosition.x + 0.5f, 1f) - 0.5f, 1e-3f);

            animation.Step(0.35f); // 2.35: the third loop's event
            Assert.Equal(3, receiver.Calls.Count);
            Near.Equal(0.35f, go.transform.localPosition.x, 1e-3f);

            var state = animation["loop"];
            Assert.NotNull(state);
            Near.Equal(2.35f, state.time, 1e-3f);
            animation.Stop();
            Assert.False(animation.isPlaying);
            Assert.Equal(0f, state.time);
            Object.DestroyImmediate(go);
        }

        [Fact]
        public void OnceClipsStopAtTheirEndAndCrossFadeBlends()
        {
            var go = new GameObject("Legacy");
            var animation = go.AddComponent<Animation>();
            var a = new AnimationClip { legacy = true, wrapMode = WrapMode.Once, name = "a" };
            a.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0f, 1f, 10f));
            var b = new AnimationClip { legacy = true, wrapMode = WrapMode.Loop, name = "b" };
            b.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0f, 1f, 20f));
            animation.AddClip(a, "a");
            animation.AddClip(b, "b");

            animation.Play("a");
            animation.Step(0f); // a state's first update samples its start (measured)
            animation.Step(0.5f);
            Near.Equal(10f, go.transform.localPosition.x);
            animation.Step(0.6f);
            Assert.False(animation.IsPlaying("a"));

            animation.Play("a");
            animation.Step(0f);
            animation.Step(0.1f);
            animation.CrossFade("b", 0.5f);
            animation.Step(0.25f); // halfway: a fades to 0.5, b in to 0.5
            Near.Equal(15f, go.transform.localPosition.x, 1e-3f);
            animation.Step(0.3f);
            Assert.False(animation.IsPlaying("a"));
            Assert.True(animation.IsPlaying("b"));
            Near.Equal(20f, go.transform.localPosition.x, 1e-3f);

            var queued = animation.PlayQueued("a", QueueMode.PlayNow);
            Assert.NotNull(queued);
            Assert.True(animation.IsPlaying("a"));
            Object.DestroyImmediate(go);
        }

        [Fact]
        public void RealControllerPlaysItsClipOnARectTransform()
        {
            var file = Load(AnimationFixtures.PopupButton);
            var controller = (RuntimeAnimatorController)file.Objects[9100000];
            var clip = (AnimationClip)file.Objects[7400000];
            Assert.Equal("PopupButton", controller.name);
            Assert.Equal(new[] { clip }, controller.animationClips);
            Near.Equal(0.3f, clip.length);

            var root = new GameObject("Button", typeof(RectTransform));
            var text = new GameObject("ContinueText", typeof(RectTransform));
            text.transform.SetParent(root.transform, false);
            var rect = (RectTransform)text.transform;
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            Assert.Equal(1, animator.layerCount);
            Assert.Equal("Base Layer", animator.GetLayerName(0));

            var info = animator.GetCurrentAnimatorStateInfo(0);
            Assert.Equal(Animator.StringToHash("Base Layer.PopupShow"), info.fullPathHash);
            Assert.True(info.IsName("PopupShow"));
            Assert.True(info.IsName("Base Layer.PopupShow"));

            animator.Update(0f);
            Near.Equal(-69.3f, rect.anchoredPosition.y, 1e-3f);
            animator.Update(0.25f);
            Near.Equal(30f, rect.anchoredPosition.y, 1e-3f);
            Near.Equal(new Vector3(1.0884f, 1.0884f, 1.0884f), rect.localScale, 1e-4f);
            info = animator.GetCurrentAnimatorStateInfo(0);
            Near.Equal(0.25f / 0.3f, info.normalizedTime, 1e-4f);
            Near.Equal(0.3f, info.length, 1e-5f);
            Assert.False(info.loop);
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            Assert.Single(clips);
            Assert.Same(clip, clips[0].clip);
            Object.DestroyImmediate(root);
        }

        [Fact]
        public void RebindPutsAnimatedValuesBackToTheirDefaults()
        {
            // Measured in the Editor's Play mode: right after Rebind the animated properties are
            // back to the values they had when the animator first bound (a pooled view's reset).
            var file = Load(AnimationFixtures.PopupButton);
            var controller = (RuntimeAnimatorController)file.Objects[9100000];
            var root = new GameObject("Button", typeof(RectTransform));
            var text = new GameObject("ContinueText", typeof(RectTransform));
            text.transform.SetParent(root.transform, false);
            var rect = (RectTransform)text.transform;
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.Update(0f);
            animator.Update(0.25f);
            Near.Equal(new Vector3(1.0884f, 1.0884f, 1.0884f), rect.localScale, 1e-4f);
            animator.Rebind();
            Near.Equal(Vector3.one, rect.localScale, 1e-6f);
            Object.DestroyImmediate(root);
        }

        [Fact]
        public void EmptyStateLeavesAtExitTimeWithAFixedDuration()
        {
            var file = Load(AnimationFixtures.PanelController);
            var controller = (RuntimeAnimatorController)file.Objects[9100000];
            var go = new GameObject("Exchange");
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            for (int i = 0; i < 7; i++) animator.Update(0.1f); // 0.7 s of a 1 s empty state
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("New State"));
            Assert.False(animator.IsInTransition(0));

            animator.Update(0.1f); // crosses 0.75: the transition starts there, 0.05 s in (measured)
            Assert.True(animator.IsInTransition(0));
            Assert.True(animator.GetNextAnimatorStateInfo(0).IsName("PanelShowClip"));
            var transition = animator.GetAnimatorTransitionInfo(0);
            Assert.Equal(DurationUnit.Fixed, transition.durationUnit);
            Near.Equal(0.25f, transition.duration);
            Near.Equal(0.2f, transition.normalizedTime, 1e-4f);

            animator.Update(0.1f);
            Assert.True(animator.IsInTransition(0));
            animator.Update(0.1f); // 0.25 s
            Assert.False(animator.IsInTransition(0));
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("PanelShowClip"));
            Object.DestroyImmediate(go);
        }

        [Fact]
        public void TransitionsFollowBoolsTriggersAndExitTimes()
        {
            var file = Load(AnimationFixtures.Locomotion);
            var controller = (RuntimeAnimatorController)file.Objects[9100000];
            var root = Hierarchy("Character", "Body");
            var body = root.transform.Find("Body");
            var receiver = root.AddComponent<AnimationEventReceiver>();
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            Assert.Equal(3, animator.parameterCount);
            Assert.Equal(0.5f, animator.GetFloat("Speed"));
            Assert.True(animator.HasState(0, Animator.StringToHash("Walk")));
            Assert.False(animator.HasState(0, Animator.StringToHash("Swim")));

            animator.Update(0.1f);
            var idle = animator.GetCurrentAnimatorStateInfo(0);
            Assert.True(idle.IsName("Idle"));
            Assert.True(idle.IsTag("Grounded"));
            Assert.True(idle.loop);

            // A bool condition without exit time switches at once (duration 0).
            animator.SetBool("Run", true);
            animator.Update(0.1f);
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"));
            Assert.False(animator.IsInTransition(0));

            // Walk plays at the Speed parameter (0.5): its 0.25 event comes after 0.5 s.
            animator.SetFloat(Animator.StringToHash("Speed"), 0.5f);
            for (int i = 0; i < 4; i++) animator.Update(0.1f);
            Assert.Empty(receiver.Calls);
            animator.Update(0.11f);
            Assert.Equal(new[] { "OnStep" }, receiver.Calls);
            var walk = animator.GetCurrentAnimatorStateInfo(0);
            Near.Equal(0.5f, walk.speedMultiplier);
            Near.Equal(0.255f, walk.normalizedTime, 1e-3f);
            Near.Equal(0.255f, body.localPosition.x, 1e-3f);

            // Clearing Run waits for the exit time (0.5), then blends back over 0.2 s.
            animator.SetBool("Run", false);
            animator.Update(0.4f); // 0.455
            Assert.False(animator.IsInTransition(0));
            animator.Update(0.2f); // 0.555: crossed 0.11 s ago (at half speed): the transition is that far in (measured)
            Assert.True(animator.IsInTransition(0));
            Near.Equal(0.55f, animator.GetAnimatorTransitionInfo(0).normalizedTime, 1e-3f);
            animator.Update(0.1f);
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"));

            // The trigger fires the Any State transition once and is consumed.
            animator.SetTrigger("Jump");
            Assert.True(animator.GetBool("Jump"));
            animator.Update(0.1f);
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("Jump"));
            Assert.False(animator.GetBool("Jump"));
            Near.Equal(2f, body.localPosition.y, 1e-4f);
            animator.Update(0.3f);
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("Jump"));
            animator.Update(0.3f); // past its end (exit time 1)
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"));
            animator.Update(0.1f);
            Near.Equal(0f, body.localPosition.y, 1e-4f);
            Object.DestroyImmediate(root);
        }

        [Fact]
        public void PlayAndCrossFadeTakeEffectOnTheNextUpdate()
        {
            var file = Load(AnimationFixtures.Locomotion);
            var controller = (RuntimeAnimatorController)file.Objects[9100000];
            var root = Hierarchy("Character", "Body");
            var body = root.transform.Find("Body");
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.Update(0f);

            animator.Play("Walk", 0, 0.5f);
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"));
            animator.Update(0f);
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("Walk"));
            Near.Equal(0.5f, body.localPosition.x, 1e-4f);

            animator.CrossFadeInFixedTime("Jump", 0.2f);
            animator.Update(0.1f);
            Assert.True(animator.IsInTransition(0));
            Assert.True(animator.GetNextAnimatorStateInfo(0).IsName("Jump"));
            animator.Update(0.15f);
            Assert.False(animator.IsInTransition(0));
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("Jump"));
            Object.DestroyImmediate(root);
        }

        [Fact]
        public void OverrideControllersReplaceClips()
        {
            var file = Load(AnimationFixtures.Locomotion);
            var controller = (RuntimeAnimatorController)file.Objects[9100000];
            var idle = (AnimationClip)file.Objects[7400001];
            var replacement = new AnimationClip { name = "Lifted" };
            replacement.legacy = true;
            replacement.SetCurve("Body", typeof(Transform), "localPosition.z", AnimationCurve.Constant(0f, 1f, 3f));
            replacement.legacy = false;

            var overrides = new AnimatorOverrideController(controller);
            Assert.Equal(3, overrides.overridesCount);
            overrides["IdleClip"] = replacement;
            Assert.Same(replacement, overrides[idle]);
            var list = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrides.GetOverrides(list);
            Assert.Equal(3, list.Count);

            var root = Hierarchy("Character", "Body");
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = overrides;
            animator.Update(0.1f);
            Near.Equal(3f, root.transform.Find("Body").localPosition.z, 1e-4f);
            Assert.Contains(replacement, overrides.animationClips);

            var copy = Object.Instantiate(overrides);
            Assert.Equal("(Clone)", copy.name.Substring(copy.name.Length - 7));
            Assert.Same(replacement, copy[idle]);
            Assert.Same(controller, copy.runtimeAnimatorController);
            var controllerCopy = Object.Instantiate(controller);
            Assert.Equal(3, controllerCopy.animationClips.Length);
            Object.DestroyImmediate(root);
        }

        [Fact]
        public void AnimatorEventsFireOncePerLoop()
        {
            var file = Load(AnimationFixtures.CircleIdle);
            var clip = (AnimationClip)file.Objects[7400000];
            var controllerFile = Load(AnimationFixtures.PopupButton);
            var controller = (RuntimeAnimatorController)controllerFile.Objects[9100000];
            var overrides = new AnimatorOverrideController(controller);
            overrides[overrides.animationClips[0]] = clip; // the looping 1.5 s clip with an event at 0.5

            var root = Hierarchy("Badge", "Circle 1");
            root.transform.Find("Circle 1").gameObject.AddComponent<SpriteRenderer>();
            var receiver = root.AddComponent<AnimationEventReceiver>();
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = overrides;
            for (int i = 0; i < 30; i++) animator.Update(0.1f); // 3 s: two loops
            Assert.Equal(2, receiver.Calls.Count);
            Assert.Equal("OnPulse(half,2.5,7,0.5)", receiver.Calls[0]);
            animator.fireEvents = false;
            for (int i = 0; i < 15; i++) animator.Update(0.1f);
            Assert.Equal(2, receiver.Calls.Count);
            Object.DestroyImmediate(root);
        }
    
        [Fact]
        public void MissingStatesParametersAndReceiversAreReported()
        {
            Expect("Parameter 'Fly' does not exist.");
            Expect("Animator.GotoState: State could not be found");
            Expect("AnimationEvent 'OnStep' on animation 'WalkClip' has no receiver!");
            Expect("The AnimationState could not be played");
            var file = Load(AnimationFixtures.Locomotion);
            var root = Hierarchy("Character", "Body");
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = (RuntimeAnimatorController)file.Objects[9100000];
            animator.SetBool("Fly", true);
            animator.Play("Swim");
            animator.Play("Walk", 0, 0.2f);
            animator.Update(0.1f);
            var animation = root.AddComponent<Animation>();
            Assert.False(animation.Play("nothing"));
            Assert.Equal(4, m_Logs.Count);
            Object.DestroyImmediate(root);
        }

        [Fact]
        public void LayersBlendTreesAndAdditiveLayersCombine()
        {
            var file = Load(AnimationFixtures.Layers);
            var root = Hierarchy("Character", "Body");
            var body = root.transform.Find("Body");
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = (RuntimeAnimatorController)file.Objects[9100000];
            Assert.Equal(3, animator.layerCount);
            Assert.Equal(2, animator.GetLayerIndex("Add"));
            Assert.Equal(1f, animator.GetLayerWeight(0));
            Assert.Equal(0.5f, animator.GetLayerWeight(1));

            animator.SetFloat("Blend", 0.25f);
            animator.Update(0.1f);
            // Base: 1D blend of x 0 and x 10; Upper rests without writing; Add adds z 2.
            Near.Equal(new Vector3(2.5f, 0f, 2f), body.localPosition, 1e-4f);
            var info = animator.GetCurrentAnimatorStateInfo(0);
            Near.Equal(1.25f, info.length, 1e-4f);
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            Assert.Equal(2, clips.Length);
            Near.Equal(0.75f, clips[0].weight);
            Near.Equal(0.25f, clips[1].weight);

            // Wave writes y 4 (and its defaults) over the base at half weight.
            animator.SetBool("Wave", true);
            animator.Update(0.1f);
            Assert.True(animator.GetCurrentAnimatorStateInfo(1).IsName("Wave"));
            Near.Equal(new Vector3(1.25f, 2f, 2f), body.localPosition, 1e-4f);

            animator.SetLayerWeight(1, 1f);
            animator.Update(0.1f);
            Near.Equal(new Vector3(0f, 4f, 2f), body.localPosition, 1e-4f);
            Object.DestroyImmediate(root);
        }

        [Fact]
        public void SubStateMachinesEnterAndExitWithBehaviours()
        {
            StateRecorder.Calls.Clear();
            var file = Load(AnimationFixtures.Machines);
            var recorder = ScriptableObject.CreateInstance<StateRecorder>();
            ((ControllerStateAsset)file.Objects[1102000002]).Behaviours.Add(recorder);
            var root = Hierarchy("Character", "Body");
            var body = root.transform.Find("Body");
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = (RuntimeAnimatorController)file.Objects[9100000];
            var instance = animator.GetBehaviour<StateRecorder>();
            Assert.NotNull(instance);
            Assert.NotSame(recorder, instance);
            Assert.Single(animator.GetBehaviours(Animator.StringToHash("Base Layer.Air.Rise"), 0));

            animator.Update(0.1f);
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("Ground"));
            animator.SetTrigger("Jump");
            animator.Update(0.1f);
            var rise = animator.GetCurrentAnimatorStateInfo(0);
            Assert.Equal(Animator.StringToHash("Base Layer.Air.Rise"), rise.fullPathHash);
            Assert.Equal(Animator.StringToHash("Rise"), rise.shortNameHash);
            Near.Equal(1f, body.localPosition.y);
            animator.Update(0.3f);
            animator.Update(0.3f); // Rise ends: Fall
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Air.Fall"));
            Near.Equal(2f, body.localPosition.y);
            animator.Update(0.3f);
            animator.Update(0.3f); // Fall exits Air: the parent's transition leads to Ground
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("Ground"));
            Near.Equal(0f, body.localPosition.y);
            Assert.Equal(new[] { "enter Rise 0", "update Rise", "exit Rise" }, StateRecorder.Calls);
            Object.DestroyImmediate(root);
        }

        [Fact]
        public void StartingSkipsTheEntryButASubMachineTakesIt()
        {
            // Measured on the 6000.0 Editor: a layer starts in its top machine's default state
            // even with an unconditional entry transition; a sub-machine entered later takes its
            // own entry transition over its default state.
            var yaml = AnimationFixtures.Machines;
            int top = yaml.IndexOf("  m_EntryTransitions: []");
            yaml = yaml.Remove(top, "  m_EntryTransitions: []".Length).Insert(top, "  m_EntryTransitions:\n  - {fileID: 1109000002}");
            int sub = yaml.IndexOf("  m_EntryTransitions: []");
            yaml = yaml.Remove(sub, "  m_EntryTransitions: []".Length).Insert(sub, "  m_EntryTransitions:\n  - {fileID: 1109000003}");
            yaml = yaml.Replace("--- !u!74 &7400001", """
--- !u!1109 &1109000002
AnimatorTransition:
  m_Name: 
  m_Conditions: []
  m_DstStateMachine: {fileID: 0}
  m_DstState: {fileID: 1102000002}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 0
  serializedVersion: 1
--- !u!1109 &1109000003
AnimatorTransition:
  m_Name: 
  m_Conditions: []
  m_DstStateMachine: {fileID: 0}
  m_DstState: {fileID: 1102000003}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 0
  serializedVersion: 1
--- !u!74 &7400001
""");
            var file = Load(yaml);
            var root = Hierarchy("Character", "Body");
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = (RuntimeAnimatorController)file.Objects[9100000];
            animator.Update(0.1f);
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("Ground"));
            animator.SetTrigger("Jump");
            animator.Update(0.1f);
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Air.Fall"));
            Object.DestroyImmediate(root);
        }

        [Fact]
        public void AnEventAtTheEndOfAStateLeftAtExitTimeFires()
        {
            // Measured on the 6000.0 Editor: an event at a clip's very end fires as an exit
            // time of 1 with no duration leaves the state for the next.
            var yaml = AnimationFixtures.Machines;
            int rise = yaml.IndexOf("m_Name: RiseClip");
            int events = yaml.IndexOf("  m_Events: []", rise);
            yaml = yaml.Remove(events, "  m_Events: []".Length).Insert(events, @"  m_Events:
  - time: 0.5
    functionName: OnStep
    data: 
    objectReferenceParameter: {fileID: 0}
    floatParameter: 0
    intParameter: 0
    messageOptions: 0");
            var file = Load(yaml);
            var root = Hierarchy("Character", "Body");
            var receiver = root.AddComponent<AnimationEventReceiver>();
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = (RuntimeAnimatorController)file.Objects[9100000];
            animator.Update(0.1f);
            animator.SetTrigger("Jump");
            animator.Update(0.1f); // Rise from 0
            for (int i = 0; i < 6; i++) animator.Update(0.1f); // past Rise's end (0.5 s): Fall
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Air.Fall"));
            Assert.Equal(new[] { "OnStep" }, receiver.Calls);
            Object.DestroyImmediate(root);
        }

        [Fact]
        public void RootMotionLeavesTheRootToScriptsAndAppliesClipDeltas()
        {
            // The 6000.0 Editor's values for the same setup (generic, no avatar).
            var file = Load(AnimationFixtures.RootMotion);
            var parent = new GameObject("Parent");
            parent.transform.position = new Vector3(0, 10, 0);
            parent.transform.rotation = Quaternion.Euler(0, 0, 90);
            parent.transform.localScale = new Vector3(2, 2, 2);
            var root = Hierarchy("Obj", "Child");
            root.transform.SetParent(parent.transform, false);
            root.transform.localPosition = new Vector3(5, 0, 0);
            root.transform.localRotation = Quaternion.Euler(0, 45, 0);
            var child = root.transform.Find("Child");
            child.localPosition = new Vector3(0, 7, 0);
            var animator = root.AddComponent<Animator>();
            animator.applyRootMotion = true;
            animator.runtimeAnimatorController = (RuntimeAnimatorController)file.Objects[9100000];
            animator.Update(0.1f);
            // An empty state leaves what code does to the root.
            root.transform.Rotate(Vector3.up, 30f);
            root.transform.localPosition += new Vector3(0, 0, 1);
            animator.Update(0.1f);
            Near.Equal(new Vector3(5, 0, 1), root.transform.localPosition, 1e-4f);
            Near.Equal(75f, root.transform.localEulerAngles.y, 1e-3f);
            animator.Play("Move", 0, 0f);
            animator.Update(0f);
            Near.Equal(new Vector3(5, 0, 1), root.transform.localPosition, 1e-4f);
            animator.Update(0.25f);
            Near.Equal(new Vector3(5.129f, 0, 0.517f), root.transform.localPosition, 2e-3f);
            Near.Equal(97.5f, root.transform.localEulerAngles.y, 1e-2f);
            Near.Equal(new Vector3(0, 0.259f, -0.966f), animator.deltaPosition, 2e-3f);
            Near.Equal(22.5f, animator.deltaRotation.eulerAngles.y, 1e-2f);
            Near.Equal(0.75f, child.localPosition.y, 1e-3f);
            animator.Update(0.25f);
            Near.Equal(new Vector3(5.259f, 0, 0.034f), root.transform.localPosition, 2e-3f);
            Near.Equal(120f, root.transform.localEulerAngles.y, 1e-2f);
            Object.DestroyImmediate(parent);
        }

        [Fact]
        public void GameCodeRunByAWriteCanSampleAnimationAgain()
        {
            var inner = new AnimationClip { legacy = true };
            inner.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0f, 1f, 7f));
            var outer = new AnimationClip { legacy = true };
            outer.SetCurve("Child", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 1f, 1f));
            outer.SetCurve("Other", typeof(Transform), "localPosition.y", AnimationCurve.Constant(0f, 1f, 3f));
            var root = Hierarchy("Root", "Child", "Other");
            var child = root.transform.Find("Child").gameObject;
            child.SetActive(false);
            child.AddComponent<SampleOnEnable>();
            SampleOnEnable.Clip = inner;
            try
            {
                outer.SampleAnimation(root, 0.5f);
            }
            finally
            {
                SampleOnEnable.Clip = null;
            }
            Assert.True(child.activeSelf);
            Near.Equal(7f, root.transform.localPosition.x);
            Near.Equal(3f, root.transform.Find("Other").localPosition.y);
            Object.DestroyImmediate(root);
        }

        [Fact]
        public void ClipsCanDriveAnimatorParameters()
        {
            var file = Load(AnimationFixtures.Locomotion);
            var controller = (RuntimeAnimatorController)file.Objects[9100000];
            var idle = (AnimationClip)file.Objects[7400001];
            // Idle also drives the Speed parameter (a curve on the Animator, class 95).
            idle.m_Curves.Add(new ClipCurve
            {
                Binding = new CurveBinding { Path = "", Attribute = "Speed", ClassId = 95 },
                Curve = AnimationCurve.Constant(0f, 1f, 0.8f),
            });
            idle.m_Version++;
            var root = Hierarchy("Character", "Body");
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            Assert.True(animator.IsParameterControlledByCurve("Speed"));
            Assert.False(animator.IsParameterControlledByCurve("Run"));
            animator.Update(0.1f);
            Near.Equal(0.8f, animator.GetFloat("Speed"));
            Object.DestroyImmediate(root);
        }
}
}
