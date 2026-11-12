//! Clusters: the input samples of a system. Its surfaces are cut by a grid of voxels of the
//! cluster size; the connected parts of one renderer within a voxel that face one half-space
//! form a cluster, and parts too small to be one join a neighbour. Each cluster gets points spread evenly over
//! its surface at the precompute's density (what a cluster receives is the mean over them), in
//! groups of four, and the texels of the system's material textures and output its points lie
//! in. The clusters form a tree whose parents stand for groups of them.
use super::geometry::Bvh;
use super::math::{V3, add, cross, dot, frame, lerp, normalized, radical_inverse, scale, sub};
use crate::data::{ClusterMaterials, InputSamples};
use anyhow::{Result, ensure};
use rayon::prelude::*;
use std::collections::HashMap;

/// Points per cluster of a full voxel face (the Unity 6000.0.71f1 Editor's precompute places
/// 32 points per square of the cluster size, in groups of four).
pub const SAMPLES_PER_CLUSTER: f32 = 32.0;
/// Points come in groups of this many neighbours (as the input samples store them; a cluster
/// most of which other geometry hides may keep fewer).
pub const GROUP: usize = 4;
/// A part of a voxel no larger than this share of a voxel face joins neighbours (the Unity
/// 6000.0.71f1 Editor's precompute of tests/projects/realtimegi: its smallest cluster covers 0.4
/// of a face, the largest part that joins others 0.2).
const MIN_CLUSTER_SHARE: f32 = 0.4;
/// Parts whose normals are closer than this (cosine) are one plane.
const COPLANAR: f32 = 0.999;
/// Candidate points per point kept, from which the kept ones are spread evenly.
const CANDIDATES: usize = 8;
/// Rays that test whether a point lies inside other geometry, and how many of them may meet a
/// back face before it does.
const CULL_RAYS: usize = 16;
const CULL_BACK_FACES: usize = 4;
/// Children per parent of the cluster tree at most.
const TREE_CHILDREN: usize = 8;
/// The cluster of a triangle no cluster holds: other geometry hides all of its part.
pub const NO_CLUSTER: u32 = u32::MAX;
/// Voxel parts of a system at most (the precompute's memory grows with them).
const MAX_PIECES: usize = 2_000_000;
/// How far ray origins are pushed off their surface, relative to the cluster size, and relative
/// to their coordinates (16 units of the last place of a float).
const PUSH_OFF: f32 = 1e-4;
const PUSH_OFF_ROUNDING: f32 = 16.0 * f32::EPSILON;

/// One renderer of a system as the precompute sees it.
pub struct Instance<'a> {
    /// World space.
    pub positions: &'a [V3],
    pub normals: &'a [V3],
    /// Realtime lightmap UVs in texels of the system's output.
    pub texels: &'a [[f32; 2]],
    pub triangles: &'a [[u32; 3]],
    /// The voxel size of its clusters (world units).
    pub cluster_size: f32,
    /// Lightmap Parameters' Modelling Tolerance: its surfaces facing geometry no farther than
    /// this are left out.
    pub modelling_tolerance: f32,
}

#[derive(Debug, Clone, Copy)]
struct Vertex {
    p: V3,
    n: V3,
    uv: [f32; 2],
}

fn vlerp(a: Vertex, b: Vertex, t: f32) -> Vertex {
    Vertex {
        p: lerp(a.p, b.p, t),
        n: lerp(a.n, b.n, t),
        uv: [
            a.uv[0] + (b.uv[0] - a.uv[0]) * t,
            a.uv[1] + (b.uv[1] - a.uv[1]) * t,
        ],
    }
}

/// A convex part of a triangle within one voxel (of its renderer's cluster size).
struct Piece {
    polygon: Vec<Vertex>,
    instance: u32,
    voxel: [i32; 3],
    normal: V3,
    area: f32,
    size: f32,
}

