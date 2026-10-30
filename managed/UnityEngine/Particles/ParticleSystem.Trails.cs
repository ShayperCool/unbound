using System.Collections.Generic;
using R = Unbound.Runtime.ParticleRandom;

namespace UnityEngine
{
    // The trails module in Particles mode: a particle leaves a point wherever it has moved
    // minVertexDistance from the last one; a point lives the trail lifetime (a multiplier of the
    // particle's start lifetime); the renderer draws each trail as a camera-facing ribbon from the
    // particle back along its points, with the trail material.
    public sealed partial class ParticleSystem
    {
        internal sealed class Trail
        {
            internal readonly List<Vector3> Points = new List<Vector3>();
            internal readonly List<float> Times = new List<float>();
            /// <summary>How long a point lives.</summary>
            internal float Lifetime;
            /// <summary>Where the particle is now (the head; it stays put once the particle died).</summary>
            internal Vector3 Head;
            internal Color32 Color;
            internal float Width;
            internal float Age01;
            internal bool Orphan;
            internal float HeadTime;
            internal bool Ribbon;
            internal List<float> Widths;
            internal List<Color32> Colors;
            internal float WidthRandom, ColorRandom;
        }

        /// <summary>Trails by id (a particle's m_TrailId − 1); null slots are free.</summary>
        internal readonly List<Trail> m_TrailList = new List<Trail>();
        readonly Stack<int> m_FreeTrails = new Stack<int>();
        internal float m_TrailTime;
        bool HasAliveTrails => m_TrailList.Count > m_FreeTrails.Count;

        bool TrailsEnabled => m_Trails.enabled;

        /// <summary>A newborn particle gets its trail (or not, by the ratio).</summary>
        void StartTrail(ref Particle p)
        {
            p.m_TrailId = 0;
            if (!m_Trails.enabled) return;
            if (m_Trails.mode != ParticleSystemTrailMode.PerParticle) return;
            if (m_Trails.ratio < 1f && R.Value(p.m_RandomSeed, R.Trail) >= m_Trails.ratio) return;
            var trail = new Trail();
            int id = m_FreeTrails.Count > 0 ? m_FreeTrails.Pop() : m_TrailList.Count;
            if (id == m_TrailList.Count) m_TrailList.Add(trail);
            else m_TrailList[id] = trail;
            float lifetime = m_Trails.lifetime.Evaluate(0f, TrailRandom(p.m_RandomSeed, 0x34bbab1bu)) * p.m_StartLifetime;
            trail.Lifetime = lifetime;
            trail.WidthRandom = TrailRandom(p.m_RandomSeed, 0xfedc345bu);
            trail.ColorRandom = TrailRandom(p.m_RandomSeed, 0x6cf2ac20u);
            if (m_Trails.sizeAffectsLifetime)
            {
                var size = CurrentSize3D(ref p);
                trail.Lifetime *= Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            }
            p.m_TrailId = id + 1;
            UpdateTrailHead(ref p, trail);
        }

        Vector3 TrailSpacePoint(Vector3 simulationPoint) =>
            m_Trails.worldSpace ? SimulationToWorld().MultiplyPoint3x4(simulationPoint) : simulationPoint;

        internal static float TrailRandom(uint seed, uint salt)
        {
            var state = Random.State.FromSeed(unchecked((int)(seed + salt)));
            return state.NextValue();
        }

        internal static Color32 TrailColor(Color color) => new Color32(
            (byte)(Mathf.Clamp01(color.r) * 255f + .5f), (byte)(Mathf.Clamp01(color.g) * 255f + .5f),
            (byte)(Mathf.Clamp01(color.b) * 255f + .5f), (byte)(Mathf.Clamp01(color.a) * 255f + .5f));

        // Native trail gradients are RGBA32 before their asymmetric byte tint multiply.
        internal static Color32 TrailTint(Color32 particle, Color32 gradient) => new Color32(
            (byte)(((particle.r + 1) * gradient.r) >> 8),
            (byte)(((particle.g + 1) * gradient.g) >> 8),
            (byte)(((particle.b + 1) * gradient.b) >> 8),
            (byte)(((particle.a + 1) * gradient.a) >> 8));

        void UpdateTrailHead(ref Particle p, Trail trail)
        {
            trail.Head = TrailSpacePoint(p.m_Position);
            trail.HeadTime = m_TrailTime;
            var size = CurrentSize3D(ref p);
            float width = size.x;
            if (hasNonUniformParticleSizes)
                width = GetComponent<ParticleSystemRenderer>()?.renderMode == ParticleSystemRenderMode.Mesh
                    ? Mathf.Pow(size.x * size.y * size.z, 1f / 3f) : Mathf.Sqrt(size.x * size.y);
            trail.Width = m_Trails.sizeAffectsWidth ? width : 1f;
            trail.Age01 = p.Age01;
            Color32 color = m_Trails.inheritParticleColor ? CurrentColor(ref p) : new Color32(255, 255, 255, 255);
            // Lifetime tint adds one to the gradient operand, the reverse of the trail tint.
            trail.Color = TrailTint(TrailColor(m_Trails.colorOverLifetime.Evaluate(p.Age01,
                TrailRandom(p.m_RandomSeed, 0x591bc05cu))), color);
        }

