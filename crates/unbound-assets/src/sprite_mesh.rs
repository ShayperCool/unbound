//! Sprite outlines and Tight meshes made from a texture's alpha, as Unity's `SpriteMeshGenerator`
//! makes them (Unity 6000.0, measured against the Editor by `tests/projects/spriteimport`).
//!
//! The pipeline, per sprite rect:
//!
//! 1. **Mask.** Pixels whose alpha exceeds the tolerance, grown by `extrude` pixels (Manhattan
//!    distance, 1 to 32; 0 means 1), then a corner mask one larger each way: each set pixel sets
//!    its four corners.
//! 2. **Contours.** Moore-neighbour tracing of the corner mask from its first set corner (row by
//!    row, from the bottom left); each traced region is removed by an XOR scanline fill, so the
//!    next contour found is a hole (its first corner is clear in the original mask) or another
//!    island. Contours enclosing less than 4 square pixels are dropped. Outer contours run
//!    counter-clockwise, holes clockwise.
//! 3. **Fit** (`opt`). The staircase is cut where its direction stops being within 45 degrees of a
//!    run's first direction (or takes a third direction), each run is split recursively at its
//!    farthest point while that is more than half a pixel (in the larger axis) off the chord, and
//!    every vertex moves one pixel out along its normal.
//! 4. **Simplify** (`simplify` mode 1, which every sprite caller uses): repeatedly the cheapest
//!    vertex goes: a reflex vertex is cut off (cost: twice the area added), a convex one is removed
//!    after moving both neighbours out along their normals until the area is kept (cost: the area
//!    added); steps that would make the outline self-intersect are skipped, the first that would
//!    shrink the outline's bounds ends it, as does reaching `detail * count` vertices (at least 5).
//!    Outer outlines are then clipped to the rect.
//! 5. Vertices are rounded to whole pixels; the mesh is libtess2's nonzero-winding triangulation of
//!    the outlines (`native/libtess2`), with near-collinear vertices removed first.
//!
//! Every float operation follows the Editor's order: the outline depends on float rounding near
//! power-of-two coordinates.
/// One sprite's pixels: the alpha of `width` x `height` pixels, rows bottom-up.
pub struct Alpha<'a> {
    pub width: usize,
    pub height: usize,
    pub alpha: &'a [u8],
}

/// The traced and simplified outlines of a pixel region.
#[derive(Debug, Clone, Default)]
pub struct Shape {
    /// Width and height of the region in pixels.
    pub width: i32,
    pub height: i32,
    pub paths: Vec<Path>,
}

#[derive(Debug, Clone, Copy, Default, PartialEq)]
struct Vertex {
    p: [f32; 2],
    /// The unit normal (`dec`).
    n: [f32; 2],
    /// 1 at a reflex vertex, -1 at a convex one (`inf`).
    s: i32,
    /// The cost already spent on the vertex's neighbourhood.
    w: f32,
    /// The cost of removing the vertex (-1: not removable).
    cost: f32,
    /// How far a convex vertex's neighbours move out when it goes.
    t: f32,
}

impl Vertex {
    fn at(x: f32, y: f32) -> Self {
        Self {
            p: [x, y],
            ..Self::default()
        }
    }
}

const OUTER: i32 = b'+' as i32;
const HOLE: i32 = b'-' as i32;

/// One outline: its vertices in region pixels, outer counter-clockwise, holes clockwise.
#[derive(Debug, Clone)]
pub struct Path {
    v: Vec<Vertex>,
    w: i32,
    h: i32,
    sign: i32,
    /// The bounds of the fitted outline (before simplification), clamped to the region.
    bounds: [f32; 4],
    /// The fitted outline, which every simplification starts from.
    org: Vec<Vertex>,
    dirty: Vec<i32>,
}

/// The 8 neighbours, counter-clockwise from the left.
const DIR: [(i32, i32); 8] = [
    (-1, 0),
    (-1, -1),
    (0, -1),
    (1, -1),
    (1, 0),
    (1, 1),
    (0, 1),
    (-1, 1),
];
/// By `4 + 3 sign(dx) - sign(dy)` of a step back: where the neighbour search starts.
const NEXT: [i32; 9] = [0, 1, 2, 7, -1, 3, 6, 5, 4];

fn sign(v: f32) -> i32 {
    if v > 0.0 {
        1
    } else if v < 0.0 {
        -1
    } else {
        0
    }
}

fn direction(dx: f32, dy: f32) -> i32 {
    let col = if dx > 0.0 {
        7
    } else if dx < 0.0 {
        1
    } else {
        4
    };
    NEXT[(col - sign(dy)) as usize]
}

fn wrap(i: i32, n: i32) -> usize {
    i.rem_euclid(n) as usize
}

/// `Vector2f::epsilon`.
const EPSILON: f32 = 1e-5;

#[derive(Clone)]
struct Mask {
    w: i32,
    h: i32,
    bits: Vec<bool>,
}

impl Mask {
    fn get(&self, x: i32, y: i32) -> bool {
        x >= 0 && y >= 0 && x < self.w && y < self.h && self.bits[(y * self.w + x) as usize]
    }
    fn flip(&mut self, x: i32, y: i32) {
        if x >= 0 && y >= 0 && x < self.w && y < self.h {
            let i = (y * self.w + x) as usize;
            self.bits[i] = !self.bits[i];
        }
    }
    fn clear(&mut self, x: i32, y: i32) {
        if x >= 0 && y >= 0 && x < self.w && y < self.h {
            self.bits[(y * self.w + x) as usize] = false;
        }
    }
}

