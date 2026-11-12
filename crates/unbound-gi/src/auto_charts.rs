//! Automatic realtime lightmap charts: what the Unity 6000.0.71f1 Editor's precompute makes of a
//! renderer with Optimize Realtime UVs (serialized as Preserve UVs off; tests/projects/realtimegi,
//! RoomAuto; docs/REALTIME_GI.md, Automatic charts).
//!
//! The mesh's triangles are grouped by connectivity. Groups that are flat within Max
//! Distance and Max Angle are joined in rounds: each round tries pairs of neighbouring
//! groups (a group is its own neighbour), drawn by a fixed pseudo-random sequence, fits a plane
//! to a pair, gathers the other groups on that plane that connect to it, and keeps the set with
//! the largest area seen along the plane. What is left over is folded into a joined set it is
//! small beside and near to, or stays a chart of its own. Every chart is then projected onto its
//! plane, in world units, with its axes turned to the legs of its right-angled triangles.
use crate::eigen::eigen_decomposition;
use anyhow::{Result, bail, ensure};

/// The limits of a renderer's Optimize Realtime UVs (Mesh Renderer, Lightmapping).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Params {
    /// Max Distance: how far a group's vertices may be from a plane it is joined on.
    pub max_distance: f32,
    /// Max Angle, in degrees: how far a group's normal may be from that plane's.
    pub max_angle: f32,
    /// Ignore Normals: vertices that differ only in their normal are one vertex.
    pub ignore_normals: bool,
}

/// One mesh instance that is not turned: positions and shading normals with the instance's
/// scale applied, the mesh's lightmap UVs and its triangles. (The precompute works with a
/// turned instance's positions and normals as its own removal of the rotation rounds them,
/// which decides some of its charts: such an instance is not given here.)
pub struct Mesh<'a> {
    pub positions: &'a [[f32; 3]],
    pub normals: &'a [[f32; 3]],
    pub uvs: &'a [[f32; 2]],
    pub triangles: &'a [[u32; 3]],
}

/// The charts made: each vertex's place in its chart, in world units from the chart's corner,
/// and each chart's triangles, in chart order.
#[derive(Debug, Clone, PartialEq)]
pub struct Charts {
    pub uvs: Vec<[f32; 2]>,
    pub triangles: Vec<Vec<usize>>,
}

/// Groups are neighbours when their boxes, each grown by this share of the distance limit,
/// meet. (The precompute's share for every renderer; the steps of RoomAuto bound it to between
/// 0.4 and 2/3.)
const NEIGHBOUR_REACH: f32 = 0.6;
/// A group whose triangles facing away from its normal are more than this share of its area is
/// not flat; a group smaller than this share of a joined set may be folded into it.
const SMALL_SHARE: f32 = 0.1;
/// Two measures closer than this share of the larger, and not equal, are a tie but for
/// rounding. Along the coordinate axes the arithmetic here is the precompute's to the last
/// digit; off them its normals differ from the ones here in theirs, and a choice between the
/// two is not made.
const TIE: f32 = 1e-5;
/// A group left on its own is planar when its vertices are within this share of its size of
/// its plane.
const PLANAR: f32 = 1e-5;
/// Pairs tried per round, and rounds at most: the precompute's own counts, which no renderer
/// setting changes.
const TRIES_PER_ROUND: u32 = 300;
const MAX_ROUNDS: usize = 100;
/// Areas and lengths below this count as none.
const TINY: f32 = f32::EPSILON;
/// A plane whose normal's third component is within this of zero takes its first axis from the
/// other two.
const BASIS_Z_EPSILON: f32 = 0.001;
/// A triangle's corner is a right angle when its edges' cosine is below this (within about 6
/// degrees). This and the next three are the precompute's own thresholds for turning a chart's
/// axes; RoomAuto's charts on tilted planes, laid out along their triangles' legs, follow them.
const RIGHT_ANGLE_COSINE: f32 = 0.1;
/// A leg lies in a plane when its cosine to the normal is below this (within about 10 degrees).
const IN_PLANE_COSINE: f32 = 0.17;
/// Directions closer than 3 degrees to parallel or to perpendicular are one direction.
const SAME_DIRECTION_COSINE: f32 = 0.998_629_5;
const PERPENDICULAR_COSINE: f32 = 0.052_335_96;

type V3 = [f32; 3];

fn sub(a: V3, b: V3) -> V3 {
    [a[0] - b[0], a[1] - b[1], a[2] - b[2]]
}

/// A dot product summed as four lanes are: (y) + (z + x).
fn dot(a: V3, b: V3) -> f32 {
    a[1] * b[1] + (a[2] * b[2] + a[0] * b[0])
}

fn cross(a: V3, b: V3) -> V3 {
    [
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    ]
}

fn scaled(a: V3, s: f32) -> V3 {
    [a[0] * s, a[1] * s, a[2] * s]
}

/// A vector of unit length, or itself when it has none.
fn normalized(a: V3) -> V3 {
    normalized_by(a, 0)
}

/// A plane: `n . p + d = 0`.
#[derive(Debug, Clone, Copy, PartialEq)]
struct Plane {
    n: [f64; 3],
    d: f64,
}

impl Plane {
    /// Whether the plane lies along two coordinate axes.
    fn along_axes(&self) -> bool {
        self.n.iter().filter(|x| **x != 0.0).count() == 1
    }
    fn normal(&self) -> V3 {
        [self.n[0] as f32, self.n[1] as f32, self.n[2] as f32]
    }
    fn flipped(&self) -> Plane {
        Plane {
            n: [-self.n[0], -self.n[1], -self.n[2]],
            d: -self.d,
        }
    }
    fn distance(&self, p: V3) -> f32 {
        let n = self.normal();
        ((self.d as f32 + p[1] * n[1]) + (p[2] * n[2] + p[0] * n[0])).abs()
    }
}

/// The frame a plane is projected in: a point of it and two unit axes.
#[derive(Debug, Clone, Copy)]
struct Frame {
    n: V3,
    origin: V3,
    u: V3,
    v: V3,
    /// The squared lengths the two axes were normalised from.
    squares: [f32; 2],
}

/// Off the coordinate axes the last digits of a unit vector here need not be the
/// precompute's. Two sets of the very same area there are taken as a tie only when they stay
/// one over a sweep of their axes' scales by this many units in the last place (`stays_tied`).
/// The sweep tells a tie by shape from one by rounding; it is not a bound on the precompute's
/// rounding.
const NORMALISATION_ULPS: i32 = 2;

/// `1 / sqrt(length2)` moved by `steps` units in the last place.
fn unit_scale(length2: f32, steps: i32) -> f32 {
    let scale = 1.0 / length2.sqrt();
    f32::from_bits(scale.to_bits().wrapping_add_signed(steps))
}

/// The vector scaled to unit length with its scale moved by `steps` units in the last place,
/// or itself when it has no length.
fn normalized_by(a: V3, steps: i32) -> V3 {
    let length2 = dot(a, a);
    if length2 > f32::MIN_POSITIVE {
        scaled(a, unit_scale(length2, steps))
    } else {
        a
    }
}

impl Frame {
    fn of(plane: &Plane) -> Frame {
        Frame::rounded(plane, 0, 0)
    }
    /// The frame with the scales of its axes moved by these many units in the last place.
    fn rounded(plane: &Plane, u_steps: i32, v_steps: i32) -> Frame {
        let n = plane.normal();
        let (d, length2) = (plane.d as f32, dot(n, n));
        let origin = n.map(|x| (-d * x) / length2);
        let first = if n[2].abs() <= BASIS_Z_EPSILON {
            [-n[1], n[0], 0.0]
        } else {
            [0.0, -n[2], n[1]]
        };
        let u = normalized_by(first, u_steps);
        let second = cross(n, u);
        let v = normalized_by(second, v_steps);
        Frame {
            n,
            origin,
            u,
            v,
            squares: [dot(first, first), dot(second, second)],
        }
    }
    fn project(&self, p: V3) -> [f32; 2] {
        let q = sub(p, self.origin);
        [dot(q, self.u), dot(q, self.v)]
    }
}

/// The standard Mersenne Twister with its default seed.
struct Random {
    state: [u32; 624],
    index: usize,
}

impl Random {
    fn new() -> Random {
        let mut state = [0u32; 624];
        state[0] = 5489;
        for i in 1..624 {
            state[i] = (state[i - 1] ^ (state[i - 1] >> 30))
                .wrapping_mul(1_812_433_253)
                .wrapping_add(i as u32);
        }
        Random { state, index: 624 }
    }
    fn next(&mut self) -> u32 {
        if self.index >= 624 {
            for i in 0..624 {
                let y = (self.state[i] & 0x8000_0000) | (self.state[(i + 1) % 624] & 0x7fff_ffff);
                let mut x = self.state[(i + 397) % 624] ^ (y >> 1);
                if y & 1 != 0 {
                    x ^= 0x9908_b0df;
                }
                self.state[i] = x;
            }
            self.index = 0;
        }
        let mut y = self.state[self.index];
        self.index += 1;
        y ^= y >> 11;
        y ^= (y << 7) & 0x9d2c_5680;
        y ^= (y << 15) & 0xefc6_0000;
        y ^ (y >> 18)
    }
}

/// What is known of one group of connected triangles.
struct Group {
    /// Its triangles: the first, then the others from the last down.
    triangles: Vec<usize>,
    /// Its vertices, ascending.
    vertices: Vec<usize>,
    min: V3,
    max: V3,
    area: f64,
    normal: V3,
    plane: Plane,
    /// Flat within the limits, and facing one way.
    candidate: bool,
}

struct Geometry<'a> {
    mesh: &'a Mesh<'a>,
    /// The mesh's triangles as the precompute holds them (`wound`).
    triangles: Vec<[u32; 3]>,
    max_distance: f32,
    cosine: f32,
    groups: Vec<Group>,
    /// Set when a test of facing was decided by less than the precompute's rounding.
    ambiguous: std::cell::Cell<bool>,
}

fn triangle_area(p: [V3; 3]) -> f32 {
    let c = cross(sub(p[1], p[0]), sub(p[2], p[0]));
    0.5 * dot(c, c).sqrt()
}

