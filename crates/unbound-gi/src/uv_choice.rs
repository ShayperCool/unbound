//! Whether a chart left on its own keeps its mesh's lightmap UVs or takes its projection, as the
//! Unity 6000.0.71f1 Editor's precompute chooses for a renderer with Optimize Realtime UVs
//! (tests/projects/realtimegi RoomCurved; docs/REALTIME_GI.md, Automatic charts).
//!
//! Two measures compare the two UV sets. The overlap: the chart's triangles drawn into a square
//! grid over the larger side of their UV bounds, each cell taking the area of every triangle
//! that covers it; the area covered more than once over the area covered at all. The mapping
//! error: over the triangles, the triangle's area times the sum of the squared differences
//! between its corners' angles in space and in the UVs. The UVs with clearly less overlap win;
//! within that margin, those with the smaller mapping error, the projection on a tie.

/// Cells per side of the overlap grid.
const OVERLAP_CELLS: usize = 256;
/// The share of the grid the larger side of the UV bounds spans.
const OVERLAP_SPAN: f32 = 0.999;
/// A difference in overlap that decides alone.
pub(crate) const OVERLAP_MARGIN: f32 = 0.25;

/// One chart's UVs measured: its overlap and its mapping error.
#[derive(Debug, Clone, Copy, PartialEq)]
pub(crate) struct Measure {
    pub overlap: f32,
    pub error: f64,
}

/// Which UVs a chart keeps.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum Pick {
    Projected,
    Own,
}

/// The precompute's choice between the projection and the mesh's own UVs. A NaN overlap (UVs of
/// extent but no area) compares as neither more nor less, and the projection is kept.
pub(crate) fn pick(projected: Measure, own: Measure) -> Pick {
    let difference = projected.overlap - own.overlap;
    if OVERLAP_MARGIN <= difference {
        Pick::Own
    } else if difference.abs() < OVERLAP_MARGIN && own.error < projected.error {
        Pick::Own
    } else {
        Pick::Projected
    }
}

/// Scratch space of the overlap measure, reused between charts.
pub(crate) struct Grid {
    cells: Vec<f32>,
}

impl Grid {
    pub(crate) fn new() -> Grid {
        Grid {
            cells: vec![0.0; OVERLAP_CELLS * OVERLAP_CELLS],
        }
    }

    /// The overlap of the triangles' UVs: the area covered more than once over the area
    /// covered. UVs without extent overlap fully (1).
    pub(crate) fn overlap(&mut self, uvs: &[[f32; 2]], triangles: &[[u32; 3]]) -> f32 {
        let (mut lo, mut hi) = ([f32::MAX; 2], [-f32::MAX; 2]);
        for t in triangles {
            for &v in t {
                let p = uvs[v as usize];
                for k in 0..2 {
                    lo[k] = lo[k].min(p[k]);
                    hi[k] = hi[k].max(p[k]);
                }
            }
        }
        let extent = (hi[1] - lo[1]).max(hi[0] - lo[0]);
        if triangles.is_empty() || extent == 0.0 {
            return 1.0;
        }
        let scale = (1.0 / extent) * OVERLAP_CELLS as f32 * OVERLAP_SPAN;
        self.cells.fill(0.0);
        for t in triangles {
            let corners = t.map(|v| {
                let p = uvs[v as usize];
                [
                    ((p[0] - lo[0]) * scale) as f64,
                    ((p[1] - lo[1]) * scale) as f64,
                ]
            });
            self.draw(corners);
        }
        let (mut covered, mut over) = (0.0f32, 0.0f32);
        for &c in &self.cells {
            if c != 0.0 {
                covered += c.min(1.0);
                over += (c - 1.0).max(0.0);
            }
        }
        over / covered
    }

    /// Adds to each cell the area of the triangle within it.
    fn draw(&mut self, corners: [[f64; 2]; 3]) {
        let last = (OVERLAP_CELLS - 1) as f64;
        let low = |k: usize| corners.iter().map(|p| p[k]).fold(f64::MAX, f64::min);
        let high = |k: usize| corners.iter().map(|p| p[k]).fold(f64::MIN, f64::max);
        let rows = (low(1).floor().max(0.0) as usize)..=(high(1).floor().min(last) as usize);
        let columns = (low(0).floor().max(0.0) as usize)..=(high(0).floor().min(last) as usize);
        let mut band = Vec::with_capacity(8);
        let mut cell = Vec::with_capacity(12);
        for y in rows {
            band.clear();
            band.extend_from_slice(&corners);
            clip(&mut band, 1, y as f64, y as f64 + 1.0);
            if band.len() < 3 {
                continue;
            }
            for x in columns.clone() {
                cell.clear();
                cell.extend_from_slice(&band);
                clip(&mut cell, 0, x as f64, x as f64 + 1.0);
                let area = polygon_area(&cell);
                if area > 0.0 {
                    self.cells[y * OVERLAP_CELLS + x] += area as f32;
                }
            }
        }
    }
}

