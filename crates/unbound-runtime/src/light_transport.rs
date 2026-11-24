//! CPU probe light transport (`UnityEngine.LightTransport`): builds a ray-traced scene world
//! from the managed side's extracted meshes, materials, lights, and sky, then integrates probe
//! direct radiance, indirect radiance, backface validity, occlusion, and SH L2 deringing using
//! `unbound_gi::baked`.
use crate::baked_gi::{EnvironmentData, LightData, MaterialData, MeshData, SourceMesh, values};
use anyhow::{Context, Result, ensure};
use base64::Engine;
use rayon::prelude::*;
use serde_json::{Value as Json, json};
use std::collections::HashMap;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, LazyLock, Mutex};
use unbound_gi::baked::direct;
use unbound_gi::baked::gbuffer;
use unbound_gi::baked::grid::{Bounds, Grid};
use unbound_gi::baked::indirect::{self, Material, Surface, World};
use unbound_gi::baked::light::{Light, Mode, Tables};
use unbound_gi::baked::light_probes;
use unbound_gi::baked::probes::{self, Coefficients};
use unbound_gi::baked::sampling::Samples;
use unbound_gi::baked::shadowmask;
use unbound_gi::baked::sky::Sky;
use unbound_gi::baked::trace::{Lod, Ray, Scene};

struct LightTransportWorld {
    scene: Scene,
    casters: Scene,
    surfaces: Vec<Surface>,
    materials: Vec<Material>,
    lights: Vec<Light>,
    bounds: Option<Bounds>,
    sky: Option<Sky>,
    samples: Samples,
}

