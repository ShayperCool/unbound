using System.Collections.Generic;
using Unbound.Runtime;
using R = Unbound.Runtime.ParticleRandom;

namespace UnityEngine
{
    // Collisions and force fields, as measured in the Editor: a particle is a sphere of half its size
    // (times the radius scale); it stops where it meets a plane or collider and spends the rest of the
    // step moving away with its velocity reflected (bounce on the normal part) and reduced by the
    // dampen; it loses lifetimeLoss of its start lifetime. Force fields push with their direction
    // and pull to their center with 30 × gravity.
    public sealed partial class ParticleSystem
    {
        /// <summary>The collisions of the frame so far, for OnParticleCollision and GetCollisionEvents.</summary>
        internal readonly List<(GameObject other, ParticleCollisionEvent e)> m_CollisionEvents = new List<(GameObject, ParticleCollisionEvent)>();

        static readonly HashSet<string> s_Unsupported = new HashSet<string>();

        /// <summary>Reports a module setting Unbound does not simulate, once per setting.</summary>
        internal static void ReportUnsupported(string what)
        {
            if (s_Unsupported.Add(what)) Debug.LogWarning("Unbound: particle " + what + " is not simulated yet");
        }

        /// <summary>Moves a particle that started the step at <paramref name="start"/> (simulation
        /// space) to its first collision on the way to where it ended, and bounces it there; false
        /// when the collision kills it.</summary>
        bool Collide(ref Particle p, Vector3 start, float age, float dt, float speedModifier)
        {
            var C = m_Collision;
            var toWorld = SimulationToWorld();
            var w0 = toWorld.MultiplyPoint3x4(start);
            var w1 = toWorld.MultiplyPoint3x4(p.m_Position);
            var move = w1 - w0;
            float distance = move.magnitude;
            if (distance <= 0f) return true;
            var size = CurrentSize3D(ref p);
            float radius = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.5f * C.radiusScale;

            float fraction = float.PositiveInfinity;
            Vector3 normal = Vector3.zero;
            GameObject other = null;
            int colliderId = 0;
            if (C.type == ParticleSystemCollisionType.Planes)
            {
                foreach (var plane in C.planes)
                {
                    if (plane == null) continue;
                    var n = plane.up;
                    var origin = plane.position;
                    float d0 = Vector3.Dot(w0 - origin, n) - radius;
                    float d1 = Vector3.Dot(w1 - origin, n) - radius;
                    // Moving into the plane, the center not yet past it, and deeper than a hair
                    // (a newborn grazing it in its first instant does not bounce, measured).
                    if (d1 >= -1e-5f || d1 >= d0 || d0 < -radius) continue;
                    float f = Mathf.Clamp01(d0 / (d0 - d1));
                    if (f < fraction)
                    {
                        fraction = f;
                        normal = n;
                        other = plane.gameObject;
                        colliderId = 0;
                    }
                }
            }
            else
            {
                if (C.mode == ParticleSystemCollisionMode.Collision2D)
                {
                    ReportUnsupported("2D world collision");
                    return true;
                }
                if (Physics.SphereCast(w0, radius, move / distance, out var hit, distance, C.collidesWith, QueryTriggerInteraction.Ignore))
                {
                    fraction = hit.distance / distance;
                    normal = hit.normal;
                    other = hit.collider != null ? hit.collider.gameObject : null;
                    colliderId = hit.collider != null ? hit.collider.GetInstanceID() : 0;
                    if (C.colliderForce != 0f && hit.collider != null) ReportColliderForce(hit.collider.attachedRigidbody);
                }
            }
            if (float.IsPositiveInfinity(fraction)) return true;

            uint seed = p.m_RandomSeed;
            var simToWorldRotation = Quaternion.Inverse(WorldToSimulationRotation());
            var velocity = simToWorldRotation * p.m_Velocity;
            float speed = (simToWorldRotation * (p.m_Velocity + p.m_AnimatedVelocity)).magnitude;
            var impact = w0 + move * fraction;
            // Collision sub-emitters ("Spawns new particles when particles from the parent
            // system collide with something"; collision0/1 spawn "at the locations of the
            // collision") emit at the point the particle meets the plane or collider, the
            // ParticleCollisionEvent intersection, whether or not collision messages are sent.
            // Unbound's choices (unmeasured): one emission per detected collision, also when the
            // collision then kills the particle (its Death sub-emitters fire as well), and what
            // the children inherit is the particle as it arrives, before the bounce, dampen and
            // lifetime loss.
            if (m_SubEmitters.enabled) EventSubEmitters(ParticleSystemSubEmitterType.Collision, p, toWorld.inverse.MultiplyPoint3x4(impact));
            if (C.sendCollisionMessages && other != null)
                m_CollisionEvents.Add((other, new ParticleCollisionEvent
                {
                    m_Intersection = impact,
                    m_Normal = normal,
                    m_Velocity = simToWorldRotation * (p.m_Velocity + p.m_AnimatedVelocity),
                    m_ColliderInstanceID = colliderId,
                }));
            float bounce = C.bounce.Evaluate(age, R.Value(seed, R.Collision));
            float dampen = C.dampen.Evaluate(age, R.Value(seed, R.Collision + 1));
            var normalPart = normal * Vector3.Dot(velocity, normal);
            velocity = (velocity - normalPart - normalPart * bounce) * (1f - dampen);
            p.m_Velocity = WorldToSimulationRotation() * velocity;
            var after = impact + simToWorldRotation * ((p.m_Velocity + p.m_AnimatedVelocity) * (speedModifier * dt * (1f - fraction)));
            p.m_Position = toWorld.inverse.MultiplyPoint3x4(after);
            p.m_AgePercent += C.lifetimeLoss.Evaluate(age, R.Value(seed, R.Collision + 2)) * 100f;
            p.ExportLifetime();
            // A kill by speed also comes after the bounce: the killed particle reports its bounced
            // velocity (6000.0.71f1, tests/projects/particletrails pp/collision/speedkill).
            if (speed < C.minKillSpeed || speed > C.maxKillSpeed) return false;
            return p.m_AgePercent <= 100f;
        }

