//! The order mesh optimization gives an imported mesh's triangles and vertices (matches the Unity
//! 6000.0.71f1 Editor, tests/projects/lightmapuvs: every model with and without
//! `meshOptimizationFlags`).
//!
//! **Polygon order.** Each sub-mesh's triangles are reordered for a vertex cache of
//! [`CACHE_SIZE`] entries, greedily:
//!
//! - The next triangle is one that still uses a vertex in the cache: the one with the most
//!   corners in the cache, then the highest [`valence_score`] (corners whose vertices have few
//!   triangles left); of equals, the first found, looking at the cache from its oldest entry and
//!   at each vertex's remaining triangles in their original order.
//! - With no such triangle, the next comes from the triangles that became the last one of some
//!   vertex: first those where that holds for two corners, then for one; then the first
//!   remaining triangle of the vertex with the fewest triangles left (the vertex that most
//!   recently got that number).
//! - The triangle's vertices enter the cache, replacing the three oldest entries; the entries
//!   that are not its corners move up first.
//!
//! A triangle keeps its corner order. Sub-meshes of more than [`TRIANGLES_PER_PART`] triangles
//! are ordered in equal parts (no capture has a mesh that large).
//!
//! **Vertex order.** Vertices are renumbered in the order the triangles first use them, over the
//! sub-meshes in order.

use super::ModelMesh;

/// The vertex cache the order is made for.
const CACHE_SIZE: usize = 16;
/// The most triangles ordered as one part.
const TRIANGLES_PER_PART: usize = 1_000_000;
const NONE: u32 = u32::MAX;

/// What a corner adds to a candidate triangle's score, by the triangles its vertex has left
/// modulo 8: most for one, less for two and three, nothing above, and the top value for a
/// multiple of 8 (lightmapuvs Organic has such vertices: its order differs without the wrap).
const VALENCE_SCORE: [i32; 8] = [4096, 256, 16, 1, 0, 0, 0, 0];

fn valence_score(remaining: u32) -> i32 {
    VALENCE_SCORE[(remaining & 7) as usize]
}

/// The vertices with their remaining triangles, grouped by how many they have left.
struct Vertices {
    /// Triangles left per vertex.
    count: Vec<u32>,
    /// Where each vertex's triangles start in `triangles`.
    offset: Vec<u32>,
    /// Each vertex's remaining triangles, in their original order.
    triangles: Vec<u32>,
    /// Per number of triangles left (minus one), the vertex that got it last; the others follow
    /// through `next`.
    head: Vec<u32>,
    next: Vec<u32>,
    previous: Vec<u32>,
}

impl Vertices {
    fn new(vertex_count: usize, triangles: &[[u32; 3]]) -> Self {
        let mut count = vec![0u32; vertex_count];
        for t in triangles {
            for &v in t {
                count[v as usize] += 1;
            }
        }
        let mut offset = vec![0u32; vertex_count];
        let (mut total, mut most) = (0, 0);
        for v in 0..vertex_count {
            offset[v] = total;
            total += count[v];
            most = most.max(count[v]);
        }
        let mut fill = offset.clone();
        let mut list = vec![0u32; total as usize];
        for (i, t) in triangles.iter().enumerate() {
            for &v in t {
                list[fill[v as usize] as usize] = i as u32;
                fill[v as usize] += 1;
            }
        }
        let mut vertices = Self {
            count,
            offset,
            triangles: list,
            head: vec![NONE; most as usize],
            next: vec![NONE; vertex_count],
            previous: vec![NONE; vertex_count],
        };
        for v in 0..vertex_count as u32 {
            if vertices.count[v as usize] > 0 {
                vertices.insert(v);
            }
        }
        vertices
    }

    fn remaining(&self, v: u32) -> &[u32] {
        let start = self.offset[v as usize] as usize;
        &self.triangles[start..start + self.count[v as usize] as usize]
    }

    /// Puts the vertex first among those with its number of triangles.
    fn insert(&mut self, v: u32) {
        let bucket = self.count[v as usize] as usize - 1;
        let first = self.head[bucket];
        if first != NONE {
            self.previous[first as usize] = v;
        }
        self.next[v as usize] = first;
        self.previous[v as usize] = NONE;
        self.head[bucket] = v;
    }

