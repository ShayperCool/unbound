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
            let m = &r.local_to_world;
            Ok(atlas::Instance {
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
            .map(|p| json!({ "lightmap": p.lightmap, "scaleOffset": p.scale_offset }))
            .collect::<Vec<_>>(),
    }))
}

#[cfg(test)]
mod tests {
    use super::*;

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
