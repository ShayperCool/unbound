//! Chart parametrization: the corner angles are corrected by a linearized angle system solved
//! with conjugate gradients, the chart is laid flat from those angles by least squares with two
//! pinned vertices, then scaled to its 3D area, rotated into its tightest box and checked for
//! overlaps and distortion (the resulting UV2 matches the Unity 6000.0.71f1 Editor's:
//! tests/projects/lightmapuvs).

use super::math;
use super::mesh::{Component, HalfEdgeMesh, NONE};
use std::collections::BTreeMap;
use std::f64::consts::{PI, TAU};

/// A column-compressed sparse matrix.
#[derive(Debug, Clone, Default)]
pub struct Sparse {
    pub rows: usize,
    pub cols: usize,
    pub values: Vec<f64>,
    pub indices: Vec<u32>,
    pub starts: Vec<u32>,
}

/// Entries by (column, row).
#[derive(Default)]
pub struct Builder(BTreeMap<(u32, u32), f64>);

impl Builder {
    pub fn set(&mut self, row: u32, col: u32, value: f64) {
        self.0.insert((col, row), value);
    }

    pub fn add(&mut self, row: u32, col: u32, value: f64) {
        let v = self.0.entry((col, row)).or_insert(0.0);
        *v = value + *v;
    }

    pub fn build(&self, rows: usize, cols: usize) -> Sparse {
        let mut starts = vec![0u32; cols + 1];
        for &(c, _) in self.0.keys() {
            starts[c as usize + 1] += 1;
        }
        for c in 0..cols {
            starts[c + 1] += starts[c];
        }
        Sparse {
            rows,
            cols,
            values: self.0.values().copied().collect(),
            indices: self.0.keys().map(|&(_, r)| r).collect(),
            starts,
        }
    }
}

impl Sparse {
    /// `out = self · x`.
    pub fn multiply(&self, x: &[f64], out: &mut [f64]) {
        out[..self.rows].iter_mut().for_each(|v| *v = 0.0);
        for c in 0..self.cols {
            for k in self.starts[c]..self.starts[c + 1] {
                let r = self.indices[k as usize] as usize;
                out[r] = self.values[k as usize] * x[c] + out[r];
            }
        }
    }

    pub fn transpose(&self) -> Sparse {
        let mut counts = vec![0u32; self.rows];
        for &r in &self.indices {
            counts[r as usize] += 1;
        }
        let mut starts = vec![0u32; self.rows + 1];
        for r in 0..self.rows {
            starts[r + 1] = counts[r] + starts[r];
        }
        let mut fill = vec![0u32; self.rows];
        let mut values = vec![0.0; self.values.len()];
        let mut indices = vec![0u32; self.values.len()];
        for c in 0..self.cols {
            for k in self.starts[c]..self.starts[c + 1] {
                let r = self.indices[k as usize] as usize;
                let at = (starts[r] + fill[r]) as usize;
                indices[at] = c as u32;
                values[at] = self.values[k as usize];
                fill[r] += 1;
            }
        }
        Sparse {
            rows: self.cols,
            cols: self.rows,
            values,
            indices,
            starts,
        }
    }

    /// `self · b`, accumulated column by column.
    pub fn product(&self, b: &Sparse) -> Sparse {
        let n = self.rows;
        let mut mark = vec![u32::MAX; n];
        let mut starts = vec![0u32; b.cols + 1];
        let mut indices = Vec::new();
        for j in 0..b.cols {
            starts[j] = indices.len() as u32;
            let first = indices.len();
            for k in b.starts[j]..b.starts[j + 1] {
                let col = b.indices[k as usize] as usize;
                for m in self.starts[col]..self.starts[col + 1] {
                    let row = self.indices[m as usize];
                    if mark[row as usize] != j as u32 {
                        mark[row as usize] = j as u32;
                        indices.push(row);
                    }
                }
            }
            indices[first..].sort_unstable();
        }
        starts[b.cols] = indices.len() as u32;
        let mut acc = vec![0.0; n];
        let mut values = vec![0.0; indices.len()];
        for j in 0..b.cols {
            for k in b.starts[j]..b.starts[j + 1] {
                let col = b.indices[k as usize] as usize;
                let bv = b.values[k as usize];
                for m in self.starts[col]..self.starts[col + 1] {
                    let row = self.indices[m as usize] as usize;
                    acc[row] = self.values[m as usize] * bv + acc[row];
                }
            }
            for k in starts[j]..starts[j + 1] {
                let row = indices[k as usize] as usize;
                values[k as usize] = acc[row];
                acc[row] = 0.0;
            }
        }
        Sparse {
            rows: n,
            cols: b.cols,
            values,
            indices,
            starts,
        }
    }

