//! The model importer: FBX (and OBJ) through `ufbx`, converted the way Unity's `ModelImporter`
//! does, into the data the runtime needs to instantiate the model prefab.
//!
//! **Coordinates.** Without "Bake Axis Conversion", the file is brought to right-handed, Y-up
//! space through the top-level transforms. Static improper-axis conversions use Unity's
//! measured positive-scale decomposition. The result is then
//! mirrored on X into Unity's left-handed space: positions `(-x, y, z)`, rotations
//! `(x, -y, -z, w)`, normals `(-x, y, z)`, tangents `(-x, y, z, -w)`, and every triangle's winding
//! reversed. Positions (vertices, node translations, bind-pose translations) are multiplied by
//! the import scale: `meshes.globalScale`, times the file's unit in metres when
//! `meshes.useFileScale` ("Convert Units") is set. Node scales are untouched.
//! Static "Bake Axis Conversion" instead maps geometry and every local transform to Unity's
//! axes. Animated/deformed baked-axis imports fail explicitly pending separate measurements.
//!
//! **Hierarchy.** The prefab root is a GameObject named after the file (`//RootNode` in the
//! fileID tables). When the file has a single top-level node and `preserveHierarchy` is off, that
//! node *is* the root (its transform and renderer move to the root), unless its default node
//! attribute (`DefaultAttributeIndex`) is a skeleton: a bone stays a child of the root (measured,
//! tests/projects/modeltoproot). Node names are made unique
//! across the model by appending ` 1`, ` 2`, ... in depth-first order, and children are sorted by
//! name when `sortHierarchyByName` (the default) is set.
//!
//! **Objects and fileIDs.** Each node has a GameObject and a Transform; a mesh node has a
//! MeshFilter + MeshRenderer, or a SkinnedMeshRenderer when skinned; each mesh node has its own
//! Mesh named after the node. IDs come from the meta's tables (see [`crate::meta`]). For objects
//! the tables do not list, Unity 2019.3+ derives IDs from a hash of the object's type and name
//! ([`crate::meta::hashed_file_id`]): meshes, materials and clips always, and with
//! `fileIdsGeneration: 1` the hierarchy's objects too (the root's under `//RootNode`). With
//! generation 2 the hierarchy's IDs come from elsewhere (not reproduced): the root objects get the
//! values every such model has (observed), others a free legacy-style ID and
//! [`FileIdSource::Derived`].
//!
//! **Blend shapes.** Each blend channel of a mesh is a shape named after the channel, its
//! keyframes the frames (full weight × 100); a mesh with shapes gets a SkinnedMeshRenderer, whose
//! weights start at the channels' DeformPercent (`importBlendShapeDeformPercent`).
//!
//! **Lightmap UVs.** With `generateSecondaryUV`, UV2 comes from Unity's unwrap reproduced in
//! [`lightmap_uv`]; meshes it does not reproduce yet warn and keep the file's UV2.
//!
//! Not imported yet (reported in [`ImportedModel::warnings`] when the file uses them): cameras and
//! lights (imported as plain nodes), and normal recalculation by smoothing angle (file normals,
//! or ufbx-generated smooth normals, are used).
use crate::{
    Importer, asset_name,
    meta::{FileIdAllocator, FileIdSource, FileIdTable, boolean, class, float, importer, int},
};
use anyhow::{Result, anyhow};
use serde::{Deserialize, Serialize};
use std::{
    collections::{HashMap, HashSet},
    path::Path,
};
use unbound_yaml::{ObjectRef, Value};

pub use materials::{MaterialDescription, TextureDescription};

/// File extensions Unity's ModelImporter handles that this importer reads.
pub const EXTENSIONS: &[&str] = &["fbx", "obj"];

/// The name the fileID tables give the prefab root.
pub const ROOT_NODE_NAME: &str = "//RootNode";

/// The class ID of the legacy `Animation` component.
const ANIMATION: i32 = 111;

/// IDs Unity 2019.3+ gives the root objects of every model when its meta does not list them
/// (identical across all models in Unity's packages; the derivation itself is not known).
const ROOT_GAME_OBJECT_ID: i64 = 919_132_149_155_446_097;
const ROOT_TRANSFORM_ID: i64 = -8_679_921_383_154_817_045;
const ROOT_MESH_RENDERER_ID: i64 = -7_511_558_181_221_131_132;
const ROOT_MESH_FILTER_ID: i64 = -5_754_084_199_372_789_682;
const ROOT_SKINNED_MESH_RENDERER_ID: i64 = -3_887_185_075_125_053_422;
const ROOT_ANIMATOR_ID: i64 = 5_866_666_021_909_216_657;

/// `tangentSpace.normalImportMode`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum NormalMode {
    Import,
    Calculate,
    None,
}

/// `tangentSpace.tangentImportMode`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum TangentMode {
    Import,
    /// Calculated: MikkTSpace on the final vertices (the legacy modes too, for now).
    Calculate,
    None,
}

/// `materials.materialImportMode`: how the importer makes the model's materials.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum MaterialImportMode {
    /// None: every slot gets the default material.
    None,
    /// "Standard (Legacy)": the importer's own conversion of the FBX material.
    Legacy,
    /// "Import via MaterialDescription": `AssetPostprocessor.OnPreprocessMaterialDescription`.
    Description,
}

/// `materials.materialLocation`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum MaterialLocation {
    /// Legacy: Material assets in a `Materials` folder, found by name (`materialName`,
    /// `materialSearch`).
    External,
    /// Sub-assets of the model.
    InPrefab,
}

/// The `ModelImporter` settings this importer applies or exposes.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ModelSettings {
    pub global_scale: f32,
    /// "Convert Units": apply the file's unit (centimetres in most FBX files).
    pub use_file_scale: bool,
    /// Bake the source coordinate axes into static geometry and node-local transforms.
    pub bake_axis_conversion: bool,
    pub import_blend_shapes: bool,
    /// `meshes.importBlendShapeDeformPercent`: renderers start at the file's shape weights.
    pub import_deform_percent: bool,
    pub swap_uv_channels: bool,
    pub normals: NormalMode,
    pub normal_smoothing_angle: f32,
    /// `tangentSpace.normalCalculationMode` (`ModelImporterNormalCalculationMode`).
    pub normal_calculation: i64,
    /// `tangentSpace.normalSmoothingSource` (`ModelImporterNormalSmoothingSource`).
    pub normal_smoothing_source: i64,
    /// `tangentSpace.blendShapeNormalImportMode`: the shapes' normals from the file, calculated
    /// (the default), or none.
    pub blend_shape_normals: NormalMode,
    pub tangents: TangentMode,
    pub material_import: MaterialImportMode,
    pub material_location: MaterialLocation,
    /// `materials.materialName`: 0 by texture name, 1 by material name, 2 model name + material
    /// name, 3 texture name or else model name + material name.
    pub material_name: i64,
    /// `materials.materialSearch`: 0 local `Materials` folder, 1 recursive up, 2 everywhere.
    pub material_search: i64,
    /// Legacy OBJ albedo values are gamma encoded when this option is disabled.
    pub use_srgb_material_color: bool,
    /// 0 None, 1 Legacy, 2 Generic, 3 Humanoid.
    pub animation_type: i32,
    /// `avatarSetup` (Generic/Humanoid): 0 no avatar and no Animator, 1 an Avatar created from
    /// the model, 2 another model's (`sourceAvatar`).
    pub avatar_setup: i64,
    pub source_avatar: Option<ObjectRef>,
    pub import_animation: bool,
    pub sort_hierarchy_by_name: bool,
    pub preserve_hierarchy: bool,
    /// `Mesh.isReadable`.
    pub is_readable: bool,
    /// `meshes.fileIdsGeneration`: how the IDs of objects the meta does not list are derived.
    pub file_ids_generation: i64,
    /// `meshes.weldVertices`: control points at equal positions are merged (Unity's unwrap input).
    #[serde(default)]
    pub weld_vertices: bool,
    /// `meshes.meshOptimizationFlags` without `VertexOrder`: control points no face uses keep
    /// their vertices (measured, tests/projects/lightmapuvs: the Editor keeps them with flags 0
    /// and removes them with `Everything`, the default).
    #[serde(default)]
    pub keep_unused_vertices: bool,
    /// `meshes.generateSecondaryUV` with its unwrap settings; `None` when off.
    #[serde(default)]
    pub secondary_uv: Option<SecondaryUvSettings>,
}

/// `meshes.secondaryUV*`: the lightmap UV unwrap settings (`ModelImporter.secondaryUV*`).
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct SecondaryUvSettings {
    /// Degrees: edges whose faces meet at a larger angle are always cut.
    pub hard_angle: f32,
    /// Percent of allowed angle distortion.
    pub angle_distortion: f32,
    /// Percent of allowed area distortion.
    pub area_distortion: f32,
    /// `secondaryUVMarginMethod`: 0 Manual (`pack_margin`), 1 Calculate (from the resolution and
    /// object scale below).
    pub margin_method: i64,
    /// Manual margin, in pixels of a 1024 texel map.
    pub pack_margin: f32,
    pub min_lightmap_resolution: f32,
    pub min_object_scale: f32,
}

impl SecondaryUvSettings {
    fn read(imp: &Value) -> Option<Self> {
        if !boolean(imp, "meshes.generateSecondaryUV").unwrap_or(false) {
            return None;
        }
        Some(Self {
            hard_angle: float(imp, "meshes.secondaryUVHardAngle").unwrap_or(88.0),
            angle_distortion: float(imp, "meshes.secondaryUVAngleDistortion").unwrap_or(8.0),
            area_distortion: float(imp, "meshes.secondaryUVAreaDistortion").unwrap_or(15.0),
            // Metas from before the margin method existed have only the manual margin
            // (unmeasured: the current default, Calculate, applies to metas that name it).
            margin_method: int(imp, "meshes.secondaryUVMarginMethod").unwrap_or(0),
            pack_margin: float(imp, "meshes.secondaryUVPackMargin").unwrap_or(4.0),
            min_lightmap_resolution: float(imp, "meshes.secondaryUVMinLightmapResolution")
                .unwrap_or(40.0),
            min_object_scale: float(imp, "meshes.secondaryUVMinObjectScale").unwrap_or(1.0),
        })
    }
}

impl ModelSettings {
    pub fn read(meta: &Value) -> Self {
        let imp = importer(meta, "ModelImporter");
        // Metas written since 2019.3 (serializedVersion 19300) take the current defaults for the
        // settings they omit; older ones upgrade to the legacy material import with external
        // materials and an avatar created from the model (measured: tests/projects/modelimport).
        let current = int(imp, "serializedVersion").unwrap_or(0) >= 19300;
        Self {
            global_scale: float(imp, "meshes.globalScale").unwrap_or(1.0),
            use_file_scale: boolean(imp, "meshes.useFileScale").unwrap_or(true),
            bake_axis_conversion: boolean(imp, "meshes.bakeAxisConversion").unwrap_or(false),
            import_blend_shapes: boolean(imp, "meshes.importBlendShapes").unwrap_or(true),
            import_deform_percent: boolean(imp, "importBlendShapeDeformPercent").unwrap_or(true),
            swap_uv_channels: boolean(imp, "meshes.swapUVChannels").unwrap_or(false),
            normals: match int(imp, "tangentSpace.normalImportMode").unwrap_or(0) {
                1 => NormalMode::Calculate,
                2 => NormalMode::None,
                _ => NormalMode::Import,
            },
            normal_smoothing_angle: float(imp, "tangentSpace.normalSmoothAngle").unwrap_or(60.0),
            normal_calculation: int(imp, "tangentSpace.normalCalculationMode").unwrap_or(4),
            normal_smoothing_source: int(imp, "tangentSpace.normalSmoothingSource").unwrap_or(0),
            blend_shape_normals: match int(imp, "tangentSpace.blendShapeNormalImportMode")
                .unwrap_or(1)
            {
                0 => NormalMode::Import,
                2 => NormalMode::None,
                _ => NormalMode::Calculate,
            },
            // ModelImporterTangents: Import 0, CalculateLegacy 1, None 2, CalculateMikk 3,
            // CalculateLegacyWithSplitTangents 4.
            tangents: match int(imp, "tangentSpace.tangentImportMode").unwrap_or(3) {
                0 => TangentMode::Import,
                2 => TangentMode::None,
                _ => TangentMode::Calculate,
            },
            material_import: match int(imp, "materials.materialImportMode") {
                Some(0) => MaterialImportMode::None,
                Some(1) => MaterialImportMode::Legacy,
                Some(_) => MaterialImportMode::Description,
                None if boolean(imp, "materials.importMaterials") == Some(false) => {
                    MaterialImportMode::None
                }
                None if current => MaterialImportMode::Description,
                None => MaterialImportMode::Legacy,
            },
            material_location: match int(imp, "materials.materialLocation") {
                Some(0) => MaterialLocation::External,
                Some(_) => MaterialLocation::InPrefab,
                None if current => MaterialLocation::InPrefab,
                None => MaterialLocation::External,
            },
            material_name: int(imp, "materials.materialName").unwrap_or(0),
            material_search: int(imp, "materials.materialSearch").unwrap_or(1),
            use_srgb_material_color: boolean(imp, "meshes.useSRGBMaterialColor").unwrap_or(true),
            animation_type: int(imp, "animationType").unwrap_or(2) as i32,
            avatar_setup: int(imp, "avatarSetup").unwrap_or(if current { 0 } else { 1 }),
            source_avatar: imp
                .get("sourceAvatar")
                .and_then(Value::as_ref)
                .filter(|r| !r.is_null()),
            import_animation: boolean(imp, "importAnimation").unwrap_or(true),
            sort_hierarchy_by_name: boolean(imp, "meshes.sortHierarchyByName").unwrap_or(true),
            preserve_hierarchy: boolean(imp, "meshes.preserveHierarchy").unwrap_or(false),
            is_readable: boolean(imp, "meshes.isReadable")
                .or_else(|| boolean(imp, "animations.isReadable"))
                .unwrap_or(false),
            file_ids_generation: int(imp, "meshes.fileIdsGeneration").unwrap_or(2),
            weld_vertices: boolean(imp, "meshes.weldVertices").unwrap_or(true),
            keep_unused_vertices: int(imp, "meshes.meshOptimizationFlags")
                .is_some_and(|flags| flags & MESH_OPTIMIZATION_VERTEX_ORDER == 0),
            secondary_uv: SecondaryUvSettings::read(imp),
        }
    }
}

/// An object of the model with its fileID.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct FileId {
    pub id: i64,
    pub source: FileIdSource,
}

impl From<(i64, FileIdSource)> for FileId {
    fn from((id, source): (i64, FileIdSource)) -> Self {
        Self { id, source }
    }
}

