//! The stitching of a renderer's lightmap UV seams (MeshRenderer.stitchLightmapSeams; matches the
//! Unity 6000.0.71f1 Editor's Progressive CPU lightmapper, tests/projects/bakedbuffers
//! `lightmap`): where two triangles share an edge in space with different lightmap UVs on either
//! side (and the same normal at the shared vertex: not a hard edge), the lightmap is changed as
//! little as possible so that the two sides read the same light along the edge. Per seam the
//! edge is sampled on both sides; the texels the samples read (bilinearly) are solved for, per
//! channel, in the least squares sense: the differences between the two sides' samples, and
//! each texel's change from its light, all weigh the same. The documentation speaks of
//! horizontal and vertical chart edges; slanted ones are stitched alike in the Editor (the
//! built-in Cylinder's and Sphere's, bakedbuffers `Shapes`).

use std::collections::HashMap;

/// A seam: an edge on one side (`a`, from its first end to its second) and the same edge in
/// space on the other side (`b`, its ends in the same order), as UV points of the mesh.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct EdgePair {
    pub a: [[f32; 2]; 2],
    pub b: [[f32; 2]; 2],
}

/// Samples along a seam per texel of its longer side.
const SAMPLES_PER_TEXEL: f32 = 3.0;
/// The length (in texels) a seam's samples take at least.
const MIN_LENGTH: f32 = 2.0;
/// The solve stops where the residual's length is below this.
const TOLERANCE: f32 = 0.01;
/// And after this many conjugate gradient steps.
const MAX_STEPS: usize = 50;

/// The seams of a mesh (its local positions, normals and lightmap UVs; its triangles, every
/// submesh's in order): each triangle edge whose reversed edge (the same two positions, exactly)
/// an earlier triangle has, with other UVs and the same normal at the shared vertex where this
/// edge starts (the normals at its other end are not compared). An edge's match is used once; of
/// two open edges with the same ends in the same direction the later stands (neither measured).
/// A triangle naming a vertex the mesh lacks is passed over.
pub fn edge_pairs(
    positions: &[[f32; 3]],
    normals: &[[f32; 3]],
    uvs: &[[f32; 2]],
    triangles: impl IntoIterator<Item = [u32; 3]>,
) -> Vec<EdgePair> {
    // An edge by its two positions' bits (the same bits: -0 and 0 are other positions; a
    // position that is not a number never matches); its UVs at both ends and the normal at its
    // second.
    type Key = [u32; 6];
    let key = |from: [f32; 3], to: [f32; 3]| -> Option<Key> {
        (from.iter().chain(&to).all(|v| !v.is_nan()))
            .then(|| std::array::from_fn(|k| if k < 3 { from[k] } else { to[k - 3] }.to_bits()))
    };
    let mut open: HashMap<Key, ([f32; 2], [f32; 2], [f32; 3])> = HashMap::new();
    let mut out = Vec::new();
    for triangle in triangles {
        for k in 0..3 {
            let (v0, v1) = (triangle[k] as usize, triangle[(k + 1) % 3] as usize);
            let have = |v: usize| v < positions.len() && v < normals.len() && v < uvs.len();
            if !have(v0) || !have(v1) {
                continue;
            }
            let reversed = key(positions[v1], positions[v0]);
            match reversed.and_then(|r| open.remove(&r).map(|o| (r, o))) {
                None => {
                    if let Some(forward) = key(positions[v0], positions[v1]) {
                        open.insert(forward, (uvs[v0], uvs[v1], normals[v1]));
                    }
                }
                Some((_, (other0, other1, other_normal))) => {
                    let continuous = other1 == uvs[v0] && other0 == uvs[v1];
                    if !continuous && other_normal == normals[v0] {
                        out.push(EdgePair {
                            a: [uvs[v0], uvs[v1]],
                            b: [other1, other0],
                        });
                    }
                }
            }
        }
    }
    out
}

/// A coordinate's bilinear footprint along an axis of `size` texels: the first texel and the
/// weight toward the second (the coordinate kept within the texel centres, below the last one).
fn footprint(p: f32, last: f32) -> (usize, f32) {
    let f = if p < 0.5 { 0.0 } else { p.min(last) + -0.5 };
    let base = f as usize;
    (base, f - base as f32)
}

