using System;

namespace Unbound.Runtime.Humanoid
{
    /// <summary>What a humanoid clip's root motion needs from the clip, computed from its muscle
    /// curves when the clip is built, as the Unity 6000.0.71f1 Editor computes them
    /// (tests/projects/humanoid): the body (root curves) at the start and the stop of the clip, the
    /// motion (the Animator's root) those give, and the averages the AnimationClip reports.</summary>
    internal sealed class HumanClipMotion
    {
        /// <summary>The intervals the clip's average speeds are measured over (21 samples from the start
        /// to the stop).</summary>
        public const int AverageSpeedIntervals = 20;

        public float StartTime, StopTime;
        public bool LoopTime;
        public HumanClipSettings Settings;

        /// <summary>The clip has at least one root curve (RootT, RootQ); without any it has no root
        /// motion and the body stays at the root.</summary>
        public bool HasRootCurves;

        /// <summary>The body at the start and at the stop: RootT, the normalized RootQ, scale 1.</summary>
        public Trs StartRootX, StopRootX;

        /// <summary>Added to RootT for the motion's position: (0, level − start RootT.y, 0) with the root
        /// position Y based upon the original (the default), x and z −start when the XZ is too.</summary>
        public F4 Offset;

        /// <summary>The rotation the motion's yaw is turned by (the root rotation offset; identity for
        /// the default settings).</summary>
        public F4 OrientationReference = F4.QIdentity;

        /// <summary>The motion at the start and at the stop (see <see cref="HumanRootMotion.MotionX"/>);
        /// a loop's cycle moves the motion by MotionStopX · MotionStartX⁻¹.</summary>
        public Trs MotionStartX, MotionStopX;

        /// <summary>AnimationClip.averageSpeed: the mean of the motion's velocities over
        /// <see cref="AverageSpeedIntervals"/> equal intervals, each in the frame of the motion at the
        /// interval's start (clip units, i.e. divided by the human scale).</summary>
        public F4 AverageSpeed;

        /// <summary>AnimationClip.averageAngularSpeed (radians per second about the motion's up axis).</summary>
        public float AverageAngularSpeed;

        /// <summary>AnimationClip.averageDuration.</summary>
        public float AverageDuration => StopTime - StartTime;

        /// <summary>AnimationClip.apparentSpeed: |averageSpeed| · (1 + (averageAngularSpeed / 2)²),
        /// in double precision.</summary>
        public float ApparentSpeed
        {
            get
            {
                var v = AverageSpeed;
                float l = MathF.Sqrt(v.z * v.z + (v.x * v.x + v.y * v.y));
                double h = AverageAngularSpeed * 0.5f;
                return (float)((h * h + 1.0) * l);
            }
        }

        /// <summary>The clip constants of a humanoid clip.</summary>
        /// <param name="sample">Fills, per curve index of <see cref="HumanClipImport.CurveNames"/>, the
        /// clip's value at a clip time (seconds), as playback evaluates the curves.</param>
        /// <param name="present">Per curve index, whether the clip has the curve.</param>
        /// <param name="start">The clip's start time (AnimationClipSettings.startTime, the clip's range).</param>
        /// <param name="stop">The clip's stop time.</param>
        /// <param name="settings">The clip's settings; only the defaults are supported (loop time on or
        /// off).</param>
        public static HumanClipMotion Build(Action<float, float[]> sample, bool[] present, float start, float stop,
            in HumanClipSettings settings)
        {
            settings.RequireDefaultPlayback();
            for (int c = 0; c < HumanClipImport.RootFirst; c++)
                if (present[c])
                    throw new NotSupportedException("humanoid clips with motion curves (a root motion node) are not supported");
            var m = new HumanClipMotion { StartTime = start, StopTime = stop, LoopTime = settings.LoopTime, Settings = settings };
            for (int c = HumanClipImport.RootFirst; c < HumanClipImport.GoalFirst; c++) m.HasRootCurves |= present[c];
            var values = new float[HumanClipImport.CurveCount];

            sample(start, values);
            m.StartRootX = HumanRootMotion.ReadRoot(values, present);
            sample(stop, values);
            m.StopRootX = HumanRootMotion.ReadRoot(values, present);

            // The defaults: no XZ offset, the Y offset brings the start's body height to the level.
            float level = settings.Level;
            m.Offset = new F4(0f, settings.KeepOriginalPositionY ? level - m.StartRootX.t.y : level, 0f, 0f);
            m.OrientationReference = F4.QIdentity;
            m.MotionStartX = HumanRootMotion.MotionX(m, m.StartRootX.t, m.StartRootX.q);
            m.MotionStopX = HumanRootMotion.MotionX(m, m.StopRootX.t, m.StopRootX.q);

            m.ComputeAverageSpeed(sample, present, values);
            return m;
        }

