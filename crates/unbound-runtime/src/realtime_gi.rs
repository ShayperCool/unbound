//! Precomputed realtime GI at runtime (docs/REALTIME_GI.md): the scenes whose lighting data holds
//! it, each with a solver per system (`unbound_gi`) and its realtime lightmap atlases. The managed
//! side loads a scene's data once (`load`, a control call), gives each system the textures its
//! materials' meta passes render (`set_material`), and calls `update` once per frame with the
//! frame's lights and environment; the atlases it reads back are what renderers draw with.
//! Updates allocate nothing.
use anyhow::{Context, Result, bail, ensure};
use serde_json::{Value as Json, json};
use std::collections::HashMap;
use std::path::Path;
use std::sync::{Arc, Mutex};
use unbound_assets::serialized::SerializedFile;
pub use unbound_gi::realtime_uv::Input as UvInput;
use unbound_gi::solver::{PointLight, SystemSolver, decode_rgb9e5};
use unbound_project::assetdb::AssetDb;

/// A light as the managed side passes it (`RealtimeGI.Light` in managed code).
#[repr(C)]
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct GiLight {
    /// UnityEngine.LightType.
    pub kind: i32,
    pub position: [f32; 3],
    pub direction: [f32; 3],
    /// Linear, intensity and bounce intensity applied (`unbound_gi::solver::light_color`).
    pub color: [f32; 3],
    pub range: f32,
    pub spot_angle: f32,
    pub inner_spot_angle: f32,
}

/// One frame's inputs (`RealtimeGI.Frame` in managed code).
#[repr(C)]
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct GiFrame {
    /// DynamicGI.indirectScale.
    pub indirect_scale: f32,
    /// A uniform environment's linear radiance (a flat ambient color).
    pub environment: [f32; 3],
    /// DynamicGI.synchronousMode: every system updates this frame, whatever the budget.
    pub synchronous: i32,
    /// The time this frame may spend updating systems, in milliseconds (at least one is updated).
    pub budget_ms: f32,
}

/// `update`'s result bits.
pub const UPDATE_CHANGED: i32 = 1;
pub const UPDATE_CONVERGED: i32 = 2;

/// Updates in a row without a change of the drawn texels after which a scene is converged.
const STABLE_UPDATES: u32 = 2;
/// A system update whose bounce changes by less than this part of its largest bounce value counts
/// as stable even when drawn texels still change: the bounce reads the drawn RGB9E5 texels, whose
/// 9-bit mantissas can keep a value flipping by one step.
const SETTLED_CHANGE: f32 = 1.0 / 512.0;

struct Atlas {
    size: u32,
    /// RGB9E5, row-major from v = 0.
    texels: Vec<u32>,
}

struct Scene {
    systems: Vec<SystemSolver>,
    /// Per system, its renderers' scale and offset within it.
    renderer_st: Vec<Vec<[f32; 4]>>,
    atlases: Vec<Atlas>,
    /// Point lights of the last frame, reused.
    points: Vec<PointLight>,
    lights: Vec<GiLight>,
    frame: GiFrame,
    /// The next system to update when the budget does not allow all.
    cursor: usize,
    /// Updates of every system since the drawn texels last changed.
    stable: u32,
    /// Systems updated since `stable` was last counted.
    updated: usize,
    changed: bool,
}

static SCENES: Mutex<Option<HashMap<u64, Scene>>> = Mutex::new(None);
static NEXT: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(1);

fn with_scene<T>(handle: u64, f: impl FnOnce(&mut Scene) -> Result<T>) -> Result<T> {
    let mut scenes = SCENES.lock().unwrap();
    let scene = scenes
        .get_or_insert_with(HashMap::new)
        .get_mut(&handle)
        .with_context(|| format!("no realtime GI scene {handle}"))?;
    f(scene)
}

/// Loads the precomputed realtime GI of lighting data `{guid, fileId}`: `null` without any, else
/// `{ handle, atlases: [size], systems: [{ atlas, offset: [x, y], width, height, renderers:
/// [{ object, prefab, st: [4] }] }] }`. `linear_space`: the project's color space.
pub fn load(
    db: &AssetDb,
    unity_data: &Path,
    guid: &str,
    file_id: i64,
    linear_space: bool,
) -> Result<Json> {
    let file = crate::lighting::serialized_file(db, unity_data, guid)?;
    let bytes = std::fs::read(&file).with_context(|| format!("reading {}", file.display()))?;
    let serialized = SerializedFile::parse(&bytes)?;
    let object = serialized
        .read(file_id)
        .with_context(|| format!("reading lighting data {file_id} of {}", file.display()))?;
    let Some(data) = unbound_gi::data::read_precomputed(&object)? else {
        return Ok(Json::Null);
    };
    register(data, linear_space)
}

