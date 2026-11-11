//! Lightmap UVs (`ModelImporter.generateSecondaryUV`): UV2 is generated per triangle corner,
//! before the corners become vertices, so that its seams split vertices like any other attribute
//! (measured: tests/projects/lightmapuvs).
//!
//! The input is the mesh as the importer holds it after triangulation: the control points in
//! Unity's space, the triangles in polygon order (each quad split into two), the polygon each
//! triangle comes from, and UV0 per triangle corner. The output is UV2 per triangle corner.
//!
//! The unwrap fits the mesh into a unit cube and welds it ([`cleanup`]), builds a half-edge map
//! ([`mesh`]), segments each connected part into charts ([`segment`]; a part of more than 100
//! triangles first on a simplified copy, [`simplify`]), lays each chart flat from corrected
//! angles by least squares and checks it ([`param`]), cuts a chart that overlaps itself, and
//! packs the charts ([`pack`]). A triangle the cleanup removed as degenerate takes its UV2 from a
//! kept neighbour ([`removed_triangle_uvs`]).

use super::SecondaryUvSettings;

mod cleanup;
mod math;
mod mesh;
mod pack;
mod param;
mod segment;
mod simplify;
mod sort;

/// The mesh the unwrap works on.
pub struct UnwrapInput<'a> {
    pub positions: &'a [[f32; 3]],
    pub triangles: &'a [[u32; 3]],
    /// The source polygon of each triangle.
    pub polygons: &'a [u32],
    /// UV0 per triangle corner, when the mesh has UVs.
    pub uvs: Option<&'a [[[f32; 2]; 3]]>,
}

/// The unwrap's parameters as the importer derives them from the settings.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct UnwrapParams {
    /// Allowed angle distortion, as a fraction.
    pub angle_error: f32,
    /// Allowed area distortion, as a fraction.
    pub area_error: f32,
    /// Degrees.
    pub hard_angle: f32,
    /// The gap between charts, as a fraction of the atlas.
    pub pack_margin: f32,
}

impl UnwrapParams {
    /// The parameters for `input`: the distortions in percent become fractions; the margin is
    /// the manual one in pixels of a 1024 map, or calculated from the mesh's area so that charts
    /// stay apart at the minimum lightmap resolution and object scale. Small meshes get margins
    /// above 1, which the packer halves until the charts fit.
    pub fn new(settings: &SecondaryUvSettings, input: &UnwrapInput) -> Result<Self, String> {
        let pack_margin = if settings.margin_method == 1 {
            let mut area = 0f32;
            for t in input.triangles {
                let [a, b, c] = t.map(|i| input.positions[i as usize]);
                let (ex, ey, ez) = (b[0] - a[0], b[1] - a[1], b[2] - a[2]);
                let (fx, fy, fz) = (c[0] - a[0], c[1] - a[1], c[2] - a[2]);
                let cx = ey * fz - ez * fy;
                let cy = ez * fx - fz * ex;
                let cz = fy * ex - fx * ey;
                area += (cy * cy + cx * cx + cz * cz).sqrt();
            }
            let size = (area * 0.5).sqrt();
            let margin =
                1.0 / (settings.min_object_scale * settings.min_lightmap_resolution * size);
            let margin = if 0.0001 <= margin { margin } else { 0.0001 } * 3.0;
            if !margin.is_finite() {
                return Err(
                    "the calculated pack margin is not finite (zero area, resolution or scale)"
                        .into(),
                );
            }
            margin
        } else {
            settings.pack_margin * (1.0 / 1024.0)
        };
        Ok(Self {
            angle_error: settings.angle_distortion / 100.0,
            area_error: settings.area_distortion / 100.0,
            hard_angle: settings.hard_angle,
            pack_margin,
        })
    }
}

/// UV2 per triangle corner: the mesh is cut into charts, each laid flat, and the charts are
/// packed into the unit square.
pub fn unwrap(input: &UnwrapInput, params: &UnwrapParams) -> Result<Vec<[[f32; 2]; 3]>, String> {
    let geometry = cleanup::prepare(input.positions, input.triangles)?;
    let uvs: Option<Vec<[[f32; 2]; 3]>> = input
        .uvs
        .map(|uvs| geometry.sources.iter().map(|&s| uvs[s as usize]).collect());
    // The polygons stay indexed by the cleaned triangles' positions, not by their sources: the
    // Editor's UV2 comes out that way when the cleanup reorders triangles.
    let desc = mesh::Triangles {
        positions: &geometry.positions,
        triangles: &geometry.triangles,
        sources: Some(input.polygons),
        uvs: uvs.as_deref(),
    };
    let map = mesh::HalfEdgeMesh::create(&desc, params.hard_angle)?;
    let mut charts = Vec::new();
    for component in map.connected_components() {
        parametrize(&map, component, params, &mut charts)?;
    }
    let (fit, _) = pack::pack(&mut charts, params.pack_margin);
    if !fit {
        return Err("the charts do not fit into the lightmap UV square".into());
    }
    let mut out = vec![[[0f32; 2]; 3]; input.triangles.len()];
    for chart in &charts {
        for (i, &facet) in chart.component.facets.iter().enumerate() {
            let source = geometry.sources[facet as usize];
            out[source as usize] = [0, 1, 2].map(|k| chart.uvs[3 * i + k].map(|c| c as f32));
        }
    }
    if !geometry.removed.is_empty() {
        let edges = KeptEdges::new(input, &geometry.sources);
        for &removed in &geometry.removed {
            out[removed as usize] =
                removed_triangle_uvs(input, &geometry.sources, &edges, removed, &out);
        }
    }
    Ok(out)
}

