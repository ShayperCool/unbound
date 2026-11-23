//! The lighting data asset a bake writes beside its scene (`LightingData.asset`), as the Unity
//! 6000.0.71f1 Editor writes it: a binary serialized file of the bake's LightingDataAsset (path
//! ID 112000000) and its LightProbes (25800002), with the Editor's own type trees
//! (`data/serialized-types-<family>.json`, made by tests/projects/bakeenvironment `types.py`).
//! Made here: a bake of the environment alone (no lightmaps, light probes or realtime GI): the
//! scene it belongs to, the Directional Mode, the ambient probe and the default reflection.
use crate::serialized_writer::{self, ExternalFile, File, FileObject, RawNode, RawType};
use anyhow::{Context, Result, bail};
use unbound_compat::{MeasuredFamily, UnityVersion};
use unbound_yaml::{Map, Value};

/// The LightingDataAsset's path ID in its file (its .meta's `mainObjectFileID`).
pub const LIGHTING_DATA_PATH_ID: i64 = 112_000_000;
/// The LightProbes' path ID.
const LIGHT_PROBES_PATH_ID: i64 = 25_800_002;
/// The path ID a scene file's SceneAsset has.
const SCENE_ASSET_PATH_ID: i64 = 102_900_000;
/// The path ID of a cubemap imported from an image (a reflection probe's EXR).
const CUBEMAP_PATH_ID: i64 = 8_900_000;
/// A renderer's dynamic (realtime) lightmap index for none.
const NO_DYNAMIC_LIGHTMAP: &str = "65535";
/// The path ID of an imported image's texture.
const TEXTURE_PATH_ID: i64 = 2_800_000;
/// A project asset in a file's list of external files.
const ASSET_FILE: i32 = 3;
/// The platform an Editor's project files are written for.
const EDITOR_PLATFORM: i32 = -2;
/// The LightProbes' hide flags (HideInHierarchy | DontSaveInEditor).
const LIGHT_PROBES_HIDE_FLAGS: &str = "9";
/// The version of the realtime GI data's format a bake writes (Unity 6000.0's).
const REALTIME_DATA_VERSION: &str = "112";
/// The realtime GI data of a bake without realtime GI: an empty archive of the release that
/// writes it: its header (signature, format 8, the "5.x.x" player version, the release, its size,
/// the sizes of its blocks' table compressed and not, its flags: LZ4HC, the table at the end),
/// padding to 16 bytes (which the Editor leaves unset; zeros here) and the table.
fn empty_realtime_archive(unity_version: &str) -> Vec<u8> {
    /// The blocks' table of an archive with no files, compressed (LZ4HC), and its size whole.
    const EMPTY_TABLE: [u8; 10] = [0x1e, 0x00, 0x01, 0x00, 0x50, 0x00, 0x00, 0x00, 0x00, 0x00];
    const TABLE_SIZE: u32 = 24;
    const FLAGS: u32 = 0xc3;
    let mut header = Vec::new();
    header.extend(b"UnityFS\0");
    header.extend(8u32.to_be_bytes());
    header.extend(b"5.x.x\0");
    header.extend(unity_version.as_bytes());
    header.push(0);
    let fields_at = header.len();
    header.extend([0u8; 8]); // the size, below
    header.extend((EMPTY_TABLE.len() as u32).to_be_bytes());
    header.extend(TABLE_SIZE.to_be_bytes());
    header.extend(FLAGS.to_be_bytes());
    header.resize(header.len().next_multiple_of(16), 0);
    header.extend(EMPTY_TABLE);
    let size = header.len() as u64;
    header[fields_at..fields_at + 8].copy_from_slice(&size.to_be_bytes());
    header
}

/// The measured type trees of a release family's lighting data.
fn measured(family: MeasuredFamily) -> &'static str {
    match family {
        MeasuredFamily::Unity6000_0 => include_str!("../data/serialized-types-6000.0.json"),
    }
}