/// Registers a scene's precomputed realtime GI; the reply of [`load`].
fn register(data: unbound_gi::data::Precomputed, linear_space: bool) -> Result<Json> {
    let systems_json = data
        .systems
        .iter()
        .map(|s| {
            let range = s.first_renderer..s.first_renderer + s.renderer_count;
            json!({
                "atlas": s.atlas,
                "offset": [s.atlas_offset.0, s.atlas_offset.1],
                "width": s.transport.width,
                "height": s.transport.height,
                "renderers": data.renderers[range.clone()].iter().zip(&data.renderer_st[range])
                    .map(|(&(object, prefab), st)| json!({ "object": object, "prefab": prefab, "st": st }))
                    .collect::<Vec<_>>(),
            })
        })
        .collect::<Vec<_>>();
    let renderer_st = data
        .systems
        .iter()
        .map(|s| data.renderer_st[s.first_renderer..s.first_renderer + s.renderer_count].to_vec())
        .collect();
    let systems = data
        .systems
        .into_iter()
        .map(|s| SystemSolver::new(Arc::new(s), linear_space))
        .collect::<Result<Vec<_>>>()?;
    let atlases = data
        .atlas_sizes
        .iter()
        .map(|&size| Atlas {
            size,
            texels: vec![0; size as usize * size as usize],
        })
        .collect();
    let handle = NEXT.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
    SCENES
        .lock()
        .unwrap()
        .get_or_insert_with(HashMap::new)
        .insert(
            handle,
            Scene {
                systems,
                renderer_st,
                atlases,
                points: Vec::new(),
                lights: Vec::new(),
                frame: GiFrame::default(),
                cursor: 0,
                stable: 0,
                updated: 0,
                changed: true,
            },
        );
    Ok(json!({ "handle": handle, "atlases": data.atlas_sizes, "systems": systems_json }))
}

pub fn release(handle: u64) {
    if let Some(scenes) = SCENES.lock().unwrap().as_mut() {
        scenes.remove(&handle);
    }
}

/// A system's albedo (`kind` 0: RGBA8 bytes) or emission (1: RGBA float) texture, as its
/// renderers' meta passes render it.
pub fn set_material(handle: u64, system: usize, kind: i32, bytes: &[u8]) -> Result<()> {
    with_scene(handle, |scene| {
        let solver = scene
            .systems
            .get_mut(system)
            .with_context(|| format!("no system {system}"))?;
        match kind {
            0 => solver.set_albedo(bytes)?,
            1 => {
                ensure!(
                    bytes.len() % 4 == 0,
                    "a float texture of {} bytes",
                    bytes.len()
                );
                let floats: Vec<f32> = bytes
                    .chunks_exact(4)
                    .map(|b| f32::from_le_bytes([b[0], b[1], b[2], b[3]]))
                    .collect();
                solver.set_emission(&floats)?
            }
            _ => bail!("material texture kind {kind}"),
        }
        scene.restart();
        Ok(())
    })
}

/// DynamicGI.SetEmissive for renderer `renderer` of system `system`.
pub fn set_emissive(handle: u64, system: usize, renderer: usize, color: [f32; 3]) -> Result<()> {
    with_scene(handle, |scene| {
        scene
            .systems
            .get_mut(system)
            .with_context(|| format!("no system {system}"))?
            .set_renderer_emission(renderer, color)?;
        scene.restart();
        Ok(())
    })
}

impl Scene {
    /// Inputs changed: the systems update until converged again.
    fn restart(&mut self) {
        self.stable = 0;
        self.updated = 0;
    }

