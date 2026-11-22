//! The bake's rays: whether anything of the scene lies between a point and a light. The scene's
//! triangles are kept in world space in a bounding volume hierarchy.

/// A ray's start and direction (not necessarily of unit length: distances along it are in
/// lengths of `direction`).
#[derive(Clone, Copy, Debug)]
pub struct Ray {
    pub origin: [f32; 3],
    pub direction: [f32; 3],
}

#[derive(Clone, Copy)]
struct Node {
    min: [f32; 3],
    max: [f32; 3],
    /// A leaf: its first triangle and their count. Else the second child (the first follows
    /// the node) and 0.
    first: u32,
    count: u32,
}

/// The triangles of a scene, for rays.
pub struct Scene {
    /// Each triangle's first corner and its two edges from it.
    triangles: Vec<[[f32; 3]; 3]>,
    nodes: Vec<Node>,
}

const LEAF_TRIANGLES: usize = 4;
/// How much wider a node's box is than its triangles, of its coordinates' size.
const BOX_MARGIN: f32 = 1e-5;

fn sub(a: [f32; 3], b: [f32; 3]) -> [f32; 3] {
    [a[0] - b[0], a[1] - b[1], a[2] - b[2]]
}

impl Scene {
    /// A scene of triangles given by their corners in world space. Triangles with a corner that
    /// is not a number are left out.
    pub fn new(corners: impl IntoIterator<Item = [[f32; 3]; 3]>) -> Scene {
        let corners: Vec<[[f32; 3]; 3]> = corners
            .into_iter()
            .filter(|t| t.iter().flatten().all(|v| v.is_finite()))
            .collect();
        let bounds: Vec<([f32; 3], [f32; 3])> = corners
            .iter()
            .map(|t| {
                let min = std::array::from_fn(|k| t[0][k].min(t[1][k]).min(t[2][k]));
                let max = std::array::from_fn(|k| t[0][k].max(t[1][k]).max(t[2][k]));
                (min, max)
            })
            .collect();
        let mut order: Vec<u32> = (0..corners.len() as u32).collect();
        let mut nodes = Vec::with_capacity(2 * corners.len() / LEAF_TRIANGLES + 1);
        if !order.is_empty() {
            build(&bounds, &mut order, 0, &mut nodes);
        }
        let triangles = order
            .iter()
            .map(|&i| {
                let t = corners[i as usize];
                [t[0], sub(t[1], t[0]), sub(t[2], t[0])]
            })
            .collect();
        Scene { triangles, nodes }
    }

    pub fn triangle_count(&self) -> usize {
        self.triangles.len()
    }

    /// Whether a triangle crosses the ray after its origin and before `limit` along it. Either
    /// side of a triangle stops the ray.
    pub fn occluded(&self, ray: &Ray, limit: f32) -> bool {
        if self.nodes.is_empty() {
            return false;
        }
        let inverse: [f32; 3] = std::array::from_fn(|k| 1.0 / ray.direction[k]);
        let mut stack = [0u32; 64];
        let mut depth = 1;
        while depth > 0 {
            depth -= 1;
            let index = stack[depth] as usize;
            let node = &self.nodes[index];
            if !crosses(node, ray, &inverse, limit) {
                continue;
            }
            if node.count > 0 {
                let leaf = &self.triangles[node.first as usize..(node.first + node.count) as usize];
                if leaf.iter().any(|t| hit(t, ray).is_some_and(|d| d < limit)) {
                    return true;
                }
            } else {
                // The hierarchy halves its triangles at every level: it is never deeper than
                // the stack.
                stack[depth] = index as u32 + 1;
                stack[depth + 1] = node.first;
                depth += 2;
            }
        }
        false
    }
}

