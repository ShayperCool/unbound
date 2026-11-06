//! Chart boxes and the atlas: each chart's tightest box by rotating calipers over its convex
//! hull, then the charts, widest first, packed into the unit square by splitting free
//! rectangles, shrinking them until all fit (the resulting layout matches the Unity 6000.0.71f1
//! Editor's: tests/projects/lightmapuvs).

use super::math;
use super::param::Chart;

const EPS: f64 = 1e-6;

fn separated(a: f64, b: f64) -> bool {
    EPS < (a - b).abs()
}

/// The convex hull (counter-clockwise from the lowest x) of four or more points; fewer are
/// returned as they are.
pub fn convex_hull(points: &[[f64; 2]]) -> Vec<[f64; 2]> {
    let n = points.len();
    if n < 4 {
        return points.to_vec();
    }
    let mut sorted = points.to_vec();
    sorted.sort_by(|a, b| {
        let less = |a: &[f64; 2], b: &[f64; 2]| {
            let d = a[0] - b[0];
            if !(b[0] < a[0] || d == 0.0) {
                return true;
            }
            d == 0.0 && a[1] <= b[1] && a[1] - b[1] != 0.0
        };
        if less(a, b) {
            std::cmp::Ordering::Less
        } else if less(b, a) {
            std::cmp::Ordering::Greater
        } else {
            std::cmp::Ordering::Equal
        }
    });
    let turn = |o: [f64; 2], a: [f64; 2], p: [f64; 2]| {
        (p[1] - o[1]) * (a[0] - o[0]) - (p[0] - o[0]) * (a[1] - o[1])
    };
    let mut hull: Vec<[f64; 2]> = Vec::with_capacity(n + 1);
    for &p in &sorted {
        while hull.len() >= 2 && turn(hull[hull.len() - 2], hull[hull.len() - 1], p) < 1e-10 {
            hull.pop();
        }
        hull.push(p);
    }
    let lower = hull.len();
    for &p in sorted[..n - 1].iter().rev() {
        while hull.len() >= lower + 1 && turn(hull[hull.len() - 2], hull[hull.len() - 1], p) < 1e-10
        {
            hull.pop();
        }
        hull.push(p);
    }
    hull.pop();
    hull
}

/// The index of the smallest of `v[0..4]`, an earlier one on near ties.
fn min4(v: &[f64]) -> usize {
    let i01 = if v[1] < v[0] && separated(v[0], v[1]) {
        1
    } else {
        0
    };
    let i23 = if v[3] < v[2] && separated(v[2], v[3]) {
        3
    } else {
        2
    };
    if (v[i01] - v[i23]).abs() <= EPS || v[i01] <= v[i23] {
        i01
    } else {
        i23
    }
}

/// The index of the largest of `v[0..4]`, an earlier one on near ties.
fn max4(v: &[f64]) -> usize {
    let i01 = if separated(v[0], v[1]) && v[0] <= v[1] {
        1
    } else {
        0
    };
    let i23 = if separated(v[2], v[3]) && v[2] <= v[3] {
        3
    } else {
        2
    };
    if (v[i01] - v[i23]).abs() <= EPS || v[i23] < v[i01] {
        i01
    } else {
        i23
    }
}

