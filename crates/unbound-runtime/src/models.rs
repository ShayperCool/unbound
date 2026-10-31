//! Imported models as the engine loads them: the documents of the model's prefab (GameObjects,
//! Transforms, renderers, Materials and Mesh sub-assets with the importer's fileIDs), so that loading
//! a model and expanding a scene's instance of one go through the same prefab machinery as a
//! `.prefab` file; and the geometry of its meshes, which the Mesh documents only point at.
use serde_json::{Value as Json, json};
use unbound_assets::{
    ImportedModel,
    model::{
        Key, MaterialDescription, MaterialImportMode, MaterialLocation, ModelClip, ModelMaterial,
        ModelMesh, RendererKind,
    },
};
use unbound_project::AssetDb;
use unbound_yaml::{Document, Guid, Map, Value};

const GAME_OBJECT: i32 = 1;
const TRANSFORM: i32 = 4;
const MATERIAL: i32 = 21;
const MESH_RENDERER: i32 = 23;
const MESH_FILTER: i32 = 33;
const MESH: i32 = 43;
const ANIMATOR: i32 = 95;
const AVATAR: i32 = 90;
const ANIMATION: i32 = 111;
const ANIMATION_CLIP: i32 = 74;
const SKINNED_MESH_RENDERER: i32 = 137;
const LOD_GROUP: i32 = 205;
/// The main object of a texture asset.
const TEXTURE_MAIN: i64 = 2_800_000;
/// The main object of a material asset.
const MATERIAL_MAIN: i64 = 2_100_000;

/// Unity's built-in resources (`unity_builtin_extra`).
const BUILTIN_EXTRA: &str = "0000000000000000f000000000000000";
const DEFAULT_MATERIAL: i64 = 10303;
const STANDARD_SHADER: i64 = 46;

fn scalar(v: impl ToString) -> Value {
    Value::Scalar(v.to_string())
}

fn map(entries: Vec<(&str, Value)>) -> Value {
    Value::Map(Map(entries
        .into_iter()
        .map(|(k, v)| (k.to_string(), v))
        .collect()))
}

fn local(file_id: i64) -> Value {
    map(vec![("fileID", scalar(file_id))])
}

fn external(file_id: i64, guid: &str, kind: i32) -> Value {
    map(vec![
        ("fileID", scalar(file_id)),
        ("guid", scalar(guid)),
        ("type", scalar(kind)),
    ])
}

fn vec3(v: [f32; 3]) -> Value {
    map(vec![
        ("x", scalar(v[0])),
        ("y", scalar(v[1])),
        ("z", scalar(v[2])),
    ])
}

fn quat(v: [f32; 4]) -> Value {
    map(vec![
        ("x", scalar(v[0])),
        ("y", scalar(v[1])),
        ("z", scalar(v[2])),
        ("w", scalar(v[3])),
    ])
}

fn doc(class_id: i32, file_id: i64, type_name: &str, body: Value) -> Document {
    Document {
        class_id,
        file_id,
        stripped: false,
        type_name: type_name.to_string(),
        body,
    }
}