/// `mask::initialize`: the corner mask of the pixels above `tolerance`, grown by `extrude`.
fn corner_mask(image: &Alpha, tolerance: u8, extrude: u32) -> Mask {
    let (w, h) = (image.width as i32, image.height as i32);
    let mut pixels: Vec<bool> = image.alpha.iter().map(|&a| a > tolerance).collect();
    if extrude != 0 && w != 0 && h != 0 {
        // `mask::mdist`: the Manhattan distance to the nearest set pixel, two passes.
        let (wu, hu) = (w as usize, h as usize);
        let mut d = vec![0u32; wu * hu];
        for y in 0..hu {
            for x in 0..wu {
                let i = y * wu + x;
                d[i] = if pixels[i] {
                    0
                } else {
                    let mut v = (h + w) as u32;
                    if y != 0 && d[i - wu] + 1 < v {
                        v = d[i - wu] + 1;
                    }
                    if x != 0 && d[i - 1] + 1 < v {
                        v = d[i - 1] + 1;
                    }
                    v
                };
            }
        }
        for y in (0..hu).rev() {
            for x in (0..wu).rev() {
                let i = y * wu + x;
                if y + 1 < hu {
                    d[i] = d[i].min(d[i + wu] + 1);
                }
                if x + 1 < wu {
                    d[i] = d[i].min(d[i + 1] + 1);
                }
            }
        }
        for (p, &dist) in pixels.iter_mut().zip(&d) {
            if dist <= extrude {
                *p = true;
            }
        }
    }
    let mut mask = Mask {
        w: w + 1,
        h: h + 1,
        bits: vec![false; ((w + 1) * (h + 1)) as usize],
    };
    for y in 0..h {
        for x in 0..w {
            if pixels[(y * w + x) as usize] {
                for (cx, cy) in [(x, y), (x + 1, y + 1), (x, y + 1), (x + 1, y)] {
                    mask.bits[(cy * mask.w + cx) as usize] = true;
                }
            }
        }
    }
    mask
}

/// `SpriteMeshGenerator::contour`: the next contour of `cur`, removed from it. `None` when no
/// set corner is left.
fn contour(org: &Mask, cur: &mut Mask) -> Option<(Vec<Vertex>, i32, f32)> {
    loop {
        let start = cur.bits.iter().position(|&b| b)?;
        let (x0, y0) = ((start as i32) % cur.w, (start as i32) / cur.w);
        let kind = if org.get(x0, y0) { OUTER } else { HOLE };
        let (fx0, fy0) = (x0 as f32, y0 as f32);
        let mut verts = Vec::new();
        let mut area = 0.0f32;
        let (mut cx, mut cy, mut px, mut py) = (fx0, fy0, fx0 + -1.0, fy0);
        loop {
            verts.push(Vertex::at(cx, cy));
            let col = if px - cx > 0.0 {
                7
            } else if px - cx < 0.0 {
                1
            } else {
                4
            };
            let idx = col - sign(py - cy);
            if idx == 4 {
                break;
            }
            let first = NEXT[idx as usize];
            let found = (0..8).find_map(|i| {
                let (dx, dy) = DIR[((first + i) % 8) as usize];
                let (nx, ny) = (dx + cx as i32, dy + cy as i32);
                cur.get(nx, ny).then_some((nx, ny))
            });
            let Some((nx, ny)) = found else { break };
            let (fnx, fny) = (nx as f32, ny as f32);
            area += (cx * fny - cy * fnx) * 0.5;
            (px, py, cx, cy) = (cx, cy, fnx, fny);
            if cx == fx0 && cy == fy0 {
                break;
            }
        }
        invmask(cur, &verts);
        let size = area.abs();
        if size >= 4.0 {
            if (area < 0.0 && kind == OUTER) || (area > 0.0 && kind == HOLE) {
                verts.reverse();
            }
            return Some((verts, kind, size));
        }
    }
}

/// `SpriteMeshGenerator::invmask`: XOR-fills the contour's region (spans from the first vertex's
/// column, one per horizontal run), then clears the contour itself.
fn invmask(cur: &mut Mask, v: &[Vertex]) {
    let n = v.len();
    if n == 0 {
        return;
    }
    let x0 = v[0].p[0] as i32;
    let mut prev_y = v[n - 1].p[1];
    let mut i = 0usize;
    loop {
        let mut x = v[i].p[0];
        let mut y = v[i].p[1];
        let j = i + 1;
        let (more, next);
        if j < n {
            if v[j].p[1] != y {
                more = true;
                next = j;
            } else {
                let mut k = j;
                let mut ky = v[k].p[1];
                loop {
                    let code = direction(x - v[k].p[0], y - ky);
                    if (code == 5 && y < prev_y) || (code == 1 && prev_y < y) {
                        y = ky;
                        x = v[k].p[0];
                    }
                    if k == n - 1 {
                        more = false;
                        next = n;
                        break;
                    }
                    ky = v[k + 1].p[1];
                    k += 1;
                    if y != ky {
                        next = k;
                        more = next < n;
                        break;
                    }
                }
            }
        } else {
            more = false;
            next = j;
        }
        let (row, col) = (y as i32, x as i32);
        let (lo, hi) = if col <= x0 { (col, x0) } else { (x0, col) };
        for c in lo..hi {
            cur.flip(c, row);
        }
        if more && y != prev_y && v[next].p[1] == prev_y {
            for c in lo..hi {
                cur.flip(c, row);
            }
        }
        if next >= n {
            for p in v {
                cur.clear(p.p[0] as i32, p.p[1] as i32);
            }
            return;
        }
        prev_y = y;
        i = next;
    }
}

impl Path {
    /// `path::path`: fits the traced contour (`opt`) and records its bounds.
    fn new(traced: Vec<Vertex>, w: i32, h: i32, sign: i32, bias: f32) -> Self {
        let mut path = Path {
            v: traced,
            w,
            h,
            sign,
            bounds: [0.0; 4],
            org: Vec::new(),
            dirty: Vec::new(),
        };
        path.opt(bias);
        path.bounds = path.clamped_bounds();
        path.org = path.v.clone();
        path
    }

    /// A path of given vertices (a custom outline), not fitted (`SetSizeUnOptimized`).
    fn unoptimized(points: Vec<[f32; 2]>, w: i32, h: i32) -> Self {
        let mut path = Path {
            v: points.into_iter().map(|p| Vertex::at(p[0], p[1])).collect(),
            w,
            h,
            sign: 0,
            bounds: [0.0; 4],
            org: Vec::new(),
            dirty: Vec::new(),
        };
        path.bounds = path.clamped_bounds();
        path
    }