/// The type trees of `unity_version`'s family (a loud failure outside every measured family).
fn types(unity_version: &str) -> Result<Vec<RawType>> {
    let version: UnityVersion = unity_version
        .parse()
        .with_context(|| format!("the Unity release {unity_version:?}"))?;
    let family = version.measured_family().with_context(|| {
        format!("the lighting data of Unity {unity_version} is not measured (its release family)")
    })?;
    let text = measured(family);
    let table: serde_json::Value = serde_json::from_str(text)?;
    let hex16 = |v: &serde_json::Value| -> Result<[u8; 16]> {
        let text = v.as_str().context("a hash")?;
        let bytes: Vec<u8> = (0..16)
            .map(|k| u8::from_str_radix(&text[2 * k..2 * k + 2], 16))
            .collect::<Result<_, _>>()?;
        Ok(bytes.try_into().unwrap())
    };
    let mut out = Vec::new();
    for t in table["types"].as_array().context("types")? {
        let strings_hex = t["strings"].as_str().context("strings")?;
        let strings = (0..strings_hex.len() / 2)
            .map(|k| u8::from_str_radix(&strings_hex[2 * k..2 * k + 2], 16))
            .collect::<Result<_, _>>()?;
        let nodes = t["nodes"]
            .as_array()
            .context("nodes")?
            .iter()
            .map(|n| -> Result<RawNode> {
                let f = |k: usize| n[k].as_u64().context("a node field");
                Ok(RawNode {
                    version: f(0)? as u16,
                    level: f(1)? as u8,
                    type_flags: f(2)? as u8,
                    type_offset: f(3)? as u32,
                    name_offset: f(4)? as u32,
                    byte_size: n[5].as_i64().context("a node field")? as i32,
                    index: n[6].as_i64().context("a node field")? as i32,
                    meta_flag: f(7)? as u32,
                    ref_hash: f(8)?,
                })
            })
            .collect::<Result<_>>()?;
        out.push(RawType {
            class_id: t["classId"].as_i64().context("classId")? as i32,
            stripped: t["stripped"].as_u64().context("stripped")? as u8,
            script_index: t["scriptIndex"].as_i64().context("scriptIndex")? as i16,
            script_hash: t["scriptHash"]
                .as_str()
                .map(|_| hex16(&t["scriptHash"]))
                .transpose()?,
            hash: hex16(&t["hash"])?,
            nodes,
            strings,
            dependencies: t["dependencies"]
                .as_array()
                .context("dependencies")?
                .iter()
                .map(|d| d.as_i64().unwrap_or(0) as i32)
                .collect(),
        });
    }
    Ok(out)
}

/// What a bake of the environment alone puts in its lighting data.
pub struct Environment {
    /// The release that writes the file ("6000.0.71f1"): its family's measured type trees are
    /// written.
    pub unity_version: String,
    /// The scene's GUID (its .meta's).
    pub scene_guid: [u8; 16],
    /// The Lighting Settings' Directional Mode (0 non-directional, 1 directional).
    pub lightmaps_mode: i32,
    /// The ambient probe, 27 coefficients (red, green, blue), linear.
    pub ambient_probe: [f32; 27],
    /// The default reflection's image (its .meta's GUID), where the bake made one.
    pub reflection_guid: Option<[u8; 16]>,
}

/// What a bake with baked global illumination adds to its lighting data.
#[derive(Clone, Debug, Default)]
pub struct Baked {
    /// Each lightmap's images, in order.
    pub lightmaps: Vec<LightmapImages>,
    /// The renderers with a lightmap, in the bake's order.
    pub renderers: Vec<LightmappedRenderer>,
    /// The bake's lights, in its order.
    pub lights: Vec<BakedLight>,
}

/// A lightmap's images, their .meta's GUIDs: its light (Lightmap-N_comp_light.exr), and where
/// the bake makes them its directions (Lightmap-N_comp_dir.png) and its shadowmask
/// (Lightmap-N_comp_shadowmask.png).
#[derive(Clone, Copy, Debug)]
pub struct LightmapImages {
    pub light: [u8; 16],
    pub directions: Option<[u8; 16]>,
    pub shadowmask: Option<[u8; 16]>,
}

/// A renderer's place in the lightmaps (Renderer.lightmapIndex and lightmapScaleOffset).
#[derive(Clone, Copy, Debug)]
pub struct LightmappedRenderer {
    /// Its file ID in the scene.
    pub file_id: i64,
    pub lightmap_index: i32,
    pub scale_offset: [f32; 4],
}

