//! The lightmap a bake makes of its texel buffers (matches the Unity 6000.0.71f1 Editor's
//! Progressive CPU lightmapper without filtering, tests/projects/bakedbuffers `lightmap`): each
//! used texel's direct, indirect and sky's light summed; the texels too many of whose rays met a
//! back face (Lightmap Parameters' Backface Tolerance) given their neighbours' light instead;
//! and the texels around the charts given their neighbours' light, as many texels out as the
//! Lighting Settings' padding, so that filtering at a chart's edge reads the chart's light. Not
//! made: the stitching of seams (texels along a renderer's UV seams and their neighbours), the
//! texels further out.

/// Light from these neighbours (the eight around a texel).
const AROUND: [(isize, isize); 8] = [
    (-1, -1),
    (0, -1),
    (1, -1),
    (-1, 0),
    (1, 0),
    (-1, 1),
    (0, 1),
    (1, 1),
];

/// The buffers of one lightmap, `width` × `height` texels, rows from the first.
pub struct Buffers<'a> {
    pub width: usize,
    pub height: usize,
    /// Whether each texel is used (a chart covers it).
    pub used: &'a [bool],
    /// Per texel its direct light (red, green, blue, and a fourth number).
    pub direct: &'a [[f32; 4]],
    /// Per texel its indirect light, where it was baked.
    pub indirect: Option<&'a [[f32; 4]]>,
    /// Per texel the sky's light straight on it, where it was baked.
    pub environment: Option<&'a [[f32; 4]]>,
    /// Per texel the part of its rays that met a back face first, where it was counted.
    pub validity: Option<&'a [f32]>,
}

/// The lightmap's texels: red, green, blue, and 1 for a texel that holds light (a used one or
/// one within `padding` texels of a used one), 0 for the others (which hold none).
/// `backface_tolerance` is the part of a texel's rays that must meet front faces for its own
/// light to stand.
pub fn composite(buffers: &Buffers, backface_tolerance: f32, padding: u32) -> Vec<[f32; 4]> {
    let (width, height) = (buffers.width, buffers.height);
    let texels = width * height;
    let mut out = vec![[0.0f32; 4]; texels];
    for (k, texel) in out.iter_mut().enumerate() {
        if !buffers.used[k] {
            continue;
        }
        for c in 0..3 {
            texel[c] = buffers.direct[k][c]
                + buffers.indirect.map_or(0.0, |b| b[k][c])
                + buffers.environment.map_or(0.0, |b| b[k][c]);
        }
        texel[3] = 1.0;
    }
    // The texels whose light does not stand (more of their rays met back faces than the
    // tolerance allows): given their neighbours' light. (A group of them that no texel holding
    // its own light reaches keeps its own light, and holds none for the rings around it: not
    // measured.)
    let invalid = 1.0 - backface_tolerance;
    let mut holds: Vec<bool> = (0..texels)
        .map(|k| buffers.used[k] && buffers.validity.is_none_or(|v| !(v[k] > invalid)))
        .collect();
    let replaced: Vec<bool> = (0..texels).map(|k| buffers.used[k] && !holds[k]).collect();
    spread(&mut out, &mut holds, width, height, replaced, usize::MAX);
    // The texels around the charts, `padding` rings of them.
    let around: Vec<bool> = buffers.used.iter().map(|&u| !u).collect();
    spread(
        &mut out,
        &mut holds,
        width,
        height,
        around,
        padding as usize,
    );
    out
}

/// The texels beside texel `k` (the eight around it, within the lightmap).
fn beside(k: usize, width: usize, height: usize) -> impl Iterator<Item = usize> {
    let (x, y) = ((k % width) as isize, (k / width) as isize);
    AROUND.into_iter().filter_map(move |(dx, dy)| {
        let (nx, ny) = (x + dx, y + dy);
        (nx >= 0 && ny >= 0 && nx < width as isize && ny < height as isize)
            .then(|| ny as usize * width + nx as usize)
    })
}