        void ComputeAverageSpeed(Action<float, float[]> sample, bool[] present, float[] values)
        {
            const int n = AverageSpeedIntervals;
            var sum = F4.Zero;
            float angular = 0f;
            if (StartTime < StopTime)
            {
                float dt = (StopTime - StartTime) / (float)n;
                var step = new F4(dt, dt, dt, 0f);
                float time = StartTime;
                F4 prevT = F4.Zero, prevQ = F4.QIdentity;
                for (int i = 0; i <= n; i++)
                {
                    sample(time, values);
                    var root = HumanRootMotion.ReadRoot(values, present);
                    var p = root.t;
                    var q = M.QNormalize(M.QMul(OrientationReference, HumanRootMotion.Yaw(root.q)));
                    if (i != 0)
                    {
                        var c = M.QConj(prevQ);
                        var d = M.QRotate(c, p - prevT);
                        var dq = M.QMul(c, q);
                        var v = new F4(d.x / step.x, d.y / step.y, d.z / step.z, 0f);
                        sum = sum + v;
                        float a = MathF.Atan(M.Quat2Qtan(dq).y);
                        angular = angular + (a + a) / dt;
                    }
                    prevT = p;
                    prevQ = q;
                    time = time + dt;
                }
            }
            float count = (float)n;
            AverageSpeed = new F4(Settings.LoopBlendPositionXZ ? 0f : sum.x / count, Settings.LoopBlendPositionY ? 0f : sum.y / count,
                Settings.LoopBlendPositionXZ ? 0f : sum.z / count, 0f);
            AverageAngularSpeed = angular / count;
        }
    }

    /// <summary>One evaluation's input: the state's normalized times (clip lengths played, with the
    /// state's speed applied) after and before the update.</summary>
    internal struct HumanClipMotionInput
    {
        public float Time, PreviousTime;
        /// <summary>The state's effective speed: its sign plays the clip backwards, its magnitude turns
        /// the normalized time step into seconds for the clip's own delta time.</summary>
        public float Speed;
        /// <summary>The state's cycle offset (added to the normalized time of a looping clip).</summary>
        public float CycleOffset;
        /// <summary>The state mirrors the clip (not supported).</summary>
        public bool Mirror;
    }

    /// <summary>One evaluation of a humanoid clip's root motion (default settings).</summary>
    internal struct HumanRootMotionSample
    {
        /// <summary>The clip has root curves: without them there is no motion (identity delta and body).</summary>
        public bool HasMotion;
        /// <summary>The motion from the previous to the current time, in the previous motion's frame:
        /// t in clip units (the Animator scales it by the human scale and its transform's scale),
        /// q a rotation about the up axis only, scale 1.</summary>
        public Trs Delta;
        /// <summary>The current motion (the clip's root transform) in clip space.</summary>
        public Trs Motion;
        /// <summary>The body (the pose's RootX) relative to the current motion, scale 1.</summary>
        public Trs Body;
        /// <summary>The feet goals relative to the current motion (scale 1).</summary>
        public Trs LeftFoot, RightFoot;
        /// <summary>How far each foot goal moved during the step, in the foot's previous frame
        /// (identity when the clip's delta time is 0).</summary>
        public Trs LeftFootSlip, RightFootSlip;
        /// <summary>The clip's delta time in seconds (clip length × normalized step / |speed|).</summary>
        public float ClipDeltaTime;
        /// <summary>How much the pivot follows the feet: 1, 0 with the XZ baked into the pose.</summary>
        public float GravityWeight;
    }

    /// <summary>The root motion state an Animator keeps between updates.</summary>
    internal sealed class HumanRootMotionMemory
    {
        /// <summary>The pivot (between the feet goals) in the motion's frame, clip units.</summary>
        public F4 Pivot;
        /// <summary>Animator.pivotWeight.</summary>
        public float PivotWeight = 0.5f;
        /// <summary>Set until an update with a non-zero delta time has finished; while set the feet
        /// pivot does not correct the motion.</summary>
        public bool FirstEvaluation = true;
    }