    fn set_inputs(&mut self, frame: &GiFrame, lights: &[GiLight]) -> Result<()> {
        let lights_changed = self.lights.as_slice() != lights;
        if lights_changed {
            self.points.clear();
            for light in lights {
                match light.kind {
                    // LightType.Point
                    2 => self.points.push(PointLight {
                        position: light.position,
                        color: light.color,
                        range: light.range,
                    }),
                    0 => bail!("spot lights in precomputed realtime GI are not supported yet"),
                    1 => {
                        bail!("directional lights in precomputed realtime GI are not supported yet")
                    }
                    other => {
                        bail!("light type {other} in precomputed realtime GI is not supported")
                    }
                }
            }
            self.lights.clear();
            self.lights.extend_from_slice(lights);
            for system in &mut self.systems {
                system.set_lights(&self.points);
            }
        }
        let environment_changed = self.frame.environment != frame.environment;
        if environment_changed {
            for system in &mut self.systems {
                system.set_uniform_environment(frame.environment);
            }
        }
        if self.frame.indirect_scale != frame.indirect_scale {
            for system in &mut self.systems {
                system.set_indirect_scale(frame.indirect_scale);
            }
            self.changed = true;
        }
        if lights_changed
            || environment_changed
            || self.frame.indirect_scale != frame.indirect_scale
        {
            self.restart();
        }
        self.frame = *frame;
        Ok(())
    }

    /// Updates systems (all of them in synchronous mode, else within the budget) and copies the
    /// changed ones into their atlases.
    fn update(&mut self) -> i32 {
        let count = self.systems.len();
        if count == 0 || self.stable >= STABLE_UPDATES {
            let changed = std::mem::take(&mut self.changed);
            return UPDATE_CONVERGED | if changed { UPDATE_CHANGED } else { 0 };
        }
        let start = std::time::Instant::now();
        let budget = std::time::Duration::from_secs_f32(self.frame.budget_ms.max(0.0) / 1000.0);
        let mut done = 0;
        while done < count && (done == 0 || self.frame.synchronous != 0 || start.elapsed() < budget)
        {
            let index = self.cursor % count;
            self.cursor = (index + 1) % count;
            let system = &mut self.systems[index];
            let change = system.update();
            let atlas = &mut self.atlases[system.system().atlas];
            let drawn = copy_into(system, atlas);
            self.changed |= drawn;
            if drawn && change > SETTLED_CHANGE * system.largest_bounce() {
                self.stable = 0;
                self.updated = 0;
            }
            done += 1;
            self.updated += 1;
            if self.updated >= count {
                self.updated = 0;
                self.stable += 1;
            }
        }
        let changed = std::mem::take(&mut self.changed);
        (if changed { UPDATE_CHANGED } else { 0 })
            | if self.stable >= STABLE_UPDATES {
                UPDATE_CONVERGED
            } else {
                0
            }
    }
}

/// Copies a system's drawn texels into its atlas; whether any changed.
fn copy_into(system: &SystemSolver, atlas: &mut Atlas) -> bool {
    let s = system.system();
    let (width, height) = (s.transport.width as usize, s.transport.height as usize);
    let (ox, oy) = (s.atlas_offset.0 as usize, s.atlas_offset.1 as usize);
    let size = atlas.size as usize;
    let mut changed = false;
    for y in 0..height {
        let row = &system.encoded()[y * width..(y + 1) * width];
        let target = &mut atlas.texels[(oy + y) * size + ox..][..width];
        if target != row {
            target.copy_from_slice(row);
            changed = true;
        }
    }
    changed
}

/// One frame: the inputs, then the updates the budget allows. Returns `UPDATE_*` bits.
pub fn update(handle: u64, frame: &GiFrame, lights: &[GiLight]) -> Result<i32> {
    with_scene(handle, |scene| {
        scene.set_inputs(frame, lights)?;
        Ok(scene.update())
    })
}

/// Atlas `atlas` as RGBA half floats (alpha 1), `size * size` texels row-major from v = 0, into
/// `out`; returns the number of texels written.
pub fn read_atlas(handle: u64, atlas: usize, out: &mut [u16]) -> Result<usize> {
    with_scene(handle, |scene| {
        let atlas = scene
            .atlases
            .get(atlas)
            .with_context(|| format!("no atlas {atlas}"))?;
        ensure!(
            out.len() >= 4 * atlas.texels.len(),
            "{} values for {} texels",
            out.len(),
            atlas.texels.len()
        );
        let one = half::f16::ONE.to_bits();
        for (texel, out) in atlas.texels.iter().zip(out.chunks_exact_mut(4)) {
            // RGB9E5 values are exact in half floats.
            let rgb = decode_rgb9e5(*texel);
            for k in 0..3 {
                out[k] = half::f16::from_f32(rgb[k]).to_bits();
            }
            out[3] = one;
        }
        Ok(atlas.texels.len())
    })
}

/// Format version of the realtime lightmap UV cache entries (`realtime_uvs`).
/// 2: the chart rectangles are stored, and only layouts that passed the check.
const UV_CACHE_VERSION: u32 = 2;