    /// The inverse of the diagonal.
    pub fn jacobi(&self) -> Sparse {
        let n = self.rows;
        let mut values = vec![0.0; n];
        for c in 0..self.cols {
            for k in self.starts[c]..self.starts[c + 1] {
                if self.indices[k as usize] as usize == c {
                    values[c] = 1.0 / self.values[k as usize];
                }
            }
        }
        Sparse {
            rows: n,
            cols: n,
            values,
            indices: (0..n as u32).collect(),
            starts: (0..=n as u32).collect(),
        }
    }
}

/// A dot product, summed in order.
fn dot(a: &[f64], b: &[f64]) -> f64 {
    let mut acc = 0.0;
    for (x, y) in a.iter().zip(b) {
        acc += x * y;
    }
    acc
}

/// Preconditioned conjugate gradients on `a·x = b`, from `x`.
pub fn conjugate_gradients(
    a: &Sparse,
    m: &Sparse,
    b: &[f64],
    x: &mut [f64],
    tolerance: f64,
    max_iterations: u32,
) {
    let n = b.len();
    let bb = dot(b, b);
    let mut r = vec![0.0; n];
    a.multiply(x, &mut r);
    for i in 0..n {
        r[i] = -(r[i] - b[i]);
    }
    let eps = tolerance * tolerance * bb;
    let mut s = vec![0.0; n];
    m.multiply(&r, &mut s);
    let mut d = s.clone();
    let mut q = vec![0.0; n];
    let mut iteration = 0;
    loop {
        if iteration >= max_iterations || dot(&r, &r) <= eps {
            return;
        }
        let rho = dot(&r, &s);
        a.multiply(&d, &mut q);
        let alpha = rho / dot(&d, &q);
        for i in 0..n {
            x[i] += d[i] * alpha;
        }
        for i in 0..n {
            q[i] = -(q[i] * alpha) + r[i];
        }
        r.copy_from_slice(&q);
        m.multiply(&r, &mut s);
        let beta = dot(&r, &s) / rho;
        for i in 0..n {
            d[i] = d[i] * beta + s[i];
        }
        iteration += 1;
    }
}

fn sub(a: [f64; 3], b: [f64; 3]) -> [f64; 3] {
    [a[0] - b[0], a[1] - b[1], a[2] - b[2]]
}

fn sqrt_pos(x: f64) -> f64 {
    if 0.0 < x { x.sqrt() } else { 0.0 }
}

/// The angle at the vertex a half-edge points to, in its facet.
pub fn corner_angle(map: &HalfEdgeMesh, edge: u32) -> f64 {
    let e = map.edge(edge);
    let a = sub(map.target(edge), map.target(e.prev));
    let b = sub(map.target(e.next), map.target(edge));
    let la = sqrt_pos(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]);
    let lb = sqrt_pos(b[0] * b[0] + b[1] * b[1] + b[2] * b[2]);
    PI - math::acos((a[1] * b[1] + a[0] * b[0] + a[2] * b[2]) / (la * lb))
}

/// A chart's distortion: mean relative angle error, its area-weighted mean, mean relative area
/// error, and the chart's fill of its box.
#[derive(Debug, Clone, Copy, Default)]
pub struct Metrics(pub [f32; 4]);

/// Mean and area-weighted mean of per-corner values (the weighted sum taken in pairs).
fn corner_means(values: &[f64], areas: &dyn Fn(usize) -> f64) -> (f32, f32) {
    let n = values.len();
    let (mut weighted, mut total) = (0.0, 0.0);
    let mut i = 0;
    while i + 1 < n {
        let (a0, a1) = (areas(i / 3), areas((i + 1) / 3));
        weighted = values[i + 1] * a1 + values[i] * a0 + weighted;
        total = total + a0 + a1;
        i += 2;
    }
    if i < n {
        let a = areas(i / 3);
        total += a;
        weighted += a * values[i];
    }
    let mut sum = 0.0;
    for &v in values {
        sum += v;
    }
    ((sum / n.max(1) as f64) as f32, (weighted / total) as f32)
}

