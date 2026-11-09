//! One system's runtime update (the model of docs/REALTIME_GI.md): direct light per cluster,
//! cluster radiance, the cluster tree, the transport into the realtime lightmap texels and the
//! bounce resampled from them. All buffers are allocated once per system; an update allocates
//! nothing.
use crate::data::{ABSENT_ID, ENVIRONMENT_ID, ENVIRONMENT_VALUES, InputSamples, System};
use crate::visibility::Visibility;
use anyhow::{Result, bail};
use std::sync::Arc;
pub use unbound_assets::color::gamma_to_linear;

/// The transport's weights: a 16-bit reference weight times an 8-bit texel weight.
const TRANSPORT_SCALE: f32 = 1.0 / (65535.0 * 255.0);
/// The cluster tree's 8-bit child weights.
const TREE_SCALE: f32 = 1.0 / 255.0;
/// The built-in pipeline's legacy point light falloff over x = distance / range:
/// 1 / (1 + 25 x^2), faded linearly to 0 between x^2 = 0.64 and 1.
const LEGACY_FALLOFF_K: f32 = 25.0;
const LEGACY_FADE_START: f32 = 0.64;
/// A segment is solved again when the change of its inputs since its last solve, relative to
/// their current brightness plus this, reaches `DynamicGI.updateThreshold` (a percentage); the
/// small term keeps black segments from dividing by zero. Both constants match the Unity
/// 6000.0.71f1 Editor, tests/projects/realtimegi.
const CHANGE_EPSILON: f32 = 1e-4 / 127.0;
/// An indirect scale change by more than this (2^-23, about one float step at 1) solves every
/// segment again.
const SCALE_CHANGE: f32 = 1.0 / 8_388_608.0;
/// `DynamicGI.updateThreshold`'s default, in percent.
pub const DEFAULT_UPDATE_THRESHOLD: f32 = 1.0;

/// A realtime light as precomputed realtime GI sees it in the built-in pipeline.
#[derive(Debug, Clone, Copy)]
pub struct Light {
    /// Linear: the light's color times its intensity and bounce intensity (in a project without
    /// linear light intensity, scaled before the conversion to linear).
    pub color: [f32; 3],
    pub kind: LightKind,
}

#[derive(Debug, Clone, Copy)]
pub enum LightKind {
    /// Not shadowed, with the legacy distance falloff (other pipelines' falloffs are not
    /// supported yet).
    Point { position: [f32; 3], range: f32 },
    /// As a point light, within its cone.
    Spot {
        position: [f32; 3],
        range: f32,
        cone: SpotCone,
    },
    /// Shadowed by the system's precomputed visibility: `to_light` is the unit direction toward
    /// the light (its forward vector negated).
    Directional { to_light: [f32; 3] },
}

/// Steps of a spot light's angular table between the cosine of half its angle and 1.
const SPOT_STEPS: usize = 31;
/// The inner cone a spot light's angular falloff starts at: half its angle's tangent times this
/// (46/64, the inner cone `LightmapperUtils.ExtractInnerCone` and `Light.innerSpotAngle`'s
/// default use; the light's own inner angle is not used).
const SPOT_INNER_TANGENT: f32 = 0.71875;
/// Keeps a degenerate cone's divisions finite.
const SPOT_MIN_STEP: f32 = 1e-6;

/// A spot light's cone: its angular falloff is the square of the position of the cosine of the
/// angle from the light's direction between those of the outer and inner half angles (1 inside
/// the inner cone, 0 outside the outer one), tabulated at `SPOT_STEPS` steps from the outer
/// cosine to 1 and read at the step at or below the point's cosine, not interpolated. Matches the
/// Unity 6000.0.71f1 Editor, tests/projects/realtimegi (`room-spot`: the direct light per cluster
/// within 0.6%).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SpotCone {
    /// The light's direction times the table steps per unit of cosine.
    axis: [f32; 3],
    /// Table position of a cosine of 0.
    offset: f32,
    /// The angular falloff at each table position; 0 at position 0 (outside the cone).
    table: [f32; SPOT_STEPS + 2],
}

