//! The coarse mesh a large connected part is segmented on first: the part is simplified by
//! collapsing one vertex at a time into a neighbour, cheapest first by an error quadric over
//! position and UV0, and the collapses are then undone in steps so that a segmentation of the
//! coarse mesh carries over to the finer ones (the resulting UV2 matches the Unity 6000.0.71f1
//! Editor's: tests/projects/lightmapuvs).
//!
//! Facets keep the index of the triangle they come from; a collapse removes the two facets
//! along the edge and moves the vertex's other half-edges to the neighbour.

use super::cleanup;
use super::mesh::NONE;

/// UV0's two coordinates take part in the quadrics.
const ATTRIBUTES: usize = 2;
/// Doubles per quadric: the plane part, then a gradient and offset per attribute.
const QUADRIC: usize = 10 + 4 * ATTRIBUTES;
const MAX_COST: f64 = 1e10;
/// Costs closer than this tie and are ordered by vertex index.
const COST_TIE: f64 = 1e-6;
const SHARP_WEIGHT: f64 = 10000.0;
const UV_SEAM: f64 = 1e-10;
const UV_WELD: f64 = 1e-6;
const DEGENERATE_COS: f64 = 0.999;

#[derive(Debug, Clone, Copy)]
struct Edge {
    twin: u32,
    next: u32,
    prev: u32,
    /// `NONE` on the border.
    facet: u32,
    /// The vertex the half-edge points to.
    vertex: u32,
}

/// One collapse: the half-edge whose source merges into its target, the half-edges that took
/// the place of the removed facets' sides, and the sharp half-edge where the UV0 of the merged
/// vertex's fan changes.
#[derive(Debug, Clone, Copy)]
struct Collapse {
    edge: u32,
    sides: [u32; 2],
    sharp: u32,
}

pub struct Simplification {
    positions: Vec<[f64; 3]>,
    /// A half-edge pointing to each vertex.
    vertex_edge: Vec<u32>,
    edges: Vec<Edge>,
    facet_edge: Vec<u32>,
    facet_alive: Vec<bool>,
    live_facets: usize,
    /// The UV0 entry of each half-edge's target corner.
    attribute: Vec<u32>,
    uvs: Vec<[f64; 2]>,
    collapses: Vec<Collapse>,
    /// How many of `collapses` are applied.
    applied: usize,
}

#[derive(Debug, Clone, Copy)]
struct Key {
    vertex: u32,
    cost: f64,
}

fn less(a: Key, b: Key) -> bool {
    if COST_TIE <= (a.cost - b.cost).abs() {
        a.cost < b.cost
    } else {
        a.vertex < b.vertex
    }
}

fn sqrt_pos(x: f64) -> f64 {
    if 0.0 < x { x.sqrt() } else { 0.0 }
}

fn sub(a: [f64; 3], b: [f64; 3]) -> [f64; 3] {
    [a[0] - b[0], a[1] - b[1], a[2] - b[2]]
}

/// The plane part of a quadric: `n nᵀ`, `-d n` and `d²` for the plane through `p` with unit
/// normal `n`.
fn plane_quadric(q: &mut [f64], n: [f64; 3], p: [f64; 3]) {
    let d = n[0] * p[0] + n[1] * p[1] + n[2] * p[2];
    let m = -d;
    q[0] = n[0] * n[0];
    q[1] = n[0] * n[1];
    q[2] = n[2] * n[0];
    q[3] = n[1] * n[1];
    q[4] = n[2] * n[1];
    q[5] = n[2] * n[2];
    q[6] = m * n[0];
    q[7] = m * n[1];
    q[8] = m * n[2];
    q[9] = d * d;
}