/// The parts of the triangle `t` in the voxels it crosses: cut into slabs along x, the slabs
/// along y, those along z, so that only the voxels the triangle meets are visited.
fn dice(t: [Vertex; 3], origin: V3, size: f32, instance: u32, out: &mut Vec<Piece>) {
    let normal = {
        let n = cross(sub(t[1].p, t[0].p), sub(t[2].p, t[0].p));
        let facing = add(add(t[0].n, t[1].n), t[2].n);
        normalized(if dot(n, facing) < 0.0 {
            scale(n, -1.0)
        } else {
            n
        })
    };
    if !normal.iter().all(|v| v.is_finite()) || dot(normal, normal) == 0.0 {
        // Degenerate or not finite: no surface.
        return;
    }
    // Each slab is the closed interval from its lower plane to the next one's, computed the one
    // way; a piece flat on a slab's upper plane belongs to the next slab only.
    let plane = |k: usize, i: i32| origin[k] + i as f32 * size;
    let range = |polygon: &[Vertex], k: usize| {
        let lo = polygon.iter().map(|v| v.p[k]).fold(f32::INFINITY, f32::min);
        let hi = polygon
            .iter()
            .map(|v| v.p[k])
            .fold(f32::NEG_INFINITY, f32::max);
        // One slab more on either side than the division gives: it may round across a plane.
        let cell = |x: f32| ((x - origin[k]) / size).floor() as i32;
        cell(lo).saturating_sub(1)..=cell(hi).saturating_add(1)
    };
    let slab = |polygon: &[Vertex], k: usize, i: i32| -> Option<Vec<Vertex>> {
        let (lo, hi) = (plane(k, i), plane(k, i + 1));
        let part = clip(&clip(polygon, k, lo, 1.0), k, hi, -1.0);
        let flat_on_top = part.iter().all(|v| v.p[k] == hi);
        (part.len() >= 3 && !(flat_on_top && lo < hi)).then_some(part)
    };
    for x in range(&t, 0) {
        let Some(px) = slab(&t, 0, x) else { continue };
        for y in range(&px, 1) {
            let Some(py) = slab(&px, 1, y) else { continue };
            for z in range(&py, 2) {
                let Some(polygon) = slab(&py, 2, z) else {
                    continue;
                };
                let area = polygon_area(&polygon);
                if area > 0.0 {
                    out.push(Piece {
                        polygon,
                        instance,
                        voxel: [x, y, z],
                        normal,
                        area,
                        size,
                    });
                }
            }
        }
    }
}

/// How many voxel parts the triangle `p` is cut into at most by the grid from `origin` of
/// cluster size `s`: no more than the voxels its bounds span, nor than its projected areas and
/// its perimeter cross.
fn parts_bound(p: [V3; 3], origin: V3, s: f32) -> f64 {
    let span: f64 = (0..3)
        .map(|k| {
            let cell = |x: f32| ((x - origin[k]) / s).floor() as f64;
            let lo = p.iter().map(|q| q[k]).fold(f32::INFINITY, f32::min);
            let hi = p.iter().map(|q| q[k]).fold(f32::NEG_INFINITY, f32::max);
            cell(hi) - cell(lo) + 1.0
        })
        .product();
    // The voxels a plane crosses grow with its areas projected on the three axis planes.
    let projected: f32 = cross(sub(p[1], p[0]), sub(p[2], p[0]))
        .iter()
        .map(|v| 0.5 * v.abs())
        .sum();
    let edges: f32 = (0..3)
        .map(|e| super::math::length(sub(p[(e + 1) % 3], p[e])))
        .sum();
    span.min((projected / (s * s) + 3.0 * edges / s + 8.0) as f64)
}

/// The part of `polygon` on the side `sign * (x[axis] - plane) >= 0`.
fn clip(polygon: &[Vertex], axis: usize, plane: f32, sign: f32) -> Vec<Vertex> {
    let mut out = Vec::with_capacity(polygon.len() + 1);
    for i in 0..polygon.len() {
        let (a, b) = (polygon[i], polygon[(i + 1) % polygon.len()]);
        let (da, db) = (sign * (a.p[axis] - plane), sign * (b.p[axis] - plane));
        if da >= 0.0 {
            out.push(a);
        }
        if (da >= 0.0) != (db >= 0.0) {
            let mut v = vlerp(a, b, da / (da - db));
            // Exactly on the plane, whatever the rounding of the interpolation.
            v.p[axis] = plane;
            out.push(v);
        }
    }
    out
}

fn polygon_area(polygon: &[Vertex]) -> f32 {
    let mut sum = [0.0; 3];
    for i in 1..polygon.len() - 1 {
        sum = add(
            sum,
            cross(
                sub(polygon[i].p, polygon[0].p),
                sub(polygon[i + 1].p, polygon[0].p),
            ),
        );
    }
    0.5 * dot(sum, sum).sqrt()
}

/// A position as a hashable key, to join parts that share a corner.
fn key(p: V3) -> [i32; 3] {
    p.map(|v| (v * 4096.0).round() as i32)
}

struct UnionFind(Vec<u32>);

