//! Lighting data assets: a scene's baked `LightingData` (lightmaps, per-renderer lightmap
//! placement, light probes, what each light was baked into), or the built-in one
//! (`unity_builtin_extra`) a scene without baked lighting points at, which holds the default
//! skybox's ambient probe.
use crate::Importer;
use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};
use std::path::Path;
use unbound_yaml::Value;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct LightingData {
    pub name: String,
    pub lightmaps_mode: i32,
    /// `m_BakedAmbientProbeInLinear`: SphericalHarmonicsL2 coefficients, red then green then blue.
    pub ambient_probe: Vec<f32>,
    /// The skybox's reflection cubemap: the first of `m_BakedReflectionProbeCubemaps` when it
    /// has one more than there are probes.
    pub default_reflection: Option<ObjectPtr>,
    /// `m_Lightmaps`: per lightmap, its color, directional and shadowmask textures.
    pub lightmaps: Vec<[Option<ObjectPtr>; 3]>,
    /// `m_LightmappedRendererData` by `m_LightmappedRendererDataIDs`.
    pub renderers: Vec<RendererLighting>,
    /// `m_LightBakingOutputs` by `m_Lights`.
    pub lights: Vec<LightOutput>,
    /// The `LightProbes` object `m_LightProbes` names (in this file).
    pub probes: Option<LightProbeData>,
}

/// A reference out of a serialized file: `guid` None is the file itself.
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct ObjectPtr {
    pub guid: Option<String>,
    pub path_id: i64,
}

/// A scene object a lighting data entry is of: its file id in the scene, or (in a prefab
/// instance) its file id in the prefab and the instance's.
#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq)]
pub struct SceneObjectId {
    pub object: i64,
    pub prefab: i64,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct RendererLighting {
    pub id: SceneObjectId,
    /// 0xFFFF: none (a renderer `lightmapIndex` of -1).
    pub lightmap_index: i32,
    pub lightmap_st: [f32; 4],
    pub lightmap_index_dynamic: i32,
    pub lightmap_st_dynamic: [f32; 4],
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct LightOutput {
    pub id: SceneObjectId,
    pub probe_occlusion_light_index: i32,
    pub occlusion_mask_channel: i32,
    pub lightmap_bake_type: i32,
    pub mixed_lighting_mode: i32,
    pub is_baked: bool,
}

/// Baked light probes: positions, their SphericalHarmonicsL2 coefficients (27 each, red then
/// green then blue) and the tetrahedralization that interpolates them.
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct LightProbeData {
    pub positions: Vec<[f32; 3]>,
    pub coefficients: Vec<[f32; 27]>,
    pub tetrahedra: Vec<Tetrahedron>,
    /// Per probe on the hull, the direction outer cells extend it in.
    pub hull_rays: Vec<[f32; 3]>,
}

/// A cell: four probes (an outer cell's fourth is -1: a hull triangle extended along its probes'
/// rays), the cells across the faces opposite each, and (inner cells) the matrix taking a point
/// less the fourth probe to the first three barycentric coordinates.
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct Tetrahedron {
    pub indices: [i32; 4],
    pub neighbors: [i32; 4],
    pub matrix: [f32; 12],
}

fn scalar<T: std::str::FromStr>(v: &Value, key: &str) -> Option<T> {
    v.get(key)?.as_str()?.parse().ok()
}

fn vector<const N: usize>(v: &Value, keys: [&str; N]) -> Option<[f32; N]> {
    let mut out = [0.0; N];
    for (o, k) in out.iter_mut().zip(keys) {
        *o = scalar(v, k)?;
    }
    Some(out)
}

fn sh27(v: &Value) -> Option<[f32; 27]> {
    let mut out = [0.0; 27];
    for (k, o) in out.iter_mut().enumerate() {
        *o = scalar(v, &format!("sh[{k:2}]"))?;
    }
    Some(out)
}

fn seq<'v>(v: &'v Value, key: &str) -> &'v [Value] {
    v.get(key).and_then(Value::as_seq).unwrap_or_default()
}