/// The adjugate of a row-major 4×4 matrix.
fn adjugate(m: &[f64; 16]) -> [f64; 16] {
    let [
        m0,
        m1,
        m2,
        m3,
        m4,
        m5,
        m6,
        m7,
        m8,
        m9,
        m10,
        m11,
        m12,
        m13,
        m14,
        m15,
    ] = *m;
    [
        m5 * m10 * m15 - m5 * m11 * m14 - m9 * m6 * m15 + m9 * m7 * m14 + m13 * m6 * m11
            - m13 * m7 * m10,
        -m1 * m10 * m15 + m1 * m11 * m14 + m9 * m2 * m15 - m9 * m3 * m14 - m13 * m2 * m11
            + m13 * m3 * m10,
        m1 * m6 * m15 - m1 * m7 * m14 - m5 * m2 * m15 + m5 * m3 * m14 + m13 * m2 * m7
            - m13 * m3 * m6,
        -m1 * m6 * m11 + m1 * m7 * m10 + m5 * m2 * m11 - m5 * m3 * m10 - m9 * m2 * m7
            + m9 * m3 * m6,
        -m4 * m10 * m15 + m4 * m11 * m14 + m8 * m6 * m15 - m8 * m7 * m14 - m12 * m6 * m11
            + m12 * m7 * m10,
        m0 * m10 * m15 - m0 * m11 * m14 - m8 * m2 * m15 + m8 * m3 * m14 + m12 * m2 * m11
            - m12 * m3 * m10,
        -m0 * m6 * m15 + m0 * m7 * m14 + m4 * m2 * m15 - m4 * m3 * m14 - m12 * m2 * m7
            + m12 * m3 * m6,
        m0 * m6 * m11 - m0 * m7 * m10 - m4 * m2 * m11 + m4 * m3 * m10 + m8 * m2 * m7 - m8 * m3 * m6,
        m4 * m9 * m15 - m4 * m11 * m13 - m8 * m5 * m15 + m8 * m7 * m13 + m12 * m5 * m11
            - m12 * m7 * m9,
        -m0 * m9 * m15 + m0 * m11 * m13 + m8 * m1 * m15 - m8 * m3 * m13 - m12 * m1 * m11
            + m12 * m3 * m9,
        m0 * m5 * m15 - m0 * m7 * m13 - m4 * m1 * m15 + m4 * m3 * m13 + m12 * m1 * m7
            - m12 * m3 * m5,
        -m0 * m5 * m11 + m0 * m7 * m9 + m4 * m1 * m11 - m4 * m3 * m9 - m8 * m1 * m7 + m8 * m3 * m5,
        -m4 * m9 * m14 + m4 * m10 * m13 + m8 * m5 * m14 - m8 * m6 * m13 - m12 * m5 * m10
            + m12 * m6 * m9,
        m0 * m9 * m14 - m0 * m10 * m13 - m8 * m1 * m14 + m8 * m2 * m13 + m12 * m1 * m10
            - m12 * m2 * m9,
        -m0 * m5 * m14 + m0 * m6 * m13 + m4 * m1 * m14 - m4 * m2 * m13 - m12 * m1 * m6
            + m12 * m2 * m5,
        m0 * m5 * m10 - m0 * m6 * m9 - m4 * m1 * m10 + m4 * m2 * m9 + m8 * m1 * m6 - m8 * m2 * m5,
    ]
}

/// A facet's quadric: its plane, and per attribute the linear function through the corners'
/// values (from the adjugate of the corners and normal); also the facet's area.
fn facet_quadric(q: &mut [f64], p: [[f64; 3]; 3], values: [[f64; ATTRIBUTES]; 3]) -> f64 {
    let (e, f) = (sub(p[1], p[0]), sub(p[2], p[0]));
    let nx = e[1] * f[2] - f[1] * e[2];
    let ny = e[2] * f[0] - f[2] * e[0];
    let nz = f[1] * e[0] - f[0] * e[1];
    let len = sqrt_pos(nx * nx + ny * ny + nz * nz);
    let n = [nx / len, ny / len, nz / len];
    plane_quadric(q, n, p[0]);
    let m = [
        p[0][0], p[0][1], p[0][2], 1.0, p[1][0], p[1][1], p[1][2], 1.0, p[2][0], p[2][1], p[2][2],
        1.0, n[0], n[1], n[2], 0.0,
    ];
    let inv = adjugate(&m);
    for k in 0..ATTRIBUTES {
        let s = [values[0][k], values[1][k], values[2][k]];
        for r in 0..4 {
            let row = &inv[4 * r..4 * r + 4];
            q[10 + 4 * k + r] = -(s[0] * row[0] + s[1] * row[1] + s[2] * row[2] + row[3] * 0.0);
        }
    }
    len * 0.5
}

/// The quadric of a sharp half-edge: the plane through it perpendicular to its facet, weighted
/// by its squared length.
fn sharp_quadric(q: &mut [f64], p: [[f64; 3]; 3], at: [f64; 3], dir: [f64; 3]) -> f64 {
    let (e, f) = (sub(p[1], p[0]), sub(p[2], p[0]));
    let fy = e[2] * f[0] - f[2] * e[0];
    let fz = e[0] * f[1] - f[0] * e[1];
    let fx = e[1] * f[2] - f[1] * e[2];
    let mx = dir[1] * fz - fy * dir[2];
    let my = dir[2] * fx - fz * dir[0];
    let mz = dir[0] * fy - fx * dir[1];
    let len = sqrt_pos(mx * mx + my * my + mz * mz);
    plane_quadric(q, [mx / len, my / len, mz / len], at);
    (dir[0] * dir[0] + dir[1] * dir[1] + dir[2] * dir[2]) * SHARP_WEIGHT
}