/// A node: a GameObject with its Transform. `nodes[0]` is the root.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ModelNode {
    /// The GameObject's name (the file name for the root).
    pub name: String,
    pub parent: Option<u32>,
    pub children: Vec<u32>,
    /// Local transform in Unity's space.
    pub position: [f32; 3],
    /// `(x, y, z, w)`.
    pub rotation: [f32; 4],
    pub scale: [f32; 3],
    pub game_object: FileId,
    pub transform: FileId,
    pub renderer: Option<ModelRenderer>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum RendererKind {
    /// MeshFilter + MeshRenderer.
    Mesh,
    SkinnedMesh,
}

/// The renderer component(s) of a node.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ModelRenderer {
    pub kind: RendererKind,
    /// Index into [`ImportedModel::meshes`].
    pub mesh: u32,
    /// Per submesh, an index into [`ImportedModel::materials`] (`None`: no material assigned).
    pub materials: Vec<Option<u32>>,
    /// The MeshRenderer or SkinnedMeshRenderer.
    pub renderer: FileId,
    /// The MeshFilter (`RendererKind::Mesh` only).
    pub filter: Option<FileId>,
    /// Skinned: the bone nodes, in the order of the mesh's bone indices and bind poses.
    pub bones: Vec<u32>,
    pub root_bone: Option<u32>,
    /// Skinned: the blend shapes' initial weights (percent; the file's DeformPercent).
    #[serde(default)]
    pub blend_shape_weights: Vec<f32>,
    /// Skinned with a root bone: `localBounds` (centre, extents) in the root bone's space.
    #[serde(default)]
    pub local_bounds: Option<([f32; 3], [f32; 3])>,
}

/// A blend shape channel of a mesh: its frames by increasing weight.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ModelBlendShape {
    pub name: String,
    pub frames: Vec<ModelBlendFrame>,
}

/// A frame: its full weight (percent) and the deltas per mesh vertex (normals and tangents empty
/// when not imported or calculated).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ModelBlendFrame {
    pub weight: f32,
    pub vertices: Vec<[f32; 3]>,
    pub normals: Vec<[f32; 3]>,
    pub tangents: Vec<[f32; 3]>,
}

/// Up to four bone influences, heaviest first, weights summing to 1.
#[derive(Debug, Clone, Copy, PartialEq, Default, Serialize, Deserialize)]
pub struct BoneWeight {
    pub bones: [u16; 4],
    pub weights: [f32; 4],
}

/// A Mesh object.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ModelMesh {
    pub name: String,
    pub file_id: FileId,
    pub positions: Vec<[f32; 3]>,
    /// Empty when normals are not imported.
    pub normals: Vec<[f32; 3]>,
    /// Empty unless imported from the file (see [`ModelSettings::tangents`]).
    pub tangents: Vec<[f32; 4]>,
    /// UV channels 0..=3 present in the file (`uvs[0]` is `Mesh.uv`).
    pub uvs: Vec<Vec<[f32; 2]>>,
    /// Empty when the file has no vertex colours.
    pub colors: Vec<[f32; 4]>,
    /// Triangle lists, one per submesh (material slot), clockwise front faces as in Unity.
    pub submeshes: Vec<Vec<u32>>,
    /// Per vertex, for skinned meshes (empty otherwise).
    pub bone_weights: Vec<BoneWeight>,
    /// Per bone: mesh space to bone space at bind time, column-major 4×4.
    pub bind_poses: Vec<[f32; 16]>,
    pub bounds_min: [f32; 3],
    pub bounds_max: [f32; 3],
    #[serde(default)]
    pub blend_shapes: Vec<ModelBlendShape>,
}

/// A material slot of the model. Which Material asset fills it (embedded, extracted, remapped
/// through `externalObjects`, or found by the legacy material search) is decided by the caller.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ModelMaterial {
    pub name: String,
    /// The FBX material as the importer describes it (empty for the legacy "No Name" material of
    /// slots without one).
    pub description: MaterialDescription,
    /// The embedded Material sub-asset's fileID.
    pub file_id: FileId,
    /// The Material assigned to this name in `externalObjects`, if any.
    pub remap: Option<ObjectRef>,
}

/// The LODGroup the importer adds to the root when node names end in `_LOD<n>`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ModelLodGroup {
    pub file_id: FileId,
    /// Per level, the nodes whose renderers it draws.
    pub lods: Vec<ModelLod>,
    /// The levels' bounds in the root's space: centre and largest extent.
    pub local_reference_point: [f32; 3],
    pub size: f32,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ModelLod {
    pub screen_relative_height: f32,
    pub renderers: Vec<u32>,
}

/// A keyframe of a baked transform curve (seconds from the clip's start).
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct Key<const N: usize> {
    pub time: f32,
    #[serde(with = "serde_arrays")]
    pub value: [f32; N],
}

/// `[f32; N]` for serde (which derives only up to fixed sizes it knows).
mod serde_arrays {
    use serde::{Deserialize, Deserializer, Serialize, Serializer};

    pub fn serialize<S: Serializer, const N: usize>(
        value: &[f32; N],
        s: S,
    ) -> Result<S::Ok, S::Error> {
        value.as_slice().serialize(s)
    }

    pub fn deserialize<'de, D: Deserializer<'de>, const N: usize>(
        d: D,
    ) -> Result<[f32; N], D::Error> {
        let v = Vec::<f32>::deserialize(d)?;
        v.try_into()
            .map_err(|_| serde::de::Error::custom("wrong array length"))
    }
}

/// The animated transform of one node in a clip, in Unity's space (local position, rotation as
/// `(x, y, z, w)`, scale). Empty lists: the property is not animated.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct NodeCurves {
    /// The node's path from the model root (`""` for the root), as clips bind it.
    pub path: String,
    pub positions: Vec<Key<3>>,
    pub rotations: Vec<Key<4>>,
    pub scales: Vec<Key<3>>,
}

/// An animated float of a component in a clip (a blend shape's weight: `blendShape.<name>` on
/// a SkinnedMeshRenderer).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct FloatCurve {
    pub path: String,
    pub attribute: String,
    pub class_id: i32,
    pub keys: Vec<Key<1>>,
}

/// An AnimationClip sub-asset: one take of the file, or a range of it from the meta's
/// `clipAnimations`, baked to keyframes at the file's frame rate (ufbx's baker).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ModelClip {
    pub name: String,
    pub file_id: FileId,
    /// Made for the legacy Animation component (`animationType` Legacy).
    pub legacy: bool,
    pub sample_rate: f32,
    /// Seconds.
    pub length: f32,
    pub wrap_mode: i32,
    pub loop_time: bool,
    pub loop_blend: bool,
    pub cycle_offset: f32,
    pub curves: Vec<NodeCurves>,
    #[serde(default)]
    pub floats: Vec<FloatCurve>,
    /// The clip's animation events from the importer's settings, times in seconds.
    #[serde(default)]
    pub events: Vec<ClipEvent>,
    /// Curves of `clipAnimations[].curves`: float curves on the root's Animator, named after
    /// the parameter they drive, times in seconds.
    #[serde(default)]
    pub parameters: Vec<ParameterCurve>,
    /// Which transforms keep their curves (`maskType`, `transformMask`, `maskSource`).
    #[serde(default)]
    pub mask: ClipMask,
    #[serde(default)]
    pub additive_reference: AdditiveReference,
    #[serde(default)]
    pub settings: ClipSettings,
    /// The Editor's `__preview__<take>` clip of a whole take (HideInHierarchy).
    #[serde(default)]
    pub preview: bool,
    /// Keys on every frame, rotations and scales with smooth tangents (the slope between the
    /// neighbouring keys) and positions linear, as animation compression Off makes them (measured,
    /// tests/projects/modelclips); else linear between the keys.
    #[serde(default)]
    pub smooth: bool,
}

/// A clip's transform mask. The Editor removes the curves of transforms the mask leaves out
/// (weight 0, or not listed); the curves of their children stay.
#[derive(Debug, Clone, Default, PartialEq, Serialize, Deserialize)]
pub enum ClipMask {
    /// `maskType` None (3): every curve stays, whatever `transformMask` lists.
    #[default]
    None,
    /// `maskType` CreateFromThisModel (0): the meta's `transformMask` (path, weight).
    Paths(Vec<(String, f32)>),
    /// `maskType` CopyFromOther (1): the AvatarMask asset `maskSource` names. The Editor reads
    /// the asset, not the copy in `transformMask`.
    Asset { guid: String, file_id: i64 },
}

/// `hasAdditiveReferencePose` and `additiveReferencePoseFrame`: a frame within the clip refers to
/// the clip itself (time from its first frame); one outside it to the take's preview clip (time
/// from the take's start).
#[derive(Debug, Clone, Default, PartialEq, Serialize, Deserialize)]
pub struct AdditiveReference {
    pub enabled: bool,
    /// The fileID of the reference clip in this model.
    pub clip: Option<i64>,
    pub time: f32,
}

/// The clip settings the importer passes on unchanged (AnimationClipSettings).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ClipSettings {
    pub mirror: bool,
    pub orientation_offset_y: f32,
    pub level: f32,
    pub loop_blend_orientation: bool,
    pub loop_blend_position_y: bool,
    pub loop_blend_position_xz: bool,
    pub keep_original_orientation: bool,
    pub keep_original_position_y: bool,
    pub keep_original_position_xz: bool,
    pub height_from_feet: bool,
}

impl Default for ClipSettings {
    fn default() -> Self {
        Self {
            mirror: false,
            orientation_offset_y: 0.0,
            level: 0.0,
            loop_blend_orientation: false,
            loop_blend_position_y: false,
            loop_blend_position_xz: false,
            keep_original_orientation: false,
            keep_original_position_y: true,
            keep_original_position_xz: false,
            height_from_feet: false,
        }
    }
}

/// A float curve with its own tangents (a clip's parameter curve).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ParameterCurve {
    pub name: String,
    pub keys: Vec<CurveKey>,
    pub pre_infinity: i32,
    pub post_infinity: i32,
}

/// A Keyframe as Unity serializes it.
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct CurveKey {
    pub time: f32,
    pub value: f32,
    pub in_slope: f32,
    pub out_slope: f32,
    pub tangent_mode: i32,
    pub weighted_mode: i32,
    pub in_weight: f32,
    pub out_weight: f32,
}

/// An AnimationEvent of an imported clip (`clipAnimations[].events`).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ClipEvent {
    pub time: f32,
    pub function_name: String,
    pub data: String,
    pub float_parameter: f32,
    pub int_parameter: i32,
    pub message_options: i32,
}

/// An imported model.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ImportedModel {
    /// The file name without extension: the root GameObject's name.
    pub name: String,
    /// Depth-first, parents before children; `nodes[0]` is the root.
    pub nodes: Vec<ModelNode>,
    pub meshes: Vec<ModelMesh>,
    pub materials: Vec<ModelMaterial>,
    pub clips: Vec<ModelClip>,
    /// The root's Animator (Generic/Humanoid) or Animation (Legacy) component.
    pub animator: Option<FileId>,
    /// The Avatar sub-asset (Generic/Humanoid).
    pub avatar: Option<FileId>,
    /// A Humanoid avatar's `humanDescription` as the meta has it (bone mapping, T-pose skeleton,
    /// limits and parameters); the engine builds the human from it and the model's hierarchy.
    #[serde(default)]
    pub human_description: Option<Value>,
    /// The LODGroup of the root, when nodes are named as LOD levels.
    #[serde(default)]
    pub lod_group: Option<ModelLodGroup>,
    /// The effective position scale.
    pub scale: f32,
    pub settings: ModelSettings,
    /// Content of the file that was not imported.
    pub warnings: Vec<String>,
}

impl ImportedModel {
    /// The node whose GameObject or Transform has `file_id`.
    pub fn node_by_file_id(&self, file_id: i64) -> Option<(usize, &ModelNode)> {
        self.nodes
            .iter()
            .enumerate()
            .find(|(_, n)| n.game_object.id == file_id || n.transform.id == file_id)
    }

    /// The mesh with `file_id`.
    pub fn mesh_by_file_id(&self, file_id: i64) -> Option<&ModelMesh> {
        self.meshes.iter().find(|m| m.file_id.id == file_id)
    }
}

/// The normal of a vertex no face uses (measured: tests/projects/lightmapuvs).
const UNUSED_VERTEX_NORMAL: [f32; 3] = [1.0, 1.0, 1.0];

/// `MeshOptimizationFlags.VertexOrder`.
const MESH_OPTIMIZATION_VERTEX_ORDER: i64 = 2;

/// The model importer.
#[derive(Debug, Clone, Default)]
pub struct ModelImporter;

impl Importer for ModelImporter {
    type Output = ImportedModel;
    const KIND: &'static str = "models";
    const VERSION: u32 = 30;

    fn dependencies(&self, source: &Path) -> Result<Vec<std::path::PathBuf>> {
        if !source
            .extension()
            .is_some_and(|e| e.eq_ignore_ascii_case("obj"))
        {
            return Ok(Vec::new());
        }
        let bytes = std::fs::read(source)?;
        Ok(obj_material_library(source, &bytes).into_iter().collect())
    }

    fn import(&self, source: &Path, bytes: &[u8], meta: &Value) -> Result<ImportedModel> {
        import_model(source, bytes, meta)
    }
}

/// Select the same authored library for dependency hashing and ufbx loading.
fn obj_material_library(source: &Path, bytes: &[u8]) -> Option<std::path::PathBuf> {
    let text = String::from_utf8_lossy(bytes);
    text.lines()
        .filter_map(|line| {
            let line = line.trim().trim_start_matches('\u{feff}');
            let (keyword, rest) = line.split_once(char::is_whitespace)?;
            if keyword != "mtllib" {
                return None;
            }
            let end = rest
                .char_indices()
                .find_map(|(i, c)| {
                    (c == '#' && (i == 0 || rest[..i].ends_with(char::is_whitespace))).then_some(i)
                })
                .unwrap_or(rest.len());
            let path = rest[..end].trim().replace('\\', "/");
            Some(source.parent().unwrap_or(Path::new(".")).join(path))
        })
        .last()
}

