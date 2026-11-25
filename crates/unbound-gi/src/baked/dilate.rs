//! The fill a bake gives each image it makes of a lightmap's texels (matches the Unity
//! 6000.0.71f1 Editor's Progressive CPU lightmapper without filtering, tests/projects/bakedbuffers
//! `lightmap`, `Directional*` and `Masks*` images): the texels whose light does not stand (more
//! of their rays met back faces than the Lightmap Parameters' Backface Tolerance allows) and then
//! the texels around the charts, as many rings out as the Lighting Settings' padding, take a
//! mean of their neighbours' light weighed by how much each holds its own.

/// The neighbours a texel is filled from, in the order their light is summed: the row above,
/// the row below, then left and right.
const NEIGHBOURS: [(isize, isize); 8] = [
    (-1, -1),
    (0, -1),
    (1, -1),
    (-1, 1),
    (0, 1),
    (1, 1),
    (-1, 0),
    (1, 0),
];

/// The factor the approximate reciprocal of a fill's weights is corrected by (exact for a weight
/// of 1; the images' texels match the Editor's with it, one level apart in many padding texels
/// without it).
const RECIPROCAL_CORRECTION: f32 = 1.000_244_259_834_289_6;

/// What a filled texel's weighed sum is multiplied by: the processor's approximate reciprocal of
/// the weights (at least 1; within 1.5 × 2⁻¹² of the exact one), corrected. (Its bits differ
/// between processors: on other than x86-64 the exact reciprocal, not measured.)
fn mean_factor(weights: f32) -> f32 {
    let weights = weights.max(1.0);
    #[cfg(target_arch = "x86_64")]
    {
        // SAFETY: SSE is part of every x86_64 processor.
        let estimate = unsafe {
            use std::arch::x86_64::{_mm_cvtss_f32, _mm_rcp_ss, _mm_set_ss};
            _mm_cvtss_f32(_mm_rcp_ss(_mm_set_ss(weights)))
        };
        estimate * RECIPROCAL_CORRECTION
    }
    #[cfg(not(target_arch = "x86_64"))]
    {
        1.0 / weights
    }
}

/// Per texel 1 where its light stands, else 0: a used texel whose rays met back faces no more
/// than `backface_tolerance` allows (not counted with a tolerance of 0) and whose four values in
/// `image` are numbers.
pub fn markers(
    used: &[bool],
    validity: Option<&[f32]>,
    backface_tolerance: f32,
    image: &[[f32; 4]],
) -> Vec<f32> {
    let limit = 1.0 - backface_tolerance;
    (0..used.len())
        .map(|k| {
            let stands = used[k]
                && (backface_tolerance <= 0.0 || validity.is_none_or(|v| !(v[k] > limit)))
                && image[k].iter().all(|v| v.is_finite());
            if stands { 1.0 } else { 0.0 }
        })
        .collect()
}

/// One pass from `src` into `dst`: a texel whose weight (fourth value) is above 0 is kept, as is
/// one outside `within` where given; every other takes the sum of its neighbours' values times
/// their weights, times [`mean_factor`] of the weights' sum, and that sum within [0, 1] as its
/// weight (a texel no neighbour holds light for comes to hold nothing: not measured). Whether any texel came
/// to hold light.
pub(crate) fn pass(
    src: &[[f32; 4]],
    dst: &mut [[f32; 4]],
    within: Option<&[bool]>,
    width: usize,
    height: usize,
) -> bool {
    dst.copy_from_slice(src);
    let mut filled = false;
    for y in 0..height {
        for x in 0..width {
            let k = y * width + x;
            if src[k][3] > 0.0 || within.is_some_and(|w| !w[k]) {
                continue;
            }
            let mut acc = [0.0f32; 4];
            let mut weights = 0.0f32;
            for (dx, dy) in NEIGHBOURS {
                let (nx, ny) = (x as isize + dx, y as isize + dy);
                if nx < 0 || ny < 0 || nx >= width as isize || ny >= height as isize {
                    continue;
                }
                let n = src[ny as usize * width + nx as usize];
                for c in 0..4 {
                    acc[c] = n[c] * n[3] + acc[c];
                }
                weights += n[3];
            }
            let r = mean_factor(weights);
            let mut out: [f32; 4] = std::array::from_fn(|c| r * acc[c]);
            // (A blend of the weighed sum's fourth value towards the weights' sum.)
            out[3] = (weights.clamp(0.0, 1.0) - out[3]) * 1.0 + out[3];
            filled |= out[3] > 0.0;
            dst[k] = out;
        }
    }
    filled
}

