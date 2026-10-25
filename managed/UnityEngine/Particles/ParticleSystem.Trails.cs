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
        }

        /// <summary>Trails by id (a particle's m_TrailId − 1); null slots are free.</summary>
        internal readonly List<Trail> m_TrailList = new List<Trail>();
        readonly Stack<int> m_FreeTrails = new Stack<int>();

        bool TrailsEnabled => m_Trails.enabled;

        /// <summary>A newborn particle gets its trail (or not, by the ratio).</summary>
        void StartTrail(ref Particle p)
        {
            p.m_TrailId = 0;
            if (!m_Trails.enabled) return;
            if (m_Trails.mode != ParticleSystemTrailMode.PerParticle)
            {
                ReportUnsupported("ribbon trails");
                return;
            }
            if (m_Trails.ratio < 1f && R.Value(p.m_RandomSeed, R.Trail) >= m_Trails.ratio) return;
            var trail = new Trail();
            int id = m_FreeTrails.Count > 0 ? m_FreeTrails.Pop() : m_TrailList.Count;
            if (id == m_TrailList.Count) m_TrailList.Add(trail);
            else m_TrailList[id] = trail;
            float lifetime = m_Trails.lifetime.Evaluate(0f, R.Value(p.m_RandomSeed, R.Trail + 1)) * p.m_StartLifetime;
            trail.Lifetime = lifetime;
            var head = TrailSpacePoint(p.m_Position);
            trail.Points.Add(head);
            trail.Times.Add(m_TotalTime);
            p.m_TrailId = id + 1;
            UpdateTrailHead(ref p, trail);
        }

        Vector3 TrailSpacePoint(Vector3 simulationPoint) =>
            m_Trails.worldSpace ? SimulationToWorld().MultiplyPoint3x4(simulationPoint) : simulationPoint;

        void UpdateTrailHead(ref Particle p, Trail trail)
        {
            trail.Head = TrailSpacePoint(p.m_Position);
            var size = CurrentSize3D(ref p);
            trail.Width = m_Trails.sizeAffectsWidth ? Mathf.Max(size.x, size.y) : 1f;
            trail.Age01 = p.Age01;
            Color color = m_Trails.inheritParticleColor ? (Color)CurrentColor(ref p) : Color.white;
            color *= m_Trails.colorOverLifetime.Evaluate(p.Age01, R.Value(p.m_RandomSeed, R.Trail + 2));
            trail.Color = color;
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
                if (trail == null) continue;
                UpdateTrailHead(ref p, trail);
                var last = trail.Points[trail.Points.Count - 1];
                if ((trail.Head - last).sqrMagnitude >= minDistance * minDistance)
                {
                    trail.Points.Add(trail.Head);
                    trail.Times.Add(m_TotalTime);
                }
            }
            for (int id = 0; id < m_TrailList.Count; id++)
            {
                var trail = m_TrailList[id];
                if (trail == null) continue;
                int expired = 0;
                while (expired < trail.Times.Count && m_TotalTime - trail.Times[expired] > trail.Lifetime) expired++;
                if (expired > 0)
                {
                    trail.Points.RemoveRange(0, expired);
                    trail.Times.RemoveRange(0, expired);
                }
                if (trail.Orphan && trail.Points.Count == 0) FreeTrail(id);
            }
        }

        /// <summary>A particle died: its trail goes with it, or stays to fade.</summary>
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
            m_TrailList[id] = null;
            m_FreeTrails.Push(id);
        }

        internal void ClearTrails()
        {
            m_TrailList.Clear();
            m_FreeTrails.Clear();
        }
    }
}