    /// Takes the triangle from the vertex's list and moves the vertex to its new group. A vertex
    /// named twice by the triangle comes here twice: the second time finds nothing to take.
    fn remove(&mut self, v: u32, triangle: u32) {
        let (start, count) = (self.offset[v as usize] as usize, self.count[v as usize]);
        let mut kept = 0;
        for i in 0..count as usize {
            let t = self.triangles[start + i];
            if t != triangle {
                self.triangles[start + kept] = t;
                kept += 1;
            }
        }
        if count > 0 && self.head[count as usize - 1] == v {
            self.head[count as usize - 1] = self.next[v as usize];
        }
        let (next, previous) = (self.next[v as usize], self.previous[v as usize]);
        if next != NONE {
            self.previous[next as usize] = previous;
        }
        if previous != NONE {
            self.next[previous as usize] = next;
        }
        self.count[v as usize] = kept as u32;
        if kept > 0 {
            self.insert(v);
        }
    }

    /// How many of the triangle's corners are their vertex's last triangle.
    fn last_uses(&self, t: [u32; 3]) -> usize {
        t.iter().filter(|&&v| self.count[v as usize] == 1).count()
    }
}

/// The triangles in cache order: the indices of `triangles` (vertex numbers below
/// `vertex_count`) in the order they are drawn.
pub fn triangle_order(vertex_count: usize, triangles: &[[u32; 3]]) -> Vec<u32> {
    let mut vertices = Vertices::new(vertex_count, triangles);
    // Triangles that are some vertex's last: with two such corners, and with one.
    let mut pending: [Vec<u32>; 2] = [Vec::new(), Vec::new()];
    let mut taken = [0usize; 2];
    for (i, &t) in triangles.iter().enumerate() {
        match vertices.last_uses(t) {
            0 => {}
            1 => pending[1].push(i as u32),
            _ => pending[0].push(i as u32),
        }
    }
    let mut cache = [NONE; CACHE_SIZE];
    let mut cached = vec![false; vertex_count];
    let mut done = vec![false; triangles.len()];
    // The cache's oldest entry.
    let mut oldest = 0usize;
    let mut order = Vec::with_capacity(triangles.len());
    for _ in 0..triangles.len() {
        let mut best = NONE;
        let (mut best_hits, mut best_score) = (-1i32, -1i32);
        for i in 0..CACHE_SIZE {
            let v = cache[(oldest + i) % CACHE_SIZE];
            if v == NONE {
                continue;
            }
            for &candidate in vertices.remaining(v) {
                let t = triangles[candidate as usize];
                let hits = t.iter().filter(|&&c| cached[c as usize]).count() as i32;
                if hits < best_hits {
                    continue;
                }
                let score: i32 = t
                    .iter()
                    .map(|&c| valence_score(vertices.count[c as usize]))
                    .sum();
                if hits > best_hits || score > best_score {
                    (best, best_hits, best_score) = (candidate, hits, score);
                }
            }
        }
        if best == NONE {
            'queues: for (queue, taken) in pending.iter().zip(&mut taken) {
                while *taken < queue.len() {
                    let candidate = queue[*taken];
                    *taken += 1;
                    if !done[candidate as usize] {
                        best = candidate;
                        break 'queues;
                    }
                }
            }
        }
        if best == NONE {
            let v = vertices
                .head
                .iter()
                .copied()
                .find(|&v| v != NONE)
                .expect("a triangle is left, so a vertex has one");
            best = vertices.remaining(v)[0];
        }
        let t = triangles[best as usize];
        done[best as usize] = true;
        for &v in &t {
            vertices.remove(v, best);
        }
        for &v in &t {
            if vertices.count[v as usize] == 1 {
                let last = vertices.remaining(v)[0];
                match vertices.last_uses(triangles[last as usize]) {
                    1 => pending[1].push(last),
                    2 => pending[0].push(last),
                    _ => {}
                }
            }
        }
        // The entries that are not the triangle's corners move towards the newest end, then
        // the triangle replaces the three oldest.
        let back = |i: usize| if i == 0 { CACHE_SIZE - 1 } else { i - 1 };
        let (mut write, mut read) = (back(oldest), back(oldest));
        for _ in 0..CACHE_SIZE {
            let entry = cache[read];
            if !t.contains(&entry) {
                cache[read] = NONE;
                cache[write] = entry;
                write = back(write);
            }
            read = back(read);
        }
        let slots = [0, 1, 2].map(|k| (oldest + k) % CACHE_SIZE);
        for slot in slots {
            if cache[slot] != NONE {
                cached[cache[slot] as usize] = false;
            }
        }
        for (slot, &v) in slots.into_iter().zip(&t) {
            cache[slot] = v;
            cached[v as usize] = true;
        }
        oldest = (oldest + 3) % CACHE_SIZE;
        order.push(best);
    }
    order
}

/// Reorders every sub-mesh's triangles (see the module documentation).
pub fn order_polygons(submeshes: &mut [Vec<u32>]) {
    order_polygons_in_parts(submeshes, TRIANGLES_PER_PART);
}