    /// <summary>What a root motion pass gives the Animator.</summary>
    internal struct AnimatorRootMotion
    {
        /// <summary>Animator.deltaPosition (world).</summary>
        public F4 DeltaPosition;
        /// <summary>Animator.deltaRotation (the motion's local rotation step, normalized).</summary>
        public F4 DeltaRotation;
        /// <summary>Animator.velocity (world).</summary>
        public F4 Velocity;
        /// <summary>Animator.angularVelocity (world, radians per second).</summary>
        public F4 AngularVelocity;
        /// <summary>The root after the motion: position, unnormalized rotation, scale. Built-in root
        /// motion writes the position and the normalized rotation (<see cref="HumanClipImport.NormalizeCurveQ"/>)
        /// to the Animator's transform; this is also Animator.rootPosition / rootRotation.</summary>
        public Trs Root;
        /// <summary>Animator.pivotPosition (world).</summary>
        public F4 PivotPosition;
    }

    /// <summary>Humanoid root motion as the Unity 6000.0.71f1 Editor evaluates it
    /// (tests/projects/humanoid): the motion is the body's position and its rotation about the up axis,
    /// moved so the start's body height is the level; the Animator's root follows the motion's steps
    /// (cycles of a looping clip chained end to start) and the body is placed relative to it.</summary>
    internal static class HumanRootMotion
    {
        const int RootFirst = HumanClipImport.RootFirst, GoalFirst = HumanClipImport.GoalFirst;

        /// <summary>The body of a clip's curve values: RootT (missing 0), the normalized RootQ (missing
        /// components 0, w 1), scale 1.</summary>
        public static Trs ReadRoot(float[] v, bool[] present)
        {
            float V(int c, float missing) => present[c] ? v[c] : missing;
            var t = new F4(V(RootFirst, 0f), V(RootFirst + 1, 0f), V(RootFirst + 2, 0f), 0f);
            var q = M.QNormalize(new F4(V(RootFirst + 3, 0f), V(RootFirst + 4, 0f), V(RootFirst + 5, 0f), V(RootFirst + 6, 1f)));
            return new Trs(t, q, F4.One3);
        }

        /// <summary>A goal of a clip's curve values (0 left foot, 1 right foot, 2 left hand, 3 right
        /// hand): position, normalized rotation, scale 1.</summary>
        public static Trs ReadGoal(float[] v, bool[] present, int goal)
        {
            int c = GoalFirst + 7 * goal;
            float V(int i, float missing) => present[c + i] ? v[c + i] : missing;
            var t = new F4(V(0, 0f), V(1, 0f), V(2, 0f), 0f);
            var q = M.QNormalize(new F4(V(3, 0f), V(4, 0f), V(5, 0f), V(6, 1f)));
            return new Trs(t, q, F4.One3);
        }

        /// <summary>A rotation's part about the up axis (y): the rotation that brings its up axis back up,
        /// applied before it, keeping y and w, normalized (identity when that is 0).</summary>
        public static F4 Yaw(F4 q)
        {
            float ax = -(q.w * q.z) + q.y * q.x;
            float ay = (q.w * q.w + -0.5f) + q.y * q.y;
            float az = q.w * q.x + q.y * q.z;
            float ux = ax + ax, uy = ay + ay, uz = az + az;
            float len = MathF.Sqrt((uz * uz + 0f) + (uy * uy + ux * ux));
            var back = M.QNormalize(new F4(uy * 0f - uz, uz * 0f - ux * 0f, ux - uy * 0f, len + uy));
            var p = M.QMul(back, q);
            return HumanClipImport.NormalizeCurveQ(new F4(p.x * 0f, p.y, p.z * 0f, p.w));
        }

        /// <summary>The motion of a body: its position plus the clip's offset, its yaw turned by the
        /// clip's orientation reference, scale 1.</summary>
        public static Trs MotionX(HumanClipMotion clip, F4 t, F4 q)
        {
            var o = clip.Offset;
            return new Trs(new F4(t.x + o.x, t.y + o.y, t.z + o.z, t.w + 0f),
                M.QNormalize(M.QMul(clip.OrientationReference, Yaw(q))), F4.One3);
        }

