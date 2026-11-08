using System;

namespace UnityEngine
{
    // Module structs are handles to their system, as in Unity: `var main = ps.main; main.loop = false;`
    // writes through to the system. Curves and gradients are copied in and out, so changing a value
    // read from a module affects the system only once it is assigned back.
    public sealed partial class ParticleSystem
    {
        public partial struct MainModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal MainModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            MainData D => m_ParticleSystem.m_Main;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            MainData W => m_ParticleSystem.ModifiedByScript().m_Main;

            [Obsolete("Please use flipRotation instead. (UnityUpgradable) -> UnityEngine.ParticleSystem/MainModule.flipRotation", false)]
            public float randomizeRotationDirection { get => D.flipRotation; set => W.flipRotation = value; }

            public Vector3 emitterVelocity { get => D.customEmitterVelocity; set => W.customEmitterVelocity = value; }

            public float duration
            {
                get => D.duration;
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

            public bool loop { get => D.loop; set => W.loop = value; }
            public bool prewarm { get => D.prewarm; set => W.prewarm = value; }
            public MinMaxCurve startDelay { get => D.startDelay.Get(); set => W.startDelay.Set(value); }
            public float startDelayMultiplier { get => D.startDelay.Multiplier; set => W.startDelay.Multiplier = value; }
            public MinMaxCurve startLifetime { get => D.startLifetime.Get(); set => W.startLifetime.Set(value); }
            public float startLifetimeMultiplier { get => D.startLifetime.Multiplier; set => W.startLifetime.Multiplier = value; }
            public MinMaxCurve startSpeed { get => D.startSpeed.Get(); set => W.startSpeed.Set(value); }
            public float startSpeedMultiplier { get => D.startSpeed.Multiplier; set => W.startSpeed.Multiplier = value; }
            public bool startSize3D { get => D.startSize3D; set => W.startSize3D = value; }
            public MinMaxCurve startSize { get => D.startSizeX.Get(); set => W.startSizeX.Set(value); }
            public float startSizeMultiplier { get => D.startSizeX.Multiplier; set => W.startSizeX.Multiplier = value; }
            public MinMaxCurve startSizeX { get => D.startSizeX.Get(); set => W.startSizeX.Set(value); }
            public float startSizeXMultiplier { get => D.startSizeX.Multiplier; set => W.startSizeX.Multiplier = value; }
            public MinMaxCurve startSizeY { get => D.startSizeY.Get(); set => W.startSizeY.Set(value); }
            public float startSizeYMultiplier { get => D.startSizeY.Multiplier; set => W.startSizeY.Multiplier = value; }
            public MinMaxCurve startSizeZ { get => D.startSizeZ.Get(); set => W.startSizeZ.Set(value); }
            public float startSizeZMultiplier { get => D.startSizeZ.Multiplier; set => W.startSizeZ.Multiplier = value; }
            public bool startRotation3D { get => D.startRotation3D; set => W.startRotation3D = value; }
            public MinMaxCurve startRotation { get => D.startRotationZ.Get(); set => W.startRotationZ.Set(value); }
            public float startRotationMultiplier { get => D.startRotationZ.Multiplier; set => W.startRotationZ.Multiplier = value; }
            public MinMaxCurve startRotationX { get => D.startRotationX.Get(); set => W.startRotationX.Set(value); }
            public float startRotationXMultiplier { get => D.startRotationX.Multiplier; set => W.startRotationX.Multiplier = value; }
            public MinMaxCurve startRotationY { get => D.startRotationY.Get(); set => W.startRotationY.Set(value); }
            public float startRotationYMultiplier { get => D.startRotationY.Multiplier; set => W.startRotationY.Multiplier = value; }
            public MinMaxCurve startRotationZ { get => D.startRotationZ.Get(); set => W.startRotationZ.Set(value); }
            public float startRotationZMultiplier { get => D.startRotationZ.Multiplier; set => W.startRotationZ.Multiplier = value; }
            public float flipRotation { get => D.flipRotation; set => W.flipRotation = value; }
            public MinMaxGradient startColor { get => D.startColor.Clone(); set => W.startColor = value.Clone(); }
            public ParticleSystemGravitySource gravitySource { get => D.gravitySource; set => W.gravitySource = value; }
            public MinMaxCurve gravityModifier { get => D.gravityModifier.Get(); set => W.gravityModifier.Set(value); }
            public float gravityModifierMultiplier { get => D.gravityModifier.Multiplier; set => W.gravityModifier.Multiplier = value; }
            public ParticleSystemSimulationSpace simulationSpace { get => D.simulationSpace; set => m_ParticleSystem.ModifiedByScript().SetSimulationSpace(value); }
            public Transform customSimulationSpace { get => D.customSimulationSpace; set => W.customSimulationSpace = value; }
            public float simulationSpeed { get => D.simulationSpeed; set => W.simulationSpeed = value; }
            public bool useUnscaledTime { get => D.useUnscaledTime; set => W.useUnscaledTime = value; }
            public ParticleSystemScalingMode scalingMode { get => D.scalingMode; set => W.scalingMode = value; }
            public bool playOnAwake { get => D.playOnAwake; set => W.playOnAwake = value; }
            public int maxParticles { get => D.maxParticles; set => W.maxParticles = Math.Max(0, value); }
            public ParticleSystemEmitterVelocityMode emitterVelocityMode { get => D.emitterVelocityMode; set => W.emitterVelocityMode = value; }
            public ParticleSystemStopAction stopAction { get => D.stopAction; set => W.stopAction = value; }
            public ParticleSystemRingBufferMode ringBufferMode { get => D.ringBufferMode; set => W.ringBufferMode = value; }
            public Vector2 ringBufferLoopRange { get => D.ringBufferLoopRange; set => W.ringBufferLoopRange = value; }
            public ParticleSystemCullingMode cullingMode { get => D.cullingMode; set => W.cullingMode = value; }
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
            public ParticleSystemEmissionType type { get => ParticleSystemEmissionType.Time; set { } }

            [Obsolete("rate property is deprecated. Use rateOverTime or rateOverDistance instead.", false)]
            public MinMaxCurve rate { get => rateOverTime; set => rateOverTime = value; }

            [Obsolete("rateMultiplier property is deprecated. Use rateOverTimeMultiplier or rateOverDistanceMultiplier instead.", false)]
            public float rateMultiplier { get => rateOverTimeMultiplier; set => rateOverTimeMultiplier = value; }

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public MinMaxCurve rateOverTime { get => D.rateOverTime.Get(); set => W.rateOverTime.Set(value); }
            public float rateOverTimeMultiplier { get => D.rateOverTime.Multiplier; set => W.rateOverTime.Multiplier = value; }
            public MinMaxCurve rateOverDistance { get => D.rateOverDistance.Get(); set => W.rateOverDistance.Set(value); }
            public float rateOverDistanceMultiplier { get => D.rateOverDistance.Multiplier; set => W.rateOverDistance.Multiplier = value; }

            public int burstCount
            {
                get => D.bursts.Count;
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
            public Vector3 box { get => D.scale; set => W.scale = value; }

            [Obsolete("meshScale property is deprecated.Please use scale instead.", false)]
            public float meshScale { get => D.scale.x; set => W.scale = new Vector3(value, value, value); }

            [Obsolete("randomDirection property is deprecated. Use randomDirectionAmount instead.", false)]
            public bool randomDirection { get => D.randomDirectionAmount >= 0.5f; set => W.randomDirectionAmount = value ? 1f : 0f; }

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public ParticleSystemShapeType shapeType { get => D.shapeType; set => W.shapeType = value; }
            public float randomDirectionAmount { get => D.randomDirectionAmount; set => W.randomDirectionAmount = value; }
            public float sphericalDirectionAmount { get => D.sphericalDirectionAmount; set => W.sphericalDirectionAmount = value; }
            public float randomPositionAmount { get => D.randomPositionAmount; set => W.randomPositionAmount = value; }
            public bool alignToDirection { get => D.alignToDirection; set => W.alignToDirection = value; }
            public float radius { get => D.radius; set => W.radius = value; }
            public ParticleSystemShapeMultiModeValue radiusMode { get => D.radiusMode; set => W.radiusMode = value; }
            public float radiusSpread { get => D.radiusSpread; set => W.radiusSpread = value; }
            public MinMaxCurve radiusSpeed { get => D.radiusSpeed.Get(); set => W.radiusSpeed.Set(value); }
            public float radiusSpeedMultiplier { get => D.radiusSpeed.Multiplier; set => W.radiusSpeed.Multiplier = value; }
            public float radiusThickness { get => D.radiusThickness; set => W.radiusThickness = value; }
            public float angle { get => D.angle; set => W.angle = value; }
            public float length { get => D.length; set => W.length = value; }
            public Vector3 boxThickness { get => D.boxThickness; set => W.boxThickness = value; }
            public ParticleSystemMeshShapeType meshShapeType { get => D.meshShapeType; set => W.meshShapeType = value; }
            public Mesh mesh { get => D.mesh; set => W.mesh = value; }
            public MeshRenderer meshRenderer { get => D.meshRenderer; set => W.meshRenderer = value; }
            public SkinnedMeshRenderer skinnedMeshRenderer { get => D.skinnedMeshRenderer as SkinnedMeshRenderer; set => W.skinnedMeshRenderer = value; }
            public Sprite sprite { get => D.sprite; set => W.sprite = value; }
            public SpriteRenderer spriteRenderer { get => D.spriteRenderer; set => W.spriteRenderer = value; }
            public bool useMeshMaterialIndex { get => D.useMeshMaterialIndex; set => W.useMeshMaterialIndex = value; }
            public int meshMaterialIndex { get => D.meshMaterialIndex; set => W.meshMaterialIndex = value; }
            public bool useMeshColors { get => D.useMeshColors; set => W.useMeshColors = value; }
            public float normalOffset { get => D.normalOffset; set => W.normalOffset = value; }
            public ParticleSystemShapeMultiModeValue meshSpawnMode { get => D.meshSpawnMode; set => W.meshSpawnMode = value; }
            public float meshSpawnSpread { get => D.meshSpawnSpread; set => W.meshSpawnSpread = value; }
            public MinMaxCurve meshSpawnSpeed { get => D.meshSpawnSpeed.Get(); set => W.meshSpawnSpeed.Set(value); }
            public float meshSpawnSpeedMultiplier { get => D.meshSpawnSpeed.Multiplier; set => W.meshSpawnSpeed.Multiplier = value; }
            public float arc { get => D.arc; set => W.arc = value; }
            public ParticleSystemShapeMultiModeValue arcMode { get => D.arcMode; set => W.arcMode = value; }
            public float arcSpread { get => D.arcSpread; set => W.arcSpread = value; }
            public MinMaxCurve arcSpeed { get => D.arcSpeed.Get(); set => W.arcSpeed.Set(value); }
            public float arcSpeedMultiplier { get => D.arcSpeed.Multiplier; set => W.arcSpeed.Multiplier = value; }
            public float donutRadius { get => D.donutRadius; set => W.donutRadius = value; }
            public Vector3 position { get => D.position; set => W.position = value; }
            public Vector3 rotation { get => D.rotation; set => W.rotation = value; }
            public Vector3 scale { get => D.scale; set => W.scale = value; }
            public Texture2D texture { get => D.texture; set => W.texture = value; }
            public ParticleSystemShapeTextureChannel textureClipChannel { get => D.textureClipChannel; set => W.textureClipChannel = value; }
            public float textureClipThreshold { get => D.textureClipThreshold; set => W.textureClipThreshold = value; }
            public bool textureColorAffectsParticles { get => D.textureColorAffectsParticles; set => W.textureColorAffectsParticles = value; }
            public bool textureAlphaAffectsParticles { get => D.textureAlphaAffectsParticles; set => W.textureAlphaAffectsParticles = value; }
            public bool textureBilinearFiltering { get => D.textureBilinearFiltering; set => W.textureBilinearFiltering = value; }
            public int textureUVChannel { get => D.textureUVChannel; set => W.textureUVChannel = value; }
        }

        public partial struct VelocityOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal VelocityOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            VelocityData D => m_ParticleSystem.m_Velocity;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            VelocityData W => m_ParticleSystem.ModifiedByScript().m_Velocity;

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public MinMaxCurve x { get => D.x.Get(); set => W.x.Set(value); }
            public MinMaxCurve y { get => D.y.Get(); set => W.y.Set(value); }
            public MinMaxCurve z { get => D.z.Get(); set => W.z.Set(value); }
            public float xMultiplier { get => D.x.Multiplier; set => W.x.Multiplier = value; }
            public float yMultiplier { get => D.y.Multiplier; set => W.y.Multiplier = value; }
            public float zMultiplier { get => D.z.Multiplier; set => W.z.Multiplier = value; }
            public MinMaxCurve orbitalX { get => D.orbitalX.Get(); set => W.orbitalX.Set(value); }
            public MinMaxCurve orbitalY { get => D.orbitalY.Get(); set => W.orbitalY.Set(value); }
            public MinMaxCurve orbitalZ { get => D.orbitalZ.Get(); set => W.orbitalZ.Set(value); }
            public float orbitalXMultiplier { get => D.orbitalX.Multiplier; set => W.orbitalX.Multiplier = value; }
            public float orbitalYMultiplier { get => D.orbitalY.Multiplier; set => W.orbitalY.Multiplier = value; }
            public float orbitalZMultiplier { get => D.orbitalZ.Multiplier; set => W.orbitalZ.Multiplier = value; }
            public MinMaxCurve orbitalOffsetX { get => D.orbitalOffsetX.Get(); set => W.orbitalOffsetX.Set(value); }
            public MinMaxCurve orbitalOffsetY { get => D.orbitalOffsetY.Get(); set => W.orbitalOffsetY.Set(value); }
            public MinMaxCurve orbitalOffsetZ { get => D.orbitalOffsetZ.Get(); set => W.orbitalOffsetZ.Set(value); }
            public float orbitalOffsetXMultiplier { get => D.orbitalOffsetX.Multiplier; set => W.orbitalOffsetX.Multiplier = value; }
            public float orbitalOffsetYMultiplier { get => D.orbitalOffsetY.Multiplier; set => W.orbitalOffsetY.Multiplier = value; }
            public float orbitalOffsetZMultiplier { get => D.orbitalOffsetZ.Multiplier; set => W.orbitalOffsetZ.Multiplier = value; }
            public MinMaxCurve radial { get => D.radial.Get(); set => W.radial.Set(value); }
            public float radialMultiplier { get => D.radial.Multiplier; set => W.radial.Multiplier = value; }
            public MinMaxCurve speedModifier { get => D.speedModifier.Get(); set => W.speedModifier.Set(value); }
            public float speedModifierMultiplier { get => D.speedModifier.Multiplier; set => W.speedModifier.Multiplier = value; }
            public ParticleSystemSimulationSpace space { get => D.space; set => W.space = value; }
        }

