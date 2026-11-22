//! `baked-gi-atlas`: the layout of a bake's baked lightmaps (unbound_gi::baked::atlas) for the
//! renderers the managed side sends.
use anyhow::{Context, Result, ensure};
use serde_json::{Value as Json, json};
use unbound_gi::baked::atlas;

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AtlasRequest {
    max_size: i32,
    padding: i32,
    resolution: f32,
    fixed_size: bool,
    renderers: Vec<AtlasRenderer>,
}

/// A renderer of the bake: its mesh (local positions, triangles of every submesh, the UVs
/// its lightmap takes), where it is and its lightmapping settings.
#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct AtlasRenderer {
    /// Its name, for messages.
    name: String,
    positions: Vec<f32>,
    triangles: Vec<u32>,
    uvs: Vec<f32>,
    /// Column-major.
    local_to_world: [f32; 16],
    scale_in_lightmap: f32,
    /// The most detailed LOD level it is in, if it is in a LOD Group.
    lod_level: Option<u32>,
    /// Its scene's GUID as text.
    scene: String,
    /// It contributes to global illumination but is lit by light probes.
    #[serde(default)]
    probe_lit: bool,
    parameters: AtlasParameters,
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct AtlasParameters {
    back_face_tolerance: f32,
    anti_aliasing_samples: i32,
    pushoff: f32,
    baked_lightmap_tag: i32,
    max_lightmap_count: i32,
}

/// A GUID's text as Unity keeps it: four 32-bit words of eight hex digits each, the lowest
/// digit first.
fn guid_words(text: &str) -> Result<[u32; 4]> {
    ensure!(
        text.len() == 32,
        "a scene GUID of {} characters",
        text.len()
    );
    let mut words = [0u32; 4];
    for (k, c) in text.chars().enumerate() {
        let digit = c
            .to_digit(16)
            .with_context(|| format!("a scene GUID '{text}'"))?;
        words[k / 8] |= digit << (4 * (k % 8));
    }
    Ok(words)
}

