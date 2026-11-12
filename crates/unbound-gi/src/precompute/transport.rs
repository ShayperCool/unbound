//! The transport: what each output texel receives from the clusters and the environment.
//! Rays leave a texel's surface point over its hemisphere, cosine distributed, so that each
//! ray counts the same: a ray that meets a cluster's surface adds its share to that cluster's
//! weight, one that leaves the scene to the environment texel it leaves through. The texels are
//! solved in segments of a block of 2x2 texels each, their weights quantized as the runtime
//! reads them, with each reference's direction for the directionality.
use super::clusters::{CULL_RAYS, Instance, around, hemisphere, hidden, push_off};
use super::math::{V3, add, dot, normalized, scale};
use super::scene::Scene;
use crate::data::{ENVIRONMENT_VALUES, Segment, Transport};
use crate::directionality::{
    DIRECTIONS, NEIGHBOUR_ABOVE, NEIGHBOUR_ABOVE_LEFT, NEIGHBOUR_ABOVE_RIGHT, NEIGHBOUR_BELOW,
    NEIGHBOUR_BELOW_LEFT, NEIGHBOUR_BELOW_RIGHT, NEIGHBOUR_LEFT, NEIGHBOUR_RIGHT,
};
use crate::environment::{RESOLUTION, level_of};
use anyhow::{Result, ensure};
use rayon::prelude::*;

/// How far (texels) a texel whose footprint is hidden looks for one of its chart that is not:
/// Unbound's choice, as far as a chart's hidden part reaches in the probe's scenes (the floor
/// under the block, 3 texels) and more; a texel with none that near receives no light.
const FILL_RADIUS: i64 = 8;
/// The cube faces' axes (+X, -X, +Y, -Y, +Z, -Z): where each face's columns and rows run (from
/// its top left) and where it looks, as a cube map stores them.
const FACES: [(V3, V3, V3); 6] = [
    ([0.0, 0.0, -1.0], [0.0, -1.0, 0.0], [1.0, 0.0, 0.0]),
    ([0.0, 0.0, 1.0], [0.0, -1.0, 0.0], [-1.0, 0.0, 0.0]),
    ([1.0, 0.0, 0.0], [0.0, 0.0, 1.0], [0.0, 1.0, 0.0]),
    ([1.0, 0.0, 0.0], [0.0, 0.0, -1.0], [0.0, -1.0, 0.0]),
    ([1.0, 0.0, 0.0], [0.0, -1.0, 0.0], [0.0, 0.0, 1.0]),
    ([-1.0, 0.0, 0.0], [0.0, -1.0, 0.0], [0.0, 0.0, -1.0]),
];

/// The environment texel (finest level) the direction `d` leaves through.
pub fn environment_texel(d: V3) -> u32 {
    let a = d.map(f32::abs);
    let face = if a[0] >= a[1] && a[0] >= a[2] {
        if d[0] >= 0.0 { 0 } else { 1 }
    } else if a[1] >= a[2] {
        if d[1] >= 0.0 { 2 } else { 3 }
    } else if d[2] >= 0.0 {
        4
    } else {
        5
    };
    let (across, down, view) = FACES[face];
    let major = dot(d, view);
    let cell = |axis: V3| {
        let t = (dot(d, axis) / major + 1.0) * 0.5 * RESOLUTION as f32;
        (t.floor().max(0.0) as usize).min(RESOLUTION - 1)
    };
    (face * RESOLUTION * RESOLUTION + cell(down) * RESOLUTION + cell(across)) as u32
}

/// The direction through the centre of the environment value `t`, of any level.
pub fn environment_direction(t: u32) -> V3 {
    let (size, start) = level_of(t as usize).unwrap_or((1, ENVIRONMENT_VALUES - 6));
    let e = (t as usize - start).min(6 * size * size - 1);
    let (face, rest) = (e / (size * size), e % (size * size));
    let (across, down, view) = FACES[face];
    let at = |i: usize| (i as f32 + 0.5) / size as f32 * 2.0 - 1.0;
    normalized(add(
        view,
        add(scale(across, at(rest % size)), scale(down, at(rest / size))),
    ))
}