/// The axes and extents of the smallest-area box around the points (the plain axes and zero
/// extents when they enclose no area).
pub fn smallest_box(points: &[[f64; 2]]) -> ([[f64; 2]; 2], [f64; 2]) {
    let mut axes = [[1.0, 0.0], [0.0, 1.0]];
    let hull = convex_hull(points);
    let k = hull.len();
    if k <= 2 {
        return (axes, [0.0, 0.0]);
    }
    let term = |a: [f64; 2], b: [f64; 2]| (b[1] * a[0] - b[0] * a[1]).abs() * 0.5;
    let mut area = (hull[0][1] * hull[k - 1][0] - hull[k - 1][1] * hull[0][0]).abs() * 0.5;
    let terms = k - 1;
    let (mut i, mut prev) = (1, hull[0]);
    for _ in 0..terms / 2 {
        let a = term(prev, hull[i]);
        let b = term(hull[i], hull[i + 1]);
        area = b + a + area;
        prev = hull[i + 1];
        i += 2;
    }
    if terms % 2 == 1 {
        area += term(prev, hull[i]);
    }
    if area < 1e-10 {
        return (axes, [0.0, 0.0]);
    }
    let (mut min, mut max) = ([1e32f64, 1e32], [-1e32f64, -1e32]);
    // Support points: lowest x, highest x, lowest y, highest y.
    let mut support = [0usize; 4];
    for (i, p) in hull.iter().enumerate() {
        if separated(p[0], min[0]) && p[0] <= min[0] {
            min[0] = p[0];
            support[0] = i;
        }
        if separated(p[1], min[1]) && p[1] <= min[1] {
            min[1] = p[1];
            support[2] = i;
        }
        if separated(p[0], max[0]) && max[0] < p[0] {
            max[0] = p[0];
            support[1] = i;
        }
        if separated(p[1], max[1]) && max[1] < p[1] {
            max[1] = p[1];
            support[3] = i;
        }
    }
    let mut extents = [max[0] - min[0], max[1] - min[1]];
    let mut best = (max[1] - min[1]) * (max[0] - min[0]);
    let mut calipers = [[0.0, -1.0], [0.0, 1.0], [1.0, 0.0], [-1.0, 0.0]];
    let mut total = 0.0;
    loop {
        let next = support.map(|s| if s + 1 == k { 0 } else { s + 1 });
        let edges: [[f64; 2]; 4] = std::array::from_fn(|j| {
            [
                hull[next[j]][0] - hull[support[j]][0],
                hull[next[j]][1] - hull[support[j]][1],
            ]
        });
        let angles: [f64; 4] = std::array::from_fn(|j| {
            let e = edges[j];
            let l2 = e[1] * e[1] + e[0] * e[0];
            let l = if 0.0 < l2 { l2.sqrt() } else { 0.0 };
            let (dx, dy) = (e[0] / l, e[1] / l);
            math::acos(dy * calipers[j][1] + dx * calipers[j][0])
        });
        let j = min4(&angles);
        total += angles[j];
        let e = edges[j];
        let l2 = e[1] * e[1] + e[0] * e[0];
        if 0.0 < l2 && 1e-10 <= l2.sqrt() {
            let c = math::cos(total);
            let s = math::sin(total);
            let z = s * 0.0;
            let k0 = c * 0.0;
            calipers = [
                [s + k0, z - c],
                [k0 - s, c + z],
                [c - z, s + k0],
                [-c - z, k0 - s],
            ];
            let l = l2.sqrt();
            let (dx, dy) = (e[0] / l, e[1] / l);
            let o = hull[support[j]];
            let end = hull[next[j]];
            let along: Vec<f64> = support
                .iter()
                .map(|&s| dy * (hull[s][1] - o[1]) + dx * (hull[s][0] - o[0]))
                .chain(std::iter::once(dy * (end[1] - o[1]) + dx * (end[0] - o[0])))
                .collect();
            let nx = -dy;
            let across: Vec<f64> = support
                .iter()
                .map(|&s| (hull[s][1] - o[1]) * dx + (hull[s][0] - o[0]) * nx)
                .chain(std::iter::once((end[1] - o[1]) * dx + (end[0] - o[0]) * nx))
                .collect();
            let pick_min = |v: &[f64]| {
                let i = min4(v);
                let mut r = i;
                if separated(v[i], v[4]) {
                    r = 4;
                }
                if v[i] <= v[4] {
                    r = i;
                }
                r
            };
            let pick_max = |v: &[f64]| {
                let i = max4(v);
                let mut r = i;
                if separated(v[i], v[4]) {
                    r = 4;
                }
                if v[4] < v[i] {
                    r = i;
                }
                r
            };
            let (lo, hi) = (along[pick_min(&along)], along[pick_max(&along)]);
            let (lo2, hi2) = (across[pick_min(&across)], across[pick_max(&across)]);
            let width = hi - lo;
            let height = hi2 - lo2;
            let a = height * width;
            if a <= best && separated(a, best) {
                axes = [[dx, dy], [nx, dx]];
                extents = [width, height];
                best = a;
            }
            support[j] = next[j];
            if std::f64::consts::FRAC_PI_2 <= total {
                break;
            }
        } else {
            support[j] = next[j];
            if !(total < std::f64::consts::FRAC_PI_2) {
                break;
            }
        }
    }
    (axes, extents)
}

#[derive(Debug, Clone, Copy)]
struct Node {
    position: [f64; 2],
    size: [f64; 2],
    children: [u32; 2],
    leaf: bool,
    used: bool,
}