impl UnionFind {
    fn new(n: usize) -> Self {
        UnionFind((0..n as u32).collect())
    }
    fn find(&mut self, mut a: u32) -> u32 {
        while self.0[a as usize] != a {
            let up = self.0[self.0[a as usize] as usize];
            self.0[a as usize] = up;
            a = up;
        }
        a
    }
    fn union(&mut self, a: u32, b: u32) {
        let (a, b) = (self.find(a), self.find(b));
        if a != b {
            self.0[a.max(b) as usize] = a.min(b);
        }
    }
}

/// A system's clusters and what the transport needs of them.
pub struct Clustering {
    pub input: InputSamples,
    pub materials: ClusterMaterials,
    /// Per cluster its first bounce sample; one more entry ends the last.
    pub bounce_samples: Vec<u32>,
    /// The output texels of each cluster's points and their share of them, most first.
    pub bounce: Vec<((u16, u16), f32)>,
    /// The surfaces cut into clusters, for tracing: per triangle its cluster ([`NO_CLUSTER`]
    /// for a surface other geometry hides).
    pub bvh: Bvh,
    pub triangle_cluster: Vec<u32>,
    /// Per cluster the surface area other geometry does not hide.
    pub areas: Vec<f32>,
}

/// Rays of a hemisphere: `count` unit directions around +z, cosine distributed.
pub fn hemisphere(count: usize) -> Vec<V3> {
    (0..count)
        .map(|i| {
            let u = (i as f32 + 0.5) / count as f32;
            let v = radical_inverse(i as u32);
            let r = u.sqrt();
            let phi = 2.0 * std::f32::consts::PI * v;
            [r * phi.cos(), r * phi.sin(), (1.0 - u).max(0.0).sqrt()]
        })
        .collect()
}

/// The direction `d` (around +z) around the unit normal `n`.
pub fn around(n: V3, d: V3) -> V3 {
    let (t, b) = frame(n);
    add(add(scale(t, d[0]), scale(b, d[1])), scale(n, d[2]))
}

/// How far a ray starts off the surface at `p`: a share of the cluster size, and far enough
/// from the surface for the rounding of coordinates as large as `p`'s.
pub(super) fn push_off(p: V3, size: f32) -> f32 {
    let magnitude = p.iter().fold(0f32, |m, v| m.max(v.abs()));
    PUSH_OFF * size + magnitude * PUSH_OFF_ROUNDING
}

/// Whether the point `p` of normal `n` is hidden: inside other geometry (too many of its rays
/// meet a back face) or in a gap no wider than `gap` (every ray meets geometry within that
/// height above its surface: Lightmap Parameters' Modelling Tolerance, "the maximum size of
/// gaps that can be ignored for GI").
pub(super) fn hidden(bvh: &Bvh, rays: &[V3], p: V3, n: V3, push: f32, gap: f32) -> bool {
    let origin = add(p, scale(n, push));
    let (mut back, mut closed) = (0, 0);
    for &d in rays {
        let dir = around(n, d);
        if let Some(h) = bvh.intersect(origin, dir, f32::INFINITY) {
            back += !h.front as usize;
            closed += (h.distance * dot(dir, n) <= gap) as usize;
        }
    }
    back > CULL_BACK_FACES || closed == rays.len()
}