/// What the bake did with a light (Light.bakingOutput).
#[derive(Clone, Copy, Debug)]
pub struct BakedLight {
    /// Its file ID in the scene.
    pub file_id: i64,
    /// LightmapBakeType: 2 baked, 1 mixed.
    pub bake_type: i32,
    /// MixedLightingMode: 0 Baked Indirect, 1 Subtractive, 2 Shadowmask.
    pub mixed_mode: i32,
    /// The shadowmask channel, -1 for none.
    pub occlusion_mask_channel: i32,
    pub probe_occlusion_light_index: i32,
}

/// A GUID as a .meta writes it (32 hex digits, each byte's low nibble first) in a file's bytes.
pub fn guid_bytes(text: &str) -> Result<[u8; 16]> {
    if text.len() != 32 {
        bail!("a GUID of {} digits", text.len());
    }
    let mut out = [0u8; 16];
    for (k, byte) in out.iter_mut().enumerate() {
        let swapped = format!("{}{}", &text[2 * k + 1..2 * k + 2], &text[2 * k..2 * k + 1]);
        *byte = u8::from_str_radix(&swapped, 16)?;
    }
    Ok(out)
}

/// The lighting data file's bytes of a bake of the environment alone.
pub fn environment(environment: &Environment) -> Result<Vec<u8>> {
    baked(environment, &Baked::default())
}

