//! Chart segmentation: facets grow into charts around seeds by a priority mixing the deviation
//! from the chart's mean normal, the distance to its centroid and the straightness of its
//! border, refined by Lloyd iterations, then neighbouring charts with similar normals merge
//! (the resulting UV2 matches the Unity 6000.0.71f1 Editor's: tests/projects/lightmapuvs).

use super::math;
use super::mesh::{Component, HalfEdgeMesh, NONE};

const DISTANCE_EXPONENT: f64 = 0.7;
const CHANGED_FRACTION: f64 = 0.01;
const CHANGED_FRACTION2: f64 = 0.01;
const GROW_ERROR: f64 = 0.112;
const LLOYD_ERROR: f64 = 0.08;
const MERGE_ERROR: f64 = 0.2;

#[derive(Debug, Clone, Copy)]
struct QueuedFacet {
    facet: u32,
    priority: f64,
    key: f64,
}

#[derive(Debug, Clone)]
struct Chart {
    normal: [f64; 3],
    centroid: [f64; 3],
    seed: u32,
    count: u32,
    area: f64,
    queue: Vec<QueuedFacet>,
    dirty: bool,
}

#[derive(Debug, Clone, Copy)]
struct Border {
    charts: [u32; 2],
    centroids: [f64; 6],
    error: f64,
    hard: bool,
}

pub struct Segmentation<'a> {
    pub map: HalfEdgeMesh,
    component: &'a Component,
    charts: Vec<Chart>,
    assign: Vec<i32>,
    prev: Vec<i32>,
    prev2: Vec<i32>,
    total_area: f64,
    small_area: f64,
    small_count: f64,
}

/// 1 - n·m.
fn deviation(n: [f64; 3], m: [f64; 3]) -> f64 {
    1.0 - (n[2] * m[2] + m[0] * n[0] + n[1] * m[1])
}

impl<'a> Segmentation<'a> {
    pub fn new(
        parent: &HalfEdgeMesh,
        component: &'a Component,
        small_area: f64,
        small_count: f64,
    ) -> Result<Self, String> {
        let (map, _) = parent.sub_map(component)?;
        let n = map.facets.len();
        let mut total_area = 0.0;
        for info in &map.infos {
            total_area += info.area;
        }
        Ok(Self {
            map,
            component,
            charts: Vec::new(),
            assign: vec![-1; n],
            prev: vec![-1; n],
            prev2: vec![-1; n],
            total_area,
            small_area,
            small_count,
        })
    }

    fn label(&self, facet: u32) -> i32 {
        self.assign[facet as usize]
    }

    fn fit_charts(&mut self) {
        let mut acc = vec![[0f64; 7]; self.charts.len()];
        for (f, &c) in self.assign.iter().enumerate() {
            if c == -1 {
                continue;
            }
            let info = &self.map.infos[f];
            let (n, a, p) = (info.normal, info.area, info.centroid);
            let s = &mut acc[c as usize];
            s[0] += n[0] * a;
            s[1] += n[1] * a;
            s[2] = a * n[2] + s[2];
            s[3] = p[0] * a + s[3];
            s[4] = p[1] * a + s[4];
            s[5] = p[2] * a + s[5];
            s[6] += a;
        }
        for (chart, s) in self.charts.iter_mut().zip(&acc) {
            let l2 = s[2] * s[2] + s[1] * s[1] + s[0] * s[0];
            let len = if 0.0 < l2 { l2.sqrt() } else { 0.0 };
            chart.normal = [s[0] / len, s[1] / len, s[2] / len];
            chart.centroid = [s[3] / s[6], s[4] / s[6], s[5] / s[6]];
        }
    }

    /// Each chart's seed: its facet nearest its normal, then nearest its centroid. Charts left
    /// without facets or seed are dropped.
    fn reseed(&mut self) {
        let k = self.charts.len();
        let mut best = vec![1e10f64; k];
        let mut best_distance = vec![1e10f64; k];
        for chart in &mut self.charts {
            chart.seed = NONE;
        }
        for f in 0..self.assign.len() {
            let c = self.assign[f];
            if c == -1 {
                continue;
            }
            let c = c as usize;
            let info = &self.map.infos[f];
            let chart = &self.charts[c];
            let d = 1.0
                - (info.normal[2] * chart.normal[2]
                    + chart.normal[0] * info.normal[0]
                    + info.normal[1] * chart.normal[1]);
            let dev = d * d;
            if dev <= best[c] && 1e-6 < (dev - best[c]).abs() {
                let dx = info.centroid[0] - chart.centroid[0];
                let dy = info.centroid[1] - chart.centroid[1];
                let dz = info.centroid[2] - chart.centroid[2];
                let distance = dz * dz + dy * dy + dx * dx;
                if dev < best[c] || distance < best_distance[c] {
                    best[c] = dev;
                    best_distance[c] = distance;
                    self.charts[c].seed = f as u32;
                }
            }
        }
        for i in (0..self.charts.len()).rev() {
            if self.charts[i].count == 0 || self.charts[i].seed == NONE {
                self.charts.remove(i);
                for label in &mut self.assign {
                    if i as i32 <= *label {
                        *label -= 1;
                    }
                }
            }
        }
    }

