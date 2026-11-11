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
use unbound_gi::environment::{FACE_VALUES, environment_values};
use unbound_gi::probes::{ProbeSetSolver, ProbeSh};
pub use unbound_gi::realtime_uv::Input as UvInput;
use unbound_gi::solver::{Light, LightKind, SpotCone, SystemSolver, decode_rgb9e5};
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
    /// DynamicGI.synchronousMode: every system updates this frame, whatever the budget.
    pub synchronous: i32,
    /// The time this frame may spend updating systems, in milliseconds (at least one is updated).
    pub budget_ms: f32,
    /// DynamicGI.updateThreshold, in percent.
    pub update_threshold: f32,
    /// The lightmaps are directional (LightmapsMode.CombinedDirectional): the systems solve
    /// their directionality too ([`read_directionality`]).
    pub directional: i32,
}

/// `update`'s result bits.
pub const UPDATE_CHANGED: i32 = 1;
pub const UPDATE_CONVERGED: i32 = 2;
/// The light probes changed (read them with [`read_probes`]).
pub const UPDATE_PROBES: i32 = 4;

struct Atlas {
    size: u32,
    /// RGB9E5, row-major from v = 0.
    texels: Vec<u32>,
    /// The directionality of a directional lightmap (RGBA8, as the texels; empty until the
    /// scene is directional).
    directionality: Vec<[u8; 4]>,
    /// Systems of the atlas whose directionality it does not hold yet since the scene became
    /// directional: until there is none the atlas's directionality is not read (a lit texel
    /// without one would be drawn far too bright).
    directionality_missing: usize,
}

struct Scene {
    systems: Vec<SystemSolver>,
    /// Per system, its renderers' scale and offset within it.
    renderer_st: Vec<Vec<[f32; 4]>>,
    atlases: Vec<Atlas>,
    /// The lights of the last frame as the solvers take them, reused.
    solver_lights: Vec<Light>,
    lights: Vec<GiLight>,
    /// The last frame's inputs; `None` before the first frame, whose inputs all go to the
    /// solvers.
    frame: Option<GiFrame>,
    /// The next system to update when the budget does not allow all.
    cursor: usize,
    /// The light probe sets lit by the systems, each with its first LightProbes index, and the
    /// probes' coefficients by LightProbes index after the last update.
    probe_sets: Vec<(ProbeSetSolver, usize)>,
    probes: Vec<ProbeSh>,
    /// The environment's values ([`set_environment`]; black until set), and scratch space for
    /// its faces and next values.
    environment: Vec<[f32; 3]>,
    faces: Vec<[f32; 3]>,
    next_environment: Vec<[f32; 3]>,
    /// Systems updated in a row without a change of their drawn texels: the scene is converged
    /// once every system has had such an update (the bounce reads the drawn texels, so nothing
    /// changes after it until the inputs do).
    stable: usize,
    changed: bool,
    /// Per system, whether its atlas holds its directionality (see
    /// [`Atlas::directionality_missing`]).
    directionality_copied: Vec<bool>,
    /// The systems solve their directionality (the last frame's mode, when the scene has one).
    directional: bool,
    /// Why the scene has no directionality (a system's blocks could not be read): it is then
    /// drawn as a non-directional one, whatever the frames' mode.
    directionality_error: Option<String>,
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
    // Light probes that cannot be placed light nothing; the lightmaps are unaffected.
    let (ranges, error) = match light_probe_ranges(&serialized, &object) {
        Ok(ranges) => (ranges, None),
        Err(e) => (Vec::new(), Some(format!("{e:#}"))),
    };
    let mut data = data;
    data.probe_errors.extend(error);
    register(data, linear_space, &ranges)
}