impl SpotCone {
    /// `direction`: the light's unit forward vector; `spot_angle`: `Light.spotAngle` in degrees.
    pub fn new(direction: [f32; 3], spot_angle: f32) -> Self {
        let pi = std::f32::consts::PI;
        // Half of the default inner angle, in radians: atan(tan(half the angle) * 0.71875).
        let inner = ((spot_angle * 0.5 / 360.0) * 2.0 * pi).tan() * SPOT_INNER_TANGENT;
        let inner_degrees = inner.atan() * 2.0 * 0.5 / pi * 360.0;
        let half_angle_cos = |degrees: f32| (0.5 * ((degrees / 360.0) * 2.0 * pi)).cos();
        let (outer, inner) = (half_angle_cos(spot_angle), half_angle_cos(inner_degrees));
        let step = ((1.0 - outer) / SPOT_STEPS as f32).max(SPOT_MIN_STEP);
        let scale = 1.0 / step;
        let inverse_width = 1.0 / (inner - outer).max(SPOT_MIN_STEP);
        let mut table = [0.0; SPOT_STEPS + 2];
        let mut cos = outer;
        for v in &mut table[1..] {
            *v = if cos < inner {
                let t = (cos - outer) * inverse_width;
                t * t
            } else {
                1.0
            };
            cos += step;
        }
        Self {
            axis: direction.map(|v| v * scale),
            offset: 1.0 - outer * scale,
            table,
        }
    }

    /// The angular falloff of a point at unit direction `to_point` from the light.
    fn falloff(&self, to_point: [f32; 3]) -> f32 {
        let a = &self.axis;
        let t = a[0] * to_point[0] + a[1] * to_point[1] + a[2] * to_point[2] + self.offset;
        // NaN (a degenerate direction) reads position 0.
        self.table[(t.clamp(0.0, (SPOT_STEPS + 1) as f32) as usize).min(SPOT_STEPS + 1)]
    }
}

fn legacy_falloff(x2: f32) -> f32 {
    if x2 > 1.0 {
        return 0.0;
    }
    let a = 1.0 / (1.0 + LEGACY_FALLOFF_K * x2);
    if x2 >= LEGACY_FADE_START {
        a * (1.0 - (x2 - LEGACY_FADE_START) / (1.0 - LEGACY_FADE_START))
    } else {
        a
    }
}

/// Number of samples of a light's distance falloff.
const FALLOFF_SAMPLES: usize = 16;

/// A light's distance falloff as precomputed realtime GI evaluates it: sampled at
/// x = distance / range where the table position t = (N - 1) / (1 + (N - 2) x^2) is a whole
/// number, and interpolated linearly in t. The last sample (x = 0) is 1, the first two (x >= 1)
/// are 0. Matches the Unity 6000.0.71f1 Editor, tests/projects/realtimegi (the direct light per
/// cluster within 0.05%).
struct FalloffTable([f32; FALLOFF_SAMPLES]);

impl FalloffTable {
    fn new(falloff: impl Fn(f32) -> f32) -> Self {
        let n = FALLOFF_SAMPLES as f32;
        let mut values = [0.0; FALLOFF_SAMPLES];
        for (i, v) in values
            .iter_mut()
            .enumerate()
            .take(FALLOFF_SAMPLES - 1)
            .skip(2)
        {
            // x^2 at table position i.
            *v = falloff(((n - 1.0) / i as f32 - 1.0) / (n - 2.0));
        }
        values[FALLOFF_SAMPLES - 1] = 1.0;
        Self(values)
    }

    /// The falloff at `x2` = (distance / range)^2.
    fn eval(&self, x2: f32) -> f32 {
        let n = FALLOFF_SAMPLES as f32;
        let t = ((n - 1.0) / (1.0 + (n - 2.0) * x2)).clamp(0.0, n - 1.0);
        let i = (t as usize).min(FALLOFF_SAMPLES - 2);
        let (a, b) = (self.0[i], self.0[i + 1]);
        a + (b - a) * (t - i as f32)
    }
}

static LEGACY_FALLOFF: std::sync::LazyLock<FalloffTable> =
    std::sync::LazyLock::new(|| FalloffTable::new(legacy_falloff));

/// RGB9E5 encoding (5-bit shared exponent, 9-bit mantissas), the realtime lightmap's format.
pub fn encode_rgb9e5(rgb: [f32; 3]) -> u32 {
    const MANTISSA: i32 = 9;
    const BIAS: i32 = 15;
    const MAX: f32 = 65408.0; // (511 / 512) * 2^16
    let c = rgb.map(|v| if v.is_nan() { 0.0 } else { v.clamp(0.0, MAX) });
    let max = c[0].max(c[1]).max(c[2]);
    if max <= 0.0 {
        return 0;
    }
    let mut exponent = (max.log2().floor() as i32).max(-BIAS - 1) + 1 + BIAS;
    let mut denominator = 2f32.powi(exponent - BIAS - MANTISSA);
    if (max / denominator + 0.5).floor() as i32 == 1 << MANTISSA {
        denominator *= 2.0;
        exponent += 1;
    }
    let m = c.map(|v| ((v / denominator + 0.5).floor() as u32).min(511));
    m[0] | (m[1] << 9) | (m[2] << 18) | ((exponent as u32) << 27)
}

