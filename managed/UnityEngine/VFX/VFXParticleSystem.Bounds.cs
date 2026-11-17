using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;

namespace Unbound.Runtime
{
    /// <summary>
    /// Bounds computed from a system's particles (an Initialize context whose bounds mode is
    /// Automatic, or a graph saved while its bounds were being recorded). The last update
    /// kernel of such a system reduces, over the particles alive, the box around each one's
    /// sphere (in the effect's space, also of a system in world space) into six words of a
    /// buffer of the system: the least and the greatest corner, as bits that order like the
    /// floats they stand for. The buffer is emptied before the kernels of a frame's last step,
    /// so a frame's box is of the particles that step left alive, and read back once a frame:
    /// an answer is taken after the frame's steps, grown by the padding the system has then,
    /// and the next one asked for. So scripts see the box of the frame before, and the
    /// renderer, whose bounds are taken during the steps, the one before that; without a
    /// particle alive the box is the padding around the effect's origin. Matches the Unity
    /// 6000.0.71f1 Editor (tests/projects/vfxbounds), but for effects of one instanced graph
    /// whose system kills no particle: there the Editor gives the first effect of a batch
    /// the particles of them all and the others none, and each effect here has its own.
    /// </summary>
    internal sealed partial class VFXParticleSystem
    {
        const string BoundsBufferName = "boundsBuffer";
        /// <summary>The words of a system's box: the least corner, then the greatest.</summary>
        const int BoundsWords = 6;

        /// <summary>The box of no particle: every least word the greatest value and every
        /// greatest word the least, which decode as no number.</summary>
        static readonly uint[] s_NoBounds = { uint.MaxValue, uint.MaxValue, uint.MaxValue, 0u, 0u, 0u };

        int m_BoundsBuffer = -1;
        bool m_ComputesBounds, m_AutomaticBounds;
        Vector3 m_ComputedMin, m_ComputedMax;
        /// <summary>Whether an answer is on its way, how many answers still to come are from
        /// before the system started over, and the answer that came and is not taken yet.</summary>
        bool m_BoundsAsked, m_BoundsArrived;
        int m_BoundsForgotten;
        readonly uint[] m_BoundsWords = new uint[BoundsWords];
        Action<AsyncGPUReadbackRequest> m_OnBounds;

        void SetUpBounds()
        {
            m_BoundsBuffer = Value(Desc.buffers, BoundsBufferName);
            m_ComputesBounds = (Desc.flags & VFXSystemFlag.SystemNeedsComputeBounds) != 0 && m_BoundsBuffer >= 0;
            m_AutomaticBounds = m_ComputesBounds && (Desc.flags & VFXSystemFlag.SystemAutomaticBounds) != 0;
        }

        /// <summary>Why a system's computed bounds are not run, null when they are (or it
        /// computes none): the graph compiler gives such a system a buffer for them.</summary>
        internal static string BoundsRefusal(in UnityEditor.VFX.VFXEditorSystemDesc system)
        {
            const VFXSystemFlag Computed = VFXSystemFlag.SystemNeedsComputeBounds | VFXSystemFlag.SystemAutomaticBounds;
            if ((system.flags & Computed) == 0 || Value(system.buffers, BoundsBufferName) >= 0) return null;
            return $"the system '{system.name}' that computes its bounds without a buffer for them";
        }

        /// <summary>The box computed from the system's particles, grown by its padding: zero
        /// for a system that computes none.</summary>
        internal Bounds ComputedBounds => m_ComputesBounds ? Box(m_ComputedMin, m_ComputedMax) : default;

        /// <summary>The system's bounds padding now (zero without one).</summary>
        internal Vector3 BoundsPadding => m_BoundsPadding >= 0 ? (Vector3)Effect.Values.GetVector(m_BoundsPadding, 3) : Vector3.zero;

        /// <summary>A box from its corners: its centre halfway, its extents half the way.</summary>
        internal static Bounds Box(Vector3 min, Vector3 max)
        {
            var box = default(Bounds);
            box.center = (min + max) * 0.5f;
            box.extents = (max - min) * 0.5f;
            return box;
        }

        void ResetBounds()
        {
            m_ComputedMin = m_ComputedMax = Vector3.zero;
            m_BoundsArrived = false;
            if (m_BoundsAsked)
            {
                m_BoundsAsked = false;
                m_BoundsForgotten++;
            }
        }

        /// <summary>Before the kernels of a frame's last step: the box of no particle.</summary>
        void EmptyBounds()
        {
            if (!m_ComputesBounds || !m_HasRenderer) return;
            Effect.GpuBuffer(m_BoundsBuffer, BoundsWords).SetData(s_NoBounds, 0, 0, BoundsWords);
        }

        /// <summary>After a frame's steps: takes the answer that came (the box, grown by the
        /// padding the system has now) and asks for the box the kernels computed in this
        /// frame, unless an answer is still on its way.</summary>
        internal void AskBounds()
        {
            if (!m_ComputesBounds || !m_HasRenderer || !m_KernelsUsable) return;
            if (m_BoundsArrived)
            {
                m_BoundsArrived = false;
                var padding = BoundsPadding;
                m_ComputedMin = new Vector3(Decoded(m_BoundsWords[0]), Decoded(m_BoundsWords[1]), Decoded(m_BoundsWords[2])) - padding;
                m_ComputedMax = new Vector3(Decoded(m_BoundsWords[3]), Decoded(m_BoundsWords[4]), Decoded(m_BoundsWords[5])) + padding;
            }
            if (m_BoundsAsked) return;
            m_BoundsAsked = true;
            try
            {
                AsyncGPUReadback.Request(Effect.GpuBuffer(m_BoundsBuffer, BoundsWords), BoundsWords * sizeof(uint), 0, m_OnBounds ??= OnBounds);
            }
            catch
            {
                m_BoundsAsked = false;
                throw;
            }
        }

        void OnBounds(AsyncGPUReadbackRequest request)
        {
            // A system that started over since it asked takes no answer from before.
            if (m_BoundsForgotten != 0)
            {
                m_BoundsForgotten--;
                return;
            }
            m_BoundsAsked = false;
            if (request.hasError) return;
            var words = request.GetData<uint>();
            for (int i = 0; i < BoundsWords; i++) m_BoundsWords[i] = words[i];
            m_BoundsArrived = true;
        }

        /// <summary>The float a word of the box stands for: the kernels flip a float's bits so
        /// that unsigned integers order as the floats do (the sign bit of a positive one, every
        /// bit of a negative one). What is no number (the box of no particle) is zero.</summary>
        internal static float Decoded(uint word)
        {
            float value = BitConverter.UInt32BitsToSingle(word ^ ((int)word < 0 ? 0x80000000u : uint.MaxValue));
            return float.IsNaN(value) ? 0f : value;
        }
    }
}
