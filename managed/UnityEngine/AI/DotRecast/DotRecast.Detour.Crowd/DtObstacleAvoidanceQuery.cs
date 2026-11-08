/*
Copyright (c) 2009-2010 Mikko Mononen memon@inside.org
recast4j copyright (c) 2015-2019 Piotr Piastucki piotr@jtilia.org
DotRecast Copyright (c) 2023-2024 Choi Ikpil ikpil@naver.com

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:
1. The origin of this software must not be misrepresented; you must not
 claim that you wrote the original software. If you use this software
 in a product, an acknowledgment in the product documentation would be
 appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
 misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
*/

using System;
using System.Runtime.CompilerServices;
using DotRecast.Core.Numerics;

namespace DotRecast.Detour.Crowd
{
    /// Unbound: velocity sampling as Unity's agents sample (tests/projects/avoidance, navcost
    /// `loop.redirect*`): the obstacles are kept relative to the agent in the horizontal plane;
    /// the desired velocity is the first candidate, then Detour's adaptive rings of candidates
    /// around a biased desired velocity; each is weighed by its distance from the desired and the
    /// current velocity and its time to impact within the prediction time.
    internal class DtObstacleAvoidanceQuery
    {
        public const int DT_MAX_PATTERN_DIVS = 32; // < Max numver of adaptive divs.
        public const int DT_MAX_PATTERN_RINGS = 4;

        /// Unbound: the agent's own sizes below which sampling gives up (Unity's).
        const float MinVelocity = 1e-5f;
        /// Unbound: how far right of the biased desired velocity the pattern is centred, as a share
        /// of the desired velocity (Unity's).
        const float CentreSideOffset = 0.04f;
        /// Unbound: an edge nearer than this to the agent counts as at least SegmentMinDistance from
        /// it on the edge's outer side, whichever side the agent is on (Unity's).
        const float SegmentTouchDistance = 0.05f;
        const float SegmentMinDistance = 0.025f;
        /// Unbound: an edge's time to impact counts double (it is divided by this; Unity's).
        const float SegmentTimeWeight = 0.5f;

        private DtObstacleAvoidanceParams m_params;
        private float m_invHorizTime;
        private float m_invVmax;

        private readonly int m_maxCircles;
        private readonly DtObstacleCircle[] m_circles;
        private int m_ncircles;

        private readonly int m_maxSegments;
        private readonly DtObstacleSegment[] m_segments;
        private int m_nsegments;

        public DtObstacleAvoidanceQuery(int maxCircles, int maxSegments)
        {
            m_maxCircles = maxCircles;
            m_circles = new DtObstacleCircle[m_maxCircles];
            for (int i = 0; i < m_maxCircles; i++)
                m_circles[i] = new DtObstacleCircle();

            m_maxSegments = maxSegments;
            m_segments = new DtObstacleSegment[m_maxSegments];
            for (int i = 0; i < m_maxSegments; i++)
                m_segments[i] = new DtObstacleSegment();
        }

        public void Reset()
        {
            m_ncircles = 0;
            m_nsegments = 0;
        }

        /// A circle at <paramref name="rel"/> from the agent (x, z) of the agent's and the
        /// neighbour's radii together, moving at <paramref name="vel"/>. <paramref name="weight"/> is
        /// the neighbour's share in the relative velocity (1: the agent avoids it alone, 0.5: both
        /// avoid each other).
        public void AddCircle(RcVec3f rel, float rad, RcVec3f vel, float weight)
        {
            if (m_ncircles >= m_maxCircles)
                return;

            DtObstacleCircle cir = m_circles[m_ncircles++];
            cir.p = new RcVec3f(rel.X, 0f, rel.Z);
            cir.rad = rad;
            cir.vel = new RcVec3f(vel.X, 0f, vel.Z);
            cir.weight = weight;
        }

