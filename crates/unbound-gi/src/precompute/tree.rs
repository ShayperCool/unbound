//! The cluster tree: parents over groups of clusters that take alike light, so that a parent
//! can stand for its children where a texel's or probe's references are too many (see
//! [`super::reduction`]). Unbound's own grouping: from the whole system down, each group is
//! split by k-means into the fewest parts (2 to [`MAX_CHILDREN`]) that each spread at most
//! [`SPLIT_SPREAD`] as far as the group, over what describes a cluster's light: where it lies,
//! which way it faces and what it sees (how far other surfaces are in a fixed set of
//! directions). The Unity 6000.0.71f1 Editor's own trees are not reproduced; with this tree
//! Unbound's light probes of tests/projects/realtimegi `Room` at the Editor's budget of
//! references are as close to its own as without any budget (docs/REALTIME_GI.md, Unbound's
//! precompute).
use super::geometry::Bvh;
use super::math::{V3, add, dot, scale};
use rayon::prelude::*;

/// A cluster's description: its centre (in voxels), its normal times [`NORMAL_WEIGHT`] and per
/// direction of [`SIGHT_DIRECTIONS`] what it sees.
pub(super) const FEATURES: usize = 6 + SIGHT_DIRECTIONS;
/// How far apart (in voxels) two clusters facing ways a unit apart count.
const NORMAL_WEIGHT: f32 = 4.0;
/// Directions (spread evenly over the sphere) a cluster's sight is taken in.
const SIGHT_DIRECTIONS: usize = 32;
/// How far apart (in voxels) two clusters count that see in one direction nothing and the
/// farthest surface counted.
const SIGHT_WEIGHT: f32 = 2.0;
/// Surfaces farther than this (in voxels) count as the farthest.
const SIGHT_RANGE: f32 = 8.0;
/// The most children of one parent.
const MAX_CHILDREN: usize = 5;
/// A split is taken at the fewest parts each spreading at most this share as far as the group.
const SPLIT_SPREAD: f32 = 0.7;
/// The k-means rounds of one split at most.
const ROUNDS: usize = 20;

/// The tree over a system's clusters in the runtime's form.
pub(super) struct TreeLayout {
    /// The clusters (indices of the described ones) in the order of the leaves.
    pub(super) order: Vec<usize>,
    /// Per parent, its number of children: children are the next values in order (leaves,
    /// then the parents level by level, every leaf at the same depth).
    pub(super) children: Vec<u8>,
    /// Per child, its share of its parent's area in 255ths.
    pub(super) weights: Vec<u8>,
}

/// The description of a cluster of `points` (positions and normals) in a grid of `voxel`
/// sized voxels, seeing through `bvh`.
pub(super) fn features(points: &[(V3, V3)], bvh: &Bvh, voxel: f32) -> [f32; FEATURES] {
    let mut out = [0f32; FEATURES];
    if points.is_empty() {
        return out;
    }
    let inv = 1.0 / points.len() as f32;
    let (centre, normal) = points
        .iter()
        .fold(([0f32; 3], [0f32; 3]), |(c, n), (p, d)| {
            (add(c, scale(*p, inv)), add(n, *d))
        });
    let length = dot(normal, normal).sqrt();
    let normal = if length > 0.0 {
        scale(normal, 1.0 / length)
    } else {
        [0.0; 3]
    };
    for k in 0..3 {
        out[k] = centre[k] / voxel;
        out[3 + k] = normal[k] * NORMAL_WEIGHT;
    }
    let origin = add(centre, scale(normal, 1e-3 * voxel));
    let range = SIGHT_RANGE * voxel;
    let golden = std::f32::consts::PI * (3.0 - 5f32.sqrt());
    for i in 0..SIGHT_DIRECTIONS {
        let z = 1.0 - (2.0 * i as f32 + 1.0) / SIGHT_DIRECTIONS as f32;
        let r = (1.0 - z * z).max(0.0).sqrt();
        let a = golden * i as f32;
        let d = [r * a.cos(), r * a.sin(), z];
        let facing = dot(normal, d);
        let seen = if facing <= 0.0 {
            0.0
        } else {
            let open = bvh
                .intersect(origin, d, range)
                .map_or(1.0, |hit| hit.distance.min(range) / range);
            facing * open
        };
        out[6 + i] = seen * SIGHT_WEIGHT;
    }
    out
}

fn distance2(a: &[f32; FEATURES], b: &[f32; FEATURES]) -> f32 {
    a.iter().zip(b).map(|(x, y)| (x - y) * (x - y)).sum()
}

