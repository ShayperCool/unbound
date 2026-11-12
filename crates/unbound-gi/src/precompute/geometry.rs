//! The triangles of a precompute and the rays it traces through them: a bounding volume
//! hierarchy (binned surface area heuristic) with nearest-hit queries that tell front faces from
//! back faces.
use super::math::{V3, cross, dot, sub};

/// Triangles per leaf at most.
const LEAF_TRIANGLES: usize = 4;
/// Bins of the surface area heuristic per split axis.
const BINS: usize = 16;
/// Below this depth a node is split by the surface area heuristic, deeper at its middle, so
/// that no tree is deeper than the traversal's stack.
const SAH_DEPTH: usize = 48;
const STACK: usize = 2 * (SAH_DEPTH + 33);

/// A ray's nearest hit.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Hit {
    pub triangle: u32,
    pub distance: f32,
    /// Whether the ray met the triangle's front (the side its winding faces).
    pub front: bool,
}

#[derive(Debug, Clone, Copy)]
struct Node {
    lo: V3,
    hi: V3,
    /// A leaf's first triangle (into `order`), or an inner node's second child (its first is the
    /// next node).
    start: u32,
    /// A leaf's triangle count; 0 for an inner node.
    count: u32,
}

/// Triangles in a bounding volume hierarchy, each with the direction it faces.
pub struct Bvh {
    triangles: Vec<[V3; 3]>,
    facing: Vec<V3>,
    order: Vec<u32>,
    nodes: Vec<Node>,
}

fn grow(lo: &mut V3, hi: &mut V3, p: V3) {
    for k in 0..3 {
        lo[k] = lo[k].min(p[k]);
        hi[k] = hi[k].max(p[k]);
    }
}

fn half_area(lo: V3, hi: V3) -> f32 {
    let d = sub(hi, lo);
    if d[0] < 0.0 {
        return 0.0;
    }
    d[0] * d[1] + d[1] * d[2] + d[2] * d[0]
}

const EMPTY_LO: V3 = [f32::INFINITY; 3];
const EMPTY_HI: V3 = [f32::NEG_INFINITY; 3];

impl Bvh {
    /// `facing`: per triangle, a direction on its front side (a ray against it meets the front).
    pub fn new(triangles: Vec<[V3; 3]>, facing: Vec<V3>) -> Self {
        assert_eq!(triangles.len(), facing.len());
        let centroids: Vec<V3> = triangles
            .iter()
            .map(|t| std::array::from_fn(|k| (t[0][k] + t[1][k] + t[2][k]) / 3.0))
            .collect();
        let mut bvh = Bvh {
            order: (0..triangles.len() as u32).collect(),
            triangles,
            facing,
            nodes: Vec::new(),
        };
        if !bvh.triangles.is_empty() {
            let n = bvh.triangles.len();
            bvh.build(&centroids, 0, n, 0);
        }
        bvh
    }

    fn bounds(&self, start: usize, end: usize) -> (V3, V3) {
        let (mut lo, mut hi) = (EMPTY_LO, EMPTY_HI);
        for &t in &self.order[start..end] {
            for p in self.triangles[t as usize] {
                grow(&mut lo, &mut hi, p);
            }
        }
        (lo, hi)
    }