fn order_polygons_in_parts(submeshes: &mut [Vec<u32>], triangles_per_part: usize) {
    for submesh in submeshes {
        let count = submesh.len() / 3;
        if count == 0 {
            continue;
        }
        let parts = (count - 1) / triangles_per_part + 1;
        let size = count / parts;
        for part in 0..parts {
            let start = part * size;
            let end = if part + 1 < parts {
                start + size
            } else {
                count
            };
            let indices = &mut submesh[3 * start..3 * end];
            // Vertex numbers from the part's lowest: their order is all the ordering reads.
            let lowest = indices.iter().copied().min().unwrap_or(0);
            let highest = indices.iter().copied().max().unwrap_or(0);
            let triangles: Vec<[u32; 3]> = indices
                .chunks_exact(3)
                .map(|t| [t[0] - lowest, t[1] - lowest, t[2] - lowest])
                .collect();
            let order = triangle_order((highest - lowest) as usize + 1, &triangles);
            for (slot, &from) in order.iter().enumerate() {
                let t = triangles[from as usize];
                for k in 0..3 {
                    indices[3 * slot + k] = t[k] + lowest;
                }
            }
        }
    }
}

/// Renumbers the mesh's vertices in the order its triangles first use them; vertices no triangle
/// uses keep their order after the others.
pub fn order_vertices(mesh: &mut ModelMesh) {
    let count = mesh.positions.len();
    let mut new_index = vec![NONE; count];
    // Per new vertex, the old one.
    let mut from: Vec<u32> = Vec::with_capacity(count);
    for &v in mesh.submeshes.iter().flatten() {
        if new_index[v as usize] == NONE {
            new_index[v as usize] = from.len() as u32;
            from.push(v);
        }
    }
    for v in 0..count as u32 {
        if new_index[v as usize] == NONE {
            new_index[v as usize] = from.len() as u32;
            from.push(v);
        }
    }
    if from.iter().enumerate().all(|(i, &v)| i as u32 == v) {
        return;
    }
    fn permute<T: Copy>(values: &mut Vec<T>, from: &[u32]) {
        if values.len() == from.len() {
            *values = from.iter().map(|&v| values[v as usize]).collect();
        }
    }
    permute(&mut mesh.positions, &from);
    permute(&mut mesh.normals, &from);
    permute(&mut mesh.tangents, &from);
    permute(&mut mesh.colors, &from);
    permute(&mut mesh.bone_weights, &from);
    for uvs in &mut mesh.uvs {
        permute(uvs, &from);
    }
    for frame in mesh.blend_shapes.iter_mut().flat_map(|s| &mut s.frames) {
        permute(&mut frame.vertices, &from);
        permute(&mut frame.normals, &from);
        permute(&mut frame.tangents, &from);
    }
    for index in mesh.submeshes.iter_mut().flatten() {
        *index = new_index[*index as usize];
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ordered(triangles: &[[u32; 3]]) -> Vec<[u32; 3]> {
        let vertices = triangles.iter().flatten().max().map_or(0, |&v| v + 1);
        triangle_order(vertices as usize, triangles)
            .into_iter()
            .map(|t| triangles[t as usize])
            .collect()
    }

    #[test]
    fn a_strip_of_quads_is_drawn_from_its_end_with_each_quad_reversed() {
        // The strip starts at a triangle that is some vertex's only one.
        let strip = [
            [0, 2, 3],
            [0, 3, 1],
            [2, 4, 5],
            [2, 5, 3],
            [4, 6, 7],
            [4, 7, 5],
        ];
        assert_eq!(
            ordered(&strip),
            [
                [0, 3, 1],
                [0, 2, 3],
                [2, 5, 3],
                [2, 4, 5],
                [4, 7, 5],
                [4, 6, 7]
            ]
        );
    }

    #[test]
    fn the_editors_order_of_a_recorded_sub_mesh() {
        // lightmapuvs Multi, first sub-mesh: RawOff's triangles and Off's, in RawOff's numbers.
        let raw = [
            25, 11, 3, 25, 3, 10, 23, 17, 5, 23, 5, 16, 39, 40, 17, 39, 17, 23, 41, 39, 23, 41, 23,
            42, 42, 23, 16, 42, 16, 43, 49, 50, 11, 49, 11, 25, 51, 49, 25, 51, 25, 52, 52, 25, 10,
            52, 10, 53,
        ];
        let editor = [
            39, 40, 17, 39, 17, 23, 41, 39, 23, 23, 17, 5, 41, 23, 42, 23, 5, 16, 42, 23, 16, 42,
            16, 43, 49, 50, 11, 49, 11, 25, 51, 49, 25, 25, 11, 3, 51, 25, 52, 25, 3, 10, 52, 25,
            10, 52, 10, 53,
        ];
        let mut submeshes = vec![raw.to_vec()];
        order_polygons(&mut submeshes);
        assert_eq!(submeshes[0], editor);
    }

    #[test]
    fn separate_triangles_keep_their_order() {
        let apart = [[0, 1, 2], [3, 4, 5], [6, 7, 8]];
        assert_eq!(ordered(&apart), apart);
    }

    #[test]
    fn every_triangle_is_drawn_once_with_its_corners_in_order() {
        // A fan, a grid and triangles that name a vertex twice, numbered irregularly.
        let mut triangles: Vec<[u32; 3]> = (1..40).map(|i| [0, i, i + 1]).collect();
        for y in 0..12u32 {
            for x in 0..12u32 {
                let a = 50 + y * 13 + x;
                triangles.push([a, a + 1, a + 14]);
                triangles.push([a, a + 14, a + 13]);
            }
        }
        triangles.extend([[7, 7, 9], [300, 301, 300], [5, 5, 5], [400, 400, 400]]);
        let out = ordered(&triangles);
        let mut sorted_in = triangles.clone();
        let mut sorted_out = out.clone();
        sorted_in.sort_unstable();
        sorted_out.sort_unstable();
        assert_eq!(sorted_in, sorted_out);
        // Drawn through a cache, the order misses no more often than the input does.
        let misses = |order: &[[u32; 3]]| {
            let mut cache: Vec<u32> = Vec::new();
            let mut misses = 0;
            for &v in order.iter().flatten() {
                if !cache.contains(&v) {
                    misses += 1;
                    cache.push(v);
                    if cache.len() > CACHE_SIZE {
                        cache.remove(0);
                    }
                }
            }
            misses
        };
        assert!(misses(&out) <= misses(&triangles));
    }

    #[test]
    fn a_sub_mesh_above_the_part_size_is_ordered_in_equal_parts() {
        // Seven separate pairs of triangles sharing an edge, listed far triangle first; with
        // parts of at most five triangles there are three parts of 4, 4 and 6.
        let mut indices = Vec::new();
        for i in 0..7u32 {
            let a = 10 * i;
            indices.extend([a, a + 1, a + 2, a + 2, a + 1, a + 3]);
        }
        let whole = {
            let mut s = vec![indices.clone()];
            order_polygons(&mut s);
            s.remove(0)
        };
        let mut parted = vec![indices.clone()];
        order_polygons_in_parts(&mut parted, 5);
        let part = |range: std::ops::Range<usize>| {
            let mut s = vec![indices[range].to_vec()];
            order_polygons(&mut s);
            s.remove(0)
        };
        let expected: Vec<u32> = [part(0..12), part(12..24), part(24..42)].concat();
        assert_eq!(parted[0], expected);
        assert_eq!(whole.len(), expected.len());
    }

    #[test]
    fn sub_meshes_are_ordered_apart_and_vertices_follow_their_first_use() {
        let mut submeshes = vec![
            vec![10, 12, 13, 10, 13, 11, 12, 14, 15, 12, 15, 13],
            vec![3, 4, 5],
        ];
        order_polygons(&mut submeshes);
        assert_eq!(
            submeshes,
            [
                vec![10, 13, 11, 10, 12, 13, 12, 15, 13, 12, 14, 15],
                vec![3, 4, 5]
            ]
        );
        let mut mesh = ModelMesh {
            name: String::new(),
            file_id: (0, super::super::FileIdSource::Derived).into(),
            positions: (0..16).map(|i| [i as f32, 0.0, 0.0]).collect(),
            normals: Vec::new(),
            tangents: Vec::new(),
            uvs: vec![(0..16).map(|i| [i as f32, 1.0]).collect()],
            colors: Vec::new(),
            submeshes,
            bone_weights: Vec::new(),
            bind_poses: Vec::new(),
            bounds_min: [0.0; 3],
            bounds_max: [0.0; 3],
            blend_shapes: Vec::new(),
        };
        order_vertices(&mut mesh);
        assert_eq!(
            mesh.submeshes,
            [vec![0, 1, 2, 0, 3, 1, 3, 4, 1, 3, 5, 4], vec![6, 7, 8]]
        );
        let firsts: Vec<f32> = mesh.positions.iter().map(|p| p[0]).collect();
        // Used vertices in first-use order, then the unused ones in their own order.
        assert_eq!(
            firsts,
            [
                10.0, 13.0, 11.0, 12.0, 15.0, 14.0, 3.0, 4.0, 5.0, 0.0, 1.0, 2.0, 6.0, 7.0, 8.0,
                9.0
            ]
        );
        assert!(
            mesh.uvs[0]
                .iter()
                .zip(&mesh.positions)
                .all(|(uv, p)| uv[0] == p[0])
        );
    }
}