/// The probe sets of a LightingData's light probes: `(hash, first probe, probe count)` in
/// LightProbes order (`m_Data.m_ProbeSets`).
fn light_probe_ranges(
    serialized: &SerializedFile,
    lighting_data: &unbound_yaml::Value,
) -> Result<Vec<([u8; 16], usize, usize)>> {
    let Some(reference) = lighting_data.get("m_LightProbes") else {
        return Ok(Vec::new());
    };
    let id = |key: &str| {
        reference
            .get(key)
            .and_then(|v| v.as_str())
            .and_then(|v| v.parse::<i64>().ok())
            .unwrap_or(0)
    };
    if id("m_PathID") == 0 || id("m_FileID") != 0 {
        return Ok(Vec::new());
    }
    let probes = serialized.read(id("m_PathID"))?;
    let sets = probes
        .get("m_Data")
        .and_then(|d| d.get("m_ProbeSets"))
        .and_then(|s| s.as_seq())
        .unwrap_or_default();
    sets.iter()
        .map(|set| {
            let number = |key: &str| -> Result<usize> {
                set.get(key)
                    .and_then(|v| v.as_str())
                    .and_then(|v| v.parse().ok())
                    .with_context(|| format!("a light probe set without {key}"))
            };
            let hash = set
                .get("m_Hash")
                .context("a light probe set without a hash")?;
            let mut bytes = [0u8; 16];
            for (k, b) in bytes.iter_mut().enumerate() {
                *b = hash
                    .get(&format!("bytes[{k}]"))
                    .and_then(|v| v.as_str())
                    .and_then(|v| v.parse().ok())
                    .context("a malformed light probe set hash")?;
            }
            Ok((bytes, number("m_Offset")?, number("m_Size")?))
        })
        .collect()
}

/// Registers a scene's precomputed realtime GI with its light probes' probe sets (`ranges`, as
/// [`light_probe_ranges`] gives them); the reply of [`load`].
fn register(
    data: unbound_gi::data::Precomputed,
    linear_space: bool,
    ranges: &[([u8; 16], usize, usize)],
) -> Result<Json> {
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
    let scene: Vec<Arc<unbound_gi::data::System>> =
        data.systems.into_iter().map(Arc::new).collect();
    let systems = scene
        .iter()
        .map(|s| SystemSolver::new_in_scene(s.clone(), linear_space, &scene))
        .collect::<Result<Vec<_>>>()?;
    // Each probe set lights the LightProbes range of its hash; one without a range, or of
    // another size, lights nothing (reported).
    let mut probe_sets = Vec::new();
    let mut probe_count = 0;
    let mut probe_error = data.probe_errors.first().cloned();
    for set in data.probe_sets {
        let range = ranges.iter().find(|r| r.0 == set.hash);
        let result = match range {
            Some(&(_, offset, size)) if size == set.probes.len() => {
                ProbeSetSolver::new(Arc::new(set), &systems).map(|s| (s, offset))
            }
            Some(&(_, _, size)) => Err(anyhow::anyhow!(
                "a light probe set of {} probes where the scene's light probes have {size}",
                set.probes.len()
            )),
            None => Err(anyhow::anyhow!(
                "a light probe set the scene's light probes do not list"
            )),
        };
        match result {
            Ok((solver, offset)) => {
                probe_count = probe_count.max(offset + solver.probe_set().probes.len());
                probe_sets.push((solver, offset));
            }
            // The first reason is kept (a failed range read leaves every set unplaced).
            Err(e) => {
                probe_error.get_or_insert_with(|| format!("{e:#}"));
            }
        }
    }
    let atlases = data
        .atlas_sizes
        .iter()
        .map(|&size| Atlas {
            size,
            texels: vec![0; size as usize * size as usize],
            directionality: Vec::new(),
            directionality_missing: 0,
        })
        .collect();
    let directionality_error = systems
        .iter()
        .find_map(|s| s.system().transport.block_error.clone());
    // Copies of blocks and texels are read as the precomputed data lists them, but no measured
    // scene has any: the managed side says so once for a directional scene.
    let copies = systems.iter().any(|s| {
        let segments = &s.system().transport.segments;
        segments.iter().any(|g| !g.copies.is_empty())
    });
    let system_count = systems.len();
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
                solver_lights: Vec::new(),
                lights: Vec::new(),
                frame: None,
                cursor: 0,
                stable: 0,
                changed: true,
                directionality_copied: vec![false; system_count],
                directional: false,
                directionality_error: directionality_error.clone(),
                probe_sets,
                probes: vec![[0.0; 27]; probe_count],
                environment: vec![[0.0; 3]; unbound_gi::data::ENVIRONMENT_VALUES],
                faces: vec![[0.0; 3]; FACE_VALUES],
                next_environment: vec![[0.0; 3]; unbound_gi::data::ENVIRONMENT_VALUES],
            },
        );
    Ok(json!({
        "handle": handle,
        "atlases": data.atlas_sizes,
        "systems": systems_json,
        "probes": { "count": probe_count, "error": probe_error },
        "directionality": { "error": directionality_error, "copies": copies },
    }))
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