        /// <summary>The clip time a normalized state time samples and the loop cycle it is in: a
        /// looping clip takes the fraction of (time + cycle offsets) (negative times wrap into the
        /// cycle below), a non-looping one clamps the time to [0, 1] (cycle 0); playing backwards
        /// mirrors the fraction (1 − u) and negates the cycle.</summary>
        public static float ClipTime(HumanClipMotion clip, float normalizedTime, float speed, float cycleOffset, out float cycle)
        {
            bool loop = clip.LoopTime;
            bool reverse = MathF.CopySign(1f, speed) < 0f;
            float clamped = 0f;
            if (0f <= normalizedTime) clamped = normalizedTime;
            float u = 1f;
            if (clamped <= 1f) u = clamped;
            float shifted = normalizedTime + clip.Settings.CycleOffset + cycleOffset;
            float whole = MathF.Truncate(shifted);
            float frac = shifted - whole;
            float c = 0f;
            if (loop)
            {
                u = frac;
                c = whole;
                if (shifted < 0f)
                {
                    u = u + 1f;
                    c = -1f + c;
                }
            }
            if (reverse) u = 1f - u;
            if (reverse && loop) c = -c;
            cycle = c;
            return (clip.StopTime - clip.StartTime) * u + clip.StartTime;
        }

        /// <summary>The motion <paramref name="x"/> moved by whole loop cycles: each cycle applies
        /// stop · start⁻¹ (start · stop⁻¹ backwards).</summary>
        public static Trs CycleX(in Trs x, in Trs start, in Trs stop, int cycles)
        {
            if (cycles == 0) return x;
            int n = cycles < 0 ? -cycles : cycles;
            var a = cycles > 0 ? start : stop;
            var b = cycles > 0 ? stop : start;
            var qa = M.QConj(a.q);
            var inv = M.QRotate(qa, -a.t);
            var d = new Trs(M.QRotate(b.q, inv) + b.t, M.QMul(b.q, qa), F4.One3);
            var r = Trs.Identity;
            while (n != 0)
            {
                if ((n & 1) != 0) r = new Trs(M.QRotate(r.q, d.t) + r.t, M.QMul(r.q, d.q), F4.One3);
                d = new Trs(M.QRotate(d.q, d.t) + d.t, M.QMul(d.q, d.q), F4.One3);
                n >>= 1;
            }
            return new Trs(M.QRotate(r.q, x.t) + r.t, M.QMul(r.q, x.q), x.s);
        }