/// Cuts a convex polygon to `low <= p[axis] <= high`.
fn clip(polygon: &mut Vec<[f64; 2]>, axis: usize, low: f64, high: f64) {
    for (bound, keep_below) in [(low, false), (high, true)] {
        let inside = |p: &[f64; 2]| {
            if keep_below {
                p[axis] <= bound
            } else {
                p[axis] >= bound
            }
        };
        let input = std::mem::take(polygon);
        for (i, p) in input.iter().enumerate() {
            let q = &input[(i + 1) % input.len()];
            if inside(p) {
                polygon.push(*p);
            }
            if inside(p) != inside(q) {
                let t = (bound - p[axis]) / (q[axis] - p[axis]);
                let mut cut = [p[0] + t * (q[0] - p[0]), p[1] + t * (q[1] - p[1])];
                cut[axis] = bound;
                polygon.push(cut);
            }
        }
    }
}

/// The unsigned area of a polygon.
fn polygon_area(polygon: &[[f64; 2]]) -> f64 {
    let mut twice = 0.0;
    for (i, p) in polygon.iter().enumerate() {
        let q = polygon[(i + 1) % polygon.len()];
        twice += p[0] * q[1] - q[0] * p[1];
    }
    0.5 * twice.abs()
}

type V3 = [f32; 3];

/// The direction of a vector (its length taken out), or the vector itself when it is too short
/// for that.
fn direction<const N: usize>(a: [f32; N]) -> [f32; N] {
    let length2 = sum(a.map(|x| x * x));
    if length2 > f32::MIN_POSITIVE {
        let scale = 1.0 / length2.sqrt();
        a.map(|x| x * scale)
    } else {
        a
    }
}

/// A sum of three terms as (x + z) + y, of two as x + y.
fn sum<const N: usize>(a: [f32; N]) -> f32 {
    match N {
        3 => (a[0] + a[2]) + a[1],
        _ => a.iter().sum(),
    }
}

fn dot<const N: usize>(a: [f32; N], b: [f32; N]) -> f32 {
    let mut p = a;
    for k in 0..N {
        p[k] = a[k] * b[k];
    }
    sum(p)
}

/// The angle between two directions, from their cosine held to [-1, 1].
fn angle_of(cosine: f32) -> f32 {
    if cosine > 1.0 {
        1.0f32
    } else {
        cosine.max(-1.0)
    }
    .acos()
}

/// A triangle's corner angles from its edges (corner 1 to 0, 2 to 0, 2 to 1): the first two
/// measured, the third what is left of a half turn; all zero when an edge has no length.
fn corner_angles<const N: usize>(e1: [f32; N], e2: [f32; N], e3: [f32; N]) -> [f32; 3] {
    if [e1, e2, e3]
        .iter()
        .any(|e| sum(e.map(|x| x * x)).sqrt() == 0.0)
    {
        return [0.0; 3];
    }
    let (d1, d2, d3) = (direction(e1), direction(e2), direction(e3));
    let first = angle_of(dot(d1, d2));
    let second = std::f32::consts::PI - angle_of(dot(d1, d3));
    [first, second, (std::f32::consts::PI - first) - second]
}

fn sub<const N: usize>(a: [f32; N], b: [f32; N]) -> [f32; N] {
    let mut d = a;
    for k in 0..N {
        d[k] = a[k] - b[k];
    }
    d
}

/// The mapping error of `uvs` over the triangles of `positions`.
pub(crate) fn mapping_error(positions: &[V3], uvs: &[[f32; 2]], triangles: &[[u32; 3]]) -> f64 {
    let mut error = 0.0f64;
    for t in triangles {
        let [a, b, c] = t.map(|v| v as usize);
        let (e1, e2, e3) = (
            sub(positions[b], positions[a]),
            sub(positions[c], positions[a]),
            sub(positions[c], positions[b]),
        );
        let n = [
            e1[1] * e2[2] - e1[2] * e2[1],
            e1[2] * e2[0] - e1[0] * e2[2],
            e1[0] * e2[1] - e1[1] * e2[0],
        ];
        let area = 0.5 * sum(n.map(|x| x * x)).sqrt() as f64;
        let space = corner_angles(e1, e2, e3);
        let flat = corner_angles(
            sub(uvs[b], uvs[a]),
            sub(uvs[c], uvs[a]),
            sub(uvs[c], uvs[b]),
        );
        let d = [0, 1, 2].map(|k| space[k] - flat[k]);
        error += area * ((d[0] * d[0] + d[1] * d[1]) + d[2] * d[2]) as f64;
    }
    error
}

