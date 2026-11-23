//! The light a bake's lights send straight to each texel of a lightmap: at each sample of the
//! texel ([`gbuffer`](super::gbuffer)), every light that bakes its direct light adds its colour
//! by the surface's facing (its interpolated normal), unless something of the scene lies
//! between the sample, pushed off its triangle, and the light. A texel takes the mean of its
//! samples, which is the Unity 6000.0.71f1 Editor's Progressive CPU lightmapper's result for
//! lights with hard shadows where its Direct Samples are a power of two not below the samples
//! of a texel (tests/projects/bakedbuffers: 16); other counts are not measured.
use super::directional::{NONE, Toward};
use super::gbuffer::GBuffer;
use super::light::{Light, Mode, Tables};
use super::trace::{Ray, Scene};
use rayon::prelude::*;

/// A renderer of a lightmap, by its place in it ([`gbuffer::Instance::index`](super::gbuffer::Instance)).
#[derive(Clone, Copy, Debug)]
pub struct Receiver {
    /// Its local-to-world matrix, column-major.
    pub local_to_world: [f32; 16],
    /// Receive Shadows.
    pub receives_shadows: bool,
}

/// How mixed lights are baked (Lighting Mode).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum MixedMode {
    /// Baked Indirect: their direct light stays realtime.
    IndirectOnly,
    /// Subtractive: their direct light is baked.
    Subtractive,
    /// Shadowmask: their direct light stays realtime.
    Shadowmask,
}

/// Whether the light's direct light goes into the lightmaps.
pub fn bakes_direct(light: &Light, mixed: MixedMode) -> bool {
    light.mode == Mode::Baked || mixed == MixedMode::Subtractive
}

/// A point through a column-major matrix.
pub fn point(m: &[f32; 16], p: [f32; 3]) -> [f32; 3] {
    std::array::from_fn(|r| m[r] * p[0] + m[4 + r] * p[1] + m[8 + r] * p[2] + m[12 + r])
}

/// A matrix's rows that take a normal to world space (the inverse of its upper 3 × 3,
/// transposed, up to scale; with the sign of its determinant).
pub fn normal_matrix(m: &[f32; 16]) -> [[f32; 3]; 3] {
    let at = |r: usize, c: usize| m[4 * c + r] as f64;
    let cofactor = |r: usize, c: usize| {
        let (r1, r2, c1, c2) = ((r + 1) % 3, (r + 2) % 3, (c + 1) % 3, (c + 2) % 3);
        at(r1, c1) * at(r2, c2) - at(r1, c2) * at(r2, c1)
    };
    let determinant: f64 = (0..3).map(|c| at(0, c) * cofactor(0, c)).sum();
    let sign = if determinant < 0.0 { -1.0 } else { 1.0 };
    std::array::from_fn(|r| std::array::from_fn(|c| (sign * cofactor(r, c)) as f32))
}

/// A unit normal in world space; itself where the matrix flattens it to nothing.
pub fn world_normal(rows: &[[f32; 3]; 3], n: [f32; 3]) -> [f32; 3] {
    let v: [f64; 3] =
        std::array::from_fn(|r| (0..3).map(|c| rows[r][c] as f64 * n[c] as f64).sum());
    let length = (v[0] * v[0] + v[1] * v[1] + v[2] * v[2]).sqrt();
    if length > 0.0 {
        v.map(|x| (x / length) as f32)
    } else {
        n
    }
}

/// Sample `k` of texel (`tx`, `ty`) of the lightmap: its place in the samples, and the
/// renderer its surface belongs to (by its place among `receivers`), if it has one.
pub(super) fn sample(
    g: &GBuffer,
    tx: usize,
    ty: usize,
    k: usize,
    receivers: usize,
) -> (usize, Option<usize>) {
    let s = g.supersampling;
    let index = g.width * s * (ty * s + k / s) + tx * s + k % s;
    let local = g.positions[index][3];
    (
        index,
        (local >= 0.0)
            .then(|| local as usize)
            .filter(|&i| i < receivers),
    )
}

/// The direct light of a lightmap's texels.
pub struct Direct {
    /// Red, green, blue, and 1 for a used texel (zeros for an unused one).
    pub irradiance: Vec<[f32; 4]>,
    /// Where the light comes from (see [`Toward::encode`]).
    pub directionality: Vec<[f32; 4]>,
    /// In the Shadowmask Lighting Mode, per channel the part of the texel's samples its mixed
    /// light reaches that see it (0 where it reaches none); zeros elsewhere.
    pub shadowmask: Vec<[f32; 4]>,
}

