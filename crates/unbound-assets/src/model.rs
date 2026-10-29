//! The model importer: FBX (and OBJ) through `ufbx`, converted the way Unity's `ModelImporter`
//! does, into the data the runtime needs to instantiate the model prefab.
//!
//! **Coordinates.** The file is first brought to a right-handed, Y-up space (axis conversion is
//! applied to the top-level nodes' transforms, as Unity does without "Bake Axis Conversion"), then
//! mirrored on X into Unity's left-handed space: positions `(-x, y, z)`, rotations
//! `(x, -y, -z, w)`, normals `(-x, y, z)`, tangents `(-x, y, z, -w)`, and every triangle's winding
//! reversed. Positions (vertices, node translations, bind-pose translations) are multiplied by
//! the import scale: `meshes.globalScale`, times the file's unit in metres when
//! `meshes.useFileScale` ("Convert Units") is set. Node scales are untouched.
//!
//! **Hierarchy.** The prefab root is a GameObject named after the file (`//RootNode` in the
//! fileID tables). When the file has a single top-level node and `preserveHierarchy` is off, that
//! node *is* the root (its transform and renderer move to the root). Node names are made unique
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
//! Not imported yet (reported in [`ImportedModel::warnings`] when the file uses them): cameras and
//! lights (imported as plain nodes), lightmap UV generation, and
//! normal recalculation by smoothing angle (file normals, or ufbx-generated smooth normals, are
//! used).
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

/// The `ModelImporter` settings this importer applies or exposes.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ModelSettings {
    pub global_scale: f32,
    /// "Convert Units": apply the file's unit (centimetres in most FBX files).
    pub use_file_scale: bool,
    pub import_blend_shapes: bool,
    /// `meshes.importBlendShapeDeformPercent`: renderers start at the file's shape weights.
    pub import_deform_percent: bool,
    pub swap_uv_channels: bool,
    pub normals: NormalMode,
    pub normal_smoothing_angle: f32,
    /// `tangentSpace.blendShapeNormalImportMode`: the shapes' normals from the file, calculated
    /// (the default), or none.
    pub blend_shape_normals: NormalMode,
    pub tangents: TangentMode,
    /// Materials are created (embedded, or extracted by the legacy importer).
    pub import_materials: bool,
    /// 0 None, 1 Legacy, 2 Generic, 3 Humanoid.
    pub animation_type: i32,
    pub import_animation: bool,
    pub sort_hierarchy_by_name: bool,
    pub preserve_hierarchy: bool,
    /// `Mesh.isReadable`.
    pub is_readable: bool,
    /// `meshes.fileIdsGeneration`: how the IDs of objects the meta does not list are derived.
    pub file_ids_generation: i64,
}