/// The documents of a model's prefab. `path` is the model's asset path; `db` finds the textures
/// and external materials its materials name.
pub fn documents(guid: Guid, path: &str, model: &ImportedModel, db: &AssetDb) -> Vec<Document> {
    let mut docs = Vec::new();
    let guid_text = guid.to_string();
    let settings = &model.settings;

    // Material slots: a remapped Material asset; with the legacy external location, the Material
    // asset the importer made and the material search finds; a sub-asset the managed side builds
    // from the material's description (legacy conversion or the description preprocessors);
    // Unity's Default-Material when the importer makes no materials.
    let mut slot_refs = Vec::with_capacity(model.materials.len());
    for material in &model.materials {
        if let Some(remap) = &material.remap
            && !remap.is_null()
        {
            slot_refs.push(external(remap.file_id, &remap.guid.to_string(), 2));
            continue;
        }
        if settings.material_import == MaterialImportMode::None {
            slot_refs.push(external(DEFAULT_MATERIAL, BUILTIN_EXTRA, 0));
            continue;
        }
        if settings.material_location == MaterialLocation::External {
            if let Some(found) = find_material(db, path, &model.name, material, settings) {
                slot_refs.push(external(MATERIAL_MAIN, &found.to_string(), 2));
                continue;
            }
            tracing::warn!(
                "{path}: no external material for '{}' (the Editor creates it on import); \
                 using the importer's own",
                material.name
            );
        }
        docs.push(doc(
            MATERIAL,
            material.file_id.id,
            "Material",
            map(vec![
                ("m_Name", scalar(&material.name)),
                ("m_Shader", external(STANDARD_SHADER, BUILTIN_EXTRA, 0)),
                (
                    "m_UnboundMaterialImport",
                    material_import(db, path, settings.material_import, &material.description),
                ),
            ]),
        ));
        slot_refs.push(local(material.file_id.id));
    }
    let material_ref = |slot: &Option<u32>| match slot {
        Some(i) => slot_refs
            .get(*i as usize)
            .cloned()
            .unwrap_or_else(|| local(0)),
        None => external(DEFAULT_MATERIAL, BUILTIN_EXTRA, 0),
    };

    for (index, mesh) in model.meshes.iter().enumerate() {
        docs.push(doc(
            MESH,
            mesh.file_id.id,
            "Mesh",
            map(vec![
                ("m_Name", scalar(&mesh.name)),
                ("m_UnboundModel", scalar(&guid_text)),
                ("m_UnboundMesh", scalar(index)),
            ]),
        ));
    }

    for clip in &model.clips {
        docs.push(clip_document(clip));
    }

    // The Animator's avatar: the model's own (a generic one: its name), or another model's.
    let avatar = if let Some(own) = model.avatar {
        docs.push(doc(
            AVATAR,
            own.id,
            "Avatar",
            map(vec![("m_Name", scalar(format!("{}Avatar", model.name)))]),
        ));
        local(own.id)
    } else if let Some(source) = &model.settings.source_avatar {
        external(source.file_id, &source.guid.to_string(), 3)
    } else {
        local(0)
    };

    for (index, node) in model.nodes.iter().enumerate() {
        let mut components = vec![local(node.transform.id)];
        let go = node.game_object.id;
        if let Some(renderer) = &node.renderer {
            let mesh_id = model
                .meshes
                .get(renderer.mesh as usize)
                .map_or(0, |m| m.file_id.id);
            let materials = Value::Seq(renderer.materials.iter().map(&material_ref).collect());
            match renderer.kind {
                RendererKind::Mesh => {
                    if let Some(filter) = renderer.filter {
                        components.push(local(filter.id));
                        docs.push(doc(
                            MESH_FILTER,
                            filter.id,
                            "MeshFilter",
                            map(vec![
                                ("m_GameObject", local(go)),
                                ("m_Mesh", local(mesh_id)),
                            ]),
                        ));
                    }
                    components.push(local(renderer.renderer.id));
                    docs.push(doc(
                        MESH_RENDERER,
                        renderer.renderer.id,
                        "MeshRenderer",
                        map(vec![
                            ("m_GameObject", local(go)),
                            ("m_Enabled", scalar(1)),
                            ("m_CastShadows", scalar(1)),
                            ("m_ReceiveShadows", scalar(1)),
                            ("m_Materials", materials),
                        ]),
                    ));
                }
                RendererKind::SkinnedMesh => {
                    components.push(local(renderer.renderer.id));
                    let bones = renderer
                        .bones
                        .iter()
                        .map(|b| local(model.nodes.get(*b as usize).map_or(0, |n| n.transform.id)))
                        .collect();
                    let root = renderer
                        .root_bone
                        .and_then(|b| model.nodes.get(b as usize))
                        .map_or(0, |n| n.transform.id);
                    docs.push(doc(
                        SKINNED_MESH_RENDERER,
                        renderer.renderer.id,
                        "SkinnedMeshRenderer",
                        map(vec![
                            ("m_GameObject", local(go)),
                            ("m_Enabled", scalar(1)),
                            ("m_CastShadows", scalar(1)),
                            ("m_ReceiveShadows", scalar(1)),
                            ("m_Materials", materials),
                            ("m_Mesh", local(mesh_id)),
                            ("m_Bones", Value::Seq(bones)),
                            ("m_RootBone", local(root)),
                            (
                                "m_BlendShapeWeights",
                                Value::Seq(
                                    renderer
                                        .blend_shape_weights
                                        .iter()
                                        .map(|w| scalar(w))
                                        .collect(),
                                ),
                            ),
                        ]),
                    ));
                }
            }
        }
        if index == 0
            && let Some(animator) = model.animator
        {
            components.push(local(animator.id));
            docs.push(if model.settings.animation_type == 1 {
                doc(
                    ANIMATION,
                    animator.id,
                    "Animation",
                    map(vec![
                        ("m_GameObject", local(go)),
                        ("m_Enabled", scalar(1)),
                        (
                            "m_Animation",
                            local(model.clips.first().map_or(0, |c| c.file_id.id)),
                        ),
                        (
                            "m_Animations",
                            Value::Seq(model.clips.iter().map(|c| local(c.file_id.id)).collect()),
                        ),
                        ("m_PlayAutomatically", scalar(1)),
                    ]),
                )
            } else {
                doc(
                    ANIMATOR,
                    animator.id,
                    "Animator",
                    map(vec![
                        ("m_GameObject", local(go)),
                        ("m_Enabled", scalar(1)),
                        ("m_Avatar", avatar.clone()),
                        ("m_Controller", local(0)),
                        ("m_ApplyRootMotion", scalar(0)),
                    ]),
                )
            });
        }
        if index == 0
            && let Some(group) = &model.lod_group
        {
            components.push(local(group.file_id.id));
            let renderer_of = |node: u32| {
                model
                    .nodes
                    .get(node as usize)
                    .and_then(|n| n.renderer.as_ref())
                    .map_or(0, |r| r.renderer.id)
            };
            let lods = group
                .lods
                .iter()
                .map(|lod| {
                    map(vec![
                        ("screenRelativeHeight", scalar(lod.screen_relative_height)),
                        ("fadeTransitionWidth", scalar(0)),
                        (
                            "renderers",
                            Value::Seq(
                                lod.renderers
                                    .iter()
                                    .map(|&n| map(vec![("renderer", local(renderer_of(n)))]))
                                    .collect(),
                            ),
                        ),
                    ])
                })
                .collect();
            docs.push(doc(
                LOD_GROUP,
                group.file_id.id,
                "LODGroup",
                map(vec![
                    ("m_GameObject", local(go)),
                    ("m_Enabled", scalar(1)),
                    ("m_LocalReferencePoint", vec3(group.local_reference_point)),
                    ("m_Size", scalar(group.size)),
                    ("m_FadeMode", scalar(0)),
                    ("m_AnimateCrossFading", scalar(0)),
                    ("m_LastLODIsBillboard", scalar(0)),
                    ("m_LODs", Value::Seq(lods)),
                ]),
            ));
        }
        docs.push(doc(
            GAME_OBJECT,
            go,
            "GameObject",
            map(vec![
                (
                    "m_Component",
                    Value::Seq(
                        components
                            .into_iter()
                            .map(|c| map(vec![("component", c)]))
                            .collect(),
                    ),
                ),
                ("m_Layer", scalar(0)),
                ("m_Name", scalar(&node.name)),
                ("m_TagString", scalar("Untagged")),
                ("m_IsActive", scalar(1)),
            ]),
        ));
        let children = node
            .children
            .iter()
            .map(|c| local(model.nodes.get(*c as usize).map_or(0, |n| n.transform.id)))
            .collect();
        let father = node
            .parent
            .and_then(|p| model.nodes.get(p as usize))
            .map_or(0, |n| n.transform.id);
        docs.push(doc(
            TRANSFORM,
            node.transform.id,
            "Transform",
            map(vec![
                ("m_GameObject", local(go)),
                ("m_LocalRotation", quat(node.rotation)),
                ("m_LocalPosition", vec3(node.position)),
                ("m_LocalScale", vec3(node.scale)),
                ("m_Children", Value::Seq(children)),
                ("m_Father", local(father)),
            ]),
        ));
    }
    docs
}