        /// <summary>colliderForce: the 6000.0 docs say only that it is "How much force is
        /// applied to a Collider when hit by particles" and that the three multiply flags scale
        /// it by the collision angle ("Grazing angles will generate less force than a head-on
        /// collision"), the particle speed and the particle size. They give no formula, force
        /// mode, direction or point of application, so no force is applied: a collision that
        /// would push a body (a Rigidbody that physics affects, i.e. not kinematic) is reported
        /// instead.</summary>
        static void ReportColliderForce(Rigidbody body)
        {
            if (body == null || body.isKinematic) return;
            Unbound.Runtime.Stubs.Ignored("ParticleSystem.CollisionModule.colliderForce",
                "is not applied: particle collisions do not push Rigidbodies (the force formula, mode and direction are undocumented)");
        }

        /// <summary>The systems handing a Collision or Trigger event of one of their particles
        /// to their sub-emitters, innermost last.</summary>
        static readonly List<ParticleSystem> s_EventSubEmitting = new List<ParticleSystem>();

        /// <summary>A Collision or Trigger event of <paramref name="parent"/>: each of its
        /// sub-emitters of <paramref name="type"/> emits its first burst at
        /// <paramref name="position"/> (simulation space), with the Death/Manual path's
        /// emitProbability roll, burst count and inherited properties, the rest of the particle as
        /// it is now.</summary>
        void EventSubEmitters(ParticleSystemSubEmitterType type, in Particle parent, Vector3 position)
        {
            if (!HasSubEmitters(type)) return;
            var at = parent;
            at.m_Position = position;
            // The event comes from inside a particle's step, which holds a reference into this
            // system's buffer: a chain of Collision/Trigger sub-emitters that leads back here (a
            // self, direct or indirect cycle made only of such links, possible only through
            // scripts) would grow that buffer under it, so it does not emit. Only those links are
            // seen: a cycle that closes through a Birth link is not caught, the same hazard pure
            // Birth cycles already have.
            s_EventSubEmitting.Add(this);
            try
            {
                foreach (var e in m_SubEmitters.emitters)
                {
                    if (e.type != type || e.system == null) continue;
                    if (s_EventSubEmitting.Contains(e.system))
                    {
                        ReportUnsupported("collision/trigger sub-emitter cycle (a sub-emitter that leads back to a system emitting it)");
                        continue;
                    }
                    if ((e.properties & ParticleSystemSubEmitterProperties.InheritDuration) != 0)
                        ReportUnsupported("sub-emitter Inherit Duration");
                    var child = e.system;
                    // As TriggerSubEmitterAt: never itself or a destroyed object, and the same
                    // stable emitProbability roll owned by the parent particle.
                    if (child == this || child.m_GameObject == null) continue;
                    if (e.emitProbability <= 0f || e.emitProbability < 1f && ModuleValue(parent.m_RandomSeed, 1520730008u) > e.emitProbability) continue;
                    // SubEmittersModule.collision0/1 (6000.0): only the first burst (see
                    // EventBurstCount, whose count rule is measured for Death and Manual). The Manual
                    // lists Trigger among the conditions that "support only a single burst" without
                    // saying what happens with more; Unbound's choice (unmeasured): Trigger follows the
                    // Collision rule, and says so once.
                    if (type == ParticleSystemSubEmitterType.Trigger && child.m_Emission.bursts.Count > 1)
                        ReportUnsupported("trigger sub-emitter bursts after the first (Unbound emits only the first, the rule Unity documents for Collision)");
                    int count = child.EventBurstCount(parent.m_RandomSeed, EventSeed);
                    if (count > 0) child.EmitFromParent(this, ref at, e.properties, count, 0f);
                }
            }
            finally { s_EventSubEmitting.RemoveAt(s_EventSubEmitting.Count - 1); }
        }