/// The area-weighted mean of `members` and how far they spread from it (the root of their
/// mean squared distance).
fn mean_and_spread(
    members: &[usize],
    features: &[[f32; FEATURES]],
    areas: &[f32],
) -> ([f32; FEATURES], f32) {
    let total: f32 = members
        .iter()
        .map(|&i| areas[i])
        .sum::<f32>()
        .max(f32::MIN_POSITIVE);
    let mut mean = [0f32; FEATURES];
    for &i in members {
        for k in 0..FEATURES {
            mean[k] += features[i][k] * areas[i] / total;
        }
    }
    let spread = members
        .iter()
        .map(|&i| areas[i] * distance2(&features[i], &mean))
        .sum::<f32>()
        / total;
    (mean, spread.sqrt())
}

/// `members` in parts by k-means: the fewest parts that each spread at most [`SPLIT_SPREAD`]
/// as far as all of them, else the last tried; halves where the descriptions do not part.
fn split(members: &[usize], features: &[[f32; FEATURES]], areas: &[f32]) -> Vec<Vec<usize>> {
    if members.len() <= 2 {
        return members.iter().map(|&i| vec![i]).collect();
    }
    let (whole, spread) = mean_and_spread(members, features, areas);
    let mut parts: Vec<Vec<usize>> = Vec::new();
    for k in 2..=MAX_CHILDREN.min(members.len()) {
        // Seeds: the member farthest from the mean, then each the one farthest from those taken.
        let mut nearest: Vec<f32> = members
            .iter()
            .map(|&i| distance2(&features[i], &whole))
            .collect();
        let mut seeds: Vec<[f32; FEATURES]> = Vec::with_capacity(k);
        while seeds.len() < k {
            let (far, _) = nearest
                .iter()
                .enumerate()
                .fold((0, f32::MIN), |b, (j, &d)| if d > b.1 { (j, d) } else { b });
            let seed = features[members[far]];
            for (j, &i) in members.iter().enumerate() {
                nearest[j] = nearest[j].min(distance2(&features[i], &seed));
            }
            seeds.push(seed);
        }
        parts = vec![Vec::new(); k];
        for _ in 0..ROUNDS {
            parts.iter_mut().for_each(Vec::clear);
            for &i in members {
                let (best, _) = seeds
                    .iter()
                    .enumerate()
                    .fold((0, f32::MAX), |b, (s, seed)| {
                        let d = distance2(&features[i], seed);
                        if d < b.1 { (s, d) } else { b }
                    });
                parts[best].push(i);
            }
            let mut moved = false;
            for (seed, part) in seeds.iter_mut().zip(&parts) {
                if !part.is_empty() {
                    let (m, _) = mean_and_spread(part, features, areas);
                    moved |= m != *seed;
                    *seed = m;
                }
            }
            if !moved {
                break;
            }
        }
        parts.retain(|p| !p.is_empty());
        if parts.len() >= 2
            && parts
                .iter()
                .all(|p| mean_and_spread(p, features, areas).1 <= SPLIT_SPREAD * spread)
        {
            break;
        }
    }
    if parts.len() < 2 {
        // Alike descriptions (clusters in one place): halves in their order.
        let half = members.len().div_ceil(2);
        return members.chunks(half).map(<[usize]>::to_vec).collect();
    }
    parts
}

