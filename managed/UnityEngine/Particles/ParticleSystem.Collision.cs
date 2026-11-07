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
            if (speed < C.minKillSpeed || speed > C.maxKillSpeed) return false;

            float bounce = C.bounce.Evaluate(age, R.Value(seed, R.Collision));
            float dampen = C.dampen.Evaluate(age, R.Value(seed, R.Collision + 1));
            var normalPart = normal * Vector3.Dot(velocity, normal);
            velocity = (velocity - normalPart - normalPart * bounce) * (1f - dampen);
            p.m_Velocity = WorldToSimulationRotation() * velocity;
            var after = impact + simToWorldRotation * ((p.m_Velocity + p.m_AnimatedVelocity) * (speedModifier * dt * (1f - fraction)));
            p.m_Position = toWorld.inverse.MultiplyPoint3x4(after);
            p.m_AgePercent += C.lifetimeLoss.Evaluate(age, R.Value(seed, R.Collision + 2)) * 100f;
            p.ExportLifetime();
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
        internal bool m_CollectTriggers;
        static readonly Collider[] s_Overlaps = new Collider[64];

        /// <summary>Classifies the particle against cached collider geometry, then applies the
        /// trigger actions. False when an action kills it.</summary>
        bool Trigger(ref Particle p)
        {
            var T = m_Trigger;
            var toWorld = SimulationToWorld();
            var world = toWorld.MultiplyPoint3x4(p.m_Position);
            var size = CurrentSize3D(ref p);
            float radius = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.5f * T.radiusScale;
            // Preserve the existing query path for other collider kinds. Sphere triggers use
            // the particle-only bounds/sweep path documented in SPHERE_MECHANISM.md.
            bool otherKinds = false;
            foreach (var c in T.colliders)
                if (c != null && !(c is SphereCollider)) { otherKinds = true; break; }
            int n = otherKinds ? Physics.OverlapSphereNonAlloc(world, radius, s_Overlaps, Physics.AllLayers, QueryTriggerInteraction.Collide) : 0;
            bool inside = false;
            for (int i = 0; i < n && !inside; i++)
                foreach (var c in T.colliders)
                    if (c != null && !(c is SphereCollider) && ReferenceEquals(c, s_Overlaps[i])) { inside = true; break; }
            if (!inside)
            {
                var start = toWorld.MultiplyPoint3x4(p.m_Position - (p.m_Velocity + p.m_AnimatedVelocity) * m_LastStepDt);
                float sphereRadius = (T.radiusScale * 0.5f) * Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                Unbound.Runtime.PhysicsWorld.PrepareQuery();
                foreach (var c in T.colliders)
                    if (c is SphereCollider sphere && sphere.m_Registered && sphere.m_Shape != 0 &&
                        SphereTriggerOverlap(start, world, sphereRadius, sphere)) { inside = true; break; }
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
        /// <summary>This system's OnParticleTrigger is being sent: SetTriggerParticles is valid.</summary>
        bool m_SendingTriggerMessage;

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

        /// <summary>SetTriggerParticles: writes particles[offset, offset + count) back to the
        /// live particles the <paramref name="type"/> entries at the same positions came from,
        /// through SetParticles' write path (6000.0 Scripting API: "Write modified particles
        /// back to the Particle System, during a call to OnParticleTrigger"; offset and count
        /// select "a subset of the returned particles"). A null list, a negative offset and an
        /// out-of-range count behave as in SetParticles (the count then covers every particle
        /// the list and the entries both have). Only the public fields the script changed are
        /// written: see <see cref="MergeChangedFields"/>.</summary>
        internal void WriteBackTriggerParticles(ParticleSystemTriggerEventType type, List<Particle> particles, int offset, int count)
        {
            if (particles == null) throw new System.ArgumentNullException(nameof(particles));
            if (offset < 0) throw new System.ArgumentOutOfRangeException(nameof(offset));
            var entries = m_TriggerParticles[(int)type];
            // The docs give no result outside the callback (Unbound's choice, unmeasured): an
            // error, and nothing is written.
            if (!m_SendingTriggerMessage)
            {
                Debug.LogError("ParticlePhysicsExtensions.SetTriggerParticles is only valid during a call to OnParticleTrigger of the particle system; nothing was written back");
                return;
            }
            // GetSafeTriggerParticlesSize bounds the entries (Unbound's choice, unmeasured: an
            // offset past them is an error, as a negative one is).
            int available = System.Math.Min(particles.Count, entries.Count) - offset;
            if (available < 0) throw new System.ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || count > available) count = available;
            if (count == 0) return;

            var slotOf = new Dictionary<uint, int>();
            for (int i = 0; i < m_Count; i++)
            {
                // An orphaned trail is no longer a particle.
                if ((m_Particles[i].m_Flags & Particle.FlagOrphanTrail) != 0) continue;
                uint serial = TriggerSerialOf(m_Particles[i]);
                if (IsCurrentTriggerSerial(serial)) slotOf.TryAdd(serial, i);
            }
            var slots = new int[count];
            var values = new Particle[count];
            var valueOf = new Dictionary<int, int>();
            int n = 0;
            for (int k = 0; k < count; k++)
            {
                var entry = entries[offset + k];
                uint serial = TriggerSerialOf(entry);
                // Unbound's choices (unmeasured): an entry whose particle is gone (killed as a
                // newborn, replaced, removed by a script) writes nothing, and a write-back does
                // not undo a trigger Kill of the same update (the particle still dies).
                if (!slotOf.TryGetValue(serial, out int slot)) continue;
                if ((m_Particles[slot].m_Flags & Particle.FlagTriggerKilled) != 0) continue;
                // A particle listed twice (by two internal steps) gets both entries' changes,
                // the later entry's winning a field both changed (Unbound's choice, unmeasured).
                var value = particles[offset + k];
                if (valueOf.TryGetValue(slot, out int index))
                {
                    MergeChangedFields(ref values[index], value, entry);
                    continue;
                }
                var probe = default(Particle);
                if (!MergeChangedFields(ref probe, value, entry)) continue;
                // WriteParticlesAt allocates the axis attribute first: read the live particle
                // after it, so an unchanged axis stays the allocated one.
                if (n == 0) MaterializeAxisOfRotation();
                var merged = m_Particles[slot];
                MergeChangedFields(ref merged, value, entry);
                valueOf.Add(slot, n);
                slots[n] = slot;
                values[n++] = merged;
            }
            WriteParticlesAt(slots, values, n);
        }

        // Unbound's choice (unmeasured; the 6000.0 docs say "Write modified particles back"
        // and show only a whole-list write-back after a colour change): an entry is a copy of
        // the particle taken part-way through an internal step (after it moved, before its
        // rotation and birth sub-emitters advanced) and possibly an earlier step of the update,
        // so writing a whole entry back would roll those back. Only the public Particle fields
        // whose value differs bit for bit from the entry the script was given are written onto
        // the live particle; the rest of it, its internal state (encoded age, sub-emitter
        // counters, noise, flags, trigger serial) included, stays as it is now. A
        // changed remaining or start lifetime clears the encoded age, as the Particle setters
        // do, so WriteParticlesAt kills or re-encodes it as SetParticles does; an unchanged
        // entry writes nothing.

        /// <summary>Copies onto <paramref name="target"/> the public fields of
        /// <paramref name="value"/> that differ from <paramref name="entry"/>; false when none
        /// does.</summary>
        static bool MergeChangedFields(ref Particle target, in Particle value, in Particle entry)
        {
            bool changed = false;
            if (!Same(value.m_Position, entry.m_Position)) { target.m_Position = value.m_Position; changed = true; }
            if (!Same(value.m_Velocity, entry.m_Velocity)) { target.m_Velocity = value.m_Velocity; changed = true; }
            if (!Same(value.m_AxisOfRotation, entry.m_AxisOfRotation)) { target.m_AxisOfRotation = value.m_AxisOfRotation; changed = true; }
            if (!Same(value.m_Rotation, entry.m_Rotation)) { target.m_Rotation = value.m_Rotation; changed = true; }
            if (!Same(value.m_AngularVelocity, entry.m_AngularVelocity)) { target.m_AngularVelocity = value.m_AngularVelocity; changed = true; }
            if (!Same(value.m_StartSize, entry.m_StartSize)) { target.m_StartSize = value.m_StartSize; changed = true; }
            var c = value.m_StartColor;
            var e = entry.m_StartColor;
            if (c.r != e.r || c.g != e.g || c.b != e.b || c.a != e.a) { target.m_StartColor = c; changed = true; }
            if (value.m_RandomSeed != entry.m_RandomSeed) { target.m_RandomSeed = value.m_RandomSeed; changed = true; }
            const uint meshSet = Particle.FlagMeshIndexSet;
            if (value.m_MeshIndex != entry.m_MeshIndex || ((value.m_Flags ^ entry.m_Flags) & meshSet) != 0)
            {
                target.m_MeshIndex = value.m_MeshIndex;
                target.m_Flags = (target.m_Flags & ~meshSet) | (value.m_Flags & meshSet);
                changed = true;
            }
            if (!Same(value.m_StartLifetime, entry.m_StartLifetime))
            {
                target.m_StartLifetime = value.m_StartLifetime;
                target.m_Flags &= ~Particle.FlagAgeInitialized;
                changed = true;
            }
            if (!Same(value.m_Lifetime, entry.m_Lifetime))
            {
                target.m_Lifetime = value.m_Lifetime;
                target.m_Flags &= ~(Particle.FlagAgeInitialized | Particle.FlagTriggerKilled);
                changed = true;
            }
            return changed;
        }

        static bool Same(float a, float b) => System.BitConverter.SingleToInt32Bits(a) == System.BitConverter.SingleToInt32Bits(b);

        static bool Same(Vector3 a, Vector3 b) => Same(a.x, b.x) && Same(a.y, b.y) && Same(a.z, b.z);

        internal void SendTriggerMessage()
        {
            if (m_GameObject == null) return;
            foreach (var list in m_TriggerParticles)
                if (list.Count > 0)
                {
                    bool sending = m_SendingTriggerMessage;
                    m_SendingTriggerMessage = true;
                    try { Messaging.SendFast(m_GameObject, "OnParticleTrigger"); }
                    finally { m_SendingTriggerMessage = sending; }
                    return;
                }
        }

        /// <summary>Sends the frame's OnParticleCollision messages: to this system's scripts and to
        /// the object hit, once per object.</summary>
        internal void SendCollisionMessages()
        {
            if (m_CollisionEvents.Count == 0 || m_GameObject == null) return;
            var seen = new HashSet<GameObject>();
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
