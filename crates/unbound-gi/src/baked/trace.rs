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

/// A triangle's LOD Group membership: which active LOD Group its renderer belongs to and the
/// 8-bit mask of LOD levels (bits 0..8) that list it. A renderer outside any active LOD Group
/// has [`Lod::NONE`].
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Lod {
    pub group: u32,
    pub mask: u8,
}

impl Lod {
    pub const NONE: Lod = Lod {
        group: u32::MAX,
        mask: 0,
    };

    /// Whether the triangle belongs to LOD 0 or to no LOD Group: visible to light probes,
    /// non-LOD surfaces and LOD 0 surfaces.
    pub const fn is_lod0(self) -> bool {
        self.mask == 0 || (self.mask & 1) != 0
    }

    /// The bit of the most detailed LOD level the renderer belongs to (`0` when not in a LOD Group).
    pub const fn lowest_bit(self) -> u8 {
        self.mask & self.mask.wrapping_neg()
    }

    /// Whether the triangle belongs to `group` with `level_bit` (a single bit >= 2) as its most
    /// detailed LOD level.
    const fn is_lower_level(self, group: u32, level_bit: u8) -> bool {
        self.group == group && self.lowest_bit() == level_bit
    }

    /// Whether a base-pass ray (LOD 0 / non-LOD set) cast for `(group, level_bit)` sees this
    /// triangle: every non-LOD and LOD 0 triangle except LOD 0-only triangles of `group` itself.
    const fn visible_in_base(self, group: u32, level_bit: u8) -> bool {
        self.mask == 0
            || ((self.mask & 1) != 0 && (self.group != group || (self.mask & level_bit) != 0))
    }
}

impl Default for Lod {
    fn default() -> Self {
        Lod::NONE
    }
}

/// Which LOD levels a ray sees (matches the Unity 6000.0.71f1 Editor's Progressive CPU
/// lightmapper, tests/projects/bakedbuffers `Shapes`, `Lod`):
/// - [`Viewer::Lod0`]: non-LOD, LOD 0, light probes, and paths after bouncing onto another
///   group or a non-LOD surface; sees only non-LOD and LOD 0 triangles.
/// - [`Viewer::Lod`]: a lower LOD level (`level_bit` is `1 << level`, `level >= 1`); rays first
///   test triangles of the same group whose most detailed level is `level_bit` (taking precedence
///   over closer base geometry), and on a miss test the base non-LOD/LOD 0 set while skipping
///   LOD 0-only triangles of its own group.
/// - [`Viewer::LodShared`]: a lower-LOD path that missed its own lower level and hit a renderer
///   shared between LOD 0 and `level_bit` of its own group; shadow and environment rays still
///   test its own lower level, while bounce rays stay in the base set.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Viewer {
    #[default]
    Lod0,
    Lod {
        group: u32,
        level_bit: u8,
    },
    LodShared {
        group: u32,
        level_bit: u8,
    },
}

impl Viewer {
    pub const fn from_lod(lod: Lod) -> Viewer {
        if lod.is_lod0() {
            Viewer::Lod0
        } else {
            Viewer::Lod {
                group: lod.group,
                level_bit: lod.lowest_bit(),
            }
        }
    }

    pub const fn is_lod0(self) -> bool {
        matches!(self, Viewer::Lod0)
    }
}

/// The triangles of a scene, for rays.
pub struct Scene {
    /// Each triangle's first corner and its two edges from it.
    triangles: Vec<[[f32; 3]; 3]>,
    /// Each triangle's number among the triangles the scene was made of.
    numbers: Vec<u32>,
    /// Each triangle's LOD Group membership, in leaf order; empty when every triangle is
    /// [`Lod::NONE`].
    lods: Vec<Lod>,
    nodes: Vec<Node>,
}

