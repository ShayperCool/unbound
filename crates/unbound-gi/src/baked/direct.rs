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
use super::sampling::{self, Samples};
use super::trace::{Ray, Scene, Viewer};
use rayon::prelude::*;

/// A renderer of a lightmap, by its place in it ([`gbuffer::Instance::index`](super::gbuffer::Instance)).
#[derive(Clone, Copy, Debug)]
pub struct Receiver {
    /// Its local-to-world matrix, column-major.
    pub local_to_world: [f32; 16],
    /// Receive Shadows.
    pub receives_shadows: bool,
    /// Which LOD levels its rays see.
    pub viewer: Viewer,
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
/// by their place in it, `channels` the lights' shadowmask channels (one per light, or none at
/// all for no shadowmask), `pushoff` how far a sample is pushed off its triangle before its
/// rays start, `direct_samples` the Lighting Settings' Direct Samples: the texel's direction
/// is weighed as if its light were summed over that many samples (each of its own samples
/// standing for its part of them; tests/projects/bakedbuffers `DirectionalFaint*`).
///
/// A light with soft shadows (a Baked Shadow Radius or Angle) is sampled as the Editor samples
/// it: Direct Samples samples, each at a sample of the texel picked by the Sobol sequence's
/// third dimension (turned by the texel's rotation), its light's colour that of the light's
/// centre and its shadow ray aimed at a point of a disk around the light picked by the texel's
/// blue noise ([`Light::jittered`]); its light is the mean over the samples. `samples` holds
/// the sequences (none: such a light is an error of the caller).
#[allow(clippy::too_many_arguments)]
pub fn direct(
    g: &GBuffer,
    receivers: &[Receiver],
    lights: &[Light],
    channels: &[Option<u8>],
    mixed: MixedMode,
    scene: &Scene,
    pushoff: f32,
    direct_samples: u32,
    samples_of: Option<&Samples>,
) -> Direct {
    assert!(
        channels.is_empty() || channels.len() == lights.len(),
        "a shadowmask channel per light, or none"
    );
    let tables = Tables::new();
    let soft = |l: &Light| l.casts_shadows && l.shadow_size > 0.0 && samples_of.is_some();
    let masked_all: Vec<(&Light, usize)> = lights
        .iter()
        .zip(channels)
        .filter(|(l, _)| mixed == MixedMode::Shadowmask && l.mode == Mode::Mixed)
        .filter_map(|(l, c)| c.map(|c| (l, c as usize)))
        .collect();
    let masked: Vec<(&Light, usize)> = masked_all
        .iter()
        .copied()
        .filter(|(l, _)| !soft(l))
        .collect();
    let soft_masked: Vec<(&Light, usize)> = masked_all
        .iter()
        .copied()
        .filter(|(l, _)| soft(l))
        .collect();
    let all: Vec<&Light> = lights.iter().filter(|l| bakes_direct(l, mixed)).collect();
    let lights: Vec<&Light> = all.iter().copied().filter(|l| !soft(l)).collect();
    let soft_lights: Vec<&Light> = all.iter().copied().filter(|l| soft(l)).collect();
    let count = direct_samples.max(1);
    let distribution = sampling::distribution(count);
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
                // What a light sends the sample, and whether the sample sees it.
                let receives = receivers[receiver].receives_shadows;
                let viewer = receivers[receiver].viewer;
                let lit = |light: &Light| {
                    light
                        .arriving(&tables, light.color, origin, smooth)
                        .map(|arriving| {
                            let shadowed = light.casts_shadows
                                && receives
                                && scene.occluded_for(
                                    &Ray {
                                        origin,
                                        direction: arriving.direction,
                                    },
                                    arriving.distance,
                                    viewer,
                                );
                            (arriving, !shadowed)
                        })
                };
                // What of the sample each shadowmask channel's light reaches and sees.
                for &(light, channel) in &masked {
                    if let Some((_, seen_here)) = lit(light) {
                        reached[channel] += 1;
                        if seen_here {
                            seen[channel] += 1;
                        }
                    }
                }
                for light in &lights {
                    let Some((arriving, seen_here)) = lit(light) else {
                        continue;
                    };
                    if seen_here {
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
            let mut light_sum = [sum[0] * scale, sum[1] * scale, sum[2] * scale];
            // The soft lights, sample by sample; their shadowmask channels likewise.
            let (mut soft_reached, mut soft_seen, mut soft_channels) =
                ([0u32; 4], [0u32; 4], [false; 4]);
            if let Some(seq) =
                samples_of.filter(|_| !soft_lights.is_empty() || !soft_masked.is_empty())
            {
                let rotation = seq.rotation(tx, ty, g.width, 0);
                let ss = s * s;
                let at_sample = |i: u32| {
                    let r = sampling::fraction(seq.sobol(i, 2) + rotation[0]);
                    let k = ((ss as f32 * r).floor() as usize) % ss;
                    let (at, receiver) = sample(g, tx, ty, k, receivers.len());
                    let receiver = receiver?;
                    let local = g.positions[at];
                    let matrix = &receivers[receiver].local_to_world;
                    let position = point(matrix, [local[0], local[1], local[2]]);
                    let smooth = g.normals[at];
                    let plane = g.plane_normals[at];
                    let smooth =
                        world_normal(&normals[receiver], [smooth[0], smooth[1], smooth[2]]);
                    let plane = world_normal(&normals[receiver], [plane[0], plane[1], plane[2]]);
                    let origin: [f32; 3] =
                        std::array::from_fn(|c| position[c] + plane[c] * pushoff);
                    Some((
                        origin,
                        smooth,
                        receivers[receiver].receives_shadows,
                        receivers[receiver].viewer,
                    ))
                };
                let rnd = |i: u32| -> [f32; 2] {
                    if (i as usize) < sampling::BLUE_SAMPLES {
                        [
                            seq.blue(i, 0, tx, ty, distribution),
                            seq.blue(i, 1, tx, ty, distribution),
                        ]
                    } else {
                        [
                            sampling::fraction(seq.sobol(i, 0) + rotation[0]),
                            sampling::fraction(seq.sobol(i, 1) + rotation[1]),
                        ]
                    }
                };
                // Whether a sample sees the light through its jittered shadow ray.
                let sees = |light: &Light,
                            arriving: &super::light::Arriving,
                            origin: [f32; 3],
                            receives: bool,
                            viewer,
                            u: [f32; 2]|
                 -> ([f32; 3], bool) {
                    let direction = light.jittered(arriving, origin, u);
                    let shadowed = receives
                        && scene.occluded_for(
                            &Ray { origin, direction },
                            arriving.distance,
                            viewer,
                        );
                    (direction, !shadowed)
                };
                for light in &soft_lights {
                    let (mut acc, mut counted) = ([0f32; 3], 0u32);
                    let mut toward_light = Toward::default();
                    for i in 0..count {
                        let Some((origin, smooth, receives, viewer)) = at_sample(i) else {
                            continue;
                        };
                        counted += 1;
                        let Some(arriving) = light.arriving(&tables, light.color, origin, smooth)
                        else {
                            continue;
                        };
                        let (direction, seen_here) =
                            sees(light, &arriving, origin, receives, viewer, rnd(i));
                        if seen_here {
                            for c in 0..3 {
                                acc[c] += arriving.color[c];
                            }
                            toward_light.add(direction, arriving.color);
                        }
                    }
                    if counted > 0 {
                        let mean = 1.0 / counted as f32;
                        for c in 0..3 {
                            light_sum[c] += acc[c] * mean;
                        }
                        // (In the units of the texel's own samples, as the hard lights add.)
                        toward.merge(&toward_light, samples as f32 * mean);
                    }
                }
                for &(light, channel) in &soft_masked {
                    soft_channels[channel] = true;
                    for i in 0..count {
                        let Some((origin, smooth, receives, viewer)) = at_sample(i) else {
                            continue;
                        };
                        let Some(arriving) = light.arriving(&tables, light.color, origin, smooth)
                        else {
                            continue;
                        };
                        soft_reached[channel] += 1;
                        if sees(light, &arriving, origin, receives, viewer, rnd(i)).1 {
                            soft_seen[channel] += 1;
                        }
                    }
                }
            }
            (
                [light_sum[0], light_sum[1], light_sum[2], 1.0],
                // (No fewer than one Direct Sample, as an asset's raw number may say.)
                toward.encode(
                    facing,
                    samples,
                    direct_samples.max(1) as f32 / samples as f32,
                ),
                std::array::from_fn(|c| {
                    let (reached, seen) = if soft_channels[c] {
                        (soft_reached[c], soft_seen[c])
                    } else {
                        (reached[c], seen[c])
                    };
                    if reached == 0 {
                        0.0
                    } else {
                        seen as f32 / reached as f32
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