/// The parametrized chart.
#[derive(Debug, Clone)]
pub struct Chart {
    pub min: [f64; 2],
    pub max: [f64; 2],
    pub center: [f64; 3],
    pub area: f64,
    pub uv_area: f64,
    pub component: Component,
    /// UV per facet corner, in the component's facet order.
    pub uvs: Vec<[f64; 2]>,
}

fn uv_area(uvs: &[[f64; 2]]) -> f64 {
    let mut area = 0.0;
    for t in uvs.chunks(3) {
        let (a, b, c) = (t[0], t[1], t[2]);
        area += ((c[1] - a[1]) * (b[0] - a[0]) - (c[0] - a[0]) * (b[1] - a[1])).abs() * 0.5;
    }
    area
}

impl Chart {
    fn new(parent: &HalfEdgeMesh, component: Component) -> Self {
        let mut area = 0.0;
        for &f in &component.facets {
            area += parent.infos[f as usize].area;
        }
        Chart {
            min: [1e32, 1e32],
            max: [-1e32, -1e32],
            center: [0.0; 3],
            area,
            uv_area: 0.0,
            uvs: vec![[0.0; 2]; component.facets.len() * 3],
            component,
        }
    }

    /// Rotates the chart into its tightest box (longer side along U), centered on its corners'
    /// mean.
    fn pack(&mut self) {
        self.min = [1e32, 1e32];
        self.max = [-1e32, -1e32];
        let (mut axes, extents) = super::pack::smallest_box(&self.uvs);
        if extents[0] <= extents[1] && 1e-6 < (extents[0] - extents[1]).abs() {
            axes.swap(0, 1);
        }
        if axes[0][0] <= 0.0 && 1e-6 < axes[0][0].abs() {
            axes[0] = [0.0 - axes[0][0], 0.0 - axes[0][1]];
        }
        if axes[1][1] <= 0.0 && 1e-6 < axes[1][1].abs() {
            axes[1] = [0.0 - axes[1][0], 0.0 - axes[1][1]];
        }
        let n = self.uvs.len();
        if n != 0 {
            let inv = 1.0 / (self.component.facets.len() as f64 * 3.0);
            let mut c = [0.0, 0.0];
            for uv in &mut self.uvs {
                *uv = [
                    axes[0][1] * uv[1] + axes[0][0] * uv[0],
                    axes[1][1] * uv[1] + axes[1][0] * uv[0],
                ];
                c[0] = uv[0] * inv + c[0];
                c[1] = uv[1] * inv + c[1];
            }
            for uv in &mut self.uvs {
                uv[0] -= c[0];
                uv[1] -= c[1];
                if uv[0] < self.min[0] {
                    self.min[0] = uv[0];
                }
                if uv[1] < self.min[1] {
                    self.min[1] = uv[1];
                }
                if self.max[0] <= uv[0] && uv[0] != self.max[0] {
                    self.max[0] = uv[0];
                }
                if self.max[1] <= uv[1] && uv[1] != self.max[1] {
                    self.max[1] = uv[1];
                }
            }
        }
        self.uv_area = uv_area(&self.uvs);
    }

    fn rescale(&mut self) {
        let scale = if 9.999999974752427e-7 <= self.uv_area {
            sqrt_pos(self.area / self.uv_area)
        } else {
            1.0
        };
        for uv in &mut self.uvs {
            *uv = [uv[0] * scale, uv[1] * scale];
        }
        self.min = [self.min[0] * scale, self.min[1] * scale];
        self.max = [self.max[0] * scale, self.max[1] * scale];
        self.uv_area = uv_area(&self.uvs);
    }