/// The system's clusters. `width` and `height`: the system's output size in texels.
pub fn cluster(instances: &[Instance], width: u32, height: u32) -> Result<Clustering> {
    ensure!(!instances.is_empty(), "a system without renderers");
    ensure!(
        width > 0 && height > 0 && width <= u16::MAX as u32 && height <= u16::MAX as u32,
        "a system output of {width}x{height}"
    );
    let mut origin = [f32::INFINITY; 3];
    let mut estimate = 0f64;
    let mut source = Vec::new();
    let mut facing = Vec::new();
    for (i, inst) in instances.iter().enumerate() {
        ensure!(
            inst.cluster_size > 0.0 && inst.cluster_size.is_finite(),
            "renderer {i}: a cluster size of {}",
            inst.cluster_size
        );
        ensure!(
            inst.modelling_tolerance >= 0.0 && inst.modelling_tolerance.is_finite(),
            "renderer {i}: a modelling tolerance of {}",
            inst.modelling_tolerance
        );
        ensure!(
            inst.normals.len() == inst.positions.len() && inst.texels.len() == inst.positions.len(),
            "renderer {i}: vertex streams of different lengths"
        );
        for t in inst.triangles {
            ensure!(
                t.iter().all(|&v| (v as usize) < inst.positions.len()),
                "renderer {i}: a triangle past its vertices"
            );
            let p = t.map(|v| inst.positions[v as usize]);
            // The grid starts at the corner of the finite triangles' bounds.
            if p.iter().flatten().all(|v| v.is_finite()) {
                for q in p {
                    for k in 0..3 {
                        origin[k] = origin[k].min(q[k]);
                    }
                }
            }
            // The side its vertex normals face; its winding's where they cancel or are zero.
            let sum = add(
                add(inst.normals[t[0] as usize], inst.normals[t[1] as usize]),
                inst.normals[t[2] as usize],
            );
            facing.push(
                if dot(sum, sum) > 0.0 && sum.iter().all(|v| v.is_finite()) {
                    sum
                } else {
                    cross(sub(p[1], p[0]), sub(p[2], p[0]))
                },
            );
            source.push(p);
        }
    }
    ensure!(
        origin.iter().all(|v| v.is_finite()),
        "a system without finite triangles"
    );
    for inst in instances {
        for t in inst.triangles {
            let p = t.map(|v| inst.positions[v as usize]);
            if p.iter().flatten().all(|v| v.is_finite()) {
                estimate += parts_bound(p, origin, inst.cluster_size);
            }
        }
    }
    ensure!(
        estimate <= MAX_PIECES as f64,
        "realtime GI: the cluster sizes cut the system into about {estimate:.0} parts, more than {MAX_PIECES}"
    );
    // Points inside other geometry are found against the renderers' own triangles.
    let scene = Bvh::new(source, facing);

    let mut pieces = Vec::new();
    for (i, inst) in instances.iter().enumerate() {
        for t in inst.triangles {
            let v = t.map(|v| Vertex {
                p: inst.positions[v as usize],
                n: inst.normals[v as usize],
                uv: inst.texels[v as usize],
            });
            dice(v, origin, inst.cluster_size, i as u32, &mut pieces);
            // A second bound, should the estimate's rounding fall short.
            ensure!(
                pieces.len() <= 2 * MAX_PIECES,
                "realtime GI: renderer {i} cuts the system into more than {} parts",
                2 * MAX_PIECES
            );
        }
    }
    ensure!(!pieces.is_empty(), "a system without surface area");

    // Parts: within a voxel, the pieces of one renderer that touch (share a corner position)
    // and whose normals are all within a right angle of each other, gathered in the pieces'
    // order: a piece joins the first part it can and merges the other parts it can join too
    // (as the Unity 6000.0.71f1 Editor's precompute groups a voxel's triangles).
    let mut by_voxel: HashMap<([i32; 3], u32), Vec<u32>> = HashMap::new();
    for (k, piece) in pieces.iter().enumerate() {
        by_voxel
            .entry((piece.voxel, piece.instance))
            .or_default()
            .push(k as u32);
    }
    let mut parts = UnionFind::new(pieces.len());
    /// A part of a voxel being gathered: its pieces, their corners and normals, and a cone
    /// round its first normal that holds them all (the cosine of its half angle).
    struct Clump {
        members: Vec<u32>,
        corners: std::collections::HashSet<[i32; 3]>,
        normals: Vec<V3>,
        axis: V3,
        radius: f32,
    }
    impl Clump {
        /// Whether `n` faces the half-space of every normal: at once when the cone and `n` are
        /// less than a right angle apart, else normal by normal.
        fn faces(&self, n: V3) -> bool {
            let quick = self.radius.acos() + dot(self.axis, n).clamp(-1.0, 1.0).acos();
            quick < std::f32::consts::FRAC_PI_2 - 1e-4
                || self.normals.iter().all(|&m| dot(m, n) > 0.0)
        }
        fn add(&mut self, n: V3) {
            self.normals.push(n);
            self.radius = self.radius.min(dot(self.axis, n));
        }
    }
    for list in by_voxel.values() {
        let mut clumps: Vec<Clump> = Vec::new();
        for &k in list {
            let piece = &pieces[k as usize];
            let corners: std::collections::HashSet<[i32; 3]> =
                piece.polygon.iter().map(|v| key(v.p)).collect();
            let joins = |c: &Clump| !c.corners.is_disjoint(&corners) && c.faces(piece.normal);
            let Some(first) = clumps.iter().position(joins) else {
                clumps.push(Clump {
                    members: vec![k],
                    corners,
                    normals: vec![piece.normal],
                    axis: piece.normal,
                    radius: 1.0,
                });
                continue;
            };
            let target = &mut clumps[first];
            target.members.push(k);
            target.corners.extend(corners.iter().copied());
            target.add(piece.normal);
            // The later parts this piece touches merge in when all their normals and the
            // part's face one half-space.
            let mut i = first + 1;
            while i < clumps.len() {
                let fits = !clumps[i].corners.is_disjoint(&corners)
                    && clumps[i].normals.iter().all(|&n| clumps[first].faces(n));
                if fits {
                    let other = clumps.remove(i);
                    let target = &mut clumps[first];
                    target.members.extend(other.members);
                    target.corners.extend(other.corners);
                    for n in other.normals {
                        target.add(n);
                    }
                } else {
                    i += 1;
                }
            }
        }
        for clump in clumps {
            for &m in &clump.members[1..] {
                parts.union(clump.members[0], m);
            }
        }
    }
    let part_of: Vec<u32> = (0..pieces.len() as u32).map(|k| parts.find(k)).collect();
    let mut part_area: HashMap<u32, f32> = HashMap::new();
    for (k, p) in pieces.iter().enumerate() {
        *part_area.entry(part_of[k]).or_default() += p.area;
    }

    // Parts that touch (share a corner position), across voxels and renderers.
    let mut touching: HashMap<[i32; 3], Vec<u32>> = HashMap::new();
    for (k, piece) in pieces.iter().enumerate() {
        for v in &piece.polygon {
            let list = touching.entry(key(v.p)).or_default();
            if !list.contains(&part_of[k]) {
                list.push(part_of[k]);
            }
        }
    }
    let mut neighbours: HashMap<u32, Vec<u32>> = HashMap::new();
    for list in touching.values() {
        for &a in list {
            for &b in list {
                if a != b {
                    let n = neighbours.entry(a).or_default();
                    if !n.contains(&b) {
                        n.push(b);
                    }
                }
            }
        }
    }
    for n in neighbours.values_mut() {
        n.sort_unstable();
    }
    let normal_of = |part: u32| pieces[part as usize].normal;
    // Every cluster must exceed this area: 0.4 of a face of the smallest voxels (the smallest
    // cluster of the Unity 6000.0.71f1 Editor's precompute of tests/projects/realtimegi covers
    // 0.4 of a face; its systems are of one cluster size, several are not measured).
    let smallest = instances
        .iter()
        .map(|i| i.cluster_size)
        .fold(f32::INFINITY, f32::min);
    let min_area = MIN_CLUSTER_SHARE * smallest * smallest;

    // Small parts (no larger than that) first join touching small parts of their plane, until
    // the group is larger; what is still small joins the touching group of the closest normal.
    let mut order: Vec<u32> = part_area.keys().copied().collect();
    order.sort_unstable();
    let mut groups = UnionFind::new(pieces.len());
    let mut group_area: HashMap<u32, f32> = part_area.clone();
    for &part in &order {
        if part_area[&part] > min_area {
            continue;
        }
        for &other in neighbours.get(&part).map(Vec::as_slice).unwrap_or(&[]) {
            let (a, b) = (groups.find(part), groups.find(other));
            if group_area[&a] > min_area {
                break;
            }
            if a == b
                || part_area[&other] > min_area
                || dot(normal_of(part), normal_of(other)) <= COPLANAR
            {
                continue;
            }
            let sum = group_area[&a] + group_area[&b];
            groups.union(a, b);
            group_area.insert(groups.find(a), sum);
        }
    }
    for &part in &order {
        let g = groups.find(part);
        if group_area[&g] > min_area {
            continue;
        }
        let best = neighbours
            .get(&part)
            .into_iter()
            .flatten()
            .map(|&o| (o, groups.find(o)))
            .filter(|&(_, og)| og != g)
            .max_by(|&(a, ag), &(b, bg)| {
                let (da, db) = (
                    dot(normal_of(part), normal_of(a)),
                    dot(normal_of(part), normal_of(b)),
                );
                da.total_cmp(&db)
                    .then(group_area[&ag].total_cmp(&group_area[&bg]))
                    .then(b.cmp(&a))
            });
        if let Some((_, og)) = best {
            let sum = group_area[&g] + group_area[&og];
            groups.union(g, og);
            group_area.insert(groups.find(g), sum);
        }
    }

    // Clusters in the order of their voxels along a Morton curve (then of their first piece),
    // so that the tree's groups are runs of neighbours.
    let mut cluster_pieces: HashMap<u32, Vec<u32>> = HashMap::new();
    for k in 0..pieces.len() as u32 {
        cluster_pieces
            .entry(groups.find(part_of[k as usize]))
            .or_default()
            .push(k);
    }
    let mut clusters: Vec<Vec<u32>> = cluster_pieces.into_values().collect();
    for c in &mut clusters {
        c.sort_unstable();
    }
    // A piece's voxel in the grid of the smallest voxels (the voxels of renderers of other
    // cluster sizes are in other grids from the same corner).
    let cells: Vec<[i32; 3]> = pieces
        .iter()
        .map(|p| {
            p.voxel
                .map(|v| ((v as f32 + 0.5) * p.size / smallest).floor() as i32)
        })
        .collect();
    let cell_min = cells.iter().fold([i32::MAX; 3], |m, c| {
        std::array::from_fn(|k| m[k].min(c[k]))
    });
    let morton = |v: [i32; 3]| -> u64 {
        let mut code = 0u64;
        for bit in 0..21 {
            for k in 0..3 {
                code |= ((((v[k] - cell_min[k]) as u64) >> bit) & 1) << (3 * bit + k);
            }
        }
        code
    };
    clusters.sort_by_key(|c| (morton(cells[c[0] as usize]), c[0]));

    // Points, per cluster in parallel.
    let rays = hemisphere(CULL_RAYS);
    let gaps: Vec<f32> = instances.iter().map(|i| i.modelling_tolerance).collect();
    let made: Vec<ClusterPoints> = clusters
        .par_iter()
        .map(|list| cluster_points(list, &pieces, &gaps, &scene, &rays))
        .collect();
    let mut positions = Vec::new();
    let mut normals = Vec::new();
    let mut cluster_points_at = vec![0u32];
    let mut point_texels = Vec::new();
    let mut point_instances = Vec::new();
    let mut areas = Vec::with_capacity(clusters.len());
    let mut triangles = Vec::new();
    let mut tri_facing = Vec::new();
    let mut triangle_cluster = Vec::new();
    let mut kept = Vec::with_capacity(clusters.len());
    for (list, m) in clusters.iter().zip(made) {
        if m.points.is_empty() {
            triangle_cluster.extend(std::iter::repeat_n(NO_CLUSTER, m.triangles.len()));
            triangles.extend(m.triangles);
            tri_facing.extend(m.facing);
            continue;
        }
        let c = kept.len();
        kept.push(list.clone());
        for (v, instance) in m.points {
            positions.push(v.p);
            normals.push(v.n);
            let texel = v.uv.map(|x| x.floor().max(0.0));
            point_texels.push((
                (texel[0] as u32).min(width - 1) as u16,
                (texel[1] as u32).min(height - 1) as u16,
            ));
            point_instances.push(instance);
        }
        cluster_points_at.push(positions.len() as u32);
        areas.push(m.area);
        triangle_cluster.extend(std::iter::repeat_n(c as u32, m.triangles.len()));
        triangles.extend(m.triangles);
        tri_facing.extend(m.facing);
    }

    ensure!(
        !kept.is_empty(),
        "a system all of whose surface other geometry hides"
    );
    let kept_cells: Vec<[i32; 3]> = kept.iter().map(|c| cells[c[0] as usize]).collect();
    let (tree_children, tree_weights) = tree(&kept_cells, &areas);
    let input = InputSamples {
        positions,
        normals,
        cluster_points: cluster_points_at,
        tree_children,
        tree_weights,
    };
    let (materials, bounce_samples, bounce) =
        samples(&input, &point_texels, &point_instances, instances.len());
    Ok(Clustering {
        input,
        materials,
        bounce_samples,
        bounce,
        bvh: Bvh::new(triangles, tri_facing),
        triangle_cluster,
        areas,
    })
}

