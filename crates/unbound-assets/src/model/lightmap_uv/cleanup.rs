//! The unwrap's preparation of the input (matches the Unity 6000.0.71f1 Editor,
//! tests/projects/lightmapuvs): the mesh is fitted into a unit cube, then vertices closer than
//! 1e-6 weld unless their triangles face opposite ways, renumbering them in the order the
//! triangles' corners first use them. Degenerate triangles are removed ([`remove_degenerate`]),
//! vertices where the mesh is not a single fan are split ([`split_non_manifold`]), and triangles
//! that split made degenerate are removed again.

use super::math;

pub struct Geometry {
    pub positions: Vec<[f32; 3]>,
    pub triangles: Vec<[u32; 3]>,
    /// The input triangle each triangle comes from.
    pub sources: Vec<u32>,
    /// The input triangles the cleanup removed.
    pub removed: Vec<u32>,
}

const RADIUS: f64 = 1e-6;
const CELL: f64 = 4.9999999999999996e-6;
const BUCKETS: u32 = 512;
const WELD_MIN_NORMAL_DOT: f64 = -0.9;

/// Centers the positions on their mean and scales the largest extent to 1/2.
fn center_and_scale(positions: &mut [[f32; 3]]) {
    if positions.is_empty() {
        return;
    }
    let inv = 1.0f32 / positions.len() as f32;
    let mut center = [-0.0f32; 3];
    let mut min = [1e32f32; 3];
    let mut max = [-1e32f32; 3];
    for p in positions.iter() {
        for i in 0..3 {
            if !(min[i] <= p[i]) {
                min[i] = p[i];
            }
            if !(p[i] <= max[i]) {
                max[i] = p[i];
            }
            center[i] += inv * p[i];
        }
    }
    let mut extent = max[0] - min[0];
    if extent <= 9e-7 {
        extent = 9e-7;
    }
    if !(max[1] - min[1] <= extent) {
        extent = max[1] - min[1];
    }
    if !(max[2] - min[2] <= extent) {
        extent = max[2] - min[2];
    }
    if 1e-6 <= extent && extent <= 1e6 {
        let scale = (1.0f32 * 0.5) / extent;
        for p in positions.iter_mut() {
            for i in 0..3 {
                p[i] = (p[i] - center[i]) * scale;
            }
        }
    }
}

fn triangle_normal(p: [[f64; 3]; 3]) -> [f64; 3] {
    let e = [p[1][0] - p[0][0], p[1][1] - p[0][1], p[1][2] - p[0][2]];
    let f = [p[2][0] - p[0][0], p[2][1] - p[0][1], p[2][2] - p[0][2]];
    let nx = e[1] * f[2] - e[2] * f[1];
    let nz = e[0] * f[1] - f[0] * e[1];
    let ny = e[2] * f[0] - f[2] * e[0];
    let len2 = ny * ny + nx * nx + nz * nz;
    if len2 <= 0.0 {
        return [0.0; 3];
    }
    let len = len2.sqrt();
    if len <= 1e-10 {
        return [0.0; 3];
    }
    [nx / len, ny / len, nz / len]
}

/// The spatial hash that welds vertices: 512 buckets of chains, newest first.
pub struct SpaceHash {
    buckets: Vec<i32>,
    next: Vec<i32>,
    pub positions: Vec<[f64; 3]>,
}

fn hash_cell(x: i32, y: i32, z: i32) -> u32 {
    let h = x
        .wrapping_mul(-1918454973)
        .wrapping_add(y.wrapping_mul(-669632447))
        .wrapping_add(z.wrapping_mul(-887442657));
    h as u32 % BUCKETS
}

impl SpaceHash {
    pub fn new() -> Self {
        Self {
            buckets: vec![-1; BUCKETS as usize],
            next: Vec::new(),
            positions: Vec::new(),
        }
    }