impl ModelSettings {
    pub fn read(meta: &Value) -> Self {
        let imp = importer(meta, "ModelImporter");
        Self {
            global_scale: float(imp, "meshes.globalScale").unwrap_or(1.0),
            use_file_scale: boolean(imp, "meshes.useFileScale").unwrap_or(true),
            import_blend_shapes: boolean(imp, "meshes.importBlendShapes").unwrap_or(true),
            import_deform_percent: boolean(imp, "importBlendShapeDeformPercent").unwrap_or(true),
            swap_uv_channels: boolean(imp, "meshes.swapUVChannels").unwrap_or(false),
            normals: match int(imp, "tangentSpace.normalImportMode").unwrap_or(0) {
                1 => NormalMode::Calculate,
                2 => NormalMode::None,
                _ => NormalMode::Import,
            },
            normal_smoothing_angle: float(imp, "tangentSpace.normalSmoothAngle").unwrap_or(60.0),
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
            import_materials: match int(imp, "materials.materialImportMode") {
                Some(mode) => mode != 0,
                None => boolean(imp, "materials.importMaterials").unwrap_or(true),
            },
            animation_type: int(imp, "animationType").unwrap_or(2) as i32,
            import_animation: boolean(imp, "importAnimation").unwrap_or(true),
            sort_hierarchy_by_name: boolean(imp, "meshes.sortHierarchyByName").unwrap_or(true),
            preserve_hierarchy: boolean(imp, "meshes.preserveHierarchy").unwrap_or(false),
            is_readable: boolean(imp, "meshes.isReadable")
                .or_else(|| boolean(imp, "animations.isReadable"))
                .unwrap_or(false),
            file_ids_generation: int(imp, "meshes.fileIdsGeneration").unwrap_or(2),
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
    /// The embedded Material sub-asset's fileID.
    pub file_id: FileId,
    /// The Material assigned to this name in `externalObjects`, if any.
    pub remap: Option<ObjectRef>,
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

/// The model importer.
#[derive(Debug, Clone, Default)]
pub struct ModelImporter;

impl Importer for ModelImporter {
    type Output = ImportedModel;
    const KIND: &'static str = "models";
    const VERSION: u32 = 4;

    fn import(&self, source: &Path, bytes: &[u8], meta: &Value) -> Result<ImportedModel> {
        import_model(source, bytes, meta)
    }
}

pub fn import_model(source: &Path, bytes: &[u8], meta: &Value) -> Result<ImportedModel> {
    let settings = ModelSettings::read(meta);
    let imp = importer(meta, "ModelImporter");
    let is_obj = source
        .extension()
        .is_some_and(|e| e.eq_ignore_ascii_case("obj"));
    let filename = source.to_string_lossy();
    let opts = ufbx::LoadOpts {
        target_axes: ufbx::CoordinateAxes::right_handed_y_up(),
        space_conversion: ufbx::SpaceConversion::AdjustTransforms,
        geometry_transform_handling: ufbx::GeometryTransformHandling::ModifyGeometry,
        generate_missing_normals: settings.normals != NormalMode::None,
        filename: filename.as_ref().into(),
        ..Default::default()
    };
    let scene = ufbx::load_memory(bytes, opts).map_err(|e| anyhow!("{}", e.description))?;

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

    let table = FileIdTable::read(imp);
    let mut ids = FileIdAllocator::new(&table);
    let mut builder = Builder {
        settings: &settings,
        scale,
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
            scale,
            settings: settings.clone(),
            warnings: Vec::new(),
        },
        unique_names: HashMap::new(),
        node_index: HashMap::new(),
        sources: Vec::new(),
        material_index: HashMap::new(),
    };

    // The root: the single top-level node, or the scene root.
    let root_node: &ufbx::Node = &scene.root_node;
    let root: &ufbx::Node = if !settings.preserve_hierarchy && root_node.children.len() == 1 {
        &root_node.children[0]
    } else {
        root_node
    };
    builder.assign_unique_names(root);
    builder.add_node(root, None, &mut ids);
    builder.add_renderers(&mut ids);
    builder.assign_material_ids(imp, &mut ids);

    let clips = if settings.import_animation && settings.animation_type != 0 {
        builder.clips(&scene, imp, &mut ids)
    } else {
        Vec::new()
    };
    let animator = match settings.animation_type {
        1 => Some(
            builder
                .hierarchy_id(&mut ids, ANIMATION, "Animation", ROOT_NODE_NAME, None)
                .into(),
        ),
        2 | 3 => Some(
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
    let mut model = builder.model;
    model.clips = clips;
    model.animator = animator;
    if matches!(settings.animation_type, 2 | 3) {
        model.avatar = Some(ids.id(class::AVATAR, ROOT_NODE_NAME, None).into());
    }
    if settings.import_animation && settings.animation_type == 3 && !model.clips.is_empty() {
        model
            .warnings
            .push("humanoid clips play as generic transform curves (no retargeting)".into());
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
    name: String,
    legacy: bool,
    model: ImportedModel,
    /// ufbx node element id → unique GameObject name.
    unique_names: HashMap<u32, String>,
    /// ufbx node element id → index in `model.nodes`.
    node_index: HashMap<u32, u32>,
    /// The ufbx node of each of `model.nodes`.
    sources: Vec<&'a ufbx::Node>,
    /// ufbx material element id → index in `model.materials`.
    material_index: HashMap<u32, u32>,
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
        let t = &node.local_transform;
        let index = self.model.nodes.len() as u32;
        self.node_index.insert(node.element.element_id, index);
        self.sources.push(node);
        self.model.nodes.push(ModelNode {
            name,
            parent,
            children: Vec::new(),
            position: self.position(t.translation),
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

    /// The clips: those the meta's `clipAnimations` define (ranges of takes), or one per take.
    fn clips(&self, scene: &ufbx::Scene, imp: &Value, ids: &mut FileIdAllocator) -> Vec<ModelClip> {
        let fps = if scene.settings.frames_per_second > 0.0 {
            scene.settings.frames_per_second
        } else {
            30.0
        };
        struct Range {
            name: String,
            take: String,
            first: f64,
            last: f64,
            wrap_mode: i32,
            loop_time: bool,
            loop_blend: bool,
            cycle_offset: f32,
        }
        let mut ranges = Vec::new();
        for item in imp
            .path("animations.clipAnimations")
            .and_then(Value::as_seq)
            .unwrap_or_default()
        {
            let name = item
                .get("name")
                .and_then(Value::as_str)
                .unwrap_or_default()
                .to_string();
            ranges.push(Range {
                take: item
                    .get("takeName")
                    .and_then(Value::as_str)
                    .map_or_else(|| name.clone(), str::to_string),
                name,
                first: float(item, "firstFrame").unwrap_or(0.0) as f64,
                last: float(item, "lastFrame").unwrap_or(0.0) as f64,
                wrap_mode: int(item, "wrapMode").unwrap_or(0) as i32,
                loop_time: boolean(item, "loopTime").unwrap_or(false),
                loop_blend: boolean(item, "loopBlend").unwrap_or(false),
                cycle_offset: float(item, "cycleOffset").unwrap_or(0.0),
            });
        }
        if ranges.is_empty() {
            for stack in scene
                .anim_stacks
                .iter()
                .filter(|s| s.time_end > s.time_begin)
            {
                ranges.push(Range {
                    name: stack.element.name.to_string(),
                    take: stack.element.name.to_string(),
                    first: stack.time_begin * fps,
                    last: stack.time_end * fps,
                    wrap_mode: 0,
                    loop_time: false,
                    loop_blend: false,
                    cycle_offset: 0.0,
                });
            }
        }
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
        let mut clips = Vec::new();
        for range in ranges {
            let stack = scene
                .anim_stacks
                .iter()
                .find(|s| s.element.name.to_string() == range.take)
                .or_else(|| scene.anim_stacks.first());
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
            let (start, end) = (range.first / fps, range.last.max(range.first) / fps);
            let in_range = |t: f64| t >= start - 1e-6 && t <= end + 1e-6;
            let mut curves = Vec::new();
            for node in baked.nodes.iter() {
                let Some(&index) = self.node_index.get(&node.element_id) else {
                    continue;
                };
                let positions: Vec<Key<3>> = node
                    .translation_keys
                    .iter()
                    .filter(|k| in_range(k.time))
                    .map(|k| Key {
                        time: (k.time - start) as f32,
                        value: self.position(k.value),
                    })
                    .collect();
                let rotations: Vec<Key<4>> = node
                    .rotation_keys
                    .iter()
                    .filter(|k| in_range(k.time))
                    .map(|k| Key {
                        time: (k.time - start) as f32,
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
                    .filter(|k| in_range(k.time))
                    .map(|k| Key {
                        time: (k.time - start) as f32,
                        value: [k.value.x as f32, k.value.y as f32, k.value.z as f32],
                    })
                    .collect();
                // Constant curves that hold the node's rest pose add nothing.
                let rest = &self.model.nodes[index as usize];
                let same3 = |keys: &[Key<3>], v: [f32; 3]| {
                    keys.iter()
                        .all(|k| (0..3).all(|i| (k.value[i] - v[i]).abs() < 1e-5))
                };
                let same4 = |keys: &[Key<4>], v: [f32; 4]| {
                    keys.iter().all(|k| {
                        (k.value[0] * v[0]
                            + k.value[1] * v[1]
                            + k.value[2] * v[2]
                            + k.value[3] * v[3])
                            .abs()
                            > 1.0 - 1e-6
                    })
                };
                let positions = if node.constant_translation && same3(&positions, rest.position) {
                    Vec::new()
                } else {
                    positions
                };
                let rotations = if node.constant_rotation && same4(&rotations, rest.rotation) {
                    Vec::new()
                } else {
                    rotations
                };
                let scales = if node.constant_scale && same3(&scales, rest.scale) {
                    Vec::new()
                } else {
                    scales
                };
                if positions.is_empty() && rotations.is_empty() && scales.is_empty() {
                    continue;
                }
                curves.push(NodeCurves {
                    path: self.path_of(index),
                    positions,
                    rotations,
                    scales,
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
                        .filter(|k| in_range(k.time))
                        .map(|k| Key {
                            time: (k.time - start) as f32,
                            value: [k.value.x as f32],
                        })
                        .collect();
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
            clips.push(ModelClip {
                file_id: self
                    .asset_id(ids, class::ANIMATION_CLIP, "AnimationClip", &range.name)
                    .into(),
                name: range.name,
                legacy: self.settings.animation_type == 1,
                sample_rate: fps as f32,
                length: (end - start) as f32,
                wrap_mode: range.wrap_mode,
                loop_time: range.loop_time,
                loop_blend: range.loop_blend,
                cycle_offset: range.cycle_offset,
                curves,
                floats,
            });
        }
        clips
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
    fn add_renderers(&mut self, ids: &mut FileIdAllocator) {
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
            let (built, slots) = self.build_mesh(mesh, node, skin);
            let mesh_name = if is_root {
                node.element.name.to_string()
            } else {
                key.clone()
            };
            let mesh_id = self.asset_id(ids, class::MESH, "Mesh", &mesh_name);
            let mesh_index = self.model.meshes.len() as u32;
            self.model.meshes.push(ModelMesh {
                name: mesh_name,
                file_id: mesh_id.into(),
                ..built
            });
            let materials = slots
                .into_iter()
                .map(|m| m.map(|m| self.material(m)))
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
                }
            };
            self.model.nodes[index].renderer = Some(renderer);
        }
    }

    /// The model material for a ufbx material, added on first use.
    fn material(&mut self, material: &ufbx::Material) -> u32 {
        if let Some(&index) = self.material_index.get(&material.element.element_id) {
            return index;
        }
        let index = self.model.materials.len() as u32;
        self.material_index
            .insert(material.element.element_id, index);
        self.model.materials.push(ModelMaterial {
            name: material.element.name.to_string(),
            file_id: FileId {
                id: 0,
                source: FileIdSource::Derived,
            },
            remap: None,
        });
        index
    }

    /// Material fileIDs (by name) and `externalObjects` remaps.
    fn assign_material_ids(&mut self, imp: &Value, ids: &mut FileIdAllocator) {
        let remaps: Vec<(String, ObjectRef)> = imp
            .get("externalObjects")
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
            .collect();
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
    ) -> (ModelMesh, Vec<Option<&'a ufbx::Material>>) {
        let settings = self.settings;
        let with_normals = settings.normals != NormalMode::None && mesh.vertex_normal.exists;
        let with_tangents = settings.tangents == TangentMode::Import
            && with_normals
            && mesh.vertex_tangent.exists
            && mesh.vertex_bitangent.exists;
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
        // Each vertex's control point (shapes move control points), each corner's vertex.
        let mut control_points: Vec<u32> = Vec::new();
        let mut corner_vertex = vec![u32::MAX; mesh.num_indices];
        let mut polygons: Vec<(Vec<u32>, Vec<usize>)> = Vec::new();
        // Vertices are welded on their exact attribute bits (and control point, with shapes).
        let mut welded: HashMap<Vec<u32>, u32> = HashMap::new();
        let mut key: Vec<u32> = Vec::new();
        let mut triangle = vec![0u32; mesh.max_face_triangles.max(1) * 3];

        // One submesh per material part with triangles, in material order.
        let all_faces: Vec<u32>;
        let parts: Vec<(Option<u32>, &[u32])> = if mesh.material_parts.is_empty() {
            all_faces = (0..mesh.faces.len() as u32).collect();
            vec![(None, &all_faces[..])]
        } else {
            mesh.material_parts
                .iter()
                .filter(|p| p.num_triangles > 0)
                .map(|p| (Some(p.index), p.face_indices.as_ref()))
                .collect()
        };
        let mut materials = Vec::new();
        for (material, faces) in parts {
            // The node's material for the slot (instances may override the mesh's).
            let material = material.and_then(|i| {
                node.materials
                    .get(i as usize)
                    .or_else(|| mesh.materials.get(i as usize))
                    .map(|m| m.as_ref())
            });
            materials.push(material);
            let mut indices = Vec::new();
            for &face_index in faces {
                let face = mesh.faces[face_index as usize];
                let corners: Vec<usize> = (face.index_begin as usize
                    ..(face.index_begin + face.num_indices) as usize)
                    .collect();
                polygons.push((
                    corners.iter().map(|&c| mesh.vertex_indices[c]).collect(),
                    corners,
                ));
                let count = mesh.triangulate_face(&mut triangle, face) as usize;
                for t in 0..count {
                    // Mirroring X flips the handedness: reverse the winding.
                    for corner in [triangle[3 * t], triangle[3 * t + 2], triangle[3 * t + 1]] {
                        let c = corner as usize;
                        let p = mesh.vertex_position[c];
                        let mut vertex = Vertex {
                            position: self.position(p),
                            ..Default::default()
                        };
                        if with_normals {
                            let n = mesh.vertex_normal[c];
                            vertex.normal = [-n.x as f32, n.y as f32, n.z as f32];
                            if with_tangents {
                                let t = mesh.vertex_tangent[c];
                                let b = mesh.vertex_bitangent[c];
                                // Handedness of the source frame, flipped by the mirror.
                                let cross = [
                                    n.y * t.z - n.z * t.y,
                                    n.z * t.x - n.x * t.z,
                                    n.x * t.y - n.y * t.x,
                                ];
                                let dot = cross[0] * b.x + cross[1] * b.y + cross[2] * b.z;
                                let w = if dot < 0.0 { 1.0 } else { -1.0 };
                                vertex.tangent = [-t.x as f32, t.y as f32, t.z as f32, w];
                            }
                        }
                        for (set, uv) in uv_sets.iter().enumerate() {
                            let v = uv[c];
                            vertex.uv[set] = [v.x as f32, v.y as f32];
                        }
                        if with_colors {
                            let v = mesh.vertex_color[c];
                            vertex.color = [v.x as f32, v.y as f32, v.z as f32, v.w as f32];
                        }
                        if let Some(skin) = skin {
                            vertex.weights = bone_weight(skin, mesh.vertex_indices[c] as usize);
                        }
                        vertex.key(&mut key);
                        let control_point = mesh.vertex_indices[c];
                        if !channels.is_empty() {
                            key.push(control_point);
                        }
                        let next = out.positions.len() as u32;
                        let index = *welded.entry(key.clone()).or_insert_with(|| {
                            control_points.push(control_point);
                            out.positions.push(vertex.position);
                            if with_normals {
                                out.normals.push(vertex.normal);
                            }
                            if with_tangents {
                                out.tangents.push(vertex.tangent);
                            }
                            for (set, uvs) in out.uvs.iter_mut().enumerate() {
                                uvs.push(vertex.uv[set]);
                            }
                            if with_colors {
                                out.colors.push(vertex.color);
                            }
                            if skin.is_some() {
                                out.bone_weights.push(vertex.weights);
                            }
                            next
                        });
                        corner_vertex[c] = index;
                        indices.push(index);
                    }
                }
            }
            out.submeshes.push(indices);
        }
        // Tangents: MikkTSpace on the final vertices.
        if settings.tangents == TangentMode::Calculate && with_normals {
            let uv0 = out.uvs.first().cloned().unwrap_or_default();
            out.tangents =
                tangent_space::mikk_tangents(&out.positions, &out.normals, &uv0, &out.submeshes);
        }
        if !channels.is_empty() {
            self.add_blend_shapes(
                mesh,
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
                .map(|c| self.matrix(&c.geometry_to_bone))
                .collect();
        }
        if let Some(first) = out.positions.first() {
            let (mut min, mut max) = (*first, *first);
            for p in &out.positions {
                for i in 0..3 {
                    min[i] = min[i].min(p[i]);
                    max[i] = max[i].max(p[i]);
                }
            }
            out.bounds_min = min;
            out.bounds_max = max;
        }
        (out, materials)
    }

    /// The mesh's blend shapes: per frame the control points' offsets for each vertex, the
    /// normals' change (the file's, or calculated as Unity does: see
    /// [`tangent_space::shape_corner_normals`]) and the tangents' (MikkTSpace on the shaped mesh).
    #[allow(clippy::too_many_arguments)]
    fn add_blend_shapes(
        &self,
        mesh: &ufbx::Mesh,
        channels: &[&ufbx::BlendChannel],
        out: &mut ModelMesh,
        control_points: &[u32],
        corner_vertex: &[u32],
        polygons: &[(Vec<u32>, Vec<usize>)],
        with_normals: bool,
    ) {
        let settings = self.settings;
        let base: Vec<[f64; 3]> = mesh.vertices.iter().map(|v| [v.x, v.y, v.z]).collect();
        let cp_polygons: Vec<Vec<u32>> = polygons.iter().map(|(cps, _)| cps.clone()).collect();
        let uv0 = out.uvs.first().cloned().unwrap_or_default();
        for channel in channels {
            let mut frames = Vec::new();
            for key in channel.keyframes.iter() {
                let shape = &key.shape;
                let mut offsets = vec![[0f64; 3]; mesh.num_vertices];
                let mut normal_offsets = vec![[0f64; 3]; mesh.num_vertices];
                for (i, &v) in shape.offset_vertices.iter().enumerate() {
                    if let Some(o) = shape.position_offsets.get(i)
                        && let Some(slot) = offsets.get_mut(v as usize)
                    {
                        *slot = [o.x, o.y, o.z];
                    }
                    if let Some(n) = shape.normal_offsets.get(i)
                        && let Some(slot) = normal_offsets.get_mut(v as usize)
                    {
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
                        .zip(&out.tangents)
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

/// One corner's attributes, before welding.
#[derive(Default)]
struct Vertex {
    position: [f32; 3],
    normal: [f32; 3],
    tangent: [f32; 4],
    uv: [[f32; 2]; 4],
    color: [f32; 4],
    weights: BoneWeight,
}

impl Vertex {
    /// The exact bits of every attribute: equal keys weld.
    fn key(&self, key: &mut Vec<u32>) {
        key.clear();
        key.extend(self.position.iter().map(|v| v.to_bits()));
        key.extend(self.normal.iter().map(|v| v.to_bits()));
        key.extend(self.tangent.iter().map(|v| v.to_bits()));
        key.extend(self.uv.iter().flatten().map(|v| v.to_bits()));
        key.extend(self.color.iter().map(|v| v.to_bits()));
        key.extend(self.weights.weights.iter().map(|v| v.to_bits()));
        key.extend(self.weights.bones.iter().map(|&b| b as u32));
    }
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

mod tangent_space;

#[cfg(test)]
mod tests;
