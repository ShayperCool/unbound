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
use unbound_gi::solver::{
    Falloff, FalloffTable, Light, LightKind, SpotCone, SystemSolver, decode_rgb9e5,
};
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
    /// The angle of a spot light's inner cone, in degrees (the built-in pipeline's:
    /// `LightmapperUtils.ExtractInnerCone`; a render pipeline gives its own).
    pub inner_spot_angle: f32,
    /// Experimental.GlobalIllumination.FalloffType of a point or spot light.
    pub falloff: i32,
}

/// Experimental.GlobalIllumination.FalloffType's values ([`GiLight::falloff`]) realtime GI
/// takes; InverseSquaredNoRangeAttenuation (1), Linear (2) and Undefined (4) are not measured.
pub const FALLOFF_INVERSE_SQUARED: i32 = 0;
pub const FALLOFF_LEGACY: i32 = 3;

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
/// [{ object, prefab, st: [4] }] }], probes: { count, error }, directionality: { error, copies } }`
/// (`directionality.error`: why the scene has none; `copies`: whether a system copies texels).
pub fn load(db: &AssetDb, unity_data: &Path, guid: &str, file_id: i64) -> Result<Json> {
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
    register(data, &ranges)
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
        .map(|s| SystemSolver::new_in_scene(s.clone(), &scene))
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
                let falloff = || {
                    Ok(FalloffTable::of(
                        match light.falloff {
                            FALLOFF_INVERSE_SQUARED => Falloff::InverseSquared,
                            FALLOFF_LEGACY => Falloff::Legacy,
                            other => bail!(
                                "light falloff type {other} in precomputed realtime GI is not supported"
                            ),
                        },
                        light.range,
                    ))
                };
                let kind = match light.kind {
                    // LightType.Point
                    2 => LightKind::Point {
                        position: light.position,
                        range: light.range,
                        falloff: falloff()?,
                    },
                    // LightType.Spot
                    0 => LightKind::Spot {
                        position: light.position,
                        range: light.range,
                        falloff: falloff()?,
                        cone: SpotCone::new(
                            light.direction,
                            light.spot_angle,
                            light.inner_spot_angle,
                        ),
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
/// 3: each chart's blocks are stored; charts go into holes between the charts placed before them.
/// 4: how the charts are made is part of an entry's key (kept from the mesh, or automatic with
/// its normals and limits).
/// 5: automatic charts of the instance's geometry as the precompute makes it (turned instances
/// too), a Max Distance of zero, parts without area and the rounds' limit.
const UV_CACHE_VERSION: u32 = 5;

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
    match input.charts {
        unbound_gi::realtime_uv::Charts::Preserved => {
            key.update(&[0]);
        }
        unbound_gi::realtime_uv::Charts::Automatic { normals, params } => {
            key.update(&[1, params.ignore_normals as u8]);
            key.update(&params.max_distance.to_le_bytes());
            key.update(&params.max_angle.to_le_bytes());
            key.update(&(normals.len() as u64).to_le_bytes());
            key.update(bytes_of(normals));
        }
    }
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
    let output = cached_or_fresh(
        &path,
        input.positions.len(),
        || unbound_gi::realtime_uv::realtime_uvs(input),
        |output| {
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
                unbound_gi::realtime_uv::check_layout(solver.system(), st, output)
            })
        },
    )?;
    for (o, uv) in out.chunks_exact_mut(2).zip(&output.uvs) {
        o.copy_from_slice(uv);
    }
    Ok(())
}

/// The cache entry at `path` when it passes `check` (a cached layout is checked again, with its
/// charts, against this scene's data); else a layout made anew, which must pass it and then
/// replaces the entry: only a layout that passed the check is cached, and a damaged entry does
/// not outlive its first use.
fn cached_or_fresh(
    path: &Path,
    vertices: usize,
    make: impl FnOnce() -> Result<unbound_gi::realtime_uv::Output>,
    check: impl Fn(&unbound_gi::realtime_uv::Output) -> Result<()>,
) -> Result<unbound_gi::realtime_uv::Output> {
    if let Some(cached) = read_cached(path, vertices).filter(|output| check(output).is_ok()) {
        return Ok(cached);
    }
    let output = make()?;
    check(&output)?;
    if let Err(e) = write_cached(path, &output) {
        tracing::debug!("caching realtime lightmap UVs in {}: {e:#}", path.display());
    }
    Ok(output)
}