pub fn decode_rgb9e5(v: u32) -> [f32; 3] {
    let scale = 2f32.powi((v >> 27) as i32 - 15 - 9);
    [v & 0x1FF, (v >> 9) & 0x1FF, (v >> 18) & 0x1FF].map(|m| m as f32 * scale)
}

/// Where a transport dependency's values come from.
#[derive(Debug, Clone, Copy, PartialEq)]
enum Source {
    Environment,
    Own,
    /// Another system of the scene (its index among them).
    System(usize),
    Absent,
}

/// The input values of the scene's other systems a system's update reads: their cluster, tree and
/// constant values of their last update and how much each changed in it.
type OtherValues<'a> = &'a dyn Fn(usize) -> (&'a [[f32; 3]], &'a [f32]);

/// One system's state between updates.
pub struct SystemSolver {
    system: Arc<System>,
    sources: Vec<Source>,
    direct: Vec<[f32; 3]>,
    /// Scratch space of directional lights: visible points per cluster, a decoded visibility
    /// stream.
    visible: Vec<u32>,
    stream: Vec<u8>,
    albedo2: Vec<[f32; 3]>,
    emission: Vec<[f32; 3]>,
    /// The emission texture (linear, system resolution) and the renderers' own emissions.
    emission_texels: Vec<[f32; 3]>,
    renderer_emission: Vec<Option<[f32; 3]>>,
    /// The clusters' emission is computed from the two above before the next update.
    emission_dirty: bool,
    bounce: Vec<[f32; 3]>,
    /// The cluster, tree and constant values of the last update, and how much each changed in
    /// it: the mean of its channels' absolute changes.
    values: Vec<[f32; 3]>,
    changes: Vec<f32>,
    environment: Vec<[f32; 3]>,
    /// The environment the last update used, and the change of each value in it.
    environment_used: Vec<[f32; 3]>,
    environment_changes: Vec<f32>,
    gathered: Vec<[f32; 3]>,
    /// Per segment, the weighted change of its inputs since it was last solved (negative right
    /// after a solve).
    pending: Vec<f32>,
    /// Every segment is solved in the next update (the first one, or the scale changed).
    solve_all: bool,
    /// `DynamicGI.updateThreshold` as a fraction.
    threshold: f32,
    /// Output texels (system resolution, row-major) as drawn: RGB9E5, indirect scale applied.
    encoded: Vec<u32>,
    indirect_scale: f32,
    /// The scale the drawn texels were solved with.
    solved_scale: f32,
}

/// The mean of the channels' absolute changes from `old` to `new`.
fn mean_change(new: [f32; 3], old: [f32; 3]) -> f32 {
    ((new[0] - old[0]).abs() + (new[1] - old[1]).abs() + (new[2] - old[2]).abs()) / 3.0
}

fn mul_add(acc: &mut [f32; 3], w: f32, v: [f32; 3]) {
    for k in 0..3 {
        acc[k] += w * v[k];
    }
}

/// The direct light of `lights` on each cluster: the mean over its points of the cosine of the
/// point's normal (as stored, not renormalized) with the direction to the light, times the
/// light's color and, for point and spot lights, its sampled distance falloff and angular
/// falloff (spot lights). A directional light lights only the points of a cluster its precomputed
/// visibility counts as seeing it, the first ones in order (`visible` and `stream` are scratch
/// space of a value per cluster).
fn shade_lights(
    input: &InputSamples,
    visibility: &std::result::Result<Visibility, String>,
    lights: &[Light],
    visible: &mut [u32],
    stream: &mut Vec<u8>,
    out: &mut [[f32; 3]],
) -> Result<()> {
    let falloff = &*LEGACY_FALLOFF;
    let points = |c: usize| {
        (
            input.cluster_points[c] as usize,
            input.cluster_points[c + 1] as usize,
        )
    };
    for (c, out) in out.iter_mut().enumerate() {
        let (first, end) = points(c);
        let mut sum = [0.0f32; 3];
        for p in first..end {
            let (pos, n) = (input.positions[p], input.normals[p]);
            for light in lights {
                let (position, range, cone) = match &light.kind {
                    LightKind::Point { position, range } => (position, *range, None),
                    LightKind::Spot {
                        position,
                        range,
                        cone,
                    } => (position, *range, Some(cone)),
                    LightKind::Directional { .. } => continue,
                };
                let d = [0, 1, 2].map(|k| position[k] - pos[k]);
                let length2 = d[0] * d[0] + d[1] * d[1] + d[2] * d[2];
                if length2 <= 0.0 || range <= 0.0 {
                    continue;
                }
                let inverse = 1.0 / length2.sqrt();
                let cos = (n[0] * d[0] + n[1] * d[1] + n[2] * d[2]) * inverse;
                if cos <= 0.0 {
                    continue;
                }
                let mut w = cos * falloff.eval(length2 / (range * range));
                if let Some(cone) = cone {
                    w *= cone.falloff(d.map(|v| -v * inverse));
                }
                mul_add(&mut sum, w, light.color);
            }
        }
        *out = sum;
    }
    for light in lights {
        let LightKind::Directional { to_light: l } = light.kind else {
            continue;
        };
        let visibility = visibility.as_ref().map_err(|e| {
            anyhow::anyhow!("a directional light without precomputed visibility: {e}")
        })?;
        visibility.visible_points(
            l,
            |c| {
                let (first, end) = points(c);
                (end - first) as u32
            },
            stream,
            visible,
        )?;
        for (c, out) in out.iter_mut().enumerate() {
            let (first, end) = points(c);
            let mut w = 0.0;
            for n in &input.normals[first..(first + visible[c] as usize).min(end)] {
                w += (n[0] * l[0] + n[1] * l[1] + n[2] * l[2]).max(0.0);
            }
            mul_add(out, w, light.color);
        }
    }
    for (c, out) in out.iter_mut().enumerate() {
        let (first, end) = points(c);
        let count = (end - first).max(1) as f32;
        *out = out.map(|v| v / count);
    }
    Ok(())
}

