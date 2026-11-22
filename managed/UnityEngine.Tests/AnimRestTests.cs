using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Unit tests for Q629 animation remainder items (measured in Unity 6000.0.71f1,
    /// tests/projects/animrest and tests/projects/animatorapi): BlendTree motion stats,
    /// Animation.SyncLayer, obsolete Animation.cullingType coercion, m_EditorCurves preservation
    /// and internal AnimationMode.GetCurveBindings, PropertyModificationToEditorCurveBinding,
    /// AnimationMode.AddPropertyModification, GenericBinding / EditorCurveBindingsToGenericBindings,
    /// and AnimatorControllerPlayable.IsParameterControlledByCurve across controller swaps.
    /// Captures all warnings/errors through <see cref="Debug.unityLogger"/>.
    /// </summary>
    [Collection("Animation")]
    public class AnimRestTests : IDisposable
    {
        readonly List<string> m_Logs = new List<string>();
        readonly List<string> m_Expected = new List<string>();
        readonly List<Object> m_Created = new List<Object>();
        readonly List<PlayableGraph> m_Graphs = new List<PlayableGraph>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;
        readonly bool m_PreviousLogEnabled;
        readonly LogType m_PreviousFilter;

        public AnimRestTests()
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
            internal CaptureHandler(ILogHandler inner, List<string> logs) { m_Inner = inner; m_Logs = logs; }
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
            public void LogException(Exception exception, Object context)
            {
                if (exception != null) m_Logs.Add($"Exception: {exception}");
                m_Inner.LogException(exception, context);
            }
        }

        public void Dispose()
        {
            try
            {
                while (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
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

        T Keep<T>(T o) where T : Object { m_Created.Add(o); return o; }

        PlayableGraph Graph(string name) { var g = PlayableGraph.Create(name); m_Graphs.Add(g); return g; }

        enum TestMode { Idle = 0, Run = 1 }

        sealed class TestBehaviour : MonoBehaviour
        {
            public float amount = 1f;
            public int steps = 2;
            public bool flag;
            public Vector3 offset;
            public TestMode mode = TestMode.Idle;
            [DiscreteEvaluation] public int counted = 3;
            public Object reference;
        }

        // ------------------------------------------------------------------ 1. BlendTree motion stats

        AnimationClip MakeRootClip(string name, Vector3 deltaPos, float yawRad, float duration)
        {
            var clip = Keep(new AnimationClip { name = name, frameRate = 60f });
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.x"), AnimationCurve.Linear(0f, 0f, duration, deltaPos.x));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y"), AnimationCurve.Linear(0f, 0f, duration, deltaPos.y));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.z"), AnimationCurve.Linear(0f, 0f, duration, deltaPos.z));
            float half = yawRad * 0.5f;
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.x"), AnimationCurve.Constant(0f, duration, 0f));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.y"), AnimationCurve.Linear(0f, 0f, duration, MathF.Sin(half)));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.z"), AnimationCurve.Constant(0f, duration, 0f));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootQ.w"), AnimationCurve.Linear(0f, 1f, duration, MathF.Cos(half)));
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0f;
            settings.stopTime = duration;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        [Fact]
        public void BlendTreeStatsAverageFlattenedNonLegacyClipsAndSkipCyclesAndLegacy()
        {
            var empty = Keep(new BlendTree { name = "Empty" });
            Assert.Equal(Vector3.zero, empty.averageSpeed);
            Assert.Equal(0f, empty.averageAngularSpeed);
            Assert.Equal(0f, empty.apparentSpeed);
            Assert.Equal(0f, empty.averageDuration);

            // Two clips with non-collinear speeds so arithmetic mean of apparentSpeed differs
            // from Motion.ApparentSpeed(meanSpeed, meanAngular).
            var straight = MakeRootClip("Straight", new Vector3(0f, 0f, 2f), 0f, 1f);
            var turn = MakeRootClip("Turn", new Vector3(4f, 0f, 0f), MathF.PI * 0.5f, 2f);
            var legacyClip = MakeRootClip("Legacy", new Vector3(100f, 0f, 0f), 0f, 5f);
            legacyClip.legacy = true;

            var inner = Keep(new BlendTree { name = "Inner" });
            inner.AddChild(turn);
            inner.AddChild(null);
            inner.AddChild(legacyClip);

            var outer = Keep(new BlendTree { name = "Outer" });
            outer.AddChild(straight);
            outer.AddChild(inner);
            // Cycle guard: inner references outer.
            inner.AddChild(outer);

            var expectedSpeed = (straight.averageSpeed + turn.averageSpeed) * 0.5f;
            float expectedAng = (straight.averageAngularSpeed + turn.averageAngularSpeed) * 0.5f;
            float expectedApp = (straight.apparentSpeed + turn.apparentSpeed) * 0.5f;
            float recomputedApp = Motion.ApparentSpeed(expectedSpeed.x, expectedSpeed.y, expectedSpeed.z, expectedAng);
            Assert.True(MathF.Abs(expectedApp - recomputedApp) > 0.05f, "diverging control: mean apparentSpeed must differ from ApparentSpeed(mean)");

            Near.Equal(expectedSpeed.x, outer.averageSpeed.x, 1e-5f);
            Near.Equal(expectedSpeed.y, outer.averageSpeed.y, 1e-5f);
            Near.Equal(expectedSpeed.z, outer.averageSpeed.z, 1e-5f);
            Near.Equal(expectedAng, outer.averageAngularSpeed, 1e-5f);
            Near.Equal(expectedApp, outer.apparentSpeed, 1e-5f);
            Near.Equal(1.5f, outer.averageDuration, 1e-5f);
        }

        // ------------------------------------------------------------------ 2. Animation.cullingType

        [Fact]
        public void ObsoleteAnimationCullingTypesCoerceToBasedOnRenderersOnSetAndDeserialize()
        {
            var go = Keep(new GameObject("Cull"));
            var anim = go.AddComponent<Animation>();
            Assert.Equal(AnimationCullingType.AlwaysAnimate, anim.cullingType);

#pragma warning disable CS0618
            anim.cullingType = AnimationCullingType.BasedOnClipBounds;
            Assert.Equal(AnimationCullingType.BasedOnRenderers, anim.cullingType);
            anim.cullingType = AnimationCullingType.AlwaysAnimate;
            anim.cullingType = AnimationCullingType.BasedOnUserBounds;
            Assert.Equal(AnimationCullingType.BasedOnRenderers, anim.cullingType);
#pragma warning restore CS0618

            // YAML deserialization path: m_CullingType: 2 (BasedOnClipBounds) -> BasedOnRenderers.
            var yaml = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1 &1\nGameObject:\n  m_Name: YamlCull\n  m_IsActive: 1\n--- !u!4 &4\nTransform:\n  m_GameObject: {fileID: 1}\n--- !u!111 &111\nAnimation:\n  m_GameObject: {fileID: 1}\n  m_Enabled: 1\n  m_CullingType: 2\n";
            var loaded = ObjectGraphBuilder.Build(UnityYaml.Parse(yaml), "test.unity", null, ObjectGraphBuilder.Kind.Scene, null);
            var loadedAnim = (Animation)loaded.Objects[111];
            Keep((GameObject)loaded.Objects[1]);
            Assert.Equal(AnimationCullingType.BasedOnRenderers, loadedAnim.cullingType);
        }

        // ------------------------------------------------------------------ 3. Animation.SyncLayer

        [Fact]
        public void SyncLayerNegativeWeightClampedAndMultipleLayersIndependent()
        {
            var clip1 = Keep(new AnimationClip { legacy = true, wrapMode = WrapMode.Loop, name = "S1" });
            clip1.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            var clip2 = Keep(new AnimationClip { legacy = true, wrapMode = WrapMode.Loop, name = "S2" });
            clip2.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0f, 0f, 2f, 2f));
            var clipOther = Keep(new AnimationClip { legacy = true, wrapMode = WrapMode.Loop, name = "Other" });
            clipOther.SetCurve("", typeof(Transform), "localPosition.z", AnimationCurve.Linear(0f, 0f, 2f, 2f));

            var go = Keep(new GameObject("SyncEdge"));
            var anim = go.AddComponent<Animation>();
            anim.AddClip(clip1, "S1");
            anim.AddClip(clip2, "S2");
            anim.AddClip(clipOther, "Other");
            var s1 = anim["S1"]; s1.layer = 0; s1.weight = -1f; s1.speed = 5f; s1.time = 0.2f; s1.enabled = true;
            var s2 = anim["S2"]; s2.layer = 0; s2.weight = 1f; s2.speed = 2f; s2.time = 1.0f; s2.enabled = true;
            var so = anim["Other"]; so.layer = 1; so.weight = 1f; so.speed = 2f; so.time = 0.5f; so.enabled = true;
            anim.SyncLayer(0);

            anim.Step(0f);
            anim.Step(0.25f);
            // Negative weight clamps to 0, so S2 dominates: avg ntime = 0.5, avg nspeed = 1.0 -> 0.75.
            Near.Equal(0.75f, s1.normalizedTime, 1e-4f);
            Near.Equal(0.75f, s2.normalizedTime, 1e-4f);
            Near.Equal(0.5f, so.normalizedTime, 1e-4f); // layer 1 unaffected: 0.25 + 0.25*1 = 0.5
        }

        /// <summary>A GameObject's name recorded as a property modification goes back to the
        /// modification's text when animation mode stops (tests/projects/animrest propmod.extra.str.*).</summary>
        [Fact]
        public void AModifiedGameObjectNameRevertsToTheModificationsText()
        {
            var go = Keep(new GameObject("OrigName"));
            var driver = Keep(ScriptableObject.CreateInstance<AnimationModeDriver>());
            AnimationMode.StartAnimationMode(driver);
            AnimationMode.AddPropertyModification(
                EditorCurveBinding.FloatCurve("", typeof(GameObject), "m_Name"),
                new PropertyModification { target = go, propertyPath = "m_Name", value = "RecordedName" }, true);
            Assert.True(AnimationMode.IsPropertyAnimated(go, "m_Name"));
            go.name = "ChangedName";
            AnimationMode.StopAnimationMode(driver);
            Assert.Equal("RecordedName", go.name);
            // Control: a name that was not recorded is left alone.
            var other = Keep(new GameObject("Other"));
            AnimationMode.StartAnimationMode(driver);
            other.name = "Renamed";
            AnimationMode.StopAnimationMode(driver);
            Assert.Equal("Renamed", other.name);
        }

        /// <summary>A state's first step advances it by the clock time since Play, at most the
        /// step (tests/projects/animrest playtiming.*): nothing at the time it began, the whole
        /// step a frame later, and the part between for a fixed-step Animation begun in Update,
        /// where the frame's time is ahead of the fixed time.</summary>
        [Fact]
        public void FirstStepOfAPlayedStateAdvancesByTheClockTimeSincePlay()
        {
            double savedTime = Time.s_Time, savedFixed = Time.s_FixedTime;
            try
            {
                var clip = Keep(new AnimationClip { legacy = true, wrapMode = WrapMode.Loop, name = "C" });
                clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 2f, 2f));
                Animation Make(string name, bool fixedStep)
                {
                    var anim = Keep(new GameObject(name)).AddComponent<Animation>();
                    anim.playAutomatically = false;
                    anim.cullingType = AnimationCullingType.AlwaysAnimate;
                    anim.updateMode = fixedStep ? AnimationUpdateMode.Fixed : AnimationUpdateMode.Normal;
                    anim.AddClip(clip, "C");
                    return anim;
                }

                // Frame time 10.01, fixed time 10.00: Play in Update.
                Time.s_Time = 10.01;
                Time.s_FixedTime = 10.0;
                var inUpdate = Make("Update", false);
                var fixedInUpdate = Make("FixedUpdate", true);
                var later = Make("Later", false);
                inUpdate.Play("C");
                fixedInUpdate.Play("C");
                later.Play("C");

                // The frame's own update: no time has passed for the frame-stepped state.
                inUpdate.Step(0.03f);
                Near.Equal(0f, inUpdate["C"].time, 1e-6f);

                // Fixed steps to 10.02 and 10.04: the first advances by 10.02 - 10.01.
                Time.s_FixedTime = 10.02;
                fixedInUpdate.Step(0.02f);
                Near.Equal(0.01f, fixedInUpdate["C"].time, 1e-5f);
                Time.s_FixedTime = 10.04;
                fixedInUpdate.Step(0.02f);
                Near.Equal(0.03f, fixedInUpdate["C"].time, 1e-5f);

                // Play inside a fixed step (the time read there is the fixed time, 10.04), stepped
                // by the frame at 10.05: the frame-stepped state advances by the 0.01 between.
                Time.s_InFixedTimeStep = true;
                var inFixedStep = Make("PlayedInFixedStep", false);
                inFixedStep.Play("C");
                Time.s_InFixedTimeStep = false;
                Time.s_Time = 10.05;
                inFixedStep.Step(0.03f);
                Near.Equal(0.01f, inFixedStep["C"].time, 1e-5f);
                Time.s_Time = 10.01;

                // The next frame: a state first stepped a frame after Play advances the whole
                // step, never more than the step even when more time has passed.
                Time.s_Time = 10.04;
                inUpdate.Step(0.03f);
                Near.Equal(0.03f, inUpdate["C"].time, 1e-6f);
                later.Step(0.03f);
                Near.Equal(0.03f, later["C"].time, 1e-6f);
                Time.s_Time = 11.0;
                var late = Make("Late", false);
                late.Play("C");
                Time.s_Time = 12.0;
                late.Step(0.03f);
                Near.Equal(0.03f, late["C"].time, 1e-6f);
            }
            finally
            {
                Time.s_InFixedTimeStep = false;
                Time.s_Time = savedTime;
                Time.s_FixedTime = savedFixed;
            }
        }

        // ------------------------------------------------------------------ 4. EditorCurves & AnimationMode.GetCurveBindings

        [Fact]
        public void EditorCurvesOrderAndAnimationModeGetCurveBindingsMatchMeasuredRules()
        {
            var clip = Keep(new AnimationClip { name = "OrderClip" });
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Z", typeof(Transform), "localEulerAnglesRaw.y"), AnimationCurve.Linear(0f, 0f, 1f, 90f));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("A", typeof(Transform), "m_LocalRotation.w"), AnimationCurve.Constant(0f, 1f, 1f));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(TestBehaviour), "amount"), AnimationCurve.Linear(0f, 0f, 1f, 1f));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("B", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Linear(0f, 0f, 1f, 2f));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("B", typeof(Transform), "m_LocalScale.z"), AnimationCurve.Linear(0f, 1f, 1f, 2f));
            AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(TestBehaviour), "reference"),
                new[] { new ObjectReferenceKeyframe { time = 0f, value = clip } });

            // GetCurveBindings preserves exact authored order.
            var util = AnimationUtility.GetCurveBindings(clip);
            Assert.Equal(
                new[] { "Z:localEulerAnglesRaw.y", "A:m_LocalRotation.w", ":amount", "B:m_LocalPosition.x", "B:m_LocalScale.z" },
                util.Select(b => $"{b.path}:{b.propertyName}").ToArray());

            // AnimationMode.GetCurveBindings emits non-Transform floats, object curves, then
            // expanded Transform rotation (ordinal path A without Euler hints, Z with Euler hints),
            // position, and scale.
            var mode = AnimationMode.GetCurveBindings(clip);
            var expectedMode = new[]
            {
                ":amount",
                ":reference",
                "A:m_LocalRotation.x", "A:m_LocalRotation.y", "A:m_LocalRotation.z", "A:m_LocalRotation.w",
                "Z:m_LocalRotation.x", "Z:m_LocalRotation.y", "Z:m_LocalRotation.z", "Z:m_LocalRotation.w",
                "Z:m_LocalEulerAnglesHint.x", "Z:m_LocalEulerAnglesHint.y", "Z:m_LocalEulerAnglesHint.z",
                "B:m_LocalPosition.x", "B:m_LocalPosition.y", "B:m_LocalPosition.z",
                "B:m_LocalScale.x", "B:m_LocalScale.y", "B:m_LocalScale.z",
            };
            Assert.Equal(expectedMode, mode.Select(b => $"{b.path}:{b.propertyName}").ToArray());

            // Removing the last Euler axis cleans up the vector fillers too.
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Z", typeof(Transform), "localEulerAnglesRaw.y"), null);
            Assert.DoesNotContain(clip.m_Curves, c => c.Binding.Path == "Z");
            Assert.DoesNotContain(AnimationMode.GetCurveBindings(clip), b => b.path == "Z");

            // Null checks throw ArgumentNullException.
            Assert.Throws<ArgumentNullException>(() => AnimationUtility.GetCurveBindings(null));
            Assert.Throws<ArgumentNullException>(() => AnimationMode.GetCurveBindings(null));
        }

        [Fact]
        public void EnsureQuaternionContinuityUpdatesEditorCurvesSeenByGetEditorCurve()
        {
            var clip = Keep(new AnimationClip { name = "QuatEditor" });
            float h = 30f * Mathf.Deg2Rad;
            var q1 = new Quaternion(0f, 0f, MathF.Sin(h), MathF.Cos(h));
            // Store key 1 in the opposite hemisphere via SetEditorCurve.
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalRotation.x"),
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, -q1.x)));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalRotation.y"),
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, -q1.y)));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalRotation.z"),
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, -q1.z)));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalRotation.w"),
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, -q1.w)));

            clip.EnsureQuaternionContinuity();

            var zCurve = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalRotation.z"));
            var wCurve = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalRotation.w"));
            Assert.NotNull(zCurve);
            Assert.NotNull(wCurve);
            Near.Equal(q1.z, zCurve[1].value, 1e-5f);
            Near.Equal(q1.w, wCurve[1].value, 1e-5f);
        }

        // ------------------------------------------------------------------ 5. PropertyModification & AddPropertyModification

        [Fact]
        public void PropertyModificationConversionCoversComponentsDiscreteAndEdgeCases()
        {
            var root = Keep(new GameObject("Root"));
            var behaviour = root.AddComponent<TestBehaviour>();
            var child = new GameObject("Child");
            child.transform.SetParent(root.transform, false);
            var meshRen = child.AddComponent<MeshRenderer>();
            var spriteRen = child.AddComponent<SpriteRenderer>();
            var unrelated = Keep(new GameObject("Unrelated"));
            var destroyed = new GameObject("Destroyed");
            destroyed.transform.SetParent(root.transform, false);
            Object.DestroyImmediate(destroyed);

            Assert.Throws<ArgumentNullException>(() =>
                AnimationUtility.PropertyModificationToEditorCurveBinding(new PropertyModification { target = behaviour, propertyPath = "amount" }, null, out _));

            // Null mod / null target / destroyed target / unrelated target -> null type, Object binding.
            Assert.Null(AnimationUtility.PropertyModificationToEditorCurveBinding(null, root, out var bNull));
            Assert.Equal(typeof(Object), bNull.type);
            Assert.Null(AnimationUtility.PropertyModificationToEditorCurveBinding(new PropertyModification { target = null, propertyPath = "amount" }, root, out _));
            Assert.Null(AnimationUtility.PropertyModificationToEditorCurveBinding(new PropertyModification { target = destroyed, propertyPath = "m_IsActive" }, root, out _));
            Assert.Null(AnimationUtility.PropertyModificationToEditorCurveBinding(new PropertyModification { target = unrelated.transform, propertyPath = "m_LocalPosition.x" }, root, out _));

            // Transform valid vs invalid (euler/hint rejected).
            Assert.Equal(typeof(float), AnimationUtility.PropertyModificationToEditorCurveBinding(new PropertyModification { target = child.transform, propertyPath = "m_LocalRotation.w" }, root, out var bRot));
            Assert.Equal("Child", bRot.path);
            Assert.Equal("m_LocalRotation.w", bRot.propertyName);
            Assert.Null(AnimationUtility.PropertyModificationToEditorCurveBinding(new PropertyModification { target = child.transform, propertyPath = "localEulerAnglesRaw.y" }, root, out _));
            Assert.Null(AnimationUtility.PropertyModificationToEditorCurveBinding(new PropertyModification { target = child.transform, propertyPath = "m_LocalEulerAnglesHint.x" }, root, out _));

            // GameObject, MeshRenderer (m_Enabled + material slot), SpriteRenderer.
            Assert.Equal(typeof(bool), AnimationUtility.PropertyModificationToEditorCurveBinding(new PropertyModification { target = child, propertyPath = "m_IsActive" }, root, out _));
            Assert.Equal(typeof(bool), AnimationUtility.PropertyModificationToEditorCurveBinding(new PropertyModification { target = meshRen, propertyPath = "m_Enabled" }, root, out var bRenEn));
            Assert.Equal(typeof(MeshRenderer), bRenEn.type);
            Assert.Equal(typeof(Material), AnimationUtility.PropertyModificationToEditorCurveBinding(new PropertyModification { target = meshRen, propertyPath = "m_Materials.Array.data[0]" }, root, out var bMat));
            Assert.True(bMat.isPPtrCurve);
            Assert.Equal(typeof(Sprite), AnimationUtility.PropertyModificationToEditorCurveBinding(new PropertyModification { target = spriteRen, propertyPath = "m_Sprite" }, root, out var bSpr));
            Assert.True(bSpr.isPPtrCurve);

            // MonoBehaviour: float, int, bool, enum (discrete), [DiscreteEvaluation] (discrete), Object (null), unknown (null).
            Assert.Equal(typeof(float), AnimationUtility.PropertyModificationToEditorCurveBinding(new PropertyModification { target = behaviour, propertyPath = "amount" }, root, out var bFloat));
            Assert.False(bFloat.isDiscreteCurve);
            Assert.Equal(typeof(int), AnimationUtility.PropertyModificationToEditorCurveBinding(new PropertyModification { target = behaviour, propertyPath = "steps" }, root, out var bInt));
            Assert.False(bInt.isDiscreteCurve);
            Assert.Equal(typeof(int), AnimationUtility.PropertyModificationToEditorCurveBinding(new PropertyModification { target = behaviour, propertyPath = "mode" }, root, out var bEnum));
            Assert.True(bEnum.isDiscreteCurve);
            Assert.Equal(typeof(int), AnimationUtility.PropertyModificationToEditorCurveBinding(new PropertyModification { target = behaviour, propertyPath = "counted" }, root, out var bDisc));
            Assert.True(bDisc.isDiscreteCurve);
            Assert.Null(AnimationUtility.PropertyModificationToEditorCurveBinding(new PropertyModification { target = behaviour, propertyPath = "reference" }, root, out _));
            Assert.Null(AnimationUtility.PropertyModificationToEditorCurveBinding(new PropertyModification { target = behaviour, propertyPath = "doesNotExist" }, root, out _));
        }

        [Fact]
        public void AddPropertyModificationValidatesRevertsDivergingDuplicateNullObjectAndDestroyedTargets()
        {
            var root = Keep(new GameObject("ModRoot"));
            var behaviour = root.AddComponent<TestBehaviour>();
            var child = new GameObject("Child");
            child.transform.SetParent(root.transform, false);
            var ren = child.AddComponent<MeshRenderer>();
            var ephemeral = new GameObject("Ephemeral");
            var ephemeralB = ephemeral.AddComponent<TestBehaviour>();
            var clipA = Keep(new AnimationClip { name = "ClipA" });
            var clipB = Keep(new AnimationClip { name = "ClipB" });

            // Outside animation mode throws exact InvalidOperationException.
            var outside = Assert.Throws<InvalidOperationException>(() =>
                AnimationMode.AddPropertyModification(default, new PropertyModification { target = behaviour, propertyPath = "steps", value = "2" }, true));
            Assert.Contains("AnimationMode.AddPropertyModification may only be called in animation mode", outside.Message);

            var driver = Keep(ScriptableObject.CreateInstance<AnimationModeDriver>());
            AnimationMode.StartAnimationMode(driver);

            // Null modification / null target throw NullReferenceException with "(null)".
            var nre1 = Assert.Throws<NullReferenceException>(() => AnimationMode.AddPropertyModification(default, null, true));
            Assert.Equal("(null)", nre1.Message);
            var nre2 = Assert.Throws<NullReferenceException>(() => AnimationMode.AddPropertyModification(default, new PropertyModification { target = null, propertyPath = "x", value = "1" }, true));
            Assert.Equal("(null)", nre2.Message);

            // Diverging control: current steps is 5, modification says "2", mutate to 9 -> Stop reverts to 2.
            behaviour.steps = 5;
            AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve("", typeof(TestBehaviour), "steps"),
                new PropertyModification { target = behaviour, propertyPath = "steps", value = "2" }, true);
            behaviour.steps = 9;

            // Duplicate registration: first ("10") wins over second ("20").
            AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve("", typeof(TestBehaviour), "amount"),
                new PropertyModification { target = behaviour, propertyPath = "amount", value = "10" }, true);
            AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve("", typeof(TestBehaviour), "amount"),
                new PropertyModification { target = behaviour, propertyPath = "amount", value = "20" }, true);
            behaviour.amount = 99f;

            // Renderer m_Enabled revert.
            ren.enabled = true;
            AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve("Child", typeof(MeshRenderer), "m_Enabled"),
                new PropertyModification { target = ren, propertyPath = "m_Enabled", value = "0" }, true);
            ren.enabled = true;

            // ObjectReference revert to null even when binding is default.
            behaviour.reference = clipA;
            AnimationMode.AddPropertyModification(default,
                new PropertyModification { target = behaviour, propertyPath = "reference", objectReference = null }, true);
            behaviour.reference = clipB;

            // Destroyed target before Stop does not throw.
            AnimationMode.AddPropertyModification(default,
                new PropertyModification { target = ephemeralB, propertyPath = "steps", value = "7" }, true);
            Object.DestroyImmediate(ephemeral);

            AnimationMode.StopAnimationMode(driver);

            Assert.Equal(2, behaviour.steps);
            Near.Equal(10f, behaviour.amount, 1e-5f);
            Assert.False(ren.enabled);
            Assert.Null(behaviour.reference);
        }

        // ------------------------------------------------------------------ 6. GenericBinding

        [Fact]
        public void EditorCurveBindingsToGenericBindingsComputesHashesFlagsAndSkipsTransform()
        {
            Assert.Empty(AnimationUtility.EditorCurveBindingsToGenericBindings(null));
            Assert.Empty(AnimationUtility.EditorCurveBindingsToGenericBindings(Array.Empty<EditorCurveBinding>()));

            var bindings = new[]
            {
                EditorCurveBinding.FloatCurve("Child", typeof(Transform), "m_LocalPosition.x"),
                EditorCurveBinding.FloatCurve("Child2", typeof(GameObject), "m_IsActive"),
                EditorCurveBinding.DiscreteCurve("", typeof(TestBehaviour), "mode"),
                EditorCurveBinding.PPtrCurve("Child", typeof(MeshRenderer), "m_Materials.Array.data[2]"),
                EditorCurveBinding.FloatCurve("", typeof(Animator), "Speed"),
                new EditorCurveBinding { path = "EmptyType", type = null, propertyName = "prop" },
                EditorCurveBinding.PPtrCurve("Child", typeof(SpriteRenderer), "m_Sprite"),
                EditorCurveBinding.FloatCurve("Child", typeof(MeshRenderer), "material._Color.r"),
                EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Smile"),
                EditorCurveBinding.FloatCurve("UI", typeof(RectTransform), "m_AnchoredPosition.x"),
                EditorCurveBinding.FloatCurve("", typeof(Camera), "field of view"),
                EditorCurveBinding.FloatCurve("", typeof(Light), "m_Range"),
            };
            var generic = AnimationUtility.EditorCurveBindingsToGenericBindings(bindings);
            Assert.Equal(11, generic.Length); // Transform skipped

            // GameObject m_IsActive
            Assert.Equal(1, generic[0].typeID);
            Assert.Equal(0, generic[0].customTypeID);
            Assert.Equal(1609142275u, generic[0].transformPathHash);
            Assert.Equal(2086281974u, generic[0].propertyNameHash);
            Assert.False(generic[0].isDiscrete);
            Assert.False(generic[0].isObjectReference);

            // MonoBehaviour discrete enum
            Assert.Equal(114, generic[1].typeID);
            Assert.True(generic[1].isDiscrete);
            Assert.False(generic[1].isObjectReference);

            // MeshRenderer material slot 2
            Assert.Equal(23, generic[2].typeID);
            Assert.Equal(21, generic[2].customTypeID);
            Assert.Equal(2u, generic[2].propertyNameHash);
            Assert.True(generic[2].isObjectReference);
            Assert.True(generic[2].isDiscrete);

            // Animator parameter
            Assert.Equal(95, generic[3].typeID);
            Assert.Equal(3471299058u, generic[3].propertyNameHash);

            // Null type handled cleanly without throwing
            Assert.Equal(0, generic[4].typeID);
            Assert.Equal(0, generic[4].scriptInstanceID);

            // Shared customType / attribute encodings (measured in scenesave clip-assets)
            Assert.Equal(23, generic[5].customTypeID);
            Assert.Equal(0u, generic[5].propertyNameHash);
            Assert.True(generic[5].isObjectReference);

            Assert.Equal(22, generic[6].customTypeID);
            Assert.Equal(AnimationClipFiles.MaterialAttribute("_Color.r"), generic[6].propertyNameHash);

            Assert.Equal(20, generic[7].customTypeID);
            Assert.Equal(HashedBindings.Crc32("Smile"), generic[7].propertyNameHash);

            Assert.Equal(28, generic[8].customTypeID);
            Assert.Equal(37, generic[9].customTypeID);
            Assert.Equal(25, generic[10].customTypeID);
        }

        // ------------------------------------------------------------------ 7. PlayableCurve after controller swap

        [Fact]
        public void PlayableIsParameterControlledByCurveSurvivesAnimatorControllerSwap()
        {
            var c1 = Keep(new AnimatorController { name = "C1" });
            c1.AddLayer("Base Layer");
            c1.AddParameter("CurveParam", AnimatorControllerParameterType.Float);
            c1.AddParameter("PlainParam", AnimatorControllerParameterType.Float);
            var clip = Keep(new AnimationClip { name = "CurveClip" });
            clip.SetCurve("", typeof(Animator), "CurveParam", AnimationCurve.Linear(0f, 0f, 1f, 5f));
            c1.AddMotion(clip);

            var c2 = Keep(new AnimatorController { name = "C2" });
            c2.AddLayer("Base Layer");
            c2.AddParameter("CurveParam", AnimatorControllerParameterType.Float);

            var go = Keep(new GameObject("SwapRig"));
            var animator = go.AddComponent<Animator>();

            var g = Graph("SwapGraph");
            g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimatorControllerPlayable.Create(g, c1);
            var output = AnimationPlayableOutput.Create(g, "Out", animator);
            output.SetSourcePlayable(playable);

            // Before evaluation: false (and unknown parameter logs missing warning).
            Assert.False(playable.IsParameterControlledByCurve("CurveParam"));
            m_Expected.Add("Parameter 'Missing' does not exist.");
            Assert.False(playable.IsParameterControlledByCurve("Missing"));
            Assert.Contains(m_Logs, l => l.Contains("Parameter 'Missing' does not exist."));

            g.Play();
            g.Evaluate(0.1f);
            Assert.True(playable.IsParameterControlledByCurve("CurveParam"));
            Assert.False(playable.IsParameterControlledByCurve("PlainParam"));
            Near.Equal(0.5f, playable.GetFloat("CurveParam"), 1e-4f);

            // Swap the Animator's own controller while the graph is alive.
            animator.runtimeAnimatorController = c2;
            g.Evaluate(0.1f);
            Assert.True(playable.IsParameterControlledByCurve("CurveParam"));
            Assert.False(playable.IsParameterControlledByCurve("PlainParam"));
            Near.Equal(1.0f, playable.GetFloat("CurveParam"), 1e-4f);

            // A second output targeting a different Animator must not hijack the playable's bindings.
            var otherGo = Keep(new GameObject("OtherAnimator"));
            var otherAnimator = otherGo.AddComponent<Animator>();
            var secondOutput = AnimationPlayableOutput.Create(g, "Out2", otherAnimator);
            secondOutput.SetSourcePlayable(playable);
            g.Evaluate(0.1f);
            Assert.True(playable.IsParameterControlledByCurve("CurveParam"));
            Near.Equal(1.5f, playable.GetFloat("CurveParam"), 1e-4f);
        }

        [Fact]
        public void PlayableRebindRefreshesParameterMapAndMasksWhenControllerSwapShiftsChannelIndices()
        {
            // Playable controller: drives CurveParam from 0 -> 5 over 1s and TestBehaviour.amount -> 9.
            var playableCtrl = Keep(new AnimatorController { name = "PlayableCtrl" });
            playableCtrl.AddLayer("Base Layer");
            playableCtrl.AddParameter("CurveParam", AnimatorControllerParameterType.Float);
            var pClip = Keep(new AnimationClip { name = "PClip" });
            pClip.SetCurve("", typeof(Animator), "CurveParam", AnimationCurve.Linear(0f, 0f, 1f, 5f));
            pClip.SetCurve("", typeof(TestBehaviour), "amount", AnimationCurve.Constant(0f, 1f, 9f));
            playableCtrl.AddMotion(pClip);

            // Initial own controller (1 clip, 1 float channel: steps=42) -> ClipEpoch = 2 after playable binds.
            var ctrlA = Keep(new AnimatorController { name = "CtrlA" });
            ctrlA.AddLayer("Base Layer");
            var clipA = Keep(new AnimationClip { name = "ClipA" });
            clipA.SetCurve("", typeof(TestBehaviour), "steps", AnimationCurve.Constant(0f, 1f, 42f));
            ctrlA.AddMotion(clipA);

            // Swapped own controller (1 clip, 3 float channels: localPosition=77) -> same ClipEpoch = 2,
            // but CurveParam's channel index shifts from 1 to 3!
            var ctrlB = Keep(new AnimatorController { name = "CtrlB" });
            ctrlB.AddLayer("Base Layer");
            var clipB = Keep(new AnimationClip { name = "ClipB" });
            clipB.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Constant(0f, 1f, 77f));
            ctrlB.AddMotion(clipB);

            // Second swap: same channel count as ctrlA (1 float channel: flag=1) in a different order.
            var ctrlC = Keep(new AnimatorController { name = "CtrlC" });
            ctrlC.AddLayer("Base Layer");
            var clipC = Keep(new AnimationClip { name = "ClipC" });
            clipC.SetCurve("", typeof(TestBehaviour), "flag", AnimationCurve.Constant(0f, 1f, 1f));
            ctrlC.AddMotion(clipC);

            var go = Keep(new GameObject("ShiftRig"));
            var behaviour = go.AddComponent<TestBehaviour>();
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = ctrlA;

            var g = Graph("ShiftGraph");
            g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimatorControllerPlayable.Create(g, playableCtrl);
            var output = AnimationPlayableOutput.Create(g, "Out", animator);
            output.SetSourcePlayable(playable);

            g.Play();
            g.Evaluate(0.1f);
            Near.Equal(0.5f, playable.GetFloat("CurveParam"), 1e-4f);
            Near.Equal(9f, behaviour.amount, 1e-4f);

            // Swap to ctrlB (same ClipEpoch, shifted channel index): must read CurveParam (1.0), not 77!
            animator.runtimeAnimatorController = ctrlB;
            g.Evaluate(0.1f);
            Assert.True(playable.IsParameterControlledByCurve("CurveParam"));
            Near.Equal(1.0f, playable.GetFloat("CurveParam"), 1e-4f);
            Near.Equal(9f, behaviour.amount, 1e-4f);

            // Swap to ctrlC (same channel count as ctrlA, different property order): masks/channels must refresh.
            behaviour.amount = -1f;
            animator.runtimeAnimatorController = ctrlC;
            g.Evaluate(0.1f);
            Assert.True(playable.IsParameterControlledByCurve("CurveParam"));
            Near.Equal(1.5f, playable.GetFloat("CurveParam"), 1e-4f);
            Near.Equal(9f, behaviour.amount, 1e-4f);
        }
    }
}