/// Renderer `renderer` of system `system`'s realtime lightmap UVs
/// (`unbound_gi::realtime_uv`), checked against the system's precomputed layout and cached under
/// `<project>/Library/Unbound/RealtimeUVs` by a hash of the inputs. Writes `2 * vertices`
/// floats into `out`.
pub fn realtime_uvs(
    handle: u64,
    system: usize,
    renderer: usize,
    input: &unbound_gi::realtime_uv::Input,
    out: &mut [f32],
) -> Result<()> {
    ensure!(
        out.len() == 2 * input.positions.len(),
        "{} values for {} vertices",
        out.len(),
        input.positions.len()
    );
    let mut key = blake3::Hasher::new();
    key.update(&UV_CACHE_VERSION.to_le_bytes());
    key.update(&input.pixel_size.to_le_bytes());
    key.update(&input.min_chart_texels.to_le_bytes());
    for list in [
        bytes_of(input.positions),
        bytes_of(input.uvs),
        bytes_of(input.triangles),
    ] {
        key.update(&(list.len() as u64).to_le_bytes());
        key.update(list);
    }
    let path = crate::state::state()
        .project_root
        .join("Library/Unbound/RealtimeUVs")
        .join(format!("{}.bin", key.finalize().to_hex()));
    let cached = read_cached(&path, input.positions.len());
    let fresh = cached.is_none();
    let output = match cached {
        Some(output) => output,
        None => unbound_gi::realtime_uv::realtime_uvs(input)?,
    };
    // A cached layout is checked again (with its charts) against this scene's data.
    with_scene(handle, |scene| {
        let solver = scene
            .systems
            .get(system)
            .with_context(|| format!("no system {system}"))?;
        let st = *scene
            .renderer_st
            .get(system)
            .and_then(|r| r.get(renderer))
            .with_context(|| format!("no renderer {renderer} in system {system}"))?;
        unbound_gi::realtime_uv::check_layout(solver.system(), st, &output)
    })?;
    // Only a layout that passed the check is cached.
    if fresh && let Err(e) = write_cached(&path, &output) {
        tracing::debug!("caching realtime lightmap UVs in {}: {e:#}", path.display());
    }
    for (o, uv) in out.chunks_exact_mut(2).zip(&output.uvs) {
        o.copy_from_slice(uv);
    }
    Ok(())
}

fn bytes_of<T: Copy>(items: &[T]) -> &[u8] {
    // Plain arrays of f32 and u32 without padding.
    unsafe { std::slice::from_raw_parts(items.as_ptr() as *const u8, std::mem::size_of_val(items)) }
}

/// A cache entry: width, height, vertex count, chart count, the UVs, then the chart rectangles
/// (little-endian 32-bit words).
fn read_cached(path: &Path, vertices: usize) -> Option<unbound_gi::realtime_uv::Output> {
    let bytes = std::fs::read(path).ok()?;
    let word = |k: usize| -> Option<u32> {
        Some(u32::from_le_bytes(
            bytes.get(4 * k..4 * k + 4)?.try_into().ok()?,
        ))
    };
    let (width, height) = (word(0)?, word(1)?);
    let (count, charts) = (word(2)? as usize, word(3)? as usize);
    if count != vertices || bytes.len() != 4 * (4 + 2 * count + 4 * charts) {
        return None;
    }
    let uvs = (0..count)
        .map(|v| {
            Some([
                f32::from_bits(word(4 + 2 * v)?),
                f32::from_bits(word(5 + 2 * v)?),
            ])
        })
        .collect::<Option<Vec<_>>>()?;
    let at = 4 + 2 * count;
    let charts = (0..charts)
        .map(|c| {
            let w = |k: usize| word(at + 4 * c + k);
            Some([w(0)?, w(1)?, w(2)?, w(3)?])
        })
        .collect::<Option<Vec<_>>>()?;
    Some(unbound_gi::realtime_uv::Output {
        width,
        height,
        uvs,
        charts,
    })
}