pub fn import_model(source: &Path, bytes: &[u8], meta: &Value) -> Result<ImportedModel> {
    let settings = ModelSettings::read(meta);
    let imp = importer(meta, "ModelImporter");
    let is_obj = source
        .extension()
        .is_some_and(|e| e.eq_ignore_ascii_case("obj"));
    let filename = source.to_string_lossy();
    let library = is_obj
        .then(|| obj_material_library(source, bytes))
        .flatten();
    let library_name = library.as_ref().map(|p| p.to_string_lossy());
    // Calculated normals that honour smoothing groups need the FBX's own smoothing masks.
    let reads_fbx_smoothing = !is_obj
        && settings.normals != NormalMode::None
        && settings.normal_calculation != 0
        && settings.normal_smoothing_source < 2;
    let load = |raw_axes| {
        ufbx::load_memory(
            bytes,
            ufbx::LoadOpts {
                target_axes: if raw_axes {
                    ufbx::CoordinateAxes::default()
                } else {
                    ufbx::CoordinateAxes::right_handed_y_up()
                },
                space_conversion: ufbx::SpaceConversion::AdjustTransforms,
                // Pivots and geometric transforms are baked as the Editor does (see `pivots`).
                geometry_transform_handling: ufbx::GeometryTransformHandling::Preserve,
                pivot_handling: ufbx::PivotHandling::Retain,
                // Missing normals are calculated as the importer does (see `normals`).
                generate_missing_normals: false,
                // The DOM identifies each geometry's smoothing layer (see `fbx_smoothing`).
                retain_dom: reads_fbx_smoothing,
                filename: filename.as_ref().into(),
                // Explicit selection keeps the loader's inputs identical to the cache dependencies.
                obj_mtl_path: library_name.as_deref().unwrap_or("").into(),
                ..Default::default()
            },
        )
        .map_err(|e| anyhow!("{}", e.description))
    };
    let baked = !is_obj && settings.bake_axis_conversion;
    let mut scene = load(baked)?;
    let static_geometry = scene.anim_stacks.is_empty()
        && scene.skin_deformers.is_empty()
        && scene.blend_deformers.is_empty()
        && scene.cache_deformers.is_empty();
    anyhow::ensure!(
        !baked || static_geometry,
        "{}: FBX Bake Axis Conversion for animation or deformers is not yet supported",
        source.display()
    );
    let conversion = if !is_obj && (baked || static_geometry) {
        let axes = axes::StaticAxes::new(scene.settings.axes, baked)?;
        (baked || axes.reverses_handedness()).then_some(axes)
    } else {
        None
    };
    // ufbx's decomposition of an improper axis conversion introduces negative node
    // scales. Static Unity imports retain positive source scales and use a different
    // top-level rotation. Read the original local transforms for that measured path.
    if conversion.is_some() && !baked {
        scene = load(true)?;
    }

    let file_unit = if is_obj || scene.settings.unit_meters <= 0.0 {
        1.0
    } else {
        scene.settings.unit_meters as f32
    };
    let scale = settings.global_scale
        * if settings.use_file_scale {
            file_unit
        } else {
            1.0
        };

    let mut warnings = Vec::new();
    let fbx_smoothing = if reads_fbx_smoothing
        && (settings.normals == NormalMode::Calculate
            || scene.meshes.iter().any(|m| !m.vertex_normal.exists))
    {
        fbx_smoothing::per_mesh(
            bytes,
            &scene,
            settings.normals == NormalMode::Import,
            &mut warnings,
        )?
    } else {
        Vec::new()
    };
    let table = FileIdTable::read(imp);
    let mut ids = FileIdAllocator::new(&table);
    let mut builder = Builder {
        settings: &settings,
        scale,
        reverse_winding: baked && conversion.as_ref().is_some_and(|c| c.reverses_handedness()),
        obj: scene.metadata.file_format == ufbx::FileFormat::Obj,
        name: asset_name(source),
        legacy: table.legacy,
        model: ImportedModel {
            name: asset_name(source),
            nodes: Vec::new(),
            meshes: Vec::new(),
            materials: Vec::new(),
            clips: Vec::new(),
            animator: None,
            avatar: None,
            human_description: None,
            lod_group: None,
            scale,
            settings: settings.clone(),
            warnings,
        },
        unique_names: HashMap::new(),
        node_index: HashMap::new(),
        sources: Vec::new(),
        pivots: Vec::new(),
        offsets: HashMap::new(),
        rest_pose: (settings.import_animation && settings.animation_type != 0)
            .then(|| scene.anim_stacks.first())
            .flatten()
            .map(|stack| (stack.anim.as_ref(), stack.time_begin)),
        material_index: HashMap::new(),
        obj_diffuse: is_obj
            .then(|| {
                scene
                    .materials
                    .iter()
                    .find_map(|m| m.fbx.diffuse_color.texture.as_deref())
            })
            .flatten(),
        obj_smoothing: if is_obj {
            obj_smoothing_groups(bytes, &scene)
        } else {
            Vec::new()
        },
        fbx_smoothing,
    };

    // The root: the single top-level node unless it is a bone, or the scene root.
    let root_node: &ufbx::Node = &scene.root_node;
    let root: &ufbx::Node = if !settings.preserve_hierarchy
        && root_node.children.len() == 1
        && !default_attribute_is_bone(&root_node.children[0])
    {
        &root_node.children[0]
    } else {
        root_node
    };
    builder.assign_unique_names(root);
    builder.add_node(root, None, &mut ids);
    builder.add_renderers(&mut ids)?;
    if let Some(conversion) = conversion {
        for (node, source) in builder.model.nodes.iter_mut().zip(&builder.sources) {
            if baked {
                conversion.baked_node(node);
            } else if source.node_depth == 1 {
                conversion.unbaked_node(node);
            }
        }
        if baked {
            for mesh in &mut builder.model.meshes {
                conversion.mesh(mesh);
            }
        }
    }
    if !is_obj && static_geometry {
        for node in &mut builder.model.nodes {
            axes::normalize_rotation(node);
        }
    }
    builder.assign_material_ids(imp, &mut ids);

    // Source descriptions are consumed only after None/remap handling. A deferred MTL
    // feature must not reject geometry whose materials use authored replacements.
    if is_obj
        && settings.material_import != MaterialImportMode::None
        && builder.model.materials.iter().any(|material| {
            material.description.texture("DiffuseColor").is_some()
                && material
                    .remap
                    .as_ref()
                    .is_none_or(|reference| reference.is_null())
        })
    {
        let mut diffuse_paths = std::collections::HashSet::new();
        for material in scene.materials.iter() {
            if let Some(texture) = material.fbx.diffuse_color.texture.as_ref() {
                diffuse_paths.insert(texture.relative_filename.as_ref());
            }
        }
        anyhow::ensure!(
            diffuse_paths.len() <= 1,
            "{}: OBJ libraries with multiple diffuse texture maps are not yet supported",
            source.display()
        );
    }

    let (clips, clip_warnings) = if settings.import_animation && settings.animation_type != 0 {
        builder.clips(&scene, imp, &mut ids)
    } else {
        (Vec::new(), Vec::new())
    };
    let animator = match settings.animation_type {
        1 => Some(
            builder
                .hierarchy_id(&mut ids, ANIMATION, "Animation", ROOT_NODE_NAME, None)
                .into(),
        ),
        // Without an avatar the Editor adds no Animator (measured: tests/projects/modelimport).
        2 | 3 if settings.avatar_setup != 0 => Some(
            builder
                .hierarchy_id(
                    &mut ids,
                    class::ANIMATOR,
                    "Animator",
                    ROOT_NODE_NAME,
                    Some(ROOT_ANIMATOR_ID),
                )
                .into(),
        ),
        _ => None,
    };
    builder.lod_group(&mut ids);
    let mut paths: HashMap<String, Vec<u32>> = HashMap::new();
    for i in 0..builder.model.nodes.len() as u32 {
        paths.entry(builder.path_of(i)).or_default().push(i);
    }
    let mut model = builder.model;
    model.clips = clips;
    // Unity bounds a skinned mesh over the poses of the model's animations (documented:
    // Renderer.localBounds). Measured where every pose is the import pose: no clip, or clips
    // whose transform curves hold the nodes' import transforms.
    if pivots::poses_are_import_pose(
        &model.nodes,
        &paths,
        model.clips.iter().flat_map(|c| &c.curves),
    ) {
        pivots::set_skinned_local_bounds(&mut model);
    }
    model.warnings.extend(clip_warnings);
    model.animator = animator;
    if matches!(settings.animation_type, 2 | 3) && settings.avatar_setup == 1 {
        model.avatar = Some(ids.id(class::AVATAR, ROOT_NODE_NAME, None).into());
    }
    if settings.animation_type == 3 && model.avatar.is_some() {
        model.human_description = imp.get("humanDescription").cloned();
    }
    if settings.import_animation && settings.animation_type == 3 && !model.clips.is_empty() {
        model
            .warnings
            .push("humanoid clips play as generic transform curves (no retargeting)".into());
    }
    if scene
        .materials
        .iter()
        .any(|m| m.element.props.num_animated > 0)
    {
        model
            .warnings
            .push("material animation is not imported (the clips do not animate materials)".into());
    }
    if !scene.cameras.is_empty() || !scene.lights.is_empty() {
        model
            .warnings
            .push("cameras and lights are imported as empty nodes".into());
    }
    Ok(model)
}

struct Builder<'a> {
    settings: &'a ModelSettings,
    scale: f32,
    /// Triangles are turned (see `build_mesh`) for a baked handedness-reversing conversion.
    reverse_winding: bool,
    /// The file is OBJ.
    obj: bool,
    name: String,
    legacy: bool,
    model: ImportedModel,
    /// ufbx node element id → unique GameObject name.
    unique_names: HashMap<u32, String>,
    /// ufbx node element id → index in `model.nodes`.
    node_index: HashMap<u32, u32>,
    /// The ufbx node of each of `model.nodes`.
    sources: Vec<&'a ufbx::Node>,
    /// Each of `model.nodes`' placement (see `pivots`).
    pivots: Vec<pivots::Placement>,
    /// ufbx mesh element id → the offset baked into it (its first node's).
    offsets: HashMap<u32, pivots::GeometryOffset>,
    /// ufbx material element id → index in `model.materials`.
    material_index: HashMap<u32, u32>,
    obj_diffuse: Option<&'a ufbx::Texture>,
    /// An OBJ's smoothing group per face of each mesh (ufbx keeps only smooth or not).
    obj_smoothing: Vec<Vec<u32>>,
    /// Preserved FBX smoothing masks per face, indexed by the mesh's typed ID.
    fbx_smoothing: Vec<Option<Vec<u32>>>,
    /// With animation import, nodes rest in the first take's pose at its start, not in the file's
    /// static transforms, which can hold another frame's pose (measured, tests/projects/modelclips
    /// and tests/projects/humanoid).
    rest_pose: Option<(&'a ufbx::Anim, f64)>,
}

impl<'a> Builder<'a> {
    /// The ID of a hierarchy object (`key`: `//RootNode` for the root, else the node's name).
    fn hierarchy_id(
        &self,
        ids: &mut FileIdAllocator,
        class_id: i32,
        class_name: &str,
        key: &str,
        root_id: Option<i64>,
    ) -> (i64, FileIdSource) {
        if !self.legacy && self.settings.file_ids_generation == 1 {
            return ids.hashed(class_id, class_name, key);
        }
        let derived = (key == ROOT_NODE_NAME && !self.legacy)
            .then_some(root_id)
            .flatten();
        ids.id(class_id, key, derived)
    }

    /// The ID of a named asset (mesh, material, clip): Unity's hash of its class and name.
    fn asset_id(
        &self,
        ids: &mut FileIdAllocator,
        class_id: i32,
        class_name: &str,
        name: &str,
    ) -> (i64, FileIdSource) {
        if self.legacy {
            ids.id(class_id, name, None)
        } else {
            ids.hashed(class_id, class_name, name)
        }
    }

    /// Unique names in depth-first file order: a repeated name gets ` 1`, ` 2`, ... The root is
    /// renamed after the file and does not take part.
    fn assign_unique_names(&mut self, root: &ufbx::Node) {
        let mut used = HashSet::new();
        let mut stack: Vec<&ufbx::Node> = root.children.iter().rev().map(|c| c.as_ref()).collect();
        while let Some(node) = stack.pop() {
            let base = node.element.name.to_string();
            let mut name = base.clone();
            let mut n = 1;
            while !used.insert(name.clone()) {
                name = format!("{base} {n}");
                n += 1;
            }
            self.unique_names.insert(node.element.element_id, name);
            stack.extend(node.children.iter().rev().map(|c| c.as_ref()));
        }
    }

    fn add_node(&mut self, node: &'a ufbx::Node, parent: Option<u32>, ids: &mut FileIdAllocator) {
        let is_root = parent.is_none();
        let (name, key) = if is_root {
            (self.name.clone(), ROOT_NODE_NAME.to_string())
        } else {
            let name = self.unique_names[&node.element.element_id].clone();
            (name.clone(), name)
        };
        let game_object = self.hierarchy_id(
            ids,
            class::GAME_OBJECT,
            "GameObject",
            &key,
            Some(ROOT_GAME_OBJECT_ID),
        );
        let transform = self.hierarchy_id(
            ids,
            class::TRANSFORM,
            "Transform",
            &key,
            Some(ROOT_TRANSFORM_ID),
        );
        let evaluated;
        let t = match self.rest_pose {
            Some((anim, time)) => {
                evaluated = ufbx::evaluate_transform(anim, node, time);
                &evaluated
            }
            None => &node.local_transform,
        };
        let index = self.model.nodes.len() as u32;
        self.node_index.insert(node.element.element_id, index);
        self.sources.push(node);
        let placement = pivots::Placement::of(node);
        self.pivots.push(placement);
        if let Some(what) = placement.unmeasured(node) {
            self.model.warnings.push(format!(
                "skinned mesh node '{name}' is placed without its pivots; {what} (unmeasured)"
            ));
        }
        let parent_offset = parent.map_or(pivots::ZERO, |p| self.pivots[p as usize].offset());
        self.model.nodes.push(ModelNode {
            name,
            parent,
            children: Vec::new(),
            position: self.position(placement.position(t, parent_offset)),
            rotation: [
                t.rotation.x as f32,
                -t.rotation.y as f32,
                -t.rotation.z as f32,
                t.rotation.w as f32,
            ],
            scale: [t.scale.x as f32, t.scale.y as f32, t.scale.z as f32],
            game_object: game_object.into(),
            transform: transform.into(),
            renderer: None,
        });
        if let Some(parent) = parent {
            self.model.nodes[parent as usize].children.push(index);
        }
        let mut children: Vec<&'a ufbx::Node> = node.children.iter().map(|c| c.as_ref()).collect();
        if self.settings.sort_hierarchy_by_name {
            let names = &self.unique_names;
            children
                .sort_by(|a, b| names[&a.element.element_id].cmp(&names[&b.element.element_id]));
        }
        for child in children {
            self.add_node(child, Some(index), ids);
        }
    }

    /// The path a clip binds a node by: the names below the root, joined by `/`.
    fn path_of(&self, index: u32) -> String {
        let mut names = Vec::new();
        let mut at = Some(index);
        while let Some(i) = at {
            let node = &self.model.nodes[i as usize];
            if node.parent.is_none() {
                break;
            }
            names.push(node.name.as_str());
            at = node.parent;
        }
        names.reverse();
        names.join("/")
    }