/// Builds the node of `order` (triangles by their numbers) and its children; `start` is the
/// place of its first triangle in the whole order.
fn build(bounds: &[([f32; 3], [f32; 3])], order: &mut [u32], start: u32, nodes: &mut Vec<Node>) {
    let (mut min, mut max) = ([f32::INFINITY; 3], [f32::NEG_INFINITY; 3]);
    let (mut centre_min, mut centre_max) = (min, max);
    for &i in order.iter() {
        let (lo, hi) = bounds[i as usize];
        for k in 0..3 {
            min[k] = min[k].min(lo[k]);
            max[k] = max[k].max(hi[k]);
            let centre = 0.5 * (lo[k] + hi[k]);
            centre_min[k] = centre_min[k].min(centre);
            centre_max[k] = centre_max[k].max(centre);
        }
    }
    // A node's box is a little wider than its triangles, so that a ray through a triangle on
    // the box's side (a flat floor: a box without thickness) is not kept out by rounding.
    for k in 0..3 {
        let margin = BOX_MARGIN * (1.0 + min[k].abs().max(max[k].abs()));
        min[k] -= margin;
        max[k] += margin;
    }
    let node = nodes.len();
    nodes.push(Node {
        min,
        max,
        first: start,
        count: order.len() as u32,
    });
    // Split at the middle triangle along the axis their centres spread most on; triangles of
    // one centre stay a leaf.
    let axis = (0..3)
        .max_by(|&a, &b| {
            (centre_max[a] - centre_min[a]).total_cmp(&(centre_max[b] - centre_min[b]))
        })
        .unwrap();
    if order.len() <= LEAF_TRIANGLES || centre_max[axis] <= centre_min[axis] {
        return;
    }
    let middle = order.len() / 2;
    let centre = |i: u32| bounds[i as usize].0[axis] + bounds[i as usize].1[axis];
    order.select_nth_unstable_by(middle, |&a, &b| centre(a).total_cmp(&centre(b)));
    let (left, right) = order.split_at_mut(middle);
    build(bounds, left, start, nodes);
    nodes[node].first = nodes.len() as u32;
    nodes[node].count = 0;
    build(bounds, right, start + middle as u32, nodes);
}

fn crosses(node: &Node, ray: &Ray, inverse: &[f32; 3], limit: f32) -> bool {
    let (mut near, mut far) = (0f32, limit);
    for k in 0..3 {
        let (a, b) = (
            (node.min[k] - ray.origin[k]) * inverse[k],
            (node.max[k] - ray.origin[k]) * inverse[k],
        );
        // A ray along a side of the box (0 × infinity) is not kept out by that axis.
        let (a, b) = if a.is_nan() || b.is_nan() {
            (f32::NEG_INFINITY, f32::INFINITY)
        } else if a <= b {
            (a, b)
        } else {
            (b, a)
        };
        near = near.max(a);
        far = far.min(b);
    }
    near <= far
}

/// Where along the ray it crosses the triangle (first corner and two edges), after its origin.
/// In double precision: with 32-bit products a ray through an edge two triangles share can
/// miss both.
fn hit(triangle: &[[f32; 3]; 3], ray: &Ray) -> Option<f32> {
    let wide = |v: [f32; 3]| v.map(f64::from);
    let cross = |a: [f64; 3], b: [f64; 3]| {
        [
            a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0],
        ]
    };
    let dot = |a: [f64; 3], b: [f64; 3]| a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    let (corner, e1, e2) = (wide(triangle[0]), wide(triangle[1]), wide(triangle[2]));
    let (origin, direction) = (wide(ray.origin), wide(ray.direction));
    let p = cross(direction, e2);
    let determinant = dot(e1, p);
    if determinant == 0.0 {
        return None;
    }
    let inverse = 1.0 / determinant;
    let s = [
        origin[0] - corner[0],
        origin[1] - corner[1],
        origin[2] - corner[2],
    ];
    let u = dot(s, p) * inverse;
    if !(0.0..=1.0).contains(&u) {
        return None;
    }
    let q = cross(s, e1);
    let v = dot(direction, q) * inverse;
    if v < 0.0 || u + v > 1.0 {
        return None;
    }
    let distance = dot(e2, q) * inverse;
    (distance > 0.0).then_some(distance as f32)
}
