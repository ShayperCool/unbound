//! The half-edge mesh the unwrap works on. The order of its half-edges (the border's appended
//! after the facets'), each vertex's half-edge and every facet's centroid, normal and area feed
//! later choices; built this way, the unwrap's UV2 matches the Unity 6000.0.71f1 Editor's
//! (tests/projects/lightmapuvs).

use super::math;

pub const NONE: u32 = u32::MAX;

#[derive(Debug, Clone, Copy)]
pub struct HalfEdge {
    pub twin: u32,
    pub next: u32,
    pub prev: u32,
    /// `NONE` on the border.
    pub facet: u32,
    /// The vertex the half-edge points to.
    pub target: u32,
}

#[derive(Debug, Clone, Copy)]
pub struct Vertex {
    pub position: [f64; 3],
    /// The last half-edge created that points to the vertex.
    pub edge: u32,
    /// The vertex's index in the triangles the map was made from.
    pub index: u32,
}

#[derive(Debug, Clone, Copy)]
pub struct FacetData {
    pub centroid: [f64; 3],
    pub normal: [f64; 3],
    pub area: f64,
    /// The triangle's source (`MeshInput::sources`).
    pub source: u32,
    /// The triangle's vertices as given.
    pub corners: [u32; 3],
}

#[derive(Debug, Clone, Copy, Default)]
pub struct EdgeInfo {
    pub length: f64,
    /// UV0 differs across the edge.
    pub seam: bool,
    /// The facets meet at more than the hard angle (always, on the border).
    pub hard: bool,
    /// Both facets come from the same source polygon.
    pub same_source: bool,
}

#[derive(Debug, Clone, Default)]
pub struct HalfEdgeMesh {
    pub vertices: Vec<Vertex>,
    pub edges: Vec<HalfEdge>,
    /// UV0 at each half-edge's target corner (zero on the border).
    pub uvs: Vec<[f64; 2]>,
    /// Each facet's first half-edge (the one pointing to its first corner).
    pub facets: Vec<u32>,
    pub infos: Vec<FacetData>,
    pub edge_infos: Vec<EdgeInfo>,
    pub hard_angle: f32,
}

/// A set of facets of a map, in increasing order.
#[derive(Debug, Clone)]
pub struct Component {
    pub facets: Vec<u32>,
}

/// Triangles to build a map from.
pub struct Triangles<'a> {
    pub positions: &'a [[f32; 3]],
    pub triangles: &'a [[u32; 3]],
    pub sources: Option<&'a [u32]>,
    pub uvs: Option<&'a [[[f32; 2]; 3]]>,
}

fn sub(a: [f64; 3], b: [f64; 3]) -> [f64; 3] {
    [a[0] - b[0], a[1] - b[1], a[2] - b[2]]
}

fn sqrt_pos(x: f64) -> f64 {
    if 0.0 < x { x.sqrt() } else { 0.0 }
}

impl HalfEdgeMesh {
    pub fn position(&self, vertex: u32) -> [f64; 3] {
        self.vertices[vertex as usize].position
    }

    /// The vertex a half-edge points to.
    pub fn target(&self, edge: u32) -> [f64; 3] {
        self.position(self.edges[edge as usize].target)
    }

    pub fn edge(&self, edge: u32) -> &HalfEdge {
        &self.edges[edge as usize]
    }

    /// The facet across a half-edge (`NONE` on the border).
    pub fn across(&self, edge: u32) -> u32 {
        self.edges[self.edges[edge as usize].twin as usize].facet
    }

    /// The facet's three half-edges, from its first.
    pub fn facet_edges(&self, facet: u32) -> [u32; 3] {
        let h0 = self.facets[facet as usize];
        let h1 = self.edges[h0 as usize].next;
        [h0, h1, self.edges[h1 as usize].next]
    }

    /// A border vertex has a border half-edge among those pointing to it.
    pub fn on_border(&self, vertex: u32) -> bool {
        let first = self.vertices[vertex as usize].edge;
        let mut e = first;
        loop {
            if self.edges[e as usize].facet == NONE {
                return true;
            }
            e = self.edges[self.edges[e as usize].twin as usize].prev;
            if e == first {
                return false;
            }
        }
    }