    fn clamped_bounds(&self) -> [f32; 4] {
        let (mut min, mut max) = ([f32::MAX; 2], [f32::MIN_POSITIVE; 2]);
        for v in &self.v {
            for a in 0..2 {
                if v.p[a] < min[a] {
                    min[a] = v.p[a];
                }
                if v.p[a] > max[a] {
                    max[a] = v.p[a];
                }
            }
        }
        let clamp = |v: f32, hi: i32| {
            if v < 0.0 {
                0.0
            } else if v <= hi as f32 {
                v
            } else {
                hi as f32
            }
        };
        [
            clamp(min[0], self.w),
            clamp(min[1], self.h),
            clamp(max[0], self.w),
            clamp(max[1], self.h),
        ]
    }

    /// The vertices, in region pixels.
    pub fn points(&self) -> Vec<[f32; 2]> {
        self.v.iter().map(|v| v.p).collect()
    }

    pub fn is_hole(&self) -> bool {
        self.sign == HOLE
    }

    fn opt(&mut self, bias: f32) {
        let n = self.v.len();
        if n < 3 {
            return;
        }
        // Corners: where the staircase leaves its run's directions.
        let mut corners = vec![0usize];
        let mut seen = [0i32; 8];
        let mut run: i32 = -1;
        for i in 0..n {
            let j = if i + 1 < n { i + 1 } else { 0 };
            let d = [
                self.v[i].p[0] - self.v[j].p[0],
                self.v[i].p[1] - self.v[j].p[1],
            ];
            let col = if d[0] > 0.0 {
                7
            } else if d[0] < 0.0 {
                1
            } else {
                4
            };
            let idx = col - sign(d[1]);
            if idx == 4 {
                continue;
            }
            let code = NEXT[idx as usize];
            seen[code as usize] = 1;
            if run < 0 {
                run = code;
                continue;
            }
            let near = code == (run + 7) % 8 || code == run || code == (run + 1) % 8;
            if near && seen.iter().sum::<i32>() < 3 {
                continue;
            }
            seen = [0; 8];
            corners.push(i);
            run = -1;
        }
        let mut out = Vec::new();
        let m = corners.len();
        for k in 0..m {
            self.fit(&mut out, corners[k] as i32, corners[(k + 1) % m] as i32);
        }
        let old = std::mem::take(&mut self.v);
        self.v = out.iter().map(|&i| old[i as usize]).collect();
        let n = self.v.len() as i32;
        for i in 0..n {
            self.dec(i);
        }
        for v in &mut self.v {
            v.p[0] = v.n[0] * bias + v.p[0];
            v.p[1] = v.n[1] * bias + v.p[1];
        }
    }

    fn fit(&self, out: &mut Vec<i32>, mut a: i32, b: i32) {
        let n = self.v.len() as i32;
        loop {
            let next = wrap(a + 1, n) as i32;
            if a == b || next == b {
                break;
            }
            let pa = self.v[a as usize].p;
            let pb = self.v[b as usize].p;
            let f9 = pa[1] - pb[1];
            let f11 = pb[0] - pa[0];
            let c = -(pa[0] * f9) - pa[1] * f11;
            let mut best = (pa[1] * f11 + pa[0] * f9 + c).abs();
            let mut idx = if best > -1.0 { a } else { -1 };
            if best <= -1.0 {
                best = -1.0;
            }
            let mut k = a;
            loop {
                k = wrap(k + 1, n) as i32;
                let p = self.v[k as usize].p;
                let d = (p[1] * f11 + p[0] * f9 + c).abs();
                if best < d {
                    idx = k;
                    best = d;
                }
                if k == b {
                    break;
                }
            }
            let tx = (f9.abs() as f64) * 0.5;
            let ty = (f11.abs() as f64) * 0.5;
            let threshold = if ty <= tx { tx } else { ty } as f32;
            if best <= threshold || idx < 0 {
                break;
            }
            self.fit(out, a, idx);
            a = idx;
        }
        out.push(b);
    }

    /// `path::dec`: the vertex normal, the normalized sum of its edges' outward normals.
    fn dec(&mut self, i: i32) {
        let n = self.v.len() as i32;
        if n < 3 {
            return;
        }
        let prev = self.v[wrap(i - 1, n)].p;
        let cur = self.v[wrap(i, n)].p;
        let next = self.v[wrap(i + 1, n)].p;
        let e1 = [prev[0] - cur[0], prev[1] - cur[1]];
        let l1 = (e1[0] * e1[0] + e1[1] * e1[1]).sqrt();
        let n1 = if l1 <= EPSILON {
            [0.0, 0.0]
        } else {
            [-e1[1] / l1, e1[0] / l1]
        };
        let e2 = [cur[0] - next[0], cur[1] - next[1]];
        let l2 = (e2[1] * e2[1] + e2[0] * e2[0]).sqrt();
        let n2 = if l2 <= EPSILON {
            [0.0, 0.0]
        } else {
            [-e2[1] / l2, e2[0] / l2]
        };
        let s = [n1[0] + n2[0], n1[1] + n2[1]];
        let l = (s[1] * s[1] + s[0] * s[0]).sqrt();
        let normal = if EPSILON < l {
            [s[0] / l, s[1] / l]
        } else {
            [0.0, 0.0]
        };
        self.v[wrap(i, n)].n = normal;
    }

    /// `path::inf`: 1 when the vertex is left of its neighbours' chord (reflex on an outer path).
    fn inf(&mut self, i: i32) {
        let n = self.v.len() as i32;
        if n < 3 {
            return;
        }
        let prev = self.v[wrap(i - 1, n)].p;
        let cur = self.v[wrap(i, n)].p;
        let next = self.v[wrap(i + 1, n)].p;
        let f9 = prev[1] - next[1];
        let f8 = next[0] - prev[0];
        let neg = if f9 != 0.0 { f9 < 0.0 } else { f8 < 0.0 };
        let l = cur[1] * f8 + cur[0] * f9 + (-(prev[0] * f9) - prev[1] * f8);
        let s = if l > 0.0 {
            1
        } else if l != 0.0 {
            -1
        } else if neg {
            1
        } else {
            -1
        };
        self.v[wrap(i, n)].s = s;
    }