    /// `count` charts, facets in the chart `labels` gives them, each chart fitted and seeded
    /// (charts left empty are dropped).
    fn start_from(&mut self, count: usize, labels: &[i32]) {
        let n = self.assign.len();
        self.assign.copy_from_slice(&labels[..n]);
        self.prev.copy_from_slice(&labels[..n]);
        self.prev2.copy_from_slice(&labels[..n]);
        let first = self.map.infos[0];
        for _ in 0..count {
            self.charts.push(Chart {
                normal: first.normal,
                centroid: first.centroid,
                seed: 0,
                count: 0,
                area: 0.0,
                queue: Vec::new(),
                dirty: false,
            });
        }
        for &label in &labels[..n] {
            if label >= 0 {
                self.charts[label as usize].count += 1;
            }
        }
        self.fit_charts();
        self.reseed();
    }

    fn priority(&self, facet: u32, chart: usize) -> f64 {
        let info = &self.map.infos[facet as usize];
        let c = &self.charts[chart];
        let dy = info.centroid[1] - c.centroid[1];
        let dx = info.centroid[0] - c.centroid[0];
        let dz = info.centroid[2] - c.centroid[2];
        let d2 = dz * dz + dy * dy + dx * dx;
        let distance = if 0.0 < d2 { d2.sqrt() } else { 0.0 };
        let dev = 1.0
            - (c.normal[2] * info.normal[2]
                + c.normal[1] * info.normal[1]
                + c.normal[0] * info.normal[0]);
        dev * dev * math::exp(DISTANCE_EXPONENT * math::ln(distance))
    }

    /// The length of the facet's border with other charts over its border with `chart`.
    fn straightness(&self, chart: usize, facet: u32) -> f64 {
        let (mut inside, mut outside) = (0.0, 0.0);
        for (i, e) in self.map.facet_edges(facet).into_iter().enumerate() {
            let g = self.map.across(e);
            let length = self.map.edge_infos[e as usize].length;
            let other = g == NONE || self.label(g) != chart as i32;
            match (i, other) {
                (0, true) => outside = length + 0.0,
                (0, false) => inside = length + 0.0,
                (_, true) => outside += length,
                (_, false) => inside += length,
            }
        }
        if 0.0 < inside { outside / inside } else { 0.0 }
    }

    fn enqueue(&mut self, facet: u32, chart: usize, weight: Option<f64>) {
        let p = self.priority(facet, chart);
        let s = self.straightness(chart, facet);
        let entry = match weight {
            Some(w) => QueuedFacet {
                facet,
                priority: p * w,
                key: s * (p * w),
            },
            None => QueuedFacet {
                facet,
                priority: p,
                key: s * p,
            },
        };
        self.charts[chart].queue.push(entry);
        self.charts[chart].dirty = true;
    }

    fn queue_neighbours(&mut self, facet: u32) {
        let c = self.label(facet);
        for e in self.map.facet_edges(facet) {
            let g = self.map.across(e);
            if g != NONE && self.label(g) == -1 && self.map.edge_infos[e as usize].same_source {
                self.assign[g as usize] = c;
                let chart = &mut self.charts[c as usize];
                chart.area = self.map.infos[g as usize].area + chart.area;
                chart.count += 1;
            }
        }
        for e in self.map.facet_edges(facet) {
            let g = self.map.across(e);
            if g == NONE || self.label(g) != -1 {
                continue;
            }
            for h in self.map.facet_edges(g) {
                let info = self.map.edge_infos[h as usize];
                if !(info.same_source || !info.hard) {
                    continue;
                }
                let weight = if info.same_source {
                    0.0
                } else if info.seam {
                    2.0
                } else {
                    1.0
                };
                let n = self.map.across(h);
                if n != NONE && self.label(n) != -1 {
                    let chart = self.label(n) as usize;
                    self.enqueue(g, chart, Some(weight));
                }
            }
        }
    }