/// The quadric's error at `p`, with the attributes' errors against `values` when given.
fn quadric_error(q: &[f64], p: [f64; 3], values: Option<[f64; ATTRIBUTES]>) -> f64 {
    let [x, y, z] = p;
    let mut e = q[0] * x * x;
    e += (q[1] + q[1]) * x * y;
    e += (q[2] + q[2]) * x * z;
    e += q[3] * y * y;
    e += (q[4] + q[4]) * y * z;
    e += q[5] * z * z;
    e += (q[6] + q[6]) * x;
    e += (q[7] + q[7]) * y;
    e += (q[8] + q[8]) * z;
    e += q[9];
    if let Some(values) = values {
        for (k, value) in values.iter().enumerate() {
            let g = &q[10 + 4 * k..14 + 4 * k];
            let r = g[2] * z + (g[0] * x + g[1] * y) + g[3] - value;
            e += r * r;
        }
    }
    e
}

/// The state used only while the collapses are chosen.
struct Costs {
    sharp: Vec<bool>,
    sharp_count: Vec<u32>,
    valence: Vec<u32>,
    quadrics: Vec<f64>,
    areas: Vec<f64>,
    /// The quadrics and weights around a vertex. Its slots keep what earlier vertices left in
    /// them where a sharp half-edge's quadric has no attribute part.
    scratch: Vec<f64>,
    best: Vec<u32>,
    heap: Vec<Key>,
    heap_vertex: Vec<u32>,
    heap_slot: Vec<u32>,
}

impl Simplification {
    /// Simplifies the triangles as far as the collapses allow. `uvs` holds UV0 per corner.
    pub fn new(
        positions: &[[f32; 3]],
        triangles: &[[u32; 3]],
        uvs: &[[[f32; 2]; 3]],
    ) -> Result<Self, String> {
        let doubles: Vec<[f64; 3]> = positions.iter().map(|p| p.map(f64::from)).collect();
        // The part is cleaned like the whole mesh; its facets are numbered as the cleanup
        // leaves them, and their UV0 is that of the triangles they come from.
        let cleaned = cleanup::clean(&doubles, triangles);
        let mut m = Self::build(cleaned.positions, &cleaned.triangles)?;
        let uvs: Vec<[[f32; 2]; 3]> = cleaned.sources.iter().map(|&s| uvs[s as usize]).collect();
        m.attributes(&uvs);
        m.simplify();
        Ok(m)
    }

    fn build(positions: Vec<[f64; 3]>, triangles: &[[u32; 3]]) -> Result<Self, String> {
        let n = positions.len();
        let mut m = Simplification {
            positions,
            vertex_edge: vec![NONE; n],
            edges: Vec::with_capacity(6 * triangles.len()),
            facet_edge: Vec::with_capacity(triangles.len()),
            facet_alive: vec![true; triangles.len()],
            live_facets: triangles.len(),
            attribute: Vec::new(),
            uvs: Vec::new(),
            collapses: Vec::new(),
            applied: 0,
        };
        let mut directed = std::collections::HashMap::new();
        for (f, t) in triangles.iter().enumerate() {
            let first = m.edges.len() as u32;
            m.facet_edge.push(first);
            for k in 0..3 {
                let (target, source) = (t[k], t[(k + 2) % 3]);
                let e = m.edges.len() as u32;
                m.edges.push(Edge {
                    twin: NONE,
                    next: first + (k as u32 + 1) % 3,
                    prev: first + (k as u32 + 2) % 3,
                    facet: f as u32,
                    vertex: target,
                });
                m.vertex_edge[target as usize] = e;
                if directed.insert((source, target), e).is_some() {
                    return Err("an edge is used twice the same way".into());
                }
                if let Some(&other) = directed.get(&(target, source)) {
                    m.edges[e as usize].twin = other;
                    m.edges[other as usize].twin = e;
                }
            }
        }
        let inner = m.edges.len();
        for i in 0..inner {
            if m.edges[i].twin != NONE {
                continue;
            }
            let source = m.edges[m.edges[i].prev as usize].vertex;
            let b = m.edges.len() as u32;
            m.edges.push(Edge {
                twin: i as u32,
                next: NONE,
                prev: NONE,
                facet: NONE,
                vertex: source,
            });
            m.edges[i].twin = b;
        }
        for i in inner..m.edges.len() {
            let twin = m.edges[i].twin;
            let (mut next, mut prev) = (twin, twin);
            loop {
                next = m.twin(m.edges[next as usize].prev);
                if m.edges[next as usize].facet == NONE {
                    break;
                }
            }
            while m.edges[prev as usize].facet != NONE {
                prev = m.twin(m.edges[prev as usize].next);
            }
            m.edges[i].next = next;
            m.edges[i].prev = prev;
        }
        if m.vertex_edge.contains(&NONE) {
            return Err("a vertex is not used by any triangle".into());
        }
        Ok(m)
    }