/// The kept triangles along each edge of the input (by the input's vertex numbers): per edge,
/// the first kept triangle in the cleanup's order that has it.
struct KeptEdges {
    /// Per undirected edge, the index into the cleanup's `sources` of the first triangle with it.
    first: std::collections::HashMap<(u32, u32), u32>,
}

impl KeptEdges {
    fn new(input: &UnwrapInput, sources: &[u32]) -> Self {
        let mut first = std::collections::HashMap::with_capacity(sources.len() * 3 / 2);
        for (order, &kept) in sources.iter().enumerate() {
            let s = input.triangles[kept as usize];
            for k in 0..3 {
                let (a, b) = (s[k], s[(k + 1) % 3]);
                first.entry((a.min(b), a.max(b))).or_insert(order as u32);
            }
        }
        Self { first }
    }

    fn first(&self, a: u32, b: u32) -> Option<u32> {
        self.first.get(&(a.min(b), a.max(b))).copied()
    }
}

/// The UV2 of a triangle the cleanup removed: that of the first kept triangle sharing one of its
/// edges (by the input's vertex numbers; of several edges of that triangle, the removed
/// triangle's first), the third corner taking the UV2 of the edge's end it lies nearer to; zero
/// when no kept triangle shares an edge.
fn removed_triangle_uvs(
    input: &UnwrapInput,
    sources: &[u32],
    edges: &KeptEdges,
    removed: u32,
    out: &[[[f32; 2]; 3]],
) -> [[f32; 2]; 3] {
    const EDGES: [(usize, usize, usize); 3] = [(0, 1, 2), (1, 2, 0), (2, 0, 1)];
    let r = input.triangles[removed as usize];
    let Some((order, e)) = EDGES
        .iter()
        .enumerate()
        .filter(|(_, &(a, b, _))| r[a] != r[b])
        .filter_map(|(e, &(a, b, _))| Some((edges.first(r[a], r[b])?, e)))
        .min()
    else {
        return [[0.0; 2]; 3];
    };
    let kept = sources[order as usize];
    let s = input.triangles[kept as usize];
    let (a, b, opposite) = EDGES[e];
    let corner_of = |v: u32| {
        s.iter()
            .position(|&c| c == v)
            .expect("the edge is the triangle's")
    };
    let (ca, cb) = (corner_of(r[a]), corner_of(r[b]));
    let p = r.map(|v| input.positions[v as usize]);
    let length2 = |i: usize, j: usize| {
        let d = [0, 1, 2].map(|c| p[i][c] - p[j][c]);
        d[2] * d[2] + d[1] * d[1] + d[0] * d[0]
    };
    let lengths = [length2(0, 1), length2(1, 2), length2(2, 0)];
    let mut corner = [0usize; 3];
    corner[a] = ca;
    corner[b] = cb;
    corner[opposite] = if lengths[(e + 2) % 3] < lengths[(e + 1) % 3] {
        ca
    } else {
        cb
    };
    corner.map(|c| out[kept as usize][c])
}