/// Updates system `index` of a scene's `systems` ([`SystemSolver::new_in_scene`]), reading the
/// other systems' values of their last updates. Returns the number of segments solved.
pub fn update_in(systems: &mut [SystemSolver], index: usize) -> usize {
    let (before, rest) = systems.split_at_mut(index);
    let (system, after) = rest
        .split_first_mut()
        .expect("a system index within the scene");
    let other = |other: usize| -> &SystemSolver {
        if other < index {
            &before[other]
        } else {
            &after[other - index - 1]
        }
    };
    // `systems` must be the scene the system was made in, in the same order.
    for (d, source) in system.sources.iter().enumerate() {
        if let Source::System(s) = *source {
            assert!(
                other(s).system.input_hash == system.system.transport.dependencies[d],
                "update_in with systems of another scene or order"
            );
        }
    }
    let others = |s: usize| -> (&[[f32; 3]], &[f32]) {
        let s = other(s);
        (&s.values, &s.changes)
    };
    system.update_with(&others)
}

impl SystemSolver {
    /// `linear_space`: the project's color space is linear (gamma-space projects are not measured
    /// and fail). Fails for transports that reference other systems (not supported yet).
    pub fn new(system: Arc<System>, linear_space: bool) -> Result<Self> {
        let scene = [system.clone()];
        Self::new_in_scene(system, linear_space, &scene)
    }

    /// A system of a scene whose systems are `scene` (in order; it is one of them): its
    /// transport may reference the others' values. A reference to a system not among them
    /// (another scene's) fails.
    pub fn new_in_scene(
        system: Arc<System>,
        linear_space: bool,
        scene: &[Arc<System>],
    ) -> Result<Self> {
        if !linear_space {
            bail!("precomputed realtime GI in a gamma-space project is not supported yet");
        }
        let sources = system
            .transport
            .dependencies
            .iter()
            .map(|id| {
                Ok(if *id == ENVIRONMENT_ID {
                    Source::Environment
                } else if *id == system.input_hash {
                    Source::Own
                } else if *id == ABSENT_ID {
                    Source::Absent
                } else if let Some(other) = scene.iter().position(|s| s.input_hash == *id) {
                    Source::System(other)
                } else {
                    bail!(
                        "precomputed realtime GI that references a system of another scene is not supported yet"
                    )
                })
            })
            .collect::<Result<Vec<_>>>()?;
        if let Some(r) = system
            .materials
            .instance_renderers
            .iter()
            .find(|&&r| r as usize >= system.renderer_count)
        {
            bail!("a cluster of renderer {r} of {}", system.renderer_count);
        }
        let values = system.input.values();
        for segment in &system.transport.segments {
            for &(dependency, index) in &segment.references {
                let limit = match sources[dependency as usize] {
                    Source::Environment => ENVIRONMENT_VALUES,
                    Source::Own => values,
                    Source::System(other) => scene[other].input.values(),
                    Source::Absent => continue,
                };
                if index as usize >= limit {
                    bail!("a transport reference to value {index} of {limit}");
                }
            }
        }
        let clusters = system.input.clusters();
        let texels = (system.transport.width * system.transport.height) as usize;
        let references = system
            .transport
            .segments
            .iter()
            .map(|s| s.references.len())
            .max()
            .unwrap_or(0);
        Ok(Self {
            sources,
            direct: vec![[0.0; 3]; clusters],
            visible: vec![0; clusters],
            stream: Vec::new(),
            albedo2: vec![[0.0; 3]; clusters],
            emission: vec![[0.0; 3]; clusters],
            emission_texels: vec![[0.0; 3]; texels],
            renderer_emission: vec![None; system.renderer_count],
            emission_dirty: false,
            bounce: vec![[0.0; 3]; clusters],
            values: vec![[0.0; 3]; values],
            changes: vec![0.0; values],
            environment: vec![[0.0; 3]; ENVIRONMENT_VALUES],
            environment_used: vec![[0.0; 3]; ENVIRONMENT_VALUES],
            environment_changes: vec![0.0; ENVIRONMENT_VALUES],
            gathered: vec![[0.0; 3]; references],
            pending: vec![-1.0; system.transport.segments.len()],
            solve_all: true,
            threshold: DEFAULT_UPDATE_THRESHOLD / 100.0,
            encoded: vec![0; texels],
            indirect_scale: 1.0,
            solved_scale: 1.0,
            system,
        })
    }