    pub fn create(desc: &Triangles, hard_angle: f32) -> Result<Self, String> {
        let mut map = HalfEdgeMesh {
            hard_angle,
            ..Default::default()
        };
        map.vertices = desc
            .positions
            .iter()
            .enumerate()
            .map(|(i, p)| Vertex {
                position: p.map(f64::from),
                edge: NONE,
                index: i as u32,
            })
            .collect();
        // The half-edges leaving each vertex.
        let mut outgoing: Vec<Vec<u32>> = vec![Vec::new(); desc.positions.len()];
        for (t, tri) in desc.triangles.iter().enumerate() {
            let first = map.edges.len() as u32;
            map.facets.push(first);
            for k in 0..3 {
                let target = tri[k];
                let source = tri[(k + 2) % 3];
                let e = map.edges.len() as u32;
                map.edges.push(HalfEdge {
                    twin: NONE,
                    next: NONE,
                    prev: NONE,
                    facet: t as u32,
                    target,
                });
                map.vertices[target as usize].edge = e;
                outgoing[source as usize].push(e);
                for i in 0..outgoing[target as usize].len() {
                    let other = outgoing[target as usize][i];
                    if map.edges[other as usize].target == source {
                        map.edges[e as usize].twin = other;
                        map.edges[other as usize].twin = e;
                    }
                }
                map.uvs.push(match desc.uvs {
                    Some(uvs) => uvs[t][k].map(f64::from),
                    None => [0.0, 0.0],
                });
            }
            let [h0, h1, h2] = [first, first + 1, first + 2];
            map.edges[h2 as usize].next = h0;
            map.edges[h0 as usize].prev = h2;
            map.edges[h0 as usize].next = h1;
            map.edges[h1 as usize].prev = h0;
            map.edges[h1 as usize].next = h2;
            map.edges[h2 as usize].prev = h1;
        }
        // Border half-edges, after all others.
        let inner = map.edges.len();
        for i in 0..inner {
            if map.edges[i].twin != NONE {
                continue;
            }
            let b = map.edges.len() as u32;
            let source = map.edges[map.edges[i].prev as usize].target;
            map.edges.push(HalfEdge {
                twin: i as u32,
                next: NONE,
                prev: NONE,
                facet: NONE,
                target: source,
            });
            map.edges[i].twin = b;
            outgoing[map.edges[i].target as usize].push(b);
            map.uvs.push([0.0, 0.0]);
        }
        for i in 0..map.edges.len() {
            if map.edges[i].facet != NONE {
                continue;
            }
            let twin = map.edges[i].twin;
            let (mut next, mut prev) = (twin, twin);
            if map.edges[twin as usize].facet != NONE {
                loop {
                    next = map.edges[map.edges[next as usize].prev as usize].twin;
                    if map.edges[next as usize].facet == NONE {
                        break;
                    }
                }
                while map.edges[prev as usize].facet != NONE {
                    prev = map.edges[map.edges[prev as usize].next as usize].twin;
                }
            }
            map.edges[i].next = next;
            map.edges[i].prev = prev;
        }
        // A vertex whose half-edges form more than one fan keeps the first and gets a copy
        // for each other (appended after all vertices), in the order of their lowest
        // half-edge.
        for v in 0..map.vertices.len() {
            let first = map.vertices[v].edge;
            if first == NONE {
                continue;
            }
            let mut count = 0;
            let mut e = first;
            loop {
                count += 1;
                e = map.edges[map.edges[e as usize].twin as usize].prev;
                if e == first {
                    break;
                }
            }
            if count == outgoing[v].len() {
                continue;
            }
            let mut rest: std::collections::BTreeSet<u32> = outgoing[v].iter().copied().collect();
            let start = map.edges[first as usize].twin;
            map.take_fan(v as u32, start, &mut rest);
            while let Some(&start) = rest.first() {
                let copy = map.vertices[v];
                map.vertices.push(copy);
                map.take_fan(map.vertices.len() as u32 - 1, start, &mut rest);
            }
        }
        for (t, tri) in desc.triangles.iter().enumerate() {
            let info = map.facet_data(t as u32, desc.sources, *tri);
            map.infos.push(info);
        }
        map.edge_infos = vec![EdgeInfo::default(); map.edges.len()];
        for i in 0..map.edges.len() {
            if map.edge_infos[i].seam {
                continue;
            }
            let e = map.edges[i];
            let twin = map.edges[e.twin as usize];
            let (a, b) = (map.uvs[e.prev as usize], map.uvs[e.twin as usize]);
            let (c, d) = (map.uvs[i], map.uvs[twin.prev as usize]);
            if a[0] - b[0] != 0.0 || a[1] - b[1] != 0.0 || c[0] - d[0] != 0.0 || c[1] - d[1] != 0.0
            {
                map.edge_infos[i].seam = true;
                map.edge_infos[e.twin as usize].seam = true;
            }
        }
        let hard_cos = math::cos(f64::from(hard_angle) * 0.017453292519943295);
        for i in 0..map.edges.len() {
            map.edge_infos[i].hard = true;
            let e = map.edges[i];
            let (fa, fb) = (e.facet, map.edges[e.twin as usize].facet);
            if fa == NONE || fb == NONE {
                continue;
            }
            let (na, nb) = (map.infos[fa as usize].normal, map.infos[fb as usize].normal);
            let dot = nb[2] * na[2] + na[0] * nb[0] + nb[1] * na[1];
            if hard_cos < dot && 1e-6 < (dot - hard_cos).abs() {
                map.edge_infos[i].hard = false;
            }
            if desc.sources.is_some()
                && map.infos[fa as usize].source == map.infos[fb as usize].source
            {
                map.edge_infos[i].same_source = true;
                map.infos[fb as usize].normal = map.infos[fa as usize].normal;
                map.edge_infos[i].hard = false;
            }
        }
        for i in 0..map.edges.len() {
            let e = map.edges[i];
            let d = sub(map.target(i as u32), map.target(e.prev));
            map.edge_infos[i].length = sqrt_pos(d[2] * d[2] + d[1] * d[1] + d[0] * d[0]);
        }
        Ok(map)
    }

