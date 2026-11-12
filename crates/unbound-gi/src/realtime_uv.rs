//! Realtime lightmap UVs: where each vertex of a mesh instance lands in its realtime lightmap, as
//! the Unity 6000.0.71f1 Editor's precompute lays them out (tests/projects/realtimegi;
//! docs/REALTIME_GI.md). With Preserve UVs the mesh's lightmap UV charts keep their shapes, each
//! scaled to its own world area at the realtime resolution; without it the precompute makes the
//! charts ([`crate::auto_charts`]), in world units. Either way each chart is snapped to whole
//! blocks of texels (at least the minimum chart size) and packed into the instance's UV space,
//! whose size in texels the scene's precomputed data also records.
use crate::auto_charts;
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

/// How an instance's charts are made.
#[derive(Debug, Clone, Copy)]
pub enum Charts<'a> {
    /// The mesh's lightmap UV charts keep their shapes (Preserve UVs).
    Preserved,
    /// The precompute makes the charts (Optimize Realtime UVs, serialized as Preserve UVs off):
    /// from the mesh's shading normals and the renderer's limits. The instance's positions and
    /// normals then have its scale applied and no rotation.
    Automatic {
        normals: &'a [[f32; 3]],
        params: auto_charts::Params,
    },
}

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
    pub charts: Charts<'a>,
}

/// A chart's place in its instance's UV space.
#[derive(Debug, Clone, PartialEq)]
pub struct PlacedChart {
    /// Its rectangle in texels: x, y, width, height (whole blocks).
    pub rect: [u32; 4],
    /// Which blocks of the rectangle the chart uses, row-major from the bottom row. The others
    /// are free for other charts (a ring's hole).
    pub blocks: Vec<bool>,
}

/// The instance's UV space (`width` x `height` texels) and each vertex's position in it (0..1);
/// vertices no triangle uses stay at 0. `charts`: each chart's place, in chart order.
#[derive(Debug, Clone, PartialEq)]
pub struct Output {
    pub width: u32,
    pub height: u32,
    pub uvs: Vec<[f32; 2]>,
    pub charts: Vec<PlacedChart>,
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
    fn placed(&self, input: &Input, uvs: &[[f32; 2]], t: usize) -> [[f64; 2]; 3] {
        input.triangles[t].map(|v| {
            let uv = uvs[v as usize];
            [0, 1].map(|k| uv[k] as f64 * self.scale[k] + self.offset[k])
        })
    }
}