/// The fill of an image of `width` × `height` texels whose fourth value is each texel's weight
/// ([`markers`]): passes over the `used` texels while one fills any, then up to `padding` passes
/// over all of them.
pub fn fill(
    image: Vec<[f32; 4]>,
    used: &[bool],
    width: usize,
    height: usize,
    padding: u32,
) -> Vec<[f32; 4]> {
    // (Each pass's result is taken, the last one's too: a texel it reached with no neighbour
    // holding light holds nothing.)
    let mut current = image;
    let mut next = current.clone();
    loop {
        let filled = pass(&current, &mut next, Some(used), width, height);
        std::mem::swap(&mut current, &mut next);
        if !filled {
            break;
        }
    }
    for _ in 0..padding {
        let filled = pass(&current, &mut next, None, width, height);
        std::mem::swap(&mut current, &mut next);
        if !filled {
            break;
        }
    }
    current
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Whether two values are as near as the fill's approximate reciprocal leaves them.
    fn near(a: f32, b: f32) -> bool {
        (a - b).abs() <= 5e-4 * b.abs().max(1.0)
    }

    #[test]
    fn the_mean_factor_is_near_the_reciprocal_and_exact_for_one() {
        for n in 1..=8 {
            let f = mean_factor(n as f32);
            assert!(near(f, 1.0 / n as f32), "{n}: {f}");
        }
        // A weight of 1 (a texel filled from one neighbour) keeps its light within a bit.
        assert!((mean_factor(1.0) - 1.0).abs() <= f32::EPSILON);
        // Weights below 1 count as 1.
        assert_eq!(mean_factor(0.0), mean_factor(1.0));
        assert_eq!(mean_factor(0.5), mean_factor(1.0));
    }

    #[test]
    fn a_texel_whose_light_does_not_stand_or_is_no_number_has_no_marker() {
        let used = [true, true, true, false, true];
        let validity = [0.0, 0.5, 0.1, 0.0, 0.5];
        let image = [
            [1.0; 4],
            [1.0; 4],
            [1.0; 4],
            [1.0; 4],
            [f32::NAN, 0.0, 0.0, 0.0],
        ];
        assert_eq!(
            markers(&used, Some(&validity), 0.9, &image),
            vec![1.0, 0.0, 1.0, 0.0, 0.0]
        );
        // A tolerance of 0 counts no back faces.
        assert_eq!(
            markers(&used, Some(&validity), 0.0, &image),
            vec![1.0, 1.0, 1.0, 0.0, 0.0]
        );
    }

    #[test]
    fn the_used_texels_fill_first_then_the_padding_ring_by_ring() {
        // A row: an unused texel, two used ones (the second's light not standing), then three
        // unused ones; padding 2.
        let used = [false, true, true, false, false, false];
        let image = vec![
            [0.0; 4],
            [4.0, 4.0, 4.0, 1.0],
            [9.0, 9.0, 9.0, 0.0],
            [0.0; 4],
            [0.0; 4],
            [0.0; 4],
        ];
        let out = fill(image, &used, 6, 1, 2);
        let red: Vec<f32> = out.iter().map(|t| t[0]).collect();
        let weight: Vec<f32> = out.iter().map(|t| t[3]).collect();
        // The used texel takes its neighbour's light; the padding then reaches two texels on
        // each side as far as the row goes (the first from the used texel only).
        assert!(near(red[2], 4.0) && near(red[0], 4.0) && near(red[3], 4.0));
        assert!(near(red[4], 4.0), "{red:?}");
        assert_eq!(red[5], 0.0);
        assert_eq!(weight, vec![1.0, 1.0, 1.0, 1.0, 1.0, 0.0]);
    }

    #[test]
    fn a_texel_between_two_lit_ones_takes_their_mean_and_one_beside_none_holds_nothing() {
        // A used texel whose light does not stand between two whose light does, and a group
        // whose light does not stand that nothing reaches (left holding nothing).
        let used = [true, true, true, false, false, true, true];
        let image = vec![
            [2.0, 0.0, 0.0, 1.0],
            [7.0, 7.0, 7.0, 0.0],
            [6.0, 0.0, 0.0, 1.0],
            [0.0; 4],
            [0.0; 4],
            [5.0, 5.0, 5.0, 0.0],
            [5.0, 5.0, 5.0, 0.0],
        ];
        let out = fill(image, &used, 7, 1, 0);
        assert!(near(out[1][0], 4.0) && out[1][1] == 0.0 && out[1][3] == 1.0);
        assert_eq!(out[5], [0.0; 4]);
        assert_eq!(out[6], [0.0; 4]);
    }

    #[test]
    fn a_neighbour_weighs_by_its_weight() {
        // Two texels beside a third: one holding light fully, one by half (the weights' sum
        // 1.5 is the divisor; the filled texel's weight is that sum within [0, 1]).
        let used = [false; 3];
        let image = vec![[3.0, 0.0, 0.0, 1.0], [0.0; 4], [6.0, 0.0, 0.0, 0.5]];
        let out = fill(image, &used, 3, 1, 1);
        assert!(near(out[1][0], (3.0 + 3.0) / 1.5), "{:?}", out[1]);
        assert_eq!(out[1][3], 1.0);
    }
}
