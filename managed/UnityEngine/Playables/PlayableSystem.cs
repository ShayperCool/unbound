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
    /// PostLateUpdate.DirectorLateUpdate.
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
            // Animation outputs evaluate with the Animators (AnimationSystem, DirectorUpdateAnimationBegin).
            AnimationSystem.EnsureRegistered();
            PlayerLoopDriver.Register(typeof(PL.PreLateUpdate.DirectorDeferredEvaluate), PlayableGraphData.DeliverAllNotifications);
            PlayerLoopDriver.Register(typeof(PL.PostLateUpdate.DirectorLateUpdate), PlayableGraphData.FlushDestroyed);
        }

        static void SampleTime()
        {
            double now = AudioSettings.dspTime;
            DspDelta = double.IsNaN(s_LastDsp) ? 0 : System.Math.Max(0, now - s_LastDsp);
            s_LastDsp = now;
        }
    }
}
