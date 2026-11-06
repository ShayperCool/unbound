//! The unwrap's preparation of the input: the mesh is fitted into a unit cube, then vertices
//! closer than 1e-6 weld unless their triangles face opposite ways, renumbering them in the
//! order the triangles' corners first use them. Degenerate triangles (repeated or coincident
//! corners, an edge shorter than 1e-6, an angle under 0.001) and non-manifold edges and vertices
//! are not supported.

use super::math;

pub struct Geometry {
    pub positions: Vec<[f32; 3]>,
    pub triangles: Vec<[u32; 3]>,
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
struct VertexHash {
    buckets: Vec<i32>,
    next: Vec<i32>,
    positions: Vec<[f64; 3]>,
    normals: Vec<[f64; 3]>,
}

fn hash_cell(x: i32, y: i32, z: i32) -> u32 {
    let h = x
        .wrapping_mul(-1918454973)
        .wrapping_add(y.wrapping_mul(-669632447))
        .wrapping_add(z.wrapping_mul(-887442657));
    h as u32 % BUCKETS
}

impl VertexHash {
    /// The index of the vertex `p` welds to, adding it when none.
    fn insert(&mut self, p: [f64; 3], normal: [f64; 3]) -> u32 {
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
                if dz * dz + dy * dy + dx * dx < RADIUS * RADIUS {
                    let n = self.normals[i as usize];
                    let dot = n[2] * normal[2] + n[0] * normal[0] + n[1] * normal[1];
                    if WELD_MIN_NORMAL_DOT <= dot && dot != WELD_MIN_NORMAL_DOT {
                        return i as u32;
                    }
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
        self.normals.push(normal);
        self.next.push(self.buckets[cell as usize]);
        self.buckets[cell as usize] = index;
        index as u32
    }
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

/// Whether an edge has more than two triangles or two running the same way, or a vertex's
/// triangles form more than one fan.
fn non_manifold(vertex_count: usize, triangles: &[[u32; 3]]) -> bool {
    use std::collections::HashMap;
    let mut directed: HashMap<(u32, u32), u32> = HashMap::new();
    for t in triangles {
        for k in 0..3 {
            let count = directed.entry((t[k], t[(k + 1) % 3])).or_insert(0);
            *count += 1;
            if *count > 1 {
                return true;
            }
        }
    }
    // Around each vertex, the triangles must be one fan: walk from a triangle across the edges
    // through the vertex and count the triangles reached.
    let mut around: Vec<Vec<usize>> = vec![Vec::new(); vertex_count];
    for (i, t) in triangles.iter().enumerate() {
        for &v in t {
            around[v as usize].push(i);
        }
    }
    for (v, tris) in around.iter().enumerate() {
        if tris.len() < 2 {
            continue;
        }
        let v = v as u32;
        let mut reached = vec![tris[0]];
        let mut i = 0;
        while i < reached.len() {
            let t = triangles[reached[i]];
            let k = t.iter().position(|&c| c == v).unwrap();
            for other in [t[(k + 1) % 3], t[(k + 2) % 3]] {
                for &u in tris {
                    if !reached.contains(&u) && triangles[u].contains(&other) {
                        reached.push(u);
                    }
                }
            }
            i += 1;
        }
        if reached.len() != tris.len() {
            return true;
        }
    }
    false
}

/// The geometry the unwrap parametrizes, its triangles in their input order.
pub fn prepare(positions: &[[f32; 3]], triangles: &[[u32; 3]]) -> Result<Geometry, String> {
    let mut fitted = positions.to_vec();
    center_and_scale(&mut fitted);
    let doubles: Vec<[f64; 3]> = fitted.iter().map(|p| p.map(f64::from)).collect();
    for t in triangles {
        if t[0] == t[1] || t[1] == t[2] || t[2] == t[0] {
            return Err("the mesh has degenerate triangles".into());
        }
    }
    let mut hash = VertexHash {
        buckets: vec![-1; BUCKETS as usize],
        next: Vec::new(),
        positions: Vec::new(),
        normals: Vec::new(),
    };
    let mut welded = Vec::with_capacity(triangles.len());
    for t in triangles {
        let p = t.map(|v| doubles[v as usize]);
        let normal = triangle_normal(p);
        welded.push(t.map(|v| hash.insert(doubles[v as usize], normal)));
    }
    for t in &welded {
        if t[0] == t[1]
            || t[1] == t[2]
            || t[2] == t[0]
            || degenerate(t.map(|v| hash.positions[v as usize]))
        {
            return Err("the mesh has degenerate triangles".into());
        }
    }
    if non_manifold(hash.positions.len(), &welded) {
        return Err("after welding, the mesh has non-manifold edges or vertices".into());
    }
    Ok(Geometry {
        positions: hash.positions.iter().map(|p| p.map(|c| c as f32)).collect(),
        triangles: welded,
    })
}
