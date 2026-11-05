using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Clip curves named after a controller parameter, played by an AnimatorControllerPlayable on an
    /// Animator, and the once-only notice of Animation.SyncLayer. Documented (6000.0 Scripting
    /// API): AnimatorControllerPlayable.IsParameterControlledByCurve and
    /// Animator.IsParameterControlledByCurve return true if the parameter is controlled by a curve,
    /// false otherwise; the playable's parameters are its controller's. That a playable's own clip
    /// curve drives the playable's own float parameter (and not the Animator's controller's) is
    /// read from those contracts; no Reference capture measures it, and the values the parameter
    /// takes are Unbound's choices (unmeasured) and are marked so. Animation.SyncLayer has no 6000.0
    /// page: its notice claims no guarantee. Created objects and graphs are destroyed in
    /// <see cref="Dispose"/>, which also fails on unexpected warnings.
    /// </summary>
    [Collection("Animation")]
    public class PlayableControllerParameterCurveTests : System.IDisposable
    {
        readonly List<string> m_Logs = new List<string>();
        readonly List<string> m_Expected = new List<string>();
        readonly List<Object> m_Created = new List<Object>();
        readonly List<PlayableGraph> m_Graphs = new List<PlayableGraph>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;
        readonly bool m_PreviousLogEnabled;
        readonly LogType m_PreviousFilter;

        public PlayableControllerParameterCurveTests()
        {
            m_PreviousHandler = m_Logger.logHandler;
            m_PreviousLogEnabled = m_Logger.logEnabled;
            m_PreviousFilter = m_Logger.filterLogType;
            m_Logger.logHandler = new CaptureHandler(m_PreviousHandler, m_Logs);
            m_Logger.logEnabled = true;
            m_Logger.filterLogType = LogType.Log;
        }

        sealed class CaptureHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Logs;

            internal CaptureHandler(ILogHandler inner, List<string> logs)
            {
                m_Inner = inner;
                m_Logs = logs;
            }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type != LogType.Log)
                {
                    string message = args == null || args.Length == 0 ? format
                        : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
                    m_Logs.Add($"{type}: {message}");
                }
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(System.Exception exception, Object context)
            {
                if (exception != null) m_Logs.Add($"Exception: {exception}");
                m_Inner.LogException(exception, context);
            }
        }

        public void Dispose()
        {
            try
            {
                foreach (var g in m_Graphs)
                    if (g.IsValid()) g.Destroy();
                foreach (var o in m_Created)
                    if (o != null) Object.DestroyImmediate(o);
            }
            finally
            {
                m_Logger.logHandler = m_PreviousHandler;
                m_Logger.logEnabled = m_PreviousLogEnabled;
                m_Logger.filterLogType = m_PreviousFilter;
            }
            var unexpected = m_Logs.FindAll(l => !m_Expected.Exists(e => l.Contains(e)));
            Assert.True(unexpected.Count == 0, string.Join("\n", unexpected));
        }

        T Keep<T>(T o) where T : Object
        {
            m_Created.Add(o);
            return o;
        }

        PlayableGraph Graph(string name)
        {
            var g = PlayableGraph.Create(name);
            m_Graphs.Add(g);
            return g;
        }

        static int Hits(string member) => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        // ------------------------------------------------------------------ fixtures

        static LoadedFile Load(string yaml) =>
            ObjectGraphBuilder.Build(UnityYaml.Parse(yaml), "test.asset", null, ObjectGraphBuilder.Kind.Asset, null);

        /// <summary>A fresh locomotion controller: floats Speed (default 0.5), bool Run, trigger
        /// Jump; it rests in Idle (Run and Jump stay false). Its clips drive no parameter.</summary>
        static RuntimeAnimatorController Locomotion() => (RuntimeAnimatorController)Load(AnimationFixtures.Locomotion).Objects[9100000];

        /// <summary>A fresh locomotion controller whose Idle clip also has an Animator curve Speed
        /// (constant <paramref name="value"/>, 0.8 by default): the clip curve of its float
        /// parameter Speed.</summary>
        static RuntimeAnimatorController SpeedCurveController(out AnimationClip idle, float value = 0.8f)
        {
            var file = Load(AnimationFixtures.Locomotion);
            idle = (AnimationClip)file.Objects[7400001];
            idle.m_Curves.Add(new ClipCurve
            {
                Binding = new CurveBinding { Path = "", Attribute = "Speed", ClassId = 95 },
                Curve = AnimationCurve.Constant(0f, 1f, value),
            });
            idle.m_Version++;
            return (RuntimeAnimatorController)file.Objects[9100000];
        }

        static RuntimeAnimatorController SpeedCurveController() => SpeedCurveController(out _);

        static RuntimeAnimatorController SpeedCurveController(float value) => SpeedCurveController(out _, value);

        /// <summary>A controller without parameters (one state; its clip animates a path the test
        /// hierarchy lacks).</summary>
        static RuntimeAnimatorController NoParameters() => (RuntimeAnimatorController)Load(AnimationFixtures.PopupButton).Objects[9100000];

        Animator Character(RuntimeAnimatorController own)
        {
            var go = Keep(new GameObject("Character"));
            new GameObject("Body").transform.SetParent(go.transform, false);
            var animator = go.AddComponent<Animator>();
            if (own != null) animator.runtimeAnimatorController = own;
            return animator;
        }

        /// <summary>A graph whose animation output on <paramref name="animator"/> plays a controller
        /// playable of <paramref name="controller"/>.</summary>
        AnimatorControllerPlayable Play(Animator animator, RuntimeAnimatorController controller, out PlayableGraph g)
        {
            g = Graph("controller");
            var output = AnimationPlayableOutput.Create(g, "anim", animator);
            var playable = AnimatorControllerPlayable.Create(g, controller);
            output.SetSourcePlayable(playable);
            return playable;
        }

        // ------------------------------------------------------------------ playable controllers

        /// <summary>The reviewed case. Documented: the playable's query is true when a curve controls
        /// its parameter; here its own clip's curve Speed controls its float Speed although the
        /// Animator's own controller has no parameter Speed, so the query is true and the curve
        /// drives the playable's Speed (0.8 after one evaluation: Unbound's choice of value,
        /// unmeasured). The Animator's own query stays false (its controller has no Speed).
        /// RED at 0f003eed: the playable answers false (with the interim not-honoured warning) and
        /// its Speed stays 0.5.</summary>
        [Fact]
        public void APlayablesCurveDrivesItsFloatWhenTheAnimatorsControllerLacksIt()
        {
            var animator = Character(NoParameters());
            var playable = Play(animator, SpeedCurveController(), out var g);
            Assert.True(playable.IsParameterControlledByCurve("Speed"));
            Assert.True(playable.IsParameterControlledByCurve(Animator.StringToHash("Speed")));
            g.Evaluate(0f);
            Near.Equal(0.8f, playable.GetFloat("Speed"));
            Assert.True(playable.IsParameterControlledByCurve("Speed"));
            Assert.False(animator.IsParameterControlledByCurve("Speed"));
        }

        /// <summary>As above, on an Animator with no controller of its own (documented contract;
        /// value Unbound's choice, unmeasured). RED at 0f003eed: false, and Speed stays 0.5.</summary>
        [Fact]
        public void APlayablesCurveDrivesItsFloatWhenTheAnimatorHasNoController()
        {
            var animator = Character(null);
            var playable = Play(animator, SpeedCurveController(), out var g);
            Assert.True(playable.IsParameterControlledByCurve("Speed"));
            g.Evaluate(0f);
            Near.Equal(0.8f, playable.GetFloat("Speed"));
            Assert.False(animator.IsParameterControlledByCurve("Speed"));
        }

        /// <summary>The Animator's own controller is a distinct one that also has a float Speed
        /// (but no curve of it): the playable's own clip curve still drives the playable's own
        /// Speed (documented contract; value Unbound's choice, unmeasured). What the Animator's own
        /// parameter does is kept as before and not asserted here. RED at 0f003eed: the playable's
        /// Speed stays 0.5 (the curve wrote only the Animator's own Speed).</summary>
        [Fact]
        public void ADistinctPlayableControllerHasItsOwnParameterDriven()
        {
            var animator = Character(Locomotion());
            var playable = Play(animator, SpeedCurveController(), out var g);
            Assert.True(playable.IsParameterControlledByCurve("Speed"));
            g.Evaluate(0f);
            Near.Equal(0.8f, playable.GetFloat("Speed"));
        }

        /// <summary>The Animator's own controller's clip has the curve Speed; the playable's
        /// controller has the float Speed but none of its clips has the curve. Documented: false
        /// when no curve controls the playable's parameter; the Animator's own curve does not claim
        /// it. The Animator's own query and parameter are unchanged (true, 0.8 after its update);
        /// the playable's Speed keeps its default 0.5 through both updates (Unbound's choice,
        /// unmeasured). RED at 0f003eed: the playable answers true (shared bindings, ledger L-4).</summary>
        [Fact]
        public void TheAnimatorsOwnCurveDoesNotClaimThePlayablesParameter()
        {
            var animator = Character(SpeedCurveController());
            var playable = Play(animator, Locomotion(), out var g);
            Assert.True(animator.IsParameterControlledByCurve("Speed"));
            Assert.False(playable.IsParameterControlledByCurve("Speed"));
            Assert.False(playable.IsParameterControlledByCurve(Animator.StringToHash("Speed")));
            animator.Update(0f);
            g.Evaluate(0f);
            Near.Equal(0.8f, animator.GetFloat("Speed"));
            Near.Equal(0.5f, playable.GetFloat("Speed"));
            Assert.False(playable.IsParameterControlledByCurve("Speed"));
        }

        /// <summary>Two controller playables mixed on one output of an Animator without a
        /// controller: only the one whose own clip has the curve answers true. The other has the
        /// float Speed too, answers false and keeps its default 0.5 (documented contract for the
        /// answers; the value is Unbound's choice, unmeasured). RED at 0f003eed: the curve's own
        /// playable answers false (the other playable's parts are guards, the same at 0f003eed).</summary>
        [Fact]
        public void AnotherPlayablesCurveDoesNotClaimItsParameter()
        {
            var animator = Character(null);
            var g = Graph("mixed");
            var output = AnimationPlayableOutput.Create(g, "anim", animator);
            var plain = AnimatorControllerPlayable.Create(g, Locomotion());
            var curved = AnimatorControllerPlayable.Create(g, SpeedCurveController());
            var mixer = AnimationMixerPlayable.Create(g, 2);
            g.Connect(plain, 0, mixer, 0);
            g.Connect(curved, 0, mixer, 1);
            mixer.SetInputWeight(0, 0.5f);
            mixer.SetInputWeight(1, 0.5f);
            output.SetSourcePlayable(mixer);
            g.Evaluate(0f);
            Assert.True(curved.IsParameterControlledByCurve("Speed"));
            Assert.False(plain.IsParameterControlledByCurve("Speed"));
            Near.Equal(0.5f, plain.GetFloat("Speed"));
        }

        /// <summary>The playable's clip was bound on the Animator before the playable existed (a
        /// clip playable played it, when no controller claimed its curve Speed, which is reported as
        /// not supported): once the controller playable plays it, the curve drives the playable's
        /// Speed (documented contract; value Unbound's choice, unmeasured). RED at 0f003eed: false,
        /// and Speed stays 0.5.</summary>
        [Fact]
        public void AClipBoundBeforeThePlayableExistedBindsForIt()
        {
            m_Expected.Add("animating Animator.Speed is not supported yet"); // the clip playable's curve, once per process
            var animator = Character(null);
            var controller = SpeedCurveController(out var idle);
            var first = Graph("clip");
            var clipOutput = AnimationPlayableOutput.Create(first, "anim", animator);
            clipOutput.SetSourcePlayable(AnimationClipPlayable.Create(first, idle));
            first.Evaluate(0f);
            first.Destroy();
            var playable = Play(animator, controller, out var g);
            Assert.True(playable.IsParameterControlledByCurve("Speed"));
            g.Evaluate(0f);
            Near.Equal(0.8f, playable.GetFloat("Speed"));
        }

        /// <summary>Guard, not a red (green at 0f003eed too): an Animator playing its own controller,
        /// without any graph, answers and is driven as before (curve Speed 0.8; Run is a bool, Jump a
        /// trigger: no curve).</summary>
        [Fact]
        public void TheAnimatorsOwnControllerPathIsUnchanged()
        {
            var animator = Character(SpeedCurveController());
            Assert.True(animator.IsParameterControlledByCurve("Speed"));
            Assert.True(animator.IsParameterControlledByCurve(Animator.StringToHash("Speed")));
            Assert.False(animator.IsParameterControlledByCurve("Run"));
            Assert.False(animator.IsParameterControlledByCurve("Missing"));
            Near.Equal(0.5f, animator.GetFloat("Speed"));
            animator.Update(0f);
            Near.Equal(0.8f, animator.GetFloat("Speed"));
        }

        // ------------------------------------------------------------------ controller-local samples

        /// <summary>A playable's parameter takes its own controller's sample, not the graph's mix
        /// (documented: the parameter takes the value of the curve; clips of the same controller
        /// blend; nothing documents an outer graph weight scaling it, so not scaling is Unbound's
        /// reading, unmeasured). The curved playable at 0.5 in a 0.5/0.5 mixer keeps its own 0.8;
        /// the plain one (no curve) keeps its default 0.5. No warning. RED at fae056c2: the curved
        /// playable's Speed is the mixed channel, 0.5 * 0.8 + 0.5 * 0.5 = 0.65.</summary>
        [Fact]
        public void AnOuterMixDoesNotBlendAPlayablesOwnParameter()
        {
            var animator = Character(null);
            var g = Graph("mixed");
            var output = AnimationPlayableOutput.Create(g, "anim", animator);
            var plain = AnimatorControllerPlayable.Create(g, Locomotion());
            var curved = AnimatorControllerPlayable.Create(g, SpeedCurveController());
            var mixer = AnimationMixerPlayable.Create(g, 2);
            g.Connect(plain, 0, mixer, 0);
            g.Connect(curved, 0, mixer, 1);
            mixer.SetInputWeight(0, 0.5f);
            mixer.SetInputWeight(1, 0.5f);
            output.SetSourcePlayable(mixer);
            g.Evaluate(0f);
            Near.Equal(0.8f, curved.GetFloat("Speed"));
            Near.Equal(0.5f, plain.GetFloat("Speed"));
        }

        /// <summary>Two controller playables mixed 0.5/0.5, each with its own clip curve Speed (0.2 and
        /// 1.0): each parameter takes its own controller's curve (documented contract per controller;
        /// values Unbound's reading, unmeasured). RED at fae056c2: both get the mixed channel, 0.6.</summary>
        [Fact]
        public void TwoControllersKeepTheirOwnCurveValues()
        {
            var animator = Character(null);
            var g = Graph("two");
            var output = AnimationPlayableOutput.Create(g, "anim", animator);
            var low = AnimatorControllerPlayable.Create(g, SpeedCurveController(0.2f));
            var high = AnimatorControllerPlayable.Create(g, SpeedCurveController(1f));
            var mixer = AnimationMixerPlayable.Create(g, 2);
            g.Connect(low, 0, mixer, 0);
            g.Connect(high, 0, mixer, 1);
            mixer.SetInputWeight(0, 0.5f);
            mixer.SetInputWeight(1, 0.5f);
            output.SetSourcePlayable(mixer);
            g.Evaluate(0f);
            Assert.True(low.IsParameterControlledByCurve("Speed"));
            Assert.True(high.IsParameterControlledByCurve("Speed"));
            Near.Equal(0.2f, low.GetFloat("Speed"));
            Near.Equal(1f, high.GetFloat("Speed"));
        }

        /// <summary>A controller playable of the graph that no output reaches (its player made by a
        /// GetFloat call) is not evaluated, so its parameter keeps its default 0.5 while the played
        /// one takes its own 1.0 (Unbound's choice, unmeasured: an unevaluated controller samples
        /// nothing). RED at fae056c2: the unconnected playable's Speed becomes the played one's 1.0.</summary>
        [Fact]
        public void AnUnconnectedPlayableKeepsItsParameters()
        {
            var animator = Character(null);
            var g = Graph("unconnected");
            var output = AnimationPlayableOutput.Create(g, "anim", animator);
            var played = AnimatorControllerPlayable.Create(g, SpeedCurveController(1f));
            var idle = AnimatorControllerPlayable.Create(g, SpeedCurveController(0.2f));
            output.SetSourcePlayable(played);
            Near.Equal(0.5f, idle.GetFloat("Speed"));
            g.Evaluate(0f);
            Near.Equal(1f, played.GetFloat("Speed"));
            Near.Equal(0.5f, idle.GetFloat("Speed"));
        }

        /// <summary>An unrelated controller playable (float Speed with default 0.3, no curve of it,
        /// never played) comes first in the graph: the Speed channel's default comes from the
        /// controller whose clip curve made it (0.5), never from the unrelated one (Unbound's choice,
        /// unmeasured; the channel default is what mixers fill with). RED at fae056c2: the channel's
        /// default is the unrelated controller's 0.3.</summary>
        [Fact]
        public void AnUnrelatedControllersDefaultIsNotTheChannels()
        {
            var animator = Character(null);
            var g = Graph("unrelated");
            var output = AnimationPlayableOutput.Create(g, "anim", animator);
            var unrelatedController = Locomotion();
            var graph = unrelatedController.Graph;
            graph.Parameters[graph.ParameterIndex("Speed")].DefaultFloat = 0.3f;
            AnimatorControllerPlayable.Create(g, unrelatedController);
            var curved = AnimatorControllerPlayable.Create(g, SpeedCurveController());
            output.SetSourcePlayable(curved);
            g.Evaluate(0f);
            var bindings = animator.StreamBindings;
            var channel = bindings.Groups.OfType<ParameterCurveGroup>().Single(p => p.Name == "Speed");
            Near.Equal(0.5f, bindings.Defaults[channel.First]);
            Near.Equal(0.8f, curved.GetFloat("Speed"));
        }

        /// <summary>A stream write of the Animator's float Speed (OpenAnimationStream, a property
        /// stream handle, CloseAnimationStream) drives the Animator's own parameter as before (not
        /// asserted here) and does not reach a controller playable's parameter: it keeps its own
        /// curve's 0.8 (Unbound's choice, unmeasured). RED at fae056c2: the playable's Speed becomes
        /// the written 0.3.</summary>
        [Fact]
        public void StreamWritesDoNotReachAPlayablesParameters()
        {
            var animator = Character(Locomotion());
            var playable = Play(animator, SpeedCurveController(), out var g);
            g.Evaluate(0f);
            Near.Equal(0.8f, playable.GetFloat("Speed"));
            var handle = animator.BindStreamProperty(animator.transform, typeof(Animator), "Speed");
            var stream = default(AnimationStream);
            Assert.True(animator.OpenAnimationStream(ref stream));
            handle.SetFloat(stream, 0.3f);
            animator.CloseAnimationStream(ref stream);
            Near.Equal(0.8f, playable.GetFloat("Speed"));
        }

        /// <summary>Guard, not a red (green at fae056c2 and, for the Animator, at 0f003eed): the
        /// Animator's own controller has the curve Speed (0.8) and a playable's controller another
        /// (0.2). The own update drives the own parameter to 0.8; the output then drives it as any
        /// curve of the name on its bindings did before (0.2, kept from 0f003eed, unmeasured); the
        /// playable has its own 0.2.</summary>
        [Fact]
        public void TheAnimatorsOwnParameterKeepsItsPathBesideAPlayable()
        {
            var animator = Character(SpeedCurveController());
            var playable = Play(animator, SpeedCurveController(0.2f), out var g);
            animator.Update(0f);
            Near.Equal(0.8f, animator.GetFloat("Speed"));
            g.Evaluate(0f);
            Near.Equal(0.2f, animator.GetFloat("Speed"));
            Near.Equal(0.2f, playable.GetFloat("Speed"));
            Assert.True(animator.IsParameterControlledByCurve("Speed"));
            Assert.True(playable.IsParameterControlledByCurve("Speed"));
        }

        // ------------------------------------------------------------------ SyncLayer notice

        /// <summary>Animation.SyncLayer has no 6000.0 page and nothing measures it: every call is
        /// counted, and the first in the process logs one notice that its semantics are unmeasured
        /// (no documented guarantee is claimed; the behaviour is not asserted here). RED at
        /// 0f003eed: the calls are not counted (no notice at all).</summary>
        [Fact]
        public void SyncLayerReportsItsUnmeasuredSemanticsOnce()
        {
            m_Expected.Add("Animation.SyncLayer");
            int before = Hits("Animation.SyncLayer");
            var animation = Keep(new GameObject("Legacy")).AddComponent<Animation>();
            animation.SyncLayer(1);
            animation.SyncLayer(1);
            animation.SyncLayer(2);
            Assert.Equal(before + 3, Hits("Animation.SyncLayer"));
            var notices = m_Logs.FindAll(l => l.Contains("Animation.SyncLayer"));
            int logged = before == 0 ? 1 : 0; // an earlier call in the process logged it already
            Assert.Equal(logged, notices.Count);
            foreach (var notice in notices)
            {
                Assert.StartsWith("Warning: Unbound: Animation.SyncLayer", notice);
                Assert.Contains("unmeasured", notice);
            }
        }
    }
}
