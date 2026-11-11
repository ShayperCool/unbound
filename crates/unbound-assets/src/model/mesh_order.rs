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
    /// Triangle corners left per vertex.
    count: Vec<u32>,
    /// Each vertex's first remaining corner (corner `3 * triangle + k`); its other corners
    /// follow through `next_corner`, in the triangles' original order.
    first_corner: Vec<u32>,
    next_corner: Vec<u32>,
    previous_corner: Vec<u32>,
    /// Whether a corner's triangle is still to be drawn.
    left: Vec<bool>,
    /// Per number of corners left (minus one), the vertex that got it last; the others follow
    /// through `next`.
    head: Vec<u32>,
    next: Vec<u32>,
    previous: Vec<u32>,
}

impl Vertices {
    fn new(vertex_count: usize, triangles: &[[u32; 3]]) -> Self {
        let mut vertices = Self {
            count: vec![0; vertex_count],
            first_corner: vec![NONE; vertex_count],
            next_corner: vec![NONE; 3 * triangles.len()],
            previous_corner: vec![NONE; 3 * triangles.len()],
            left: vec![true; 3 * triangles.len()],
            head: Vec::new(),
            next: vec![NONE; vertex_count],
            previous: vec![NONE; vertex_count],
        };
        let mut last_corner = vec![NONE; vertex_count];
        for (corner, &v) in triangles.iter().flatten().enumerate() {
            let (v, corner) = (v as usize, corner as u32);
            vertices.count[v] += 1;
            match last_corner[v] {
                NONE => vertices.first_corner[v] = corner,
                last => {
                    vertices.next_corner[last as usize] = corner;
                    vertices.previous_corner[corner as usize] = last;
                }
            }
            last_corner[v] = corner;
        }
        let most = vertices.count.iter().copied().max().unwrap_or(0);
        vertices.head = vec![NONE; most as usize];
        for v in 0..vertex_count as u32 {
            if vertices.count[v as usize] > 0 {
                vertices.insert(v);
            }
        }
        vertices
    }