        /// <summary>A humanoid clip's root motion for one evaluation.</summary>
        /// <param name="clip">The clip's constants.</param>
        /// <param name="input">The state's times and speed.</param>
        /// <param name="previous">The curve values at <see cref="ClipTime"/> of the previous time.</param>
        /// <param name="current">The curve values at <see cref="ClipTime"/> of the current time.</param>
        /// <param name="present">Per curve index, whether the clip has the curve.</param>
        /// <param name="result">Output.</param>
        public static void Evaluate(HumanClipMotion clip, in HumanClipMotionInput input, float[] previous, float[] current, bool[] present,
            out HumanRootMotionSample result)
        {
            if (input.Mirror)
                throw new NotSupportedException("mirrored humanoid states are not supported");
            result = new HumanRootMotionSample
            {
                Delta = Trs.Identity, Motion = Trs.Identity, Body = Trs.Identity, LeftFoot = Trs.Identity, RightFoot = Trs.Identity,
                LeftFootSlip = Trs.Identity, RightFootSlip = Trs.Identity,
                GravityWeight = clip.Settings.LoopBlendPositionXZ ? 0f : 1f,
            };
            if (!clip.HasRootCurves) return;
            result.HasMotion = true;

            ClipTime(clip, input.Time, input.Speed, input.CycleOffset, out float cycle);
            ClipTime(clip, input.PreviousTime, input.Speed, input.CycleOffset, out float prevCycle);

            var cur = ReadRoot(current, present);
            var prev = ReadRoot(previous, present);
            var curX = MotionX(clip, cur.t, cur.q);
            var prevX = MotionX(clip, prev.t, prev.q);

            // The body relative to the motion.
            var curConj = M.QConj(curX.q);
            var body = new Trs(M.QRotate(curConj, cur.t - curX.t), M.QMul(curConj, cur.q), F4.One3);
            var prevConj = M.QConj(prevX.q);
            var prevBody = new Trs(M.QRotate(prevConj, prev.t - prevX.t), M.QMul(prevConj, prev.q), F4.One3);

            // The step: the previous motion to the current one, across the cycles a loop wrapped.
            F4 dt = F4.Zero, dq = F4.QIdentity;
            if (clip.LoopTime)
            {
                var c = CycleX(curX, clip.MotionStartX, clip.MotionStopX, (int)(cycle - prevCycle));
                dt = M.QRotate(prevConj, c.t - prevX.t);
                dq = M.QMul(prevConj, c.q);
            }
            else if (input.Time != input.PreviousTime)
            {
                dt = M.QRotate(prevConj, curX.t - prevX.t);
                dq = M.QMul(prevConj, curX.q);
            }
            // Only the turn about the up axis is kept.
            dq = M.QNormalize(new F4(0f, M.Quat2Qtan(dq).y, 0f, 1f));
            var delta = new Trs(dt, dq, F4.One3);

            float clipDt = 0f;
            if (input.Speed != 0f)
                clipDt = ((clip.StopTime - clip.StartTime) * (input.Time - input.PreviousTime)) / MathF.Abs(input.Speed);

            result.Delta = delta;
            result.Motion = curX;
            result.Body = body;
            result.ClipDeltaTime = clipDt;
            result.LeftFoot = M.Mul(body, ReadGoal(current, present, 0));
            result.RightFoot = M.Mul(body, ReadGoal(current, present, 1));
            if (clipDt != 0f)
            {
                result.LeftFootSlip = Slip(delta, result.LeftFoot, M.Mul(prevBody, ReadGoal(previous, present, 0)));
                result.RightFootSlip = Slip(delta, result.RightFoot, M.Mul(prevBody, ReadGoal(previous, present, 1)));
            }
        }

        /// <summary>A foot goal's move during a step, in its previous frame: prev⁻¹ · delta · cur.</summary>
        static Trs Slip(in Trs delta, in Trs cur, in Trs prev)
        {
            var moved = new Trs(M.QRotate(delta.q, cur.t) + delta.t, M.QMul(delta.q, cur.q), F4.One3);
            var c = M.QConj(prev.q);
            return new Trs(M.QRotate(c, moved.t - prev.t), M.QMul(c, moved.q), F4.One3);
        }

        /// <summary>The human pose of a clip evaluation placed by the root motion: after
        /// <see cref="HumanClipImport.PoseFromCurves"/> of the current values, the body becomes
        /// <see cref="HumanRootMotionSample.Body"/> and the goals (relative to the clip's body) move
        /// with it.</summary>
        public static void ApplyToPose(HumanPose pose, in HumanRootMotionSample sample)
        {
            pose.RootX = sample.Body;
            for (int g = 0; g < pose.Goals.Length; g++) pose.Goals[g].X = M.Mul(sample.Body, pose.Goals[g].X);
        }

