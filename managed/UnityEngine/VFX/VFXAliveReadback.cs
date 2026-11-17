using System.Collections.Generic;
using UnityEngine.VFX;

namespace Unbound.Runtime
{
    /// <summary>
    /// When the alive counts of particle systems that kill particles are read back from the GPU.
    /// The Editor runs the effects of a graph it can instance as one batch, and asks for the
    /// count of one of the graph's systems for all the batch's effects at once: every effect
    /// then awake and updated gets a new count, the next one is asked for a period after the
    /// answer, and an effect whose system wakes has one asked for the frame after. So the
    /// counts of effects of one such graph change together, also for an effect that did
    /// nothing itself (tests/projects/vfxstrips, `single` with `reinit` and `toggled`, `dying`
    /// with `dying reinit`). An effect of a graph that cannot be instanced, of an asset that
    /// turns instancing off or in a component that does not allow it is on its own
    /// (tests/projects/vfxevents, `on die` with `reinit`; `alone`, `dying apart`).
    ///
    /// Each effect here keeps buffers of its own, so a batch's question is one request for
    /// each of its systems, sent in the same frame.
    /// </summary>
    internal sealed class VFXAliveReadback
    {
        /// <summary>How many frames pass between an answer and the next question.</summary>
        const uint Period = 60;

        static readonly Dictionary<(VisualEffectAsset Asset, int System), VFXAliveReadback> s_Shared = new Dictionary<(VisualEffectAsset, int), VFXAliveReadback>();
        /// <summary>Every schedule that has a system, asked in turn after a frame's updates.</summary>
        static readonly List<VFXAliveReadback> s_All = new List<VFXAliveReadback>();

        readonly List<VFXParticleSystem> m_Systems = new List<VFXParticleSystem>();
        readonly (VisualEffectAsset Asset, int System) m_Key;
        uint m_NextFrame;
        int m_Waiting;

        VFXAliveReadback((VisualEffectAsset, int) key)
        {
            m_Key = key;
            m_NextFrame = VFXManager.Frame + 1;
        }

        /// <summary>The frame of the last question (zero before the first).</summary>
        internal uint AskedFrame { get; private set; }

        /// <summary>Whether a question is waiting for its answers.</summary>
        internal bool Waiting => m_Waiting != 0;

        /// <summary>Whether the Editor runs an effect with the others of its asset as one
        /// batch: a graph that can be instanced, of an asset that does not turn instancing off,
        /// in a component that allows it.</summary>
        internal static bool Batched(VisualEffect effect)
        {
            var asset = effect.visualEffectAsset;
            return asset != null && effect.AllowsInstancing && asset.m_Data.InstancingDisabledReason == VFXInstancingDisabledReason.None
                && asset.m_Settings.InstancingMode != VFXInstancingMode.Disabled;
        }

        /// <summary>The schedule of a system that kills particles: the one its asset's effects
        /// share for that system when they are a batch, else one of its own.</summary>
        internal static VFXAliveReadback Join(VFXParticleSystem system)
        {
            var asset = system.Effect.visualEffectAsset;
            VFXAliveReadback schedule;
            if (!Batched(system.Effect)) schedule = new VFXAliveReadback((null, system.Index));
            else if (!s_Shared.TryGetValue((asset, system.Index), out schedule)) s_Shared[(asset, system.Index)] = schedule = new VFXAliveReadback((asset, system.Index));
            if (schedule.m_Systems.Count == 0) s_All.Add(schedule);
            schedule.m_Systems.Add(system);
            return schedule;
        }

        internal void Leave(VFXParticleSystem system)
        {
            if (!m_Systems.Remove(system) || m_Systems.Count != 0) return;
            s_All.Remove(this);
            // By reference: the asset may be destroyed before its last effect goes.
            if (!(m_Key.Asset is null)) s_Shared.Remove(m_Key);
        }

        /// <summary>A system of the schedule woke: a count is asked for in the next frame, also
        /// when one was due in this frame: effects of a batch that wake one frame after the
        /// other put the question off each time (tests/projects/vfxstrips, `wakes`: three
        /// frames running).</summary>
        internal void Woke() => m_NextFrame = VFXManager.Frame + 1;

        /// <summary>One of the question's answers came, or is not waited for any more (its
        /// system started over): after the last, the next question is a period away.</summary>
        internal void Answered()
        {
            if (m_Waiting != 0 && --m_Waiting == 0) m_NextFrame += Period;
        }

        /// <summary>After a frame's updates of every effect: the questions that are due, each
        /// to the systems awake and updated in the frame.</summary>
        internal static void AskDue(uint frame)
        {
            foreach (var schedule in s_All) schedule.Ask(frame);
        }

        void Ask(uint frame)
        {
            if (m_NextFrame > frame || m_Waiting != 0) return;
            // One more is waited for until every system is asked: an answer may come at once
            // (a request that fails does), and the question ends with its last answer.
            m_Waiting = 1;
            int asked = 0;
            try
            {
                foreach (var system in m_Systems)
                {
                    m_Waiting++;
                    bool did = false;
                    try { did = system.AskAlive(frame); }
                    finally { if (!did) m_Waiting--; }
                    if (did) asked++;
                }
            }
            finally
            {
                // Also when a request threw: the question is the answers asked for so far.
                if (asked == 0) m_Waiting = 0;
                else
                {
                    AskedFrame = frame;
                    Answered();
                }
            }
        }
    }
}