/// A LightingDataAsset of a serialized file, its references resolved by the file's externals.
pub fn read_lighting_data(
    file: &crate::serialized::SerializedFile,
    path_id: i64,
) -> Result<LightingData> {
    let object = file.read(path_id)?;
    let ptr = |v: &Value| -> Option<ObjectPtr> {
        let file_id: usize = scalar(v, "m_FileID")?;
        let path_id: i64 = scalar(v, "m_PathID")?;
        if path_id == 0 {
            return None;
        }
        let guid = match file_id {
            0 => None,
            k => Some(file.externals.get(k - 1)?.guid_hex()),
        };
        Some(ObjectPtr { guid, path_id })
    };
    let name = object
        .get("m_Name")
        .and_then(Value::as_str)
        .unwrap_or("")
        .to_string();
    let lightmaps_mode = scalar(&object, "m_LightmapsMode").unwrap_or(0);
    let ambient_probe = sh27(
        object
            .get("m_BakedAmbientProbeInLinear")
            .context("lighting data without an ambient probe")?,
    )
    .context("the ambient probe has fewer than 27 coefficients")?
    .to_vec();
    let cubemaps = seq(&object, "m_BakedReflectionProbeCubemaps");
    let default_reflection = (cubemaps.len() == seq(&object, "m_BakedReflectionProbes").len() + 1)
        .then(|| cubemaps.first().and_then(ptr))
        .flatten();
    let lightmaps = seq(&object, "m_Lightmaps")
        .iter()
        .map(|m| ["m_Lightmap", "m_DirLightmap", "m_ShadowMask"].map(|k| m.get(k).and_then(ptr)))
        .collect();
    let id = |v: &Value| SceneObjectId {
        object: scalar(v, "targetObject").unwrap_or(0),
        prefab: scalar(v, "targetPrefab").unwrap_or(0),
    };
    let xyzw = |v: Option<&Value>| {
        v.and_then(|v| vector(v, ["x", "y", "z", "w"]))
            .unwrap_or([1.0, 1.0, 0.0, 0.0])
    };
    let renderers = seq(&object, "m_LightmappedRendererData")
        .iter()
        .zip(seq(&object, "m_LightmappedRendererDataIDs"))
        .map(|(d, i)| RendererLighting {
            id: id(i),
            lightmap_index: scalar(d, "lightmapIndex").unwrap_or(0xFFFF),
            lightmap_st: xyzw(d.get("lightmapST")),
            lightmap_index_dynamic: scalar(d, "lightmapIndexDynamic").unwrap_or(0xFFFF),
            lightmap_st_dynamic: xyzw(d.get("lightmapSTDynamic")),
        })
        .collect();
    let lights = seq(&object, "m_LightBakingOutputs")
        .iter()
        .zip(seq(&object, "m_Lights"))
        .map(|(o, i)| {
            let mode = o.get("lightmapBakeMode");
            LightOutput {
                id: id(i),
                probe_occlusion_light_index: scalar(o, "probeOcclusionLightIndex").unwrap_or(-1),
                occlusion_mask_channel: scalar(o, "occlusionMaskChannel").unwrap_or(-1),
                lightmap_bake_type: mode
                    .and_then(|m| scalar(m, "lightmapBakeType"))
                    .unwrap_or(4),
                mixed_lighting_mode: mode
                    .and_then(|m| scalar(m, "mixedLightingMode"))
                    .unwrap_or(0),
                is_baked: o.get("isBaked").and_then(Value::as_str) == Some("true"),
            }
        })
        .collect();
    let probes = match object.get("m_LightProbes").and_then(ptr) {
        Some(ObjectPtr {
            guid: None,
            path_id,
        }) => Some(read_light_probes(&file.read(path_id)?)?),
        Some(_) => anyhow::bail!("the lighting data's light probes are in another file"),
        None => None,
    };
    Ok(LightingData {
        name,
        lightmaps_mode,
        ambient_probe,
        default_reflection,
        lightmaps,
        renderers,
        lights,
        probes,
    })
}

/// A LightProbes object's probes and tetrahedralization.
fn read_light_probes(object: &Value) -> Result<LightProbeData> {
    let data = object.get("m_Data").context("light probes without data")?;
    let xyz = |v: &Value| vector(v, ["x", "y", "z"]).context("a malformed vector");
    let positions = seq(data, "m_Positions")
        .iter()
        .map(xyz)
        .collect::<Result<Vec<_>>>()?;
    let coefficients = seq(object, "m_BakedCoefficients")
        .iter()
        .map(|v| sh27(v).context("a probe with fewer than 27 coefficients"))
        .collect::<Result<Vec<_>>>()?;
    if coefficients.len() != positions.len() {
        anyhow::bail!(
            "{} light probe positions but {} baked probes",
            positions.len(),
            coefficients.len()
        );
    }
    let tetrahedralization = data
        .get("m_Tetrahedralization")
        .context("light probes without cells")?;
    let tetrahedra = seq(tetrahedralization, "m_Tetrahedra")
        .iter()
        .map(|t| {
            let four = |name: &str| -> Result<[i32; 4]> {
                let mut out = [0; 4];
                for (k, o) in out.iter_mut().enumerate() {
                    *o = scalar(t, &format!("{name}[{k}]")).context("a malformed cell")?;
                }
                Ok(out)
            };
            let m = t.get("matrix").context("a cell without a matrix")?;
            let mut matrix = [0.0; 12];
            for (k, o) in matrix.iter_mut().enumerate() {
                *o = scalar(m, &format!("e{}{}", k / 4, k % 4))
                    .context("a malformed cell matrix")?;
            }
            Ok(Tetrahedron {
                indices: four("indices")?,
                neighbors: four("neighbors")?,
                matrix,
            })
        })
        .collect::<Result<Vec<_>>>()?;
    let hull_rays = seq(tetrahedralization, "m_HullRays")
        .iter()
        .map(xyz)
        .collect::<Result<Vec<_>>>()?;
    Ok(LightProbeData {
        positions,
        coefficients,
        tetrahedra,
        hull_rays,
    })
}