/// Gives the `waiting` texels light, ring by ring (at most `rings` of them): in each, every
/// waiting texel beside a texel that holds light takes the mean of those neighbours' light as
/// it was before the ring, and then holds light itself. Each ring looks only at the texels
/// beside the last one's (the first: beside a texel that holds light).
fn spread(
    out: &mut [[f32; 4]],
    holds: &mut [bool],
    width: usize,
    height: usize,
    waiting: Vec<bool>,
    rings: usize,
) {
    // Whether a texel is in a ring already (the one being made or the next).
    let mut queued = vec![false; waiting.len()];
    let mut ring: Vec<usize> = (0..waiting.len())
        .filter(|&k| waiting[k] && beside(k, width, height).any(|n| holds[n]))
        .collect();
    for &k in &ring {
        queued[k] = true;
    }
    let mut taken: Vec<(usize, [f32; 3])> = Vec::new();
    for _ in 0..rings {
        if ring.is_empty() {
            break;
        }
        taken.clear();
        for &k in &ring {
            let mut sum = [0.0f32; 3];
            let mut count = 0u32;
            for n in beside(k, width, height) {
                if holds[n] {
                    for c in 0..3 {
                        sum[c] += out[n][c];
                    }
                    count += 1;
                }
            }
            taken.push((k, sum.map(|s| s / count as f32)));
        }
        for &(k, light) in &taken {
            out[k] = [light[0], light[1], light[2], 1.0];
            holds[k] = true;
        }
        ring.clear();
        for &(k, _) in &taken {
            for n in beside(k, width, height) {
                if waiting[n] && !queued[n] {
                    queued[n] = true;
                    ring.push(n);
                }
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn lit(values: &[f32]) -> Vec<[f32; 4]> {
        values.iter().map(|&v| [v, v, v, 1.0]).collect()
    }

    #[test]
    fn a_used_texel_holds_its_direct_indirect_and_skys_light() {
        let used = [true, false];
        let direct = lit(&[1.0, 5.0]);
        let indirect = lit(&[0.25, 5.0]);
        let environment = lit(&[0.5, 5.0]);
        let out = composite(
            &Buffers {
                width: 2,
                height: 1,
                used: &used,
                direct: &direct,
                indirect: Some(&indirect),
                environment: Some(&environment),
                validity: Some(&[0.0, 0.0]),
            },
            0.9,
            0,
        );
        assert_eq!(out, vec![[1.75, 1.75, 1.75, 1.0], [0.0; 4]]);
    }

    #[test]
    fn a_texel_whose_rays_met_back_faces_takes_its_neighbours_light_ring_by_ring() {
        // A row of five used texels: the last two met back faces (half of their rays), the
        // third a tenth of them (as the tolerance allows).
        let used = [true; 5];
        let direct = lit(&[2.0, 4.0, 6.0, 100.0, 200.0]);
        let validity = [0.0, 0.0, 0.1, 0.5, 0.5];
        let out = composite(
            &Buffers {
                width: 5,
                height: 1,
                used: &used,
                direct: &direct,
                indirect: None,
                environment: None,
                validity: Some(&validity),
            },
            0.9,
            0,
        );
        // The fourth takes the third's light; the fifth, beside no texel whose light stands at
        // first, the fourth's as the first ring gave it.
        let red: Vec<f32> = out.iter().map(|t| t[0]).collect();
        assert_eq!(red, vec![2.0, 4.0, 6.0, 6.0, 6.0]);
    }

    #[test]
    fn back_face_texels_no_texel_reaches_keep_their_light_and_hold_none_for_the_padding() {
        // A chart of two texels whose rays all met back faces, with an unused texel beside it.
        let used = [true, true, false];
        let direct = lit(&[1.0, 2.0, 0.0]);
        let out = composite(
            &Buffers {
                width: 3,
                height: 1,
                used: &used,
                direct: &direct,
                indirect: None,
                environment: None,
                validity: Some(&[1.0, 1.0, 0.0]),
            },
            0.9,
            2,
        );
        assert_eq!(
            out,
            vec![[1.0, 1.0, 1.0, 1.0], [2.0, 2.0, 2.0, 1.0], [0.0; 4]]
        );
    }

    #[test]
    fn the_texels_around_a_chart_take_its_light_as_far_as_the_padding() {
        // A 5 x 1 row: a used texel in the middle, padding 1.
        let used = [false, false, true, false, false];
        let direct = lit(&[0.0, 0.0, 3.0, 0.0, 0.0]);
        let buffers = Buffers {
            width: 5,
            height: 1,
            used: &used,
            direct: &direct,
            indirect: None,
            environment: None,
            validity: None,
        };
        let out = composite(&buffers, 0.9, 1);
        let alpha: Vec<f32> = out.iter().map(|t| t[3]).collect();
        assert_eq!(alpha, vec![0.0, 1.0, 1.0, 1.0, 0.0]);
        assert_eq!(out[1][0], 3.0);
        // Two rings: the mean of the neighbours that hold light, ring by ring.
        let out = composite(&buffers, 0.9, 2);
        assert_eq!(out.iter().map(|t| t[0]).collect::<Vec<_>>(), vec![3.0; 5]);
        // A texel between two charts takes the mean of both.
        let used = [true, false, true];
        let direct = lit(&[1.0, 0.0, 3.0]);
        let out = composite(
            &Buffers {
                width: 3,
                height: 1,
                used: &used,
                direct: &direct,
                indirect: None,
                environment: None,
                validity: None,
            },
            0.9,
            1,
        );
        assert_eq!(out[1], [2.0, 2.0, 2.0, 1.0]);
    }
}
