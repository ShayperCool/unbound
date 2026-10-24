//! Lighting data assets from Unity's built-in resources: a scene without baked lighting points at
//! the built-in `LightingData` (`unity_builtin_extra`), which holds the default skybox's ambient
//! probe.
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
    /// has one more than there are probes, as (file id, path id) in this file's references.
    pub default_reflection: Option<(i32, i64)>,
}

/// A LightingDataAsset's name, lightmaps mode and ambient probe, from its fields.
pub fn read_lighting_data(object: &Value) -> Result<LightingData> {
    let name = object.get("m_Name").and_then(Value::as_str).unwrap_or("").to_string();
    let lightmaps_mode = object
        .get("m_LightmapsMode")
        .and_then(Value::as_str)
        .and_then(|s| s.parse().ok())
        .unwrap_or(0);
    let probe = object
        .get("m_BakedAmbientProbeInLinear")
        .context("lighting data without an ambient probe")?;
    let mut ambient_probe = Vec::with_capacity(27);
    for k in 0..27 {
        let v = probe
            .get(&format!("sh[{k:2}]"))
            .and_then(Value::as_str)
            .and_then(|s| s.parse::<f32>().ok())
            .with_context(|| format!("the ambient probe has no sh[{k:2}]"))?;
        ambient_probe.push(v);
    }
    let pptr = |v: &Value| -> Option<(i32, i64)> {
        Some((
            v.get("m_FileID")?.as_str()?.parse().ok()?,
            v.get("m_PathID")?.as_str()?.parse().ok()?,
        ))
    };
    let cubemaps = object
        .get("m_BakedReflectionProbeCubemaps")
        .and_then(Value::as_seq)
        .unwrap_or_default();
    let probes = object
        .get("m_BakedReflectionProbes")
        .and_then(Value::as_seq)
        .map_or(0, <[Value]>::len);
    let default_reflection = (cubemaps.len() == probes + 1)
        .then(|| cubemaps.first().and_then(pptr))
        .flatten()
        .filter(|(_, path)| *path != 0);
    Ok(LightingData {
        name,
        lightmaps_mode,
        ambient_probe,
        default_reflection,
    })
}

/// The LightingDataAsset `path_id` of a serialized file (`unity_builtin_extra`).
pub struct BuiltinLightingImporter {
    pub path_id: i64,
}

impl Importer for BuiltinLightingImporter {
    type Output = LightingData;
    const KIND: &'static str = "builtin-lighting";
    const VERSION: u32 = 2;

    fn import(&self, _source: &Path, bytes: &[u8], _meta: &Value) -> Result<LightingData> {
        let file = crate::serialized::SerializedFile::parse(bytes)?;
        read_lighting_data(&file.read(self.path_id)?)
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
        let Some(home) = std::env::var_os("HOME") else { return };
        let file = Path::new(&home).join("Unity/Hub/Editor/6000.0.71f1/Editor/Data/Resources/unity_builtin_extra");
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
        assert_eq!(data.default_reflection, Some((0, 20200)));
        let cube = crate::serialized_texture::SerializedTextureImporter { path_id: 20200 }
            .import(&file, &bytes, &Value::Null)
            .unwrap();
        assert_eq!((cube.width, cube.image_count, cube.mip_count, cube.format), (128, 6, 8, 4));
        assert_eq!(cube.data.len(), 87380 * 6);
    }
}
