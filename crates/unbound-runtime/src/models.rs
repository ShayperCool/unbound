//! Imported models as the engine loads them: the documents of the model's prefab (GameObjects,
//! Transforms, renderers, Materials and Mesh sub-assets with the importer's fileIDs), so that loading
//! a model and expanding a scene's instance of one go through the same prefab machinery as a
//! `.prefab` file; and the geometry of its meshes, which the Mesh documents only point at.
use serde_json::{Value as Json, json};
use unbound_assets::{
    ImportedModel,
    model::{Key, ModelClip, ModelMesh, RendererKind},
};
use unbound_yaml::{Document, Guid, Map, Value};

const GAME_OBJECT: i32 = 1;
const TRANSFORM: i32 = 4;
const MATERIAL: i32 = 21;
const MESH_RENDERER: i32 = 23;
const MESH_FILTER: i32 = 33;
const MESH: i32 = 43;
const ANIMATOR: i32 = 95;
const ANIMATION: i32 = 111;
const ANIMATION_CLIP: i32 = 74;
const SKINNED_MESH_RENDERER: i32 = 137;

/// Unity's built-in resources (`unity_builtin_extra`).
const BUILTIN_EXTRA: &str = "0000000000000000f000000000000000";
const DEFAULT_MATERIAL: i64 = 10303;
const STANDARD_SHADER: i64 = 46;

fn scalar(v: impl ToString) -> Value {
    Value::Scalar(v.to_string())
}

fn map(entries: Vec<(&str, Value)>) -> Value {
    Value::Map(Map(entries.into_iter().map(|(k, v)| (k.to_string(), v)).collect()))
}

fn local(file_id: i64) -> Value {
    map(vec![("fileID", scalar(file_id))])
}

fn external(file_id: i64, guid: &str, kind: i32) -> Value {
    map(vec![("fileID", scalar(file_id)), ("guid", scalar(guid)), ("type", scalar(kind))])
}

fn vec3(v: [f32; 3]) -> Value {
    map(vec![("x", scalar(v[0])), ("y", scalar(v[1])), ("z", scalar(v[2]))])
}

fn quat(v: [f32; 4]) -> Value {
    map(vec![("x", scalar(v[0])), ("y", scalar(v[1])), ("z", scalar(v[2])), ("w", scalar(v[3]))])
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

/// The documents of a model's prefab.
pub fn documents(guid: Guid, model: &ImportedModel) -> Vec<Document> {
    let mut docs = Vec::new();
    let guid_text = guid.to_string();

    // Material slots: a remapped Material asset, an embedded one when the importer makes
    // materials, Unity's Default-Material otherwise.
    let mut slot_refs = Vec::with_capacity(model.materials.len());
    for material in &model.materials {
        if let Some(remap) = &material.remap
            && !remap.is_null()
        {
            slot_refs.push(external(remap.file_id, &remap.guid.to_string(), 2));
        } else if model.settings.import_materials {
            docs.push(doc(
                MATERIAL,
                material.file_id.id,
                "Material",
                map(vec![
                    ("m_Name", scalar(&material.name)),
                    ("m_Shader", external(STANDARD_SHADER, BUILTIN_EXTRA, 0)),
                ]),
            ));
            slot_refs.push(local(material.file_id.id));
        } else {
            slot_refs.push(external(DEFAULT_MATERIAL, BUILTIN_EXTRA, 0));
        }
    }
    let material_ref = |slot: &Option<u32>| match slot {
        Some(i) => slot_refs.get(*i as usize).cloned().unwrap_or_else(|| local(0)),
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

    for (index, node) in model.nodes.iter().enumerate() {
        let mut components = vec![local(node.transform.id)];
        let go = node.game_object.id;
        if let Some(renderer) = &node.renderer {
            let mesh_id = model.meshes.get(renderer.mesh as usize).map_or(0, |m| m.file_id.id);
            let materials = Value::Seq(renderer.materials.iter().map(&material_ref).collect());
            match renderer.kind {
                RendererKind::Mesh => {
                    if let Some(filter) = renderer.filter {
                        components.push(local(filter.id));
                        docs.push(doc(
                            MESH_FILTER,
                            filter.id,
                            "MeshFilter",
                            map(vec![("m_GameObject", local(go)), ("m_Mesh", local(mesh_id))]),
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
                                Value::Seq(renderer.blend_shape_weights.iter().map(|w| scalar(w)).collect()),
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
                        ("m_Animation", local(model.clips.first().map_or(0, |c| c.file_id.id))),
                        ("m_Animations", Value::Seq(model.clips.iter().map(|c| local(c.file_id.id)).collect())),
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
                        ("m_Avatar", local(0)),
                        ("m_Controller", local(0)),
                        ("m_ApplyRootMotion", scalar(0)),
                    ]),
                )
            });
        }
        docs.push(doc(
            GAME_OBJECT,
            go,
            "GameObject",
            map(vec![
                (
                    "m_Component",
                    Value::Seq(components.into_iter().map(|c| map(vec![("component", c)])).collect()),
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
                let incoming = if i > 0 { slope(&keys[i - 1], key) } else { [0.0; N] };
                let outgoing = if i + 1 < keys.len() { slope(key, &keys[i + 1]) } else { incoming };
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
                let dot: f32 = (0..4).map(|c| keys[i].value[c] * keys[i - 1].value[c]).sum();
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
            let Value::Map(Map(mut entries)) = curve(&f.path, &f.keys, &[""]) else { unreachable!() };
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
            ("m_Events", Value::Seq(Vec::new())),
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
        let b = [chunk[0], *chunk.get(1).unwrap_or(&0), *chunk.get(2).unwrap_or(&0)];
        let n = (u32::from(b[0]) << 16) | (u32::from(b[1]) << 8) | u32::from(b[2]);
        out.push(ALPHABET[(n >> 18) as usize & 63] as char);
        out.push(ALPHABET[(n >> 12) as usize & 63] as char);
        out.push(if chunk.len() > 1 { ALPHABET[(n >> 6) as usize & 63] as char } else { '=' });
        out.push(if chunk.len() > 2 { ALPHABET[n as usize & 63] as char } else { '=' });
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