impl Geometry<'_> {
    /// Whether the plane faces as `normal` does, within the cosine. A cosine closer to the
    /// limit than the rounding of the precompute's normals (and of its cosine of Max Angle)
    /// could fall on the other side of it there: such a test marks the geometry ambiguous.
    fn faces(&self, plane: &Plane, normal: V3, cosine: f32) -> bool {
        let n = plane.normal();
        let d = dot(n, normal);
        let terms = (n[0] * normal[0]).abs() + (n[1] * normal[1]).abs() + (n[2] * normal[2]).abs();
        if (d - cosine).abs() <= COSINE_ROUNDING * terms + 2.0 * f32::EPSILON * cosine.abs() {
            self.ambiguous.set(true);
        }
        d >= cosine
    }
    fn corners(&self, triangle: usize) -> [V3; 3] {
        self.triangles[triangle].map(|v| self.mesh.positions[v as usize])
    }

    /// The plane through the groups' vertices that their squared distances are least from.
    fn fit(&self, groups: &[usize]) -> Plane {
        let mut sum = [0.0f64; 3];
        let mut count = 0usize;
        for &g in groups {
            for &v in &self.groups[g].vertices {
                let p = self.mesh.positions[v];
                for k in 0..3 {
                    sum[k] += p[k] as f64;
                }
            }
            count += self.groups[g].vertices.len();
        }
        let centre = sum.map(|s| s / count as f64);
        let mut m = [[0.0f64; 3]; 3];
        for &g in groups {
            for &v in &self.groups[g].vertices {
                let p = self.mesh.positions[v];
                let q = [0, 1, 2].map(|k| p[k] as f64 - centre[k]);
                m[0][0] += q[0] * q[0];
                m[0][2] += q[0] * q[2];
                m[0][1] += q[0] * q[1];
                m[1][1] += q[1] * q[1];
                m[1][2] += q[1] * q[2];
                m[2][2] += q[2] * q[2];
            }
        }
        m[1][0] = m[0][1];
        m[2][0] = m[0][2];
        m[2][1] = m[1][2];
        let (vectors, _) = eigen_decomposition(&m);
        let n = [vectors[0][0], vectors[1][0], vectors[2][0]];
        Plane {
            n,
            d: -((centre[0] * n[0] + n[1] * centre[1]) + n[2] * centre[2]),
        }
    }

    /// How far the group's furthest vertex is from the plane.
    fn reach(&self, group: usize, plane: &Plane) -> f32 {
        self.groups[group]
            .vertices
            .iter()
            .map(|&v| plane.distance(self.mesh.positions[v]))
            .fold(0.0, f32::max)
    }

    /// The area the groups' triangles cover seen along the plane's normal.
    fn projected_area(&self, groups: &[usize], plane: &Plane) -> f32 {
        self.area_in(groups, &Frame::of(plane))
    }

    /// The area the groups' triangles cover in the frame.
    fn area_in(&self, groups: &[usize], frame: &Frame) -> f32 {
        let mut area = 0.0f32;
        for &g in groups {
            for &t in &self.groups[g].triangles {
                let p = self.corners(t).map(|p| frame.project(p));
                let (a, b) = (
                    [p[1][0] - p[0][0], p[1][1] - p[0][1]],
                    [p[2][0] - p[0][0], p[2][1] - p[0][1]],
                );
                area += 0.5 * (b[1] * a[0] - a[1] * b[0]).abs();
            }
        }
        area
    }

    /// A group's box grown by what its neighbours may be beyond it.
    fn grown(&self, group: usize) -> (V3, V3) {
        let e = NEIGHBOUR_REACH * self.max_distance;
        let g = &self.groups[group];
        (g.min.map(|x| x - e), g.max.map(|x| x + e))
    }
}

fn boxes_meet(a: (V3, V3), b: (V3, V3)) -> bool {
    (0..3).all(|k| !(b.1[k] < a.0[k]) && !(a.1[k] < b.0[k]))
}

/// The triangles' groups: triangles that share a vertex, where vertices with one position,
/// lightmap UV and (unless normals are ignored) normal are one (tests/projects/realtimegi
/// RoomTurned, FoldKept and FoldIgnored). Groups come in the order of their lowest vertices.
fn groups(mesh: &Mesh, ignore_normals: bool) -> Vec<Vec<usize>> {
    let count = mesh.positions.len();
    let mut parent: Vec<usize> = (0..count).collect();
    fn find(parent: &mut [usize], mut v: usize) -> usize {
        while parent[v] != v {
            parent[v] = parent[parent[v]];
            v = parent[v];
        }
        v
    }
    let key = |v: usize| {
        let (p, t, n) = (mesh.positions[v], mesh.uvs[v], mesh.normals[v]);
        let n = if ignore_normals { [0.0; 3] } else { n };
        // Equal values are one key: a negative zero is a zero.
        [p[0], p[1], p[2], t[0], t[1], n[0], n[1], n[2]].map(|x| (x + 0.0).to_bits())
    };
    let mut order: Vec<usize> = (0..count).collect();
    order.sort_by_key(|&v| key(v));
    for pair in order.windows(2) {
        if key(pair[0]) == key(pair[1]) {
            let (a, b) = (find(&mut parent, pair[0]), find(&mut parent, pair[1]));
            parent[a.max(b)] = a.min(b);
        }
    }
    for t in mesh.triangles {
        let a = find(&mut parent, t[0] as usize);
        for &v in &t[1..] {
            let b = find(&mut parent, v as usize);
            let a = find(&mut parent, a);
            if a != b {
                parent[a.max(b)] = a.min(b);
            }
        }
    }
    // Roots are their groups' lowest vertices: groups in the order of their roots.
    let mut of_root = vec![usize::MAX; count];
    let mut used = vec![false; count];
    for t in mesh.triangles {
        used[find(&mut parent, t[0] as usize)] = true;
    }
    let mut out: Vec<Vec<usize>> = Vec::new();
    for v in 0..count {
        if parent[v] == v && used[v] {
            of_root[v] = out.len();
            out.push(Vec::new());
        }
    }
    for (i, t) in mesh.triangles.iter().enumerate() {
        out[of_root[find(&mut parent, t[0] as usize)]].push(i);
    }
    // A group lists its first triangle, then the others from the last down.
    for list in &mut out {
        list[1..].reverse();
    }
    out
}

/// The mesh's triangles as the precompute holds them: their corners in the opposite order
/// (its space is right-handed), and then each wound to face its vertices' normals (a triangle
/// whose corners already turn that way, as an inside-out mesh's do, stays as it is).
fn wound(mesh: &Mesh) -> Vec<[u32; 3]> {
    mesh.triangles
        .iter()
        .map(|t| {
            let [a, b, c] = [t[2], t[1], t[0]];
            let p = [a, b, c].map(|v| mesh.positions[v as usize]);
            let n = [a, b, c].map(|v| mesh.normals[v as usize]);
            let facing = cross(sub(p[1], p[0]), sub(p[2], p[0]));
            let shading = [0, 1, 2].map(|k| n[0][k] + n[1][k] + n[2][k]);
            if dot(facing, shading) < 0.0 {
                [a, c, b]
            } else {
                [a, b, c]
            }
        })
        .collect()
}

fn geometry<'a>(mesh: &'a Mesh<'a>, params: &Params) -> Geometry<'a> {
    let triangles = wound(mesh);
    let mut geometry = Geometry {
        mesh,
        triangles,
        max_distance: params.max_distance,
        cosine: (2.0 * (params.max_angle / 360.0) * std::f32::consts::PI).cos(),
        groups: Vec::new(),
        ambiguous: std::cell::Cell::new(false),
    };
    for triangles in groups(mesh, params.ignore_normals) {
        let mut vertices: Vec<usize> = triangles
            .iter()
            .flat_map(|&t| geometry.triangles[t].map(|v| v as usize))
            .collect();
        vertices.sort_unstable();
        vertices.dedup();
        let (mut min, mut max) = ([f32::MAX; 3], [f32::MIN; 3]);
        for &v in &vertices {
            for k in 0..3 {
                min[k] = min[k].min(mesh.positions[v][k]);
                max[k] = max[k].max(mesh.positions[v][k]);
            }
        }
        // The area and the area-weighted mean of the triangles' shading normals.
        let mut area = 0.0f64;
        let mut sum = [0.0f64; 3];
        let mut areas = Vec::with_capacity(triangles.len());
        let mut normals = Vec::with_capacity(triangles.len());
        for &t in &triangles {
            let [a, b, c] = geometry.triangles[t].map(|v| v as usize);
            let own = triangle_area([a, b, c].map(|v| mesh.positions[v]));
            let (na, nb, nc) = (mesh.normals[a], mesh.normals[b], mesh.normals[c]);
            let total = [0, 1, 2].map(|k| nb[k] + na[k] + nc[k]);
            let normal = if dot(total, total) > f32::MIN_POSITIVE {
                normalized(total)
            } else {
                na
            };
            if own > TINY {
                area += own as f64;
                for k in 0..3 {
                    sum[k] += normal[k] as f64 * own as f64;
                }
            }
            areas.push(own);
            normals.push(normal);
        }
        let degenerate = triangles.is_empty() || area < TINY as f64;
        let length = (sum[0] * sum[0] + sum[1] * sum[1] + sum[2] * sum[2]).sqrt();
        let has_normal = length > TINY as f64;
        let normal = if has_normal {
            sum.map(|s| (s * (1.0 / length)) as f32)
        } else {
            [0.0; 3]
        };
        geometry.groups.push(Group {
            triangles,
            vertices,
            min,
            max,
            area: if degenerate { 0.0 } else { area },
            normal,
            plane: Plane {
                n: [0.0; 3],
                d: 0.0,
            },
            candidate: false,
        });
        let index = geometry.groups.len() - 1;
        let mut plane = geometry.fit(&[index]);
        // (A plane nearly across the normal leaves the part not flat; its side does not
        // matter then.)
        if dot(plane.normal(), normal) < 0.0 {
            plane = plane.flipped();
        }
        let flat = has_normal
            && geometry.faces(&plane, normal, geometry.cosine)
            && geometry.reach(index, &plane) <= geometry.max_distance;
        // The share of its area that faces away from its normal.
        let away: f32 = areas
            .iter()
            .zip(&normals)
            .filter(|(_, n)| dot(**n, normal) <= 0.0)
            .map(|(a, _)| *a)
            .sum();
        let two_sided = degenerate || (away as f64 / area) > SMALL_SHARE as f64;
        let group = &mut geometry.groups[index];
        group.plane = plane;
        group.candidate = flat && !two_sided;
    }
    geometry
}

