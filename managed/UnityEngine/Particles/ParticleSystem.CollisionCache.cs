using System.Collections.Generic;

namespace UnityEngine
{
    // World collisions at Medium and Low quality, as the 6000.0 Manual describes them ("a grid of
    // voxels to cache previous collisions", "only suitable for static colliders", Medium making
    // more physics queries per frame than Low) and as measured in the Unity 6000.0.71f1 Editor
    // (tests/projects/particlerender collide/world/slope_medium, slope_low, cache_*):
    // - Each frame the systems that may query share QualitySettings.particleRaycastBudget evenly
    //   (each at most its particle count, the rest handed on). A Medium system may query every
    //   frame, a Low one every fifth frame (from the fifth after its quality is first seen); a
    //   paused one (advanced by Simulate) never, so it collides only with planes already cached.
    // - Each collision pass queries for a window of the budget's size from a rolling offset; a
    //   queried particle sweeps four voxels ahead along its movement, and the plane it finds (the
    //   contact and normal) is stored for the voxel of its start and the main axis of its
    //   direction. Any other particle takes the plane stored for its own voxel and direction.
    // - A particle collides when its step crosses that plane, at the crossing.
    // Voxels are voxelSize wide along x and z and four times that along y.
    public sealed partial class ParticleSystem
    {
        struct VoxelKey : System.IEquatable<VoxelKey>
        {
            internal int x, y, z, axis;
            public bool Equals(VoxelKey o) => x == o.x && y == o.y && z == o.z && axis == o.axis;
            public override bool Equals(object o) => o is VoxelKey k && Equals(k);
            public override int GetHashCode() => System.HashCode.Combine(x, y, z, axis);
        }

        struct CachedPlane
        {
            internal Vector3 point, normal;
            internal Collider collider;
        }

        /// <summary>The planes found so far, by voxel and direction (kept for the system's life),
        /// and the voxel size and simulation space they were stored for: a change of either
        /// empties the cache (Unbound's choice, unmeasured), as its keys and planes would mean
        /// other places.</summary>
        readonly Dictionary<VoxelKey, CachedPlane> m_PlaneCache = new Dictionary<VoxelKey, CachedPlane>();
        float m_PlaneCacheVoxel;
        ParticleSystemSimulationSpace m_PlaneCacheSpace;

        /// <summary>The queries this frame gives the system; those left in the current update; where
        /// the next pass's window starts; the window of the current pass.</summary>
        int m_AssignedRays, m_Rays, m_RayOffset, m_RayWindowStart, m_RayWindowEnd;
        /// <summary>Frames left before a Low system may query again, and the quality it was counted for.</summary>
        int m_RayCountdown;
        ParticleSystemCollisionQuality m_RayQuality = ParticleSystemCollisionQuality.High;

        /// <summary>Frames a system waits between frames it may query, by quality (High, Medium, Low).</summary>
        static readonly int[] s_RayIntervals = { 0, 0, 4 };
        /// <summary>A cached query sweeps this many voxels ahead.</summary>
        const float CacheSweepVoxels = 4f;
        /// <summary>Voxels are this many times taller (y) than wide.</summary>
        const float VoxelHeightScale = 4f;
        /// <summary>A step moving along a cached plane within this cosine-scaled length crosses nothing.</summary>
        const float CacheParallelLimit = 1.1920929e-7f;
        static readonly List<ParticleSystem> s_RaySystems = new List<ParticleSystem>();

        bool UsesCollisionCache => m_Collision.enabled && m_Collision.type == ParticleSystemCollisionType.World
            && m_Collision.mode == ParticleSystemCollisionMode.Collision3D && m_Collision.quality != ParticleSystemCollisionQuality.High;