    pub fn metrics(&self, parent: &HalfEdgeMesh) -> Metrics {
        let f = self.component.facets.len();
        let mut angles3 = vec![0.0; f * 3];
        let mut angles2 = vec![0.0; f * 3];
        let mut areas3 = vec![0.0; f];
        let mut areas2 = vec![0.0; f];
        const TINY: f64 = 9.999999974752427e-7;
        for (i, &facet) in self.component.facets.iter().enumerate() {
            let [h0, h1, h2] = parent.facet_edges(facet);
            let (p0, p1, p2) = (parent.target(h0), parent.target(h1), parent.target(h2));
            let d01 = sub(p1, p0);
            let d12 = sub(p2, p1);
            let d20 = sub(p0, p2);
            let l01 = sqrt_pos(d01[0] * d01[0] + d01[1] * d01[1] + d01[2] * d01[2]);
            let l12 = sqrt_pos(d12[2] * d12[2] + d12[0] * d12[0] + d12[1] * d12[1]);
            let l20sq = d20[0] * d20[0] + d20[1] * d20[1] + d20[2] * d20[2];
            let l20 = sqrt_pos(l20sq);
            if l20sq <= 0.0 || l01 < TINY || l12 < TINY || l20 < TINY {
                angles3[3 * i..3 * i + 3].copy_from_slice(&[TINY; 3]);
                areas3[i] = TINY;
            } else {
                let ca = d20[2] * d01[1] - d20[1] * d01[2];
                let cb = d20[0] * d01[2] - d20[2] * d01[0];
                let cc = d20[1] * d01[0] - d20[0] * d01[1];
                areas3[i] = {
                    let l2 = ca * ca + cb * cb + cc * cc;
                    if 0.0 < l2 { l2.sqrt() * 0.5 } else { 0.0 }
                };
                let a = [d01[0] / l01, d01[1] / l01, d01[2] / l01];
                let c = [d20[0] / l20, d20[1] / l20, d20[2] / l20];
                let b = [d12[0] / l12, d12[1] / l12, d12[2] / l12];
                angles3[3 * i] = PI - math::acos(a[0] * c[0] + a[1] * c[1] + a[2] * c[2]);
                angles3[3 * i + 1] = PI - math::acos(a[2] * b[2] + a[1] * b[1] + a[0] * b[0]);
                angles3[3 * i + 2] = PI - math::acos(b[1] * c[1] + b[0] * c[0] + b[2] * c[2]);
            }
            let (q0, q1, q2) = (self.uvs[3 * i], self.uvs[3 * i + 1], self.uvs[3 * i + 2]);
            let e12 = [q2[0] - q1[0], q2[1] - q1[1]];
            let e01 = [q1[0] - q0[0], q1[1] - q0[1]];
            let e20 = [q0[0] - q2[0], q0[1] - q2[1]];
            let l01 = sqrt_pos(e01[1] * e01[1] + e01[0] * e01[0]);
            let l12 = sqrt_pos(e12[1] * e12[1] + e12[0] * e12[0]);
            let l20sq = e20[1] * e20[1] + e20[0] * e20[0];
            let l20 = sqrt_pos(l20sq);
            if l20sq <= 0.0 || l01 < TINY || l12 < TINY || l20 < TINY {
                angles2[3 * i..3 * i + 3].copy_from_slice(&[TINY; 3]);
                areas2[i] = TINY;
            } else {
                areas2[i] = (e20[1] * e01[0] - e01[1] * e20[0]).abs() * 0.5;
                let a = [e01[0] / l01, e01[1] / l01];
                let c = [e20[0] / l20, e20[1] / l20];
                angles2[3 * i] = PI - math::acos(a[1] * c[1] + a[0] * c[0]);
                angles2[3 * i + 1] = PI - math::acos(a[1] * (e12[1] / l12) + a[0] * (e12[0] / l12));
                angles2[3 * i + 2] = PI - math::acos((e12[1] / l12) * c[1] + (e12[0] / l12) * c[0]);
            }
        }
        let relative: Vec<f64> = (0..3 * f)
            .map(|c| {
                if 0.017453292519943295 < angles3[c] {
                    (angles2[c] - angles3[c]).abs() / angles3[c].abs()
                } else {
                    0.0
                }
            })
            .collect();
        let (angle_mean, angle_weighted) = corner_means(&relative, &|i| areas3[i]);
        let ratios: Vec<f64> = (0..f).map(|i| areas3[i] / areas2[i]).collect();
        let (mut ratio_sum, mut uv_total) = (0.0, 0.0);
        let mut i = 0;
        while i + 1 < f {
            ratio_sum = ratios[i + 1] + ratios[i] + ratio_sum;
            uv_total = uv_total + areas2[i] + areas2[i + 1];
            i += 2;
        }
        if i < f {
            uv_total += areas2[i];
            ratio_sum += ratios[i];
        }
        let mean = ratio_sum / f as f64;
        let mut area_error = 0.0;
        for &r in &ratios {
            area_error += (1.0 - r / mean).abs();
        }
        let box_area = (self.max[1] - self.min[1]) * (self.max[0] - self.min[0]);
        Metrics([
            angle_mean,
            angle_weighted,
            (area_error / f.max(1) as f64) as f32,
            if 9.999999974752427e-7 <= box_area {
                (uv_total / box_area) as f32
            } else {
                1.0
            },
        ])
    }
}

