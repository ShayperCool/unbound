using System;
using System.Collections.Generic;

namespace UnityEngine
{
    // The state behind each module. A module struct (`ps.main`, `ps.emission`, ...) is a handle that
    // reads and writes these. Defaults are those of a newly added Particle System.
    public sealed partial class ParticleSystem
    {
        /// <summary>Module state; copied shallowly for Instantiate.</summary>
        internal abstract class ModuleData
        {
            internal object MemberwiseCopy() => MemberwiseClone();
        }

        static MinMaxCurve Linear01(float multiplier) => new MinMaxCurve(multiplier, AnimationCurve.Linear(0f, 0f, 1f, 1f));

        internal sealed class MainData : ModuleData
        {
            internal float duration = 5f;
            internal bool loop = true;
            internal bool prewarm;
            internal MinMaxCurve startDelay = 0f;
            internal MinMaxCurve startLifetime = 5f;
            internal MinMaxCurve startSpeed = 5f;
            internal bool startSize3D;
            internal MinMaxCurve startSizeX = 1f;
            internal MinMaxCurve startSizeY = 1f;
            internal MinMaxCurve startSizeZ = 1f;
            internal bool startRotation3D;
            internal MinMaxCurve startRotationX = 0f;
            internal MinMaxCurve startRotationY = 0f;
            internal MinMaxCurve startRotationZ = 0f;
            internal float flipRotation;
            internal MinMaxGradient startColor = Color.white;
            internal ParticleSystemGravitySource gravitySource;
            internal MinMaxCurve gravityModifier = 0f;
            internal ParticleSystemSimulationSpace simulationSpace;
            internal Transform customSimulationSpace;
            internal float simulationSpeed = 1f;
            internal bool useUnscaledTime;
            internal ParticleSystemScalingMode scalingMode = ParticleSystemScalingMode.Local;
            internal bool playOnAwake = true;
            internal int maxParticles = 1000;
            internal ParticleSystemEmitterVelocityMode emitterVelocityMode;
            internal Vector3 customEmitterVelocity;
            internal ParticleSystemStopAction stopAction;
            internal ParticleSystemRingBufferMode ringBufferMode;
            internal Vector2 ringBufferLoopRange = new Vector2(0f, 1f);
            internal ParticleSystemCullingMode cullingMode;
            internal bool autoRandomSeed = true;
            internal uint randomSeed;
        }

        internal sealed class EmissionData : ModuleData
        {
            internal bool enabled = true;
            internal MinMaxCurve rateOverTime = 10f;
            internal MinMaxCurve rateOverDistance = 0f;
            internal List<Burst> bursts = new List<Burst>();
        }

        internal sealed class ShapeData : ModuleData
        {
            internal bool enabled = true;
            internal ParticleSystemShapeType shapeType = ParticleSystemShapeType.Cone;
            internal float randomDirectionAmount;
            internal float sphericalDirectionAmount;
            internal float randomPositionAmount;
            internal bool alignToDirection;
            internal float radius = 1f;
            internal ParticleSystemShapeMultiModeValue radiusMode;
            internal float radiusSpread;
            internal MinMaxCurve radiusSpeed = 1f;
            internal float radiusThickness = 1f;
            internal float angle = 25f;
            internal float length = 5f;
            internal Vector3 boxThickness;
            internal ParticleSystemMeshShapeType meshShapeType;
            internal Mesh mesh;
            internal MeshRenderer meshRenderer;
            internal Renderer skinnedMeshRenderer;
            internal Sprite sprite;
            internal SpriteRenderer spriteRenderer;
            internal bool useMeshMaterialIndex;
            internal int meshMaterialIndex;
            internal bool useMeshColors = true;
            internal float normalOffset;
            internal ParticleSystemShapeMultiModeValue meshSpawnMode;
            internal float meshSpawnSpread;
            internal MinMaxCurve meshSpawnSpeed = 1f;
            internal float arc = 360f;
            internal ParticleSystemShapeMultiModeValue arcMode;
            internal float arcSpread;
            internal MinMaxCurve arcSpeed = 1f;
            internal float donutRadius = 0.2f;
            internal Vector3 position;
            internal Vector3 rotation;
            internal Vector3 scale = Vector3.one;
            internal Texture2D texture;
            internal ParticleSystemShapeTextureChannel textureClipChannel = ParticleSystemShapeTextureChannel.Alpha;
            internal float textureClipThreshold;
            internal bool textureColorAffectsParticles = true;
            internal bool textureAlphaAffectsParticles = true;
            internal bool textureBilinearFiltering;
            internal int textureUVChannel;
        }