    /// The clips: those the meta's `clipAnimations` define (ranges of takes), or one per take;
    /// then the Editor's hidden `__preview__<take>` clip of every take.
    /// The model's clips, and warnings about content they do not reproduce.
    fn clips(
        &self,
        scene: &ufbx::Scene,
        imp: &Value,
        ids: &mut FileIdAllocator,
    ) -> (Vec<ModelClip>, Vec<String>) {
        let fps = if scene.settings.frames_per_second > 0.0 {
            scene.settings.frames_per_second
        } else {
            30.0
        };
        let takes: Vec<&ufbx::AnimStack> = scene
            .anim_stacks
            .iter()
            .map(|s| s.as_ref())
            .filter(|s| s.time_end > s.time_begin)
            .collect();
        let mut ranges = clip_ranges(imp);
        if ranges.is_empty() {
            for stack in &takes {
                let name = stack.element.name.to_string();
                ranges.push(ClipRange::take(
                    &name,
                    stack.time_begin * fps,
                    stack.time_end * fps,
                ));
            }
        }
        // IDs the meta gives clips (`internalID`) are taken first, so a hashed ID never collides.
        for range in &ranges {
            if let Some(id) = range.internal_id {
                ids.reserve(id);
            }
        }
        let previews: Vec<(String, i64)> = takes
            .iter()
            .map(|stack| {
                let name = format!("__preview__{}", stack.element.name);
                let (id, _) = self.asset_id(ids, class::ANIMATION_CLIP, "AnimationClip", &name);
                (stack.element.name.to_string(), id)
            })
            .collect();
        // Blend channel element → the renderers' paths and the shape's name it drives.
        let mut shape_channels: HashMap<u32, Vec<(String, String)>> = HashMap::new();
        for (index, node) in self.sources.iter().enumerate() {
            let Some(mesh) = node.mesh.as_ref() else {
                continue;
            };
            for channel in self.blend_channels(mesh) {
                shape_channels
                    .entry(channel.element.element_id)
                    .or_default()
                    .push((self.path_of(index as u32), channel.element.name.to_string()));
            }
        }
        // Compression Off keeps a key on every frame from the clip's first one, with smooth
        // tangents; the reducing modes are approximated by the baker's (nearly lossless) key
        // reduction with linear tangents.
        let every_frame = animation_compression_off(imp);
        // The root node of a generic avatar (`motionNodeName`): its motion relative to the model's
        // root becomes the clips' MotionT/MotionQ curves on the Animator, which root motion uses
        // (measured, tests/projects/modelroot). Without an avatar there is none. Humanoid clips
        // move the root through their body curves; a root node there is not supported.
        let motion_node = motion_node_name(imp)
            .filter(|name| !name.is_empty() && self.settings.avatar_setup != 0)
            .filter(|name| {
                let generic = self.settings.animation_type == 2;
                if !generic && self.settings.animation_type == 3 {
                    tracing::warn!("{}: the root node '{name}' of a Humanoid model is ignored (not supported)", self.name);
                }
                generic
            })
            .and_then(|name| {
                let found = (1..self.model.nodes.len() as u32)
                    .find(|&i| self.path_of(i) == name)
                    .or_else(|| (1..self.model.nodes.len() as u32).find(|&i| self.model.nodes[i as usize].name == name));
                if found.is_none() {
                    tracing::warn!("{}: the root node '{name}' is not in the model; its clips have no motion curves", self.name);
                }
                found
            });
        let mut clips = Vec::new();
        let mut warnings = Vec::new();
        let preview_ranges = takes.iter().map(|stack| {
            let name = stack.element.name.to_string();
            let mut range = ClipRange::take(&name, stack.time_begin * fps, stack.time_end * fps);
            range.name = format!("__preview__{name}");
            range.preview = true;
            range
        });
        for range in ranges.into_iter().chain(preview_ranges.collect::<Vec<_>>()) {
            let stack = takes
                .iter()
                .find(|s| s.element.name.to_string() == range.take)
                .or_else(|| takes.first());
            let Some(stack) = stack else { continue };
            let opts = ufbx::BakeOpts {
                resample_rate: fps,
                minimum_sample_rate: fps,
                key_reduction_enabled: true,
                ..Default::default()
            };
            let Ok(baked) = ufbx::bake_anim(scene, &stack.anim, opts) else {
                continue;
            };
            // A range past the take's end ends with the take (measured).
            let start = range.first / fps;
            let end = (range.last.max(range.first) / fps).min(stack.time_end.max(start));
            // Humanoid clips are converted to muscle curves from these transform curves; that
            // conversion is measured for whole takes without a transform mask only.
            if self.settings.animation_type == 3 && !range.preview {
                let half_frame = 0.5 / fps;
                if (start - stack.time_begin).abs() > half_frame
                    || (stack.time_end - end).abs() > half_frame
                {
                    warnings.push(format!(
                        "humanoid clip '{}' covers only a part of its take: its muscle curves are not measured (not supported)",
                        range.name
                    ));
                }
                if range.mask != ClipMask::None {
                    warnings.push(format!(
                        "humanoid clip '{}' has a transform mask: its muscle curves are not measured (not supported)",
                        range.name
                    ));
                }
            }
            let mut curves = Vec::new();
            for node in baked.nodes.iter() {
                let Some(&index) = self.node_index.get(&node.element_id) else {
                    continue;
                };
                let path = self.path_of(index);
                if !range.mask.keeps(&path) {
                    continue;
                }
                // Every animated translation, rotation and scale gets its curves, constant ones
                // too (measured with every animation compression); a node whose take animates
                // none of them (only custom properties) gets none. Generic rigs only: humanoid
                // clips keep every channel (their muscle curves are converted from the whole
                // pose), legacy ones too (unmeasured), and so do pivoted nodes, whose position
                // follows their rotation and scale (unmeasured).
                let source = self.sources[index as usize];
                let channels = if self.settings.animation_type != 2 || self.pivoted(index) {
                    [true; 3]
                } else {
                    animated_channels(stack, source)
                };
                if channels == [false; 3] {
                    continue;
                }
                if every_frame {
                    let (mut positions, mut rotations, mut scales) =
                        (Vec::new(), Vec::new(), Vec::new());
                    for time in frame_times(start, end, fps) {
                        let t = ufbx::evaluate_transform(&stack.anim, source, time);
                        let time = (time - start) as f32;
                        positions.push(Key {
                            time,
                            value: self.position(self.placed(index, &t)),
                        });
                        rotations.push(Key {
                            time,
                            value: [
                                t.rotation.x as f32,
                                -t.rotation.y as f32,
                                -t.rotation.z as f32,
                                t.rotation.w as f32,
                            ],
                        });
                        scales.push(Key {
                            time,
                            value: [t.scale.x as f32, t.scale.y as f32, t.scale.z as f32],
                        });
                    }
                    curves.push(NodeCurves {
                        path,
                        positions: only(channels[0], positions),
                        rotations: only(channels[1], rotations),
                        scales: only(channels[2], scales),
                    });
                    continue;
                }
                let positions: Vec<Key<3>> = if self.pivoted(index) {
                    // The position depends on rotation and scale too: evaluated at every key
                    // time of the three. The baker does not reduce rotation keys, so a turning
                    // node is keyed on every frame (checked by the AnimPivot unit test).
                    let source = self.sources[index as usize];
                    let mut times: Vec<f64> = (node.translation_keys.iter().map(|k| k.time))
                        .chain(node.rotation_keys.iter().map(|k| k.time))
                        .chain(node.scale_keys.iter().map(|k| k.time))
                        .collect();
                    times.sort_by(f64::total_cmp);
                    times.dedup();
                    times
                        .into_iter()
                        .map(|time| Key {
                            time: time as f32,
                            value: self.position(self.placed(
                                index,
                                &ufbx::evaluate_transform(&stack.anim, source, time),
                            )),
                        })
                        .collect()
                } else {
                    node.translation_keys
                        .iter()
                        .map(|k| Key {
                            time: k.time as f32,
                            value: self.position(k.value),
                        })
                        .collect()
                };
                let rotations: Vec<Key<4>> = node
                    .rotation_keys
                    .iter()
                    .map(|k| Key {
                        time: k.time as f32,
                        value: [
                            k.value.x as f32,
                            -k.value.y as f32,
                            -k.value.z as f32,
                            k.value.w as f32,
                        ],
                    })
                    .collect();
                let scales: Vec<Key<3>> = node
                    .scale_keys
                    .iter()
                    .map(|k| Key {
                        time: k.time as f32,
                        value: [k.value.x as f32, k.value.y as f32, k.value.z as f32],
                    })
                    .collect();
                curves.push(NodeCurves {
                    path,
                    positions: only(channels[0], clip_range(&positions, start, end, lerp_key)),
                    rotations: only(channels[1], clip_range(&rotations, start, end, slerp_key)),
                    scales: only(channels[2], clip_range(&scales, start, end, lerp_key)),
                });
            }
            curves.sort_by(|a, b| a.path.cmp(&b.path));
            // Blend shape weights: the channels' DeformPercent, on their meshes' renderers.
            let mut floats = Vec::new();
            for element in baked.elements.iter() {
                let Some(targets) = shape_channels.get(&element.element_id) else {
                    continue;
                };
                for prop in element.props.iter() {
                    if prop.name.to_string() != "DeformPercent" {
                        continue;
                    }
                    let keys: Vec<Key<1>> = prop
                        .keys
                        .iter()
                        .map(|k| Key {
                            time: k.time as f32,
                            value: [k.value.x as f32],
                        })
                        .collect();
                    let keys = if every_frame {
                        let source = &scene.elements[element.element_id as usize];
                        frame_times(start, end, fps)
                            .into_iter()
                            .map(|time| Key {
                                time: (time - start) as f32,
                                value: [ufbx::evaluate_prop_flags(
                                    &stack.anim,
                                    source,
                                    "DeformPercent",
                                    time,
                                    0,
                                )
                                .value_vec4
                                .x as f32],
                            })
                            .collect()
                    } else {
                        clip_range(&keys, start, end, lerp_key)
                    };
                    for (path, name) in targets {
                        floats.push(FloatCurve {
                            path: path.clone(),
                            attribute: format!("blendShape.{name}"),
                            class_id: class::SKINNED_MESH_RENDERER,
                            keys: keys.clone(),
                        });
                    }
                }
            }
            let file_id = match (range.preview, range.internal_id) {
                (true, _) => previews
                    .iter()
                    .find(|(take, _)| *take == range.take)
                    .map(|(_, id)| (*id, FileIdSource::Derived)),
                (false, Some(id)) => Some((id, FileIdSource::Meta)),
                (false, None) => None,
            }
            .unwrap_or_else(|| {
                self.asset_id(ids, class::ANIMATION_CLIP, "AnimationClip", &range.name)
            });
            // In single precision, as the Editor's clip length (a range from frame 40 to 48 at
            // 24 fps is 0.33333337 s).
            let length = end as f32 - start as f32;
            let additive_reference = match range.additive_frame {
                Some(frame) if range.has_additive_reference => {
                    let inside = frame >= range.first && frame <= range.last;
                    AdditiveReference {
                        enabled: true,
                        clip: if inside {
                            Some(file_id.0)
                        } else {
                            previews
                                .iter()
                                .find(|(take, _)| *take == stack.element.name.to_string())
                                .map(|(_, id)| *id)
                        },
                        time: if inside {
                            ((frame - range.first) / fps) as f32
                        } else {
                            (frame / fps - stack.time_begin) as f32
                        },
                    }
                }
                // Without a reference pose the frame is kept as a time on the take.
                frame => AdditiveReference {
                    enabled: false,
                    clip: None,
                    time: frame.map_or(0.0, |f| (f / fps) as f32),
                },
            };
            if let Some(node) = motion_node {
                floats.extend(self.motion_curves(node, &stack.anim, start, end, fps));
            }
            clips.push(ModelClip {
                file_id: file_id.into(),
                name: range.name,
                legacy: self.settings.animation_type == 1,
                sample_rate: fps as f32,
                length,
                wrap_mode: range.wrap_mode,
                loop_time: range.loop_time,
                loop_blend: range.loop_blend,
                cycle_offset: range.cycle_offset,
                curves,
                floats,
                // Event and parameter curve times are normalized to the clip's length.
                events: range
                    .events
                    .iter()
                    .map(|e| ClipEvent {
                        time: e.time * length,
                        ..e.clone()
                    })
                    .collect(),
                parameters: range
                    .parameters
                    .iter()
                    .map(|c| ParameterCurve {
                        keys: c
                            .keys
                            .iter()
                            .map(|k| CurveKey {
                                time: k.time * length,
                                in_slope: if length > 0.0 {
                                    k.in_slope / length
                                } else {
                                    0.0
                                },
                                out_slope: if length > 0.0 {
                                    k.out_slope / length
                                } else {
                                    0.0
                                },
                                ..*k
                            })
                            .collect(),
                        ..c.clone()
                    })
                    .collect(),
                mask: range.mask.clone(),
                smooth: every_frame,
                additive_reference,
                settings: range.settings.clone(),
                preview: range.preview,
            });
        }
        (clips, warnings)
    }

    /// The MotionT (position) and MotionQ (rotation) curves of a root node: its pose relative to
    /// the model's root, at the clip's frames.
    fn motion_curves(
        &self,
        node: u32,
        anim: &ufbx::Anim,
        start: f64,
        end: f64,
        fps: f64,
    ) -> Vec<FloatCurve> {
        let mut chain = Vec::new();
        let mut at = Some(node);
        while let Some(i) = at {
            if self.model.nodes[i as usize].parent.is_none() {
                break;
            }
            chain.push(i);
            at = self.model.nodes[i as usize].parent;
        }
        chain.reverse();
        let mut position: [Vec<Key<1>>; 3] = Default::default();
        let mut rotation: [Vec<Key<1>>; 4] = Default::default();
        let mut last: Option<[f32; 4]> = None;
        for time in frame_times(start, end, fps) {
            let (mut p, mut q, mut s) = ([0f32; 3], [0f32, 0.0, 0.0, 1.0], [1f32; 3]);
            for &i in &chain {
                let t = ufbx::evaluate_transform(anim, self.sources[i as usize], time);
                let lp = self.position(self.placed(i, &t));
                let lq = [
                    t.rotation.x as f32,
                    -t.rotation.y as f32,
                    -t.rotation.z as f32,
                    t.rotation.w as f32,
                ];
                let moved = rotate(q, [lp[0] * s[0], lp[1] * s[1], lp[2] * s[2]]);
                p = [p[0] + moved[0], p[1] + moved[1], p[2] + moved[2]];
                q = quat_mul(q, lq);
                s = [
                    s[0] * t.scale.x as f32,
                    s[1] * t.scale.y as f32,
                    s[2] * t.scale.z as f32,
                ];
            }
            // In one hemisphere from key to key.
            if last.is_some_and(|l| (0..4).map(|c| l[c] * q[c]).sum::<f32>() < 0.0) {
                q = q.map(|v| -v);
            }
            last = Some(q);
            let time = (time - start) as f32;
            for c in 0..3 {
                position[c].push(Key {
                    time,
                    value: [p[c]],
                });
            }
            for c in 0..4 {
                rotation[c].push(Key {
                    time,
                    value: [q[c]],
                });
            }
        }
        let curve = |attribute: String, keys: Vec<Key<1>>| FloatCurve {
            path: String::new(),
            attribute,
            class_id: class::ANIMATOR,
            keys,
        };
        let [px, py, pz] = position;
        let [qx, qy, qz, qw] = rotation;
        vec![
            curve("MotionT.x".into(), px),
            curve("MotionT.y".into(), py),
            curve("MotionT.z".into(), pz),
            curve("MotionQ.x".into(), qx),
            curve("MotionQ.y".into(), qy),
            curve("MotionQ.z".into(), qz),
            curve("MotionQ.w".into(), qw),
        ]
    }