/// Where a ray first meets the scene.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Hit {
    /// The triangle's number among the triangles the scene was made of.
    pub triangle: u32,
    /// How far along the ray, in lengths of its direction.
    pub distance: f32,
    /// The point's weights of the triangle's second and third corners.
    pub weights: [f32; 2],
    /// Whether the ray meets the side the triangle's corners run clockwise on (the side its
    /// corners' cross product points to).
    pub front: bool,
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
        Scene::with_lods(corners.into_iter().map(|t| (t, Lod::NONE)))
    }

    /// A scene of triangles given by their corners in world space and each triangle's LOD Group
    /// membership. Triangles with a corner that is not a number are left out.
    pub fn with_lods(items: impl IntoIterator<Item = ([[f32; 3]; 3], Lod)>) -> Scene {
        let mut given = Vec::new();
        let mut corners = Vec::new();
        let mut given_lods = Vec::new();
        let mut any_lod = false;
        for (number, (t, lod)) in items.into_iter().enumerate() {
            if !t.iter().flatten().all(|v| v.is_finite()) {
                continue;
            }
            given.push(number as u32);
            corners.push(t);
            given_lods.push(lod);
            any_lod |= lod.mask != 0;
        }
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
        let lods = if any_lod {
            order.iter().map(|&i| given_lods[i as usize]).collect()
        } else {
            Vec::new()
        };
        Scene {
            triangles,
            numbers: order.iter().map(|&i| given[i as usize]).collect(),
            lods,
            nodes,
        }
    }

    pub fn triangle_count(&self) -> usize {
        self.triangles.len()
    }

    /// Whether a triangle crosses the ray after its origin and before `limit` along it. Either
    /// side of a triangle stops the ray (seen as [`Viewer::Lod0`]).
    pub fn occluded(&self, ray: &Ray, limit: f32) -> bool {
        self.occluded_for(ray, limit, Viewer::Lod0)
    }

    /// Whether a triangle crosses the ray at or after its origin (`t >= 0`) and before `limit`
    /// along it. Either side of a triangle stops the ray (seen as [`Viewer::Lod0`]).
    pub fn occluded_inclusive(&self, ray: &Ray, limit: f32) -> bool {
        self.occluded_for_inner(ray, limit, Viewer::Lod0, true)
    }

    /// Whether a triangle visible to `viewer` crosses the ray after its origin and before `limit`
    /// along it. Either side of a triangle stops the ray.
    pub fn occluded_for(&self, ray: &Ray, limit: f32, viewer: Viewer) -> bool {
        self.occluded_for_inner(ray, limit, viewer, false)
    }

    fn occluded_for_inner(
        &self,
        ray: &Ray,
        limit: f32,
        viewer: Viewer,
        include_zero: bool,
    ) -> bool {
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
                let range = node.first as usize..(node.first + node.count) as usize;
                if self.lods.is_empty() {
                    if self.triangles[range]
                        .iter()
                        .any(|t| hit(t, ray, include_zero).is_some_and(|h| h.distance < limit))
                    {
                        return true;
                    }
                } else {
                    for k in range {
                        let lod = self.lods[k];
                        let max_t = match viewer {
                            Viewer::Lod0 => {
                                if !lod.is_lod0() {
                                    continue;
                                }
                                limit
                            }
                            Viewer::Lod { group, level_bit }
                            | Viewer::LodShared { group, level_bit } => {
                                if lod.is_lower_level(group, level_bit)
                                    || lod.visible_in_base(group, level_bit)
                                {
                                    limit
                                } else {
                                    continue;
                                }
                            }
                        };
                        if hit(&self.triangles[k], ray, include_zero)
                            .is_some_and(|h| h.distance < max_t)
                        {
                            return true;
                        }
                    }
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

    /// The nearest triangle the ray crosses after its origin and before `limit` along it (seen
    /// as [`Viewer::Lod0`]).
    pub fn closest(&self, ray: &Ray, limit: f32) -> Option<Hit> {
        self.closest_for(ray, limit, Viewer::Lod0)
            .map(|(hit, _)| hit)
    }

    /// The nearest triangle visible to `viewer` that the ray crosses after its origin and before
    /// `limit`, together with the viewer a path carries on from that hit.
    pub fn closest_for(&self, ray: &Ray, limit: f32, viewer: Viewer) -> Option<(Hit, Viewer)> {
        self.closest_from(ray, ray.origin, limit, viewer)
            .map(|(hit, next, _)| (hit, next))
    }

    /// Like [`closest_for`](Self::closest_for), but when a lower-LOD ray misses its own group and
    /// level and falls back to the base (non-LOD / LOD 0) geometry, the base pass starts from
    /// `base_origin` (and returns the origin `hit.distance` is measured from).
    pub fn closest_from(
        &self,
        ray: &Ray,
        base_origin: [f32; 3],
        limit: f32,
        viewer: Viewer,
    ) -> Option<(Hit, Viewer, [f32; 3])> {
        self.closest_from_inner(ray, base_origin, limit, viewer, false)
    }

    /// Like [`closest_from`](Self::closest_from), but also includes intersections right at the
    /// ray's origin (`t >= 0`), matching Unity's probe ray tracing (`tMin = 0`) for coplanar
    /// probes.
    pub fn closest_from_inclusive(
        &self,
        ray: &Ray,
        base_origin: [f32; 3],
        limit: f32,
        viewer: Viewer,
    ) -> Option<(Hit, Viewer, [f32; 3])> {
        self.closest_from_inner(ray, base_origin, limit, viewer, true)
    }

    fn closest_from_inner(
        &self,
        ray: &Ray,
        base_origin: [f32; 3],
        limit: f32,
        viewer: Viewer,
        include_zero: bool,
    ) -> Option<(Hit, Viewer, [f32; 3])> {
        if self.nodes.is_empty() {
            return None;
        }
        match viewer {
            Viewer::Lod0 => self
                .closest_matching(ray, limit, include_zero, |lod| lod.is_lod0())
                .map(|(hit, _)| (hit, Viewer::Lod0, ray.origin)),
            Viewer::Lod { group, level_bit } => {
                if !self.lods.is_empty()
                    && let Some((hit, _)) = self.closest_matching(ray, limit, include_zero, |lod| {
                        lod.is_lower_level(group, level_bit)
                    })
                {
                    return Some((hit, viewer, ray.origin));
                }
                let base_ray = Ray {
                    origin: base_origin,
                    direction: ray.direction,
                };
                self.closest_matching(&base_ray, limit, include_zero, |lod| {
                    lod.visible_in_base(group, level_bit)
                })
                .map(|(hit, lod)| {
                    let next = if lod.mask != 0 && lod.group == group {
                        Viewer::LodShared { group, level_bit }
                    } else {
                        Viewer::Lod0
                    };
                    (hit, next, base_origin)
                })
            }
            Viewer::LodShared { group, level_bit } => self
                .closest_matching(ray, limit, include_zero, |lod| {
                    lod.visible_in_base(group, level_bit)
                })
                .map(|(hit, lod)| {
                    let next = if lod.mask != 0 && lod.group == group {
                        viewer
                    } else {
                        Viewer::Lod0
                    };
                    (hit, next, ray.origin)
                }),
        }
    }

    fn closest_matching(
        &self,
        ray: &Ray,
        mut limit: f32,
        include_zero: bool,
        accept: impl Fn(Lod) -> bool,
    ) -> Option<(Hit, Lod)> {
        let inverse: [f32; 3] = std::array::from_fn(|k| 1.0 / ray.direction[k]);
        let mut stack = [0u32; 64];
        let mut depth = 1;
        let mut nearest: Option<(Hit, Lod)> = None;
        while depth > 0 {
            depth -= 1;
            let index = stack[depth] as usize;
            let node = &self.nodes[index];
            if !crosses(node, ray, &inverse, limit) {
                continue;
            }
            if node.count > 0 {
                for k in node.first as usize..(node.first + node.count) as usize {
                    let lod = if self.lods.is_empty() {
                        Lod::NONE
                    } else {
                        self.lods[k]
                    };
                    if !accept(lod) {
                        continue;
                    }
                    if let Some(found) = hit(&self.triangles[k], ray, include_zero)
                        && found.distance < limit
                    {
                        limit = found.distance;
                        nearest = Some((
                            Hit {
                                triangle: self.numbers[k],
                                ..found
                            },
                            lod,
                        ));
                    }
                }
            } else {
                stack[depth] = index as u32 + 1;
                stack[depth + 1] = node.first;
                depth += 2;
            }
        }
        nearest
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
    // A node's box is a little wider than its triangles: a precaution against a ray through
    // a triangle on the box's side (a flat floor: a box without thickness) being kept out by
    // rounding. No such ray is known.
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

/// Where along the ray it crosses the triangle (first corner and two edges), after its origin
/// (or at its origin when `include_zero` is set).
/// In double precision: with 32-bit products a ray through an edge two triangles share can
/// miss both.
fn hit(triangle: &[[f32; 3]; 3], ray: &Ray, include_zero: bool) -> Option<Hit> {
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
    let valid = if include_zero {
        distance >= 0.0
    } else {
        distance > 0.0
    };
    valid.then_some(Hit {
        triangle: 0,
        distance: if distance == 0.0 {
            0.0
        } else {
            distance as f32
        },
        weights: [u as f32, v as f32],
        // The corners' cross product is against the ray.
        front: determinant > 0.0,
    })
}
