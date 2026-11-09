using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Animation.AddClip(clip, newName, firstFrame, lastFrame, addLoopFrame). Documented (6000.0):
    /// the clip plays only between firstFrame and lastFrame, and addLoopFrame inserts an extra frame
    /// at the end that matches the first frame. The extra frame's position one frame after
    /// lastFrame (1 / frameRate), and the values between lastFrame and it (a line, or a step after
    /// a stepped segment, the shorter way for a quaternion rotation), are Unbound's reading and are
    /// tested as such, not as measured Unity values: the assertions on them are marked "Unbound's
    /// unmeasured choice". Every expected value comes from the source curve itself; the frame
    /// rates are arbitrary and run at two values so that no rate is assumed. Created clips and
    /// GameObjects are destroyed in <see cref="Dispose"/>, which runs when a test fails too.
    /// </summary>
    [Collection("Animation")]
    public class AnimationLoopFrameTests : System.IDisposable
    {
        readonly List<string> m_Logs = new List<string>();
        readonly List<string> m_Expected = new List<string>();
        readonly List<Object> m_Created = new List<Object>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;
        readonly bool m_PreviousLogEnabled;
        readonly LogType m_PreviousFilter;
        const string NotInserted = "the extra loop frame is not inserted";

        public AnimationLoopFrameTests()
        {
            m_PreviousHandler = m_Logger.logHandler;
            m_PreviousLogEnabled = m_Logger.logEnabled;
            m_PreviousFilter = m_Logger.filterLogType;
            // Unbound diagnostics bypass the game's Application log callbacks.
            // Observe them before routing, and forward every call to the original handler.
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

        /// <summary>A smooth curve whose keys fall between frames, so a window's end cuts a segment.</summary>
        static AnimationCurve SmoothCurve() => new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(0.37f, 1.5f, 2f, 2f),
            new Keyframe(0.81f, -0.4f, -1f, -1f),
            new Keyframe(1.3f, 2f, 0f, 0f));

        T Keep<T>(T o) where T : Object
        {
            m_Created.Add(o);
            return o;
        }

        Animation NewAnimation(out GameObject go)
        {
            go = Keep(new GameObject("Legacy"));
            return go.AddComponent<Animation>();
        }

        AnimationClip Source(AnimationCurve curve, float rate, WrapMode wrap = WrapMode.ClampForever)
        {
            var clip = Keep(new AnimationClip { legacy = true, name = "source", frameRate = rate, wrapMode = wrap });
            clip.SetCurve("", typeof(Transform), "localPosition.x", curve);
            return clip;
        }

        /// <summary>The warnings logged by one call.</summary>
        List<string> WarningsOf(System.Action call)
        {
            int before = m_Logs.Count;
            call();
            return m_Logs.GetRange(before, m_Logs.Count - before).FindAll(l => l.StartsWith("Warning:"));
        }

        /// <summary>The state's value at a time on it, through the Animation's own sampling.</summary>
        static float StateValue(Animation animation, string name, float time)
        {
            animation[name].time = time;
            animation.Sample();
            return animation.transform.localPosition.x;
        }

        [Theory]
        [InlineData(24f)]
        [InlineData(30f)]
        public void ExtraFrameMatchesTheFirstSampledFrame(float rate)
        {
            var reference = SmoothCurve();
            var animation = NewAnimation(out var go);
            animation.AddClip(Source(SmoothCurve(), rate), "loop", 0, 10, true);
            var state = animation["loop"];
            Assert.True(animation.Play("loop"));

            // One frame more than frames 0..10.
            Near.Equal(11f / rate, state.length, 1e-6f);
            float first = StateValue(animation, "loop", 0f);
            Near.Equal(reference.Evaluate(0f), first, 1e-5f);
            Near.Equal(first, StateValue(animation, "loop", state.length), 1e-4f);
            // The last frame stays where it was.
            Near.Equal(reference.Evaluate(10f / rate), StateValue(animation, "loop", 10f / rate), 1e-4f);

            // The clip itself samples the same at its end.
            var clip = animation.GetClip("loop");
            clip.SampleAnimation(go, clip.length);
            Near.Equal(first, go.transform.localPosition.x, 1e-4f);
        }

        [Theory]
        [InlineData(24f)]
        [InlineData(30f)]
        public void TrimmedWindowKeepsItsFramesAndClosesOnItsOwnFirstFrame(float rate)
        {
            var reference = SmoothCurve();
            var animation = NewAnimation(out _);
            animation.AddClip(Source(SmoothCurve(), rate), "mid", 6, 18, true);
            var state = animation["mid"];
            Assert.True(animation.Play("mid"));

            Near.Equal(13f / rate, state.length, 1e-6f);
            float first = StateValue(animation, "mid", 0f);
            Near.Equal(reference.Evaluate(6f / rate), first, 1e-5f);
            // Every frame of the window, and times between them (the cut segment keeps its shape).
            for (int k = 0; k <= 12; k++)
            {
                Near.Equal(reference.Evaluate((6 + k) / rate), StateValue(animation, "mid", k / rate), 1e-4f);
                if (k < 12)
                    Near.Equal(reference.Evaluate((6.5f + k) / rate), StateValue(animation, "mid", (k + 0.5f) / rate), 1e-4f);
            }
            // The extra frame is the window's first frame, not the source's frame 19 or frame 0.
            float end = StateValue(animation, "mid", state.length);
            Near.Equal(first, end, 1e-4f);
            Assert.True(System.Math.Abs(reference.Evaluate(19f / rate) - end) > 1e-2f);
        }

        [Theory]
        [InlineData(24f)]
        [InlineData(30f)]
        public void LoopingStateWrapsFromTheExtraFrameToTheFirstFrame(float rate)
        {
            var animation = NewAnimation(out _);
            animation.AddClip(Source(SmoothCurve(), rate, WrapMode.Loop), "mid", 6, 18, true);
            var state = animation["mid"];
            Assert.True(animation.Play("mid"));
            float first = StateValue(animation, "mid", 0f);
            float justBefore = StateValue(animation, "mid", state.length * 0.99999f);
            float justAfter = StateValue(animation, "mid", state.length * 1.00001f);
            Near.Equal(first, justBefore, 1e-3f);
            Near.Equal(first, justAfter, 1e-3f);
        }

        [Theory]
        [InlineData(24f)]
        [InlineData(30f)]
        public void WithoutTheLoopFrameTheWindowIsUnchanged(float rate)
        {
            var reference = SmoothCurve();
            var animation = NewAnimation(out _);
            var source = Source(SmoothCurve(), rate);
            animation.AddClip(source, "four", 6, 18);
            animation.AddClip(source, "five", 6, 18, false);
            animation.AddClip(source, "whole");
            Assert.Same(source, animation.GetClip("whole"));

            foreach (var name in new[] { "four", "five" })
            {
                var state = animation[name];
                Near.Equal(12f / rate, state.length, 1e-6f);
                Assert.True(animation.Play(name));
                Near.Equal(reference.Evaluate(6f / rate), StateValue(animation, name, 0f), 1e-5f);
                Near.Equal(reference.Evaluate(18f / rate), StateValue(animation, name, state.length), 1e-4f);
                Assert.NotSame(source, animation.GetClip(name));
                Assert.Equal(name, animation.GetClip(name).name);
            }
        }

        [Fact]
        public void TheSourceClipIsNotChanged()
        {
            const float rate = 24f;
            var reference = SmoothCurve();
            var animation = NewAnimation(out _);
            var source = Source(SmoothCurve(), rate);
            var keysBefore = source.m_Curves[0].Curve.keys;
            float lengthBefore = source.length;

            animation.AddClip(source, "mid", 6, 18, true);
            var sub = animation.GetClip("mid");
            Assert.NotSame(source, sub);
            Assert.Equal(source.m_Curves.Count, sub.m_Curves.Count);
            Assert.NotSame(source.m_Curves[0].Curve, sub.m_Curves[0].Curve);

            Assert.Equal(lengthBefore, source.length);
            Assert.False(source.m_HasRange);
            Assert.Equal(rate, source.frameRate);
            Assert.Equal(keysBefore, source.m_Curves[0].Curve.keys);
            for (float t = 0f; t <= 1.3f; t += 0.05f)
                Near.Equal(reference.Evaluate(t), source.m_Curves[0].Evaluate(t), 1e-6f);

            // Later edits of the source do not reach the added clip.
            source.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0f, 1f, 7f));
            Assert.True(animation.Play("mid"));
            Near.Equal(reference.Evaluate(6f / rate), StateValue(animation, "mid", 0f), 1e-5f);
        }

        [Theory]
        [InlineData(24f)]
        [InlineData(30f)]
        public void SteppedCurvesStepToTheExtraFrame(float rate)
        {
            var stepped = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, float.PositiveInfinity),
                new Keyframe(0.31f, 1f, float.PositiveInfinity, float.PositiveInfinity),
                new Keyframe(0.56f, 2f, float.PositiveInfinity, float.PositiveInfinity),
                new Keyframe(0.9f, 3f, float.PositiveInfinity, 0f));
            var animation = NewAnimation(out _);
            animation.AddClip(Source(stepped, rate), "steps", 6, 12, true);
            Assert.True(animation.Play("steps"));
            var state = animation["steps"];
            Near.Equal(7f / rate, state.length, 1e-6f);
            float last = stepped.Evaluate(12f / rate);
            Near.Equal(last, StateValue(animation, "steps", 6f / rate));
            // Unbound's unmeasured choice (not a Unity contract): held after a stepped segment, not a line.
            Near.Equal(last, StateValue(animation, "steps", 6.5f / rate));
            Near.Equal(stepped.Evaluate(6f / rate), StateValue(animation, "steps", state.length));
        }

        /// <summary>A window ending inside a segment stepped by a −∞ tangent keeps that step: the
        /// window holds the segment's second key as the source curve does.</summary>
        [Theory]
        [InlineData(24f)]
        [InlineData(30f)]
        public void AWindowCuttingANegativeInfinityStepHoldsTheSecondKey(float rate)
        {
            var stepped = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f),
                new Keyframe(0.1f, 1f, 0f, float.NegativeInfinity),
                new Keyframe(0.9f, 3f, 0f, 0f));
            var animation = NewAnimation(out _);
            animation.AddClip(Source(stepped, rate), "steps", 6, 12, true);
            Assert.True(animation.Play("steps"));
            foreach (float local in new[] { 0f, 2f / rate, 5f / rate })
                Near.Equal(stepped.Evaluate(6f / rate + local), StateValue(animation, "steps", local));
            Near.Equal(3f, StateValue(animation, "steps", 0f));
        }

        [Theory]
        [InlineData(24f)]
        [InlineData(30f)]
        public void ObjectReferenceCurvesGetTheFirstFrameObject(float rate)
        {
            var a = Keep(new AnimationClip { name = "A" });
            var b = Keep(new AnimationClip { name = "B" });
            var c = Keep(new AnimationClip { name = "C" });
            var d = Keep(new AnimationClip { name = "D" });
            var source = Keep(new AnimationClip { legacy = true, name = "source", frameRate = rate });
            source.m_ObjectCurves.Add(new ClipObjectCurve
            {
                Binding = CurveBinding.ForType("", "m_Sprite", typeof(SpriteRenderer)),
                Times = new[] { 0f, 0.31f, 0.56f, 0.9f },
                Values = new Object[] { a, b, c, d },
            });
            var original = source.m_ObjectCurves[0];
            var animation = NewAnimation(out _);
            animation.AddClip(source, "swap", 6, 12, true);
            var sub = animation.GetClip("swap");
            Near.Equal(7f / rate, sub.length, 1e-6f);

            var curve = sub.m_ObjectCurves[0];
            float start = sub.StartTime;
            Assert.Same(original.Evaluate(6f / rate), curve.Evaluate(start));
            for (int k = 0; k <= 6; k++)
                Assert.Same(original.Evaluate((6 + k) / rate), curve.Evaluate(start + k / rate));
            // Unbound's unmeasured choice (not a Unity contract): the last object is held until the extra frame.
            Assert.Same(original.Evaluate(12f / rate), curve.Evaluate(start + 6.5f / rate));
            Assert.Same(original.Evaluate(6f / rate), curve.Evaluate(start + sub.length));
            // The source's curve keeps its keys.
            Assert.Same(original, source.m_ObjectCurves[0]);
            Assert.Equal(4, original.Times.Length);
        }

        [Fact]
        public void AnUnsupportedCurveLeavesTheWindowWithoutTheFrameAndSaysSo()
        {
            const float rate = 24f;
            // A weighted segment across the window's end is not split.
            var weighted = new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f, 0.5f, 0.5f), new Keyframe(1f, 1f, 1f, 1f, 0.5f, 0.5f));
            var animation = NewAnimation(out _);
            m_Expected.Add(NotInserted);
            animation.AddClip(Source(weighted, rate), "weighted", 6, 12, true);
            Assert.Contains(m_Logs, l => l.Contains(NotInserted) && l.Contains("weighted"));
            var sub = animation.GetClip("weighted");
            Near.Equal(6f / rate, sub.length, 1e-6f);
            Assert.True(animation.Play("weighted"));
            Near.Equal(weighted.Evaluate(12f / rate), StateValue(animation, "weighted", sub.length), 1e-4f);
        }

        // ------------------------------------------------------------------ quaternion rotations

        static readonly string[] s_Axes = { "x", "y", "z", "w" };

        /// <summary>A legacy clip turning about z by <paramref name="degreesPerFrame"/> on every frame
        /// 0..<paramref name="frames"/>, keyed as the four quaternion component curves: through
        /// SetCurve (a Transform's m_LocalRotation x, y, z, w), or as curves the clip marks as one
        /// quaternion.</summary>
        AnimationClip Spin(float rate, float degreesPerFrame, int frames, bool marked)
        {
            var keys = new Keyframe[4][];
            for (int a = 0; a < 4; a++) keys[a] = new Keyframe[frames + 1];
            for (int k = 0; k <= frames; k++)
            {
                var q = Quaternion.AngleAxis(degreesPerFrame * k, Vector3.forward);
                float t = k / rate;
                keys[0][k] = new Keyframe(t, q.x);
                keys[1][k] = new Keyframe(t, q.y);
                keys[2][k] = new Keyframe(t, q.z);
                keys[3][k] = new Keyframe(t, q.w);
            }
            var clip = Keep(new AnimationClip { legacy = true, name = "spin", frameRate = rate });
            for (int a = 0; a < 4; a++)
            {
                var curve = new AnimationCurve(keys[a]);
                if (!marked) clip.SetCurve("", typeof(Transform), "localRotation." + s_Axes[a], curve);
                else clip.m_Curves.Add(new ClipCurve
                {
                    Binding = CurveBinding.ForType("", "m_LocalRotation." + s_Axes[a], typeof(Transform)),
                    Curve = curve,
                    QuaternionStart = a == 0,
                });
            }
            return clip;
        }

        static Quaternion StateRotation(Animation animation, string name, float time)
        {
            animation[name].time = time;
            animation.Sample();
            return animation.transform.localRotation;
        }

        static ClipCurve RotationCurve(AnimationClip clip, string axis) =>
            clip.m_Curves.Find(c => c.Binding.Attribute == "m_LocalRotation." + axis);

        [Theory]
        [InlineData(24f, false)]
        [InlineData(30f, false)]
        [InlineData(24f, true)]
        [InlineData(30f, true)]
        public void AFullTurnClosesTheShorterWayToItsFirstFrame(float rate, bool marked)
        {
            // Frames 0..29 at 12 degrees a frame: the last frame is at 348 degrees, in the opposite
            // quaternion hemisphere from frame 0.
            var source = Spin(rate, 12f, 29, marked);
            var probe = Keep(new GameObject("Probe"));
            var animation = NewAnimation(out _);
            animation.AddClip(source, "spin", 0, 29, true);
            var state = animation["spin"];
            Near.Equal(30f / rate, state.length, 1e-6f);
            Assert.True(animation.Play("spin"));

            // The window's frames are the source's.
            foreach (int k in new[] { 0, 7, 15, 29 })
            {
                source.SampleAnimation(probe, k / rate);
                Near.SameRotation(probe.transform.localRotation, StateRotation(animation, "spin", k / rate), 1e-2f);
            }
            var first = StateRotation(animation, "spin", 0f);
            var last = StateRotation(animation, "spin", 29f / rate);
            // Documented: the extra frame matches the first frame. Treating q and -q as
            // that match is Unbound's reading; the raw signs remain observable to scripts.
            Near.SameRotation(first, StateRotation(animation, "spin", state.length), 1e-2f);

            // Unbound's unmeasured choice (not a Unity contract): half way to the extra frame the
            // rotation lies on the shorter arc from the last frame to the first, not 180 degrees away.
            float arc = Quaternion.Angle(last, first);
            Assert.True(arc < 90f, $"the last frame is {arc} degrees from the first");
            var half = StateRotation(animation, "spin", 29.5f / rate);
            Near.Equal(arc, Quaternion.Angle(half, last) + Quaternion.Angle(half, first), 1e-2f);

            // The extra keys are the first frame's quaternion negated (the same rotation).
            var sub = animation.GetClip("spin");
            for (int a = 0; a < 4; a++)
            {
                float atFirst = RotationCurve(source, s_Axes[a]).Evaluate(0f);
                Near.Equal(-atFirst, RotationCurve(sub, s_Axes[a]).Evaluate(sub.m_StopTime), 1e-6f);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MixedBindingFormsCloseAsOneRotationEvenAfterPartialTypeResolution(bool resolveOne)
        {
            const float rate = 24f;
            var source = Spin(rate, 12f, 29, false);
            for (int axis = 0; axis < 3; axis++)
            {
                var curve = RotationCurve(source, s_Axes[axis]);
                var binding = curve.Binding;
                // A genuinely unresolved serialized binding, unlike ForType's cached result.
                curve.Binding = new CurveBinding
                {
                    Path = binding.Path, Attribute = binding.Attribute,
                    Hashed = binding.Hashed, PathHash = binding.PathHash,
                    ClassId = EngineTypes.TransformId,
                };
            }
            if (resolveOne)
                Assert.Equal(typeof(Transform), RotationCurve(source, "z").Binding.ResolveType());
            var animation = NewAnimation(out _);
            animation.AddClip(source, "mixed", 0, 29, true);
            var sub = animation.GetClip("mixed");
            Near.Equal(30f / rate, sub.length, 1e-6f);

            // Internal preservation of Unbound's chosen quaternion closure, not a new Unity
            // interpolation guarantee: the binder treats all four forms as one rotation.
            for (int axis = 0; axis < 4; axis++)
            {
                float first = RotationCurve(source, s_Axes[axis]).Evaluate(0f);
                Near.Equal(-first, RotationCurve(sub, s_Axes[axis]).Evaluate(sub.m_StopTime), 1e-6f);
            }
            Assert.Equal(null, RotationCurve(source, "x").Binding.Type);
            Assert.Equal(typeof(Transform), RotationCurve(source, "w").Binding.Type);
        }

        [Theory]
        [InlineData(24f, false)]
        [InlineData(30f, true)]
        public void ARotationInTheSameHemisphereClosesOnTheFirstFrameAsItIs(float rate, bool marked)
        {
            // Frames 0..10 at 12 degrees a frame: 120 degrees, the same hemisphere as frame 0.
            var source = Spin(rate, 12f, 10, marked);
            var animation = NewAnimation(out _);
            animation.AddClip(source, "part", 0, 10, true);
            Assert.True(animation.Play("part"));
            var sub = animation.GetClip("part");
            for (int a = 0; a < 4; a++)
                Assert.Equal(RotationCurve(source, s_Axes[a]).Evaluate(0f), RotationCurve(sub, s_Axes[a]).Evaluate(sub.m_StopTime));
            Near.SameRotation(StateRotation(animation, "part", 0f), StateRotation(animation, "part", animation["part"].length), 1e-2f);
        }

        [Theory]
        [InlineData(24f)]
        [InlineData(30f)]
        public void TheCurvesOfARotationStepTogether(float rate)
        {
            // Frame 0 to frame 6 turns 60 degrees about z; only the w curve's segment is stepped.
            var q0 = Quaternion.identity;
            var q6 = Quaternion.AngleAxis(60f, Vector3.forward);
            float t6 = 6f / rate;
            float slope = (q6.z - q0.z) / t6;
            var clip = Keep(new AnimationClip { legacy = true, name = "turn", frameRate = rate });
            clip.SetCurve("", typeof(Transform), "localRotation.x", AnimationCurve.Constant(0f, t6, 0f));
            clip.SetCurve("", typeof(Transform), "localRotation.y", AnimationCurve.Constant(0f, t6, 0f));
            clip.SetCurve("", typeof(Transform), "localRotation.z", new AnimationCurve(new Keyframe(0f, q0.z, slope, slope), new Keyframe(t6, q6.z, slope, slope)));
            clip.SetCurve("", typeof(Transform), "localRotation.w",
                new AnimationCurve(new Keyframe(0f, q0.w, 0f, float.PositiveInfinity), new Keyframe(t6, q6.w, float.PositiveInfinity, 0f)));
            var animation = NewAnimation(out _);
            animation.AddClip(clip, "turn", 0, 6, true);
            Assert.True(animation.Play("turn"));
            Near.Equal(7f / rate, animation["turn"].length, 1e-6f);
            var last = StateRotation(animation, "turn", t6);
            Near.SameRotation(q6, last, 1e-2f);
            // Unbound's unmeasured choice (not a Unity contract): the whole rotation is held until
            // the extra frame, not the w curve held while the z curve goes in a line.
            Near.SameRotation(last, StateRotation(animation, "turn", 6.5f / rate), 1e-2f);
            Near.SameRotation(q0, StateRotation(animation, "turn", animation["turn"].length), 1e-2f);
        }

        // ------------------------------------------------------------------ window edges

        [Theory]
        [InlineData(24f)]
        [InlineData(30f)]
        public void AKeyAtTheLastFrameEndsTheWindowAsItIs(float rate)
        {
            // Two keys exactly at lastFrame (a jump there), then a key after the window.
            float at = 12f / rate;
            var reference = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f),
                new Keyframe(at, 1f, 2f, 2f),
                new Keyframe(at, 5f, -1f, -1f),
                new Keyframe(1.3f, 2f, 0f, 0f));
            var animation = NewAnimation(out _);
            animation.AddClip(Source(reference, rate), "end", 6, 12, true);
            var state = animation["end"];
            Near.Equal(7f / rate, state.length, 1e-6f);
            Assert.True(animation.Play("end"));
            var sub = animation.GetClip("end");
            float start = sub.StartTime;
            for (int k = 0; k < 6; k++)
            {
                Near.Equal(reference.Evaluate(start + k / rate), StateValue(animation, "end", k / rate), 1e-4f);
                Near.Equal(reference.Evaluate(start + (k + 0.5f) / rate), StateValue(animation, "end", (k + 0.5f) / rate), 1e-4f);
            }
            // At lastFrame itself the curve has the source's value (the last key at that time).
            Assert.Equal(reference.Evaluate(at), sub.m_Curves[0].Evaluate(at));
            Near.Equal(reference.Evaluate(6f / rate), StateValue(animation, "end", state.length), 1e-4f);
        }

        [Theory]
        [InlineData(24f)]
        [InlineData(30f)]
        public void AWindowPastTheLastKeyClosesFromTheLastKeysValue(float rate)
        {
            var reference = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.2f, 1f, 0f, 0f));
            var animation = NewAnimation(out _);
            animation.AddClip(Source(reference, rate), "past", 0, 12, true);
            var state = animation["past"];
            Near.Equal(13f / rate, state.length, 1e-6f);
            Assert.True(animation.Play("past"));
            for (int k = 0; k <= 12; k++)
                Near.Equal(reference.Evaluate(k / rate), StateValue(animation, "past", k / rate), 1e-4f);
            Near.Equal(reference.Evaluate(0f), StateValue(animation, "past", state.length), 1e-4f);
        }

        [Theory]
        [InlineData(24f)]
        [InlineData(30f)]
        public void AWindowBeforeTheFirstKeyKeepsTheFirstKeysValue(float rate)
        {
            var reference = new AnimationCurve(new Keyframe(1f, 2f, 0f, 0f), new Keyframe(1.5f, 3f, 0f, 0f));
            var animation = NewAnimation(out _);
            animation.AddClip(Source(reference, rate), "before", 0, 6, true);
            var state = animation["before"];
            Near.Equal(7f / rate, state.length, 1e-6f);
            Assert.True(animation.Play("before"));
            for (int k = 0; k <= 6; k++)
                Near.Equal(reference.Evaluate(k / rate), StateValue(animation, "before", k / rate), 1e-5f);
            // Between two equal values any closing (line or step) has that value.
            Near.Equal(reference.Evaluate(0f), StateValue(animation, "before", 6.5f / rate), 1e-5f);
            Near.Equal(reference.Evaluate(0f), StateValue(animation, "before", state.length), 1e-5f);
        }

        // ------------------------------------------------------------------ refused cases

        [Fact]
        public void TheWholeClipWithTheLoopFrameIsAddedAsItIsAndSaysSo()
        {
            var source = Source(SmoothCurve(), 24f);
            float length = source.length;
            var animation = NewAnimation(out _);
            m_Expected.Add("no extra loop frame is inserted");
            var warnings = WarningsOf(() => animation.AddClip(source, "whole", int.MinValue, int.MaxValue, true));
            Assert.Single(warnings);
            Assert.Contains("no extra loop frame is inserted", warnings[0]);
            Assert.Same(source, animation.GetClip("whole"));
            Assert.Equal(length, source.length);
            // The two-argument overload adds the whole clip without a message (Dispose checks).
            animation.AddClip(source, "plain");
            Assert.Same(source, animation.GetClip("plain"));
        }

        [Theory]
        [InlineData(WrapMode.Loop, false)]
        [InlineData(WrapMode.PingPong, false)]
        [InlineData(WrapMode.Loop, true)]
        [InlineData(WrapMode.PingPong, true)]
        public void ALoopingWrapModeTheWindowReachesIsRefusedAndSaysSo(WrapMode mode, bool pre)
        {
            const float rate = 24f;
            // Keys inside frames 0..12: the window reaches before the first and after the last.
            var reference = new AnimationCurve(new Keyframe(0.3f, 0f, 0f, 0f), new Keyframe(0.45f, 1f, 0f, 0f));
            if (pre) reference.preWrapMode = mode;
            else reference.postWrapMode = mode;
            var animation = NewAnimation(out _);
            m_Expected.Add(NotInserted);
            var warnings = WarningsOf(() => animation.AddClip(Source(reference, rate), "wrap", 0, 12, true));
            Assert.Single(warnings);
            Assert.Contains(NotInserted, warnings[0]);
            Assert.Contains($"{(pre ? "pre" : "post")}-wrap mode {mode}", warnings[0]);
            var state = animation["wrap"];
            Near.Equal(12f / rate, state.length, 1e-6f);
            Assert.True(animation.Play("wrap"));
            for (int k = 0; k <= 12; k++)
                Near.Equal(reference.Evaluate(k / rate), StateValue(animation, "wrap", k / rate), 1e-4f);
        }

        [Fact]
        public void OneUnsupportedCurveAmongSeveralLeavesThemAllWithoutTheFrameOnEveryCall()
        {
            const float rate = 24f;
            var source = Source(SmoothCurve(), rate);
            // A weighted segment across frame 12, and a looping post-wrap mode before it.
            source.SetCurve("", typeof(Transform), "localPosition.y",
                new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f, 0.5f, 0.5f), new Keyframe(1f, 1f, 1f, 1f, 0.5f, 0.5f)));
            source.SetCurve("", typeof(Transform), "localPosition.z",
                new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.3f, 1f, 0f, 0f)) { postWrapMode = WrapMode.Loop });
            var animation = NewAnimation(out _);
            m_Expected.Add(NotInserted);
            foreach (var name in new[] { "first", "second" })
            {
                // One warning on every call (not once only), naming every curve refused.
                var warnings = WarningsOf(() => animation.AddClip(source, name, 6, 12, true));
                Assert.Single(warnings);
                Assert.Contains(NotInserted, warnings[0]);
                Assert.Contains($"\"{name}\"", warnings[0]);
                Assert.Contains("m_LocalPosition.y", warnings[0]);
                Assert.Contains("m_LocalPosition.z", warnings[0]);
                Assert.DoesNotContain("m_LocalPosition.x", warnings[0]);
                // All or nothing: no curve gets the frame, the supported one included.
                var sub = animation.GetClip(name);
                Near.Equal(6f / rate, sub.length, 1e-6f);
                for (int i = 0; i < source.m_Curves.Count; i++)
                    Assert.Same(source.m_Curves[i], sub.m_Curves[i]);
            }
        }

        [Theory]
        [InlineData(24f)]
        [InlineData(30f)]
        public void AWindowTooFarFromTimeZeroGetsNoShiftedExtraFrame(float rate)
        {
            // From int.MinValue the times near lastFrame are seconds apart: the extra frame is
            // either within half a frame of its time, or refused with the warning.
            var source = Source(SmoothCurve(), rate);
            var animation = NewAnimation(out _);
            m_Expected.Add(NotInserted);
            foreach (int lastFrame in new[] { 12, 120, 1000 })
            {
                var name = "far" + lastFrame;
                var warnings = WarningsOf(() => animation.AddClip(source, name, int.MinValue, lastFrame, true));
                var sub = animation.GetClip(name);
                if (warnings.Count > 0)
                {
                    Assert.Single(warnings);
                    Assert.Contains(NotInserted, warnings[0]);
                    Assert.Equal(lastFrame / rate, sub.m_StopTime);
                }
                else
                {
                    double wanted = (double)(lastFrame / rate) + 1f / rate;
                    Assert.True(System.Math.Abs(sub.m_StopTime - wanted) <= 0.5 / rate, $"frame {lastFrame}: extra frame at {sub.m_StopTime:R}s for {wanted:R}s");
                }
            }
        }
    }
}