    /// Gives `vertex` the fan of half-edges leaving it around `start` (removed from `rest`),
    /// closing the border around the fan.
    fn take_fan(&mut self, vertex: u32, start: u32, rest: &mut std::collections::BTreeSet<u32>) {
        let mut first = start;
        loop {
            if self.edges[first as usize].facet == NONE {
                break;
            }
            first = self.edges[self.edges[first as usize].prev as usize].twin;
            if first == start {
                break;
            }
        }
        let twin = self.edges[first as usize].twin;
        self.vertices[vertex as usize].edge = twin;
        self.edges[twin as usize].target = vertex;
        rest.remove(&first);
        let mut h = first;
        loop {
            let t = self.edges[h as usize].twin;
            if self.edges[t as usize].facet == NONE {
                break;
            }
            h = self.edges[t as usize].next;
            if h == first {
                break;
            }
            let t = self.edges[h as usize].twin;
            self.edges[t as usize].target = vertex;
            rest.remove(&h);
        }
        if self.edges[first as usize].facet == NONE {
            let t = self.edges[h as usize].twin;
            self.edges[t as usize].next = first;
            self.edges[first as usize].prev = t;
        }
    }

    fn facet_data(&self, facet: u32, sources: Option<&[u32]>, corners: [u32; 3]) -> FacetData {
        let [h0, h1, h2] = self.facet_edges(facet);
        let (p0, p1, p2) = (self.target(h0), self.target(h1), self.target(h2));
        let centroid = [
            (p0[0] + p1[0] + p2[0]) / 3.0,
            (p0[1] + p1[1] + p2[1]) / 3.0,
            (p0[2] + p1[2] + p2[2]) / 3.0,
        ];
        let e = sub(p1, p0);
        let f = sub(p2, p0);
        let cy = e[2] * f[0] - f[2] * e[0];
        let cz = e[0] * f[1] - f[0] * e[1];
        let cx = e[1] * f[2] - f[1] * e[2];
        let len2 = cx * cx + cy * cy + cz * cz;
        FacetData {
            centroid,
            normal: self.facet_normal(facet),
            area: if 0.0 < len2 { len2.sqrt() * 0.5 } else { 0.0 },
            source: sources.map_or(facet, |s| s[facet as usize]),
            corners,
        }
    }

    /// The unit normal from the facet's edges into its first corner, both normalized first.
    fn facet_normal(&self, facet: u32) -> [f64; 3] {
        let h0 = self.facets[facet as usize];
        let h2 = self.edges[h0 as usize].prev;
        let twin = self.edges[h2 as usize].twin;
        let v0 = self.target(h0);
        let v2 = self.target(h2);
        let a = sub(v0, v2);
        let la = sqrt_pos(a[2] * a[2] + a[0] * a[0] + a[1] * a[1]);
        // The second edge through the facet's last half-edge's twin (its endpoints).
        let v1 = self.target(twin);
        let w2 = self.target(self.edges[twin as usize].prev);
        let b = sub(v1, w2);
        let lb = sqrt_pos(b[0] * b[0] + b[1] * b[1] + b[2] * b[2]);
        let (ax, ay, az) = (a[0] / la, a[1] / la, a[2] / la);
        let (bx, by, bz) = (b[0] / lb, b[1] / lb, b[2] / lb);
        let nx = ay * bz - by * az;
        let ny = az * bx - bz * ax;
        let nz = by * ax - bx * ay;
        let len = sqrt_pos(nx * nx + ny * ny + nz * nz);
        [nx / len, ny / len, nz / len]
    }