/// The parametrization of one component.
pub struct Parametrization<'a> {
    parent: &'a HalfEdgeMesh,
    component: &'a Component,
    map: HalfEdgeMesh,
    /// The parent vertex → this map's vertex.
    vertex_of: Vec<u32>,
    center: [f64; 3],
    corner_of: Vec<u32>,
    optimal: Vec<f64>,
    pub angles: Vec<f64>,
    system: Sparse,
    rhs: Vec<f64>,
    uvs: Vec<f64>,
}

impl<'a> Parametrization<'a> {
    pub fn new(parent: &'a HalfEdgeMesh, component: &'a Component) -> Result<Self, String> {
        // A vertex where the chart touches itself is split into one per fan; the corners take
        // the UV of the first.
        let (mut map, parents) = parent.sub_map(component)?;
        let mut vertex_of = vec![NONE; parent.vertices.len()];
        for (i, &p) in parents.iter().enumerate() {
            vertex_of[p as usize] = i as u32;
        }
        let mut center = [0.0; 3];
        let n = map.vertices.len();
        if n != 0 {
            let inv = 1.0 / n as f64;
            for v in &map.vertices {
                for k in 0..3 {
                    center[k] = v.position[k] * inv + center[k];
                }
            }
            for v in &mut map.vertices {
                for k in 0..3 {
                    v.position[k] -= center[k];
                }
            }
        }
        Ok(Self {
            parent,
            component,
            map,
            vertex_of,
            center,
            corner_of: Vec::new(),
            optimal: Vec::new(),
            angles: Vec::new(),
            system: Sparse::default(),
            rhs: Vec::new(),
            uvs: Vec::new(),
        })
    }

    fn around(&self, vertex: usize) -> Vec<u32> {
        let first = self.map.vertices[vertex].edge;
        let mut out = Vec::new();
        let mut e = first;
        loop {
            out.push(e);
            e = self.map.edge(self.map.edge(e).twin).prev;
            if e == first {
                return out;
            }
        }
    }

    pub fn angle_system(&mut self) {
        let map = &self.map;
        let f = map.facets.len();
        self.corner_of = vec![NONE; map.edges.len()];
        for t in 0..f as u32 {
            for (k, e) in map.facet_edges(t).into_iter().enumerate() {
                self.corner_of[e as usize] = 3 * t + k as u32;
            }
        }
        self.optimal = vec![0.0; 3 * f];
        let mut interior = 0u32;
        for v in 0..map.vertices.len() {
            if map.vertices[v].edge == NONE {
                continue;
            }
            let scale = if !map.on_border(v as u32) {
                let mut sum = 0.0;
                for e in self.around(v) {
                    sum += corner_angle(map, e);
                }
                interior += 1;
                TAU / sum
            } else {
                1.0
            };
            for e in self.around(v) {
                if map.edge(e).facet != NONE {
                    self.optimal[self.corner_of[e as usize] as usize] =
                        corner_angle(map, e) * scale;
                }
            }
        }
        let rows = f as u32 + interior * 2;
        self.rhs = vec![0.0; rows as usize];
        let mut builder = Builder::default();
        let mut row = 0u32;
        for v in 0..map.vertices.len() {
            if map.vertices[v].edge == NONE || map.on_border(v as u32) {
                continue;
            }
            self.rhs[row as usize] = TAU;
            for e in self.around(v) {
                let c = self.corner_of[e as usize];
                builder.set(row, c, self.optimal[c as usize]);
                self.rhs[row as usize] -= self.optimal[c as usize];
            }
            row += 1;
        }
        for t in 0..f as u32 {
            self.rhs[row as usize] = PI;
            for e in map.facet_edges(t) {
                let c = self.corner_of[e as usize];
                builder.set(row, c, self.optimal[c as usize]);
                self.rhs[row as usize] -= self.optimal[c as usize];
            }
            row += 1;
        }
        for v in 0..map.vertices.len() {
            if map.vertices[v].edge == NONE || map.on_border(v as u32) {
                continue;
            }
            self.rhs[row as usize] = 0.0;
            for e in self.around(v) {
                let next = self.corner_of[map.edge(e).next as usize];
                let prev = self.corner_of[map.edge(e).prev as usize];
                let a1 = self.optimal[next as usize];
                builder.set(row, next, (math::cos(a1) * a1) / math::sin(a1));
                let a2 = self.optimal[prev as usize];
                builder.set(row, prev, -(math::cos(a2) * a2) / math::sin(a2));
                let l2 = math::ln(math::sin(a2));
                let l1 = math::ln(math::sin(a1));
                self.rhs[row as usize] = (l2 - l1) + self.rhs[row as usize];
            }
            row += 1;
        }
        self.system = builder.build(rows as usize, 3 * f);
    }