    /// Builds the node of `order[start..end]` and its subtree; returns its index.
    fn build(&mut self, centroids: &[V3], start: usize, end: usize, depth: usize) -> usize {
        let (lo, hi) = self.bounds(start, end);
        let index = self.nodes.len();
        self.nodes.push(Node {
            lo,
            hi,
            start: start as u32,
            count: (end - start) as u32,
        });
        if end - start <= LEAF_TRIANGLES {
            return index;
        }
        // The split of least cost over binned centroids along each axis.
        let (mut clo, mut chi) = (EMPTY_LO, EMPTY_HI);
        for &t in &self.order[start..end] {
            grow(&mut clo, &mut chi, centroids[t as usize]);
        }
        let mut best: Option<(f32, usize, f32)> = None;
        for axis in (0..3).filter(|_| depth < SAH_DEPTH) {
            let extent = chi[axis] - clo[axis];
            if extent <= 0.0 {
                continue;
            }
            let bin_of =
                |c: V3| (((c[axis] - clo[axis]) / extent * BINS as f32) as usize).min(BINS - 1);
            let mut counts = [0usize; BINS];
            let mut boxes = [(EMPTY_LO, EMPTY_HI); BINS];
            for &t in &self.order[start..end] {
                let b = bin_of(centroids[t as usize]);
                counts[b] += 1;
                for p in self.triangles[t as usize] {
                    grow(&mut boxes[b].0, &mut boxes[b].1, p);
                }
            }
            // Costs of splitting after bin `s`, from both ends.
            let mut left = [0f32; BINS];
            let (mut l_lo, mut l_hi, mut l_n) = (EMPTY_LO, EMPTY_HI, 0);
            for s in 0..BINS - 1 {
                l_n += counts[s];
                grow(&mut l_lo, &mut l_hi, boxes[s].0);
                grow(&mut l_lo, &mut l_hi, boxes[s].1);
                left[s] = half_area(l_lo, l_hi) * l_n as f32;
            }
            let (mut r_lo, mut r_hi, mut r_n) = (EMPTY_LO, EMPTY_HI, 0);
            for s in (0..BINS - 1).rev() {
                r_n += counts[s + 1];
                grow(&mut r_lo, &mut r_hi, boxes[s + 1].0);
                grow(&mut r_lo, &mut r_hi, boxes[s + 1].1);
                let cost = left[s] + half_area(r_lo, r_hi) * r_n as f32;
                let l_count: usize = counts[..=s].iter().sum();
                if l_count > 0 && l_count < end - start && best.is_none_or(|(c, _, _)| cost < c) {
                    let split = clo[axis] + extent * (s + 1) as f32 / BINS as f32;
                    best = Some((cost, axis, split));
                }
            }
        }
        let mid = match best {
            Some((_, axis, split)) => {
                let slice = &mut self.order[start..end];
                let mut i = 0;
                for j in 0..slice.len() {
                    if centroids[slice[j] as usize][axis] < split {
                        slice.swap(i, j);
                        i += 1;
                    }
                }
                start + i
            }
            None => start,
        };
        // All centroids coincide, the tree is deep, or the split's rounding put everything on
        // one side: halve the list.
        let mid = if mid == start || mid == end {
            start + (end - start) / 2
        } else {
            mid
        };
        self.build(centroids, start, mid, depth + 1);
        let second = self.build(centroids, mid, end, depth + 1);
        self.nodes[index].start = second as u32;
        self.nodes[index].count = 0;
        index
    }

    pub fn triangle(&self, t: u32) -> [V3; 3] {
        self.triangles[t as usize]
    }

    /// The nearest hit of the ray from `origin` along `direction` (not necessarily unit) closer
    /// than `max_distance` (in units of `direction`'s length).
    pub fn intersect(&self, origin: V3, direction: V3, max_distance: f32) -> Option<Hit> {
        if self.nodes.is_empty() {
            return None;
        }
        let inverse: V3 = std::array::from_fn(|k| 1.0 / direction[k]);
        let mut best: Option<Hit> = None;
        let mut limit = max_distance;
        let mut stack = [0u32; STACK];
        let mut top = 1;
        while top > 0 {
            top -= 1;
            let at = stack[top];
            let node = self.nodes[at as usize];
            if !slab(node.lo, node.hi, origin, inverse, limit) {
                continue;
            }
            if node.count > 0 {
                for &t in &self.order[node.start as usize..(node.start + node.count) as usize] {
                    if let Some(d) =
                        intersect_triangle(self.triangles[t as usize], origin, direction)
                        && d < limit
                    {
                        limit = d;
                        best = Some(Hit {
                            triangle: t,
                            distance: d,
                            front: dot(direction, self.facing[t as usize]) < 0.0,
                        });
                    }
                }
            } else {
                // The first child follows its parent.
                assert!(top + 2 <= stack.len(), "a hierarchy deeper than its stack");
                stack[top] = node.start;
                stack[top + 1] = at + 1;
                top += 2;
            }
        }
        best
    }
}

fn slab(lo: V3, hi: V3, origin: V3, inverse: V3, limit: f32) -> bool {
    let (mut near, mut far) = (0f32, limit);
    for k in 0..3 {
        let a = (lo[k] - origin[k]) * inverse[k];
        let b = (hi[k] - origin[k]) * inverse[k];
        // NaN (a zero direction component on the slab's plane) keeps the interval.
        let (a, b) = if a <= b { (a, b) } else { (b, a) };
        if a > near {
            near = a;
        }
        if b < far {
            far = b;
        }
        if near > far {
            return false;
        }
    }
    true
}

/// Möller-Trumbore: the distance along `direction`.
fn intersect_triangle(t: [V3; 3], origin: V3, direction: V3) -> Option<f32> {
    let e1 = sub(t[1], t[0]);
    let e2 = sub(t[2], t[0]);
    let p = cross(direction, e2);
    let det = dot(e1, p);
    if det == 0.0 || !det.is_finite() {
        return None;
    }
    let inv = 1.0 / det;
    let s = sub(origin, t[0]);
    let u = dot(s, p) * inv;
    if !(0.0..=1.0).contains(&u) {
        return None;
    }
    let q = cross(s, e1);
    let v = dot(direction, q) * inv;
    if v < 0.0 || u + v > 1.0 {
        return None;
    }
    let d = dot(e2, q) * inv;
    (d > 0.0).then_some(d)
}