        /// <summary>Shares the frame's queries out among the systems (see the class comment).</summary>
        internal static void AssignRayBudgets(ParticleSystem[] systems, int n)
        {
            s_RaySystems.Clear();
            for (int i = 0; i < n; i++)
            {
                var ps = systems[i];
                if (ps == null || !ps.m_Registered) continue;
                var quality = ps.m_Collision.quality;
                int interval = s_RayIntervals[Mathf.Clamp((int)quality, 0, s_RayIntervals.Length - 1)];
                if (quality != ps.m_RayQuality)
                {
                    ps.m_RayCountdown = interval;
                    ps.m_RayQuality = quality;
                }
                ps.m_AssignedRays = 0;
                // A paused system (one Simulate advances) takes no share and does not query
                // (6000.0.71f1, tests/projects/particlerender collide/cache/simulate_medium).
                if (ps.m_RayCountdown == 0 && !ps.m_IsPaused && ps.m_Collision.enabled && ps.m_Collision.type == ParticleSystemCollisionType.World
                    && ps.m_GameObject != null && ps.m_GameObject.activeInHierarchy)
                {
                    if (quality != ParticleSystemCollisionQuality.High) s_RaySystems.Add(ps);
                    else ps.m_AssignedRays = ps.m_Count;
                }
                ps.m_RayCountdown = ps.m_RayCountdown == 0 ? interval : ps.m_RayCountdown - 1;
            }
            int count = s_RaySystems.Count;
            if (count == 0) return;
            int left = QualitySettings.particleRaycastBudget;
            int share = Mathf.Max(0, left / count);
            foreach (var ps in s_RaySystems)
            {
                ps.m_AssignedRays = Mathf.Min(share, ps.m_Count);
                left = Mathf.Max(0, left - ps.m_AssignedRays);
            }
            foreach (var ps in s_RaySystems)
            {
                int more = Mathf.Min(ps.m_Count - ps.m_AssignedRays, left);
                ps.m_AssignedRays += more;
                left -= more;
            }
            s_RaySystems.Clear();
        }

        /// <summary>A collision pass over the slots <paramref name="start"/> to
        /// <paramref name="end"/>: the window of slots that query, from the rolling offset.</summary>
        void BeginRayPass(int start, int end)
        {
            if (!UsesCollisionCache) return;
            if (m_PlaneCacheVoxel != CacheVoxel || m_PlaneCacheSpace != m_Main.simulationSpace)
            {
                m_PlaneCache.Clear();
                m_PlaneCacheVoxel = CacheVoxel;
                m_PlaneCacheSpace = m_Main.simulationSpace;
            }
            int offset = m_RayOffset >= start && m_RayOffset < end ? m_RayOffset : start;
            int windowEnd = (int)System.Math.Min((long)offset + m_Rays, end);
            m_RayOffset = offset + m_Rays;
            m_RayWindowStart = offset;
            m_RayWindowEnd = windowEnd;
            m_Rays = (System.Math.Max(0, m_Rays - (windowEnd - offset)) + 3) & ~3;
        }

        /// <summary>Whether the particle of <paramref name="slot"/> queries. A particle collided
        /// outside a pass (no slot) queries while the update has queries left and uses one up,
        /// leaving the rolling offset of the passes alone (Unbound's choice; unmeasured).</summary>
        bool QueriesInPass(int slot)
        {
            if (slot >= 0) return slot >= m_RayWindowStart && slot < m_RayWindowEnd;
            if (m_Rays <= 0) return false;
            m_Rays--;
            return true;
        }

        static VoxelKey KeyOf(Vector3 position, Vector3 direction, float voxel)
        {
            var key = new VoxelKey
            {
                x = (int)Mathf.Floor(position.x / voxel),
                y = (int)Mathf.Floor(position.y / (voxel * VoxelHeightScale)),
                z = (int)Mathf.Floor(position.z / voxel),
            };
            float ax = Mathf.Abs(direction.x), ay = Mathf.Abs(direction.y), az = Mathf.Abs(direction.z);
            if (ax >= ay && ax >= az) key.axis = direction.x <= 0f ? 1 : 0;
            else if (ay >= az) key.axis = direction.y > 0f ? 2 : 3;
            else key.axis = direction.z > 0f ? 4 : 5;
            return key;
        }

