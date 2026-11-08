using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using Xunit;
using PL = UnityEngine.PlayerLoop;

namespace UnityEngine.Tests
{
    /// <summary>
    /// AudioSource/AudioListener <c>velocityUpdateMode</c>: the loop that samples the doppler
    /// velocity. Documented (Unity 6000.0 AudioVelocityUpdateMode): Fixed updates in the FixedUpdate
    /// loop, Dynamic in the Update loop, Auto in the FixedUpdate loop when attached to a Rigidbody
    /// and in the Update loop otherwise. Everything else checked here (the parent Rigidbody counts,
    /// Rigidbody2D does not for Auto, the last fixed step wins, a frame without a step keeps the
    /// velocity, a loop change restarts the sampling, a fixed sample reads an interpolated or
    /// extrapolated body's simulated pose) is Unbound's unmeasured choice, revisable when measured.
    /// The fixed node is driven through the player loop registry, so the file builds without the
    /// fixed-loop sampling it checks. Predictions (unexecuted): without fixed-loop sampling, 12
    /// cases fail (FixedMode ×2, SeveralSteps, AutoWithARigidbody ×4, ChangingTheLoop, and the 4
    /// child cases of InterpolatedRigidbody2D, whose per-frame chord of a turning child differs
    /// from the per-step chord) and 17 pass: the 5 frame-loop controls, the 8 interpolated 3D
    /// body cases and the 4 Rigidbody2D body cases (the in-between pose moves linearly per
    /// frame); with fixed-loop sampling of the Transform, the 8 interpolated 3D cases and the 8
    /// Rigidbody2D cases fail and the other 13 pass; with the simulated pose of a 3D body only,
    /// the 8 Rigidbody2D cases fail and the other 21 pass; with the simulated pose of both kinds
    /// of body, all 29 pass.
    /// </summary>
    public class AudioVelocityUpdateModeTests : IDisposable
    {
        const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        readonly List<GameObject> m_Objects = new List<GameObject>();
        readonly float m_FixedDeltaTime = Time.s_FixedDeltaTime;
        readonly double m_RealDeltaTime = Time.s_RealDeltaTime;
        readonly float m_DeltaTime = Time.s_DeltaTime;

        public AudioVelocityUpdateModeTests()
        {
            AudioSystem.ResetForTests();
            AudioBackend.Use(new VirtualAudioBackend());
        }

        public void Dispose()
        {
            foreach (var go in m_Objects)
                if (go != null) Object.DestroyImmediate(go);
            AudioSystem.ResetForTests();
            Time.s_FixedDeltaTime = m_FixedDeltaTime;
            Time.s_RealDeltaTime = m_RealDeltaTime;
            Time.s_DeltaTime = m_DeltaTime;
            Time.s_InFixedTimeStep = false;
        }

        /// <summary>A source or the (only, so hearing) listener on a new GameObject.</summary>
        sealed class Ear
        {
            internal GameObject Owner;
            internal AudioSource Source;
            internal AudioListener Listener;

            internal Vector3 Velocity => Source != null ? Source.m_Velocity : Listener.m_Velocity;

            internal AudioVelocityUpdateMode Mode
            {
                set
                {
                    if (Source != null) Source.velocityUpdateMode = value;
                    else Listener.velocityUpdateMode = value;
                }
            }

            internal void MoveTo(float x) => Owner.transform.position = new Vector3(x, 0f, 0f);
        }

        GameObject Track(GameObject go)
        {
            m_Objects.Add(go);
            return go;
        }

        Ear Make(bool listener, AudioVelocityUpdateMode mode, Transform parent = null)
        {
            var go = Track(new GameObject(listener ? "listener" : "source"));
            if (parent != null) go.transform.SetParent(parent, false);
            var ear = new Ear { Owner = go };
            if (listener) ear.Listener = go.AddComponent<AudioListener>();
            else ear.Source = go.AddComponent<AudioSource>();
            ear.Mode = mode;
            return ear;
        }

        /// <summary>One fixed step's FixedUpdate.AudioFixedUpdate node, audio actions only.</summary>
        static void FixedStep(float step)
        {
            var engine = (Dictionary<Type, List<Action>>)typeof(PlayerLoopDriver).GetField("s_Engine", StaticPrivate).GetValue(null);
            Time.s_FixedDeltaTime = step;
            Time.s_InFixedTimeStep = true;
            try
            {
                if (engine.TryGetValue(typeof(PL.FixedUpdate.AudioFixedUpdate), out var actions))
                    foreach (var action in actions.ToArray())
                        if (action.Method.DeclaringType == typeof(AudioSystem)) action();
            }
            finally { Time.s_InFixedTimeStep = false; }
        }

