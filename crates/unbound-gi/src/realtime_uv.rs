//! Realtime lightmap UVs: where each vertex of a mesh instance lands in its realtime lightmap, as
//! the Unity 6000.0.71f1 Editor's precompute lays them out for renderers with Preserve UVs
//! (tests/projects/realtimegi; docs/REALTIME_GI.md). The mesh's lightmap UV charts keep their
//! shapes; each is scaled to its own world area at the realtime resolution, snapped to whole
//! blocks of texels (at least the minimum chart size) and packed into the instance's UV space,
//! whose size in texels the scene's precomputed data also records.
use crate::data::{NO_CHART, System};
use anyhow::{Result, bail, ensure};

/// Texels per packing block.
pub const BLOCK_TEXELS: u32 = 2;
/// The largest instance UV space, in texels per side.
pub const MAX_TEXELS: u32 = 512;
/// A chart's border, in blocks, on top of its own extent: half a block on each side less a
/// small margin, so its texel centres stay inside the chart.
const CHART_BORDER_MARGIN: f64 = 1e-4;
/// The offset of a chart from its mask's origin, in blocks (half a texel, plus a margin that
/// the precompute keeps as a single-precision value).
const RASTER_BORDER_MARGIN: f64 = 9.999_999_747_378_752e-6;
/// Single-precision epsilon, added to a chart's offset from its mask's origin.
const RASTER_ORIGIN_EPSILON: f64 = 1.192_092_895_507_812_5e-7;
/// The rasterization bloat is the chart border less this.
const BLOAT_MARGIN: f64 = 1e-4;

/// One mesh instance's input: world-space positions (the instance's scale applied), its lightmap
/// UVs and triangles.
pub struct Input<'a> {
    pub positions: &'a [[f32; 3]],
    pub uvs: &'a [[f32; 2]],
    pub triangles: &'a [[u32; 3]],
    /// World units per texel: 1 / (realtime resolution * the renderer's scale in lightmap).
    pub pixel_size: f32,
    /// The renderer's minimum chart size, in texels.
    pub min_chart_texels: u32,
}

/// The instance's UV space (`width` x `height` texels) and each vertex's position in it (0..1);
/// vertices no triangle uses stay at 0. `charts`: each chart's rectangle in texels (x, y, width,
/// height), in chart order.
#[derive(Debug, Clone, PartialEq)]
pub struct Output {
    pub width: u32,
    pub height: u32,
    pub uvs: Vec<[f32; 2]>,
    pub charts: Vec<[u32; 4]>,
}

/// A packing mask: `width * height` cells, row-major from the bottom row; `true` is used.
#[derive(Debug, Clone)]
struct Mask {
    width: usize,
    height: usize,
    cells: Vec<bool>,
}

impl Mask {
    fn new(width: usize, height: usize) -> Self {
        Self {
            width,
            height,
            cells: vec![false; width * height],
        }
    }
    fn get(&self, x: usize, y: usize) -> bool {
        self.cells[y * self.width + x]
    }
    fn usage(&self) -> usize {
        self.cells.iter().filter(|&&c| c).count()
    }
}

/// A chart: its triangles (indices into the input) and the affine map from lightmap UV to
/// blocks within its mask: `uv * scale + offset`.
struct Chart {
    triangles: Vec<usize>,
    scale: [f64; 2],
    offset: [f64; 2],
    mask: Mask,
}

/// Union-find over vertices.
fn find(parent: &mut [usize], mut v: usize) -> usize {
    while parent[v] != v {
        parent[v] = parent[parent[v]];
        v = parent[v];
    }
    v
}

/// Triangles grouped into charts: triangles sharing a vertex (the same index, or the same
/// position and UV) belong to one chart. Charts are ordered by their first triangle.
fn charts(input: &Input) -> Vec<Vec<usize>> {
    let n = input.positions.len();
    let mut parent: Vec<usize> = (0..n).collect();
    // Vertices with the same position and UV are one vertex.
    let mut order: Vec<usize> = (0..n).collect();
    let key = |v: usize| {
        let p = input.positions[v];
        let t = input.uvs[v];
        [p[0], p[1], p[2], t[0], t[1]].map(f32::to_bits)
    };
    order.sort_by_key(|&v| key(v));
    for pair in order.windows(2) {
        if key(pair[0]) == key(pair[1]) {
            let (a, b) = (find(&mut parent, pair[0]), find(&mut parent, pair[1]));
            parent[a.max(b)] = a.min(b);
        }
    }
    for t in input.triangles {
        let a = find(&mut parent, t[0] as usize);
        for &v in &t[1..] {
            let b = find(&mut parent, v as usize);
            if a != b {
                let (lo, hi) = (a.min(b), a.max(b));
                parent[hi] = lo;
            }
        }
    }
    let mut chart_of_root = std::collections::HashMap::new();
    let mut out: Vec<Vec<usize>> = Vec::new();
    for (i, t) in input.triangles.iter().enumerate() {
        let root = find(&mut parent, t[0] as usize);
        let index = *chart_of_root.entry(root).or_insert_with(|| {
            out.push(Vec::new());
            out.len() - 1
        });
        out[index].push(i);
    }
    out
}

