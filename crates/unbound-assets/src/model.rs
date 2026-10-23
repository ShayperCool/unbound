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
//! the tables do not list, Unity 2019.3+ derives IDs from a hash of the object's type and name;
//! that hash is not reproduced (TODO), except for the root objects whose IDs are the same in
//! every model (observed in Unity's own packages): those get the observed values, others a free
//! legacy-style ID and [`FileIdSource::Derived`].
//!
//! Not imported yet (reported in [`ImportedModel::warnings`] when the file uses them): blend
//! shapes, animations, cameras and lights (imported as plain nodes), lightmap UV generation, and
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
    /// Calculated (MikkTSpace or legacy): left to the runtime, which has the final vertex data.
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
    pub swap_uv_channels: bool,
    pub normals: NormalMode,
    pub normal_smoothing_angle: f32,
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
}

impl ModelSettings {
    pub fn read(meta: &Value) -> Self {
        let imp = importer(meta, "ModelImporter");
        Self {
            global_scale: float(imp, "meshes.globalScale").unwrap_or(1.0),
            use_file_scale: boolean(imp, "meshes.useFileScale").unwrap_or(true),
            import_blend_shapes: boolean(imp, "meshes.importBlendShapes").unwrap_or(true),
            swap_uv_channels: boolean(imp, "meshes.swapUVChannels").unwrap_or(false),
            normals: match int(imp, "tangentSpace.normalImportMode").unwrap_or(0) {
                1 => NormalMode::Calculate,
                2 => NormalMode::None,
                _ => NormalMode::Import,
            },
            normal_smoothing_angle: float(imp, "tangentSpace.normalSmoothAngle").unwrap_or(60.0),
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

/// An imported model.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ImportedModel {
    /// The file name without extension: the root GameObject's name.
    pub name: String,
    /// Depth-first, parents before children; `nodes[0]` is the root.
    pub nodes: Vec<ModelNode>,
    pub meshes: Vec<ModelMesh>,
    pub materials: Vec<ModelMaterial>,
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
    const VERSION: u32 = 1;

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

    let mut model = builder.model;
    model.animator = match settings.animation_type {
        1 => Some(ids.id(ANIMATION, ROOT_NODE_NAME, None).into()),
        2 | 3 => Some(ids.id(class::ANIMATOR, ROOT_NODE_NAME, None).into()),
        _ => None,
    };
    if matches!(settings.animation_type, 2 | 3) {
        model.avatar = Some(ids.id(class::AVATAR, ROOT_NODE_NAME, None).into());
    }
    if settings.import_animation && scene.anim_stacks.iter().any(|s| s.time_end > s.time_begin) {
        model
            .warnings
            .push("animation clips are not imported yet".into());
    }
    if settings.import_blend_shapes && !scene.blend_deformers.is_empty() {
        model
            .warnings
            .push("blend shapes are not imported yet".into());
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
        let derived = |root_id| (is_root && !self.legacy).then_some(root_id);
        let game_object = ids.id(class::GAME_OBJECT, &key, derived(ROOT_GAME_OBJECT_ID));
        let transform = ids.id(class::TRANSFORM, &key, derived(ROOT_TRANSFORM_ID));
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
            let mesh_id = ids.id(class::MESH, &mesh_name, None);
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
            let renderer = if let Some(skin) = skin {
                let bones: Vec<u32> = skin
                    .clusters
                    .iter()
                    .map(|c| {
                        c.bone_node
                            .as_ref()
                            .and_then(|b| self.node_index.get(&b.element.element_id).copied())
                            .unwrap_or(0)
                    })
                    .collect();
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
                    renderer: ids.id(class::SKINNED_MESH_RENDERER, &key, None).into(),
                    filter: None,
                    bones,
                    root_bone,
                }
            } else {
                let derived = (is_root && !self.legacy).then_some(ROOT_MESH_RENDERER_ID);
                ModelRenderer {
                    kind: RendererKind::Mesh,
                    mesh: mesh_index,
                    materials,
                    renderer: ids.id(class::MESH_RENDERER, &key, derived).into(),
                    filter: Some(ids.id(class::MESH_FILTER, &key, None).into()),
                    bones: Vec::new(),
                    root_bone: None,
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
        for material in &mut self.model.materials {
            material.file_id = ids.id(class::MATERIAL, &material.name, None).into();
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
        };
        // Vertices are welded on their exact attribute bits.
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
                        let next = out.positions.len() as u32;
                        let index = *welded.entry(key.clone()).or_insert_with(|| {
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
                        indices.push(index);
                    }
                }
            }
            out.submeshes.push(indices);
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

#[cfg(test)]
mod tests;
