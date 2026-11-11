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

        /// <summary>The clip has at least one root curve (RootT, RootQ); without any its body is
        /// the identity, so the body stays on the root and the root still.</summary>
        public bool HasRootCurves;

        /// <summary>The body at the start and at the stop: RootT, the normalized RootQ, scale 1.</summary>
        public Trs StartRootX, StopRootX;

        /// <summary>Added to RootT for the motion's position: (0, level − start RootT.y, 0) with the root
        /// position Y based upon the original (the default), x and z −start when the XZ is too.</summary>
        public F4 Offset;

        /// <summary>The rotation the motion's yaw is turned by (the root rotation offset; identity for
        /// the default settings).</summary>
        public F4 OrientationReference = F4.QIdentity;

        /// <summary>What a part baked into the pose keeps: the start's body position (the lowest foot's
        /// height with the root position Y based upon the feet) and yaw.</summary>
        public F4 BakedT, BakedQ = F4.QIdentity;

        /// <summary>The root position Y is based upon the feet: the motion's height is the lowest foot
        /// goal's (measured, tests/projects/humanoid).</summary>
        public bool HeightFromFeet;

        /// <summary>Loop pose: the body relative to the motion at the stop, inverted, times the
        /// start's; a cycle's body is moved by its fraction of it (identity without loop pose).</summary>
        public Trs LoopBlendX = Trs.Identity;

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
        public float ApparentSpeed =>
            UnityEngine.Motion.ApparentSpeed(AverageSpeed.x, AverageSpeed.y, AverageSpeed.z, AverageAngularSpeed);

        /// <summary>The clip constants of a humanoid clip.</summary>
        /// <param name="sample">Fills, per curve index of <see cref="HumanClipImport.CurveNames"/>, the
        /// clip's value at a clip time (seconds), as playback evaluates the curves.</param>
        /// <param name="present">Per curve index, whether the clip has the curve.</param>
        /// <param name="start">The clip's start time (AnimationClipSettings.startTime, the clip's range).</param>
        /// <param name="stop">The clip's stop time.</param>
        /// <param name="settings">The clip's settings.</param>
        public static HumanClipMotion Build(Action<float, float[]> sample, bool[] present, float start, float stop,
            in HumanClipSettings settings)
        {
            for (int c = 0; c < HumanClipImport.RootFirst; c++)
                if (present[c])
                    throw new NotSupportedException("humanoid clips with motion curves (a root motion node) are not supported");
            var m = new HumanClipMotion { StartTime = start, StopTime = stop, LoopTime = settings.LoopTime, Settings = settings };
            for (int c = HumanClipImport.RootFirst; c < HumanClipImport.GoalFirst; c++) m.HasRootCurves |= present[c];
            var values = new float[HumanClipImport.CurveCount];

            m.HeightFromFeet = settings.HeightFromFeet && !settings.KeepOriginalPositionY;
            // A foot without goal curves counts at the body's height (tests/projects/humanoid
            // feetNoGoals, feetLeftGoal).
            sample(start, values);
            m.StartRootX = HumanRootMotion.ReadRoot(values, present);
            float startFeet = HumanRootMotion.FeetY(values, present, m.StartRootX);
            sample(stop, values);
            m.StopRootX = HumanRootMotion.ReadRoot(values, present);
            float stopFeet = HumanRootMotion.FeetY(values, present, m.StopRootX);

            // Based upon the original: the motion starts at the origin (XZ, Y) or facing forward
            // (rotation) and follows the body from there; otherwise it is the body's (center of
            // mass) position and yaw. The offsets add the level and turn the yaw.
            var startX = m.StartRootX;
            float level = settings.Level;
            m.Offset = new F4(settings.KeepOriginalPositionXZ ? -startX.t.x : 0f, settings.KeepOriginalPositionY ? level - startX.t.y : level,
                settings.KeepOriginalPositionXZ ? -startX.t.z : 0f, 0f);
            // Mirrored, the clip turns the other way, the rotation offset too (measured).
            float half = (settings.Mirror ? -settings.OrientationOffsetY : settings.OrientationOffsetY) * (MathF.PI / 360f);
            m.OrientationReference = new F4(0f, MathF.Sin(half), 0f, MathF.Cos(half));
            if (settings.KeepOriginalOrientation) m.OrientationReference = M.QNormalize(M.QMul(m.OrientationReference, M.QConj(HumanRootMotion.Yaw(startX.q))));
            // Bake into pose: the motion keeps the start's value of that part.
            m.BakedT = new F4(startX.t.x, m.HeightFromFeet ? startFeet : startX.t.y, startX.t.z, 0f);
            m.BakedQ = HumanRootMotion.Yaw(startX.q);
            m.MotionStartX = HumanRootMotion.MotionX(m, m.StartRootX.t, m.StartRootX.q, startFeet);
            m.MotionStopX = HumanRootMotion.MotionX(m, m.StopRootX.t, m.StopRootX.q, stopFeet);
            // Loop pose: the body relative to the motion moves by the stop-to-start difference
            // over the cycle, so that the stop's body is the start's.
            if (settings.LoopBlend && settings.LoopTime)
                m.LoopBlendX = M.InvMul(M.InvMul(m.MotionStopX, m.StopRootX), M.InvMul(m.MotionStartX, m.StartRootX));

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
                    var q = M.QNormalize(M.QMul(OrientationReference, Settings.LoopBlendOrientation ? BakedQ : HumanRootMotion.Yaw(root.q)));
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
    }

    /// <summary>One evaluation of a humanoid clip's root motion (default settings).</summary>
    internal struct HumanRootMotionSample
    {
        /// <summary>A humanoid clip was evaluated (false for the default value: no humanoid clip).</summary>
        public bool HasMotion;
        /// <summary>The motion from the previous to the current time, in the previous motion's frame:
        /// t in clip units (the Animator scales it by the human scale and its transform's scale),
        /// q a rotation about the up axis only, scale 1.</summary>
        public Trs Delta;
        /// <summary>The current motion (the clip's root transform) in clip space.</summary>
        public Trs Motion;
        /// <summary>The body (the pose's RootX) relative to the current motion, scale 1.</summary>
        public Trs Body;
        /// <summary>The body in clip space (the clip's RootX), scale 1.</summary>
        public Trs Root;
        /// <summary>The feet goals relative to the current motion (scale 1).</summary>
        public Trs LeftFoot, RightFoot;
        /// <summary>How far each foot goal moved during the step, in the foot's previous frame
        /// (identity when the clip's delta time is 0).</summary>
        public Trs LeftFootSlip, RightFootSlip;
        /// <summary>The clip's delta time in seconds (clip length × normalized step / |speed|).</summary>
        public float ClipDeltaTime;
        /// <summary>How much the pivot follows the feet: 1, 0 with the XZ baked into the pose or
        /// for a clip without body curves.</summary>
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
        /// <summary>The sampled local step after the feet-pivot correction, retained for replay.</summary>
        public Trs Delta;
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

        /// <summary>Whether a clip has a body curve (RootT, RootQ).</summary>
        public static bool HasRoot(bool[] present)
        {
            for (int c = RootFirst; c < GoalFirst; c++)
                if (present[c]) return true;
            return false;
        }

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

        /// <summary>The height of a clip's lowest foot goal (clip space) for the body <paramref name="root"/>.</summary>
        public static float FeetY(float[] v, bool[] present, in Trs root) =>
            MathF.Min(M.Mul(root, ReadGoal(v, present, 0)).t.y, M.Mul(root, ReadGoal(v, present, 1)).t.y);

        /// <summary>The motion of a body: its position (with the root position Y based upon the feet,
        /// the lowest foot's height <paramref name="feetY"/>) plus the clip's offset, its yaw turned
        /// by the clip's orientation reference, scale 1; the parts baked into the pose keep the
        /// start's value (measured, tests/projects/humanoid).</summary>
        public static Trs MotionX(HumanClipMotion clip, F4 t, F4 q, float feetY = 0f)
        {
            var o = clip.Offset;
            ref readonly var s = ref clip.Settings;
            var b = clip.BakedT;
            if (clip.HeightFromFeet) t.y = feetY;
            if (s.LoopBlendPositionXZ) { t.x = b.x; t.z = b.z; }
            if (s.LoopBlendPositionY) t.y = b.y;
            return new Trs(new F4(t.x + o.x, t.y + o.y, t.z + o.z, t.w + 0f),
                M.QNormalize(M.QMul(clip.OrientationReference, s.LoopBlendOrientation ? clip.BakedQ : Yaw(q))), F4.One3);
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
            result = new HumanRootMotionSample
            {
                Delta = Trs.Identity, Motion = Trs.Identity, Body = Trs.Identity, Root = Trs.Identity, LeftFoot = Trs.Identity, RightFoot = Trs.Identity,
                LeftFootSlip = Trs.Identity, RightFootSlip = Trs.Identity,
                // A clip without body curves does not hold the feet pivot either (6000.0.71f1,
                // tests/projects/humanoid body.noneTree, bodyGraph.none: the pivot weight follows
                // the other clip's feet by that clip's weight alone).
                GravityWeight = clip.Settings.LoopBlendPositionXZ || !clip.HasRootCurves ? 0f : 1f,
            };
            // A clip without body curves has the identity body (ReadRoot) and blends with it
            // like any other (6000.0.71f1, tests/projects/humanoid body.noneTree,
            // body.noneTransition, bodyGraph.none: beside HumanA's clip the body is that clip's
            // weight's share of its height).
            result.HasMotion = true;

            float curTime = ClipTime(clip, input.Time, input.Speed, input.CycleOffset, out float cycle);
            float prevTime = ClipTime(clip, input.PreviousTime, input.Speed, input.CycleOffset, out float prevCycle);

            var cur = ReadRoot(current, present);
            var prev = ReadRoot(previous, present);
            var curX = MotionX(clip, cur.t, cur.q, clip.HeightFromFeet ? FeetY(current, present, cur) : 0f);
            var prevX = MotionX(clip, prev.t, prev.q, clip.HeightFromFeet ? FeetY(previous, present, prev) : 0f);

            // The body relative to the motion.
            var curConj = M.QConj(curX.q);
            var body = LoopBlend(clip, new Trs(M.QRotate(curConj, cur.t - curX.t), M.QMul(curConj, cur.q), F4.One3), curTime);
            var prevConj = M.QConj(prevX.q);
            var prevBody = LoopBlend(clip, new Trs(M.QRotate(prevConj, prev.t - prevX.t), M.QMul(prevConj, prev.q), F4.One3), prevTime);

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
            result.Root = cur;
            result.ClipDeltaTime = clipDt;
            result.LeftFoot = M.Mul(body, ReadGoal(current, present, 0));
            result.RightFoot = M.Mul(body, ReadGoal(current, present, 1));
            if (clipDt != 0f)
            {
                result.LeftFootSlip = Slip(delta, result.LeftFoot, M.Mul(prevBody, ReadGoal(previous, present, 0)));
                result.RightFootSlip = Slip(delta, result.RightFoot, M.Mul(prevBody, ReadGoal(previous, present, 1)));
            }
        }

        /// <summary>Where a target body part of a clip is at one time relative to the clip's motion at
        /// another (target matching): (motion at <c>input.PreviousTime</c>)⁻¹ · (motion at
        /// <c>input.Time</c>, across the cycles a loop wraps) · part, the part being nothing for the
        /// root (target 0), the body relative to its motion (1) or a goal there (2..5: the feet, then
        /// the hands), its rotation in the API's goal frame, positions in clip units
        /// (tests/projects/humanoid match.*).</summary>
        /// <param name="from">The clip's values at <c>input.PreviousTime</c>.</param>
        /// <param name="to">The clip's values at <c>input.Time</c>.</param>
        public static Trs MatchPart(HumanClipMotion clip, in HumanClipMotionInput input, float[] from, float[] to, bool[] present, int target)
        {
            ClipTime(clip, input.PreviousTime, input.Speed, input.CycleOffset, out float fromCycle);
            float toTime = ClipTime(clip, input.Time, input.Speed, input.CycleOffset, out float toCycle);
            var root = ReadRoot(to, present);
            var motion = Trs.Identity;
            var body = root;
            if (clip.HasRootCurves)
            {
                var start = ReadRoot(from, present);
                var startX = MotionX(clip, start.t, start.q, clip.HeightFromFeet ? FeetY(from, present, start) : 0f);
                var endX = MotionX(clip, root.t, root.q, clip.HeightFromFeet ? FeetY(to, present, root) : 0f);
                var conj = M.QConj(endX.q);
                body = LoopBlend(clip, new Trs(M.QRotate(conj, root.t - endX.t), M.QMul(conj, root.q), F4.One3), toTime);
                if (clip.LoopTime) endX = CycleX(endX, clip.MotionStartX, clip.MotionStopX, (int)(toCycle - fromCycle));
                motion = M.InvMul(startX, endX);
            }
            if (target == 0) return motion;
            if (target == 1) return M.Mul(motion, body);
            int goal = target - 2;
            var x = M.Mul(body, ReadGoal(to, present, goal));
            x.q = M.QNormalize(M.QMul(x.q, HumanIK.GoalOrientationOffset[goal]));
            return M.Mul(motion, x);
        }

        /// <summary>A body relative to the motion at a clip time with the clip's loop pose (with loop
        /// time, as generic loop pose): moved by
        /// the time's fraction of the cycle of <see cref="HumanClipMotion.LoopBlendX"/> in its own
        /// frame, body · (u t, normalize(u q.xyz, q.w)) as generic loop pose turns rotations
        /// (measured, tests/projects/humanoid).</summary>
        internal static Trs LoopBlend(HumanClipMotion clip, in Trs body, float time)
        {
            if (!clip.Settings.LoopBlend || !clip.LoopTime || !(clip.StopTime > clip.StartTime)) return body;
            float u = (time - clip.StartTime) / (clip.StopTime - clip.StartTime);
            var d = clip.LoopBlendX;
            var q = d.q;
            var part = new Trs(d.t * u, M.QNormalize(new F4(q.x * u, q.y * u, q.z * u, q.w)), F4.One3);
            return M.Mul(body, part);
        }

        /// <summary>The root motion of several clip evaluations blended by weight (the states of a
        /// transition, a blend tree's clips): every part (step, motion, body, feet, feet slips)
        /// summed by weight, rotations by their weighted sum (aligned), normalized (measured,
        /// tests/projects/humanoid). Weights summing to less than 1 (a transition with a state
        /// without a humanoid clip that writes default values or holds, outside the cases
        /// <c>AloneBesideEmpty</c> covers on the base layer and <c>CoveredShare</c> on the layers
        /// above, <see cref="Override"/>
        /// over no motion) leave the rest of the step still, the feet slipping by what the root no longer covers; the other parts are
        /// normalized by the total weight (not measured).
        /// With <paramref name="sum"/> (an AnimationMixerPlayable's inputs, <see cref="OverNoClip"/>)
        /// no part is normalized by the total: each is Σ wᵢ·partᵢ, and below a total of 1 the
        /// identity takes the rest (positions toward zero, rotations toward the identity) and
        /// <paramref name="restGravity"/> the rest of the feet pivot weight, as default values do
        /// for generic channels (6000.0.71f1, tests/projects/animculling humanMixerShape: sums 0,
        /// 0.5 and 1.5 scale the body and step; rotations are the normalized filled sum). Unit sums
        /// give the same result either way. The identity that takes the rest of a rotation is
        /// (0, 0, 0, 1) as it is, whatever hemisphere the inputs' sum is in (<see cref="InSum"/>
        /// says which hemisphere a sum starts in and what measures it: a goal's rotation in a
        /// controller's state; the bodies and feet here follow that rule unmeasured); with
        /// <paramref name="alignRest"/> it is taken in the sum's hemisphere, as a layer's blend
        /// from the identity is (<see cref="OverNoClip"/>).</summary>
        public static HumanRootMotionSample Blend(ReadOnlySpan<HumanRootMotionSample> samples, ReadOnlySpan<float> weights, bool sum = false,
            float restGravity = 1f, bool alignRest = false)
        {
            if (samples.Length == 1 && (sum ? weights[0] == 1f : weights[0] >= 1f)) return samples[0];
            float total = 0f;
            bool any = false;
            for (int i = 0; i < samples.Length; i++)
                if (samples[i].HasMotion) { total += weights[i]; any = true; }
            var r = new HumanRootMotionSample { Delta = Trs.Identity, Motion = Trs.Identity, Body = Trs.Identity, Root = Trs.Identity,
                LeftFoot = Trs.Identity, RightFoot = Trs.Identity, LeftFootSlip = Trs.Identity, RightFootSlip = Trs.Identity };
            if (sum ? !any : total <= 0f) return samples.Length > 0 ? samples[0] : r;
            r.HasMotion = true;
            Span<Trs> parts = stackalloc Trs[9];
            var zero = new Trs(F4.Zero, F4.Zero, F4.One3);
            for (int k = 0; k < parts.Length; k++) parts[k] = zero;
            for (int i = 0; i < samples.Length; i++)
            {
                ref readonly var s = ref samples[i];
                if (!s.HasMotion) continue;
                float w = weights[i], n = sum ? w : w / total;
                Add(ref parts[0], s.Delta, w);
                Add(ref parts[1], s.Motion, n);
                Add(ref parts[2], s.LeftFoot, n);
                Add(ref parts[3], s.RightFoot, n);
                Add(ref parts[4], s.LeftFootSlip, n);
                Add(ref parts[5], s.RightFootSlip, n);
                Add(ref parts[6], s.Body, n);
                Add(ref parts[7], s.Root, n);
                if (!sum) Add(ref parts[8], s.Delta, w / total);
                r.ClipDeltaTime += s.ClipDeltaTime * w;
                r.GravityWeight += s.GravityWeight * n;
            }
            float rest = 1f - total;
            if (rest > 0f)
            {
                // The rest of a step stays still; summed mixer parts also move toward the identity.
                for (int k = 0; k < (sum ? 8 : 1); k++) parts[k].q.w += alignRest && parts[k].q.w < 0f ? -rest : rest;
                if (sum) r.GravityWeight += rest * restGravity;
            }
            // Above a total of one the feet pivot follows the feet by the clips' mean weight.
            else if (sum && total > 1f) r.GravityWeight /= total;
            if (sum) parts[8] = Trs.Identity;
            for (int k = 0; k < parts.Length; k++) parts[k].q = M.QNormalize(parts[k].q);
            r.Delta = parts[0];
            r.Motion = parts[1];
            r.LeftFoot = parts[2];
            r.RightFoot = parts[3];
            r.Body = parts[6];
            r.Root = parts[7];
            if (r.ClipDeltaTime != 0f)
            {
                r.LeftFootSlip = parts[4];
                r.RightFootSlip = parts[5];
                if (!sum && total < 1f)
                {
                    // The feet stay where the clips' whole step leaves them.
                    r.LeftFootSlip = Slip(r.Delta, r.LeftFoot, Previous(parts[8], r.LeftFoot, r.LeftFootSlip));
                    r.RightFootSlip = Slip(r.Delta, r.RightFoot, Previous(parts[8], r.RightFoot, r.RightFootSlip));
                }
            }
            return r;
        }

        /// <summary>No root motion: every part identity.</summary>
        public static readonly HumanRootMotionSample NoMotion = new HumanRootMotionSample
        {
            Delta = Trs.Identity, Motion = Trs.Identity, Body = Trs.Identity, Root = Trs.Identity, LeftFoot = Trs.Identity,
            RightFoot = Trs.Identity, LeftFootSlip = Trs.Identity, RightFootSlip = Trs.Identity, GravityWeight = 1f,
        };

        /// <summary>A layer's root motion when no layer below plays a humanoid clip: every part
        /// (step, motion, body, feet, feet slips) moves from the identity by the layer's weight,
        /// as a mixer's inputs over its default values do, and the feet pivot follows the feet by
        /// that weight alone (6000.0.71f1, tests/projects/humanoid emptyPartial and
        /// emptyPartialQuarter, layers of weight 0.5 and 0.25 over an empty base state: the body
        /// stays on the root at the weight's share of its height).</summary>
        public static HumanRootMotionSample OverNoClip(in HumanRootMotionSample layer, float weight)
        {
            if (!layer.HasMotion || weight >= 1f) return layer;
            Span<HumanRootMotionSample> one = stackalloc HumanRootMotionSample[] { layer };
            Span<float> share = stackalloc float[] { weight };
            return Blend(one, share, sum: true, restGravity: 0f, alignRest: true);
        }

        /// <summary>An override layer's root motion over the layers below it: each part moved
        /// towards the layer's by its weight (positions lerped, rotations by the normalized lerp).
        /// Over no motion (a playable output over none) the layer's step is its weight's share
        /// and its body and feet stay its own (not measured; a layer over no humanoid clip is
        /// <see cref="OverNoClip"/>).</summary>
        public static HumanRootMotionSample Override(in HumanRootMotionSample below, in HumanRootMotionSample layer, float weight)
        {
            if (!layer.HasMotion || weight <= 0f) return below;
            if (weight >= 1f) return layer;
            if (!below.HasMotion)
            {
                Span<HumanRootMotionSample> one = stackalloc HumanRootMotionSample[] { layer };
                Span<float> share = stackalloc float[] { weight };
                return Blend(one, share);
            }
            Span<HumanRootMotionSample> pair = stackalloc HumanRootMotionSample[] { below, layer };
            Span<float> weights = stackalloc float[] { 1f - weight, weight };
            return Blend(pair, weights);
        }

        /// <summary>An additive layer's body difference: each clip's body (RootX) relative to its
        /// additive reference body, reference⁻¹ · body, summed by the clips' weights (rotations
        /// aligned); the position is scaled by the layer weight, the rotation is normalized and its
        /// vector part scaled by the layer weight times the clips' total weight, then normalized, as
        /// generic additive rotations are (measured for one clip, tests/projects/humanoid; the sum of
        /// several clips is Unbound's choice).</summary>
        public static Trs AdditiveBody(ReadOnlySpan<Trs> differences, ReadOnlySpan<float> weights, float layerWeight)
        {
            F4 t = F4.Zero, q = F4.Zero;
            float total = 0f;
            for (int i = 0; i < differences.Length; i++)
            {
                float w = weights[i];
                var d = differences[i];
                var dq = d.q.w < 0f ? -d.q : d.q;
                t = t + d.t * w;
                q = q + dq * w;
                total += w;
            }
            float m = MathF.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            var r = F4.QIdentity;
            if (m >= 1e-6f)
            {
                float s = layerWeight * total / m;
                r = M.QNormalize(new F4(q.x * s, q.y * s, q.z * s, q.w / m));
            }
            return new Trs(t * layerWeight, r, F4.One3);
        }

        /// <summary>The body of the layers below with an additive layer's
        /// <see cref="AdditiveBody"/> difference composed in the clip's body space: body · difference
        /// (measured, tests/projects/humanoid). The motion, step and feet stay as they were (additive
        /// layers leave root motion alone).</summary>
        public static void AddBody(ref HumanRootMotionSample sample, in Trs difference) =>
            sample.Body = M.Mul(sample.Body, difference);

        /// <summary>One layer of a humanoid Animator's root motion over the layers below it
        /// (<paramref name="below"/>; <paramref name="any"/>: whether a layer below has humanoid
        /// clips). An override layer blends its clips' <paramref name="samples"/> and moves the motion
        /// towards them by its weight; an additive layer composes its clips' body
        /// <paramref name="differences"/> onto the body below (<see cref="AdditiveBody"/>). With the
        /// layer mask's humanoid <paramref name="parts"/> (AvatarMaskBodyPart order, null: all), a
        /// layer without Root adds neither body nor motion, and an override layer without a foot IK
        /// part keeps the feet below for the feet pivot (measured, tests/projects/humanoid).</summary>
        public static void CombineLayer(ref HumanRootMotionSample below, ref bool any, ReadOnlySpan<HumanRootMotionSample> samples,
            ReadOnlySpan<Trs> differences, ReadOnlySpan<float> weights, bool additive, float layerWeight, bool[] parts)
        {
            if (additive ? differences.Length == 0 : samples.Length == 0) return;
            if (layerWeight <= 0f) return;
            // Also over no humanoid clip: an empty base state or generic root motion below
            // (override layers measured, tests/projects/humanoid noRootOverEmpty, noRootOverGeneric;
            // additive ones follow unmeasured).
            if (parts != null && !parts[0]) return;
            if (additive)
            {
                if (!any)
                {
                    Unbound.Runtime.Stubs.Ignored("Animator additive humanoid body over no humanoid clip",
                        "an additive layer's humanoid body differences over no humanoid clip below are left out (not measured)");
                    return;
                }
                AddBody(ref below, AdditiveBody(differences, weights, layerWeight));
                return;
            }
            var layer = Blend(samples, weights);
            // Skipped layers (weight 0) and no humanoid motion below keep the feet pivot as it was.
            var result = any ? Override(below, layer, layerWeight) : OverNoClip(layer, layerWeight);
            if (any && below.HasMotion && layer.HasMotion && parts != null)
            {
                if (!parts[9]) { result.LeftFoot = below.LeftFoot; result.LeftFootSlip = below.LeftFootSlip; }
                if (!parts[10]) { result.RightFoot = below.RightFoot; result.RightFootSlip = below.RightFootSlip; }
            }
            below = result;
            any = true;
        }

        /// <summary>How far the body curves may be from the clips' blended RootX and still hold
        /// just that: the layers' pose and the root motion pass blend the same bodies with their
        /// operations in another order, a few units in the last place apart (clip units and unit
        /// rotations, so about 1e-7).</summary>
        const float RootRounding = 1e-6f;

        static bool Near(F4 a, F4 b) => Near(a.x, b.x) && Near(a.y, b.y) && Near(a.z, b.z) && Near(a.w, b.w);

        static bool Near(float a, float b) => MathF.Abs(a - b) <= RootRounding * MathF.Max(1f, MathF.Abs(b));

        /// <summary>A rotation as it joins a weighted sum of rotations: negated when its dot
        /// product with the sum has the sign bit set. That is the sum's hemisphere, and for an
        /// empty sum a rotation whose four components are all negative (a dot product of −0),
        /// which so starts the sum as its positive twin; any other first rotation starts it as
        /// it is. Matches the Unity 6000.0.71f1 Editor, tests/projects/humanoid
        /// body.directHalfNegGoal: a foot goal alone at 0.5 in a Direct blend tree, whose
        /// rotation as its clip's body places it has four negative components in 28 recorded
        /// updates, where the identity completes its positive twin, and a positive z beside a
        /// negative w in one (step 25), where the identity completes it as it is, in the other
        /// hemisphere. Measured for that goal; a body's rotation, a mixer's inputs and a zero
        /// component (which counts by its sign here) follow the same arithmetic unmeasured. A
        /// body's curves do not bring their sign to a sum: body.directLowNeg and
        /// bodyGraph.lowNeg, with every RootQ key negated, record the rows of body.directLow
        /// and bodyGraph.low.</summary>
        internal static F4 InSum(F4 sum, F4 q) =>
            float.IsNegative((sum.x * q.x + sum.y * q.y) + (sum.z * q.z + sum.w * q.w)) ? -q : q;

        static void Add(ref Trs sum, in Trs x, float w)
        {
            sum.t = sum.t + x.t * w;
            sum.q = sum.q + InSum(sum.q, x.q) * w;
        }

        /// <summary>A foot goal's move during a step, in its previous frame: prev⁻¹ · delta · cur.</summary>
        static Trs Slip(in Trs delta, in Trs cur, in Trs prev)
        {
            var moved = new Trs(M.QRotate(delta.q, cur.t) + delta.t, M.QMul(delta.q, cur.q), F4.One3);
            var c = M.QConj(prev.q);
            return new Trs(M.QRotate(c, moved.t - prev.t), M.QMul(c, moved.q), F4.One3);
        }

        /// <summary>The human pose placed by the root motion: after
        /// <see cref="HumanClipImport.PoseFromCurves"/> of the layers' blended values, the body
        /// becomes the clips' bodies relative to their motions, blended, plus what the layers'
        /// values hold beyond the clips' blended RootX (additive layers); the goals (relative to the
        /// body) move with it.</summary>
        /// <param name="placed">Per curve index, whether the pose's curve was written, when the
        /// written goal curves hold goals placed by their clips' bodies already
        /// (<see cref="HumanStateGoals"/>): those goals stay as they are, whatever additive
        /// layers add to the body (null: every goal is relative to the body).</param>
        public static void ApplyToPose(HumanPose pose, in HumanRootMotionSample sample, bool[] placed = null)
        {
            var body = PoseBody(pose.RootX, sample);
            pose.RootX = body;
            for (int g = 0; g < pose.Goals.Length; g++)
                if (placed == null || !AnyGoalCurve(placed, g)) pose.Goals[g].X = M.Mul(body, pose.Goals[g].X);
        }

        static bool AnyGoalCurve(bool[] present, int goal)
        {
            int first = GoalFirst + HumanStateGoals.Width * goal;
            for (int c = first; c < first + HumanStateGoals.Width; c++)
                if (present[c]) return true;
            return false;
        }

        /// <summary>The body relative to the motion that <see cref="ApplyToPose"/> gives a pose whose
        /// body curves hold <paramref name="rootX"/>: the sample's, plus what the curves hold
        /// beyond the clips' blended RootX.</summary>
        internal static Trs PoseBody(in Trs rootX, in HumanRootMotionSample sample)
        {
            if (Near(rootX.t, sample.Root.t) && (Near(rootX.q, sample.Root.q) || Near(rootX.q, -sample.Root.q))) return sample.Body;
            var extra = M.QRotate(M.QConj(sample.Motion.q), rootX.t - sample.Root.t);
            var turn = M.QMul(M.QConj(sample.Root.q), rootX.q);
            return new Trs(sample.Body.t + extra, M.QNormalize(M.QMul(sample.Body.q, turn)), F4.One3);
        }

        /// <summary>The Animator's root motion pass for a humanoid avatar: the feet pivot, the
        /// Animator's deltas and velocities, the root moved by the motion and the pivot position.</summary>
        /// <param name="sample">The Animator's (blended) root motion.</param>
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

            ProjectMotion(new Trs(dt, dq, F4.One3), root, deltaTime, humanScale, memory.Pivot, out result);
        }

        /// <summary>An update in which no humanoid clip plays (a state without a motion on the base
        /// layer): the root stays, the feet pivot rests at it, the pivot weight returns to 0.5 and
        /// the first evaluation ends, so the next humanoid clip's first update moves the root by
        /// its feet pivot's way back to where this one rests (6000.0.71f1, tests/projects/humanoid
        /// emptyExit, emptyCond and emptyBack: about (-0.34, 31.4, 0) on HumanA). Returns
        /// Animator.pivotPosition (the root's position).</summary>
        public static F4 Idle(HumanRootMotionMemory memory, in Trs root, float deltaTime)
        {
            if (deltaTime != 0f) memory.PivotWeight = 0.5f;
            memory.Pivot = F4.Zero;
            EndUpdate(memory, deltaTime);
            return root.t;
        }

        /// <summary>Convert a retained local step in the current root frame without updating
        /// feet-pivot memory. Visibility return replays motion in that frame (animculling).</summary>
        internal static void ProjectMotion(in Trs delta, in Trs root, float deltaTime, float humanScale,
            F4 pivot, out AnimatorRootMotion result)
        {
            var dt = delta.t;
            var dq = delta.q;
            result.Delta = delta;
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
            var p2 = pivot;
            var ms = moved.s;
            result.PivotPosition = M.QRotate(moved.q, new F4(hs * p2.x * ms.x, hs * p2.y * ms.y, hs * p2.z * ms.z, 0f)) + moved.t;
        }

        /// <summary>A foot goal at the previous time in the previous motion's frame: delta · cur · slip⁻¹.</summary>
        static Trs Previous(in Trs delta, in Trs cur, in Trs slip)
        {
            var sc = M.QConj(slip.q);
            var moved = new Trs(M.QRotate(delta.q, cur.t) + delta.t, M.QMul(delta.q, cur.q), F4.One3);
            return M.Mul(moved, new Trs(M.QRotate(sc, -slip.t), sc, F4.One3));
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
        internal static F4 AngularVelocityOf(F4 dq, float deltaTime) => AngularVelocity(dq, deltaTime);

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