    /// Node `index`'s GameObject position (FBX space) for its transform `t` (see `pivots`).
    fn placed(&self, index: u32, t: &ufbx::Transform) -> ufbx::Vec3 {
        let parent = self.model.nodes.get(index as usize).and_then(|n| n.parent);
        let parent = parent.map_or(pivots::ZERO, |p| self.pivots[p as usize].offset());
        self.pivots[index as usize].position(t, parent)
    }

    /// The node or its parent is placed away from its FBX origin, so its position depends on
    /// its rotation and scale as well as its translation.
    fn pivoted(&self, index: u32) -> bool {
        let parent = self.model.nodes[index as usize].parent;
        self.pivots[index as usize].moves()
            || parent.is_some_and(|p| self.pivots[p as usize].shifts_children())
    }

    /// A position in Unity's space, scaled.
    fn position(&self, v: ufbx::Vec3) -> [f32; 3] {
        [
            -v.x as f32 * self.scale,
            v.y as f32 * self.scale,
            v.z as f32 * self.scale,
        ]
    }

    /// Renderers and meshes, once all nodes exist (bones may come after the mesh node).
    fn add_renderers(&mut self, ids: &mut FileIdAllocator) -> Result<()> {
        for index in 0..self.sources.len() {
            let node = self.sources[index];
            let Some(mesh) = node.mesh.as_ref() else {
                continue;
            };
            if mesh.num_triangles == 0 {
                continue;
            }
            let is_root = index == 0;
            let key = if is_root {
                ROOT_NODE_NAME.to_string()
            } else {
                self.model.nodes[index].name.clone()
            };
            let skin = mesh
                .skin_deformers
                .iter()
                .find(|s| !s.clusters.is_empty())
                .map(|s| s.as_ref());
            let pivot = self.pivots[index].offset();
            let offset = self
                .offsets
                .entry(mesh.element.element_id)
                .or_insert_with(|| pivots::GeometryOffset::of(node, pivot))
                .clone();
            let (built, slots, warning) = self.build_mesh(mesh, node, skin, &offset)?;
            let mesh_name = if is_root {
                node.element.name.to_string()
            } else {
                key.clone()
            };
            if let Some(warning) = warning {
                self.model
                    .warnings
                    .push(format!("mesh '{mesh_name}': {warning}"));
            }
            let mesh_id = self.asset_id(ids, class::MESH, "Mesh", &mesh_name);
            let mesh_index = self.model.meshes.len() as u32;
            self.model.meshes.push(ModelMesh {
                name: mesh_name,
                file_id: mesh_id.into(),
                ..built
            });
            let materials = slots
                .into_iter()
                .map(|m| match m {
                    Some(m) => Some(self.material(m)),
                    // The legacy importer gives a slot without material one named "No Name".
                    None if self.settings.material_import == MaterialImportMode::Legacy => {
                        Some(self.no_name_material())
                    }
                    None => None,
                })
                .collect();
            let weights: Vec<f32> = self
                .blend_channels(mesh)
                .iter()
                .map(|c| {
                    if self.settings.import_deform_percent {
                        (c.weight * 100.0) as f32
                    } else {
                        0.0
                    }
                })
                .collect();
            let renderer = if skin.is_some() || !weights.is_empty() {
                let bones: Vec<u32> = skin.map_or_else(Vec::new, |skin| {
                    skin.clusters
                        .iter()
                        .map(|c| {
                            c.bone_node
                                .as_ref()
                                .and_then(|b| self.node_index.get(&b.element.element_id).copied())
                                .unwrap_or(0)
                        })
                        .collect()
                });
                // The root bone: the first bone none of whose ancestors is a bone.
                let is_bone: HashSet<u32> = bones.iter().copied().collect();
                let root_bone = bones.iter().copied().find(|&b| {
                    let mut parent = self.model.nodes[b as usize].parent;
                    while let Some(p) = parent {
                        if is_bone.contains(&p) {
                            return false;
                        }
                        parent = self.model.nodes[p as usize].parent;
                    }
                    true
                });
                ModelRenderer {
                    kind: RendererKind::SkinnedMesh,
                    mesh: mesh_index,
                    materials,
                    renderer: self
                        .hierarchy_id(
                            ids,
                            class::SKINNED_MESH_RENDERER,
                            "SkinnedMeshRenderer",
                            &key,
                            Some(ROOT_SKINNED_MESH_RENDERER_ID),
                        )
                        .into(),
                    filter: None,
                    bones,
                    root_bone,
                    blend_shape_weights: weights,
                    local_bounds: None,
                }
            } else {
                ModelRenderer {
                    kind: RendererKind::Mesh,
                    mesh: mesh_index,
                    materials,
                    renderer: self
                        .hierarchy_id(
                            ids,
                            class::MESH_RENDERER,
                            "MeshRenderer",
                            &key,
                            Some(ROOT_MESH_RENDERER_ID),
                        )
                        .into(),
                    filter: Some(
                        self.hierarchy_id(
                            ids,
                            class::MESH_FILTER,
                            "MeshFilter",
                            &key,
                            Some(ROOT_MESH_FILTER_ID),
                        )
                        .into(),
                    ),
                    bones: Vec::new(),
                    root_bone: None,
                    blend_shape_weights: Vec::new(),
                    local_bounds: None,
                }
            };
            self.model.nodes[index].renderer = Some(renderer);
        }
        Ok(())
    }

    /// The model material for a ufbx material, added on first use.
    fn material(&mut self, material: &ufbx::Material) -> u32 {
        if let Some(&index) = self.material_index.get(&material.element.element_id) {
            return index;
        }
        // Same-name source materials bind the first used embedded material, including
        // its properties (6000.0.71f1 SharedNames and SharedNamesReverse captures).
        if let Some(index) = self.model.materials.iter().position(|m| {
            m.name == material.element.name.as_ref()
                && m.description != MaterialDescription::default()
        }) {
            let index = index as u32;
            self.material_index
                .insert(material.element.element_id, index);
            return index;
        }
        let index = self.model.materials.len() as u32;
        self.material_index
            .insert(material.element.element_id, index);
        self.model.materials.push(ModelMaterial {
            name: material.element.name.to_string(),
            description: if self.obj {
                let mut description = materials::describe_obj(material, self.obj_diffuse);
                if self.settings.material_import == MaterialImportMode::Legacy
                    && self.obj_diffuse.is_none()
                    && !self.settings.use_srgb_material_color
                {
                    if let Some((_, color)) = description
                        .vectors
                        .iter_mut()
                        .find(|(n, _)| n == "DiffuseColor")
                    {
                        for channel in &mut color[..3] {
                            *channel = if *channel <= 0.0031308 {
                                12.92 * *channel
                            } else {
                                1.055 * channel.powf(1.0 / 2.4) - 0.055
                            };
                        }
                    }
                }
                description
            } else {
                materials::describe(material)
            },
            file_id: FileId {
                id: 0,
                source: FileIdSource::Derived,
            },
            remap: None,
        });
        index
    }

    /// One LODGroup on the root for every renderer node named `<anything>_LOD<n>` (case
    /// sensitive), level n. Level i < last switches at 0.25 / 2^i of the screen height, the last
    /// at 0.01; the group's bounds are the levels' renderers' in the root's space (measured:
    /// tests/projects/modelimport).
    fn lod_group(&mut self, ids: &mut FileIdAllocator) {
        let mut levels: Vec<Vec<u32>> = Vec::new();
        for (index, node) in self.model.nodes.iter().enumerate() {
            let Some(level) = lod_level(&node.name) else {
                continue;
            };
            if node.renderer.is_none() {
                continue;
            }
            if levels.len() <= level {
                levels.resize(level + 1, Vec::new());
            }
            levels[level].push(index as u32);
        }
        if levels.is_empty() {
            return;
        }
        let (mut min, mut max) = ([f32::MAX; 3], [f32::MIN; 3]);
        for &index in levels.iter().flatten() {
            let node = &self.model.nodes[index as usize];
            let mesh = &self.model.meshes[node.renderer.as_ref().unwrap().mesh as usize];
            for corner in 0..8 {
                let local = [0, 1, 2].map(|axis| {
                    if corner & (1 << axis) == 0 {
                        mesh.bounds_min[axis]
                    } else {
                        mesh.bounds_max[axis]
                    }
                });
                let p = self.to_root(index, local);
                for axis in 0..3 {
                    min[axis] = min[axis].min(p[axis]);
                    max[axis] = max[axis].max(p[axis]);
                }
            }
        }
        let count = levels.len();
        let lods = levels
            .into_iter()
            .enumerate()
            .map(|(i, renderers)| ModelLod {
                screen_relative_height: if i + 1 == count {
                    0.01
                } else {
                    0.25 / (1u32 << i) as f32
                },
                renderers,
            })
            .collect();
        let file_id = self
            .hierarchy_id(ids, class::LOD_GROUP, "LODGroup", ROOT_NODE_NAME, None)
            .into();
        self.model.lod_group = Some(ModelLodGroup {
            file_id,
            lods,
            local_reference_point: [0, 1, 2].map(|a| (min[a] + max[a]) * 0.5),
            size: (0..3).map(|a| max[a] - min[a]).fold(0.0, f32::max),
        });
    }

    /// A point of node `index`'s space in the root's.
    fn to_root(&self, index: u32, mut p: [f32; 3]) -> [f32; 3] {
        let mut current = Some(index);
        while let Some(i) = current {
            let node = &self.model.nodes[i as usize];
            if node.parent.is_none() {
                break;
            }
            let scaled = [0, 1, 2].map(|a| p[a] * node.scale[a]);
            let rotated = rotate(node.rotation, scaled);
            p = [0, 1, 2].map(|a| rotated[a] + node.position[a]);
            current = node.parent;
        }
        p
    }

    /// The legacy importer's material for slots the file assigns none.
    fn no_name_material(&mut self) -> u32 {
        const NO_NAME: &str = "No Name";
        if let Some(index) = self
            .model
            .materials
            .iter()
            .position(|m| m.name == NO_NAME && m.description == MaterialDescription::default())
        {
            return index as u32;
        }
        self.model.materials.push(ModelMaterial {
            name: NO_NAME.into(),
            description: MaterialDescription::default(),
            file_id: FileId {
                id: 0,
                source: FileIdSource::Derived,
            },
            remap: None,
        });
        self.model.materials.len() as u32 - 1
    }

    /// Material fileIDs (by name) and `externalObjects` remaps.
    fn assign_material_ids(&mut self, imp: &Value, ids: &mut FileIdAllocator) {
        let remaps = material_remaps(imp);
        let legacy = self.legacy;
        for material in &mut self.model.materials {
            material.file_id = if legacy {
                ids.id(class::MATERIAL, &material.name, None)
            } else {
                ids.hashed(class::MATERIAL, "Material", &material.name)
            }
            .into();
            material.remap = remaps
                .iter()
                .find(|(name, _)| *name == material.name)
                .map(|(_, r)| *r);
        }
    }