        /// <summary>The particles of the frame's trigger events by type, for OnParticleTrigger and
        /// GetTriggerParticles (filled in the player loop only, measured).</summary>
        internal readonly List<Particle>[] m_TriggerParticles = { new List<Particle>(), new List<Particle>(), new List<Particle>(), new List<Particle>() };
        /// <summary>Per event type and entry, where the entry's colliders start in
        /// <see cref="m_TriggerColliders"/> (GetTriggerParticles' ColliderData).</summary>
        internal readonly List<int>[] m_TriggerColliderStarts = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
        internal readonly List<Component>[] m_TriggerColliders = { new List<Component>(), new List<Component>(), new List<Component>(), new List<Component>() };
        internal bool m_CollectTriggers;
        static readonly Collider[] s_Overlaps = new Collider[64];
        /// <summary>The colliders the particle being classified is inside, in the Trigger module's
        /// order, as its collider query mode asks (none, the first, all).</summary>
        readonly List<Component> m_TriggerHits = new List<Component>();

        internal void ClearTriggerEvents()
        {
            for (int type = 0; type < m_TriggerParticles.Length; type++)
            {
                m_TriggerParticles[type].Clear();
                m_TriggerColliderStarts[type].Clear();
                m_TriggerColliders[type].Clear();
            }
        }

        /// <summary>Classifies the particle against cached collider geometry, then applies the
        /// trigger actions. False when an action kills it.</summary>
        bool Trigger(ref Particle p)
        {
            var T = m_Trigger;
            var toWorld = SimulationToWorld();
            var world = toWorld.MultiplyPoint3x4(p.m_Position);
            var size = CurrentSize3D(ref p);
            // The particle sweeps the step's movement: a collider counts when the sphere moving
            // from where the step started to where it ended touches it (6000.0.71f1: sphere
            // colliders through the measured path of SPHERE_MECHANISM.md; other colliders as a
            // capsule overlap, tests/projects/particletriggerapi, where a particle leaving a box
            // is still inside in the step it crosses the face).
            var start = toWorld.MultiplyPoint3x4(p.m_Position - (p.m_Velocity + p.m_AnimatedVelocity) * m_LastStepDt);
            float sphereRadius = (T.radiusScale * 0.5f) * Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            // Only the layers of the module's other colliders, so unrelated colliders do not fill
            // the result buffer.
            int layers = 0;
            foreach (var c in T.colliders)
                if (c != null && !(c is SphereCollider)) layers |= 1 << c.gameObject.layer;
            int n = layers != 0 ? Physics.OverlapCapsuleNonAlloc(start, world, sphereRadius, s_Overlaps, layers, QueryTriggerInteraction.Collide) : 0;
            // Which colliders hold it, in the module's order: the first decides inside, the query
            // mode how many more ColliderData lists (6000.0.71f1, tests/projects/particletriggerapi:
            // Disabled none, One the first in the list, All every one).
            var mode = T.colliderQueryMode;
            m_TriggerHits.Clear();
            bool inside = false, spheresPrepared = false;
            var colliders = T.colliders;
            for (int ci = 0; ci < colliders.Count; ci++)
            {
                var c = colliders[ci];
                if (c == null) continue;
                bool hit = false;
                if (c is SphereCollider sphere)
                {
                    if (!sphere.m_Registered || sphere.m_Shape == 0) continue;
                    if (!spheresPrepared)
                    {
                        Unbound.Runtime.PhysicsWorld.PrepareQuery();
                        spheresPrepared = true;
                    }
                    hit = SphereTriggerOverlap(start, world, sphereRadius, sphere);
                }
                else
                    for (int i = 0; i < n; i++)
                        if (ReferenceEquals(c, s_Overlaps[i])) { hit = true; break; }
                if (!hit) continue;
                inside = true;
                if (mode == ParticleSystemColliderQueryMode.Disabled) break;
                m_TriggerHits.Add(c);
                if (mode == ParticleSystemColliderQueryMode.One) break;
            }
            bool was = (p.m_Flags & Particle.FlagInsideTrigger) != 0;
            if (inside) p.m_Flags |= Particle.FlagInsideTrigger;
            else p.m_Flags &= ~Particle.FlagInsideTrigger;
            if (!Act(ref p, inside ? T.inside : T.outside, inside ? ParticleSystemTriggerEventType.Inside : ParticleSystemTriggerEventType.Outside)) return false;
            if (inside && !was && !Act(ref p, T.enter, ParticleSystemTriggerEventType.Enter)) return false;
            if (!inside && was && !Act(ref p, T.exit, ParticleSystemTriggerEventType.Exit)) return false;
            return true;
        }