/// The lighting data file's bytes: the environment's, and the baked lightmaps, their renderers
/// and the bake's lights.
pub fn baked(environment: &Environment, baked: &Baked) -> Result<Vec<u8>> {
    let types = types(&environment.unity_version)?;
    let index = |class: i32| {
        types
            .iter()
            .position(|t| t.class_id == class)
            .with_context(|| format!("no type tree of class {class}"))
    };
    let (data_type, probes_type) = (index(1120)?, index(258)?);
    let pptr = |file: i32, path: i64| {
        Value::Map(Map::from(vec![
            ("m_FileID".into(), Value::Scalar(file.to_string())),
            ("m_PathID".into(), Value::Scalar(path.to_string())),
        ]))
    };
    let mut data = default_value(&types[data_type], 0);
    set(&mut data, "m_Name", Value::Scalar("LightingData".into()))?;
    set(&mut data, "m_Scene", pptr(1, SCENE_ASSET_PATH_ID))?;
    set(&mut data, "m_LightProbes", pptr(0, LIGHT_PROBES_PATH_ID))?;
    set(
        &mut data,
        "m_LightmapsMode",
        Value::Scalar(environment.lightmaps_mode.to_string()),
    )?;
    let probe = data
        .get_mut_field("m_BakedAmbientProbeInLinear")
        .context("no ambient probe field")?;
    for (k, v) in environment.ambient_probe.iter().enumerate() {
        probe.set_field(&format!("sh[{k:2}]"), Value::Scalar(format!("{v:?}")))?;
    }
    let mut externals = vec![ExternalFile {
        guid: environment.scene_guid,
        kind: ASSET_FILE,
        path: String::new(),
    }];
    // The lightmaps' images name files of their own, after the scene and before the
    // reflection's: each lightmap's light, then its directions or shadowmask.
    let mut lightmaps = Vec::new();
    for images in &baked.lightmaps {
        let mut entry = element_default(&types[data_type], "m_Lightmaps")?;
        for (field, guid) in [
            ("m_Lightmap", Some(images.light)),
            ("m_DirLightmap", images.directions),
            ("m_ShadowMask", images.shadowmask),
        ] {
            let Some(guid) = guid else { continue };
            externals.push(ExternalFile {
                guid,
                kind: ASSET_FILE,
                path: String::new(),
            });
            entry.set_field(field, pptr(externals.len() as i32, TEXTURE_PATH_ID))?;
        }
        lightmaps.push(entry);
    }
    set(&mut data, "m_Lightmaps", Value::Seq(lightmaps))?;
    let mut renderers = Vec::new();
    let mut renderer_ids = Vec::new();
    for r in &baked.renderers {
        let mut entry = element_default(&types[data_type], "m_LightmappedRendererData")?;
        // As the Editor writes a renderer without realtime GI or terrain: no dynamic lightmap,
        // identity scale and offsets (tests/projects/bakedbuffers lightingData.json).
        entry.set_field(
            "lightmapIndexDynamic",
            Value::Scalar(NO_DYNAMIC_LIGHTMAP.into()),
        )?;
        for field in [
            "terrainDynamicUVST",
            "terrainChunkDynamicUVST",
            "lightmapSTDynamic",
        ] {
            let st = entry
                .get_mut_field(field)
                .with_context(|| format!("no {field}"))?;
            for (name, v) in ["x", "y", "z", "w"]
                .iter()
                .zip(["1.0", "1.0", "0.0", "0.0"])
            {
                st.set_field(name, Value::Scalar(v.into()))?;
            }
        }
        entry.set_field("lightmapIndex", Value::Scalar(r.lightmap_index.to_string()))?;
        let st = entry.get_mut_field("lightmapST").context("no lightmapST")?;
        for (name, v) in ["x", "y", "z", "w"].iter().zip(r.scale_offset) {
            st.set_field(name, Value::Scalar(format!("{v:?}")))?;
        }
        renderers.push(entry);
        renderer_ids.push(object_id(
            &types[data_type],
            "m_LightmappedRendererDataIDs",
            r.file_id,
        )?);
    }
    set(
        &mut data,
        "m_LightmappedRendererData",
        Value::Seq(renderers),
    )?;
    set(
        &mut data,
        "m_LightmappedRendererDataIDs",
        Value::Seq(renderer_ids),
    )?;
    let mut lights = Vec::new();
    let mut outputs = Vec::new();
    for l in &baked.lights {
        lights.push(object_id(&types[data_type], "m_Lights", l.file_id)?);
        let mut output = element_default(&types[data_type], "m_LightBakingOutputs")?;
        output.set_field(
            "probeOcclusionLightIndex",
            Value::Scalar(l.probe_occlusion_light_index.to_string()),
        )?;
        output.set_field(
            "occlusionMaskChannel",
            Value::Scalar(l.occlusion_mask_channel.to_string()),
        )?;
        let mode = output
            .get_mut_field("lightmapBakeMode")
            .context("no lightmapBakeMode")?;
        mode.set_field("lightmapBakeType", Value::Scalar(l.bake_type.to_string()))?;
        mode.set_field("mixedLightingMode", Value::Scalar(l.mixed_mode.to_string()))?;
        output.set_field("isBaked", Value::Scalar("true".into()))?;
        outputs.push(output);
    }
    set(&mut data, "m_Lights", Value::Seq(lights))?;
    set(&mut data, "m_LightBakingOutputs", Value::Seq(outputs))?;
    if let Some(guid) = environment.reflection_guid {
        externals.push(ExternalFile {
            guid,
            kind: ASSET_FILE,
            path: String::new(),
        });
        set(
            &mut data,
            "m_BakedReflectionProbeCubemaps",
            Value::Seq(vec![pptr(externals.len() as i32, CUBEMAP_PATH_ID)]),
        )?;
    }
    set(
        &mut data,
        "m_EnlightenData",
        Value::Scalar(
            empty_realtime_archive(&environment.unity_version)
                .iter()
                .map(|b| format!("{b:02x}"))
                .collect(),
        ),
    )?;
    set(
        &mut data,
        "m_EnlightenDataVersion",
        Value::Scalar(REALTIME_DATA_VERSION.into()),
    )?;
    let mut probes = default_value(&types[probes_type], 0);
    set(
        &mut probes,
        "m_ObjectHideFlags",
        Value::Scalar(LIGHT_PROBES_HIDE_FLAGS.into()),
    )?;
    set(&mut probes, "m_Name", Value::Scalar("(Clone)".into()))?;
    let objects = vec![
        FileObject {
            path_id: LIGHT_PROBES_PATH_ID,
            type_index: probes_type,
            data: serialized_writer::write_value(&types[probes_type], &probes)?,
        },
        FileObject {
            path_id: LIGHTING_DATA_PATH_ID,
            type_index: data_type,
            data: serialized_writer::write_value(&types[data_type], &data)?,
        },
    ];
    Ok(serialized_writer::write(&File {
        unity_version: environment.unity_version.clone(),
        platform: EDITOR_PLATFORM,
        types,
        objects,
        // The lighting data first, then its light probes.
        data_order: vec![1, 0],
        externals,
    }))
}