    /// The mesh of a node, and the material of each submesh.
    fn build_mesh(
        &self,
        mesh: &'a ufbx::Mesh,
        node: &'a ufbx::Node,
        skin: Option<&ufbx::SkinDeformer>,
        offset: &pivots::GeometryOffset,
    ) -> Result<(ModelMesh, Vec<Option<&'a ufbx::Material>>, Option<String>)> {
        let settings = self.settings;
        let with_normals = settings.normals != NormalMode::None;
        let mut uv_sets: Vec<&ufbx::VertexVec2> =
            mesh.uv_sets.iter().take(4).map(|s| &s.vertex_uv).collect();
        if settings.swap_uv_channels && uv_sets.len() >= 2 {
            uv_sets.swap(0, 1);
        }
        let with_colors = mesh.vertex_color.exists;

        let mut out = ModelMesh {
            name: String::new(),
            file_id: FileId {
                id: 0,
                source: FileIdSource::Derived,
            },
            positions: Vec::new(),
            normals: Vec::new(),
            tangents: Vec::new(),
            uvs: vec![Vec::new(); uv_sets.len()],
            colors: Vec::new(),
            submeshes: Vec::new(),
            bone_weights: Vec::new(),
            bind_poses: Vec::new(),
            bounds_min: [0.0; 3],
            bounds_max: [0.0; 3],
            blend_shapes: Vec::new(),
        };
        let channels = self.blend_channels(mesh);
        // The control points with the node's geometric transform and pivot offset baked in.
        let vertices: std::borrow::Cow<[ufbx::Vec3]> = if offset.is_identity() {
            std::borrow::Cow::Borrowed(&*mesh.vertices)
        } else {
            mesh.vertices.iter().map(|&v| offset.point(v)).collect()
        };
        // Control points as the importer welds them: in file order (an OBJ's in first-use order),
        // equal positions with equal skin weights made one (with blend shapes, only the same point).
        let mut welded = vec![0u32; mesh.vertices.len()];
        let mut points: Vec<u32> = Vec::new();
        let mut file_positions: Vec<[f32; 3]> = Vec::new();
        {
            let mut first: HashMap<Vec<u32>, u32> = HashMap::new();
            let mut key = Vec::new();
            for v in self.control_point_order(mesh) {
                // Mirrored into Unity's handedness, unscaled.
                let p = vertices[v as usize];
                let p = [-p.x as f32, p.y as f32, p.z as f32];
                key.clear();
                key.extend(p.map(|c| (c + 0.0).to_bits()));
                if let Some(skin) = skin {
                    let w = bone_weight(skin, v as usize);
                    key.extend(w.weights.map(f32::to_bits));
                    key.extend(w.bones.map(u32::from));
                }
                if !channels.is_empty() || !settings.weld_vertices {
                    key.push(v);
                }
                welded[v as usize] = *first.entry(key.clone()).or_insert_with(|| {
                    points.push(v);
                    file_positions.push(p);
                    points.len() as u32 - 1
                });
            }
        }

        // Lightmap UVs replace UV2 (measured: tests/projects/lightmapuvs).
        let mut warning = None;
        let lightmap = settings.secondary_uv.as_ref().and_then(|unwrap| {
            match self.lightmap_uvs(mesh, &vertices, uv_sets.first().copied(), unwrap, &welded) {
                Ok(lightmap) => Some(lightmap),
                Err(error) => {
                    warning = Some(format!(
                        "lightmap UVs are not generated: {error}; the mesh keeps the file's UV2, \
                         so baked lightmaps do not land on it (not supported)"
                    ));
                    None
                }
            }
        });
        if lightmap.is_some() && out.uvs.len() < 2 {
            out.uvs.resize(2, Vec::new());
        }
        // One submesh per used material, in first face-use order (measured on Unity
        // 6000.0.71f1: modelimport/material_slots.py, including per-instance bindings).
        // Keep this permutation shared by the face assignments and renderer slots;
        // sorting material names or traversing faces by material changes API answers.
        let mut part_of = vec![None; mesh.faces.len()];
        let mut materials = Vec::new();
        if mesh.material_parts.is_empty() {
            part_of.fill(Some(0));
            materials.push(None);
        } else {
            for &part in mesh.material_part_usage_order.iter() {
                let p = &mesh.material_parts[part as usize];
                if p.num_triangles == 0 {
                    continue;
                }
                for &f in p.face_indices.iter() {
                    part_of[f as usize] = Some(materials.len());
                }
                // The node's material for the slot (instances may override the mesh's).
                materials.push(
                    node.materials
                        .get(p.index as usize)
                        .or_else(|| mesh.materials.get(p.index as usize))
                        .map(|m| m.as_ref()),
                );
            }
        }
        out.submeshes = vec![Vec::new(); materials.len()];

        // The faces' corners ("wedges") in face order: triangles and quads as they are (in Unity's
        // winding), larger polygons as triangles; degenerate faces are dropped (`face_pieces`).
        let mut polygons: Vec<(Vec<u32>, Vec<usize>)> = Vec::new();
        let mut wedges: Vec<Wedge> = Vec::new();
        let mut face_start: Vec<u32> = vec![0];
        let mut triangles: Vec<TriangleCorners> = Vec::new();
        let mut discarded = 0usize;
        let point = |c: u32| welded[mesh.vertex_indices[c as usize] as usize];
        for (face_index, face) in mesh.faces.iter().enumerate() {
            let Some(part) = part_of[face_index] else {
                continue;
            };
            let range = face.index_begin as usize..(face.index_begin + face.num_indices) as usize;
            polygons.push((
                range.clone().map(|c| mesh.vertex_indices[c]).collect(),
                range.clone().collect(),
            ));
            let face_pieces = face_pieces(mesh, &vertices, *face, point)?;
            discarded += face_pieces.discarded as usize;
            let pieces: Vec<&[u32]> = match &face_pieces.quad {
                Some(quad) => vec![&quad[..]],
                None => face_pieces.triangles.iter().map(|t| &t[..]).collect(),
            };
            for (t, piece) in pieces.into_iter().enumerate() {
                let p: Vec<u32> = piece.iter().map(|&c| point(c)).collect();
                let first = wedges.len();
                for (&c, &point) in piece.iter().zip(&p) {
                    wedges.push(Wedge {
                        corner: c as usize,
                        face: face_index,
                        point,
                    });
                }
                face_start.push(wedges.len() as u32);
                // A quad becomes the triangles (0, 1, 2) and (0, 2, 3) after the normals.
                let split: &[[usize; 3]] = if piece.len() == 4 {
                    &[[0, 1, 2], [0, 2, 3]]
                } else {
                    &[[0, 1, 2]]
                };
                for (i, tri) in split.iter().enumerate() {
                    let t = t + i;
                    triangles.push(TriangleCorners {
                        part,
                        wedges: tri.map(|k| first + k),
                        uv2: lightmap.as_ref().map(|l| l.uvs[l.first[face_index] + t]),
                    });
                }
            }
        }
        if discarded > 0 {
            let note = format!(
                "{discarded} self-intersecting polygon(s) of mesh '{}' are discarded, as the Unity \
                 importer does",
                mesh.element.name
            );
            warning = Some(match warning {
                Some(w) => format!("{w}; {note}"),
                None => note,
            });
        }
        let corners: Vec<u32> = wedges.iter().map(|w| w.point).collect();

        // Normals per wedge (mirrored): the file's, or calculated (also when the file has none);
        // then scaled as the mesh is and renormalized.
        let normals: Vec<[f32; 3]> = if !with_normals {
            Vec::new()
        } else if settings.normals == NormalMode::Import && mesh.vertex_normal.exists {
            wedges
                .iter()
                .map(|w| {
                    let n = offset.direction(mesh.vertex_normal[w.corner]);
                    [-n.x as f32, n.y as f32, n.z as f32]
                })
                .collect()
        } else {
            // Smoothing groups per wedge: an OBJ's `s` groups, an FBX's per-polygon smoothing.
            let obj = self.obj_smoothing.get(mesh.element.typed_id as usize);
            let smoothing = match obj {
                Some(groups) if !groups.is_empty() => {
                    Some(wedges.iter().map(|w| groups[w.face]).collect::<Vec<u32>>())
                }
                _ if self.obj => None,
                _ => self
                    .fbx_smoothing
                    .get(mesh.element.typed_id as usize)
                    .and_then(Option::as_ref)
                    .map(|groups| wedges.iter().map(|w| groups[w.face]).collect()),
            };
            normals::calculate(
                &file_positions,
                &normals::Faces {
                    corners: &corners,
                    start: &face_start,
                },
                normals::Weighting::from_mode(settings.normal_calculation),
                normals::SmoothingSource::from_mode(settings.normal_smoothing_source),
                settings.normal_smoothing_angle,
                smoothing.as_deref(),
            )
        };
        let inverse_scale = [1.0 / self.scale; 3];
        let normals: Vec<[f32; 3]> = normals
            .into_iter()
            .map(|n| normals::transform_normal(n, inverse_scale))
            .collect();
        let uv = |set: usize, w: &Wedge| -> [f32; 2] {
            uv_sets.get(set).map_or([0.0; 2], |s| {
                let v = s[w.corner];
                [v.x as f32, v.y as f32]
            })
        };
        let positions: Vec<[f32; 3]> = points
            .iter()
            .map(|&v| self.position(vertices[v as usize]))
            .collect();
        // Tangents per wedge: the file's (with imported normals), or MikkTSpace on the wedges.
        let tangents: Vec<[f32; 4]> = match settings.tangents {
            _ if !with_normals => Vec::new(),
            TangentMode::Import
                if settings.normals == NormalMode::Import
                    && mesh.vertex_normal.exists
                    && mesh.vertex_tangent.exists
                    && mesh.vertex_bitangent.exists =>
            {
                wedges
                    .iter()
                    .map(|w| {
                        let n = offset.direction(mesh.vertex_normal[w.corner]);
                        let t = offset.direction(mesh.vertex_tangent[w.corner]);
                        let b = offset.direction(mesh.vertex_bitangent[w.corner]);
                        // Handedness of the source frame, flipped by the mirror.
                        let cross = [
                            n.y * t.z - n.z * t.y,
                            n.z * t.x - n.x * t.z,
                            n.x * t.y - n.y * t.x,
                        ];
                        let dot = cross[0] * b.x + cross[1] * b.y + cross[2] * b.z;
                        let w = if dot < 0.0 { 1.0 } else { -1.0 };
                        [-t.x as f32, t.y as f32, t.z as f32, w]
                    })
                    .collect()
            }
            TangentMode::Calculate => {
                let wedge_positions: Vec<[f32; 3]> =
                    corners.iter().map(|&p| positions[p as usize]).collect();
                let uv0: Vec<[f32; 2]> = wedges.iter().map(|w| uv(0, w)).collect();
                tangent_space::face_tangents(&wedge_positions, &normals, &uv0, &face_start)
            }
            _ => Vec::new(),
        };
        let with_tangents = !tangents.is_empty();

        // Vertices: each control point's first wedge makes its vertex; a later wedge reuses the
        // newest vertex of its control point with the same attributes, or makes one after them.
        // Points no face uses are left out, unless vertex optimization is off.
        let mut used = vec![
            if settings.keep_unused_vertices {
                0
            } else {
                u32::MAX
            };
            points.len()
        ];
        let mut used_points = Vec::new();
        for &p in &corners {
            if used[p as usize] == u32::MAX {
                used[p as usize] = 0;
            }
        }
        for (p, slot) in used.iter_mut().enumerate() {
            if *slot == 0 {
                *slot = used_points.len() as u32;
                used_points.push(p);
            }
        }
        let count = used_points.len();
        // Each vertex's control point (shapes move control points), each corner's vertex.
        let mut control_points: Vec<u32> = used_points.iter().map(|&p| points[p]).collect();
        out.positions = used_points.iter().map(|&p| positions[p]).collect();
        if with_normals {
            out.normals = vec![[0.0; 3]; count];
        }
        if with_tangents {
            out.tangents = vec![[0.0; 4]; count];
        }
        for uvs in &mut out.uvs {
            *uvs = vec![[0.0; 2]; count];
        }
        if with_colors {
            out.colors = vec![[0.0; 4]; count];
        }
        if let Some(skin) = skin {
            out.bone_weights = control_points
                .iter()
                .map(|&v| bone_weight(skin, v as usize))
                .collect();
        }
        let color = |w: &Wedge| {
            let v = mesh.vertex_color[w.corner];
            [v.x as f32, v.y as f32, v.z as f32, v.w as f32]
        };
        let threshold = normals::split_threshold();
        let mut splitter = normals::Splitter::new(count);
        let mut corner_vertex = vec![u32::MAX; mesh.num_indices];
        for triangle in &triangles {
            // A baked handedness-reversing axis conversion turns each triangle before its
            // corners make vertices, so split vertices follow the turned order (measured,
            // tests/projects/modelaxis MaxZupBake).
            let order = if self.reverse_winding {
                [0, 2, 1]
            } else {
                [0, 1, 2]
            };
            for k in order {
                let i = triangle.wedges[k];
                let w = &wedges[i];
                let uv = |set: usize, w: &Wedge| match (set, triangle.uv2) {
                    (1, Some(uv2)) => uv2[k],
                    _ => uv(set, w),
                };
                let p = used[w.point as usize];
                let fill = |out: &mut ModelMesh, v: usize| {
                    if with_normals {
                        out.normals[v] = normals[i];
                    }
                    if with_tangents {
                        out.tangents[v] = tangents[i];
                    }
                    for (set, uvs) in out.uvs.iter_mut().enumerate() {
                        uvs[v] = uv(set, w);
                    }
                    if with_colors {
                        out.colors[v] = color(w);
                    }
                };
                let same = |v: u32| {
                    let v = v as usize;
                    let close = |a: [f32; 2], b: [f32; 2]| {
                        let d = [a[0] - b[0], a[1] - b[1]];
                        d[1] * d[1] + d[0] * d[0] <= normals::UV_WELD
                    };
                    (!with_normals || normals::dot(out.normals[v], normals[i]) >= threshold)
                        && (!with_colors || out.colors[v] == color(w))
                        && out
                            .uvs
                            .iter()
                            .enumerate()
                            .all(|(set, uvs)| close(uvs[v], uv(set, w)))
                        && (!with_tangents || {
                            let (a, b) = (out.tangents[v], tangents[i]);
                            normals::dot([a[0], a[1], a[2]], [b[0], b[1], b[2]]) >= threshold
                                && (b[3] - a[3]).abs() <= normals::W_WELD
                        })
                };
                let vertex = if splitter.is_new(p) {
                    fill(&mut out, p as usize);
                    splitter.add(p, p);
                    p
                } else if let Some(v) = splitter.find(p, same) {
                    v
                } else {
                    let v = out.positions.len() as u32;
                    out.positions.push(out.positions[p as usize]);
                    control_points.push(control_points[p as usize]);
                    if with_normals {
                        out.normals.push([0.0; 3]);
                    }
                    if with_tangents {
                        out.tangents.push([0.0; 4]);
                    }
                    for uvs in &mut out.uvs {
                        uvs.push([0.0; 2]);
                    }
                    if with_colors {
                        out.colors.push([0.0; 4]);
                    }
                    if skin.is_some() {
                        out.bone_weights.push(out.bone_weights[p as usize]);
                    }
                    fill(&mut out, v as usize);
                    splitter.add(p, v);
                    v
                };
                corner_vertex[w.corner] = vertex;
                out.submeshes[triangle.part].push(vertex);
            }
        }
        // A kept point without a face has default attributes: zero UVs and colours, and this
        // normal (measured, tests/projects/lightmapuvs; tangents are not measured).
        if with_normals {
            for p in 0..count as u32 {
                if splitter.is_new(p) {
                    out.normals[p as usize] = UNUSED_VERTEX_NORMAL;
                }
            }
        }
        if !channels.is_empty() {
            self.add_blend_shapes(
                mesh,
                &vertices,
                offset,
                &channels,
                &mut out,
                &control_points,
                &corner_vertex,
                &polygons,
                with_normals,
            );
        }
        if let Some(skin) = skin {
            out.bind_poses = skin
                .clusters
                .iter()
                .map(|c| self.matrix(&offset.bind(&c.geometry_to_bone)))
                .collect();
        }
        // The bounds hold every blend shape frame's positions too (measured,
        // tests/projects/modelaxis Skinned set).
        if let Some(first) = out.positions.first() {
            let (mut min, mut max) = (*first, *first);
            let shaped = out
                .blend_shapes
                .iter()
                .flat_map(|s| &s.frames)
                .flat_map(|f| out.positions.iter().zip(&f.vertices))
                .map(|(p, d)| [p[0] + d[0], p[1] + d[1], p[2] + d[2]]);
            for p in out.positions.iter().copied().chain(shaped) {
                for i in 0..3 {
                    min[i] = min[i].min(p[i]);
                    max[i] = max[i].max(p[i]);
                }
            }
            out.bounds_min = min;
            out.bounds_max = max;
        }
        Ok((out, materials, warning))
    }

    /// The control points in the importer's order: the file's, an OBJ's in the order the faces'
    /// corners first use them.
    fn control_point_order(&self, mesh: &ufbx::Mesh) -> Vec<u32> {
        if !self.obj {
            return (0..mesh.vertices.len() as u32).collect();
        }
        let mut seen = vec![false; mesh.vertices.len()];
        let mut order = Vec::with_capacity(mesh.vertices.len());
        for &v in mesh.vertex_indices.iter() {
            if !std::mem::replace(&mut seen[v as usize], true) {
                order.push(v);
            }
        }
        order.extend((0..mesh.vertices.len() as u32).filter(|&v| !seen[v as usize]));
        order
    }

