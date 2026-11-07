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

        /// <summary>What <c>GetTrails</c> captures: the system's own per-particle
        /// trail state, so <c>SetTrails</c> can put it back exactly. Unity's
        /// <c>Trails</c> layout is undocumented (only <c>capacity</c> is public), so this is
        /// Unbound's representation, not Unity's.</summary>
        internal sealed class TrailCapture
        {
            /// <summary>The system the trails were captured from.</summary>
            internal ParticleSystem Owner;
            /// <summary>Copies of the trail slots by id; null slots are free.</summary>
            internal readonly List<Trail> Slots = new List<Trail>();
            /// <summary>The free slot ids in the order the next trails take them.</summary>
            internal int[] FreeOrder = global::System.Array.Empty<int>();
            internal float TrailTime;
            /// <summary>How many particles SetParticles keeps when it is given the particles
            /// captured with these trails (those with a positive remaining lifetime).</summary>
            internal int KeptParticleCount;
            /// <summary>The trail links of those particles, sorted: SetParticles drops the others
            /// and reorders the survivors, so the links are matched without their order.</summary>
            internal int[] KeptTrailIds = global::System.Array.Empty<int>();
            /// <summary>The sorted links of the particles that only carry a fading trail (their
            /// particle died; GetParticles exports them at zero lifetime, SetParticles drops them).</summary>
            internal int[] FadingTrailIds = global::System.Array.Empty<int>();
            /// <summary>The sorted links of living particles at zero lifetime: they die at the next
            /// step, and SetParticles drops them at once.</summary>
            internal int[] EndingTrailIds = global::System.Array.Empty<int>();
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
        bool HasAliveTrails => m_TrailList.Count > m_FreeTrails.Count;

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
            trail.BirthPosition = birthPosition.HasValue ? TrailSpacePoint(birthPosition.Value) : null;
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
            m_TrailList[id] = null;
            m_FreeTrails.Push(id);
        }

        internal void ClearTrails()
        {
            m_TrailList.Clear();
            m_FreeTrails.Clear();
            m_TrailTime = 0f;
        }

        /// <summary>Copies the trail state into the capture <paramref name="trailData"/> holds
        /// (live and fading trails; Ribbon mode keeps none, its ribbons are drawn from the
        /// particles). Its storage list must be set.</summary>
        internal void CaptureTrails(in Trails trailData)
        {
            var capture = s_TrailCaptures.GetValue(trailData.positions, _ => new TrailCapture());
            capture.Owner = this;
            capture.Slots.Clear();
            foreach (var trail in m_TrailList) capture.Slots.Add(trail?.Copy());
            capture.FreeOrder = m_FreeTrails.ToArray();
            capture.TrailTime = m_TrailTime;
            capture.KeptParticleCount = SortedTrailLinks(out capture.KeptTrailIds, out capture.FadingTrailIds, out capture.EndingTrailIds);
        }

        /// <summary>The trail links of the current particles, sorted, split as SetParticles treats
        /// the particles: those it keeps (a positive remaining lifetime; their count is returned),
        /// the carriers of fading trails, and living particles at zero lifetime. Links of 0 (no
        /// trail) are left out; kept particles without a trail still count.</summary>
        int SortedTrailLinks(out int[] kept, out int[] fading, out int[] ending)
        {
            var keptLinks = new List<int>(); var fadingLinks = new List<int>(); var endingLinks = new List<int>();
            int keptCount = 0;
            for (int i = 0; i < m_Count; i++)
            {
                ref var p = ref m_Particles[i];
                // SetParticles' own test: a nonpositive remaining lifetime dies when written.
                bool dropped = p.m_Lifetime <= 0f;
                if (!dropped) keptCount++;
                if (p.m_TrailId == 0) continue;
                if (!dropped) keptLinks.Add(p.m_TrailId);
                else if ((p.m_Flags & Particle.FlagOrphanTrail) != 0) fadingLinks.Add(p.m_TrailId);
                else endingLinks.Add(p.m_TrailId);
            }
            keptLinks.Sort(); fadingLinks.Sort(); endingLinks.Sort();
            kept = keptLinks.ToArray(); fading = fadingLinks.ToArray(); ending = endingLinks.ToArray();
            return keptCount;
        }

        /// <summary>Whether every id of the sorted <paramref name="part"/> is matched by its own id
        /// of the sorted <paramref name="whole"/>.</summary>
        static bool IsSubMultiset(int[] part, int[] whole)
        {
            int j = 0;
            foreach (int id in part)
            {
                while (j < whole.Length && whole[j] < id) j++;
                if (j == whole.Length || whole[j] != id) return false;
                j++;
            }
            return true;
        }

        /// <summary>Why the capture <paramref name="trailData"/> holds cannot be put back into this
        /// system as it is now, or null when it can. Unity documents only restoring an earlier
        /// GetTrails, together with SetParticles and SetPlaybackState; anything else is refused
        /// whole rather than half-applied (Unbound's choice, unmeasured). The particles SetParticles
        /// keeps must be the captured ones, matched by their trail links in any order (SetParticles
        /// reorders them). Carriers of fading trails and particles at zero lifetime may be gone,
        /// as SetParticles drops them; any still present must hold links the capture gave the same
        /// kind of particle, so a plain GetTrails then SetTrails is accepted while trails fade.</summary>
        internal string TrailCaptureMismatch(in Trails trailData)
        {
            var capture = CaptureOf(trailData);
            if (capture == null)
                return "was given a Trails value that ParticleSystem.GetTrails never filled";
            if (!ReferenceEquals(capture.Owner, this))
                return "was given trails captured from another particle system";
            int keptCount = SortedTrailLinks(out var kept, out var fading, out var ending);
            if (keptCount != capture.KeptParticleCount)
                return "was given trails captured with " + capture.KeptParticleCount + " living particles, but the system has "
                    + keptCount + " now (restore the particles captured with them first, with SetParticles)";
            if (!global::System.Linq.Enumerable.SequenceEqual(kept, capture.KeptTrailIds)
                || !IsSubMultiset(fading, capture.FadingTrailIds) || !IsSubMultiset(ending, capture.EndingTrailIds))
                return "was given trails that do not belong to the current particles (restore the particles captured"
                    + " with them first, with SetParticles)";
            return null;
        }

        /// <summary>Puts a capture back: the trail slots, their free order and the trail clock, as
        /// copies, so the capture can be applied again later. The trail of a captured particle that
        /// SetParticles has dropped (at zero lifetime, or carrying a fading trail) goes with it, as
        /// SetParticles measures.</summary>
        internal void RestoreTrails(in Trails trailData)
        {
            var capture = CaptureOf(trailData);
            m_TrailList.Clear();
            foreach (var trail in capture.Slots) m_TrailList.Add(trail?.Copy());
            m_FreeTrails.Clear();
            for (int i = capture.FreeOrder.Length - 1; i >= 0; i--) m_FreeTrails.Push(capture.FreeOrder[i]);
            m_TrailTime = capture.TrailTime;
            FreeUnheldTrails();
        }

        int m_RenderPass;

        /// <summary>The trails to draw. Per-particle trails come in the order of the particles holding
        /// them (live, or dead and carrying a fading trail), as Unity bakes and draws them
        /// (6000.0.71f1, tests/projects/particletrails: a particle moved by a death takes its trail
        /// along).</summary>
        internal IEnumerable<Trail> RenderTrails()
        {
            if (m_Trails.mode == ParticleSystemTrailMode.PerParticle)
            {
                int pass = ++m_RenderPass;
                for (int i = 0; i < m_Count; i++)
                {
                    int link = m_Particles[i].m_TrailId;
                    var trail = link > 0 && link <= m_TrailList.Count ? m_TrailList[link - 1] : null;
                    if (trail == null || trail.RenderPass == pass) continue;
                    trail.RenderPass = pass;
                    yield return trail;
                }
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