#[cfg(test)]
mod tests {
    use super::*;

    const QUAD: [[u32; 3]; 2] = [[0, 1, 2], [0, 2, 3]];

    #[test]
    fn a_square_covers_without_overlap() {
        let uvs = [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]];
        let mut grid = Grid::new();
        assert_eq!(grid.overlap(&uvs, &QUAD), 0.0);
    }

    /// The cells of the last row and column are covered in part (the square spans 255.744 of
    /// 256): each counts once as covered and only its covered part as overlap.
    fn edge_cells(twice_covered: f64) -> f64 {
        let part = 255.744f64 - 255.0;
        let over =
            255.0 * 255.0 + 2.0 * 255.0 * (2.0 * part - 1.0) + (2.0 * part * part - 1.0).max(0.0);
        over * twice_covered / (256.0 * 256.0)
    }

    #[test]
    fn a_square_drawn_twice_overlaps_fully() {
        let uvs = [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]];
        let triangles = [QUAD[0], QUAD[1], QUAD[0], QUAD[1]];
        let mut grid = Grid::new();
        let overlap = grid.overlap(&uvs, &triangles) as f64;
        assert!((overlap - edge_cells(1.0)).abs() < 1e-4, "{overlap}");
        assert!(overlap > 0.99);
    }

    #[test]
    fn half_a_square_drawn_again_overlaps_a_half() {
        // A square of two triangles and one of them again, its area half the square's.
        let uvs = [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]];
        let triangles = [QUAD[0], QUAD[1], QUAD[0]];
        let mut grid = Grid::new();
        let overlap = grid.overlap(&uvs, &triangles);
        // (Cells along the diagonal and the partly covered edge cells take a little less.)
        assert!((overlap - 0.5).abs() < 2e-3, "{overlap}");
    }

    #[test]
    fn uvs_without_extent_overlap_fully_and_uvs_without_area_measure_nan() {
        let mut grid = Grid::new();
        assert_eq!(grid.overlap(&[[0.5, 0.5]; 3], &[[0, 1, 2]]), 1.0);
        assert!(
            grid.overlap(&[[0.0, 0.0], [0.5, 0.0], [1.0, 0.0]], &[[0, 1, 2]])
                .is_nan()
        );
    }

    #[test]
    fn a_true_copy_has_no_mapping_error_and_a_stretched_one_has() {
        let positions = [
            [0.0, 0.0, 0.0],
            [2.0, 0.0, 0.0],
            [2.0, 0.0, 1.0],
            [0.0, 0.0, 1.0],
        ];
        let true_copy = [[0.0, 0.0], [0.5, 0.0], [0.5, 0.25], [0.0, 0.25]];
        let stretched = [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]];
        let copy = mapping_error(&positions, &true_copy, &QUAD);
        assert!(copy < 1e-12, "{copy}");
        // Each triangle's angles: 90, 26.57 and 63.43 degrees against 90, 45 and 45.
        let d = (45.0f64 - 26.565_05).to_radians();
        let expected = 2.0 * 1.0 * (2.0 * d * d);
        let error = mapping_error(&positions, &stretched, &QUAD);
        assert!((error - expected).abs() < 1e-5, "{error} {expected}");
    }

    #[test]
    fn a_triangle_whose_uvs_have_a_point_counts_all_its_angles() {
        let positions = [[0.0, 0.0, 0.0], [1.0, 0.0, 0.0], [0.0, 1.0, 0.0]];
        let error = mapping_error(&positions, &[[0.3, 0.3]; 3], &[[0, 1, 2]]);
        let right = std::f64::consts::FRAC_PI_2;
        let quarter = std::f64::consts::FRAC_PI_4;
        let expected = 0.5 * (quarter * quarter + right * right + quarter * quarter);
        assert!((error - expected).abs() < 1e-5, "{error} {expected}");
    }

    #[test]
    fn overlap_decides_then_the_mapping_error_then_the_projection() {
        let m = |overlap, error| Measure { overlap, error };
        assert_eq!(pick(m(0.3, 0.0), m(0.0, 1.0)), Pick::Own);
        assert_eq!(pick(m(0.25, 0.0), m(0.0, 1.0)), Pick::Own);
        assert_eq!(pick(m(0.0, 1.0), m(0.3, 0.0)), Pick::Projected);
        assert_eq!(pick(m(0.0, 1.0), m(0.2, 0.5)), Pick::Own);
        assert_eq!(pick(m(0.0, 0.5), m(0.0, 0.5)), Pick::Projected);
        assert_eq!(pick(m(f32::NAN, 1.0), m(0.0, 0.0)), Pick::Projected);
    }
}