    /// The index of the first entry closer than 1e-6 to `p` that `accept`s it, searching the
    /// cells around `p`; a new entry when none (its index is the old length).
    pub fn insert(&mut self, p: [f64; 3], mut accept: impl FnMut(usize) -> bool) -> u32 {
        let lo = |c: f64| ((c - RADIUS) / CELL) as i32;
        let hi = |c: f64| ((RADIUS + c) / CELL) as i32;
        let (x0, y0, z0) = (lo(p[0]), lo(p[1]), lo(p[2]));
        let (x1, y1, z1) = (hi(p[0]), hi(p[1]), hi(p[2]));
        let cells = [
            hash_cell(x0, y0, z0),
            hash_cell(x0, y0, z1),
            hash_cell(x0, y1, z0),
            hash_cell(x0, y1, z1),
            hash_cell(x1, y0, z0),
            hash_cell(x1, y0, z1),
            hash_cell(x1, y1, z0),
            hash_cell(x1, y1, z1),
        ];
        let mut visited: Vec<u32> = Vec::with_capacity(8);
        for &cell in &cells {
            if visited.contains(&cell) {
                continue;
            }
            visited.push(cell);
            let mut i = self.buckets[cell as usize];
            while i >= 0 {
                let q = self.positions[i as usize];
                let (dy, dx, dz) = (p[1] - q[1], p[0] - q[0], p[2] - q[2]);
                if dz * dz + dy * dy + dx * dx < RADIUS * RADIUS && accept(i as usize) {
                    return i as u32;
                }
                i = self.next[i as usize];
            }
        }
        let cell = hash_cell(
            (p[0] / CELL) as i32,
            (p[1] / CELL) as i32,
            (p[2] / CELL) as i32,
        );
        let index = self.positions.len() as i32;
        self.positions.push(p);
        self.next.push(self.buckets[cell as usize]);
        self.buckets[cell as usize] = index;
        index as u32
    }
}

/// Welds vertices closer than 1e-6 unless their triangles face opposite ways, renumbering them
/// in the order the triangles' corners first use them.
pub fn weld(positions: &[[f64; 3]], triangles: &[[u32; 3]]) -> (Vec<[f64; 3]>, Vec<[u32; 3]>) {
    let mut hash = SpaceHash::new();
    let mut normals: Vec<[f64; 3]> = Vec::new();
    let mut welded = Vec::with_capacity(triangles.len());
    for t in triangles {
        let normal = triangle_normal(t.map(|v| positions[v as usize]));
        welded.push(t.map(|v| {
            let i = hash.insert(positions[v as usize], |i| {
                let n = normals[i];
                let dot = n[2] * normal[2] + n[0] * normal[0] + n[1] * normal[1];
                WELD_MIN_NORMAL_DOT <= dot && dot != WELD_MIN_NORMAL_DOT
            });
            if i as usize == normals.len() {
                normals.push(normal);
            }
            i
        }));
    }
    (hash.positions, welded)
}

fn length(d: [f64; 3]) -> f64 {
    let l2 = d[0] * d[0] + d[1] * d[1] + d[2] * d[2];
    if 0.0 < l2 { l2.sqrt() } else { 0.0 }
}

/// Whether the cleanup would remove the triangle.
fn degenerate(p: [[f64; 3]; 3]) -> bool {
    let e01 = [p[1][0] - p[0][0], p[1][1] - p[0][1], p[1][2] - p[0][2]];
    let e12 = [p[2][0] - p[1][0], p[2][1] - p[1][1], p[2][2] - p[1][2]];
    let e20 = [p[0][0] - p[2][0], p[0][1] - p[2][1], p[0][2] - p[2][2]];
    let cross = [
        e01[1] * -e20[2] - e01[2] * -e20[1],
        e01[2] * -e20[0] - e01[0] * -e20[2],
        e01[0] * -e20[1] - e01[1] * -e20[0],
    ];
    let area = length(cross);
    if (if 0.0 < area { area.sqrt() } else { 0.0 }) < 1e-10 {
        return true;
    }
    let (l01, l12, l20) = (length(e01), length(e12), length(e20));
    if l01 < 1e-6 || l12 < 1e-6 || l20 < 1e-6 {
        return true;
    }
    let dot = |a: [f64; 3], la: f64, b: [f64; 3], lb: f64| {
        (a[0] / la) * (b[0] / lb) + (a[1] / la) * (b[1] / lb) + (a[2] / la) * (b[2] / lb)
    };
    let angles = [
        std::f64::consts::PI - math::acos(dot(e01, l01, e20, l20)),
        std::f64::consts::PI - math::acos(dot(e01, l01, e12, l12)),
        std::f64::consts::PI - math::acos(dot(e12, l12, e20, l20)),
    ];
    angles.iter().any(|&a| a < 0.001)
}

