//! Normals and vertex splits of imported meshes as Unity's model importer makes them (matches the
//! Unity 6000.0.71f1 Editor, tests/projects/lightmapuvs).
//!
//! The importer keeps one normal per face corner ("wedge") of the position-welded
//! mesh: the file's, or calculated (Normals "Calculate", or "Import" when the file has none).
//! Calculated normals start from each face's normal; an edge between two faces is smooth
//! by the smoothing source (smoothing groups, the smoothing angle, or never); each corner sums the
//! weighted normals of the faces reached around its control point across smooth edges. The
//! normals are then scaled into the mesh's space and renormalized. Vertices come from the wedges:
//! each control point's first wedge makes its vertex, and a later wedge makes a new vertex unless
//! its attributes match one of the control point's vertices so far (normals and tangents within
//! about 1°, UVs within 0.001, equal colours).

pub type V3 = [f32; 3];

const NONE: u32 = u32::MAX;

/// cos(angle) − 0.001: the importer's threshold for "within `degrees`".
pub fn cos_threshold(degrees: f32) -> f32 {
    let half = degrees / 360.0;
    ((half + half) * std::f32::consts::PI).cos() + -0.001
}

/// The threshold of the vertex split (1°).
pub fn split_threshold() -> f32 {
    cos_threshold(1.0)
}

/// Largest squared distance between UVs (and tangent handedness difference) that still welds.
pub const UV_WELD: f32 = f32::from_bits(0x3586_37be);
pub const W_WELD: f32 = f32::from_bits(0x3586_37bd);

pub fn dot(a: V3, b: V3) -> f32 {
    (a[0] * b[0] + a[1] * b[1]) + a[2] * b[2]
}

fn sub(a: V3, b: V3) -> V3 {
    [a[0] - b[0], a[1] - b[1], a[2] - b[2]]
}

fn cross(a: V3, b: V3) -> V3 {
    [
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    ]
}

fn squared_length(v: V3) -> f32 {
    (v[0] * v[0] + v[1] * v[1]) + v[2] * v[2]
}

/// Unit length by the largest component: components within 1e-5 of zero count as zero, the zero
/// vector becomes (0, 1, 0).
pub fn normalize_robust(v: V3) -> V3 {
    const EPS: f32 = 1e-5;
    let keep = |c: f32| if c.abs() > EPS { c } else { 0.0 };
    let [x, y, z] = v.map(keep);
    let [ax, ay, az] = [x.abs(), y.abs(), z.abs()];
    // The two small components divided by the largest; the largest becomes ±1/length.
    let finish = |a: f32, b: f32, big: f32| {
        let inv = 1.0 / ((a * a + b * b) + 1.0).sqrt();
        (a * inv, b * inv, inv.copysign(big))
    };
    if ay > ax {
        if az > ay {
            let (a, b, c) = finish(x / az, y / az, z);
            return [a, b, c];
        }
        let (a, b, c) = finish(x / ay, z / ay, y);
        return [a, c, b];
    }
    if az > ax {
        let (a, b, c) = finish(x / az, y / az, z);
        return [a, b, c];
    }
    if ax <= 0.0 {
        return [0.0, 1.0, 0.0];
    }
    let (a, b, c) = finish(y / ax, z / ax, x);
    [c, a, b]
}

/// A normal brought into Unity's space by the inverse transpose of the mesh's diagonal transform.
pub fn transform_normal(n: V3, inverse_scale: V3) -> V3 {
    normalize_robust([
        n[0] * inverse_scale[0],
        n[1] * inverse_scale[1],
        n[2] * inverse_scale[2],
    ])
}

/// The reciprocal estimate refined twice, as the importer scales a triangle into a unit box.
fn reciprocal(m: f32) -> f32 {
    #[cfg(target_arch = "x86_64")]
    let r0 = {
        use std::arch::x86_64::{_mm_cvtss_f32, _mm_rcp_ss, _mm_set_ss};
        // SAFETY: SSE is part of the x86_64 baseline.
        unsafe { _mm_cvtss_f32(_mm_rcp_ss(_mm_set_ss(m))) }
    };
    #[cfg(not(target_arch = "x86_64"))]
    let r0 = 1.0 / m;
    let r1 = (f32::from_bits(0x4000_0002) - m * r0) * r0;
    let r2 = (2.0 - m * r1) * r1;
    if r2.is_nan() { r0 } else { r2 }
}