/// The charts of one connected component: its segments laid flat, those too distorted
/// segmented further.
fn parametrize(
    map: &mesh::HalfEdgeMesh,
    component: mesh::Component,
    params: &UnwrapParams,
    charts: &mut Vec<param::Chart>,
) -> Result<(), String> {
    let segments = if component.facets.len() > 100 {
        segment_large(map, &component)?
    } else {
        segment::segment(map, &component, None)?
    };
    // Pieces cut from a chart that overlapped itself are marked: one that overlaps itself again
    // is segmented further instead.
    let mut work: Vec<(mesh::Component, bool)> = segments.into_iter().map(|c| (c, false)).collect();
    let mut accepted = Vec::new();
    let mut i = 0;
    while i < work.len() {
        let (piece, cut) = work[i].clone();
        i += 1;
        let mut p = param::Parametrization::new(map, &piece)?;
        p.angle_system();
        p.solve_angles();
        let (angular, _) = p.angular_metrics();
        if params.angle_error <= angular {
            let pieces = segment::Segmentation::force(map, &piece)?;
            work.extend(pieces.into_iter().map(|c| (c, false)));
            continue;
        }
        p.lay_flat();
        let chart = p.chart();
        if chart.component.facets.len() == 1 {
            accepted.push(chart);
            continue;
        }
        let overlapping = param::overlapping(&chart);
        if !overlapping.is_empty() && !cut {
            let facets: Vec<u32> = overlapping.iter().map(|&k| piece.facets[k]).collect();
            let mut rest = piece.facets.clone();
            rest.retain(|f| facets.binary_search(f).is_err());
            let pieces = map.components_of(&facets);
            work.extend(pieces.into_iter().map(|c| (c, true)));
            work.extend(map.components_of(&rest).into_iter().map(|c| (c, true)));
            continue;
        }
        let m = chart.metrics(map);
        if !overlapping.is_empty() || params.angle_error <= m.0[0] || params.area_error <= m.0[2] {
            let pieces = segment::Segmentation::force(map, &piece)?;
            work.extend(pieces.into_iter().map(|c| (c, false)));
        } else {
            accepted.push(chart);
        }
    }
    charts.extend(accepted);
    Ok(())
}

/// The segmentation of a component of more than 100 facets: the component is simplified to a
/// tenth of its facets and segmented; the simplification is undone in three steps (two of a
/// third of the collapses left, the last undoing the rest), each segmented again from the
/// charts carried over.
///
/// The coarse meshes' facets are numbered by their position in the component and, like the
/// charts carried over, used as facet numbers of the whole mesh: the Editor's UV2 comes out the
/// same way when the large part is not the mesh's first (measured: Compound and Twins in
/// tests/projects/lightmapuvs).
fn segment_large(
    map: &mesh::HalfEdgeMesh,
    component: &mesh::Component,
) -> Result<Vec<mesh::Component>, String> {
    let n = component.facets.len();
    let sub = map.sub_triangles(component)?;
    let mut simplified = simplify::Simplification::new(&sub.positions, &sub.triangles, &sub.uvs)?;
    let target = (n as f32 * 0.1f32) as i64 as usize;
    let coarse = if target == 0 {
        simplified.current()
    } else {
        simplified.with_facets(target)
    };
    let mut charts = segment::segment(map, &mesh::Component { facets: coarse }, None)?;
    let step = simplified.applied() / 3;
    let mut labels = vec![-1i32; n];
    let mut count;
    let mut round = 0;
    loop {
        count = charts.len();
        labels.fill(-1);
        for (i, chart) in charts.iter().enumerate() {
            for &f in &chart.facets {
                *labels
                    .get_mut(f as usize)
                    .ok_or("a coarse facet is outside the part")? = i as i32;
            }
        }
        if round == 2 {
            break;
        }
        simplified.refine(&mut labels, step);
        let finer = mesh::Component {
            facets: simplified.current(),
        };
        charts.extend(segment::segment(map, &finer, Some((count, &labels)))?);
        round += 1;
    }
    simplified.refine(&mut labels, usize::MAX);
    segment::segment(map, component, Some((count, &labels)))
}

#[cfg(test)]
mod tests;

#[cfg(test)]
mod margin_tests {
    use super::*;

    fn settings(margin_method: i64) -> SecondaryUvSettings {
        SecondaryUvSettings {
            hard_angle: 88.0,
            angle_distortion: 8.0,
            area_distortion: 15.0,
            margin_method,
            pack_margin: 4.0,
            min_lightmap_resolution: 40.0,
            min_object_scale: 1.0,
        }
    }

    #[test]
    fn margin_is_manual_pixels_or_calculated_from_the_area() {
        // A unit square: area 1.
        let positions = [
            [0.0, 0.0, 0.0],
            [1.0, 0.0, 0.0],
            [1.0, 1.0, 0.0],
            [0.0, 1.0, 0.0],
        ];
        let triangles = [[0, 1, 2], [0, 2, 3]];
        let input = UnwrapInput {
            positions: &positions,
            triangles: &triangles,
            polygons: &[0, 1],
            uvs: None,
        };
        let manual = UnwrapParams::new(&settings(0), &input).unwrap();
        assert_eq!(manual.pack_margin, 4.0 / 1024.0);
        assert_eq!((manual.angle_error, manual.area_error), (0.08, 0.15));
        assert_eq!(manual.hard_angle, 88.0);
        // 3 / (scale × resolution × √area), at least 3e-4.
        let calculated = UnwrapParams::new(&settings(1), &input).unwrap();
        assert_eq!(calculated.pack_margin, 1.0 / 40.0 * 3.0);
        let mut large = settings(1);
        large.min_lightmap_resolution = 1e6;
        let floor = UnwrapParams::new(&large, &input).unwrap();
        assert_eq!(floor.pack_margin, 0.0001 * 3.0);
    }
}