        internal sealed class VelocityData : ModuleData
        {
            internal bool enabled;
            internal MinMaxCurve x = 0f, y = 0f, z = 0f;
            internal MinMaxCurve orbitalX = 0f, orbitalY = 0f, orbitalZ = 0f;
            internal MinMaxCurve orbitalOffsetX = 0f, orbitalOffsetY = 0f, orbitalOffsetZ = 0f;
            internal MinMaxCurve radial = 0f;
            internal MinMaxCurve speedModifier = 1f;
            internal ParticleSystemSimulationSpace space;
        }

        internal sealed class LimitVelocityData : ModuleData
        {
            internal bool enabled;
            internal MinMaxCurve limitX = 1f, limitY = 1f, limitZ = 1f;
            internal MinMaxCurve limit = 1f;
            internal float dampen = 1f;
            internal bool separateAxes;
            internal ParticleSystemSimulationSpace space;
            internal MinMaxCurve drag = 0f;
            internal bool multiplyDragByParticleSize = true;
            internal bool multiplyDragByParticleVelocity = true;
        }

        internal sealed class InheritVelocityData : ModuleData
        {
            internal bool enabled;
            internal ParticleSystemInheritVelocityMode mode;
            internal MinMaxCurve curve = 0f;
        }

        internal sealed class LifetimeByEmitterSpeedData : ModuleData
        {
            internal bool enabled;
            internal MinMaxCurve curve = 1f;
            internal Vector2 range = new Vector2(0f, 1f);
        }

        internal sealed class ForceData : ModuleData
        {
            internal bool enabled;
            internal MinMaxCurve x = 0f, y = 0f, z = 0f;
            internal ParticleSystemSimulationSpace space;
            internal bool randomized;
        }

        internal sealed class ColorData : ModuleData
        {
            internal bool enabled;
            internal MinMaxGradient color = new MinMaxGradient(new Gradient());
        }

        internal sealed class ColorBySpeedData : ModuleData
        {
            internal bool enabled;
            internal MinMaxGradient color = new MinMaxGradient(new Gradient());
            internal Vector2 range = new Vector2(0f, 1f);
        }

        internal sealed class SizeData : ModuleData
        {
            internal bool enabled;
            internal bool separateAxes;
            internal MinMaxCurve x = Linear01(1f), y = Linear01(1f), z = Linear01(1f);
        }

        internal sealed class SizeBySpeedData : ModuleData
        {
            internal bool enabled;
            internal bool separateAxes;
            internal MinMaxCurve x = Linear01(1f), y = Linear01(1f), z = Linear01(1f);
            internal Vector2 range = new Vector2(0f, 1f);
        }

        internal sealed class RotationData : ModuleData
        {
            internal bool enabled;
            internal bool separateAxes;
            internal MinMaxCurve x = 0f, y = 0f, z = 45f * Mathf.Deg2Rad;
        }

        internal sealed class RotationBySpeedData : ModuleData
        {
            internal bool enabled;
            internal bool separateAxes;
            internal MinMaxCurve x = 0f, y = 0f, z = 45f * Mathf.Deg2Rad;
            internal Vector2 range = new Vector2(0f, 1f);
        }