    /// `path::cvx_cost`: removing a convex vertex after moving its neighbours out by `t`.
    fn cvx_cost(&mut self, k: usize) {
        let n = self.v.len() as i32;
        if n < 5 {
            return;
        }
        let ki = k as i32;
        let pv = self.v[wrap(ki - 1, n)];
        let nv = self.v[wrap(ki + 1, n)];
        let (pnx, pny, nnx, nny) = (pv.n[0], pv.n[1], nv.n[0], nv.n[1]);
        let dot = pny * nny + pnx * nnx;
        let reject = |v: &mut Vertex| {
            v.cost = -1.0;
            v.t = 0.0;
        };
        if 0.0 > dot || near_ulps(dot, 0.0) || near_ulps(dot, 1.0) {
            reject(&mut self.v[k]);
            return;
        }
        let (px, py) = (pv.p[0], pv.p[1]);
        let (nx, ny) = (nv.p[0], nv.p[1]);
        let (cx, cy) = (self.v[k].p[0], self.v[k].p[1]);
        let a = cx - px;
        let b = cy - py;
        let c = px - nx;
        let d = py - ny;
        let quad = pny * nnx - nny * pnx;
        let lin = c * b - d * a;
        let mid = (pny * c - d * pnx) + ((nnx - pnx) * b - (nny - pny) * a);
        let (r1, r2);
        if quad.abs() < EPSILON {
            if !(mid.abs() > EPSILON) {
                reject(&mut self.v[k]);
                return;
            }
            r1 = lin / mid;
            r2 = -1.0f32;
        } else {
            let disc = (-4.0 * quad) * lin + mid * mid;
            if 0.0 > disc {
                reject(&mut self.v[k]);
                return;
            }
            let inv = 0.5 / quad;
            let s = disc.sqrt();
            r1 = (mid + s) * inv;
            r2 = (mid - s) * inv;
        }
        let max = if r1 > r2 { r1 } else { r2 };
        let min = if r1 < r2 { r1 } else { r2 };
        let t = if 0.0 < r2 {
            if 0.0 < r1 { min } else { max }
        } else {
            max
        };
        if !(t > 0.0 || t == 0.0) {
            reject(&mut self.v[k]);
            return;
        }
        let p2 = self.v[wrap(ki - 2, n)].p;
        let n2 = self.v[wrap(ki + 2, n)].p;
        let ax = pnx * t + px;
        let ay = pny * t + py;
        let bx = nnx * t + nx;
        let by = nny * t + ny;
        let t1 = (py - p2[1]) * (ax - p2[0]) - (p2[1] - ay) * (p2[0] - px);
        let t2 = (py - ay) * (cx - ax) - (ay - cy) * (ax - px) + t1;
        let t3 = (ny - cy) * (bx - cx) - (cy - by) * (cx - nx) + t2;
        let t4 = (ny - by) * (n2[0] - bx) - (by - n2[1]) * (bx - nx) + t3;
        self.v[k].cost = t4;
        self.v[k].t = t;
    }

    /// `path::cve_cost`: cutting off a reflex vertex.
    fn cve_cost(&mut self, k: usize) {
        let n = self.v.len() as i32;
        if n < 3 {
            return;
        }
        let ki = k as i32;
        let p = self.v[wrap(ki - 1, n)].p;
        let q = self.v[wrap(ki + 1, n)].p;
        let c = self.v[k].p;
        let cost = (c[1] - p[1]) * (q[0] - p[0]) - (p[1] - q[1]) * (p[0] - c[0]);
        self.v[k].cost = if cost >= 0.0 { cost } else { -1.0 };
        self.v[k].t = 0.0;
    }

    /// `path::self_intersect`: whether a..b crosses an edge not sharing its end points.
    fn self_intersect(&self, a: [f32; 2], b: [f32; 2]) -> bool {
        let n = self.v.len();
        for i in 1..=n {
            let p = self.v[i - 1].p;
            let q = self.v[if i < n { i } else { 0 }].p;
            if p == a || q == b || b == p || a == q {
                continue;
            }
            let qx = q[0] - p[0];
            let qy = q[1] - p[1];
            let bx = b[0] - a[0];
            let by = b[1] - a[1];
            let denom = (qy * bx - qx * by) as f64;
            if EPSILON as f64 > denom.abs() {
                continue;
            }
            let ax = a[0] - p[0];
            let ay = a[1] - p[1];
            let ua = (bx * ay - by * ax) as f64 / denom;
            if ua > 1.0 || 0.0 > ua {
                continue;
            }
            let ub = (qx * ay - qy * ax) as f64 / denom;
            if 0.0 > ub || ub > 1.0 {
                continue;
            }
            return true;
        }
        false
    }

    /// `path::select`: removes the cheapest removable vertex; false when none can go.
    fn select(&mut self) -> bool {
        let n = self.v.len() as i32;
        if n <= 4 {
            return false;
        }
        for attempt in 0..=n {
            let mut best = f32::MAX;
            let mut k: i32 = -1;
            for (i, v) in self.v.iter().enumerate() {
                if 0.0 <= v.cost && v.cost + v.w < best {
                    best = v.cost + v.w;
                    k = i as i32;
                }
            }
            if k < 0 {
                return false;
            }
            let ku = k as usize;
            let pi = wrap(k - 1, n);
            let ni = wrap(k + 1, n);
            let cost = self.v[ku].cost;
            if self.v[ku].s < 1 {
                let t = self.v[ku].t;
                let p2 = self.v[wrap(k - 2, n)].p;
                let n2 = self.v[wrap(k + 2, n)].p;
                let (pv, nv) = (self.v[pi], self.v[ni]);
                let a = [pv.n[0] * t + pv.p[0], pv.n[1] * t + pv.p[1]];
                let b = [nv.n[0] * t + nv.p[0], t * nv.n[1] + nv.p[1]];
                if !self.self_intersect(p2, a)
                    && !self.self_intersect(a, b)
                    && !self.self_intersect(b, n2)
                {
                    self.v[pi].p = a;
                    self.v[ni].p = b;
                    self.v[pi].w += cost;
                    self.v[ni].w = cost + self.v[ni].w;
                    self.dirty.push(wrap(k - 2, n) as i32);
                    self.dirty.push(pi as i32);
                    self.dirty.push(ni as i32);
                    self.dirty.push(wrap(k + 2, n) as i32);
                    return self.remove_if_bounds_kept(ku);
                }
            } else if !self.self_intersect(self.v[pi].p, self.v[ni].p) {
                self.v[pi].w += cost;
                self.v[ni].w = cost + self.v[ni].w;
                self.dirty.push(wrap(k, n) as i32);
                self.dirty.push(ni as i32);
                return self.remove_if_bounds_kept(ku);
            }
            self.v[ku].cost = -1.0;
            if attempt == n {
                break;
            }
        }
        false
    }

