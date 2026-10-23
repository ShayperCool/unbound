#pragma warning disable CS0612 // the obsolete shell shape types still appear in data
using System;
using Unbound.Runtime;
using R = Unbound.Runtime.ParticleRandom;

namespace UnityEngine
{
    // Spaces: a particle lives in its system's simulation space. "Emitter space" is the system
    // transform's frame with the scale its scaling mode selects (hierarchy: lossy scale, local:
    // local scale, shape: none). Local simulation keeps particles in emitter space; world
    // simulation keeps them in world space; custom keeps them in the custom transform's space.
    public sealed partial class ParticleSystem
    {
        /// <summary>World gravity (Physics.gravity / Physics2D.gravity defaults).</summary>
        internal static Vector3 s_Gravity3D = new Vector3(0f, -9.81f, 0f);
        internal static Vector3 s_Gravity2D = new Vector3(0f, -9.81f, 0f);

        Vector3 m_LastPosition;
        bool m_HasLastPosition;
        /// <summary>The emitter's world velocity (from its movement, or the custom value).</summary>
        internal Vector3 m_EmitterVelocity;
        /// <summary>The emitter's world movement of the current frame, spread over its steps.</summary>
        float m_FrameDistance;
        float m_FrameSimTime;

        internal void FrameUpdate()
        {
            float raw = m_Main.useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            TrackEmitter(raw);
            if (m_IsPaused) return;
            if (!m_IsPlaying && m_Count == 0) return;
            float dt = raw * m_Main.simulationSpeed;
            if (dt <= 0f) return;
            float max = Time.maximumParticleDeltaTime > 0f ? Time.maximumParticleDeltaTime : dt;
            int steps = Math.Min(Mathf.CeilToInt(dt / max - 1e-4f), 64);
            if (steps < 1) steps = 1;
            float step = dt / steps;
            m_FrameSimTime = dt;
            for (int i = 0; i < steps; i++)
            {
                Step(step, m_FrameDistance * (step / dt));
                if (m_IsPaused || !m_Registered) break;
            }
        }

        void TrackEmitter(float dt)
        {
            if (m_GameObject == null) return;
            var position = m_GameObject.m_Transform.position;
            if (!m_HasLastPosition)
            {
                m_LastPosition = position;
                m_HasLastPosition = true;
            }
            var delta = position - m_LastPosition;
            m_LastPosition = position;
            m_FrameDistance = delta.magnitude;
            if (m_Main.emitterVelocityMode == ParticleSystemEmitterVelocityMode.Custom)
                m_EmitterVelocity = m_Main.customEmitterVelocity;
            else if (dt > 0f)
                m_EmitterVelocity = delta / dt;
        }

        /// <summary>One simulation step: existing particles move and age, then new ones are emitted.</summary>
        internal void Step(float dt, float distance)
        {
            UpdateParticles(dt);
            if (m_IsPlaying && !m_StopEmitting && m_SubEmitterParent == null)
                AdvanceEmission(dt, distance);
            if (m_IsPlaying && m_StopEmitting && m_Count == 0)
            {
                m_IsPlaying = false;
                m_StopEmitting = false;
                OnStopped();
            }
        }

        // ------------------------------------------------------------------ spaces

        internal Vector3 EmitterScale()
        {
            var t = m_GameObject.m_Transform;
            switch (m_Main.scalingMode)
            {
                case ParticleSystemScalingMode.Hierarchy: return t.lossyScale;
                case ParticleSystemScalingMode.Local: return t.localScale;
                default: return Vector3.one;
            }
        }

        /// <summary>Emitter space to world.</summary>
        internal Matrix4x4 EmitterMatrix()
        {
            var t = m_GameObject.m_Transform;
            return Matrix4x4.TRS(t.position, t.rotation, EmitterScale());
        }

        /// <summary>Simulation space to world.</summary>
        internal Matrix4x4 SimulationToWorld()
        {
            switch (m_Main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.World:
                    return Matrix4x4.identity;
                case ParticleSystemSimulationSpace.Custom when m_Main.customSimulationSpace != null:
                    return m_Main.customSimulationSpace.localToWorldMatrix;
                default:
                    return EmitterMatrix();
            }
        }

        /// <summary>The rotation from world to simulation space (directions only).</summary>
        Quaternion WorldToSimulationRotation()
        {
            switch (m_Main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.World:
                    return Quaternion.identity;
                case ParticleSystemSimulationSpace.Custom when m_Main.customSimulationSpace != null:
                    return Quaternion.Inverse(m_Main.customSimulationSpace.rotation);
                default:
                    return Quaternion.Inverse(m_GameObject.m_Transform.rotation);
            }
        }

        /// <summary>A module vector given in `space` (local or world) expressed in simulation space.</summary>
        Vector3 ModuleToSimulation(Vector3 v, ParticleSystemSimulationSpace space, Quaternion worldToSim)
        {
            bool simWorld = m_Main.simulationSpace == ParticleSystemSimulationSpace.World;
            if (space == ParticleSystemSimulationSpace.World)
                return simWorld ? v : worldToSim * v;
            // Local module space: the emitter's axes.
            return simWorld ? m_GameObject.m_Transform.rotation * v : (m_Main.simulationSpace == ParticleSystemSimulationSpace.Local ? v : worldToSim * (m_GameObject.m_Transform.rotation * v));
        }

        // ------------------------------------------------------------------ emission over time

        void AdvanceEmission(float dt, float distance)
        {
            if (m_DelayRemaining > 0f)
            {
                float d = Mathf.Min(dt, m_DelayRemaining);
                m_DelayRemaining -= d;
                m_TotalTime += d;
                dt -= d;
                if (dt <= 0f) return;
            }
            float duration = Mathf.Max(m_Main.duration, 0.0001f);
            float remaining = dt;
            float totalDt = dt;
            int guard = 0;
            while (remaining > 0f && !m_StopEmitting && guard++ < 1000)
            {
                float t0 = m_Time;
                float t1 = Mathf.Min(t0 + remaining, duration);
                float segment = Mathf.Max(0f, t1 - t0);
                remaining -= segment;
                if (remaining < 1e-7f) remaining = 0f;
                EmitOver(t0, t1, remaining, distance * (segment / totalDt));
                m_Time = t1;
                m_TotalTime += segment;
                if (m_Time >= duration)
                {
                    if (m_Main.loop)
                    {
                        m_Time = 0f;
                        m_Loop++;
                        Array.Clear(m_BurstCycles, 0, m_BurstCycles.Length);
                    }
                    else
                    {
                        m_Time = duration;
                        m_StopEmitting = true;
                    }
                }
            }
        }

