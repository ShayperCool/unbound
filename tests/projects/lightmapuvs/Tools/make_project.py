#!/usr/bin/env python3
"""Copies every model of Sources/ into Assets/Resources/<variant>/ with a ModelImporter meta that
turns on generateSecondaryUV with the variant's unwrap settings:

    Default   the defaults (hard angle 88, angle distortion 8, area distortion 15, margin
              Calculate with min lightmap resolution 40 and min object scale 1)
    Hard30    hard angle 30
    Distort   angle distortion 2, area distortion 40
    Manual    margin method Manual, pack margin 10
    Calc      margin method Calculate, min lightmap resolution 10, min object scale 0.5
    Off       generateSecondaryUV off (the file's own UV2, if any)
    Raw       mesh optimization off (vertices and triangles in import order), defaults otherwise
    RawOff    mesh optimization and generateSecondaryUV off

GUIDs are derived from the asset path, so a rerun rewrites the same project.
"""
import hashlib
from pathlib import Path
import shutil

PROBE = Path(__file__).resolve().parents[1]
DEFAULTS = dict(generateSecondaryUV=1, secondaryUVAngleDistortion=8, secondaryUVAreaDistortion=15.000001,
                secondaryUVHardAngle=88, secondaryUVMarginMethod=1, secondaryUVMinLightmapResolution=40,
                secondaryUVMinObjectScale=1, secondaryUVPackMargin=4, meshOptimizationFlags=-1)
VARIANTS = {
    'Default': {},
    'Hard30': dict(secondaryUVHardAngle=30),
    'Distort': dict(secondaryUVAngleDistortion=2, secondaryUVAreaDistortion=40),
    'Manual': dict(secondaryUVMarginMethod=0, secondaryUVPackMargin=10),
    'Calc': dict(secondaryUVMinLightmapResolution=10, secondaryUVMinObjectScale=0.5),
    'Off': dict(generateSecondaryUV=0),
    'Raw': dict(meshOptimizationFlags=0),
    'RawOff': dict(meshOptimizationFlags=0, generateSecondaryUV=0),
}

META = '''fileFormatVersion: 2
guid: {guid}
ModelImporter:
  serializedVersion: 22200
  internalIDToNameTable: []
  externalObjects: {{}}
  materials:
    materialImportMode: 0
    materialName: 0
    materialSearch: 1
    materialLocation: 1
  animations:
    legacyGenerateAnimations: 4
    bakeSimulation: 0
    resampleCurves: 1
    optimizeGameObjects: 0
    removeConstantScaleCurves: 0
    motionNodeName: 
    animationImportErrors: 
    animationImportWarnings: 
    animationRetargetingWarnings: 
    animationDoRetargetingWarnings: 0
    importAnimatedCustomProperties: 0
    importConstraints: 0
    animationCompression: 1
    animationRotationError: 0.5
    animationPositionError: 0.5
    animationScaleError: 0.5
    animationWrapMode: 0
    extraExposedTransformPaths: []
    extraUserProperties: []
    clipAnimations: []
    isReadable: 1
  meshes:
    lODScreenPercentages: []
    globalScale: 1
    meshCompression: 0
    addColliders: 0
    useSRGBMaterialColor: 1
    sortHierarchyByName: 1
    importPhysicalCameras: 1
    importVisibility: 1
    importBlendShapes: 1
    importCameras: 1
    importLights: 1
    nodeNameCollisionStrategy: 1
    fileIdsGeneration: 2
    swapUVChannels: 0
    generateSecondaryUV: {generateSecondaryUV}
    useFileUnits: 1
    keepQuads: 0
    weldVertices: 1
    bakeAxisConversion: 0
    preserveHierarchy: 0
    skinWeightsMode: 0
    maxBonesPerVertex: 4
    minBoneWeight: 0.001
    optimizeBones: 1
    meshOptimizationFlags: {meshOptimizationFlags}
    indexFormat: 0
    secondaryUVAngleDistortion: {secondaryUVAngleDistortion}
    secondaryUVAreaDistortion: {secondaryUVAreaDistortion}
    secondaryUVHardAngle: {secondaryUVHardAngle}
    secondaryUVMarginMethod: {secondaryUVMarginMethod}
    secondaryUVMinLightmapResolution: {secondaryUVMinLightmapResolution}
    secondaryUVMinObjectScale: {secondaryUVMinObjectScale}
    secondaryUVPackMargin: {secondaryUVPackMargin}
    useFileScale: 1
    strictVertexDataChecks: 0
  tangentSpace:
    normalSmoothAngle: 60
    normalImportMode: 0
    tangentImportMode: 3
    normalCalculationMode: 4
    legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes: 0
    blendShapeNormalImportMode: 1
    normalSmoothingSource: 0
  referencedClips: []
  importAnimation: 0
  humanDescription:
    serializedVersion: 3
    human: []
    skeleton: []
    armTwist: 0.5
    foreArmTwist: 0.5
    upperLegTwist: 0.5
    legTwist: 0.5
    armStretch: 0.05
    legStretch: 0.05
    feetSpacing: 0
    globalScale: 1
    rootMotionBoneName: 
    hasTranslationDoF: 0
    hasExtraRoot: 0
    skeletonHasParents: 1
  lastHumanDescriptionAvatarSource: {{instanceID: 0}}
  autoGenerateAvatarMappingIfUnspecified: 1
  animationType: 0
  humanoidOversampling: 1
  avatarSetup: 0
  addHumanoidExtraRootOnlyWhenUsingAvatar: 1
  importBlendShapeDeformPercent: 1
  remapMaterialsIfMaterialImportModeIsNone: 0
  additionalBone: 0
  userData: 
  assetBundleName: 
  assetBundleVariant: 
'''

FOLDER = '''fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
'''


def guid(path):
    return hashlib.md5(('lightmapuvs/' + path).encode()).hexdigest()


def main():
    resources = PROBE/'Assets/Resources'
    if resources.exists():
        shutil.rmtree(resources)
    resources.mkdir(parents=True)
    (PROBE/'Assets/Resources.meta').write_text(FOLDER.format(guid=guid('Assets/Resources')))
    sources = sorted(p for p in (PROBE/'Sources').iterdir() if p.suffix in ('.fbx', '.obj'))
    for variant, overrides in VARIANTS.items():
        folder = resources/variant
        folder.mkdir()
        rel = f'Assets/Resources/{variant}'
        (resources/(variant + '.meta')).write_text(FOLDER.format(guid=guid(rel)))
        settings = dict(DEFAULTS, **overrides)
        for source in sources:
            shutil.copyfile(source, folder/source.name)
            (folder/(source.name + '.meta')).write_text(
                META.format(guid=guid(f'{rel}/{source.name}'), **settings))


if __name__ == '__main__':
    main()