/// A texel's surface point.
#[derive(Clone, Copy, Debug)]
pub struct Origin {
    pub p: V3,
    pub n: V3,
    /// Its renderer.
    pub instance: usize,
    /// Units per texel there (for offsets).
    pub texel_size: f32,
}

/// Sub-positions per texel side whose surface points are a texel's ray origins (Unbound's
/// choice: 16 origins, 512 of the default 8192 rays each).
const SUB: usize = 4;
// A texel's sub-positions are bits of a `u16`.
const _: () = assert!(SUB * SUB <= 16);

/// A texel's surface: the point at its centre and its ray origins.
#[derive(Clone, Debug)]
pub struct Footprint {
    pub centre: Origin,
    pub origins: Vec<Origin>,
}

/// Per texel of the output: the surface points of a grid of positions over the texel within its
/// renderer's triangles (a texel none of whose positions lies on them, a sliver, its centre's);
/// `None` where the texel's centre lies in no triangle (no output there).
pub fn footprints(instances: &[Instance], width: u32, height: u32) -> Vec<Option<Footprint>> {
    let size = (width * height) as usize;
    // Which renderer each texel's centre lies in (the triangle it lies deepest in), and there.
    let mut owner: Vec<Option<(usize, f32, Origin)>> = vec![None; size];
    let mut out: Vec<Vec<Origin>> = vec![Vec::new(); size];
    // Which of a texel's sub-positions an origin was made of: one on an edge of two of its
    // triangles counts once (and where triangles of its renderer overlap in the UVs, the first
    // gives it).
    let mut taken = vec![0u16; size];
    for pass in 0..2 {
        for (i, inst) in instances.iter().enumerate() {
            for t in inst.triangles {
                let uv = t.map(|v| inst.texels[v as usize]);
                let p = t.map(|v| inst.positions[v as usize]);
                let n = t.map(|v| inst.normals[v as usize]);
                let det = (uv[1][0] - uv[0][0]) * (uv[2][1] - uv[0][1])
                    - (uv[2][0] - uv[0][0]) * (uv[1][1] - uv[0][1]);
                if det.abs() < 1e-12 || !det.is_finite() {
                    continue;
                }
                let bary = |q: [f32; 2]| {
                    let b1 = ((q[0] - uv[0][0]) * (uv[2][1] - uv[0][1])
                        - (uv[2][0] - uv[0][0]) * (q[1] - uv[0][1]))
                        / det;
                    let b2 = ((uv[1][0] - uv[0][0]) * (q[1] - uv[0][1])
                        - (q[0] - uv[0][0]) * (uv[1][1] - uv[0][1]))
                        / det;
                    [1.0 - b1 - b2, b1, b2]
                };
                let world_area = super::math::length(super::math::cross(
                    super::math::sub(p[1], p[0]),
                    super::math::sub(p[2], p[0]),
                ));
                let texel_size = (world_area / det.abs()).sqrt();
                let point = |b: [f32; 3]| Origin {
                    instance: i,
                    p: add(add(scale(p[0], b[0]), scale(p[1], b[1])), scale(p[2], b[2])),
                    n: normalized(add(
                        add(scale(n[0], b[0]), scale(n[1], b[1])),
                        scale(n[2], b[2]),
                    )),
                    texel_size,
                };
                let lo = [0, 1].map(|k| uv.iter().map(|v| v[k]).fold(f32::INFINITY, f32::min));
                let hi = [0, 1].map(|k| uv.iter().map(|v| v[k]).fold(f32::NEG_INFINITY, f32::max));
                if !(lo.iter().chain(&hi).all(|v| v.is_finite())) {
                    continue;
                }
                let xs = (lo[0] - 1.0).floor().max(0.0) as u32..((hi[0] + 1.0) as u32).min(width);
                for y in (lo[1] - 1.0).floor().max(0.0) as u32..((hi[1] + 1.0) as u32).min(height) {
                    for x in xs.clone() {
                        let at = (y * width + x) as usize;
                        if pass == 0 {
                            let b = bary([x as f32 + 0.5, y as f32 + 0.5]);
                            let inside = b.iter().copied().fold(f32::INFINITY, f32::min);
                            if inside >= -1e-4
                                && owner[at].as_ref().is_none_or(|(_, best, _)| inside > *best)
                            {
                                owner[at] = Some((i, inside, point(b)));
                            }
                            continue;
                        }
                        if owner[at].as_ref().is_none_or(|(o, _, _)| *o != i) {
                            continue;
                        }
                        for sy in 0..SUB {
                            for sx in 0..SUB {
                                let q = [
                                    x as f32 + (sx as f32 + 0.5) / SUB as f32,
                                    y as f32 + (sy as f32 + 0.5) / SUB as f32,
                                ];
                                let b = bary(q);
                                let bit = 1u16 << (sy * SUB + sx);
                                if taken[at] & bit == 0 && b.iter().all(|&v| v >= -1e-5) {
                                    taken[at] |= bit;
                                    out[at].push(point(b));
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    owner
        .into_iter()
        .zip(out)
        .map(|(o, origins)| {
            o.map(|(_, _, centre)| Footprint {
                origins: if origins.is_empty() {
                    vec![centre.clone()]
                } else {
                    origins
                },
                centre,
            })
        })
        .collect()
}

/// What one texel receives: per reference its weight and the sum of the directions its rays
/// arrive from. References are the scene's values ([`Scene`]: each system's clusters, then its
/// tree's parents), then the environment's values from [`Refs::environment`].
#[derive(Default, Clone)]
struct Gathered {
    refs: Vec<(u32, (f32, V3))>,
}

/// How the references of a texel are numbered: the systems' values first.
#[derive(Clone, Copy)]
pub(super) struct Refs {
    /// The first environment reference.
    pub(super) environment: u32,
}

fn gather(origins: &[Origin], scene: &Scene, rays: &[V3]) -> Gathered {
    let mut acc: std::collections::HashMap<u32, (f32, V3)> = std::collections::HashMap::new();
    let share = 1.0 / rays.len() as f32;
    for (k, &d) in rays.iter().enumerate() {
        // Each ray takes an origin of its own scrambled index: the rays of one origin spread
        // over the whole hemisphere (consecutive rays sweep its azimuths, see [`hemisphere`]).
        let origin = &origins[scramble(k as u32) as usize % origins.len()];
        let start = add(
            origin.p,
            scale(origin.n, push_off(origin.p, origin.texel_size)),
        );
        let dir = around(origin.n, d);
        let reference = match scene.bvh.intersect(start, dir, f32::INFINITY) {
            // A back face (inside other geometry) or a part no cluster holds: no light.
            Some(hit) => match scene.reference(&hit) {
                Some(r) => r,
                None => continue,
            },
            None => scene.refs.environment + environment_texel(dir),
        };
        let e = acc.entry(reference).or_default();
        e.0 += share;
        e.1 = add(e.1, dir);
    }
    let mut refs: Vec<(u32, (f32, V3))> = acc.into_iter().collect();
    refs.sort_unstable_by_key(|r| r.0);
    Gathered { refs }
}

/// A bijective scramble of 32-bit indices (the finalizer of MurmurHash3).
fn scramble(mut k: u32) -> u32 {
    k ^= k >> 16;
    k = k.wrapping_mul(0x85eb_ca6b);
    k ^= k >> 13;
    k = k.wrapping_mul(0xc2b2_ae35);
    k ^ (k >> 16)
}

/// The value one coarser than `r`: a cluster's or parent's parent in the tree, an
/// environment value's in the next level (each the mean of the 2x2 under it); `None` at the top.
fn coarser(r: u32, refs: Refs, tree_parent: &[u32]) -> Option<u32> {
    if r < refs.environment {
        return tree_parent
            .get(r as usize)
            .copied()
            .filter(|&p| p != u32::MAX);
    }
    let e = (r - refs.environment) as usize;
    let (size, start) = level_of(e)?;
    if size == 1 {
        return None;
    }
    let (face, rest) = ((e - start) / (size * size), (e - start) % (size * size));
    let (x, y) = (rest % size, rest / size);
    let half = size / 2;
    let next = start + 6 * size * size;
    Some(refs.environment + (next + face * half * half + (y / 2) * half + x / 2) as u32)
}

/// The values one coarser value stands for, and the share of it each child has: a tree parent's
/// children (their weights), a coarser environment value's 2x2 finer ones (a quarter each).
/// Over the values of a scene's systems, each numbered from its base.
pub(super) struct Hierarchy {
    refs: Refs,
    tree_parent: Vec<u32>,
    tree_children: Vec<Vec<(u32, f32)>>,
}

impl Hierarchy {
    /// `inputs`: each system's input samples, its values numbered from `bases`.
    pub(super) fn new(inputs: &[&crate::data::InputSamples], bases: &[u32], refs: Refs) -> Self {
        let total = refs.environment as usize;
        let mut tree_parent = vec![u32::MAX; total];
        let mut tree_children = vec![Vec::new(); total];
        for (input, &base) in inputs.iter().zip(bases) {
            let clusters = input.clusters();
            let mut child = 0usize;
            for (p, &count) in input.tree_children.iter().enumerate() {
                let parent = base + (clusters + p) as u32;
                for c in child..child + count as usize {
                    tree_parent[base as usize + c] = parent;
                    tree_children[parent as usize]
                        .push((base + c as u32, input.tree_weights[c] as f32 / 255.0));
                }
                child += count as usize;
            }
        }
        Hierarchy {
            refs,
            tree_parent,
            tree_children,
        }
    }

    /// The children of `p` (a reference one coarser) and their shares.
    fn children(&self, p: u32, out: &mut Vec<(u32, f32)>) {
        out.clear();
        if p < self.refs.environment {
            if let Some(list) = self.tree_children.get(p as usize) {
                out.extend_from_slice(list);
            }
            return;
        }
        // The environment value's level and place, and the four under it in the level before.
        let e = (p - self.refs.environment) as usize;
        let Some((half, start)) = level_of(e) else {
            return;
        };
        if half == RESOLUTION {
            return;
        }
        let size = 2 * half;
        let finer = start - 6 * size * size;
        let (face, rest) = ((e - start) / (half * half), (e - start) % (half * half));
        let (x, y) = (rest % half, rest / half);
        for (dx, dy) in [(0, 0), (1, 0), (0, 1), (1, 1)] {
            let fine = finer + face * size * size + (2 * y + dy) * size + 2 * x + dx;
            out.push((self.refs.environment + fine as u32, 0.25));
        }
    }
}

/// A reference's weight that groups of references can be joined by.
pub(super) trait Weight: Copy + Default {
    /// Adds `other`'s weight.
    fn join(&mut self, other: &Self);
    /// The parts of it the joining compares (the light it takes, per coefficient).
    fn parts(&self) -> &[f32];
}

/// A texel's weight of a reference and the sum of the directions its rays arrive from.
impl Weight for (f32, V3) {
    fn join(&mut self, other: &Self) {
        self.0 += other.0;
        self.1 = add(self.1, other.1);
    }
    fn parts(&self) -> &[f32] {
        std::slice::from_ref(&self.0)
    }
}

/// Cuts references (sorted, each with its weight) down to `budget`: groups that share a coarser
/// value are replaced by it with their joined weight, those first whose weights are nearest the
/// coarser value's own shares of its children (where the replacement changes least); when no
/// two share one, the cheapest are raised to their coarser values. Until few enough are left or
/// all are the coarsest (the tree's roots, the environment's six faces).
pub(super) fn reduce<W: Weight>(refs: &mut Vec<(u32, W)>, budget: usize, h: &Hierarchy) {
    let mut children = Vec::new();
    while refs.len() > budget {
        let mut groups: std::collections::BTreeMap<u32, usize> = Default::default();
        for &(r, _) in refs.iter() {
            if let Some(p) = coarser(r, h.refs, &h.tree_parent) {
                *groups.entry(p).or_default() += 1;
            }
        }
        // Each joinable group's change: how far its weights are from the coarser value's
        // shares of their sum, over all its children and every part of the weights.
        let parts = refs.first().map_or(0, |r| r.1.parts().len());
        let weight = |r: u32, k: usize| {
            refs.binary_search_by_key(&r, |x| x.0)
                .map_or(0.0, |i| refs[i].1.parts()[k])
        };
        let mut joinable: Vec<(f32, u32, usize)> = Vec::new();
        for (&p, &n) in &groups {
            h.children(p, &mut children);
            let mut change = 0.0;
            for k in 0..parts {
                let total: f32 = children.iter().map(|&(c, _)| weight(c, k)).sum();
                change += children
                    .iter()
                    .map(|&(c, share)| (weight(c, k) - total * share).abs())
                    .sum::<f32>();
            }
            joinable.push((change, p, n));
        }
        if joinable.is_empty() {
            return;
        }
        joinable.sort_by(|a, b| a.0.total_cmp(&b.0).then(a.1.cmp(&b.1)));
        let mut excess = refs.len() - budget;
        let mut chosen = std::collections::HashSet::new();
        if joinable.iter().any(|&(_, _, n)| n > 1) {
            for &(_, p, n) in joinable.iter().filter(|j| j.2 > 1) {
                if excess == 0 {
                    break;
                }
                chosen.insert(p);
                excess = excess.saturating_sub(n - 1);
            }
        } else {
            // No two share a coarser value: the cheapest are raised to theirs (as many as are
            // too many), so that they meet others there.
            chosen.extend(joinable.iter().take(excess).map(|j| j.1));
        }
        let mut merged: std::collections::BTreeMap<u32, W> = Default::default();
        for &(r, w) in refs.iter() {
            let key = match coarser(r, h.refs, &h.tree_parent) {
                Some(p) if chosen.contains(&p) => p,
                _ => r,
            };
            merged.entry(key).or_default().join(&w);
        }
        *refs = merged.into_iter().collect();
    }
}

/// The index of the direction of [`DIRECTIONS`] closest to `d` (0: none).
fn direction_index(d: V3) -> u8 {
    if dot(d, d) <= 0.0 {
        return 0;
    }
    let d = normalized(d);
    let mut best = (0u8, f32::NEG_INFINITY);
    for (i, v) in DIRECTIONS.iter().enumerate().skip(1) {
        let c = dot(*v, d);
        if c > best.1 {
            best = (i as u8, c);
        }
    }
    best.0
}

/// The precompute's settings of a system (its Lightmap Parameters).
#[derive(Debug, Clone, Copy)]
pub struct Settings {
    /// Rays per texel: Irradiance Quality.
    pub rays: usize,
    /// References per texel at most: Irradiance Budget.
    pub budget: usize,
}

/// Lightmap Parameters' defaults.
pub const DEFAULT_SETTINGS: Settings = Settings {
    rays: 8192,
    budget: 128,
};

/// The transport of system `own` of `scene` (its renderers `instances`): its texels' light from
/// every system's clusters and the environment. `chart_mask`: per output texel its chart (the
/// directionality is interpolated within charts).
pub fn transport(
    instances: &[Instance],
    scene: &Scene,
    own: usize,
    width: u32,
    height: u32,
    chart_mask: &[u32],
    settings: Settings,
) -> Result<Transport> {
    ensure!(own < scene.systems.len(), "no system {own} in the scene");
    let clustering = scene.systems[own];
    ensure!(
        chart_mask.len() == (width * height) as usize,
        "a chart mask of {} texels for {width}x{height}",
        chart_mask.len()
    );
    ensure!(
        settings.rays > 0 && settings.budget > 0,
        "an irradiance quality of {} and budget of {}",
        settings.rays,
        settings.budget
    );
    let footprints = footprints(instances, width, height);
    let rays = hemisphere(settings.rays);
    let test = hemisphere(CULL_RAYS);
    // A texel's origins that other geometry does not hide (also in a gap its renderer's
    // Modelling Tolerance ignores).
    let visible: Vec<Option<Vec<Origin>>> = footprints
        .par_iter()
        .map(|f| {
            f.as_ref().map(|f| {
                f.origins
                    .iter()
                    .filter(|o| {
                        let push = push_off(o.p, o.texel_size);
                        let gap = instances[o.instance].modelling_tolerance;
                        !hidden(&scene.bvh, &test, o.p, o.n, push, gap)
                    })
                    .cloned()
                    .collect()
            })
        })
        .collect();
    let mut gathered: Vec<Option<Gathered>> = visible
        .par_iter()
        .map(|o| match o {
            Some(list) if !list.is_empty() => {
                let mut g = gather(list, scene, &rays);
                reduce(&mut g.refs, settings.budget, &scene.hierarchy);
                Some(g)
            }
            Some(_) => Some(Gathered::default()),
            None => None,
        })
        .collect();
    // A texel all of whose footprint is hidden takes what the nearest texel of its chart with
    // a visible one receives.
    let empty: Vec<usize> = (0..gathered.len())
        .filter(|&at| visible[at].as_ref().is_some_and(Vec::is_empty))
        .collect();
    for at in empty {
        let (x, y) = ((at as u32 % width) as i64, (at as u32 / width) as i64);
        let chart = chart_mask[at];
        let mut best: Option<(i64, usize)> = None;
        for dy in -FILL_RADIUS..=FILL_RADIUS {
            for dx in -FILL_RADIUS..=FILL_RADIUS {
                let (nx, ny) = (x + dx, y + dy);
                if nx < 0 || ny < 0 || nx >= width as i64 || ny >= height as i64 {
                    continue;
                }
                let n = (ny * width as i64 + nx) as usize;
                let d = dx * dx + dy * dy;
                if chart_mask[n] == chart
                    && visible[n].as_ref().is_some_and(|v| !v.is_empty())
                    && best.is_none_or(|(bd, _)| d < bd)
                {
                    best = Some((d, n));
                }
            }
        }
        if let Some((_, n)) = best {
            gathered[at] = gathered[n].clone();
        }
    }

    // One segment per block of 2x2 texels; its first texel stands for the block.
    let mut segments = Vec::new();
    for by in 0..height.div_ceil(2) {
        for bx in 0..width.div_ceil(2) {
            let texels: Vec<(u16, u16)> = [(0, 0), (1, 0), (0, 1), (1, 1)]
                .iter()
                .map(|&(dx, dy)| (2 * bx + dx, 2 * by + dy))
                .filter(|&(x, y)| {
                    x < width && y < height && gathered[(y * width + x) as usize].is_some()
                })
                .map(|(x, y)| (x as u16, y as u16))
                .collect();
            if texels.is_empty() {
                continue;
            }
            let of = |t: (u16, u16)| {
                gathered[t.1 as usize * width as usize + t.0 as usize]
                    .as_ref()
                    .unwrap()
            };
            let mut refs: Vec<u32> = texels
                .iter()
                .flat_map(|&t| of(t).refs.iter().map(|r| r.0))
                .collect();
            refs.sort_unstable();
            refs.dedup();
            let mut max = vec![0f32; refs.len()];
            let mut dense = vec![0f32; texels.len() * refs.len()];
            for (t, &texel) in texels.iter().enumerate() {
                for &(r, (w, _)) in &of(texel).refs {
                    let k = refs.binary_search(&r).unwrap();
                    dense[t * refs.len() + k] = w;
                    max[k] = max[k].max(w);
                }
            }
            let reference_weights: Vec<u16> = max
                .iter()
                .map(|&m| (m * 65535.0).round().clamp(1.0, 65535.0) as u16)
                .collect();
            let weights: Vec<u8> = dense
                .iter()
                .enumerate()
                .map(|(i, &w)| {
                    let rw = reference_weights[i % refs.len()] as f32 / 65535.0;
                    (w / rw * 255.0).round().clamp(0.0, 255.0) as u8
                })
                .collect();
            let references: Vec<(u16, u32)> = refs
                .iter()
                .map(|&r| scene.reference_of(r, Some(own)))
                .collect();
            // The block's normal and each reference's direction, at its first texel.
            let first = of(texels[0]);
            let block_directions: Vec<u8> = refs
                .iter()
                .map(|&r| match first.refs.binary_search_by_key(&r, |x| x.0) {
                    Ok(i) => direction_index(first.refs[i].1.1),
                    Err(_) => 0,
                })
                .collect();
            let normal = footprints[texels[0].1 as usize * width as usize + texels[0].0 as usize]
                .as_ref()
                .map_or([0.0, 0.0, 1.0], |f| f.centre.n);
            // Only whole blocks have directionality data (the runtime reads blocks of 2x2).
            let whole = 2 * bx + 2 <= width && 2 * by + 2 <= height;
            // Environment references come first, as the dependencies are ordered.
            let mut order: Vec<usize> = (0..refs.len()).collect();
            order.sort_by_key(|&k| references[k]);
            segments.push(Segment {
                references: order.iter().map(|&k| references[k]).collect(),
                reference_weights: order.iter().map(|&k| reference_weights[k]).collect(),
                weights: (0..texels.len())
                    .flat_map(|t| order.iter().map(move |&k| (t, k)))
                    .map(|(t, k)| weights[t * refs.len() + k])
                    .collect(),
                texels,
                copies: Vec::new(),
                block_normals: if whole { vec![normal] } else { Vec::new() },
                block_directions: if whole {
                    order.iter().map(|&k| block_directions[k]).collect()
                } else {
                    Vec::new()
                },
                block_copies: 0,
            });
        }
    }

    // Per block, which neighbouring blocks share its chart.
    let (bw, bh) = (width / 2, height / 2);
    let chart_of_block = |x: i64, y: i64| -> Option<u32> {
        if x < 0 || y < 0 || x >= bw as i64 || y >= bh as i64 {
            return None;
        }
        (0..4)
            .map(|k| chart_mask[((2 * y + k / 2) * width as i64 + 2 * x + k % 2) as usize])
            .find(|&c| c != crate::data::NO_CHART)
    };
    let mut block_neighbours = vec![0u8; (bw * bh) as usize];
    for y in 0..bh as i64 {
        for x in 0..bw as i64 {
            let Some(own) = chart_of_block(x, y) else {
                continue;
            };
            let mut bits = 0;
            for (dx, dy, bit) in [
                (-1, 0, NEIGHBOUR_LEFT),
                (1, 0, NEIGHBOUR_RIGHT),
                (0, -1, NEIGHBOUR_BELOW),
                (0, 1, NEIGHBOUR_ABOVE),
                (-1, -1, NEIGHBOUR_BELOW_LEFT),
                (-1, 1, NEIGHBOUR_ABOVE_LEFT),
                (1, -1, NEIGHBOUR_BELOW_RIGHT),
                (1, 1, NEIGHBOUR_ABOVE_RIGHT),
            ] {
                if chart_of_block(x + dx, y + dy) == Some(own) {
                    bits |= bit;
                }
            }
            block_neighbours[(y * bw as i64 + x) as usize] = bits;
        }
    }

    Ok(Transport {
        width,
        height,
        dependencies: scene.dependencies(Some(own)),
        segments,
        block_neighbours,
        block_error: None,
        bounce_samples: clustering.bounce_samples.clone(),
        bounce: clustering.bounce.clone(),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_environment_value_is_a_child_of_its_coarser_one() {
        let refs = Refs { environment: 100 };
        let h = Hierarchy {
            refs,
            tree_parent: Vec::new(),
            tree_children: Vec::new(),
        };
        let mut children = Vec::new();
        let mut seen = 0;
        for e in 0..ENVIRONMENT_VALUES as u32 {
            let r = refs.environment + e;
            match coarser(r, refs, &h.tree_parent) {
                Some(p) => {
                    h.children(p, &mut children);
                    assert_eq!(children.len(), 4);
                    assert!(
                        children.iter().any(|&(c, share)| c == r && share == 0.25),
                        "{e}"
                    );
                    seen += 1;
                }
                // The last level, one value per face.
                None => assert!(e as usize >= ENVIRONMENT_VALUES - 6),
            }
        }
        assert_eq!(seen, ENVIRONMENT_VALUES - 6);
    }
}