        /// A NavMesh edge from <paramref name="p"/> to <paramref name="q"/>, relative to the agent,
        /// oriented as the NavMesh's wall edges are (the walkable side where p.z·dx − p.x·dz ≥ 0 puts
        /// the agent). An edge the agent is behind does not count; one nearer than
        /// SegmentTouchDistance counts, moved out to at least SegmentMinDistance from the agent.
        public void AddSegment(RcVec3f p, RcVec3f q)
        {
            if (m_nsegments >= m_maxSegments)
                return;

            float dx = q.X - p.X, dz = q.Z - p.Z;
            float len2 = dx * dx + dz * dz;
            float cx = p.X, cz = p.Z;
            if (len2 != 0f)
            {
                float t = Math.Clamp((-p.X * dx - p.Z * dz) / len2, 0f, 1f);
                cx += t * dx;
                cz += t * dz;
            }

            if (cx * cx + cz * cz < SegmentTouchDistance * SegmentTouchDistance)
            {
                float len = MathF.Sqrt(len2);
                if (len > 1e-6f)
                {
                    float ux = dx / len, uz = dz / len;
                    // The outward normal (-uz, ux); how far the edge's line is in front of the agent.
                    float push = MathF.Max(SegmentMinDistance - (ux * q.Z - uz * q.X), 0f);
                    p = new RcVec3f(p.X - uz * push, 0f, p.Z + ux * push);
                    q = new RcVec3f(q.X - uz * push, 0f, q.Z + ux * push);
                }
            }
            else if (p.Z * dx - p.X * dz < 0f)
            {
                return;
            }

            DtObstacleSegment seg = m_segments[m_nsegments++];
            seg.p = new RcVec3f(p.X, 0f, p.Z);
            seg.q = new RcVec3f(q.X, 0f, q.Z);
        }

        public int GetObstacleCircleCount() => m_ncircles;

        public DtObstacleCircle GetObstacleCircle(int i) => m_circles[i];

        public int GetObstacleSegmentCount() => m_nsegments;

        public DtObstacleSegment GetObstacleSegment(int i) => m_segments[i];

        /// The time the agent, at the origin, moving at <paramref name="vcand"/> takes to meet the
        /// nearest obstacle, at most the prediction time.
        private float TimeToImpact(RcVec3f vcand, RcVec3f vel)
        {
            float tmin = m_params.horizTime;
            for (int i = 0; i < m_ncircles; ++i)
            {
                DtObstacleCircle cir = m_circles[i];
                float w = cir.weight;
                float vx = vcand.X - ((1f - w) * vel.X + w * cir.vel.X);
                float vz = vcand.Z - ((1f - w) * vel.Z + w * cir.vel.Z);
                float a = vx * vx + vz * vz;
                if (a < 1e-4f)
                    continue; // not moving towards or away from it

                float b = vx * cir.p.X + vz * cir.p.Z;
                float c = cir.p.X * cir.p.X + cir.p.Z * cir.p.Z - cir.rad * cir.rad;
                float d = b * b - c * a;
                if (d < 0f)
                    continue; // no intersection

                float inva = 1f / a;
                float rd = MathF.Sqrt(d);
                float t = (b - rd) * inva;
                if (t < 0f)
                {
                    // Overlapping: the sooner it gets out, the more it counts.
                    float tout = (b + rd) * inva;
                    if (tout > 0f)
                        t = t * -2f / tout;
                }

                if (t >= 0f)
                    tmin = MathF.Min(t, tmin);
            }

            for (int i = 0; i < m_nsegments; ++i)
            {
                DtObstacleSegment seg = m_segments[i];
                float sx = seg.q.X - seg.p.X, sz = seg.q.Z - seg.p.Z;
                float den = sx * vcand.Z - sz * vcand.X;
                float t = float.MaxValue;
                if (MathF.Abs(den) >= 1e-6f)
                {
                    float inv = 1f / den;
                    float along = (-sz * seg.p.X + sx * seg.p.Z) * inv;
                    float s = (-seg.p.X * vcand.Z + seg.p.Z * vcand.X) * inv;
                    if (along >= 0f && s >= 0f && s <= 1f)
                        t = along;
                }

                tmin = MathF.Min(t / SegmentTimeWeight, tmin);
            }

            return tmin;
        }

