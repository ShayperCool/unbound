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
                }
            }
            if (float.IsPositiveInfinity(fraction)) return true;

            uint seed = p.m_RandomSeed;
            var simToWorldRotation = Quaternion.Inverse(WorldToSimulationRotation());
            var velocity = simToWorldRotation * p.m_Velocity;
            float speed = (simToWorldRotation * (p.m_Velocity + p.m_AnimatedVelocity)).magnitude;
            var impact = w0 + move * fraction;
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

        /// <summary>The particles of the frame's trigger events by type, for OnParticleTrigger and
        /// GetTriggerParticles (filled in the player loop only, measured).</summary>
        internal readonly List<Particle>[] m_TriggerParticles = { new List<Particle>(), new List<Particle>(), new List<Particle>(), new List<Particle>() };
        internal bool m_CollectTriggers;
        static readonly Collider[] s_Overlaps = new Collider[64];

        /// <summary>Applies the trigger module to a particle: inside means its sphere (half its size
        /// times the radius scale) touches one of the colliders as the physics scene has them (moved
        /// colliders only count once synced). False when an action kills it.</summary>
        bool Trigger(ref Particle p)
        {
            var T = m_Trigger;
            var world = SimulationToWorld().MultiplyPoint3x4(p.m_Position);
            var size = CurrentSize3D(ref p);
            float radius = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.5f * T.radiusScale;
            int n = Physics.OverlapSphereNonAlloc(world, radius, s_Overlaps, Physics.AllLayers, QueryTriggerInteraction.Collide);
            bool inside = false;
            for (int i = 0; i < n && !inside; i++)
                foreach (var c in T.colliders)
                    if (c != null && ReferenceEquals(c, s_Overlaps[i])) { inside = true; break; }
            bool was = (p.m_Flags & Particle.FlagInsideTrigger) != 0;
            if (inside) p.m_Flags |= Particle.FlagInsideTrigger;
            else p.m_Flags &= ~Particle.FlagInsideTrigger;
            if (!Act(ref p, inside ? T.inside : T.outside, inside ? ParticleSystemTriggerEventType.Inside : ParticleSystemTriggerEventType.Outside)) return false;
            if (inside && !was && !Act(ref p, T.enter, ParticleSystemTriggerEventType.Enter)) return false;
            if (!inside && was && !Act(ref p, T.exit, ParticleSystemTriggerEventType.Exit)) return false;
            return true;
        }

        bool Act(ref Particle p, ParticleSystemOverlapAction action, ParticleSystemTriggerEventType type)
        {
            if (action == ParticleSystemOverlapAction.Kill) return false;
            if (action == ParticleSystemOverlapAction.Callback && m_CollectTriggers) m_TriggerParticles[(int)type].Add(p);
            return true;
        }

        internal void SendTriggerMessage()
        {
            if (m_GameObject == null) return;
            foreach (var list in m_TriggerParticles)
                if (list.Count > 0)
                {
                    Messaging.SendFast(m_GameObject, "OnParticleTrigger");
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

        /// <summary>The acceleration (simulation space) the force fields give a particle.</summary>
        Vector3 ForceFieldAcceleration(ref Particle p, float age, Quaternion worldToSim)
        {
            var X = m_ExternalForces;
            var world = SimulationToWorld().MultiplyPoint3x4(p.m_Position);
            var total = Vector3.zero;
            foreach (var field in ParticleSystemForceField.FindAll())
            {
                if (field == null || !field.isActiveAndEnabled || !Influences(field)) continue;
                var t = field.transform;
                var local = t.InverseTransformPoint(world);
                if (!field.Contains(local)) continue;
                float r = R.Value(p.m_RandomSeed, R.Force + 7);
                var direction = new Vector3(field.directionX.Evaluate(age, r), field.directionY.Evaluate(age, r), field.directionZ.Evaluate(age, r));
                var accel = t.rotation * direction;
                float gravity = field.gravity.Evaluate(age, r);
                if (gravity != 0f)
                {
                    var toCenter = t.position - world;
                    if (toCenter.sqrMagnitude > 1e-12f) accel += toCenter.normalized * (gravity * 30f);
                }
                if (field.rotationSpeed.Evaluate(age, r) != 0f || field.rotationAttraction.Evaluate(age, r) != 0f) ReportUnsupported("force field rotation");
                float drag = field.drag.Evaluate(age, r);
                if (drag != 0f)
                {
                    // A deceleration of drag along the velocity (measured), times the particle's
                    // cross-section and speed when asked.
                    var velocity = Quaternion.Inverse(worldToSim) * (p.m_Velocity + p.m_AnimatedVelocity);
                    float speed = velocity.magnitude;
                    if (speed > 0f)
                    {
                        if (field.multiplyDragByParticleSize)
                        {
                            var size = CurrentSize3D(ref p);
                            float radius = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.5f;
                            drag *= Mathf.PI * radius * radius;
                        }
                        if (field.multiplyDragByParticleVelocity) drag *= speed;
                        accel -= velocity / speed * drag;
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
            float multiplier = X.multiplier * X.multiplierCurve.Evaluate(age, R.Value(p.m_RandomSeed, R.Force + 8));
            return worldToSim * (total * multiplier);
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
