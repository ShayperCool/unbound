//! What a bake knows of each texel of a baked lightmap before it lights it: the point of the
//! surface it stands for and the surface's normals there, the chart it belongs to and whether
//! it is used at all, as the Unity 6000.0.71f1 Editor's Progressive CPU lightmapper derives them
//! from the renderers laid out in the lightmap (tests/projects/bakedbuffers). Charts are
//! rasterized conservatively ([`raster`](super::raster)) at the lightmap's size times its
//! supersampling, so each texel has several samples.
use super::raster;
use std::collections::HashMap;

/// A mesh as the bake takes it.
#[derive(Clone, Debug)]
pub struct Mesh {
    pub positions: Vec<[f32; 3]>,
    /// Of unit length ([`unit_normal`]).
    pub normals: Vec<[f32; 3]>,
    /// The lightmap UVs brought to 0..1 over their bounds ([`Mesh::new`]).
    pub uvs: Vec<[f32; 2]>,
    /// What brought them there: UV × xy + zw.
    pub uv_scale_offset: [f32; 4],
    /// The triangles of each submesh.
    pub submeshes: Vec<Vec<[u32; 3]>>,
}

/// Why a mesh cannot be baked.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum MeshError {
    NoPositions,
    NoNormals,
    NoUvs,
    /// A lightmap UV that is infinite or not a number.
    UvNotFinite,
}

impl std::fmt::Display for MeshError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(match self {
            MeshError::NoPositions => "it has no vertices",
            MeshError::NoNormals => "it has no normals",
            MeshError::NoUvs => "it has no UVs",
            MeshError::UvNotFinite => "a lightmap UV of it is infinite or not a number",
        })
    }
}

/// A normal at unit length; the up axis for one shorter than this.
const SHORTEST_NORMAL: f32 = 1e-5;

/// A vertex normal as the bake takes it.
pub fn unit_normal(n: [f32; 3]) -> [f32; 3] {
    let length = (n[2] * n[2] + n[0] * n[0] + n[1] * n[1]).sqrt();
    if length <= SHORTEST_NORMAL {
        [0.0, 1.0, 0.0]
    } else {
        [n[0] / length, n[1] / length, n[2] / length]
    }
}

/// What brings lightmap UVs of the bounds `[x, y, width, height]` to 0..1: UV × xy + zw. A side
/// without extent has scale 0.
pub fn uv_scale_offset(bounds: [f32; 4]) -> [f32; 4] {
    let scale = |extent: f32| {
        let s = if 0.0 < extent { 1.0 / extent } else { 0.0 };
        if s.is_finite() { s } else { 0.0 }
    };
    let (sx, sy) = (scale(bounds[2]), scale(bounds[3]));
    [sx, sy, -(bounds[0] * sx), -(bounds[1] * sy)]
}

impl Mesh {
    /// A mesh for the bake: its normals at unit length and its lightmap UVs (`uv2`, else `uv`)
    /// brought to 0..1 over their bounds. A triangle naming a vertex the mesh does not have is
    /// left out, as the layout leaves it out of the mesh's area (what the Editor's bake makes of
    /// one is not measured: its meshes cannot have them).
    pub fn new(
        positions: Vec<[f32; 3]>,
        normals: &[[f32; 3]],
        uv: &[[f32; 2]],
        uv2: &[[f32; 2]],
        submeshes: Vec<Vec<[u32; 3]>>,
    ) -> Result<Mesh, MeshError> {
        let count = positions.len();
        if count == 0 {
            return Err(MeshError::NoPositions);
        }
        let source = if uv2.len() == count { uv2 } else { uv };
        if source.len() != count {
            return Err(MeshError::NoUvs);
        }
        if normals.len() != count {
            return Err(MeshError::NoNormals);
        }
        if source.iter().flatten().any(|v| !v.is_finite()) {
            return Err(MeshError::UvNotFinite);
        }
        let submeshes = submeshes
            .into_iter()
            .map(|triangles| {
                triangles
                    .into_iter()
                    .filter(|t| t.iter().all(|&i| (i as usize) < count))
                    .collect()
            })
            .collect();
        let so = uv_scale_offset(super::atlas::uv_bounds(source));
        Ok(Mesh {
            positions,
            normals: normals.iter().map(|&n| unit_normal(n)).collect(),
            uvs: source
                .iter()
                .map(|uv| [uv[0] * so[0] + so[2], uv[1] * so[1] + so[3]])
                .collect(),
            uv_scale_offset: so,
            submeshes,
        })
    }

