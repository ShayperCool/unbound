using System.Linq;
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

        /// <summary>A clip's length follows the curves it has. AnimationClip.SetCurve on a component of
        /// a Transform vector property (position, rotation, Euler angles, scale) keys every component
        /// of the vector at the union of their key times, legacy and non-legacy clips alike (a new
        /// component is 0, or 1 for scale and rotation w); removing one component that way logs the
        /// Editor's error and keeps it; the whole property's name removes the vector; SetEditorCurve
        /// sets and removes single components. Matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/editorui `ClipLength.*` (recorded with the macOS Editor) and
        /// tests/projects/defaultstate `Small/animation/*` (1, 0.5, 0.5, 3, 3).</summary>
        [Fact]
        public void ClipLengthFollowsTheCurvesTheClipHas()
        {
            string version = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            Expect("Can't remove individual");
            var created = new List<Object>();
            AnimationClip Clip(bool legacy) { var c = new AnimationClip { legacy = legacy }; created.Add(c); return c; }
            var light = new System.Func<string, UnityEditor.EditorCurveBinding>(p => UnityEditor.EditorCurveBinding.FloatCurve("", typeof(Light), p));
            UnityEditor.EditorCurveBinding Position(string axis) => UnityEditor.EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition." + axis);
            var full = AnimationCurve.Linear(0f, 0f, 2f, 1f);
            try
            {
                // editorui: an Editor edit of a clip made with SetCurve.
                var edited = Clip(false);
                edited.SetCurve("Child", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 2f));
                UnityEditor.AnimationUtility.SetEditorCurve(edited, light("m_Intensity"), AnimationCurve.Linear(0f, 1f, 2f, 3f));
                Assert.Equal(2f, edited.length);
                UnityEditor.AnimationUtility.SetEditorCurve(edited, light("m_Intensity"), null);
                Assert.Equal(1f, edited.length);
                Assert.Equal(1f, UnityEditor.AnimationUtility.GetAnimationClipSettings(edited).stopTime);

                foreach (bool legacy in new[] { false, true })
                {
                    // Other curves count while the clip has them.
                    var two = Clip(legacy);
                    two.SetCurve("", typeof(Light), "m_Intensity", AnimationCurve.Linear(0f, 0f, 1f, 1f));
                    two.SetCurve("", typeof(Light), "m_Range", full);
                    Assert.Equal(2f, two.length);
                    two.SetCurve("", typeof(Light), "m_Range", null);
                    Assert.Equal(1f, two.length);

                    // A vector's components: all of them, at the union of the key times.
                    foreach (var (property, count) in new[] { ("localPosition", 3), ("localRotation", 4), ("localEulerAngles", 3), ("localScale", 3) })
                    {
                        var vector = Clip(legacy);
                        vector.SetCurve("", typeof(Transform), property + ".x", full);
                        Assert.Equal(count, UnityEditor.AnimationUtility.GetCurveBindings(vector).Length);
                        vector.SetCurve("", typeof(Transform), property + ".x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
                        Assert.Equal(2f, vector.length);
                        int logs = m_Logs.Count;
                        vector.SetCurve("", typeof(Transform), property + ".x", null);
                        Assert.Equal(count, UnityEditor.AnimationUtility.GetCurveBindings(vector).Length);
                        Assert.Equal(2f, vector.length);
                        Assert.Single(m_Logs.GetRange(logs, m_Logs.Count - logs));
                        Assert.StartsWith("Error: Can't remove individual", m_Logs[logs]);
                        // The whole vector goes by its serialized name, and by the script name but
                        // for the Euler angles, which log the error and stay.
                        logs = m_Logs.Count;
                        vector.SetCurve("", typeof(Transform), property, null);
                        Assert.Equal(property == "localEulerAngles" ? count : 0, UnityEditor.AnimationUtility.GetCurveBindings(vector).Length);
                        if (property == "localEulerAngles")
                            Assert.Equal("Error: Can't remove individual rotation animation curve localEulerAngles you must remove the entire animation curve with m_LocalEuler.", Assert.Single(m_Logs.GetRange(logs, m_Logs.Count - logs)));
                        vector.SetCurve("", typeof(Transform), property + ".x", full);
                        string whole = property == "localEulerAngles" ? "m_LocalEuler" : "m_" + char.ToUpperInvariant(property[0]) + property.Substring(1);
                        vector.SetCurve("", typeof(Transform), whole, null);
                        Assert.Empty(UnityEditor.AnimationUtility.GetCurveBindings(vector));
                        Assert.Equal(1f, vector.length);
                    }
                    var keys = Clip(legacy);
                    keys.SetCurve("", typeof(Transform), "localPosition.x", full);
                    keys.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0.5f, 0f, 1f, 1f));
                    float[] Times(string axis) => UnityEditor.AnimationUtility.GetEditorCurve(keys, Position(axis)).keys.Select(k => k.time).ToArray();
                    Assert.Equal(new[] { 0f, 0.5f, 1f, 2f }, Times("x"));
                    Assert.Equal(new[] { 0f, 0.5f, 1f, 2f }, Times("y"));
                    keys.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(-2f, 0f, -1f, 1f));
                    Assert.Equal(new[] { -2f, -1f, 0f, 0.5f, 1f, 2f }, Times("z"));
                    Assert.Equal(2f, keys.length);
                    keys.SetCurve("", typeof(Transform), "localPosition", null);
                    Assert.Empty(UnityEditor.AnimationUtility.GetCurveBindings(keys));
                    Assert.Equal(1f, keys.length);
                    keys.SetCurve("", typeof(Transform), "localPosition.x", full);
                    keys.SetCurve("", typeof(Transform), "m_LocalPosition", null);
                    Assert.Equal(1f, keys.length);

                    // SetEditorCurve sets and removes single components.
                    var editor = Clip(legacy);
                    UnityEditor.AnimationUtility.SetEditorCurve(editor, Position("x"), full);
                    Assert.Equal(new[] { 2f, 1f }, new[] { editor.length, UnityEditor.AnimationUtility.GetCurveBindings(editor).Length });
                    UnityEditor.AnimationUtility.SetEditorCurve(editor, Position("x"), null);
                    Assert.Equal(1f, editor.length);
                    var mixed = Clip(legacy);
                    mixed.SetCurve("", typeof(Transform), "localPosition.x", full);
                    UnityEditor.AnimationUtility.SetEditorCurve(mixed, Position("x"), AnimationCurve.Linear(0f, 0f, 1f, 1f));
                    Assert.Equal(2f, mixed.length);
                    foreach (var axis in new[] { "x", "y", "z" }) UnityEditor.AnimationUtility.SetEditorCurve(mixed, Position(axis), null);
                    Assert.Equal(1f, mixed.length);
                }
                var negative = Clip(false);
                negative.SetCurve("", typeof(Light), "m_Intensity", AnimationCurve.Linear(-2f, 0f, -1f, 1f));
                Assert.Equal(-1f, negative.length);

                // defaultstate: a legacy clip's position.
                var position = Clip(true);
                position.ClearCurves();
                Assert.Equal(1f, position.length);
                position.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 0.5f, 1f));
                Assert.Equal(0.5f, position.length);
                position.SetCurve("", typeof(Transform), "localPosition.x", null);
                Assert.Equal(0.5f, position.length);
                position.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(2f, 0f, 3f, 1f));
                Assert.Equal(3f, position.length);
                position.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(-2f, 0f, -1f, 1f));
                Assert.Equal(3f, position.length);
            }
            finally
            {
                foreach (var o in created) Object.DestroyImmediate(o);
                ProjectInfo.UnityVersion = version;
            }
        }

        /// <summary>The keys SetCurve adds to a vector's other components leave each curve as it
        /// was: on its Hermite segment (value and slope) and on a stepped segment (still held).
        /// A player clip's compiled component and a weighted curve keep their keys (Unbound's
        /// console warns).</summary>
        [Fact]
        public void VectorKeysAddedBySetCurveKeepEachCurve()
        {
            var smooth = new AnimationCurve(new Keyframe(0f, 0f, 0f, 3f), new Keyframe(2f, 1f, -1f, 0f));
            var stepped = new AnimationCurve(new Keyframe(0f, 0f, 0f, float.PositiveInfinity), new Keyframe(2f, 1f));
            var steppedLate = new AnimationCurve(new Keyframe(0f, 0f, 0f, float.NegativeInfinity), new Keyframe(2f, 1f));
            var steppedIn = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(2f, 1f, float.NegativeInfinity, 0f));
            var steppedBoth = new AnimationCurve(new Keyframe(0f, 0f, 0f, float.NegativeInfinity), new Keyframe(2f, 1f, float.PositiveInfinity, 0f));
            foreach (var original in new[] { smooth, stepped, steppedLate, steppedIn, steppedBoth })
            {
                var clip = new AnimationClip { legacy = true };
                clip.SetCurve("", typeof(Transform), "localPosition.x", original);
                clip.SetCurve("", typeof(Transform), "localPosition.y", new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1.25f, 2f)));
                var x = UnityEditor.AnimationUtility.GetEditorCurve(clip, UnityEditor.EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x"));
                Assert.Equal(new[] { 0f, 0.5f, 1.25f, 2f }, x.keys.Select(k => k.time).ToArray());
                for (float t = 0f; t <= 2f; t += 0.125f) Assert.Equal(original.Evaluate(t), x.Evaluate(t), 5);
                Object.DestroyImmediate(clip);
            }
            // A copy made before keeps its own keys (defaultstate Small/animation/clipCopies: an
            // Animation's clip added under another name keeps 2 after the original's x reaches 3).
            var source = new AnimationClip { legacy = true };
            source.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 2f, 1f));
            var copy = Object.Instantiate(source);
            source.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 3f, 1f));
            Assert.Equal(3f, source.length);
            Assert.Equal(2f, copy.length);
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(copy);
            var player = new AnimationClip { legacy = true, name = "player" };
            player.m_Curves.Add(new ClipCurve { Binding = CurveBinding.ForType("", "m_LocalPosition.x", typeof(Transform)) });
            player.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            Assert.Equal(2, player.m_Curves.Count);
            Assert.Null(player.m_Curves[0].Curve);
            var weighted = new AnimationClip { legacy = true, name = "weighted" };
            UnityEditor.AnimationUtility.SetEditorCurve(weighted, UnityEditor.EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x"),
                new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f, 0.5f, 0.5f) { weightedMode = WeightedMode.Both }, new Keyframe(1f, 1f, 1f, 1f, 0.5f, 0.5f) { weightedMode = WeightedMode.Both }));
            weighted.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0f, 0f, 2f, 1f));
            Assert.Equal(2, UnityEditor.AnimationUtility.GetEditorCurve(weighted, UnityEditor.EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x")).length);
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(weighted);
        }

        /// <summary>Where the keys SetCurve unites go past a component's own keys, its end tangents
        /// on that side become flat and the added keys hold its end values; inside, they follow its
        /// Hermite segment; a time within 2e-6 s of a key is that key (tests/projects/editorui
        /// SetCurveKeys.*).</summary>
        [Fact]
        public void VectorKeysAddedBySetCurveFlattenTheEndsTheyGoPast()
        {
            var clip = new AnimationClip();
            clip.SetCurve("", typeof(Transform), "localPosition.y", new AnimationCurve(
                new Keyframe(0.25f, 1f, 2f, 2f), new Keyframe(1f, 3f, -1f, 4f), new Keyframe(1.5f, 0f, 0.5f, -3f)));
            clip.SetCurve("", typeof(Transform), "localPosition.x", new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(2f, 5f, 3f, 3f)));
            Keyframe[] Keys(string axis) => UnityEditor.AnimationUtility.GetEditorCurve(clip,
                UnityEditor.EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition." + axis)).keys;
            var y = Keys("y");
            Assert.Equal(new[] { 0f, 0.25f, 1f, 1.5f, 2f }, y.Select(k => k.time).ToArray());
            Assert.Equal(new[] { 1f, 1f, 3f, 0f, 0f }, y.Select(k => k.value).ToArray());
            Assert.Equal(new[] { 0f, 0f, -1f, 0.5f, 0f }, y.Select(k => k.inTangent).ToArray());
            Assert.Equal(new[] { 0f, 2f, 4f, 0f, 0f }, y.Select(k => k.outTangent).ToArray());
            var x = Keys("x");
            Near.Equal(0.32421875f, x[1].value, 1e-6f);
            Near.Equal(1.578125f, x[1].inTangent, 1e-5f);
            Near.Equal(1.578125f, x[1].outTangent, 1e-5f);
            Assert.All(Keys("z"), k => Assert.Equal((0f, 0f, 0f), (k.value, k.inTangent, k.outTangent)));

            var close = new AnimationClip();
            close.SetCurve("", typeof(Transform), "localScale.y", AnimationCurve.Linear(0f, 1f, 2f, 3f));
            close.SetCurve("", typeof(Transform), "localScale.x", new AnimationCurve(new Keyframe(0.5f, 3f), new Keyframe(1.5f, 3f), new Keyframe(1.5000005f, 3f)));
            Assert.Equal(new[] { 0f, 0.5f, 1.5f, 2f }, UnityEditor.AnimationUtility.GetEditorCurve(close,
                UnityEditor.EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalScale.x")).keys.Select(k => k.time).ToArray());
            // Alone, a curve keeps keys however close (SetCurveKeys.*.alone).
            var alone = new AnimationClip();
            alone.SetCurve("", typeof(Transform), "localScale.x", new AnimationCurve(new Keyframe(0.5f, 3f), new Keyframe(1.5f, 3f), new Keyframe(1.5000005f, 3f)));
            Assert.Equal(3, UnityEditor.AnimationUtility.GetEditorCurve(alone,
                UnityEditor.EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalScale.x")).length);
            // A key dropped for its closeness shifts the keys after it onto the next times
            // (SetCurveKeys.*.tolerance): 1.9e-6 s apart is one time, 2.1e-6 s two.
            var shifted = new AnimationClip();
            shifted.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0f, 0f, 2f, 1f));
            shifted.SetCurve("", typeof(Transform), "localPosition.x", new AnimationCurve(new Keyframe(0.5f, 1f), new Keyframe(0.5000019f, 2f),
                new Keyframe(1f, 3f), new Keyframe(1.0000021f, 4f), new Keyframe(100f, 5f), new Keyframe(100.0000076f, 6f)));
            var sx = UnityEditor.AnimationUtility.GetEditorCurve(shifted, UnityEditor.EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x")).keys;
            Assert.Equal(new[] { 0f, 0.5f, 1f, 1.0000021f, 2f, 100f, 100.0000076f }, sx.Select(k => k.time).ToArray());
            Assert.Equal(new[] { 1f, 1f, 2f, 3f, 4f }, sx.Take(5).Select(k => k.value).ToArray());
            Near.Equal(4.00030375f, sx[5].value, 1e-5f);
            Assert.Equal(5f, sx[6].value);
            Object.DestroyImmediate(clip);
            Object.DestroyImmediate(close);
            Object.DestroyImmediate(alone);
            Object.DestroyImmediate(shifted);
        }

        /// <summary>Removing the Euler angles by m_LocalEuler leaves the clip settings' range until
        /// the clip changes again (tests/projects/editorui ClipLength.*.serializedRemoval,
        /// SetCurveKeys.*.eulerRemovalThenLight).</summary>
        [Fact]
        public void EulerRemovalKeepsTheSettingsRangeUntilTheNextChange()
        {
            string version = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            try { EulerRemovalKeepsTheSettingsRange(); }
            finally { ProjectInfo.UnityVersion = version; }
        }

        static void EulerRemovalKeepsTheSettingsRange()
        {
            var clip = new AnimationClip();
            clip.SetCurve("", typeof(Transform), "localEulerAngles.x", AnimationCurve.Linear(0f, 0f, 2f, 1f));
            clip.SetCurve("", typeof(Transform), "m_LocalEuler", null);
            Assert.Equal(1f, clip.length);
            Assert.Equal(2f, UnityEditor.AnimationUtility.GetAnimationClipSettings(clip).stopTime);
            // A copy keeps it too (Unbound's choice, unmeasured).
            var copy = Object.Instantiate(clip);
            Assert.Equal(2f, UnityEditor.AnimationUtility.GetAnimationClipSettings(copy).stopTime);
            Object.DestroyImmediate(copy);
            clip.SetCurve("", typeof(Light), "m_Intensity", AnimationCurve.Linear(0f, 0f, 0.5f, 1f));
            Assert.Equal(0.5f, UnityEditor.AnimationUtility.GetAnimationClipSettings(clip).stopTime);
            // Removing a position by m_LocalPosition updates it.
            var position = new AnimationClip();
            position.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 2f, 1f));
            position.SetCurve("", typeof(Transform), "m_LocalPosition", null);
            Assert.Equal(1f, UnityEditor.AnimationUtility.GetAnimationClipSettings(position).stopTime);
            Object.DestroyImmediate(clip);
            Object.DestroyImmediate(position);
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
            // The position's other components are curves of 0 (editorui
            // ClipLength.legacy.SetCurve.companions: (5, 0, 0) in the Editor).
            Near.Equal(new Vector3(5f, 0f, 0f), child.localPosition);
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
            // b, enabled now, fades in from the next update; a fades out at once, and its weight
            // alone counts in full (tests/projects/animpartial legacy.crossfade).
            animation.Step(0.25f);
            Near.Equal(0.5f, animation["a"].weight);
            Near.Equal(0f, animation["b"].weight);
            Near.Equal(10f, go.transform.localPosition.x, 1e-3f);
            animation.Step(0.25f); // a reaches 0 and stays enabled until it passes it; b is at 0.5
            Assert.True(animation.IsPlaying("a"));
            Near.Equal(20f, go.transform.localPosition.x, 1e-3f);
            animation.Step(0.25f); // a stops, sampled once more at a weight of 1e-4
            Assert.False(animation.IsPlaying("a"));
            Assert.True(animation.IsPlaying("b"));
            Near.Equal(20f, go.transform.localPosition.x, 2e-3f);

            var queued = animation.PlayQueued("a", QueueMode.PlayNow);
            Assert.NotNull(queued);
            Assert.True(animation.IsPlaying("a"));
            Object.DestroyImmediate(go);
        }

        static AnimationClip LegacyClip(string name, WrapMode wrap, params (string Property, AnimationCurve Curve)[] curves)
        {
            var clip = new AnimationClip { legacy = true, wrapMode = wrap, name = name };
            foreach (var (property, curve) in curves) clip.SetCurve("", typeof(Transform), property, curve);
            return clip;
        }

        static void EnableAt(Animation animation, string name, float weight, int layer = 0)
        {
            var state = animation[name];
            state.layer = layer;
            state.enabled = true;
            state.weight = weight;
        }

        /// <summary>Legacy states share each channel among those that animate it, higher layers
        /// first, and the shares are normalized: a lone state at half weight plays in full, a
        /// layer below gets what the layer above leaves (1 − 0.6) times its own weight, and what
        /// no state animates keeps the value a script gave it (tests/projects/animpartial
        /// legacy.half, legacy.layers.low).</summary>
        [Fact]
        public void LegacyChannelsAreSharedAmongTheirStatesAndNormalized()
        {
            var go = new GameObject("Legacy");
            var animation = go.AddComponent<Animation>();
            animation.AddClip(LegacyClip("p", WrapMode.Loop, ("localPosition.x", AnimationCurve.Constant(0f, 1f, 10f)),
                ("localScale.y", AnimationCurve.Constant(0f, 1f, 20f))), "p");
            animation.AddClip(LegacyClip("q", WrapMode.Loop, ("localPosition.x", AnimationCurve.Constant(0f, 1f, -10f))), "q");
            go.transform.localRotation = Quaternion.Euler(0f, 30f, 0f);
            EnableAt(animation, "p", 0.5f);
            animation.Step(0f);
            Near.Equal(10f, go.transform.localPosition.x, 1e-5f);
            Near.Equal(20f, go.transform.localScale.y, 1e-5f);
            EnableAt(animation, "q", 0.6f, layer: 1);
            animation.Step(0.1f);
            // x: q's 0.6 and p's 0.5 × (1 − 0.6), normalized; y only p animates.
            Near.Equal((0.6f * -10f + 0.2f * 10f) / 0.8f, go.transform.localPosition.x, 1e-4f);
            Near.Equal(20f, go.transform.localScale.y, 1e-5f);
            Near.Equal(30f, go.transform.localEulerAngles.y, 1e-3f);
            // At a weight of 1e-4 or less a state adds nothing.
            animation["q"].weight = 1e-4f;
            animation.Step(0.1f);
            Near.Equal(10f, go.transform.localPosition.x, 1e-5f);
            Object.DestroyImmediate(go);
        }

        /// <summary>A cross-faded-out state reaches weight 0 and stops an update later; in that
        /// update it is sampled once more, so what only it animates keeps its last value
        /// (tests/projects/animpartial legacy.crossfade).</summary>
        [Fact]
        public void LegacyStateStoppingInAnUpdateLeavesItsLastPose()
        {
            var go = new GameObject("Legacy");
            var animation = go.AddComponent<Animation>();
            // A clip with a curve of one position component writes the whole position (the
            // others at 0): what only p animates here is its scale.
            animation.AddClip(LegacyClip("p", WrapMode.Loop, ("localPosition.x", AnimationCurve.Constant(0f, 2f, 1f)),
                ("localScale.y", AnimationCurve.Linear(0f, 0f, 2f, 2f))), "p");
            animation.AddClip(LegacyClip("q", WrapMode.Loop, ("localPosition.x", AnimationCurve.Constant(0f, 2f, 5f))), "q");
            animation.Play("p");
            animation.Step(0f);
            animation.Step(0.5f);
            Near.Equal(0.5f, go.transform.localScale.y, 1e-5f);
            animation.CrossFade("q", 0.25f);
            animation.Step(0.25f); // p at weight 0, q not faded in yet: nothing is written
            Near.Equal(0f, animation["p"].weight);
            Assert.True(animation.IsPlaying("p"));
            Near.Equal(0.5f, go.transform.localScale.y, 1e-5f);
            Near.Equal(1f, go.transform.localPosition.x, 1e-5f);
            animation.Step(0.25f); // p passes 0 and stops at its time of 1 s
            Assert.False(animation.IsPlaying("p"));
            Near.Equal(0f, animation["p"].time);
            Near.Equal(1f, go.transform.localScale.y, 1e-4f);
            Near.Equal(5f, go.transform.localPosition.x, 1e-3f);
            Object.DestroyImmediate(go);
        }

        /// <summary>A legacy state fires the events its time passes in an update whatever its
        /// weight: at weight 0, and in the update its fade passes 0 and stops it; one at its start
        /// fires in the first update that moves its time (the Unity 6000.0.71f1 Editor,
        /// tests/projects/animpartial legacy.events.zero, legacy.events.fade,
        /// legacy.events.start.*).</summary>
        [Fact]
        public void LegacyStatesFireEventsAtWeightZeroAndInTheUpdateTheirFadeStopsThem()
        {
            var go = new GameObject("Legacy");
            var receiver = go.AddComponent<AnimationEventReceiver>();
            var animation = go.AddComponent<Animation>();
            var e = new AnimationClip { legacy = true, wrapMode = WrapMode.Loop, name = "e" };
            e.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            foreach (float t in new[] { 0.3125f, 0.4375f, 0.5625f })
                e.AddEvent(new AnimationEvent { time = t, functionName = "OnHit", floatParameter = t });
            animation.AddClip(e, "e");
            animation.AddClip(LegacyClip("q", WrapMode.Loop, ("localPosition.y", AnimationCurve.Constant(0f, 1f, 2f))), "q");
            animation.Play("e");
            animation.Step(0.125f); // its first update samples its start
            animation.Step(0.125f);
            animation.Step(0.125f); // 0.25
            animation.CrossFade("q", 0.25f);
            animation.Step(0.125f); // 0.375 at weight 0.5
            Assert.Equal(new[] { "OnHit(0.3125)" }, receiver.Calls);
            animation.Step(0.125f); // 0.5 at weight 0: still enabled (the fade has not passed 0)
            Near.Equal(0f, animation["e"].weight);
            Assert.True(animation.IsPlaying("e"));
            Assert.Equal(new[] { "OnHit(0.3125)", "OnHit(0.4375)" }, receiver.Calls);
            animation.Step(0.125f); // the fade passes 0 and stops it: 0.5 to 0.625 fires
            Assert.False(animation.IsPlaying("e"));
            Assert.Equal(new[] { "OnHit(0.3125)", "OnHit(0.4375)", "OnHit(0.5625)" }, receiver.Calls);
            animation.Step(0.125f);
            Assert.Equal(3, receiver.Calls.Count);

            // At weight 0 from the start of its play: its events still fire.
            receiver.Calls.Clear();
            animation.Play("e");
            animation["e"].weight = 0f;
            animation.Step(0.125f);
            animation.Step(0.25f);
            animation.Step(0.25f); // 0.5
            Assert.Equal(new[] { "OnHit(0.3125)", "OnHit(0.4375)" }, receiver.Calls);

            // An event at the start fires in the update after the start, which first moves the
            // time, not in the start's own update (legacy.events.start.*), cross-faded in too.
            var z = new AnimationClip { legacy = true, wrapMode = WrapMode.Loop, name = "z" };
            z.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            z.AddEvent(new AnimationEvent { time = 0f, functionName = "OnHit", floatParameter = 7f });
            animation.AddClip(z, "z");
            foreach (bool fade in new[] { false, true })
            {
                receiver.Calls.Clear();
                animation.Play("q");
                animation.Step(0.125f);
                if (fade) animation.CrossFade("z", 0.25f);
                else animation.Play("z");
                animation.Step(0.125f); // the start: time 0, no event
                Assert.Empty(receiver.Calls);
                animation.Step(0.125f);
                Assert.Equal(new[] { "OnHit(7)" }, receiver.Calls);
                animation.Step(0.125f);
                Assert.Single(receiver.Calls);
                animation.Stop();
            }
            // At speed 0 the time does not move and nothing fires; the start's event fires when it
            // first moves (legacy.events.start.speed0).
            receiver.Calls.Clear();
            animation["z"].speed = 0f;
            animation.Play("z");
            for (int i = 0; i < 3; i++) animation.Step(0.125f);
            Assert.Empty(receiver.Calls);
            animation["z"].speed = 1f;
            animation.Step(0.125f);
            Assert.Equal(new[] { "OnHit(7)" }, receiver.Calls);
            Object.DestroyImmediate(go);
        }

        /// <summary>A legacy additive state adds its difference from its clip's first keys, by its
        /// weight up to 1, to what the other states write, or to the current value where none
        /// does (which then accumulates), on a Transform's position (tests/projects/animpartial
        /// legacy.additive).</summary>
        [Fact]
        public void LegacyAdditiveStatesAddTheirDifferenceToTheCurrentValue()
        {
            var go = new GameObject("Legacy");
            var animation = go.AddComponent<Animation>();
            animation.AddClip(LegacyClip("base", WrapMode.Loop, ("localPosition.x", AnimationCurve.Constant(0f, 2f, 1f))), "base");
            animation.AddClip(LegacyClip("add", WrapMode.Loop, ("localPosition.x", AnimationCurve.Linear(0f, 3f, 2f, 5f)),
                ("localScale.y", AnimationCurve.Linear(0f, 3f, 2f, 5f))), "add");
            go.transform.localScale = new Vector3(1f, 7f, 1f);
            animation["add"].blendMode = AnimationBlendMode.Additive;
            animation.Play("base");
            EnableAt(animation, "add", 0.5f, layer: 1);
            animation.Step(0f);
            animation.Step(0.5f);
            // x: base 1 + 0.5 × (3.5 − 3); the scale's y: 7 + 0.25, then + 0.5 more.
            Near.Equal(1.25f, go.transform.localPosition.x, 1e-4f);
            Near.Equal(new Vector3(1f, 7.25f, 1f), go.transform.localScale, 1e-4f);
            animation.Step(0.5f);
            Near.Equal(1.5f, go.transform.localPosition.x, 1e-4f);
            Near.Equal(new Vector3(1f, 7.75f, 1f), go.transform.localScale, 1e-4f);
            animation["add"].weight = 3f; // counts as 1
            animation.Step(0.5f);
            Near.Equal(2.5f, go.transform.localPosition.x, 1e-4f);
            Near.Equal(new Vector3(1f, 9.25f, 1f), go.transform.localScale, 1e-4f);
            Object.DestroyImmediate(go);
        }

        /// <summary>Play during a once clip's fade out before its end sets the weight back to 1
        /// and the clip still stops at its end; a cross-faded once clip stops after fading out.</summary>
        [Fact]
        public void LegacyOnceClipPlayedDuringItsEndFadeStillStops()
        {
            var go = new GameObject("Legacy");
            var animation = go.AddComponent<Animation>();
            animation.AddClip(LegacyClip("s", WrapMode.Once, ("localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f))), "s");
            animation.CrossFade("s", 0.5f);
            animation.Step(0f);
            for (int i = 0; i < 7; i++) animation.Step(0.1f); // 0.7 s: in the last 0.5 s, fading out
            Assert.True(animation["s"].weight < 1f);
            animation.Play("s");
            Assert.Equal(1f, animation["s"].weight);
            for (int i = 0; i < 4; i++) animation.Step(0.1f); // past the end
            Assert.False(animation.IsPlaying("s"));
            Near.Equal(1f, go.transform.localPosition.x, 1e-3f);
            animation.CrossFade("s", 0.25f);
            animation.Step(0f);
            for (int i = 0; i < 15 && animation.IsPlaying("s"); i++) animation.Step(0.1f);
            Assert.False(animation.IsPlaying("s"));

            // A weight set during the fade out ends it too: the next update starts it again from
            // the time past its start (0.4 s at speed -2 from 1) and the step (0.1 s), to 0.
            animation.CrossFade("s", 0.5f);
            animation.Step(0f);
            for (int i = 0; i < 8; i++) animation.Step(0.1f); // 0.8 s
            animation["s"].weight = 1f;
            Assert.Equal(1f, animation["s"].weight);
            animation.Step(0.1f);
            Assert.True(animation["s"].weight <= 1e-5f);
            animation.Step(0.1f);
            Assert.False(animation.IsPlaying("s"));
            Object.DestroyImmediate(go);
        }

        [Fact]
        public void AnimatedBoolsAreTrueBeyondAThousandthOfZero()
        {
            foreach (var (value, expected) in new[] { (0f, false), (0.001f, false), (-0.001f, false), (0.0011f, true), (-0.0011f, true),
                         (0.25f, true), (1f, true), (float.NaN, false) })
                Assert.Equal(expected, Unbound.Runtime.AnimationBindings.AnimatedBool(value));
        }

        /// <summary>A blend tree child's share of a channel it has no curve for takes the default
        /// value with write defaults, the current value without (tests/projects/animpartial tree.*):
        /// Slow (x 0) with WaveClip (y 4) at 0.25, on a body resting at (8, 1, 0).</summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BlendTreeChildrenLeaveWhatTheyDoNotAnimateToTheStatesWriteDefaults(bool writeDefaults)
        {
            var yaml = AnimationFixtures.Layers
                .Replace("    m_Motion: {fileID: 7400002}\n    m_Threshold: 1", "    m_Motion: {fileID: 7400003}\n    m_Threshold: 1")
                .Replace("  m_WriteDefaultValues: 1\n  m_Motion: {fileID: 20600001}", "  m_WriteDefaultValues: " + (writeDefaults ? 1 : 0) + "\n  m_Motion: {fileID: 20600001}");
            Assert.Contains("m_Motion: {fileID: 7400003}\n    m_Threshold: 1", yaml);
            var file = Load(yaml);
            var root = Hierarchy("Character", "Body");
            var body = root.transform.Find("Body");
            body.localPosition = new Vector3(8f, 1f, 0f);
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = (RuntimeAnimatorController)file.Objects[9100000];
            animator.SetLayerWeight(1, 0f);
            animator.SetLayerWeight(2, 0f);
            animator.SetFloat("Blend", 0.25f);
            animator.Update(0.1f);
            Near.Equal(new Vector3(2f, 1.75f, 0f), body.localPosition, 1e-4f);
            animator.Update(0.1f);
            Near.Equal(writeDefaults ? new Vector3(2f, 1.75f, 0f) : new Vector3(0.5f, 2.3125f, 0f), body.localPosition, 1e-4f);
            // Only WaveClip: with write defaults Slow's x returns to its default.
            animator.SetFloat("Blend", 1f);
            animator.Update(0.1f);
            Near.Equal(writeDefaults ? 8f : 0.5f, body.localPosition.x, 1e-4f);
            Near.Equal(4f, body.localPosition.y, 1e-4f);
            Object.DestroyImmediate(root);
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
            try
            {
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
            }
            finally { Object.DestroyImmediate(root); }
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
            // Base: 1D blend of x 0 and x 10; Upper rests without writing; Add's constant z 2 is
            // its own reference pose (its first frame, measured): it adds no difference.
            Near.Equal(new Vector3(2.5f, 0f, 0f), body.localPosition, 1e-4f);
            var info = animator.GetCurrentAnimatorStateInfo(0);
            Near.Equal(1.25f, info.length, 1e-4f);
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            Assert.Equal(2, clips.Length);
            Near.Equal(0.75f, clips[0].weight);
            Near.Equal(0.25f, clips[1].weight);

            // Wave writes y 4 over the base at half weight; its write defaults cover only what
            // its layer animates, not the base layer's x (measured).
            animator.SetBool("Wave", true);
            animator.Update(0.1f);
            Assert.True(animator.GetCurrentAnimatorStateInfo(1).IsName("Wave"));
            Near.Equal(new Vector3(2.5f, 2f, 0f), body.localPosition, 1e-4f);

            animator.SetLayerWeight(1, 1f);
            animator.Update(0.1f);
            Near.Equal(new Vector3(2.5f, 4f, 0f), body.localPosition, 1e-4f);
            Object.DestroyImmediate(root);
        }

        [Fact]
        public void AStatesMirrorReachesItsDefinition()
        {
            // Mirrored states play humanoid clips mirrored: the flag must get there.
            var file = Load(AnimationFixtures.Locomotion);
            var controller = (RuntimeAnimatorController)file.Objects[9100000];
            var state = file.Objects.Values.OfType<ControllerStateAsset>().First();
            state.Mirror = true;
            var graph = ControllerGraph.Build(controller);
            var def = graph.Layers.SelectMany(l => l.States).Single(s => s.Name == state.m_Name);
            Assert.True(def.Mirror);
            Assert.All(graph.Layers.SelectMany(l => l.States).Where(s => s != def), s => Assert.False(s.Mirror));
            Assert.Equal(-1, def.MirrorParameter);
            // A mirror parameter (a bool) decides instead when it is active.
            state.MirrorParameter = "Run";
            def = ControllerGraph.Build(controller).Layers.SelectMany(l => l.States).Single(s => s.Name == state.m_Name);
            Assert.Equal(controller.m_Parameters.FindIndex(p => p.m_Name == "Run"), def.MirrorParameter);
        }

        public class ClipInfoReceiver : MonoBehaviour
        {
            public readonly List<AnimationClip> Clips = new List<AnimationClip>();
            void OnStep(AnimationEvent e) => Clips.Add(e.animatorClipInfo.clip);
        }

        [Fact]
        public void AMirroredStatesHumanoidClipKeepsItsIdentity()
        {
            // A mirrored state plays a humanoid clip through a mirrored copy: events, clip infos
            // and the object list still see the clip itself.
            var file = Load(AnimationFixtures.PopupButton);
            var controller = (RuntimeAnimatorController)file.Objects[9100000];
            var clip = (AnimationClip)file.Objects[7400000];
            clip.m_Curves.Add(new ClipCurve
            {
                Binding = new CurveBinding { Path = "", Attribute = "Left Arm Down-Up", ClassId = 95 },
                Curve = AnimationCurve.Linear(0f, 0.2f, 0.3f, -0.4f),
            });
            clip.m_Version++;
            clip.AddEvent(new AnimationEvent { time = 0.1f, functionName = "OnStep" });
            file.Objects.Values.OfType<ControllerStateAsset>().Single().Mirror = true;
            Assert.True(clip.HasHumanCurves);

            var root = new GameObject("Button", typeof(RectTransform));
            var text = new GameObject("ContinueText", typeof(RectTransform));
            text.transform.SetParent(root.transform, false);
            var receiver = root.AddComponent<ClipInfoReceiver>();
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.Update(0f);
            animator.Update(0.25f);
            Assert.Single(receiver.Clips);
            Assert.Same(clip, receiver.Clips[0]);
            Assert.Same(clip, animator.GetCurrentAnimatorClipInfo(0).Single().clip);
            var copy = clip.StateMirroredIfMade;
            Assert.NotNull(copy);
            Assert.NotSame(clip, copy);
            Assert.Same(clip, copy.Asset);
            var listed = Resources.FindObjectsOfTypeAll<AnimationClip>();
            Assert.Contains(clip, listed);
            Assert.DoesNotContain(copy, listed);
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
