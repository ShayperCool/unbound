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

            [Obsolete("Please use flipRotation instead. (UnityUpgradable) -> UnityEngine.ParticleSystem/MainModule.flipRotation", false)]
            public float randomizeRotationDirection { get => D.flipRotation; set => D.flipRotation = value; }

            public Vector3 emitterVelocity { get => D.customEmitterVelocity; set => D.customEmitterVelocity = value; }

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
                    D.duration = Mathf.Max(0.05f, value);
                }
            }

            public bool loop { get => D.loop; set => D.loop = value; }
            public bool prewarm { get => D.prewarm; set => D.prewarm = value; }
            public MinMaxCurve startDelay { get => D.startDelay.Get(); set => D.startDelay.Set(value); }
            public float startDelayMultiplier { get => D.startDelay.Multiplier; set => D.startDelay.Multiplier = value; }
            public MinMaxCurve startLifetime { get => D.startLifetime.Get(); set => D.startLifetime.Set(value); }
            public float startLifetimeMultiplier { get => D.startLifetime.Multiplier; set => D.startLifetime.Multiplier = value; }
            public MinMaxCurve startSpeed { get => D.startSpeed.Get(); set => D.startSpeed.Set(value); }
            public float startSpeedMultiplier { get => D.startSpeed.Multiplier; set => D.startSpeed.Multiplier = value; }
            public bool startSize3D { get => D.startSize3D; set => D.startSize3D = value; }
            public MinMaxCurve startSize { get => D.startSizeX.Get(); set => D.startSizeX.Set(value); }
            public float startSizeMultiplier { get => D.startSizeX.Multiplier; set => D.startSizeX.Multiplier = value; }
            public MinMaxCurve startSizeX { get => D.startSizeX.Get(); set => D.startSizeX.Set(value); }
            public float startSizeXMultiplier { get => D.startSizeX.Multiplier; set => D.startSizeX.Multiplier = value; }
            public MinMaxCurve startSizeY { get => D.startSizeY.Get(); set => D.startSizeY.Set(value); }
            public float startSizeYMultiplier { get => D.startSizeY.Multiplier; set => D.startSizeY.Multiplier = value; }
            public MinMaxCurve startSizeZ { get => D.startSizeZ.Get(); set => D.startSizeZ.Set(value); }
            public float startSizeZMultiplier { get => D.startSizeZ.Multiplier; set => D.startSizeZ.Multiplier = value; }
            public bool startRotation3D { get => D.startRotation3D; set => D.startRotation3D = value; }
            public MinMaxCurve startRotation { get => D.startRotationZ.Get(); set => D.startRotationZ.Set(value); }
            public float startRotationMultiplier { get => D.startRotationZ.Multiplier; set => D.startRotationZ.Multiplier = value; }
            public MinMaxCurve startRotationX { get => D.startRotationX.Get(); set => D.startRotationX.Set(value); }
            public float startRotationXMultiplier { get => D.startRotationX.Multiplier; set => D.startRotationX.Multiplier = value; }
            public MinMaxCurve startRotationY { get => D.startRotationY.Get(); set => D.startRotationY.Set(value); }
            public float startRotationYMultiplier { get => D.startRotationY.Multiplier; set => D.startRotationY.Multiplier = value; }
            public MinMaxCurve startRotationZ { get => D.startRotationZ.Get(); set => D.startRotationZ.Set(value); }
            public float startRotationZMultiplier { get => D.startRotationZ.Multiplier; set => D.startRotationZ.Multiplier = value; }
            public float flipRotation { get => D.flipRotation; set => D.flipRotation = value; }
            public MinMaxGradient startColor { get => D.startColor.Clone(); set => D.startColor = value.Clone(); }
            public ParticleSystemGravitySource gravitySource { get => D.gravitySource; set => D.gravitySource = value; }
            public MinMaxCurve gravityModifier { get => D.gravityModifier.Get(); set => D.gravityModifier.Set(value); }
            public float gravityModifierMultiplier { get => D.gravityModifier.Multiplier; set => D.gravityModifier.Multiplier = value; }
            public ParticleSystemSimulationSpace simulationSpace { get => D.simulationSpace; set => m_ParticleSystem.SetSimulationSpace(value); }
            public Transform customSimulationSpace { get => D.customSimulationSpace; set => D.customSimulationSpace = value; }
            public float simulationSpeed { get => D.simulationSpeed; set => D.simulationSpeed = value; }
            public bool useUnscaledTime { get => D.useUnscaledTime; set => D.useUnscaledTime = value; }
            public ParticleSystemScalingMode scalingMode { get => D.scalingMode; set => D.scalingMode = value; }
            public bool playOnAwake { get => D.playOnAwake; set => D.playOnAwake = value; }
            public int maxParticles { get => D.maxParticles; set => D.maxParticles = Math.Max(0, value); }
            public ParticleSystemEmitterVelocityMode emitterVelocityMode { get => D.emitterVelocityMode; set => D.emitterVelocityMode = value; }
            public ParticleSystemStopAction stopAction { get => D.stopAction; set => D.stopAction = value; }
            public ParticleSystemRingBufferMode ringBufferMode { get => D.ringBufferMode; set => D.ringBufferMode = value; }
            public Vector2 ringBufferLoopRange { get => D.ringBufferLoopRange; set => D.ringBufferLoopRange = value; }
            public ParticleSystemCullingMode cullingMode { get => D.cullingMode; set => D.cullingMode = value; }
        }

        public partial struct EmissionModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal EmissionModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            EmissionData D => m_ParticleSystem.m_Emission;

            [Obsolete("ParticleSystemEmissionType no longer does anything. Time and Distance based emission are now both always active.", false)]
            [Unbound.EditorFaithful("obsolete: Unity says it no longer does anything")]
            public ParticleSystemEmissionType type { get => ParticleSystemEmissionType.Time; set { } }

            [Obsolete("rate property is deprecated. Use rateOverTime or rateOverDistance instead.", false)]
            public MinMaxCurve rate { get => rateOverTime; set => rateOverTime = value; }

            [Obsolete("rateMultiplier property is deprecated. Use rateOverTimeMultiplier or rateOverDistanceMultiplier instead.", false)]
            public float rateMultiplier { get => rateOverTimeMultiplier; set => rateOverTimeMultiplier = value; }

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public MinMaxCurve rateOverTime { get => D.rateOverTime.Get(); set => D.rateOverTime.Set(value); }
            public float rateOverTimeMultiplier { get => D.rateOverTime.Multiplier; set => D.rateOverTime.Multiplier = value; }
            public MinMaxCurve rateOverDistance { get => D.rateOverDistance.Get(); set => D.rateOverDistance.Set(value); }
            public float rateOverDistanceMultiplier { get => D.rateOverDistance.Multiplier; set => D.rateOverDistance.Multiplier = value; }

            public int burstCount
            {
                get => D.bursts.Count;
                set
                {
                    value = Math.Max(0, value);
                    while (D.bursts.Count > value) D.bursts.RemoveAt(D.bursts.Count - 1);
                    while (D.bursts.Count < value) D.bursts.Add(new Burst(0f, (short)30));
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

            [Obsolete("Please use scale instead. (UnityUpgradable) -> UnityEngine.ParticleSystem/ShapeModule.scale", false)]
            public Vector3 box { get => D.scale; set => D.scale = value; }

            [Obsolete("meshScale property is deprecated.Please use scale instead.", false)]
            public float meshScale { get => D.scale.x; set => D.scale = new Vector3(value, value, value); }

            [Obsolete("randomDirection property is deprecated. Use randomDirectionAmount instead.", false)]
            public bool randomDirection { get => D.randomDirectionAmount >= 0.5f; set => D.randomDirectionAmount = value ? 1f : 0f; }

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public ParticleSystemShapeType shapeType { get => D.shapeType; set => D.shapeType = value; }
            public float randomDirectionAmount { get => D.randomDirectionAmount; set => D.randomDirectionAmount = value; }
            public float sphericalDirectionAmount { get => D.sphericalDirectionAmount; set => D.sphericalDirectionAmount = value; }
            public float randomPositionAmount { get => D.randomPositionAmount; set => D.randomPositionAmount = value; }
            public bool alignToDirection { get => D.alignToDirection; set => D.alignToDirection = value; }
            public float radius { get => D.radius; set => D.radius = value; }
            public ParticleSystemShapeMultiModeValue radiusMode { get => D.radiusMode; set => D.radiusMode = value; }
            public float radiusSpread { get => D.radiusSpread; set => D.radiusSpread = value; }
            public MinMaxCurve radiusSpeed { get => D.radiusSpeed.Get(); set => D.radiusSpeed.Set(value); }
            public float radiusSpeedMultiplier { get => D.radiusSpeed.Multiplier; set => D.radiusSpeed.Multiplier = value; }
            public float radiusThickness { get => D.radiusThickness; set => D.radiusThickness = value; }
            public float angle { get => D.angle; set => D.angle = value; }
            public float length { get => D.length; set => D.length = value; }
            public Vector3 boxThickness { get => D.boxThickness; set => D.boxThickness = value; }
            public ParticleSystemMeshShapeType meshShapeType { get => D.meshShapeType; set => D.meshShapeType = value; }
            public Mesh mesh { get => D.mesh; set => D.mesh = value; }
            public MeshRenderer meshRenderer { get => D.meshRenderer; set => D.meshRenderer = value; }
            public Sprite sprite { get => D.sprite; set => D.sprite = value; }
            public SpriteRenderer spriteRenderer { get => D.spriteRenderer; set => D.spriteRenderer = value; }
            public bool useMeshMaterialIndex { get => D.useMeshMaterialIndex; set => D.useMeshMaterialIndex = value; }
            public int meshMaterialIndex { get => D.meshMaterialIndex; set => D.meshMaterialIndex = value; }
            public bool useMeshColors { get => D.useMeshColors; set => D.useMeshColors = value; }
            public float normalOffset { get => D.normalOffset; set => D.normalOffset = value; }
            public ParticleSystemShapeMultiModeValue meshSpawnMode { get => D.meshSpawnMode; set => D.meshSpawnMode = value; }
            public float meshSpawnSpread { get => D.meshSpawnSpread; set => D.meshSpawnSpread = value; }
            public MinMaxCurve meshSpawnSpeed { get => D.meshSpawnSpeed.Get(); set => D.meshSpawnSpeed.Set(value); }
            public float meshSpawnSpeedMultiplier { get => D.meshSpawnSpeed.Multiplier; set => D.meshSpawnSpeed.Multiplier = value; }
            public float arc { get => D.arc; set => D.arc = value; }
            public ParticleSystemShapeMultiModeValue arcMode { get => D.arcMode; set => D.arcMode = value; }
            public float arcSpread { get => D.arcSpread; set => D.arcSpread = value; }
            public MinMaxCurve arcSpeed { get => D.arcSpeed.Get(); set => D.arcSpeed.Set(value); }
            public float arcSpeedMultiplier { get => D.arcSpeed.Multiplier; set => D.arcSpeed.Multiplier = value; }
            public float donutRadius { get => D.donutRadius; set => D.donutRadius = value; }
            public Vector3 position { get => D.position; set => D.position = value; }
            public Vector3 rotation { get => D.rotation; set => D.rotation = value; }
            public Vector3 scale { get => D.scale; set => D.scale = value; }
            public Texture2D texture { get => D.texture; set => D.texture = value; }
            public ParticleSystemShapeTextureChannel textureClipChannel { get => D.textureClipChannel; set => D.textureClipChannel = value; }
            public float textureClipThreshold { get => D.textureClipThreshold; set => D.textureClipThreshold = value; }
            public bool textureColorAffectsParticles { get => D.textureColorAffectsParticles; set => D.textureColorAffectsParticles = value; }
            public bool textureAlphaAffectsParticles { get => D.textureAlphaAffectsParticles; set => D.textureAlphaAffectsParticles = value; }
            public bool textureBilinearFiltering { get => D.textureBilinearFiltering; set => D.textureBilinearFiltering = value; }
            public int textureUVChannel { get => D.textureUVChannel; set => D.textureUVChannel = value; }
        }

        public partial struct VelocityOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal VelocityOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            VelocityData D => m_ParticleSystem.m_Velocity;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public MinMaxCurve x { get => D.x.Get(); set => D.x.Set(value); }
            public MinMaxCurve y { get => D.y.Get(); set => D.y.Set(value); }
            public MinMaxCurve z { get => D.z.Get(); set => D.z.Set(value); }
            public float xMultiplier { get => D.x.Multiplier; set => D.x.Multiplier = value; }
            public float yMultiplier { get => D.y.Multiplier; set => D.y.Multiplier = value; }
            public float zMultiplier { get => D.z.Multiplier; set => D.z.Multiplier = value; }
            public MinMaxCurve orbitalX { get => D.orbitalX.Get(); set => D.orbitalX.Set(value); }
            public MinMaxCurve orbitalY { get => D.orbitalY.Get(); set => D.orbitalY.Set(value); }
            public MinMaxCurve orbitalZ { get => D.orbitalZ.Get(); set => D.orbitalZ.Set(value); }
            public float orbitalXMultiplier { get => D.orbitalX.Multiplier; set => D.orbitalX.Multiplier = value; }
            public float orbitalYMultiplier { get => D.orbitalY.Multiplier; set => D.orbitalY.Multiplier = value; }
            public float orbitalZMultiplier { get => D.orbitalZ.Multiplier; set => D.orbitalZ.Multiplier = value; }
            public MinMaxCurve orbitalOffsetX { get => D.orbitalOffsetX.Get(); set => D.orbitalOffsetX.Set(value); }
            public MinMaxCurve orbitalOffsetY { get => D.orbitalOffsetY.Get(); set => D.orbitalOffsetY.Set(value); }
            public MinMaxCurve orbitalOffsetZ { get => D.orbitalOffsetZ.Get(); set => D.orbitalOffsetZ.Set(value); }
            public float orbitalOffsetXMultiplier { get => D.orbitalOffsetX.Multiplier; set => D.orbitalOffsetX.Multiplier = value; }
            public float orbitalOffsetYMultiplier { get => D.orbitalOffsetY.Multiplier; set => D.orbitalOffsetY.Multiplier = value; }
            public float orbitalOffsetZMultiplier { get => D.orbitalOffsetZ.Multiplier; set => D.orbitalOffsetZ.Multiplier = value; }
            public MinMaxCurve radial { get => D.radial.Get(); set => D.radial.Set(value); }
            public float radialMultiplier { get => D.radial.Multiplier; set => D.radial.Multiplier = value; }
            public MinMaxCurve speedModifier { get => D.speedModifier.Get(); set => D.speedModifier.Set(value); }
            public float speedModifierMultiplier { get => D.speedModifier.Multiplier; set => D.speedModifier.Multiplier = value; }
            public ParticleSystemSimulationSpace space { get => D.space; set => D.space = value; }
        }

        public partial struct LimitVelocityOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal LimitVelocityOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            LimitVelocityData D => m_ParticleSystem.m_LimitVelocity;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public MinMaxCurve limitX { get => D.limitX.Get(); set => D.limitX.Set(value); }
            public float limitXMultiplier { get => D.limitX.Multiplier; set => D.limitX.Multiplier = value; }
            public MinMaxCurve limitY { get => D.limitY.Get(); set => D.limitY.Set(value); }
            public float limitYMultiplier { get => D.limitY.Multiplier; set => D.limitY.Multiplier = value; }
            public MinMaxCurve limitZ { get => D.limitZ.Get(); set => D.limitZ.Set(value); }
            public float limitZMultiplier { get => D.limitZ.Multiplier; set => D.limitZ.Multiplier = value; }
            public MinMaxCurve limit { get => D.limit.Get(); set => D.limit.Set(value); }
            public float limitMultiplier { get => D.limit.Multiplier; set => D.limit.Multiplier = value; }
            public float dampen { get => D.dampen; set => D.dampen = value; }
            public bool separateAxes { get => D.separateAxes; set => D.separateAxes = value; }
            public ParticleSystemSimulationSpace space { get => D.space; set => D.space = value; }
            public MinMaxCurve drag { get => D.drag.Get(); set => D.drag.Set(value); }
            public float dragMultiplier { get => D.drag.Multiplier; set => D.drag.Multiplier = value; }
            public bool multiplyDragByParticleSize { get => D.multiplyDragByParticleSize; set => D.multiplyDragByParticleSize = value; }
            public bool multiplyDragByParticleVelocity { get => D.multiplyDragByParticleVelocity; set => D.multiplyDragByParticleVelocity = value; }
        }

        public partial struct InheritVelocityModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal InheritVelocityModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            InheritVelocityData D => m_ParticleSystem.m_InheritVelocity;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public ParticleSystemInheritVelocityMode mode { get => D.mode; set => D.mode = value; }
            public MinMaxCurve curve { get => D.curve.Get(); set => D.curve.Set(value); }
            public float curveMultiplier { get => D.curve.Multiplier; set => D.curve.Multiplier = value; }
        }

        public partial struct LifetimeByEmitterSpeedModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal LifetimeByEmitterSpeedModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            LifetimeByEmitterSpeedData D => m_ParticleSystem.m_LifetimeByEmitterSpeed;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public MinMaxCurve curve { get => D.curve.Get(); set => D.curve.Set(value); }
            public float curveMultiplier { get => D.curve.Multiplier; set => D.curve.Multiplier = value; }
            public Vector2 range { get => D.range; set => D.range = value; }
        }

        public partial struct ForceOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal ForceOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            ForceData D => m_ParticleSystem.m_Force;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public MinMaxCurve x { get => D.x.Get(); set => D.x.Set(value); }
            public MinMaxCurve y { get => D.y.Get(); set => D.y.Set(value); }
            public MinMaxCurve z { get => D.z.Get(); set => D.z.Set(value); }
            public float xMultiplier { get => D.x.Multiplier; set => D.x.Multiplier = value; }
            public float yMultiplier { get => D.y.Multiplier; set => D.y.Multiplier = value; }
            public float zMultiplier { get => D.z.Multiplier; set => D.z.Multiplier = value; }
            public ParticleSystemSimulationSpace space { get => D.space; set => D.space = value; }
            public bool randomized { get => D.randomized; set => D.randomized = value; }
        }

        public partial struct ColorOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal ColorOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            ColorData D => m_ParticleSystem.m_Color;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public MinMaxGradient color { get => D.color.Clone(); set => D.color = value.Clone(); }
        }

        public partial struct ColorBySpeedModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal ColorBySpeedModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            ColorBySpeedData D => m_ParticleSystem.m_ColorBySpeed;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public MinMaxGradient color { get => D.color.Clone(); set => D.color = value.Clone(); }
            public Vector2 range { get => D.range; set => D.range = value; }
        }

        public partial struct SizeOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal SizeOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            SizeData D => m_ParticleSystem.m_Size;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public MinMaxCurve size { get => D.x.Get(); set => D.x.Set(value); }
            public float sizeMultiplier { get => D.x.Multiplier; set => D.x.Multiplier = value; }
            public MinMaxCurve x { get => D.x.Get(); set => D.x.Set(value); }
            public float xMultiplier { get => D.x.Multiplier; set => D.x.Multiplier = value; }
            public MinMaxCurve y { get => D.y.Get(); set => D.y.Set(value); }
            public float yMultiplier { get => D.y.Multiplier; set => D.y.Multiplier = value; }
            public MinMaxCurve z { get => D.z.Get(); set => D.z.Set(value); }
            public float zMultiplier { get => D.z.Multiplier; set => D.z.Multiplier = value; }
            public bool separateAxes { get => D.separateAxes; set => D.separateAxes = value; }
        }

        public partial struct SizeBySpeedModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal SizeBySpeedModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            SizeBySpeedData D => m_ParticleSystem.m_SizeBySpeed;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public MinMaxCurve size { get => D.x.Get(); set => D.x.Set(value); }
            public float sizeMultiplier { get => D.x.Multiplier; set => D.x.Multiplier = value; }
            public MinMaxCurve x { get => D.x.Get(); set => D.x.Set(value); }
            public float xMultiplier { get => D.x.Multiplier; set => D.x.Multiplier = value; }
            public MinMaxCurve y { get => D.y.Get(); set => D.y.Set(value); }
            public float yMultiplier { get => D.y.Multiplier; set => D.y.Multiplier = value; }
            public MinMaxCurve z { get => D.z.Get(); set => D.z.Set(value); }
            public float zMultiplier { get => D.z.Multiplier; set => D.z.Multiplier = value; }
            public bool separateAxes { get => D.separateAxes; set => D.separateAxes = value; }
            public Vector2 range { get => D.range; set => D.range = value; }
        }

        public partial struct RotationOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal RotationOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            RotationData D => m_ParticleSystem.m_Rotation;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public MinMaxCurve x { get => D.x.Get(); set => D.x.Set(value); }
            public float xMultiplier { get => D.x.Multiplier; set => D.x.Multiplier = value; }
            public MinMaxCurve y { get => D.y.Get(); set => D.y.Set(value); }
            public float yMultiplier { get => D.y.Multiplier; set => D.y.Multiplier = value; }
            public MinMaxCurve z { get => D.z.Get(); set => D.z.Set(value); }
            public float zMultiplier { get => D.z.Multiplier; set => D.z.Multiplier = value; }
            public bool separateAxes { get => D.separateAxes; set => D.separateAxes = value; }
        }

        public partial struct RotationBySpeedModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal RotationBySpeedModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            RotationBySpeedData D => m_ParticleSystem.m_RotationBySpeed;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public MinMaxCurve x { get => D.x.Get(); set => D.x.Set(value); }
            public float xMultiplier { get => D.x.Multiplier; set => D.x.Multiplier = value; }
            public MinMaxCurve y { get => D.y.Get(); set => D.y.Set(value); }
            public float yMultiplier { get => D.y.Multiplier; set => D.y.Multiplier = value; }
            public MinMaxCurve z { get => D.z.Get(); set => D.z.Set(value); }
            public float zMultiplier { get => D.z.Multiplier; set => D.z.Multiplier = value; }
            public bool separateAxes { get => D.separateAxes; set => D.separateAxes = value; }
            public Vector2 range { get => D.range; set => D.range = value; }
        }

        public partial struct ExternalForcesModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal ExternalForcesModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            ExternalForcesData D => m_ParticleSystem.m_ExternalForces;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public float multiplier { get => D.multiplierCurve.Multiplier; set => D.multiplierCurve.Multiplier = value; }
            public MinMaxCurve multiplierCurve { get => D.multiplierCurve.Get(); set => D.multiplierCurve.Set(value); }
            public ParticleSystemGameObjectFilter influenceFilter { get => D.influenceFilter; set => D.influenceFilter = value; }
            public LayerMask influenceMask { get => D.influenceMask; set => D.influenceMask = value; }
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

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public bool separateAxes { get => D.separateAxes; set => D.separateAxes = value; }
            public MinMaxCurve strength { get => D.strengthX.Get(); set => D.strengthX.Set(value); }
            public float strengthMultiplier { get => D.strengthX.Multiplier; set => D.strengthX.Multiplier = value; }
            public MinMaxCurve strengthX { get => D.strengthX.Get(); set => D.strengthX.Set(value); }
            public float strengthXMultiplier { get => D.strengthX.Multiplier; set => D.strengthX.Multiplier = value; }
            public MinMaxCurve strengthY { get => D.strengthY.Get(); set => D.strengthY.Set(value); }
            public float strengthYMultiplier { get => D.strengthY.Multiplier; set => D.strengthY.Multiplier = value; }
            public MinMaxCurve strengthZ { get => D.strengthZ.Get(); set => D.strengthZ.Set(value); }
            public float strengthZMultiplier { get => D.strengthZ.Multiplier; set => D.strengthZ.Multiplier = value; }
            public float frequency { get => D.frequency; set => D.frequency = value; }
            public bool damping { get => D.damping; set => D.damping = value; }
            public int octaveCount { get => D.octaveCount; set => D.octaveCount = Mathf.Clamp(value, 1, 4); }
            public float octaveMultiplier { get => D.octaveMultiplier; set => D.octaveMultiplier = value; }
            public float octaveScale { get => D.octaveScale; set => D.octaveScale = value; }
            public ParticleSystemNoiseQuality quality { get => D.quality; set => D.quality = value; }
            public MinMaxCurve scrollSpeed { get => D.scrollSpeed.Get(); set => D.scrollSpeed.Set(value); }
            public float scrollSpeedMultiplier { get => D.scrollSpeed.Multiplier; set => D.scrollSpeed.Multiplier = value; }
            public bool remapEnabled { get => D.remapEnabled; set => D.remapEnabled = value; }
            public MinMaxCurve remap { get => D.remapX.Get(); set => D.remapX.Set(value); }
            public float remapMultiplier { get => D.remapX.Multiplier; set => D.remapX.Multiplier = value; }
            public MinMaxCurve remapX { get => D.remapX.Get(); set => D.remapX.Set(value); }
            public float remapXMultiplier { get => D.remapX.Multiplier; set => D.remapX.Multiplier = value; }
            public MinMaxCurve remapY { get => D.remapY.Get(); set => D.remapY.Set(value); }
            public float remapYMultiplier { get => D.remapY.Multiplier; set => D.remapY.Multiplier = value; }
            public MinMaxCurve remapZ { get => D.remapZ.Get(); set => D.remapZ.Set(value); }
            public float remapZMultiplier { get => D.remapZ.Multiplier; set => D.remapZ.Multiplier = value; }
            public MinMaxCurve positionAmount { get => D.positionAmount.Get(); set => D.positionAmount.Set(value); }
            public MinMaxCurve rotationAmount { get => D.rotationAmount.Get(); set => D.rotationAmount.Set(value); }
            public MinMaxCurve sizeAmount { get => D.sizeAmount.Get(); set => D.sizeAmount.Set(value); }
        }

        /// <summary>Collision settings. Particle collisions are not simulated by Unbound (the data is kept).</summary>
        public partial struct CollisionModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal CollisionModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            CollisionData D => m_ParticleSystem.m_Collision;

            [Obsolete("The maxPlaneCount restriction has been removed. Please use planeCount instead to find out how many planes there are. (UnityUpgradable) -> UnityEngine.ParticleSystem/CollisionModule.planeCount", false)]
            public int maxPlaneCount => D.planes.Count;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public ParticleSystemCollisionType type { get => D.type; set => D.type = value; }
            public ParticleSystemCollisionMode mode { get => D.mode; set => D.mode = value; }
            public MinMaxCurve dampen { get => D.dampen.Get(); set => D.dampen.Set(value); }
            public float dampenMultiplier { get => D.dampen.Multiplier; set => D.dampen.Multiplier = value; }
            public MinMaxCurve bounce { get => D.bounce.Get(); set => D.bounce.Set(value); }
            public float bounceMultiplier { get => D.bounce.Multiplier; set => D.bounce.Multiplier = value; }
            public MinMaxCurve lifetimeLoss { get => D.lifetimeLoss.Get(); set => D.lifetimeLoss.Set(value); }
            public float lifetimeLossMultiplier { get => D.lifetimeLoss.Multiplier; set => D.lifetimeLoss.Multiplier = value; }
            public float minKillSpeed { get => D.minKillSpeed; set => D.minKillSpeed = value; }
            public float maxKillSpeed { get => D.maxKillSpeed; set => D.maxKillSpeed = value; }
            public LayerMask collidesWith { get => D.collidesWith; set => D.collidesWith = value; }
            public bool enableDynamicColliders { get => D.enableDynamicColliders; set => D.enableDynamicColliders = value; }
            public int maxCollisionShapes { get => D.maxCollisionShapes; set => D.maxCollisionShapes = value; }
            public ParticleSystemCollisionQuality quality { get => D.quality; set => D.quality = value; }
            public float voxelSize { get => D.voxelSize; set => D.voxelSize = value; }
            public float radiusScale { get => D.radiusScale; set => D.radiusScale = value; }
            public bool sendCollisionMessages { get => D.sendCollisionMessages; set => D.sendCollisionMessages = value; }
            public float colliderForce { get => D.colliderForce; set => D.colliderForce = value; }
            public bool multiplyColliderForceByCollisionAngle { get => D.multiplyColliderForceByCollisionAngle; set => D.multiplyColliderForceByCollisionAngle = value; }
            public bool multiplyColliderForceByParticleSpeed { get => D.multiplyColliderForceByParticleSpeed; set => D.multiplyColliderForceByParticleSpeed = value; }
            public bool multiplyColliderForceByParticleSize { get => D.multiplyColliderForceByParticleSize; set => D.multiplyColliderForceByParticleSize = value; }
            public int planeCount => D.planes.Count;

            [Obsolete("enableInteriorCollisions property is deprecated and is no longer required and has no effect on the particle system.", false)]
            public bool enableInteriorCollisions { get => D.enableInteriorCollisions; set => D.enableInteriorCollisions = value; }

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

            [Obsolete("The maxColliderCount restriction has been removed. Please use colliderCount instead to find out how many colliders there are. (UnityUpgradable) -> UnityEngine.ParticleSystem/TriggerModule.colliderCount", false)]
            public int maxColliderCount => D.colliders.Count;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public ParticleSystemOverlapAction inside { get => D.inside; set => D.inside = value; }
            public ParticleSystemOverlapAction outside { get => D.outside; set => D.outside = value; }
            public ParticleSystemOverlapAction enter { get => D.enter; set => D.enter = value; }
            public ParticleSystemOverlapAction exit { get => D.exit; set => D.exit = value; }
            public ParticleSystemColliderQueryMode colliderQueryMode { get => D.colliderQueryMode; set => D.colliderQueryMode = value; }
            public float radiusScale { get => D.radiusScale; set => D.radiusScale = value; }
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
                set { D.enabled = value; m_ParticleSystem.SubEmittersChanged(); }
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

            [Obsolete("flipU property is deprecated. Use ParticleSystemRenderer.flip.x instead.", false)]
            public float flipU { get => D.flipU; set => D.flipU = value; }
            [Obsolete("flipV property is deprecated. Use ParticleSystemRenderer.flip.y instead.", false)]
            public float flipV { get => D.flipV; set => D.flipV = value; }
            [Obsolete("useRandomRow property is deprecated. Use rowMode instead.", false)]
            public bool useRandomRow
            {
                get => D.rowMode == ParticleSystemAnimationRowMode.Random;
                set => D.rowMode = value ? ParticleSystemAnimationRowMode.Random : ParticleSystemAnimationRowMode.Custom;
            }

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public ParticleSystemAnimationMode mode { get => D.mode; set => D.mode = value; }
            public ParticleSystemAnimationTimeMode timeMode { get => D.timeMode; set => D.timeMode = value; }
            public float fps { get => D.fps; set => D.fps = value; }
            public int numTilesX { get => D.numTilesX; set => D.numTilesX = Math.Max(1, value); }
            public int numTilesY { get => D.numTilesY; set => D.numTilesY = Math.Max(1, value); }
            public ParticleSystemAnimationType animation { get => D.animation; set => D.animation = value; }
            public ParticleSystemAnimationRowMode rowMode { get => D.rowMode; set => D.rowMode = value; }
            public MinMaxCurve frameOverTime { get => D.frameOverTime.Get(); set => D.frameOverTime.Set(value); }
            public float frameOverTimeMultiplier { get => D.frameOverTime.Multiplier; set => D.frameOverTime.Multiplier = value; }
            public MinMaxCurve startFrame { get => D.startFrame.Get(); set => D.startFrame.Set(value); }
            public float startFrameMultiplier { get => D.startFrame.Multiplier; set => D.startFrame.Multiplier = value; }
            public int cycleCount { get => D.cycleCount; set => D.cycleCount = value; }
            public int rowIndex { get => D.rowIndex; set => D.rowIndex = value; }
            public Rendering.UVChannelFlags uvChannelMask { get => D.uvChannelMask; set => D.uvChannelMask = value; }
            public int spriteCount => D.sprites.Count;
            public Vector2 speedRange { get => D.speedRange; set => D.speedRange = value; }

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

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public float ratio { get => D.ratio; set => D.ratio = value; }
            public bool useRandomDistribution { get => D.useRandomDistribution; set => D.useRandomDistribution = value; }
            public bool useParticleColor { get => D.useParticleColor; set => D.useParticleColor = value; }
            public bool sizeAffectsRange { get => D.sizeAffectsRange; set => D.sizeAffectsRange = value; }
            public bool alphaAffectsIntensity { get => D.alphaAffectsIntensity; set => D.alphaAffectsIntensity = value; }
            public MinMaxCurve range { get => D.range.Get(); set => D.range.Set(value); }
            public float rangeMultiplier { get => D.range.Multiplier; set => D.range.Multiplier = value; }
            public MinMaxCurve intensity { get => D.intensity.Get(); set => D.intensity.Set(value); }
            public float intensityMultiplier { get => D.intensity.Multiplier; set => D.intensity.Multiplier = value; }
            public int maxLights { get => D.maxLights; set => D.maxLights = value; }
            public Light light { get => D.light as Light; set => D.light = value; }
        }

        /// <summary>Trail settings (see ParticleSystem.Trails.cs).</summary>
        public partial struct TrailModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal TrailModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            TrailData D => m_ParticleSystem.m_Trails;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public ParticleSystemTrailMode mode { get => D.mode; set => D.mode = value; }
            public float ratio { get => D.ratio; set => D.ratio = value; }
            public MinMaxCurve lifetime { get => D.lifetime.Get(); set => D.lifetime.Set(value); }
            public float lifetimeMultiplier { get => D.lifetime.Multiplier; set => D.lifetime.Multiplier = value; }
            public float minVertexDistance { get => D.minVertexDistance; set => D.minVertexDistance = value; }
            public ParticleSystemTrailTextureMode textureMode { get => D.textureMode; set => D.textureMode = value; }
            public Vector2 textureScale { get => D.textureScale; set => D.textureScale = value; }
            public bool worldSpace { get => D.worldSpace; set => D.worldSpace = value; }
            public bool dieWithParticles { get => D.dieWithParticles; set => D.dieWithParticles = value; }
            public bool sizeAffectsWidth { get => D.sizeAffectsWidth; set => D.sizeAffectsWidth = value; }
            public bool sizeAffectsLifetime { get => D.sizeAffectsLifetime; set => D.sizeAffectsLifetime = value; }
            public bool inheritParticleColor { get => D.inheritParticleColor; set => D.inheritParticleColor = value; }
            public MinMaxGradient colorOverLifetime { get => D.colorOverLifetime.Clone(); set => D.colorOverLifetime = value.Clone(); }
            public MinMaxCurve widthOverTrail { get => D.widthOverTrail.Get(); set => D.widthOverTrail.Set(value); }
            public float widthOverTrailMultiplier { get => D.widthOverTrail.Multiplier; set => D.widthOverTrail.Multiplier = value; }
            public MinMaxGradient colorOverTrail { get => D.colorOverTrail.Clone(); set => D.colorOverTrail = value.Clone(); }
            public bool generateLightingData { get => D.generateLightingData; set => D.generateLightingData = value; }
            public int ribbonCount { get => D.ribbonCount; set => D.ribbonCount = value; }
            public float shadowBias { get => D.shadowBias; set => D.shadowBias = value; }
            public bool splitSubEmitterRibbons { get => D.splitSubEmitterRibbons; set => D.splitSubEmitterRibbons = value; }
            public bool attachRibbonsToTransform { get => D.attachRibbonsToTransform; set => D.attachRibbonsToTransform = value; }
        }

        public partial struct CustomDataModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal CustomDataModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            CustomDataData D => m_ParticleSystem.m_CustomData;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
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
