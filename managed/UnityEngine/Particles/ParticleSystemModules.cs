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
            public MinMaxCurve startDelay { get => D.startDelay.Clone(); set => D.startDelay = value.Clone(); }
            public float startDelayMultiplier { get => D.startDelay.curveMultiplier; set => D.startDelay.curveMultiplier = value; }
            public MinMaxCurve startLifetime { get => D.startLifetime.Clone(); set => D.startLifetime = value.Clone(); }
            public float startLifetimeMultiplier { get => D.startLifetime.curveMultiplier; set => D.startLifetime.curveMultiplier = value; }
            public MinMaxCurve startSpeed { get => D.startSpeed.Clone(); set => D.startSpeed = value.Clone(); }
            public float startSpeedMultiplier { get => D.startSpeed.curveMultiplier; set => D.startSpeed.curveMultiplier = value; }
            public bool startSize3D { get => D.startSize3D; set => D.startSize3D = value; }
            public MinMaxCurve startSize { get => D.startSizeX.Clone(); set => D.startSizeX = value.Clone(); }
            public float startSizeMultiplier { get => D.startSizeX.curveMultiplier; set => D.startSizeX.curveMultiplier = value; }
            public MinMaxCurve startSizeX { get => D.startSizeX.Clone(); set => D.startSizeX = value.Clone(); }
            public float startSizeXMultiplier { get => D.startSizeX.curveMultiplier; set => D.startSizeX.curveMultiplier = value; }
            public MinMaxCurve startSizeY { get => D.startSizeY.Clone(); set => D.startSizeY = value.Clone(); }
            public float startSizeYMultiplier { get => D.startSizeY.curveMultiplier; set => D.startSizeY.curveMultiplier = value; }
            public MinMaxCurve startSizeZ { get => D.startSizeZ.Clone(); set => D.startSizeZ = value.Clone(); }
            public float startSizeZMultiplier { get => D.startSizeZ.curveMultiplier; set => D.startSizeZ.curveMultiplier = value; }
            public bool startRotation3D { get => D.startRotation3D; set => D.startRotation3D = value; }
            public MinMaxCurve startRotation { get => D.startRotationZ.Clone(); set => D.startRotationZ = value.Clone(); }
            public float startRotationMultiplier { get => D.startRotationZ.curveMultiplier; set => D.startRotationZ.curveMultiplier = value; }
            public MinMaxCurve startRotationX { get => D.startRotationX.Clone(); set => D.startRotationX = value.Clone(); }
            public float startRotationXMultiplier { get => D.startRotationX.curveMultiplier; set => D.startRotationX.curveMultiplier = value; }
            public MinMaxCurve startRotationY { get => D.startRotationY.Clone(); set => D.startRotationY = value.Clone(); }
            public float startRotationYMultiplier { get => D.startRotationY.curveMultiplier; set => D.startRotationY.curveMultiplier = value; }
            public MinMaxCurve startRotationZ { get => D.startRotationZ.Clone(); set => D.startRotationZ = value.Clone(); }
            public float startRotationZMultiplier { get => D.startRotationZ.curveMultiplier; set => D.startRotationZ.curveMultiplier = value; }
            public float flipRotation { get => D.flipRotation; set => D.flipRotation = value; }
            public MinMaxGradient startColor { get => D.startColor.Clone(); set => D.startColor = value.Clone(); }
            public ParticleSystemGravitySource gravitySource { get => D.gravitySource; set => D.gravitySource = value; }
            public MinMaxCurve gravityModifier { get => D.gravityModifier.Clone(); set => D.gravityModifier = value.Clone(); }
            public float gravityModifierMultiplier { get => D.gravityModifier.curveMultiplier; set => D.gravityModifier.curveMultiplier = value; }
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
            public MinMaxCurve rateOverTime { get => D.rateOverTime.Clone(); set => D.rateOverTime = value.Clone(); }
            public float rateOverTimeMultiplier { get => D.rateOverTime.curveMultiplier; set => D.rateOverTime.curveMultiplier = value; }
            public MinMaxCurve rateOverDistance { get => D.rateOverDistance.Clone(); set => D.rateOverDistance = value.Clone(); }
            public float rateOverDistanceMultiplier { get => D.rateOverDistance.curveMultiplier; set => D.rateOverDistance.curveMultiplier = value; }

            public int burstCount
            {
                get => D.bursts.Count;
                set
                {
                    value = Math.Max(0, value);
                    while (D.bursts.Count > value) D.bursts.RemoveAt(D.bursts.Count - 1);
                    while (D.bursts.Count < value) D.bursts.Add(new Burst(0f, (short)30));
                }
            }

            public void SetBursts(Burst[] bursts) => SetBursts(bursts, bursts?.Length ?? 0);

            public void SetBursts(Burst[] bursts, int size)
            {
                D.bursts.Clear();
                if (bursts == null) return;
                for (int i = 0; i < size && i < bursts.Length; i++) D.bursts.Add(Copy(bursts[i]));
            }

            public int GetBursts(Burst[] bursts)
            {
                if (bursts == null) throw new ArgumentNullException(nameof(bursts));
                int n = Math.Min(bursts.Length, D.bursts.Count);
                for (int i = 0; i < n; i++) bursts[i] = Copy(D.bursts[i]);
                return n;
            }

            public void SetBurst(int index, Burst burst)
            {
                if (index < 0 || index >= D.bursts.Count) throw new IndexOutOfRangeException("Burst index out of range.");
                D.bursts[index] = Copy(burst);
            }

            public Burst GetBurst(int index)
            {
                if (index < 0 || index >= D.bursts.Count) throw new IndexOutOfRangeException("Burst index out of range.");
                return Copy(D.bursts[index]);
            }

            static Burst Copy(Burst b)
            {
                b.m_Count = b.m_Count.Clone();
                return b;
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
            public MinMaxCurve radiusSpeed { get => D.radiusSpeed.Clone(); set => D.radiusSpeed = value.Clone(); }
            public float radiusSpeedMultiplier { get => D.radiusSpeed.curveMultiplier; set => D.radiusSpeed.curveMultiplier = value; }
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
            public MinMaxCurve meshSpawnSpeed { get => D.meshSpawnSpeed.Clone(); set => D.meshSpawnSpeed = value.Clone(); }
            public float meshSpawnSpeedMultiplier { get => D.meshSpawnSpeed.curveMultiplier; set => D.meshSpawnSpeed.curveMultiplier = value; }
            public float arc { get => D.arc; set => D.arc = value; }
            public ParticleSystemShapeMultiModeValue arcMode { get => D.arcMode; set => D.arcMode = value; }
            public float arcSpread { get => D.arcSpread; set => D.arcSpread = value; }
            public MinMaxCurve arcSpeed { get => D.arcSpeed.Clone(); set => D.arcSpeed = value.Clone(); }
            public float arcSpeedMultiplier { get => D.arcSpeed.curveMultiplier; set => D.arcSpeed.curveMultiplier = value; }
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
            public MinMaxCurve x { get => D.x.Clone(); set => D.x = value.Clone(); }
            public MinMaxCurve y { get => D.y.Clone(); set => D.y = value.Clone(); }
            public MinMaxCurve z { get => D.z.Clone(); set => D.z = value.Clone(); }
            public float xMultiplier { get => D.x.curveMultiplier; set => D.x.curveMultiplier = value; }
            public float yMultiplier { get => D.y.curveMultiplier; set => D.y.curveMultiplier = value; }
            public float zMultiplier { get => D.z.curveMultiplier; set => D.z.curveMultiplier = value; }
            public MinMaxCurve orbitalX { get => D.orbitalX.Clone(); set => D.orbitalX = value.Clone(); }
            public MinMaxCurve orbitalY { get => D.orbitalY.Clone(); set => D.orbitalY = value.Clone(); }
            public MinMaxCurve orbitalZ { get => D.orbitalZ.Clone(); set => D.orbitalZ = value.Clone(); }
            public float orbitalXMultiplier { get => D.orbitalX.curveMultiplier; set => D.orbitalX.curveMultiplier = value; }
            public float orbitalYMultiplier { get => D.orbitalY.curveMultiplier; set => D.orbitalY.curveMultiplier = value; }
            public float orbitalZMultiplier { get => D.orbitalZ.curveMultiplier; set => D.orbitalZ.curveMultiplier = value; }
            public MinMaxCurve orbitalOffsetX { get => D.orbitalOffsetX.Clone(); set => D.orbitalOffsetX = value.Clone(); }
            public MinMaxCurve orbitalOffsetY { get => D.orbitalOffsetY.Clone(); set => D.orbitalOffsetY = value.Clone(); }
            public MinMaxCurve orbitalOffsetZ { get => D.orbitalOffsetZ.Clone(); set => D.orbitalOffsetZ = value.Clone(); }
            public float orbitalOffsetXMultiplier { get => D.orbitalOffsetX.curveMultiplier; set => D.orbitalOffsetX.curveMultiplier = value; }
            public float orbitalOffsetYMultiplier { get => D.orbitalOffsetY.curveMultiplier; set => D.orbitalOffsetY.curveMultiplier = value; }
            public float orbitalOffsetZMultiplier { get => D.orbitalOffsetZ.curveMultiplier; set => D.orbitalOffsetZ.curveMultiplier = value; }
            public MinMaxCurve radial { get => D.radial.Clone(); set => D.radial = value.Clone(); }
            public float radialMultiplier { get => D.radial.curveMultiplier; set => D.radial.curveMultiplier = value; }
            public MinMaxCurve speedModifier { get => D.speedModifier.Clone(); set => D.speedModifier = value.Clone(); }
            public float speedModifierMultiplier { get => D.speedModifier.curveMultiplier; set => D.speedModifier.curveMultiplier = value; }
            public ParticleSystemSimulationSpace space { get => D.space; set => D.space = value; }
        }

        public partial struct LimitVelocityOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal LimitVelocityOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            LimitVelocityData D => m_ParticleSystem.m_LimitVelocity;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public MinMaxCurve limitX { get => D.limitX.Clone(); set => D.limitX = value.Clone(); }
            public float limitXMultiplier { get => D.limitX.curveMultiplier; set => D.limitX.curveMultiplier = value; }
            public MinMaxCurve limitY { get => D.limitY.Clone(); set => D.limitY = value.Clone(); }
            public float limitYMultiplier { get => D.limitY.curveMultiplier; set => D.limitY.curveMultiplier = value; }
            public MinMaxCurve limitZ { get => D.limitZ.Clone(); set => D.limitZ = value.Clone(); }
            public float limitZMultiplier { get => D.limitZ.curveMultiplier; set => D.limitZ.curveMultiplier = value; }
            public MinMaxCurve limit { get => D.limit.Clone(); set => D.limit = value.Clone(); }
            public float limitMultiplier { get => D.limit.curveMultiplier; set => D.limit.curveMultiplier = value; }
            public float dampen { get => D.dampen; set => D.dampen = value; }
            public bool separateAxes { get => D.separateAxes; set => D.separateAxes = value; }
            public ParticleSystemSimulationSpace space { get => D.space; set => D.space = value; }
            public MinMaxCurve drag { get => D.drag.Clone(); set => D.drag = value.Clone(); }
            public float dragMultiplier { get => D.drag.curveMultiplier; set => D.drag.curveMultiplier = value; }
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
            public MinMaxCurve curve { get => D.curve.Clone(); set => D.curve = value.Clone(); }
            public float curveMultiplier { get => D.curve.curveMultiplier; set => D.curve.curveMultiplier = value; }
        }

        public partial struct LifetimeByEmitterSpeedModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal LifetimeByEmitterSpeedModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            LifetimeByEmitterSpeedData D => m_ParticleSystem.m_LifetimeByEmitterSpeed;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public MinMaxCurve curve { get => D.curve.Clone(); set => D.curve = value.Clone(); }
            public float curveMultiplier { get => D.curve.curveMultiplier; set => D.curve.curveMultiplier = value; }
            public Vector2 range { get => D.range; set => D.range = value; }
        }

        public partial struct ForceOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal ForceOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            ForceData D => m_ParticleSystem.m_Force;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public MinMaxCurve x { get => D.x.Clone(); set => D.x = value.Clone(); }
            public MinMaxCurve y { get => D.y.Clone(); set => D.y = value.Clone(); }
            public MinMaxCurve z { get => D.z.Clone(); set => D.z = value.Clone(); }
            public float xMultiplier { get => D.x.curveMultiplier; set => D.x.curveMultiplier = value; }
            public float yMultiplier { get => D.y.curveMultiplier; set => D.y.curveMultiplier = value; }
            public float zMultiplier { get => D.z.curveMultiplier; set => D.z.curveMultiplier = value; }
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
            public MinMaxCurve size { get => D.x.Clone(); set => D.x = value.Clone(); }
            public float sizeMultiplier { get => D.x.curveMultiplier; set => D.x.curveMultiplier = value; }
            public MinMaxCurve x { get => D.x.Clone(); set => D.x = value.Clone(); }
            public float xMultiplier { get => D.x.curveMultiplier; set => D.x.curveMultiplier = value; }
            public MinMaxCurve y { get => D.y.Clone(); set => D.y = value.Clone(); }
            public float yMultiplier { get => D.y.curveMultiplier; set => D.y.curveMultiplier = value; }
            public MinMaxCurve z { get => D.z.Clone(); set => D.z = value.Clone(); }
            public float zMultiplier { get => D.z.curveMultiplier; set => D.z.curveMultiplier = value; }
            public bool separateAxes { get => D.separateAxes; set => D.separateAxes = value; }
        }

        public partial struct SizeBySpeedModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal SizeBySpeedModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            SizeBySpeedData D => m_ParticleSystem.m_SizeBySpeed;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public MinMaxCurve size { get => D.x.Clone(); set => D.x = value.Clone(); }
            public float sizeMultiplier { get => D.x.curveMultiplier; set => D.x.curveMultiplier = value; }
            public MinMaxCurve x { get => D.x.Clone(); set => D.x = value.Clone(); }
            public float xMultiplier { get => D.x.curveMultiplier; set => D.x.curveMultiplier = value; }
            public MinMaxCurve y { get => D.y.Clone(); set => D.y = value.Clone(); }
            public float yMultiplier { get => D.y.curveMultiplier; set => D.y.curveMultiplier = value; }
            public MinMaxCurve z { get => D.z.Clone(); set => D.z = value.Clone(); }
            public float zMultiplier { get => D.z.curveMultiplier; set => D.z.curveMultiplier = value; }
            public bool separateAxes { get => D.separateAxes; set => D.separateAxes = value; }
            public Vector2 range { get => D.range; set => D.range = value; }
        }

        public partial struct RotationOverLifetimeModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal RotationOverLifetimeModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            RotationData D => m_ParticleSystem.m_Rotation;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public MinMaxCurve x { get => D.x.Clone(); set => D.x = value.Clone(); }
            public float xMultiplier { get => D.x.curveMultiplier; set => D.x.curveMultiplier = value; }
            public MinMaxCurve y { get => D.y.Clone(); set => D.y = value.Clone(); }
            public float yMultiplier { get => D.y.curveMultiplier; set => D.y.curveMultiplier = value; }
            public MinMaxCurve z { get => D.z.Clone(); set => D.z = value.Clone(); }
            public float zMultiplier { get => D.z.curveMultiplier; set => D.z.curveMultiplier = value; }
            public bool separateAxes { get => D.separateAxes; set => D.separateAxes = value; }
        }

        public partial struct RotationBySpeedModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal RotationBySpeedModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            RotationBySpeedData D => m_ParticleSystem.m_RotationBySpeed;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public MinMaxCurve x { get => D.x.Clone(); set => D.x = value.Clone(); }
            public float xMultiplier { get => D.x.curveMultiplier; set => D.x.curveMultiplier = value; }
            public MinMaxCurve y { get => D.y.Clone(); set => D.y = value.Clone(); }
            public float yMultiplier { get => D.y.curveMultiplier; set => D.y.curveMultiplier = value; }
            public MinMaxCurve z { get => D.z.Clone(); set => D.z = value.Clone(); }
            public float zMultiplier { get => D.z.curveMultiplier; set => D.z.curveMultiplier = value; }
            public bool separateAxes { get => D.separateAxes; set => D.separateAxes = value; }
            public Vector2 range { get => D.range; set => D.range = value; }
        }

        public partial struct ExternalForcesModule
        {
            internal ParticleSystem m_ParticleSystem;
            internal ExternalForcesModule(ParticleSystem particleSystem) { m_ParticleSystem = particleSystem; }
            ExternalForcesData D => m_ParticleSystem.m_ExternalForces;

            public bool enabled { get => D.enabled; set => D.enabled = value; }
            public float multiplier { get => D.multiplier; set => D.multiplier = value; }
            public MinMaxCurve multiplierCurve { get => D.multiplierCurve.Clone(); set => D.multiplierCurve = value.Clone(); }
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
            public MinMaxCurve strength { get => D.strengthX.Clone(); set => D.strengthX = value.Clone(); }
            public float strengthMultiplier { get => D.strengthX.curveMultiplier; set => D.strengthX.curveMultiplier = value; }
            public MinMaxCurve strengthX { get => D.strengthX.Clone(); set => D.strengthX = value.Clone(); }
            public float strengthXMultiplier { get => D.strengthX.curveMultiplier; set => D.strengthX.curveMultiplier = value; }
            public MinMaxCurve strengthY { get => D.strengthY.Clone(); set => D.strengthY = value.Clone(); }
            public float strengthYMultiplier { get => D.strengthY.curveMultiplier; set => D.strengthY.curveMultiplier = value; }
            public MinMaxCurve strengthZ { get => D.strengthZ.Clone(); set => D.strengthZ = value.Clone(); }
            public float strengthZMultiplier { get => D.strengthZ.curveMultiplier; set => D.strengthZ.curveMultiplier = value; }
            public float frequency { get => D.frequency; set => D.frequency = value; }
            public bool damping { get => D.damping; set => D.damping = value; }
            public int octaveCount { get => D.octaveCount; set => D.octaveCount = Mathf.Clamp(value, 1, 4); }
            public float octaveMultiplier { get => D.octaveMultiplier; set => D.octaveMultiplier = value; }
            public float octaveScale { get => D.octaveScale; set => D.octaveScale = value; }
            public ParticleSystemNoiseQuality quality { get => D.quality; set => D.quality = value; }
            public MinMaxCurve scrollSpeed { get => D.scrollSpeed.Clone(); set => D.scrollSpeed = value.Clone(); }
            public float scrollSpeedMultiplier { get => D.scrollSpeed.curveMultiplier; set => D.scrollSpeed.curveMultiplier = value; }
            public bool remapEnabled { get => D.remapEnabled; set => D.remapEnabled = value; }
            public MinMaxCurve remap { get => D.remapX.Clone(); set => D.remapX = value.Clone(); }
            public float remapMultiplier { get => D.remapX.curveMultiplier; set => D.remapX.curveMultiplier = value; }
            public MinMaxCurve remapX { get => D.remapX.Clone(); set => D.remapX = value.Clone(); }
            public float remapXMultiplier { get => D.remapX.curveMultiplier; set => D.remapX.curveMultiplier = value; }
            public MinMaxCurve remapY { get => D.remapY.Clone(); set => D.remapY = value.Clone(); }
            public float remapYMultiplier { get => D.remapY.curveMultiplier; set => D.remapY.curveMultiplier = value; }
            public MinMaxCurve remapZ { get => D.remapZ.Clone(); set => D.remapZ = value.Clone(); }
            public float remapZMultiplier { get => D.remapZ.curveMultiplier; set => D.remapZ.curveMultiplier = value; }
            public MinMaxCurve positionAmount { get => D.positionAmount.Clone(); set => D.positionAmount = value.Clone(); }
            public MinMaxCurve rotationAmount { get => D.rotationAmount.Clone(); set => D.rotationAmount = value.Clone(); }
            public MinMaxCurve sizeAmount { get => D.sizeAmount.Clone(); set => D.sizeAmount = value.Clone(); }
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
            public MinMaxCurve dampen { get => D.dampen.Clone(); set => D.dampen = value.Clone(); }
            public float dampenMultiplier { get => D.dampen.curveMultiplier; set => D.dampen.curveMultiplier = value; }
            public MinMaxCurve bounce { get => D.bounce.Clone(); set => D.bounce = value.Clone(); }
            public float bounceMultiplier { get => D.bounce.curveMultiplier; set => D.bounce.curveMultiplier = value; }
            public MinMaxCurve lifetimeLoss { get => D.lifetimeLoss.Clone(); set => D.lifetimeLoss = value.Clone(); }
            public float lifetimeLossMultiplier { get => D.lifetimeLoss.curveMultiplier; set => D.lifetimeLoss.curveMultiplier = value; }
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
            public MinMaxCurve frameOverTime { get => D.frameOverTime.Clone(); set => D.frameOverTime = value.Clone(); }
            public float frameOverTimeMultiplier { get => D.frameOverTime.curveMultiplier; set => D.frameOverTime.curveMultiplier = value; }
            public MinMaxCurve startFrame { get => D.startFrame.Clone(); set => D.startFrame = value.Clone(); }
            public float startFrameMultiplier { get => D.startFrame.curveMultiplier; set => D.startFrame.curveMultiplier = value; }
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
            public MinMaxCurve range { get => D.range.Clone(); set => D.range = value.Clone(); }
            public float rangeMultiplier { get => D.range.curveMultiplier; set => D.range.curveMultiplier = value; }
            public MinMaxCurve intensity { get => D.intensity.Clone(); set => D.intensity = value.Clone(); }
            public float intensityMultiplier { get => D.intensity.curveMultiplier; set => D.intensity.curveMultiplier = value; }
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
            public MinMaxCurve lifetime { get => D.lifetime.Clone(); set => D.lifetime = value.Clone(); }
            public float lifetimeMultiplier { get => D.lifetime.curveMultiplier; set => D.lifetime.curveMultiplier = value; }
            public float minVertexDistance { get => D.minVertexDistance; set => D.minVertexDistance = value; }
            public ParticleSystemTrailTextureMode textureMode { get => D.textureMode; set => D.textureMode = value; }
            public Vector2 textureScale { get => D.textureScale; set => D.textureScale = value; }
            public bool worldSpace { get => D.worldSpace; set => D.worldSpace = value; }
            public bool dieWithParticles { get => D.dieWithParticles; set => D.dieWithParticles = value; }
            public bool sizeAffectsWidth { get => D.sizeAffectsWidth; set => D.sizeAffectsWidth = value; }
            public bool sizeAffectsLifetime { get => D.sizeAffectsLifetime; set => D.sizeAffectsLifetime = value; }
            public bool inheritParticleColor { get => D.inheritParticleColor; set => D.inheritParticleColor = value; }
            public MinMaxGradient colorOverLifetime { get => D.colorOverLifetime.Clone(); set => D.colorOverLifetime = value.Clone(); }
            public MinMaxCurve widthOverTrail { get => D.widthOverTrail.Clone(); set => D.widthOverTrail = value.Clone(); }
            public float widthOverTrailMultiplier { get => D.widthOverTrail.curveMultiplier; set => D.widthOverTrail.curveMultiplier = value; }
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
            public void SetVector(ParticleSystemCustomData stream, int component, MinMaxCurve curve) => D.vectors[(int)stream, component] = curve.Clone();
            public MinMaxCurve GetVector(ParticleSystemCustomData stream, int component) => D.vectors[(int)stream, component].Clone();
            public void SetColor(ParticleSystemCustomData stream, MinMaxGradient gradient) => D.colors[(int)stream] = gradient.Clone();
            public MinMaxGradient GetColor(ParticleSystemCustomData stream) => D.colors[(int)stream].Clone();
        }
    }
}