    fn grow(&mut self, max_error: f64) {
        while !self.charts.is_empty() {
            let mut min = 1e30;
            for chart in &self.charts {
                let key = chart.queue.last().map_or(1e32, |q| q.key);
                min = if min <= key { min } else { key };
            }
            let selected: Vec<usize> = (0..self.charts.len())
                .filter(|&c| {
                    self.charts[c]
                        .queue
                        .last()
                        .is_some_and(|q| (q.key - min).abs() < 1e-6)
                })
                .collect();
            if selected.is_empty() {
                break;
            }
            for c in selected {
                let Some(entry) = self.charts[c].queue.pop() else {
                    continue;
                };
                let f = entry.facet;
                let info = self.map.infos[f as usize];
                let n = self.charts[c].normal;
                let d =
                    1.0 - (info.normal[2] * n[2] + n[0] * info.normal[0] + info.normal[1] * n[1]);
                if d * d < max_error && self.label(f) == -1 {
                    let chart = &mut self.charts[c];
                    chart.area = info.area + chart.area;
                    chart.count += 1;
                    self.assign[f as usize] = c as i32;
                    self.queue_neighbours(f);
                    for c2 in 0..self.charts.len() {
                        if !self.charts[c2].dirty {
                            continue;
                        }
                        let mut queue = std::mem::take(&mut self.charts[c2].queue);
                        for q in &mut queue {
                            q.key = self.straightness(c2, q.facet) * q.priority;
                        }
                        queue.sort_by(|a, b| {
                            a.key
                                .partial_cmp(&b.key)
                                .unwrap_or(std::cmp::Ordering::Equal)
                        });
                        self.charts[c2].queue = queue;
                        self.charts[c2].dirty = false;
                    }
                }
            }
        }
    }

    fn grow_from(&mut self, facet: u32, chart: usize, max_error: f64) {
        self.enqueue(facet, chart, None);
        self.grow(max_error);
    }

    fn new_chart(&mut self, facet: u32, max_error: f64) {
        let info = self.map.infos[facet as usize];
        self.charts.push(Chart {
            normal: info.normal,
            centroid: info.centroid,
            seed: facet,
            count: 0,
            area: 0.0,
            queue: Vec::new(),
            dirty: false,
        });
        let c = self.charts.len() - 1;
        self.grow_from(facet, c, max_error);
    }

    /// Regrows the newest chart from its seed after refitting it.
    fn regrow_last(&mut self, max_error: f64) {
        let c = self.charts.len() - 1;
        for label in &mut self.assign {
            if *label == c as i32 {
                *label = -1;
            }
        }
        self.charts[c].count = 0;
        let seed = self.charts[c].seed;
        self.grow_from(seed, c, max_error);
    }

    fn seed_charts(&mut self, max_error: f64) {
        let mut seed = 0u32;
        loop {
            self.new_chart(seed, max_error);
            self.fit_charts();
            self.regrow_last(max_error);
            if self.assign.is_empty() {
                return;
            }
            let last = self.charts[self.charts.len() - 1].normal;
            let mut best = -1.0;
            let mut chosen = NONE;
            for f in 0..self.assign.len() {
                if self.assign[f] != -1 {
                    continue;
                }
                let n = self.map.infos[f].normal;
                let d = 1.0 - (n[2] * last[2] + n[1] * last[1] + n[0] * last[0]);
                let d = d * d;
                if !(d < best || (best - d).abs() <= 1e-6) {
                    best = d;
                    chosen = f as u32;
                }
            }
            if chosen == NONE {
                return;
            }
            seed = chosen;
        }
    }

    fn drop_small_charts(&mut self) {
        let area = self.total_area * self.small_area;
        let polys = self.assign.len() as f64 * self.small_count;
        for i in (0..self.charts.len()).rev() {
            let chart = &self.charts[i];
            if chart.area <= area
                && area != chart.area
                && chart.count < (polys as i64 as i32 as u32).wrapping_add(1)
            {
                for label in &mut self.assign {
                    if *label == i as i32 {
                        *label = -1;
                    } else if i as i32 + 1 <= *label {
                        *label -= 1;
                    }
                }
                self.charts.remove(i);
            }
        }
    }