/// What the managed side needs to make a model's material: the importer (`legacy` or
/// `description`), the model's path, and the description with each texture's asset found.
fn material_import(
    db: &AssetDb,
    model_path: &str,
    mode: MaterialImportMode,
    description: &MaterialDescription,
) -> Value {
    let named = |entries: Vec<(String, Value)>| Value::Map(Map(entries.into_iter().collect()));
    let xy = |v: [f32; 2]| map(vec![("x", scalar(v[0])), ("y", scalar(v[1]))]);
    let textures = description
        .textures
        .iter()
        .map(|t| {
            let texture = find_texture(db, model_path, &t.relative_path, &t.path)
                .map_or_else(|| local(0), |g| external(TEXTURE_MAIN, &g.to_string(), 3));
            (
                t.property.clone(),
                map(vec![
                    ("path", scalar(&t.path)),
                    ("relativePath", scalar(&t.relative_path)),
                    ("texture", texture),
                    ("offset", xy(t.offset)),
                    ("scale", xy(t.scale)),
                ]),
            )
        })
        .collect();
    map(vec![
        (
            "importer",
            scalar(match mode {
                MaterialImportMode::Legacy => "legacy",
                _ => "description",
            }),
        ),
        ("assetPath", scalar(model_path)),
        (
            "floats",
            named(
                description
                    .floats
                    .iter()
                    .map(|(n, v)| (n.clone(), scalar(v)))
                    .collect(),
            ),
        ),
        (
            "vectors",
            named(
                description
                    .vectors
                    .iter()
                    .map(|(n, v)| (n.clone(), quat(*v)))
                    .collect(),
            ),
        ),
        (
            "strings",
            named(
                description
                    .strings
                    .iter()
                    .map(|(n, v)| (n.clone(), scalar(v)))
                    .collect(),
            ),
        ),
        ("textures", named(textures)),
    ])
}

