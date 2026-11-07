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

        static ModuleCurve C(float value, CurveRange range = CurveRange.Any) => ModuleCurve.Constant(value, range);

        static ModuleCurve Linear(float multiplier, float from, float to, CurveRange range = CurveRange.Any) =>
            ModuleCurve.Curve(multiplier, AnimationCurve.Linear(0f, from, 1f, to), range);

        internal sealed class MainData : ModuleData
        {
            internal float duration = 5f;
            internal bool loop = true;
            internal bool prewarm;
            internal ModuleCurve startDelay = C(0f, CurveRange.NonNegative);
            internal ModuleCurve startLifetime = C(5f, CurveRange.StartLifetime);
            internal ModuleCurve startSpeed = C(5f);
            internal bool startSize3D;
            internal ModuleCurve startSizeX = C(1f, CurveRange.NonNegative);
            internal ModuleCurve startSizeY = C(1f, CurveRange.NonNegative);
            internal ModuleCurve startSizeZ = C(1f, CurveRange.NonNegative);
            internal bool startRotation3D;
            internal ModuleCurve startRotationX = C(0f);
            internal ModuleCurve startRotationY = C(0f);
            internal ModuleCurve startRotationZ = C(0f);
            internal float flipRotation;
            internal MinMaxGradient startColor = Color.white;
            internal ParticleSystemGravitySource gravitySource;
            internal ModuleCurve gravityModifier = C(0f);
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
            internal ModuleCurve rateOverTime = C(10f, CurveRange.NonNegative);
            internal ModuleCurve rateOverDistance = C(0f, CurveRange.NonNegative);
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
            internal ModuleCurve radiusSpeed = C(1f);
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
            internal ModuleCurve meshSpawnSpeed = C(1f);
            internal float arc = 360f;
            internal ParticleSystemShapeMultiModeValue arcMode;
            internal float arcSpread;
            internal ModuleCurve arcSpeed = C(1f);
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
            internal ModuleCurve x = C(0f), y = C(0f), z = C(0f);
            internal ModuleCurve orbitalX = C(0f), orbitalY = C(0f), orbitalZ = C(0f);
            internal ModuleCurve orbitalOffsetX = C(0f), orbitalOffsetY = C(0f), orbitalOffsetZ = C(0f);
            internal ModuleCurve radial = C(0f);
            internal ModuleCurve speedModifier = C(1f);
            internal ParticleSystemSimulationSpace space;
        }

        internal sealed class LimitVelocityData : ModuleData
        {
            internal bool enabled;
            internal ModuleCurve limitX = C(1f, CurveRange.NonNegative), limitY = C(1f, CurveRange.NonNegative), limitZ = C(1f, CurveRange.NonNegative);
            internal ModuleCurve limit = C(1f, CurveRange.NonNegative);
            internal float dampen = 1f;
            internal bool separateAxes;
            internal ParticleSystemSimulationSpace space;
            internal ModuleCurve drag = C(0f, CurveRange.NonNegative);
            internal bool multiplyDragByParticleSize = true;
            internal bool multiplyDragByParticleVelocity = true;
        }

        internal sealed class InheritVelocityData : ModuleData
        {
            internal bool enabled;
            internal ParticleSystemInheritVelocityMode mode;
            internal ModuleCurve curve = C(0f);
        }

        internal sealed class LifetimeByEmitterSpeedData : ModuleData
        {
            internal bool enabled;
            internal ModuleCurve curve = Linear(1f, 1f, 0.2f);
            internal Vector2 range = new Vector2(0f, 1f);
        }

        internal sealed class ForceData : ModuleData
        {
            internal bool enabled;
            internal ModuleCurve x = C(0f), y = C(0f), z = C(0f);
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
            internal ModuleCurve x = Linear(1f, 0f, 1f), y = Linear(1f, 0f, 1f), z = Linear(1f, 0f, 1f);
        }

        internal sealed class SizeBySpeedData : ModuleData
        {
            internal bool enabled;
            internal bool separateAxes;
            internal ModuleCurve x = Linear(1f, 0f, 1f), y = Linear(1f, 0f, 1f), z = Linear(1f, 0f, 1f);
            internal Vector2 range = new Vector2(0f, 1f);
        }

        internal sealed class RotationData : ModuleData
        {
            internal bool enabled;
            internal bool separateAxes;
            internal ModuleCurve x = C(0f), y = C(0f), z = C(45f * Mathf.Deg2Rad);
        }

        internal sealed class RotationBySpeedData : ModuleData
        {
            internal bool enabled;
            internal bool separateAxes;
            internal ModuleCurve x = C(0f), y = C(0f), z = C(45f * Mathf.Deg2Rad);
            internal Vector2 range = new Vector2(0f, 1f);
        }

        internal sealed class ExternalForcesData : ModuleData
        {
            internal bool enabled;
            // `multiplier` is this curve's stored scalar (6000.0.71f1, particlecurves).
            internal ModuleCurve multiplierCurve = C(1f, CurveRange.NonNegative);
            internal ParticleSystemGameObjectFilter influenceFilter;
            internal LayerMask influenceMask = -1;
            internal List<ParticleSystemForceField> influences = new List<ParticleSystemForceField>();
        }

        internal sealed class NoiseData : ModuleData
        {
            internal bool enabled;
            internal bool separateAxes;
            internal ModuleCurve strengthX = C(1f), strengthY = C(1f), strengthZ = C(1f);
            internal float frequency = 0.5f;
            internal bool damping = true;
            internal int octaveCount = 1;
            internal float octaveMultiplier = 0.5f;
            internal float octaveScale = 2f;
            internal ParticleSystemNoiseQuality quality = ParticleSystemNoiseQuality.High;
            internal ModuleCurve scrollSpeed = C(0f);
            internal bool remapEnabled;
            internal ModuleCurve remapX = Linear(1f, -1f, 1f);
            internal ModuleCurve remapY = Linear(1f, -1f, 1f);
            internal ModuleCurve remapZ = Linear(1f, -1f, 1f);
            internal ModuleCurve positionAmount = C(1f);
            internal ModuleCurve rotationAmount = C(0f);
            internal ModuleCurve sizeAmount = C(0f);
        }

        internal sealed class CollisionData : ModuleData
        {
            internal bool enabled;
            internal ParticleSystemCollisionType type;
            internal ParticleSystemCollisionMode mode;
            internal ModuleCurve dampen = C(0f, CurveRange.Unit);
            internal ModuleCurve bounce = C(1f, CurveRange.Bounce);
            internal ModuleCurve lifetimeLoss = C(0f, CurveRange.Unit);
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
            internal ModuleCurve frameOverTime = Linear(0.9999f, 0f, 1f, CurveRange.Frame);
            internal ModuleCurve startFrame = C(0f, CurveRange.Frame);
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
            internal ModuleCurve range = C(1f, CurveRange.NonNegative);
            internal ModuleCurve intensity = C(1f, CurveRange.NonNegative);
            internal int maxLights = 20;
        }

        internal sealed class TrailData : ModuleData
        {
            internal bool enabled;
            internal ParticleSystemTrailMode mode;
            internal float ratio = 1f;
            internal ModuleCurve lifetime = C(1f, CurveRange.Unit);
            internal float minVertexDistance = 0.2f;
            internal ParticleSystemTrailTextureMode textureMode;
            internal Vector2 textureScale = Vector2.one;
            internal bool worldSpace;
            internal bool dieWithParticles = true;
            internal bool sizeAffectsWidth = true;
            internal bool sizeAffectsLifetime;
            internal bool inheritParticleColor = true;
            internal MinMaxGradient colorOverLifetime = Color.white;
            internal ModuleCurve widthOverTrail = C(1f, CurveRange.NonNegative);
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
            internal ModuleCurve[,] vectors = new ModuleCurve[2, 4];
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