/// A face's unit normal (zero when degenerate), on its corners scaled into a unit box: a
/// triangle's cross product, a quad's sum over its edges.
fn face_normal(p: &[V3]) -> V3 {
    let mut min = p[0];
    let mut max = p[0];
    for q in &p[1..] {
        for i in 0..3 {
            min[i] = if min[i] < q[i] { min[i] } else { q[i] };
            max[i] = if max[i] > q[i] { max[i] } else { q[i] };
        }
    }
    let e = sub(max, min);
    let pick = |a: f32, b: f32| if a > b { a } else { b };
    let m = pick(pick(pick(e[0], e[1]), pick(e[2], e[0])), 1e-6);
    let r = reciprocal(m);
    let q: Vec<V3> = p
        .iter()
        .map(|v| {
            [
                (v[0] - min[0]) * r,
                (v[1] - min[1]) * r,
                (v[2] - min[2]) * r,
            ]
        })
        .collect();
    let c = if q.len() < 4 {
        cross(sub(q[1], q[0]), sub(q[2], q[0]))
    } else {
        let mut c = [0.0f32; 3];
        for (i, a) in q.iter().enumerate() {
            let b = q[(i + 1) % q.len()];
            c[0] += (a[2] + b[2]) * (a[1] - b[1]);
            c[1] += (a[0] + b[0]) * (a[2] - b[2]);
            c[2] += (a[1] + b[1]) * (a[0] - b[0]);
        }
        c
    };
    let l2 = squared_length(c);
    if !(l2 > 1e-10) {
        return [0.0; 3];
    }
    let inv = 1.0 / l2.sqrt();
    c.map(|v| inv * v)
}

/// The cross product a face's area and legacy normal come from: of the diagonals for a quad.
fn face_cross(p: &[V3]) -> V3 {
    cross(
        sub(p[1], p[if p.len() == 4 { 3 } else { 0 }]),
        sub(p[2], p[0]),
    )
}

/// Faces of three or four corners, each corner a control point.
pub struct Faces<'a> {
    pub corners: &'a [u32],
    /// Each face's first corner, and the corner count at the end.
    pub start: &'a [u32],
}

impl Faces<'_> {
    fn count(&self) -> usize {
        self.start.len() - 1
    }

    fn range(&self, f: usize) -> std::ops::Range<usize> {
        self.start[f] as usize..self.start[f + 1] as usize
    }
}

/// `ModelImporterNormalCalculationMode`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Weighting {
    UnweightedLegacy,
    Unweighted,
    Area,
    Angle,
    AreaAndAngle,
}

impl Weighting {
    pub fn from_mode(mode: i64) -> Self {
        match mode {
            0 => Self::UnweightedLegacy,
            1 => Self::Unweighted,
            2 => Self::Area,
            3 => Self::Angle,
            _ => Self::AreaAndAngle,
        }
    }
}

/// `ModelImporterNormalSmoothingSource`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SmoothingSource {
    PreferSmoothingGroups,
    FromSmoothingGroups,
    FromAngle,
    None,
}

impl SmoothingSource {
    pub fn from_mode(mode: i64) -> Self {
        match mode {
            1 => Self::FromSmoothingGroups,
            2 => Self::FromAngle,
            3 => Self::None,
            _ => Self::PreferSmoothingGroups,
        }
    }
}

struct Edge {
    faces: [u32; 2],
    corners: [u8; 2],
}

/// Faces meeting at each control point, and the edges two faces share with opposite winding
/// (the first face before the second).
struct Adjacency {
    faces_of: Vec<Vec<u32>>,
    edges: Vec<Edge>,
    corner_edge: Vec<u32>,
}

impl Adjacency {
    fn new(faces: &Faces, vertex_count: usize) -> Self {
        let corners = faces.corners;
        let mut faces_of = vec![Vec::new(); vertex_count];
        for f in 0..faces.count() {
            for c in faces.range(f) {
                faces_of[corners[c] as usize].push(f as u32);
            }
        }
        let mut edges = Vec::new();
        let mut corner_edge = vec![NONE; corners.len()];
        for f in 0..faces.count() {
            let range = faces.range(f);
            let n = range.len();
            for k in 0..n {
                if corner_edge[range.start + k] != NONE {
                    continue;
                }
                let a = corners[range.start + k];
                let b = corners[range.start + (k + 1) % n];
                let (fa, fb) = (&faces_of[a as usize], &faces_of[b as usize]);
                let (mut i, mut j) = (0, 0);
                while i < fa.len() && j < fb.len() {
                    if fa[i] < fb[j] {
                        i += 1;
                        continue;
                    }
                    if fa[i] == fb[j] && fa[i] as usize > f {
                        let g = fa[i] as usize;
                        let other = faces.range(g);
                        let m = other.len();
                        if let Some(l) = (0..m).find(|&l| corners[other.start + l] == b)
                            && corners[other.start + (l + 1) % m] == a
                            && corner_edge[other.start + l] == NONE
                        {
                            corner_edge[range.start + k] = edges.len() as u32;
                            corner_edge[other.start + l] = edges.len() as u32;
                            edges.push(Edge {
                                faces: [f as u32, g as u32],
                                corners: [k as u8, l as u8],
                            });
                        }
                    }
                    if fa[i] == fb[j] {
                        i += 1;
                    }
                    j += 1;
                }
            }
        }
        Self {
            faces_of,
            edges,
            corner_edge,
        }
    }
}