/// A node's value as a new object of the type has it: zeros, empty strings and arrays.
fn default_value(t: &RawType, i: usize) -> Value {
    let kids = t.children_of(i);
    let type_name = t.type_name(i);
    if type_name == "TypelessData" {
        return Value::Scalar(String::new());
    }
    if kids.is_empty() {
        return Value::Scalar(if type_name == "bool" { "false" } else { "0" }.into());
    }
    if t.nodes[kids[0]].type_flags & 1 != 0 {
        let element = t.children_of(kids[0]).get(1).copied();
        let bytes = element.is_some_and(|e| {
            t.children_of(e).is_empty() && matches!(t.type_name(e), "char" | "UInt8" | "SInt8")
        });
        return if bytes {
            Value::Scalar(String::new())
        } else {
            Value::Seq(Vec::new())
        };
    }
    Value::Map(Map::from(
        kids.iter()
            .map(|&k| (t.field_name(k).to_string(), default_value(t, k)))
            .collect::<Vec<_>>(),
    ))
}

/// A new element of the top-level array field `field` of type `t`.
fn element_default(t: &RawType, field: &str) -> Result<Value> {
    let node = t
        .children_of(0)
        .into_iter()
        .find(|&k| t.field_name(k) == field)
        .with_context(|| format!("no field `{field}`"))?;
    let array = *t.children_of(node).first().context("not an array")?;
    let element = *t
        .children_of(array)
        .get(1)
        .context("an array without elements")?;
    Ok(default_value(t, element))
}

/// A scene object's identifier (SceneObjectIdentifier) as an element of the array `field`.
fn object_id(t: &RawType, field: &str, file_id: i64) -> Result<Value> {
    let mut id = element_default(t, field)?;
    id.set_field("targetObject", Value::Scalar(file_id.to_string()))?;
    id.set_field("targetPrefab", Value::Scalar("0".into()))?;
    Ok(id)
}

fn set(value: &mut Value, field: &str, to: Value) -> Result<()> {
    value.set_field(field, to)
}

trait Fields {
    fn get_mut_field(&mut self, field: &str) -> Option<&mut Value>;
    fn set_field(&mut self, field: &str, to: Value) -> Result<()>;
}

impl Fields for Value {
    fn get_mut_field(&mut self, field: &str) -> Option<&mut Value> {
        match self {
            Value::Map(map) => map.get_mut(field),
            _ => None,
        }
    }