/// One cluster's points (and their renderers), its area other geometry does not hide, and its
/// surface as triangles with the side they face.
struct ClusterPoints {
    points: Vec<(Vertex, u32)>,
    area: f32,
    triangles: Vec<[V3; 3]>,
    facing: Vec<V3>,
}

fn cluster_points(
    list: &[u32],
    pieces: &[Piece],
    gaps: &[f32],
    scene: &Bvh,
    rays: &[V3],
) -> ClusterPoints {
    let area: f32 = list.iter().map(|&k| pieces[k as usize].area).sum();
    let list_size = pieces[list[0] as usize].size;
    // The points its pieces ask for at their renderers' cluster sizes.
    let points: f32 = list
        .iter()
        .map(|&k| {
            let p = &pieces[k as usize];
            p.area * SAMPLES_PER_CLUSTER / (p.size * p.size)
        })
        .sum();
    let wanted = (points / GROUP as f32).round().max(1.0) as usize * GROUP;
    // Candidates spread over the pieces by area, each with its piece's renderer.
    let mut candidates: Vec<(Vertex, u32)> = Vec::new();
    let total = wanted * CANDIDATES;
    let mut seen = 0f32;
    let (mut triangles, mut facing) = (Vec::new(), Vec::new());
    for &k in list {
        let piece = &pieces[k as usize];
        for i in 1..piece.polygon.len() - 1 {
            let tri = [piece.polygon[0], piece.polygon[i], piece.polygon[i + 1]];
            let a = 0.5
                * dot(
                    cross(sub(tri[1].p, tri[0].p), sub(tri[2].p, tri[0].p)),
                    piece.normal,
                )
                .abs();
            // This triangle's share of the candidates, by cumulative area.
            let from = (seen / area * total as f32).round() as usize;
            seen += a;
            let to = (seen / area * total as f32).round() as usize;
            for j in from..to {
                // Stratified along one barycentric axis, a radical inverse along the other.
                let s = ((j - from) as f32 + 0.5) / (to - from) as f32;
                let t = radical_inverse(j as u32 + 1);
                let r = s.sqrt();
                let (b1, b2) = (r * (1.0 - t), r * t);
                let at = |q: [V3; 3]| {
                    add(
                        q[0],
                        add(scale(sub(q[1], q[0]), b1), scale(sub(q[2], q[0]), b2)),
                    )
                };
                let n = at(tri.map(|v| v.n));
                candidates.push((
                    Vertex {
                        p: at(tri.map(|v| v.p)),
                        n: normalized(if dot(n, piece.normal) > 0.0 {
                            n
                        } else {
                            piece.normal
                        }),
                        uv: std::array::from_fn(|q| {
                            tri[0].uv[q]
                                + (tri[1].uv[q] - tri[0].uv[q]) * b1
                                + (tri[2].uv[q] - tri[0].uv[q]) * b2
                        }),
                    },
                    piece.instance,
                ));
            }
            triangles.push(tri.map(|v| v.p));
            facing.push(piece.normal);
        }
    }
    let visible: Vec<(Vertex, u32)> = candidates
        .iter()
        .copied()
        .filter(|(v, instance)| {
            let gap = gaps[*instance as usize];
            !hidden(scene, rays, v.p, v.n, push_off(v.p, list_size), gap)
        })
        .collect();
    // The cluster's area that other geometry does not hide; one all of which is hidden is no
    // cluster (its surface stays in the scene, as a part of no cluster).
    let shown = if candidates.is_empty() {
        0.0
    } else {
        area * visible.len() as f32 / candidates.len() as f32
    };
    let count = ((points * shown / area.max(f32::MIN_POSITIVE) / GROUP as f32)
        .round()
        .max(1.0) as usize
        * GROUP)
        .min(visible.len() / GROUP * GROUP)
        .max(GROUP.min(visible.len()));
    ClusterPoints {
        points: grouped(&spread(&visible, count))
            .into_iter()
            .flatten()
            .collect(),
        area: shown,
        triangles,
        facing,
    }
}

