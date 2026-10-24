using System.Collections.Generic;
using Unbound.Tests;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Writes its callbacks into a shared list.</summary>
    public class LoggingBehaviour : PlayableBehaviour
    {
        public static readonly List<string> Log = new List<string>();
        public string label = "?";

        public override void OnGraphStart(Playable playable) => Log.Add(label + ".start");
        public override void OnGraphStop(Playable playable) => Log.Add(label + ".stop");
        public override void OnBehaviourPlay(Playable playable, FrameData info) => Log.Add(label + ".play");
        public override void OnBehaviourPause(Playable playable, FrameData info) => Log.Add(label + ".pause");
        public override void PrepareFrame(Playable playable, FrameData info) => Log.Add($"{label}.prepare t={playable.GetTime():0.##} w={info.weight:0.##} seek={info.seekOccurred}");
        public override void ProcessFrame(Playable playable, FrameData info, object playerData) => Log.Add(label + ".process");
        public override void OnPlayableDestroy(Playable playable) => Log.Add(label + ".destroy");
    }

    /// <summary>The graph rules tests/projects/playables recorded from the Unity Editor (the probe
    /// checks them in full; these keep the main ones from regressing).</summary>
    [Collection("Animation")]
    public class PlayablesTests
    {
        static ScriptPlayable<LoggingBehaviour> Make(PlayableGraph g, string label, int inputs = 0) =>
            ScriptPlayable<LoggingBehaviour>.Create(g, new LoggingBehaviour { label = label }, inputs);

        [Fact]
        public void PrepareRunsParentFirstAndProcessInputsFirst()
        {
            LoggingBehaviour.Log.Clear();
            var g = PlayableGraph.Create("order");
            var o = ScriptPlayableOutput.Create(g, "out");
            var root = Make(g, "root", 1);
            var child = Make(g, "child");
            root.ConnectInput(0, child, 0, 0.5f);
            o.SetSourcePlayable(root);
            g.Evaluate(0.1f);
            Assert.Equal(new[]
            {
                "root.start", "root.play", "root.prepare t=0.1 w=1 seek=False",
                "child.start", "child.play", "child.prepare t=0.1 w=0.5 seek=False",
                "child.process", "root.process",
            }, LoggingBehaviour.Log);
            LoggingBehaviour.Log.Clear();
            root.SetTime(2);
            g.Evaluate(0.1f);
            Assert.Equal("root.prepare t=2 w=1 seek=True", LoggingBehaviour.Log[0]);
            g.Destroy();
        }

        [Fact]
        public void PausedSubtreesLearnTheyArePausedButDoNotPrepare()
        {
            LoggingBehaviour.Log.Clear();
            var g = PlayableGraph.Create("pause");
            var o = ScriptPlayableOutput.Create(g, "out");
            var root = Make(g, "root", 1);
            var child = Make(g, "child");
            root.ConnectInput(0, child, 0, 1f);
            o.SetSourcePlayable(root);
            g.Evaluate(0.1f);
            root.Pause();
            LoggingBehaviour.Log.Clear();
            g.Evaluate(0.1f);
            Assert.Equal(new[] { "root.pause", "child.pause" }, LoggingBehaviour.Log);
            Near.Equal(0.1f, (float)child.GetTime());
            g.Destroy();
        }

        [Fact]
        public void WrapModesAtTheDuration()
        {
            var g = PlayableGraph.Create("wrap");
            var o = ScriptPlayableOutput.Create(g, "out");
            var p = Playable.Create(g);
            o.SetSourcePlayable(p);
            p.SetDuration(1);
            p.SetTimeWrapMode(DirectorWrapMode.Loop);
            for (int i = 0; i < 4; i++) g.Evaluate(0.3f);
            Near.Equal(0.2f, (float)p.GetTime());
            Assert.False(p.IsDone());
            p.SetTimeWrapMode(DirectorWrapMode.None);
            g.Evaluate(0.9f);
            Near.Equal(1f, (float)p.GetTime());
            Assert.True(p.IsDone());
            g.Destroy();
        }

        [Fact]
        public void MixersFillMissingWeightWithDefaultValues()
        {
            var go = new GameObject("Mixed");
            go.transform.localPosition = new Vector3(5f, 5f, 5f);
            var animator = go.AddComponent<Animator>();
            // Runtime SetCurve takes legacy clips only, as in Unity: build legacy, then play as a playable clip.
            var clip = new AnimationClip { legacy = true };
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0f, 1f, 1f));
            clip.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Constant(0f, 1f, 1f));
            clip.SetCurve("", typeof(Transform), "localPosition.z", AnimationCurve.Constant(0f, 1f, 1f));
            clip.legacy = false;
            var g = PlayableGraph.Create("mixer");
            var output = AnimationPlayableOutput.Create(g, "anim", animator);
            var mixer = AnimationMixerPlayable.Create(g, 1);
            g.Connect(AnimationClipPlayable.Create(g, clip), 0, mixer, 0);
            output.SetSourcePlayable(mixer);
            mixer.SetInputWeight(0, 0.5f);
            g.Evaluate(0f);
            Near.Equal(new Vector3(3f, 3f, 3f), go.transform.localPosition);
            mixer.SetInputWeight(0, 1f);
            g.Evaluate(0f);
            Near.Equal(new Vector3(1f, 1f, 1f), go.transform.localPosition);
            g.Destroy();
            Object.DestroyImmediate(go);
        }
    }
}