        /// <summary>Emission for the loop time [t0, t1). Particles born in it are aged to the end of
        /// the step (`extraAge` is the step's time left after t1).</summary>
        void EmitOver(float t0, float t1, float extraAge, float distance)
        {
            if (!m_Emission.enabled) return;
            float duration = Mathf.Max(m_Main.duration, 0.0001f);
            float span = t1 - t0;
            float nt = Mathf.Clamp01(t0 / duration);

            float rate = m_Emission.rateOverTime.Evaluate(nt, NextFloat());
            if (rate > 0f && span > 0f)
            {
                m_RateAccumulator += rate * span;
                int n = (int)(m_RateAccumulator + 1e-4f);
                if (n > 0)
                {
                    m_RateAccumulator -= n;
                    for (int k = 0; k < n; k++)
                    {
                        float bornAt = t0 + span * (k + 1f) / n;
                        EmitParticles(1, (t1 - bornAt) + extraAge, default, false, k, n);
                    }
                }
            }

            float perDistance = m_Emission.rateOverDistance.Evaluate(nt, NextFloat());
            if (perDistance > 0f && distance > 0f)
            {
                m_DistanceAccumulator += perDistance * distance;
                int n = (int)(m_DistanceAccumulator + 1e-4f);
                if (n > 0)
                {
                    m_DistanceAccumulator -= n;
                    EmitParticles(n, extraAge, default, false);
                }
            }

            var bursts = m_Emission.bursts;
            if (bursts.Count == 0) return;
            if (m_BurstCycles.Length != bursts.Count) Array.Resize(ref m_BurstCycles, bursts.Count);
            for (int i = 0; i < bursts.Count; i++)
            {
                var burst = bursts[i];
                int cycles = burst.cycleCount; // 0: forever
                float interval = Mathf.Max(burst.m_RepeatInterval, 0.0001f);
                int c = m_BurstCycles[i];
                while (cycles == 0 || c < cycles)
                {
                    float at = burst.m_Time + c * interval;
                    if (at >= t1 || at >= duration) break;
                    if (at >= t0 - 1e-6f && (burst.probability >= 1f || NextFloat() < burst.probability))
                    {
                        int count = BurstCount(burst.m_Count, at / duration);
                        if (count > 0) EmitParticles(count, (t1 - at) + extraAge, default, false);
                    }
                    c++;
                    if (cycles == 0 && c > 100000) break;
                }
                m_BurstCycles[i] = c;
            }
        }

        int BurstCount(MinMaxCurve count, float nt)
        {
            float r = NextFloat();
            if (count.mode == ParticleSystemCurveMode.TwoConstants)
            {
                float min = Mathf.Min(count.constantMin, count.constantMax), max = Mathf.Max(count.constantMin, count.constantMax);
                return Mathf.Clamp((int)Math.Floor(Mathf.Lerp(min, max + 1f, r)), (int)min, (int)max);
            }
            return Mathf.Max(0, Mathf.RoundToInt(count.Evaluate(nt, r)));
        }

        // ------------------------------------------------------------------ emitting particles

        internal void EmitParticles(int count, float preAge, EmitParams overrides, bool hasOverrides, int spreadIndex = -1, int spreadCount = 0)
        {
            if (m_GameObject == null) return;
            var emitter = EmitterMatrix();
            for (int k = 0; k < count; k++)
            {
                int index = Allocate();
                if (index < 0) return;
                int si = spreadIndex >= 0 ? spreadIndex : k;
                int sn = spreadIndex >= 0 ? spreadCount : count;
                if (!InitParticle(ref m_Particles[index], emitter, preAge, hasOverrides, ref overrides, si, sn, null))
                    RemoveAt(index);
            }
        }