        /// <summary>After a step: each trail takes a point where its particle moved far enough and
        /// drops the points that lived out their time; orphaned trails only fade.</summary>
        void UpdateTrails()
        {
            if (m_TrailList.Count == 0) return;
            float minDistance = Mathf.Max(m_Trails.minVertexDistance, 0f);
            for (int i = 0; i < m_Count; i++)
            {
                ref var p = ref m_Particles[i];
                if (p.m_TrailId == 0) continue;
                var trail = m_TrailList[p.m_TrailId - 1];
                if (trail == null || trail.Orphan) continue;
                UpdateTrailHead(ref p, trail);
                if (p.m_AgePercent >= 100f) continue;
                if (trail.Points.Count == 0 || (trail.Head - trail.Points[trail.Points.Count - 1]).sqrMagnitude > minDistance * minDistance)
                {
                    trail.Points.Add(trail.Head);
                    trail.Times.Add(m_TrailTime);
                }
            }
            for (int id = 0; id < m_TrailList.Count; id++)
            {
                var trail = m_TrailList[id];
                if (trail == null) continue;
                int expired = 0;
                while (expired < trail.Times.Count && m_TrailTime - trail.Times[expired] > trail.Lifetime) expired++;
                if (expired == trail.Times.Count && trail.Orphan) { FreeTrail(id); continue; }
                // Keep one expired anchor so the renderer can cut the tail at its exact age.
                expired = Mathf.Max(0, expired - 1);
                if (expired > 0)
                {
                    trail.Points.RemoveRange(0, expired);
                    trail.Times.RemoveRange(0, expired);
                }
                if (trail.Orphan && trail.Points.Count == 0) FreeTrail(id);
            }
        }

        /// <summary>A particle died: its trail goes with it, or stays to fade.</summary>
        bool KeepOrphanTrail(ref Particle p)
        {
            if (m_Trails.dieWithParticles || p.m_TrailId == 0) return false;
            var trail = m_TrailList[p.m_TrailId - 1];
            if (trail == null) return false;
            trail.Orphan = true;
            p.m_Flags |= Particle.FlagOrphanTrail;
            p.m_Lifetime = 0f;
            p.m_Velocity = p.m_AnimatedVelocity = Vector3.zero;
            return true;
        }

        void EndTrail(ref Particle p)
        {
            if (p.m_TrailId == 0) return;
            int id = p.m_TrailId - 1;
            p.m_TrailId = 0;
            var trail = id < m_TrailList.Count ? m_TrailList[id] : null;
            if (trail == null) return;
            if (m_Trails.dieWithParticles) FreeTrail(id);
            else trail.Orphan = true;
        }

        void FreeTrail(int id)
        {
            if (m_TrailList[id]?.Orphan == true)
                for (int i = m_Count - 1; i >= 0; i--)
                    if (m_Particles[i].m_TrailId == id + 1) RemoveAt(i);
            m_TrailList[id] = null;
            m_FreeTrails.Push(id);
        }

        internal void ClearTrails()
        {
            m_TrailList.Clear();
            m_FreeTrails.Clear();
            m_TrailTime = 0f;
        }

        internal IEnumerable<Trail> RenderTrails()
        {
            if (m_Trails.mode == ParticleSystemTrailMode.PerParticle)
            {
                foreach (var trail in m_TrailList) if (trail != null) yield return trail;
                yield break;
            }
            int ribbons = Mathf.Max(1, m_Trails.ribbonCount);
            if (m_Count / ribbons < 2) yield break;
            for (int offset = 0; offset < Mathf.Min(ribbons, m_Count); offset++)
            {
                int group = (m_Count - 1 - offset) % ribbons;
                var trail = new Trail { Ribbon = true, Lifetime = float.PositiveInfinity,
                    Widths = new List<float>(), Colors = new List<Color32>() };
                for (int i = group; i < m_Count; i += ribbons)
                {
                    UpdateTrailHead(ref m_Particles[i], trail);
                    trail.Points.Add(trail.Head); trail.Times.Add(m_TrailTime);
                    trail.Widths.Add(trail.Width); trail.Colors.Add(trail.Color);
                }
                if (trail.Points.Count > 1) yield return trail;
            }
        }
    }
}
