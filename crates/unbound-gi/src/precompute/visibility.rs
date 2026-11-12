//! The visibility of directional lights: per stored direction, per cluster the share of its
//! points from which a ray toward that direction leaves the scene, rounded to 2 bits. The
//! directions lie on 27 rings of latitude around +y (`crate::visibility`).
use super::clusters::{Clustering, push_off};
use crate::data::InputSamples;
use crate::visibility::{Visibility, ring_direction, ring_layout};
use anyhow::Result;
use rayon::prelude::*;

/// Rings of latitude of the stored directions (the Unity 6000.0.71f1 Editor's precompute stores
/// 27, tests/projects/realtimegi).
pub const RINGS: usize = 27;
/// Values per byte: 2 bits each, 0 (no point sees the direction) to 3 (all do), and directions
/// per stream, as the Editor's precompute stores them.
pub const VALUES_PER_BYTE: usize = 4;
pub const DIRECTIONS_PER_STREAM: usize = 4;
/// A point sees a direction less than this (cosine) behind its surface along its surface (so
/// that a light at the horizon, read between stored directions, is not cut off), and none
/// farther behind (as the Editor's precompute, tests/projects/realtimegi).
const BEHIND: f32 = -0.25;

/// The visibility of `input`'s clusters (their points, in order) through the surfaces traced by
/// `clustering`.
pub fn visibility(input: &InputSamples, clustering: &Clustering) -> Result<Visibility> {
    let rings = ring_layout(RINGS);
    let directions: Vec<[f32; 3]> = rings
        .iter()
        .enumerate()
        .flat_map(|(r, &(_, n))| (0..n).map(move |j| ring_direction(RINGS, r, j, n)))
        .collect();
    let top = (1u32 << (8 / VALUES_PER_BYTE)) - 1;
    let values: Vec<Vec<u8>> = directions
        .par_iter()
        .map(|&d| {
            (0..input.clusters())
                .map(|c| {
                    let points =
                        input.cluster_points[c] as usize..input.cluster_points[c + 1] as usize;
                    let count = points.len().max(1) as f32;
                    let open = points
                        .filter(|&p| {
                            let (q, n) = (input.positions[p], input.normals[p]);
                            let facing = d[0] * n[0] + d[1] * n[1] + d[2] * n[2];
                            // (A point of a normal that is not finite sees nothing.)
                            if !(facing > BEHIND) {
                                return false;
                            }
                            // A direction a little behind the surface is taken along it.
                            let ray = if facing > 0.0 {
                                d
                            } else {
                                std::array::from_fn(|k| d[k] - facing * n[k])
                            };
                            let push = push_off(q, 1.0);
                            let origin = std::array::from_fn(|k| q[k] + n[k] * push);
                            clustering
                                .bvh
                                .intersect(origin, ray, f32::INFINITY)
                                .is_none()
                        })
                        .count();
                    (open as f32 / count * top as f32).round() as u8
                })
                .collect()
        })
        .collect();
    Visibility::from_values(rings, VALUES_PER_BYTE, DIRECTIONS_PER_STREAM, &values)
}