/// Which joined set each group is in, each set's plane, and how many sets were made by
/// joining.
struct Joined {
    of_group: Vec<usize>,
    planes: Vec<Plane>,
    joined: usize,
}

const NONE: usize = usize::MAX;

/// Each group's neighbours among the candidates: those whose grown boxes meet its own, itself
/// among them, ascending. An entry is struck out once its group is in a set.
struct Neighbours {
    lists: Vec<Vec<usize>>,
    struck: Vec<Vec<bool>>,
    stale: Vec<bool>,
}

impl Neighbours {
    fn new(geometry: &Geometry, candidates: &[usize]) -> Neighbours {
        let count = geometry.groups.len();
        let mut lists = vec![Vec::new(); count];
        for (i, &a) in candidates.iter().enumerate() {
            let own = geometry.grown(a);
            for &b in &candidates[i..] {
                if boxes_meet(own, geometry.grown(b)) {
                    lists[a].push(b);
                    if a != b {
                        lists[b].push(a);
                    }
                }
            }
        }
        for list in &mut lists {
            list.sort_unstable();
        }
        let struck = lists.iter().map(|l| vec![false; l.len()]).collect();
        Neighbours {
            lists,
            struck,
            stale: vec![false; count],
        }
    }
    /// Drops the entries struck out of a group's list.
    fn tidy(&mut self, group: usize) {
        if !self.stale[group] {
            return;
        }
        let struck = std::mem::take(&mut self.struck[group]);
        let mut at = 0;
        self.lists[group].retain(|_| {
            at += 1;
            !struck[at - 1]
        });
        self.struck[group] = vec![false; self.lists[group].len()];
        self.stale[group] = false;
    }
    /// Whether `other` is in the group's list and not struck out.
    fn linked(&self, group: usize, other: usize) -> bool {
        match self.lists[group].binary_search(&other) {
            Ok(at) => !self.struck[group][at],
            Err(_) => false,
        }
    }
    /// The group is in a set: its neighbours no longer list it, and it lists none.
    fn retire(&mut self, group: usize) {
        let list = std::mem::take(&mut self.lists[group]);
        for &other in &list {
            if other == group {
                continue;
            }
            if let Ok(at) = self.lists[other].binary_search(&group) {
                self.struck[other][at] = true;
                self.stale[other] = true;
            }
        }
        self.struck[group].clear();
    }
}

/// The candidates other than the seeds that lie on the plane within their limits and face as
/// it does.
fn on_plane(
    geometry: &Geometry,
    candidates: &[usize],
    seeds: &[usize],
    plane: &Plane,
) -> Vec<usize> {
    candidates
        .iter()
        .copied()
        .filter(|g| !seeds.contains(g))
        .filter(|&g| {
            geometry.faces(&plane, geometry.groups[g].normal, geometry.cosine)
                && geometry.reach(g, plane) <= geometry.max_distance
        })
        .collect()
}

/// Moves into `set` every group of `others` that neighbours a group of the set, growing it.
/// The order is that of a walk over `others` for each member of the set in turn, in which a
/// taken group's place goes to the last of `others`: the members' order decides the last
/// digits of the plane fitted to them. `place` is scratch: a group's place in `others`, `NONE`
/// everywhere on entry and on return.
fn take_connected(
    neighbours: &Neighbours,
    set: &mut Vec<usize>,
    others: &mut Vec<usize>,
    place: &mut [usize],
) {
    for (i, &g) in others.iter().enumerate() {
        place[g] = i;
    }
    // The member's neighbours among `others`, lowest place first. The scan stands at the
    // lowest place pending, and the group moved into a taken group's place is the next one it
    // looks at: when that group is a neighbour too, it is noted again at its new place, and
    // the note of its old place is dropped when it comes up.
    let mut pending = std::collections::BinaryHeap::new();
    let mut at = 0;
    while at < set.len() && !others.is_empty() {
        let member = set[at];
        let list = &neighbours.lists[member];
        for (k, &g) in list.iter().enumerate() {
            if place[g] != NONE && !neighbours.struck[member][k] {
                pending.push(std::cmp::Reverse((place[g], g)));
            }
        }
        while let Some(std::cmp::Reverse((noted, g))) = pending.pop() {
            let i = place[g];
            if i != noted {
                continue;
            }
            set.push(g);
            place[g] = NONE;
            others.swap_remove(i);
            if i < others.len() {
                let moved = others[i];
                place[moved] = i;
                if neighbours.linked(member, moved) {
                    pending.push(std::cmp::Reverse((i, moved)));
                }
            }
        }
        at += 1;
    }
    for &g in others.iter() {
        place[g] = NONE;
    }
}

/// The scan the walk above stands for, as written in its description.
#[cfg(test)]
fn take_connected_plainly(neighbours: &Neighbours, set: &mut Vec<usize>, others: &mut Vec<usize>) {
    let mut at = 0;
    while at < set.len() && !others.is_empty() {
        let mut i = 0;
        while i < others.len() {
            if neighbours.linked(set[at], others[i]) {
                set.push(others[i]);
                others.swap_remove(i);
            } else {
                i += 1;
            }
        }
        at += 1;
    }
}

/// Whether two measures are apart by rounding alone: close, and not the same.
fn tied(a: f32, b: f32) -> bool {
    a != b && (a - b).abs() <= TIE * a.abs().max(b.abs())
}

/// Whether two sets of the very same area keep it for every scale of their frames' axes in a
/// sweep of `NORMALISATION_ULPS` units in the last place either way, axes normalised from the
/// same squared length scaled alike. It holds when the two projections are images of each
/// other whatever the axes' scale, as are the opposite faces of a box about its mesh's origin
/// or the two sides of a roof mirrored across a coordinate plane: the areas are then equal by
/// their shape, not by the rounding here.
fn stays_tied(geometry: &Geometry, first: (&[usize], &Plane), second: (&[usize], &Plane)) -> bool {
    let steps = -NORMALISATION_ULPS..=NORMALISATION_ULPS;
    let sweep = || {
        steps
            .clone()
            .flat_map(|u| steps.clone().map(move |v| (u, v)))
    };
    let seconds: Vec<(i32, i32, [f32; 2], f32)> = sweep()
        .map(|(u, v)| {
            let frame = Frame::rounded(second.1, u, v);
            (u, v, frame.squares, geometry.area_in(second.0, &frame))
        })
        .collect();
    sweep().all(|(ua, va)| {
        let frame = Frame::rounded(first.1, ua, va);
        let area = geometry.area_in(first.0, &frame);
        seconds.iter().all(|&(ub, vb, squares, other)| {
            (frame.squares[0] == squares[0] && ua != ub)
                || (frame.squares[1] == squares[1] && va != vb)
                || area == other
        })
    })
}

/// A set of groups as one number, whatever their order.
fn set_key(set: &[usize]) -> u64 {
    set.iter().fold(set.len() as u64, |key, &g| {
        key.wrapping_add(
            (g as u64 + 1)
                .wrapping_mul(0x9e37_79b9_7f4a_7c15)
                .rotate_left(23),
        )
    })
}

/// One pair tried in a round, and the area of the set it gathered.
struct Try {
    area: f32,
    key: u64,
    plane: Plane,
    a: usize,
    b: usize,
}

fn join(geometry: &Geometry) -> Result<Joined> {
    let count = geometry.groups.len();
    let mut joined = Joined {
        of_group: vec![NONE; count],
        planes: Vec::new(),
        joined: 0,
    };
    let (mut candidates, mut others) = (Vec::new(), Vec::new());
    if geometry.max_distance > 0.0 {
        for (g, group) in geometry.groups.iter().enumerate() {
            if group.candidate {
                candidates.push(g);
            } else {
                others.push(g);
            }
        }
    }
    let mut neighbours = Neighbours::new(geometry, &candidates);
    // Two neighbouring groups whose normals' cosine is so near zero that the precompute's
    // rounding of their normals could give it the other sign: whether they face the same
    // half-space, and so whether they may be joined at all, is then the rounding's. A cosine
    // all of whose terms are zero is zero in any arithmetic.
    for &a in &candidates {
        for &b in neighbours.lists[a].iter().filter(|&&b| b > a) {
            let (na, nb) = (geometry.groups[a].normal, geometry.groups[b].normal);
            let exactly_zero = (0..3).all(|k| na[k] == 0.0 || nb[k] == 0.0);
            ensure!(
                exactly_zero || cosine_settled(na, nb) || !joins_at_right_angle(geometry, a, b),
                "two faces at a right angle but for rounding are close enough to one plane to be joined: whether the precompute joins them hangs on that rounding"
            );
        }
    }
    let mut boxes: Vec<(V3, V3)> = Vec::new();
    let mut random = Random::new();
    let mut place = vec![NONE; count];
    let mut tries: Vec<Try> = Vec::with_capacity(TRIES_PER_ROUND as usize);
    // The flat parts that the rounds leave are folded or stay charts of their own, as those
    // a round does not take (tests/projects/realtimegi RoomTurned, Many: 110 parts).
    for _ in 0..MAX_ROUNDS {
        if candidates.len() < 2 {
            break;
        }
        let mut best: Option<(f32, Plane, usize, usize, u64)> = None;
        tries.clear();
        for _ in 0..TRIES_PER_ROUND {
            let a = candidates[(random.next() as u64 % candidates.len() as u64) as usize];
            neighbours.tidy(a);
            if neighbours.lists[a].is_empty() {
                continue;
            }
            let b = neighbours.lists[a]
                [(random.next() as u64 % neighbours.lists[a].len() as u64) as usize];
            let (na, nb) = (geometry.groups[a].normal, geometry.groups[b].normal);
            if dot(na, nb) <= 0.0 {
                continue;
            }
            let mut plane = geometry.fit(&[a, b]);
            if !geometry.faces(&plane, na, 0.0) {
                plane = plane.flipped();
            }
            if !geometry.faces(&plane, na, geometry.cosine)
                || !geometry.faces(&plane, nb, geometry.cosine)
            {
                continue;
            }
            if geometry.reach(a, &plane) > geometry.max_distance
                || geometry.reach(b, &plane) > geometry.max_distance
            {
                continue;
            }
            let mut rest = on_plane(geometry, &candidates, &[a, b], &plane);
            let mut set = if a == b { vec![a] } else { vec![a, b] };
            take_connected(&neighbours, &mut set, &mut rest, &mut place);
            let area = geometry.projected_area(&set, &plane);
            let key = set_key(&set);
            tries.push(Try {
                area,
                key,
                plane,
                a,
                b,
            });
            if best.as_ref().is_none_or(|(most, ..)| area > *most) && area > 0.0 {
                best = Some((area, plane, a, b, key));
            }
        }
        let Some((most, plane, a, b, key)) = best else {
            break;
        };
        let gather = |a: usize, b: usize, plane: &Plane, place: &mut [usize]| {
            let mut rest = on_plane(geometry, &candidates, &[a, b], plane);
            let mut set = if a == b { vec![a] } else { vec![a, b] };
            take_connected(&neighbours, &mut set, &mut rest, place);
            set
        };
        let set = gather(a, b, &plane, &mut place);
        // Another set whose area is the winner's or is so but for rounding: along the
        // coordinate axes the arithmetic here is the precompute's, and the first tried is
        // taken. Off them the precompute's own rounding decides, unless the two areas are the
        // very same and stay so over the sweep of `stays_tied`: equal by their shape, the first
        // tried is taken there too.
        let along = plane.along_axes();
        let mut checked: Vec<(u64, Plane)> = Vec::new();
        for other in &tries {
            if other.key == key || (other.area != most && !tied(other.area, most)) {
                continue;
            }
            if along && other.plane.along_axes() {
                continue;
            }
            if checked.contains(&(other.key, other.plane)) {
                continue;
            }
            checked.push((other.key, other.plane));
            let by_shape = other.area == most && {
                let other_set = gather(other.a, other.b, &other.plane, &mut place);
                stays_tied(geometry, (&set, &plane), (&other_set, &other.plane))
            };
            ensure!(
                by_shape,
                "two sets of faces are the largest of a round but for rounding (areas {most} and {}): which the precompute takes hangs on that rounding",
                other.area
            );
        }
        let mut fitted = geometry.fit(&set);
        if dot(plane.normal(), fitted.normal()) < 0.0 {
            fitted = fitted.flipped();
        }
        let index = joined.planes.len();
        joined.planes.push(fitted);
        let mut bounds = ([f32::MAX; 3], [f32::MIN; 3]);
        for &g in &set {
            joined.of_group[g] = index;
            for k in 0..3 {
                bounds.0[k] = bounds.0[k].min(geometry.groups[g].min[k]);
                bounds.1[k] = bounds.1[k].max(geometry.groups[g].max[k]);
            }
            neighbours.retire(g);
            if let Some(at) = candidates.iter().position(|&c| c == g) {
                candidates.swap_remove(at);
            }
        }
        boxes.push(bounds);
    }
    for list in [&others, &candidates] {
        fold(geometry, &mut joined, list, &boxes)?;
    }
    joined.joined = joined.planes.len();
    for g in 0..count {
        if joined.of_group[g] == NONE {
            joined.of_group[g] = joined.planes.len();
            joined.planes.push(geometry.groups[g].plane);
        }
    }
    Ok(joined)
}

