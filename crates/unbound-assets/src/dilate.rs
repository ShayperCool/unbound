//! The jump flood the Unity Editor fills empty texels with: the texture importer's "Alpha is
//! Transparency" and the sprite atlas packer's border fill both find, for every texel that is
//! not solid, the nearest solid texel this way (Unity 6000.0.71f1, tests/projects/textureimport
//! and tests/projects/spriteatlas).

/// No solid texel reached.
pub const NONE: (u16, u16) = (u16::MAX, u16::MAX);

/// The solid texel each texel of a `width` x `height` image (bottom-up rows, `y = 0` first)
/// takes its colour from, or [`NONE`]. Each texel starts with itself if `solid`, then for steps
/// `k = min(max(w, h) / 2, max_step)`, `k / 2`, ..., 1 looks at its own candidate and those of
/// the texels `k` left, right, below and above (in that order), keeping the first one nearest in
/// Manhattan distance; a solid texel keeps itself. Images of 0xffff texels or more on a side are
/// not flooded (every texel gets [`NONE`]), as the Editor's 16-bit coordinates cannot hold them.
pub fn jump_flood(
    width: usize,
    height: usize,
    max_step: usize,
    solid: impl Fn(usize, usize) -> bool,
) -> Vec<(u16, u16)> {
    let (w, h) = (width, height);
    if w == 0 || h == 0 || w > 0xfffe || h > 0xfffe {
        return vec![NONE; w * h];
    }
    let mut seeds: Vec<(u16, u16)> = vec![NONE; w * h];
    for y in 0..h {
        for x in 0..w {
            if solid(x, y) {
                seeds[y * w + x] = (x as u16, y as u16);
            }
        }
    }
    let distance = |(sx, sy): (u16, u16), x: usize, y: usize| {
        (sx as isize - x as isize).unsigned_abs() + (sy as isize - y as isize).unsigned_abs()
    };
    let mut next = seeds.clone();
    let mut step = (w.max(h) / 2).min(max_step);
    while step > 0 {
        let k = step as isize;
        for y in 0..h {
            for x in 0..w {
                let mut best = seeds[y * w + x];
                if best != (x as u16, y as u16) {
                    let mut best_distance = if best == NONE {
                        usize::MAX
                    } else {
                        distance(best, x, y)
                    };
                    for (dx, dy) in [(-k, 0), (k, 0), (0, -k), (0, k)] {
                        let (nx, ny) = (x as isize + dx, y as isize + dy);
                        if nx < 0 || ny < 0 || nx >= w as isize || ny >= h as isize {
                            continue;
                        }
                        let seed = seeds[ny as usize * w + nx as usize];
                        if seed == NONE {
                            continue;
                        }
                        let d = distance(seed, x, y);
                        if d < best_distance {
                            (best, best_distance) = (seed, d);
                        }
                    }
                }
                next[y * w + x] = best;
            }
        }
        std::mem::swap(&mut seeds, &mut next);
        step /= 2;
    }
    seeds
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn floods_nearest_solid_within_the_step_reach() {
        // One solid texel at x = 0 of a 16 x 1 row; steps 4, 2, 1 reach 7 texels.
        let seeds = jump_flood(16, 1, 4, |x, _| x == 0);
        for (x, seed) in seeds.iter().enumerate() {
            assert_eq!(*seed == (0, 0), x <= 7, "{x}");
        }
        // Ties keep the first candidate (left before right).
        let seeds = jump_flood(5, 1, 4, |x, _| x == 0 || x == 4);
        assert_eq!(seeds[2], (0, 0));
        assert_eq!(jump_flood(0, 4, 4, |_, _| true), Vec::new());
        assert!(jump_flood(3, 2, 4, |_, _| false).iter().all(|s| *s == NONE));
    }
}
