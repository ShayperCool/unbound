using UnityEngine;
using PL = UnityEngine.PlayerLoop;

namespace Unbound.Runtime.Playables
{
    /// <summary>
    /// Where the player loop runs playable graphs, as Unity's Director module does: the DSP clock
    /// is sampled in Initialization.DirectorSampleTime, playing graphs (and pending Play/Stop)
    /// update in Update.DirectorUpdate, after the scripts' Update; graphs destroyed during a frame
    /// are torn down at its end.
    /// </summary>
    internal static class PlayableSystem
    {
        static bool s_Registered;
        static double s_LastDsp = double.NaN;

        /// <summary>The DSP clock's advance this frame (DirectorUpdateMode.DSPClock).</summary>
        internal static double DspDelta { get; private set; }

        internal static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            PlayerLoopDriver.Register(typeof(PL.Initialization.DirectorSampleTime), SampleTime);
            PlayerLoopDriver.Register(typeof(PL.Update.DirectorUpdate), PlayableGraphData.UpdateAll);
            PlayerLoopDriver.Register(typeof(PL.PostLateUpdate.PlayerSendFrameComplete), PlayableGraphData.FlushDestroyed);
        }

        static void SampleTime()
        {
            double now = AudioSettings.dspTime;
            DspDelta = double.IsNaN(s_LastDsp) ? 0 : System.Math.Max(0, now - s_LastDsp);
            s_LastDsp = now;
        }
    }
}