/// The largest float below `size - 0.5` (toward zero).
fn below_last_centre(size: f32) -> f32 {
    let centre = size + -0.5;
    if centre > 0.0 {
        f32::from_bits(centre.to_bits() - 1)
    } else {
        centre
    }
}

/// Stitches the seams of one renderer, `pairs`, placed in the lightmap by `scale_offset`
/// (x and y scale, x and y offset, as Renderer.lightmapScaleOffset), on `image`, `width` ×
/// `height` texels from the first row (lightmap v 0): its first `channels` channels, kept within
/// [0, `upper`]. All in 32-bit floats as the Editor computes them.
pub fn stitch(
    image: &mut [[f32; 4]],
    width: usize,
    height: usize,
    pairs: &[EdgePair],
    scale_offset: [f32; 4],
    channels: usize,
    upper: f32,
) {
    // (A lightmap of one texel a side has no bilinear footprint: never one the bake makes.)
    if pairs.is_empty() || width < 2 || height < 2 {
        return;
    }
    let (w, h) = (width as f32, height as f32);
    let [sx, sy, ox, oy] = scale_offset;
    let (last_x, last_y) = (below_last_centre(w), below_last_centre(h));
    let placed = |p: [f32; 2]| [p[0] * sx + ox, p[1] * sy + oy];
    // Per seam its sample count and each side's first sample and step, in texels.
    struct Plan {
        count: usize,
        sides: [([f32; 2], [f32; 2]); 2],
    }
    let plans: Vec<Plan> = pairs
        .iter()
        .map(|pair| {
            let ends = [pair.a, pair.b].map(|[p0, p1]| (placed(p0), placed(p1)));
            let length = |(p0, p1): ([f32; 2], [f32; 2])| {
                let dx = p1[0] * w - p0[0] * w;
                let dy = p1[1] * h - p0[1] * h;
                (dy * dy + dx * dx).sqrt()
            };
            let longer = length(ends[0]).max(length(ends[1])).max(MIN_LENGTH);
            let count = (longer * SAMPLES_PER_TEXEL) as usize;
            let steps = (count - 1) as f32;
            let sides = ends.map(|(p0, p1)| {
                let start = [p0[0] * w, p0[1] * h];
                (
                    start,
                    [
                        (p1[0] * w - start[0]) / steps,
                        (p1[1] * h - start[1]) / steps,
                    ],
                )
            });
            Plan { count, sides }
        })
        .collect();
    let sample = |(start, step): ([f32; 2], [f32; 2]), i: usize| {
        [step[0] * i as f32 + start[0], step[1] * i as f32 + start[1]]
    };
    // The texels the samples read, numbered as first read (side a's samples, then side b's,
    // seam by seam), with their light then.
    let mut number: HashMap<(usize, usize), usize> = HashMap::new();
    let mut texels: Vec<usize> = Vec::new();
    for plan in &plans {
        for side in plan.sides {
            for i in 0..plan.count {
                let p = sample(side, i);
                let (bx, _) = footprint(p[0], last_x);
                let (by, _) = footprint(p[1], last_y);
                for k in 0..4 {
                    let t = (bx + (k & 1), by + (k >> 1));
                    number.entry(t).or_insert_with(|| {
                        texels.push(t.1 * width + t.0);
                        texels.len() - 1
                    });
                }
            }
        }
    }
    let weights = |p: [f32; 2]| -> ([usize; 4], [f32; 4]) {
        let (bx, fx) = footprint(p[0], last_x);
        let (by, fy) = footprint(p[1], last_y);
        let mut ids = [0; 4];
        let mut ws = [0.0; 4];
        for k in 0..4 {
            let (dx, dy) = (k & 1, k >> 1);
            ids[k] = number[&(bx + dx, by + dy)];
            let wy = (2.0 * dy as f32 - 1.0) * fy + (1 - dy) as f32;
            let wx = (2.0 * dx as f32 - 1.0) * fx + (1 - dx) as f32;
            ws[k] = wy * wx;
        }
        (ids, ws)
    };
    // The normal equations, I + Σ d dᵀ with d the two sides' weights (b's negated): rows of
    // (column, value) in the order their entries are first added.
    let n = texels.len();
    let mut rows: Vec<Vec<(usize, f32)>> = vec![Vec::new(); n];
    let mut add = |r: usize, c: usize, v: f32| match rows[r].iter_mut().find(|e| e.0 == c) {
        Some(entry) => entry.1 += v,
        None => rows[r].push((c, 0.0 + v)),
    };
    for plan in &plans {
        for i in 0..plan.count {
            let (ia, wa) = weights(sample(plan.sides[0], i));
            let (ib, wb) = weights(sample(plan.sides[1], i));
            for j in 0..4 {
                for k in 0..4 {
                    add(ia[j], ia[k], wa[j] * wa[k]);
                    add(ib[j], ib[k], wb[j] * wb[k]);
                    add(ia[j], ib[k], -(wb[k] * wa[j]));
                    add(ib[j], ia[k], -(wa[k] * wb[j]));
                }
            }
        }
    }
    for i in 0..n {
        add(i, i, 1.0);
    }
    let product = |x: &[f32], out: &mut [f32]| {
        for (row, o) in rows.iter().zip(out.iter_mut()) {
            let mut acc = 0.0f32;
            for &(c, v) in row {
                acc += v * x[c];
            }
            *o = acc;
        }
    };
    let dot = |a: &[f32], b: &[f32]| a.iter().zip(b).fold(0.0f32, |acc, (x, y)| acc + x * y);
    let tolerance = TOLERANCE * TOLERANCE;
    let (mut ap, mut r, mut p) = (vec![0.0; n], vec![0.0; n], vec![0.0; n]);
    for c in 0..channels {
        // Conjugate gradients from the texels' light.
        let b: Vec<f32> = texels.iter().map(|&t| image[t][c]).collect();
        let mut x = b.clone();
        product(&x, &mut ap);
        for i in 0..n {
            r[i] = b[i] - ap[i];
        }
        let mut rr = dot(&r, &r);
        // (Light that is not a finite number keeps the texels as they are: not measured.)
        if !rr.is_finite() {
            continue;
        }
        if tolerance <= rr {
            p.copy_from_slice(&r);
            for _ in 0..MAX_STEPS {
                product(&p, &mut ap);
                let alpha = rr / dot(&p, &ap);
                for i in 0..n {
                    x[i] = p[i] * alpha + x[i];
                    r[i] = -(ap[i] * alpha) + r[i];
                }
                let rn = dot(&r, &r);
                if rn < tolerance {
                    break;
                }
                let beta = rn / rr;
                for i in 0..n {
                    p[i] = r[i] + p[i] * beta;
                }
                rr = rn;
            }
        }
        for (i, &t) in texels.iter().enumerate() {
            image[t][c] = if x[i] < 0.0 { 0.0 } else { x[i].min(upper) };
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Two quads side by side in space, sharing the edge x = 1, their lightmap UVs apart.
    fn two_quads(
        normal_right: [f32; 3],
    ) -> (Vec<[f32; 3]>, Vec<[f32; 3]>, Vec<[f32; 2]>, Vec<[u32; 3]>) {
        let positions = vec![
            [0.0, 0.0, 0.0],
            [1.0, 0.0, 0.0],
            [1.0, 1.0, 0.0],
            [0.0, 1.0, 0.0],
            [1.0, 0.0, 0.0],
            [2.0, 0.0, 0.0],
            [2.0, 1.0, 0.0],
            [1.0, 1.0, 0.0],
        ];
        let up = [0.0, 0.0, -1.0];
        let normals = vec![
            up,
            up,
            up,
            up,
            normal_right,
            normal_right,
            normal_right,
            normal_right,
        ];
        let uvs = vec![
            [0.1, 0.1],
            [0.4, 0.1],
            [0.4, 0.4],
            [0.1, 0.4],
            [0.6, 0.1],
            [0.9, 0.1],
            [0.9, 0.4],
            [0.6, 0.4],
        ];
        let triangles = vec![[0, 1, 2], [0, 2, 3], [4, 5, 6], [4, 6, 7]];
        (positions, normals, uvs, triangles)
    }

    #[test]
    fn a_shared_edge_with_other_uvs_is_a_seam_and_a_hard_edge_is_not() {
        let (positions, normals, uvs, triangles) = two_quads([0.0, 0.0, -1.0]);
        let pairs = edge_pairs(&positions, &normals, &uvs, triangles.clone());
        // The right quad's edge from (1,1) to (1,0) meets the left quad's from (1,0) to (1,1):
        // the pair runs the same way in space on both sides.
        assert_eq!(
            pairs,
            vec![EdgePair {
                a: [[0.6, 0.4], [0.6, 0.1]],
                b: [[0.4, 0.4], [0.4, 0.1]]
            }]
        );
        // The same UVs on both sides: no seam.
        let mut joined = uvs.clone();
        joined[4] = uvs[1];
        joined[7] = uvs[2];
        assert!(edge_pairs(&positions, &normals, &joined, triangles.clone()).is_empty());
        // Another normal at the shared vertex: a hard edge, not stitched.
        let (positions, normals, uvs, triangles) = two_quads([0.0, -1.0, 0.0]);
        assert!(edge_pairs(&positions, &normals, &uvs, triangles).is_empty());
    }

    #[test]
    fn a_triangle_naming_a_missing_vertex_is_passed_over() {
        let (positions, normals, uvs, mut triangles) = two_quads([0.0, 0.0, -1.0]);
        triangles.push([0, 1, 99]);
        assert_eq!(edge_pairs(&positions, &normals, &uvs, triangles).len(), 1);
    }

    #[test]
    fn light_that_is_not_a_number_is_kept() {
        let (positions, normals, uvs, triangles) = two_quads([0.0, 0.0, -1.0]);
        let pairs = edge_pairs(&positions, &normals, &uvs, triangles);
        let mut image = vec![[0.5, 0.5, 0.5, 1.0]; 16 * 16];
        image[4 * 16 + 6] = [f32::NAN, 0.5, 0.5, 1.0];
        stitch(
            &mut image,
            16,
            16,
            &pairs,
            [1.0, 1.0, 0.0, 0.0],
            3,
            f32::MAX,
        );
        // Red is solved with a texel that is not a number: it keeps its texels; green and blue
        // are solved.
        assert!(image.iter().all(|t| t[0].is_nan() || t[0] == 0.5));
        assert!(image.iter().all(|t| t[1] < f32::MAX));
    }

    #[test]
    fn stitching_draws_the_two_sides_together_and_changes_nothing_else() {
        let (positions, normals, uvs, triangles) = two_quads([0.0, 0.0, -1.0]);
        let pairs = edge_pairs(&positions, &normals, &uvs, triangles);
        let (width, height) = (16, 16);
        // Left chart dark, right chart bright.
        let mut image: Vec<[f32; 4]> = (0..width * height)
            .map(|k| {
                if k % width < 8 {
                    [0.2, 0.2, 0.2, 1.0]
                } else {
                    [1.0, 0.5, 0.0, 1.0]
                }
            })
            .collect();
        let before = image.clone();
        stitch(
            &mut image,
            width,
            height,
            &pairs,
            [1.0, 1.0, 0.0, 0.0],
            3,
            f32::MAX,
        );
        let changed: Vec<usize> = (0..image.len())
            .filter(|&k| image[k] != before[k])
            .collect();
        assert!(!changed.is_empty());
        // Only texels next to the two seam sides (x 0.4 and 0.6 of 16: texels 5..7 and 8..10)
        // change, alpha never, and nothing goes below zero.
        for &k in &changed {
            let x = k % width;
            assert!((5..=10).contains(&x), "texel {x} changed");
            assert_eq!(image[k][3], 1.0);
            assert!(image[k].iter().all(|&v| v >= 0.0));
        }
        // The sides' reads along the seam come closer.
        let read = |img: &[[f32; 4]], x: usize, y: usize| img[y * width + x][0];
        let gap = |img: &[[f32; 4]]| (read(img, 6, 4) - read(img, 9, 4)).abs();
        assert!(gap(&image) < gap(&before));
        // Clamped above (an LDR image).
        let mut ldr = before.clone();
        stitch(
            &mut ldr,
            width,
            height,
            &pairs,
            [1.0, 1.0, 0.0, 0.0],
            4,
            1.0,
        );
        assert!(ldr.iter().flatten().all(|&v| (0.0..=1.0).contains(&v)));
    }
}