        /// <summary>A new particle with the main and shape modules' start values. `origin` (world)
        /// replaces the emitter's position (sub-emitters emit from their parent particle).</summary>
        bool InitParticle(ref Particle p, Matrix4x4 emitter, float preAge, bool hasOverrides, ref EmitParams o, int spreadIndex, int spreadCount, Vector3? origin)
        {
            uint seed = hasOverrides && o.m_RandomSeedSet ? o.m_Particle.m_RandomSeed : NextUInt();
            var M = m_Main;
            float duration = Mathf.Max(M.duration, 0.0001f);
            float nt = Mathf.Clamp01(m_Time / duration);

            p = default;
            p.m_RandomSeed = seed;
            float lifetime = M.startLifetime.Evaluate(nt, R.Value(seed, R.Lifetime));
            if (m_LifetimeByEmitterSpeed.enabled)
            {
                var range = m_LifetimeByEmitterSpeed.range;
                float s = Mathf.InverseLerp(range.x, range.y, m_EmitterVelocity.magnitude);
                lifetime *= m_LifetimeByEmitterSpeed.curve.Evaluate(s, R.Value(seed, R.Inherit + 100));
            }
            float speed = M.startSpeed.Evaluate(nt, R.Value(seed, R.Speed));
            Vector3 size;
            if (M.startSize3D)
                size = new Vector3(M.startSizeX.Evaluate(nt, R.Value(seed, R.SizeX)), M.startSizeY.Evaluate(nt, R.Value(seed, R.SizeY)), M.startSizeZ.Evaluate(nt, R.Value(seed, R.SizeZ)));
            else
            {
                float s = M.startSizeX.Evaluate(nt, R.Value(seed, R.SizeX));
                size = new Vector3(s, s, s);
            }
            Vector3 rotation = M.startRotation3D
                ? new Vector3(M.startRotationX.Evaluate(nt, R.Value(seed, R.RotX)), M.startRotationY.Evaluate(nt, R.Value(seed, R.RotY)), M.startRotationZ.Evaluate(nt, R.Value(seed, R.RotZ)))
                : new Vector3(0f, 0f, M.startRotationZ.Evaluate(nt, R.Value(seed, R.RotZ)));
            if (M.flipRotation > 0f && R.Value(seed, R.Flip) < M.flipRotation)
            {
                rotation = -rotation;
                p.m_Flags |= Particle.FlagFlipRotation;
            }
            var color = M.startColor.Evaluate(nt, R.Value(seed, R.Color));

            SampleShape(spreadIndex, spreadCount, out var pos, out var dir);
            var shapeOffset = pos;
            var velocity = dir * speed;

            // Emitter space to simulation space.
            var simSpace = M.simulationSpace;
            if (simSpace == ParticleSystemSimulationSpace.Custom && M.customSimulationSpace == null) simSpace = ParticleSystemSimulationSpace.Local;
            if (origin.HasValue)
            {
                // Emitting from a world point with this system's orientation and scale.
                var t = m_GameObject.m_Transform;
                var frame = Matrix4x4.TRS(origin.Value, t.rotation, EmitterScale());
                var worldPos = frame.MultiplyPoint3x4(pos);
                var worldVel = frame.MultiplyVector(velocity);
                FromWorld(simSpace, emitter, worldPos, worldVel, out pos, out velocity);
            }
            else if (simSpace != ParticleSystemSimulationSpace.Local)
            {
                FromWorld(simSpace, emitter, emitter.MultiplyPoint3x4(pos), emitter.MultiplyVector(velocity), out pos, out velocity);
            }

            if (m_InheritVelocity.enabled && m_InheritVelocity.mode == ParticleSystemInheritVelocityMode.Initial && simSpace != ParticleSystemSimulationSpace.Local)
            {
                var inherited = m_EmitterVelocity * m_InheritVelocity.curve.Evaluate(nt, R.Value(seed, R.Inherit));
                velocity += simSpace == ParticleSystemSimulationSpace.World ? inherited : WorldToSimulationRotation() * inherited;
            }

            if (hasOverrides)
            {
                if (o.m_PositionSet)
                {
                    if (o.m_ApplyShapeToPosition)
                    {
                        var offset = simSpace == ParticleSystemSimulationSpace.Local ? shapeOffset : emitter.MultiplyVector(shapeOffset);
                        pos = o.m_Particle.m_Position + offset;
                    }
                    else pos = o.m_Particle.m_Position;
                }
                if (o.m_VelocitySet) velocity = o.m_Particle.m_Velocity;
                if (o.m_StartLifetimeSet) lifetime = o.m_Particle.m_StartLifetime;
                if (o.m_StartSizeSet) size = o.m_Particle.m_StartSize;
                if (o.m_RotationSet) rotation = o.m_Particle.m_Rotation;
                if (o.m_AngularVelocitySet) p.m_AngularVelocity = o.m_Particle.m_AngularVelocity;
                if (o.m_AxisOfRotationSet) p.m_AxisOfRotation = o.m_Particle.m_AxisOfRotation;
                if (o.m_StartColorSet) color = o.m_Particle.m_StartColor;
                if (o.m_MeshIndexSet)
                {
                    p.m_MeshIndex = o.m_Particle.m_MeshIndex;
                    p.m_Flags |= Particle.FlagMeshIndexSet;
                }
            }
            if (!(hasOverrides && o.m_AxisOfRotationSet)) p.m_AxisOfRotation = Vector3.forward;

            p.m_Position = pos;
            p.m_Velocity = velocity;
            p.m_InitialVelocity = velocity;
            p.m_StartSize = size;
            p.m_Rotation = rotation;
            p.m_StartColor = color;
            p.m_StartLifetime = lifetime;
            p.m_Lifetime = lifetime;
            if (lifetime <= 0f) return false;
            if (preAge > 0f)
            {
                p.m_Position += velocity * preAge;
                p.m_Lifetime -= preAge;
                if (p.m_Lifetime <= 0f) return false;
            }
            return true;
        }

        void FromWorld(ParticleSystemSimulationSpace simSpace, Matrix4x4 emitter, Vector3 worldPos, Vector3 worldVel, out Vector3 pos, out Vector3 vel)
        {
            switch (simSpace)
            {
                case ParticleSystemSimulationSpace.World:
                    pos = worldPos;
                    vel = worldVel;
                    return;
                case ParticleSystemSimulationSpace.Custom:
                {
                    var inv = m_Main.customSimulationSpace.worldToLocalMatrix;
                    pos = inv.MultiplyPoint3x4(worldPos);
                    vel = inv.MultiplyVector(worldVel);
                    return;
                }
                default:
                {
                    var inv = emitter.inverse;
                    pos = inv.MultiplyPoint3x4(worldPos);
                    vel = inv.MultiplyVector(worldVel);
                    return;
                }
            }
        }

        // ------------------------------------------------------------------ the shape module