    /// One round of re-seeding and regrowing, `iterations` times; whether the charts still
    /// changed enough to go on.
    fn lloyd(&mut self, iterations: u32, max_error: f64) -> bool {
        for _ in 0..iterations {
            self.fit_charts();
            self.reseed();
            let fresh = std::mem::take(&mut self.prev2);
            self.prev2 =
                std::mem::replace(&mut self.prev, std::mem::replace(&mut self.assign, fresh));
            self.assign.iter_mut().for_each(|l| *l = -1);
            for c in 0..self.charts.len() {
                self.charts[c].area = 0.0;
                self.charts[c].count = 0;
                let seed = self.charts[c].seed;
                self.enqueue(seed, c, None);
            }
            self.grow(max_error);
        }
        self.drop_small_charts();
        let n = self.assign.len();
        let changed = (0..n).filter(|&f| self.prev[f] != self.assign[f]).count() as u32;
        let changed2 = (0..n).filter(|&f| self.prev2[f] != self.assign[f]).count() as u32;
        ((n as f64 * CHANGED_FRACTION2) as i64 as i32 as u32).wrapping_add(1) <= changed2
            && ((CHANGED_FRACTION * n as f64) as i64 as i32 as u32).wrapping_add(1) <= changed
    }

    fn cover_unassigned(&mut self, create_error: f64, grow_error: f64) {
        loop {
            let before = self.charts.len();
            for f in 0..self.assign.len() {
                if self.assign[f] != -1 {
                    continue;
                }
                self.new_chart(f as u32, create_error);
                self.fit_charts();
                self.reseed();
                self.regrow_last(grow_error);
            }
            if self.charts.len() == before {
                return;
            }
        }
    }

    fn merge_error(&self, border: &Border) -> f64 {
        if border.hard {
            return 1e32;
        }
        let in_border = |f: usize| {
            let c = self.assign[f];
            c == border.charts[0] as i32 || c == border.charts[1] as i32
        };
        let (mut sx, mut sy, mut sz) = (0.0, 0.0, 0.0);
        for f in 0..self.assign.len() {
            if in_border(f) {
                let info = &self.map.infos[f];
                sx += info.area * info.normal[0];
                sz += info.area * info.normal[2];
                sy += info.normal[1] * info.area;
            }
        }
        let l2 = sy * sy + sx * sx + sz * sz;
        if !(0.0 < l2) || l2.sqrt() < 1e-10 {
            return 1e32;
        }
        let l = l2.sqrt();
        let (mut error, mut total) = (0.0, 0.0);
        for f in 0..self.assign.len() {
            if in_border(f) {
                let info = &self.map.infos[f];
                let d = 1.0
                    - (info.normal[2] * (sz / l)
                        + info.normal[1] * (sy / l)
                        + info.normal[0] * (sx / l));
                error += d * d * info.area;
                total += info.area;
            }
        }
        error / total
    }

    fn borders(&self) -> Vec<Border> {
        let mut borders: Vec<Border> = Vec::new();
        let mut index: std::collections::BTreeMap<(u32, u32), usize> = Default::default();
        for f in 0..self.map.facets.len() as u32 {
            for e in self.map.facet_edges(f) {
                let g = self.map.across(e);
                if self.map.edge(e).facet == NONE || g == NONE {
                    continue;
                }
                let (a, b) = (
                    self.label(self.map.edge(e).facet) as u32,
                    self.label(g) as u32,
                );
                let (lo, hi) = (a.min(b), a.max(b));
                if lo == hi {
                    continue;
                }
                let i = *index.entry((lo, hi)).or_insert_with(|| {
                    let (p, q) = (
                        self.charts[lo as usize].centroid,
                        self.charts[hi as usize].centroid,
                    );
                    borders.push(Border {
                        charts: [lo, hi],
                        centroids: [p[0], p[1], p[2], q[0], q[1], q[2]],
                        error: 0.0,
                        hard: false,
                    });
                    borders.len() - 1
                });
                if self.map.edge_infos[e as usize].hard {
                    borders[i].hard = true;
                }
            }
        }
        for i in 0..borders.len() {
            borders[i].error = self.merge_error(&borders[i]);
        }
        super::sort::introsort(&mut borders, |a, b| {
            for k in 0..6 {
                if a.centroids[k] - b.centroids[k] != 0.0 {
                    return b.centroids[k] < a.centroids[k];
                }
            }
            false
        });
        borders.sort_by(|a, b| {
            a.error
                .partial_cmp(&b.error)
                .unwrap_or(std::cmp::Ordering::Equal)
        });
        borders
    }