/// The environment of the scene's systems: six cube faces of linear RGB
/// (`unbound_gi::environment`, `FACE_VALUES` values of 3 floats). Unchanged values change nothing.
pub fn set_environment(handle: u64, faces: &[f32]) -> Result<()> {
    ensure!(
        faces.len() == 3 * FACE_VALUES,
        "an environment of {} floats, not {}",
        faces.len(),
        3 * FACE_VALUES
    );
    with_scene(handle, |scene| {
        for (face, rgb) in scene.faces.iter_mut().zip(faces.chunks_exact(3)) {
            *face = [rgb[0], rgb[1], rgb[2]];
        }
        environment_values(&scene.faces, &mut scene.next_environment)?;
        if scene.next_environment != scene.environment {
            for system in &mut scene.systems {
                system.set_environment(&scene.next_environment)?;
            }
            std::mem::swap(&mut scene.environment, &mut scene.next_environment);
            scene.restart();
        }
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
    }

    fn set_inputs(&mut self, frame: &GiFrame, lights: &[GiLight]) -> Result<()> {
        let lights_changed = self.lights.as_slice() != lights;
        if lights_changed {
            self.solver_lights.clear();
            for light in lights {
                let kind = match light.kind {
                    // LightType.Point
                    2 => LightKind::Point {
                        position: light.position,
                        range: light.range,
                    },
                    // LightType.Spot
                    0 => LightKind::Spot {
                        position: light.position,
                        range: light.range,
                        cone: SpotCone::new(light.direction, light.spot_angle),
                    },
                    // LightType.Directional
                    1 => LightKind::Directional {
                        to_light: light.direction.map(|v| -v),
                    },
                    other => {
                        bail!("light type {other} in precomputed realtime GI is not supported")
                    }
                };
                self.solver_lights.push(Light {
                    color: light.color,
                    kind,
                });
            }
            for system in &mut self.systems {
                system.set_lights(&self.solver_lights)?;
            }
            self.lights.clear();
            self.lights.extend_from_slice(lights);
        }
        let last = self.frame.replace(*frame);
        let differs = |f: fn(&GiFrame) -> [f32; 3]| last.is_none_or(|l| f(&l) != f(frame));
        let scale_changed = differs(|f| [f.indirect_scale; 3]);
        if scale_changed {
            for system in &mut self.systems {
                system.set_indirect_scale(frame.indirect_scale);
            }
            self.changed = true;
        }
        let threshold_changed = differs(|f| [f.update_threshold; 3]);
        if threshold_changed {
            for system in &mut self.systems {
                system.set_update_threshold(frame.update_threshold);
            }
        }
        // Directional lightmaps: every system solves its directionality from its next update
        // on (and stops when the mode is left, keeping the atlases' last texels).
        let directional = frame.directional != 0 && self.directionality_error.is_none();
        let directional_changed = self.directional != directional;
        if directional_changed {
            self.directional = directional;
            for system in &mut self.systems {
                system.set_directional(directional)?;
            }
            if directional {
                // Every system solves its directionality again; an atlas's is read once it holds
                // all of them.
                self.directionality_copied.fill(false);
                for atlas in &mut self.atlases {
                    atlas.directionality.resize(atlas.texels.len(), [0; 4]);
                    atlas.directionality_missing = 0;
                }
                for system in &self.systems {
                    self.atlases[system.system().atlas].directionality_missing += 1;
                }
            }
        }
        if lights_changed || threshold_changed || scale_changed || directional_changed {
            self.restart();
        }
        Ok(())
    }

    /// Updates systems (all of them in synchronous mode, else within the budget) and copies the
    /// changed ones into their atlases.
    fn update(&mut self) -> i32 {
        let count = self.systems.len();
        if count == 0 || self.stable >= count {
            let changed = std::mem::take(&mut self.changed);
            return UPDATE_CONVERGED | if changed { UPDATE_CHANGED } else { 0 };
        }
        let frame = self.frame.unwrap_or_default();
        let start = std::time::Instant::now();
        let budget = std::time::Duration::from_secs_f32(frame.budget_ms.max(0.0) / 1000.0);
        let mut done = 0;
        while done < count
            && self.stable < count
            && (done == 0 || frame.synchronous != 0 || start.elapsed() < budget)
        {
            let index = self.cursor % count;
            self.cursor = (index + 1) % count;
            unbound_gi::solver::update_in(&mut self.systems, index);
            let system = &self.systems[index];
            let atlas = &mut self.atlases[system.system().atlas];
            let drawn = copy_rows(system, system.encoded(), atlas.size, &mut atlas.texels);
            // The directionality changes only with solved segments; convergence is the drawn
            // light's, which the bounce reads.
            self.changed |= drawn;
            if let Some(texels) = system.directionality() {
                let copied = copy_rows(system, texels, atlas.size, &mut atlas.directionality);
                // An atlas's first complete directionality is a change too, whatever it holds.
                let first = !std::mem::replace(&mut self.directionality_copied[index], true);
                if first {
                    atlas.directionality_missing -= 1;
                }
                self.changed |= copied || first;
            }
            self.stable = if drawn { 0 } else { self.stable + 1 };
            done += 1;
        }
        // The probes follow every update: they read the input values, which change with each.
        // (Their solve is not counted in the budget: it is linear in their references.)
        for (set, offset) in &self.probe_sets {
            let end = offset + set.probe_set().probes.len();
            set.solve(
                &self.systems,
                &self.environment,
                frame.indirect_scale,
                &mut self.probes[*offset..end],
            );
        }
        let probes = if self.probe_sets.is_empty() {
            0
        } else {
            UPDATE_PROBES
        };
        let changed = std::mem::take(&mut self.changed);
        probes
            | (if changed { UPDATE_CHANGED } else { 0 })
            | if self.stable >= count {
                UPDATE_CONVERGED
            } else {
                0
            }
    }
}

/// Copies a system's texels (`rows`, row-major at its resolution: its drawn light or its
/// directionality) to its place in an atlas of `size` x `size` texels; whether any changed.
fn copy_rows<T: Copy + PartialEq>(
    system: &SystemSolver,
    rows: &[T],
    size: u32,
    atlas: &mut [T],
) -> bool {
    let s = system.system();
    let (width, height) = (s.transport.width as usize, s.transport.height as usize);
    let (ox, oy) = (s.atlas_offset.0 as usize, s.atlas_offset.1 as usize);
    let size = size as usize;
    let mut changed = false;
    for y in 0..height {
        let row = &rows[y * width..(y + 1) * width];
        let target = &mut atlas[(oy + y) * size + ox..][..width];
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

/// Atlas `atlas`'s directionality as RGBA8 (`unity_DynamicDirectionality`), `size * size` texels
/// row-major from v = 0, into `out`; returns the number of texels written: 0 while the scene is
/// not directional, and until every system of the atlas has solved its directionality since it
/// became directional.
pub fn read_directionality(handle: u64, atlas: usize, out: &mut [u8]) -> Result<usize> {
    with_scene(handle, |scene| {
        let atlas = scene
            .atlases
            .get(atlas)
            .with_context(|| format!("no atlas {atlas}"))?;
        if !scene.directional || atlas.directionality_missing > 0 {
            return Ok(0);
        }
        let texels = &atlas.directionality;
        ensure!(
            out.len() >= 4 * texels.len(),
            "{} bytes for {} texels",
            out.len(),
            texels.len()
        );
        for (texel, out) in texels.iter().zip(out.chunks_exact_mut(4)) {
            out.copy_from_slice(texel);
        }
        Ok(texels.len())
    })
}

/// The light probes the scene's probe sets light into `out` (27 coefficients each by LightProbes
/// index; the others are left as they are); the number of probes the sets span.
pub fn read_probes(handle: u64, out: &mut [f32]) -> Result<usize> {
    with_scene(handle, |scene| {
        ensure!(
            out.len() >= 27 * scene.probes.len(),
            "{} values for {} light probes",
            out.len(),
            scene.probes.len()
        );
        for (set, offset) in &scene.probe_sets {
            let end = offset + set.probe_set().probes.len();
            for (i, sh) in scene.probes[*offset..end].iter().enumerate() {
                out[27 * (offset + i)..][..27].copy_from_slice(sh);
            }
        }
        Ok(scene.probes.len())
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

    /// The Room scene's precomputed realtime GI and its light probes' probe sets.
    fn room_with_probes() -> (unbound_gi::data::Precomputed, Vec<([u8; 16], usize, usize)>) {
        let file = Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/realtimegi/Assets/Scenes/Room/LightingData.asset");
        let bytes = std::fs::read(file).unwrap();
        let serialized = SerializedFile::parse(&bytes).unwrap();
        let object = serialized.read(112000000).unwrap();
        let ranges = light_probe_ranges(&serialized, &object).unwrap();
        let data = unbound_gi::data::read_precomputed(&object)
            .unwrap()
            .unwrap();
        (data, ranges)
    }

    fn room() -> unbound_gi::data::Precomputed {
        room_with_probes().0
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
        let reply = register(room(), true, &[]).unwrap();
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
        // White albedo everywhere, a dim blue environment.
        set_material(handle, 0, 0, &vec![200u8; 4 * 52 * 52]).unwrap();
        let sky = [0.01f32, 0.01, 0.02].repeat(FACE_VALUES);
        set_environment(handle, &sky).unwrap();
        let frame = GiFrame {
            indirect_scale: 1.0,
            synchronous: 1,
            budget_ms: 2.0,
            update_threshold: 1.0,
            directional: 0,
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
        // Converged: further frames change nothing, nor does the same environment again; another
        // one updates again, and a malformed one fails.
        assert_eq!(update(handle, &frame, &lights).unwrap(), UPDATE_CONVERGED);
        set_environment(handle, &sky).unwrap();
        assert_eq!(update(handle, &frame, &lights).unwrap(), UPDATE_CONVERGED);
        assert!(set_environment(handle, &sky[1..]).is_err());
        let brighter = [0.1f32, 0.1, 0.2].repeat(FACE_VALUES);
        set_environment(handle, &brighter).unwrap();
        assert_eq!(
            update(handle, &frame, &lights).unwrap() & UPDATE_CONVERGED,
            0
        );
        set_environment(handle, &sky).unwrap();
        while update(handle, &frame, &lights).unwrap() & UPDATE_CONVERGED == 0 {}
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
        // DynamicGI.updateThreshold 0 solves every part again in each update: the output still
        // converges (the drawn texels stop changing).
        let every = GiFrame {
            update_threshold: 0.0,
            ..scaled
        };
        let lights = [point([1.0, 2.0, 0.0])];
        assert!((0..500).any(|_| update(handle, &every, &lights).unwrap() & UPDATE_CONVERGED != 0));
        // A spot light lights the scene too.
        let spot = GiLight {
            kind: 0,
            direction: [0.0, -1.0, 0.0],
            spot_angle: 60.0,
            ..point([0.0, 2.0, 0.0])
        };
        assert_eq!(
            update(handle, &frame, &[spot]).unwrap() & UPDATE_CONVERGED,
            0
        );
        // A directional light too (shadowed by the system's precomputed visibility).
        let directional = GiLight {
            kind: 1,
            direction: [0.3, -0.8, 0.5],
            ..spot
        };
        assert_eq!(
            update(handle, &frame, &[directional]).unwrap() & UPDATE_CONVERGED,
            0
        );
        // Unsupported lights fail loudly; the scene goes away with its handle.
        let area = GiLight { kind: 3, ..spot };
        assert!(
            update(handle, &frame, &[area])
                .unwrap_err()
                .to_string()
                .contains("light type 3")
        );
        release(handle);
        assert!(update(handle, &frame, &[]).is_err());
    }

    #[test]
    fn light_probes_follow_the_update_and_the_indirect_scale() {
        let (data, ranges) = room_with_probes();
        // The baked and the realtime probe sets both cover the nine probes.
        assert_eq!(ranges.len(), 2);
        assert!(ranges.iter().all(|r| (r.1, r.2) == (0, 9)));
        let reply = register(data, true, &ranges).unwrap();
        assert_eq!(reply["probes"], json!({ "count": 9, "error": null }));
        let handle = reply["handle"].as_u64().unwrap();
        set_material(handle, 0, 0, &vec![200u8; 4 * 52 * 52]).unwrap();
        let mut frame = GiFrame {
            indirect_scale: 1.0,
            synchronous: 1,
            budget_ms: 2.0,
            // Every part solved until the output stops changing: the probes then scale with the
            // indirect scale (RGB9E5 rounding aside).
            update_threshold: 0.0,
            directional: 0,
        };
        let lights = [point([0.0, 2.0, 0.0])];
        let converge = |frame: &GiFrame| {
            // An update solves the probes: the first after a change reports them.
            assert_ne!(update(handle, frame, &lights).unwrap() & UPDATE_PROBES, 0);
            for _ in 0..500 {
                if update(handle, frame, &lights).unwrap() & UPDATE_CONVERGED != 0 {
                    // Converged, nothing is solved again.
                    assert_eq!(update(handle, frame, &lights).unwrap(), UPDATE_CONVERGED);
                    let mut sh = vec![0f32; 27 * 9];
                    assert_eq!(read_probes(handle, &mut sh).unwrap(), 9);
                    return sh;
                }
            }
            panic!("no convergence");
        };
        let one = converge(&frame);
        // Lit probes: band 0 of every probe is positive in every channel.
        assert!((0..9).all(|p| (0..3).all(|c| one[27 * p + 9 * c] > 0.01)));
        frame.indirect_scale = 2.0;
        let two = converge(&frame);
        for (a, b) in one.iter().zip(&two) {
            assert!((2.0 * a - b).abs() <= 0.01 * (0.01 + b.abs()), "{a} {b}");
        }
        // Too small a buffer fails.
        assert!(read_probes(handle, &mut [0f32; 26]).is_err());
        release(handle);
        // A probe set the light probes do not list lights nothing, and says so.
        let reply = register(room(), true, &[]).unwrap();
        assert_eq!(reply["probes"]["count"], json!(0));
        assert!(
            reply["probes"]["error"]
                .as_str()
                .unwrap()
                .contains("do not list")
        );
        release(reply["handle"].as_u64().unwrap());
        // A probe set that could not be read is reported; the lightmaps are unaffected.
        let (mut data, ranges) = room_with_probes();
        data.probe_sets.clear();
        data.probe_errors
            .push("a light probe set of another version".into());
        let reply = register(data, true, &ranges).unwrap();
        assert_eq!(reply["systems"].as_array().unwrap().len(), 1);
        assert_eq!(reply["probes"]["count"], json!(0));
        assert_eq!(
            reply["probes"]["error"],
            json!("a light probe set of another version")
        );
        release(reply["handle"].as_u64().unwrap());
    }

    #[test]
    fn the_first_frame_sets_every_input() {
        // DynamicGI.indirectScale 0 and updateThreshold 0 from a scene's first frame on (the
        // solvers start at 1 and 1%): the drawn texels are black, and the threshold reaches
        // the solvers.
        let handle = register(room(), true, &[]).unwrap()["handle"]
            .as_u64()
            .unwrap();
        set_material(handle, 0, 0, &vec![200u8; 4 * 52 * 52]).unwrap();
        let frame = GiFrame {
            indirect_scale: 0.0,
            synchronous: 1,
            budget_ms: 2.0,
            update_threshold: 0.0,
            directional: 0,
        };
        let lights = [point([0.0, 2.0, 0.0])];
        for _ in 0..3 {
            update(handle, &frame, &lights).unwrap();
        }
        let mut texels = vec![0u16; 4 * 64 * 64];
        read_atlas(handle, 0, &mut texels).unwrap();
        assert!(texels.chunks_exact(4).all(|t| {
            t[..3]
                .iter()
                .all(|&h| half::f16::from_bits(h).to_f32() == 0.0)
        }));
        with_scene(handle, |scene| {
            assert!(scene.systems.iter().all(|s| s.update_threshold() == 0.0));
            Ok(())
        })
        .unwrap();
        release(handle);
    }

    #[test]
    fn a_directional_scene_fills_its_directionality_atlas() {
        let handle = register(room(), true, &[]).unwrap()["handle"]
            .as_u64()
            .unwrap();
        set_material(handle, 0, 0, &vec![200u8; 4 * 52 * 52]).unwrap();
        let mut frame = GiFrame {
            indirect_scale: 1.0,
            synchronous: 1,
            budget_ms: 2.0,
            update_threshold: 1.0,
            directional: 0,
        };
        let lights = [point([0.0, 2.0, 0.0])];
        let converge = |frame: &GiFrame| {
            for _ in 0..1000 {
                if update(handle, frame, &lights).unwrap() & UPDATE_CONVERGED != 0 {
                    return;
                }
            }
            panic!("no convergence");
        };
        // Not directional: nothing to read.
        converge(&frame);
        let mut texels = vec![0u8; 4 * 64 * 64];
        assert_eq!(read_directionality(handle, 0, &mut texels).unwrap(), 0);
        // Directional from a converged state: the update starts again (every part of the
        // lightmap is solved once more, for its directionality) and changes the atlases.
        frame.directional = 1;
        let first = update(handle, &frame, &lights).unwrap();
        assert_eq!(first & UPDATE_CHANGED, UPDATE_CHANGED);
        converge(&frame);
        assert_eq!(
            read_directionality(handle, 0, &mut texels).unwrap(),
            64 * 64
        );
        // The room's sides are lit from its inside: the floor from above (the direction's y well
        // above 1/2), the ceiling from below, the walls from their sides, each with a fourth
        // channel (the half Lambert term of the direction with the side's normal) well above
        // its least.
        let lit: Vec<&[u8]> = texels.chunks_exact(4).filter(|t| t[3] != 0).collect();
        assert!(lit.len() > 2000, "{} texels", lit.len());
        assert!(lit.iter().all(|t| t[3] >= 127));
        let facing = |f: fn(&[u8]) -> bool| lit.iter().filter(|t| t[3] > 180 && f(t)).count();
        assert!(facing(|t| t[1] > 180) > 150, "the floor");
        assert!(facing(|t| t[1] < 75) > 150, "the ceiling");
        assert!(
            facing(|t| t[0] > 180 || t[0] < 75 || t[2] > 180 || t[2] < 75) > 400,
            "the walls"
        );
        // A capacity below the atlas fails; back in the other mode there is nothing to read.
        assert!(read_directionality(handle, 0, &mut texels[..16]).is_err());
        frame.directional = 0;
        update(handle, &frame, &lights).unwrap();
        assert_eq!(read_directionality(handle, 0, &mut texels).unwrap(), 0);
        release(handle);
    }

    #[test]
    fn an_atlas_of_several_systems_gives_its_directionality_once_all_are_solved() {
        // The two-system scene (one atlas), one system updated per frame: becoming directional
        // while lit, the atlas has no directionality to read until both systems have solved
        // theirs, and then none of its lit texels lacks one.
        let file = Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/realtimegi/Assets/Scenes/RoomSystems/LightingData.asset");
        let bytes = std::fs::read(file).unwrap();
        let serialized = SerializedFile::parse(&bytes).unwrap();
        let data = unbound_gi::data::read_precomputed(&serialized.read(112000000).unwrap())
            .unwrap()
            .unwrap();
        let reply = register(data, true, &[]).unwrap();
        assert_eq!(
            reply["directionality"],
            json!({ "error": null, "copies": false })
        );
        let handle = reply["handle"].as_u64().unwrap();
        let size = reply["atlases"][0].as_u64().unwrap() as usize;
        for (index, system) in reply["systems"].as_array().unwrap().iter().enumerate() {
            let texels =
                (system["width"].as_u64().unwrap() * system["height"].as_u64().unwrap()) as usize;
            set_material(handle, index, 0, &vec![200u8; 4 * texels]).unwrap();
        }
        let mut frame = GiFrame {
            indirect_scale: 1.0,
            synchronous: 0,
            budget_ms: 0.0,
            update_threshold: 1.0,
            directional: 0,
        };
        let lights = [point([0.0, 2.0, 0.0])];
        let mut converged = false;
        for _ in 0..4000 {
            if update(handle, &frame, &lights).unwrap() & UPDATE_CONVERGED != 0 {
                converged = true;
                break;
            }
        }
        assert!(converged);
        let mut light = vec![0u16; 4 * size * size];
        read_atlas(handle, 0, &mut light).unwrap();
        let mut texels = vec![0u8; 4 * size * size];
        frame.directional = 1;
        // The first frame updates one system: nothing to read yet.
        update(handle, &frame, &lights).unwrap();
        assert_eq!(read_directionality(handle, 0, &mut texels).unwrap(), 0);
        // The second one completes the atlas, and says so.
        let second = update(handle, &frame, &lights).unwrap();
        assert_eq!(second & UPDATE_CHANGED, UPDATE_CHANGED);
        assert_eq!(
            read_directionality(handle, 0, &mut texels).unwrap(),
            size * size
        );
        read_atlas(handle, 0, &mut light).unwrap();
        let lit_without = light
            .chunks_exact(4)
            .zip(texels.chunks_exact(4))
            .filter(|(l, d)| l[..3].iter().any(|&v| v != 0) && d[3] < 127)
            .count();
        assert_eq!(lit_without, 0);
        assert!(texels.chunks_exact(4).filter(|d| d[3] >= 127).count() > 2000);
        release(handle);
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
