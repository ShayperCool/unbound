using System;
using Unbound.Runtime;

namespace UnityEngine
{
    // Module structs are handles to their system, as in Unity: `var main = ps.main; main.loop = false;`
    // writes through to the system. Curves and gradients are copied in and out, so changing a value
    // read from a module affects the system only once it is assigned back.
    public sealed partial class ParticleSystem
    {
        // Missing owners return fallback property values in the 6000.0.71f1
        // defaultstate reference, including handles retained after destruction.
        static bool ModuleHasNoOwner(ParticleSystem owner) => owner == null && Compat.ParticleModuleHandlesMeasured;

        // These managed-facing properties require a live owner rather than a fallback value.
        static ParticleSystem ModuleManagedOwner(ParticleSystem owner) => ModuleHasNoOwner(owner)
            ? throw new NullReferenceException() : owner;

        public partial struct MainModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal MainModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            MainData D => m_ParticleSystem.m_Main;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            MainData W => m_ParticleSystem.ModifiedByScript().m_Main;

            [Obsolete("Please use flipRotation instead. (UnityUpgradable) -> UnityEngine.ParticleSystem/MainModule.flipRotation", false)]
            public float randomizeRotationDirection { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.flipRotation; set => W.flipRotation = value; }

            public Vector3 emitterVelocity { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.customEmitterVelocity; set => W.customEmitterVelocity = value; }

            public float duration
            {
                get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.duration;
                set
                {
                    if (m_ParticleSystem.isPlaying || m_ParticleSystem.m_Count > 0)
                    {
                        Debug.LogError("Setting the duration while system is still playing is not supported. Please wait until the system has stopped and all particles have expired or call Stop with ParticleSystemStopBehavior.StopEmittingAndClear to stop and clear the system.", m_ParticleSystem);
                        return;
                    }
                    W.duration = Mathf.Max(0.05f, value);
                }
            }