    fn remove_if_bounds_kept(&mut self, k: usize) -> bool {
        let (mut min, mut max) = ([f32::INFINITY; 2], [f32::NEG_INFINITY; 2]);
        for (i, v) in self.v.iter().enumerate() {
            if i == k {
                continue;
            }
            for a in 0..2 {
                if !(min[a] <= v.p[a]) {
                    min[a] = v.p[a];
                }
                if !(v.p[a] <= max[a]) {
                    max[a] = v.p[a];
                }
            }
        }
        let b = self.bounds;
        if min[0] <= b[0] && min[1] <= b[1] && b[2] <= max[0] && b[3] <= max[1] {
            self.v.remove(k);
            for d in &mut self.dirty {
                if *d > k as i32 {
                    *d -= 1;
                }
            }
            return true;
        }
        false
    }

    /// `path::simplify` (mode 1): from the fitted outline, toward `(1 - quality) * count`
    /// vertices, then clipped to the region.
    fn simplify(&mut self, quality: f32) {
        self.v = self.org.clone();
        let n = self.v.len() as i32;
        if n > 4 {
            let keep = if quality < 0.0 {
                1.0
            } else {
                1.0 - if quality <= 1.0 { quality } else { 1.0 }
            };
            let target = (keep * n as f32) as i32;
            self.simplify_cost(target.max(5));
        }
        if self.sign == OUTER {
            for edge in [Edge::MinX, Edge::MaxX, Edge::MinY, Edge::MaxY] {
                self.clip(edge);
            }
        }
    }

    fn simplify_cost(&mut self, target: i32) {
        self.dirty = (0..self.v.len() as i32).collect();
        loop {
            let n = self.v.len() as i32;
            let dirty = std::mem::take(&mut self.dirty);
            for &d in &dirty {
                self.dec(d);
                self.inf(d);
            }
            for &d in &dirty {
                let k = wrap(d, n);
                if self.v[k].s < 1 {
                    self.cvx_cost(k);
                } else {
                    self.cve_cost(k);
                }
            }
            let removed = self.select();
            if !(target < n && removed) {
                break;
            }
        }
    }