        /// <summary>The Animator's root motion pass for a humanoid avatar: the feet pivot, the
        /// Animator's deltas and velocities, the root moved by the motion and the pivot position.</summary>
        /// <param name="sample">The root motion of the Animator's (single, full weight) clip.</param>
        /// <param name="root">The Animator transform's global position, rotation and scale before the
        /// pass.</param>
        /// <param name="deltaTime">The update's delta time.</param>
        /// <param name="humanScale">The avatar's human scale (Animator.humanScale).</param>
        /// <param name="feetPivotActive">Animator.feetPivotActive (1 by default).</param>
        /// <param name="memory">The Animator's root motion memory (updated).</param>
        /// <param name="result">Output.</param>
        public static void ApplyToAnimator(in HumanRootMotionSample sample, in Trs root, float deltaTime, float humanScale,
            float feetPivotActive, HumanRootMotionMemory memory, out AnimatorRootMotion result)
        {
            var dt = sample.Delta.t;
            var dq = sample.Delta.q;
            float g = sample.GravityWeight;

            // The feet pivot: the motion moves so the last pivot stays between the feet where it was.
            if (!memory.FirstEvaluation)
            {
                var pl = Unslipped(sample.Delta, sample.LeftFoot, sample.LeftFootSlip);
                var pr = Unslipped(sample.Delta, sample.RightFoot, sample.RightFootSlip);
                float w0 = memory.PivotWeight;
                var p = memory.Pivot;
                float cx = feetPivotActive * g * (p.x - ((pr.x - pl.x) * w0 + pl.x)) * 1f;
                float cz = (p.z - ((pr.z - pl.z) * w0 + pl.z)) * feetPivotActive * g * 1f;
                dt = M.QRotate(dq, new F4(cx, 1f * 0f, cz, 0f)) + dt;
            }
            if (deltaTime != 0f)
            {
                var ls = sample.LeftFootSlip.t;
                var rs = sample.RightFootSlip.t;
                float l = MathF.Sqrt(((ls.z * ls.z + 0f) + ls.y * ls.y) + ls.x * ls.x);
                float sum = MathF.Sqrt(((rs.z * rs.z + 0f) + rs.y * rs.y) + rs.x * rs.x) + l;
                float w = 0f;
                if (0f < sum)
                {
                    float f = l / sum;
                    w = (f <= 1f ? f : 1f) + -0.5f;
                }
                memory.PivotWeight = w * g + 0.5f;
            }
            var a = sample.LeftFoot.t;
            var b = sample.RightFoot.t;
            float pw = memory.PivotWeight;
            memory.Pivot = new F4((b.x - a.x) * pw + a.x, (b.y - a.y) * pw + a.y, (b.z - a.z) * pw + a.z, (b.w - a.w) * pw + a.w);

            var s = root.s;
            float hs = humanScale;
            result.DeltaPosition = M.QRotate(root.q, new F4(dt.x * s.x * hs, dt.y * s.y * hs, dt.z * s.z * hs, 0f));
            result.DeltaRotation = HumanClipImport.NormalizeCurveQ(dq);
            if (deltaTime != 0f)
            {
                var v = new F4(dt.x / deltaTime, dt.y / deltaTime, dt.z / deltaTime, 0f);
                result.Velocity = M.QRotate(root.q, new F4(v.x * s.x * hs, v.y * s.y * hs, v.z * s.z * hs, 0f));
                result.AngularVelocity = M.QRotate(root.q, AngularVelocity(dq, deltaTime));
            }
            else
            {
                result.Velocity = F4.Zero;
                result.AngularVelocity = M.QRotate(root.q, F4.Zero);
            }

            var step = new F4(hs * dt.x * s.x, hs * dt.y * s.y, hs * dt.z * s.z, 0f);
            var moved = new Trs(M.QRotate(root.q, step) + root.t, M.QMul(root.q, dq), new F4(s.x, s.y, s.z, s.w * 0f));
            result.Root = moved;
            var p2 = memory.Pivot;
            var ms = moved.s;
            result.PivotPosition = M.QRotate(moved.q, new F4(hs * p2.x * ms.x, hs * p2.y * ms.y, hs * p2.z * ms.z, 0f)) + moved.t;
        }

        /// <summary>A foot goal's previous position in the previous motion's frame, recovered from the
        /// current goal and its slip: (delta · cur · slip⁻¹).t.</summary>
        static F4 Unslipped(in Trs delta, in Trs cur, in Trs slip)
        {
            var sc = M.QConj(slip.q);
            var inv = M.QRotate(sc, -slip.t);
            return M.QRotate(M.QMul(delta.q, cur.q), inv) + (M.QRotate(delta.q, cur.t) + delta.t);
        }

        /// <summary>A rotation step's angular velocity: its axis times its angle, per second (0 for
        /// no rotation).</summary>
        static F4 AngularVelocity(F4 dq, float deltaTime)
        {
            var q = M.QNormalize(dq);
            float s = MathF.Sqrt((q.z * q.z + 0f) + (q.y * q.y + q.x * q.x));
            if (s == 0f) return F4.Zero;
            float half = MathF.Atan(s / MathF.Sqrt(1f - s * s));
            float k = half + half;
            return new F4(k * q.x / s / deltaTime, k * q.y / s / deltaTime, k * q.z / s / deltaTime, 0f);
        }

        /// <summary>After an update: the first evaluation lasts until an update with a non-zero delta time.</summary>
        public static void EndUpdate(HumanRootMotionMemory memory, float deltaTime) =>
            memory.FirstEvaluation = memory.FirstEvaluation && deltaTime == 0f;
    }
}