/// The direct light of every texel of the lightmap. `receivers` are the lightmap's renderers
/// by their place in it, `channels` the lights' shadowmask channels, `pushoff` how far a sample
/// is pushed off its triangle before its rays start.
pub fn direct(
    g: &GBuffer,
    receivers: &[Receiver],
    lights: &[Light],
    channels: &[Option<u8>],
    mixed: MixedMode,
    scene: &Scene,
    pushoff: f32,
) -> Direct {
    let tables = Tables::new();
    let masked: Vec<(&Light, usize)> = lights
        .iter()
        .zip(channels)
        .filter(|(l, _)| mixed == MixedMode::Shadowmask && l.mode == Mode::Mixed)
        .filter_map(|(l, c)| c.map(|c| (l, c as usize)))
        .collect();
    let lights: Vec<&Light> = lights.iter().filter(|l| bakes_direct(l, mixed)).collect();
    let normals: Vec<[[f32; 3]; 3]> = receivers
        .iter()
        .map(|r| normal_matrix(&r.local_to_world))
        .collect();
    let s = g.supersampling;
    let texels = (0..g.width * g.height)
        .into_par_iter()
        .map(|texel| {
            if g.occupancy[texel] == 0 {
                return ([0.0; 4], NONE, [0.0; 4]);
            }
            let (tx, ty) = (texel % g.width, texel / g.width);
            let mut sum = [0f32; 3];
            let mut samples = 0u32;
            let mut toward = Toward::default();
            let mut facing = [0f32; 3];
            // Per shadowmask channel the samples its light reaches, and those that see it.
            let (mut reached, mut seen) = ([0u32; 4], [0u32; 4]);
            for k in 0..s * s {
                let (at, receiver) = sample(g, tx, ty, k, receivers.len());
                let Some(receiver) = receiver else {
                    continue;
                };
                let local = g.positions[at];
                samples += 1;
                let matrix = &receivers[receiver].local_to_world;
                let position = point(matrix, [local[0], local[1], local[2]]);
                let smooth = g.normals[at];
                let plane = g.plane_normals[at];
                let smooth = world_normal(&normals[receiver], [smooth[0], smooth[1], smooth[2]]);
                let plane = world_normal(&normals[receiver], [plane[0], plane[1], plane[2]]);
                let origin: [f32; 3] = std::array::from_fn(|c| position[c] + plane[c] * pushoff);
                for c in 0..3 {
                    facing[c] += smooth[c];
                }
                // What of the sample each shadowmask channel's light reaches and sees.
                for &(light, channel) in &masked {
                    let Some(arriving) = light.arriving(&tables, light.color, origin, smooth)
                    else {
                        continue;
                    };
                    let shadowed = light.casts_shadows
                        && receivers[receiver].receives_shadows
                        && scene.occluded(
                            &Ray {
                                origin,
                                direction: arriving.direction,
                            },
                            arriving.distance,
                        );
                    reached[channel] += 1;
                    if !shadowed {
                        seen[channel] += 1;
                    }
                }
                for light in &lights {
                    let Some(arriving) = light.arriving(&tables, light.color, origin, smooth)
                    else {
                        continue;
                    };
                    let shadowed = light.casts_shadows
                        && receivers[receiver].receives_shadows
                        && scene.occluded(
                            &Ray {
                                origin,
                                direction: arriving.direction,
                            },
                            arriving.distance,
                        );
                    if !shadowed {
                        for c in 0..3 {
                            sum[c] += arriving.color[c];
                        }
                        toward.add(arriving.direction, arriving.color);
                    }
                }
            }
            if samples == 0 {
                return ([0.0; 4], NONE, [0.0; 4]);
            }
            let scale = 1.0 / samples as f32;
            (
                [sum[0] * scale, sum[1] * scale, sum[2] * scale, 1.0],
                toward.encode(facing, samples),
                std::array::from_fn(|c| {
                    if reached[c] == 0 {
                        0.0
                    } else {
                        seen[c] as f32 / reached[c] as f32
                    }
                }),
            )
        })
        .collect::<Vec<_>>();
    Direct {
        irradiance: texels.iter().map(|t| t.0).collect(),
        directionality: texels.iter().map(|t| t.1).collect(),
        shadowmask: texels.iter().map(|t| t.2).collect(),
    }
}