        /// <summary>A start position and direction in emitter space.</summary>
        void SampleShape(int spreadIndex, int spreadCount, out Vector3 pos, out Vector3 dir)
        {
            var S = m_Shape;
            if (!S.enabled)
            {
                pos = Vector3.zero;
                dir = Vector3.forward;
                return;
            }
            float radius = S.radius;
            float thickness = Mathf.Clamp01(S.radiusThickness);
            switch (S.shapeType)
            {
                case ParticleSystemShapeType.Sphere:
                case ParticleSystemShapeType.SphereShell:
                case ParticleSystemShapeType.Hemisphere:
                case ParticleSystemShapeType.HemisphereShell:
                {
                    dir = RandomUnitVector();
                    if (S.shapeType == ParticleSystemShapeType.Hemisphere || S.shapeType == ParticleSystemShapeType.HemisphereShell)
                        dir.z = Mathf.Abs(dir.z);
                    bool shell = S.shapeType == ParticleSystemShapeType.SphereShell || S.shapeType == ParticleSystemShapeType.HemisphereShell;
                    float r = shell ? radius : radius * Mathf.Lerp(1f - thickness, 1f, Mathf.Pow(NextFloat(), 1f / 3f));
                    pos = dir * r;
                    break;
                }
                case ParticleSystemShapeType.Cone:
                case ParticleSystemShapeType.ConeShell:
                case ParticleSystemShapeType.ConeVolume:
                case ParticleSystemShapeType.ConeVolumeShell:
                {
                    float phi = ArcAngle(spreadIndex, spreadCount) * Mathf.Deg2Rad;
                    bool shell = S.shapeType == ParticleSystemShapeType.ConeShell || S.shapeType == ParticleSystemShapeType.ConeVolumeShell;
                    float r01 = shell ? 1f : Mathf.Lerp(1f - thickness, 1f, Mathf.Sqrt(NextFloat()));
                    float angle = Mathf.Clamp(S.angle, 0f, 90f) * Mathf.Deg2Rad;
                    float cos = Mathf.Cos(phi), sin = Mathf.Sin(phi);
                    // Directions fan out with the distance from the axis, reaching the cone angle at the rim.
                    float tilt = radius > 1e-4f ? angle * r01 : angle * Mathf.Sqrt(NextFloat());
                    dir = new Vector3(cos * Mathf.Sin(tilt), sin * Mathf.Sin(tilt), Mathf.Cos(tilt));
                    pos = new Vector3(cos * radius * r01, sin * radius * r01, 0f);
                    if (S.shapeType == ParticleSystemShapeType.ConeVolume || S.shapeType == ParticleSystemShapeType.ConeVolumeShell)
                    {
                        float along = NextFloat() * S.length;
                        pos += dir * (along / Mathf.Max(dir.z, 1e-4f));
                    }
                    break;
                }
                case ParticleSystemShapeType.Box:
                    pos = new Vector3(NextFloat() - 0.5f, NextFloat() - 0.5f, NextFloat() - 0.5f);
                    dir = Vector3.forward;
                    break;
                case ParticleSystemShapeType.BoxShell:
                {
                    pos = new Vector3(NextFloat() - 0.5f, NextFloat() - 0.5f, NextFloat() - 0.5f);
                    int axis = (int)(NextFloat() * 3f) % 3;
                    pos[axis] = NextFloat() < 0.5f ? -0.5f : 0.5f;
                    dir = Vector3.forward;
                    break;
                }
                case ParticleSystemShapeType.BoxEdge:
                {
                    pos = new Vector3(NextFloat() < 0.5f ? -0.5f : 0.5f, NextFloat() < 0.5f ? -0.5f : 0.5f, NextFloat() < 0.5f ? -0.5f : 0.5f);
                    int axis = (int)(NextFloat() * 3f) % 3;
                    pos[axis] = NextFloat() - 0.5f;
                    dir = Vector3.forward;
                    break;
                }
                case ParticleSystemShapeType.Circle:
                case ParticleSystemShapeType.CircleEdge:
                {
                    float phi = ArcAngle(spreadIndex, spreadCount) * Mathf.Deg2Rad;
                    float r01 = S.shapeType == ParticleSystemShapeType.CircleEdge ? 1f : Mathf.Lerp(1f - thickness, 1f, Mathf.Sqrt(NextFloat()));
                    dir = new Vector3(Mathf.Cos(phi), Mathf.Sin(phi), 0f);
                    pos = dir * (radius * r01);
                    break;
                }
                case ParticleSystemShapeType.SingleSidedEdge:
                {
                    float u = S.radiusMode == ParticleSystemShapeMultiModeValue.BurstSpread && spreadCount > 1
                        ? spreadIndex / (float)(spreadCount - 1)
                        : NextFloat();
                    pos = new Vector3(Mathf.Lerp(-radius, radius, u), 0f, 0f);
                    dir = Vector3.up;
                    break;
                }
                case ParticleSystemShapeType.Donut:
                {
                    float phi = ArcAngle(spreadIndex, spreadCount) * Mathf.Deg2Rad;
                    float theta = NextFloat() * Mathf.PI * 2f;
                    float tube = S.donutRadius * Mathf.Lerp(1f - thickness, 1f, Mathf.Sqrt(NextFloat()));
                    var ring = new Vector3(Mathf.Cos(phi), Mathf.Sin(phi), 0f);
                    dir = ring * Mathf.Cos(theta) + Vector3.forward * Mathf.Sin(theta);
                    pos = ring * radius + dir * tube;
                    break;
                }
                case ParticleSystemShapeType.Rectangle:
                    pos = new Vector3(NextFloat() - 0.5f, NextFloat() - 0.5f, 0f);
                    dir = Vector3.forward;
                    break;
                default:
                    SampleMeshShape(out pos, out dir);
                    break;
            }

            if (S.randomPositionAmount > 0f) pos += RandomUnitVector() * (S.randomPositionAmount * Mathf.Pow(NextFloat(), 1f / 3f));

            var rot = Quaternion.Euler(S.rotation);
            var shapeMatrix = Matrix4x4.TRS(S.position, rot, S.scale);
            pos = shapeMatrix.MultiplyPoint3x4(pos);
            var d = shapeMatrix.MultiplyVector(dir);
            dir = d.sqrMagnitude > 1e-12f ? d.normalized : rot * dir;

            if (S.sphericalDirectionAmount > 0f)
            {
                var outward = (pos - S.position).sqrMagnitude > 1e-12f ? (pos - S.position).normalized : dir;
                dir = Vector3.Lerp(dir, outward, S.sphericalDirectionAmount).normalized;
            }
            if (S.randomDirectionAmount > 0f)
            {
                var random = Vector3.Lerp(dir, RandomUnitVector(), S.randomDirectionAmount);
                dir = random.sqrMagnitude > 1e-12f ? random.normalized : dir;
            }
            if (m_Main.scalingMode == ParticleSystemScalingMode.Shape)
                pos = Vector3.Scale(pos, m_GameObject.m_Transform.lossyScale);
        }