        static bool SphereTriggerOverlap(Vector3 start, Vector3 end, float radius, SphereCollider sphere)
        {
            // Bounds come from the cached shape and actor, with native inflation 1. The fixed
            // particle bounds pad is the literal 0x3727c5ac in FindParticleOverlaps, not a
            // public-query contact offset. Keep midpoint/extents float operation order.
            var bounds = sphere.bounds;
            var half = (end - start) * 0.5f;
            var midpoint = start + half;
            var pe = new Vector3(Mathf.Abs(half.x) + radius, Mathf.Abs(half.y) + radius, Mathf.Abs(half.z) + radius);
            float pad = System.BitConverter.Int32BitsToSingle(0x3727c5ac);
            var ce = bounds.extents + Vector3.one * pad;
            var offset = midpoint - bounds.center;
            if (!(Mathf.Abs(offset.x) <= pe.x + ce.x && Mathf.Abs(offset.y) <= pe.y + ce.y && Mathf.Abs(offset.z) <= pe.z + ce.z)) return false;

            // m_ShapeMatrix is collider-to-body, never a world pose. Query the cached actor
            // and use the same local sphere geometry as CreateShape after PrepareQuery.
            var body = Unbound.Box3D.B3.ua3_shape_body(sphere.m_Shape);
            Unbound.Box3D.B3.ua3_body_get_transform(body, out var position, out var rotation);
            var center = position + rotation * sphere.m_ShapeMatrix.MultiplyPoint3x4(sphere.m_Center);
            var scale = Collider.AxisScales(sphere.m_ShapeMatrix);
            float colliderRadius = Mathf.Max(1e-4f, Mathf.Abs(sphere.m_Radius) * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)));
            return SphereTriggerSweep(start, end, radius, center, colliderRadius);
        }

        // Boolean portion of GuSweepSphereSphere.cpp, including the a == 0 branch. This
        // stays local to particle sphere triggers; public Physics query semantics differ.
        static bool SphereTriggerSweep(Vector3 start, Vector3 end, float radius, Vector3 center, float colliderRadius)
        {
            var delta = end - start;
            float length = Mathf.Sqrt(delta.x * delta.x + (delta.y * delta.y + delta.z * delta.z));
            var direction = length > 1e-15f ? delta / length : Vector3.up;
            var motion = direction * length;
            var relative = (start + motion) - start;
            var separation = start - center;
            float a = SphereTriggerDot(relative, relative);
            float b = 2f * SphereTriggerDot(relative, separation);
            float sum = colliderRadius + radius;
            float c = SphereTriggerDot(separation, separation) - sum * sum;
            if (c <= 0f || a == 0f) return true;
            float q = b * b - (4f * a) * c;
            if (!(q >= 0f)) return false;
            float root = Mathf.Sqrt(q);
            float divisor = 1f / (2f * a);
            float u0 = (-b + root) * divisor;
            float u1 = (-b - root) * divisor;
            return !(Mathf.Max(u0, u1) < 0f || Mathf.Min(u0, u1) > 1f);
        }

        static float SphereTriggerDot(Vector3 a, Vector3 b) => (a.y * b.y + a.x * b.x) + a.z * b.z;

        bool Act(ref Particle p, ParticleSystemOverlapAction action, ParticleSystemTriggerEventType type)
        {
            // Trigger sub-emitters spawn "when particles from the parent system pass conditions
            // in the Trigger Module" (the Manual: "when a parent particle interacts with a
            // trigger volume"). Unbound's choices (unmeasured): a condition is passed when the
            // particle meets an Inside, Outside, Enter or Exit condition whose action is not
            // Ignore (Callback or Kill), each time the trigger evaluation finds it (once per
            // internal step, and once for each such condition met in that step), at the
            // particle's position, before a Kill takes it, and whether or not this update
            // collects the OnParticleTrigger lists.
            if (action != ParticleSystemOverlapAction.Ignore && m_SubEmitters.enabled)
                EventSubEmitters(ParticleSystemSubEmitterType.Trigger, p, p.m_Position);
            if (action == ParticleSystemOverlapAction.Kill) return false;
            if (action == ParticleSystemOverlapAction.Callback && m_CollectTriggers)
            {
                StampTriggerSerial(ref p);
                m_TriggerParticles[(int)type].Add(p);
                // Outside and Exit have no colliders (their ColliderData query throws).
                var colliders = m_TriggerColliders[(int)type];
                m_TriggerColliderStarts[(int)type].Add(colliders.Count);
                if (type == ParticleSystemTriggerEventType.Inside || type == ParticleSystemTriggerEventType.Enter)
                    colliders.AddRange(m_TriggerHits);
            }
            return true;
        }

        // Trigger entry identity, for SetTriggerParticles. Each entry is a copy of a live
        // particle; the write-back goes to that particle wherever it is by then (removals move
        // particles, emission and SetParticles add or replace them). Act stamps the live
        // particle with a per-system serial in bits 8-31 of its flags (the Particle.Flag*
        // constants use bits 0-6), so the stamp moves with it through swap-back removal and
        // SetParticles round trips, a reused slot loses it (InitParticle starts from default),
        // and the entry's copy carries it. Serials run 1..TriggerSerialCount, wrapping, and
        // only those handed out since the lists were last cleared are current. A new collection
        // first strips every live particle's serial, so a particle not listed in it carries
        // none, even once the counter has wrapped back to the serial it was given earlier.
        const int TriggerSerialShift = 8;
        const uint TriggerSerialMask = 0xFFFFFF00u;
        const uint TriggerSerialCount = 0xFFFFFFu;
        uint m_TriggerSerialNext = 1;
        /// <summary>The first serial of the entries now in <see cref="m_TriggerParticles"/>.</summary>
        uint m_TriggerSerialStart = 1;

        static uint TriggerSerialOf(in Particle p) => (p.m_Flags & TriggerSerialMask) >> TriggerSerialShift;

        bool IsCurrentTriggerSerial(uint serial)
        {
            if (serial == 0) return false;
            uint span = (m_TriggerSerialNext + TriggerSerialCount - m_TriggerSerialStart) % TriggerSerialCount;
            return (serial + TriggerSerialCount - m_TriggerSerialStart) % TriggerSerialCount < span;
        }

        /// <summary>Gives the live particle a serial for this collection, keeping the one it was
        /// already given in it (the same particle can be listed by several events and steps).</summary>
        void StampTriggerSerial(ref Particle p)
        {
            bool first = true;
            foreach (var list in m_TriggerParticles)
                if (list.Count > 0) { first = false; break; }
            // FrameUpdate cleared the lists: a new collection, earlier serials are stale. Strip
            // them all (the particle being listed included, wherever it is), so after a wrap a
            // reissued serial can only be on the particle it is reissued to. A serial an older
            // GetParticles copy brings back through SetParticles stays outside the window
            // until the counter wraps.
            if (first)
            {
                m_TriggerSerialStart = m_TriggerSerialNext;
                for (int i = 0; i < m_Count; i++) m_Particles[i].m_Flags &= ~TriggerSerialMask;
                p.m_Flags &= ~TriggerSerialMask;
            }
            if (IsCurrentTriggerSerial(TriggerSerialOf(p))) return;
            p.m_Flags = (p.m_Flags & ~TriggerSerialMask) | (m_TriggerSerialNext << TriggerSerialShift);
            m_TriggerSerialNext = m_TriggerSerialNext == TriggerSerialCount ? 1 : m_TriggerSerialNext + 1;
        }

        /// <summary>Per event type, the live slot of each entry (-1: its particle is gone), as
        /// <see cref="ResolveTriggerSlots"/> last found them.</summary>
        readonly List<int> m_TriggerSlots = new List<int>();
        readonly Dictionary<uint, int> m_TriggerSlotOf = new Dictionary<uint, int>();

        /// <summary>Finds the live slot of each <paramref name="type"/> entry by its serial (slots
        /// move as particles are removed or written, also inside the callback).</summary>
        void ResolveTriggerSlots(ParticleSystemTriggerEventType type)
        {
            m_TriggerSlotOf.Clear();
            for (int i = 0; i < m_Count; i++)
            {
                // An orphaned trail is no longer a particle.
                if (m_Particles[i].IsTrailCarrier) continue;
                uint serial = TriggerSerialOf(m_Particles[i]);
                if (IsCurrentTriggerSerial(serial)) m_TriggerSlotOf.TryAdd(serial, i);
            }
            m_TriggerSlots.Clear();
            // A slot whose particle a write-back replaced with another particle (another seed)
            // no longer holds the listed one (measured: write/subset, the entry is not returned).
            foreach (var entry in m_TriggerParticles[(int)type])
                m_TriggerSlots.Add(m_TriggerSlotOf.TryGetValue(TriggerSerialOf(entry), out int slot)
                    && m_Particles[slot].m_RandomSeed == entry.m_RandomSeed ? slot : -1);
        }

        /// <summary>GetTriggerParticles: the particles of a trigger event as they are now (a write
        /// back to one event's list shows in another's read after it), without those no longer
        /// there (6000.0.71f1, tests/projects/particletriggerapi write/*). Leaves
        /// <see cref="m_TriggerSlots"/> resolved for the collider data.</summary>
        internal int ReadTriggerParticles(ParticleSystemTriggerEventType type, List<Particle> particles)
        {
            var entries = m_TriggerParticles[(int)type];
            particles?.Clear();
            ResolveTriggerSlots(type);
            int n = 0;
            if (entries.Count > 0) MaterializeAxisOfRotation();
            for (int k = 0; k < entries.Count; k++)
            {
                int slot = m_TriggerSlots[k];
                if (slot < 0) continue;
                particles?.Add(m_Particles[slot]);
                n++;
            }
            return n;
        }

        /// <summary>The collider data of the entries <see cref="ReadTriggerParticles"/> just
        /// returned, in the same order.</summary>
        internal ColliderData TriggerColliderData(ParticleSystemTriggerEventType type)
        {
            var starts = m_TriggerColliderStarts[(int)type];
            var colliders = m_TriggerColliders[(int)type];
            int n = 0, total = 0;
            for (int k = 0; k < m_TriggerSlots.Count; k++)
            {
                if (m_TriggerSlots[k] < 0) continue;
                n++;
                total += (k + 1 < starts.Count ? starts[k + 1] : colliders.Count) - starts[k];
            }
            var data = new ColliderData { colliders = new Component[total], colliderIndices = new int[total], particleStartIndices = new int[n] };
            int i = 0, c = 0;
            for (int k = 0; k < m_TriggerSlots.Count; k++)
            {
                if (m_TriggerSlots[k] < 0) continue;
                data.particleStartIndices[i++] = c;
                int end = k + 1 < starts.Count ? starts[k + 1] : colliders.Count;
                for (int j = starts[k]; j < end; j++)
                {
                    data.colliders[c] = colliders[j];
                    data.colliderIndices[c] = c;
                    c++;
                }
            }
            return data;
        }

        /// <summary>SetTriggerParticles: writes particles[0, count) whole to the live particles of
        /// the <paramref name="type"/> entries [offset, offset + count), through SetParticles'
        /// write path (6000.0 Scripting API: "Write modified particles back to the Particle
        /// System, during a call to OnParticleTrigger"; offset is "the offset into the
        /// destination particle array"). The lists exist only during the callback, so elsewhere
        /// nothing is written and nothing is logged (measured). With an offset and count
        /// (<paramref name="whole"/> false) the Editor throws unless offset + count is smaller
        /// than the list's size (measured: offset 1 and count 2 throw for 3 particles and write
        /// for 4, the first two of the list into the second and third particles). Entries whose
        /// particle a trigger Kill ended write nothing (Unbound's choice).</summary>
        internal void WriteBackTriggerParticles(ParticleSystemTriggerEventType type, List<Particle> particles, int offset, int count, bool whole)
        {
            if (particles == null) throw new System.ArgumentNullException(nameof(particles));
            if (!whole && (offset < 0 || count < 0 || offset + count >= particles.Count))
                throw new System.ArgumentOutOfRangeException(nameof(count), "offset+count should be smaller than the size of the particles list.");
            ResolveTriggerSlots(type);
            // Positions count in the list GetTriggerParticles returns now, which leaves out the
            // entries whose particle is gone (Unbound's choice: the Editor's mapping for a list
            // with gone entries is unmeasured).
            int live = 0;
            for (int k = 0; k < m_TriggerSlots.Count; k++)
                if (m_TriggerSlots[k] >= 0) m_TriggerSlots[live++] = m_TriggerSlots[k];
            count = System.Math.Min(count, live - offset);
            if (count <= 0) return;
            if (m_TriggerWriteSlots.Length < count)
            {
                m_TriggerWriteSlots = new int[count];
                m_TriggerWriteValues = new Particle[count];
            }
            int n = 0;
            for (int k = 0; k < count; k++)
            {
                int slot = m_TriggerSlots[offset + k];
                // A particle a trigger Kill ended in this update stays dead (Unbound's choice,
                // unmeasured).
                if ((m_Particles[slot].m_Flags & Particle.FlagKilled) != 0) continue;
                // The slot keeps its own listing: the written particle takes its trigger serial.
                var value = particles[k];
                value.m_Flags = (value.m_Flags & ~TriggerSerialMask) | (m_Particles[slot].m_Flags & TriggerSerialMask);
                m_TriggerWriteSlots[n] = slot;
                m_TriggerWriteValues[n++] = value;
            }
            WriteParticlesAt(m_TriggerWriteSlots, m_TriggerWriteValues, n);
        }

        int[] m_TriggerWriteSlots = System.Array.Empty<int>();
        Particle[] m_TriggerWriteValues = System.Array.Empty<Particle>();

        /// <summary>Sends OnParticleTrigger when the update listed particles; the lists are
        /// empty again once it returns (GetTriggerParticles elsewhere finds none: measured).</summary>
        internal void SendTriggerMessage()
        {
            if (m_GameObject == null) return;
            try
            {
                foreach (var list in m_TriggerParticles)
                    if (list.Count > 0)
                    {
                        Messaging.SendFast(m_GameObject, "OnParticleTrigger");
                        return;
                    }
            }
            finally { ClearTriggerEvents(); }
        }

        static readonly HashSet<GameObject> s_CollisionMessageTargets = new HashSet<GameObject>();

        /// <summary>Sends the frame's OnParticleCollision messages: to this system's scripts and to
        /// the object hit, once per object.</summary>
        internal void SendCollisionMessages()
        {
            if (m_CollisionEvents.Count == 0 || m_GameObject == null) return;
            var seen = s_CollisionMessageTargets;
            seen.Clear();
            foreach (var (other, _) in m_CollisionEvents)
            {
                if (other == null || !seen.Add(other)) continue;
                Messaging.Send(m_GameObject, "OnParticleCollision", other, SendMessageOptions.DontRequireReceiver, out _);
                if (other != m_GameObject)
                    Messaging.Send(other, "OnParticleCollision", m_GameObject, SendMessageOptions.DontRequireReceiver, out _);
            }
        }

        /// <summary>Applies field acceleration and the measured sequential velocity relaxation.
        /// Rotation targets a constant tangential speed, rather than an angular acceleration.</summary>
        void ApplyForceFields(ref Particle p, ref Vector3 velocity, float age, Quaternion worldToSim, float dt)
        {
            var X = m_ExternalForces;
            var toWorld = SimulationToWorld();
            var world = toWorld.MultiplyPoint3x4(p.m_Position);
            var total = Vector3.zero;
            float multiplier = X.multiplierCurve.Evaluate(age, R.Value(p.m_RandomSeed, R.Force + 8));
            bool rotated = false;
            foreach (var field in ParticleSystemForceField.FindAll())
            {
                if (field == null || !field.isActiveAndEnabled || !Influences(field)) continue;
                var t = field.transform;
                var local = t.InverseTransformPoint(world);
                if (!field.Contains(local)) continue;
                float r = R.Value(p.m_RandomSeed, R.Force + 7);
                var direction = new Vector3(field.m_DirectionX.Evaluate(age, r), field.m_DirectionY.Evaluate(age, r), field.m_DirectionZ.Evaluate(age, r));
                var accel = t.rotation * direction;
                float gravity = field.m_Gravity.Evaluate(age, r);
                if (gravity != 0f)
                {
                    var toCenter = t.position - world;
                    if (toCenter.sqrMagnitude > 1e-12f) accel += toCenter.normalized * (gravity * 30f);
                }
                float distance = field.RotationCurveTime(local);
                ref readonly var speedCurve = ref field.m_RotationSpeed;
                ref readonly var attractionCurve = ref field.m_RotationAttraction;
                float rotationSpeed = speedCurve.Evaluate(distance, r);
                float attraction = attractionCurve.Evaluate(distance, r);
                if (rotationSpeed != 0f && attraction != 0f)
                {
                    if (field.rotationRandomness != Vector2.zero)
                        ReportUnsupported("force field rotation randomness");
                    else if (speedCurve.mode == ParticleSystemCurveMode.TwoConstants || speedCurve.mode == ParticleSystemCurveMode.TwoCurves ||
                             attractionCurve.mode == ParticleSystemCurveMode.TwoConstants || attractionCurve.mode == ParticleSystemCurveMode.TwoCurves)
                        ReportUnsupported("force field randomized rotation curves");
                    else if (multiplier > 0f)
                    {
                        // Native applies the field's direction/gravity first, then relaxes the
                        // stored velocity toward its tangent. The multiplier scales both curves.
                        // Neither the blend nor signed attraction is clamped.
                        velocity += worldToSim * ((total + accel) * (multiplier * dt));
                        total = accel = Vector3.zero;
                        var tangent = t.TransformVector(new Vector3(local.z, 0f, -local.x));
                        var target = toWorld.inverse.MultiplyVector(tangent).normalized * (rotationSpeed * multiplier);
                        velocity += (target - velocity) * (attraction * multiplier * (30f * dt));
                        rotated = true;
                    }
                }
                float drag = field.m_Drag.Evaluate(age, r);
                if (drag != 0f)
                {
                    // A deceleration of drag along the velocity (measured), times the particle's
                    // cross-section and speed when asked.
                    var dragVelocity = Quaternion.Inverse(worldToSim) * ((rotated ? velocity : p.m_Velocity) + p.m_AnimatedVelocity);
                    // A rotating field has already applied its direction to the stored velocity.
                    float speed = dragVelocity.magnitude;
                    if (speed > 0f)
                    {
                        if (field.multiplyDragByParticleSize)
                        {
                            var size = CurrentSize3D(ref p);
                            float radius = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.5f;
                            drag *= Mathf.PI * radius * radius;
                        }
                        if (field.multiplyDragByParticleVelocity) drag *= speed;
                        accel -= dragVelocity / speed * drag;
                    }
                }
                if (field.vectorField != null) ReportUnsupported("force field vector field");
                total += accel;
            }
            // Wind zones, by layer only: the influence list holds force fields (measured).
            if (X.influenceFilter != ParticleSystemGameObjectFilter.List)
            {
                foreach (var zone in WindZone.All)
                {
                    if (zone == null || (X.influenceMask.value & (1 << zone.gameObject.layer)) == 0) continue;
                    total += zone.Acceleration(world, Time.time);
                }
            }
            velocity += worldToSim * (total * (multiplier * dt));
        }

        bool Influences(ParticleSystemForceField field)
        {
            var X = m_ExternalForces;
            bool inMask = (X.influenceMask.value & (1 << field.gameObject.layer)) != 0;
            bool inList = X.influences.Contains(field);
            switch (X.influenceFilter)
            {
                case ParticleSystemGameObjectFilter.List: return inList;
                case ParticleSystemGameObjectFilter.LayerMaskAndList: return inMask || inList;
                default: return inMask;
            }
        }
    }
}