/// The folder of an asset path (`Assets/A/b.fbx` → `Assets/A`).
fn folder(path: &str) -> &str {
    path.rsplit_once('/').map_or("", |(dir, _)| dir)
}

/// `relative` resolved against `dir`, with `.` and `..` segments applied.
fn join(dir: &str, relative: &str) -> String {
    let mut parts: Vec<&str> = if dir.is_empty() {
        Vec::new()
    } else {
        dir.split('/').collect()
    };
    for part in relative.split(['/', '\\']) {
        match part {
            "" | "." => {}
            ".." => {
                parts.pop();
            }
            _ => parts.push(part),
        }
    }
    parts.join("/")
}

/// The folders from `dir` up to the root of its asset tree (`Assets` or `Packages/<name>`).
fn up_from(dir: &str) -> Vec<&str> {
    let top = if dir.starts_with("Packages/") { 2 } else { 1 };
    let mut out = Vec::new();
    let mut current = dir;
    loop {
        out.push(current);
        if current.split('/').count() <= top {
            break;
        }
        match current.rsplit_once('/') {
            Some((parent, _)) => current = parent,
            None => break,
        }
    }
    out
}

/// The texture asset a model's material names, found as the Editor's model importer finds it: at
/// the path relative to the model, in the model's folder or its `Textures` folder, in the
/// `Textures` folder of each folder above, then by file name anywhere in the project (measured:
/// tests/projects/modelimport). An embedded texture is not extracted: unless such a file exists
/// in the project, the slot stays empty, as in the Editor.
fn find_texture(db: &AssetDb, model_path: &str, relative: &str, path: &str) -> Option<Guid> {
    let dir = folder(model_path);
    let is_file = |p: &str| db.by_path(p).filter(|a| !a.is_folder).map(|a| a.guid);
    for candidate in [relative, path] {
        if !candidate.is_empty() && !candidate.starts_with('/') && !candidate.contains(':') {
            if let Some(found) = is_file(&join(dir, candidate)) {
                return Some(found);
            }
        }
    }
    let name = path
        .rsplit(['/', '\\'])
        .next()
        .filter(|n| !n.is_empty())
        .or_else(|| relative.rsplit(['/', '\\']).next())?;
    if name.is_empty() {
        return None;
    }
    if let Some(found) = is_file(&format!("{dir}/{name}")) {
        return Some(found);
    }
    for folder in up_from(dir) {
        if let Some(found) = is_file(&format!("{folder}/Textures/{name}")) {
            return Some(found);
        }
    }
    by_file_name(db, name)
}