fn cross2(a: [f64; 2], b: [f64; 2], c: [f64; 2]) -> f64 {
    (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
}

/// Whether the closed axis-aligned cell `[x, x+1] x [y, y+1]` meets the closed convex polygon.
fn cell_meets(polygon: &[[f64; 2]], x: f64, y: f64) -> bool {
    let corners = [[x, y], [x + 1.0, y], [x + 1.0, y + 1.0], [x, y + 1.0]];
    let (min, max) = polygon
        .iter()
        .fold(([f64::MAX; 2], [f64::MIN; 2]), |(mn, mx), p| {
            (
                [mn[0].min(p[0]), mn[1].min(p[1])],
                [mx[0].max(p[0]), mx[1].max(p[1])],
            )
        });
    if max[0] < x || min[0] > x + 1.0 || max[1] < y || min[1] > y + 1.0 {
        return false;
    }
    // Separating axes: the polygon's edge normals (the cell's are the box test above).
    let count = polygon.len();
    let area: f64 = (0..count)
        .map(|i| {
            let (p, q) = (polygon[i], polygon[(i + 1) % count]);
            p[0] * q[1] - q[0] * p[1]
        })
        .sum();
    for i in 0..count {
        let (p, q) = (polygon[i], polygon[(i + 1) % count]);
        // Outside of edge p -> q (for counter-clockwise polygons, to the right).
        let outside = |c: &[f64; 2]| {
            let s = cross2(p, q, *c);
            if area >= 0.0 { s < 0.0 } else { s > 0.0 }
        };
        if corners.iter().all(outside) {
            return false;
        }
    }
    true
}

/// The convex hull (counter-clockwise) of points.
fn hull(mut points: Vec<[f64; 2]>) -> Vec<[f64; 2]> {
    points.sort_by(|a, b| a[0].total_cmp(&b[0]).then(a[1].total_cmp(&b[1])));
    points.dedup();
    if points.len() < 3 {
        return points;
    }
    let mut lower: Vec<[f64; 2]> = Vec::new();
    for &p in &points {
        while lower.len() >= 2 && cross2(lower[lower.len() - 2], lower[lower.len() - 1], p) <= 0.0 {
            lower.pop();
        }
        lower.push(p);
    }
    let mut upper: Vec<[f64; 2]> = Vec::new();
    for &p in points.iter().rev() {
        while upper.len() >= 2 && cross2(upper[upper.len() - 2], upper[upper.len() - 1], p) <= 0.0 {
            upper.pop();
        }
        upper.push(p);
    }
    lower.pop();
    upper.pop();
    lower.extend(upper);
    lower
}

impl Chart {
    /// The chart's triangles in blocks within its mask.
    fn placed(&self, input: &Input, t: usize) -> [[f64; 2]; 3] {
        input.triangles[t].map(|v| {
            let uv = input.uvs[v as usize];
            [0, 1].map(|k| uv[k] as f64 * self.scale[k] + self.offset[k])
        })
    }
}

/// Builds a chart: scaled to its world area, snapped to whole blocks, moved to its mask's origin
/// and rasterized into the mask (each triangle grown by the border, every cell it touches).
fn build_chart(input: &Input, triangles: Vec<usize>) -> Result<Chart> {
    let block = BLOCK_TEXELS as f64;
    let (mut world, mut uv_area) = (0.0f64, 0.0f64);
    let (mut lo, mut hi) = ([f64::MAX; 2], [f64::MIN; 2]);
    for &t in &triangles {
        let [a, b, c] = input.triangles[t].map(|v| v as usize);
        let p = [a, b, c].map(|v| input.positions[v].map(|x| x as f64));
        let e1 = [0, 1, 2].map(|k| p[1][k] - p[0][k]);
        let e2 = [0, 1, 2].map(|k| p[2][k] - p[0][k]);
        let n = [
            e1[1] * e2[2] - e1[2] * e2[1],
            e1[2] * e2[0] - e1[0] * e2[2],
            e1[0] * e2[1] - e1[1] * e2[0],
        ];
        world += 0.5 * (n[0] * n[0] + n[1] * n[1] + n[2] * n[2]).sqrt();
        let u = [a, b, c].map(|v| input.uvs[v].map(|x| x as f64));
        uv_area += 0.5 * cross2(u[0], u[1], u[2]).abs();
        for q in u {
            for k in 0..2 {
                lo[k] = lo[k].min(q[k]);
                hi[k] = hi[k].max(q[k]);
            }
        }
    }
    // Blocks per UV unit: the chart's world size over its UV size, in blocks of texels.
    let per_uv = if uv_area > 0.0 {
        (world / uv_area).sqrt() / (input.pixel_size as f64 * block)
    } else {
        0.0
    };
    let min_blocks = (input.min_chart_texels / BLOCK_TEXELS).max(1) as f64;
    let border = 1.0 / block;
    let mut scale = [0.0; 2];
    for k in 0..2 {
        let extent = (hi[k] - lo[k]) * per_uv;
        let blocks = (extent + border + CHART_BORDER_MARGIN)
            .ceil()
            .max(min_blocks);
        // Checked before its mask is made: a sliver of UV area on a large face would ask for a
        // huge one.
        ensure!(
            blocks <= (MAX_TEXELS / BLOCK_TEXELS) as f64,
            "a chart of {} texels, more than {MAX_TEXELS}",
            blocks * block
        );
        scale[k] = if extent > 0.0 {
            per_uv * ((blocks - border - CHART_BORDER_MARGIN) / extent)
        } else {
            per_uv
        };
    }
    let raster_border = 0.5 / block + RASTER_BORDER_MARGIN;
    let offset = [0, 1].map(|k| raster_border - lo[k] * scale[k] + RASTER_ORIGIN_EPSILON);
    let size = [0, 1].map(|k| ((hi[k] - lo[k]) * scale[k] + 2.0 * raster_border + 1.0) as usize);
    let mut chart = Chart {
        triangles,
        scale,
        offset,
        mask: Mask::new(size[0].max(1), size[1].max(1)),
    };
    let bloat = raster_border as f32 as f64 - BLOAT_MARGIN;
    for &t in &chart.triangles {
        let tri = chart.placed(input, t).map(|p| p.map(|x| x as f32 as f64));
        let polygon = hull(
            tri.iter()
                .flat_map(|p| {
                    [[1.0, 1.0], [-1.0, 1.0], [-1.0, -1.0], [1.0, -1.0]]
                        .map(|d: [f64; 2]| [p[0] + d[0] * bloat, p[1] + d[1] * bloat])
                })
                .collect(),
        );
        if polygon.is_empty() {
            continue;
        }
        let (min, max) = polygon
            .iter()
            .fold(([f64::MAX; 2], [f64::MIN; 2]), |(mn, mx), p| {
                (
                    [mn[0].min(p[0]), mn[1].min(p[1])],
                    [mx[0].max(p[0]), mx[1].max(p[1])],
                )
            });
        let mask = &mut chart.mask;
        let x0 = (min[0].floor().max(0.0)) as usize;
        let y0 = (min[1].floor().max(0.0)) as usize;
        let x1 = (max[0].floor() as isize).min(mask.width as isize - 1);
        let y1 = (max[1].floor() as isize).min(mask.height as isize - 1);
        for y in y0..=(y1.max(-1) as usize).min(mask.height - 1) {
            if (y as isize) > y1 {
                break;
            }
            for x in x0..=(x1.max(-1) as usize).min(mask.width - 1) {
                if (x as isize) > x1 {
                    break;
                }
                if !mask.cells[y * mask.width + x]
                    && (polygon.len() < 3 || cell_meets(&polygon, x as f64, y as f64))
                {
                    mask.cells[y * mask.width + x] = true;
                }
            }
        }
    }
    Ok(chart)
}

/// The two-skyline packing heuristic: each column's lowest free cell and each row's leftmost
/// free cell; a shape rests on the columns or against the rows where it wastes the least area.
struct Skylines {
    column_top: Vec<i32>,
    row_end: Vec<i32>,
}

/// A shape's profile: per row the empty cells before its first used one, per column the empty
/// cells below its lowest used one (the full size where the row or column is empty).
struct Profile {
    left: Vec<i32>,
    bottom: Vec<i32>,
}

fn profile(mask: &Mask) -> Profile {
    let (w, h) = (mask.width, mask.height);
    Profile {
        left: (0..h)
            .map(|y| (0..w).position(|x| mask.get(x, y)).unwrap_or(w) as i32)
            .collect(),
        bottom: (0..w)
            .map(|x| (0..h).position(|y| mask.get(x, y)).unwrap_or(h) as i32)
            .collect(),
    }
}

impl Skylines {
    fn new(width: usize, height: usize) -> Self {
        Self {
            column_top: vec![0; width],
            row_end: vec![0; height],
        }
    }

    /// The best position of `mask` in a `width` x `height` target: least wasted area below and
    /// to the left, then the lowest, then the leftmost.
    fn best(&self, mask: &Mask, width: usize, height: usize) -> Option<(usize, usize)> {
        let (w, h) = (mask.width as i32, mask.height as i32);
        let (tw, th) = (width as i32, height as i32);
        if h > th || w > tw {
            return None;
        }
        let p = profile(mask);
        let mut best: Option<(f32, i32, i32)> = None;
        let mut consider = |score: f32, x: i32, y: i32| {
            let better = match best {
                None => true,
                Some((s, bx, by)) => score < s || (score == s && (y < by || (y == by && x < bx))),
            };
            if better {
                best = Some((score, x, y));
            }
        };
        // Resting on the columns.
        for x in 0..=(tw - w) {
            let mut score = 0.0f32;
            let mut lowest = 0i32;
            let mut fits = true;
            for i in 0..w {
                let u = p.bottom[i as usize] - self.column_top[(x + i) as usize];
                if u < h - th {
                    fits = false;
                    break;
                }
                if u < lowest {
                    score += ((lowest - u) * i) as f32;
                    lowest = u;
                } else {
                    score += (u - lowest) as f32;
                }
            }
            if !fits {
                continue;
            }
            let y = lowest.abs();
            for r in 0..h {
                let end = self.row_end[(y + r) as usize];
                let need = p.left[r as usize] + x;
                if end < need {
                    score += (need - end) as f32;
                }
            }
            consider(score, x, y);
        }
        // Against the rows.
        for y in 0..=(th - h) {
            let mut score = 0.0f32;
            let mut lowest = 0i32;
            let mut fits = true;
            for i in 0..h {
                let u = p.left[i as usize] - self.row_end[(y + i) as usize];
                if u < w - tw {
                    fits = false;
                    break;
                }
                if u < lowest {
                    score += ((lowest - u) * i) as f32;
                    lowest = u;
                } else {
                    score += (u - lowest) as f32;
                }
            }
            if !fits {
                continue;
            }
            let x = lowest.abs();
            for c in 0..w {
                let top = self.column_top[(x + c) as usize];
                let need = p.bottom[c as usize] + y;
                if top < need {
                    score += (need - top) as f32;
                }
            }
            consider(score, x, y);
        }
        best.map(|(_, x, y)| (x as usize, y as usize))
    }

    fn place(&mut self, mask: &Mask, x: usize, y: usize) {
        for c in 0..mask.width {
            if let Some(top) = (0..mask.height).rev().find(|&r| mask.get(c, r)) {
                let t = &mut self.column_top[x + c];
                *t = (*t).max((y + top + 1) as i32);
            }
        }
        for r in 0..mask.height {
            if let Some(end) = (0..mask.width).rev().find(|&c| mask.get(c, r)) {
                let e = &mut self.row_end[y + r];
                *e = (*e).max((x + end + 1) as i32);
            }
        }
    }
}

/// Packs the masks, largest first (ties in chart order), into a target that starts at one
/// block and grows along its shorter side by the size of a shape that does not fit, until all
/// fit; then crops it to what is used. Returns each mask's position and the target's size.
fn pack(masks: &[&Mask]) -> Result<(Vec<(usize, usize)>, usize, usize)> {
    let max = (MAX_TEXELS / BLOCK_TEXELS) as usize;
    let mut order: Vec<usize> = (0..masks.len()).collect();
    let usage: Vec<usize> = masks.iter().map(|m| m.usage()).collect();
    order.sort_by(|&a, &b| usage[b].cmp(&usage[a]).then(a.cmp(&b)));
    let (mut width, mut height) = (1usize, 1usize);
    loop {
        let mut skylines = Skylines::new(width, height);
        let mut target = Mask::new(width, height);
        let mut positions = vec![(0, 0); masks.len()];
        let (mut need_w, mut need_h) = (0usize, 0usize);
        for &i in &order {
            let mask = masks[i];
            match skylines.best(mask, width, height) {
                Some((x, y)) => {
                    skylines.place(mask, x, y);
                    for (cy, cx) in
                        (0..mask.height).flat_map(|cy| (0..mask.width).map(move |cx| (cy, cx)))
                    {
                        if mask.get(cx, cy) {
                            target.cells[(y + cy) * width + x + cx] = true;
                        }
                    }
                    positions[i] = (x, y);
                }
                None => {
                    // The skylines found no place; where a hole they do not see would hold the
                    // shape, the precompute places it with another heuristic (not supported yet).
                    if fits_somewhere(&target, mask) {
                        bail!(
                            "a chart that only fits into a hole between others: its packing is not supported yet"
                        );
                    }
                    need_w = need_w.max(mask.width);
                    need_h = need_h.max(mask.height);
                }
            }
        }
        if need_w == 0 && need_h == 0 {
            let used_w = skylines
                .column_top
                .iter()
                .rposition(|&t| t > 0)
                .map_or(0, |c| c + 1);
            let used_h = skylines
                .row_end
                .iter()
                .rposition(|&e| e > 0)
                .map_or(0, |r| r + 1);
            return Ok((positions, used_w, used_h));
        }
        if width == max && height == max {
            bail!("the charts do not fit in {MAX_TEXELS}x{MAX_TEXELS} texels");
        }
        // Grow along the shorter side (height when square) by the rejected shape's size, at
        // least to it.
        let (mut dx, mut dy) = (0usize, 0usize);
        let (room_w, room_h) = (max - width, max - height);
        if room_h == 0 || need_h == 0 {
            if room_w == 0 || need_w == 0 {
                bail!("the charts do not fit in {MAX_TEXELS}x{MAX_TEXELS} texels");
            }
            dx = need_w.min(room_w);
        } else if room_w == 0 || need_w == 0 || width as f32 / height as f32 >= 1.0 {
            dy = need_h.min(room_h);
        } else {
            dx = need_w.min(room_w);
        }
        if width < need_w {
            dx = dx.max(need_w) - width;
        }
        if height < need_h {
            dy = dy.max(need_h) - height;
        }
        width = (width + dx).min(max);
        height = (height + dy).min(max);
    }
}

/// Whether `mask` fits on free cells of `target` anywhere.
fn fits_somewhere(target: &Mask, mask: &Mask) -> bool {
    if mask.width > target.width || mask.height > target.height {
        return false;
    }
    (0..=target.height - mask.height).any(|y| {
        (0..=target.width - mask.width).any(|x| {
            (0..mask.height).all(|cy| {
                (0..mask.width).all(|cx| !mask.get(cx, cy) || !target.get(x + cx, y + cy))
            })
        })
    })
}

/// The instance's realtime lightmap UVs.
pub fn realtime_uvs(input: &Input) -> Result<Output> {
    ensure!(
        input.uvs.len() == input.positions.len(),
        "{} UVs for {} vertices",
        input.uvs.len(),
        input.positions.len()
    );
    ensure!(
        input.pixel_size > 0.0 && input.pixel_size.is_finite(),
        "a realtime texel size of {}",
        input.pixel_size
    );
    if let Some(t) = input
        .triangles
        .iter()
        .find(|t| t.iter().any(|&v| v as usize >= input.positions.len()))
    {
        bail!(
            "a triangle {t:?} past the {} vertices",
            input.positions.len()
        );
    }
    if input
        .positions
        .iter()
        .flatten()
        .chain(input.uvs.iter().flatten())
        .any(|v| !v.is_finite())
    {
        bail!("a vertex with a non-finite position or lightmap UV");
    }
    let charts: Vec<Chart> = charts(input)
        .into_iter()
        .map(|t| build_chart(input, t))
        .collect::<Result<_>>()?;
    let masks: Vec<&Mask> = charts.iter().map(|c| &c.mask).collect();
    let (positions, width, height) = pack(&masks)?;
    let mut uvs = vec![[0.0f32; 2]; input.positions.len()];
    for (chart, &(x, y)) in charts.iter().zip(&positions) {
        for &t in &chart.triangles {
            for &v in &input.triangles[t] {
                let uv = input.uvs[v as usize];
                uvs[v as usize] = [
                    ((uv[0] as f64 * chart.scale[0] + chart.offset[0] + x as f64) / width as f64)
                        as f32,
                    ((uv[1] as f64 * chart.scale[1] + chart.offset[1] + y as f64) / height as f64)
                        as f32,
                ];
            }
        }
    }
    let charts = charts
        .iter()
        .zip(&positions)
        .map(|(c, &(x, y))| [x, y, c.mask.width, c.mask.height].map(|v| v as u32 * BLOCK_TEXELS))
        .collect();
    Ok(Output {
        width: width as u32 * BLOCK_TEXELS,
        height: height as u32 * BLOCK_TEXELS,
        uvs,
        charts,
    })
}

/// Checks generated UVs against a system's precomputed layout: the instance's UV space has the
/// size its scale and offset in the system give it (`st`, `dynamicLightmapSTInSystem`), and each
/// chart covers texels of one precomputed chart of its own. A difference means Unbound would draw
/// the system's lightmap at other texels than the precompute lit.
pub fn check_layout(system: &System, st: [f32; 4], output: &Output) -> Result<()> {
    let (sw, sh) = (system.transport.width, system.transport.height);
    let size = [st[0] * sw as f32, st[1] * sh as f32].map(|v| v.round() as i64);
    let origin = [st[2] * sw as f32, st[3] * sh as f32].map(|v| v.round() as i64);
    ensure!(
        size == [output.width as i64, output.height as i64],
        "realtime lightmap UVs of {}x{} texels where the precomputed data has {}x{}",
        output.width,
        output.height,
        size[0],
        size[1]
    );
    ensure!(
        origin[0] >= 0
            && origin[1] >= 0
            && origin[0] + size[0] <= sw as i64
            && origin[1] + size[1] <= sh as i64,
        "an instance outside its {sw}x{sh} system"
    );
    let mut seen = Vec::with_capacity(output.charts.len());
    for chart in &output.charts {
        let mut id = None;
        for y in chart[1]..chart[1] + chart[3] {
            for x in chart[0]..chart[0] + chart[2] {
                let texel = (origin[1] as u32 + y) * sw + origin[0] as u32 + x;
                let found = system.chart_mask[texel as usize];
                ensure!(
                    found != NO_CHART && id.is_none_or(|i| i == found),
                    "a realtime lightmap chart at {chart:?} over texels the precompute did not lay out for it"
                );
                id = Some(found);
            }
        }
        if let Some(id) = id {
            ensure!(!seen.contains(&id), "two charts over one precomputed chart");
            seen.push(id);
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn mask(width: usize, height: usize, used: impl Fn(usize, usize) -> bool) -> Mask {
        let mut m = Mask::new(width, height);
        for y in 0..height {
            for x in 0..width {
                m.cells[y * width + x] = used(x, y);
            }
        }
        m
    }

    #[test]
    fn packing_grows_the_shorter_side_and_crops() {
        // Four 2x2 blocks: the target grows from one block to 2x2, then by the rejected shape's
        // size along its shorter side (height when square), and ends cropped.
        let square = mask(2, 2, |_, _| true);
        let (positions, w, h) = pack(&[&square, &square, &square, &square]).unwrap();
        assert_eq!(positions, vec![(0, 0), (2, 0), (0, 2), (2, 2)]);
        assert_eq!((w, h), (4, 4));
        // Larger shapes first, ties in order.
        let big = mask(3, 3, |_, _| true);
        let (positions, _, _) = pack(&[&square, &big]).unwrap();
        assert_eq!(positions[1], (0, 0));
    }

    #[test]
    fn a_shape_only_a_hole_holds_fails_loudly() {
        // A ring with a 3x3 hole the skylines cannot reach, then a block that fits only there.
        let ring = mask(5, 5, |x, y| x == 0 || y == 0 || x == 4 || y == 4);
        let small = mask(2, 2, |_, _| true);
        let error = pack(&[&ring, &small]).unwrap_err().to_string();
        assert!(error.contains("hole"), "{error}");
    }
}
