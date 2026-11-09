//! One system's runtime update (the model of docs/REALTIME_GI.md): direct light per cluster,
//! cluster radiance, the cluster tree, the transport into the realtime lightmap texels and the
//! bounce resampled from them. All buffers are allocated once per system; an update allocates
//! nothing.
use crate::data::{ABSENT_ID, ENVIRONMENT_ID, ENVIRONMENT_VALUES, System};
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

/// A light's input color in a linear-space project: its color scaled by intensity and bounce
/// intensity, converted to linear after the scaling unless the project uses linear light
/// intensity. Gamma-space projects are not measured and fail.
pub fn light_color(
    color: [f32; 3],
    intensity: f32,
    bounce: f32,
    linear_space: bool,
    linear_intensity: bool,
) -> Result<[f32; 3]> {
    if !linear_space {
        bail!("precomputed realtime GI in a gamma-space project is not supported yet");
    }
    Ok(if linear_intensity {
        color.map(|c| gamma_to_linear(c) * intensity * bounce)
    } else {
        color.map(|c| gamma_to_linear(c * intensity * bounce))
    })
}

/// A realtime point light as precomputed realtime GI sees it in the built-in pipeline: not
/// shadowed, with the legacy falloff (other pipelines' falloffs are not supported yet).
#[derive(Debug, Clone, Copy)]
pub struct PointLight {
    pub position: [f32; 3],
    /// Linear, see [`light_color`].
    pub color: [f32; 3],
    pub range: f32,
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
    Absent,
}

/// One system's state between updates.
pub struct SystemSolver {
    system: Arc<System>,
    sources: Vec<Source>,
    direct: Vec<[f32; 3]>,
    albedo2: Vec<[f32; 3]>,
    emission: Vec<[f32; 3]>,
    bounce: Vec<[f32; 3]>,
    values: Vec<[f32; 3]>,
    environment: Vec<[f32; 3]>,
    gathered: Vec<[f32; 3]>,
    /// Output texels (system resolution, row-major): drawn (RGB9E5, indirect scale applied) and
    /// as the bounce reads them (decoded, unscaled).
    encoded: Vec<u32>,
    output: Vec<[f32; 3]>,
    indirect_scale: f32,
}

fn mul_add(acc: &mut [f32; 3], w: f32, v: [f32; 3]) {
    for k in 0..3 {
        acc[k] += w * v[k];
    }
}