/// Builds a chart from its UVs: scaled to its world area, snapped to whole blocks, moved to its
/// mask's origin and rasterized into the mask (each triangle grown by the border, every cell it
/// touches). `automatic`: a chart the precompute made (`auto_charts`), whose UVs are a
/// projection in world units: the scale to its world area is one for a flat chart, and more
/// where faces lean from its plane or are folded into it as lines (tests/projects/realtimegi
/// RoomTurned, FoldIgnored: a floor and a wall at a right angle projected onto the plane
/// between them).
fn build_chart(
    input: &Input,
    uvs: &[[f32; 2]],
    automatic: bool,
    triangles: Vec<usize>,
) -> Result<Chart> {
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
        let u = [a, b, c].map(|v| uvs[v].map(|x| x as f64));
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
        // An automatic chart of no extent (a part without area) takes one block, whatever the
        // minimum chart size (tests/projects/realtimegi RoomTurned, BoxSliver and BoxPoint).
        let least = if automatic && extent == 0.0 {
            1.0
        } else {
            min_blocks
        };
        let blocks = (extent + border + CHART_BORDER_MARGIN).ceil().max(least);
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
        let tri = chart
            .placed(input, uvs, t)
            .map(|p| p.map(|x| x as f32 as f64));
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

/// The smallest shape of a packing pass, each measure taken on its own over all shapes: free
/// regions that could not hold it are given up.
#[derive(Clone, Copy)]
struct Smallest {
    usage: i32,
    width: i32,
    height: i32,
}

/// A connected region of free cells: its bounding box (inclusive) and cell count.
#[derive(Clone, Copy, Debug, PartialEq)]
struct Region {
    id: i32,
    min_x: i32,
    min_y: i32,
    max_x: i32,
    max_y: i32,
    count: i32,
}

/// The region that is open to the rows above every used cell.
const OPEN_REGION: i32 = 1;
/// A region smaller than this many times the smallest shape is searched for a place that holds
/// that shape, when the shape is a full rectangle (the limit itself is not measured:
/// docs/REALTIME_GI.md).
const BLOCK_SEARCH_FACTOR: i32 = 100;

/// The hole-filling heuristic: the target's free cells as 4-connected regions, kept from the
/// first shape on that the skylines find no place for. A shape goes into the smallest region
/// that holds it, at its lowest, then leftmost position.
struct Regions {
    width: usize,
    /// Per cell: 0 when used or given up, else the id of its region.
    labels: Vec<i32>,
    /// The regions, fewest cells first.
    list: Vec<Region>,
    /// The highest used row, -1 without one. The open region's rows above the next one are
    /// neither scanned nor relabelled.
    top: i32,
}

impl Regions {
    fn new(target: &Mask, smallest: Smallest, skylines: &mut Skylines) -> Self {
        let (w, h) = (target.width as i32, target.height as i32);
        let mut regions = Self {
            width: target.width,
            labels: target
                .cells
                .iter()
                .map(|&used| if used { 0 } else { OPEN_REGION })
                .collect(),
            list: vec![Region {
                id: OPEN_REGION,
                min_x: 0,
                min_y: 0,
                max_x: w - 1,
                max_y: h - 1,
                count: w * h,
            }],
            top: (0..target.height)
                .rev()
                .find(|&y| (0..target.width).any(|x| target.get(x, y)))
                .map_or(-1, |y| y as i32),
        };
        regions.split(0, OPEN_REGION, smallest, skylines);
        regions
    }

    fn label(&self, x: i32, y: i32) -> i32 {
        self.labels[y as usize * self.width + x as usize]
    }

    fn label_mut(&mut self, x: i32, y: i32) -> &mut i32 {
        &mut self.labels[y as usize * self.width + x as usize]
    }

    /// Where `mask` goes: the first region in the list with enough cells and a large enough box
    /// that holds every used cell of it, at the lowest, then leftmost such position.
    fn find(&self, mask: &Mask, usage: i32) -> Option<(usize, usize)> {
        let (w, h) = (mask.width as i32, mask.height as i32);
        for region in &self.list {
            if usage > region.count
                || w > region.max_x - region.min_x + 1
                || h > region.max_y - region.min_y + 1
            {
                continue;
            }
            for y in region.min_y..=region.max_y - h + 1 {
                for x in region.min_x..=region.max_x - w + 1 {
                    let inside = (0..h).all(|cy| {
                        (0..w).all(|cx| {
                            !mask.get(cx as usize, cy as usize)
                                || self.label(x + cx, y + cy) == region.id
                        })
                    });
                    if inside {
                        return Some((x as usize, y as usize));
                    }
                }
            }
        }
        None
    }

    /// Takes the cells of a placed shape out of their regions. A region left with fewer free
    /// cells than the smallest shape (counting the whole shape against it) is given up; the
    /// others are split into what is still connected.
    fn place(
        &mut self,
        mask: &Mask,
        x: usize,
        y: usize,
        smallest: Smallest,
        skylines: &mut Skylines,
    ) {
        let mut touched: Vec<i32> = Vec::new();
        for cy in 0..mask.height {
            for cx in 0..mask.width {
                if mask.get(cx, cy) {
                    let (tx, ty) = ((x + cx) as i32, (y + cy) as i32);
                    let label = std::mem::replace(self.label_mut(tx, ty), 0);
                    if !touched.contains(&label) {
                        touched.push(label);
                    }
                    self.top = self.top.max(ty);
                }
            }
        }
        let usage = mask.usage() as i32;
        for id in touched {
            let Some(index) = self.list.iter().position(|r| r.id == id) else {
                continue;
            };
            if self.list[index].count - usage < smallest.usage {
                let region = self.list.remove(index);
                self.give_up(&region, skylines);
            } else {
                let last_id = self.list.iter().map(|r| r.id).max().unwrap_or(OPEN_REGION);
                self.split(index, last_id, smallest, skylines);
            }
        }
    }

    /// Gives a region up: its cells count as used from now on, for the skylines as well (what
    /// that changes for later shapes is not measured: docs/REALTIME_GI.md).
    fn give_up(&mut self, region: &Region, skylines: &mut Skylines) {
        for y in region.min_y..=region.max_y {
            for x in region.min_x..=region.max_x {
                if self.label(x, y) == region.id {
                    *self.label_mut(x, y) = 0;
                    let end = &mut skylines.row_end[y as usize];
                    *end = (*end).max(x + 1);
                    let top = &mut skylines.column_top[x as usize];
                    *top = (*top).max(y + 1);
                }
            }
        }
    }

    /// Whether a `width` x `height` block of the region's cells lies somewhere in it.
    fn holds_block(&self, region: &Region, width: i32, height: i32) -> bool {
        (region.min_y..=region.max_y - height + 1).any(|y| {
            (region.min_x..=region.max_x - width + 1).any(|x| {
                (0..height).all(|cy| (0..width).all(|cx| self.label(x + cx, y + cy) == region.id))
            })
        })
    }

    /// Replaces the region at `index` by its 4-connected parts, numbered after `last_id` in the
    /// order of their first cells (bottom row first, left to right). Of the open region, the part
    /// that reaches the row above the highest used one stays the open region, with all rows above
    /// it. Parts that cannot hold the smallest shape are given up; the others join the list,
    /// which stays ordered by cell count.
    fn split(&mut self, index: usize, last_id: i32, smallest: Smallest, skylines: &mut Skylines) {
        if self.top < 0 {
            return;
        }
        let whole = self.list[index];
        let end = (self.top + 1).min(whole.max_y);
        // The parts, marked -1, -2, ... for now.
        let mut parts = 0i32;
        let mut pending: Vec<(i32, i32)> = Vec::new();
        for y in whole.min_y..=end {
            for x in whole.min_x..=whole.max_x {
                if self.label(x, y) != whole.id {
                    continue;
                }
                parts += 1;
                *self.label_mut(x, y) = -parts;
                pending.push((x, y));
                while let Some((px, py)) = pending.pop() {
                    for (nx, ny) in [(px - 1, py), (px + 1, py), (px, py - 1), (px, py + 1)] {
                        if nx >= whole.min_x
                            && nx <= whole.max_x
                            && ny >= whole.min_y
                            && ny <= end
                            && self.label(nx, ny) == whole.id
                        {
                            *self.label_mut(nx, ny) = -parts;
                            pending.push((nx, ny));
                        }
                    }
                }
            }
        }
        let open = if whole.id == OPEN_REGION && self.top < whole.max_y {
            (whole.min_x..=whole.max_x)
                .rev()
                .map(|x| self.label(x, end))
                .find(|&label| label != 0)
                .filter(|&label| label < 0)
        } else {
            None
        };
        // The open region's rows above the scanned ones count one column short of its width
        // (not measured: docs/REALTIME_GI.md).
        let mut rest = Region {
            id: OPEN_REGION,
            min_x: 0,
            min_y: whole.max_y,
            max_x: whole.max_x,
            max_y: whole.max_y,
            count: (whole.max_x - whole.min_x) * (whole.max_y - self.top - 1),
        };
        let mut made: Vec<Region> = Vec::with_capacity(parts as usize + 1);
        // Per part: its index in `made`, none for the open one.
        let mut made_of: Vec<Option<usize>> = Vec::with_capacity(parts as usize);
        for part in 1..=parts {
            if open == Some(-part) {
                made_of.push(None);
                continue;
            }
            made_of.push(Some(made.len()));
            made.push(Region {
                id: last_id + 1 + made.len() as i32,
                min_x: whole.max_x,
                min_y: whole.max_y,
                max_x: whole.min_x,
                max_y: whole.min_y,
                count: 0,
            });
        }
        for y in whole.min_y..=end {
            for x in whole.min_x..=whole.max_x {
                let label = self.label(x, y);
                if label >= 0 {
                    continue;
                }
                let region = match made_of[(-label - 1) as usize] {
                    Some(i) => {
                        let region = &mut made[i];
                        region.min_x = region.min_x.min(x);
                        region.max_x = region.max_x.max(x);
                        region.max_y = region.max_y.max(y);
                        region
                    }
                    None => &mut rest,
                };
                region.min_y = region.min_y.min(y);
                region.count += 1;
                *self.label_mut(x, y) = region.id;
            }
        }
        if whole.id == OPEN_REGION {
            made.push(rest);
        }
        // Largest first; of equal ones the later, which puts the later of two equal regions
        // first in the list (tests/projects/realtimegi RoomHoles: of two holes of one size a
        // plate takes the right one, and of two stacked the upper one).
        let mut kept: Vec<Region> = Vec::with_capacity(made.len());
        for region in made {
            let (width, height) = (
                region.max_x - region.min_x + 1,
                region.max_y - region.min_y + 1,
            );
            let too_small = region.count < smallest.usage
                || width < smallest.width
                || height < smallest.height
                || ((smallest.width > 1 || smallest.height > 1)
                    && smallest.usage == smallest.width * smallest.height
                    && region.count < smallest.usage * BLOCK_SEARCH_FACTOR
                    && !self.holds_block(&region, smallest.width, smallest.height));
            if too_small {
                self.give_up(&region, skylines);
            } else {
                let at = kept
                    .iter()
                    .position(|k| region.count >= k.count)
                    .unwrap_or(kept.len());
                kept.insert(at, region);
            }
        }
        // Each goes before the whole region's place, behind every region there with no more
        // cells than it has.
        self.list.remove(index);
        let mut at = index;
        for region in kept {
            while at > 0 && region.count < self.list[at - 1].count {
                at -= 1;
            }
            self.list.insert(at, region);
            at += 1;
        }
    }
}

/// One packing pass: the target, its skylines and, once the skylines failed a shape that the
/// target's size admits, its free regions.
struct Pass {
    target: Mask,
    skylines: Skylines,
    regions: Option<Regions>,
    smallest: Smallest,
}

impl Pass {
    fn new(width: usize, height: usize, smallest: Smallest) -> Self {
        Self {
            target: Mask::new(width, height),
            skylines: Skylines::new(width, height),
            regions: None,
            smallest,
        }
    }

    /// The shape's position: where the skylines put it, else a free region that holds it.
    fn best(&mut self, mask: &Mask) -> Option<(usize, usize)> {
        let (width, height) = (self.target.width, self.target.height);
        if let Some(found) = self.skylines.best(mask, width, height) {
            return Some(found);
        }
        if mask.width > width || mask.height > height {
            return None;
        }
        if self.regions.is_none() {
            self.regions = Some(Regions::new(
                &self.target,
                self.smallest,
                &mut self.skylines,
            ));
        }
        self.regions
            .as_ref()
            .and_then(|regions| regions.find(mask, mask.usage() as i32))
    }

    fn place(&mut self, mask: &Mask, x: usize, y: usize) {
        self.skylines.place(mask, x, y);
        for cy in 0..mask.height {
            for cx in 0..mask.width {
                if mask.get(cx, cy) {
                    self.target.cells[(y + cy) * self.target.width + x + cx] = true;
                }
            }
        }
        if let Some(regions) = &mut self.regions {
            regions.place(mask, x, y, self.smallest, &mut self.skylines);
        }
    }

    /// The target's used size: one past its last used column and row.
    fn used(&self) -> (usize, usize) {
        let target = &self.target;
        let columns = (0..target.width)
            .rev()
            .find(|&x| (0..target.height).any(|y| target.get(x, y)));
        let rows = (0..target.height)
            .rev()
            .find(|&y| (0..target.width).any(|x| target.get(x, y)));
        (columns.map_or(0, |x| x + 1), rows.map_or(0, |y| y + 1))
    }
}

/// Packs the masks, largest first (ties in chart order), into a target that starts at one
/// block and grows along its shorter side by the size of a shape that does not fit, until all
/// fit; then crops it to what is used. A shape goes where the skylines put it, else into a hole
/// between the shapes placed before it. Returns each mask's position and the target's size.
fn pack(masks: &[&Mask]) -> Result<(Vec<(usize, usize)>, usize, usize)> {
    let max = (MAX_TEXELS / BLOCK_TEXELS) as usize;
    let mut order: Vec<usize> = (0..masks.len()).collect();
    let usage: Vec<usize> = masks.iter().map(|m| m.usage()).collect();
    order.sort_by(|&a, &b| usage[b].cmp(&usage[a]).then(a.cmp(&b)));
    let least = |of: fn(&Mask) -> usize| masks.iter().map(|m| of(m)).min().unwrap_or(0) as i32;
    let smallest = Smallest {
        usage: usage.iter().copied().min().unwrap_or(0) as i32,
        width: least(|m| m.width),
        height: least(|m| m.height),
    };
    let (mut width, mut height) = (1usize, 1usize);
    loop {
        let mut pass = Pass::new(width, height, smallest);
        let mut positions = vec![(0, 0); masks.len()];
        let (mut need_w, mut need_h) = (0usize, 0usize);
        for &i in &order {
            let mask = masks[i];
            match pass.best(mask) {
                Some((x, y)) => {
                    pass.place(mask, x, y);
                    positions[i] = (x, y);
                }
                None => {
                    need_w = need_w.max(mask.width);
                    need_h = need_h.max(mask.height);
                }
            }
        }
        if need_w == 0 && need_h == 0 {
            let (used_w, used_h) = pass.used();
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
    let made;
    let (lists, chart_uvs, automatic) = match input.charts {
        Charts::Preserved => (charts(input), input.uvs, false),
        Charts::Automatic { normals, params } => {
            let auto_charts::Charts { uvs, triangles } = auto_charts::auto_charts(
                &auto_charts::Mesh {
                    positions: input.positions,
                    normals,
                    uvs: input.uvs,
                    triangles: input.triangles,
                },
                &params,
            )?;
            made = uvs;
            (triangles, made.as_slice(), true)
        }
    };
    let charts: Vec<Chart> = lists
        .into_iter()
        .map(|t| build_chart(input, chart_uvs, automatic, t))
        .collect::<Result<_>>()?;
    let masks: Vec<&Mask> = charts.iter().map(|c| &c.mask).collect();
    let (positions, width, height) = pack(&masks)?;
    let mut uvs = vec![[0.0f32; 2]; input.positions.len()];
    for (chart, &(x, y)) in charts.iter().zip(&positions) {
        for &t in &chart.triangles {
            for &v in &input.triangles[t] {
                let uv = chart_uvs[v as usize];
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
        .map(|(c, &(x, y))| PlacedChart {
            rect: [x, y, c.mask.width, c.mask.height].map(|v| v as u32 * BLOCK_TEXELS),
            blocks: c.mask.cells.clone(),
        })
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
/// chart's blocks cover texels of one precomputed chart of its own. A difference means Unbound
/// would draw the system's lightmap at other texels than the precompute lit.
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
        let [left, bottom, width, height] = chart.rect;
        let across = width / BLOCK_TEXELS;
        // A cached layout may hold anything: no sum here may wrap.
        ensure!(
            width % BLOCK_TEXELS == 0
                && height % BLOCK_TEXELS == 0
                && chart.blocks.len() as u64 == across as u64 * (height / BLOCK_TEXELS) as u64
                && left as u64 + width as u64 <= output.width as u64
                && bottom as u64 + height as u64 <= output.height as u64,
            "a realtime lightmap chart at {:?} with {} blocks in UVs of {}x{} texels",
            chart.rect,
            chart.blocks.len(),
            output.width,
            output.height
        );
        let mut id = None;
        for y in 0..height {
            for x in 0..width {
                if !chart.blocks[((y / BLOCK_TEXELS) * across + x / BLOCK_TEXELS) as usize] {
                    continue;
                }
                let texel = (origin[1] as u32 + bottom + y) * sw + origin[0] as u32 + left + x;
                let found = system.chart_mask[texel as usize];
                ensure!(
                    found != NO_CHART && id.is_none_or(|i| i == found),
                    "a realtime lightmap chart at {:?} over texels the precompute did not lay out for it",
                    chart.rect
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
    fn a_shape_goes_into_a_hole_the_skylines_do_not_see() {
        // A ring with a 3x3 hole, then a block that fits only there: the target stays the ring.
        let ring = mask(5, 5, |x, y| x == 0 || y == 0 || x == 4 || y == 4);
        let small = mask(2, 2, |_, _| true);
        let (positions, w, h) = pack(&[&ring, &small]).unwrap();
        assert_eq!(positions, vec![(0, 0), (1, 1)]);
        assert_eq!((w, h), (5, 5));
        // A pocket that opens downwards is no hole, and hidden from both skylines all the same.
        let arch = mask(5, 4, |x, y| x == 0 || x == 4 || y == 3);
        let (positions, w, h) = pack(&[&arch, &small]).unwrap();
        assert_eq!(positions, vec![(0, 0), (1, 0)]);
        assert_eq!((w, h), (5, 4));
    }

    #[test]
    fn a_shape_no_hole_holds_grows_the_target() {
        // The hole is one block short of the second shape: the target grows as without a hole.
        let ring = mask(5, 5, |x, y| x == 0 || y == 0 || x == 4 || y == 4);
        let large = mask(4, 4, |_, _| true);
        let (positions, w, h) = pack(&[&ring, &large]).unwrap();
        assert_eq!(positions[0], (0, 0));
        assert!(positions[1].0 >= 5 || positions[1].1 >= 5, "{positions:?}");
        assert!(w * h >= 5 * 5 + 4 * 4, "{w}x{h}");
        // An L of three blocks does not go into a hole of two.
        let frame = mask(4, 3, |x, y| x == 0 || x == 3 || y == 0 || y == 2);
        let corner = mask(2, 2, |x, y| x == 0 || y == 0);
        let (positions, _, _) = pack(&[&frame, &corner]).unwrap();
        assert!(positions[1].0 >= 4 || positions[1].1 >= 3, "{positions:?}");
    }

    #[test]
    fn the_smallest_hole_that_holds_a_shape_takes_it() {
        // Two holes, the larger one first in every scan order: 3x3 at (1, 1), 2x2 at (5, 1).
        let plate = mask(8, 5, |x, y| {
            !((1..4).contains(&x) && (1..4).contains(&y))
                && !((5..7).contains(&x) && (1..3).contains(&y))
        });
        let small = mask(2, 2, |_, _| true);
        let (positions, w, h) = pack(&[&plate, &small, &small]).unwrap();
        // The first block takes the hole of its own size; the second the lowest, leftmost place
        // of the other.
        assert_eq!(positions, vec![(0, 0), (5, 1), (1, 1)]);
        assert_eq!((w, h), (8, 5));
    }

    #[test]
    fn free_regions_split_as_shapes_are_placed() {
        // A 6x4 target with a wall in column 2 up to row 2, and a used cell at (4, 0).
        let target = mask(6, 4, |x, y| (x == 2 && y < 3) || (x == 4 && y == 0));
        let smallest = Smallest {
            usage: 1,
            width: 1,
            height: 1,
        };
        let mut skylines = Skylines::new(6, 4);
        let mut regions = Regions::new(&target, smallest, &mut skylines);
        // Everything is still one region through the top row: the open one.
        assert_eq!(regions.list.len(), 1);
        let open = regions.list[0];
        assert_eq!((open.id, open.min_x, open.max_x, open.max_y), (1, 0, 5, 3));
        assert_eq!(open.min_y, 0);
        assert_eq!(open.count, 6 * 4 - 4);
        // The open region's rows above the scanned ones count one column short of its width:
        // with one used cell in a 6x6 target, rows 0 and 1 give 11 cells and the four rows above
        // five each.
        let corner = mask(6, 6, |x, y| x == 0 && y == 0);
        let above = Regions::new(&corner, smallest, &mut Skylines::new(6, 6));
        assert_eq!(above.list.len(), 1);
        assert_eq!((above.list[0].id, above.list[0].count), (1, 11 + 4 * 5));
        // A bar across row 3 closes both sides: two regions, the smaller first, numbered by
        // their first cells; the open one is used up and given up.
        let bar = mask(6, 1, |_, _| true);
        regions.place(&bar, 0, 3, smallest, &mut skylines);
        let ids: Vec<(i32, i32)> = regions.list.iter().map(|r| (r.id, r.count)).collect();
        assert_eq!(ids, vec![(2, 6), (3, 8)]);
        assert_eq!(regions.list[0].min_x, 0);
        assert_eq!(regions.list[0].max_x, 1);
        assert_eq!(regions.list[1].min_x, 3);
        assert_eq!(regions.list[1].max_x, 5);
        // A block leaves the left region with two cells, fewer than a smallest shape of three
        // would need: with that smallest shape the rest is given up and counts as used.
        let smallest = Smallest {
            usage: 3,
            width: 1,
            height: 1,
        };
        let block = mask(2, 2, |_, _| true);
        assert_eq!(regions.find(&block, 4), Some((0, 0)));
        regions.place(&block, 0, 0, smallest, &mut skylines);
        assert!(regions.list.iter().all(|r| r.id != 2));
        assert_eq!(regions.label(0, 2), 0);
        assert_eq!(skylines.column_top[0], 3);
        assert_eq!(skylines.row_end[2], 2);
    }
}