            public bool loop { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.loop; set => W.loop = value; }
            public bool prewarm { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.prewarm; set => W.prewarm = value; }
            public MinMaxCurve startDelay { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startDelay.Get(); set => W.startDelay.Set(value); }
            public float startDelayMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startDelay.Multiplier; set => W.startDelay.Multiplier = value; }
            public MinMaxCurve startLifetime { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startLifetime.Get(); set => W.startLifetime.Set(value); }
            public float startLifetimeMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startLifetime.Multiplier; set => W.startLifetime.Multiplier = value; }
            public MinMaxCurve startSpeed { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startSpeed.Get(); set => W.startSpeed.Set(value); }
            public float startSpeedMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startSpeed.Multiplier; set => W.startSpeed.Multiplier = value; }
            public bool startSize3D { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startSize3D; set => W.startSize3D = value; }
            public MinMaxCurve startSize { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startSizeX.Get(); set => W.startSizeX.Set(value); }
            public float startSizeMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startSizeX.Multiplier; set => W.startSizeX.Multiplier = value; }
            public MinMaxCurve startSizeX { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startSizeX.Get(); set => W.startSizeX.Set(value); }
            public float startSizeXMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startSizeX.Multiplier; set => W.startSizeX.Multiplier = value; }
            public MinMaxCurve startSizeY { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startSizeY.Get(); set => W.startSizeY.Set(value); }
            public float startSizeYMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startSizeY.Multiplier; set => W.startSizeY.Multiplier = value; }
            public MinMaxCurve startSizeZ { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startSizeZ.Get(); set => W.startSizeZ.Set(value); }
            public float startSizeZMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startSizeZ.Multiplier; set => W.startSizeZ.Multiplier = value; }
            public bool startRotation3D { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startRotation3D; set => W.startRotation3D = value; }
            public MinMaxCurve startRotation { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startRotationZ.Get(); set => W.startRotationZ.Set(value); }
            public float startRotationMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startRotationZ.Multiplier; set => W.startRotationZ.Multiplier = value; }
            public MinMaxCurve startRotationX { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startRotationX.Get(); set => W.startRotationX.Set(value); }
            public float startRotationXMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startRotationX.Multiplier; set => W.startRotationX.Multiplier = value; }
            public MinMaxCurve startRotationY { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startRotationY.Get(); set => W.startRotationY.Set(value); }
            public float startRotationYMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startRotationY.Multiplier; set => W.startRotationY.Multiplier = value; }
            public MinMaxCurve startRotationZ { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startRotationZ.Get(); set => W.startRotationZ.Set(value); }
            public float startRotationZMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startRotationZ.Multiplier; set => W.startRotationZ.Multiplier = value; }
            public float flipRotation { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.flipRotation; set => W.flipRotation = value; }
            /// <summary>Like every module colour, the getter hands out the module's own gradients:
            /// changing a key of the read value changes the module; the setter keeps copies
            /// (6000.0.71f1, tests/projects/particlecurves gradient/module).</summary>
            public MinMaxGradient startColor { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startColor; set => W.startColor = value.ForModule(); }
            public ParticleSystemGravitySource gravitySource { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.gravitySource; set => W.gravitySource = value; }
            public MinMaxCurve gravityModifier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.gravityModifier.Get(); set => W.gravityModifier.Set(value); }
            public float gravityModifierMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.gravityModifier.Multiplier; set => W.gravityModifier.Multiplier = value; }
            public ParticleSystemSimulationSpace simulationSpace { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.simulationSpace; set => m_ParticleSystem.ModifiedByScript().SetSimulationSpace(value); }
            public Transform customSimulationSpace { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.customSimulationSpace; set => W.customSimulationSpace = value; }
            public float simulationSpeed { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.simulationSpeed; set => W.simulationSpeed = value; }
            public bool useUnscaledTime { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.useUnscaledTime; set => W.useUnscaledTime = value; }
            public ParticleSystemScalingMode scalingMode { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.scalingMode; set => W.scalingMode = value; }
            public bool playOnAwake { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.playOnAwake; set => W.playOnAwake = value; }
            public int maxParticles { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.maxParticles; set => W.maxParticles = Math.Max(0, value); }
            public ParticleSystemEmitterVelocityMode emitterVelocityMode { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.emitterVelocityMode; set => W.emitterVelocityMode = value; }
            public ParticleSystemStopAction stopAction { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.stopAction; set => W.stopAction = value; }
            public ParticleSystemRingBufferMode ringBufferMode { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.ringBufferMode; set => W.ringBufferMode = value; }
            public Vector2 ringBufferLoopRange { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.ringBufferLoopRange; set => W.ringBufferLoopRange = value; }
            public ParticleSystemCullingMode cullingMode { get => ModuleHasNoOwner(m_ParticleSystem) ? ParticleSystemCullingMode.PauseAndCatchup : D.cullingMode; set => W.cullingMode = value; }
        }

        public partial struct EmissionModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal EmissionModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            EmissionData D => m_ParticleSystem.m_Emission;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            EmissionData W => m_ParticleSystem.ModifiedByScript().m_Emission;

            [Obsolete("ParticleSystemEmissionType no longer does anything. Time and Distance based emission are now both always active.", false)]
            [Unbound.EditorFaithful("obsolete: Unity says it no longer does anything")]
            public ParticleSystemEmissionType type { get => ModuleHasNoOwner(m_ParticleSystem) ? default : ParticleSystemEmissionType.Time; set { } }

            [Obsolete("rate property is deprecated. Use rateOverTime or rateOverDistance instead.", false)]
            public MinMaxCurve rate { get => ModuleHasNoOwner(m_ParticleSystem) ? default : rateOverTime; set => rateOverTime = value; }

            [Obsolete("rateMultiplier property is deprecated. Use rateOverTimeMultiplier or rateOverDistanceMultiplier instead.", false)]
            public float rateMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : rateOverTimeMultiplier; set => rateOverTimeMultiplier = value; }

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public MinMaxCurve rateOverTime { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.rateOverTime.Get(); set => W.rateOverTime.Set(value); }
            public float rateOverTimeMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.rateOverTime.Multiplier; set => W.rateOverTime.Multiplier = value; }
            public MinMaxCurve rateOverDistance { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.rateOverDistance.Get(); set => W.rateOverDistance.Set(value); }
            public float rateOverDistanceMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.rateOverDistance.Multiplier; set => W.rateOverDistance.Multiplier = value; }

            public int burstCount
            {
                get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.bursts.Count;
                set
                {
                    value = Math.Max(0, value);
                    while (W.bursts.Count > value) W.bursts.RemoveAt(W.bursts.Count - 1);
                    while (W.bursts.Count < value) W.bursts.Add(new Burst(0f, (short)30));
                    m_ParticleSystem.BurstsChanged();
                }
            }

            public void SetBursts(Burst[] bursts) => SetBursts(bursts, bursts?.Length ?? 0);

            public void SetBursts(Burst[] bursts, int size)
            {
                D.bursts.Clear();
                if (bursts != null)
                    for (int i = 0; i < size && i < bursts.Length; i++) D.bursts.Add(bursts[i].Stored());
                m_ParticleSystem.BurstsChanged();
            }

            public int GetBursts(Burst[] bursts)
            {
                if (bursts == null) throw new ArgumentNullException(nameof(bursts));
                int n = Math.Min(bursts.Length, D.bursts.Count);
                for (int i = 0; i < n; i++) bursts[i] = D.bursts[i].Stored();
                return n;
            }

            public void SetBurst(int index, Burst burst)
            {
                if (index < 0 || index >= D.bursts.Count) throw new IndexOutOfRangeException("Burst index out of range.");
                D.bursts[index] = burst.Stored();
                m_ParticleSystem.BurstsChanged();
            }

            public Burst GetBurst(int index)
            {
                if (index < 0 || index >= D.bursts.Count) throw new IndexOutOfRangeException("Burst index out of range.");
                return D.bursts[index].Stored();
            }

        }

        public partial struct ShapeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal ShapeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            ShapeData D => m_ParticleSystem.m_Shape;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            ShapeData W => m_ParticleSystem.ModifiedByScript().m_Shape;

            [Obsolete("Please use scale instead. (UnityUpgradable) -> UnityEngine.ParticleSystem/ShapeModule.scale", false)]
            public Vector3 box { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.scale; set => W.scale = value; }

            [Obsolete("meshScale property is deprecated.Please use scale instead.", false)]
            public float meshScale { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.scale.x; set => W.scale = new Vector3(value, value, value); }

            [Obsolete("randomDirection property is deprecated. Use randomDirectionAmount instead.", false)]
            public bool randomDirection { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.randomDirectionAmount >= 0.5f; set => W.randomDirectionAmount = value ? 1f : 0f; }

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public ParticleSystemShapeType shapeType { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.shapeType; set => W.shapeType = value; }
            public float randomDirectionAmount { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.randomDirectionAmount; set => W.randomDirectionAmount = value; }
            public float sphericalDirectionAmount { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.sphericalDirectionAmount; set => W.sphericalDirectionAmount = value; }
            public float randomPositionAmount { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.randomPositionAmount; set => W.randomPositionAmount = value; }
            public bool alignToDirection { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.alignToDirection; set => W.alignToDirection = value; }
            public float radius { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.radius; set => W.radius = value; }
            public ParticleSystemShapeMultiModeValue radiusMode { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.radiusMode; set => W.radiusMode = value; }
            public float radiusSpread { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.radiusSpread; set => W.radiusSpread = value; }
            public MinMaxCurve radiusSpeed { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.radiusSpeed.Get(); set => W.radiusSpeed.Set(value); }
            public float radiusSpeedMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.radiusSpeed.Multiplier; set => W.radiusSpeed.Multiplier = value; }
            public float radiusThickness { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.radiusThickness; set => W.radiusThickness = value; }
            public float angle { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.angle; set => W.angle = value; }
            public float length { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.length; set => W.length = value; }
            public Vector3 boxThickness { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.boxThickness; set => W.boxThickness = value; }
            public ParticleSystemMeshShapeType meshShapeType { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.meshShapeType; set => W.meshShapeType = value; }
            public Mesh mesh { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.mesh; set => W.mesh = value; }
            public MeshRenderer meshRenderer { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.meshRenderer; set => W.meshRenderer = value; }
            public SkinnedMeshRenderer skinnedMeshRenderer { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.skinnedMeshRenderer as SkinnedMeshRenderer; set => W.skinnedMeshRenderer = value; }
            public Sprite sprite { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.sprite; set => W.sprite = value; }
            public SpriteRenderer spriteRenderer { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.spriteRenderer; set => W.spriteRenderer = value; }
            public bool useMeshMaterialIndex { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.useMeshMaterialIndex; set => W.useMeshMaterialIndex = value; }
            public int meshMaterialIndex { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.meshMaterialIndex; set => W.meshMaterialIndex = value; }
            public bool useMeshColors { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.useMeshColors; set => W.useMeshColors = value; }
            public float normalOffset { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.normalOffset; set => W.normalOffset = value; }
            public ParticleSystemShapeMultiModeValue meshSpawnMode { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.meshSpawnMode; set => W.meshSpawnMode = value; }
            public float meshSpawnSpread { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.meshSpawnSpread; set => W.meshSpawnSpread = value; }
            public MinMaxCurve meshSpawnSpeed { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.meshSpawnSpeed.Get(); set => W.meshSpawnSpeed.Set(value); }
            public float meshSpawnSpeedMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.meshSpawnSpeed.Multiplier; set => W.meshSpawnSpeed.Multiplier = value; }
            public float arc { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.arc; set => W.arc = value; }
            public ParticleSystemShapeMultiModeValue arcMode { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.arcMode; set => W.arcMode = value; }
            public float arcSpread { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.arcSpread; set => W.arcSpread = value; }
            public MinMaxCurve arcSpeed { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.arcSpeed.Get(); set => W.arcSpeed.Set(value); }
            public float arcSpeedMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.arcSpeed.Multiplier; set => W.arcSpeed.Multiplier = value; }
            public float donutRadius { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.donutRadius; set => W.donutRadius = value; }
            public Vector3 position { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.position; set => W.position = value; }
            public Vector3 rotation { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.rotation; set => W.rotation = value; }
            public Vector3 scale { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.scale; set => W.scale = value; }
            public Texture2D texture { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.texture; set => W.texture = value; }
            public ParticleSystemShapeTextureChannel textureClipChannel { get => ModuleHasNoOwner(m_ParticleSystem) ? ParticleSystemShapeTextureChannel.Alpha : D.textureClipChannel; set => W.textureClipChannel = value; }
            public float textureClipThreshold { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.textureClipThreshold; set => W.textureClipThreshold = Mathf.Clamp01(value); }
            public bool textureColorAffectsParticles { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.textureColorAffectsParticles; set => W.textureColorAffectsParticles = value; }
            public bool textureAlphaAffectsParticles { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.textureAlphaAffectsParticles; set => W.textureAlphaAffectsParticles = value; }
            public bool textureBilinearFiltering { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.textureBilinearFiltering; set => W.textureBilinearFiltering = value; }
            public int textureUVChannel { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.textureUVChannel; set => W.textureUVChannel = Math.Max(0, value); }
        }

        public partial struct VelocityOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal VelocityOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            VelocityData D => m_ParticleSystem.m_Velocity;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            VelocityData W => m_ParticleSystem.ModifiedByScript().m_Velocity;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public MinMaxCurve x { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.x.Get(); set => W.x.Set(value); }
            public MinMaxCurve y { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.y.Get(); set => W.y.Set(value); }
            public MinMaxCurve z { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.z.Get(); set => W.z.Set(value); }
            public float xMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.x.Multiplier; set => W.x.Multiplier = value; }
            public float yMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.y.Multiplier; set => W.y.Multiplier = value; }
            public float zMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.z.Multiplier; set => W.z.Multiplier = value; }
            public MinMaxCurve orbitalX { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.orbitalX.Get(); set => W.orbitalX.Set(value); }
            public MinMaxCurve orbitalY { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.orbitalY.Get(); set => W.orbitalY.Set(value); }
            public MinMaxCurve orbitalZ { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.orbitalZ.Get(); set => W.orbitalZ.Set(value); }
            public float orbitalXMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.orbitalX.Multiplier; set => W.orbitalX.Multiplier = value; }
            public float orbitalYMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.orbitalY.Multiplier; set => W.orbitalY.Multiplier = value; }
            public float orbitalZMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.orbitalZ.Multiplier; set => W.orbitalZ.Multiplier = value; }
            public MinMaxCurve orbitalOffsetX { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.orbitalOffsetX.Get(); set => W.orbitalOffsetX.Set(value); }
            public MinMaxCurve orbitalOffsetY { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.orbitalOffsetY.Get(); set => W.orbitalOffsetY.Set(value); }
            public MinMaxCurve orbitalOffsetZ { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.orbitalOffsetZ.Get(); set => W.orbitalOffsetZ.Set(value); }
            public float orbitalOffsetXMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.orbitalOffsetX.Multiplier; set => W.orbitalOffsetX.Multiplier = value; }
            public float orbitalOffsetYMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.orbitalOffsetY.Multiplier; set => W.orbitalOffsetY.Multiplier = value; }
            public float orbitalOffsetZMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.orbitalOffsetZ.Multiplier; set => W.orbitalOffsetZ.Multiplier = value; }
            public MinMaxCurve radial { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.radial.Get(); set => W.radial.Set(value); }
            public float radialMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.radial.Multiplier; set => W.radial.Multiplier = value; }
            public MinMaxCurve speedModifier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.speedModifier.Get(); set => W.speedModifier.Set(value); }
            public float speedModifierMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.speedModifier.Multiplier; set => W.speedModifier.Multiplier = value; }
            public ParticleSystemSimulationSpace space { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.space; set => W.space = value; }
        }

        public partial struct LimitVelocityOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal LimitVelocityOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            LimitVelocityData D => m_ParticleSystem.m_LimitVelocity;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            LimitVelocityData W => m_ParticleSystem.ModifiedByScript().m_LimitVelocity;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public MinMaxCurve limitX { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.limitX.Get(); set => W.limitX.Set(value); }
            public float limitXMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.limitX.Multiplier; set => W.limitX.Multiplier = value; }
            public MinMaxCurve limitY { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.limitY.Get(); set => W.limitY.Set(value); }
            public float limitYMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.limitY.Multiplier; set => W.limitY.Multiplier = value; }
            public MinMaxCurve limitZ { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.limitZ.Get(); set => W.limitZ.Set(value); }
            public float limitZMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.limitZ.Multiplier; set => W.limitZ.Multiplier = value; }
            public MinMaxCurve limit { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.limit.Get(); set => W.limit.Set(value); }
            public float limitMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.limit.Multiplier; set => W.limit.Multiplier = value; }
            public float dampen { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.dampen; set => W.dampen = value; }
            public bool separateAxes { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.separateAxes; set => W.separateAxes = value; }
            public ParticleSystemSimulationSpace space { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.space; set => W.space = value; }
            public MinMaxCurve drag { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.drag.Get(); set => W.drag.Set(value); }
            public float dragMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.drag.Multiplier; set => W.drag.Multiplier = value; }
            public bool multiplyDragByParticleSize { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.multiplyDragByParticleSize; set => W.multiplyDragByParticleSize = value; }
            public bool multiplyDragByParticleVelocity { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.multiplyDragByParticleVelocity; set => W.multiplyDragByParticleVelocity = value; }
        }

        public partial struct InheritVelocityModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal InheritVelocityModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            InheritVelocityData D => m_ParticleSystem.m_InheritVelocity;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            InheritVelocityData W => m_ParticleSystem.ModifiedByScript().m_InheritVelocity;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public ParticleSystemInheritVelocityMode mode { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.mode; set => W.mode = value; }
            public MinMaxCurve curve { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.curve.Get(); set => W.curve.Set(value); }
            public float curveMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.curve.Multiplier; set => W.curve.Multiplier = value; }
        }

        public partial struct LifetimeByEmitterSpeedModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal LifetimeByEmitterSpeedModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            LifetimeByEmitterSpeedData D => m_ParticleSystem.m_LifetimeByEmitterSpeed;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            LifetimeByEmitterSpeedData W => m_ParticleSystem.ModifiedByScript().m_LifetimeByEmitterSpeed;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public MinMaxCurve curve { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.curve.Get(); set => W.curve.Set(value); }
            public float curveMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.curve.Multiplier; set => W.curve.Multiplier = value; }
            public Vector2 range { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.range; set => W.range = value; }
        }

        public partial struct ForceOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal ForceOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            ForceData D => m_ParticleSystem.m_Force;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            ForceData W => m_ParticleSystem.ModifiedByScript().m_Force;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public MinMaxCurve x { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.x.Get(); set => W.x.Set(value); }
            public MinMaxCurve y { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.y.Get(); set => W.y.Set(value); }
            public MinMaxCurve z { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.z.Get(); set => W.z.Set(value); }
            public float xMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.x.Multiplier; set => W.x.Multiplier = value; }
            public float yMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.y.Multiplier; set => W.y.Multiplier = value; }
            public float zMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.z.Multiplier; set => W.z.Multiplier = value; }
            public ParticleSystemSimulationSpace space { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.space; set => W.space = value; }
            public bool randomized { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.randomized; set => W.randomized = value; }
        }

        public partial struct ColorOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal ColorOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            ColorData D => m_ParticleSystem.m_Color;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            ColorData W => m_ParticleSystem.ModifiedByScript().m_Color;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public MinMaxGradient color { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.color; set => W.color = value.ForModule(); }
        }

        public partial struct ColorBySpeedModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal ColorBySpeedModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            ColorBySpeedData D => m_ParticleSystem.m_ColorBySpeed;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            ColorBySpeedData W => m_ParticleSystem.ModifiedByScript().m_ColorBySpeed;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public MinMaxGradient color { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.color; set => W.color = value.ForModule(); }
            public Vector2 range { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.range; set => W.range = value; }
        }

        public partial struct SizeOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal SizeOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            SizeData D => m_ParticleSystem.m_Size;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            SizeData W => m_ParticleSystem.ModifiedByScript().m_Size;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public MinMaxCurve size { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.x.Get(); set => W.x.Set(value); }
            public float sizeMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.x.Multiplier; set => W.x.Multiplier = value; }
            public MinMaxCurve x { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.x.Get(); set => W.x.Set(value); }
            public float xMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.x.Multiplier; set => W.x.Multiplier = value; }
            public MinMaxCurve y { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.y.Get(); set => W.y.Set(value); }
            public float yMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.y.Multiplier; set => W.y.Multiplier = value; }
            public MinMaxCurve z { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.z.Get(); set => W.z.Set(value); }
            public float zMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.z.Multiplier; set => W.z.Multiplier = value; }
            public bool separateAxes { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.separateAxes; set => W.separateAxes = value; }
        }

        public partial struct SizeBySpeedModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal SizeBySpeedModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            SizeBySpeedData D => m_ParticleSystem.m_SizeBySpeed;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            SizeBySpeedData W => m_ParticleSystem.ModifiedByScript().m_SizeBySpeed;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public MinMaxCurve size { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.x.Get(); set => W.x.Set(value); }
            public float sizeMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.x.Multiplier; set => W.x.Multiplier = value; }
            public MinMaxCurve x { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.x.Get(); set => W.x.Set(value); }
            public float xMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.x.Multiplier; set => W.x.Multiplier = value; }
            public MinMaxCurve y { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.y.Get(); set => W.y.Set(value); }
            public float yMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.y.Multiplier; set => W.y.Multiplier = value; }
            public MinMaxCurve z { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.z.Get(); set => W.z.Set(value); }
            public float zMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.z.Multiplier; set => W.z.Multiplier = value; }
            public bool separateAxes { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.separateAxes; set => W.separateAxes = value; }
            public Vector2 range { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.range; set => W.range = value; }
        }

        public partial struct RotationOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal RotationOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            RotationData D => m_ParticleSystem.m_Rotation;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            RotationData W => m_ParticleSystem.ModifiedByScript().m_Rotation;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public MinMaxCurve x { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.x.Get(); set => W.x.Set(value); }
            public float xMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.x.Multiplier; set => W.x.Multiplier = value; }
            public MinMaxCurve y { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.y.Get(); set => W.y.Set(value); }
            public float yMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.y.Multiplier; set => W.y.Multiplier = value; }
            public MinMaxCurve z { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.z.Get(); set => W.z.Set(value); }
            public float zMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.z.Multiplier; set => W.z.Multiplier = value; }
            public bool separateAxes { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.separateAxes; set => W.separateAxes = value; }
        }

        public partial struct RotationBySpeedModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal RotationBySpeedModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            RotationBySpeedData D => m_ParticleSystem.m_RotationBySpeed;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            RotationBySpeedData W => m_ParticleSystem.ModifiedByScript().m_RotationBySpeed;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public MinMaxCurve x { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.x.Get(); set => W.x.Set(value); }
            public float xMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.x.Multiplier; set => W.x.Multiplier = value; }
            public MinMaxCurve y { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.y.Get(); set => W.y.Set(value); }
            public float yMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.y.Multiplier; set => W.y.Multiplier = value; }
            public MinMaxCurve z { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.z.Get(); set => W.z.Set(value); }
            public float zMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.z.Multiplier; set => W.z.Multiplier = value; }
            public bool separateAxes { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.separateAxes; set => W.separateAxes = value; }
            public Vector2 range { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.range; set => W.range = value; }
        }

        public partial struct ExternalForcesModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal ExternalForcesModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            ExternalForcesData D => m_ParticleSystem.m_ExternalForces;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            ExternalForcesData W => m_ParticleSystem.ModifiedByScript().m_ExternalForces;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public float multiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.multiplierCurve.Multiplier; set => W.multiplierCurve.Multiplier = value; }
            public MinMaxCurve multiplierCurve { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.multiplierCurve.Get(); set => W.multiplierCurve.Set(value); }
            public ParticleSystemGameObjectFilter influenceFilter { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.influenceFilter; set => W.influenceFilter = value; }
            public LayerMask influenceMask { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.influenceMask; set => W.influenceMask = value; }
            public int influenceCount => ModuleManagedOwner(m_ParticleSystem).m_ExternalForces.influences.Count;

            public bool IsAffectedBy(ParticleSystemForceField field)
            {
                if (field == null) return false;
                bool inList = D.influences.Contains(field);
                bool inMask = (D.influenceMask.value & (1 << field.gameObject.layer)) != 0;
                switch (D.influenceFilter)
                {
                    case ParticleSystemGameObjectFilter.List: return inList;
                    case ParticleSystemGameObjectFilter.LayerMaskAndList: return inList || inMask;
                    default: return inMask;
                }
            }

            public void AddInfluence(ParticleSystemForceField field) => D.influences.Add(field);
            public void RemoveInfluence(int index) => D.influences.RemoveAt(index);
            public void RemoveInfluence(ParticleSystemForceField field) => D.influences.Remove(field);
            public void RemoveAllInfluences() => D.influences.Clear();
            public void SetInfluence(int index, ParticleSystemForceField field) => D.influences[index] = field;
            public ParticleSystemForceField GetInfluence(int index) => D.influences[index];
        }

        public partial struct NoiseModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal NoiseModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            NoiseData D => m_ParticleSystem.m_Noise;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            NoiseData W => m_ParticleSystem.ModifiedByScript().m_Noise;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public bool separateAxes { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.separateAxes; set => W.separateAxes = value; }
            public MinMaxCurve strength { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.strengthX.Get(); set => W.strengthX.Set(value); }
            public float strengthMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.strengthX.Multiplier; set => W.strengthX.Multiplier = value; }
            public MinMaxCurve strengthX { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.strengthX.Get(); set => W.strengthX.Set(value); }
            public float strengthXMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.strengthX.Multiplier; set => W.strengthX.Multiplier = value; }
            public MinMaxCurve strengthY { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.strengthY.Get(); set => W.strengthY.Set(value); }
            public float strengthYMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.strengthY.Multiplier; set => W.strengthY.Multiplier = value; }
            public MinMaxCurve strengthZ { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.strengthZ.Get(); set => W.strengthZ.Set(value); }
            public float strengthZMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.strengthZ.Multiplier; set => W.strengthZ.Multiplier = value; }
            public float frequency { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.frequency; set => W.frequency = value; }
            public bool damping { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.damping; set => W.damping = value; }
            public int octaveCount { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.octaveCount; set => W.octaveCount = Mathf.Clamp(value, 1, 4); }
            public float octaveMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.octaveMultiplier; set => W.octaveMultiplier = value; }
            public float octaveScale { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.octaveScale; set => W.octaveScale = value; }
            public ParticleSystemNoiseQuality quality { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.quality; set => W.quality = value; }
            public MinMaxCurve scrollSpeed { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.scrollSpeed.Get(); set => W.scrollSpeed.Set(value); }
            public float scrollSpeedMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.scrollSpeed.Multiplier; set => W.scrollSpeed.Multiplier = value; }
            public bool remapEnabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.remapEnabled; set => W.remapEnabled = value; }
            public MinMaxCurve remap { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.remapX.Get(); set => W.remapX.Set(value); }
            public float remapMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.remapX.Multiplier; set => W.remapX.Multiplier = value; }
            public MinMaxCurve remapX { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.remapX.Get(); set => W.remapX.Set(value); }
            public float remapXMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.remapX.Multiplier; set => W.remapX.Multiplier = value; }
            public MinMaxCurve remapY { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.remapY.Get(); set => W.remapY.Set(value); }
            public float remapYMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.remapY.Multiplier; set => W.remapY.Multiplier = value; }
            public MinMaxCurve remapZ { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.remapZ.Get(); set => W.remapZ.Set(value); }
            public float remapZMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.remapZ.Multiplier; set => W.remapZ.Multiplier = value; }
            public MinMaxCurve positionAmount { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.positionAmount.Get(); set => W.positionAmount.Set(value); }
            public MinMaxCurve rotationAmount { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.rotationAmount.Get(); set => W.rotationAmount.Set(value); }
            public MinMaxCurve sizeAmount { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.sizeAmount.Get(); set => W.sizeAmount.Set(value); }
        }

        /// <summary>Collision settings. Particle collisions are not simulated by Unbound (the data is kept).</summary>
        public partial struct CollisionModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal CollisionModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            CollisionData D => m_ParticleSystem.m_Collision;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            CollisionData W => m_ParticleSystem.ModifiedByScript().m_Collision;

            [Obsolete("The maxPlaneCount restriction has been removed. Please use planeCount instead to find out how many planes there are. (UnityUpgradable) -> UnityEngine.ParticleSystem/CollisionModule.planeCount", false)]
            public int maxPlaneCount => ModuleManagedOwner(m_ParticleSystem).m_Collision.planes.Count;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public ParticleSystemCollisionType type { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.type; set => W.type = value; }
            public ParticleSystemCollisionMode mode { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.mode; set => W.mode = value; }
            public MinMaxCurve dampen { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.dampen.Get(); set => W.dampen.Set(value); }
            public float dampenMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.dampen.Multiplier; set => W.dampen.Multiplier = value; }
            public MinMaxCurve bounce { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.bounce.Get(); set => W.bounce.Set(value); }
            public float bounceMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.bounce.Multiplier; set => W.bounce.Multiplier = value; }
            public MinMaxCurve lifetimeLoss { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.lifetimeLoss.Get(); set => W.lifetimeLoss.Set(value); }
            public float lifetimeLossMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.lifetimeLoss.Multiplier; set => W.lifetimeLoss.Multiplier = value; }
            public float minKillSpeed { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.minKillSpeed; set => W.minKillSpeed = value; }
            public float maxKillSpeed { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.maxKillSpeed; set => W.maxKillSpeed = value; }
            public LayerMask collidesWith { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.collidesWith; set => W.collidesWith = value; }
            public bool enableDynamicColliders { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enableDynamicColliders; set => W.enableDynamicColliders = value; }
            public int maxCollisionShapes { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.maxCollisionShapes; set => W.maxCollisionShapes = value; }
            public ParticleSystemCollisionQuality quality { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.quality; set => W.quality = value; }
            public float voxelSize { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.voxelSize; set => W.voxelSize = value; }
            public float radiusScale { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.radiusScale; set => W.radiusScale = value; }
            public bool sendCollisionMessages { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.sendCollisionMessages; set => W.sendCollisionMessages = value; }
            public float colliderForce { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.colliderForce; set => W.colliderForce = value; }
            public bool multiplyColliderForceByCollisionAngle { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.multiplyColliderForceByCollisionAngle; set => W.multiplyColliderForceByCollisionAngle = value; }
            public bool multiplyColliderForceByParticleSpeed { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.multiplyColliderForceByParticleSpeed; set => W.multiplyColliderForceByParticleSpeed = value; }
            public bool multiplyColliderForceByParticleSize { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.multiplyColliderForceByParticleSize; set => W.multiplyColliderForceByParticleSize = value; }
            public int planeCount => ModuleManagedOwner(m_ParticleSystem).m_Collision.planes.Count;

            [Obsolete("enableInteriorCollisions property is deprecated and is no longer required and has no effect on the particle system.", false)]
            public bool enableInteriorCollisions { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enableInteriorCollisions; set => W.enableInteriorCollisions = value; }

            public void AddPlane(Transform transform) => D.planes.Add(transform);
            public void RemovePlane(int index) => D.planes.RemoveAt(index);
            public void RemovePlane(Transform transform) => D.planes.Remove(transform);
            public void SetPlane(int index, Transform transform)
            {
                while (D.planes.Count <= index) D.planes.Add(null);
                D.planes[index] = transform;
            }
            public Transform GetPlane(int index) => index >= 0 && index < D.planes.Count ? D.planes[index] : null;
        }

        /// <summary>Trigger settings. Particle triggers are not simulated by Unbound (the data is kept).</summary>
        public partial struct TriggerModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal TriggerModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            TriggerData D => m_ParticleSystem.m_Trigger;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            TriggerData W => m_ParticleSystem.ModifiedByScript().m_Trigger;

            [Obsolete("The maxColliderCount restriction has been removed. Please use colliderCount instead to find out how many colliders there are. (UnityUpgradable) -> UnityEngine.ParticleSystem/TriggerModule.colliderCount", false)]
            public int maxColliderCount => ModuleManagedOwner(m_ParticleSystem).m_Trigger.colliders.Count;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public ParticleSystemOverlapAction inside { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.inside; set => W.inside = value; }
            public ParticleSystemOverlapAction outside { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.outside; set => W.outside = value; }
            public ParticleSystemOverlapAction enter { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enter; set => W.enter = value; }
            public ParticleSystemOverlapAction exit { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.exit; set => W.exit = value; }
            public ParticleSystemColliderQueryMode colliderQueryMode { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.colliderQueryMode; set => W.colliderQueryMode = value; }
            public float radiusScale { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.radiusScale; set => W.radiusScale = value; }
            public int colliderCount => ModuleManagedOwner(m_ParticleSystem).m_Trigger.colliders.Count;

            public void AddCollider(Component collider) => D.colliders.Add(collider);
            public void RemoveCollider(int index) => D.colliders.RemoveAt(index);
            public void RemoveCollider(Component collider) => D.colliders.Remove(collider);
            public void SetCollider(int index, Component collider)
            {
                while (D.colliders.Count <= index) D.colliders.Add(null);
                D.colliders[index] = collider;
            }
            public Component GetCollider(int index) => index >= 0 && index < D.colliders.Count ? D.colliders[index] : null;
        }

        public partial struct SubEmittersModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal SubEmittersModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            SubEmittersData D => m_ParticleSystem.m_SubEmitters;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            SubEmittersData W => m_ParticleSystem.ModifiedByScript().m_SubEmitters;

            [Obsolete("birth0 property is deprecated. Use AddSubEmitter, RemoveSubEmitter, SetSubEmitterSystem and GetSubEmitterSystem instead.", false)]
            public ParticleSystem birth0 { get => Find(ParticleSystemSubEmitterType.Birth, 0); set => Put(ParticleSystemSubEmitterType.Birth, 0, value); }
            [Obsolete("birth1 property is deprecated. Use AddSubEmitter, RemoveSubEmitter, SetSubEmitterSystem and GetSubEmitterSystem instead.", false)]
            public ParticleSystem birth1 { get => Find(ParticleSystemSubEmitterType.Birth, 1); set => Put(ParticleSystemSubEmitterType.Birth, 1, value); }
            [Obsolete("collision0 property is deprecated. Use AddSubEmitter, RemoveSubEmitter, SetSubEmitterSystem and GetSubEmitterSystem instead.", false)]
            public ParticleSystem collision0 { get => Find(ParticleSystemSubEmitterType.Collision, 0); set => Put(ParticleSystemSubEmitterType.Collision, 0, value); }
            [Obsolete("collision1 property is deprecated. Use AddSubEmitter, RemoveSubEmitter, SetSubEmitterSystem and GetSubEmitterSystem instead.", false)]
            public ParticleSystem collision1 { get => Find(ParticleSystemSubEmitterType.Collision, 1); set => Put(ParticleSystemSubEmitterType.Collision, 1, value); }
            [Obsolete("death0 property is deprecated. Use AddSubEmitter, RemoveSubEmitter, SetSubEmitterSystem and GetSubEmitterSystem instead.", false)]
            public ParticleSystem death0 { get => Find(ParticleSystemSubEmitterType.Death, 0); set => Put(ParticleSystemSubEmitterType.Death, 0, value); }
            [Obsolete("death1 property is deprecated. Use AddSubEmitter, RemoveSubEmitter, SetSubEmitterSystem and GetSubEmitterSystem instead.", false)]
            public ParticleSystem death1 { get => Find(ParticleSystemSubEmitterType.Death, 1); set => Put(ParticleSystemSubEmitterType.Death, 1, value); }

            public bool enabled
            {
                get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled;
                set { W.enabled = value; m_ParticleSystem.SubEmittersChanged(); }
            }

            public int subEmittersCount => ModuleHasNoOwner(m_ParticleSystem) ? default : D.emitters.Count;

            public void AddSubEmitter(ParticleSystem subEmitter, ParticleSystemSubEmitterType type, ParticleSystemSubEmitterProperties properties, float emitProbability)
            {
                if (subEmitter == null) throw new ArgumentNullException(nameof(subEmitter));
                D.emitters.Add(new SubEmitter { system = subEmitter, type = type, properties = properties, emitProbability = emitProbability });
                m_ParticleSystem.SubEmittersChanged();
            }

            public void AddSubEmitter(ParticleSystem subEmitter, ParticleSystemSubEmitterType type, ParticleSystemSubEmitterProperties properties) =>
                AddSubEmitter(subEmitter, type, properties, 1f);

            public void RemoveSubEmitter(int index)
            {
                D.emitters.RemoveAt(index);
                m_ParticleSystem.SubEmittersChanged();
            }

            public void RemoveSubEmitter(ParticleSystem subEmitter)
            {
                D.emitters.RemoveAll(e => e.system == subEmitter);
                m_ParticleSystem.SubEmittersChanged();
            }

            public void SetSubEmitterSystem(int index, ParticleSystem subEmitter) => Modify(index, (ref SubEmitter e) => e.system = subEmitter);
            public void SetSubEmitterType(int index, ParticleSystemSubEmitterType type) => Modify(index, (ref SubEmitter e) => e.type = type);
            public void SetSubEmitterProperties(int index, ParticleSystemSubEmitterProperties properties) => Modify(index, (ref SubEmitter e) => e.properties = properties);
            public void SetSubEmitterEmitProbability(int index, float emitProbability) => Modify(index, (ref SubEmitter e) => e.emitProbability = emitProbability);
            public ParticleSystem GetSubEmitterSystem(int index) => D.emitters[index].system;
            public ParticleSystemSubEmitterType GetSubEmitterType(int index) => D.emitters[index].type;
            public ParticleSystemSubEmitterProperties GetSubEmitterProperties(int index) => D.emitters[index].properties;
            public float GetSubEmitterEmitProbability(int index) => D.emitters[index].emitProbability;

            delegate void Modifier(ref SubEmitter e);

            void Modify(int index, Modifier modify)
            {
                if (index < 0 || index >= D.emitters.Count) throw new IndexOutOfRangeException("Sub-emitter index out of range.");
                var e = D.emitters[index];
                modify(ref e);
                D.emitters[index] = e;
                m_ParticleSystem.SubEmittersChanged();
            }

            ParticleSystem Find(ParticleSystemSubEmitterType type, int nth)
            {
                if (Compat.RemovedParticleMembersMeasured) throw new NotImplementedException();
                foreach (var e in D.emitters)
                    if (e.type == type && nth-- == 0) return e.system;
                return null;
            }

            void Put(ParticleSystemSubEmitterType type, int nth, ParticleSystem system)
            {
                if (Compat.RemovedParticleMembersMeasured) throw new NotImplementedException();
                for (int i = 0; i < D.emitters.Count; i++)
                {
                    if (D.emitters[i].type != type || nth-- != 0) continue;
                    if (system == null) D.emitters.RemoveAt(i);
                    else { var e = D.emitters[i]; e.system = system; D.emitters[i] = e; }
                    m_ParticleSystem.SubEmittersChanged();
                    return;
                }
                if (system != null) AddSubEmitter(system, type, ParticleSystemSubEmitterProperties.InheritNothing);
            }
        }

        public partial struct TextureSheetAnimationModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal TextureSheetAnimationModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            TextureSheetData D => m_ParticleSystem.m_TextureSheet;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            TextureSheetData W => m_ParticleSystem.ModifiedByScript().m_TextureSheet;

            [Obsolete("flipU property is deprecated. Use ParticleSystemRenderer.flip.x instead.", false)]
            public float flipU { get => ModuleManagedOwner(m_ParticleSystem).m_TextureSheet.flipU; set => W.flipU = value; }
            [Obsolete("flipV property is deprecated. Use ParticleSystemRenderer.flip.y instead.", false)]
            public float flipV { get => ModuleManagedOwner(m_ParticleSystem).m_TextureSheet.flipV; set => W.flipV = value; }
            [Obsolete("useRandomRow property is deprecated. Use rowMode instead.", false)]
            public bool useRandomRow
            {
                get => rowMode == ParticleSystemAnimationRowMode.Random;
                set => W.rowMode = value ? ParticleSystemAnimationRowMode.Random : ParticleSystemAnimationRowMode.Custom;
            }

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public ParticleSystemAnimationMode mode { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.mode; set => W.mode = value; }
            public ParticleSystemAnimationTimeMode timeMode { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.timeMode; set => W.timeMode = value; }
            public float fps { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.fps; set => W.fps = value; }
            public int numTilesX { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.numTilesX; set => W.numTilesX = Math.Max(1, value); }
            public int numTilesY { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.numTilesY; set => W.numTilesY = Math.Max(1, value); }
            public ParticleSystemAnimationType animation { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.animation; set => W.animation = value; }
            public ParticleSystemAnimationRowMode rowMode { get => ModuleHasNoOwner(m_ParticleSystem) ? ParticleSystemAnimationRowMode.Random : D.rowMode; set => W.rowMode = value; }
            public MinMaxCurve frameOverTime { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.frameOverTime.Get(); set => W.frameOverTime.Set(value); }
            public float frameOverTimeMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.frameOverTime.Multiplier; set => W.frameOverTime.Multiplier = value; }
            public MinMaxCurve startFrame { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startFrame.Get(); set => W.startFrame.Set(value); }
            public float startFrameMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.startFrame.Multiplier; set => W.startFrame.Multiplier = value; }
            public int cycleCount { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.cycleCount; set => W.cycleCount = value; }
            public int rowIndex { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.rowIndex; set => W.rowIndex = value; }
            public Rendering.UVChannelFlags uvChannelMask { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.uvChannelMask; set => W.uvChannelMask = value; }
            public int spriteCount => ModuleHasNoOwner(m_ParticleSystem) ? default : D.sprites.Count;
            public Vector2 speedRange { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.speedRange; set => W.speedRange = value; }

            public void AddSprite(Sprite sprite)
            {
                var sprites = D.sprites;
                if (Compat.DefaultConstructorStateMeasured)
                {
                    if (sprite == null) throw new NullReferenceException("sprite is null");
                    // A single empty slot is the initial placeholder, not an animation frame.
                    if (sprites.Count == 1 && sprites[0] == null)
                    {
                        sprites[0] = sprite;
                        return;
                    }
                }
                sprites.Add(sprite);
            }
            public void RemoveSprite(int index) => D.sprites.RemoveAt(index);
            public void SetSprite(int index, Sprite sprite) => D.sprites[index] = sprite;
            public Sprite GetSprite(int index) => D.sprites[index];
        }

        /// <summary>Lights at the particles (copies of a light, see ParticleSystem.Lights.cs).</summary>
        public partial struct LightsModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal LightsModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            LightsData D => m_ParticleSystem.m_Lights;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            LightsData W => m_ParticleSystem.ModifiedByScript().m_Lights;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public float ratio { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.ratio; set => W.ratio = value; }
            public bool useRandomDistribution { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.useRandomDistribution; set => W.useRandomDistribution = value; }
            public bool useParticleColor { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.useParticleColor; set => W.useParticleColor = value; }
            public bool sizeAffectsRange { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.sizeAffectsRange; set => W.sizeAffectsRange = value; }
            public bool alphaAffectsIntensity { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.alphaAffectsIntensity; set => W.alphaAffectsIntensity = value; }
            public MinMaxCurve range { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.range.Get(); set => W.range.Set(value); }
            public float rangeMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.range.Multiplier; set => W.range.Multiplier = value; }
            public MinMaxCurve intensity { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.intensity.Get(); set => W.intensity.Set(value); }
            public float intensityMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.intensity.Multiplier; set => W.intensity.Multiplier = value; }
            public int maxLights { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.maxLights; set => W.maxLights = value; }
            public Light light { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.light as Light; set => W.light = value; }
        }

        /// <summary>Trail settings (see ParticleSystem.Trails.cs).</summary>
        public partial struct TrailModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal TrailModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            TrailData D => m_ParticleSystem.m_Trails;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            TrailData W => m_ParticleSystem.ModifiedByScript().m_Trails;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public ParticleSystemTrailMode mode { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.mode; set => W.mode = value; }
            public float ratio { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.ratio; set => W.ratio = value; }
            public MinMaxCurve lifetime { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.lifetime.Get(); set => W.lifetime.Set(value); }
            public float lifetimeMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.lifetime.Multiplier; set => W.lifetime.Multiplier = value; }
            public float minVertexDistance { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.minVertexDistance; set => W.minVertexDistance = value; }
            public ParticleSystemTrailTextureMode textureMode { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.textureMode; set => W.textureMode = value; }
            public Vector2 textureScale { get => ModuleHasNoOwner(m_ParticleSystem) ? Vector2.one : D.textureScale; set => W.textureScale = value; }
            public bool worldSpace { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.worldSpace; set => W.worldSpace = value; }
            public bool dieWithParticles { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.dieWithParticles; set => W.dieWithParticles = value; }
            public bool sizeAffectsWidth { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.sizeAffectsWidth; set => W.sizeAffectsWidth = value; }
            public bool sizeAffectsLifetime { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.sizeAffectsLifetime; set => W.sizeAffectsLifetime = value; }
            public bool inheritParticleColor { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.inheritParticleColor; set => W.inheritParticleColor = value; }
            public MinMaxGradient colorOverLifetime { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.colorOverLifetime; set => W.colorOverLifetime = value.ForModule(); }
            public MinMaxCurve widthOverTrail { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.widthOverTrail.Get(); set => W.widthOverTrail.Set(value); }
            public float widthOverTrailMultiplier { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.widthOverTrail.Multiplier; set => W.widthOverTrail.Multiplier = value; }
            public MinMaxGradient colorOverTrail { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.colorOverTrail; set => W.colorOverTrail = value.ForModule(); }
            public bool generateLightingData { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.generateLightingData; set => W.generateLightingData = value; }
            public int ribbonCount { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.ribbonCount; set => W.ribbonCount = value; }
            public float shadowBias { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.shadowBias; set => W.shadowBias = value; }
            public bool splitSubEmitterRibbons { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.splitSubEmitterRibbons; set => W.splitSubEmitterRibbons = value; }
            public bool attachRibbonsToTransform { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.attachRibbonsToTransform; set => W.attachRibbonsToTransform = value; }
        }

        public partial struct CustomDataModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal CustomDataModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            CustomDataData D => m_ParticleSystem.m_CustomData;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            CustomDataData W => m_ParticleSystem.ModifiedByScript().m_CustomData;

            public bool enabled { get => ModuleHasNoOwner(m_ParticleSystem) ? default : D.enabled; set => W.enabled = value; }
            public void SetMode(ParticleSystemCustomData stream, ParticleSystemCustomDataMode mode) => D.modes[(int)stream] = mode;
            public ParticleSystemCustomDataMode GetMode(ParticleSystemCustomData stream) => D.modes[(int)stream];
            public void SetVectorComponentCount(ParticleSystemCustomData stream, int count) => D.componentCounts[(int)stream] = Mathf.Clamp(count, 0, 4);
            public int GetVectorComponentCount(ParticleSystemCustomData stream) => D.componentCounts[(int)stream];
            public void SetVector(ParticleSystemCustomData stream, int component, MinMaxCurve curve) => D.vectors[(int)stream, component].Set(curve);
            public MinMaxCurve GetVector(ParticleSystemCustomData stream, int component) => D.vectors[(int)stream, component].Get();
            public void SetColor(ParticleSystemCustomData stream, MinMaxGradient gradient) => D.colors[(int)stream] = gradient.ForModule();
            public MinMaxGradient GetColor(ParticleSystemCustomData stream) => D.colors[(int)stream];
        }
    }
}