static NEXT_WORLD_ID: AtomicU64 = AtomicU64::new(1);
static WORLDS: LazyLock<Mutex<HashMap<u64, Arc<LightTransportWorld>>>> =
    LazyLock::new(|| Mutex::new(HashMap::new()));

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PopulateRequest {
    meshes: Vec<MeshData>,
    renderers: Vec<RendererInput>,
    #[serde(default)]
    lights: Vec<LightData>,
    /// `MixedLightingMode`: 0 = IndirectOnly, 1 = Subtractive, 2 = Shadowmask.
    #[serde(default)]
    mixed_mode: u8,
    blue_noise: String,
    #[serde(default)]
    environment: Option<EnvironmentData>,
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AssignChannelsRequest {
    #[serde(default)]
    lights: Vec<LightData>,
    /// `MixedLightingMode`: 0 = IndirectOnly, 1 = Subtractive, 2 = Shadowmask.
    #[serde(default)]
    mixed_mode: u8,
}

/// Assigns shadowmask channels via [`shadowmask::assign`] for `Subtractive` (`1`) and
/// `Shadowmask` (`2`) modes; in `Shadowmask` mode overflow mixed shadow-casting lights fall back
/// to [`Mode::Baked`]. In `IndirectOnly` (`0`), no channels are assigned.
fn assign_light_channels(lights: &mut [Light], mixed_mode: u8) -> Result<Vec<Option<u8>>> {
    match mixed_mode {
        0 => Ok(vec![None; lights.len()]),
        1 => Ok(shadowmask::assign(lights, false)),
        2 => Ok(shadowmask::assign(lights, true)),
        other => anyhow::bail!("a Lighting Mode of {other}"),
    }
}

pub fn assign_channels(request: AssignChannelsRequest) -> Result<Json> {
    let mut lights: Vec<Light> = request
        .lights
        .iter()
        .map(LightData::light)
        .collect::<Result<_>>()?;
    let channels = assign_light_channels(&mut lights, request.mixed_mode)?;
    let channel_ids: Vec<i32> = channels
        .into_iter()
        .map(|c| c.map_or(-1, |ch| ch as i32))
        .collect();
    let modes: Vec<u8> = lights
        .iter()
        .map(|l| if l.mode == Mode::Mixed { 1 } else { 2 })
        .collect();
    Ok(json!({ "channels": channel_ids, "modes": modes }))
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct RendererInput {
    name: String,
    mesh: usize,
    local_to_world: [f32; 16],
    #[serde(default)]
    has_material: Option<Vec<bool>>,
    #[serde(default = "yes")]
    casts_shadows: bool,
    material: MaterialData,
    /// Scale and offset bringing mesh UVs to the renderer's albedo/emissive meta-pass image `[0, 1]`.
    #[serde(default = "identity_st")]
    image_scale_offset: [f32; 4],
}

fn yes() -> bool {
    true
}

fn identity_st() -> [f32; 4] {
    [1.0, 1.0, 0.0, 0.0]
}

impl RendererInput {
    fn stops_rays(&self, submesh: usize) -> bool {
        match &self.has_material {
            Some(has) => has.get(submesh).copied().unwrap_or(false),
            None => true,
        }
    }
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct IntegrateRequest {
    world_id: u64,
    kind: String,
    positions: String,
    sample_count: u32,
    #[serde(default = "default_bounces")]
    max_bounces: u32,
    #[serde(default = "default_pushoff")]
    pushoff: f32,
    #[serde(default)]
    ignore_environment: bool,
    #[serde(default)]
    max_lights_per_probe: usize,
    #[serde(default)]
    per_probe_light_indices: Option<String>,
}

fn default_bounces() -> u32 {
    2
}

fn default_pushoff() -> f32 {
    0.0001
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DeringRequest {
    sh: String,
}

fn encode_f32s(values: &[f32]) -> String {
    let mut bytes = Vec::with_capacity(values.len() * 4);
    for &v in values {
        bytes.extend_from_slice(&v.to_le_bytes());
    }
    base64::engine::general_purpose::STANDARD.encode(&bytes)
}

/// Lay out `probes::Coefficients` (`[[r,g,b]; 9]`) as Unity's `SphericalHarmonicsL2`
/// (`[r0..r8, g0..g8, b0..b8]`, 27 floats per probe) in raw radiance basis order.
fn encode_sh(coefficients: &[Coefficients]) -> String {
    let mut out = Vec::with_capacity(coefficients.len() * 27);
    for c in coefficients {
        for channel in 0..3 {
            for coeff in 0..9 {
                out.push(c[coeff][channel]);
            }
        }
    }
    encode_f32s(&out)
}

pub fn populate(request: PopulateRequest) -> Result<Json> {
    let blue_noise = std::fs::read(&request.blue_noise).with_context(|| {
        format!(
            "reading the blue noise data {} of the Unity installation",
            request.blue_noise
        )
    })?;
    let samples = Samples::new(&blue_noise).map_err(|size| {
        anyhow::anyhow!(
            "the blue noise data {} has {} bytes",
            request.blue_noise,
            size.0
        )
    })?;
    let meshes: Vec<SourceMesh> = request
        .meshes
        .iter()
        .map(SourceMesh::read)
        .collect::<Result<_>>()?;
    let mut lights: Vec<Light> = request
        .lights
        .iter()
        .map(LightData::light)
        .collect::<Result<_>>()?;
    assign_light_channels(&mut lights, request.mixed_mode)?;
    let sky = request
        .environment
        .as_ref()
        .map(EnvironmentData::sky)
        .transpose()?;

    let mut surfaces = Vec::new();
    let mut materials = Vec::new();
    let mut caster_tris = Vec::new();
    let mut world_points = Vec::new();

    for r in &request.renderers {
        ensure!(
            r.mesh < meshes.len(),
            "'{}': mesh index {} out of range ({})",
            r.name,
            r.mesh,
            meshes.len()
        );
        let mesh = &meshes[r.mesh];
        if !mesh.has_normals() || mesh.positions.is_empty() {
            continue;
        }
        let m = &r.local_to_world;
        let rows = direct::normal_matrix(m);
        let determinant = m[0] * (m[5] * m[10] - m[6] * m[9]) - m[4] * (m[1] * m[10] - m[2] * m[9])
            + m[8] * (m[1] * m[6] - m[2] * m[5]);
        let mirrored = determinant < 0.0;
        let uvs = mesh.lightmap_uvs();
        let st = r.image_scale_offset;
        let count = mesh.positions.len() as u32;
        let material_index = materials.len() as u32;

        for &p in &mesh.positions {
            world_points.push(direct::point(m, p));
        }
        for (submesh, triangles) in mesh.submeshes.iter().enumerate() {
            if !r.stops_rays(submesh) {
                continue;
            }
            let double_sided = r
                .material
                .double_sided
                .get(submesh)
                .copied()
                .unwrap_or(false);
            for t in triangles {
                if !t.iter().all(|&v| v < count) {
                    continue;
                }
                let corners = t.map(|v| direct::point(m, mesh.positions[v as usize]));
                let normals = t.map(|v| {
                    direct::world_normal(&rows, gbuffer::unit_normal(mesh.normals[v as usize]))
                });
                let tri_uvs = t.map(|v| {
                    let uv = uvs.get(v as usize).copied().unwrap_or([0.0, 0.0]);
                    if !uv[0].is_finite() || !uv[1].is_finite() {
                        [0.0, 0.0]
                    } else {
                        let mapped = [uv[0] * st[0] + st[2], uv[1] * st[1] + st[3]];
                        if mapped[0].is_finite() && mapped[1].is_finite() {
                            mapped
                        } else {
                            [0.0, 0.0]
                        }
                    }
                });
                surfaces.push(Surface {
                    material: material_index,
                    mirrored,
                    double_sided,
                    corners,
                    normals,
                    uvs: tri_uvs,
                });
                if r.casts_shadows {
                    caster_tris.push((corners, Lod::NONE));
                }
            }
        }
        materials.push(Material {
            albedo: r.material.albedo.albedo(&r.name)?,
            emissive: r.material.emissive.emissive(&r.name)?,
        });
    }

    let scene = Scene::with_lods(surfaces.iter().map(|s| (s.corners, Lod::NONE)));
    let casters = Scene::with_lods(caster_tris);
    let bounds = Bounds::around(world_points);

    let world = Arc::new(LightTransportWorld {
        scene,
        casters,
        surfaces,
        materials,
        lights,
        bounds,
        sky,
        samples,
    });
    let id = NEXT_WORLD_ID.fetch_add(1, Ordering::Relaxed);
    WORLDS.lock().unwrap().insert(id, world);
    Ok(json!({ "worldId": id }))
}

pub fn release(world_id: u64) {
    WORLDS.lock().unwrap().remove(&world_id);
}

fn world_bounds_with_probes(scene_bounds: Option<Bounds>, points: &[[f32; 3]]) -> Bounds {
    let mut points_iter = points.iter().copied();
    let probe_bounds = Bounds::around(&mut points_iter);
    match (scene_bounds, probe_bounds) {
        (Some(a), Some(b)) => Bounds {
            min: std::array::from_fn(|k| a.min[k].min(b.min[k])),
            max: std::array::from_fn(|k| a.max[k].max(b.max[k])),
        },
        (Some(a), None) => a,
        (None, Some(b)) => b,
        (None, None) => Bounds {
            min: [0.0; 3],
            max: [0.0; 3],
        },
    }
}

pub fn integrate(request: IntegrateRequest) -> Result<Json> {
    let world = WORLDS
        .lock()
        .unwrap()
        .get(&request.world_id)
        .cloned()
        .with_context(|| format!("unknown light transport world {}", request.world_id))?;
    ensure!(
        request.pushoff.is_finite() && request.pushoff >= 0.0,
        "pushoff must be finite and non-negative, got {}",
        request.pushoff
    );
    let points: Vec<[f32; 3]> = values(&request.positions, "probe positions", f32::from_le_bytes)?;
    let around = world_bounds_with_probes(world.bounds, &points);
    let grid = Grid::new(&world.lights, around);
    let data = match request.kind.as_str() {
        "direct" => {
            let direct_sh = probes::direct(&points, &world.lights, &grid, &world.casters);
            encode_sh(&direct_sh)
        }
        "indirect" => {
            let sky = (!request.ignore_environment)
                .then_some(world.sky.as_ref())
                .flatten();
            let settings = indirect::Settings {
                samples: request.sample_count,
                max_bounces: request.max_bounces,
                min_bounces: 1.min(request.max_bounces),
                pushoff: request.pushoff,
                environment_samples: if sky.is_some() {
                    request.sample_count
                } else {
                    0
                },
            };
            let w = World {
                scene: &world.scene,
                casters: &world.casters,
                surfaces: &world.surfaces,
                materials: &world.materials,
                lights: &world.lights,
                grid: &grid,
                sky,
            };
            let probed = indirect::probes(
                std::iter::once(points.as_slice()),
                &w,
                &world.samples,
                settings,
            );
            encode_sh(&probed.radiance)
        }
        "validity" => {
            let w = World {
                scene: &world.scene,
                casters: &world.casters,
                surfaces: &world.surfaces,
                materials: &world.materials,
                lights: &world.lights,
                grid: &grid,
                sky: None,
            };
            let validity =
                indirect::probe_validity(&points, &w, &world.samples, request.sample_count);
            encode_f32s(&validity)
        }
        "occlusion" => {
            let max_lights = request.max_lights_per_probe;
            ensure!(max_lights > 0, "maxLightsPerProbe must be > 0");
            let indices_b64 = request
                .per_probe_light_indices
                .as_deref()
                .context("occlusion integration requires perProbeLightIndices")?;
            let indices: Vec<[i32; 1]> =
                values(indices_b64, "per-probe light indices", i32::from_le_bytes)?;
            ensure!(
                indices.len() == points.len() * max_lights,
                "perProbeLightIndices length {} does not match {} probes * {} lights",
                indices.len(),
                points.len(),
                max_lights
            );
            let tables = &Tables::new();
            let indices = indices.as_slice();
            let world = world.as_ref();
            let out: Vec<f32> = points
                .par_iter()
                .enumerate()
                .flat_map_iter(|(pi, &p)| {
                    let base = pi * max_lights;
                    (0..max_lights).map(move |slot| {
                        let light_idx = indices[base + slot][0];
                        if light_idx < 0 || (light_idx as usize) >= world.lights.len() {
                            return 0.0f32;
                        }
                        let light = &world.lights[light_idx as usize];
                        let reaches =
                            light
                                .arriving_around(tables, light.color, p)
                                .is_some_and(|arriving| {
                                    !(light.casts_shadows
                                        && world.casters.occluded_inclusive(
                                            &Ray {
                                                origin: p,
                                                direction: arriving.direction,
                                            },
                                            arriving.distance,
                                        ))
                                });
                        if reaches { 1.0 } else { 0.0 }
                    })
                })
                .collect();
            encode_f32s(&out)
        }
        other => anyhow::bail!("unknown light transport integration kind '{other}'"),
    };
    Ok(json!({ "data": data }))
}

pub fn dering(request: DeringRequest) -> Result<Json> {
    let raw: Vec<[f32; 1]> = values(&request.sh, "SH coefficients", f32::from_le_bytes)?;
    ensure!(
        raw.len() % 27 == 0,
        "SH buffer has {} floats (not a multiple of 27)",
        raw.len()
    );
    let mut flat: Vec<f32> = raw.into_iter().map(|v| v[0]).collect();
    flat.par_chunks_exact_mut(27).for_each(|chunk| {
        let mut sh: [f32; 27] = chunk.try_into().unwrap();
        light_probes::dering(&mut sh);
        chunk.copy_from_slice(&sh);
    });
    Ok(json!({ "data": encode_f32s(&flat) }))
}

#[cfg(test)]
mod tests {
    use super::*;
    use unbound_gi::baked::sampling::BLUE_NOISE_BYTES;

    fn b64_f32(vals: &[f32]) -> String {
        encode_f32s(vals)
    }

    fn b64_u32(vals: &[u32]) -> String {
        let mut bytes = Vec::with_capacity(vals.len() * 4);
        for &v in vals {
            bytes.extend_from_slice(&v.to_le_bytes());
        }
        base64::engine::general_purpose::STANDARD.encode(&bytes)
    }

    fn b64_i32(vals: &[i32]) -> String {
        let mut bytes = Vec::with_capacity(vals.len() * 4);
        for &v in vals {
            bytes.extend_from_slice(&v.to_le_bytes());
        }
        base64::engine::general_purpose::STANDARD.encode(&bytes)
    }

    fn decode_f32(b64: &str) -> Vec<f32> {
        values::<f32, 1>(b64, "test floats", f32::from_le_bytes)
            .unwrap()
            .into_iter()
            .map(|v| v[0])
            .collect()
    }

    #[test]
    fn populate_integrate_dering_and_release_round_trip() {
        let tmp = std::env::temp_dir().join(format!("unbound-lt-test-{}.bin", std::process::id()));
        std::fs::write(&tmp, vec![0u8; BLUE_NOISE_BYTES]).unwrap();

        // A horizontal 20x20 quad at y = 0 facing +y (normal = (0, 1, 0)).
        let positions = b64_f32(&[
            -10.0, 0.0, -10.0, 10.0, 0.0, -10.0, 10.0, 0.0, 10.0, -10.0, 0.0, 10.0,
        ]);
        let normals = b64_f32(&[0.0, 1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 0.0]);
        let uvs = b64_f32(&[0.0, 0.0, 1.0, 0.0, 1.0, 1.0, 0.0, 1.0]);
        let tris = b64_u32(&[0, 2, 1, 0, 3, 2]);
        let albedo_px = base64::engine::general_purpose::STANDARD.encode([200u8, 200, 200, 255]);
        let emissive_px = b64_f32(&[0.0, 0.0, 0.0, 1.0]);
        let sky_faces = b64_f32(&[0.5f32; 6 * 3]);

        // Directional light shining straight down (-y): rotation +90 deg around X -> (sin(45), 0, 0, cos(45)).
        let s = std::f32::consts::FRAC_1_SQRT_2;
        let pop_req: PopulateRequest = serde_json::from_value(json!({
            "meshes": [{
                "positions": positions,
                "normals": normals,
                "uv": uvs,
                "uv2": uvs,
                "submeshes": [tris],
            }],
            "renderers": [{
                "name": "Floor",
                "mesh": 0,
                "localToWorld": [
                    1.0, 0.0, 0.0, 0.0,
                    0.0, 1.0, 0.0, 0.0,
                    0.0, 0.0, 1.0, 0.0,
                    0.0, 0.0, 0.0, 1.0
                ],
                "hasMaterial": [true],
                "castsShadows": true,
                "material": {
                    "albedo": { "width": 1, "height": 1, "data": albedo_px },
                    "emissive": { "width": 1, "height": 1, "data": emissive_px },
                    "doubleSided": [false]
                }
            }],
            "lights": [
                {
                    "name": "BakedSun",
                    "fileId": 1,
                    "kind": 0,
                    "mode": 2,
                    "color": [2.0, 1.0, 0.5],
                    "indirectColor": [2.0, 1.0, 0.5],
                    "position": [0.0, 10.0, 0.0],
                    "rotation": [s, 0.0, 0.0, s],
                    "range": 0.0,
                    "spotAngle": 0.0,
                    "castsShadows": true
                },
                {
                    "name": "MixedSun",
                    "fileId": 2,
                    "kind": 0,
                    "mode": 1,
                    "color": [1.0, 1.0, 1.0],
                    "indirectColor": [1.0, 1.0, 1.0],
                    "position": [0.0, 10.0, 0.0],
                    "rotation": [s, 0.0, 0.0, s],
                    "range": 0.0,
                    "spotAngle": 0.0,
                    "castsShadows": true
                }
            ],
            "blueNoise": tmp.to_string_lossy(),
            "environment": {
                "samples": 32,
                "importanceSampling": false,
                "side": 1,
                "faces": sky_faces
            }
        }))
        .unwrap();
        let pop_res = populate(pop_req).unwrap();
        let _ = std::fs::remove_file(&tmp);
        let world_id = pop_res["worldId"].as_u64().unwrap();

        // Two probes: (0, 1, 0) above the floor, (0, -1, 0) below the floor (shadowed & seeing
        // backfaces).
        let probe_pos = b64_f32(&[0.0, 1.0, 0.0, 0.0, -1.0, 0.0]);

        let direct_res = integrate(IntegrateRequest {
            world_id,
            kind: "direct".into(),
            positions: probe_pos.clone(),
            sample_count: 16,
            max_bounces: 2,
            pushoff: 0.0001,
            ignore_environment: false,
            max_lights_per_probe: 0,
            per_probe_light_indices: None,
        })
        .unwrap();
        let direct_vals = decode_f32(direct_res["data"].as_str().unwrap());
        assert_eq!(direct_vals.len(), 54);
        let k0_pi = 0.282_094_8 * std::f32::consts::PI;
        assert!(
            (direct_vals[0] - 2.0 * k0_pi).abs() < 1e-4,
            "above-floor probe R L0 matches BakedSun only (excludes MixedSun): {}",
            direct_vals[0]
        );
        assert!(
            (direct_vals[9] - 1.0 * k0_pi).abs() < 1e-4,
            "above-floor probe G L0 matches BakedSun only: {}",
            direct_vals[9]
        );
        assert!(
            (direct_vals[18] - 0.5 * k0_pi).abs() < 1e-4,
            "above-floor probe B L0 matches BakedSun only: {}",
            direct_vals[18]
        );
        assert_eq!(direct_vals[27], 0.0, "below-floor probe is shadowed");

        let indirect_sky = integrate(IntegrateRequest {
            world_id,
            kind: "indirect".into(),
            positions: probe_pos.clone(),
            sample_count: 64,
            max_bounces: 2,
            pushoff: 0.0001,
            ignore_environment: false,
            max_lights_per_probe: 0,
            per_probe_light_indices: None,
        })
        .unwrap();
        let indirect_nosky = integrate(IntegrateRequest {
            world_id,
            kind: "indirect".into(),
            positions: probe_pos.clone(),
            sample_count: 64,
            max_bounces: 2,
            pushoff: 0.0001,
            ignore_environment: true,
            max_lights_per_probe: 0,
            per_probe_light_indices: None,
        })
        .unwrap();
        let ind_sky = decode_f32(indirect_sky["data"].as_str().unwrap());
        let ind_nosky = decode_f32(indirect_nosky["data"].as_str().unwrap());
        assert!(
            ind_sky[0] > ind_nosky[0],
            "environment light increases indirect L0"
        );
        assert!(
            ind_nosky[0] > 0.0,
            "above-floor probe receives bounced light from floor"
        );

        let val_res = integrate(IntegrateRequest {
            world_id,
            kind: "validity".into(),
            positions: probe_pos.clone(),
            sample_count: 64,
            max_bounces: 2,
            pushoff: 0.0001,
            ignore_environment: false,
            max_lights_per_probe: 0,
            per_probe_light_indices: None,
        })
        .unwrap();
        let validity = decode_f32(val_res["data"].as_str().unwrap());
        assert_eq!(validity.len(), 2);
        assert_eq!(validity[0], 0.0, "above-floor probe sees no backfaces");
        assert!(
            validity[1] > 0.3,
            "below-floor probe hits floor backface: {}",
            validity[1]
        );

        let occ_res = integrate(IntegrateRequest {
            world_id,
            kind: "occlusion".into(),
            positions: probe_pos.clone(),
            sample_count: 16,
            max_bounces: 2,
            pushoff: 0.0001,
            ignore_environment: false,
            max_lights_per_probe: 4,
            per_probe_light_indices: Some(b64_i32(&[1, -1, -1, -1, 1, -1, -1, -1])),
        })
        .unwrap();
        let occ = decode_f32(occ_res["data"].as_str().unwrap());
        assert_eq!(occ, vec![1.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0]);

        // Deringing attenuates ringing L1 while keeping L0 unchanged.
        let mut sh = [0.0f32; 27];
        sh[0] = 0.28209478;
        sh[3] = 1.5;
        let dering_res = dering(DeringRequest { sh: b64_f32(&sh) }).unwrap();
        let deringed = decode_f32(dering_res["data"].as_str().unwrap());
        assert_eq!(deringed[0], sh[0]);
        assert!(deringed[3].abs() < sh[3].abs());

        release(world_id);
        assert!(
            integrate(IntegrateRequest {
                world_id,
                kind: "direct".into(),
                positions: probe_pos,
                sample_count: 1,
                max_bounces: 1,
                pushoff: 0.0001,
                ignore_environment: true,
                max_lights_per_probe: 0,
                per_probe_light_indices: None,
            })
            .is_err()
        );
    }

    #[test]
    fn assign_channels_reuses_shadowmask_rules_and_falls_back_overflow_lights() {
        let s = std::f32::consts::FRAC_1_SQRT_2;
        let point = |id: i64, x: f32| {
            json!({
                "name": format!("P{id}"), "fileId": id, "kind": 1, "mode": 1,
                "color": [1.0, 1.0, 1.0], "indirectColor": [1.0, 1.0, 1.0],
                "position": [x, 1.0, 0.0], "rotation": [0.0, 0.0, 0.0, 1.0],
                "range": 2.0, "spotAngle": 0.0, "castsShadows": true,
            })
        };
        let directional = |id: i64| {
            json!({
                "name": format!("D{id}"), "fileId": id, "kind": 0, "mode": 1,
                "color": [1.0, 1.0, 1.0], "indirectColor": [1.0, 1.0, 1.0],
                "position": [0.0, 10.0, 0.0], "rotation": [s, 0.0, 0.0, s],
                "range": 0.0, "spotAngle": 0.0, "castsShadows": true,
            })
        };

        // 1. Subtractive (mixedMode = 1) assigns channel 0 and keeps Mixed mode.
        let sub_req: AssignChannelsRequest = serde_json::from_value(json!({
            "mixedMode": 1,
            "lights": [point(1, 0.0)],
        }))
        .unwrap();
        let sub_res = assign_channels(sub_req).unwrap();
        assert_eq!(sub_res["channels"], json!([0]));
        assert_eq!(sub_res["modes"], json!([1]));

        // 2. IndirectOnly (mixedMode = 0) assigns no channels.
        let ind_req: AssignChannelsRequest = serde_json::from_value(json!({
            "mixedMode": 0,
            "lights": [point(1, 0.0)],
        }))
        .unwrap();
        let ind_res = assign_channels(ind_req).unwrap();
        assert_eq!(ind_res["channels"], json!([-1]));
        assert_eq!(ind_res["modes"], json!([1]));

        // 3. Directional takes channel 0 even when listed after a point light.
        let order_req: AssignChannelsRequest = serde_json::from_value(json!({
            "mixedMode": 2,
            "lights": [point(1, 0.0), directional(2)],
        }))
        .unwrap();
        let order_res = assign_channels(order_req).unwrap();
        assert_eq!(order_res["channels"], json!([1, 0]));

        // 4. Five non-overlapping point lights reuse channel 0 for all five.
        let non_overlap: AssignChannelsRequest = serde_json::from_value(json!({
            "mixedMode": 2,
            "lights": [point(1, 0.0), point(2, 10.0), point(3, 20.0), point(4, 30.0), point(5, 40.0)],
        }))
        .unwrap();
        let non_overlap_res = assign_channels(non_overlap).unwrap();
        assert_eq!(non_overlap_res["channels"], json!([0, 0, 0, 0, 0]));
        assert_eq!(non_overlap_res["modes"], json!([1, 1, 1, 1, 1]));

        // 5. Five overlapping point lights at the origin: 5th gets -1 and falls back to Baked (2),
        //    so populate + direct integration bakes its direct light into probes.
        let tmp =
            std::env::temp_dir().join(format!("unbound-lt-shadowmask-{}.bin", std::process::id()));
        std::fs::write(&tmp, vec![0u8; BLUE_NOISE_BYTES]).unwrap();
        let overlap_lights = vec![
            point(1, 0.0),
            point(2, 0.0),
            point(3, 0.0),
            point(4, 0.0),
            point(5, 0.0),
        ];
        let overlap_req: AssignChannelsRequest = serde_json::from_value(json!({
            "mixedMode": 2,
            "lights": overlap_lights,
        }))
        .unwrap();
        let overlap_res = assign_channels(overlap_req).unwrap();
        assert_eq!(overlap_res["channels"], json!([0, 1, 2, 3, -1]));
        assert_eq!(overlap_res["modes"], json!([1, 1, 1, 1, 2]));

        let pop_req: PopulateRequest = serde_json::from_value(json!({
            "meshes": [],
            "renderers": [],
            "mixedMode": 2,
            "lights": [point(1, 0.0), point(2, 0.0), point(3, 0.0), point(4, 0.0), point(5, 0.0)],
            "blueNoise": tmp.to_string_lossy(),
        }))
        .unwrap();
        let world_id = populate(pop_req).unwrap()["worldId"].as_u64().unwrap();
        let _ = std::fs::remove_file(&tmp);
        let direct_res = integrate(IntegrateRequest {
            world_id,
            kind: "direct".into(),
            positions: b64_f32(&[0.0, 1.5, 0.0]),
            sample_count: 16,
            max_bounces: 1,
            pushoff: 0.0001,
            ignore_environment: true,
            max_lights_per_probe: 0,
            per_probe_light_indices: None,
        })
        .unwrap();
        release(world_id);
        let direct_vals = decode_f32(direct_res["data"].as_str().unwrap());
        assert!(
            direct_vals[0] > 0.0,
            "overflow mixed light falling back to Baked contributes to direct probe radiance"
        );
    }

    #[test]
    fn zero_length_vertex_normals_default_to_up_like_gbuffer() {
        let tmp =
            std::env::temp_dir().join(format!("unbound-lt-zeronorm-{}.bin", std::process::id()));
        std::fs::write(&tmp, vec![0u8; BLUE_NOISE_BYTES]).unwrap();
        let positions = b64_f32(&[
            -10.0, 0.0, -10.0, 10.0, 0.0, -10.0, 10.0, 0.0, 10.0, -10.0, 0.0, 10.0,
        ]);
        // Degenerate zero-length vertex normals must normalize to +Y like gbuffer::unit_normal.
        let normals = b64_f32(&[0.0; 12]);
        let uvs = b64_f32(&[0.0, 0.0, 1.0, 0.0, 1.0, 1.0, 0.0, 1.0]);
        let tris = b64_u32(&[0, 2, 1, 0, 3, 2]);
        let albedo_px = base64::engine::general_purpose::STANDARD.encode([220u8, 220, 220, 255]);
        let emissive_px = b64_f32(&[0.0, 0.0, 0.0, 1.0]);
        let s = std::f32::consts::FRAC_1_SQRT_2;

        let pop_req: PopulateRequest = serde_json::from_value(json!({
            "meshes": [{
                "positions": positions,
                "normals": normals,
                "uv": uvs,
                "uv2": uvs,
                "submeshes": [tris],
            }],
            "renderers": [{
                "name": "ZeroNormFloor",
                "mesh": 0,
                "localToWorld": [
                    1.0, 0.0, 0.0, 0.0,
                    0.0, 1.0, 0.0, 0.0,
                    0.0, 0.0, 1.0, 0.0,
                    0.0, 0.0, 0.0, 1.0
                ],
                "hasMaterial": [true],
                "castsShadows": true,
                "material": {
                    "albedo": { "width": 1, "height": 1, "data": albedo_px },
                    "emissive": { "width": 1, "height": 1, "data": emissive_px },
                    "doubleSided": [false]
                }
            }],
            "lights": [{
                "name": "Sun", "fileId": 1, "kind": 0, "mode": 2,
                "color": [2.0, 2.0, 2.0], "indirectColor": [2.0, 2.0, 2.0],
                "position": [0.0, 10.0, 0.0], "rotation": [s, 0.0, 0.0, s],
                "range": 0.0, "spotAngle": 0.0, "castsShadows": true
            }],
            "blueNoise": tmp.to_string_lossy(),
        }))
        .unwrap();
        let world_id = populate(pop_req).unwrap()["worldId"].as_u64().unwrap();
        let _ = std::fs::remove_file(&tmp);
        let res = integrate(IntegrateRequest {
            world_id,
            kind: "indirect".into(),
            positions: b64_f32(&[0.0, 1.0, 0.0]),
            sample_count: 64,
            max_bounces: 2,
            pushoff: 0.0001,
            ignore_environment: true,
            max_lights_per_probe: 0,
            per_probe_light_indices: None,
        })
        .unwrap();
        release(world_id);
        let vals = decode_f32(res["data"].as_str().unwrap());
        assert!(
            vals[0] > 0.0,
            "zero-length normal falls back to +Y and bounces sunlight: {}",
            vals[0]
        );
    }

    #[test]
    fn non_finite_vertex_uvs_do_not_poison_indirect_sh_with_nan() {
        let tmp = std::env::temp_dir().join(format!("unbound-lt-nanuv-{}.bin", std::process::id()));
        std::fs::write(&tmp, vec![0u8; BLUE_NOISE_BYTES]).unwrap();
        let positions = b64_f32(&[
            -10.0, 0.0, -10.0, 10.0, 0.0, -10.0, 10.0, 0.0, 10.0, -10.0, 0.0, 10.0,
        ]);
        let normals = b64_f32(&[0.0, 1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 0.0]);
        let uvs = b64_f32(&[f32::NAN, 0.0, f32::INFINITY, 0.0, 1.0, 1.0, 0.0, 1.0]);
        let tris = b64_u32(&[0, 2, 1, 0, 3, 2]);
        let albedo_px = base64::engine::general_purpose::STANDARD.encode([200u8, 200, 200, 255]);
        let emissive_px = b64_f32(&[0.0, 0.0, 0.0, 1.0]);
        let s = std::f32::consts::FRAC_1_SQRT_2;

        let pop_req: PopulateRequest = serde_json::from_value(json!({
            "meshes": [{
                "positions": positions,
                "normals": normals,
                "uv": uvs,
                "uv2": uvs,
                "submeshes": [tris],
            }],
            "renderers": [{
                "name": "NanUvFloor",
                "mesh": 0,
                "localToWorld": [
                    1.0, 0.0, 0.0, 0.0,
                    0.0, 1.0, 0.0, 0.0,
                    0.0, 0.0, 1.0, 0.0,
                    0.0, 0.0, 0.0, 1.0
                ],
                "hasMaterial": [true],
                "castsShadows": true,
                "material": {
                    "albedo": { "width": 1, "height": 1, "data": albedo_px },
                    "emissive": { "width": 1, "height": 1, "data": emissive_px },
                    "doubleSided": [false]
                }
            }],
            "lights": [{
                "name": "Sun", "fileId": 1, "kind": 0, "mode": 2,
                "color": [2.0, 2.0, 2.0], "indirectColor": [2.0, 2.0, 2.0],
                "position": [0.0, 10.0, 0.0], "rotation": [s, 0.0, 0.0, s],
                "range": 0.0, "spotAngle": 0.0, "castsShadows": true
            }],
            "blueNoise": tmp.to_string_lossy(),
        }))
        .unwrap();
        let world_id = populate(pop_req).unwrap()["worldId"].as_u64().unwrap();
        let _ = std::fs::remove_file(&tmp);
        let res = integrate(IntegrateRequest {
            world_id,
            kind: "indirect".into(),
            positions: b64_f32(&[0.0, 1.0, 0.0]),
            sample_count: 64,
            max_bounces: 2,
            pushoff: 0.0001,
            ignore_environment: true,
            max_lights_per_probe: 0,
            per_probe_light_indices: None,
        })
        .unwrap();
        release(world_id);
        let vals = decode_f32(res["data"].as_str().unwrap());
        assert_eq!(vals.len(), 27);
        assert!(
            vals.iter().all(|v| v.is_finite()),
            "non-finite UVs must not produce NaN/Inf SH values: {vals:?}"
        );
        assert!(vals[0] > 0.0, "floor still bounces light: {}", vals[0]);
    }

    #[test]
    fn coplanar_probe_hits_floor_at_t_zero_for_direct_validity_and_occlusion() {
        let tmp =
            std::env::temp_dir().join(format!("unbound-lt-coplanar-{}.bin", std::process::id()));
        std::fs::write(&tmp, vec![0u8; BLUE_NOISE_BYTES]).unwrap();
        let positions = b64_f32(&[
            -10.0, 0.0, -10.0, 10.0, 0.0, -10.0, 10.0, 0.0, 10.0, -10.0, 0.0, 10.0,
        ]);
        let normals = b64_f32(&[0.0, 1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 0.0]);
        let uvs = b64_f32(&[0.0, 0.0, 1.0, 0.0, 1.0, 1.0, 0.0, 1.0]);
        let tris = b64_u32(&[0, 2, 1, 0, 3, 2]);
        let albedo_px = base64::engine::general_purpose::STANDARD.encode([200u8, 200, 200, 255]);
        let emissive_px = b64_f32(&[0.0, 0.0, 0.0, 1.0]);
        let s = std::f32::consts::FRAC_1_SQRT_2;

        let pop_req: PopulateRequest = serde_json::from_value(json!({
            "meshes": [{
                "positions": positions,
                "normals": normals,
                "uv": uvs,
                "uv2": uvs,
                "submeshes": [tris],
            }],
            "renderers": [{
                "name": "Floor",
                "mesh": 0,
                "localToWorld": [
                    1.0, 0.0, 0.0, 0.0,
                    0.0, 1.0, 0.0, 0.0,
                    0.0, 0.0, 1.0, 0.0,
                    0.0, 0.0, 0.0, 1.0
                ],
                "hasMaterial": [true],
                "castsShadows": true,
                "material": {
                    "albedo": { "width": 1, "height": 1, "data": albedo_px },
                    "emissive": { "width": 1, "height": 1, "data": emissive_px },
                    "doubleSided": [false]
                }
            }],
            "lights": [{
                "name": "Sun", "fileId": 1, "kind": 0, "mode": 2,
                "color": [2.0, 2.0, 2.0], "indirectColor": [2.0, 2.0, 2.0],
                "position": [0.0, 10.0, 0.0], "rotation": [s, 0.0, 0.0, s],
                "range": 0.0, "spotAngle": 0.0, "castsShadows": true
            }],
            "blueNoise": tmp.to_string_lossy(),
        }))
        .unwrap();
        let world_id = populate(pop_req).unwrap()["worldId"].as_u64().unwrap();
        let _ = std::fs::remove_file(&tmp);

        let val_res = integrate(IntegrateRequest {
            world_id,
            kind: "validity".into(),
            positions: b64_f32(&[0.0, 0.0, 0.0]),
            sample_count: 256,
            max_bounces: 1,
            pushoff: 0.0001,
            ignore_environment: true,
            max_lights_per_probe: 0,
            per_probe_light_indices: None,
        })
        .unwrap();
        let validity = decode_f32(val_res["data"].as_str().unwrap());
        assert!(
            (validity[0] - 0.5).abs() < 0.05,
            "coplanar probe validity expected ~0.5, got {}",
            validity[0]
        );

        let occ_res = integrate(IntegrateRequest {
            world_id,
            kind: "occlusion".into(),
            positions: b64_f32(&[0.0, 0.0, 0.0]),
            sample_count: 1,
            max_bounces: 1,
            pushoff: 0.0001,
            ignore_environment: true,
            max_lights_per_probe: 1,
            per_probe_light_indices: Some(b64_i32(&[0])),
        })
        .unwrap();
        release(world_id);
        let occ = decode_f32(occ_res["data"].as_str().unwrap());
        assert_eq!(
            occ,
            vec![0.0],
            "coplanar probe is occluded by floor at t == 0"
        );
    }
}