impl SystemSolver {
    /// Fails for transports that reference other systems (not supported yet).
    pub fn new(system: Arc<System>) -> Result<Self> {
        let sources = system
            .transport
            .dependencies
            .iter()
            .map(|id| {
                Ok(if *id == ENVIRONMENT_ID {
                    Source::Environment
                } else if *id == system.hash {
                    Source::Own
                } else if *id == ABSENT_ID {
                    Source::Absent
                } else {
                    bail!(
                        "precomputed realtime GI that references other systems is not supported yet"
                    )
                })
            })
            .collect::<Result<Vec<_>>>()?;
        let values = system.input.values();
        for segment in &system.transport.segments {
            for &(dependency, index) in &segment.references {
                let limit = match sources[dependency as usize] {
                    Source::Environment => ENVIRONMENT_VALUES,
                    Source::Own => values,
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
            albedo2: vec![[0.0; 3]; clusters],
            emission: vec![[0.0; 3]; clusters],
            bounce: vec![[0.0; 3]; clusters],
            values: vec![[0.0; 3]; system.input.values()],
            environment: vec![[0.0; 3]; ENVIRONMENT_VALUES],
            gathered: vec![[0.0; 3]; references],
            encoded: vec![0; texels],
            output: vec![[0.0; 3]; texels],
            indirect_scale: 1.0,
            system,
        })
    }

    pub fn system(&self) -> &System {
        &self.system
    }

    /// The direct light of `lights` on each cluster: the mean over its points.
    pub fn set_lights(&mut self, lights: &[PointLight]) {
        let input = &self.system.input;
        for (c, out) in self.direct.iter_mut().enumerate() {
            let (first, end) = (
                input.cluster_points[c] as usize,
                input.cluster_points[c + 1] as usize,
            );
            let mut sum = [0.0f32; 3];
            for p in first..end {
                let (pos, n) = (input.positions[p], input.normals[p]);
                for light in lights {
                    let d = [0, 1, 2].map(|k| light.position[k] - pos[k]);
                    let length2 = d[0] * d[0] + d[1] * d[1] + d[2] * d[2];
                    if length2 <= 0.0 || light.range <= 0.0 {
                        continue;
                    }
                    let length = length2.sqrt();
                    let cos = (n[0] * d[0] + n[1] * d[1] + n[2] * d[2]) / length;
                    if cos <= 0.0 {
                        continue;
                    }
                    let falloff = legacy_falloff(length2 / (light.range * light.range));
                    mul_add(&mut sum, cos * falloff, light.color);
                }
            }
            let count = (end - first).max(1) as f32;
            *out = sum.map(|v| v / count);
        }
    }

    /// The renderers' albedo (gamma space, 0..1, as their meta pass writes it) and emission
    /// (linear), one per renderer of the system in scene mapping order. A cluster takes the
    /// point-weighted mean of the renderers covering it; its albedo is kept as 8 bits and used
    /// squared.
    pub fn set_materials(&mut self, albedo: &[[f32; 3]], emission: &[[f32; 3]]) -> Result<()> {
        let m = &self.system.materials;
        let renderers = self.system.renderer_count;
        if albedo.len() != renderers || emission.len() != renderers {
            bail!(
                "{renderers} renderers but {} albedos and {} emissions",
                albedo.len(),
                emission.len()
            );
        }
        for c in 0..self.albedo2.len() {
            let (mut a, mut e, mut total) = ([0.0f32; 3], [0.0f32; 3], 0.0f32);
            for &(instance, points) in
                &m.groups[m.cluster_groups[c] as usize..m.cluster_groups[c + 1] as usize]
            {
                let r = m.instance_renderers[instance as usize] as usize;
                if r >= renderers {
                    bail!("a cluster of renderer {r} of {renderers}");
                }
                mul_add(&mut a, points as f32, albedo[r]);
                mul_add(&mut e, points as f32, emission[r]);
                total += points as f32;
            }
            let total = total.max(1.0);
            self.albedo2[c] = a.map(|v| {
                let byte = (v / total * 255.0).round().clamp(0.0, 255.0) / 255.0;
                byte * byte
            });
            self.emission[c] = e.map(|v| v / total);
        }
        Ok(())
    }

    /// A uniform environment of linear radiance `rgb` (a flat ambient color).
    pub fn set_uniform_environment(&mut self, rgb: [f32; 3]) {
        self.environment.fill(rgb);
    }

    /// DynamicGI.indirectScale: scales the texels drawn; the bounce reads the unscaled ones.
    /// (The Editor's texels come out 2.00 to 2.07 times brighter with 2: the small extra is
    /// unexplained.)
    pub fn set_indirect_scale(&mut self, scale: f32) {
        self.indirect_scale = scale;
    }

    /// Starts the bounce at the environment's first value (only converged output is compared
    /// with the Editor, so the start is not measured).
    pub fn reset_bounce(&mut self) {
        let start = self.environment[0];
        self.bounce.fill(start);
    }

    /// One update: radiance from the current direct light and bounce, the transport into the
    /// texels and the next bounce. Returns the largest change of a bounce value.
    pub fn update(&mut self) -> f32 {
        let system = &*self.system;
        let clusters = self.direct.len();
        for c in 0..clusters {
            self.values[c] = [0, 1, 2].map(|k| {
                self.albedo2[c][k] * (self.direct[c][k] + self.bounce[c][k]) + self.emission[c][k]
            });
        }
        let mut child = 0;
        let mut weight = 0;
        for (p, &count) in system.input.tree_children.iter().enumerate() {
            let mut sum = [0.0f32; 3];
            for _ in 0..count {
                mul_add(
                    &mut sum,
                    system.input.tree_weights[weight] as f32 * TREE_SCALE,
                    self.values[child],
                );
                child += 1;
                weight += 1;
            }
            self.values[clusters + p] = sum;
        }
        let last = self.values.len() - 1;
        self.values[last] = [0.0; 3];

        let width = system.transport.width as usize;
        for segment in &system.transport.segments {
            let refs = segment.references.len();
            for (k, &(dependency, index)) in segment.references.iter().enumerate() {
                // Indices were checked against the values in `new`.
                let value = match self.sources[dependency as usize] {
                    Source::Environment => self.environment[index as usize],
                    Source::Own => self.values[index as usize],
                    Source::Absent => [0.0; 3],
                };
                let w = segment.reference_weights[k] as f32 * TRANSPORT_SCALE;
                self.gathered[k] = value.map(|v| v * w);
            }
            for (t, &(x, y)) in segment.texels.iter().enumerate() {
                let row = &segment.weights[t * refs..(t + 1) * refs];
                let mut sum = [0.0f32; 3];
                for (k, &w) in row.iter().enumerate() {
                    mul_add(&mut sum, w as f32, self.gathered[k]);
                }
                let texel = y as usize * width + x as usize;
                let sum = sum.map(|v| v.max(0.0));
                self.output[texel] = decode_rgb9e5(encode_rgb9e5(sum));
                self.encoded[texel] = encode_rgb9e5(sum.map(|v| v * self.indirect_scale));
            }
            for &((dx, dy), (sx, sy)) in &segment.copies {
                let (d, s) = (
                    dy as usize * width + dx as usize,
                    sy as usize * width + sx as usize,
                );
                self.encoded[d] = self.encoded[s];
                self.output[d] = self.output[s];
            }
        }

        let transport = &system.transport;
        let mut change = 0.0f32;
        for c in 0..clusters {
            let mut sum = [0.0f32; 3];
            let samples = &transport.bounce
                [transport.bounce_samples[c] as usize..transport.bounce_samples[c + 1] as usize];
            for &((x, y), w) in samples {
                mul_add(&mut sum, w, self.output[y as usize * width + x as usize]);
            }
            for k in 0..3 {
                change = change.max((sum[k] - self.bounce[c][k]).abs());
            }
            self.bounce[c] = sum;
        }
        change
    }

    /// The texels to draw (scaled by the indirect scale), RGB9E5, `width * height` row-major at
    /// system resolution.
    pub fn encoded(&self) -> &[u32] {
        &self.encoded
    }

    /// The texels before the indirect scale, decoded.
    pub fn output(&self) -> &[[f32; 3]] {
        &self.output
    }
}