/// The asset with this file name anywhere in the project (the first by path).
fn by_file_name(db: &AssetDb, name: &str) -> Option<Guid> {
    db.iter()
        .filter(|a| {
            !a.is_folder
                && a.path
                    .rsplit('/')
                    .next()
                    .is_some_and(|n| n.eq_ignore_ascii_case(name))
        })
        .min_by(|a, b| a.path.cmp(&b.path))
        .map(|a| a.guid)
}

/// The Material asset the legacy importer made for a material with the external location, found
/// by `materialName` and `materialSearch` (measured: tests/projects/modelimport): named after the
/// diffuse texture, the material, or the model and the material (`<model>-<material>`), looked up
/// in the `Materials` folder next to the model, in those of the folders above it, or anywhere.
fn find_material(
    db: &AssetDb,
    model_path: &str,
    model_name: &str,
    material: &ModelMaterial,
    settings: &unbound_assets::model::ModelSettings,
) -> Option<Guid> {
    let texture = material
        .description
        .texture("DiffuseColor")
        .and_then(|t| {
            let file = t.path.rsplit(['/', '\\']).next()?;
            Some(
                file.rsplit_once('.')
                    .map_or(file, |(stem, _)| stem)
                    .to_string(),
            )
        })
        .filter(|n| !n.is_empty());
    let by_model = format!("{model_name}-{}", material.name);
    let name = match settings.material_name {
        0 => texture.unwrap_or_else(|| material.name.clone()),
        2 => by_model,
        3 => texture.unwrap_or(by_model),
        _ => material.name.clone(),
    };
    let file = format!("{name}.mat");
    let dir = folder(model_path);
    let is_file = |p: &str| db.by_path(p).filter(|a| !a.is_folder).map(|a| a.guid);
    if settings.material_search == 0 {
        return is_file(&format!("{dir}/Materials/{file}"));
    }
    for folder in up_from(dir) {
        if let Some(found) = is_file(&format!("{folder}/Materials/{file}")) {
            return Some(found);
        }
    }
    if settings.material_search == 2 {
        return by_file_name(db, &file);
    }
    None
}