    pub fn solve_angles(&mut self) {
        let at = self.system.transpose();
        let aat = self.system.product(&at);
        let m = aat.jacobi();
        let n = aat.rows;
        let mut y = vec![0.0; n];
        conjugate_gradients(
            &aat,
            &m,
            &self.rhs,
            &mut y,
            1e-8,
            (n as f64 * 5.0) as i64 as u32,
        );
        let mut e = vec![0.0; at.rows];
        at.multiply(&y, &mut e);
        self.angles = (0..e.len())
            .map(|i| (e[i] + 1.0) * self.optimal[i])
            .collect();
    }

    /// Mean and area-weighted mean relative change of the corner angles.
    pub fn angular_metrics(&self) -> (f32, f32) {
        let map = &self.map;
        let mut relative = vec![0.0; 3 * map.facets.len()];
        for t in 0..map.facets.len() as u32 {
            for (k, e) in map.facet_edges(t).into_iter().enumerate() {
                let c = 3 * t as usize + k;
                let original = corner_angle(map, e);
                relative[c] = if 0.017453292519943295 < original {
                    (self.angles[c] - original).abs() / original.abs()
                } else {
                    0.0
                };
            }
        }
        corner_means(&relative, &|i| map.infos[i].area)
    }

    /// The projection the least squares start from, and its two pinned vertices.
    fn projection(&mut self) -> [u32; 2] {
        let map = &self.map;
        let mut max = [-1e32f64; 3];
        let mut min = [1e32f64; 3];
        for v in &map.vertices {
            let p = v.position;
            for k in 0..3 {
                if max[k] < p[k] {
                    max[k] = p[k];
                }
                if !(min[k] <= p[k]) {
                    min[k] = p[k];
                }
            }
        }
        let extent = [max[0] - min[0], max[1] - min[1], max[2] - min[2]];
        let axis = |k: usize| {
            let mut a = [0.0; 3];
            a[k] = 1.0;
            a
        };
        let separated = |a: f64, b: f64| 1e-6 < (a - b).abs();
        let (big, other) = if extent[1] < extent[0] && separated(extent[0], extent[1]) {
            ((extent[0], 0), (extent[1], 1))
        } else {
            ((extent[1], 1), (extent[0], 0))
        };
        let (mut u, candidate) = if extent[2] < big.0 && separated(big.0, extent[2]) {
            (axis(big.1), (extent[2], 2))
        } else {
            (axis(2), big)
        };
        let v = if candidate.0 < other.0 && separated(other.0, candidate.0) {
            axis(other.1)
        } else {
            axis(candidate.1)
        };
        let [h0, h1, h2] = map.facet_edges(0);
        let (p0, p1, p2) = (map.target(h0), map.target(h1), map.target(h2));
        let e = sub(p1, p0);
        let g = sub(p2, p0);
        let det = (e[1] * g[2] - g[1] * e[2]) * (u[1] * v[2] - v[1] * u[2])
            + (g[0] * e[2] - g[2] * e[0]) * (v[0] * u[2] - v[2] * u[0])
            + (e[0] * g[1] - g[0] * e[1]) * (u[0] * v[1] - v[0] * u[1]);
        if det < 0.0 {
            u = [0.0 - u[0], 0.0 - u[1], 0.0 - u[2]];
        }
        self.uvs = vec![0.0; 2 * map.vertices.len()];
        let mut pins = [0u32, 0u32];
        let (mut lo, mut hi) = (1e32, -1e32);
        for (i, vert) in map.vertices.iter().enumerate() {
            let p = vert.position;
            let pu = p[2] * u[2] + p[1] * u[1] + p[0] * u[0];
            self.uvs[2 * i] = pu;
            self.uvs[2 * i + 1] = p[2] * v[2] + p[1] * v[1] + p[0] * v[0];
            if pu < lo {
                pins[0] = i as u32;
                lo = pu;
            }
            if hi < pu {
                pins[1] = i as u32;
                hi = pu;
            }
        }
        if pins[1] < pins[0] {
            pins.swap(0, 1);
        }
        pins
    }