/// `count` of `pool`, spread evenly: each next one the farthest from those taken.
fn spread(pool: &[(Vertex, u32)], count: usize) -> Vec<(Vertex, u32)> {
    if pool.is_empty() || count == 0 {
        return Vec::new();
    }
    let count = count.min(pool.len());
    let mut nearest = vec![f32::INFINITY; pool.len()];
    let mut out = Vec::with_capacity(count);
    let mut next = 0;
    for _ in 0..count {
        out.push(pool[next]);
        let p = pool[next].0.p;
        let mut best = (0usize, -1f32);
        for (i, (v, _)) in pool.iter().enumerate() {
            let d = sub(v.p, p);
            nearest[i] = nearest[i].min(dot(d, d));
            if nearest[i] > best.1 {
                best = (i, nearest[i]);
            }
        }
        next = best.0;
    }
    out
}

/// `points` in groups of [`GROUP`] neighbours, in the order of their first points.
fn grouped(points: &[(Vertex, u32)]) -> Vec<Vec<(Vertex, u32)>> {
    let mut used = vec![false; points.len()];
    let mut out = Vec::new();
    for i in 0..points.len() {
        if used[i] {
            continue;
        }
        used[i] = true;
        let mut group = vec![points[i]];
        while group.len() < GROUP {
            let near = (0..points.len()).filter(|&j| !used[j]).min_by(|&a, &b| {
                let da = sub(points[a].0.p, points[i].0.p);
                let db = sub(points[b].0.p, points[i].0.p);
                dot(da, da).total_cmp(&dot(db, db))
            });
            match near {
                Some(j) => {
                    used[j] = true;
                    group.push(points[j]);
                }
                None => break,
            }
        }
        out.push(group);
    }
    out
}