        /// <summary>The angle (degrees) around the arc for a new particle, by the arc's mode.</summary>
        float ArcAngle(int index, int count)
        {
            var S = m_Shape;
            float arc = S.arc;
            float u;
            switch (S.arcMode)
            {
                case ParticleSystemShapeMultiModeValue.Loop:
                    u = Frac(S.arcSpeed.Evaluate(0f, 1f) * m_TotalTime);
                    break;
                case ParticleSystemShapeMultiModeValue.PingPong:
                    u = Mathf.PingPong(S.arcSpeed.Evaluate(0f, 1f) * m_TotalTime, 1f);
                    break;
                case ParticleSystemShapeMultiModeValue.BurstSpread:
                    u = count > 0 ? index / (float)count : 0f;
                    break;
                default:
                    u = NextFloat();
                    break;
            }
            if (S.arcSpread > 0f) u = Mathf.Floor(u / S.arcSpread) * S.arcSpread;
            return u * arc;
        }

        static float Frac(float x) => x - Mathf.Floor(x);

        /// <summary>Mesh, renderer and sprite shapes: a random vertex of the mesh (with its normal),
        /// or a point of the sprite's rectangle; a unit box when there is nothing to sample.</summary>
        void SampleMeshShape(out Vector3 pos, out Vector3 dir)
        {
            var S = m_Shape;
            Mesh mesh = S.mesh;
            if (S.shapeType == ParticleSystemShapeType.MeshRenderer && S.meshRenderer != null)
            {
                var filter = S.meshRenderer.GetComponent<MeshFilter>();
                mesh = filter != null ? filter.m_Mesh : null;
            }
            if ((S.shapeType == ParticleSystemShapeType.Mesh || S.shapeType == ParticleSystemShapeType.MeshRenderer) && mesh != null && mesh.m_Vertices.Length > 0)
            {
                int i = Math.Min((int)(NextFloat() * mesh.m_Vertices.Length), mesh.m_Vertices.Length - 1);
                pos = mesh.m_Vertices[i];
                dir = mesh.m_Normals != null && i < mesh.m_Normals.Length && mesh.m_Normals[i].sqrMagnitude > 0f ? mesh.m_Normals[i].normalized : Vector3.forward;
                pos += dir * S.normalOffset;
                return;
            }
            Sprite sprite = S.shapeType == ParticleSystemShapeType.SpriteRenderer && S.spriteRenderer != null ? S.spriteRenderer.sprite : S.sprite;
            if ((S.shapeType == ParticleSystemShapeType.Sprite || S.shapeType == ParticleSystemShapeType.SpriteRenderer) && sprite != null)
            {
                var b = sprite.bounds;
                pos = new Vector3(Mathf.Lerp(b.min.x, b.max.x, NextFloat()), Mathf.Lerp(b.min.y, b.max.y, NextFloat()), 0f);
                dir = Vector3.forward;
                return;
            }
            pos = new Vector3(NextFloat() - 0.5f, NextFloat() - 0.5f, NextFloat() - 0.5f);
            dir = Vector3.forward;
        }