/// Folds small groups into the joined set they are nearest to: one whose box they reach, that
/// is more than ten times their area, and whose plane all their vertices are within the
/// distance limit of. Their direction is not asked.
fn fold(
    geometry: &Geometry,
    joined: &mut Joined,
    list: &[usize],
    boxes: &[(V3, V3)],
) -> Result<()> {
    let mut areas = vec![0.0f32; joined.planes.len()];
    for (g, &set) in joined.of_group.iter().enumerate() {
        if set != NONE {
            areas[set] += geometry.groups[g].area as f32;
        }
    }
    let mut reaches = Vec::new();
    for &g in list {
        let area = geometry.groups[g].area as f32;
        let own = geometry.grown(g);
        let mut nearest = geometry.max_distance;
        let mut into = NONE;
        reaches.clear();
        for set in 0..joined.planes.len() {
            if area / areas[set] < SMALL_SHARE && boxes_meet(own, boxes[set]) {
                let reach = geometry.reach(g, &joined.planes[set]);
                reaches.push((reach, set));
                if reach < nearest {
                    nearest = reach;
                    into = set;
                }
            }
        }
        if into == NONE {
            continue;
        }
        // Sets as near or so but for rounding: the first is taken along the axes (a slab's
        // end face between its two large faces); off them the choice is not made.
        let along = joined.planes[into].along_axes();
        ensure!(
            reaches.iter().all(|&(reach, set)| set == into
                || (reach != nearest && !tied(reach, nearest))
                || (along && joined.planes[set].along_axes())),
            "a small part is as near to two sets of faces but for rounding: which of them the precompute folds it into hangs on that rounding"
        );
        joined.of_group[g] = into;
    }
    Ok(())
}

/// A right-angled triangle's two legs and their directions: the first corner, in the order
/// first, third, second vertex, whose edges' cosine is small enough.
fn right_angle(p: [V3; 3]) -> Option<([V3; 2], [V3; 2])> {
    let edges = [sub(p[1], p[0]), sub(p[2], p[0]), sub(p[2], p[1])];
    let directions = edges.map(normalized);
    for (i, j) in [(0, 1), (1, 2), (2, 0)] {
        if dot(directions[i], directions[j]).abs() < RIGHT_ANGLE_COSINE {
            return Some(([edges[i], edges[j]], [directions[i], directions[j]]));
        }
    }
    None
}

/// Directions with the area of the right-angled triangles whose legs run along them. Parallel
/// and perpendicular directions are one entry, the first met; the others are ordered by their
/// components, as a red-black tree of the insertions holds them.
#[derive(Default)]
struct Directions {
    nodes: Vec<DirectionNode>,
    root: usize,
}

struct DirectionNode {
    key: V3,
    area: f32,
    left: usize,
    right: usize,
    parent: usize,
    red: bool,
}

impl Directions {
    fn before(a: V3, b: V3) -> bool {
        let d = dot(a, b).abs();
        if d > SAME_DIRECTION_COSINE || d < PERPENDICULAR_COSINE {
            return false;
        }
        a[0] < b[0] || (a[0] == b[0] && (a[1] < b[1] || (a[1] == b[1] && a[2] < b[2])))
    }
    fn new() -> Directions {
        Directions {
            nodes: Vec::new(),
            root: NONE,
        }
    }
    fn previous(&self, mut node: usize) -> usize {
        if self.nodes[node].left != NONE {
            node = self.nodes[node].left;
            while self.nodes[node].right != NONE {
                node = self.nodes[node].right;
            }
            return node;
        }
        let mut parent = self.nodes[node].parent;
        while parent != NONE && self.nodes[parent].left == node {
            node = parent;
            parent = self.nodes[node].parent;
        }
        parent
    }
    fn add(&mut self, key: V3, area: f32) {
        let (mut parent, mut node, mut left) = (NONE, self.root, true);
        while node != NONE {
            parent = node;
            left = Self::before(key, self.nodes[node].key);
            node = if left {
                self.nodes[node].left
            } else {
                self.nodes[node].right
            };
        }
        // The entry the key is not before: its own when that is not before the key either.
        let mut at = parent;
        if left && parent != NONE {
            at = self.previous(parent);
        }
        if at != NONE && !(left && parent == NONE) && !Self::before(self.nodes[at].key, key) {
            self.nodes[at].area += area;
            return;
        }
        let new = self.nodes.len();
        self.nodes.push(DirectionNode {
            key,
            area,
            left: NONE,
            right: NONE,
            parent,
            red: true,
        });
        if parent == NONE {
            self.root = new;
        } else if left {
            self.nodes[parent].left = new;
        } else {
            self.nodes[parent].right = new;
        }
        self.balance(new);
    }
    fn rotate(&mut self, node: usize, to_left: bool) {
        let child = if to_left {
            self.nodes[node].right
        } else {
            self.nodes[node].left
        };
        let inner = if to_left {
            self.nodes[child].left
        } else {
            self.nodes[child].right
        };
        if to_left {
            self.nodes[node].right = inner;
        } else {
            self.nodes[node].left = inner;
        }
        if inner != NONE {
            self.nodes[inner].parent = node;
        }
        let parent = self.nodes[node].parent;
        self.nodes[child].parent = parent;
        if parent == NONE {
            self.root = child;
        } else if self.nodes[parent].left == node {
            self.nodes[parent].left = child;
        } else {
            self.nodes[parent].right = child;
        }
        if to_left {
            self.nodes[child].left = node;
        } else {
            self.nodes[child].right = node;
        }
        self.nodes[node].parent = child;
    }
    fn balance(&mut self, mut node: usize) {
        while node != self.root && self.nodes[self.nodes[node].parent].red {
            let parent = self.nodes[node].parent;
            let grand = self.nodes[parent].parent;
            let parent_is_left = self.nodes[grand].left == parent;
            let uncle = if parent_is_left {
                self.nodes[grand].right
            } else {
                self.nodes[grand].left
            };
            if uncle != NONE && self.nodes[uncle].red {
                self.nodes[parent].red = false;
                self.nodes[uncle].red = false;
                self.nodes[grand].red = true;
                node = grand;
                continue;
            }
            let inner = if parent_is_left {
                self.nodes[parent].right
            } else {
                self.nodes[parent].left
            };
            if node == inner {
                node = parent;
                self.rotate(node, parent_is_left);
            }
            let parent = self.nodes[node].parent;
            let grand = self.nodes[parent].parent;
            self.nodes[parent].red = false;
            self.nodes[grand].red = true;
            self.rotate(grand, !parent_is_left);
        }
        let root = self.root;
        self.nodes[root].red = false;
    }
    /// The direction with the most area, the first in order of several.
    fn best(&self) -> Option<V3> {
        let mut order = Vec::new();
        let mut stack = Vec::new();
        let mut node = self.root;
        while node != NONE || !stack.is_empty() {
            while node != NONE {
                stack.push(node);
                node = self.nodes[node].left;
            }
            node = stack.pop().unwrap();
            order.push(node);
            node = self.nodes[node].right;
        }
        let mut best = None;
        let mut most = 0.0f32;
        for node in order {
            if self.nodes[node].area > most {
                most = self.nodes[node].area;
                best = Some(self.nodes[node].key);
            }
        }
        best
    }
}