        public partial struct LimitVelocityOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal LimitVelocityOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            LimitVelocityData D => m_ParticleSystem.m_LimitVelocity;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            LimitVelocityData W => m_ParticleSystem.ModifiedByScript().m_LimitVelocity;

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public MinMaxCurve limitX { get => D.limitX.Get(); set => W.limitX.Set(value); }
            public float limitXMultiplier { get => D.limitX.Multiplier; set => W.limitX.Multiplier = value; }
            public MinMaxCurve limitY { get => D.limitY.Get(); set => W.limitY.Set(value); }
            public float limitYMultiplier { get => D.limitY.Multiplier; set => W.limitY.Multiplier = value; }
            public MinMaxCurve limitZ { get => D.limitZ.Get(); set => W.limitZ.Set(value); }
            public float limitZMultiplier { get => D.limitZ.Multiplier; set => W.limitZ.Multiplier = value; }
            public MinMaxCurve limit { get => D.limit.Get(); set => W.limit.Set(value); }
            public float limitMultiplier { get => D.limit.Multiplier; set => W.limit.Multiplier = value; }
            public float dampen { get => D.dampen; set => W.dampen = value; }
            public bool separateAxes { get => D.separateAxes; set => W.separateAxes = value; }
            public ParticleSystemSimulationSpace space { get => D.space; set => W.space = value; }
            public MinMaxCurve drag { get => D.drag.Get(); set => W.drag.Set(value); }
            public float dragMultiplier { get => D.drag.Multiplier; set => W.drag.Multiplier = value; }
            public bool multiplyDragByParticleSize { get => D.multiplyDragByParticleSize; set => W.multiplyDragByParticleSize = value; }
            public bool multiplyDragByParticleVelocity { get => D.multiplyDragByParticleVelocity; set => W.multiplyDragByParticleVelocity = value; }
        }