    /// `path::clip_edge`: Sutherland-Hodgman against one side of the bounds.
    fn clip(&mut self, edge: Edge) {
        let n = self.v.len();
        let [min_x, min_y, max_x, max_y] = self.bounds;
        let mut out = Vec::new();
        let inside = |p: [f32; 2]| match edge {
            Edge::MinX => p[0] >= min_x,
            Edge::MaxX => max_x >= p[0],
            Edge::MinY => p[1] >= min_y,
            Edge::MaxY => max_y >= p[1],
        };
        // Where a..b crosses the edge, from the slope through `from` (as the Editor does).
        let cross = |from: [f32; 2], slope: f32| -> [f32; 2] {
            let m = slope as f64;
            let c = from[1] as f64 - from[0] as f64 * m;
            match edge {
                Edge::MinX | Edge::MaxX => {
                    let x = if edge == Edge::MinX { min_x } else { max_x } as f64;
                    [x as f32, (c + m * x) as f32]
                }
                Edge::MinY | Edge::MaxY => {
                    let y = if edge == Edge::MinY { min_y } else { max_y } as f64;
                    let x = if m.is_finite() {
                        (y - c) / m
                    } else {
                        from[0] as f64
                    };
                    [x as f32, y as f32]
                }
            }
        };
        for i in 1..=n {
            let a = self.v[i - 1].p;
            let b = self.v[if i < n { i } else { 0 }].p;
            match (inside(a), inside(b)) {
                (true, true) => out.push(Vertex::at(b[0], b[1])),
                (true, false) => {
                    let p = cross(a, (b[1] - a[1]) / (b[0] - a[0]));
                    out.push(Vertex::at(p[0], p[1]));
                }
                (false, true) => {
                    let p = cross(b, (a[1] - b[1]) / (a[0] - b[0]));
                    out.push(Vertex::at(p[0], p[1]));
                    out.push(Vertex::at(b[0], b[1]));
                }
                (false, false) => {}
            }
        }
        self.v = out;
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Edge {
    MinX,
    MaxX,
    MinY,
    MaxY,
}

/// `CompareApproximately` by units in the last place (at most 10), as the Editor compares a
/// normals' dot product with 0 and 1.
fn near_ulps(v: f32, to: f32) -> bool {
    let bits = v.to_bits() as i32;
    if to == 0.0 {
        if bits < 0 { v == 0.0 } else { bits < 11 }
    } else {
        // Only reached for v >= 0 with more than 10 ulps from 0.
        if bits < 0 {
            v == to
        } else {
            let d = bits.wrapping_sub(to.to_bits() as i32);
            (if d < 0 { -d } else { d }) <= 10
        }
    }
}

/// `SpriteMeshGenerator::MakeShape`: traces, fits and simplifies the outlines of `image`.
/// `quality` is `1 - detail` (negative: chosen from the outlines' size), `extrude` 1 to 32.
pub fn make_shape(image: &Alpha, quality: f32, tolerance: u8, holes: bool, extrude: u32) -> Shape {
    let (w, h) = (image.width as i32, image.height as i32);
    let org = corner_mask(image, tolerance, extrude);
    let mut cur = org.clone();
    let min_area = (h * w) as f32 * 0.25;
    let mut area_sum = 0.0f32;
    let mut paths = Vec::new();
    while let Some((verts, sign, area)) = contour(&org, &mut cur) {
        if (sign != HOLE || holes) && (quality >= 0.0 || sign != HOLE || min_area <= area) {
            let signed = if sign == OUTER { area } else { -area };
            area_sum += signed;
            paths.push(Path::new(verts, w, h, sign, 1.0));
        }
    }
    let mut q = quality;
    if quality < 0.0 {
        let count: i32 = paths
            .iter()
            .map(|p| {
                if p.sign == HOLE {
                    2
                } else {
                    p.v.len() as i32 - 2
                }
            })
            .sum();
        let f = (area_sum * f32::from_bits(0xb9d5_5555)) / count as f32 + 1.0;
        q = if f < 0.0 {
            0.0
        } else if f <= 1.0 {
            f
        } else {
            1.0
        };
    }
    for p in &mut paths {
        p.simplify(q);
        for v in &mut p.v {
            v.p = [(v.p[0] + 0.5).floor(), (v.p[1] + 0.5).floor()];
        }
    }
    Shape {
        width: w,
        height: h,
        paths,
    }
}

impl Shape {
    /// A shape of given outlines (a custom outline, in region pixels), not simplified.
    pub fn from_outline(outline: &[Vec<[f32; 2]>], width: i32, height: i32) -> Self {
        Shape {
            width,
            height,
            paths: outline
                .iter()
                .map(|p| Path::unoptimized(p.clone(), width, height))
                .collect(),
        }
    }

    /// `GetOptimizedPath` of every path: its vertices without the near-collinear ones.
    pub fn outlines(&self) -> Vec<Vec<[f32; 2]>> {
        self.paths
            .iter()
            .map(|p| {
                let mut v = p.points();
                if !v.is_empty() {
                    remove_collinear(&mut v);
                }
                v
            })
            .collect()
    }

    /// `FindBounds`: the union of the fitted outlines' bounds (x, y, width, height), in region
    /// pixels; `None` without outlines.
    pub fn bounds(&self) -> Option<[f32; 4]> {
        let first = self.paths.first()?;
        let mut b = first.bounds;
        for p in &self.paths[1..] {
            for a in 0..2 {
                if !(b[a] <= p.bounds[a]) {
                    b[a] = p.bounds[a];
                }
                if !(p.bounds[a + 2] <= b[a + 2]) {
                    b[a + 2] = p.bounds[a + 2];
                }
            }
        }
        Some([b[0], b[1], b[2] - b[0], b[3] - b[1]])
    }

    /// `SpriteMeshGenerator::Decompose`: libtess2's triangles of the outlines (nonzero winding),
    /// vertices shared within `Vector2f::epsilon`, rounded to whole pixels when `round`.
    pub fn triangulate(&self, round: bool) -> (Vec<[f32; 2]>, Vec<u16>) {
        let mut contours = Vec::new();
        for p in &self.paths {
            let mut v = p.points();
            if !v.is_empty() {
                remove_collinear(&mut v);
                if !v.is_empty() {
                    contours.push(v);
                }
            }
        }
        if contours.is_empty() {
            return (Vec::new(), Vec::new());
        }
        let (tv, te) = tess::triangulate(&contours);
        let mut vertices: Vec<[f32; 2]> = Vec::new();
        let mut indices = Vec::new();
        for &e in &te {
            let p = tv[e as usize];
            let found = vertices
                .iter()
                .position(|v| (v[0] - p[0]).abs() <= EPSILON && (v[1] - p[1]).abs() <= EPSILON);
            indices.push(match found {
                Some(i) => i as u16,
                None => {
                    vertices.push(p);
                    (vertices.len() - 1) as u16
                }
            });
        }
        if round {
            for v in &mut vertices {
                *v = [(v[0] + 0.5).floor(), (v[1] + 0.5).floor()];
            }
        }
        (vertices, indices)
    }
}

/// A sprite's mesh in Unity's terms: vertices in units from the pivot, UVs in the texture.
#[derive(Debug, Clone, PartialEq, Default, serde::Serialize, serde::Deserialize)]
pub struct SpriteMesh {
    pub vertices: Vec<[f32; 2]>,
    pub uv: Vec<[f32; 2]>,
    pub triangles: Vec<u16>,
}

/// What `Sprite::Initialize` makes of a sprite's settings and its texture.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct SpriteGeometry {
    /// The Tight mesh; `None` for a quad (Full Rect, a rect under 32 pixels, nothing traced).
    pub mesh: Option<SpriteMesh>,
    /// `Sprite.textureRect` (x, y, width, height) in texture pixels.
    pub texture_rect: [f32; 4],
    /// `Sprite.GetPhysicsShape`: in units from the pivot.
    pub physics: Vec<Vec<[f32; 2]>>,
}

/// The texture as `GetPixels32` gives it: RGBA8, rows bottom-up (`None`: not traceable, a
/// floating-point texture).
pub struct Texture<'a> {
    pub width: i32,
    pub height: i32,
    pub rgba: Option<&'a [u8]>,
}

/// A sprite as the importer hands it to `Sprite::Initialize`.
pub struct SpriteSource<'a> {
    /// In texture pixels, origin bottom-left.
    pub rect: [f32; 4],
    /// Normalized within `rect`.
    pub pivot: [f32; 2],
    pub pixels_per_unit: f32,
    pub extrude: u32,
    /// `spriteMeshType` Tight.
    pub tight: bool,
    /// `spriteTessellationDetail` (-1: chosen from the outlines' size).
    pub detail: f32,
    /// The custom outline (Sprite Editor), in pixels from the rect's centre.
    pub outline: &'a [Vec<[f32; 2]>],
    /// The custom physics shape, in pixels from the rect's centre.
    pub physics_shape: &'a [Vec<[f32; 2]>],
    /// `spriteGenerateFallbackPhysicsShape`.
    pub generate_physics_shape: bool,
}

/// The pixels `GenerateSpriteOutline` works on: the rect widened to whole pixels.
struct Region {
    x0: i32,
    y0: i32,
    w: i32,
    h: i32,
    /// Region pixels to pixels from the pivot: subtract this.
    offset: [f32; 2],
}

impl Region {
    fn new(texture: &Texture, rect: [f32; 4], pivot_from_centre: [f32; 2]) -> Option<Self> {
        let [x, y, w, h] = rect;
        let x0 = x.floor() as i32;
        let y0 = y.floor() as i32;
        let mut x1 = (w + x).ceil() as i32;
        let mut y1 = (h + y).ceil() as i32;
        if texture.height + 1 < y1 || (y0 | x0) < 0 || texture.width + 1 < x1 || x1 < x0 || y1 < y0
        {
            return None;
        }
        y1 = y1.min(texture.height);
        x1 = x1.min(texture.width);
        let (rw, rh) = (x1 - x0, y1 - y0);
        Some(Region {
            x0,
            y0,
            w: rw,
            h: rh,
            offset: [
                (pivot_from_centre[0] + rw as f32 * 0.5) - (x - x0 as f32),
                (pivot_from_centre[1] + rh as f32 * 0.5) - (y - y0 as f32),
            ],
        })
    }