        /// <summary>The frame's PostLateUpdate.UpdateAudio after <paramref name="realDelta"/> seconds.</summary>
        static void Frame(double realDelta)
        {
            Time.s_RealDeltaTime = realDelta;
            AudioSystem.Update();
        }

        static Vector3 X(float x) => new Vector3(x, 0f, 0f);

        // Documented: Auto without a Rigidbody, and Dynamic, update in the frame loop. The values
        // are the per-frame motion over the frame's real time, as before velocityUpdateMode was read.
        [Theory]
        [InlineData(AudioVelocityUpdateMode.Auto, false)]
        [InlineData(AudioVelocityUpdateMode.Auto, true)]
        [InlineData(AudioVelocityUpdateMode.Dynamic, false)]
        [InlineData(AudioVelocityUpdateMode.Dynamic, true)]
        public void FrameLoopModesIgnoreFixedStepsAndSampleEachFrame(AudioVelocityUpdateMode mode, bool listener)
        {
            var ear = Make(listener, mode);
            Frame(0.5);
            Assert.Equal(Vector3.zero, ear.Velocity);
            ear.MoveTo(1f);
            FixedStep(0.25f);
            Assert.Equal(Vector3.zero, ear.Velocity);
            Frame(0.5);
            Assert.Equal(X(2f), ear.Velocity);
            ear.MoveTo(3f);
            FixedStep(0.25f);
            Assert.Equal(X(2f), ear.Velocity);
            Frame(0.5);
            Assert.Equal(X(4f), ear.Velocity);
        }

        // Documented: Fixed updates in the fixed loop. Unbound's choices: the step's
        // fixedDeltaTime divides the motion since the previous step; frames without a step (as at
        // timeScale 0) keep the last step's velocity.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FixedModeSamplesOnlyInFixedSteps(bool listener)
        {
            var ear = Make(listener, AudioVelocityUpdateMode.Fixed);
            FixedStep(0.25f);
            Assert.Equal(Vector3.zero, ear.Velocity);
            ear.MoveTo(0.5f);
            FixedStep(0.25f);
            Assert.Equal(X(2f), ear.Velocity);
            ear.MoveTo(1.5f);
            Frame(0.5);
            Assert.Equal(X(2f), ear.Velocity);
            ear.MoveTo(2f);
            Frame(0.5);
            Assert.Equal(X(2f), ear.Velocity);
            FixedStep(0.25f);
            Assert.Equal(X(6f), ear.Velocity);
        }

        // Unbound's choice: every step samples, so the frame sends the last step's velocity.
        [Fact]
        public void MeasuredSourcesUseBodyVelocityAndListenersUseTransformMotion()
        {
            string previousRelease = ProjectInfo.UnityVersion;
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                Time.s_DeltaTime = 0.5f;
                var source = Make(false, AudioVelocityUpdateMode.Dynamic);
                var body = source.Owner.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.linearVelocity = X(3f);
                Frame(0.5);
                source.MoveTo(10f);
                Frame(0.5);
                Assert.Equal(X(3f), source.Velocity);
                var kinematic = Make(false, AudioVelocityUpdateMode.Dynamic);
                kinematic.Owner.AddComponent<Rigidbody>().isKinematic = true;
                Frame(0.5);
                kinematic.MoveTo(20f);
                Frame(0.5);
                Assert.Equal(Vector3.zero, kinematic.Velocity);
                var listener = Make(true, AudioVelocityUpdateMode.Dynamic);
                listener.Owner.AddComponent<Rigidbody>().isKinematic = true;
                Frame(0.5);
                listener.MoveTo(2f);
                Frame(0.5);
                Assert.Equal(X(4f), listener.Velocity);
            }
            finally { ProjectInfo.UnityVersion = previousRelease; }
        }