    /// The cluster, tree and constant values of the last update (what transports reference).
    pub fn values(&self) -> &[[f32; 3]] {
        &self.values
    }

    pub fn system(&self) -> &System {
        &self.system
    }

    /// The direct light of `lights` on each cluster. Fails for a directional light when the
    /// system's precomputed visibility could not be read.
    pub fn set_lights(&mut self, lights: &[Light]) -> Result<()> {
        shade_lights(
            &self.system.input,
            &self.system.visibility,
            lights,
            &mut self.visible,
            &mut self.stream,
            &mut self.direct,
        )
    }

    /// The system's albedo texture as the meta pass renders it: RGBA8, `width * height` texels
    /// row-major from v = 0, gamma-space bytes in a linear-space project. Each cluster takes the
    /// root mean square of its samples' bytes (squared as an approximation of the gamma
    /// curve), kept as 8 bits, and is lit with its square.
    pub fn set_albedo(&mut self, rgba8: &[u8]) -> Result<()> {
        let m = &self.system.materials;
        let width = self.system.transport.width as usize;
        let texels = width * self.system.transport.height as usize;
        if rgba8.len() != 4 * texels {
            bail!(
                "an albedo texture of {} bytes for {texels} texels",
                rgba8.len()
            );
        }
        for c in 0..self.albedo2.len() {
            let (mut sum, mut points) = ([0.0f32; 3], 0u32);
            for g in m.cluster_groups[c] as usize..m.cluster_groups[c + 1] as usize {
                points += m.groups[g].1 as u32;
                for &((x, y), w) in
                    &m.samples[m.group_samples[g] as usize..m.group_samples[g + 1] as usize]
                {
                    let at = 4 * (y as usize * width + x as usize);
                    for k in 0..3 {
                        let v = rgba8[at + k] as f32 / 255.0;
                        sum[k] += w as f32 * v * v;
                    }
                }
            }
            let mean = sum.map(|v| if points > 0 { v / points as f32 } else { 0.0 });
            self.albedo2[c] = mean.map(|v| {
                let byte = (v.max(0.0).sqrt() * 255.0 + 0.5).floor().min(255.0) / 255.0;
                byte * byte
            });
        }
        Ok(())
    }

    /// The system's emission texture as the meta pass renders it: linear RGBA, `width * height`
    /// texels row-major from v = 0.
    pub fn set_emission(&mut self, rgba: &[f32]) -> Result<()> {
        if rgba.len() != 4 * self.emission_texels.len() {
            bail!(
                "an emission texture of {} values for {} texels",
                rgba.len(),
                self.emission_texels.len()
            );
        }
        for (texel, v) in self.emission_texels.iter_mut().zip(rgba.chunks_exact(4)) {
            *texel = [v[0], v[1], v[2]];
        }
        self.emission_dirty = true;
        Ok(())
    }

    /// DynamicGI.SetEmissive: renderer `renderer` of the system emits `color` (linear) for all its
    /// points instead of its texels, also after later emission textures (measured:
    /// tests/projects/realtimegi room-setemissive-then-call). The clusters are computed once
    /// before the next update, however many renderers change.
    pub fn set_renderer_emission(&mut self, renderer: usize, color: [f32; 3]) -> Result<()> {
        let Some(slot) = self.renderer_emission.get_mut(renderer) else {
            bail!("renderer {renderer} of {}", self.renderer_emission.len());
        };
        *slot = Some(color);
        self.emission_dirty = true;
        Ok(())
    }