    /// UV2 per triangle corner from the lightmap unwrap of the whole mesh. Unity unwraps the
    /// mesh as it holds it after triangulation and welding: the control points in file order (an
    /// OBJ's in the order the faces' corners first use them) with each position kept once, the
    /// triangles in face order, each its own source polygon (inferred; checked by the unwrap
    /// against tests/projects/lightmapuvs).
    fn lightmap_uvs(
        &self,
        mesh: &ufbx::Mesh,
        vertices: &[ufbx::Vec3],
        uv0: Option<&ufbx::VertexVec2>,
        settings: &SecondaryUvSettings,
        welded: &[u32],
    ) -> Result<LightmapUvs, String> {
        if !self.settings.weld_vertices {
            return Err("meshes without weldVertices are not measured".into());
        }
        let order = self.control_point_order(mesh);
        // Equal positions weld to the first (compared as floats: -0 and 0 weld).
        let mut index = vec![0u32; mesh.vertices.len()];
        let mut positions: Vec<[f32; 3]> = Vec::with_capacity(order.len());
        let mut first: HashMap<[u32; 3], u32> = HashMap::new();
        for &v in &order {
            let p = self.position(vertices[v as usize]);
            index[v as usize] = *first
                .entry(p.map(|c| (c + 0.0).to_bits()))
                .or_insert_with(|| {
                    positions.push(p);
                    positions.len() as u32 - 1
                });
        }
        let mut triangles = Vec::new();
        let mut uvs = Vec::new();
        let mut first = Vec::with_capacity(mesh.faces.len());
        for &face in mesh.faces.iter() {
            first.push(triangles.len());
            // The mesh's own triangles (over its welded points), numbered as `build_mesh` does.
            let pieces = face_pieces(mesh, vertices, face, |c| {
                welded[mesh.vertex_indices[c as usize] as usize]
            })
            .map_err(|e| e.to_string())?;
            let quad = pieces.quad.map(|[a, b, c, d]| [[a, b, c], [a, c, d]]);
            for t in quad.iter().flatten().chain(&pieces.triangles) {
                triangles
                    .push([0, 1, 2].map(|k| index[mesh.vertex_indices[t[k] as usize] as usize]));
                if let Some(uv) = uv0 {
                    uvs.push([0, 1, 2].map(|k| {
                        let v = uv[t[k] as usize];
                        [v.x as f32, v.y as f32]
                    }));
                }
            }
        }
        // Unused control points are dropped, in order.
        let mut used = vec![u32::MAX; positions.len()];
        let mut kept = Vec::with_capacity(positions.len());
        for t in &triangles {
            for &v in t {
                used[v as usize] = 0;
            }
        }
        for (v, slot) in used.iter_mut().enumerate() {
            if *slot == 0 {
                *slot = kept.len() as u32;
                kept.push(positions[v]);
            }
        }
        for t in &mut triangles {
            *t = t.map(|v| used[v as usize]);
        }
        let positions = kept;
        let polygons: Vec<u32> = (0..triangles.len() as u32).collect();
        let input = lightmap_uv::UnwrapInput {
            positions: &positions,
            triangles: &triangles,
            polygons: &polygons,
            uvs: uv0.map(|_| &uvs[..]),
        };
        let params = lightmap_uv::UnwrapParams::new(settings, &input)?;
        let uvs = lightmap_uv::unwrap(&input, &params)?;
        Ok(LightmapUvs { first, uvs })
    }

    /// The mesh's blend shapes: per frame the control points' offsets for each vertex, the
    /// normals' change (the file's, or calculated as Unity does: see
    /// [`tangent_space::shape_corner_normals`]) and the tangents' (MikkTSpace on the shaped mesh).
    #[allow(clippy::too_many_arguments)]
    fn add_blend_shapes(
        &self,
        mesh: &ufbx::Mesh,
        vertices: &[ufbx::Vec3],
        offset: &pivots::GeometryOffset,
        channels: &[&ufbx::BlendChannel],
        out: &mut ModelMesh,
        control_points: &[u32],
        corner_vertex: &[u32],
        polygons: &[(Vec<u32>, Vec<usize>)],
        with_normals: bool,
    ) {
        let settings = self.settings;
        let base: Vec<[f64; 3]> = vertices.iter().map(|v| [v.x, v.y, v.z]).collect();
        let cp_polygons: Vec<Vec<u32>> = polygons.iter().map(|(cps, _)| cps.clone()).collect();
        let uv0 = out.uvs.first().cloned().unwrap_or_default();
        // Tangent deltas compare MikkTSpace on the final vertices, shaped and not.
        let base_tangents = if out.tangents.is_empty() {
            Vec::new()
        } else {
            tangent_space::mikk_tangents(&out.positions, &out.normals, &uv0, &out.submeshes)
        };
        for channel in channels {
            let mut frames = Vec::new();
            for key in channel.keyframes.iter() {
                let shape = &key.shape;
                let mut offsets = vec![[0f64; 3]; mesh.num_vertices];
                let mut normal_offsets = vec![[0f64; 3]; mesh.num_vertices];
                for (i, &v) in shape.offset_vertices.iter().enumerate() {
                    if let Some(&o) = shape.position_offsets.get(i)
                        && let Some(slot) = offsets.get_mut(v as usize)
                    {
                        let o = offset.delta(o);
                        *slot = [o.x, o.y, o.z];
                    }
                    if let Some(&n) = shape.normal_offsets.get(i)
                        && let Some(slot) = normal_offsets.get_mut(v as usize)
                    {
                        let n = offset.direction(n);
                        *slot = [n.x, n.y, n.z];
                    }
                }
                let to_unity = |o: [f64; 3]| {
                    self.position(ufbx::Vec3 {
                        x: o[0],
                        y: o[1],
                        z: o[2],
                    })
                };
                let vertices: Vec<[f32; 3]> = control_points
                    .iter()
                    .map(|&cp| to_unity(offsets[cp as usize]))
                    .collect();
                let normals: Vec<[f32; 3]> = match settings.blend_shape_normals {
                    _ if !with_normals => Vec::new(),
                    NormalMode::None => Vec::new(),
                    NormalMode::Import => control_points
                        .iter()
                        .map(|&cp| {
                            let n = normal_offsets[cp as usize];
                            [-n[0] as f32, n[1] as f32, n[2] as f32]
                        })
                        .collect(),
                    NormalMode::Calculate => {
                        let shaped: Vec<[f64; 3]> = base
                            .iter()
                            .zip(&offsets)
                            .map(|(p, o)| [p[0] + o[0], p[1] + o[1], p[2] + o[2]])
                            .collect();
                        let corner_normals = tangent_space::shape_corner_normals(
                            &base,
                            &shaped,
                            &cp_polygons,
                            settings.normal_smoothing_angle as f64,
                        );
                        let mut deltas = vec![[0f32; 3]; out.positions.len()];
                        let mut done = vec![false; out.positions.len()];
                        for ((_, corners), normals) in polygons.iter().zip(&corner_normals) {
                            for (&c, n) in corners.iter().zip(normals) {
                                let v = corner_vertex[c];
                                if v == u32::MAX || done[v as usize] {
                                    continue;
                                }
                                done[v as usize] = true;
                                let base_normal = out.normals[v as usize];
                                deltas[v as usize] = [
                                    -n[0] as f32 - base_normal[0],
                                    n[1] as f32 - base_normal[1],
                                    n[2] as f32 - base_normal[2],
                                ];
                            }
                        }
                        deltas
                    }
                };
                // Tangents: MikkTSpace on the shaped vertices and normals.
                let tangents = if out.tangents.is_empty() {
                    Vec::new()
                } else {
                    let positions: Vec<[f32; 3]> = out
                        .positions
                        .iter()
                        .zip(&vertices)
                        .map(|(p, d)| [p[0] + d[0], p[1] + d[1], p[2] + d[2]])
                        .collect();
                    let shaped_normals: Vec<[f32; 3]> = out
                        .normals
                        .iter()
                        .enumerate()
                        .map(|(i, n)| {
                            let d = normals.get(i).copied().unwrap_or([0.0; 3]);
                            let v = [n[0] + d[0], n[1] + d[1], n[2] + d[2]];
                            let l = (v[0] * v[0] + v[1] * v[1] + v[2] * v[2]).sqrt().max(1e-20);
                            [v[0] / l, v[1] / l, v[2] / l]
                        })
                        .collect();
                    let shaped = tangent_space::mikk_tangents(
                        &positions,
                        &shaped_normals,
                        &uv0,
                        &out.submeshes,
                    );
                    shaped
                        .iter()
                        .zip(&base_tangents)
                        .map(|(t, b)| [t[0] - b[0], t[1] - b[1], t[2] - b[2]])
                        .collect()
                };
                frames.push(ModelBlendFrame {
                    weight: (key.target_weight * 100.0) as f32,
                    vertices,
                    normals,
                    tangents,
                });
            }
            out.blend_shapes.push(ModelBlendShape {
                name: channel.element.name.to_string(),
                frames,
            });
        }
    }

    /// The blend channels of a mesh that have frames, when blend shapes are imported.
    fn blend_channels(&self, mesh: &'a ufbx::Mesh) -> Vec<&'a ufbx::BlendChannel> {
        if !self.settings.import_blend_shapes {
            return Vec::new();
        }
        mesh.blend_deformers
            .iter()
            .flat_map(|d| d.channels.iter())
            .filter(|c| !c.keyframes.is_empty())
            .map(|c| c.as_ref())
            .collect()
    }

    /// An affine ufbx matrix in Unity's space (mirrored on X, translation scaled), column-major.
    fn matrix(&self, m: &ufbx::Matrix) -> [f32; 16] {
        let s = self.scale as f64;
        // F·M·F with F = diag(-1, 1, 1): entries mixing x with y/z change sign.
        [
            m.m00,
            -m.m10,
            -m.m20,
            0.0, //
            -m.m01,
            m.m11,
            m.m21,
            0.0, //
            -m.m02,
            m.m12,
            m.m22,
            0.0, //
            -m.m03 * s,
            m.m13 * s,
            m.m23 * s,
            1.0,
        ]
        .map(|v| v as f32)
    }
}

/// One entry of the meta's `clipAnimations`, or a whole take.
struct ClipRange {
    name: String,
    take: String,
    internal_id: Option<i64>,
    /// Frames on the take's timeline.
    first: f64,
    last: f64,
    wrap_mode: i32,
    loop_time: bool,
    loop_blend: bool,
    cycle_offset: f32,
    has_additive_reference: bool,
    additive_frame: Option<f64>,
    /// Times normalized to the clip's length, as the importer stores them.
    events: Vec<ClipEvent>,
    /// Times normalized to the clip's length.
    parameters: Vec<ParameterCurve>,
    mask: ClipMask,
    settings: ClipSettings,
    preview: bool,
}

impl ClipRange {
    fn take(name: &str, first: f64, last: f64) -> Self {
        Self {
            name: name.to_string(),
            take: name.to_string(),
            internal_id: None,
            first,
            last,
            wrap_mode: 0,
            loop_time: false,
            loop_blend: false,
            cycle_offset: 0.0,
            has_additive_reference: false,
            additive_frame: None,
            events: Vec::new(),
            parameters: Vec::new(),
            mask: ClipMask::None,
            settings: ClipSettings::default(),
            preview: false,
        }
    }

