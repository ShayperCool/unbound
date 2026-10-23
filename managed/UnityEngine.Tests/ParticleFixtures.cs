namespace UnityEngine.Tests
{
    /// <summary>A particle system document of the test project (the test game,
    /// "coins"), trimmed: the disabled modules and the
    /// curves no reader looks at (those of constant values) are left out.</summary>
    internal static class ParticleFixtures
    {
        internal const string CoinsSystem = """
--- !u!198 &198114385889436340
ParticleSystem:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 1170650771564200}
  serializedVersion: 8
  lengthInSec: 1
  simulationSpeed: 1
  stopAction: 0
  cullingMode: 3
  ringBufferMode: 0
  ringBufferLoopRange: {x: 0, y: 1}
  emitterVelocityMode: 0
  looping: 0
  prewarm: 0
  playOnAwake: 1
  useUnscaledTime: 0
  autoRandomSeed: 1
  startDelay:
    serializedVersion: 2
    minMaxState: 0
    scalar: 0
    minScalar: 0
  moveWithTransform: 0
  moveWithCustomTransform: {fileID: 0}
  scalingMode: 0
  randomSeed: 0
  InitialModule:
    serializedVersion: 3
    enabled: 1
    startLifetime:
      serializedVersion: 2
      minMaxState: 3
      scalar: 0.45
      minScalar: 0.35
    startSpeed:
      serializedVersion: 2
      minMaxState: 3
      scalar: 1500
      minScalar: 950
    startColor:
      serializedVersion: 2
      minMaxState: 0
      minColor: {r: 0.654902, g: 0.87058824, b: 0.8392157, a: 1}
      maxColor: {r: 1, g: 1, b: 1, a: 1}
    startSize:
      serializedVersion: 2
      minMaxState: 3
      scalar: 30
      minScalar: 20
    startSizeY:
      serializedVersion: 2
      minMaxState: 3
      scalar: 1
      minScalar: 0
    startSizeZ:
      serializedVersion: 2
      minMaxState: 3
      scalar: 1
      minScalar: 0
    startRotationX:
      serializedVersion: 2
      minMaxState: 3
      scalar: 0
      minScalar: 0
    startRotationY:
      serializedVersion: 2
      minMaxState: 3
      scalar: 0
      minScalar: 0
    startRotation:
      serializedVersion: 2
      minMaxState: 3
      scalar: 6.283185
      minScalar: 0
    randomizeRotationDirection: 0.5
    maxNumParticles: 200
    customEmitterVelocity: {x: 0, y: 0, z: 0}
    size3D: 0
    rotation3D: 0
    gravityModifier:
      serializedVersion: 2
      minMaxState: 0
      scalar: 2
      minScalar: 0
  ShapeModule:
    serializedVersion: 6
    enabled: 1
    type: 10
    angle: 25
    length: 5
    boxThickness: {x: 0, y: 0, z: 0}
    radiusThickness: 1
    donutRadius: 0.2
    m_Position: {x: 0, y: 0, z: 0}
    m_Rotation: {x: 0, y: 0, z: 0}
    m_Scale: {x: 1, y: 1, z: 1}
    placementMode: 0
    m_MeshMaterialIndex: 0
    m_MeshNormalOffset: 0
    m_MeshSpawn:
      mode: 0
      spread: 0
      speed:
        serializedVersion: 2
        minMaxState: 0
        scalar: 1
        minScalar: 1
    m_Mesh: {fileID: 0}
    m_MeshRenderer: {fileID: 0}
    m_SkinnedMeshRenderer: {fileID: 0}
    m_Sprite: {fileID: 0}
    m_SpriteRenderer: {fileID: 0}
    m_UseMeshMaterialIndex: 0
    m_UseMeshColors: 1
    alignToDirection: 0
    m_Texture: {fileID: 0}
    m_TextureClipChannel: 3
    m_TextureClipThreshold: 0
    m_TextureUVChannel: 0
    m_TextureColorAffectsParticles: 1
    m_TextureAlphaAffectsParticles: 1
    m_TextureBilinearFiltering: 0
    randomDirectionAmount: 0
    sphericalDirectionAmount: 0
    randomPositionAmount: 0
    radius:
      value: 40
      mode: 0
      spread: 0
      speed:
        serializedVersion: 2
        minMaxState: 0
        scalar: 1
        minScalar: 0
    arc:
      value: 90
      mode: 0
      spread: 0
      speed:
        serializedVersion: 2
        minMaxState: 0
        scalar: 1
        minScalar: 0
  EmissionModule:
    enabled: 1
    serializedVersion: 4
    rateOverTime:
      serializedVersion: 2
      minMaxState: 0
      scalar: 0
      minScalar: 0
    rateOverDistance:
      serializedVersion: 2
      minMaxState: 0
      scalar: 0
      minScalar: 0
    m_BurstCount: 1
    m_Bursts:
    - serializedVersion: 2
      time: 0
      countCurve:
        serializedVersion: 2
        minMaxState: 3
        scalar: 10
        minScalar: 8
      cycleCount: 1
      repeatInterval: 0.01
      probability: 1
  SizeModule:
    enabled: 1
    curve:
      serializedVersion: 2
      minMaxState: 1
      scalar: 1
      minScalar: 0
      maxCurve:
        serializedVersion: 2
        m_Curve:
        - serializedVersion: 3
          time: 0
          value: 0
          inSlope: 13.79048
          outSlope: 13.79048
          tangentMode: 0
          weightedMode: 0
          inWeight: 0.33333334
          outWeight: 0.33333334
        - serializedVersion: 3
          time: 0.15637419
          value: 1
          inSlope: 0
          outSlope: 0
          tangentMode: 0
          weightedMode: 0
          inWeight: 0.33333334
          outWeight: 0.33333334
        - serializedVersion: 3
          time: 0.8397762
          value: 0.99126625
          inSlope: 0
          outSlope: 0
          tangentMode: 0
          weightedMode: 0
          inWeight: 0.33333334
          outWeight: 0.33333334
        - serializedVersion: 3
          time: 1
          value: 0
          inSlope: -11.116922
          outSlope: -11.116922
          tangentMode: 0
          weightedMode: 0
          inWeight: 0.33333334
          outWeight: 0.33333334
        m_PreInfinity: 2
        m_PostInfinity: 2
        m_RotationOrder: 0
      minCurve:
        serializedVersion: 2
        m_Curve:
        - serializedVersion: 3
          time: 0
          value: 0
          inSlope: 0
          outSlope: 0
          tangentMode: 0
          weightedMode: 0
          inWeight: 0.33333334
          outWeight: 0.33333334
        - serializedVersion: 3
          time: 1
          value: 0
          inSlope: 0
          outSlope: 0
          tangentMode: 0
          weightedMode: 0
          inWeight: 0.33333334
          outWeight: 0.33333334
        m_PreInfinity: 2
        m_PostInfinity: 2
        m_RotationOrder: 4
    separateAxes: 0
  RotationModule:
    enabled: 1
    x:
      serializedVersion: 2
      minMaxState: 0
      scalar: 0
      minScalar: 0
    y:
      serializedVersion: 2
      minMaxState: 0
      scalar: 0
      minScalar: 0
    curve:
      serializedVersion: 2
      minMaxState: 0
      scalar: 10.471975
      minScalar: 3.1415925
    separateAxes: 0
  ColorModule:
    enabled: 1
    gradient:
      serializedVersion: 2
      minMaxState: 1
      minColor: {r: 1, g: 1, b: 1, a: 1}
      maxColor: {r: 1, g: 1, b: 1, a: 1}
      maxGradient:
        serializedVersion: 2
        key0: {r: 1, g: 1, b: 1, a: 1}
        key1: {r: 1, g: 1, b: 1, a: 1}
        key2: {r: 1, g: 0.91764706, b: 0, a: 0}
        ctime0: 0
        ctime1: 65535
        ctime2: 43038
        atime0: 3277
        atime1: 52428
        atime2: 65535
        m_Mode: 0
        m_NumColorKeys: 2
        m_NumAlphaKeys: 3
      minGradient:
        serializedVersion: 2
        key0: {r: 1, g: 1, b: 1, a: 1}
        key1: {r: 1, g: 1, b: 1, a: 1}
        ctime0: 0
        ctime1: 65535
        atime0: 0
        atime1: 65535
        m_Mode: 0
        m_NumColorKeys: 2
        m_NumAlphaKeys: 2
  ClampVelocityModule:
    enabled: 1
    x:
      serializedVersion: 2
      minMaxState: 0
      scalar: 1
      minScalar: 0
    y:
      serializedVersion: 2
      minMaxState: 0
      scalar: 1
      minScalar: 0
    z:
      serializedVersion: 2
      minMaxState: 0
      scalar: 1
      minScalar: 0
    magnitude:
      serializedVersion: 2
      minMaxState: 0
      scalar: 1
      minScalar: 0
    separateAxis: 0
    inWorldSpace: 0
    multiplyDragByParticleSize: 1
    multiplyDragByParticleVelocity: 1
    dampen: 0.15
    drag:
      serializedVersion: 2
      minMaxState: 0
      scalar: 0
      minScalar: 0
""";

        internal const string CoinsRenderer = """
--- !u!199 &199288045231649594
ParticleSystemRenderer:
  serializedVersion: 6
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 1170650771564200}
  m_Enabled: 0
  m_CastShadows: 0
  m_ReceiveShadows: 0
  m_DynamicOccludee: 1
  m_StaticShadowCaster: 0
  m_MotionVectors: 1
  m_LightProbeUsage: 1
  m_ReflectionProbeUsage: 0
  m_RayTracingMode: 0
  m_RayTraceProcedural: 0
  m_RenderingLayerMask: 4294967295
  m_RendererPriority: 0
  m_Materials:
  - {fileID: 2100000, guid: 18028a8d06240e84b2642535a97cd9c4, type: 2}
  - {fileID: 0}
  m_StaticBatchInfo:
    firstSubMesh: 0
    subMeshCount: 0
  m_StaticBatchRoot: {fileID: 0}
  m_ProbeAnchor: {fileID: 0}
  m_LightProbeVolumeOverride: {fileID: 0}
  m_ScaleInLightmap: 1
  m_ReceiveGI: 1
  m_PreserveUVs: 0
  m_IgnoreNormalsForChartDetection: 0
  m_ImportantGI: 0
  m_StitchLightmapSeams: 0
  m_SelectedEditorRenderState: 0
  m_MinimumChartSize: 4
  m_AutoUVMaxDistance: 0.5
  m_AutoUVMaxAngle: 89
  m_LightmapParameters: {fileID: 0}
  m_SortingLayerID: -890204995
  m_SortingLayer: 11
  m_SortingOrder: 22
  m_RenderMode: 0
  m_MeshDistribution: 0
  m_SortMode: 3
  m_MinParticleSize: 0
  m_MaxParticleSize: 1
  m_CameraVelocityScale: 0
  m_VelocityScale: 0
  m_LengthScale: 2
  m_SortingFudge: 0
  m_NormalDirection: 1
  m_ShadowBias: 0
  m_RenderAlignment: 0
  m_Pivot: {x: 0, y: 0, z: 0}
  m_Flip: {x: 0, y: 0, z: 0}
  m_UseCustomVertexStreams: 0
  m_EnableGPUInstancing: 0
  m_ApplyActiveColorSpace: 0
  m_AllowRoll: 1
  m_FreeformStretching: 0
  m_RotateWithStretchDirection: 1
  m_VertexStreams: 0001030405
  m_Mesh: {fileID: 0}
  m_Mesh1: {fileID: 0}
  m_Mesh2: {fileID: 0}
  m_Mesh3: {fileID: 0}
  m_MeshWeighting: 1
  m_MeshWeighting1: 1
  m_MeshWeighting2: 1
  m_MeshWeighting3: 1
  m_MaskInteraction: 0
""";
    }
}