        internal sealed class ExternalForcesData : ModuleData
        {
            internal bool enabled;
            internal float multiplier = 1f;
            internal MinMaxCurve multiplierCurve = 1f;
            internal ParticleSystemGameObjectFilter influenceFilter;
            internal LayerMask influenceMask = -1;
            internal List<ParticleSystemForceField> influences = new List<ParticleSystemForceField>();
        }

        internal sealed class NoiseData : ModuleData
        {
            internal bool enabled;
            internal bool separateAxes;
            internal MinMaxCurve strengthX = 1f, strengthY = 1f, strengthZ = 1f;
            internal float frequency = 0.5f;
            internal bool damping = true;
            internal int octaveCount = 1;
            internal float octaveMultiplier = 0.5f;
            internal float octaveScale = 2f;
            internal ParticleSystemNoiseQuality quality = ParticleSystemNoiseQuality.High;
            internal MinMaxCurve scrollSpeed = 0f;
            internal bool remapEnabled;
            internal MinMaxCurve remapX = new MinMaxCurve(1f, AnimationCurve.Linear(0f, -1f, 1f, 1f));
            internal MinMaxCurve remapY = new MinMaxCurve(1f, AnimationCurve.Linear(0f, -1f, 1f, 1f));
            internal MinMaxCurve remapZ = new MinMaxCurve(1f, AnimationCurve.Linear(0f, -1f, 1f, 1f));
            internal MinMaxCurve positionAmount = 1f;
            internal MinMaxCurve rotationAmount = 0f;
            internal MinMaxCurve sizeAmount = 0f;
        }

        internal sealed class CollisionData : ModuleData
        {
            internal bool enabled;
            internal ParticleSystemCollisionType type;
            internal ParticleSystemCollisionMode mode;
            internal MinMaxCurve dampen = 0f;
            internal MinMaxCurve bounce = 1f;
            internal MinMaxCurve lifetimeLoss = 0f;
            internal float minKillSpeed;
            internal float maxKillSpeed = 10000f;
            internal LayerMask collidesWith = -1;
            internal bool enableDynamicColliders = true;
            internal int maxCollisionShapes = 256;
            internal ParticleSystemCollisionQuality quality;
            internal float voxelSize = 0.5f;
            internal float radiusScale = 1f;
            internal bool sendCollisionMessages;
            internal float colliderForce;
            internal bool multiplyColliderForceByCollisionAngle = true;
            internal bool multiplyColliderForceByParticleSpeed;
            internal bool multiplyColliderForceByParticleSize;
            internal bool enableInteriorCollisions = true;
            internal List<Transform> planes = new List<Transform>();
        }

        internal sealed class TriggerData : ModuleData
        {
            internal bool enabled;
            internal ParticleSystemOverlapAction inside = ParticleSystemOverlapAction.Kill;
            internal ParticleSystemOverlapAction outside;
            internal ParticleSystemOverlapAction enter;
            internal ParticleSystemOverlapAction exit;
            internal ParticleSystemColliderQueryMode colliderQueryMode;
            internal float radiusScale = 1f;
            internal List<Component> colliders = new List<Component>();
        }

        internal struct SubEmitter
        {
            internal ParticleSystem system;
            internal ParticleSystemSubEmitterType type;
            internal ParticleSystemSubEmitterProperties properties;
            internal float emitProbability;
        }

        internal sealed class SubEmittersData : ModuleData
        {
            internal bool enabled;
            internal List<SubEmitter> emitters = new List<SubEmitter>();
        }