    fn twin(&self, e: u32) -> u32 {
        self.edges[e as usize].twin
    }

    fn next(&self, e: u32) -> u32 {
        self.edges[e as usize].next
    }

    fn prev(&self, e: u32) -> u32 {
        self.edges[e as usize].prev
    }

    fn facet(&self, e: u32) -> u32 {
        self.edges[e as usize].facet
    }

    fn vertex(&self, e: u32) -> u32 {
        self.edges[e as usize].vertex
    }

    fn position(&self, e: u32) -> [f64; 3] {
        self.positions[self.vertex(e) as usize]
    }

    /// The next half-edge pointing to the same vertex.
    fn around(&self, e: u32) -> u32 {
        self.twin(self.next(e))
    }

    /// The UV0 entries: corners at the same place whose UV0 differ by at most 1e-6 share one,
    /// the first such corner's. A border half-edge takes the one at its target in the facet
    /// across.
    fn attributes(&mut self, uvs: &[[[f32; 2]; 3]]) {
        let mut hash = cleanup::SpaceHash::new();
        let mut corner_uv: Vec<[f64; 2]> = Vec::new();
        self.attribute = vec![NONE; self.edges.len()];
        for f in 0..self.facet_edge.len() {
            let mut e = self.facet_edge[f];
            for k in 0..3 {
                let uv = uvs[f][k].map(f64::from);
                let p = self.position(e);
                let a = hash.insert(p, |i| {
                    let (dx, dy) = (uv[0] - corner_uv[i][0], uv[1] - corner_uv[i][1]);
                    let d2 = dy * dy + dx * dx;
                    !(0.0 < d2) || d2.sqrt() <= UV_WELD
                });
                if a as usize == corner_uv.len() {
                    corner_uv.push(uv);
                }
                self.attribute[e as usize] = a;
                e = self.next(e);
            }
        }
        for i in 0..self.edges.len() {
            if self.attribute[i] == NONE {
                let inner = self.prev(self.twin(i as u32));
                self.attribute[i] = self.attribute[inner as usize];
            }
        }
        self.uvs = corner_uv;
    }

    fn uv(&self, e: u32) -> [f64; 2] {
        self.uvs[self.attribute[e as usize] as usize]
    }

    /// A half-edge is sharp on the border and where UV0 changes across it.
    fn is_sharp(&self, e: u32) -> bool {
        let t = self.twin(e);
        if self.facet(e) == NONE || self.facet(t) == NONE {
            return true;
        }
        let distance = |a: [f64; 2], b: [f64; 2]| {
            let (dx, dy) = (a[0] - b[0], a[1] - b[1]);
            sqrt_pos(dy * dy + dx * dx)
        };
        let here = distance(self.uv(e), self.uv(self.prev(t)));
        let there = distance(self.uv(t), self.uv(self.prev(e)));
        UV_SEAM < here || UV_SEAM < there
    }

    fn touches_border(&self, v: u32) -> bool {
        let first = self.vertex_edge[v as usize];
        let mut e = first;
        loop {
            if self.facet(e) == NONE {
                return true;
            }
            e = self.around(e);
            if e == first {
                return false;
            }
        }
    }

    fn closes_tetrahedron(&self, e: u32) -> bool {
        let t = self.twin(e);
        let a = self.twin(self.next(self.next(e)));
        let b = self.next(t);
        if b == a {
            return false;
        }
        let c = self.twin(self.next(e));
        let d = self.next(c);
        if d == t {
            return false;
        }
        let g = self.next(a);
        g != c
            && self.next(self.twin(b)) == a
            && self.next(self.twin(d)) == t
            && self.next(self.twin(g)) == c
            && self.twin(self.next(b)) == d
            && self.twin(self.next(d)) == g
            && self.twin(self.next(g)) == b
            && self.facet(e) != NONE
            && self.facet(b) != NONE
            && self.facet(d) != NONE
            && self.facet(g) != NONE
    }

    fn facet_corners(&self, f: u32) -> [u32; 3] {
        let e = self.facet_edge[f as usize];
        [e, self.next(e), self.prev(e)]
    }

    fn facet_quadric(&self, b: &mut Costs, f: u32) {
        let corners = self.facet_corners(f);
        let p = corners.map(|e| self.position(e));
        let values = corners.map(|e| self.uv(e));
        let q = &mut b.quadrics[QUADRIC * f as usize..QUADRIC * (f as usize + 1)];
        b.areas[f as usize] = facet_quadric(q, p, values);
    }