/// Removes the triangles with a repeated corner or [`degenerate`] ones, walking from the last
/// triangle to the first: a removed triangle's place is taken by the last kept one, and their
/// entries in `sources` swap, so the removed sources end up after the kept ones. Then the vertices
/// no triangle uses are dropped, keeping the others in order.
fn remove_degenerate(
    positions: &mut Vec<[f64; 3]>,
    triangles: &mut Vec<[u32; 3]>,
    sources: &mut [u32],
) {
    let mut live = triangles.len();
    for i in (0..triangles.len()).rev() {
        let t = triangles[i];
        let repeated = t[0] == t[1] || t[1] == t[2] || t[2] == t[0];
        if repeated || degenerate(t.map(|v| positions[v as usize])) {
            live -= 1;
            triangles[i] = triangles[live];
            sources.swap(i, live);
        }
    }
    triangles.truncate(live);
    let mut used = vec![false; positions.len()];
    for t in triangles.iter() {
        for &v in t {
            used[v as usize] = true;
        }
    }
    let mut remap: Vec<u32> = (0..positions.len() as u32).collect();
    let mut kept = used.iter().position(|&u| !u).unwrap_or(positions.len());
    for v in kept..positions.len() {
        if used[v] {
            positions[kept] = positions[v];
            remap[v] = kept as u32;
            kept += 1;
        }
    }
    positions.truncate(kept);
    for t in triangles.iter_mut() {
        *t = t.map(|v| remap[v as usize]);
    }
}

/// The edges of the triangles: for each, the triangles along it with the corner it starts at.
struct EdgeTriangles {
    edges: std::collections::HashMap<(u32, u32), Vec<(u32, u8)>>,
}

impl EdgeTriangles {
    fn new(triangles: &[[u32; 3]]) -> Self {
        let mut edges: std::collections::HashMap<(u32, u32), Vec<(u32, u8)>> =
            std::collections::HashMap::with_capacity(triangles.len() * 3 / 2);
        for (i, t) in triangles.iter().enumerate() {
            for k in 0..3 {
                let (a, b) = (t[k], t[(k + 1) % 3]);
                edges
                    .entry((a.min(b), a.max(b)))
                    .or_default()
                    .push((i as u32, k as u8));
            }
        }
        Self { edges }
    }

    fn get(&self, a: u32, b: u32) -> &[(u32, u8)] {
        self.edges
            .get(&(a.min(b), a.max(b)))
            .map_or(&[], |v| v.as_slice())
    }
}

/// Whether the edge's triangles keep the mesh a surface there: more than two triangles, or two
/// that run the same way along it or share their third corner, do not.
fn manifold_edge(triangles: &[[u32; 3]], along: &[(u32, u8)]) -> bool {
    match along {
        [_] => true,
        [(a, ka), (b, kb)] => {
            let (ta, tb) = (triangles[*a as usize], triangles[*b as usize]);
            let (ka, kb) = (*ka as usize, *kb as usize);
            ta[ka] != tb[kb] && ta[(ka + 2) % 3] != tb[(kb + 2) % 3]
        }
        _ => false,
    }
}