        public partial struct InheritVelocityModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal InheritVelocityModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            InheritVelocityData D => m_ParticleSystem.m_InheritVelocity;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            InheritVelocityData W => m_ParticleSystem.ModifiedByScript().m_InheritVelocity;

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public ParticleSystemInheritVelocityMode mode { get => D.mode; set => W.mode = value; }
            public MinMaxCurve curve { get => D.curve.Get(); set => W.curve.Set(value); }
            public float curveMultiplier { get => D.curve.Multiplier; set => W.curve.Multiplier = value; }
        }

        public partial struct LifetimeByEmitterSpeedModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal LifetimeByEmitterSpeedModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            LifetimeByEmitterSpeedData D => m_ParticleSystem.m_LifetimeByEmitterSpeed;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            LifetimeByEmitterSpeedData W => m_ParticleSystem.ModifiedByScript().m_LifetimeByEmitterSpeed;

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public MinMaxCurve curve { get => D.curve.Get(); set => W.curve.Set(value); }
            public float curveMultiplier { get => D.curve.Multiplier; set => W.curve.Multiplier = value; }
            public Vector2 range { get => D.range; set => W.range = value; }
        }

        public partial struct ForceOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal ForceOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            ForceData D => m_ParticleSystem.m_Force;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            ForceData W => m_ParticleSystem.ModifiedByScript().m_Force;

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public MinMaxCurve x { get => D.x.Get(); set => W.x.Set(value); }
            public MinMaxCurve y { get => D.y.Get(); set => W.y.Set(value); }
            public MinMaxCurve z { get => D.z.Get(); set => W.z.Set(value); }
            public float xMultiplier { get => D.x.Multiplier; set => W.x.Multiplier = value; }
            public float yMultiplier { get => D.y.Multiplier; set => W.y.Multiplier = value; }
            public float zMultiplier { get => D.z.Multiplier; set => W.z.Multiplier = value; }
            public ParticleSystemSimulationSpace space { get => D.space; set => W.space = value; }
            public bool randomized { get => D.randomized; set => W.randomized = value; }
        }

        public partial struct ColorOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal ColorOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            ColorData D => m_ParticleSystem.m_Color;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            ColorData W => m_ParticleSystem.ModifiedByScript().m_Color;

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public MinMaxGradient color { get => D.color.Clone(); set => W.color = value.Clone(); }
        }

        public partial struct ColorBySpeedModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal ColorBySpeedModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            ColorBySpeedData D => m_ParticleSystem.m_ColorBySpeed;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            ColorBySpeedData W => m_ParticleSystem.ModifiedByScript().m_ColorBySpeed;

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public MinMaxGradient color { get => D.color.Clone(); set => W.color = value.Clone(); }
            public Vector2 range { get => D.range; set => W.range = value; }
        }

        public partial struct SizeOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal SizeOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            SizeData D => m_ParticleSystem.m_Size;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            SizeData W => m_ParticleSystem.ModifiedByScript().m_Size;

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public MinMaxCurve size { get => D.x.Get(); set => W.x.Set(value); }
            public float sizeMultiplier { get => D.x.Multiplier; set => W.x.Multiplier = value; }
            public MinMaxCurve x { get => D.x.Get(); set => W.x.Set(value); }
            public float xMultiplier { get => D.x.Multiplier; set => W.x.Multiplier = value; }
            public MinMaxCurve y { get => D.y.Get(); set => W.y.Set(value); }
            public float yMultiplier { get => D.y.Multiplier; set => W.y.Multiplier = value; }
            public MinMaxCurve z { get => D.z.Get(); set => W.z.Set(value); }
            public float zMultiplier { get => D.z.Multiplier; set => W.z.Multiplier = value; }
            public bool separateAxes { get => D.separateAxes; set => W.separateAxes = value; }
        }

        public partial struct SizeBySpeedModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal SizeBySpeedModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            SizeBySpeedData D => m_ParticleSystem.m_SizeBySpeed;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            SizeBySpeedData W => m_ParticleSystem.ModifiedByScript().m_SizeBySpeed;

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public MinMaxCurve size { get => D.x.Get(); set => W.x.Set(value); }
            public float sizeMultiplier { get => D.x.Multiplier; set => W.x.Multiplier = value; }
            public MinMaxCurve x { get => D.x.Get(); set => W.x.Set(value); }
            public float xMultiplier { get => D.x.Multiplier; set => W.x.Multiplier = value; }
            public MinMaxCurve y { get => D.y.Get(); set => W.y.Set(value); }
            public float yMultiplier { get => D.y.Multiplier; set => W.y.Multiplier = value; }
            public MinMaxCurve z { get => D.z.Get(); set => W.z.Set(value); }
            public float zMultiplier { get => D.z.Multiplier; set => W.z.Multiplier = value; }
            public bool separateAxes { get => D.separateAxes; set => W.separateAxes = value; }
            public Vector2 range { get => D.range; set => W.range = value; }
        }

        public partial struct RotationOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal RotationOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            RotationData D => m_ParticleSystem.m_Rotation;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            RotationData W => m_ParticleSystem.ModifiedByScript().m_Rotation;

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public MinMaxCurve x { get => D.x.Get(); set => W.x.Set(value); }
            public float xMultiplier { get => D.x.Multiplier; set => W.x.Multiplier = value; }
            public MinMaxCurve y { get => D.y.Get(); set => W.y.Set(value); }
            public float yMultiplier { get => D.y.Multiplier; set => W.y.Multiplier = value; }
            public MinMaxCurve z { get => D.z.Get(); set => W.z.Set(value); }
            public float zMultiplier { get => D.z.Multiplier; set => W.z.Multiplier = value; }
            public bool separateAxes { get => D.separateAxes; set => W.separateAxes = value; }
        }

        public partial struct RotationBySpeedModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal RotationBySpeedModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            RotationBySpeedData D => m_ParticleSystem.m_RotationBySpeed;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            RotationBySpeedData W => m_ParticleSystem.ModifiedByScript().m_RotationBySpeed;

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public MinMaxCurve x { get => D.x.Get(); set => W.x.Set(value); }
            public float xMultiplier { get => D.x.Multiplier; set => W.x.Multiplier = value; }
            public MinMaxCurve y { get => D.y.Get(); set => W.y.Set(value); }
            public float yMultiplier { get => D.y.Multiplier; set => W.y.Multiplier = value; }
            public MinMaxCurve z { get => D.z.Get(); set => W.z.Set(value); }
            public float zMultiplier { get => D.z.Multiplier; set => W.z.Multiplier = value; }
            public bool separateAxes { get => D.separateAxes; set => W.separateAxes = value; }
            public Vector2 range { get => D.range; set => W.range = value; }
        }

        public partial struct ExternalForcesModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal ExternalForcesModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            ExternalForcesData D => m_ParticleSystem.m_ExternalForces;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            ExternalForcesData W => m_ParticleSystem.ModifiedByScript().m_ExternalForces;

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public float multiplier { get => D.multiplierCurve.Multiplier; set => W.multiplierCurve.Multiplier = value; }
            public MinMaxCurve multiplierCurve { get => D.multiplierCurve.Get(); set => W.multiplierCurve.Set(value); }
            public ParticleSystemGameObjectFilter influenceFilter { get => D.influenceFilter; set => W.influenceFilter = value; }
            public LayerMask influenceMask { get => D.influenceMask; set => W.influenceMask = value; }
            public int influenceCount => D.influences.Count;

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

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public bool separateAxes { get => D.separateAxes; set => W.separateAxes = value; }
            public MinMaxCurve strength { get => D.strengthX.Get(); set => W.strengthX.Set(value); }
            public float strengthMultiplier { get => D.strengthX.Multiplier; set => W.strengthX.Multiplier = value; }
            public MinMaxCurve strengthX { get => D.strengthX.Get(); set => W.strengthX.Set(value); }
            public float strengthXMultiplier { get => D.strengthX.Multiplier; set => W.strengthX.Multiplier = value; }
            public MinMaxCurve strengthY { get => D.strengthY.Get(); set => W.strengthY.Set(value); }
            public float strengthYMultiplier { get => D.strengthY.Multiplier; set => W.strengthY.Multiplier = value; }
            public MinMaxCurve strengthZ { get => D.strengthZ.Get(); set => W.strengthZ.Set(value); }
            public float strengthZMultiplier { get => D.strengthZ.Multiplier; set => W.strengthZ.Multiplier = value; }
            public float frequency { get => D.frequency; set => W.frequency = value; }
            public bool damping { get => D.damping; set => W.damping = value; }
            public int octaveCount { get => D.octaveCount; set => W.octaveCount = Mathf.Clamp(value, 1, 4); }
            public float octaveMultiplier { get => D.octaveMultiplier; set => W.octaveMultiplier = value; }
            public float octaveScale { get => D.octaveScale; set => W.octaveScale = value; }
            public ParticleSystemNoiseQuality quality { get => D.quality; set => W.quality = value; }
            public MinMaxCurve scrollSpeed { get => D.scrollSpeed.Get(); set => W.scrollSpeed.Set(value); }
            public float scrollSpeedMultiplier { get => D.scrollSpeed.Multiplier; set => W.scrollSpeed.Multiplier = value; }
            public bool remapEnabled { get => D.remapEnabled; set => W.remapEnabled = value; }
            public MinMaxCurve remap { get => D.remapX.Get(); set => W.remapX.Set(value); }
            public float remapMultiplier { get => D.remapX.Multiplier; set => W.remapX.Multiplier = value; }
            public MinMaxCurve remapX { get => D.remapX.Get(); set => W.remapX.Set(value); }
            public float remapXMultiplier { get => D.remapX.Multiplier; set => W.remapX.Multiplier = value; }
            public MinMaxCurve remapY { get => D.remapY.Get(); set => W.remapY.Set(value); }
            public float remapYMultiplier { get => D.remapY.Multiplier; set => W.remapY.Multiplier = value; }
            public MinMaxCurve remapZ { get => D.remapZ.Get(); set => W.remapZ.Set(value); }
            public float remapZMultiplier { get => D.remapZ.Multiplier; set => W.remapZ.Multiplier = value; }
            public MinMaxCurve positionAmount { get => D.positionAmount.Get(); set => W.positionAmount.Set(value); }
            public MinMaxCurve rotationAmount { get => D.rotationAmount.Get(); set => W.rotationAmount.Set(value); }
            public MinMaxCurve sizeAmount { get => D.sizeAmount.Get(); set => W.sizeAmount.Set(value); }
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
            public int maxPlaneCount => D.planes.Count;

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public ParticleSystemCollisionType type { get => D.type; set => W.type = value; }
            public ParticleSystemCollisionMode mode { get => D.mode; set => W.mode = value; }
            public MinMaxCurve dampen { get => D.dampen.Get(); set => W.dampen.Set(value); }
            public float dampenMultiplier { get => D.dampen.Multiplier; set => W.dampen.Multiplier = value; }
            public MinMaxCurve bounce { get => D.bounce.Get(); set => W.bounce.Set(value); }
            public float bounceMultiplier { get => D.bounce.Multiplier; set => W.bounce.Multiplier = value; }
            public MinMaxCurve lifetimeLoss { get => D.lifetimeLoss.Get(); set => W.lifetimeLoss.Set(value); }
            public float lifetimeLossMultiplier { get => D.lifetimeLoss.Multiplier; set => W.lifetimeLoss.Multiplier = value; }
            public float minKillSpeed { get => D.minKillSpeed; set => W.minKillSpeed = value; }
            public float maxKillSpeed { get => D.maxKillSpeed; set => W.maxKillSpeed = value; }
            public LayerMask collidesWith { get => D.collidesWith; set => W.collidesWith = value; }
            public bool enableDynamicColliders { get => D.enableDynamicColliders; set => W.enableDynamicColliders = value; }
            public int maxCollisionShapes { get => D.maxCollisionShapes; set => W.maxCollisionShapes = value; }
            public ParticleSystemCollisionQuality quality { get => D.quality; set => W.quality = value; }
            public float voxelSize { get => D.voxelSize; set => W.voxelSize = value; }
            public float radiusScale { get => D.radiusScale; set => W.radiusScale = value; }
            public bool sendCollisionMessages { get => D.sendCollisionMessages; set => W.sendCollisionMessages = value; }
            public float colliderForce { get => D.colliderForce; set => W.colliderForce = value; }
            public bool multiplyColliderForceByCollisionAngle { get => D.multiplyColliderForceByCollisionAngle; set => W.multiplyColliderForceByCollisionAngle = value; }
            public bool multiplyColliderForceByParticleSpeed { get => D.multiplyColliderForceByParticleSpeed; set => W.multiplyColliderForceByParticleSpeed = value; }
            public bool multiplyColliderForceByParticleSize { get => D.multiplyColliderForceByParticleSize; set => W.multiplyColliderForceByParticleSize = value; }
            public int planeCount => D.planes.Count;

            [Obsolete("enableInteriorCollisions property is deprecated and is no longer required and has no effect on the particle system.", false)]
            public bool enableInteriorCollisions { get => D.enableInteriorCollisions; set => W.enableInteriorCollisions = value; }

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
            public int maxColliderCount => D.colliders.Count;

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public ParticleSystemOverlapAction inside { get => D.inside; set => W.inside = value; }
            public ParticleSystemOverlapAction outside { get => D.outside; set => W.outside = value; }
            public ParticleSystemOverlapAction enter { get => D.enter; set => W.enter = value; }
            public ParticleSystemOverlapAction exit { get => D.exit; set => W.exit = value; }
            public ParticleSystemColliderQueryMode colliderQueryMode { get => D.colliderQueryMode; set => W.colliderQueryMode = value; }
            public float radiusScale { get => D.radiusScale; set => W.radiusScale = value; }
            public int colliderCount => D.colliders.Count;

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
                get => D.enabled;
                set { W.enabled = value; m_ParticleSystem.SubEmittersChanged(); }
            }

            public int subEmittersCount => D.emitters.Count;

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
                foreach (var e in D.emitters)
                    if (e.type == type && nth-- == 0) return e.system;
                return null;
            }

            void Put(ParticleSystemSubEmitterType type, int nth, ParticleSystem system)
            {
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
            public float flipU { get => D.flipU; set => W.flipU = value; }
            [Obsolete("flipV property is deprecated. Use ParticleSystemRenderer.flip.y instead.", false)]
            public float flipV { get => D.flipV; set => W.flipV = value; }
            [Obsolete("useRandomRow property is deprecated. Use rowMode instead.", false)]
            public bool useRandomRow
            {
                get => D.rowMode == ParticleSystemAnimationRowMode.Random;
                set => W.rowMode = value ? ParticleSystemAnimationRowMode.Random : ParticleSystemAnimationRowMode.Custom;
            }

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public ParticleSystemAnimationMode mode { get => D.mode; set => W.mode = value; }
            public ParticleSystemAnimationTimeMode timeMode { get => D.timeMode; set => W.timeMode = value; }
            public float fps { get => D.fps; set => W.fps = value; }
            public int numTilesX { get => D.numTilesX; set => W.numTilesX = Math.Max(1, value); }
            public int numTilesY { get => D.numTilesY; set => W.numTilesY = Math.Max(1, value); }
            public ParticleSystemAnimationType animation { get => D.animation; set => W.animation = value; }
            public ParticleSystemAnimationRowMode rowMode { get => D.rowMode; set => W.rowMode = value; }
            public MinMaxCurve frameOverTime { get => D.frameOverTime.Get(); set => W.frameOverTime.Set(value); }
            public float frameOverTimeMultiplier { get => D.frameOverTime.Multiplier; set => W.frameOverTime.Multiplier = value; }
            public MinMaxCurve startFrame { get => D.startFrame.Get(); set => W.startFrame.Set(value); }
            public float startFrameMultiplier { get => D.startFrame.Multiplier; set => W.startFrame.Multiplier = value; }
            public int cycleCount { get => D.cycleCount; set => W.cycleCount = value; }
            public int rowIndex { get => D.rowIndex; set => W.rowIndex = value; }
            public Rendering.UVChannelFlags uvChannelMask { get => D.uvChannelMask; set => W.uvChannelMask = value; }
            public int spriteCount => D.sprites.Count;
            public Vector2 speedRange { get => D.speedRange; set => W.speedRange = value; }

            public void AddSprite(Sprite sprite) => D.sprites.Add(sprite);
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

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public float ratio { get => D.ratio; set => W.ratio = value; }
            public bool useRandomDistribution { get => D.useRandomDistribution; set => W.useRandomDistribution = value; }
            public bool useParticleColor { get => D.useParticleColor; set => W.useParticleColor = value; }
            public bool sizeAffectsRange { get => D.sizeAffectsRange; set => W.sizeAffectsRange = value; }
            public bool alphaAffectsIntensity { get => D.alphaAffectsIntensity; set => W.alphaAffectsIntensity = value; }
            public MinMaxCurve range { get => D.range.Get(); set => W.range.Set(value); }
            public float rangeMultiplier { get => D.range.Multiplier; set => W.range.Multiplier = value; }
            public MinMaxCurve intensity { get => D.intensity.Get(); set => W.intensity.Set(value); }
            public float intensityMultiplier { get => D.intensity.Multiplier; set => W.intensity.Multiplier = value; }
            public int maxLights { get => D.maxLights; set => W.maxLights = value; }
            public Light light { get => D.light as Light; set => W.light = value; }
        }

        /// <summary>Trail settings (see ParticleSystem.Trails.cs).</summary>
        public partial struct TrailModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal TrailModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            TrailData D => m_ParticleSystem.m_Trails;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            TrailData W => m_ParticleSystem.ModifiedByScript().m_Trails;

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public ParticleSystemTrailMode mode { get => D.mode; set => W.mode = value; }
            public float ratio { get => D.ratio; set => W.ratio = value; }
            public MinMaxCurve lifetime { get => D.lifetime.Get(); set => W.lifetime.Set(value); }
            public float lifetimeMultiplier { get => D.lifetime.Multiplier; set => W.lifetime.Multiplier = value; }
            public float minVertexDistance { get => D.minVertexDistance; set => W.minVertexDistance = value; }
            public ParticleSystemTrailTextureMode textureMode { get => D.textureMode; set => W.textureMode = value; }
            public Vector2 textureScale { get => D.textureScale; set => W.textureScale = value; }
            public bool worldSpace { get => D.worldSpace; set => W.worldSpace = value; }
            public bool dieWithParticles { get => D.dieWithParticles; set => W.dieWithParticles = value; }
            public bool sizeAffectsWidth { get => D.sizeAffectsWidth; set => W.sizeAffectsWidth = value; }
            public bool sizeAffectsLifetime { get => D.sizeAffectsLifetime; set => W.sizeAffectsLifetime = value; }
            public bool inheritParticleColor { get => D.inheritParticleColor; set => W.inheritParticleColor = value; }
            public MinMaxGradient colorOverLifetime { get => D.colorOverLifetime.Clone(); set => W.colorOverLifetime = value.Clone(); }
            public MinMaxCurve widthOverTrail { get => D.widthOverTrail.Get(); set => W.widthOverTrail.Set(value); }
            public float widthOverTrailMultiplier { get => D.widthOverTrail.Multiplier; set => W.widthOverTrail.Multiplier = value; }
            public MinMaxGradient colorOverTrail { get => D.colorOverTrail.Clone(); set => W.colorOverTrail = value.Clone(); }
            public bool generateLightingData { get => D.generateLightingData; set => W.generateLightingData = value; }
            public int ribbonCount { get => D.ribbonCount; set => W.ribbonCount = value; }
            public float shadowBias { get => D.shadowBias; set => W.shadowBias = value; }
            public bool splitSubEmitterRibbons { get => D.splitSubEmitterRibbons; set => W.splitSubEmitterRibbons = value; }
            public bool attachRibbonsToTransform { get => D.attachRibbonsToTransform; set => W.attachRibbonsToTransform = value; }
        }

        public partial struct CustomDataModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal CustomDataModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            CustomDataData D => m_ParticleSystem.m_CustomData;
            /// <summary>The module's data for a write from script (see <see cref="ParticleSystem.ModifiedByScript"/>).</summary>
            CustomDataData W => m_ParticleSystem.ModifiedByScript().m_CustomData;

            public bool enabled { get => D.enabled; set => W.enabled = value; }
            public void SetMode(ParticleSystemCustomData stream, ParticleSystemCustomDataMode mode) => D.modes[(int)stream] = mode;
            public ParticleSystemCustomDataMode GetMode(ParticleSystemCustomData stream) => D.modes[(int)stream];
            public void SetVectorComponentCount(ParticleSystemCustomData stream, int count) => D.componentCounts[(int)stream] = Mathf.Clamp(count, 0, 4);
            public int GetVectorComponentCount(ParticleSystemCustomData stream) => D.componentCounts[(int)stream];
            public void SetVector(ParticleSystemCustomData stream, int component, MinMaxCurve curve) => D.vectors[(int)stream, component].Set(curve);
            public MinMaxCurve GetVector(ParticleSystemCustomData stream, int component) => D.vectors[(int)stream, component].Get();
            public void SetColor(ParticleSystemCustomData stream, MinMaxGradient gradient) => D.colors[(int)stream] = gradient.Clone();
            public MinMaxGradient GetColor(ParticleSystemCustomData stream) => D.colors[(int)stream].Clone();
        }
    }
}