/// The LightingDataAsset `path_id` of a serialized file (`unity_builtin_extra`).
pub struct BuiltinLightingImporter {
    pub path_id: i64,
}

impl Importer for BuiltinLightingImporter {
    type Output = LightingData;
    const KIND: &'static str = "builtin-lighting";
    const VERSION: u32 = 3;

    fn import(&self, _source: &Path, bytes: &[u8], _meta: &Value) -> Result<LightingData> {
        let file = crate::serialized::SerializedFile::parse(bytes)?;
        read_lighting_data(&file, self.path_id)
    }

    fn options_key(&self) -> String {
        self.path_id.to_string()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The built-in LightingData of the installed 6000.0.71f1 (skipped without it): the probe the
    /// Editor reports for a scene with the default skybox (tests/projects/urp CapsProbe).
    #[test]
    fn the_builtin_lighting_data_holds_the_default_ambient_probe() {
        let Some(data) = unbound_project::install::installed_data("6000.0.71f1") else {
            eprintln!("no 6000.0.71f1 install: skipped");
            return;
        };
        let file = data.join("Resources/unity_builtin_extra");
        let Ok(bytes) = std::fs::read(&file) else {
            eprintln!("no 6000.0.71f1 install: skipped");
            return;
        };
        let data = BuiltinLightingImporter { path_id: 20201 }
            .import(&file, &bytes, &Value::Null)
            .unwrap();
        assert_eq!(data.name, "LightingData");
        assert_eq!(data.ambient_probe.len(), 27);
        assert!((data.ambient_probe[0] - 0.180283785).abs() < 1e-7);
        assert!((data.ambient_probe[18] - 0.306922853).abs() < 1e-7);
        assert_eq!(
            data.default_reflection,
            Some(ObjectPtr {
                guid: None,
                path_id: 20200
            })
        );
        let cube = crate::serialized_texture::SerializedTextureImporter { path_id: 20200 }
            .import(&file, &bytes, &Value::Null)
            .unwrap();
        assert_eq!(
            (cube.width, cube.image_count, cube.mip_count, cube.format),
            (128, 6, 8, 4)
        );
        assert_eq!(cube.data.len(), 87380 * 6);
    }

    /// tests/projects/baked's bake (6000.0.71f1): three lightmapped renderers on one lightmap, two
    /// baked lights, an 18-probe grid (the Editor's own readings are in its Reference/).
    #[test]
    fn a_baked_scenes_lighting_data() {
        let file = Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/baked/Assets/Scenes/Baked/LightingData.asset");
        let bytes = std::fs::read(&file).unwrap();
        let data = BuiltinLightingImporter { path_id: 112000000 }
            .import(&file, &bytes, &Value::Null)
            .unwrap();
        let lightmap = ObjectPtr {
            guid: Some("96a7994b4d6074e308ed29be6a522349".into()),
            path_id: 2800000,
        };
        assert_eq!(data.lightmaps, vec![[Some(lightmap), None, None]]);
        assert_eq!(data.renderers.len(), 3);
        let plane = &data.renderers[2];
        assert_eq!((plane.id.object, plane.lightmap_index), (1156501550, 0));
        assert_eq!(plane.lightmap_st, [0.625, 0.625, 0.0, 0.0]);
        assert_eq!(data.lights.len(), 2);
        assert!(
            data.lights
                .iter()
                .all(|l| l.is_baked && l.lightmap_bake_type == 2)
        );
        let probes = data.probes.unwrap();
        assert_eq!(
            (probes.positions.len(), probes.coefficients.len()),
            (18, 18)
        );
        assert_eq!(probes.tetrahedra.len(), 56);
        assert_eq!(probes.tetrahedra[0].indices, [7, 11, 17, 10]);
        assert!((probes.coefficients[0][0] - 0.86758745).abs() < 1e-7);
        assert_eq!(
            data.default_reflection,
            Some(ObjectPtr {
                guid: Some("1cbc0731a65fa6ec6ae504d72ab33e05".into()),
                path_id: 8900000
            })
        );
    }
}
