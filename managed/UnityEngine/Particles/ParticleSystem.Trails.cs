using System;
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
            /// <summary>The particle died and the trail only fades (dieWithParticles off).</summary>
            internal bool Orphan;
            /// <summary>A fading trail whose points all expired: the dead particle carrying it leaves,
            /// and frees it, in the next update.</summary>
            internal bool Spent;
            /// <summary>The <see cref="RenderTrails"/> pass that last returned this trail.</summary>
            internal int RenderPass;
            internal float HeadTime;
            internal bool Ribbon;
            internal List<float> Widths;
            internal List<Color32> Colors;
            internal float WidthRandom, ColorRandom;
            internal Vector3? BirthPosition;

            /// <summary>A deep copy: a capture and the live trail never share points.</summary>
            internal Trail Copy()
            {
                var copy = new Trail
                {
                    Lifetime = Lifetime, Head = Head, Color = Color, Width = Width, Age01 = Age01,
                    Orphan = Orphan, Spent = Spent, HeadTime = HeadTime, Ribbon = Ribbon,
                    Widths = Widths != null ? new List<float>(Widths) : null,
                    Colors = Colors != null ? new List<Color32>(Colors) : null,
                    WidthRandom = WidthRandom, ColorRandom = ColorRandom, BirthPosition = BirthPosition,
                };
                copy.Points.AddRange(Points);
                copy.Times.AddRange(Times);
                return copy;
            }
        }

        /// <summary>What <c>GetTrails</c> captures: each particle's trail points by particle index,
        /// for <c>SetTrails</c>. Unity's <c>Trails</c> fields are internal (only <c>capacity</c> is
        /// public), so this is Unbound's representation beside them.</summary>
        internal sealed class TrailCapture
        {
            /// <summary>Copies of the particles' trails by particle index; null where a particle
            /// had none.</summary>
            internal readonly List<Trail> Slots = new List<Trail>();
            /// <summary>The trail clock at the capture (point times are taken relative to it).</summary>
            internal float TrailTime;
        }

        /// <summary>The capture each Trails value holds, keyed by the storage list GetTrails gives it
        /// (copies of a Trails value share that list, so they share the capture, and GetTrails(ref)
        /// refills it in place). Kept beside the struct so its fields stay as they are.</summary>
        static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<List<Vector4>, TrailCapture> s_TrailCaptures =
            new global::System.Runtime.CompilerServices.ConditionalWeakTable<List<Vector4>, TrailCapture>();

        static TrailCapture CaptureOf(in Trails trailData) =>
            trailData.positions != null && s_TrailCaptures.TryGetValue(trailData.positions, out var capture) ? capture : null;

        /// <summary>Trails by id (a particle's m_TrailId − 1); null slots are free.</summary>
        internal readonly List<Trail> m_TrailList = new List<Trail>();
        readonly Stack<int> m_FreeTrails = new Stack<int>();
        internal float m_TrailTime;
        internal bool HasAliveTrails => m_TrailList.Count > m_FreeTrails.Count;

        bool TrailsEnabled => m_Trails.enabled;

        /// <summary>The salt of the per-particle draw the trail ratio compares with: a particle gets a
        /// trail when the draw is below the ratio (6000.0.71f1, tests/projects/particletrails
        /// pp/ratiobig, 600 seeds at ratios 0.5 and 0.25; 0x8abff360 fits them equally).</summary>
        const uint TrailRatioSalt = 0x0abff360u;

        /// <summary>Whether a particle with this seed gets a trail under the module's ratio.</summary>
        internal static bool GetsTrail(uint seed, float ratio) => ratio >= 1f || TrailRandom(seed, TrailRatioSalt) < ratio;

        /// <summary>A newborn particle gets its trail (or not, by the ratio).</summary>
        void StartTrail(ref Particle p, Vector3? birthPosition = null)
        {
            p.m_TrailId = 0;
            if (!m_Trails.enabled) return;
            if (m_Trails.mode != ParticleSystemTrailMode.PerParticle) return;
            if (!GetsTrail(p.m_RandomSeed, m_Trails.ratio)) return;
            var trail = NewTrail(ref p);
            p.m_TrailId = AddTrail(trail) + 1;
            trail.BirthPosition = birthPosition.HasValue ? TrailSpacePoint(birthPosition.Value) : null;
        }

        /// <summary>A particle written into a live slot keeps the slot's trail, or its lack of one
        /// (what the trail ratio gave the slot's particle at birth); one written past the live
        /// slots is new and gets a trail by the ratio.</summary>
        void StartWrittenTrail(ref Particle p, bool newSlot)
        {
            if (newSlot) StartTrail(ref p);
        }

        /// <summary>Freed trails kept for reuse, so a birth does not allocate one.</summary>
        readonly Stack<Trail> m_TrailPool = new Stack<Trail>();

        /// <summary>An empty trail for the particle: its lifetime and draws come from its seed, its
        /// head from where it is.</summary>
        Trail NewTrail(ref Particle p)
        {
            var trail = m_TrailPool.Count > 0 ? m_TrailPool.Pop() : new Trail();
            SetTrailDraws(ref p, trail);
            UpdateTrailHead(ref p, trail);
            return trail;
        }

        /// <summary>The trail's lifetime and its width and color draws, from the particle's seed.</summary>
        void SetTrailDraws(ref Particle p, Trail trail)
        {
            trail.Lifetime = m_Trails.lifetime.Evaluate(0f, TrailRandom(p.m_RandomSeed, 0x34bbab1bu)) * p.m_StartLifetime;
            trail.WidthRandom = TrailRandom(p.m_RandomSeed, 0xfedc345bu);
            trail.ColorRandom = TrailRandom(p.m_RandomSeed, 0x6cf2ac20u);
            if (m_Trails.sizeAffectsLifetime)
            {
                var size = CurrentSize3D(ref p);
                trail.Lifetime *= Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            }
        }

        /// <summary>Puts a trail into a free slot; returns its id.</summary>
        int AddTrail(Trail trail)
        {
            int id = m_FreeTrails.Count > 0 ? m_FreeTrails.Pop() : m_TrailList.Count;
            if (id == m_TrailList.Count) m_TrailList.Add(trail);
            else m_TrailList[id] = trail;
            return id;
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
            m_TrailStorageParticles = Math.Max(m_TrailStorageParticles, m_Count);
            float minDistance = Mathf.Max(m_Trails.minVertexDistance, 0f);
            for (int i = 0; i < m_Count; i++)
            {
                ref var p = ref m_Particles[i];
                if (p.m_TrailId == 0) continue;
                var trail = m_TrailList[p.m_TrailId - 1];
                if (trail == null || trail.Orphan) continue;
                UpdateTrailHead(ref p, trail);
                if (p.m_AgePercent >= 100f) continue;
                int newest = trail.Points.Count - 1;
                // A live particle whose newest point outlived the trail lifetime (a minimum vertex
                // distance it has not covered since) restarts its trail at its head: the expired
                // points go, without a cut tail, and a point is taken whatever the distance
                // (6000.0.71f1, tests/projects/particletrails cap/*/die0ntmv0.29, 3 and 0.31 alike).
                bool stale = newest >= 0 && m_TrailTime - trail.Times[newest] > trail.Lifetime;
                if (stale)
                {
                    trail.Points.Clear();
                    trail.Times.Clear();
                }
                if (newest < 0 || stale || (trail.Head - trail.Points[newest]).sqrMagnitude > minDistance * minDistance)
                {
                    // Automatic births keep their emission coordinate in the first scheduled
                    // sample, even when newborn integration already moved the live head.
                    trail.Points.Add(trail.BirthPosition ?? trail.Head);
                    trail.Times.Add(m_TrailTime);
                    trail.BirthPosition = null;
                }
            }
            for (int id = 0; id < m_TrailList.Count; id++)
            {
                var trail = m_TrailList[id];
                if (trail == null) continue;
                if (trail.Spent) continue;
                int expired = 0;
                while (expired < trail.Times.Count && m_TrailTime - trail.Times[expired] > trail.Lifetime) expired++;
                if (expired == trail.Times.Count && trail.Orphan)
                {
                    trail.Points.Clear();
                    trail.Times.Clear();
                    trail.Spent = true;
                    continue;
                }
                // Keep one expired anchor so the renderer can cut the tail at its exact age.
                expired = Mathf.Max(0, expired - 1);
                if (expired > 0)
                {
                    trail.Points.RemoveRange(0, expired);
                    trail.Times.RemoveRange(0, expired);
                }
                m_TrailStoragePoints = Math.Max(m_TrailStoragePoints, trail.Points.Count);
            }
        }

        /// <summary>Whether the trail a carrier holds is spent (or gone): Unity 6000.0.71f1 removes
        /// the carrier in the first update that starts so, with that update's dead and in their
        /// order (tests/projects/particletrails).</summary>
        bool CarriedTrailSpent(int link)
        {
            var trail = link > 0 && link <= m_TrailList.Count ? m_TrailList[link - 1] : null;
            return trail == null || trail.Spent;
        }

        /// <summary>A particle died: its trail goes with it, or stays to fade. A fading trail keeps
        /// its dead particle in the buffer as its carrier, at zero remaining lifetime and at rest:
        /// GetParticles and particleCount report it, the renderer does not draw it, and it holds its
        /// slot against maxParticles (6000.0.71f1, tests/projects/particletrails).</summary>
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

        /// <summary>A particle leaves the buffer (a death that keeps no fading trail, or a carrier
        /// whose trail is spent): its trail goes with it.</summary>
        void EndTrail(ref Particle p)
        {
            int id = p.m_TrailId - 1;
            p.m_TrailId = 0;
            if (id >= 0 && id < m_TrailList.Count && m_TrailList[id] != null) FreeTrail(id);
        }

        bool[] m_HeldTrails = global::System.Array.Empty<bool>();

        /// <summary>Frees, in slot order, every trail no particle in the buffer holds: a trail lives
        /// as long as its particle (live, or dead and carrying it while it fades).</summary>
        void FreeUnheldTrails()
        {
            int slots = m_TrailList.Count;
            if (slots == 0) return;
            if (m_HeldTrails.Length < slots) m_HeldTrails = new bool[global::System.Math.Max(slots, m_HeldTrails.Length * 2)];
            else global::System.Array.Clear(m_HeldTrails, 0, slots);
            for (int i = 0; i < m_Count; i++)
            {
                int link = m_Particles[i].m_TrailId;
                if (link > 0 && link <= slots) m_HeldTrails[link - 1] = true;
            }
            for (int id = 0; id < slots; id++)
                if (!m_HeldTrails[id] && m_TrailList[id] != null) FreeTrail(id);
        }

        void FreeTrail(int id)
        {
            RecycleTrail(m_TrailList[id]);
            m_TrailList[id] = null;
            m_FreeTrails.Push(id);
        }

        void RecycleTrail(Trail trail)
        {
            if (trail == null || trail.Ribbon) return;
            trail.Points.Clear();
            trail.Times.Clear();
            trail.Orphan = trail.Spent = false;
            trail.RenderPass = 0;
            trail.BirthPosition = null;
            m_TrailPool.Push(trail);
        }

        internal void ClearTrails()
        {
            foreach (var trail in m_TrailList) RecycleTrail(trail);
            m_TrailList.Clear();
            m_FreeTrails.Clear();
            m_TrailTime = 0f;
        }

        /// <summary>The largest particle count and trail point count of this system's per-particle
        /// trails so far: the trail storage Unity grows and never shrinks, which GetTrails reports.</summary>
        internal int m_TrailStorageParticles, m_TrailStoragePoints;

        /// <summary>The trail storage size GetTrails returns and sets as the Trails' capacity:
        /// slots for the particles (at least 32, a power of two) times points per trail (at least 4,
        /// a power of two), the largest so far; 0 in Ribbon mode (6000.0.71f1,
        /// tests/projects/particletrails api/*).</summary>
        int TrailStorageSize()
        {
            if (!m_Trails.enabled || m_Trails.mode != ParticleSystemTrailMode.PerParticle) return 0;
            return Math.Max(MinTrailStorageParticles, NextPowerOfTwo(m_TrailStorageParticles))
                * Math.Max(MinTrailStoragePoints, NextPowerOfTwo(m_TrailStoragePoints));
        }

        const int MinTrailStorageParticles = 32, MinTrailStoragePoints = 4;

        static int NextPowerOfTwo(int n)
        {
            int p = 1;
            while (p < n && p < (1 << 30)) p <<= 1;
            return p;
        }

        /// <summary>Copies each particle's trail, by its index in the buffer, into the capture
        /// <paramref name="trailData"/> holds (Ribbon mode keeps none: ribbons are drawn from the
        /// particles). Its storage list must be set.</summary>
        internal void CaptureTrails(in Trails trailData)
        {
            var capture = s_TrailCaptures.GetValue(trailData.positions, _ => new TrailCapture());
            capture.Slots.Clear();
            capture.TrailTime = m_TrailTime;
            if (m_Trails.mode != ParticleSystemTrailMode.PerParticle) return;
            for (int i = 0; i < m_Count; i++)
            {
                int link = m_Particles[i].m_TrailId;
                var trail = link > 0 && link <= m_TrailList.Count ? m_TrailList[link - 1] : null;
                capture.Slots.Add(trail?.Copy());
            }
        }

        /// <summary>Puts a capture back by particle index (6000.0.71f1, tests/projects/particletrails
        /// api/rt): the particle at index i takes the trail captured at index i, or none if the
        /// particle there had none, whatever particle held it then, so a capture also restores into
        /// another system, and after SetParticles moved or dropped particles the trails stay with
        /// the indices. Indices past either count are left as they are. Lifetime, width and color
        /// draws stay the particle's own; point times keep their age.</summary>
        internal void RestoreTrails(in Trails trailData)
        {
            var capture = CaptureOf(trailData);
            if (capture == null || !m_Trails.enabled || m_Trails.mode != ParticleSystemTrailMode.PerParticle) return;
            // The trail clock goes back to the capture's, so restored points keep their exact
            // times; trails not restored keep their ages.
            float shift = capture.TrailTime - m_TrailTime;
            m_TrailTime = capture.TrailTime;
            int n = Math.Min(m_Count, capture.Slots.Count);
            for (int i = n; i < m_Count; i++)
            {
                int kept = m_Particles[i].m_TrailId;
                var other = kept > 0 && kept <= m_TrailList.Count ? m_TrailList[kept - 1] : null;
                if (other == null) continue;
                for (int t = 0; t < other.Times.Count; t++) other.Times[t] += shift;
                other.HeadTime += shift;
            }
            for (int i = 0; i < n; i++)
            {
                ref var p = ref m_Particles[i];
                var source = capture.Slots[i];
                if (source == null)
                {
                    p.m_TrailId = 0;
                    continue;
                }
                var trail = NewTrail(ref p);
                trail.Points.AddRange(source.Points);
                trail.Times.AddRange(source.Times);
                trail.HeadTime = source.HeadTime;
                // A particle that only carries a fading trail keeps fading the restored one.
                trail.Orphan = p.IsTrailCarrier;
                trail.Spent = trail.Orphan && trail.Points.Count == 0;
                int link = p.m_TrailId;
                if (link > 0 && link <= m_TrailList.Count && m_TrailList[link - 1] != null)
                {
                    RecycleTrail(m_TrailList[link - 1]);
                    m_TrailList[link - 1] = trail;
                }
                else p.m_TrailId = AddTrail(trail) + 1;
            }
            FreeUnheldTrails();
        }

        int m_RenderPass;
        /// <summary>The Ribbon trails of the last <see cref="RenderTrails"/> call, reused (with
        /// their lists) by the next one.</summary>
        readonly List<Trail> m_RibbonTrails = new List<Trail>();

        /// <summary>Fills <paramref name="into"/> with the trails to draw. Per-particle trails come
        /// in the order of the particles holding them (live, or dead and carrying a fading trail), as
        /// Unity bakes and draws them (6000.0.71f1, tests/projects/particletrails: a particle moved by
        /// a death takes its trail along). Ribbons are rebuilt from the particles into trails this
        /// system keeps, so drawing every frame allocates nothing.</summary>
        internal void RenderTrails(List<Trail> into)
        {
            into.Clear();
            if (m_Trails.mode == ParticleSystemTrailMode.PerParticle)
            {
                int pass = ++m_RenderPass;
                for (int i = 0; i < m_Count; i++)
                {
                    int link = m_Particles[i].m_TrailId;
                    var trail = link > 0 && link <= m_TrailList.Count ? m_TrailList[link - 1] : null;
                    if (trail == null || trail.RenderPass == pass) continue;
                    trail.RenderPass = pass;
                    into.Add(trail);
                }
                return;
            }
            int ribbons = Mathf.Max(1, m_Trails.ribbonCount);
            if (m_Count / ribbons < 2) return;
            int used = 0;
            for (int offset = 0; offset < Mathf.Min(ribbons, m_Count); offset++)
            {
                int group = (m_Count - 1 - offset) % ribbons;
                if (used == m_RibbonTrails.Count)
                    m_RibbonTrails.Add(new Trail { Ribbon = true, Widths = new List<float>(), Colors = new List<Color32>() });
                var trail = m_RibbonTrails[used];
                trail.Points.Clear(); trail.Times.Clear(); trail.Widths.Clear(); trail.Colors.Clear();
                trail.Lifetime = float.PositiveInfinity;
                for (int i = group; i < m_Count; i += ribbons)
                {
                    UpdateTrailHead(ref m_Particles[i], trail);
                    trail.Points.Add(trail.Head); trail.Times.Add(m_TrailTime);
                    trail.Widths.Add(trail.Width); trail.Colors.Add(trail.Color);
                }
                if (trail.Points.Count > 1) { into.Add(trail); used++; }
            }
        }
    }
}
