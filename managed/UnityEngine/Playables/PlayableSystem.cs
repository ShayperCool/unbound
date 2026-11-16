using UnityEngine;
using PL = UnityEngine.PlayerLoop;

namespace Unbound.Runtime.Playables
{
    /// <summary>
    /// Where the player loop runs playable graphs, as Unity's Director module does: the DSP clock
    /// is sampled in Initialization.DirectorSampleTime; playing graphs (and pending Play/Stop)
    /// update in Update.DirectorUpdate, after the scripts' Update; their animation outputs are
    /// written in PreLateUpdate.DirectorUpdateAnimationBegin and the notifications they pushed are
    /// delivered in DirectorDeferredEvaluate; destroyed graphs are torn down in DirectorUpdate and
    /// PostLateUpdate.DirectorLateUpdate, where playables the frame's updates finished report done.
    /// </summary>
    internal static class PlayableSystem
    {
        static bool s_Registered;
        static double s_LastDsp = double.NaN;

        /// <summary>The DSP clock's advance this frame (DirectorUpdateMode.DSPClock).</summary>
        internal static double DspDelta { get; private set; }

        /// <summary>The DSP clock graphs follow, in seconds (<see cref="SampleTime"/>).</summary>
        internal static double GraphDspTime => s_LastDsp;

        /// <summary>Starts the graphs' DSP clock again at the next sample (tests).</summary>
        internal static void ResetClockForTests()
        {
            s_LastDsp = double.NaN;
            DspDelta = 0;
        }

        internal static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            // DirectorSampleTime samples the clock in every frame, graphs or not: the first graph
            // moves by the clock from the start of the frame it was made in.
            s_LastDsp = AudioSettings.dspTime;
            PlayerLoopDriver.Register(typeof(PL.Initialization.DirectorSampleTime), SampleTime);
            PlayerLoopDriver.Register(typeof(PL.Update.DirectorUpdate), PlayableGraphData.UpdateAll);
            // Animation outputs evaluate with the Animators (AnimationSystem, DirectorUpdateAnimationBegin).
            AnimationSystem.EnsureRegistered();
            PlayerLoopDriver.Register(typeof(PL.PreLateUpdate.DirectorDeferredEvaluate), PlayableGraphData.DeliverAllNotifications);
            PlayerLoopDriver.Register(typeof(PL.PostLateUpdate.DirectorLateUpdate), PlayableGraphData.LateUpdate);
        }

        /// <summary>The DSP clock as graphs follow it: the audio clock moves in whole dsp buffers,
        /// a graph on it moves every frame by the frame's real time, never more than one buffer
        /// ahead of the audio clock nor behind it, so it meets the audio clock again where that
        /// one steps further (matches the Unity 6000.0.71f1 Editor, tests/projects/dspclock
        /// `graph.*`: a graph's time moves every frame between the audio clock's buffers).</summary>
        internal static void SampleTime()
        {
            double dsp = AudioSettings.dspTime;
            if (double.IsNaN(s_LastDsp))
            {
                DspDelta = 0;
                s_LastDsp = dsp;
                return;
            }
            var configuration = AudioSettings.GetConfiguration();
            double buffer = configuration.sampleRate > 0 ? (double)configuration.dspBufferSize / configuration.sampleRate : 0;
            double real = double.IsFinite(Time.s_RealDeltaTime) ? System.Math.Max(0, Time.s_RealDeltaTime) : 0;
            double next = System.Math.Clamp(s_LastDsp + real, dsp, dsp + buffer);
            DspDelta = System.Math.Max(0, next - s_LastDsp);
            s_LastDsp = System.Math.Max(s_LastDsp, next);
        }
    }
}