    /// Each cluster's emission: the weighted mean of its samples' texels (a renderer with an
    /// emission of its own counts it for all its points), kept as a half float.
    fn update_emission(&mut self) {
        if !std::mem::take(&mut self.emission_dirty) {
            return;
        }
        let m = &self.system.materials;
        let width = self.system.transport.width as usize;
        for c in 0..self.emission.len() {
            let (mut sum, mut points) = ([0.0f32; 3], 0u32);
            for g in m.cluster_groups[c] as usize..m.cluster_groups[c + 1] as usize {
                let (instance, count) = m.groups[g];
                points += count as u32;
                let renderer = m.instance_renderers[instance as usize] as usize;
                if let Some(color) = self.renderer_emission[renderer] {
                    mul_add(&mut sum, count as f32, color);
                    continue;
                }
                for &((x, y), w) in
                    &m.samples[m.group_samples[g] as usize..m.group_samples[g + 1] as usize]
                {
                    mul_add(
                        &mut sum,
                        w as f32,
                        self.emission_texels[y as usize * width + x as usize],
                    );
                }
            }
            // A NaN texel (a meta pass dividing by zero) emits nothing rather than spreading
            // NaN through the cluster tree.
            self.emission[c] = sum.map(|v| {
                let mean = if points > 0 && !v.is_nan() {
                    v / points as f32
                } else {
                    0.0
                };
                half::f16::from_f32(mean.clamp(0.0, half::f16::MAX.to_f32())).to_f32()
            });
        }
    }

    /// A uniform environment of linear radiance `rgb` (a flat ambient color).
    pub fn set_uniform_environment(&mut self, rgb: [f32; 3]) {
        self.environment.fill(rgb);
    }

    /// DynamicGI.indirectScale: scales the texels drawn; the bounce reads them divided by it
    /// again. A change solves every segment in the next update.
    pub fn set_indirect_scale(&mut self, scale: f32) {
        self.indirect_scale = scale;
    }

    /// DynamicGI.updateThreshold, in percent: how much the inputs of a segment have to change,
    /// relative to their brightness, before it is solved again.
    pub fn set_update_threshold(&mut self, percent: f32) {
        self.threshold = percent / 100.0;
    }

    /// DynamicGI.updateThreshold, in percent.
    pub fn update_threshold(&self) -> f32 {
        self.threshold * 100.0
    }

    /// One update (matches the Unity 6000.0.71f1 Editor, tests/projects/realtimegi): radiance
    /// from the current direct light and bounce, the transport into the texels of the segments
    /// whose inputs changed enough, and the next bounce. Returns the number of segments solved:
    /// 0 when the output is converged.
    pub fn update(&mut self) -> usize {
        assert!(
            !self.sources.iter().any(|s| matches!(s, Source::System(_))),
            "a system that reads other systems updates with update_in"
        );
        self.update_with(&|_| (&[], &[]))
    }