/// The cluster tree: parents over runs of neighbouring clusters (`cells`: each cluster's voxel in
/// the grid of the smallest voxels), bottom up, each child weighted by its share of its parent's
/// area (in 255ths), up to a single root.
fn tree(cells: &[[i32; 3]], areas: &[f32]) -> (Vec<u8>, Vec<u8>) {
    let mut level: Vec<([i32; 3], f32)> =
        cells.iter().copied().zip(areas.iter().copied()).collect();
    let mut children = Vec::new();
    let mut weights = Vec::new();
    let mut shift = 1;
    while level.len() > 1 && shift < 31 {
        let mut next: Vec<([i32; 3], f32)> = Vec::new();
        let (mut level_children, mut level_weights) = (Vec::new(), Vec::new());
        let mut i = 0;
        while i < level.len() {
            let cell = level[i].0.map(|v| v >> shift);
            let mut j = i + 1;
            while j < level.len() && j - i < TREE_CHILDREN && level[j].0.map(|v| v >> shift) == cell
            {
                j += 1;
            }
            let area: f32 = level[i..j].iter().map(|c| c.1).sum();
            level_children.push((j - i) as u8);
            let mut given = 0u32;
            for (k, c) in level[i..j].iter().enumerate() {
                let w = if k + 1 == j - i {
                    255 - given.min(255)
                } else {
                    ((c.1 / area * 255.0).round() as u32).min(255 - given)
                };
                given += w;
                level_weights.push(w as u8);
            }
            next.push((level[i].0, area));
            i = j;
        }
        shift += 1;
        // A cell size that groups nothing adds no level.
        if next.len() < level.len() {
            children.extend(level_children);
            weights.extend(level_weights);
            level = next;
        }
    }
    (children, weights)
}

