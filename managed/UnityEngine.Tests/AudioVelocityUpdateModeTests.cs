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
    /// Rigidbody2D does not, the last fixed step wins, a frame without a step keeps the velocity, a
    /// loop change restarts the sampling) is Unbound's unmeasured choice, revisable when measured.
    /// The fixed node is driven through the player loop registry, so the file builds without the
    /// fixed-loop sampling it checks.
    /// </summary>
    public class AudioVelocityUpdateModeTests : IDisposable
    {
        const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        readonly List<GameObject> m_Objects = new List<GameObject>();
        readonly float m_FixedDeltaTime = Time.s_FixedDeltaTime;
        readonly double m_RealDeltaTime = Time.s_RealDeltaTime;

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
    }
}
