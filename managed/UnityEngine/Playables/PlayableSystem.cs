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

        /// <summary>How far behind the audio clock, in buffers, a graph on it is pulled up to it:
        /// Unbound's choice inside the 2.8 to 5.7 ms the Editor measures (<see cref="SampleTime"/>).</summary>
        const double PullUpBuffers = 0.25;

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
        /// a graph on it moves every frame by the frame's real time, and is pulled up to the audio
        /// clock when that would leave it more than a quarter of a buffer behind. Measured with the
        /// Unity 6000.0.71f1 Editor (tests/projects/dspclock `graph.*`, and an exploratory capture: a
        /// graph 18 ms ahead of the clock is not pulled back, one left 2.8 ms behind is not pulled
        /// up, ones that would be 5.7 and 12 ms behind are; a quarter of a buffer, 5.3 ms there, is
        /// Unbound's choice inside 2.8 to 5.7 ms: where a graph stands against the clock depends on
        /// the Editor's real frame timing, so no recorded control pins it). Unbound also holds it
        /// at most a buffer ahead (unmeasured), so that it follows a device clock that drifts from
        /// real time. The buffer is the one of the last audio update (no per-frame settings
        /// query).</summary>
        internal static void SampleTime()
        {
            double dsp = AudioSettings.dspTime;
            if (double.IsNaN(s_LastDsp))
            {
                DspDelta = 0;
                s_LastDsp = dsp;
                return;
            }
            var status = AudioSystem.Status;
            double buffer = status.SampleRate > 0 ? (double)status.BufferSize / status.SampleRate : 0;
            double real = double.IsFinite(Time.s_RealDeltaTime) ? System.Math.Max(0, Time.s_RealDeltaTime) : 0;
            double next = System.Math.Min(s_LastDsp + real, dsp + buffer);
            if (next < dsp - PullUpBuffers * buffer) next = dsp;
            DspDelta = System.Math.Max(0, next - s_LastDsp);
            s_LastDsp = System.Math.Max(s_LastDsp, next);
        }
    }
}