    fn read(item: &Value) -> Self {
        let name = item
            .get("name")
            .and_then(Value::as_str)
            .unwrap_or_default()
            .to_string();
        let flag = |key: &str, default: bool| boolean(item, key).unwrap_or(default);
        let defaults = ClipSettings::default();
        let mask = match int(item, "maskType").unwrap_or(3) {
            0 => ClipMask::Paths(
                item.get("transformMask")
                    .and_then(Value::as_seq)
                    .unwrap_or_default()
                    .iter()
                    .map(|e| {
                        (
                            e.get("path")
                                .and_then(Value::as_str)
                                .unwrap_or_default()
                                .to_string(),
                            float(e, "weight").unwrap_or(1.0),
                        )
                    })
                    .collect(),
            ),
            1 => {
                let source = item.get("maskSource");
                let guid = source.and_then(|s| s.get("guid")).and_then(Value::as_str);
                match guid {
                    Some(guid) => ClipMask::Asset {
                        guid: guid.to_string(),
                        file_id: source.and_then(|s| int(s, "fileID")).unwrap_or(0),
                    },
                    None => ClipMask::None,
                }
            }
            _ => ClipMask::None,
        };
        Self {
            take: item
                .get("takeName")
                .and_then(Value::as_str)
                .map_or_else(|| name.clone(), str::to_string),
            internal_id: int(item, "internalID").filter(|&id| id != 0),
            first: float(item, "firstFrame").unwrap_or(0.0) as f64,
            last: float(item, "lastFrame").unwrap_or(0.0) as f64,
            wrap_mode: int(item, "wrapMode").unwrap_or(0) as i32,
            loop_time: flag("loopTime", false),
            loop_blend: flag("loopBlend", false),
            cycle_offset: float(item, "cycleOffset").unwrap_or(0.0),
            has_additive_reference: flag("hasAdditiveReferencePose", false),
            additive_frame: float(item, "additiveReferencePoseFrame").map(f64::from),
            events: item
                .get("events")
                .and_then(Value::as_seq)
                .unwrap_or_default()
                .iter()
                .map(|e| ClipEvent {
                    time: float(e, "time").unwrap_or(0.0),
                    function_name: e
                        .get("functionName")
                        .and_then(Value::as_str)
                        .unwrap_or_default()
                        .to_string(),
                    data: e
                        .get("data")
                        .and_then(Value::as_str)
                        .unwrap_or_default()
                        .to_string(),
                    float_parameter: float(e, "floatParameter").unwrap_or(0.0),
                    int_parameter: int(e, "intParameter").unwrap_or(0) as i32,
                    message_options: int(e, "messageOptions").unwrap_or(0) as i32,
                })
                .collect(),
            parameters: item
                .get("curves")
                .and_then(Value::as_seq)
                .unwrap_or_default()
                .iter()
                .map(|c| {
                    let curve = c.get("curve");
                    let keys = curve
                        .and_then(|v| v.get("curve"))
                        .and_then(Value::as_seq)
                        .unwrap_or_default();
                    ParameterCurve {
                        name: c
                            .get("name")
                            .and_then(Value::as_str)
                            .unwrap_or_default()
                            .to_string(),
                        keys: keys
                            .iter()
                            .map(|k| CurveKey {
                                time: float(k, "time").unwrap_or(0.0),
                                value: float(k, "value").unwrap_or(0.0),
                                in_slope: float(k, "inSlope").unwrap_or(0.0),
                                out_slope: float(k, "outSlope").unwrap_or(0.0),
                                tangent_mode: int(k, "tangentMode").unwrap_or(0) as i32,
                                weighted_mode: int(k, "weightedMode").unwrap_or(0) as i32,
                                in_weight: float(k, "inWeight").unwrap_or(1.0 / 3.0),
                                out_weight: float(k, "outWeight").unwrap_or(1.0 / 3.0),
                            })
                            .collect(),
                        pre_infinity: curve.and_then(|v| int(v, "preInfinity")).unwrap_or(2) as i32,
                        post_infinity: curve.and_then(|v| int(v, "postInfinity")).unwrap_or(2)
                            as i32,
                    }
                })
                .collect(),
            mask,
            settings: ClipSettings {
                mirror: flag("mirror", defaults.mirror),
                orientation_offset_y: float(item, "orientationOffsetY").unwrap_or(0.0),
                level: float(item, "level").unwrap_or(0.0),
                loop_blend_orientation: flag("loopBlendOrientation", false),
                loop_blend_position_y: flag("loopBlendPositionY", false),
                loop_blend_position_xz: flag("loopBlendPositionXZ", false),
                keep_original_orientation: flag("keepOriginalOrientation", false),
                keep_original_position_y: flag("keepOriginalPositionY", true),
                keep_original_position_xz: flag("keepOriginalPositionXZ", false),
                height_from_feet: flag("heightFromFeet", false),
            },
            name,
            preview: false,
        }
    }
}

impl ClipMask {
    /// Whether the curves of the transform at `path` stay. Listed with weight 0, or not listed
    /// when the mask lists any, the curves go. An asset mask is applied where the model's documents
    /// are made (it is another asset); here it keeps everything.
    pub fn keeps(&self, path: &str) -> bool {
        match self {
            ClipMask::Paths(paths) => mask_keeps(paths, path),
            ClipMask::None | ClipMask::Asset { .. } => true,
        }
    }
}

/// An AvatarMask's transform list: a path stays when listed with a weight above one half, or
/// when the list is empty.
pub fn mask_keeps(paths: &[(String, f32)], path: &str) -> bool {
    paths.is_empty() || paths.iter().any(|(p, w)| p == path && *w > 0.5)
}

/// The keys of `[start, end]` (seconds on the take), from `start`: keys exactly at both ends,
/// interpolated where the take has none (a range starting or ending between frames).
fn clip_range<const N: usize>(
    keys: &[Key<N>],
    start: f64,
    end: f64,
    interpolate: fn(&Key<N>, &Key<N>, f32) -> [f32; N],
) -> Vec<Key<N>> {
    const EPSILON: f64 = 1e-6;
    let at = |time: f64| -> Option<[f32; N]> {
        let t = time as f32;
        let after = keys.iter().position(|k| k.time >= t)?;
        if after == 0 || keys[after].time == t {
            return Some(keys[after].value);
        }
        let (a, b) = (&keys[after - 1], &keys[after]);
        Some(interpolate(a, b, (t - a.time) / (b.time - a.time)))
    };
    let mut out = Vec::new();
    let inside = |k: &&Key<N>| {
        let t = k.time as f64;
        t >= start - EPSILON && t <= end + EPSILON
    };
    if keys
        .first()
        .is_some_and(|k| (k.time as f64) < start - EPSILON)
        && !keys
            .iter()
            .any(|k| ((k.time as f64) - start).abs() <= EPSILON)
        && let Some(value) = at(start)
    {
        out.push(Key { time: 0.0, value });
    }
    out.extend(keys.iter().filter(inside).map(|k| Key {
        time: (k.time as f64 - start) as f32,
        value: k.value,
    }));
    if keys.last().is_some_and(|k| (k.time as f64) > end + EPSILON)
        && !keys
            .iter()
            .any(|k| ((k.time as f64) - end).abs() <= EPSILON)
        && let Some(value) = at(end)
    {
        out.push(Key {
            time: (end - start) as f32,
            value,
        });
    }
    out
}

/// The times a clip is resampled at: its start, every frame of the take after it, and its end
/// (a range from frame 4.5 has keys at 4.5, 5, 6, ...; measured, tests/projects/modelclips).
fn frame_times(start: f64, end: f64, fps: f64) -> Vec<f64> {
    const EPSILON: f64 = 1e-6;
    let mut times = vec![start];
    let first = (start * fps + EPSILON).floor() as i64 + 1;
    times.extend(
        (first..)
            .map(|frame| frame as f64 / fps)
            .take_while(|&t| t < end - EPSILON),
    );
    if end > start + EPSILON {
        times.push(end);
    }
    times
}

fn lerp_key<const N: usize>(a: &Key<N>, b: &Key<N>, t: f32) -> [f32; N] {
    std::array::from_fn(|i| a.value[i] + (b.value[i] - a.value[i]) * t)
}

/// Spherical interpolation between two rotation keys, as ufbx evaluates baked rotations.
fn slerp_key(a: &Key<4>, b: &Key<4>, t: f32) -> [f32; 4] {
    let (p, mut q) = (a.value, b.value);
    let mut dot: f32 = (0..4).map(|i| p[i] * q[i]).sum();
    if dot < 0.0 {
        q = q.map(|v| -v);
        dot = -dot;
    }
    let (wa, wb) = if dot > 0.9995 {
        (1.0 - t, t)
    } else {
        let theta = dot.min(1.0).acos();
        let s = theta.sin();
        (((1.0 - t) * theta).sin() / s, (t * theta).sin() / s)
    };
    let r: [f32; 4] = std::array::from_fn(|i| wa * p[i] + wb * q[i]);
    let m = r.iter().map(|v| v * v).sum::<f32>().sqrt();
    r.map(|v| v / m)
}

/// The level of a node named `<name>_LOD<n>`.
fn lod_level(name: &str) -> Option<usize> {
    let (_, digits) = name.rsplit_once("_LOD")?;
    if digits.is_empty() || !digits.bytes().all(|b| b.is_ascii_digit()) {
        return None;
    }
    digits.parse().ok()
}

/// `v` rotated by the unit quaternion `q` (`x, y, z, w`).
fn quat_mul(a: [f32; 4], b: [f32; 4]) -> [f32; 4] {
    let [x1, y1, z1, w1] = a;
    let [x2, y2, z2, w2] = b;
    [
        w1 * x2 + x1 * w2 + y1 * z2 - z1 * y2,
        w1 * y2 - x1 * z2 + y1 * w2 + z1 * x2,
        w1 * z2 + x1 * y2 - y1 * x2 + z1 * w2,
        w1 * w2 - x1 * x2 - y1 * y2 - z1 * z2,
    ]
}

fn rotate(q: [f32; 4], v: [f32; 3]) -> [f32; 3] {
    let u = [q[0], q[1], q[2]];
    let cross = |a: [f32; 3], b: [f32; 3]| {
        [
            a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0],
        ]
    };
    let t = cross(u, v).map(|c| 2.0 * c);
    let c = cross(u, t);
    [0, 1, 2].map(|a| v[a] + q[3] * t[a] + c[a])
}

/// A mesh's lightmap UVs: per face its first triangle, per triangle UV2 at its corners.
struct LightmapUvs {
    first: Vec<usize>,
    uvs: Vec<[[f32; 2]; 3]>,
}

/// A face as the importer keeps it: file corners in Unity's winding (mirroring X reverses the
/// corners after the first), as one quad split (0, 1, 2) and (0, 2, 3) after the normals, or as
/// triangles; nothing when the face is dropped.
#[derive(Debug, Default, PartialEq)]
struct FacePieces {
    quad: Option<[u32; 4]>,
    triangles: Vec<[u32; 3]>,
    /// The polygon's edges cross, so the importer discards it.
    discarded: bool,
}

/// The pieces of `face` over welded control points (`point` of a file corner), as the Unity
/// 6000.0.71f1 Editor makes them (tests/projects/lightmapuvs):
/// - triangles and quads first lose corners that repeat the previous corner's point (the
///   first of a run stays); three corners left make a triangle, four a quad, and a face left
///   with fewer, or still repeating a point (a quad folded onto its diagonal), is dropped.
///   Zero-area faces with distinct points stay.
/// - larger polygons are swept over their mirrored float32 positions (`triangulation`):
///   coincident corners merge, zero-area polygons give no triangles, crossing ones are
///   discarded. A triangle repeating a welded point is dropped.
fn face_pieces(
    mesh: &ufbx::Mesh,
    vertices: &[ufbx::Vec3],
    face: ufbx::Face,
    point: impl Fn(u32) -> u32,
) -> Result<FacePieces> {
    let (b, n) = (face.index_begin, face.num_indices);
    let mut pieces = FacePieces::default();
    if n < 3 {
        return Ok(pieces);
    }
    if n <= 4 {
        let mut kept: Vec<u32> = Vec::with_capacity(4);
        for c in b..b + n {
            if kept.last().is_none_or(|&k| point(k) != point(c)) {
                kept.push(c);
            }
        }
        if kept.len() > 1 && point(kept[0]) == point(kept[kept.len() - 1]) {
            kept.pop();
        }
        let points: Vec<u32> = kept.iter().map(|&c| point(c)).collect();
        if (0..points.len()).any(|i| points[i + 1..].contains(&points[i])) {
            return Ok(pieces);
        }
        match kept[..] {
            [c0, c1, c2] => pieces.triangles.push([c0, c2, c1]),
            [c0, c1, c2, c3] => pieces.quad = Some([c0, c3, c2, c1]),
            _ => {}
        }
        return Ok(pieces);
    }
    let positions: Vec<[f32; 3]> = (b..b + n)
        .map(|c| vertices[mesh.vertex_indices[c as usize] as usize])
        .map(|p| [-p.x as f32, p.y as f32, p.z as f32])
        .collect();
    let Some(indices) = triangulation::polygon(&positions)? else {
        pieces.discarded = true;
        return Ok(pieces);
    };
    for t in indices.chunks_exact(3) {
        let t = [b + t[0], b + t[1], b + t[2]];
        let p = t.map(&point);
        if p[0] != p[1] && p[1] != p[2] && p[0] != p[2] {
            pieces.triangles.push(t);
        }
    }
    Ok(pieces)
}

/// A face corner: its file corner, face and welded control point.
struct Wedge {
    corner: usize,
    face: usize,
    point: u32,
}

/// A triangle of the final mesh: its submesh, its face corners and generated UV2.
struct TriangleCorners {
    part: usize,
    wedges: [usize; 3],
    uv2: Option<[[f32; 2]; 3]>,
}

/// An OBJ file's smoothing group per face, per mesh in ufbx's order (`s off` and `s 0` are no
/// group); empty when no face has a group (the file has no smoothing then, as measured) or the
/// faces do not add up to the meshes'.
fn obj_smoothing_groups(bytes: &[u8], scene: &ufbx::Scene) -> Vec<Vec<u32>> {
    let mut group = 0;
    let mut faces = Vec::new();
    for line in bytes.split(|&b| b == b'\n') {
        let line = String::from_utf8_lossy(line);
        let mut words = line.split_whitespace();
        match words.next() {
            Some("s") => group = words.next().and_then(|w| w.parse().ok()).unwrap_or(0),
            Some("f") => faces.push(group),
            _ => {}
        }
    }
    if faces.iter().all(|&g| g == 0)
        || faces.len() != scene.meshes.iter().map(|m| m.faces.len()).sum::<usize>()
    {
        return Vec::new();
    }
    let mut rest = &faces[..];
    scene
        .meshes
        .iter()
        .map(|m| {
            let (mine, others) = rest.split_at(m.faces.len());
            rest = others;
            mine.to_vec()
        })
        .collect()
}

/// The four heaviest influences on a skin vertex, renormalized.
fn bone_weight(skin: &ufbx::SkinDeformer, vertex: usize) -> BoneWeight {
    let mut out = BoneWeight::default();
    let Some(v) = skin.vertices.get(vertex) else {
        return out;
    };
    let mut influences: Vec<(u32, f64)> = (v.weight_begin..v.weight_begin + v.num_weights)
        .map(|i| {
            let w = &skin.weights[i as usize];
            (w.cluster_index, w.weight)
        })
        .filter(|&(_, w)| w > 0.0)
        .collect();
    influences.sort_by(|a, b| b.1.total_cmp(&a.1));
    influences.truncate(4);
    let total: f64 = influences.iter().map(|i| i.1).sum();
    if total <= 0.0 {
        return out;
    }
    for (slot, (bone, weight)) in influences.into_iter().enumerate() {
        out.bones[slot] = bone as u16;
        out.weights[slot] = (weight / total) as f32;
    }
    out
}

mod axes;
mod fbx_smoothing;
mod lightmap_uv;
mod materials;
mod normals;
mod pivots;
mod tangent_space;
mod triangulation;

#[cfg(test)]
mod tests;
#[cfg(test)]
mod version_guard;

fn clip_ranges(imp: &Value) -> Vec<ClipRange> {
    imp.path("animations.clipAnimations")
        .and_then(Value::as_seq)
        .unwrap_or_default()
        .iter()
        .map(ClipRange::read)
        .collect()
}

fn material_remaps(imp: &Value) -> Vec<(String, ObjectRef)> {
    imp.get("externalObjects")
        .and_then(Value::as_seq)
        .unwrap_or_default()
        .iter()
        .filter_map(|e| {
            let first = e.get("first")?;
            let ty = first.get("type")?.as_str()?;
            if !ty.ends_with("Material") {
                return None;
            }
            Some((
                first.get("name")?.as_str()?.to_string(),
                e.get("second")?.as_ref()?,
            ))
        })
        .collect()
}

pub(crate) fn read_metadata_inputs(imp: &Value) {
    let _ = FileIdTable::read(imp);
    let _ = clip_ranges(imp);
    let _ = material_remaps(imp);
    let _ = animation_compression_off(imp);
    let _ = motion_node_name(imp);
}

/// Whether the node's default attribute (the one `DefaultAttributeIndex` selects, none when it is
/// absent or -1) is a skeleton. The FBX SDK reads a node's type from it; a node can have attributes
/// without a default one.
fn default_attribute_is_bone(node: &ufbx::Node) -> bool {
    let index = node
        .element
        .props
        .find_prop("DefaultAttributeIndex")
        .map_or(-1, |p| p.value_int);
    usize::try_from(index)
        .ok()
        .and_then(|i| node.all_attribs.get(i))
        .is_some_and(|attrib| attrib.type_ == ufbx::ElementType::Bone)
}

/// Which of the node's local translation, rotation and scale the take animates: those with an
/// animation curve node in one of its layers. The importer gives a clip curves for these only
/// (measured for translation alone, tests/projects/modeltoproot; rotation or scale alone and
/// curve nodes without curves are inferred), constant ones included (tests/projects/modelclips).
fn animated_channels(stack: &ufbx::AnimStack, node: &ufbx::Node) -> [bool; 3] {
    ["Lcl Translation", "Lcl Rotation", "Lcl Scaling"].map(|prop| {
        stack
            .layers
            .iter()
            .any(|layer| ufbx::find_anim_prop(layer, &node.element, prop).is_some())
    })
}

/// The keys of an animated channel, none for a channel the take leaves alone.
fn only<T>(animated: bool, keys: Vec<T>) -> Vec<T> {
    if animated { keys } else { Vec::new() }
}

fn animation_compression_off(imp: &Value) -> bool {
    int(imp, "animations.animationCompression") == Some(0)
}

fn motion_node_name(imp: &Value) -> Option<&str> {
    imp.path("animations.motionNodeName")
        .and_then(Value::as_str)
}
