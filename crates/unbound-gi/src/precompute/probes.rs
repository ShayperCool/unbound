//! The light probes' transport: per probe, rays leave its position in all directions, evenly
//! spread, each weighted by the spherical harmonics basis of its direction: one that meets a
//! cluster's front adds them to that cluster's weights, one that leaves the scene to its
//! environment texel's, one that meets a back face (inside geometry) nothing. The weights are
//! the probe's `SphericalHarmonicsL2` coefficients per unit of each reference's value.
use super::math::V3;
use super::scene::Scene;
use super::transport::{Weight, environment_texel, reduce};
use crate::data::{Probe, ProbeSet, SH_COEFFICIENTS};
use anyhow::{Result, ensure};
use rayon::prelude::*;

/// Rays per probe: Unbound's choice, as many as a texel's at the default Irradiance Quality (the
/// Editor's own number for probes is not measured).
pub const DEFAULT_PROBE_RAYS: usize = 8192;
/// References per probe at most, joined through the cluster tree and the environment's levels
/// as a texel's are; it bounds a probe's data in large systems. (The probe sets of the Unity
/// 6000.0.71f1 Editor's precompute in tests/projects/realtimegi hold at most 64 references per
/// probe. With Unbound's tree, which groups clusters by position alone, fewer give more of the
/// Room's coefficients off: 23 of the base case's 243 at 128, 14 at 512; a likely cause, not
/// isolated, is that its parents join clusters that face different ways.)
pub const PROBE_BUDGET: usize = 512;

/// A probe's coefficients per unit of a reference's value.
#[derive(Clone, Copy, Default)]
struct Sh([f32; SH_COEFFICIENTS]);

impl Weight for Sh {
    fn join(&mut self, other: &Self) {
        for k in 0..SH_COEFFICIENTS {
            self.0[k] += other.0[k];
        }
    }
    fn parts(&self) -> &[f32] {
        &self.0
    }
}

/// The real spherical harmonics basis of bands 0 to 2 in `SphericalHarmonicsL2`'s order (1, y,
/// z, x, xy, yz, 3z^2 - 1, xz, x^2 - y^2), each times its coefficient's [`SCALE`].
fn basis(d: V3) -> [f32; SH_COEFFICIENTS] {
    let [x, y, z] = d;
    let b = [
        K0,
        K1 * y,
        K1 * z,
        K1 * x,
        K2 * x * y,
        K2 * y * z,
        K6 * (3.0 * z * z - 1.0),
        K2 * x * z,
        K8 * (x * x - y * y),
    ];
    std::array::from_fn(|k| b[k] * SCALE[k])
}

/// The basis' normalisation constants.
const K0: f32 = 0.282_094_8;
const K1: f32 = 0.488_602_5;
const K2: f32 = 1.092_548_4;
const K6: f32 = 0.315_391_6;
const K8: f32 = 0.546_274_2;

/// Per coefficient, what its radiance projection is scaled by into a probe's coefficient: the
/// cosine lobe's band factor over pi (1, 2/3, 1/4) times the coefficient's own constant, as
/// `LightProbes.GetInterpolatedProbe` returns them (matches the Unity 6000.0.71f1 Editor's probe
/// transport of tests/projects/realtimegi `Room` coefficient by coefficient).
const SCALE: [f32; SH_COEFFICIENTS] = [
    K0,
    2.0 / 3.0 * K1,
    2.0 / 3.0 * K1,
    2.0 / 3.0 * K1,
    0.25 * K2,
    0.25 * K2,
    0.25 * K6,
    0.25 * K2,
    0.25 * K8,
];

/// `count` unit directions evenly spread over the sphere (a spherical Fibonacci lattice).
fn sphere(count: usize) -> Vec<V3> {
    let golden = std::f64::consts::PI * (3.0 - 5f64.sqrt());
    (0..count)
        .map(|i| {
            let z = 1.0 - (2.0 * i as f64 + 1.0) / count as f64;
            let r = (1.0 - z * z).max(0.0).sqrt();
            let phi = golden * i as f64;
            [(r * phi.cos()) as f32, (r * phi.sin()) as f32, z as f32]
        })
        .collect()
}

/// The transport of probes at `positions` from the systems of `scene` and the environment
/// (`set_hash`: the probe set's id).
pub fn probe_set(
    positions: &[V3],
    scene: &Scene,
    set_hash: [u8; 16],
    rays: usize,
    budget: usize,
) -> Result<ProbeSet> {
    ensure!(
        rays > 0 && budget > 0,
        "{rays} rays and {budget} references per probe"
    );
    ensure!(
        scene.systems.len() < u8::MAX as usize,
        "light probes lit by {} systems",
        scene.systems.len()
    );
    let directions = sphere(rays);
    let share = 4.0 * std::f32::consts::PI / rays as f32;
    let probes = positions
        .par_iter()
        .map(|&p| {
            let mut refs: std::collections::BTreeMap<u32, Sh> = Default::default();
            for &d in &directions {
                let key = match scene.bvh.intersect(p, d, f32::INFINITY) {
                    Some(hit) => match scene.reference(&hit) {
                        Some(r) => r,
                        None => continue,
                    },
                    None => scene.refs.environment + environment_texel(d),
                };
                let b = basis(d);
                let w = refs.entry(key).or_default();
                for k in 0..SH_COEFFICIENTS {
                    w.0[k] += b[k] * share;
                }
            }
            let mut refs: Vec<(u32, Sh)> = refs.into_iter().collect();
            reduce(&mut refs, budget, &scene.hierarchy);
            Probe {
                references: refs
                    .iter()
                    .map(|&(r, _)| {
                        let (dependency, index) = scene.reference_of(r, None);
                        (dependency as u8, index)
                    })
                    .collect(),
                weights: refs.into_iter().map(|(_, w)| w.0).collect(),
            }
        })
        .collect();
    Ok(ProbeSet {
        hash: set_hash,
        dependencies: scene.dependencies(None),
        probes,
    })
}