    /// The update of a system whose transport reads the other systems' values (`others`).
    fn update_with(&mut self, others: OtherValues) -> usize {
        self.update_emission();
        let system = &*self.system;
        let clusters = self.direct.len();
        for c in 0..clusters {
            let value = [0, 1, 2].map(|k| {
                self.albedo2[c][k] * (self.direct[c][k] + self.bounce[c][k]) + self.emission[c][k]
            });
            self.changes[c] = mean_change(value, self.values[c]);
            self.values[c] = value;
        }
        let mut child = 0;
        for (p, &count) in system.input.tree_children.iter().enumerate() {
            let (mut sum, mut change) = ([0.0f32; 3], 0.0f32);
            for _ in 0..count {
                let w = system.input.tree_weights[child] as f32 * TREE_SCALE;
                mul_add(&mut sum, w, self.values[child]);
                change += w * self.changes[child];
                child += 1;
            }
            self.values[clusters + p] = sum;
            self.changes[clusters + p] = change;
        }
        let last = self.values.len() - 1;
        self.values[last] = [0.0; 3];
        self.changes[last] = 0.0;
        for ((used, change), &now) in self
            .environment_used
            .iter_mut()
            .zip(&mut self.environment_changes)
            .zip(&self.environment)
        {
            *change = mean_change(now, *used);
            *used = now;
        }

        let solve_all = std::mem::take(&mut self.solve_all)
            || (self.indirect_scale - self.solved_scale).abs() > SCALE_CHANGE;
        self.solved_scale = self.indirect_scale;
        let width = system.transport.width as usize;
        let mut solved = 0;
        for (segment, pending) in system.transport.segments.iter().zip(&mut self.pending) {
            let refs = segment.references.len();
            let (mut brightness, mut change) = ([0.0f32; 3], 0.0f32);
            for (k, &(dependency, index)) in segment.references.iter().enumerate() {
                // Indices were checked against the values in `new`.
                let (value, delta) = match self.sources[dependency as usize] {
                    Source::Environment => (
                        self.environment[index as usize],
                        self.environment_changes[index as usize],
                    ),
                    Source::Own => (self.values[index as usize], self.changes[index as usize]),
                    Source::System(other) => {
                        let (values, changes) = others(other);
                        (values[index as usize], changes[index as usize])
                    }
                    Source::Absent => ([0.0; 3], 0.0),
                };
                let w = segment.reference_weights[k] as f32 * TRANSPORT_SCALE;
                self.gathered[k] = value.map(|v| v * w);
                mul_add(&mut brightness, 1.0, self.gathered[k]);
                change += w * delta;
            }
            if !solve_all {
                *pending = pending.max(0.0) + change;
                let mean = (brightness[0] + brightness[1] + brightness[2]) / 3.0;
                if *pending / (mean + CHANGE_EPSILON) < self.threshold {
                    continue;
                }
            }
            *pending = -1.0;
            solved += 1;
            for (t, &(x, y)) in segment.texels.iter().enumerate() {
                let row = &segment.weights[t * refs..(t + 1) * refs];
                let mut sum = [0.0f32; 3];
                for (k, &w) in row.iter().enumerate() {
                    mul_add(&mut sum, w as f32, self.gathered[k]);
                }
                self.encoded[y as usize * width + x as usize] =
                    encode_rgb9e5(sum.map(|v| v.max(0.0) * self.indirect_scale));
            }
            for &((dx, dy), (sx, sy)) in &segment.copies {
                self.encoded[dy as usize * width + dx as usize] =
                    self.encoded[sy as usize * width + sx as usize];
            }
        }

        // Each cluster's bounce is the drawn texel of its first resampling sample, divided by
        // the indirect scale (the precomputed data lists more weighted samples per cluster;
        // the Editor reads only the first).
        let transport = &system.transport;
        let scale = if self.indirect_scale != 0.0 {
            self.indirect_scale
        } else {
            1.0
        };
        for c in 0..clusters {
            let first = transport.bounce_samples[c] as usize;
            self.bounce[c] = if first < transport.bounce_samples[c + 1] as usize {
                let ((x, y), _) = transport.bounce[first];
                decode_rgb9e5(self.encoded[y as usize * width + x as usize]).map(|v| v / scale)
            } else {
                [0.0; 3]
            };
        }
        solved
    }

    /// Per cluster, the albedo it is lit with (the square of its 8-bit albedo) and its emission.
    pub fn cluster_materials(&mut self) -> (&[[f32; 3]], &[[f32; 3]]) {
        self.update_emission();
        (&self.albedo2, &self.emission)
    }

    /// The texels to draw (scaled by the indirect scale), RGB9E5, `width * height` row-major at
    /// system resolution.
    pub fn encoded(&self) -> &[u32] {
        &self.encoded
    }
}

#[cfg(test)]
mod falloff_tests {
    use super::*;

    #[test]
    fn the_falloff_table_samples_and_interpolates_the_curve() {
        let table = &*LEGACY_FALLOFF;
        // At the sample positions the table is the curve; at x = 0 it is 1, from the range on 0.
        for i in 2..FALLOFF_SAMPLES - 1 {
            let x2 = (15.0 / i as f32 - 1.0) / 14.0;
            assert!(
                (table.eval(x2) - legacy_falloff(x2)).abs() < 1e-6,
                "sample {i}"
            );
        }
        assert_eq!(table.eval(0.0), 1.0);
        assert_eq!(table.eval(1.0), 0.0);
        assert_eq!(table.eval(4.0), 0.0);
        assert_eq!(table.eval(f32::INFINITY), 0.0);
        // Between samples it is linear in the table position, not the curve: at x = 0.5 the
        // position is 15 / 4.5 (between samples 3 and 4).
        let t = 15.0f32 / 4.5;
        let (a, b) = (
            legacy_falloff((15.0 / 3.0 - 1.0) / 14.0),
            legacy_falloff((15.0 / 4.0 - 1.0) / 14.0),
        );
        let expected = a + (b - a) * (t - 3.0);
        assert!((table.eval(0.25) - expected).abs() < 1e-6);
        assert!((table.eval(0.25) - legacy_falloff(0.25)).abs() > 1e-4);
    }