/// The layout: each lightmap's size and each renderer's lightmap and scale and offset, in the
/// order sent.
pub fn atlas_request(request: AtlasRequest) -> Result<Json> {
    let settings = atlas::Settings {
        max_size: request.max_size,
        padding: request.padding,
        resolution: request.resolution,
        fixed_size: request.fixed_size,
    };
    let instances = request
        .renderers
        .iter()
        .map(|r| -> Result<atlas::Instance> {
            ensure!(
                r.positions.len() % 3 == 0 && r.triangles.len() % 3 == 0 && r.uvs.len() % 2 == 0,
                "a renderer's mesh of {} position, {} index and {} UV values",
                r.positions.len(),
                r.triangles.len(),
                r.uvs.len()
            );
            let positions: Vec<[f32; 3]> = r
                .positions
                .chunks_exact(3)
                .map(|c| [c[0], c[1], c[2]])
                .collect();
            let triangles: Vec<[u32; 3]> = r
                .triangles
                .chunks_exact(3)
                .map(|c| [c[0], c[1], c[2]])
                .collect();
            let uvs: Vec<[f32; 2]> = r.uvs.chunks_exact(2).map(|c| [c[0], c[1]]).collect();
            let area = atlas::surface_area(&positions, &triangles, &r.local_to_world);
            let uv_bounds = atlas::uv_bounds(&uvs);
            ensure!(
                uv_bounds[2] > 0.0 && uv_bounds[3] > 0.0,
                "'{}': its lightmap UVs span no area (a mesh without UVs, or all of one value), which the layout does not support",
                r.name
            );
            let m = &r.local_to_world;
            Ok(atlas::Instance {
                probe_lit: r.probe_lit,
                scene: guid_words(&r.scene)?,
                parameters: atlas::Parameters {
                    back_face_tolerance: r.parameters.back_face_tolerance,
                    anti_aliasing_samples: r.parameters.anti_aliasing_samples,
                    pushoff: r.parameters.pushoff,
                    baked_lightmap_tag: r.parameters.baked_lightmap_tag,
                    max_lightmap_count: r.parameters.max_lightmap_count,
                },
                rect: atlas::rectangle(
                    r.scale_in_lightmap,
                    settings.resolution,
                    r.lod_level,
                    area,
                    uv_bounds,
                    settings.max_size,
                ),
                uv_bounds,
                morton: atlas::morton([m[12], m[13], m[14]]),
            })
        })
        .collect::<Result<Vec<_>>>()?;
    let layout = atlas::layout(&settings, &instances)?;
    Ok(json!({
        "lightmaps": layout.lightmaps,
        "placements": layout
            .placements
            .iter()
            .zip(&request.renderers)
            .map(|(p, r)| {
                // Packed like any other, without a lightmap of its own.
                let lightmap = if r.scale_in_lightmap > 0.0 { p.lightmap } else { atlas::NO_LIGHTMAP };
                json!({ "lightmap": lightmap, "scaleOffset": p.scale_offset })
            })
            .collect::<Vec<_>>(),
    }))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn request(renderers: Vec<Json>) -> AtlasRequest {
        serde_json::from_value(json!({
            "maxSize": 64, "padding": 2, "resolution": 10.0, "fixedSize": true, "renderers": renderers,
        }))
        .unwrap()
    }

    fn quad(name: &str, scale: f32, uvs: Vec<f32>, x: f32) -> Json {
        json!({
            "name": name,
            "positions": [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0],
            "triangles": [0, 2, 1, 0, 3, 2],
            "uvs": uvs,
            "localToWorld": [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, 0, 0, 1],
            "scaleInLightmap": scale,
            "lodLevel": null,
            "scene": "10000000200000003000000040000000",
            "parameters": {
                "backFaceTolerance": 0.9, "antiAliasingSamples": 2, "pushoff": 0.0001,
                "bakedLightmapTag": -1, "maxLightmapCount": 0,
            },
        })
    }

    #[test]
    fn scale_in_lightmap_0_is_packed_without_a_lightmap() {
        let unit = vec![0.0, 0.0, 1.0, 0.0, 1.0, 1.0, 0.0, 1.0];
        // The scale-0 quad sorts first (its Morton code) and takes a one-texel slot at the
        // origin, which moves the other above it (a 3-texel strip with the padding).
        let out = atlas_request(request(vec![
            quad("lit", 1.0, unit.clone(), 0.2),
            quad("zero", 0.0, unit, 0.0),
        ]))
        .unwrap();
        let placements = out["placements"].as_array().unwrap();
        assert_eq!(placements[1]["lightmap"], json!(atlas::NO_LIGHTMAP));
        assert_eq!(placements[0]["lightmap"], json!(0));
        assert_eq!(placements[0]["scaleOffset"][3], json!(3.0 / 64.0));
    }

    #[test]
    fn lightmap_uvs_without_area_are_refused_by_name() {
        let error = atlas_request(request(vec![quad("bare", 1.0, vec![], 0.0)]))
            .err()
            .unwrap();
        assert!(error.to_string().contains("'bare'"), "{error}");
        let flat = vec![0.5; 8];
        assert!(atlas_request(request(vec![quad("flat", 1.0, flat, 0.0)])).is_err());
        // Not a number (which JSON cannot carry: set after reading).
        let unit = vec![0.0, 0.0, 1.0, 0.0, 1.0, 1.0, 0.0, 1.0];
        let mut nan = request(vec![quad("nan", 1.0, unit, 0.0)]);
        nan.renderers[0].uvs[6] = f32::NAN;
        let error = atlas_request(nan).err().unwrap();
        assert!(error.to_string().contains("'nan'"), "{error}");
    }

    #[test]
    fn guid_text_is_low_digit_first() {
        assert_eq!(
            guid_words("10000000200000003000000040000000").unwrap(),
            [1, 2, 3, 4]
        );
        assert_eq!(
            guid_words("f0000000000000000000000000000001").unwrap()[3],
            0x1000_0000
        );
        assert!(guid_words("xyz").is_err());
    }
}