/// The material samples (per cluster and renderer, the texels of its points) and the bounce
/// resampling (per cluster, the output texels of its points by their share, most first).
fn samples(
    input: &InputSamples,
    texels: &[(u16, u16)],
    instances: &[u32],
    renderers: usize,
) -> (ClusterMaterials, Vec<u32>, Vec<((u16, u16), f32)>) {
    let mut materials = ClusterMaterials {
        instance_renderers: (0..renderers as u16).collect(),
        cluster_groups: vec![0],
        groups: Vec::new(),
        group_samples: vec![0],
        samples: Vec::new(),
    };
    let mut bounce_samples = vec![0u32];
    let mut bounce = Vec::new();
    for c in 0..input.clusters() {
        let range = input.cluster_points[c] as usize..input.cluster_points[c + 1] as usize;
        let n = range.len();
        // Per renderer in the order it first appears, the texels and their point counts.
        let mut by_renderer: Vec<(u32, Vec<((u16, u16), u16)>)> = Vec::new();
        let mut all: Vec<((u16, u16), u32)> = Vec::new();
        for p in range {
            let r = instances[p];
            let at = match by_renderer.iter().position(|g| g.0 == r) {
                Some(at) => at,
                None => {
                    by_renderer.push((r, Vec::new()));
                    by_renderer.len() - 1
                }
            };
            let list = &mut by_renderer[at].1;
            match list.iter_mut().find(|s| s.0 == texels[p]) {
                Some(s) => s.1 += 1,
                None => list.push((texels[p], 1)),
            }
            match all.iter_mut().find(|s| s.0 == texels[p]) {
                Some(s) => s.1 += 1,
                None => all.push((texels[p], 1)),
            }
        }
        for (r, list) in by_renderer {
            let points: u16 = list.iter().map(|s| s.1).sum();
            materials.groups.push((r as u16, points));
            materials.samples.extend(list);
            materials.group_samples.push(materials.samples.len() as u32);
        }
        materials.cluster_groups.push(materials.groups.len() as u32);
        // Stable: equal shares keep the order of their first points.
        all.sort_by(|a, b| b.1.cmp(&a.1));
        bounce.extend(all.iter().map(|&(t, k)| (t, k as f32 / n.max(1) as f32)));
        bounce_samples.push(bounce.len() as u32);
    }
    (materials, bounce_samples, bounce)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_part_bound_covers_a_tilted_triangle() {
        // A large triangle across the grid's diagonal crosses about the sum of its projected
        // areas in voxels, more than its own area.
        let n = 60.0;
        let p = [[0.0, 0.0, n], [n, 0.0, 0.0], [0.0, n, 0.0]];
        let v = p.map(|p| Vertex {
            p,
            n: [1.0, 1.0, 1.0],
            uv: [0.0; 2],
        });
        let mut pieces = Vec::new();
        dice(v, [-0.37; 3], 1.0, 0, &mut pieces);
        let bound = parts_bound(p, [-0.37; 3], 1.0);
        assert!(bound >= pieces.len() as f64, "{bound} < {}", pieces.len());
        assert!((pieces.len() as f64) > 0.5 * 1800.0 * 3f64.sqrt());
    }
}