    /// The vertex's place in a lightmap whose scale and offset for the mesh's UVs are `st`.
    fn lightmap_uv(&self, vertex: usize, st: [f32; 4]) -> [f32; 2] {
        let so = self.uv_scale_offset;
        let back = |s: f32| if s != 0.0 { 1.0 / s } else { 1.0 };
        let uv = self.uvs[vertex];
        [
            (uv[0] - so[2]) * back(so[0]) * st[0] + st[2],
            (uv[1] - so[3]) * back(so[1]) * st[1] + st[3],
        ]
    }

    /// The chart of every triangle, per submesh, numbered from `first`, and how many numbers
    /// the mesh took. Triangles that share a vertex (one of the same position, normal and
    /// lightmap UV counts as shared) are of one chart. A submesh numbers its charts from
    /// `first` again, in the order its triangles open them; the number of a chart that joined
    /// another is not given again.
    pub fn charts(&self, first: i32) -> (Vec<Vec<i32>>, i32) {
        // Vertices that are the same to the bake, by the first of them.
        let mut welded: HashMap<[u32; 8], u32> = HashMap::with_capacity(self.positions.len());
        let same: Vec<u32> = (0..self.positions.len())
            .map(|v| {
                let (uv, p, n) = (self.uvs[v], self.positions[v], self.normals[v]);
                let key = [uv[0], uv[1], p[0], p[1], p[2], n[0], n[1], n[2]].map(f32::to_bits);
                *welded.entry(key).or_insert(v as u32)
            })
            .collect();
        let mut taken = 0;
        let charts = self
            .submeshes
            .iter()
            .map(|triangles| {
                let mut of_vertex: HashMap<u32, i32> = HashMap::new();
                let mut members: Vec<Vec<u32>> = Vec::new();
                for t in triangles {
                    let shared = t.map(|v| same[v as usize]);
                    let mut chart: Option<i32> = None;
                    for v in shared {
                        let Some(&found) = of_vertex.get(&v) else {
                            continue;
                        };
                        match chart {
                            None => chart = Some(found),
                            Some(kept) if kept != found => {
                                // Two charts meet in this triangle: the later joins the first.
                                let moved = std::mem::take(&mut members[(found - first) as usize]);
                                for &m in &moved {
                                    of_vertex.insert(m, kept);
                                }
                                members[(kept - first) as usize].extend(moved);
                            }
                            Some(_) => {}
                        }
                    }
                    let chart = chart.unwrap_or_else(|| {
                        members.push(Vec::new());
                        members.len() as i32 - 1 + first
                    });
                    for v in t.iter().copied().chain(shared) {
                        if of_vertex.insert(v, chart) != Some(chart) {
                            members[(chart - first) as usize].push(v);
                        }
                    }
                }
                taken += members.len() as i32;
                triangles.iter().map(|t| of_vertex[&t[0]]).collect()
            })
            .collect();
        (charts, taken)
    }
}

/// A renderer of a lightmap.
#[derive(Clone, Copy, Debug)]
pub struct Instance<'a> {
    pub mesh: &'a Mesh,
    /// Its lightmap scale and offset.
    pub scale_offset: [f32; 4],
    /// Its place among the lightmap's renderers.
    pub index: i32,
}

/// A sample without a surface.
pub const EMPTY: f32 = -1.0e6;

/// A lightmap's texels before lighting.
#[derive(Clone, Debug, PartialEq)]
pub struct GBuffer {
    pub width: usize,
    pub height: usize,
    /// Samples per texel along each side (1, 2 or 4).
    pub supersampling: usize,
    /// Per sample (`width` × `supersampling` a row): the point in its renderer's space, and
    /// the renderer's place in the lightmap ([`EMPTY`] without a surface).
    pub positions: Vec<[f32; 4]>,
    /// Per sample: the vertex normals interpolated, in the renderer's space, and the
    /// renderer's place plus the triangle's number in its submesh.
    pub normals: Vec<[f32; 4]>,
    /// Per sample: the triangle's own normal, with the same fourth value.
    pub plane_normals: Vec<[f32; 4]>,
    /// Per texel: its chart, -1 for none.
    pub charts: Vec<i32>,
    /// Per texel: how many of its samples have a surface, once those without one have
    /// borrowed one: none, or all of them.
    pub occupancy: Vec<u8>,
}

fn length(v: [f32; 3]) -> f32 {
    (v[2] * v[2] + v[1] * v[1] + v[0] * v[0]).sqrt()
}