        internal sealed class TextureSheetData : ModuleData
        {
            internal bool enabled;
            internal ParticleSystemAnimationMode mode;
            internal ParticleSystemAnimationTimeMode timeMode;
            internal float fps = 30f;
            internal int numTilesX = 1;
            internal int numTilesY = 1;
            internal ParticleSystemAnimationType animation;
            internal ParticleSystemAnimationRowMode rowMode;
            internal MinMaxCurve frameOverTime = Linear01(0.9999f);
            internal MinMaxCurve startFrame = 0f;
            internal int cycleCount = 1;
            internal int rowIndex;
            internal Rendering.UVChannelFlags uvChannelMask = (Rendering.UVChannelFlags)(-1);
            internal Vector2 speedRange = new Vector2(0f, 1f);
            internal float flipU;
            internal float flipV;
            internal List<Sprite> sprites = new List<Sprite>();
        }

        internal sealed class LightsData : ModuleData
        {
            internal bool enabled;
            internal float ratio;
            internal bool useRandomDistribution = true;
            internal Object light;
            internal bool useParticleColor = true;
            internal bool sizeAffectsRange = true;
            internal bool alphaAffectsIntensity = true;
            internal MinMaxCurve range = 1f;
            internal MinMaxCurve intensity = 1f;
            internal int maxLights = 20;
        }

        internal sealed class TrailData : ModuleData
        {
            internal bool enabled;
            internal ParticleSystemTrailMode mode;
            internal float ratio = 1f;
            internal MinMaxCurve lifetime = 1f;
            internal float minVertexDistance = 0.2f;
            internal ParticleSystemTrailTextureMode textureMode;
            internal Vector2 textureScale = Vector2.one;
            internal bool worldSpace;
            internal bool dieWithParticles = true;
            internal bool sizeAffectsWidth = true;
            internal bool sizeAffectsLifetime;
            internal bool inheritParticleColor = true;
            internal MinMaxGradient colorOverLifetime = Color.white;
            internal MinMaxCurve widthOverTrail = 1f;
            internal MinMaxGradient colorOverTrail = Color.white;
            internal bool generateLightingData;
            internal int ribbonCount = 1;
            internal float shadowBias = 0.5f;
            internal bool splitSubEmitterRibbons;
            internal bool attachRibbonsToTransform;
        }

        internal sealed class CustomDataData : ModuleData
        {
            internal bool enabled;
            internal ParticleSystemCustomDataMode[] modes = new ParticleSystemCustomDataMode[2];
            internal int[] componentCounts = { 4, 4 };
            internal MinMaxCurve[,] vectors = new MinMaxCurve[2, 4];
            internal MinMaxGradient[] colors = { Color.white, Color.white };
        }

        internal MainData m_Main = new MainData();
        internal EmissionData m_Emission = new EmissionData();
        internal ShapeData m_Shape = new ShapeData();
        internal VelocityData m_Velocity = new VelocityData();
        internal LimitVelocityData m_LimitVelocity = new LimitVelocityData();
        internal InheritVelocityData m_InheritVelocity = new InheritVelocityData();
        internal LifetimeByEmitterSpeedData m_LifetimeByEmitterSpeed = new LifetimeByEmitterSpeedData();
        internal ForceData m_Force = new ForceData();
        internal ColorData m_Color = new ColorData();
        internal ColorBySpeedData m_ColorBySpeed = new ColorBySpeedData();
        internal SizeData m_Size = new SizeData();
        internal SizeBySpeedData m_SizeBySpeed = new SizeBySpeedData();
        internal RotationData m_Rotation = new RotationData();
        internal RotationBySpeedData m_RotationBySpeed = new RotationBySpeedData();
        internal ExternalForcesData m_ExternalForces = new ExternalForcesData();
        internal NoiseData m_Noise = new NoiseData();
        internal CollisionData m_Collision = new CollisionData();
        internal TriggerData m_Trigger = new TriggerData();
        internal SubEmittersData m_SubEmitters = new SubEmittersData();
        internal TextureSheetData m_TextureSheet = new TextureSheetData();
        internal LightsData m_Lights = new LightsData();
        internal TrailData m_Trails = new TrailData();
        internal CustomDataData m_CustomData = new CustomDataData();
    }
}