    fn alpha(&self, rgba: &[u8], texture_width: i32) -> Vec<u8> {
        let mut alpha = Vec::with_capacity((self.w * self.h) as usize);
        for y in 0..self.h {
            for x in 0..self.w {
                let i = ((y + self.y0) * texture_width + x + self.x0) as usize;
                alpha.push(rgba[i * 4 + 3]);
            }
        }
        alpha
    }

    /// `GenerateSpriteOutline` from the texture: `quality` as `MakeShape` takes it.
    fn trace(&self, texture: &Texture, detail: f32, tolerance: u8, extrude: u32) -> Option<Shape> {
        let quality = if detail >= 0.0 {
            1.0 - if detail <= 1.0 { detail } else { 1.0 }
        } else {
            detail
        };
        let extrude = if extrude == 0 { 1 } else { extrude.min(32) };
        let alpha = self.alpha(texture.rgba?, texture.width);
        let image = Alpha {
            width: self.w as usize,
            height: self.h as usize,
            alpha: &alpha,
        };
        Some(make_shape(&image, quality, tolerance, true, extrude))
    }
}

/// `Sprite::Initialize`: the Tight mesh, texture rect and physics shape of a sprite. `round` is
/// false for Polygon-mode sprites (their outline vertices are kept as authored).
pub fn sprite_geometry(texture: &Texture, s: &SpriteSource, round: bool) -> SpriteGeometry {
    let [rx, ry, rw, rh] = s.rect;
    let pivot_from_centre = [
        (s.pivot[0] * rw + rx) - (rw * 0.5 + rx),
        (s.pivot[1] * rh + ry) - (rh * 0.5 + ry),
    ];
    let ppu = s.pixels_per_unit;
    let mut out = SpriteGeometry {
        mesh: None,
        texture_rect: s.rect,
        physics: Vec::new(),
    };
    let region = Region::new(texture, s.rect, pivot_from_centre);
    let tight = if s.outline.is_empty() {
        s.tight && 32.0 <= rw && 32.0 <= rh
    } else {
        s.tight
    };
    if tight {
        let mut bounds = [0.0f32; 4];
        if let Some(region) = &region {
            let shape = if s.outline.is_empty() {
                region.trace(texture, s.detail, 0, s.extrude)
            } else {
                let (hw, hh) = (region.w as f32 * 0.5, region.h as f32 * 0.5);
                let outline: Vec<Vec<[f32; 2]>> = s
                    .outline
                    .iter()
                    .map(|p| p.iter().map(|v| [v[0] + hw, v[1] + hh]).collect())
                    .collect();
                Some(Shape::from_outline(&outline, region.w, region.h))
            };
            let shape = shape.unwrap_or_else(|| Shape {
                width: region.w,
                height: region.h,
                paths: Vec::new(),
            });
            let (vertices, mut triangles) = shape.triangulate(round);
            bounds = shape
                .bounds()
                .unwrap_or([0.0, 0.0, region.w as f32, region.h as f32]);
            if !triangles.is_empty() {
                triangles.reverse();
                let (tw, th) = (texture.width as f32, texture.height as f32);
                let [ox, oy] = region.offset;
                // `RecalculateUVs`: from the unit vertices, with the transform the outline
                // left (`ppu`, rect origin + offset).
                let units: Vec<[f32; 2]> = vertices
                    .iter()
                    .map(|v| [(v[0] - ox) / ppu, (v[1] - oy) / ppu])
                    .collect();
                let (ux, uy) = (rx + ox, ry + oy);
                out.mesh = Some(SpriteMesh {
                    uv: units
                        .iter()
                        .map(|v| [(v[0] * ppu + ux) / tw, (v[1] * ppu + uy) / th])
                        .collect(),
                    vertices: units,
                    triangles,
                });
            }
        }
        out.texture_rect = [bounds[0] + rx, bounds[1] + ry, bounds[2], bounds[3]];
    }
    if !s.physics_shape.is_empty() && ppu > 0.0 {
        let scale = 1.0 / ppu;
        out.physics = s
            .physics_shape
            .iter()
            .map(|p| {
                p.iter()
                    .map(|v| {
                        [
                            (v[0] - pivot_from_centre[0]) * scale,
                            (v[1] - pivot_from_centre[1]) * scale,
                        ]
                    })
                    .collect()
            })
            .collect();
    } else if s.generate_physics_shape && !round {
        tracing::warn!(
            "the default physics shape of a Polygon-mode sprite (the outline of its mesh) is not \
             supported: the sprite gets none"
        );
    } else if s.generate_physics_shape
        && let Some(region) = &region
        && let Some(shape) = region.trace(texture, 0.25, 200, 0)
    {
        let scale = 1.0 / ppu;
        let [ox, oy] = region.offset;
        out.physics = shape
            .outlines()
            .into_iter()
            .map(|p| {
                p.into_iter()
                    .map(|v| [(v[0] - ox) * scale, (v[1] - oy) * scale])
                    .collect()
            })
            .collect();
    }
    out
}

/// `ProcessVertices`: removes vertices within 1e-4 of their neighbours' segment, then the two
/// around the seam.
fn remove_collinear(v: &mut Vec<[f32; 2]>) {
    const TOLERANCE: f32 = 1e-4;
    fn dist(p: [f32; 2], a: [f32; 2], c: [f32; 2]) -> f32 {
        let d = [c[0] - a[0], c[1] - a[1]];
        let e = [p[0] - a[0], p[1] - a[1]];
        let dot = d[1] * e[1] + d[0] * e[0];
        let sq = if dot <= 0.0 {
            e[0] * e[0] + e[1] * e[1]
        } else {
            let len2 = d[1] * d[1] + d[0] * d[0];
            let q = if dot < len2 {
                let f = dot / len2;
                [a[0] + d[0] * f, a[1] + d[1] * f]
            } else {
                c
            };
            let r = [p[0] - q[0], p[1] - q[1]];
            r[0] * r[0] + r[1] * r[1]
        };
        sq.sqrt()
    }
    if v.len() < 3 {
        return;
    }
    let mut i = 0;
    while i + 2 < v.len() {
        if dist(v[i + 1], v[i], v[i + 2]) <= TOLERANCE {
            v.remove(i + 1);
        } else {
            i += 1;
        }
    }
    let n = v.len();
    if n > 2 && dist(v[0], v[n - 1], v[1]) <= TOLERANCE {
        v.remove(0);
    }
    let n = v.len();
    if n > 2 && dist(v[n - 1], v[n - 2], v[0]) <= TOLERANCE {
        v.pop();
    }
}