/// Rasterizes the renderers of a lightmap, in their order (a later one takes the texels it
/// covers no less than an earlier one).
pub fn gbuffer(
    width: usize,
    height: usize,
    supersampling: usize,
    instances: &[Instance],
) -> GBuffer {
    let (sw, sh) = (width * supersampling, height * supersampling);
    let empty = [0.0, 0.0, 0.0, EMPTY];
    let mut positions = vec![empty; sw * sh];
    let mut normals = vec![empty; sw * sh];
    let mut plane_normals = vec![empty; sw * sh];
    let mut coverage = vec![0f32; sw * sh];
    for instance in instances {
        let mesh = instance.mesh;
        for triangles in &mesh.submeshes {
            for (number, t) in triangles.iter().enumerate() {
                let corners = t.map(|v| v as usize);
                let uv = corners.map(|v| mesh.lightmap_uv(v, instance.scale_offset));
                raster::triangle(uv, sw, sh, &mut coverage, |texel| {
                    let [a, b, c] = texel.corners.map(|k| corners[k]);
                    let [wa, wb, wc] = texel.weights;
                    let at = |values: &[[f32; 3]], k: usize| {
                        values[c][k] * wc + values[b][k] * wb + values[a][k] * wa
                    };
                    let (pa, pb, pc) = (mesh.positions[a], mesh.positions[b], mesh.positions[c]);
                    let sample = sw * texel.y + texel.x;
                    positions[sample] = [
                        at(&mesh.positions, 0),
                        at(&mesh.positions, 1),
                        at(&mesh.positions, 2),
                        instance.index as f32,
                    ];
                    // The triangle's own normal, of its corners as the mesh orders them.
                    let (e1, e2) = (
                        [pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2]],
                        [pc[0] - pa[0], pc[1] - pa[1], pc[2] - pa[2]],
                    );
                    let mut plane = [
                        e1[1] * e2[2] - e2[1] * e1[2],
                        e2[0] * e1[2] - e1[0] * e2[2],
                        e1[0] * e2[1] - e1[1] * e2[0],
                    ];
                    if texel.corners[0] != 0 {
                        plane = [
                            e2[1] * e1[2] - e1[1] * e2[2],
                            e1[0] * e2[2] - e2[0] * e1[2],
                            e1[1] * e2[0] - e1[0] * e2[1],
                        ];
                    }
                    let n = |k: usize| {
                        mesh.normals[b][k] * wb + mesh.normals[a][k] * wa + mesh.normals[c][k] * wc
                    };
                    let smooth = [n(0), n(1), n(2)];
                    let smooth_length = length(smooth);
                    let plane_length = length(plane);
                    let plane = if plane_length != 0.0 {
                        plane.map(|v| v / plane_length)
                    } else if smooth_length > 0.0 {
                        smooth.map(|v| v / smooth_length)
                    } else {
                        [1.0, 0.0, 0.0]
                    };
                    let smooth = if smooth_length > 0.0 {
                        smooth.map(|v| v / smooth_length)
                    } else {
                        plane
                    };
                    let id = (instance.index + number as i32) as f32;
                    plane_normals[sample] = [plane[0], plane[1], plane[2], id];
                    normals[sample] = [smooth[0], smooth[1], smooth[2], id];
                });
            }
        }
    }
    // A texel with some samples on a surface lends them to its samples without one, in turn.
    let s = supersampling;
    let mut lent = Vec::with_capacity(s * s);
    for ty in 0..height {
        for tx in 0..width {
            let samples: Vec<usize> = (0..s)
                .flat_map(|sy| (0..s).map(move |sx| sw * (ty * s + sy) + tx * s + sx))
                .collect();
            lent.clear();
            lent.extend(
                samples
                    .iter()
                    .filter(|&&k| 0.0 <= positions[k][3])
                    .map(|&k| (positions[k], normals[k], plane_normals[k])),
            );
            if lent.is_empty() || lent.len() == s * s {
                continue;
            }
            let mut turn = 0;
            for &k in &samples {
                if positions[k][3] < 0.0 {
                    (positions[k], normals[k], plane_normals[k]) = lent[turn % lent.len()];
                    turn += 1;
                }
            }
        }
    }
    // The charts, at the lightmap's own size.
    let mut charts = vec![-1i32; width * height];
    let mut coverage = vec![0f32; width * height];
    let mut first = 0;
    for instance in instances {
        let (of_triangle, taken) = instance.mesh.charts(first);
        first += taken;
        for (triangles, of_triangle) in instance.mesh.submeshes.iter().zip(&of_triangle) {
            for (t, &chart) in triangles.iter().zip(of_triangle) {
                let uv = t.map(|v| instance.mesh.lightmap_uv(v as usize, instance.scale_offset));
                raster::triangle(uv, width, height, &mut coverage, |texel| {
                    charts[width * texel.y + texel.x] = chart
                });
            }
        }
    }
    let occupancy = (0..width * height)
        .map(|texel| {
            let (tx, ty) = (texel % width, texel / width);
            (0..s * s)
                .filter(|k| 0.0 <= positions[sw * (ty * s + k / s) + tx * s + k % s][3])
                .count() as u8
        })
        .collect();
    GBuffer {
        width,
        height,
        supersampling,
        positions,
        normals,
        plane_normals,
        charts,
        occupancy,
    }
}