/// Turns the frame's axes to the direction most of the set's right-angled triangles have a
/// leg along.
fn turn(frame: &mut Frame, geometry: &Geometry, triangles: &[usize]) {
    let mut directions = Directions::new();
    for &t in triangles {
        let Some((legs, along)) = right_angle(geometry.corners(t)) else {
            continue;
        };
        let c = cross(legs[0], legs[1]);
        let area = 0.5 * dot(c, c).sqrt();
        if area <= 0.0 {
            continue;
        }
        for direction in along {
            if dot(frame.n, direction).abs() < IN_PLANE_COSINE {
                directions.add(direction, area);
            }
        }
    }
    let Some(best) = directions.best() else {
        return;
    };
    if dot(best, best).sqrt() <= 0.0 {
        return;
    }
    let in_plane = sub(best, scaled(frame.n, dot(best, frame.n)));
    if dot(in_plane, in_plane).sqrt() <= 0.0 {
        return;
    }
    frame.u = normalized(in_plane);
    frame.v = normalized(cross(frame.n, frame.u));
}

/// How far the precompute's cosine of two group normals may be from the one here, as a share
/// of the sum of its terms' magnitudes: each component of a normal may differ in its last units
/// (`NORMALISATION_ULPS` each way), and the three products are rounded and summed.
const COSINE_ROUNDING: f32 = (4 * NORMALISATION_ULPS + 4) as f32 * f32::EPSILON;

/// Whether the cosine of two group normals has the same sign in the precompute's rounding.
fn cosine_settled(a: V3, b: V3) -> bool {
    let terms = (a[0] * b[0]).abs() + (a[1] * b[1]).abs() + (a[2] * b[2]).abs();
    dot(a, b).abs() > COSINE_ROUNDING * terms
}

/// Whether two neighbouring flat groups at a right angle would be joined if their normals
/// leant towards each other: a plane through both that faces as each does within the angle
/// limit and that both stay within the distance limit of. (A thin side of a slab beside its
/// large face does: the plane tilts by a few degrees.)
fn joins_at_right_angle(geometry: &Geometry, a: usize, b: usize) -> bool {
    let (na, nb) = (geometry.groups[a].normal, geometry.groups[b].normal);
    let fitted = geometry.fit(&[a, b]);
    [fitted, fitted.flipped()].iter().any(|plane| {
        geometry.faces(&plane, na, geometry.cosine)
            && geometry.faces(&plane, nb, geometry.cosine)
            && geometry.reach(a, plane) <= geometry.max_distance
            && geometry.reach(b, plane) <= geometry.max_distance
    })
}

/// Below this, a chart's own lightmap UVs are as true to its shape as its projection is: the
/// largest difference between the scales of a triangle's three edges, over the smallest.
const OWN_UVS_AS_TRUE: f32 = 1e-3;

/// How far a group's own lightmap UVs are from a scaled copy of its shape: over its triangles,
/// the largest ratio between the scales (UV length over length) of two edges, less one.
fn own_uv_distortion(geometry: &Geometry, group: &Group) -> f32 {
    let mut worst = 0.0f32;
    for &t in &group.triangles {
        let [a, b, c] = geometry.triangles[t].map(|v| v as usize);
        let (mut least, mut most) = (f32::MAX, 0.0f32);
        for (i, j) in [(a, b), (a, c), (b, c)] {
            let e = sub(geometry.mesh.positions[j], geometry.mesh.positions[i]);
            let length = dot(e, e).sqrt();
            let (p, q) = (geometry.mesh.uvs[i], geometry.mesh.uvs[j]);
            let own = ((q[0] - p[0]).powi(2) + (q[1] - p[1]).powi(2)).sqrt();
            if length <= 0.0 {
                continue;
            }
            least = least.min(own / length);
            most = most.max(own / length);
        }
        if most > 0.0 {
            worst = worst.max(if least > 0.0 {
                most / least - 1.0
            } else {
                f32::MAX
            });
        }
    }
    worst
}

/// Whether a group's own lightmap UVs lay it out as `projected` does: the same but for a scale
/// and an offset along each axis, which a chart's fit into its texels takes out again. Then it
/// does not matter which of the two the chart is made of.
fn laid_out_alike(mesh: &Mesh, projected: &[[f32; 2]], group: &Group) -> bool {
    (0..2).all(|k| {
        let (mut low, mut high) = (group.vertices[0], group.vertices[0]);
        for &v in &group.vertices {
            if projected[v][k] < projected[low][k] {
                low = v;
            }
            if projected[v][k] > projected[high][k] {
                high = v;
            }
        }
        let extent = projected[high][k] - projected[low][k];
        let own = mesh.uvs[high][k] - mesh.uvs[low][k];
        if !(extent > 0.0 && own > 0.0) {
            return false;
        }
        let scale = own / extent;
        group.vertices.iter().all(|&v| {
            let expected = mesh.uvs[low][k] + (projected[v][k] - projected[low][k]) * scale;
            (mesh.uvs[v][k] - expected).abs() <= OWN_UVS_AS_TRUE * own
        })
    })
}

/// The automatic charts of a mesh. Fails for what is not made as the Editor makes it: Ignore
/// Normals, a part of the mesh that is not flat within the limits, a part left on its own that
/// is bent or whose own UVs are as true to its shape as its projection on a tilted plane, and
/// every choice that rounding decides (faces at a right angle but for rounding that could
/// join, sets or distances that tie but for rounding).
pub fn auto_charts(mesh: &Mesh, params: &Params) -> Result<Charts> {
    ensure!(
        mesh.normals.len() == mesh.positions.len() && mesh.uvs.len() == mesh.positions.len(),
        "{} normals and {} UVs for {} vertices",
        mesh.normals.len(),
        mesh.uvs.len(),
        mesh.positions.len()
    );
    // (With a limit of zero the precompute keeps the mesh's own charts: those are not made
    // here.)
    ensure!(
        params.max_distance > 0.0 && params.max_angle > 0.0,
        "automatic charts with Max Distance {} and Max Angle {}",
        params.max_distance,
        params.max_angle
    );
    if mesh
        .triangles
        .iter()
        .flatten()
        .any(|&v| v as usize >= mesh.positions.len())
    {
        bail!("a triangle past the {} vertices", mesh.positions.len());
    }
    if mesh
        .positions
        .iter()
        .chain(mesh.normals)
        .flatten()
        .chain(mesh.uvs.iter().flatten())
        .any(|v| !v.is_finite())
    {
        bail!("a vertex with a non-finite position, normal or lightmap UV");
    }
    let geometry = geometry(mesh, params);
    let joined = join(&geometry)?;
    ensure!(
        !geometry.ambiguous.get(),
        "a part faces a plane at the Max Angle limit ({} degrees) but for rounding: whether the precompute joins it there hangs on that rounding",
        params.max_angle
    );
    let sets = joined.planes.len();
    // Each set's triangles: its groups' in group order.
    let mut triangles = vec![Vec::new(); sets];
    let mut set_of_vertex = vec![NONE; mesh.positions.len()];
    for (g, group) in geometry.groups.iter().enumerate() {
        let set = joined.of_group[g];
        triangles[set].extend(&group.triangles);
        for &v in &group.vertices {
            set_of_vertex[v] = set;
        }
    }
    let frames: Vec<Frame> = (0..sets)
        .map(|set| {
            let mut frame = Frame::of(&joined.planes[set]);
            turn(&mut frame, &geometry, &triangles[set]);
            frame
        })
        .collect();
    // A part without area that lies by no chart is a chart of its own with all its vertices
    // at its corner (tests/projects/realtimegi RoomTurned, BoxSliver and BoxPoint: three points
    // on a line, and three at one point).
    let mut empty = vec![false; sets];
    for (g, group) in geometry.groups.iter().enumerate() {
        let set = joined.of_group[g];
        if set >= joined.joined && group.area == 0.0 {
            empty[set] = true;
        }
    }
    let mut uvs = vec![[0.0f32; 2]; mesh.positions.len()];
    let mut corner = vec![[f32::MAX; 2]; sets];
    for v in 0..mesh.positions.len() {
        let set = set_of_vertex[v];
        if set == NONE || empty[set] {
            continue;
        }
        uvs[v] = frames[set].project(mesh.positions[v]);
        for k in 0..2 {
            corner[set][k] = corner[set][k].min(uvs[v][k]);
        }
    }
    for v in 0..mesh.positions.len() {
        let set = set_of_vertex[v];
        if set != NONE && !empty[set] {
            uvs[v] = [uvs[v][0] - corner[set][0], uvs[v][1] - corner[set][1]];
        }
    }
    // A group left on its own keeps its own lightmap UVs when they are truer to its shape than
    // its projection (by the angles of its triangles). That comparison is made here for planar
    // groups alone: the projection of one on a plane along the coordinate axes repeats the
    // positions' own coordinates and is exact; elsewhere it is the truer one when the own UVs
    // are clearly not a scaled copy of the shape. Otherwise the two differ by rounding alone:
    // the choice is not made here, unless both lay the chart out the same way.
    for (g, group) in geometry.groups.iter().enumerate() {
        let set = joined.of_group[g];
        if set < joined.joined || empty[set] {
            continue;
        }
        ensure!(
            group.normal != [0.0; 3],
            "a part of the mesh ({} triangles) has no normal to face along",
            group.triangles.len()
        );
        ensure!(
            group.candidate,
            "a part of the mesh ({} triangles) is not flat within Max Distance {} and Max Angle {}: charts of such parts are not made yet",
            group.triangles.len(),
            params.max_distance,
            params.max_angle
        );
        let size = dot(sub(group.max, group.min), sub(group.max, group.min)).sqrt();
        ensure!(
            geometry.reach(g, &group.plane) <= PLANAR * size,
            "a bent part of the mesh ({} triangles) is left on its own: whether the precompute keeps its lightmap UVs or projects it is not made yet",
            group.triangles.len()
        );
        ensure!(
            joined.planes[set].along_axes()
                || own_uv_distortion(&geometry, group) >= OWN_UVS_AS_TRUE
                || laid_out_alike(mesh, &uvs, group),
            "a chart on a tilted plane ({} triangles) has lightmap UVs as true to its shape as its projection: which of the two the precompute keeps is not made yet",
            group.triangles.len()
        );
    }
    Ok(Charts { uvs, triangles })
}

#[cfg(test)]
mod tests {
    use super::*;

    const LIMITS: Params = Params {
        max_distance: 0.5,
        max_angle: 89.0,
        ignore_normals: false,
    };

    #[derive(Default)]
    struct Shape {
        positions: Vec<V3>,
        normals: Vec<V3>,
        uvs: Vec<[f32; 2]>,
        triangles: Vec<[u32; 3]>,
    }