    fn set_field(&mut self, field: &str, to: Value) -> Result<()> {
        *self
            .get_mut_field(field)
            .with_context(|| format!("no field `{field}`"))? = to;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::serialized::SerializedFile;

    fn reference(scene: &str) -> Vec<u8> {
        std::fs::read(std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join(format!(
            "../../tests/projects/bakeenvironment/Reference/unity-6000.0.71f1/{scene}/written/LightingData.asset"
        )))
        .unwrap()
    }

    /// The ambient probe the reference holds.
    fn probe_of(data: &[u8]) -> [f32; 27] {
        let file = SerializedFile::parse(data).unwrap();
        let value = file.read(LIGHTING_DATA_PATH_ID).unwrap();
        let probe = value.get("m_BakedAmbientProbeInLinear").unwrap();
        std::array::from_fn(|k| {
            probe
                .get(&format!("sh[{k:2}]"))
                .unwrap()
                .as_str()
                .unwrap()
                .parse()
                .unwrap()
        })
    }

    #[test]
    fn the_lighting_data_of_an_environment_bake_is_the_editors() {
        // With a sky (the reflection's GUID is zeros in the reference, new with every bake),
        // and without one (no reflection).
        for (scene, reflection) in [("EnvSky", Some([0u8; 16])), ("EnvNoSky", None)] {
            let expected = reference(scene);
            let externals = serialized_writer::raw_externals(&expected).unwrap();
            let made = environment(&Environment {
                unity_version: "6000.0.71f1".into(),
                scene_guid: externals[0].guid,
                lightmaps_mode: 1,
                ambient_probe: probe_of(&expected),
                reflection_guid: reflection,
            })
            .unwrap();
            let first = made.iter().zip(&expected).position(|(a, b)| a != b);
            assert!(
                made.len() == expected.len() && first.is_none(),
                "{scene}: {} bytes, expected {}; first difference at {first:?}",
                made.len(),
                expected.len()
            );
        }
    }

    #[test]
    fn the_baked_lightmaps_renderers_and_lights_are_written() {
        let environment = Environment {
            unity_version: "6000.0.71f1".into(),
            scene_guid: [1; 16],
            lightmaps_mode: 0,
            ambient_probe: [0.0; 27],
            reflection_guid: Some([4; 16]),
        };
        let made = baked(
            &environment,
            &Baked {
                lightmaps: vec![
                    LightmapImages {
                        light: [2; 16],
                        directions: None,
                        shadowmask: None,
                    },
                    LightmapImages {
                        light: [3; 16],
                        directions: None,
                        shadowmask: None,
                    },
                ],
                renderers: vec![
                    LightmappedRenderer {
                        file_id: 1161936178,
                        lightmap_index: 0,
                        scale_offset: [0.33057046, 0.33057046, 0.6549093, 0.15490928],
                    },
                    LightmappedRenderer {
                        file_id: 267123693,
                        lightmap_index: 1,
                        scale_offset: [0.625, 0.625, 0.0, 0.0],
                    },
                ],
                lights: vec![BakedLight {
                    file_id: 1569681067,
                    bake_type: 2,
                    mixed_mode: 0,
                    occlusion_mask_channel: -1,
                    probe_occlusion_light_index: 0,
                }],
            },
        )
        .unwrap();
        // The files named: the scene, the lightmaps in order, then the reflection.
        let externals = serialized_writer::raw_externals(&made).unwrap();
        assert_eq!(
            externals.iter().map(|e| e.guid[0]).collect::<Vec<_>>(),
            [1, 2, 3, 4]
        );
        let file = SerializedFile::parse(&made).unwrap();
        let data = file.read(LIGHTING_DATA_PATH_ID).unwrap();
        let lightmaps = data.get("m_Lightmaps").unwrap().as_seq().unwrap();
        let lightmap = |k: usize, field: &str| {
            lightmaps[k]
                .get("m_Lightmap")
                .unwrap()
                .get(field)
                .unwrap()
                .as_str()
                .unwrap()
                .to_string()
        };
        assert_eq!(
            (
                lightmap(0, "m_FileID"),
                lightmap(1, "m_FileID"),
                lightmap(1, "m_PathID")
            ),
            ("2".into(), "3".into(), "2800000".into())
        );
        let renderers = data
            .get("m_LightmappedRendererData")
            .unwrap()
            .as_seq()
            .unwrap();
        let ids = data
            .get("m_LightmappedRendererDataIDs")
            .unwrap()
            .as_seq()
            .unwrap();
        assert_eq!(
            ids[1].get("targetObject").unwrap().as_str(),
            Some("267123693")
        );
        assert_eq!(
            renderers[1].get("lightmapIndex").unwrap().as_str(),
            Some("1")
        );
        assert_eq!(
            renderers[0]
                .get("lightmapST")
                .unwrap()
                .get("w")
                .unwrap()
                .as_str(),
            Some("0.15490928")
        );
        assert_eq!(
            renderers[0].get("lightmapIndexDynamic").unwrap().as_str(),
            Some("65535")
        );
        let outputs = data.get("m_LightBakingOutputs").unwrap().as_seq().unwrap();
        assert_eq!(
            outputs[0].get("occlusionMaskChannel").unwrap().as_str(),
            Some("-1")
        );
        assert_eq!(outputs[0].get("isBaked").unwrap().as_str(), Some("true"));
        assert_eq!(
            data.get("m_Lights").unwrap().as_seq().unwrap()[0]
                .get("targetObject")
                .unwrap()
                .as_str(),
            Some("1569681067")
        );
    }

    #[test]
    fn a_guid_is_read_with_each_bytes_low_nibble_first() {
        assert_eq!(
            guid_bytes("05fda24a8b1a6c4438db10d5708b8352").unwrap()[..2],
            [0x50, 0xdf]
        );
        assert!(guid_bytes("05fd").is_err());
    }

    #[test]
    fn an_unmeasured_family_has_no_lighting_data() {
        let error = environment(&Environment {
            unity_version: "6000.3.0f1".into(),
            scene_guid: [0; 16],
            lightmaps_mode: 1,
            ambient_probe: [0.0; 27],
            reflection_guid: None,
        })
        .unwrap_err();
        assert!(error.to_string().contains("not measured"), "{error}");
        // Every release of the measured family has it.
        assert!(
            environment(&Environment {
                unity_version: "6000.0.12f1".into(),
                scene_guid: [0; 16],
                lightmaps_mode: 1,
                ambient_probe: [0.0; 27],
                reflection_guid: None,
            })
            .is_ok()
        );
    }
}