    #[test]
    fn a_spot_cone_steps_between_its_default_inner_and_its_outer_angle() {
        let cone = SpotCone::new([0.0, 0.0, 1.0], 60.0);
        let at = |degrees: f32| {
            let r = degrees.to_radians();
            cone.falloff([r.sin(), 0.0, r.cos()])
        };
        // Full inside the default inner cone (half angle atan(tan(30) * 0.71875) = 22.54
        // degrees), nothing outside the outer one.
        assert_eq!(at(0.0), 1.0);
        assert_eq!(at(22.0), 1.0);
        assert_eq!(at(30.5), 0.0);
        assert_eq!(at(90.0), 0.0);
        assert_eq!(at(180.0), 0.0);
        // In between: the square of the position in cosine between the two half angles, at the
        // table step at or below the point's cosine.
        let (outer, inner) = (30f32.to_radians().cos(), 22.5427f32.to_radians().cos());
        let step = (1.0 - outer) / SPOT_STEPS as f32;
        let cos = 26f32.to_radians().cos();
        let below = outer + ((cos - outer) / step).floor() * step;
        let expected = ((below - outer) / (inner - outer)).powi(2);
        assert!(
            (at(26.0) - expected).abs() < 1e-3,
            "{} vs {expected}",
            at(26.0)
        );
        assert!((at(26.0) - ((cos - outer) / (inner - outer)).powi(2)).abs() > 1e-3);
        let mut last = 1.0;
        for d in 0..40 {
            let v = at(d as f32);
            assert!(v <= last, "not decreasing at {d}");
            last = v;
        }
        // A degenerate cone or direction stays finite.
        let point = SpotCone::new([0.0, 0.0, 1.0], 0.0);
        assert!(point.falloff([0.0, 0.0, 1.0]).is_finite());
        assert_eq!(cone.falloff([f32::NAN; 3]), 0.0);
    }

    /// The direct light of `lights` on one cluster of `input` without precomputed visibility.
    fn shade(input: &crate::data::InputSamples, lights: &[Light]) -> Result<[f32; 3]> {
        let mut out = [[0.0f32; 3]; 1];
        let none = Err("none".to_string());
        shade_lights(input, &none, lights, &mut [0], &mut Vec::new(), &mut out)?;
        Ok(out[0])
    }

    #[test]
    fn a_point_light_lights_a_cluster_by_its_normal_and_the_table() {
        // A cluster of one quad at the origin facing +y, a light 2 above it with range 4.
        let mut input = crate::data::InputSamples {
            positions: vec![[0.0; 3]; 4],
            normals: vec![[0.0, 1.0, 0.0]; 4],
            cluster_points: vec![0, 4],
            tree_children: vec![],
            tree_weights: vec![],
        };
        let at = |kind| Light {
            color: [1.0, 0.5, 0.25],
            kind,
        };
        let point = at(LightKind::Point {
            position: [0.0, 2.0, 0.0],
            range: 4.0,
        });
        let direct = |input: &crate::data::InputSamples| shade(input, &[point]).unwrap();
        let f = LEGACY_FALLOFF.eval(0.25);
        assert_eq!(direct(&input), [f, 0.5 * f, 0.25 * f]);
        // Facing away: nothing; at 45 degrees for half the points: their cosine.
        input.normals = vec![[0.0, -1.0, 0.0]; 4];
        assert_eq!(direct(&input), [0.0; 3]);
        let h = std::f32::consts::FRAC_1_SQRT_2;
        input.normals = vec![[0.0, 1.0, 0.0], [0.0, 1.0, 0.0], [h, h, 0.0], [h, h, 0.0]];
        assert!((direct(&input)[0] - f * (1.0 + h) / 2.0).abs() < 1e-6);
        // A spot light pointing down lights the cluster as the point light did; pointing
        // sideways, not at all.
        input.normals = vec![[0.0, 1.0, 0.0]; 4];
        let spot = |direction| {
            at(LightKind::Spot {
                position: [0.0, 2.0, 0.0],
                range: 4.0,
                cone: SpotCone::new(direction, 30.0),
            })
        };
        assert_eq!(
            shade(&input, &[spot([0.0, -1.0, 0.0])]).unwrap(),
            [f, 0.5 * f, 0.25 * f]
        );
        assert_eq!(shade(&input, &[spot([1.0, 0.0, 0.0])]).unwrap(), [0.0; 3]);
        // Out of range: nothing.
        input.positions = vec![[0.0, -3.0, 0.0]; 4];
        assert_eq!(direct(&input), [0.0; 3]);
        // A directional light needs the precomputed visibility.
        let sun = at(LightKind::Directional {
            to_light: [0.0, 1.0, 0.0],
        });
        let error = shade(&input, &[sun]).unwrap_err().to_string();
        assert!(error.contains("visibility"), "{error}");
    }
}