    fn ring_count(&self, v: u32, mut f: impl FnMut(u32) -> u32) -> u32 {
        let first = self.vertex_edge[v as usize];
        let (mut e, mut count) = (first, 0);
        loop {
            count += f(e);
            e = self.around(e);
            if e == first {
                return count;
            }
        }
    }

    /// Whether moving the source of `e` onto its target keeps the mesh manifold, flips no facet
    /// and degenerates none, and keeps sharp lines in place.
    fn collapsible(&self, b: &Costs, e: u32) -> bool {
        let t = self.twin(e);
        let (u, v) = (self.vertex(e), self.vertex(t));
        // A vertex next to both ends must be the apex of one of the edge's facets.
        let first = self.vertex_edge[v as usize];
        let mut a = first;
        loop {
            let w = self.vertex(self.twin(a));
            let w_first = self.vertex_edge[w as usize];
            let mut c = w_first;
            loop {
                if self.vertex(self.twin(c)) == u
                    && (self.facet(e) == NONE || self.vertex(self.next(e)) != w)
                    && (self.facet(t) == NONE || self.vertex(self.next(t)) != w)
                {
                    return false;
                }
                c = self.around(c);
                if c == w_first {
                    break;
                }
            }
            a = self.around(a);
            if a == first {
                break;
            }
        }
        let inner_between_borders = self.touches_border(u)
            && self.touches_border(v)
            && self.facet(e) != NONE
            && self.facet(t) != NONE;
        if inner_between_borders || self.closes_tetrahedron(e) {
            return false;
        }
        let stop = self.prev(e);
        let mut h = self.around(t);
        loop {
            if self.facet(h) != NONE && !self.keeps_facet(h, u, v) {
                return false;
            }
            h = self.around(h);
            if h == stop {
                break;
            }
        }
        let sharp = |e: u32| b.sharp[e as usize];
        if self.facet(e) != NONE && sharp(self.next(e)) && sharp(self.prev(e)) {
            return false;
        }
        if self.facet(t) != NONE && sharp(self.next(t)) && sharp(self.prev(t)) {
            return false;
        }
        let (su, sv) = (b.sharp_count[u as usize], b.sharp_count[v as usize]);
        let s = sharp(e);
        if su != 0 && sv != 0 && !s {
            return false;
        }
        if 2 < su && 2 < sv && s {
            return false;
        }
        if sv == 1 && s {
            return false;
        }
        if s && su == 1 && sv != 2 {
            return false;
        }
        sv < 2 || s
    }