    pub fn lay_flat(&mut self) {
        let pins = self.projection();
        let locked = [2 * pins[0], 2 * pins[0] + 1, 2 * pins[1], 2 * pins[1] + 1];
        let n = (2 * self.map.vertices.len()).saturating_sub(4);
        let mut rhs = vec![0.0; n];
        let mut builder = Builder::default();
        let free_vertex = |v: u32| {
            if pins[1] < v {
                v - 2
            } else {
                v - (pins[0] < v) as u32
            }
        };
        let free_var = |var: u32| {
            if locked[3] < var {
                var - 4
            } else if locked[2] < var {
                var - 3
            } else if locked[1] < var {
                var - 2
            } else {
                var - (locked[0] < var) as u32
            }
        };
        struct Row {
            free: Vec<(u32, f64)>,
            fixed: Vec<f64>,
            values: Vec<f64>,
        }
        let mut row = Row {
            free: Vec::new(),
            fixed: Vec::new(),
            values: Vec::new(),
        };
        let uvs = self.uvs.clone();
        let vertex = |row: &mut Row, v: u32, c: [f64; 2]| {
            if v == pins[0] || v == pins[1] {
                row.fixed.extend([c[0], c[1]]);
                row.values
                    .extend([uvs[2 * v as usize], uvs[2 * v as usize + 1]]);
            } else {
                let i = free_vertex(v);
                row.free.push((2 * i, c[0]));
                row.free.push((2 * i + 1, c[1]));
            }
        };
        let coeff = |row: &mut Row, var: u32, c: f64| {
            if locked.contains(&var) {
                row.fixed.push(c);
                row.values.push(uvs[var as usize]);
            } else {
                row.free.push((free_var(var), c));
            }
        };
        let end = |row: &mut Row, builder: &mut Builder, rhs: &mut [f64], w: f64| {
            for &(i, ci) in &row.free {
                for &(j, cj) in &row.free {
                    builder.add(i, j, ci * cj * (w * w));
                }
            }
            let (mut fixed, mut k) = (0.0, 0);
            while k + 1 < row.fixed.len() {
                fixed = row.fixed[k + 1] * row.values[k + 1] + row.fixed[k] * row.values[k] + fixed;
                k += 2;
            }
            if k < row.fixed.len() {
                fixed += row.fixed[k] * row.values[k];
            }
            for &(i, ci) in &row.free {
                rhs[i as usize] -= ci * (w * w * fixed);
            }
            row.free.clear();
            row.fixed.clear();
            row.values.clear();
        };
        for t in 0..self.map.facets.len() as u32 {
            let [h0, h1, h2] = self.map.facet_edges(t);
            let vs = [h0, h1, h2].map(|e| self.map.edge(e).target);
            let a = [0, 1, 2].map(|k| self.angles[3 * t as usize + k]);
            let s = a.map(math::sin);
            // The corner with the largest sine is C; A and B follow it.
            let c = if !(s[0] <= s[1] || s[0] <= s[2]) {
                0
            } else if s[0] < s[1] && s[2] < s[1] {
                1
            } else {
                2
            };
            let (ia, ib) = ((c + 1) % 3, (c + 2) % 3);
            let ratio = math::sin(a[ib]) / math::sin(a[c]);
            let cos_a = math::cos(a[ia]);
            let sin_a = math::sin(a[ia]) * ratio;
            let cos_a = cos_a * ratio;
            let w = sqrt_pos(self.map.infos[t as usize].area);
            vertex(&mut row, vs[ia], [1.0 - cos_a, sin_a]);
            vertex(&mut row, vs[ib], [cos_a, -0.0 - sin_a]);
            coeff(&mut row, 2 * vs[c], -1.0);
            end(&mut row, &mut builder, &mut rhs, w);
            vertex(&mut row, vs[ia], [-0.0 - sin_a, 1.0 - cos_a]);
            vertex(&mut row, vs[ib], [sin_a, cos_a]);
            coeff(&mut row, 2 * vs[c] + 1, -1.0);
            end(&mut row, &mut builder, &mut rhs, w);
        }
        let system = builder.build(n, n);
        let m = system.jacobi();
        let mut x = Vec::with_capacity(n);
        for v in 0..self.map.vertices.len() as u32 {
            if v != pins[0] && v != pins[1] {
                x.push(self.uvs[2 * v as usize]);
                x.push(self.uvs[2 * v as usize + 1]);
            }
        }
        conjugate_gradients(
            &system,
            &m,
            &rhs,
            &mut x,
            1e-8,
            (n as f64 * 5.0) as i64 as u32,
        );
        let mut k = 0;
        for v in 0..self.map.vertices.len() as u32 {
            if v != pins[0] && v != pins[1] {
                self.uvs[2 * v as usize] = x[k];
                self.uvs[2 * v as usize + 1] = x[k + 1];
                k += 2;
            }
        }
    }