    impl Shape {
        fn mesh(&self) -> Mesh<'_> {
            Mesh {
                positions: &self.positions,
                normals: &self.normals,
                uvs: &self.uvs,
                triangles: &self.triangles,
            }
        }
        /// A quad of its own vertices, with lightmap UVs that fill the unit square.
        fn quad(&mut self, corners: [V3; 4], normal: V3) {
            let first = self.positions.len() as u32;
            self.positions.extend(corners);
            self.normals.extend([normal; 4]);
            self.uvs
                .extend([[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]]);
            self.triangles.push([first, first + 2, first + 1]);
            self.triangles.push([first, first + 3, first + 2]);
        }
    }

    /// A box about the origin, one quad a face.
    fn solid(size: V3) -> Shape {
        let mut shape = Shape::default();
        for axis in 0..3 {
            for side in [-1.0f32, 1.0] {
                let (u, v) = ((axis + 1) % 3, (axis + 2) % 3);
                let corner = |su: f32, sv: f32| {
                    let mut p = [0.0; 3];
                    p[axis] = side * 0.5 * size[axis];
                    p[u] = su * 0.5 * size[u];
                    p[v] = sv * 0.5 * size[v];
                    p
                };
                let mut normal = [0.0; 3];
                normal[axis] = side;
                shape.quad(
                    [
                        corner(-1.0, -1.0),
                        corner(1.0, -1.0),
                        corner(1.0, 1.0),
                        corner(-1.0, 1.0),
                    ],
                    normal,
                );
            }
        }
        shape
    }

    /// A rectangle of 2 by 1 in a plane tilted by 30 degrees about X.
    fn tilted(uvs: [[f32; 2]; 4]) -> Shape {
        let (sin, cos) = 30.0f32.to_radians().sin_cos();
        let mut shape = Shape::default();
        shape.quad(
            [
                [0.0, 0.0, 0.0],
                [2.0, 0.0, 0.0],
                [2.0, sin, cos],
                [0.0, sin, cos],
            ],
            [0.0, cos, -sin],
        );
        shape.uvs = uvs.to_vec();
        shape
    }

    /// Each chart's extent along its two axes, the longer first.
    fn extents(shape: &Shape, charts: &Charts) -> Vec<[f32; 2]> {
        charts
            .triangles
            .iter()
            .map(|triangles| {
                let mut most = [0.0f32; 2];
                for &t in triangles {
                    for v in shape.triangles[t] {
                        for k in 0..2 {
                            most[k] = most[k].max(charts.uvs[v as usize][k]);
                        }
                    }
                }
                [most[0].max(most[1]), most[0].min(most[1])]
            })
            .collect()
    }

    fn near(a: f32, b: f32) -> bool {
        (a - b).abs() < 1e-5
    }

    #[test]
    fn the_sequence_is_the_standard_one() {
        let mut random = Random::new();
        assert_eq!(random.next(), 3_499_211_612);
        let mut last = 0;
        for _ in 1..10_000 {
            last = random.next();
        }
        assert_eq!(last, 4_123_659_995);
    }

    #[test]
    fn a_cube_keeps_its_six_faces() {
        let cube = solid([1.0, 1.0, 1.0]);
        let charts = auto_charts(&cube.mesh(), &LIMITS).unwrap();
        assert_eq!(charts.triangles.len(), 6);
        assert!(charts.triangles.iter().all(|t| t.len() == 2));
        for extent in extents(&cube, &charts) {
            assert!(near(extent[0], 1.0) && near(extent[1], 1.0), "{extent:?}");
        }
    }

    /// The faces are taken largest first while two are left; the last one is small beside a
    /// large face and within the distance limit of its plane, and becomes a line in its chart.
    #[test]
    fn a_slab_folds_its_last_face_into_a_large_one() {
        let slab = solid([6.0, 0.2, 4.0]);
        let charts = auto_charts(&slab.mesh(), &LIMITS).unwrap();
        let sizes: Vec<usize> = charts.triangles.iter().map(Vec::len).collect();
        assert_eq!(sizes, [4, 2, 2, 2, 2]);
        let extents = extents(&slab, &charts);
        for (extent, expected) in
            extents
                .iter()
                .zip([[6.0, 4.0], [6.0, 4.0], [6.0, 0.2], [6.0, 0.2], [4.0, 0.2]])
        {
            assert!(
                near(extent[0], expected[0]) && near(extent[1], expected[1]),
                "{extents:?}"
            );
        }
        // The folded face: the two triangles of the first chart that are not in its plane.
        let folded: Vec<usize> = charts.triangles[0]
            .iter()
            .copied()
            .filter(|&t| slab.normals[slab.triangles[t][0] as usize][1] == 0.0)
            .collect();
        assert_eq!(folded.len(), 2);
        let corners: Vec<[f32; 2]> = folded
            .iter()
            .flat_map(|&t| slab.triangles[t].map(|v| charts.uvs[v as usize]))
            .collect();
        assert!(
            (0..2).any(|k| corners.iter().all(|c| c[k] == corners[0][k])),
            "{corners:?}"
        );
    }

    /// A triangle without area in a face changes nothing but that face's triangle count.
    #[test]
    fn a_triangle_without_area_stays_in_its_face() {
        let mut cube = solid([1.0, 1.0, 1.0]);
        cube.triangles.push([0, 0, 1]);
        let charts = auto_charts(&cube.mesh(), &LIMITS).unwrap();
        let mut sizes: Vec<usize> = charts.triangles.iter().map(Vec::len).collect();
        sizes.sort_unstable();
        assert_eq!(sizes, [2, 2, 2, 2, 2, 3]);
    }

    #[test]
    fn vertices_that_differ_in_their_normal_are_one_when_normals_are_ignored() {
        let mut shape = Shape::default();
        shape.quad(
            [
                [0.0, 0.0, 0.0],
                [1.0, 0.0, 0.0],
                [1.0, 0.0, 1.0],
                [0.0, 0.0, 1.0],
            ],
            [0.0, 1.0, 0.0],
        );
        shape.quad(
            [
                [1.0, 0.0, 0.0],
                [2.0, 0.0, 0.0],
                [2.0, 0.0, 1.0],
                [1.0, 0.0, 1.0],
            ],
            [0.6, 0.8, 0.0],
        );
        // The second quad's first and last corners lie on the first one's second and third.
        shape.uvs[4] = shape.uvs[1];
        shape.uvs[7] = shape.uvs[2];
        assert_eq!(groups(&shape.mesh(), false).len(), 2);
        assert_eq!(groups(&shape.mesh(), true).len(), 1);
        // A negative zero is the same value as a zero.
        shape.normals[4] = [0.0, 1.0, -0.0];
        shape.normals[7] = [-0.0, 1.0, 0.0];
        assert_eq!(groups(&shape.mesh(), false).len(), 1);
    }

    #[test]
    fn a_part_that_is_not_flat_fails() {
        // Two quads of 4 m at a right angle that share their edge's vertices.
        let mut bend = Shape::default();
        let smooth = normalized([0.0, 1.0, -1.0]);
        bend.positions = vec![
            [0.0, 0.0, 0.0],
            [4.0, 0.0, 0.0],
            [4.0, 0.0, 4.0],
            [0.0, 0.0, 4.0],
            [4.0, 4.0, 4.0],
            [0.0, 4.0, 4.0],
        ];
        bend.normals = vec![
            [0.0, 1.0, 0.0],
            [0.0, 1.0, 0.0],
            smooth,
            smooth,
            [0.0, 0.0, -1.0],
            [0.0, 0.0, -1.0],
        ];
        bend.uvs = vec![
            [0.0, 0.0],
            [1.0, 0.0],
            [1.0, 0.5],
            [0.0, 0.5],
            [1.0, 1.0],
            [0.0, 1.0],
        ];
        bend.triangles = vec![[0, 2, 1], [0, 3, 2], [3, 4, 2], [3, 5, 4]];
        let error = auto_charts(&bend.mesh(), &LIMITS).unwrap_err().to_string();
        assert!(error.contains("not flat"), "{error}");
        // Within a distance limit that holds it, it is flat within the limits, and bent.
        let wide = Params {
            max_distance: 4.0,
            ..LIMITS
        };
        let error = auto_charts(&bend.mesh(), &wide).unwrap_err().to_string();
        assert!(error.contains("bent part"), "{error}");
    }

    /// A small part that is not flat, beside a large flat one, is folded into its chart.
    #[test]
    fn a_small_part_that_is_not_flat_is_folded_into_a_set_it_lies_by() {
        let narrow = Params {
            max_angle: 30.0,
            ..LIMITS
        };
        // A small level quad whose second triangle faces down by its vertices' normals: a
        // quarter of its area faces away from its normal.
        let mut shape = Shape::default();
        shape.positions = vec![
            [1.0, 0.05, 1.0],
            [1.4, 0.05, 1.0],
            [1.4, 0.05, 1.4],
            [1.1, 0.05, 1.3],
        ];
        shape.normals = vec![
            [1.0, 0.0, 0.0],
            [0.0, 1.0, 0.0],
            [-1.0, 0.0, 0.0],
            [0.0, -1.0, 0.0],
        ];
        shape.uvs = vec![[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.2, 0.8]];
        shape.triangles = vec![[0, 2, 1], [0, 3, 2]];
        let mesh = shape.mesh();
        assert!(!geometry(&mesh, &narrow).groups[0].candidate);
        let floor = |shape: &mut Shape, x: f32, size: f32| {
            shape.quad(
                [
                    [x, 0.0, 0.0],
                    [x + size, 0.0, 0.0],
                    [x + size, 0.0, size],
                    [x, 0.0, size],
                ],
                [0.0, 1.0, 0.0],
            )
        };
        floor(&mut shape, 0.0, 4.0);
        floor(&mut shape, 10.0, 3.0);
        let charts = auto_charts(&shape.mesh(), &narrow).unwrap();
        assert_eq!(charts.triangles.len(), 2);
        let mut first = charts.triangles[0].clone();
        first.sort_unstable();
        assert_eq!(first, [0, 1, 2, 3]);
        // It lies in the large face's chart, where the face's plane puts it.
        let [u0, w0] = charts.uvs[0];
        for v in 1..4 {
            let [u, w] = charts.uvs[v];
            let (p, q) = (shape.positions[v], shape.positions[0]);
            assert!(near(
                (u - u0).abs() + (w - w0).abs(),
                (p[0] - q[0]).abs() + (p[2] - q[2]).abs()
            ));
        }
        for v in 0..8 {
            assert!(charts.uvs[v].iter().all(|&x| (0.0..=4.0).contains(&x)));
        }
    }

    /// Flat parts that each take a round of their own: a hundred rounds are the most.
    #[test]
    fn the_parts_past_the_last_round_are_charts_of_their_own_in_their_order() {
        let row = |count: usize| {
            let mut shape = Shape::default();
            for i in 0..count {
                let x = 2.0 * i as f32;
                shape.quad(
                    [
                        [x, 0.0, 0.0],
                        [x + 1.0, 0.0, 0.0],
                        [x + 1.0, 0.0, 1.0],
                        [x, 0.0, 1.0],
                    ],
                    [0.0, 1.0, 0.0],
                );
            }
            shape
        };
        let charts = auto_charts(&row(MAX_ROUNDS + 1).mesh(), &LIMITS).unwrap();
        assert_eq!(charts.triangles.len(), MAX_ROUNDS + 1);
        // Two left after the last round: they follow the rounds' charts, lowest first.
        let charts = auto_charts(&row(MAX_ROUNDS + 5).mesh(), &LIMITS).unwrap();
        assert_eq!(charts.triangles.len(), MAX_ROUNDS + 5);
        let firsts: Vec<usize> = charts.triangles[MAX_ROUNDS..]
            .iter()
            .map(|t| t[0])
            .collect();
        assert!(firsts.windows(2).all(|w| w[0] < w[1]), "{firsts:?}");
    }

    /// A part that faces a plane at the Max Angle limit but for rounding is not joined or kept
    /// apart here; limits of zero are not automatic charts at all.
    #[test]
    fn a_part_at_the_angle_limit_and_limits_of_zero_fail() {
        // Two quads 40 degrees apart, each 20 from the plane through both.
        let (sin, cos) = 20.0f32.to_radians().sin_cos();
        let mut roof = Shape::default();
        roof.quad(
            [
                [0.0, 0.0, 0.0],
                [0.0, 0.0, 2.0],
                [cos, -sin, 2.0],
                [cos, -sin, 0.0],
            ],
            [sin, cos, 0.0],
        );
        roof.quad(
            [
                [0.0, 0.0, 0.0],
                [-cos, -sin, 0.0],
                [-cos, -sin, 2.0],
                [0.0, 0.0, 2.0],
            ],
            [-sin, cos, 0.0],
        );
        let at = |max_angle: f32| {
            auto_charts(
                &roof.mesh(),
                &Params {
                    max_angle,
                    ..LIMITS
                },
            )
        };
        assert!(at(20.5).is_ok() && at(19.5).is_ok());
        let error = at(20.0).unwrap_err().to_string();
        assert!(error.contains("Max Angle limit"), "{error}");
        for limits in [
            Params {
                max_distance: 0.0,
                ..LIMITS
            },
            Params {
                max_angle: 0.0,
                ..LIMITS
            },
        ] {
            let error = auto_charts(&roof.mesh(), &limits).unwrap_err().to_string();
            assert!(
                error.contains("automatic charts with Max Distance"),
                "{error}"
            );
        }
    }

    /// A cosine of normals far from zero against its terms has the precompute's sign; one of
    /// terms that cancel does not.
    #[test]
    fn a_cosine_is_settled_when_rounding_cannot_change_its_sign() {
        // The +Z and -Y faces of RoomTurned's BoxTurned.
        let up = [4.257_474_7e-8, -1.117_587_1e-8, 1.0];
        let down = [1.596_553e-8, -1.0, -6.208_816e-9];
        assert!(cosine_settled(up, down) && dot(up, down) > 0.0);
        // The +Z and +X faces of a cube turned 25 degrees (RoomTurned's SharedTurned).
        let front = [-3.311_369_3e-8, 0.0, 1.0];
        let side = [1.0, 0.0, 3.311_369_3e-8];
        assert!(!cosine_settled(front, side));
        assert!(cosine_settled([0.0, 1.0, 0.0], normalized([0.0, 1.0, 1.0])));
    }

    /// Turns a shape in its own mesh: by `yaw` degrees about Y, then by `pitch` about X.
    fn turn_in_mesh(shape: &mut Shape, yaw: f32, pitch: f32) {
        let (sy, cy) = yaw.to_radians().sin_cos();
        let (sp, cp) = pitch.to_radians().sin_cos();
        let turn = |p: V3| {
            let q = [p[0] * cy + p[2] * sy, p[1], p[2] * cy - p[0] * sy];
            [q[0], q[1] * cp - q[2] * sp, q[1] * sp + q[2] * cp]
        };
        for v in 0..shape.positions.len() {
            shape.positions[v] = turn(shape.positions[v]);
            shape.normals[v] = turn(shape.normals[v]);
        }
    }

    /// Faces that are at a right angle but for the rounding of their normals: where a plane
    /// through two of them is within the limits of both, that rounding would decide whether
    /// they are joined.
    #[test]
    fn faces_at_a_right_angle_but_for_rounding_fail_where_they_could_join() {
        let could_join = |size: V3| {
            let shape = solid(size);
            let mesh = shape.mesh();
            let geometry = geometry(&mesh, &LIMITS);
            (0..6).any(|a| {
                (0..a).any(|b| {
                    dot(geometry.groups[a].normal, geometry.groups[b].normal) == 0.0
                        && joins_at_right_angle(&geometry, a, b)
                })
            })
        };
        // A slab's thin side beside its large face, any two faces of a small cube; no two
        // faces of a large box are within the distance limit of one plane.
        assert!(could_join([3.0, 0.2, 2.0]) && could_join([1.0, 1.0, 1.0]));
        assert!(!could_join([4.0, 3.0, 5.0]));
        // Along the axes the faces are at a right angle exactly and are not joined; turned in
        // the mesh, their normals are so but for rounding.
        for size in [[3.0, 0.2, 2.0], [1.0, 1.0, 1.0]] {
            let mut shape = solid(size);
            assert!(auto_charts(&shape.mesh(), &LIMITS).is_ok());
            turn_in_mesh(&mut shape, 30.0, 20.0);
            let rounded = (0..shape.normals.len()).any(|a| {
                (0..a).any(|b| {
                    let cosine = dot(shape.normals[a], shape.normals[b]);
                    cosine != 0.0 && cosine.abs() < 1e-6
                })
            });
            assert!(rounded);
            let error = auto_charts(&shape.mesh(), &LIMITS).unwrap_err().to_string();
            assert!(error.contains("but for rounding"), "{error}");
        }
        let mut cube = solid([1.0, 1.0, 1.0]);
        turn_in_mesh(&mut cube, 30.0, 20.0);
        let error = auto_charts(&cube.mesh(), &LIMITS).unwrap_err().to_string();
        assert!(error.contains("right angle but for rounding"), "{error}");
        // Turned by 45 degrees the cosine of a thin side and a large face is zero here because
        // its terms cancel, which the precompute's own normals need not do.
        let mut slab = solid([3.0, 0.2, 2.0]);
        turn_in_mesh(&mut slab, 0.0, 45.0);
        let mesh = slab.mesh();
        let geometry = geometry(&mesh, &LIMITS);
        assert!((0..6).any(|a| (0..a).any(|b| {
            let (na, nb) = (geometry.groups[a].normal, geometry.groups[b].normal);
            dot(na, nb) == 0.0 && (0..3).any(|k| na[k] != 0.0 && nb[k] != 0.0)
        })));
        let error = auto_charts(&mesh, &LIMITS).unwrap_err().to_string();
        assert!(error.contains("right angle but for rounding"), "{error}");
    }

    /// Off the coordinate axes a set of the very same area as the winner's is refused unless
    /// the two areas stay equal over a sweep of the axes' scales: equal by their shape.
    #[test]
    fn congruent_sets_off_the_axes_are_refused_unless_they_stay_tied_by_shape() {
        let mut centred = solid([4.0, 3.0, 5.0]);
        turn_in_mesh(&mut centred, 45.0, 0.0);
        // Opposite faces about the mesh's origin are normalised from the same squared lengths,
        // and their areas stay equal over the sweep.
        assert_eq!(
            auto_charts(&centred.mesh(), &LIMITS)
                .unwrap()
                .triangles
                .len(),
            6
        );
        let mut moved = solid([4.0, 3.0, 5.0]);
        turn_in_mesh(&mut moved, 45.0, 0.0);
        for p in &mut moved.positions {
            *p = [p[0] + 1.3, p[1] + 0.7, p[2] - 2.1];
        }
        let error = auto_charts(&moved.mesh(), &LIMITS).unwrap_err().to_string();
        assert!(
            error.contains("largest of a round but for rounding"),
            "{error}"
        );
        let opposite_faces_settle = |shape: &Shape| {
            let mesh = shape.mesh();
            let geometry = geometry(&mesh, &LIMITS);
            (0..6).all(|a| {
                let n = geometry.groups[a].normal;
                let b = (0..6)
                    .find(|&o| dot(geometry.groups[o].normal, n) < -0.999)
                    .unwrap();
                let (pa, pb) = (&geometry.groups[a].plane, &geometry.groups[b].plane);
                pa.along_axes() || stays_tied(&geometry, (&[a], pa), (&[b], pb))
            })
        };
        assert!(opposite_faces_settle(&centred));
        assert!(!opposite_faces_settle(&moved));

        // Two rectangles turned alike, one larger by two parts in a million: the larger would
        // win for every scale of the axes, and the margin is still the rounding's.
        let mut pair = Shape::default();
        for (x, depth) in [(0.0, 0.7), (10.0, 0.7 * (1.0 + 2e-6))] {
            pair.quad(
                [
                    [x, 0.0, 0.0],
                    [x + 2.2, 0.0, 0.0],
                    [x + 2.2, 0.0, depth],
                    [x, 0.0, depth],
                ],
                [0.0, 1.0, 0.0],
            );
        }
        turn_in_mesh(&mut pair, 37.0, 23.0);
        let error = auto_charts(&pair.mesh(), &LIMITS).unwrap_err().to_string();
        assert!(
            error.contains("largest of a round but for rounding"),
            "{error}"
        );
    }

    /// Two rectangles of the same size that lie differently: their areas along their planes
    /// differ in the last digits, and which is taken first would be the rounding's. Along the
    /// coordinate axes the arithmetic is exact, and the first tried is.
    #[test]
    fn sets_that_tie_but_for_rounding_fail() {
        let pair = |yaw: f32, pitch: f32| {
            let mut first = Shape::default();
            first.quad(
                [
                    [0.0, 0.0, 0.0],
                    [2.2, 0.0, 0.0],
                    [2.2, 0.0, 0.7],
                    [0.0, 0.0, 0.7],
                ],
                [0.0, 1.0, 0.0],
            );
            turn_in_mesh(&mut first, yaw, pitch);
            let mut both = Shape::default();
            both.quad(
                [
                    [10.0, 0.0, 0.0],
                    [10.7, 0.0, 0.0],
                    [10.7, 0.0, 2.2],
                    [10.0, 0.0, 2.2],
                ],
                [0.0, 1.0, 0.0],
            );
            both.positions.extend(first.positions);
            both.normals.extend(first.normals);
            both.uvs.extend(first.uvs);
            both.triangles.extend([[4, 6, 5], [4, 7, 6]]);
            both
        };
        let level = pair(0.0, 0.0);
        assert_eq!(
            auto_charts(&level.mesh(), &LIMITS).unwrap().triangles.len(),
            2
        );
        let error = auto_charts(&pair(37.0, 23.0).mesh(), &LIMITS)
            .unwrap_err()
            .to_string();
        assert!(
            error.contains("largest of a round but for rounding"),
            "{error}"
        );
        assert!(tied(1.0, 1.000_001) && !tied(1.0, 1.0) && !tied(1.0, 1.001));
    }

    #[test]
    fn a_bent_part_left_on_its_own_fails() {
        // Two quads 40 degrees apart that share their ridge's vertices, lightmap UVs and
        // normals: one part, flat within the limits, and not planar.
        let (sin, cos) = 20.0f32.to_radians().sin_cos();
        let mut bend = Shape::default();
        bend.positions = vec![
            [-cos, -sin, 0.0],
            [0.0, 0.0, 0.0],
            [cos, -sin, 0.0],
            [-cos, -sin, 1.0],
            [0.0, 0.0, 1.0],
            [cos, -sin, 1.0],
        ];
        bend.normals = vec![
            [-sin, cos, 0.0],
            [0.0, 1.0, 0.0],
            [sin, cos, 0.0],
            [-sin, cos, 0.0],
            [0.0, 1.0, 0.0],
            [sin, cos, 0.0],
        ];
        bend.uvs = vec![
            [0.0, 0.0],
            [0.5, 0.0],
            [1.0, 0.0],
            [0.0, 1.0],
            [0.5, 1.0],
            [1.0, 1.0],
        ];
        bend.triangles = vec![[0, 3, 4], [0, 4, 1], [1, 4, 5], [1, 5, 2]];
        let error = auto_charts(&bend.mesh(), &LIMITS).unwrap_err().to_string();
        assert!(error.contains("bent part"), "{error}");
        // Beside a second part it is taken in a round, and projected onto its plane.
        bend.quad(
            [
                [3.0, 0.0, 0.0],
                [3.5, 0.0, 0.0],
                [3.5, 0.0, 0.5],
                [3.0, 0.0, 0.5],
            ],
            [0.0, 1.0, 0.0],
        );
        let charts = auto_charts(&bend.mesh(), &LIMITS).unwrap();
        assert_eq!(charts.triangles, [vec![0, 3, 2, 1], vec![4, 5]]);
        let extent = extents(&bend, &charts)[0];
        assert!(
            near(extent[0], 2.0 * cos) && near(extent[1], 1.0),
            "{extent:?}"
        );
    }

    #[test]
    fn parts_without_area_or_normals_and_ignored_normals_fail_by_name() {
        let mut cube = solid([1.0, 1.0, 1.0]);
        cube.positions.extend([[5.0, 5.0, 5.0]; 3]);
        cube.normals.extend([[0.0, 1.0, 0.0]; 3]);
        cube.uvs.extend([[0.0, 0.0]; 3]);
        cube.triangles.push([24, 25, 26]);
        // A part without area that lies by no chart is a chart of its own, at its corner.
        let charts = auto_charts(&cube.mesh(), &LIMITS).unwrap();
        assert_eq!(charts.triangles.len(), 7);
        assert_eq!(charts.triangles[6], [12]);
        assert!(charts.uvs[24..].iter().all(|uv| *uv == [0.0, 0.0]));
        // Three points on a line are one too.
        cube.positions[25] = [5.0, 5.2, 5.0];
        cube.positions[26] = [5.0, 5.4, 5.0];
        let charts = auto_charts(&cube.mesh(), &LIMITS).unwrap();
        assert_eq!(charts.triangles[6], [12]);
        assert!(charts.uvs[24..].iter().all(|uv| *uv == [0.0, 0.0]));

        let mut cube = solid([1.0, 1.0, 1.0]);
        for normal in &mut cube.normals[..4] {
            *normal = [0.0; 3];
        }
        let error = auto_charts(&cube.mesh(), &LIMITS).unwrap_err().to_string();
        assert!(error.contains("no normal"), "{error}");
    }

    /// Max Angle bounds each part's lean from the plane through a pair, Max Distance its
    /// vertices' distance from it.
    #[test]
    fn the_limits_decide_what_is_joined() {
        // Two quads 40 degrees apart, each 20 from the plane through both.
        let (sin, cos) = 20.0f32.to_radians().sin_cos();
        let mut roof = Shape::default();
        roof.quad(
            [
                [0.0, 0.0, 0.0],
                [0.0, 0.0, 2.0],
                [cos, -sin, 2.0],
                [cos, -sin, 0.0],
            ],
            [sin, cos, 0.0],
        );
        roof.quad(
            [
                [0.0, 0.0, 0.0],
                [-cos, -sin, 0.0],
                [-cos, -sin, 2.0],
                [0.0, 0.0, 2.0],
            ],
            [-sin, cos, 0.0],
        );
        let with = |max_angle: f32, max_distance: f32| {
            let limits = Params {
                max_angle,
                max_distance,
                ..LIMITS
            };
            auto_charts(&roof.mesh(), &limits).unwrap().triangles.len()
        };
        assert_eq!(with(30.0, 0.5), 1);
        assert_eq!(with(15.0, 0.5), 2);
        // The eaves are sin(20) / 2 = 0.17 from that plane.
        assert_eq!(with(30.0, 0.2), 1);
        assert_eq!(with(30.0, 0.15), 2);
    }

    /// The walk over the neighbour lists gathers a set in the order of the plain scan.
    #[test]
    fn a_set_is_gathered_in_the_order_of_the_plain_scan() {
        // A strip of 40 coplanar quads and a grid of 8 by 8, each quad its own part, numbered
        // out of order; in the grid a part has up to eight neighbours, so a group moved into a
        // taken one's place is often a neighbour of the same member.
        let cells = |columns: usize, rows: usize, step: usize| {
            let mut shape = Shape::default();
            let count = columns * rows;
            for i in 0..count {
                let cell = (i * step) % count;
                let (x, z) = ((cell % columns) as f32, (cell / columns) as f32);
                shape.quad(
                    [
                        [x, 0.0, z],
                        [x + 1.0, 0.0, z],
                        [x + 1.0, 0.0, z + 1.0],
                        [x, 0.0, z + 1.0],
                    ],
                    [0.0, 1.0, 0.0],
                );
            }
            shape
        };
        let mut shapes = vec![cells(40, 1, 17)];
        shapes.extend([1, 7, 9, 23, 39, 57].map(|step| cells(8, 8, step)));
        for shape in &shapes {
            let mesh = shape.mesh();
            let geometry = geometry(&mesh, &LIMITS);
            let candidates: Vec<usize> = (0..geometry.groups.len()).collect();
            let mut neighbours = Neighbours::new(&geometry, &candidates);
            let retired = candidates.len() / 2;
            neighbours.retire(retired);
            let mut place = vec![NONE; candidates.len()];
            for seed in candidates.iter().copied().filter(|&g| g != retired) {
                let others: Vec<usize> = candidates
                    .iter()
                    .copied()
                    .filter(|&g| g != seed && g != retired)
                    .collect();
                let (mut walked, mut left) = (vec![seed], others.clone());
                take_connected(&neighbours, &mut walked, &mut left, &mut place);
                let (mut scanned, mut rest) = (vec![seed], others);
                take_connected_plainly(&neighbours, &mut scanned, &mut rest);
                assert_eq!(walked, scanned);
                assert_eq!(left, rest);
                assert!(walked.len() > 1);
                assert!(place.iter().all(|&p| p == NONE));
            }
        }
    }

    #[test]
    fn a_lone_chart_on_a_tilted_plane_is_projected_when_its_own_uvs_are_not_true_to_it() {
        let stretched = tilted([[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]]);
        let charts = auto_charts(&stretched.mesh(), &LIMITS).unwrap();
        assert_eq!(charts.triangles, [vec![0, 1]]);
        let extent = extents(&stretched, &charts)[0];
        assert!(near(extent[0], 2.0) && near(extent[1], 1.0), "{extent:?}");

        // Own UVs that are a scaled copy of the projection: either gives the same chart.
        let projected: [[f32; 2]; 4] =
            std::array::from_fn(|v| charts.uvs[v].map(|x| 0.1 + x * 0.25));
        assert_eq!(
            auto_charts(&tilted(projected).mesh(), &LIMITS).unwrap(),
            charts
        );

        // Own UVs that are a turned copy of the shape: as true as the projection, another chart.
        let turned: [[f32; 2]; 4] = std::array::from_fn(|v| {
            let [x, y] = charts.uvs[v];
            [0.5 + (x - y) * 0.2, (x + y) * 0.2]
        });
        let error = auto_charts(&tilted(turned).mesh(), &LIMITS)
            .unwrap_err()
            .to_string();
        assert!(error.contains("tilted plane"), "{error}");
    }

    #[test]
    fn parallel_and_perpendicular_directions_are_one() {
        let diagonal = normalized([1.0, 1.0, 0.0]);
        let mut directions = Directions::new();
        assert_eq!(directions.best(), None);
        directions.add([1.0, 0.0, 0.0], 1.0);
        directions.add(diagonal, 2.5);
        directions.add([0.0, 1.0, 0.0], 1.0);
        directions.add([-1.0, 0.0, 0.0], 1.0);
        assert_eq!(directions.nodes.len(), 2);
        assert_eq!(directions.best(), Some([1.0, 0.0, 0.0]));
        // Of two directions with the same area, the first by its components.
        let mut directions = Directions::new();
        directions.add([1.0, 0.0, 0.0], 2.0);
        directions.add(diagonal, 2.0);
        assert_eq!(directions.best(), Some(diagonal));
    }
}