/// The tree over clusters of `features` and `areas`.
pub(super) fn build(features: &[[f32; FEATURES]], areas: &[f32]) -> TreeLayout {
    struct Node {
        children: Vec<usize>,
        leaf: Option<usize>,
        area: f32,
        depth: usize,
    }
    let mut nodes: Vec<Node> = Vec::new();
    // Top down, level by level, each level's groups split in parallel; each node's parts in
    // order.
    let mut level: Vec<(Vec<usize>, Option<usize>)> = vec![((0..features.len()).collect(), None)];
    while !level.is_empty() {
        let parts: Vec<Vec<Vec<usize>>> = level
            .par_iter()
            .map(|(members, _)| {
                if members.len() > 1 {
                    split(members, features, areas)
                } else {
                    Vec::new()
                }
            })
            .collect();
        let mut next = Vec::new();
        for ((members, parent), parts) in level.into_iter().zip(parts) {
            let index = nodes.len();
            let depth = parent.map_or(0, |p| nodes[p].depth + 1);
            nodes.push(Node {
                children: Vec::new(),
                leaf: (members.len() == 1).then(|| members[0]),
                area: members.iter().map(|&i| areas[i]).sum(),
                depth,
            });
            if let Some(p) = parent {
                nodes[p].children.push(index);
            }
            next.extend(parts.into_iter().map(|p| (p, Some(index))));
        }
        level = next;
    }
    // The levels, root first, in depth-first order: a leaf above the deepest level stands for
    // itself (as its own single child) on every level below it.
    let deepest = nodes.iter().map(|n| n.depth).max().unwrap_or(0);
    let mut levels: Vec<Vec<usize>> = vec![Vec::new(); deepest + 1];
    let mut walk = vec![0usize];
    while let Some(i) = walk.pop() {
        let n = &nodes[i];
        if n.leaf.is_some() {
            for level in &mut levels[n.depth..] {
                level.push(i);
            }
        } else {
            levels[n.depth].push(i);
        }
        walk.extend(n.children.iter().rev());
    }
    let order = levels[deepest]
        .iter()
        .filter_map(|&i| nodes[i].leaf)
        .collect();
    let mut children = Vec::new();
    let mut weights = Vec::new();
    for level in levels[..deepest].iter().rev() {
        for &i in level {
            let n = &nodes[i];
            let list: &[usize] = if n.leaf.is_some() {
                std::slice::from_ref(&i)
            } else {
                &n.children
            };
            children.push(list.len() as u8);
            let mut given = 0u32;
            for (k, &c) in list.iter().enumerate() {
                let w = if k + 1 == list.len() {
                    255 - given.min(255)
                } else {
                    let share = nodes[c].area / n.area.max(f32::MIN_POSITIVE);
                    ((share * 255.0).round() as u32).min(255 - given)
                };
                given += w;
                weights.push(w as u8);
            }
        }
    }
    TreeLayout {
        order,
        children,
        weights,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn described(p: V3, n: V3) -> [f32; FEATURES] {
        let mut f = [0f32; FEATURES];
        f[..3].copy_from_slice(&p);
        for k in 0..3 {
            f[3 + k] = n[k] * NORMAL_WEIGHT;
        }
        f
    }

    /// Every parent's children are the next values, all leaves at one depth, one root.
    fn check_form(t: &TreeLayout, leaves: usize) {
        let mut order = t.order.clone();
        order.sort_unstable();
        assert_eq!(order, (0..leaves).collect::<Vec<_>>());
        let children: usize = t.children.iter().map(|&c| c as usize).sum();
        assert_eq!(children, leaves + t.children.len() - 1, "one root");
        assert_eq!(t.weights.len(), children);
        let mut at = 0;
        for &c in &t.children {
            let w: u32 = t.weights[at..at + c as usize]
                .iter()
                .map(|&w| w as u32)
                .sum();
            assert_eq!(w, 255);
            at += c as usize;
        }
    }

    #[test]
    fn clusters_facing_other_ways_part_first() {
        // A floor and a wall meeting at an edge: clusters beside each other across the edge
        // face ways a quarter turn apart and fall into different children of the root.
        let mut f = Vec::new();
        for x in 0..4 {
            for z in 0..4 {
                f.push(described(
                    [x as f32 + 0.5, 0.0, z as f32 + 0.5],
                    [0.0, 1.0, 0.0],
                ));
            }
        }
        for y in 0..4 {
            for z in 0..4 {
                f.push(described(
                    [0.0, y as f32 + 0.5, z as f32 + 0.5],
                    [1.0, 0.0, 0.0],
                ));
            }
        }
        let areas = vec![1.0; f.len()];
        let t = build(&f, &areas);
        check_form(&t, f.len());
        // The root's children: the last parent's. Each holds clusters of one face only.
        let root_children = *t.children.last().unwrap() as usize;
        assert_eq!(root_children, 2);
        let first_half: Vec<usize> = t.order[..16].to_vec();
        assert!(
            first_half.iter().all(|&c| c < 16) || first_half.iter().all(|&c| c >= 16),
            "{first_half:?}"
        );
    }

    #[test]
    fn alike_and_single_clusters_make_a_tree() {
        // Clusters in one place facing one way (coincident geometry), and a lone cluster.
        let f = vec![described([1.0, 1.0, 1.0], [0.0, 1.0, 0.0]); 7];
        let t = build(&f, &[0.5; 7]);
        check_form(&t, 7);
        let one = build(&f[..1], &[1.0]);
        assert_eq!(one.order, vec![0]);
        assert!(one.children.is_empty() && one.weights.is_empty());
    }

    #[test]
    fn shares_follow_the_areas() {
        let f = vec![
            described([0.0, 0.0, 0.0], [0.0, 1.0, 0.0]),
            described([0.0, 0.0, 0.0], [0.0, -1.0, 0.0]),
        ];
        let t = build(&f, &[3.0, 1.0]);
        check_form(&t, 2);
        let share = |c: usize| t.weights[t.order.iter().position(|&o| o == c).unwrap()];
        assert_eq!((share(0), share(1)), (191, 64));
    }

    #[test]
    fn a_large_system_with_far_outliers_makes_a_tree() {
        // A grid of 4800 clusters on a floor and 200 far from it in a line, each farther than
        // the one before (spread over three orders of magnitude): the tree stays well formed.
        let mut f = Vec::new();
        for i in 0..4800 {
            f.push(described(
                [(i % 80) as f32, 0.0, (i / 80) as f32],
                [0.0, 1.0, 0.0],
            ));
        }
        for i in 0..200 {
            f.push(described(
                [1e3 * 2f32.powf(i as f32 / 20.0), 5.0, 0.0],
                [0.0, -1.0, 0.0],
            ));
        }
        let areas = vec![1.0; f.len()];
        let t = build(&f, &areas);
        check_form(&t, f.len());
    }
}
