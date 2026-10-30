using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public sealed partial class ParticleSystem
    {
        /// <summary>One particle. Rotations and angular velocities are kept in radians; the
        /// properties speak degrees, as Unity's do.</summary>
        public partial struct Particle
        {
            internal Vector3 m_Position;
            internal Vector3 m_Velocity;
            internal Vector3 m_AnimatedVelocity;
            internal Vector3 m_InitialVelocity;
            internal Vector3 m_NoiseImpulse;
            internal Vector3 m_NoiseSum;
            internal Vector3 m_AxisOfRotation;
            internal Vector3 m_Rotation;
            internal Vector3 m_AngularVelocity;
            internal Vector3 m_StartSize;
            internal Color32 m_StartColor;
            internal uint m_RandomSeed;
            internal uint m_ParentRandomSeed;
            internal float m_Lifetime;
            internal float m_StartLifetime;
            internal float m_AgePercent;
            internal float m_InvStartLifetime;
            internal int m_MeshIndex;
            internal float m_EmitAccumulator0;
            internal float m_EmitAccumulator1;
            internal uint m_Flags;
            /// <summary>The particle's trail (1-based id, 0: none).</summary>
            internal int m_TrailId;

            internal const uint FlagMeshIndexSet = 1, FlagFlipRotation = 2, FlagBirthBurstsDone = 4, FlagInsideTrigger = 8, FlagAgeInitialized = 16, FlagOrphanTrail = 32;

            [Obsolete("Please use Particle.remainingLifetime instead. (UnityUpgradable) -> UnityEngine.ParticleSystem/Particle.remainingLifetime", false)]
            public float lifetime { get => remainingLifetime; set => remainingLifetime = value; }

            [Obsolete("randomValue property is deprecated. Use randomSeed instead to control random behavior of particles.", false)]
            public float randomValue
            {
                get => m_RandomSeed / 4294967296f;
                set => m_RandomSeed = (uint)(Mathf.Clamp01(value) * 4294967295f);
            }

            [Obsolete("size property is deprecated. Use startSize or GetCurrentSize() instead.", false)]
            public float size { get => startSize; set => startSize = value; }

            [Obsolete("color property is deprecated. Use startColor or GetCurrentColor() instead.", false)]
            public Color32 color { get => startColor; set => startColor = value; }

            public Vector3 position { get => m_Position; set => m_Position = value; }
            public Vector3 velocity { get => m_Velocity; set => m_Velocity = value; }
            public Vector3 animatedVelocity => m_AnimatedVelocity;
            public Vector3 totalVelocity => m_Velocity + m_AnimatedVelocity;
            public float remainingLifetime { get => m_Lifetime; set { m_Lifetime = value; m_Flags &= ~FlagAgeInitialized; } }
            public float startLifetime { get => m_StartLifetime; set { m_StartLifetime = value; m_Flags &= ~FlagAgeInitialized; } }
            public Color32 startColor { get => m_StartColor; set => m_StartColor = value; }
            public uint randomSeed { get => m_RandomSeed; set => m_RandomSeed = value; }
            public Vector3 axisOfRotation { get => m_AxisOfRotation; set => m_AxisOfRotation = value; }

            public float startSize
            {
                get => m_StartSize.x;
                set => m_StartSize = new Vector3(value, value, value);
            }

            public Vector3 startSize3D { get => m_StartSize; set => m_StartSize = value; }

            public float rotation
            {
                get => m_Rotation.z * Mathf.Rad2Deg;
                set => m_Rotation = new Vector3(0f, 0f, value * Mathf.Deg2Rad);
            }

            public Vector3 rotation3D
            {
                get => m_Rotation * Mathf.Rad2Deg;
                set => m_Rotation = value * Mathf.Deg2Rad;
            }

            public float angularVelocity
            {
                get => m_AngularVelocity.z * Mathf.Rad2Deg;
                set => m_AngularVelocity = new Vector3(0f, 0f, value * Mathf.Deg2Rad);
            }

            public Vector3 angularVelocity3D
            {
                get => m_AngularVelocity * Mathf.Rad2Deg;
                set => m_AngularVelocity = value * Mathf.Deg2Rad;
            }

            public float GetCurrentSize(ParticleSystem system) => system.CurrentSize3D(ref this, includeNoise: false).x;

            public Vector3 GetCurrentSize3D(ParticleSystem system) => system.CurrentSize3D(ref this, includeNoise: false);

            public Color32 GetCurrentColor(ParticleSystem system) => system.CurrentColor(ref this);

            public void SetMeshIndex(int index)
            {
                m_MeshIndex = index;
                m_Flags |= FlagMeshIndexSet;
            }

            public int GetMeshIndex(ParticleSystem system) => system.MeshIndexOf(ref this);

            /// <summary>Normalized age: 0 at birth, 1 at death.</summary>
            internal float Age01 => (m_Flags & FlagAgeInitialized) != 0 ? Mathf.Clamp01(m_AgePercent * .01f)
                : m_StartLifetime > 0f ? Mathf.Clamp01(1f - m_Lifetime / m_StartLifetime) : 1f;

            internal void InitializeAge()
            {
                m_InvStartLifetime = 1f / m_StartLifetime;
                m_AgePercent = m_StartLifetime == 0f || float.IsPositiveInfinity(m_StartLifetime) ? 0f
                    : m_Lifetime <= 0f ? 100.00000762939453f
                    : ((m_StartLifetime - m_Lifetime) / m_StartLifetime) * 100f;
                m_Flags |= FlagAgeInitialized;
                ExportLifetime();
            }

            internal void ExportLifetime()
            {
                m_StartLifetime = 1f / m_InvStartLifetime;
                m_Lifetime = (1f + m_AgePercent * -.01f) * m_StartLifetime;
            }
        }

        /// <summary>Overrides for particles emitted by <see cref="Emit(EmitParams, int)"/>.</summary>
        public partial struct EmitParams
        {
            internal Particle m_Particle;
            internal bool m_PositionSet;
            internal bool m_VelocitySet;
            internal bool m_AxisOfRotationSet;
            internal bool m_RotationSet;
            internal bool m_AngularVelocitySet;
            internal bool m_StartSizeSet;
            internal bool m_StartColorSet;
            internal bool m_RandomSeedSet;
            internal bool m_StartLifetimeSet;
            internal bool m_MeshIndexSet;
            internal bool m_ApplyShapeToPosition;

            public Particle particle
            {
                get => m_Particle;
                set
                {
                    m_Particle = value;
                    m_PositionSet = m_VelocitySet = m_AxisOfRotationSet = m_RotationSet = m_AngularVelocitySet = true;
                    m_StartSizeSet = m_StartColorSet = m_RandomSeedSet = m_StartLifetimeSet = true;
                }
            }

            public Vector3 position
            {
                get => m_Particle.position;
                set { m_Particle.position = value; m_PositionSet = true; }
            }

            public bool applyShapeToPosition { get => m_ApplyShapeToPosition; set => m_ApplyShapeToPosition = value; }

            public Vector3 velocity
            {
                get => m_Particle.velocity;
                set { m_Particle.velocity = value; m_VelocitySet = true; }
            }

            public float startLifetime
            {
                get => m_Particle.startLifetime;
                set { m_Particle.startLifetime = value; m_StartLifetimeSet = true; }
            }

            public float startSize
            {
                get => m_Particle.startSize;
                set { m_Particle.startSize = value; m_StartSizeSet = true; }
            }

            public Vector3 startSize3D
            {
                get => m_Particle.startSize3D;
                set { m_Particle.startSize3D = value; m_StartSizeSet = true; }
            }

            public Vector3 axisOfRotation
            {
                get => m_Particle.axisOfRotation;
                set { m_Particle.axisOfRotation = value; m_AxisOfRotationSet = true; }
            }

            public float rotation
            {
                get => m_Particle.rotation;
                set { m_Particle.rotation = value; m_RotationSet = true; }
            }

            public Vector3 rotation3D
            {
                get => m_Particle.rotation3D;
                set { m_Particle.rotation3D = value; m_RotationSet = true; }
            }

            public float angularVelocity
            {
                get => m_Particle.angularVelocity;
                set { m_Particle.angularVelocity = value; m_AngularVelocitySet = true; }
            }

            public Vector3 angularVelocity3D
            {
                get => m_Particle.angularVelocity3D;
                set { m_Particle.angularVelocity3D = value; m_AngularVelocitySet = true; }
            }

            public Color32 startColor
            {
                get => m_Particle.startColor;
                set { m_Particle.startColor = value; m_StartColorSet = true; }
            }

            public uint randomSeed
            {
                get => m_Particle.randomSeed;
                set { m_Particle.randomSeed = value; m_RandomSeedSet = true; }
            }

            public int meshIndex
            {
                set { m_Particle.SetMeshIndex(value); m_MeshIndexSet = true; }
            }

            public void ResetPosition() => m_PositionSet = false;
            public void ResetVelocity() => m_VelocitySet = false;
            public void ResetAxisOfRotation() => m_AxisOfRotationSet = false;
            public void ResetRotation() => m_RotationSet = false;
            public void ResetAngularVelocity() => m_AngularVelocitySet = false;
            public void ResetStartSize() => m_StartSizeSet = false;
            public void ResetStartColor() => m_StartColorSet = false;
            public void ResetRandomSeed() => m_RandomSeedSet = false;
            public void ResetStartLifetime() => m_StartLifetimeSet = false;
            public void ResetMeshIndex() => m_MeshIndexSet = false;
        }

        /// <summary>A burst of the emission module: `count` particles at `time` of each loop,
        /// repeated `cycleCount` times (0: forever) every `repeatInterval` seconds.</summary>
        public partial struct Burst
        {
            internal float m_Time;
            internal MinMaxCurve m_Count;
            // Stored as cycles - 1 (and 1 - probability) so that default(Burst) fires once, always.
            internal int m_RepeatCount;
            internal float m_RepeatInterval;
            internal float m_InvProbability;

            public Burst(float _time, short _count)
            {
                m_Time = _time;
                m_Count = _count;
                m_RepeatCount = 0;
                m_RepeatInterval = 0f;
                m_InvProbability = 0f;
            }

            public Burst(float _time, short _minCount, short _maxCount)
            {
                m_Time = _time;
                m_Count = new MinMaxCurve(_minCount, _maxCount);
                m_RepeatCount = 0;
                m_RepeatInterval = 0f;
                m_InvProbability = 0f;
            }

            public Burst(float _time, short _minCount, short _maxCount, int _cycleCount, float _repeatInterval)
            {
                m_Time = _time;
                m_Count = new MinMaxCurve(_minCount, _maxCount);
                m_RepeatCount = _cycleCount - 1;
                m_RepeatInterval = _repeatInterval;
                m_InvProbability = 0f;
            }

            public Burst(float _time, MinMaxCurve _count)
            {
                m_Time = _time;
                m_Count = _count;
                m_RepeatCount = 0;
                m_RepeatInterval = 0f;
                m_InvProbability = 0f;
            }

            public Burst(float _time, MinMaxCurve _count, int _cycleCount, float _repeatInterval)
            {
                m_Time = _time;
                m_Count = _count;
                m_RepeatCount = _cycleCount - 1;
                m_RepeatInterval = _repeatInterval;
                m_InvProbability = 0f;
            }

            public float time { get => m_Time; set => m_Time = value; }
            public MinMaxCurve count { get => m_Count; set => m_Count = value; }

            public short minCount
            {
                get => (short)m_Count.constantMin;
                set
                {
                    if (m_Count.mode == ParticleSystemCurveMode.Constant) m_Count = new MinMaxCurve(m_Count.constantMax, m_Count.constantMax);
                    m_Count.constantMin = value;
                }
            }

            public short maxCount
            {
                get => (short)m_Count.constantMax;
                set => m_Count.constantMax = value;
            }

            public int cycleCount
            {
                get => m_RepeatCount + 1;
                set
                {
                    if (value < 0) throw new ArgumentOutOfRangeException(nameof(cycleCount), "cycleCount must be at least 0: " + value);
                    m_RepeatCount = value - 1;
                }
            }

            public float repeatInterval
            {
                get => m_RepeatInterval;
                set
                {
                    if (value <= 0f) throw new ArgumentOutOfRangeException(nameof(repeatInterval), "repeatInterval must be greater than 0.0f: " + value);
                    m_RepeatInterval = value;
                }
            }

            public float probability
            {
                get => 1f - m_InvProbability;
                set
                {
                    if (value < 0f || value > 1f) throw new ArgumentOutOfRangeException(nameof(probability), "probability must be between 0.0f and 1.0f: " + value);
                    m_InvProbability = 1f - value;
                }
            }
        }

        /// <summary>A snapshot of a system's playback, for <see cref="GetPlaybackState"/>.</summary>
        public partial struct PlaybackState
        {
            internal float m_AccumulatedDt;
            internal float m_StartDelay;
            internal float m_PlaybackTime;
            internal int m_RingBufferIndex;
            internal float m_TotalTime;
            internal int m_Loop;
            internal bool m_Playing;
            internal bool m_Paused;
            internal bool m_StopEmitting;
            internal int[] m_BurstCycles;
            internal ulong m_RandomState;
        }

        /// <summary>Trail data of a system (trails are not simulated: always empty).</summary>
        public partial struct Trails
        {
            internal List<Vector4> positions;
            internal List<int> frontPositions;
            internal List<int> backPositions;
            internal List<int> positionCounts;
            internal List<float> textureOffsets;
            internal int maxTrailCount;
            internal int maxPositionsPerTrailCount;

            public int capacity
            {
                get => positions?.Capacity ?? 0;
                set
                {
                    positions ??= new List<Vector4>();
                    positions.Capacity = Math.Max(value, positions.Count);
                }
            }
        }

        /// <summary>Colliders overlapping particles reported by the trigger module (none: triggers are not simulated).</summary>
        public partial struct ColliderData
        {
            internal Component[] colliders;
            internal int[] colliderIndices;
            internal int[] particleStartIndices;

            public int GetColliderCount(int particleIndex)
            {
                if (particleStartIndices == null || particleIndex < 0 || particleIndex >= particleStartIndices.Length) return 0;
                int end = particleIndex + 1 < particleStartIndices.Length ? particleStartIndices[particleIndex + 1] : colliderIndices.Length;
                return end - particleStartIndices[particleIndex];
            }

            public Component GetCollider(int particleIndex, int colliderIndex)
            {
                if (colliderIndex < 0 || colliderIndex >= GetColliderCount(particleIndex))
                    throw new IndexOutOfRangeException("colliderIndex exceeded the total number of colliders for the requested particle");
                return colliders[colliderIndices[particleStartIndices[particleIndex] + colliderIndex]];
            }
        }
    }

    /// <summary>A particle's collision: where its center was, the surface normal, its velocity.</summary>
    public partial struct ParticleCollisionEvent
    {
        internal Vector3 m_Intersection;
        internal Vector3 m_Normal;
        internal Vector3 m_Velocity;
        internal int m_ColliderInstanceID;

        [Obsolete("collider property is deprecated. Use colliderComponent instead, which supports Collider and Collider2D components (UnityUpgradable) -> colliderComponent", true)]
        [Unbound.EditorFaithful("obsolete as an error: no compiled game code can call it")]
        public Component collider => null;

        public Vector3 intersection => m_Intersection;
        public Vector3 normal => m_Normal;
        public Vector3 velocity => m_Velocity;
        public Component colliderComponent => m_ColliderInstanceID != 0 ? Unbound.Runtime.ObjectRegistry.FindById(m_ColliderInstanceID) as Component : null;
    }

    /// <summary>Collision and trigger queries.</summary>
    public static partial class ParticlePhysicsExtensions
    {
        [Obsolete("GetCollisionEvents function using ParticleCollisionEvent[] is deprecated. Use List<ParticleCollisionEvent> instead.", false)]
        public static int GetCollisionEvents(this ParticleSystem ps, GameObject go, ParticleCollisionEvent[] collisionEvents)
        {
            int n = 0;
            foreach (var (other, e) in ps.m_CollisionEvents)
            {
                if (other != go) continue;
                if (collisionEvents == null || n >= collisionEvents.Length) break;
                collisionEvents[n++] = e;
            }
            return n;
        }

        public static int GetSafeCollisionEventSize(this ParticleSystem ps) => ps.m_CollisionEvents.Count;

        public static int GetCollisionEvents(this ParticleSystem ps, GameObject go, List<ParticleCollisionEvent> collisionEvents)
        {
            collisionEvents?.Clear();
            int n = 0;
            foreach (var (other, e) in ps.m_CollisionEvents)
            {
                if (other != go) continue;
                collisionEvents?.Add(e);
                n++;
            }
            return n;
        }

        public static int GetSafeTriggerParticlesSize(this ParticleSystem ps, ParticleSystemTriggerEventType type) => ps.m_TriggerParticles[(int)type].Count;

        public static int GetTriggerParticles(this ParticleSystem ps, ParticleSystemTriggerEventType type, List<ParticleSystem.Particle> particles)
        {
            particles?.Clear();
            var list = ps.m_TriggerParticles[(int)type];
            particles?.AddRange(list);
            return list.Count;
        }

        public static int GetTriggerParticles(this ParticleSystem ps, ParticleSystemTriggerEventType type, List<ParticleSystem.Particle> particles, out ParticleSystem.ColliderData colliderData)
        {
            colliderData = default;
            return GetTriggerParticles(ps, type, particles);
        }

        public static void SetTriggerParticles(this ParticleSystem ps, ParticleSystemTriggerEventType type, List<ParticleSystem.Particle> particles, int offset, int count) =>
            Unbound.Runtime.Stubs.Ignored("ParticlePhysicsExtensions.SetTriggerParticles", "is not honoured yet: changed trigger particles are not written back");

        public static void SetTriggerParticles(this ParticleSystem ps, ParticleSystemTriggerEventType type, List<ParticleSystem.Particle> particles) =>
            Unbound.Runtime.Stubs.Ignored("ParticlePhysicsExtensions.SetTriggerParticles", "is not honoured yet: changed trigger particles are not written back");
    }
}