fn write_cached(path: &Path, output: &unbound_gi::realtime_uv::Output) -> Result<()> {
    let mut bytes = Vec::with_capacity(4 * (4 + 2 * output.uvs.len() + 4 * output.charts.len()));
    let header = [
        output.width,
        output.height,
        output.uvs.len() as u32,
        output.charts.len() as u32,
    ];
    let words = header
        .into_iter()
        .chain(output.uvs.iter().flat_map(|uv| uv.map(f32::to_bits)))
        .chain(output.charts.iter().flatten().copied());
    for v in words {
        bytes.extend_from_slice(&v.to_le_bytes());
    }
    let dir = path.parent().context("no cache folder")?;
    std::fs::create_dir_all(dir)?;
    let temp = path.with_extension(format!("tmp{}", std::process::id()));
    std::fs::write(&temp, &bytes)?;
    std::fs::rename(&temp, path)?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn room() -> unbound_gi::data::Precomputed {
        let file = Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/realtimegi/Assets/Scenes/Room/LightingData.asset");
        let bytes = std::fs::read(file).unwrap();
        let serialized = SerializedFile::parse(&bytes).unwrap();
        unbound_gi::data::read_precomputed(&serialized.read(112000000).unwrap())
            .unwrap()
            .unwrap()
    }

    fn point(position: [f32; 3]) -> GiLight {
        GiLight {
            kind: 2,
            position,
            color: [1.0, 0.9, 0.7],
            range: 10.0,
            ..GiLight::default()
        }
    }

    #[test]
    fn a_scene_updates_until_converged_and_again_after_a_change() {
        let reply = register(room(), true).unwrap();
        let handle = reply["handle"].as_u64().unwrap();
        assert_eq!(reply["atlases"], json!([64]));
        let system = &reply["systems"][0];
        assert_eq!(
            (
                system["width"].as_u64(),
                system["renderers"].as_array().unwrap().len()
            ),
            (Some(52), 7)
        );
        // White albedo everywhere.
        set_material(handle, 0, 0, &vec![200u8; 4 * 52 * 52]).unwrap();
        let frame = GiFrame {
            indirect_scale: 1.0,
            environment: [0.01, 0.01, 0.02],
            synchronous: 1,
            budget_ms: 2.0,
        };
        let lights = [point([0.0, 2.0, 0.0])];
        let mut frames = 0;
        let mut changed = 0;
        loop {
            let result = update(handle, &frame, &lights).unwrap();
            frames += 1;
            changed += (result & UPDATE_CHANGED != 0) as usize;
            if result & UPDATE_CONVERGED != 0 {
                break;
            }
            assert!(frames < 500, "no convergence");
        }
        assert!(changed > 0);
        // Converged: further frames change nothing.
        assert_eq!(update(handle, &frame, &lights).unwrap(), UPDATE_CONVERGED);
        let mut texels = vec![0u16; 4 * 64 * 64];
        assert_eq!(read_atlas(handle, 0, &mut texels).unwrap(), 64 * 64);
        let lit = texels
            .chunks_exact(4)
            .filter(|t| half::f16::from_bits(t[1]).to_f32() > 0.0)
            .count();
        assert!(lit > 1000, "{lit} lit texels");
        // A moved light updates again; DynamicGI.indirectScale alone changes the drawn texels.
        assert_eq!(
            update(handle, &frame, &[point([1.0, 2.0, 0.0])]).unwrap() & UPDATE_CONVERGED,
            0
        );
        let scaled = GiFrame {
            indirect_scale: 2.0,
            ..frame
        };
        assert_ne!(
            update(handle, &scaled, &[point([1.0, 2.0, 0.0])]).unwrap() & UPDATE_CHANGED,
            0
        );
        // Unsupported lights fail loudly; the scene goes away with its handle.
        let spot = GiLight {
            kind: 0,
            ..point([0.0, 2.0, 0.0])
        };
        assert!(
            update(handle, &frame, &[spot])
                .unwrap_err()
                .to_string()
                .contains("spot")
        );
        release(handle);
        assert!(update(handle, &frame, &[]).is_err());
    }

    #[test]
    fn realtime_uv_cache_entries_round_trip_and_reject_others() {
        let dir = std::env::temp_dir().join(format!("unbound-uv-cache-{}", std::process::id()));
        let path = dir.join("entry.bin");
        let output = unbound_gi::realtime_uv::Output {
            width: 8,
            height: 12,
            uvs: vec![[0.25, 0.5], [0.75, 1.0]],
            charts: vec![[0, 0, 4, 4], [4, 0, 4, 12]],
        };
        write_cached(&path, &output).unwrap();
        assert_eq!(read_cached(&path, 2), Some(output));
        // Another vertex count or a truncated entry is not used.
        assert_eq!(read_cached(&path, 3), None);
        let bytes = std::fs::read(&path).unwrap();
        std::fs::write(&path, &bytes[..bytes.len() - 1]).unwrap();
        assert_eq!(read_cached(&path, 2), None);
        std::fs::remove_dir_all(dir).unwrap();
    }
}
