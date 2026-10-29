//! Normals and tangents the importer calculates: MikkTSpace tangents (Unity's "Calculate Tangent
//! Space"), and the normals of a blend shape's frame as Unity calculates them (measured:
//! tests/projects/blendshapes).

use bevy_mikktspace::{Geometry, TangentSpace};

type V3 = [f64; 3];

fn sub(a: V3, b: V3) -> V3 {
    [a[0] - b[0], a[1] - b[1], a[2] - b[2]]
}

fn dot(a: V3, b: V3) -> f64 {
    a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
}

fn length(a: V3) -> f64 {
    dot(a, a).sqrt()
}

fn normalized(a: V3) -> V3 {
    let l = length(a);
    if l > 0.0 {
        [a[0] / l, a[1] / l, a[2] / l]
    } else {
        a
    }
}

fn angle(a: V3, b: V3) -> f64 {
    let (la, lb) = (length(a), length(b));
    if la == 0.0 || lb == 0.0 {
        return 0.0;
    }
    (dot(a, b) / (la * lb)).clamp(-1.0, 1.0).acos()
}

/// A polygon's normal by Newell's method; its length is twice the polygon's area.
fn newell(points: &[V3]) -> V3 {
    let mut n = [0.0; 3];
    for (k, a) in points.iter().enumerate() {
        let b = points[(k + 1) % points.len()];
        n[0] += (a[1] - b[1]) * (a[2] + b[2]);
        n[1] += (a[2] - b[2]) * (a[0] + b[0]);
        n[2] += (a[0] - b[0]) * (a[1] + b[1]);
    }
    n
}

/// The normals at the polygons' corners of a blend shape frame: around each control point, the
/// shaped polygons' normals weighted by their shaped area and by the corner's angle in the base
/// mesh, over the polygons smooth with the corner's (base normals within the smoothing angle).
/// `polygons` list control points; the result is per polygon corner, in the same order.
pub fn shape_corner_normals(
    base: &[V3],
    shaped: &[V3],
    polygons: &[Vec<u32>],
    smoothing_angle: f64,
) -> Vec<Vec<V3>> {
    let points = |positions: &[V3], polygon: &[u32]| -> Vec<V3> {
        polygon.iter().map(|&c| positions[c as usize]).collect()
    };
    let base_normals: Vec<V3> = polygons
        .iter()
        .map(|p| normalized(newell(&points(base, p))))
        .collect();
    let shaped_normals: Vec<V3> = polygons
        .iter()
        .map(|p| newell(&points(shaped, p)))
        .collect();
    // The polygons around each control point, with the base angle of their corner there.
    let mut around: Vec<Vec<(usize, f64)>> = vec![Vec::new(); base.len()];
    for (f, polygon) in polygons.iter().enumerate() {
        let n = polygon.len();
        for k in 0..n {
            let p = base[polygon[k] as usize];
            let next = base[polygon[(k + 1) % n] as usize];
            let previous = base[polygon[(k + n - 1) % n] as usize];
            around[polygon[k] as usize].push((f, angle(sub(next, p), sub(previous, p))));
        }
    }
    let cos_smoothing = smoothing_angle.to_radians().cos();
    polygons
        .iter()
        .enumerate()
        .map(|(f, polygon)| {
            polygon
                .iter()
                .map(|&c| {
                    let mut sum = [0.0; 3];
                    for &(g, corner_angle) in &around[c as usize] {
                        if g != f && dot(base_normals[f], base_normals[g]) < cos_smoothing {
                            continue;
                        }
                        // |newell| is twice the area: normal × area = newell / 2 (the factor
                        // cancels in the normalization).
                        let n = shaped_normals[g];
                        sum = [
                            sum[0] + n[0] * corner_angle,
                            sum[1] + n[1] * corner_angle,
                            sum[2] + n[2] * corner_angle,
                        ];
                    }
                    normalized(sum)
                })
                .collect()
        })
        .collect()
}

struct Triangles<'a> {
    positions: &'a [[f32; 3]],
    normals: &'a [[f32; 3]],
    uvs: &'a [[f32; 2]],
    indices: &'a [u32],
    tangents: Vec<[f32; 4]>,
    found: Vec<bool>,
}

impl Geometry for Triangles<'_> {
    fn num_faces(&self) -> usize {
        self.indices.len() / 3
    }

    fn num_vertices_of_face(&self, _face: usize) -> usize {
        3
    }

    fn position(&self, face: usize, vert: usize) -> [f32; 3] {
        self.positions[self.indices[face * 3 + vert] as usize]
    }

    fn normal(&self, face: usize, vert: usize) -> [f32; 3] {
        self.normals[self.indices[face * 3 + vert] as usize]
    }

    fn tex_coord(&self, face: usize, vert: usize) -> [f32; 2] {
        self.uvs
            .get(self.indices[face * 3 + vert] as usize)
            .copied()
            .unwrap_or([0.0, 0.0])
    }

    fn set_tangent(&mut self, tangent_space: Option<TangentSpace>, face: usize, vert: usize) {
        let vertex = self.indices[face * 3 + vert] as usize;
        if let Some(space) = tangent_space
            && !self.found[vertex]
        {
            self.found[vertex] = true;
            self.tangents[vertex] = space.tangent_encoded();
        }
    }
}

/// MikkTSpace tangents of a triangle mesh, per vertex (the first corner's where corners of a
/// vertex differ); vertices it finds none for get (1, 0, 0, 1).
pub fn mikk_tangents(
    positions: &[[f32; 3]],
    normals: &[[f32; 3]],
    uvs: &[[f32; 2]],
    triangles: &[Vec<u32>],
) -> Vec<[f32; 4]> {
    let indices: Vec<u32> = triangles.iter().flatten().copied().collect();
    let mut geometry = Triangles {
        positions,
        normals,
        uvs,
        indices: &indices,
        tangents: vec![[1.0, 0.0, 0.0, 1.0]; positions.len()],
        found: vec![false; positions.len()],
    };
    if bevy_mikktspace::generate_tangents(&mut geometry).is_err() {
        return vec![[1.0, 0.0, 0.0, 1.0]; positions.len()];
    }
    geometry.tangents
}