fn bytes_of<T: Copy>(items: &[T]) -> &[u8] {
    // Plain arrays of f32 and u32 without padding.
    unsafe { std::slice::from_raw_parts(items.as_ptr() as *const u8, std::mem::size_of_val(items)) }
}

/// Bits per word of a cached chart's blocks.
const BLOCK_WORD_BITS: usize = u32::BITS as usize;

/// The blocks of a chart rectangle of `rect` texels, none when it is not of whole blocks.
fn rect_blocks(rect: [u32; 4]) -> Option<usize> {
    let block = unbound_gi::realtime_uv::BLOCK_TEXELS;
    (rect[2] % block == 0 && rect[3] % block == 0)
        .then(|| (rect[2] / block) as usize * (rect[3] / block) as usize)
}

/// A cache entry: width, height, vertex count, chart count, the UVs, the chart rectangles, then
/// each chart's blocks, one bit each from a word's lowest (little-endian 32-bit words).
fn read_cached(path: &Path, vertices: usize) -> Option<unbound_gi::realtime_uv::Output> {
    let bytes = std::fs::read(path).ok()?;
    let word = |k: usize| -> Option<u32> {
        Some(u32::from_le_bytes(
            bytes.get(4 * k..4 * k + 4)?.try_into().ok()?,
        ))
    };
    let (width, height) = (word(0)?, word(1)?);
    let (count, charts) = (word(2)? as usize, word(3)? as usize);
    if count != vertices {
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
    let rects = (0..charts)
        .map(|c| {
            let w = |k: usize| word(at.checked_add(4 * c)?.checked_add(k)?);
            Some([w(0)?, w(1)?, w(2)?, w(3)?])
        })
        .collect::<Option<Vec<_>>>()?;
    let mut at = at + 4 * charts;
    let mut placed = Vec::with_capacity(charts);
    for rect in rects {
        let blocks = rect_blocks(rect)?;
        let words = blocks.div_ceil(BLOCK_WORD_BITS);
        let blocks = (0..blocks)
            .map(|b| Some(word(at + b / BLOCK_WORD_BITS)? >> (b % BLOCK_WORD_BITS) & 1 != 0))
            .collect::<Option<Vec<_>>>()?;
        at += words;
        placed.push(unbound_gi::realtime_uv::PlacedChart { rect, blocks });
    }
    if bytes.len() != 4 * at {
        return None;
    }
    Some(unbound_gi::realtime_uv::Output {
        width,
        height,
        uvs,
        charts: placed,
    })
}

fn write_cached(path: &Path, output: &unbound_gi::realtime_uv::Output) -> Result<()> {
    let header = [
        output.width,
        output.height,
        output.uvs.len() as u32,
        output.charts.len() as u32,
    ];
    let mut words: Vec<u32> = header
        .into_iter()
        .chain(output.uvs.iter().flat_map(|uv| uv.map(f32::to_bits)))
        .chain(output.charts.iter().flat_map(|chart| chart.rect))
        .collect();
    for chart in &output.charts {
        ensure!(
            rect_blocks(chart.rect) == Some(chart.blocks.len()),
            "a chart at {:?} with {} blocks",
            chart.rect,
            chart.blocks.len()
        );
        words.extend(chart.blocks.chunks(BLOCK_WORD_BITS).map(|bits| {
            bits.iter()
                .enumerate()
                .fold(0u32, |word, (b, &used)| word | (used as u32) << b)
        }));
    }
    let bytes: Vec<u8> = words.into_iter().flat_map(u32::to_le_bytes).collect();
    write_atomic(path, &bytes)
}

/// Format version of the bake's cache entries (`bake`), beyond what the precompute's own
/// version (`unbound_gi::precompute::FORMAT_VERSION`) covers.
/// 1: a checksum, then `unbound_gi::precompute::bake::Baked::encode`'s encoding.
const BAKE_CACHE_VERSION: u32 = 1;

/// One renderer of a scene as Unbound's own precompute takes it.
pub struct BakeInput {
    /// The scene mapping's ids of it (its object and prefab file IDs).
    pub object: i64,
    pub prefab: i64,
    /// Its mesh in its own space (normals empty when the mesh has none).
    pub positions: Vec<[f32; 3]>,
    pub normals: Vec<[f32; 3]>,
    /// The input of its realtime lightmap UVs (Mesh.uv3, else uv2, else uv).
    pub uvs: Vec<[f32; 2]>,
    pub triangles: Vec<[u32; 3]>,
    /// Local to world, column by column.
    pub local_to_world: [f32; 16],
    pub pixel_size: f32,
    pub min_chart_texels: u32,
    /// Automatic charts (Optimize Realtime UVs): the precompute's instance matrix and limits;
    /// `None` keeps the mesh's charts.
    pub automatic: Option<(
        unbound_gi::instance::Matrix,
        unbound_gi::auto_charts::Params,
    )>,
    pub cluster_size: f32,
    pub modelling_tolerance: f32,
    /// Renderers share a system only within one group (Lightmap Parameters and System Tag).
    pub group: u64,
    /// Its Lightmap Parameters' Irradiance Quality and Budget (one per group).
    pub settings: unbound_gi::precompute::transport::Settings,
}

fn point(m: &[f32; 16], p: [f32; 3]) -> [f32; 3] {
    [0, 1, 2].map(|r| m[r] * p[0] + m[4 + r] * p[1] + m[8 + r] * p[2] + m[12 + r])
}

impl BakeInput {
    fn hash_into(&self, key: &mut blake3::Hasher) {
        let automatic = self.automatic.as_ref();
        key.update(&self.object.to_le_bytes());
        key.update(&self.prefab.to_le_bytes());
        for list in [
            bytes_of(&self.positions),
            bytes_of(&self.normals),
            bytes_of(&self.uvs),
            bytes_of(&self.triangles),
            bytes_of(&self.local_to_world),
            bytes_of(automatic.map_or(&[][..], |a| &a.0[..])),
        ] {
            key.update(&(list.len() as u64).to_le_bytes());
            key.update(list);
        }
        key.update(&[automatic.is_some() as u8]);
        if let Some((_, p)) = automatic {
            key.update(&p.max_distance.to_le_bytes());
            key.update(&p.max_angle.to_le_bytes());
            key.update(&[p.ignore_normals as u8]);
        }
        for v in [self.pixel_size, self.cluster_size, self.modelling_tolerance] {
            key.update(&v.to_le_bytes());
        }
        key.update(&self.min_chart_texels.to_le_bytes());
        key.update(&self.group.to_le_bytes());
        key.update(&(self.settings.rays as u64).to_le_bytes());
        key.update(&(self.settings.budget as u64).to_le_bytes());
    }
}

/// A scene's realtime GI made by Unbound's own precompute from `inputs` and the light probes at
/// `probes` (world positions), registered as a loaded scene's is (`load`'s answer); cached
/// under `<project>/Library/Unbound/RealtimeGI` by a hash of every input and the format
/// version.
pub fn bake(inputs: &[BakeInput], probes: &[[f32; 3]]) -> Result<Json> {
    let path = crate::state::state()
        .project_root
        .join("Library/Unbound/RealtimeGI")
        .join(format!("{}.bin", bake_key(inputs, probes)));
    let baked = match read_baked(&path) {
        Some(baked) => baked,
        None => {
            let baked = bake_fresh(inputs, probes)?;
            if let Err(e) = write_baked(&path, &baked) {
                tracing::debug!(
                    "caching precomputed realtime GI in {}: {e:#}",
                    path.display()
                );
            }
            baked
        }
    };
    let ranges: Vec<([u8; 16], usize, usize)> = baked
        .data
        .probe_sets
        .iter()
        .map(|set| (set.hash, 0, set.probes.len()))
        .collect();
    let mut answer = register(baked.data, &ranges)?;
    answer["refused"] = baked
        .refused
        .iter()
        .map(|((object, prefab), error)| json!({ "object": object, "prefab": prefab, "error": error }))
        .collect();
    Ok(answer)
}

/// The cache key of a bake: every input, the probes, and the versions of everything that makes
/// its output.
fn bake_key(inputs: &[BakeInput], probes: &[[f32; 3]]) -> String {
    let mut key = blake3::Hasher::new();
    key.update(&BAKE_CACHE_VERSION.to_le_bytes());
    key.update(&unbound_gi::precompute::FORMAT_VERSION.to_le_bytes());
    key.update(&UV_CACHE_VERSION.to_le_bytes());
    key.update(&(inputs.len() as u64).to_le_bytes());
    for input in inputs {
        input.hash_into(&mut key);
    }
    key.update(&(probes.len() as u64).to_le_bytes());
    key.update(bytes_of(probes));
    key.finalize().to_hex().to_string()
}

/// A renderer as the bake places it: world positions and normals, realtime lightmap UVs and
/// world bounds.
struct Placed {
    positions: Vec<[f32; 3]>,
    normals: Vec<[f32; 3]>,
    uvs: unbound_gi::realtime_uv::Output,
    bounds: ([f32; 3], [f32; 3]),
}

/// A normal through a matrix's rotation and scale (column by column): by its cofactors, the
/// inverse transpose up to the determinant's size and kept when the matrix flattens one axis
/// (a plane scaled to no height keeps its normal; mirrored too, its sign is then a choice of
/// Unbound's: the Editor's is not measured); its sign follows the determinant's (a mirror
/// turns it); zero when the matrix flattens two axes.
fn turned_normal(m: &[f32; 9], n: [f32; 3]) -> [f32; 3] {
    let c = |i: usize, j: usize| m[3 * j + i];
    let cofactor = |i: usize, j: usize| {
        let (i1, i2) = ((i + 1) % 3, (i + 2) % 3);
        let (j1, j2) = ((j + 1) % 3, (j + 2) % 3);
        c(i1, j1) * c(i2, j2) - c(i1, j2) * c(i2, j1)
    };
    let det: f32 = (0..3).map(|j| c(0, j) * cofactor(0, j)).sum();
    let sign = if det < 0.0 { -1.0 } else { 1.0 };
    let turned: [f32; 3] =
        [0, 1, 2].map(|i| sign * (0..3).map(|j| cofactor(i, j) * n[j]).sum::<f32>());
    let length = (turned[0] * turned[0] + turned[1] * turned[1] + turned[2] * turned[2]).sqrt();
    if length > 0.0 && length.is_finite() {
        turned.map(|v| v / length)
    } else {
        [0.0; 3]
    }
}

/// `input` placed in the world, its realtime lightmap UVs made as the managed side makes them.
fn place(input: &BakeInput) -> Result<Placed> {
    let m = &input.local_to_world;
    let positions: Vec<[f32; 3]> = input.positions.iter().map(|&p| point(m, p)).collect();
    let rotation_scale = [m[0], m[1], m[2], m[4], m[5], m[6], m[8], m[9], m[10]];
    let normals = if input.normals.len() == input.positions.len() {
        input
            .normals
            .iter()
            .map(|&n| turned_normal(&rotation_scale, n))
            .collect()
    } else {
        vec![[0.0; 3]; input.positions.len()]
    };
    let placed;
    let uv_input = match &input.automatic {
        None => UvInput {
            positions: &positions,
            uvs: &input.uvs,
            triangles: &input.triangles,
            pixel_size: input.pixel_size,
            min_chart_texels: input.min_chart_texels,
            charts: unbound_gi::realtime_uv::Charts::Preserved,
        },
        Some((matrix, params)) => {
            placed = unbound_gi::instance::apply(matrix, &input.positions, &input.normals)
                .ok_or_else(|| anyhow::anyhow!("the instance's matrix {matrix:?} is singular"))?;
            UvInput {
                positions: &placed.0,
                uvs: &input.uvs,
                triangles: &input.triangles,
                pixel_size: input.pixel_size,
                min_chart_texels: input.min_chart_texels,
                charts: unbound_gi::realtime_uv::Charts::Automatic {
                    normals: &placed.1,
                    params: *params,
                },
            }
        }
    };
    let uvs = unbound_gi::realtime_uv::realtime_uvs(&uv_input)?;
    // The mesh's bounds through the matrix, as Renderer.bounds.
    let (mut lo, mut hi) = ([f32::INFINITY; 3], [f32::NEG_INFINITY; 3]);
    let (mut llo, mut lhi) = ([f32::INFINITY; 3], [f32::NEG_INFINITY; 3]);
    for p in &input.positions {
        for a in 0..3 {
            llo[a] = llo[a].min(p[a]);
            lhi[a] = lhi[a].max(p[a]);
        }
    }
    for corner in 0..8 {
        let p = [0, 1, 2].map(|a| if corner >> a & 1 == 0 { llo[a] } else { lhi[a] });
        let w = point(m, p);
        for a in 0..3 {
            lo[a] = lo[a].min(w[a]);
            hi[a] = hi[a].max(w[a]);
        }
    }
    Ok(Placed {
        positions,
        normals,
        uvs,
        bounds: (lo, hi),
    })
}

/// The bake of `inputs`: a renderer that cannot be placed (its realtime lightmap UVs cannot be
/// made, say) is left out and listed, as is one of no triangles.
fn bake_fresh(
    inputs: &[BakeInput],
    probes: &[[f32; 3]],
) -> Result<unbound_gi::precompute::bake::Baked> {
    let mut refused = Vec::new();
    let mut taken = Vec::with_capacity(inputs.len());
    for input in inputs {
        if input.triangles.is_empty() || input.positions.is_empty() {
            refused.push(((input.object, input.prefab), "no triangles".to_string()));
            continue;
        }
        match place(input) {
            Ok(placed) => taken.push((input, placed)),
            Err(e) => refused.push(((input.object, input.prefab), format!("{e:#}"))),
        }
    }
    let renderers: Vec<unbound_gi::precompute::bake::BakeRenderer> = taken
        .iter()
        .map(|(input, made)| unbound_gi::precompute::bake::BakeRenderer {
            id: (input.object, input.prefab),
            positions: &made.positions,
            normals: &made.normals,
            triangles: &input.triangles,
            uvs: &made.uvs,
            bounds: made.bounds,
            cluster_size: input.cluster_size,
            modelling_tolerance: input.modelling_tolerance,
            group: input.group,
            settings: input.settings,
        })
        .collect();
    let mut baked = unbound_gi::precompute::bake::bake(&renderers, probes)?;
    refused.append(&mut baked.refused);
    baked.refused = refused;
    Ok(baked)
}

/// The cache entry at `path`: a checksum of the rest, then the encoded data; `None` when there
/// is none or it is damaged (it is made again).
fn read_baked(path: &Path) -> Option<unbound_gi::precompute::bake::Baked> {
    let bytes = std::fs::read(path).ok()?;
    let (sum, rest) = bytes.split_at_checked(32)?;
    if blake3::hash(rest).as_bytes() != sum {
        return None;
    }
    unbound_gi::precompute::bake::Baked::decode(rest).ok()
}

fn write_baked(path: &Path, baked: &unbound_gi::precompute::bake::Baked) -> Result<()> {
    let encoded = baked.encode();
    let mut bytes = blake3::hash(&encoded).as_bytes().to_vec();
    bytes.extend_from_slice(&encoded);
    write_atomic(path, &bytes)
}

/// Writes a cache entry whole or not at all: to a file of its own, then renamed into place.
fn write_atomic(path: &Path, bytes: &[u8]) -> Result<()> {
    static NEXT_TEMP: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
    let dir = path.parent().context("no cache folder")?;
    std::fs::create_dir_all(dir)?;
    let n = NEXT_TEMP.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
    let temp = path.with_extension(format!("tmp{}-{n}", std::process::id()));
    std::fs::write(&temp, bytes)?;
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
            falloff: FALLOFF_LEGACY,
            ..GiLight::default()
        }
    }

    #[test]
    fn a_scene_updates_until_converged_and_again_after_a_change() {
        let reply = register(room(), &[]).unwrap();
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
        // A render pipeline's inverse-squared falloff lights the scene, and differently from
        // the legacy one; the other falloff types fail loudly.
        let lights = [point([0.0, 2.0, 0.0])];
        assert!((0..500).any(|_| update(handle, &frame, &lights).unwrap() & UPDATE_CONVERGED != 0));
        let mut legacy = vec![0u16; 4 * 64 * 64];
        read_atlas(handle, 0, &mut legacy).unwrap();
        let inverse = [GiLight {
            falloff: FALLOFF_INVERSE_SQUARED,
            ..lights[0]
        }];
        assert!(
            (0..500).any(|_| update(handle, &frame, &inverse).unwrap() & UPDATE_CONVERGED != 0)
        );
        let mut lit = vec![0u16; 4 * 64 * 64];
        read_atlas(handle, 0, &mut lit).unwrap();
        assert!(lit.chunks_exact(4).any(|t| t[0] != 0));
        assert!(lit != legacy);
        for falloff in [1, 2, 4, 9] {
            let error = update(
                handle,
                &frame,
                &[GiLight {
                    falloff,
                    ..lights[0]
                }],
            )
            .unwrap_err()
            .to_string();
            assert!(error.contains("falloff type"), "{error}");
        }
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
        let reply = register(data, &ranges).unwrap();
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
        let reply = register(room(), &[]).unwrap();
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
        let reply = register(data, &ranges).unwrap();
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
        let handle = register(room(), &[]).unwrap()["handle"].as_u64().unwrap();
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
        let handle = register(room(), &[]).unwrap()["handle"].as_u64().unwrap();
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
    fn a_scene_without_readable_blocks_draws_its_lightmap_in_the_directional_mode() {
        // A system whose blocks could not be read: the scene says why, updates in the
        // directional mode all the same, has no directionality and the lightmap of the scene
        // as it is read whole in the other mode.
        let mut data = room();
        let transport = &mut data.systems[0].transport;
        transport.block_error = Some("no blocks".into());
        for segment in &mut transport.segments {
            segment.block_normals.clear();
            segment.block_directions.clear();
        }
        // A copy of a texel onto itself: the reply says that the scene has copies.
        let texel = transport.segments[0].texels[0];
        transport.segments[0].copies.push((texel, texel));
        let reply = register(data, &[]).unwrap();
        assert_eq!(
            reply["directionality"],
            json!({ "error": "no blocks", "copies": true })
        );
        let lights = [point([0.0, 2.0, 0.0])];
        let lightmap = |handle: u64, directional: i32| {
            set_material(handle, 0, 0, &vec![200u8; 4 * 52 * 52]).unwrap();
            let frame = GiFrame {
                indirect_scale: 1.0,
                synchronous: 1,
                budget_ms: 2.0,
                update_threshold: 1.0,
                directional,
            };
            let converged =
                (0..1000).any(|_| update(handle, &frame, &lights).unwrap() & UPDATE_CONVERGED != 0);
            assert!(converged);
            let mut light = vec![0u16; 4 * 64 * 64];
            read_atlas(handle, 0, &mut light).unwrap();
            light
        };
        let handle = reply["handle"].as_u64().unwrap();
        let light = lightmap(handle, 1);
        let mut texels = vec![0u8; 4 * 64 * 64];
        assert_eq!(read_directionality(handle, 0, &mut texels).unwrap(), 0);
        release(handle);
        let whole = register(room(), &[]).unwrap()["handle"].as_u64().unwrap();
        assert!(light.chunks_exact(4).any(|l| l[0] != 0));
        assert!(light == lightmap(whole, 0));
        release(whole);
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
        let reply = register(data, &[]).unwrap();
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
            width: 16,
            height: 12,
            uvs: vec![[0.25, 0.5], [0.75, 1.0]],
            // A full chart and one of more blocks than a word holds, with a hole.
            charts: vec![
                unbound_gi::realtime_uv::PlacedChart {
                    rect: [0, 0, 4, 4],
                    blocks: vec![true; 4],
                },
                unbound_gi::realtime_uv::PlacedChart {
                    rect: [4, 0, 12, 12],
                    blocks: (0..36).map(|b| b % 7 != 3).collect(),
                },
            ],
        };
        write_cached(&path, &output).unwrap();
        assert_eq!(read_cached(&path, 2), Some(output.clone()));
        // A chart whose blocks are not its rectangle's is not written.
        let mut wrong = output.clone();
        wrong.charts[0].blocks.pop();
        assert!(write_cached(&dir.join("wrong.bin"), &wrong).is_err());
        // An entry that passes the check is used as it is, without making a layout.
        let passes = |o: &unbound_gi::realtime_uv::Output| {
            ensure!(o.width == 16, "a layout {} texels wide", o.width);
            Ok(())
        };
        let used = cached_or_fresh(&path, 2, || bail!("made anew"), passes).unwrap();
        assert_eq!(used, output);
        // One that fails it (a damaged entry) gives way to a layout made anew, which replaces it.
        let mut damaged = output.clone();
        damaged.width = 12;
        write_cached(&path, &damaged).unwrap();
        let made = cached_or_fresh(&path, 2, || Ok(output.clone()), passes).unwrap();
        assert_eq!(made, output);
        assert_eq!(read_cached(&path, 2), Some(output.clone()));
        // A new layout that fails the check is an error and is not cached.
        write_cached(&path, &damaged).unwrap();
        let error = cached_or_fresh(&path, 2, || Ok(damaged.clone()), passes).unwrap_err();
        assert!(error.to_string().contains("12 texels wide"), "{error}");
        assert_eq!(read_cached(&path, 2), Some(damaged));
        // Another vertex count or a truncated entry is not used.
        assert_eq!(read_cached(&path, 3), None);
        let bytes = std::fs::read(&path).unwrap();
        std::fs::write(&path, &bytes[..bytes.len() - 1]).unwrap();
        assert_eq!(read_cached(&path, 2), None);
        std::fs::remove_dir_all(dir).unwrap();
    }

    /// A box of `size` at `at` (a cube's 24 vertices and 12 triangles, its lightmap UVs each
    /// face's own square in a 3 x 2 grid) as the bake takes a renderer.
    fn box_input(object: i64, size: [f32; 3], at: [f32; 3]) -> BakeInput {
        let (mut positions, mut normals, mut uvs, mut triangles) =
            (Vec::new(), Vec::new(), Vec::new(), Vec::new());
        for (f, (n, u, v)) in [
            ([1.0, 0.0, 0.0], [0.0, 0.0, -1.0], [0.0, 1.0, 0.0]),
            ([-1.0, 0.0, 0.0], [0.0, 0.0, 1.0], [0.0, 1.0, 0.0]),
            ([0.0, 1.0, 0.0], [1.0, 0.0, 0.0], [0.0, 0.0, -1.0]),
            ([0.0, -1.0, 0.0], [1.0, 0.0, 0.0], [0.0, 0.0, 1.0]),
            ([0.0, 0.0, 1.0], [1.0, 0.0, 0.0], [0.0, 1.0, 0.0]),
            ([0.0, 0.0, -1.0], [-1.0, 0.0, 0.0], [0.0, 1.0, 0.0]),
        ]
        .into_iter()
        .enumerate()
        {
            let base = positions.len() as u32;
            for (a, b) in [(-1.0f32, -1.0f32), (1.0, -1.0), (1.0, 1.0), (-1.0, 1.0)] {
                positions.push([0, 1, 2].map(|k| 0.5 * (n[k] + a * u[k] + b * v[k])));
                normals.push(n);
                let cell = [(f % 3) as f32, (f / 3) as f32];
                uvs.push([
                    (cell[0] + 0.1 + 0.4 * (a + 1.0)) / 3.0,
                    (cell[1] + 0.1 + 0.4 * (b + 1.0)) / 2.0,
                ]);
            }
            triangles.push([base, base + 1, base + 2]);
            triangles.push([base, base + 2, base + 3]);
        }
        let mut local_to_world = [0.0; 16];
        for k in 0..3 {
            local_to_world[5 * k] = size[k];
            local_to_world[12 + k] = at[k];
        }
        local_to_world[15] = 1.0;
        BakeInput {
            object,
            prefab: 0,
            positions,
            normals,
            uvs,
            triangles,
            local_to_world,
            pixel_size: 0.5,
            min_chart_texels: 4,
            automatic: None,
            cluster_size: 1.0,
            modelling_tolerance: 0.01,
            group: 0,
            settings: unbound_gi::precompute::transport::DEFAULT_SETTINGS,
        }
    }

    #[test]
    fn a_baked_scene_is_cached_and_a_damaged_entry_is_not_used() {
        // An open box of a floor and two walls, and a block on the floor.
        let inputs = [
            box_input(1, [4.0, 0.2, 4.0], [0.0, -0.1, 0.0]),
            box_input(2, [0.2, 3.0, 4.0], [-2.1, 1.5, 0.0]),
            box_input(3, [4.0, 3.0, 0.2], [0.0, 1.5, 2.1]),
            box_input(4, [1.0, 1.0, 1.0], [0.5, 0.5, -0.5]),
        ];
        let baked = bake_fresh(&inputs, &[[0.0, 1.0, 0.0]]).unwrap();
        assert!(baked.refused.is_empty());
        let data = &baked.data;
        assert_eq!(data.systems.len(), 1);
        assert_eq!(data.renderers.len(), 4);
        assert_eq!(data.probe_sets.len(), 1);
        let dir = std::env::temp_dir().join(format!("unbound-bake-{}", std::process::id()));
        let path = dir.join("scene.bin");
        write_baked(&path, &baked).unwrap();
        assert_eq!(read_baked(&path).unwrap().encode(), baked.encode());
        // A changed byte or a truncated entry is not used (it is made again).
        let mut bytes = std::fs::read(&path).unwrap();
        let last = bytes.len() - 1;
        bytes[last] ^= 1;
        std::fs::write(&path, &bytes).unwrap();
        assert!(read_baked(&path).is_none());
        std::fs::write(&path, &bytes[..40]).unwrap();
        assert!(read_baked(&path).is_none());
        std::fs::remove_dir_all(dir).unwrap();
        // A renderer of no triangles, and one whose UVs cannot be made (a vertex that is not
        // finite), are left out and listed by their ids; the others are baked.
        let mut empty = box_input(6, [1.0; 3], [0.0; 3]);
        empty.triangles.clear();
        let mut broken = box_input(7, [1.0; 3], [0.0, 3.0, 0.0]);
        broken.positions[0][0] = f32::NAN;
        let mut more: Vec<BakeInput> = inputs.into_iter().collect();
        more.push(empty);
        more.push(broken);
        let baked = bake_fresh(&more, &[]).unwrap();
        assert_eq!(baked.data.renderers.len(), 4);
        let refused: Vec<i64> = baked.refused.iter().map(|r| r.0.0).collect();
        assert_eq!(refused, vec![6, 7]);
        assert!(
            baked.refused[1].1.contains("non-finite"),
            "{}",
            baked.refused[1].1
        );
        // Other irradiance settings change the key (and the transport they make).
        let mut fewer = box_input(1, [4.0, 0.2, 4.0], [0.0, -0.1, 0.0]);
        fewer.settings.budget = 32;
        assert_ne!(
            bake_key(&[fewer], &[]),
            bake_key(&[box_input(1, [4.0, 0.2, 4.0], [0.0, -0.1, 0.0])], &[])
        );
    }

    #[test]
    fn a_renderer_is_placed_through_its_matrix() {
        // A box of 2 x 1 x 1 turned 90 degrees about Y and moved to (5, 1, -3): its +x face
        // faces -z, the vertex (0.5, 0.5, 0.5) of that face lies at (5.5, 1.5, -4), and its
        // bounds are 1 x 1 x 2 around (5, 1, -3).
        let mut input = box_input(1, [1.0; 3], [0.0; 3]);
        input.local_to_world = [
            0.0, 0.0, -2.0, 0.0, // x to -2z
            0.0, 1.0, 0.0, 0.0, // y
            1.0, 0.0, 0.0, 0.0, // z to x
            5.0, 1.0, -3.0, 1.0,
        ];
        let placed = place(&input).unwrap();
        let v = (0..input.positions.len())
            .find(|&i| input.positions[i] == [0.5; 3] && input.normals[i] == [1.0, 0.0, 0.0])
            .unwrap();
        assert_eq!(placed.positions[v], [5.5, 1.5, -4.0]);
        assert_eq!(placed.normals[v], [0.0, 0.0, -1.0]);
        assert_eq!(placed.bounds, ([4.5, 0.5, -4.0], [5.5, 1.5, -2.0]));
        // A mirror turns the normals with it; a plane scaled to no height keeps its up normal
        // (the bake takes it), one flattened along two axes has none.
        let mut mirrored = box_input(2, [-1.0, 1.0, 1.0], [0.0; 3]);
        mirrored.normals = vec![[1.0, 0.0, 0.0]; mirrored.positions.len()];
        assert_eq!(place(&mirrored).unwrap().normals[0], [-1.0, 0.0, 0.0]);
        let mut flat = box_input(3, [10.0, 0.0, 10.0], [0.0; 3]);
        flat.normals = vec![[0.0, 1.0, 0.0]; flat.positions.len()];
        assert_eq!(place(&flat).unwrap().normals[0], [0.0, 1.0, 0.0]);
        assert_eq!(
            turned_normal(
                &[10.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
                [0.0, 1.0, 0.0]
            ),
            [0.0; 3]
        );
    }

    #[test]
    fn a_bakes_key_covers_its_inputs() {
        let inputs = [box_input(1, [4.0, 0.2, 4.0], [0.0; 3])];
        let key = bake_key(&inputs, &[]);
        assert_eq!(key, bake_key(&inputs, &[]));
        let mut moved = box_input(1, [4.0, 0.2, 4.0], [0.0; 3]);
        moved.local_to_world[13] = 0.001;
        assert_ne!(key, bake_key(&[moved], &[]));
        let mut grouped = box_input(1, [4.0, 0.2, 4.0], [0.0; 3]);
        grouped.group = 1;
        assert_ne!(key, bake_key(&[grouped], &[]));
        assert_ne!(key, bake_key(&inputs, &[[0.0, 1.0, 0.0]]));
    }
}
