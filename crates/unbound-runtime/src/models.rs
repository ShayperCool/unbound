//! Imported models as the engine loads them: the documents of the model's prefab (GameObjects,
//! Transforms, renderers, Materials and Mesh sub-assets with the importer's fileIDs), so that loading
//! a model and expanding a scene's instance of one go through the same prefab machinery as a
//! `.prefab` file; and the geometry of its meshes, which the Mesh documents only point at.
use serde_json::{Value as Json, json};
use unbound_assets::{
    ImportedModel,
    model::{
        ClipMask, CurveKey, Key, MaterialDescription, MaterialImportMode, MaterialLocation,
        ModelClip, ModelMaterial, ModelMesh, RendererKind, curves, mask_keeps,
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
    Value::Map(Map::from(
        entries
            .into_iter()
            .map(|(k, v)| (k.to_string(), v))
            .collect::<Vec<_>>(),
    ))
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
        let mut body = vec![
            ("m_Name", scalar(&mesh.name)),
            ("m_UnboundModel", scalar(&guid_text)),
            ("m_UnboundMesh", scalar(index)),
        ];
        // A skinned mesh names its bones by their paths below the model's root (Unity's
        // bone name hashes): what skins it from an Animator's skeleton once the bones are gone.
        if let Some(skin) = model.nodes.iter().find_map(|n| {
            n.renderer
                .as_ref()
                .filter(|r| r.kind == RendererKind::SkinnedMesh && r.mesh as usize == index)
        }) {
            body.push((
                "m_UnboundBonePaths",
                Value::Seq(
                    skin.bones
                        .iter()
                        .map(|&b| scalar(node_path(model, b)))
                        .collect(),
                ),
            ));
            if let Some(root) = skin.root_bone {
                body.push(("m_UnboundRootBonePath", scalar(node_path(model, root))));
            }
        }
        docs.push(doc(MESH, mesh.file_id.id, "Mesh", map(body)));
    }

    // A Humanoid model's clips are muscle clips of its avatar (or the one it copies): they carry
    // the avatar, through which the managed side turns their transform curves into muscle curves.
    let human_avatar = (settings.animation_type == 3)
        .then(|| {
            model.avatar.map(|a| local(a.id)).or_else(|| {
                settings
                    .source_avatar
                    .as_ref()
                    .map(|s| external(s.file_id, &s.guid.to_string(), 3))
            })
        })
        .flatten();
    for clip in &model.clips {
        let mask = match &clip.mask {
            ClipMask::Asset { guid, .. } => Some(mask_asset(db, path, &clip.name, guid)),
            _ => None,
        };
        let mut document = clip_document(clip, mask.as_deref());
        if let (Some(avatar), false, Value::Map(map)) =
            (&human_avatar, clip.legacy, &mut document.body)
        {
            map.0
                .push(("m_UnboundHumanAvatar".to_string(), avatar.clone()));
        }
        docs.push(document);
    }

    // The Animator's avatar: the model's own, or another model's. It carries its model's root
    // and the model's transforms in the file's order, from which the managed side builds the
    // avatar's skeleton as the importer does; a humanoid one also the meta's human description
    // (AvatarBuilder over the imported hierarchy).
    let avatar = if let Some(own) = model.avatar {
        let mut body = vec![
            ("m_Name", scalar(format!("{}Avatar", model.name))),
            (
                "m_UnboundRoot",
                local(model.nodes.first().map_or(0, |n| n.game_object.id)),
            ),
            (
                "m_UnboundSkeletonOrder",
                Value::Seq(
                    model
                        .file_order
                        .iter()
                        .filter_map(|&i| model.nodes.get(i as usize))
                        .map(|n| local(n.transform.id))
                        .collect(),
                ),
            ),
        ];
        if let Some(human) = &model.human_description {
            body.push(("m_UnboundHumanDescription", human.clone()));
        }
        docs.push(doc(AVATAR, own.id, "Avatar", map(body)));
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
                    // The importer's local bounds (Update When Offscreen off).
                    let aabb = renderer.local_bounds.map(|(centre, extent)| {
                        map(vec![("m_Center", vec3(centre)), ("m_Extent", vec3(extent))])
                    });
                    docs.push(doc(
                        SKINNED_MESH_RENDERER,
                        renderer.renderer.id,
                        "SkinnedMeshRenderer",
                        map(aabb
                            .map(|aabb| ("m_AABB", aabb))
                            .into_iter()
                            .chain([
                                ("m_GameObject", local(go)),
                                ("m_Enabled", scalar(1)),
                                ("m_CastShadows", scalar(1)),
                                ("m_ReceiveShadows", scalar(1)),
                                ("m_Materials", materials),
                                ("m_Mesh", local(mesh_id)),
                                ("m_Bones", Value::Seq(bones)),
                                ("m_RootBone", local(root)),
                                (
                                    "m_UpdateWhenOffscreen",
                                    scalar(i32::from(renderer.update_when_offscreen)),
                                ),
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
                            ])
                            .collect()),
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
                            local(
                                model
                                    .clips
                                    .iter()
                                    .find(|c| !c.preview)
                                    .map_or(0, |c| c.file_id.id),
                            ),
                        ),
                        (
                            "m_Animations",
                            Value::Seq(
                                model
                                    .clips
                                    .iter()
                                    .filter(|c| !c.preview)
                                    .map(|c| local(c.file_id.id))
                                    .collect(),
                            ),
                        ),
                        ("m_PlayAutomatically", scalar(1)),
                    ]),
                )
            } else {
                let mut body = vec![
                    ("m_GameObject", local(go)),
                    ("m_Enabled", scalar(1)),
                    ("m_Avatar", avatar.clone()),
                    ("m_Controller", local(0)),
                    // The importer applies root motion for humanoid avatars (measured:
                    // tests/projects/humanoid, the model moves with its clip).
                    (
                        "m_ApplyRootMotion",
                        scalar(model.human_description.is_some() as i32),
                    ),
                ];
                // Optimize Game Objects: the importer optimizes the hierarchy with the extra
                // exposed paths; the managed side does it when the prefab is loaded.
                if model.settings.optimize_game_objects {
                    body.push(("m_UnboundOptimizeGameObjects", scalar(1)));
                    body.push((
                        "m_UnboundExtraExposedTransformPaths",
                        Value::Seq(
                            model
                                .settings
                                .extra_exposed_transform_paths
                                .iter()
                                .map(scalar)
                                .collect(),
                        ),
                    ));
                }
                doc(ANIMATOR, animator.id, "Animator", map(body))
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

/// A node's path below the model's root: its ancestors' names joined by `/` (empty for the root).
fn node_path(model: &ImportedModel, node: u32) -> String {
    let mut names = Vec::new();
    let mut at = model.nodes.get(node as usize);
    while let Some(n) = at {
        let Some(parent) = n.parent else { break };
        names.push(n.name.as_str());
        at = model.nodes.get(parent as usize);
    }
    names.reverse();
    names.join("/")
}

/// What the managed side needs to make a model's material: the importer (`legacy` or
/// `description`), the model's path, and the description with each texture's asset found.
fn material_import(
    db: &AssetDb,
    model_path: &str,
    mode: MaterialImportMode,
    description: &MaterialDescription,
) -> Value {
    let named = |entries: Vec<(String, Value)>| {
        Value::Map(Map::from(entries.into_iter().collect::<Vec<_>>()))
    };
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

/// The transform list of the AvatarMask a clip copies its mask from (`maskSource`). The Editor
/// reads the asset when it imports, not the copy in the clip's `transformMask`. A missing or
/// unreadable mask keeps every curve, with a warning.
fn mask_asset(db: &AssetDb, model: &str, clip: &str, guid: &str) -> Vec<(String, f32)> {
    let read = || -> Option<Vec<(String, f32)>> {
        let asset = db.by_guid(Guid::parse(guid)?)?;
        let text = std::fs::read_to_string(&asset.file).ok()?;
        let docs = unbound_yaml::parse_documents(&text).ok()?;
        let mask = docs.iter().find(|d| d.type_name == "AvatarMask")?;
        Some(
            mask.body
                .get("m_Elements")
                .and_then(Value::as_seq)
                .unwrap_or_default()
                .iter()
                .map(|e| {
                    (
                        e.get("m_Path")
                            .and_then(Value::as_str)
                            .unwrap_or_default()
                            .to_string(),
                        e.get("m_Weight").and_then(Value::as_f64).unwrap_or(1.0) as f32,
                    )
                })
                .collect(),
        )
    };
    read().unwrap_or_else(|| {
        tracing::warn!("{model}: clip '{clip}' copies its mask from a missing AvatarMask ({guid}); masking nothing");
        Vec::new()
    })
}

/// A clip as Unity serializes an AnimationClip, so the managed clip reader takes it as it is:
/// position, rotation and scale curves per node path, with slopes from the neighbouring keys;
/// curves of transforms an asset `mask` leaves out are not there.
fn clip_document(clip: &ModelClip, mask: Option<&[(String, f32)]>) -> Document {
    fn axes<const N: usize>(names: &[&'static str; N], v: [f32; N]) -> Value {
        // A float curve's values are plain numbers.
        if N == 1 && names[0].is_empty() {
            return scalar(v[0]);
        }
        map(names.iter().zip(v).map(|(n, x)| (*n, scalar(x))).collect())
    }
    fn curve<const N: usize>(
        path: &str,
        keys: &[Key<N>],
        slopes: curves::Slopes<N>,
        names: &[&'static str; N],
    ) -> Value {
        let third = [1.0 / 3.0; N];
        let frames = keys
            .iter()
            .zip(slopes)
            .map(|(key, (incoming, outgoing))| {
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
    // A Humanoid clip's nodes all stay for the conversion; its mask picks, among the paths the
    // importer lists, the transforms whose curves the converted clip keeps.
    let human_transforms: Option<Vec<Value>> = clip.human_transforms.as_ref().map(|paths| {
        paths
            .iter()
            .filter(|path| mask.is_none_or(|m| mask_keeps(m, path)))
            .map(|path| scalar(path.as_str()))
            .collect()
    });
    for node in &clip.curves {
        if human_transforms.is_none() && mask.is_some_and(|m| !mask_keeps(m, &node.path)) {
            continue;
        }
        if !node.positions.is_empty() {
            positions.push(curve(
                &node.path,
                &node.positions,
                node.positions_slopes(),
                &XYZ,
            ));
        }
        if !node.rotations.is_empty() {
            let (keys, slopes) = node.rotation_track(clip.smooth);
            rotations.push(curve(&node.path, &keys, slopes, &XYZW));
        }
        if !node.scales.is_empty() {
            scales.push(curve(
                &node.path,
                &node.scales,
                node.scales_slopes(clip.smooth),
                &XYZ,
            ));
        }
    }
    let float_curve = |curve: Value, attribute: &str, class_id: i32| {
        let Value::Map(curve_map) = curve else {
            unreachable!()
        };
        let mut entries = curve_map.0;
        entries.push(("attribute".to_string(), scalar(attribute)));
        entries.push(("classID".to_string(), scalar(class_id)));
        entries.push(("script".to_string(), map(vec![("fileID", scalar(0))])));
        Value::Map(Map::from(entries))
    };
    let mut floats: Vec<Value> = clip
        .floats
        .iter()
        .map(|f| {
            float_curve(
                curve(&f.path, &f.keys, f.key_slopes(), &[""]),
                &f.attribute,
                f.class_id,
            )
        })
        .collect();
    // Parameter curves: on the root's Animator, keys with their own tangents.
    for parameter in &clip.parameters {
        let key = |k: &CurveKey| {
            map(vec![
                ("serializedVersion", scalar(3)),
                ("time", scalar(k.time)),
                ("value", scalar(k.value)),
                ("inSlope", scalar(k.in_slope)),
                ("outSlope", scalar(k.out_slope)),
                ("tangentMode", scalar(k.tangent_mode)),
                ("weightedMode", scalar(k.weighted_mode)),
                ("inWeight", scalar(k.in_weight)),
                ("outWeight", scalar(k.out_weight)),
            ])
        };
        let curve = map(vec![
            (
                "curve",
                map(vec![
                    ("serializedVersion", scalar(2)),
                    (
                        "m_Curve",
                        Value::Seq(parameter.keys.iter().map(key).collect()),
                    ),
                    ("m_PreInfinity", scalar(parameter.pre_infinity)),
                    ("m_PostInfinity", scalar(parameter.post_infinity)),
                    ("m_RotationOrder", scalar(4)),
                ]),
            ),
            ("path", scalar("")),
        ]);
        floats.push(float_curve(curve, &parameter.name, ANIMATOR));
    }
    let settings = &clip.settings;
    let reference = &clip.additive_reference;
    doc(
        ANIMATION_CLIP,
        clip.file_id.id,
        "AnimationClip",
        map(vec![
            // Imported clips are NotEditable; a take's preview clip is also HideInHierarchy.
            (
                "m_ObjectHideFlags",
                scalar(if clip.preview { 9 } else { 8 }),
            ),
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
                "m_UnboundHumanKeyReduction",
                scalar(clip.human_key_reduction.unwrap_or(0.0)),
            ),
            (
                "m_UnboundHumanTransformPaths",
                human_transforms.map_or(Value::Null, Value::Seq),
            ),
            (
                "m_AnimationClipSettings",
                map(vec![
                    ("m_StartTime", scalar(0)),
                    ("m_StopTime", scalar(clip.length)),
                    ("m_LoopTime", scalar(clip.loop_time as i32)),
                    ("m_LoopBlend", scalar(clip.loop_blend as i32)),
                    ("m_CycleOffset", scalar(clip.cycle_offset)),
                    (
                        "m_HasAdditiveReferencePose",
                        scalar(reference.enabled as i32),
                    ),
                    (
                        "m_AdditiveReferencePoseClip",
                        local(reference.clip.unwrap_or(0)),
                    ),
                    ("m_AdditiveReferencePoseTime", scalar(reference.time)),
                    (
                        "m_OrientationOffsetY",
                        scalar(settings.orientation_offset_y),
                    ),
                    ("m_Level", scalar(settings.level)),
                    (
                        "m_LoopBlendOrientation",
                        scalar(settings.loop_blend_orientation as i32),
                    ),
                    (
                        "m_LoopBlendPositionY",
                        scalar(settings.loop_blend_position_y as i32),
                    ),
                    (
                        "m_LoopBlendPositionXZ",
                        scalar(settings.loop_blend_position_xz as i32),
                    ),
                    (
                        "m_KeepOriginalOrientation",
                        scalar(settings.keep_original_orientation as i32),
                    ),
                    (
                        "m_KeepOriginalPositionY",
                        scalar(settings.keep_original_position_y as i32),
                    ),
                    (
                        "m_KeepOriginalPositionXZ",
                        scalar(settings.keep_original_position_xz as i32),
                    ),
                    ("m_HeightFromFeet", scalar(settings.height_from_feet as i32)),
                    ("m_Mirror", scalar(settings.mirror as i32)),
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