/// A clip as Unity serializes an AnimationClip, so the managed clip reader takes it as it is:
/// position, rotation and scale curves per node path, with slopes from the neighbouring keys.
fn clip_document(clip: &ModelClip) -> Document {
    fn axes<const N: usize>(names: &[&'static str; N], v: [f32; N]) -> Value {
        // A float curve's values are plain numbers.
        if N == 1 && names[0].is_empty() {
            return scalar(v[0]);
        }
        map(names.iter().zip(v).map(|(n, x)| (*n, scalar(x))).collect())
    }
    fn curve<const N: usize>(path: &str, keys: &[Key<N>], names: &[&'static str; N]) -> Value {
        let slope = |a: &Key<N>, b: &Key<N>| {
            let dt = b.time - a.time;
            let mut out = [0f32; N];
            if dt > 0.0 {
                for i in 0..N {
                    out[i] = (b.value[i] - a.value[i]) / dt;
                }
            }
            out
        };
        let third = [1.0 / 3.0; N];
        let frames = keys
            .iter()
            .enumerate()
            .map(|(i, key)| {
                let incoming = if i > 0 {
                    slope(&keys[i - 1], key)
                } else {
                    [0.0; N]
                };
                let outgoing = if i + 1 < keys.len() {
                    slope(key, &keys[i + 1])
                } else {
                    incoming
                };
                let incoming = if i > 0 { incoming } else { outgoing };
                map(vec![
                    ("serializedVersion", scalar(3)),
                    ("time", scalar(key.time)),
                    ("value", axes(names, key.value)),
                    ("inSlope", axes(names, incoming)),
                    ("outSlope", axes(names, outgoing)),
                    ("tangentMode", scalar(0)),
                    ("weightedMode", scalar(0)),
                    ("inWeight", axes(names, third)),
                    ("outWeight", axes(names, third)),
                ])
            })
            .collect();
        map(vec![
            (
                "curve",
                map(vec![
                    ("serializedVersion", scalar(2)),
                    ("m_Curve", Value::Seq(frames)),
                    ("m_PreInfinity", scalar(2)),
                    ("m_PostInfinity", scalar(2)),
                    ("m_RotationOrder", scalar(4)),
                ]),
            ),
            ("path", scalar(path)),
        ])
    }
    const XYZ: [&str; 3] = ["x", "y", "z"];
    const XYZW: [&str; 4] = ["x", "y", "z", "w"];
    let mut positions = Vec::new();
    let mut rotations = Vec::new();
    let mut scales = Vec::new();
    for node in &clip.curves {
        if !node.positions.is_empty() {
            positions.push(curve(&node.path, &node.positions, &XYZ));
        }
        if !node.rotations.is_empty() {
            // Keep each key in the hemisphere of the previous one, so slopes do not jump.
            let mut keys = node.rotations.clone();
            for i in 1..keys.len() {
                let dot: f32 = (0..4)
                    .map(|c| keys[i].value[c] * keys[i - 1].value[c])
                    .sum();
                if dot < 0.0 {
                    for c in 0..4 {
                        keys[i].value[c] = -keys[i].value[c];
                    }
                }
            }
            rotations.push(curve(&node.path, &keys, &XYZW));
        }
        if !node.scales.is_empty() {
            scales.push(curve(&node.path, &node.scales, &XYZ));
        }
    }
    let floats: Vec<Value> = clip
        .floats
        .iter()
        .map(|f| {
            let Value::Map(Map(mut entries)) = curve(&f.path, &f.keys, &[""]) else {
                unreachable!()
            };
            entries.push(("attribute".to_string(), scalar(&f.attribute)));
            entries.push(("classID".to_string(), scalar(f.class_id)));
            entries.push(("script".to_string(), map(vec![("fileID", scalar(0))])));
            Value::Map(Map(entries))
        })
        .collect();
    doc(
        ANIMATION_CLIP,
        clip.file_id.id,
        "AnimationClip",
        map(vec![
            ("m_Name", scalar(&clip.name)),
            ("serializedVersion", scalar(7)),
            ("m_Legacy", scalar(clip.legacy as i32)),
            ("m_RotationCurves", Value::Seq(rotations)),
            ("m_PositionCurves", Value::Seq(positions)),
            ("m_ScaleCurves", Value::Seq(scales)),
            ("m_FloatCurves", Value::Seq(floats)),
            ("m_SampleRate", scalar(clip.sample_rate)),
            ("m_WrapMode", scalar(clip.wrap_mode)),
            (
                "m_AnimationClipSettings",
                map(vec![
                    ("m_StartTime", scalar(0)),
                    ("m_StopTime", scalar(clip.length)),
                    ("m_LoopTime", scalar(clip.loop_time as i32)),
                    ("m_LoopBlend", scalar(clip.loop_blend as i32)),
                    ("m_CycleOffset", scalar(clip.cycle_offset)),
                ]),
            ),
            (
                "m_Events",
                Value::Seq(
                    clip.events
                        .iter()
                        .map(|e| {
                            map(vec![
                                ("time", scalar(e.time)),
                                ("functionName", scalar(&e.function_name)),
                                ("data", scalar(&e.data)),
                                ("objectReferenceParameter", map(vec![("fileID", scalar(0))])),
                                ("floatParameter", scalar(e.float_parameter)),
                                ("intParameter", scalar(e.int_parameter)),
                                ("messageOptions", scalar(e.message_options)),
                            ])
                        })
                        .collect(),
                ),
            ),
        ]),
    )
}

/// A mesh's geometry for the managed Mesh: little-endian arrays in base64.
pub fn mesh_json(mesh: &ModelMesh) -> Json {
    fn floats<const N: usize>(items: &[[f32; N]]) -> String {
        let mut bytes = Vec::with_capacity(items.len() * N * 4);
        for item in items {
            for v in item {
                bytes.extend_from_slice(&v.to_le_bytes());
            }
        }
        base64(&bytes)
    }
    let submeshes: Vec<String> = mesh
        .submeshes
        .iter()
        .map(|indices| {
            let mut bytes = Vec::with_capacity(indices.len() * 4);
            for i in indices {
                bytes.extend_from_slice(&i.to_le_bytes());
            }
            base64(&bytes)
        })
        .collect();
    let mut weights = Vec::with_capacity(mesh.bone_weights.len() * 24);
    for w in &mesh.bone_weights {
        for b in w.bones {
            weights.extend_from_slice(&(b as i32).to_le_bytes());
        }
        for v in w.weights {
            weights.extend_from_slice(&v.to_le_bytes());
        }
    }
    json!({
        "name": mesh.name,
        "vertexCount": mesh.positions.len(),
        "positions": floats(&mesh.positions),
        "normals": floats(&mesh.normals),
        "tangents": floats(&mesh.tangents),
        "uvs": mesh.uvs.iter().map(|uv| floats(uv)).collect::<Vec<_>>(),
        "colors": floats(&mesh.colors),
        "submeshes": submeshes,
        "boneWeights": base64(&weights),
        "bindPoses": floats(&mesh.bind_poses),
        "boundsMin": mesh.bounds_min,
        "boundsMax": mesh.bounds_max,
        "blendShapes": mesh.blend_shapes.iter().map(|shape| json!({
            "name": shape.name,
            "frames": shape.frames.iter().map(|f| json!({
                "weight": f.weight,
                "vertices": floats(&f.vertices),
                "normals": floats(&f.normals),
                "tangents": floats(&f.tangents),
            })).collect::<Vec<_>>(),
        })).collect::<Vec<_>>(),
    })
}

/// Standard base64 with padding.
pub(crate) fn base64(bytes: &[u8]) -> String {
    const ALPHABET: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut out = String::with_capacity(bytes.len().div_ceil(3) * 4);
    for chunk in bytes.chunks(3) {
        let b = [
            chunk[0],
            *chunk.get(1).unwrap_or(&0),
            *chunk.get(2).unwrap_or(&0),
        ];
        let n = (u32::from(b[0]) << 16) | (u32::from(b[1]) << 8) | u32::from(b[2]);
        out.push(ALPHABET[(n >> 18) as usize & 63] as char);
        out.push(ALPHABET[(n >> 12) as usize & 63] as char);
        out.push(if chunk.len() > 1 {
            ALPHABET[(n >> 6) as usize & 63] as char
        } else {
            '='
        });
        out.push(if chunk.len() > 2 {
            ALPHABET[n as usize & 63] as char
        } else {
            '='
        });
    }
    out
}

#[cfg(test)]
mod tests {
    use super::base64;

    #[test]
    fn base64_matches_the_rfc_vectors() {
        assert_eq!(base64(b""), "");
        assert_eq!(base64(b"f"), "Zg==");
        assert_eq!(base64(b"fo"), "Zm8=");
        assert_eq!(base64(b"foo"), "Zm9v");
        assert_eq!(base64(b"foobar"), "Zm9vYmFy");
    }
}