/// Splits the vertices around which the triangles do not form one fan: the triangles at a
/// vertex are grouped by crossing the manifold edges between them, the group of the triangle
/// listed first (the vertex's triangles from the last to the first) keeps the vertex, and every
/// other group gets a new copy of it, appended in vertex order. Returns the vertex each copy is
/// made from.
fn split_non_manifold(vertex_count: usize, triangles: &mut [[u32; 3]]) -> Vec<u32> {
    let edges = EdgeTriangles::new(triangles);
    let mut around: Vec<Vec<u32>> = vec![Vec::new(); vertex_count];
    for (i, t) in triangles.iter().enumerate().rev() {
        for &v in t {
            around[v as usize].push(i as u32);
        }
    }
    // Per vertex, the group of each of its triangles (in `around` order) and the group count.
    let mut groups: Vec<(Vec<u32>, u32)> = Vec::with_capacity(vertex_count);
    let mut stack = Vec::new();
    for tris in &around {
        let mut labels = vec![u32::MAX; tris.len()];
        let mut count = 0;
        for seed in 0..tris.len() {
            if labels[seed] != u32::MAX {
                continue;
            }
            labels[seed] = count;
            stack.push(seed);
            while let Some(i) = stack.pop() {
                let t = triangles[tris[i] as usize];
                for k in 0..3 {
                    let along = edges.get(t[k], t[(k + 1) % 3]);
                    if along.len() < 2 || !manifold_edge(triangles, along) {
                        continue;
                    }
                    let other = if along[0].0 == tris[i] {
                        along[1].0
                    } else {
                        along[0].0
                    };
                    // `tris` runs from the last triangle to the first.
                    if let Ok(j) = tris.binary_search_by(|x| other.cmp(x))
                        && labels[j] == u32::MAX
                    {
                        labels[j] = count;
                        stack.push(j);
                    }
                }
            }
            count += 1;
        }
        groups.push((labels, count));
    }
    let mut copies = Vec::new();
    for v in 0..vertex_count {
        let (labels, count) = &groups[v];
        if *count <= 1 {
            continue;
        }
        let first = (vertex_count + copies.len()) as u32;
        for _ in 1..*count {
            copies.push(v as u32);
        }
        for (&t, &label) in around[v].iter().zip(labels) {
            if label != 0 {
                for c in triangles[t as usize].iter_mut() {
                    if *c == v as u32 {
                        *c = first + label - 1;
                    }
                }
            }
        }
    }
    copies
}

/// The cleanup of a mesh: welding, then the removal of degenerate triangles, the split of
/// non-manifold vertices and a second removal.
pub struct Cleaned {
    pub positions: Vec<[f64; 3]>,
    pub triangles: Vec<[u32; 3]>,
    /// The input triangle each triangle comes from.
    pub sources: Vec<u32>,
    /// The input triangles the cleanup removed.
    pub removed: Vec<u32>,
}

pub fn clean(positions: &[[f64; 3]], triangles: &[[u32; 3]]) -> Cleaned {
    let (mut positions, mut triangles) = weld(positions, triangles);
    let mut sources: Vec<u32> = (0..triangles.len() as u32).collect();
    remove_degenerate(&mut positions, &mut triangles, &mut sources);
    let copies = split_non_manifold(positions.len(), &mut triangles);
    for v in copies {
        positions.push(positions[v as usize]);
    }
    let live = triangles.len();
    remove_degenerate(&mut positions, &mut triangles, &mut sources[..live]);
    let removed = sources.split_off(triangles.len());
    Cleaned {
        positions,
        triangles,
        sources,
        removed,
    }
}

/// The geometry the unwrap parametrizes: the mesh fitted into a unit cube and cleaned; none when
/// the cleanup leaves no triangle.
pub fn prepare(positions: &[[f32; 3]], triangles: &[[u32; 3]]) -> Option<Geometry> {
    let mut fitted = positions.to_vec();
    center_and_scale(&mut fitted);
    let doubles: Vec<[f64; 3]> = fitted.iter().map(|p| p.map(f64::from)).collect();
    let cleaned = clean(&doubles, triangles);
    if cleaned.triangles.is_empty() {
        return None;
    }
    Some(Geometry {
        positions: cleaned
            .positions
            .iter()
            .map(|p| p.map(|c| c as f32))
            .collect(),
        triangles: cleaned.triangles,
        sources: cleaned.sources,
        removed: cleaned.removed,
    })
}