        [Fact]
        public void MeasuredMotionUsesTheGameFrameStepWhenWallTimeDiffers()
        {
            string previousRelease = ProjectInfo.UnityVersion;
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                Time.s_DeltaTime = 0.25f;
                var source = Make(false, AudioVelocityUpdateMode.Dynamic);
                var listener = Make(true, AudioVelocityUpdateMode.Dynamic);
                Frame(0.5);
                source.MoveTo(2f);
                listener.MoveTo(1f);
                Frame(0.5);
                Assert.Equal(X(8f), source.Velocity);
                Assert.Equal(X(4f), listener.Velocity);
            }
            finally { ProjectInfo.UnityVersion = previousRelease; }
        }

        [Fact]
        public void SeveralStepsInOneFrameLeaveTheLastStepsVelocity()
        {
            var ear = Make(false, AudioVelocityUpdateMode.Fixed);
            FixedStep(0.25f);
            ear.MoveTo(0.5f);
            FixedStep(0.25f);
            ear.MoveTo(1.5f);
            FixedStep(0.25f);
            Frame(0.5);
            Assert.Equal(X(4f), ear.Velocity);
        }

        // Documented: Auto with a Rigidbody updates in the fixed loop. Unbound's choice: a parent's
        // Rigidbody counts too.
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void AutoWithARigidbodyFollowsTheFixedLoop(bool onParent, bool listener)
        {
            Ear ear;
            if (onParent)
            {
                var body = Track(new GameObject("body"));
                body.AddComponent<Rigidbody>().isKinematic = true;
                ear = Make(listener, AudioVelocityUpdateMode.Auto, body.transform);
            }
            else
            {
                ear = Make(listener, AudioVelocityUpdateMode.Auto);
                ear.Owner.AddComponent<Rigidbody>().isKinematic = true;
            }
            FixedStep(0.25f);
            ear.MoveTo(0.5f);
            FixedStep(0.25f);
            Assert.Equal(X(2f), ear.Velocity);
            ear.MoveTo(1.5f);
            Frame(0.5);
            Assert.Equal(X(2f), ear.Velocity);
        }

        // Unbound's choice (unmeasured): the page names Rigidbody; a Rigidbody2D keeps Auto in the
        // frame loop. An implementation check, not a Unity guarantee.
        [Fact]
        public void AutoWithOnlyARigidbody2DStaysInTheFrameLoop()
        {
            var ear = Make(false, AudioVelocityUpdateMode.Auto);
            ear.Owner.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            Frame(0.5);
            ear.MoveTo(1f);
            FixedStep(0.25f);
            Assert.Equal(Vector3.zero, ear.Velocity);
            Frame(0.5);
            Assert.Equal(X(2f), ear.Velocity);
        }

        // Unbound's choice (unmeasured): switching loops starts the sampling again from the next
        // position in the new loop, keeping the last velocity until its second sample.
        [Fact]
        public void ChangingTheLoopRestartsSamplingAndKeepsTheVelocity()
        {
            var ear = Make(false, AudioVelocityUpdateMode.Fixed);
            FixedStep(0.25f);
            ear.MoveTo(0.5f);
            FixedStep(0.25f);
            Assert.Equal(X(2f), ear.Velocity);
            ear.Mode = AudioVelocityUpdateMode.Dynamic;
            ear.MoveTo(5f);
            Frame(0.5);
            Assert.Equal(X(2f), ear.Velocity);
            ear.MoveTo(5.5f);
            Frame(0.5);
            Assert.Equal(X(1f), ear.Velocity);
            ear.Mode = AudioVelocityUpdateMode.Fixed;
            ear.MoveTo(9f);
            FixedStep(0.25f);
            Assert.Equal(X(1f), ear.Velocity);
            ear.MoveTo(9.75f);
            FixedStep(0.25f);
            Assert.Equal(X(3f), ear.Velocity);
        }

        /// <summary>The source or (only, so hearing) listener added to an existing GameObject.</summary>
        static Ear Attach(GameObject go, bool listener, AudioVelocityUpdateMode mode)
        {
            var ear = new Ear { Owner = go };
            if (listener) ear.Listener = go.AddComponent<AudioListener>();
            else ear.Source = go.AddComponent<AudioSource>();
            ear.Mode = mode;
            return ear;
        }

        static void AssertNear(Vector3 expected, Vector3 actual, int frame)
        {
            if ((expected - actual).magnitude < 1e-3f) return;
            Assert.Fail($"frame {frame}: expected ({expected.x:F4}, {expected.y:F4}, {expected.z:F4}), got ({actual.x:F4}, {actual.y:F4}, {actual.z:F4})");
        }

        // Documented: Auto with a Rigidbody updates in the fixed loop, and the Remarks of
        // velocityUpdateMode tie the loop to the one the object moves in. An interpolated or
        // extrapolated body's Transform holds the pose between steps when the fixed sample runs
        // (PreUpdate.PhysicsUpdate writes it each frame). Unbound's choice (unmeasured): the fixed
        // sample reads the step's simulated pose, a child keeping its offset from the body, so a
        // body at constant velocity gives that velocity at every step. Frames every 0.015 s, steps
        // every 0.02 s: one step in most frames, none in some; the body is turned 90 degrees about
        // y and the child sits at local (0, 0, 1). The values are physics results, compared within
        // 1e-3 (a Transform sample is off by 0.5 or more: 1.5 or 3 instead of 2).
        [Theory]
        [InlineData(RigidbodyInterpolation.Interpolate, false, false)]
        [InlineData(RigidbodyInterpolation.Interpolate, false, true)]
        [InlineData(RigidbodyInterpolation.Interpolate, true, false)]
        [InlineData(RigidbodyInterpolation.Interpolate, true, true)]
        [InlineData(RigidbodyInterpolation.Extrapolate, false, false)]
        [InlineData(RigidbodyInterpolation.Extrapolate, false, true)]
        [InlineData(RigidbodyInterpolation.Extrapolate, true, false)]
        [InlineData(RigidbodyInterpolation.Extrapolate, true, true)]
        public void InterpolatedBodiesSampleTheSimulatedPoseInFixedSteps(RigidbodyInterpolation interpolation, bool onChild, bool listener)
        {
            const float step = 0.02f;
            const double frame = 0.015;
            var mode = Physics.simulationMode;
            double time = Time.s_Time, fixedTime = Time.s_FixedTime;
            var interpolate = typeof(PhysicsWorld).GetMethod("Interpolate", StaticPrivate);
            Assert.NotNull(interpolate);
            try
            {
                Physics.simulationMode = SimulationMode.FixedUpdate;
                var body = Track(new GameObject("body"));
                body.transform.position = new Vector3(100f, 200f, 300f);
                body.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                body.AddComponent<BoxCollider>();
                var rb = body.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.linearDamping = 0f;
                rb.sleepThreshold = 0f;
                rb.interpolation = interpolation;
                rb.linearVelocity = X(2f);
                Ear ear;
                if (onChild)
                {
                    ear = Make(listener, AudioVelocityUpdateMode.Auto, body.transform);
                    ear.Owner.transform.localPosition = new Vector3(0f, 0f, 1f);
                }
                else ear = Attach(body, listener, AudioVelocityUpdateMode.Auto);

                Time.s_Time = Time.s_FixedTime = 0.0;
                bool first = true;
                int steps = 0, checkedSteps = 0;
                for (int f = 0; f < 20; f++)
                {
                    if (f > 0) Time.s_Time += frame;
                    bool stepped = false;
                    // FixedUpdate: AudioFixedUpdate, then PhysicsFixedUpdate, per step.
                    while (first || Time.s_FixedTime + step <= Time.s_Time + 1e-7)
                    {
                        if (!first) Time.s_FixedTime += step;
                        first = false;
                        FixedStep(step);
                        PhysicsWorld.Simulate(step);
                        stepped = true;
                        steps++;
                    }
                    interpolate.Invoke(null, null); // PreUpdate.PhysicsUpdate
                    Frame(frame);
                    // From the third sample on, every frame with a step has sampled the motion
                    // between two simulated poses.
                    if (stepped && steps >= 3)
                    {
                        AssertNear(X(2f), ear.Velocity, f);
                        checkedSteps++;
                    }
                }
                Assert.True(checkedSteps >= 10, $"checked {checkedSteps} steps");
            }
            finally
            {
                Physics.simulationMode = mode;
                Time.s_Time = time;
                Time.s_FixedTime = fixedTime;
            }
        }

        // The 2D counterpart, in explicit Fixed mode (Auto with only a Rigidbody2D stays in the frame
        // loop). PreUpdate.Physics2DUpdate writes the pose between steps to an interpolated or
        // extrapolated Rigidbody2D's Transform each frame. Unbound's choice (unmeasured): the fixed
        // sample reads the step's simulated pose, a child keeping its offset from the body turned
        // with the simulated angle. Frames every 0.015 s, steps every 0.02 s. The body, turned 30
        // degrees about z, moves at (2, 0) and turns at 90 degrees/s; the child sits at local
        // (1, 0, 0). The body must give (2, 0, 0) at every step; the child the chord between the
        // simulated poses that Rigidbody2D.position and rotation report before each step. Compared
        // within 1e-3: a float32 model of this arithmetic puts the expected error below 2e-5, and a
        // Transform sample is off by 0.5 or more (1.5 instead of 2 for the body at frame 3).
        [Theory]
        [InlineData(RigidbodyInterpolation2D.Interpolate, false, false)]
        [InlineData(RigidbodyInterpolation2D.Interpolate, false, true)]
        [InlineData(RigidbodyInterpolation2D.Interpolate, true, false)]
        [InlineData(RigidbodyInterpolation2D.Interpolate, true, true)]
        [InlineData(RigidbodyInterpolation2D.Extrapolate, false, false)]
        [InlineData(RigidbodyInterpolation2D.Extrapolate, false, true)]
        [InlineData(RigidbodyInterpolation2D.Extrapolate, true, false)]
        [InlineData(RigidbodyInterpolation2D.Extrapolate, true, true)]
        public void InterpolatedRigidbody2DSampleTheSimulatedPoseInFixedMode(RigidbodyInterpolation2D interpolation, bool onChild, bool listener)
        {
            const float step = 0.02f;
            const double frame = 0.015;
            var mode = Physics2D.simulationMode;
            double time = Time.s_Time, fixedTime = Time.s_FixedTime;
            var interpolate = typeof(Physics2DWorld).GetMethod("Interpolate", StaticPrivate);
            Assert.NotNull(interpolate);
            try
            {
                Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
                var body = Track(new GameObject("body2D"));
                body.transform.position = new Vector3(1f, 2f, 3f);
                body.transform.rotation = Quaternion.Euler(0f, 0f, 30f);
                var rb = body.AddComponent<Rigidbody2D>();
                rb.gravityScale = 0f;
                rb.linearDamping = 0f;
                rb.angularDamping = 0f;
                rb.interpolation = interpolation;
                rb.linearVelocity = new Vector2(2f, 0f);
                rb.angularVelocity = 90f;
                Ear ear;
                if (onChild)
                {
                    ear = Make(listener, AudioVelocityUpdateMode.Fixed, body.transform);
                    ear.Owner.transform.localPosition = new Vector3(1f, 0f, 0f);
                }
                else ear = Attach(body, listener, AudioVelocityUpdateMode.Fixed);

                // The simulated pose of the sampled object, from what the body reports.
                Vector3 Simulated()
                {
                    var at = new Vector3(rb.position.x, rb.position.y, 3f);
                    return onChild ? at + Quaternion.Euler(0f, 0f, rb.rotation) * Vector3.right : at;
                }

                Time.s_Time = Time.s_FixedTime = 0.0;
                bool first = true;
                int steps = 0, checkedSteps = 0;
                var last = Vector3.zero;
                var chord = Vector3.zero;
                for (int f = 0; f < 20; f++)
                {
                    if (f > 0) Time.s_Time += frame;
                    bool stepped = false;
                    // FixedUpdate: AudioFixedUpdate, then Physics2DFixedUpdate, per step.
                    while (first || Time.s_FixedTime + step <= Time.s_Time + 1e-7)
                    {
                        if (!first) Time.s_FixedTime += step;
                        var pose = Simulated();
                        if (!first) chord = (pose - last) / step;
                        last = pose;
                        first = false;
                        FixedStep(step);
                        Physics2DWorld.Simulate(step, -1, fromLoop: true);
                        stepped = true;
                        steps++;
                    }
                    interpolate.Invoke(null, null); // PreUpdate.Physics2DUpdate
                    Frame(frame);
                    if (stepped && steps >= 3)
                    {
                        AssertNear(onChild ? chord : X(2f), ear.Velocity, f);
                        checkedSteps++;
                    }
                }
                Assert.True(checkedSteps >= 10, $"checked {checkedSteps} steps");
            }
            finally
            {
                Physics2D.simulationMode = mode;
                Time.s_Time = time;
                Time.s_FixedTime = fixedTime;
            }
        }
    }
}