/// Calculated normals per corner (unit length, before scaling into the mesh's space) of `faces`
/// over `positions` (the file's, mirrored into Unity's handedness). `smoothing` holds a
/// smoothing group per corner; the importer looks it up at the control point's index, which is
/// reproduced.
pub fn calculate(
    positions: &[V3],
    faces: &Faces,
    weighting: Weighting,
    source: SmoothingSource,
    angle: f32,
    smoothing: Option<&[u32]>,
) -> Vec<V3> {
    let corners = faces.corners;
    let points = |f: usize| -> Vec<V3> {
        faces
            .range(f)
            .map(|c| positions[corners[c] as usize])
            .collect()
    };
    let face_of: Vec<usize> = (0..faces.count())
        .flat_map(|f| faces.range(f).map(move |_| f))
        .collect();
    let adjacency = Adjacency::new(faces, positions.len());
    let threshold = cos_threshold(angle);
    if weighting == Weighting::UnweightedLegacy {
        let normals: Vec<V3> = (0..faces.count())
            .map(|f| normalize_robust(face_cross(&points(f))))
            .collect();
        return (0..corners.len())
            .map(|c| {
                let own = normals[face_of[c]];
                let mut sum = [0.0f32; 3];
                for &g in &adjacency.faces_of[corners[c] as usize] {
                    let n = normals[g as usize];
                    if dot(own, n) > threshold {
                        sum = [sum[0] + n[0], sum[1] + n[1], sum[2] + n[2]];
                    }
                }
                normalize_robust(sum)
            })
            .collect();
    }
    let normals: Vec<V3> = (0..faces.count())
        .map(|f| face_normal(&points(f)))
        .collect();
    let groups = |e: &Edge, s: &[u32]| {
        let group = |side: usize| {
            let corner =
                corners[faces.start[e.faces[side] as usize] as usize + e.corners[side] as usize];
            s.get(corner as usize).copied().unwrap_or(0)
        };
        group(0) & group(1) != 0
    };
    let smooth: Vec<bool> = adjacency
        .edges
        .iter()
        .map(|e| match (source, smoothing) {
            (SmoothingSource::None, _) | (SmoothingSource::FromSmoothingGroups, None) => false,
            (
                SmoothingSource::PreferSmoothingGroups | SmoothingSource::FromSmoothingGroups,
                Some(s),
            ) => groups(e, s),
            _ => dot(normals[e.faces[0] as usize], normals[e.faces[1] as usize]) > threshold,
        })
        .collect();
    let area: Vec<f32> = match weighting {
        Weighting::Area | Weighting::AreaAndAngle => (0..faces.count())
            .map(|f| squared_length(face_cross(&points(f))).sqrt())
            .collect(),
        _ => vec![1.0; faces.count()],
    };
    let mut corner_angle = vec![1.0f32; corners.len()];
    if matches!(weighting, Weighting::Angle | Weighting::AreaAndAngle) {
        for f in 0..faces.count() {
            let (range, p) = (faces.range(f), points(f));
            let n = p.len();
            for k in 0..n {
                let (a, b) = (sub(p[(k + n - 1) % n], p[k]), sub(p[(k + 1) % n], p[k]));
                let cos = dot(a, b) / (squared_length(a).sqrt() * squared_length(b).sqrt());
                corner_angle[range.start + k] = cos.max(-1.0).min(1.0).acos();
            }
        }
    }
    let weighted = |f: usize, c: usize| normals[f].map(|v| v * corner_angle[c]);
    let mut sums = vec![[0.0f32; 3]; corners.len()];
    let mut owner = vec![NONE; corners.len()];
    for f in 0..faces.count() {
        let range = faces.range(f);
        let n = range.len();
        for k in 0..n {
            let c = range.start + k;
            if owner[c] != NONE {
                sums[c] = sums[owner[c] as usize];
                continue;
            }
            let mut sum = weighted(f, c).map(|v| v * area[f]);
            // Around the control point both ways, across smooth edges; a closed fan stops the walk.
            'walk: for direction in 0..2 {
                let mut current = f as u32;
                let mut e = adjacency.corner_edge[range.start + (k + n - direction) % n];
                while e != NONE && smooth[e as usize] {
                    let edge = &adjacency.edges[e as usize];
                    let side = if edge.faces[0] == current { 1 } else { 0 };
                    let (g, at) = (edge.faces[side] as usize, edge.corners[side] as usize);
                    if g == f {
                        break 'walk;
                    }
                    let other = faces.range(g);
                    let m = other.len();
                    let corner = other.start + (at + 1 - direction) % m;
                    let term = weighted(g, corner);
                    owner[corner] = c as u32;
                    let next = if direction == 0 {
                        (at + 1) % m
                    } else {
                        (at + m - 1) % m
                    };
                    for i in 0..3 {
                        sum[i] += term[i] * area[g];
                    }
                    e = adjacency.corner_edge[other.start + next];
                    current = g as u32;
                }
            }
            sums[c] = sum;
        }
    }
    sums.into_iter().map(normalize_robust).collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn robust_normalization_scales_by_the_largest_component() {
        assert_eq!(normalize_robust([0.0, 0.0, 0.99999994]), [0.0, 0.0, 1.0]);
        assert_eq!(normalize_robust([0.0, 0.0, 0.0]), [0.0, 1.0, 0.0]);
        assert_eq!(normalize_robust([0.0, -3.0, 4e-6]), [0.0, -1.0, 0.0]);
    }

    #[test]
    fn smooth_quad_corners_share_a_normal() {
        // Two triangles of a folded quad: the shared corners get the same normal when smooth.
        let positions = [
            [0.0, 0.0, 0.0],
            [1.0, 0.0, 0.0],
            [1.0, 1.0, 0.2],
            [0.0, 1.0, 0.0],
        ];
        let faces = Faces {
            corners: &[0, 1, 2, 0, 2, 3],
            start: &[0, 3, 6],
        };
        let smooth = calculate(
            &positions,
            &faces,
            Weighting::AreaAndAngle,
            SmoothingSource::FromAngle,
            60.0,
            None,
        );
        assert_eq!(smooth[0], smooth[3]);
        assert_eq!(smooth[2], smooth[4]);
        let hard = calculate(
            &positions,
            &faces,
            Weighting::AreaAndAngle,
            SmoothingSource::None,
            60.0,
            None,
        );
        assert_ne!(hard[0], hard[3]);
    }
    #[test]
    fn weighting_modes_match_the_editor_reference() {
        let probe = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/lightmapuvs");
        let bytes = std::fs::read(probe.join("Sources/Normals/Tent.fbx")).unwrap();
        let scene = ufbx::load_memory(
            &bytes,
            ufbx::LoadOpts {
                target_axes: ufbx::CoordinateAxes::right_handed_y_up(),
                space_conversion: ufbx::SpaceConversion::AdjustTransforms,
                geometry_transform_handling: ufbx::GeometryTransformHandling::ModifyGeometry,
                pivot_handling: ufbx::PivotHandling::AdjustToPivot,
                ..Default::default()
            },
        )
        .unwrap();
        let mesh = &scene.meshes[0];
        let positions: Vec<_> = mesh
            .vertices
            .iter()
            .map(|v| [-v.x as f32, v.y as f32, v.z as f32])
            .collect();
        let mut corners = Vec::new();
        let mut start = vec![0];
        for face in mesh.faces.iter() {
            let b = face.index_begin as usize;
            corners.push(mesh.vertex_indices[b]);
            corners.extend(
                (b + 1..b + face.num_indices as usize)
                    .rev()
                    .map(|c| mesh.vertex_indices[c]),
            );
            start.push(corners.len() as u32);
        }
        let reference =
            std::fs::read_to_string(probe.join("Reference/unity-6000.0.71f1.jsonl")).unwrap();
        let rows: Vec<serde_json::Value> = reference
            .lines()
            .map(|line| serde_json::from_str(line).unwrap())
            .collect();
        for (variant, mode) in [
            ("NormLegacy", 0),
            ("NormUnweighted", 1),
            ("NormArea", 2),
            ("NormAngle", 3),
            ("NormCalc", 4),
        ] {
            let row = rows
                .iter()
                .find(|r| r["variant"] == variant && r["mesh"] == "Tent/0")
                .unwrap();
            let calculated = calculate(
                &positions,
                &Faces {
                    corners: &corners,
                    start: &start,
                },
                Weighting::from_mode(mode),
                SmoothingSource::PreferSmoothingGroups,
                60.0,
                None,
            );
            let mut actual = Vec::new();
            for range in start.windows(2) {
                let b = range[0] as usize;
                actual.extend([b, b + 1, b + 2]);
                if range[1] - range[0] == 4 {
                    actual.extend([b, b + 2, b + 3]);
                }
            }
            let expected = row["subMeshes"][0].as_array().unwrap();
            assert_eq!(actual.len(), expected.len());
            for (&corner, vertex) in actual.iter().zip(expected) {
                let n = transform_normal(calculated[corner], [100.0; 3]);
                let expected = &row["normals"][vertex.as_u64().unwrap() as usize];
                let expected: V3 = std::array::from_fn(|i| expected[i].as_f64().unwrap() as f32);
                assert_eq!(n, expected, "{variant}, corner {corner}");
            }
        }
    }
}