    /// Facets connected through edges, each component's facets in increasing order, components
    /// in the order of their first facet.
    pub fn connected_components(&self) -> Vec<Component> {
        let all: Vec<u32> = (0..self.facets.len() as u32).collect();
        self.components_of(&all)
    }

    /// The facets of `facets` connected through edges among themselves, as
    /// [`Self::connected_components`].
    pub fn components_of(&self, facets: &[u32]) -> Vec<Component> {
        const OUT: i32 = -2;
        const IN: i32 = -1;
        let mut label = vec![OUT; self.facets.len()];
        for &f in facets {
            label[f as usize] = IN;
        }
        let mut count = 0;
        for start in 0..self.facets.len() {
            if label[start] != IN {
                continue;
            }
            let mut stack = vec![start as u32];
            while let Some(f) = stack.pop() {
                if label[f as usize] != IN {
                    continue;
                }
                label[f as usize] = count;
                for e in self.facet_edges(f) {
                    let g = self.across(e);
                    if g != NONE && label[g as usize] == IN {
                        stack.push(g);
                    }
                }
            }
            count += 1;
        }
        let mut components = vec![Component { facets: Vec::new() }; count as usize];
        for (f, &l) in label.iter().enumerate() {
            if l >= 0 {
                components[l as usize].facets.push(f as u32);
            }
        }
        components
    }

    /// The facets of `component` as triangles of their own: the vertices they use in increasing
    /// order, each triangle's corners in their original order, with the parent's sources and
    /// UVs. Also the parent vertex of each new vertex.
    pub fn sub_triangles(&self, component: &Component) -> Result<SubTriangles, String> {
        let mut used = vec![false; self.vertices.len()];
        for &f in &component.facets {
            for e in self.facet_edges(f) {
                used[self.edges[e as usize].target as usize] = true;
            }
        }
        let mut new_index = vec![NONE; self.vertices.len()];
        let mut parents = Vec::new();
        for v in 0..self.vertices.len() {
            if used[v] {
                new_index[v] = parents.len() as u32;
                parents.push(v as u32);
            }
        }
        let positions: Vec<[f32; 3]> = parents
            .iter()
            .map(|&v| self.position(v).map(|c| c as f32))
            .collect();
        let mut triangles = Vec::with_capacity(component.facets.len());
        let mut uvs = Vec::with_capacity(component.facets.len());
        let mut sources = Vec::with_capacity(component.facets.len());
        for &f in &component.facets {
            let info = &self.infos[f as usize];
            sources.push(info.source);
            let mut tri = [0u32; 3];
            let mut uv = [[0f32; 2]; 3];
            for e in self.facet_edges(f) {
                let target = self.edges[e as usize].target;
                let index = self.vertices[target as usize].index;
                let mut slot = NONE;
                for k in 0..3 {
                    if index == info.corners[k] {
                        slot = k as u32;
                    }
                }
                if slot == NONE {
                    return Err("a facet's corner is not among its vertices".into());
                }
                tri[slot as usize] = new_index[target as usize];
                uv[slot as usize] = self.uvs[e as usize].map(|c| c as f32);
            }
            triangles.push(tri);
            uvs.push(uv);
        }
        Ok(SubTriangles {
            positions,
            triangles,
            uvs,
            sources,
            parents,
        })
    }

    /// The facets of `component` as a map of their own ([`Self::sub_triangles`]) with the
    /// parent's hard angle, and the parent vertex of each new vertex (vertices split apart as
    /// separate fans come after these).
    pub fn sub_map(&self, component: &Component) -> Result<(HalfEdgeMesh, Vec<u32>), String> {
        let sub = self.sub_triangles(component)?;
        let desc = Triangles {
            positions: &sub.positions,
            triangles: &sub.triangles,
            sources: Some(&sub.sources),
            uvs: Some(&sub.uvs),
        };
        Ok((HalfEdgeMesh::create(&desc, self.hard_angle)?, sub.parents))
    }
}

pub struct SubTriangles {
    pub positions: Vec<[f32; 3]>,
    pub triangles: Vec<[u32; 3]>,
    pub uvs: Vec<[[f32; 2]; 3]>,
    pub sources: Vec<u32>,
    pub parents: Vec<u32>,
}