fn insert(nodes: &mut Vec<Node>, mut node: usize, size: [f64; 2], margin: f64) -> Option<[f64; 2]> {
    loop {
        if !nodes[node].leaf {
            let [a, b] = nodes[node].children;
            if let Some(p) = insert(nodes, a as usize, size, margin) {
                return Some(p);
            }
            node = b as usize;
            continue;
        }
        let n = nodes[node];
        if n.used {
            return None;
        }
        let dw = n.size[0] - size[0];
        let dh = n.size[1] - size[1];
        if dw < 0.0 || dh < 0.0 {
            return None;
        }
        if dw <= margin && dh <= margin {
            nodes[node].used = true;
            return Some(n.position);
        }
        let (first, second) = if dw < dh || (dh - dw).abs() <= EPS {
            (
                ([n.position[0], n.position[1]], [n.size[0], size[1]]),
                (
                    [0.0 + n.position[0], size[1] + n.position[1]],
                    [n.size[0], dh],
                ),
            )
        } else {
            (
                ([n.position[0], n.position[1]], [size[0], n.size[1]]),
                (
                    [size[0] + n.position[0], 0.0 + n.position[1]],
                    [dw, n.size[1]],
                ),
            )
        };
        let len = nodes.len() as u32;
        nodes[node].leaf = false;
        nodes[node].children = [len, len + 1];
        for (position, size) in [first, second] {
            nodes.push(Node {
                position,
                size,
                children: [u32::MAX; 2],
                leaf: true,
                used: false,
            });
        }
        node = len as usize;
    }
}

/// Places the charts in the unit square with `margin` between them. Returns whether they fit,
/// and whether the margin had to be reduced.
pub fn pack(charts: &mut [Chart], margin: f32) -> (bool, bool) {
    struct Rect {
        min: [f64; 2],
        size: [f64; 2],
        scale: f64,
        position: [f64; 2],
        chart: usize,
    }
    let mut rects: Vec<Rect> = charts
        .iter()
        .enumerate()
        .map(|(i, c)| Rect {
            min: c.min,
            size: [c.max[0] - c.min[0], c.max[1] - c.min[1]],
            scale: 1.0,
            position: c.min,
            chart: i,
        })
        .collect();
    rects.sort_by(|a, b| {
        let less = |a: &Rect, b: &Rect| {
            a.size[0] > b.size[0] || (a.size[0] == b.size[0] && a.size[1] > b.size[1])
        };
        if less(a, b) {
            std::cmp::Ordering::Less
        } else if less(b, a) {
            std::cmp::Ordering::Greater
        } else {
            std::cmp::Ordering::Equal
        }
    });
    let mut margin = f64::from(margin);
    let n = rects.len();
    let mut total = 0.0;
    let mut i = 0;
    while i + 1 < n {
        let (a, b) = (&rects[i], &rects[i + 1]);
        total = (b.size[1] + margin) * (b.size[0] + margin)
            + (a.size[1] + margin) * (a.size[0] + margin)
            + total;
        i += 2;
    }
    if i < n {
        total += (rects[i].size[1] + margin) * (rects[i].size[0] + margin);
    }
    let mut scale = 1.0 / if 0.0 < total { total.sqrt() } else { 0.0 };
    let mut fit = 1.0;
    for r in &rects {
        let a = (1.0 / scale) / (r.size[0] + margin);
        let a = if fit <= a { fit } else { a };
        let b = (1.0 / scale) / (r.size[1] + margin);
        fit = if a <= b { a } else { b };
    }
    if n == 0 {
        fit = 1.0;
    }
    scale *= fit;
    let mut reduced = false;
    let mut attempts = 0;
    let mut placed: Vec<[f64; 2]> = vec![[0.0; 2]; n];
    let ok = loop {
        let half = margin * 0.5;
        let room = 1.0 - half;
        let mut ok = false;
        for step in 0..26u32 {
            let s = (step as f64 * -0.02 + 1.0) * scale;
            let mut nodes = vec![Node {
                position: [half, half],
                size: [room, room],
                children: [u32::MAX; 2],
                leaf: true,
                used: false,
            }];
            ok = true;
            for (k, r) in rects.iter_mut().enumerate() {
                r.scale = s;
                let size = [r.size[0] * s + margin, r.size[1] * s + margin];
                match insert(&mut nodes, 0, size, margin) {
                    Some(p) => placed[k] = p,
                    None => {
                        ok = false;
                        break;
                    }
                }
            }
            if ok {
                break;
            }
        }
        if ok {
            for (k, r) in rects.iter_mut().enumerate() {
                r.position = [placed[k][0] + half, placed[k][1] + half];
            }
            break true;
        }
        attempts += 1;
        reduced = true;
        margin = half;
        if attempts == 10 {
            break false;
        }
    };
    for r in &rects {
        let chart = &mut charts[r.chart];
        for uv in &mut chart.uvs {
            *uv = [
                (uv[0] - r.min[0]) * r.scale + r.position[0],
                (uv[1] - r.min[1]) * r.scale + r.position[1],
            ];
        }
    }
    (ok, reduced)
}