    /// The vertex's remaining triangles in their original order (one named by two of its
    /// corners comes twice).
    fn remaining(&self, v: u32) -> impl Iterator<Item = u32> + '_ {
        let mut corner = self.first_corner[v as usize];
        std::iter::from_fn(move || {
            (corner != NONE).then(|| {
                let triangle = corner / 3;
                corner = self.next_corner[corner as usize];
                triangle
            })
        })
    }

    /// Puts the vertex first among those with its number of corners.
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

    /// Takes the drawn triangle's corners from the vertex and puts the vertex first in its new
    /// group. A vertex named by two corners comes here twice: the second time finds nothing to
    /// take and only moves the vertex to the front of its group.
    fn remove(&mut self, v: u32, triangle: u32, corners: [u32; 3]) {
        let count = self.count[v as usize];
        if count == 0 {
            return;
        }
        let mut taken = 0;
        for k in 0..3 {
            let corner = 3 * triangle + k;
            if corners[k as usize] != v || !self.left[corner as usize] {
                continue;
            }
            self.left[corner as usize] = false;
            taken += 1;
            let (next, previous) = (
                self.next_corner[corner as usize],
                self.previous_corner[corner as usize],
            );
            match previous {
                NONE => self.first_corner[v as usize] = next,
                previous => self.next_corner[previous as usize] = next,
            }
            if next != NONE {
                self.previous_corner[next as usize] = previous;
            }
        }
        if self.head[count as usize - 1] == v {
            self.head[count as usize - 1] = self.next[v as usize];
        }
        let (next, previous) = (self.next[v as usize], self.previous[v as usize]);
        if next != NONE {
            self.previous[next as usize] = previous;
        }
        if previous != NONE {
            self.next[previous as usize] = next;
        }
        (self.next[v as usize], self.previous[v as usize]) = (NONE, NONE);
        self.count[v as usize] = count - taken;
        if count > taken {
            self.insert(v);
        }
    }

    /// How many of the triangle's corners are their vertex's last.
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
            for candidate in vertices.remaining(v) {
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
            best = vertices
                .remaining(v)
                .next()
                .expect("a vertex in a group has a triangle");
        }
        let t = triangles[best as usize];
        done[best as usize] = true;
        for &v in &t {
            vertices.remove(v, best, t);
        }
        for &v in &t {
            if vertices.count[v as usize] == 1 {
                let last = vertices
                    .remaining(v)
                    .next()
                    .expect("a vertex with a corner left has a triangle");
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
    // A channel the mesh does not have is empty; one it has is as long as the positions.
    fn permute<T: Copy>(values: &mut Vec<T>, from: &[u32]) {
        assert!(
            values.is_empty() || values.len() == from.len(),
            "a vertex channel of {} values for {} vertices",
            values.len(),
            from.len()
        );
        if !values.is_empty() {
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
    fn triangles_naming_a_vertex_twice_are_ordered_without_losing_any() {
        // The importer drops such triangles before this; the order is still total.
        let cases: [&[[u32; 3]]; 3] = [
            &[[0, 0, 0], [2, 3, 2], [3, 1, 1]],
            &[[0, 1, 0], [1, 0, 1], [1, 1, 0], [0, 2, 2], [2, 2, 2]],
            &[[4, 4, 5], [5, 4, 4], [4, 5, 4], [5, 5, 5], [0, 4, 5]],
        ];
        for triangles in cases {
            let mut out = ordered(triangles);
            let mut expected = triangles.to_vec();
            out.sort_unstable();
            expected.sort_unstable();
            assert_eq!(out, expected);
        }
        // A pseudo-random soup over few vertices, repeats included.
        let mut state = 12345u32;
        let mut next = |n: u32| {
            state = state.wrapping_mul(1664525).wrapping_add(1013904223);
            (state >> 16) % n
        };
        for _ in 0..500 {
            let count = 1 + next(12) as usize;
            let triangles: Vec<[u32; 3]> =
                (0..count).map(|_| [next(5), next(5), next(5)]).collect();
            let mut out = ordered(&triangles);
            let mut expected = triangles.clone();
            out.sort_unstable();
            expected.sort_unstable();
            assert_eq!(out, expected);
        }
    }

    #[test]
    fn a_large_fan_and_a_large_grid_are_ordered() {
        // 20 000 triangles around one vertex, then a grid of 80 000 triangles.
        let fan: Vec<[u32; 3]> = (1..=20_000).map(|i| [0, i, i + 1]).collect();
        assert_eq!(ordered(&fan).len(), fan.len());
        let mut grid = Vec::new();
        for y in 0..200u32 {
            for x in 0..200u32 {
                let a = y * 201 + x;
                grid.push([a, a + 1, a + 202]);
                grid.push([a, a + 202, a + 201]);
            }
        }
        assert_eq!(ordered(&grid).len(), grid.len());
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
            normals: (0..16).map(|i| [0.0, i as f32, 0.0]).collect(),
            tangents: (0..16).map(|i| [0.0, 0.0, i as f32, 1.0]).collect(),
            // The second channel is one the mesh does not have.
            uvs: vec![(0..16).map(|i| [i as f32, 1.0]).collect(), Vec::new()],
            colors: (0..16).map(|i| [i as f32, 0.0, 0.0, 1.0]).collect(),
            submeshes,
            bone_weights: (0..16)
                .map(|i| super::super::BoneWeight {
                    bones: [i, 0, 0, 0],
                    weights: [1.0, 0.0, 0.0, 0.0],
                })
                .collect(),
            bind_poses: Vec::new(),
            bounds_min: [0.0; 3],
            bounds_max: [0.0; 3],
            blend_shapes: vec![super::super::ModelBlendShape {
                name: String::new(),
                frames: vec![super::super::ModelBlendFrame {
                    weight: 100.0,
                    vertices: (0..16).map(|i| [i as f32, 2.0, 0.0]).collect(),
                    normals: (0..16).map(|i| [i as f32, 3.0, 0.0]).collect(),
                    tangents: Vec::new(),
                }],
            }],
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
        // Every channel moved with its vertex.
        let frame = &mesh.blend_shapes[0].frames[0];
        for (i, p) in mesh.positions.iter().enumerate() {
            let old = p[0];
            assert_eq!(mesh.normals[i][1], old);
            assert_eq!(mesh.tangents[i][2], old);
            assert_eq!(mesh.uvs[0][i][0], old);
            assert_eq!(mesh.colors[i][0], old);
            assert_eq!(mesh.bone_weights[i].bones[0] as f32, old);
            assert_eq!(frame.vertices[i][0], old);
            assert_eq!(frame.normals[i][0], old);
        }
        assert!(mesh.uvs[1].is_empty() && frame.tangents.is_empty());
    }
}