        private float ProcessSample(RcVec3f vcand, float cs, RcVec3f vel, RcVec3f dvel, DtObstacleAvoidanceDebugData debug)
        {
            float tmin = TimeToImpact(vcand, vel);
            float vpen = m_params.weightDesVel * (RcVec.Dist2D(vcand, dvel) * m_invVmax);
            float vcpen = m_params.weightCurVel * (RcVec.Dist2D(vcand, vel) * m_invVmax);
            float tpen = 1f / (tmin * m_invHorizTime + 0.1f) * m_params.weightToi;
            float penalty = vpen + vcpen + tpen;
            debug?.AddSample(vcand, cs, penalty, vpen, vcpen, 0f, tpen);
            return penalty;
        }

        /// Unbound: the velocity the agent plans (in nvel): the desired velocity when there is
        /// nothing to avoid (its height kept), else the best candidate (horizontal). Returns the
        /// number of candidates weighed.
        public int SampleVelocityAdaptive(float vmax, RcVec3f vel, RcVec3f dvel, out RcVec3f nvel,
            DtObstacleAvoidanceParams option, DtObstacleAvoidanceDebugData debug)
        {
            debug?.Reset();
            if (option.horizTime < MinVelocity || (m_ncircles == 0 && m_nsegments == 0))
            {
                nvel = dvel;
                return 0;
            }

            nvel = RcVec3f.Zero;
            if (vmax < MinVelocity)
                return 0;

            m_params = option;
            m_invHorizTime = 1.0f / m_params.horizTime;
            m_invVmax = 1.0f / vmax;
            vel = new RcVec3f(vel.X, 0f, vel.Z);
            dvel = new RcVec3f(dvel.X, 0f, dvel.Z);

            // The pattern: the centre, then rings from the outside in, each of nd points at even
            // angles from the desired direction, every other ring turned by half a step.
            Span<float> pat = stackalloc float[(DT_MAX_PATTERN_DIVS * DT_MAX_PATTERN_RINGS + 1) * 2];
            // Unity's limits of the pattern (its presets stay within them).
            int nd = Math.Clamp(m_params.adaptiveDivs, 1, 7);
            int nr = Math.Clamp(m_params.adaptiveRings, 1, 3);
            int depth = m_params.adaptiveDepth;
            float da = 6.2831855f / nd;
            float heading = MathF.Atan2(dvel.Z, dvel.X);
            pat[0] = 0f;
            pat[1] = 0f;
            int npat = 1;
            for (int j = 0; j < nr; ++j)
            {
                float r = (float)(nr - j) / nr;
                float angle = (j & 1) * 0.5f * da + heading;
                for (int i = 0; i < nd; ++i)
                {
                    pat[npat * 2 + 0] = MathF.Cos(angle) * r;
                    pat[npat * 2 + 1] = MathF.Sin(angle) * r;
                    npat++;
                    angle += da;
                }
            }

            float cr = (1.0f - m_params.velBias) * vmax;
            float limit = (vmax + 0.001f) * (vmax + 0.001f);
            float resX = dvel.X * m_params.velBias + dvel.Z * CentreSideOffset;
            float resZ = dvel.Z * m_params.velBias - dvel.X * CentreSideOffset;
            int ns = 0;
            for (int k = 0; k < depth; ++k)
            {
                float minPenalty = float.MaxValue;
                float bx = 0f, bz = 0f;
                for (int i = 0; i < npat; ++i)
                {
                    RcVec3f vcand;
                    if (k == 0 && i == 0)
                    {
                        vcand = dvel;
                    }
                    else
                    {
                        vcand = new RcVec3f(pat[i * 2 + 0] * cr + resX, 0f, pat[i * 2 + 1] * cr + resZ);
                        if (vcand.X * vcand.X + vcand.Z * vcand.Z > limit)
                            continue;
                    }

                    float penalty = ProcessSample(vcand, cr / 5f, vel, dvel, debug);
                    ns++;
                    if (penalty < minPenalty)
                    {
                        bx = vcand.X;
                        bz = vcand.Z;
                    }

                    minPenalty = MathF.Min(minPenalty, penalty);
                }

                resX = bx;
                resZ = bz;
                cr *= 0.5f;
            }

            nvel = new RcVec3f(resX, 0f, resZ);
            return ns;
        }
    }
}