/// The libtess2 calls of `Decompose`.
mod tess {
    use std::ffi::{c_int, c_void};

    unsafe extern "C" {
        fn tessNewTess(alloc: *mut c_void) -> *mut c_void;
        fn tessDeleteTess(tess: *mut c_void);
        fn tessAddContour(
            tess: *mut c_void,
            size: c_int,
            pointer: *const c_void,
            stride: c_int,
            count: c_int,
        );
        fn tessTesselate(
            tess: *mut c_void,
            winding_rule: c_int,
            element_type: c_int,
            poly_size: c_int,
            vertex_size: c_int,
            normal: *const f32,
        ) -> c_int;
        fn tessGetVertexCount(tess: *mut c_void) -> c_int;
        fn tessGetVertices(tess: *mut c_void) -> *const f32;
        fn tessGetElementCount(tess: *mut c_void) -> c_int;
        fn tessGetElements(tess: *mut c_void) -> *const c_int;
    }

    const WINDING_NONZERO: c_int = 1;
    const POLYGONS: c_int = 0;

    /// Triangles (vertex indices) of the contours.
    pub fn triangulate(contours: &[Vec<[f32; 2]>]) -> (Vec<[f32; 2]>, Vec<i32>) {
        // SAFETY: a tesselator used on this thread only; inputs outlive the calls, outputs are
        // copied before it is deleted.
        unsafe {
            let t = tessNewTess(std::ptr::null_mut());
            assert!(!t.is_null(), "libtess2: out of memory");
            for c in contours {
                tessAddContour(t, 2, c.as_ptr().cast(), 8, c.len() as c_int);
            }
            let ok = tessTesselate(t, WINDING_NONZERO, POLYGONS, 3, 2, std::ptr::null());
            let mut vertices = Vec::new();
            let mut elements = Vec::new();
            if ok == 1 {
                let nv = tessGetVertexCount(t) as usize;
                let v = std::slice::from_raw_parts(tessGetVertices(t), nv * 2);
                vertices = v.chunks(2).map(|p| [p[0], p[1]]).collect();
                let ne = tessGetElementCount(t) as usize;
                elements = std::slice::from_raw_parts(tessGetElements(t), ne * 3).to_vec();
            }
            tessDeleteTess(t);
            (vertices, elements)
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn image(w: usize, h: usize, f: impl Fn(usize, usize) -> bool) -> Vec<u8> {
        (0..w * h)
            .map(|i| if f(i % w, i / w) { 255 } else { 0 })
            .collect()
    }

    fn circle(cx: f32, cy: f32, r: f32) -> impl Fn(usize, usize) -> bool {
        move |x, y| {
            let (dx, dy) = (x as f32 + 0.5 - cx, y as f32 + 0.5 - cy);
            dx * dx + dy * dy <= r * r
        }
    }

    fn mesh(alpha: &[u8], w: usize, h: usize, extrude: u32) -> (Vec<[f32; 2]>, Vec<u16>) {
        let image = Alpha {
            width: w,
            height: h,
            alpha,
        };
        let (v, mut t) = make_shape(&image, -1.0, 0, true, extrude).triangulate(true);
        t.reverse();
        (v, t)
    }

    // The values below are the Editor's (SpriteUtility.GenerateOutline and Sprite.Create).
    #[test]
    fn circle_outline_and_mesh() {
        let alpha = image(128, 128, circle(64.0, 64.0, 20.0));
        let image = Alpha {
            width: 128,
            height: 128,
            alpha: &alpha,
        };
        let outline: Vec<Vec<[f32; 2]>> = make_shape(&image, 1.0, 0, true, 1)
            .outlines()
            .into_iter()
            .map(|p| p.into_iter().map(|v| [v[0] - 64.0, v[1] - 64.0]).collect())
            .collect();
        assert_eq!(
            outline,
            vec![vec![
                [-22.0, -17.0],
                [-16.0, -22.0],
                [16.0, -22.0],
                [22.0, -19.0],
                [22.0, 10.0],
                [16.0, 22.0],
                [-16.0, 22.0],
                [-22.0, 14.0]
            ]]
        );
        let (v, t) = mesh(&alpha, 128, 128, 1);
        assert_eq!(
            v,
            [
                [80.0, 86.0],
                [86.0, 45.0],
                [86.0, 74.0],
                [80.0, 42.0],
                [48.0, 86.0],
                [48.0, 42.0],
                [42.0, 78.0],
                [42.0, 47.0]
            ]
        );
        assert_eq!(t, [7, 6, 5, 4, 5, 6, 3, 5, 4, 0, 3, 4, 1, 3, 0, 2, 1, 0]);
    }

    #[test]
    fn extrude_grows_by_manhattan_distance() {
        let alpha = image(32, 32, |x, y| {
            (12..15).contains(&x) && (12..17).contains(&y)
        });
        let (v, t) = mesh(&alpha, 32, 32, 4);
        assert_eq!(
            v,
            [
                [15.0, 22.0],
                [20.0, 12.0],
                [20.0, 17.0],
                [15.0, 7.0],
                [12.0, 22.0],
                [12.0, 7.0],
                [7.0, 17.0],
                [7.0, 12.0]
            ]
        );
        assert_eq!(t, [7, 6, 5, 4, 5, 6, 3, 5, 4, 0, 3, 4, 1, 3, 0, 2, 1, 0]);
        let (v, t) = mesh(&alpha, 32, 32, 1);
        assert_eq!(v, [[10.0, 19.0], [17.0, 10.0], [17.0, 19.0], [10.0, 10.0]]);
        assert_eq!(t, [3, 0, 1, 2, 1, 0]);
    }
}