        /// <summary>A world collision at Medium or Low quality (see the class comment).</summary>
        bool CachedWorldContact(Vector3 end, Vector3 velocity, float radius, float dt, in StepContext c,
            out Vector3 contact, out Vector3 normal, out Collider hit)
        {
            contact = normal = default;
            hit = null;
            float voxel = CacheVoxel;
            var w1 = c.SimToWorld.MultiplyPoint3x4(end);
            var w0 = c.SimToWorld.MultiplyPoint3x4(end - velocity * dt);
            var move = w1 - w0;
            CachedPlane plane;
            if (QueriesInPass(m_IntegrateSlot))
            {
                float length = move.magnitude;
                if (!(length > MinSweepLength)) return false;
                var direction = move / length;
                if (!SweepContact(w0, direction, CacheSweepVoxels * voxel, radius, out var worldContact, out var worldNormal, out var collider))
                    return false;
                plane = new CachedPlane
                {
                    point = c.WorldToSimMatrix.MultiplyPoint3x4(worldContact),
                    normal = c.WorldToSimMatrix.MultiplyVector(worldNormal).normalized,
                    collider = collider,
                };
                m_PlaneCache[KeyOf(w0, direction, voxel)] = plane;
            }
            else if (!m_PlaneCache.TryGetValue(KeyOf(w0, move, voxel), out plane)) return false;
            return CrossesPlane(plane, end, velocity, dt, out contact, out normal, out hit);
        }

        float CacheVoxel => Mathf.Max(m_Collision.voxelSize, 1e-4f);

        /// <summary>Whether a particle's cache lookup waits for the end of its collision pass: one
        /// before the pass's querying window reads the planes the window finds in the same pass.</summary>
        bool DefersCacheLookup(int slot) => UsesCollisionCache && slot >= 0 && slot < m_RayWindowStart && m_RayWindowEnd > m_RayWindowStart;

        /// <summary>The lookup of a hit queued by <see cref="DefersCacheLookup"/>, at the end of its
        /// pass: fills the contact; false when it meets no cached plane.</summary>
        bool ResolveCacheLookup(ref PendingCollision h, in StepContext c)
        {
            var w0 = c.SimToWorld.MultiplyPoint3x4(h.end - h.velocity * h.dt);
            var move = c.SimToWorld.MultiplyPoint3x4(h.end) - w0;
            if (!m_PlaneCache.TryGetValue(KeyOf(w0, move, CacheVoxel), out var plane)) return false;
            if (!CrossesPlane(plane, h.end, h.velocity, h.dt, out h.contact, out h.normal, out var collider)) return false;
            h.other = collider != null ? collider.gameObject : null;
            h.colliderId = collider != null ? collider.GetInstanceID() : 0;
            if (m_Collision.colliderForce != 0f && collider != null) ReportColliderForce(collider.attachedRigidbody);
            return true;
        }

        /// <summary>Whether the step of a particle that ended at <paramref name="end"/> crosses a
        /// cached plane moving into it, and where.</summary>
        static bool CrossesPlane(in CachedPlane plane, Vector3 end, Vector3 velocity, float dt, out Vector3 contact, out Vector3 normal, out Collider hit)
        {
            contact = normal = default;
            hit = null;
            var step = velocity * dt;
            var from = end - step;
            float into = Vector3.Dot(plane.normal, step);
            if (!(into < 0f) || -into < CacheParallelLimit) return false;
            float t = Vector3.Dot(plane.point - from, plane.normal) / into;
            if (t < 0f || t > 1f) return false;
            contact = from + step * t;
            normal = plane.normal;
            hit = plane.collider;
            return true;
        }
    }
}