        Vector3 RandomUnitVector()
        {
            float z = NextFloat() * 2f - 1f;
            float a = NextFloat() * Mathf.PI * 2f;
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), z);
        }

        // ------------------------------------------------------------------ updating particles

        void UpdateParticles(float dt)
        {
            if (m_Count == 0) return;
            float duration = Mathf.Max(m_Main.duration, 0.0001f);
            float nt = Mathf.Clamp01(m_Time / duration);
            var worldToSim = WorldToSimulationRotation();

            var gravityWorld = m_Main.gravitySource == ParticleSystemGravitySource.Physics2D ? s_Gravity2D : s_Gravity3D;
            bool hasGravity = m_Main.gravityModifier.mode != ParticleSystemCurveMode.Constant || m_Main.gravityModifier.constant != 0f;
            var gravity = m_Main.simulationSpace == ParticleSystemSimulationSpace.World ? gravityWorld : worldToSim * gravityWorld;

            bool simWorld = m_Main.simulationSpace == ParticleSystemSimulationSpace.World;
            var center = simWorld ? m_GameObject.m_Transform.position : Vector3.zero;
            if (m_Main.simulationSpace == ParticleSystemSimulationSpace.Custom && m_Main.customSimulationSpace != null)
                center = m_Main.customSimulationSpace.InverseTransformPoint(m_GameObject.m_Transform.position);

            var ring = m_Main.ringBufferMode;
            bool birthSubs = m_SubEmitters.enabled && HasSubEmitters(ParticleSystemSubEmitterType.Birth);
            bool deathSubs = m_SubEmitters.enabled && HasSubEmitters(ParticleSystemSubEmitterType.Death);
            var V = m_Velocity;
            var F = m_Force;
            var L = m_LimitVelocity;
            var N = m_Noise;
            var RO = m_Rotation;
            var RS = m_RotationBySpeed;
            var IV = m_InheritVelocity;
            bool inheritCurrent = IV.enabled && IV.mode == ParticleSystemInheritVelocityMode.Current && simWorld;
            float forceRandom = F.enabled && F.randomized ? NextFloat() : 0f;
            float noiseTime = N.enabled ? N.scrollSpeed.Evaluate(nt, 1f) * m_TotalTime : 0f;

            for (int i = m_Count - 1; i >= 0; i--)
            {
                ref var p = ref m_Particles[i];
                float ageBefore = p.m_StartLifetime - p.m_Lifetime;
                p.m_Lifetime -= dt;
                if (ring == ParticleSystemRingBufferMode.LoopUntilReplaced && p.m_StartLifetime > 0f)
                {
                    var range = m_Main.ringBufferLoopRange;
                    float start = Mathf.Clamp01(Mathf.Min(range.x, range.y)), end = Mathf.Clamp01(Mathf.Max(range.x, range.y));
                    if (end > start && p.Age01 >= end)
                        p.m_Lifetime = p.m_StartLifetime * (1f - start);
                }
                if (p.m_Lifetime <= 0f)
                {
                    if (ring == ParticleSystemRingBufferMode.PauseUntilReplaced)
                    {
                        p.m_Lifetime = 0f;
                        continue;
                    }
                    if (deathSubs) TriggerSubEmitters(ParticleSystemSubEmitterType.Death, ref p);
                    RemoveAt(i);
                    continue;
                }
                uint seed = p.m_RandomSeed;
                float age = p.Age01;
                var v = p.m_Velocity;

                if (hasGravity)
                    v += gravity * (m_Main.gravityModifier.Evaluate(nt, R.Value(seed, R.Gravity)) * dt);

                if (F.enabled)
                {
                    float r = F.randomized ? forceRandom : R.Value(seed, R.Force);
                    var f = new Vector3(F.x.Evaluate(age, r), F.y.Evaluate(age, r), F.z.Evaluate(age, r));
                    v += ModuleToSimulation(f, F.space, worldToSim) * dt;
                }

                if (L.enabled) LimitVelocity(ref v, ref p, age, worldToSim, dt);
                p.m_Velocity = v;

                var animated = Vector3.zero;
                float speedModifier = 1f;
                if (V.enabled)
                {
                    var lin = new Vector3(V.x.Evaluate(age, R.Value(seed, R.VelX)), V.y.Evaluate(age, R.Value(seed, R.VelY)), V.z.Evaluate(age, R.Value(seed, R.VelZ)));
                    if (lin != Vector3.zero) animated += ModuleToSimulation(lin, V.space, worldToSim);
                    float ro = R.Value(seed, R.Orbital);
                    var orbital = new Vector3(V.orbitalX.Evaluate(age, ro), V.orbitalY.Evaluate(age, ro), V.orbitalZ.Evaluate(age, ro));
                    float radial = V.radial.Evaluate(age, R.Value(seed, R.Radial));
                    if (orbital != Vector3.zero || radial != 0f)
                    {
                        var offset = new Vector3(V.orbitalOffsetX.Evaluate(age, ro), V.orbitalOffsetY.Evaluate(age, ro), V.orbitalOffsetZ.Evaluate(age, ro));
                        var rel = p.m_Position - (center + offset);
                        if (orbital != Vector3.zero && dt > 0f)
                        {
                            var turned = Quaternion.Euler(orbital * (dt * Mathf.Rad2Deg)) * rel;
                            animated += (turned - rel) / dt;
                        }
                        if (radial != 0f && rel.sqrMagnitude > 1e-12f) animated += rel.normalized * radial;
                    }
                    speedModifier = V.speedModifier.Evaluate(age, R.Value(seed, R.SpeedMod));
                }
                if (N.enabled) animated += NoiseVelocity(ref p, age, noiseTime);

                p.m_AnimatedVelocity = animated;
                p.m_Position += (v + animated) * (speedModifier * dt);
                if (inheritCurrent)
                    p.m_Position += m_EmitterVelocity * (IV.curve.Evaluate(age, R.Value(seed, R.Inherit)) * dt);

                if (RO.enabled || RS.enabled)
                {
                    var w = Vector3.zero;
                    if (RO.enabled)
                    {
                        float r = R.Value(seed, R.RotLife);
                        w += RO.separateAxes ? new Vector3(RO.x.Evaluate(age, r), RO.y.Evaluate(age, r), RO.z.Evaluate(age, r)) : new Vector3(0f, 0f, RO.z.Evaluate(age, r));
                    }
                    if (RS.enabled)
                    {
                        float s = Mathf.InverseLerp(RS.range.x, RS.range.y, (v + animated).magnitude);
                        float r = R.Value(seed, R.RotSpeed);
                        w += RS.separateAxes ? new Vector3(RS.x.Evaluate(s, r), RS.y.Evaluate(s, r), RS.z.Evaluate(s, r)) : new Vector3(0f, 0f, RS.z.Evaluate(s, r));
                    }
                    if ((p.m_Flags & Particle.FlagFlipRotation) != 0) w = -w;
                    p.m_Rotation += w * dt;
                }
                p.m_Rotation += p.m_AngularVelocity * dt;

                if (birthSubs) BirthSubEmitters(ref p, ageBefore, p.m_StartLifetime - p.m_Lifetime, dt);
            }
        }

        void LimitVelocity(ref Vector3 v, ref Particle p, float age, Quaternion worldToSim, float dt)
        {
            var L = m_LimitVelocity;
            uint seed = p.m_RandomSeed;
            float r = R.Value(seed, R.Limit);
            float dampen = Mathf.Clamp01(L.dampen);
            if (L.separateAxes)
            {
                var limits = new Vector3(L.limitX.Evaluate(age, r), L.limitY.Evaluate(age, r), L.limitZ.Evaluate(age, r));
                // Limits are given in the module's space.
                bool convert = L.space == ParticleSystemSimulationSpace.World != (m_Main.simulationSpace == ParticleSystemSimulationSpace.World);
                var toModule = L.space == ParticleSystemSimulationSpace.World ? Quaternion.Inverse(worldToSim) : worldToSim;
                var local = convert ? toModule * v : v;
                for (int a = 0; a < 3; a++)
                {
                    float lim = Mathf.Abs(limits[a]);
                    float c = local[a];
                    if (Mathf.Abs(c) > lim) local[a] = Mathf.Lerp(c, Mathf.Sign(c) * lim, dampen);
                }
                v = convert ? Quaternion.Inverse(toModule) * local : local;
            }
            else
            {
                float lim = Mathf.Abs(L.limit.Evaluate(age, r));
                float speed = v.magnitude;
                if (speed > lim && speed > 0f) v *= Mathf.Lerp(1f, lim / speed, dampen);
            }
            float drag = L.drag.Evaluate(age, R.Value(seed, R.Drag));
            if (drag > 0f)
            {
                if (L.multiplyDragByParticleSize) drag *= Mathf.Max(p.m_StartSize.x, Mathf.Max(p.m_StartSize.y, p.m_StartSize.z));
                if (L.multiplyDragByParticleVelocity) drag *= v.magnitude;
                v *= Mathf.Max(0f, 1f - drag * dt);
            }
        }

        // ------------------------------------------------------------------ noise

        Vector3 NoiseVelocity(ref Particle p, float age, float scroll)
        {
            var N = m_Noise;
            uint seed = p.m_RandomSeed;
            float freq = Mathf.Max(N.frequency, 1e-4f);
            var q = p.m_Position * freq + new Vector3(scroll, scroll * 0.7f, scroll * 1.3f);
            var n = new Vector3(Fbm(q, 0), Fbm(q, 1), Fbm(q, 2));
            if (N.remapEnabled)
            {
                n.x = N.remapX.Evaluate((n.x + 1f) * 0.5f, R.Value(seed, R.Noise));
                n.y = (N.separateAxes ? N.remapY : N.remapX).Evaluate((n.y + 1f) * 0.5f, R.Value(seed, R.Noise));
                n.z = (N.separateAxes ? N.remapZ : N.remapX).Evaluate((n.z + 1f) * 0.5f, R.Value(seed, R.Noise));
            }
            float r = R.Value(seed, R.NoiseStrength);
            var strength = N.separateAxes
                ? new Vector3(N.strengthX.Evaluate(age, r), N.strengthY.Evaluate(age, r), N.strengthZ.Evaluate(age, r))
                : Vector3.one * N.strengthX.Evaluate(age, r);
            if (N.damping) strength *= freq;
            return Vector3.Scale(n, strength) * N.positionAmount.Evaluate(age, r);
        }

        float Fbm(Vector3 q, int channel)
        {
            var N = m_Noise;
            int octaves = Mathf.Clamp(N.octaveCount, 1, 4);
            float sum = 0f, amplitude = 1f, norm = 0f, scale = 1f;
            var offset = new Vector3(channel * 31.416f, channel * 17.23f, channel * 47.77f);
            for (int o = 0; o < octaves; o++)
            {
                sum += GradientNoise(q * scale + offset) * amplitude;
                norm += amplitude;
                amplitude *= N.octaveMultiplier;
                scale *= N.octaveScale;
            }
            return norm > 0f ? Mathf.Clamp(sum / norm, -1f, 1f) : 0f;
        }

        /// <summary>Smooth 3D gradient noise in about [-1, 1], with hashed gradients.</summary>
        internal static float GradientNoise(Vector3 p)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
            float fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;
            float u = Fade(fx), v = Fade(fy), w = Fade(fz);
            float Corner(int dx, int dy, int dz)
            {
                uint h = ParticleRandom.Hash((uint)(x0 + dx) * 73856093u ^ (uint)(y0 + dy) * 19349663u ^ (uint)(z0 + dz) * 83492791u);
                // 12 edge directions of a cube.
                float gx, gy, gz;
                switch (h % 12u)
                {
                    case 0: gx = 1; gy = 1; gz = 0; break;
                    case 1: gx = -1; gy = 1; gz = 0; break;
                    case 2: gx = 1; gy = -1; gz = 0; break;
                    case 3: gx = -1; gy = -1; gz = 0; break;
                    case 4: gx = 1; gy = 0; gz = 1; break;
                    case 5: gx = -1; gy = 0; gz = 1; break;
                    case 6: gx = 1; gy = 0; gz = -1; break;
                    case 7: gx = -1; gy = 0; gz = -1; break;
                    case 8: gx = 0; gy = 1; gz = 1; break;
                    case 9: gx = 0; gy = -1; gz = 1; break;
                    case 10: gx = 0; gy = 1; gz = -1; break;
                    default: gx = 0; gy = -1; gz = -1; break;
                }
                return gx * (fx - dx) + gy * (fy - dy) + gz * (fz - dz);
            }
            float x00 = Mathf.LerpUnclamped(Corner(0, 0, 0), Corner(1, 0, 0), u);
            float x10 = Mathf.LerpUnclamped(Corner(0, 1, 0), Corner(1, 1, 0), u);
            float x01 = Mathf.LerpUnclamped(Corner(0, 0, 1), Corner(1, 0, 1), u);
            float x11 = Mathf.LerpUnclamped(Corner(0, 1, 1), Corner(1, 1, 1), u);
            float y0v = Mathf.LerpUnclamped(x00, x10, v);
            float y1v = Mathf.LerpUnclamped(x01, x11, v);
            return Mathf.Clamp(Mathf.LerpUnclamped(y0v, y1v, w), -1f, 1f);
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        // ------------------------------------------------------------------ sub-emitters

        bool HasSubEmitters(ParticleSystemSubEmitterType type)
        {
            foreach (var e in m_SubEmitters.emitters)
                if (e.type == type && e.system != null) return true;
            return false;
        }

        void TriggerSubEmitters(ParticleSystemSubEmitterType type, ref Particle parent)
        {
            foreach (var e in m_SubEmitters.emitters)
                if (e.type == type) TriggerSubEmitterAt(e, ref parent);
        }

        /// <summary>A death/collision/trigger/manual sub-emitter: the child emits its bursts at the particle.</summary>
        void TriggerSubEmitterAt(SubEmitter sub, ref Particle parent)
        {
            var child = sub.system;
            if (child == null || child == this || child.m_GameObject == null) return;
            if (sub.emitProbability < 1f && NextFloat() >= sub.emitProbability) return;
            int count = 0;
            foreach (var burst in child.m_Emission.bursts)
                count += child.BurstCount(burst.m_Count, 0f) * Math.Max(1, burst.cycleCount);
            if (count > 0) child.EmitFromParent(this, ref parent, sub.properties, count, 0f);
        }

        /// <summary>Birth sub-emitters emit with their own rate and bursts over the parent particle's life.</summary>
        void BirthSubEmitters(ref Particle parent, float ageBefore, float ageAfter, float dt)
        {
            int slot = 0;
            foreach (var e in m_SubEmitters.emitters)
            {
                if (e.type != ParticleSystemSubEmitterType.Birth) continue;
                var child = e.system;
                if (child == null || child == this || child.m_GameObject == null) { slot++; continue; }
                if (e.emitProbability < 1f && ParticleRandom.Value(parent.m_RandomSeed, R.SubEmit + (uint)slot) >= e.emitProbability) { slot++; continue; }
                float age01 = parent.Age01;
                int n = 0;
                if (child.m_Emission.enabled)
                {
                    float rate = child.m_Emission.rateOverTime.Evaluate(age01, child.NextFloat());
                    if (slot == 0)
                    {
                        parent.m_EmitAccumulator0 += rate * dt;
                        n = (int)parent.m_EmitAccumulator0;
                        parent.m_EmitAccumulator0 -= n;
                    }
                    else
                    {
                        parent.m_EmitAccumulator1 += rate * dt;
                        n = (int)parent.m_EmitAccumulator1;
                        parent.m_EmitAccumulator1 -= n;
                    }
                    foreach (var burst in child.m_Emission.bursts)
                    {
                        float at = burst.m_Time;
                        bool first = ageBefore <= 0f && at <= 0f;
                        if (first || (at > ageBefore && at <= ageAfter))
                            n += child.BurstCount(burst.m_Count, age01);
                    }
                }
                if (n > 0) child.EmitFromParent(this, ref parent, e.properties, n, 0f);
                slot++;
            }
        }

        /// <summary>Emits `count` particles at a parent system's particle, inheriting what the
        /// sub-emitter's properties ask for.</summary>
        void EmitFromParent(ParticleSystem parentSystem, ref Particle parent, ParticleSystemSubEmitterProperties properties, int count, float preAge)
        {
            var origin = parentSystem.SimulationToWorld().MultiplyPoint3x4(parent.m_Position);
            var emitter = EmitterMatrix();
            Color parentColor = parentSystem.CurrentColor(ref parent);
            var parentSize = parentSystem.CurrentSize3D(ref parent);
            var parentRotation = parent.m_Rotation;
            float parentLife = parent.m_StartLifetime > 0f ? parent.m_Lifetime / parent.m_StartLifetime : 0f;
            EmitParams none = default;
            for (int k = 0; k < count; k++)
            {
                int index = Allocate();
                if (index < 0) return;
                ref var p = ref m_Particles[index];
                if (!InitParticle(ref p, emitter, preAge, false, ref none, k, count, origin))
                {
                    RemoveAt(index);
                    continue;
                }
                p.m_ParentRandomSeed = parent.m_RandomSeed;
                if ((properties & ParticleSystemSubEmitterProperties.InheritColor) != 0)
                    p.m_StartColor = (Color)p.m_StartColor * parentColor;
                if ((properties & ParticleSystemSubEmitterProperties.InheritSize) != 0)
                    p.m_StartSize = Vector3.Scale(p.m_StartSize, parentSize);
                if ((properties & ParticleSystemSubEmitterProperties.InheritRotation) != 0)
                    p.m_Rotation += parentRotation;
                if ((properties & ParticleSystemSubEmitterProperties.InheritLifetime) != 0)
                {
                    p.m_StartLifetime *= parentLife;
                    p.m_Lifetime = p.m_StartLifetime;
                    if (p.m_Lifetime <= 0f) RemoveAt(index);
                }
            }
        }

        // ------------------------------------------------------------------ current values

        /// <summary>A particle's size now: start size, size over lifetime, size by speed.</summary>
        internal Vector3 CurrentSize3D(ref Particle p)
        {
            var size = p.m_StartSize;
            uint seed = p.m_RandomSeed;
            if (m_Size.enabled)
            {
                float age = p.Age01;
                float r = R.Value(seed, R.SizeLife);
                if (m_Size.separateAxes)
                    size = Vector3.Scale(size, new Vector3(m_Size.x.Evaluate(age, r), m_Size.y.Evaluate(age, r), m_Size.z.Evaluate(age, r)));
                else
                    size *= m_Size.x.Evaluate(age, r);
            }
            if (m_SizeBySpeed.enabled)
            {
                var range = m_SizeBySpeed.range;
                float s = Mathf.InverseLerp(range.x, range.y, (p.m_Velocity + p.m_AnimatedVelocity).magnitude);
                float r = R.Value(seed, R.SizeSpeed);
                if (m_SizeBySpeed.separateAxes)
                    size = Vector3.Scale(size, new Vector3(m_SizeBySpeed.x.Evaluate(s, r), m_SizeBySpeed.y.Evaluate(s, r), m_SizeBySpeed.z.Evaluate(s, r)));
                else
                    size *= m_SizeBySpeed.x.Evaluate(s, r);
            }
            return size;
        }

        /// <summary>A particle's color now: start color, color over lifetime, color by speed.</summary>
        internal Color32 CurrentColor(ref Particle p)
        {
            Color c = p.m_StartColor;
            uint seed = p.m_RandomSeed;
            if (m_Color.enabled)
                c *= m_Color.color.Evaluate(p.Age01, R.Value(seed, R.ColorLife));
            if (m_ColorBySpeed.enabled)
            {
                var range = m_ColorBySpeed.range;
                float s = Mathf.InverseLerp(range.x, range.y, (p.m_Velocity + p.m_AnimatedVelocity).magnitude);
                c *= m_ColorBySpeed.color.Evaluate(s, R.Value(seed, R.ColorSpeed));
            }
            return c;
        }

        internal int MeshIndexOf(ref Particle p)
        {
            if ((p.m_Flags & Particle.FlagMeshIndexSet) != 0) return p.m_MeshIndex;
            var renderer = m_GameObject != null ? m_GameObject.GetComponent<ParticleSystemRenderer>() : null;
            int count = renderer != null ? renderer.meshCount : 1;
            if (count <= 1) return 0;
            return Math.Min((int)(R.Value(p.m_RandomSeed, R.MeshIndex) * count), count - 1);
        }
    }
}