    /// The chart: UVs gathered per facet corner, rotated, centered and scaled to its area.
    pub fn chart(&self) -> Chart {
        let mut chart = Chart::new(self.parent, self.component.clone());
        for (i, &f) in self.component.facets.iter().enumerate() {
            for (k, e) in self.parent.facet_edges(f).into_iter().enumerate() {
                let v = self.vertex_of[self.parent.edge(e).target as usize] as usize;
                chart.uvs[3 * i + k] = [self.uvs[2 * v], self.uvs[2 * v + 1]];
            }
        }
        chart.center = self.center;
        chart.uv_area = uv_area(&chart.uvs);
        chart.pack();
        chart.rescale();
        chart
    }
}

/// The positions (in the chart's component) of the triangles that overlap another when the chart
/// is rasterized at 512²; none when the chart is valid.
pub fn overlapping(chart: &Chart) -> Vec<usize> {
    const SIZE: i32 = 512;
    let mut grid = vec![0i32; (SIZE * SIZE) as usize];
    let n = chart.component.facets.len();
    let w = chart.max[0] - chart.min[0];
    let h = chart.max[1] - chart.min[1];
    let extent = if w <= h { h } else { w };
    let scale = (SIZE - 1) as f64 / extent;
    let cell = |uv: [f64; 2]| {
        [
            ((uv[0] - chart.min[0]) * scale) as u32 as i32,
            ((uv[1] - chart.min[1]) * scale) as u32 as i32,
        ]
    };
    let mut overlap = vec![false; n];
    for i in 0..n {
        let [xa, ya] = cell(chart.uvs[3 * i]);
        let [mut xb, mut yb] = cell(chart.uvs[3 * i + 1]);
        let [mut xc, mut yc] = cell(chart.uvs[3 * i + 2]);
        if (yc - ya).wrapping_mul(xb - xa) < (yb - ya).wrapping_mul(xc - xa) {
            std::mem::swap(&mut xb, &mut xc);
            std::mem::swap(&mut yb, &mut yc);
        }
        let min_x = xa.min(xb).min(xc);
        let max_x = xa.max(xb).max(xc);
        let min_y = ya.min(yb).min(yc);
        let max_y = ya.max(yb).max(yc);
        // Edges that do not own their pixels are biased by one.
        let owns = |y0: i32, y1: i32, x0: i32, x1: i32| if y0 == y1 { x1 < x0 } else { y1 <= y0 };
        let ab = owns(ya, yb, xa, xb);
        let bc = owns(yb, yc, xb, xc);
        let ca = owns(yc, ya, xc, xa);
        let mut w0 = ((min_y - ya).wrapping_mul(xb - xa))
            .wrapping_sub((min_x - xa).wrapping_mul(yb - ya))
            .wrapping_sub(!ab as i32);
        let mut w2 = ((min_y - yc).wrapping_mul(xa - xc))
            .wrapping_sub((min_x - xc).wrapping_mul(ya - yc))
            .wrapping_sub(!ca as i32);
        let mut w1 = ((min_y - yb).wrapping_mul(xc - xb))
            .wrapping_sub((min_x - xb).wrapping_mul(yc - yb))
            .wrapping_sub(!bc as i32);
        let mut row = (SIZE * min_y + min_x) as u32;
        for _ in min_y..=max_y {
            let (mut e0, mut e1, mut e2) = (w0, w1, w2);
            let mut at = row;
            for _ in min_x..=max_x {
                if (e2 | e0 | e1) >= 0 {
                    let previous = grid[at as usize];
                    if previous != 0 {
                        overlap[i] = true;
                        overlap[previous as usize - 1] = true;
                    }
                    grid[at as usize] = i as i32 + 1;
                }
                at = at.wrapping_add(1);
                e0 = e0.wrapping_add(ya - yb);
                e2 = e2.wrapping_add(yc - ya);
                e1 = e1.wrapping_sub(yc - yb);
            }
            row = row.wrapping_add(SIZE as u32);
            w0 = w0.wrapping_add(xb - xa);
            w2 = w2.wrapping_add(xa - xc);
            w1 = w1.wrapping_add(xc - xb);
        }
    }
    (0..n).filter(|&i| overlap[i]).collect()
}
