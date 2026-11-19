using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The Editor's controller object model built in memory (UnityEditor.Animations), the restart of
    /// an Animator whose controller is edited, the messages a compile logs, AnimationUtility's
    /// curve tools and AnimationMode. Expected values match the Unity 6000.0.71f1 Editor,
    /// tests/projects/animatorapi (named per test); controls outside the probe's exact setup are
    /// marked. Created objects are destroyed and unexpected warnings fail in <see cref="Dispose"/>.
    /// </summary>
    [Collection("Animation")]
    public class AnimatorControllerModelTests : System.IDisposable
    {
        readonly List<string> m_Logs = new List<string>();
        readonly List<Object> m_Created = new List<Object>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;

        public AnimatorControllerModelTests()
        {
            m_PreviousHandler = m_Logger.logHandler;
            m_Logger.logHandler = new CaptureHandler(m_PreviousHandler, m_Logs);
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
                    m_Logs.Add($"{type}: " + (args == null || args.Length == 0 ? format : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args)));
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(System.Exception exception, Object context) => m_Logs.Add($"Exception: {exception}");
        }

        public void Dispose()
        {
            foreach (var o in m_Created)
                if (o != null) Object.DestroyImmediate(o);
            m_Logger.logHandler = m_PreviousHandler;
            Assert.True(m_Logs.Count == 0, string.Join("\n", m_Logs));
        }

        T Keep<T>(T o) where T : Object
        {
            m_Created.Add(o);
            return o;
        }

        /// <summary>The logs since the last call.</summary>
        List<string> TakeLogs()
        {
            var logs = new List<string>(m_Logs);
            m_Logs.Clear();
            return logs;
        }

        AnimatorController Controller(string name = "C")
        {
            var c = Keep(new AnimatorController { name = name });
            c.AddLayer("Base Layer");
            return c;
        }

        AnimationClip Clip(string name, float length)
        {
            var clip = Keep(new AnimationClip { name = name });
            clip.SetCurve("Child", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, length, 1));
            return clip;
        }

        // ------------------------------------------------------------------ the object model

        /// <summary>model.params.*, model.layers.unique*: unique names add " 0", " 1"; an added
        /// object keeps its name; removing by value needs every field to match.</summary>
        [Fact]
        public void ParametersAndLayersTakeUniqueNames()
        {
            var c = Controller();
            c.AddParameter("Speed", AnimatorControllerParameterType.Float);
            c.AddParameter("Speed", AnimatorControllerParameterType.Float);
            c.AddParameter(new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Int });
            Assert.Equal(new[] { "Speed", "Speed 0", "Speed" }, c.parameters.Select(p => p.name));
            Assert.Equal("Speed 1", c.MakeUniqueParameterName("Speed"));
            Assert.Equal("Base Layer 0", c.MakeUniqueLayerName("Base Layer"));
            c.RemoveParameter(new AnimatorControllerParameter { name = "Speed 0", type = AnimatorControllerParameterType.Int });
            Assert.Equal(3, c.parameters.Length);
            c.RemoveParameter(new AnimatorControllerParameter { name = "Speed 0", type = AnimatorControllerParameterType.Float });
            Assert.Equal(new[] { "Speed", "Speed" }, c.parameters.Select(p => p.name));
            // The getter answers copies: changing one changes nothing.
            c.parameters[0].name = "Changed";
            Assert.Equal("Speed", c.parameters[0].name);
        }

        /// <summary>model.states.*, extra.uniqueClash: states go at (200,0,0) then 35 right and 65
        /// down; the first becomes the default; names are unique per machine and states do not
        /// collide with machines; adding a state twice warns.</summary>
        [Fact]
        public void StatesAreAddedWhereTheEditorPutsThem()
        {
            var sm = Controller().layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            var again = sm.AddState("Idle");
            var sub = sm.AddStateMachine("Sub");
            var inner = sub.AddState("Idle");
            Assert.Equal(new[] { new Vector3(200, 0, 0), new Vector3(235, 65, 0) }, sm.states.Select(s => s.position));
            Assert.Equal("Idle 0", again.name);
            Assert.Equal("Idle", inner.name);
            Assert.Same(idle, sm.defaultState);
            Assert.Equal(Vector3.zero, sm.stateMachines[0].position);
            Assert.Equal(HideFlags.HideInHierarchy, idle.hideFlags);
            Assert.Equal("Sub", sm.MakeUniqueStateName("Sub"));
            Assert.Equal("Sub 0", sm.MakeUniqueStateMachineName("Sub"));
            sm.AddState(idle, Vector3.one);
            Assert.Equal(new[] { "Warning: State 'Idle' already exists in state machine 'Base Layer', discarding new state." }, TakeLogs());
        }

        /// <summary>extra.newStateTransition, model.transitions.*: a new transition lasts 0.1 and
        /// leaves at 0.9 with an exit time; AddTransition turns the exit time off and a fixed
        /// duration on; the default exit time leaves 0.25 s before the motion's end; Any State
        /// transitions start at 0.25 and 0.75 without an exit time.</summary>
        [Fact]
        public void TransitionsStartWithTheEditorsTiming()
        {
            var fresh = Keep(new AnimatorStateTransition());
            Assert.Equal((0.1f, 0.9f, true, false), (fresh.duration, fresh.exitTime, fresh.hasExitTime, fresh.hasFixedDuration));
            var c = Controller();
            var sm = c.layers[0].stateMachine;
            var a = c.AddMotion(Clip("Long", 2f));
            var b = sm.AddState("B");
            var plain = a.AddTransition(b);
            Assert.Equal((0.1f, 0.9f, false, true), (plain.duration, plain.exitTime, plain.hasExitTime, plain.hasFixedDuration));
            var timed = a.AddTransition(b, true);
            Assert.Equal((0.25f, 0.875f, true), (timed.duration, timed.exitTime, timed.hasExitTime));
            var noMotion = b.AddTransition(a, true);
            Assert.Equal((0.25f, 0.75f), (noMotion.duration, noMotion.exitTime));
            var any = sm.AddAnyStateTransition(b);
            Assert.Equal((0.25f, 0.75f, false, true), (any.duration, any.exitTime, any.hasExitTime, any.hasFixedDuration));
            Assert.Equal("Long -> B", plain.GetDisplayName(a));
            Assert.Equal("Base Layer -> B", any.GetDisplayName(sm));
            Assert.Equal("Long -> Exit", a.AddExitTransition().GetDisplayName(a));
            Assert.Equal("Entry -> INVALID", Keep(new AnimatorTransition()).GetDisplayName(null));
        }

        /// <summary>model.remove.*, extra.removeFromSub: removing a state drops and destroys the
        /// transitions to it inside the machine it is removed from; a parent's stay.</summary>
        [Fact]
        public void RemovingAStateRemovesTheTransitionsToItInItsMachine()
        {
            var sm = Controller().layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            var temp = sm.AddState("Temp");
            var sub = sm.AddStateMachine("Sub");
            var deep = sub.AddState("Deep");
            var toTemp = idle.AddTransition(temp);
            sm.AddAnyStateTransition(temp);
            sm.AddEntryTransition(temp);
            sm.AddStateMachineTransition(sub, temp);
            idle.AddTransition(deep);
            sm.RemoveState(temp);
            Assert.True(temp == null && toTemp == null);
            Assert.Equal((1, 0, 0, 0), (idle.transitions.Length, sm.anyStateTransitions.Length, sm.entryTransitions.Length, sm.GetStateMachineTransitions(sub).Length));
            // Control: removing from the sub-machine leaves the parent's transition to it.
            sub.RemoveState(deep);
            Assert.Single(idle.transitions);
        }

        /// <summary>model.transitions.ofSub, removeMachine: a machine transition with neither a
        /// destination nor an exit is left out; removing one destroys it and answers false.</summary>
        [Fact]
        public void MachineTransitionsNeedADestinationOrAnExit()
        {
            var sm = Controller().layers[0].stateMachine;
            var sub = sm.AddStateMachine("Sub");
            var idle = sm.AddState("Idle");
            var dangling = sm.AddStateMachineTransition(sub);
            var kept = sm.AddStateMachineTransition(sub, idle);
            sm.AddStateMachineExitTransition(sub);
            Assert.Equal(new[] { "Sub -> Idle", "Sub -> Exit" }, sm.GetStateMachineTransitions(sub).Select(t => t.GetDisplayName(sub)));
            Assert.False(sm.RemoveStateMachineTransition(sub, dangling));
            Assert.True(dangling == null);
            Assert.False(sm.RemoveStateMachineTransition(sub, kept));
            Assert.Single(sm.GetStateMachineTransitions(sub));
        }

        /// <summary>model.trees.*, plain.params: automatic thresholds spread from the minimum to the
        /// maximum in single precision; a tree's average duration is its clips' mean (nested trees
        /// flattened, an empty one counts nothing); a controller without a float parameter gets
        /// "Blend".</summary>
        [Fact]
        public void BlendTreesSpreadThresholdsAndAverageTheirClips()
        {
            var c = Controller();
            var state = c.CreateBlendTreeInController("Move", out var tree);
            Assert.Equal(new[] { "Blend" }, c.parameters.Select(p => p.name));
            Assert.Same(tree, state.motion);
            Assert.Equal(HideFlags.None, tree.hideFlags);
            tree.AddChild(Clip("One", 1f));
            tree.AddChild(Clip("Two", 2f));
            var nested = tree.CreateBlendTreeChild(0f);
            tree.AddChild(Clip("Three", 3f));
            Assert.Equal(2f, tree.averageDuration);
            Assert.Equal(HideFlags.HideInHierarchy, nested.hideFlags);
            tree.minThreshold = -1;
            tree.maxThreshold = 4;
            Assert.Equal(new[] { -1f, 0.66666675f, 2.3333335f, 4f }, tree.children.Select(ch => ch.threshold));
            // Control: manual thresholds stay where they are set.
            tree.useAutomaticThresholds = false;
            tree.AddChild(Clip("Four", 4f), 9f);
            Assert.Equal(9f, tree.children[4].threshold);
            Assert.Equal("Blend", tree.children[4].directBlendParameter);
            Assert.Equal(1f, tree.children[4].timeScale);
        }

        /// <summary>model.behaviours.*, extra experiments: machine behaviours come first, then the
        /// states'; a machine behaviour's context adds the state whose full path hash is the
        /// smallest (here Walk, not the last state); a synced layer's override has the synced
        /// layer's index.</summary>
        [Fact]
        public void BehavioursAreListedAndFoundAsInTheEditor()
        {
            var c = Controller();
            var sm = c.layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            var walk = sm.AddState("Walk");
            sm.AddState("Run");
            var own = idle.AddStateMachineBehaviour<StateMachineBehaviourProbe>();
            var machine = sm.AddStateMachineBehaviour<StateMachineBehaviourProbe>();
            c.AddLayer(new AnimatorControllerLayer { name = "Synced", syncedLayerIndex = 0 });
            var synced = c.AddEffectiveStateMachineBehaviour<StateMachineBehaviourProbe>(walk, 1);
            Assert.Equal(new StateMachineBehaviour[] { machine, own, machine, synced }, c.GetBehaviours<StateMachineBehaviourProbe>());
            Assert.Equal(new Object[] { sm, walk }, AnimatorController.FindStateMachineBehaviourContext(machine).Select(x => x.animatorObject));
            Assert.Equal(1, AnimatorController.FindStateMachineBehaviourContext(synced).Single().layerIndex);
            Assert.Equal(new StateMachineBehaviour[] { synced }, c.layers[1].GetOverrideBehaviours(walk));
            Assert.Empty(c.GetStateEffectiveBehaviours(idle, 1));
        }

        /// <summary>extra.compile: compiling logs each missing blend parameter, then each state or
        /// Any State transition left without an exit time or a condition (muted ones and, beside
        /// a solo, non-solo ones skipped; entry transitions never).</summary>
        [Fact]
        public void CompilingLogsTheEditorsMessages()
        {
            var c = Controller("Checks");
            var sm = c.layers[0].stateMachine;
            var a = sm.AddState("A");
            var b = sm.AddState("B");
            sm.AddAnyStateTransition(b);
            sm.AddEntryTransition(b);
            var tree = Keep(new BlendTree { name = "Two", blendType = BlendTreeType.FreeformCartesian2D, blendParameter = "MissA", blendParameterY = "MissB" });
            sm.AddState("TreeState").motion = tree;
            a.AddTransition(b).mute = true;
            a.AddExitTransition().solo = true;
            a.AddTransition(b);
            _ = c.animationClips;
            Assert.Equal(new[]
            {
                "Error: BlendTree \"Two\" in state \"TreeState\" in Controller \"Checks\" uses parameter \"MissA\" which does not exist.",
                "Error: BlendTree \"Two\" in state \"TreeState\" in Controller \"Checks\" uses parameter \"MissB\" which does not exist.",
                "Warning: Asset '': Transition 'A -> Exit' in state 'A' doesn't have an Exit Time or any condition, transition will be ignored",
                "Warning: Asset '': Transition 'AnyState -> B' in state 'AnyState' doesn't have an Exit Time or any condition, transition will be ignored",
            }, TakeLogs());
            // Compiled once: no messages until it is edited.
            _ = c.animationClips;
            Assert.Empty(TakeLogs());
        }

        /// <summary>play.*: an edit of the playing controller restarts its Animators at once
        /// (parameters at their defaults, no state until the next update, which evaluates the
        /// default state at time 0); an Animator playing another controller goes on.</summary>
        [Fact]
        public void EditingAPlayingControllerRestartsItsAnimators()
        {
            var c = Controller();
            c.AddParameter("Go", AnimatorControllerParameterType.Trigger);
            c.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var sm = c.layers[0].stateMachine;
            var a = sm.AddState("A");
            a.motion = Clip("Walk", 1f);
            var b = sm.AddState("B");
            var ab = a.AddTransition(b);
            ab.duration = 0;
            ab.AddCondition(AnimatorConditionMode.If, 0, "Go");
            var other = Controller("Other");
            other.layers[0].stateMachine.AddState("X").motion = Clip("Walk2", 1f);
            Animator Rig(RuntimeAnimatorController controller)
            {
                var go = Keep(new GameObject("Rig"));
                new GameObject("Child").transform.SetParent(go.transform, false);
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                return animator;
            }
            var animator = Rig(c);
            var control = Rig(other);
            animator.Update(0.02f);
            control.Update(0.02f);
            animator.SetTrigger("Go");
            animator.SetFloat("Speed", 2);
            animator.Update(0.02f);
            control.Update(0.02f);
            Assert.Equal(b.nameHash, animator.GetCurrentAnimatorStateInfo(0).shortNameHash);
            c.AddParameter("Extra", AnimatorControllerParameterType.Float);
            Assert.Equal(0, animator.GetCurrentAnimatorStateInfo(0).shortNameHash);
            Assert.Equal((3, 0f), (animator.parameterCount, animator.GetFloat("Speed")));
            animator.Update(0.02f);
            var info = animator.GetCurrentAnimatorStateInfo(0);
            Assert.Equal((a.nameHash, 0f), (info.shortNameHash, info.normalizedTime));
            animator.Update(0.02f);
            Assert.Equal(0.02f, animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 5);
            control.Update(0.02f);
            Assert.Equal(0.06f, control.GetCurrentAnimatorStateInfo(0).normalizedTime, 5);
        }

        /// <summary>RootT/RootQ curves on an Animator without a human are its root motion
        /// (tests/projects/rootcurves): binding them reports nothing (it warned that they were not
        /// supported); a body clip still moves the root.</summary>
        [Fact]
        public void BodyCurvesOfAnAnimatorWithoutAHumanBindSilently()
        {
            var clip = Keep(new AnimationClip { name = "Body" });
            clip.SetCurve("", typeof(Animator), "RootT.z", AnimationCurve.Linear(0, 0, 1, 2));
            clip.SetCurve("", typeof(Animator), "RootQ.w", AnimationCurve.Constant(0, 1, 1));
            var c = Controller();
            c.AddMotion(clip);
            var go = Keep(new GameObject("Rig"));
            var animator = go.AddComponent<Animator>();
            animator.applyRootMotion = true;
            animator.runtimeAnimatorController = c;
            animator.Update(0.25f);
            animator.Update(0.25f);
            Assert.Empty(TakeLogs());
            Assert.True(go.transform.position.z > 0f);
        }

        /// <summary>defaultstate BlendTree: an empty tree loops and is a human motion.</summary>
        [Fact]
        public void AnEmptyBlendTreeLoopsAndIsHuman()
        {
            var tree = Keep(new BlendTree());
            Assert.True(tree.isLooping && tree.isHumanMotion);
            tree.AddChild(Clip("Generic", 1f));
            Assert.False(tree.isHumanMotion);
        }

        // ------------------------------------------------------------------ AnimationUtility, AnimationMode

        /// <summary>util.modified.*: each curve set or removed calls back, batches once more for the
        /// clip; the runtime SetCurve does not.</summary>
        [Fact]
        public void CurveEditsCallBack()
        {
            var log = new List<string>();
            AnimationUtility.OnCurveWasModified callback = (clip, binding, type) => log.Add($"{clip.name}:{type}:{binding.propertyName}");
            AnimationUtility.onCurveWasModified += callback;
            try
            {
                var clip = Keep(new AnimationClip());
                clip.name = "Edited";
                var x = EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x");
                var y = EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.y");
                AnimationUtility.SetEditorCurve(clip, x, AnimationCurve.Linear(0, 0, 1, 1));
                AnimationUtility.SetEditorCurves(clip, new[] { x, y }, new[] { AnimationCurve.Linear(0, 0, 1, 1), null });
                clip.SetCurve("", typeof(Transform), "m_LocalPosition.z", AnimationCurve.Linear(0, 0, 1, 1));
                Assert.Equal(new[] { ":ClipModified:", "Edited:CurveModified:m_LocalPosition.x", "Edited:CurveModified:m_LocalPosition.x",
                    "Edited:CurveDeleted:m_LocalPosition.y", "Edited:ClipModified:" }, log.Select(l => l.Replace(":(null)", ":")));
            }
            finally
            {
                AnimationUtility.onCurveWasModified -= callback;
            }
        }

        /// <summary>util.tangents.*, extra.tangents.*: Linear points at the neighbour; ClampedAuto is
        /// the central difference, flat at an extremum and the ends and at most four times the
        /// smaller rise over the span; Auto the mean slope; Constant is infinite; either smooth mode
        /// sets both sides; setting a mode marks the key broken.</summary>
        [Fact]
        public void TangentModesSetTheEditorsTangents()
        {
            var curve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 0.1f), new Keyframe(1.1f, 5), new Keyframe(2, 5.5f));
            AnimationUtility.SetKeyLeftTangentMode(curve, 1, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(curve, 2, AnimationUtility.TangentMode.Auto);
            Assert.Equal(0.36364f, curve[1].outTangent, 5);
            Assert.Equal(24.77777f, curve[2].inTangent, 4);
            Assert.True(AnimationUtility.GetKeyBroken(curve, 1));
            AnimationUtility.SetKeyLeftTangentMode(curve, 3, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, 3, AnimationUtility.TangentMode.Constant);
            Assert.Equal((0.5555556f, float.PositiveInfinity), (curve[3].inTangent, curve[3].outTangent));
            Assert.Equal(1 | (2 << 1) | (3 << 5), curve[3].tangentModeInternal);
            AnimationUtility.SetKeyRightTangentMode(curve, 0, AnimationUtility.TangentMode.ClampedAuto);
            Assert.Equal((0f, 0f), (curve[0].inTangent, curve[0].outTangent));
            Assert.Throws<System.IndexOutOfRangeException>(() => AnimationUtility.GetKeyLeftTangentMode(curve, 7));
        }

        /// <summary>mode.*: sampling records what it writes and stopping the last driver writes it
        /// back; a driver that did not start stops nothing; sampling outside the mode throws.</summary>
        [Fact]
        public void AnimationModeRevertsWhatItSampled()
        {
            var go = Keep(new GameObject("Rig"));
            var child = new GameObject("Child").transform;
            child.SetParent(go.transform, false);
            child.localPosition = new Vector3(5, 6, 7);
            var driver = Keep(ScriptableObject.CreateInstance<AnimationModeDriver>());
            var other = Keep(ScriptableObject.CreateInstance<AnimationModeDriver>());
            var clip = Clip("Walk", 1f);
            Assert.Throws<System.InvalidOperationException>(() => AnimationMode.SampleAnimationClip(go, clip, 0.5f));
            AnimationMode.StartAnimationMode(driver);
            try
            {
                AnimationMode.SampleAnimationClip(go, clip, 0.5f);
                Assert.Equal(0.5f, child.localPosition.x, 5);
                Assert.True(AnimationMode.IsPropertyAnimated(child, "m_LocalPosition"));
                Assert.False(AnimationMode.IsPropertyAnimated(child, "m_LocalScale.x"));
                AnimationMode.StopAnimationMode(other);
                Assert.True(AnimationMode.InAnimationMode());
            }
            finally
            {
                AnimationMode.StopAnimationMode(driver);
            }
            Assert.False(AnimationMode.InAnimationMode());
            Assert.Equal(new Vector3(5, 6, 7), child.localPosition);
        }

        /// <summary>bounds.line.*: the box of the positions in the line's own space grown by the
        /// width multiplier, the other space's bounds that box transformed; one position gives an
        /// empty box.</summary>
        [Fact]
        public void LineBoundsAreBoxedInTheLinesOwnSpace()
        {
            var holder = Keep(new GameObject("Lines"));
            holder.transform.position = new Vector3(1, 2, 3);
            holder.transform.rotation = Quaternion.Euler(0, 30, 0);
            holder.transform.localScale = new Vector3(2, 1, 1);
            LineRenderer Line(bool world, int count)
            {
                var go = new GameObject("Line");
                go.transform.SetParent(holder.transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = world;
                line.widthMultiplier = 0.5f;
                line.positionCount = count;
                if (count == 3) line.SetPositions(new[] { new Vector3(0, 0, 0), new Vector3(1, 2, 0), new Vector3(3, -1, 1) });
                return line;
            }
            var local = Line(false, 3);
            Assert.Equal(new Vector3(3.5f, 3.5f, 1.5f), local.localBounds.size);
            Assert.Equal(6.81218f, local.bounds.size.x, 4);
            var world = Line(true, 3);
            Assert.Equal(new Vector3(3.5f, 3.5f, 1.5f), world.bounds.size);
            Assert.Equal(1.89054f, world.localBounds.size.x, 4);
            var single = Line(false, 1);
            Assert.Equal(Vector3.zero, single.bounds.size);
        }
    }

    /// <summary>A behaviour type for the controller tests.</summary>
    public class StateMachineBehaviourProbe : StateMachineBehaviour { }
}