    /// Whether the facet of `h` (which points to `v`) keeps its orientation and does not
    /// degenerate when `v` moves to `u`.
    fn keeps_facet(&self, h: u32, u: u32, v: u32) -> bool {
        let (pv, pu) = (self.positions[v as usize], self.positions[u as usize]);
        let a = self.position(self.next(h));
        let c = self.position(self.twin(h));
        let normal = |o: [f64; 3]| {
            let (p, q) = (sub(a, o), sub(c, o));
            let n = [
                p[1] * q[2] - q[1] * p[2],
                p[2] * q[0] - q[2] * p[0],
                p[0] * q[1] - q[0] * p[1],
            ];
            let len = sqrt_pos(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
            [n[0] / len, n[1] / len, n[2] / len]
        };
        let (before, after) = (normal(pv), normal(pu));
        if before[0] * after[0] + before[1] * after[1] + before[2] * after[2] < 0.0 {
            return false;
        }
        let unit = |d: [f64; 3]| {
            let len = sqrt_pos(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
            [d[0] / len, d[1] / len, d[2] / len]
        };
        // A facet that collapses to a point gives no number here and passes.
        let (p, q) = (unit(sub(a, pu)), unit(sub(c, pu)));
        !((p[0] * q[0] + p[1] * q[1] + p[2] * q[2]).abs() > DEGENERATE_COS)
    }

    /// The cheapest collapse of `v` into a neighbour (kept in `b.best`) and its cost: the
    /// quadrics of the facets around `v` (of the plane through a sharp half-edge instead) at
    /// the neighbour, weighted by area (by squared length).
    fn collapse_cost(&self, b: &mut Costs, v: u32) -> f64 {
        b.best[v as usize] = NONE;
        let n = b.valence[v as usize] as usize;
        let weights = QUADRIC * n + 3;
        let needed = QUADRIC * n + n + 5;
        if b.scratch.len() < needed {
            b.scratch.resize(needed, 0.0);
        }
        let first = self.vertex_edge[v as usize];
        let (mut h, mut i) = (first, 0);
        loop {
            let slot = QUADRIC * i;
            if !b.sharp[h as usize] {
                let f = self.facet(h) as usize;
                b.scratch[slot..slot + QUADRIC]
                    .copy_from_slice(&b.quadrics[QUADRIC * f..QUADRIC * (f + 1)]);
                b.scratch[weights + i] = b.areas[f];
            } else {
                let f = if self.facet(h) != NONE {
                    self.facet(h)
                } else {
                    self.facet(self.twin(h))
                };
                let (pv, ps) = (self.position(h), self.position(self.twin(h)));
                let dir = if self.facet(h) == NONE {
                    sub(pv, ps)
                } else {
                    sub(ps, pv)
                };
                let p = self.facet_corners(f).map(|e| self.position(e));
                b.scratch[weights + i] =
                    sharp_quadric(&mut b.scratch[slot..slot + QUADRIC], p, pv, dir);
            }
            i += 1;
            h = self.around(h);
            if h == first {
                break;
            }
        }
        let start = self.twin(first);
        let mut c = start;
        let mut best = MAX_COST;
        loop {
            if self.collapsible(b, c) {
                let mut cost = 0.0;
                let target = self.position(c);
                let values = (!b.sharp[c as usize]).then(|| self.uv(c));
                for i in 0..n {
                    let q = &b.scratch[QUADRIC * i..QUADRIC * (i + 1)];
                    cost += quadric_error(q, target, values) * b.scratch[weights + i];
                }
                if cost < best {
                    b.best[v as usize] = c;
                    best = cost;
                }
            }
            c = self.twin(self.prev(c));
            if c == start {
                break;
            }
        }
        best
    }

    fn swap_keys(b: &mut Costs, i: usize, j: usize) {
        b.heap_slot[b.heap_vertex[i] as usize] = j as u32;
        b.heap_slot[b.heap_vertex[j] as usize] = i as u32;
        b.heap_vertex.swap(i, j);
        b.heap.swap(i, j);
    }

    fn sift_up(b: &mut Costs, mut i: usize) -> usize {
        while i != 0 {
            let parent = (i - 1) >> 1;
            if !less(b.heap[i], b.heap[parent]) {
                break;
            }
            Self::swap_keys(b, i, parent);
            i = parent;
        }
        i
    }

    fn sift_down(b: &mut Costs, mut i: usize) {
        loop {
            let (l, r) = (2 * i + 1, 2 * i + 2);
            let mut m = i;
            if l < b.heap.len() && less(b.heap[l], b.heap[i]) {
                m = l;
            }
            if r < b.heap.len() && less(b.heap[r], b.heap[m]) {
                m = r;
            }
            if m == i {
                return;
            }
            Self::swap_keys(b, i, m);
            i = m;
        }
    }

    fn update_key(&self, b: &mut Costs, v: u32) {
        let cost = self.collapse_cost(b, v);
        let slot = b.heap_slot[v as usize] as usize;
        b.heap[slot] = Key { vertex: v, cost };
        let slot = Self::sift_up(b, slot);
        Self::sift_down(b, slot);
    }

    fn simplify(&mut self) {
        let (nv, nf) = (self.positions.len(), self.facet_edge.len());
        let mut b = Costs {
            sharp: (0..self.edges.len() as u32)
                .map(|e| self.is_sharp(e))
                .collect(),
            sharp_count: vec![0; nv],
            valence: vec![0; nv],
            quadrics: vec![0.0; QUADRIC * nf],
            areas: vec![0.0; nf],
            scratch: Vec::new(),
            best: vec![NONE; nv],
            heap: Vec::with_capacity(nv),
            heap_vertex: Vec::with_capacity(nv),
            heap_slot: vec![NONE; nv],
        };
        for v in 0..nv as u32 {
            b.sharp_count[v as usize] = self.ring_count(v, |e| b.sharp[e as usize] as u32);
            b.valence[v as usize] = self.ring_count(v, |_| 1);
        }
        for f in 0..nf as u32 {
            self.facet_quadric(&mut b, f);
        }
        for v in 0..nv as u32 {
            let cost = self.collapse_cost(&mut b, v);
            b.heap_slot[v as usize] = b.heap.len() as u32;
            b.heap.push(Key { vertex: v, cost });
            b.heap_vertex.push(v);
            let last = b.heap.len() - 1;
            Self::sift_up(&mut b, last);
        }
        while self.collapse_next(&mut b) {}
        self.applied = self.collapses.len();
    }

    /// Applies the cheapest collapse; false (and nothing more is simplified) when the cheapest
    /// vertex cannot collapse.
    fn collapse_next(&mut self, b: &mut Costs) -> bool {
        let Some(top) = b.heap.first().copied() else {
            return false;
        };
        let e = b.best[top.vertex as usize];
        if e == NONE || !self.collapsible(b, e) || !(top.cost < MAX_COST) {
            return false;
        }
        let u = self.vertex(e);
        let collapse = self.record(e);
        self.collapse(collapse);
        self.refresh_sharpness(b, u);
        self.collapses.push(collapse);
        let last = b.heap.len() - 1;
        Self::swap_keys(b, 0, last);
        b.heap.pop();
        b.heap_vertex.pop();
        Self::sift_down(b, 0);
        self.refresh_around(b, u);
        true
    }

    fn record(&self, e: u32) -> Collapse {
        let t = self.twin(e);
        let side = |e: u32| {
            if self.facet(e) != NONE {
                self.twin(self.next(e))
            } else {
                NONE
            }
        };
        let mut sharp = NONE;
        if self.is_sharp(e) {
            let mut h = t;
            loop {
                h = self.around(h);
                if self.is_sharp(h) {
                    break;
                }
            }
            sharp = h;
        }
        Collapse {
            edge: e,
            sides: [side(e), side(t)],
            sharp,
        }
    }

    /// Points the half-edges from `start` up to (not including) `stop`, around their vertex, to
    /// `vertex` with UV0 entry `attribute`.
    fn retarget(&mut self, start: u32, stop: u32, vertex: u32, attribute: u32) {
        let mut h = start;
        while h != stop {
            self.attribute[h as usize] = attribute;
            self.edges[h as usize].vertex = vertex;
            h = self.around(h);
        }
    }

    fn collapse(&mut self, c: Collapse) {
        let (e, t) = (c.edge, self.twin(c.edge));
        let ep = self.prev(e);
        let u = self.vertex(e);
        let stop = if c.sharp != NONE { c.sharp } else { ep };
        let h = self.around(t);
        if h != stop {
            let a = self.attribute[self.prev(t) as usize];
            self.attribute[h as usize] = a;
            self.edges[h as usize].vertex = u;
            let next = self.around(h);
            self.retarget(next, stop, u, a);
        }
        if stop != ep {
            let a = self.attribute[e as usize];
            self.attribute[stop as usize] = a;
            self.edges[stop as usize].vertex = u;
            let next = self.around(stop);
            self.retarget(next, ep, u, a);
        }
        self.edges[ep as usize].vertex = u;
        self.drop_side(e);
        self.drop_side(t);
        let ue = self.vertex_edge[u as usize];
        if ue == e || ue == self.prev(t) {
            self.vertex_edge[u as usize] = if c.sides[0] != NONE {
                c.sides[0]
            } else {
                c.sides[1]
            };
        }
    }

    fn drop_side(&mut self, h: u32) {
        let (n, p) = (self.next(h), self.prev(h));
        self.edges[p as usize].next = n;
        self.edges[n as usize].prev = p;
        let f = self.facet(h);
        if f != NONE {
            let (a, b) = (self.twin(n), self.twin(p));
            self.edges[a as usize].twin = b;
            self.edges[b as usize].twin = a;
            let w = self.vertex(n);
            self.vertex_edge[w as usize] = self.twin(p);
            self.facet_alive[f as usize] = false;
            self.live_facets -= 1;
        }
    }

    fn split(&mut self, c: Collapse) {
        let (e, t) = (c.edge, self.twin(c.edge));
        self.restore_side(e, c.sides[0]);
        self.restore_side(t, c.sides[1]);
        let v = self.vertex(t);
        let ep = self.prev(e);
        let stop = if c.sharp != NONE { c.sharp } else { ep };
        let h = self.around(t);
        if h != stop {
            let a = self.attribute[t as usize];
            self.retarget(h, stop, v, a);
        }
        if stop != ep {
            let a = self.attribute[ep as usize];
            self.attribute[stop as usize] = a;
            self.edges[stop as usize].vertex = v;
            let next = self.around(stop);
            self.retarget(next, ep, v, a);
        }
        self.edges[ep as usize].vertex = v;
    }

    fn restore_side(&mut self, h: u32, side: u32) {
        let (n, p) = (self.next(h), self.prev(h));
        self.edges[n as usize].prev = h;
        self.edges[p as usize].next = h;
        let v = self.vertex(h);
        self.vertex_edge[v as usize] = h;
        if side == NONE {
            return;
        }
        let f = self.facet(h);
        self.facet_alive[f as usize] = true;
        self.live_facets += 1;
        let old = self.twin(side);
        self.edges[side as usize].twin = n;
        self.edges[n as usize].twin = side;
        self.edges[old as usize].twin = p;
        self.edges[p as usize].twin = old;
        self.facet_edge[f as usize] = h;
        self.edges[n as usize].facet = f;
        self.edges[p as usize].facet = f;
    }

    /// After collapsing into `u`: the sharpness of the half-edges at `u` and the valence and
    /// sharp count of `u` and its neighbours.
    fn refresh_sharpness(&self, b: &mut Costs, u: u32) {
        let first = self.vertex_edge[u as usize];
        let mut h = first;
        loop {
            let s = self.is_sharp(h);
            b.sharp[self.twin(h) as usize] = s;
            b.sharp[h as usize] = s;
            let w = self.vertex(self.twin(h));
            b.valence[w as usize] = self.ring_count(w, |_| 1);
            h = self.around(h);
            if h == first {
                break;
            }
        }
        b.valence[u as usize] = self.ring_count(u, |_| 1);
        let mut h = first;
        loop {
            let w = self.vertex(self.twin(h));
            b.sharp_count[w as usize] = self.ring_count(w, |e| b.sharp[e as usize] as u32);
            h = self.around(h);
            if h == first {
                break;
            }
        }
        b.sharp_count[u as usize] = self.ring_count(u, |e| b.sharp[e as usize] as u32);
    }

    /// After collapsing into `u`: the quadrics of its facets, then the costs of its neighbours
    /// and its own.
    fn refresh_around(&self, b: &mut Costs, u: u32) {
        let first = self.vertex_edge[u as usize];
        let mut h = first;
        loop {
            if self.facet(h) != NONE {
                self.facet_quadric(b, self.facet(h));
            }
            h = self.around(h);
            if h == first {
                break;
            }
        }
        let start = self.twin(first);
        let mut h = start;
        loop {
            self.update_key(b, self.vertex(h));
            h = self.twin(self.prev(h));
            if h == start {
                break;
            }
        }
        self.update_key(b, u);
    }

    /// The live facets, in increasing order.
    pub fn current(&self) -> Vec<u32> {
        (0..self.facet_alive.len() as u32)
            .filter(|&f| self.facet_alive[f as usize])
            .collect()
    }

    /// Undoes collapses until at least `count` facets are live (or applies more until no more
    /// than `count` are), then the live facets.
    pub fn with_facets(&mut self, count: usize) -> Vec<u32> {
        while count < self.live_facets && self.applied < self.collapses.len() {
            let c = self.collapses[self.applied];
            self.collapse(c);
            self.applied += 1;
        }
        while self.live_facets < count && self.applied != 0 {
            self.applied -= 1;
            let c = self.collapses[self.applied];
            self.split(c);
        }
        self.current()
    }

    /// How many collapses are applied now.
    pub fn applied(&self) -> usize {
        self.applied
    }

    /// Undoes up to `steps` collapses, giving each restored facet the chart of the neighbour
    /// whose normal is closer to its own.
    pub fn refine(&mut self, labels: &mut [i32], steps: usize) {
        for _ in 0..steps {
            if self.applied == 0 {
                return;
            }
            self.applied -= 1;
            let c = self.collapses[self.applied];
            let facets = [self.facet(c.edge), self.facet(self.twin(c.edge))];
            self.split(c);
            for f in facets {
                if f != NONE {
                    labels[f as usize] = self.chart_of(f, labels);
                }
            }
        }
    }

    fn facet_normal(&self, f: u32) -> [f64; 3] {
        let [e0, e1, e2] = self.facet_corners(f);
        let o = self.position(e0);
        let (a, b) = (sub(self.position(e1), o), sub(self.position(e2), o));
        let ny = a[2] * b[0] - b[2] * a[0];
        let nz = a[0] * b[1] - b[0] * a[1];
        let nx = a[1] * b[2] - b[1] * a[2];
        let len = sqrt_pos(nx * nx + ny * ny + nz * nz);
        [nx / len, ny / len, nz / len]
    }

    fn chart_of(&self, f: u32, labels: &[i32]) -> i32 {
        let e = self.facet_edge[f as usize];
        let n1 = self.facet(self.twin(self.next(e)));
        let n2 = self.facet(self.twin(self.prev(e)));
        if n1 == NONE {
            return if n2 == NONE { -1 } else { labels[n2 as usize] };
        }
        let l1 = labels[n1 as usize];
        if n2 == NONE || labels[n2 as usize] == l1 {
            return l1;
        }
        let n = self.facet_normal(f);
        let (a, b) = (self.facet_normal(n1), self.facet_normal(n2));
        let d1 = n[2] * a[2] + n[1] * a[1] + n[0] * a[0];
        let d2 = n[2] * b[2] + n[1] * b[1] + n[0] * b[0];
        // A degenerate neighbour (NaN normal) gives way to the second one.
        if d1 > d2 { l1 } else { labels[n2 as usize] }
    }
}