    fn remove_chart(&mut self, gone: u32, keep: u32) {
        for label in &mut self.assign {
            if *label == gone as i32 {
                *label = keep as i32;
            } else if (gone as i32) < *label {
                *label -= 1;
            }
        }
        let count = self.charts[gone as usize].count;
        self.charts[keep as usize].count += count;
        self.charts.remove(gone as usize);
    }

    fn merge(&mut self, borders: &mut [Border], index: usize) {
        let [keep, gone] = borders[index].charts;
        self.remove_chart(gone, keep);
        let mut touched: Vec<u8> = Vec::new();
        for (j, b) in borders.iter_mut().enumerate() {
            let [mut c0, mut c1] = b.charts;
            if c1 == keep || c0 == gone || c0 == keep || c1 == gone {
                touched.push(j as u8);
            }
            if gone < c0 {
                c0 -= 1;
            } else if c0 == gone {
                c0 = keep;
            }
            if gone < c1 {
                c1 -= 1;
            } else if c1 == gone {
                c1 = keep;
            }
            b.charts = if c1 < c0 { [c1, c0] } else { [c0, c1] };
        }
        self.fit_charts();
        self.reseed();
        for &j in &touched {
            borders[j as usize].error = self.merge_error(&borders[j as usize]);
        }
        borders[index + 1..].sort_by(|a, b| {
            a.error
                .partial_cmp(&b.error)
                .unwrap_or(std::cmp::Ordering::Equal)
        });
    }

    /// The charts as components of the parent map.
    fn components(&self) -> Result<Vec<Component>, String> {
        let mut out = vec![Component { facets: Vec::new() }; self.charts.len()];
        for (f, &c) in self.assign.iter().enumerate() {
            if c < 0 || c as usize >= out.len() {
                return Err("a facet is left without a chart".into());
            }
            out[c as usize].facets.push(self.component.facets[f]);
        }
        Ok(out)
    }

    /// Splits a chart that would not parametrize: grows charts from the facet farthest from
    /// the single chart's normal.
    pub fn force(
        parent: &HalfEdgeMesh,
        component: &'a Component,
    ) -> Result<Vec<Component>, String> {
        let mut s = Segmentation::new(parent, component, 0.0, 0.0)?;
        s.seed_charts(GROW_ERROR);
        while s.charts.len() == 1 {
            s.fit_charts();
            let n = s.charts[0].normal;
            let mut best = -1.0;
            let mut chosen = NONE;
            for f in 0..s.map.facets.len() {
                let m = s.map.infos[f].normal;
                let d = 1.0 - (m[2] * n[2] + m[0] * n[0] + m[1] * n[1]);
                let d = d * d;
                if best < d && f as u32 != s.charts[0].seed {
                    best = d;
                    chosen = f as u32;
                }
            }
            if chosen == NONE {
                return Err("a chart cannot be split".into());
            }
            s.assign[chosen as usize] = -1;
            s.new_chart(chosen, GROW_ERROR);
        }
        s.lloyd(1, LLOYD_ERROR);
        s.cover_unassigned(GROW_ERROR, LLOYD_ERROR);
        s.components()
    }
}

/// The charts of a component: grown from seeds and refined by Lloyd iterations, or, given the
/// chart of each facet (by position in the component) from a coarser segmentation, refined from
/// those by one iteration; then neighbours with similar normals merge.
pub fn segment(
    parent: &HalfEdgeMesh,
    component: &Component,
    labels: Option<(usize, &[i32])>,
) -> Result<Vec<Component>, String> {
    let mut s = Segmentation::new(parent, component, 0.02, 0.01)?;
    let (rounds, iterations) = match labels {
        None => {
            s.seed_charts(GROW_ERROR);
            s.drop_small_charts();
            (50, 4)
        }
        Some((count, labels)) => {
            s.start_from(count, labels);
            (1, 1)
        }
    };
    for _ in 0..rounds {
        if !s.lloyd(iterations, LLOYD_ERROR) {
            break;
        }
    }
    s.cover_unassigned(GROW_ERROR, LLOYD_ERROR);
    let mut borders = s.borders();
    for i in 0..borders.len() {
        let b = borders[i];
        if b.charts[0] != b.charts[1] && b.error <= MERGE_ERROR {
            let (c0, c1) = (
                &s.charts[b.charts[0] as usize],
                &s.charts[b.charts[1] as usize],
            );
            let d = deviation(c1.normal, c0.normal);
            if d * d <= MERGE_ERROR {
                s.merge(&mut borders, i);
            }
        }
    }
    s.components()
}
