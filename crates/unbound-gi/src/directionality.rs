//! The directionality of a directional realtime lightmap (`unity_DynamicDirectionality`,
//! docs/REALTIME_GI.md): per block of 2x2 texels the dominant direction its light comes from,
//! interpolated to the texels within each chart. Matches the Unity 6000.0.71f1 Editor,
//! tests/projects/realtimegi (`roomdirectional-*`).
use crate::data::{Segment, Transport};
use std::sync::LazyLock;

/// Number of directions a transport's block references choose from.
pub const DIRECTION_COUNT: usize = 256;

/// The directions a block receives its references' light from, by index: none for index 0, then
/// 255 unit vectors spread over the sphere, direction i at height z = 2 r(i - 1) - 1 (r: the
/// base-2 radical inverse, so the first few already cover the sphere) and at azimuth
/// (i - 1/2) / 255 of a turn.
pub static DIRECTIONS: LazyLock<[[f32; 3]; DIRECTION_COUNT]> = LazyLock::new(|| {
    let mut out = [[0.0; 3]; DIRECTION_COUNT];
    let around = (DIRECTION_COUNT - 1) as f64;
    for (i, d) in out.iter_mut().enumerate().skip(1) {
        // The base-2 radical inverse of i - 1: its 8 bits mirrored behind the binary point.
        let z = 2.0 * ((i as u8 - 1).reverse_bits() as f64 / DIRECTION_COUNT as f64) - 1.0;
        let radius = (1.0 - z * z).max(0.0).sqrt();
        let azimuth = (i as f64 - 0.5) / around * std::f64::consts::TAU;
        *d = [
            (radius * azimuth.cos()) as f32,
            (radius * azimuth.sin()) as f32,
            z as f32,
        ];
    }
    out
});

/// Bits of [`Transport::block_neighbours`]: the neighbour at x - 1, x + 1, y - 1, y + 1 and the
/// four diagonal ones belong to the block's chart.
pub const NEIGHBOUR_LEFT: u8 = 0x01;
pub const NEIGHBOUR_RIGHT: u8 = 0x02;
pub const NEIGHBOUR_BELOW: u8 = 0x04;
pub const NEIGHBOUR_ABOVE: u8 = 0x08;
pub const NEIGHBOUR_BELOW_LEFT: u8 = 0x10;
pub const NEIGHBOUR_ABOVE_LEFT: u8 = 0x20;
pub const NEIGHBOUR_BELOW_RIGHT: u8 = 0x40;
pub const NEIGHBOUR_ABOVE_RIGHT: u8 = 0x80;

/// How much each channel of a reference's light counts for its weight in a block's direction:
/// its luminance (Rec. 709; with equal or Rec. 601 weights four to seven times as many of the
/// probe's values differ from the Editor's).
const LUMINANCE: [f32; 3] = [0.2126, 0.7152, 0.0722];
/// A block whose weighted light is below this keeps a direction shorter in proportion (its sum
/// divided by this instead of by the light), so that a black block has none.
const MIN_LIGHT: f32 = 0.001;
/// The least a directionality texel's fourth channel is (see [`Directionality::solve`]).
const MIN_REBALANCING: f32 = 0.5;
/// The bilinear weights of a texel's own block, the two blocks beside it and the diagonal one.
const WEIGHT_OWN: f32 = 9.0 / 16.0;
const WEIGHT_SIDE: f32 = 3.0 / 16.0;
const WEIGHT_DIAGONAL: f32 = 1.0 / 16.0;

/// A system's directionality between updates.
pub struct Directionality {
    width: usize,
    height: usize,
    /// Per block (row-major, `(width / 2) * (height / 2)`), its value in this update as it is
    /// stored: the direction times 1/2 plus 1/2, and the rebalancing term.
    blocks: Vec<[f32; 4]>,
    /// The block was not solved in this update: its texels stay as they are.
    kept: Vec<bool>,
    /// The texture: RGBA8, `width * height` row-major at system resolution.
    texels: Vec<[u8; 4]>,
}

impl Directionality {
    pub fn new(transport: &Transport) -> Self {
        let (width, height) = (transport.width as usize, transport.height as usize);
        let blocks = (width / 2) * (height / 2);
        Self {
            width,
            height,
            blocks: vec![[0.0; 4]; blocks],
            kept: vec![true; blocks],
            texels: vec![[0; 4]; width * height],
        }
    }

    /// The texture: RGBA8 texels, `width * height` row-major at system resolution. RGB is the
    /// direction most of the texel's light comes from, times 1/2 plus 1/2 (shorter the more
    /// evenly the light comes from all around); alpha what Unity's shaders divide the half
    /// Lambert term of the shaded normal by: that term for the surface's own normal.
    pub fn texels(&self) -> &[[u8; 4]] {
        &self.texels
    }

    fn block_of(&self, (x, y): (u16, u16)) -> usize {
        // The reader checked that block texels lie in the grid of whole blocks.
        (y as usize / 2) * (self.width / 2) + x as usize / 2
    }

    /// A solved segment's blocks. `gathered`: per reference, its value times its weight (as the
    /// texels sum them). A block's direction is the mean of its references' directions weighted
    /// by the light each sends to the block's first texel.
    pub fn solve(&mut self, segment: &Segment, gathered: &[[f32; 3]]) {
        let refs = segment.references.len();
        let directions = &*DIRECTIONS;
        for (b, normal) in segment.block_normals.iter().enumerate() {
            let weights = &segment.weights[b * refs..(b + 1) * refs];
            let indices = &segment.block_directions[b * refs..(b + 1) * refs];
            let (mut sum, mut light) = ([0.0f32; 3], 0.0f32);
            for ((&w, &index), value) in weights.iter().zip(indices).zip(gathered) {
                let l = w as f32
                    * (value[0] * LUMINANCE[0] + value[1] * LUMINANCE[1] + value[2] * LUMINANCE[2]);
                let d = directions[index as usize];
                for k in 0..3 {
                    sum[k] += l * d[k];
                }
                light += l;
            }
            // NaN light (it compares false) takes the dark branch and is stored as 0 below.
            let scale = if light >= MIN_LIGHT {
                1.0 / light
            } else {
                1.0 / MIN_LIGHT
            };
            let d = sum.map(|v| v * scale);
            let cosine = normal[0] * d[0] + normal[1] * d[1] + normal[2] * d[2];
            let block = self.block_of(segment.texels[b]);
            self.blocks[block] = [
                d[0] * 0.5 + 0.5,
                d[1] * 0.5 + 0.5,
                d[2] * 0.5 + 0.5,
                (cosine * 0.5 + 0.5).max(MIN_REBALANCING),
            ];
            self.kept[block] = false;
        }
    }

    /// A segment that is not solved in this update: its blocks' texels stay, and the blocks
    /// beside them read each as the mean of its four texels.
    pub fn keep(&mut self, segment: &Segment) {
        for b in 0..segment.block_normals.len() {
            let (x, y) = segment.texels[b];
            let (x, y) = (x as usize & !1, y as usize & !1);
            let mut sum = [0.0f32; 4];
            for at in [0, 1, self.width, self.width + 1] {
                let texel = self.texels[y * self.width + x + at];
                for k in 0..4 {
                    sum[k] += texel[k] as f32;
                }
            }
            let block = self.block_of(segment.texels[b]);
            self.blocks[block] = sum.map(|v| v * 0.25 * (1.0 / 255.0));
            self.kept[block] = true;
        }
    }

    /// After every segment was solved or kept: the blocks' copies, the texels of the solved
    /// blocks (each block's value at the middle of its 2x2 texels, interpolated bilinearly with
    /// the neighbours of its chart; a missing side neighbour counts as the block itself, a
    /// missing diagonal one as the two side neighbours' sum less the block), then the texels'
    /// copies.
    pub fn finish(&mut self, transport: &Transport) {
        for segment in &transport.segments {
            for &(to, from) in &segment.copies[..segment.block_copies] {
                let (to, from) = (self.block_of(to), self.block_of(from));
                self.blocks[to] = self.blocks[from];
                self.kept[to] = self.kept[from];
            }
        }
        let (bw, bh) = (self.width / 2, self.height / 2);
        for by in 0..bh {
            for bx in 0..bw {
                let b = by * bw + bx;
                if self.kept[b] {
                    continue;
                }
                let flags = transport.block_neighbours[b];
                let own = self.blocks[b];
                // A neighbour the flags name, when it lies in the grid.
                let at = |bit: u8, dx: isize, dy: isize| -> Option<[f32; 4]> {
                    let (x, y) = (bx as isize + dx, by as isize + dy);
                    (flags & bit != 0 && x >= 0 && y >= 0 && (x as usize) < bw && (y as usize) < bh)
                        .then(|| self.blocks[y as usize * bw + x as usize])
                };
                let side = |bit, dx, dy| at(bit, dx, dy).unwrap_or(own);
                let (left, right) = (side(NEIGHBOUR_LEFT, -1, 0), side(NEIGHBOUR_RIGHT, 1, 0));
                let (below, above) = (side(NEIGHBOUR_BELOW, 0, -1), side(NEIGHBOUR_ABOVE, 0, 1));
                let corner = |bit, dx, dy, a: [f32; 4], c: [f32; 4]| {
                    at(bit, dx, dy).unwrap_or(std::array::from_fn(|k| a[k] + c[k] - own[k]))
                };
                let quarters = [
                    (
                        0,
                        left,
                        below,
                        corner(NEIGHBOUR_BELOW_LEFT, -1, -1, left, below),
                    ),
                    (
                        1,
                        right,
                        below,
                        corner(NEIGHBOUR_BELOW_RIGHT, 1, -1, right, below),
                    ),
                    (
                        self.width,
                        left,
                        above,
                        corner(NEIGHBOUR_ABOVE_LEFT, -1, 1, left, above),
                    ),
                    (
                        self.width + 1,
                        right,
                        above,
                        corner(NEIGHBOUR_ABOVE_RIGHT, 1, 1, right, above),
                    ),
                ];
                let first = 2 * by * self.width + 2 * bx;
                for (offset, a, c, diagonal) in quarters {
                    self.texels[first + offset] = std::array::from_fn(|k| {
                        let v = own[k] * WEIGHT_OWN
                            + (a[k] + c[k]) * WEIGHT_SIDE
                            + diagonal[k] * WEIGHT_DIAGONAL;
                        // Truncated to 8 bits; NaN becomes 0.
                        (v.clamp(0.0, 1.0) * 255.0) as u8
                    });
                }
            }
        }
        for segment in &transport.segments {
            for &((dx, dy), (sx, sy)) in &segment.copies[segment.block_copies..] {
                self.texels[dy as usize * self.width + dx as usize] =
                    self.texels[sy as usize * self.width + sx as usize];
            }
        }
        // Blocks no segment owns are never written.
        self.kept.fill(true);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A transport of `width` x `height` texels whose blocks are `owned` (block x, block y,
    /// normal), in one segment of `references` references; every block's first texel takes its
    /// light from the reference directions `directions[block]` with texel weights
    /// `weights[block]`.
    fn transport(
        width: u32,
        height: u32,
        owned: &[(u16, u16, [f32; 3])],
        directions: &[&[u8]],
        weights: &[&[u8]],
    ) -> Transport {
        let references = directions[0].len();
        let mut segment = Segment {
            references: (0..references as u32).map(|k| (0, k)).collect(),
            reference_weights: vec![u16::MAX; references],
            weights: Vec::new(),
            texels: Vec::new(),
            copies: Vec::new(),
            block_normals: owned.iter().map(|o| o.2).collect(),
            block_directions: directions.concat(),
            block_copies: 0,
        };
        // The blocks' first texels, then their other three.
        for (b, &(x, y, _)) in owned.iter().enumerate() {
            segment.texels.push((2 * x, 2 * y));
            segment.weights.extend_from_slice(weights[b]);
        }
        for (b, &(x, y, _)) in owned.iter().enumerate() {
            for (dx, dy) in [(1, 0), (0, 1), (1, 1)] {
                segment.texels.push((2 * x + dx, 2 * y + dy));
                segment.weights.extend_from_slice(weights[b]);
            }
        }
        Transport {
            width,
            height,
            dependencies: vec![[0; 16]],
            segments: vec![segment],
            block_neighbours: vec![0; (width / 2 * (height / 2)) as usize],
            block_error: None,
            bounce_samples: vec![0],
            bounce: Vec::new(),
        }
    }

    fn texel(d: &Directionality, x: usize, y: usize) -> [u8; 4] {
        d.texels()[y * d.width + x]
    }

    #[test]
    fn the_directions_cover_the_sphere() {
        let directions = &*DIRECTIONS;
        assert_eq!(directions[0], [0.0; 3]);
        assert_eq!(directions[1], [0.0, 0.0, -1.0]);
        for (i, d) in directions.iter().enumerate().skip(1) {
            let length = (d[0] * d[0] + d[1] * d[1] + d[2] * d[2]).sqrt();
            assert!((length - 1.0).abs() < 1e-6, "direction {i}: {length}");
        }
        // Heights halve their gaps as the index grows; the azimuth turns once over the set.
        let z: Vec<f32> = directions[1..9].iter().map(|d| d[2]).collect();
        assert_eq!(z, [-1.0, 0.0, -0.5, 0.5, -0.75, 0.25, -0.25, 0.75]);
        let turn = (1.5f64 / 255.0 * std::f64::consts::TAU) as f32;
        assert!((directions[2][0] - turn.cos()).abs() < 1e-6);
        assert!((directions[2][1] - turn.sin()).abs() < 1e-6);
        assert!(directions[255][1] < 0.0 && directions[255][0] > 0.99);
        // Every octant has directions.
        for octant in 0..8 {
            let inside = |d: &&[f32; 3]| (0..3).all(|k| (d[k] > 0.0) == (octant >> k & 1 == 1));
            assert!(directions.iter().filter(inside).count() > 20, "{octant}");
        }
    }

    #[test]
    fn a_block_points_where_its_light_comes_from() {
        // Reference 0 arrives along direction 2 (about +x), reference 1 along direction 4
        // (x 0.863, y 0.075, z 1/2); the block faces +x.
        let t = transport(2, 2, &[(0, 0, [1.0, 0.0, 0.0])], &[&[2, 4]], &[&[10, 30]]);
        let mut d = Directionality::new(&t);
        assert!(d.texels().iter().all(|t| *t == [0; 4]));
        // Green light of 0.1 from the first, red light of 0.4 from the second: weights
        // 10 * 0.07152 and 30 * 0.08504 by luminance.
        let gathered = [[0.0, 0.1, 0.0], [0.4, 0.0, 0.0]];
        d.solve(&t.segments[0], &gathered);
        d.finish(&t);
        let (a, b) = (10.0 * 0.1 * 0.7152, 30.0 * 0.4 * 0.2126);
        let (d2, d4) = (DIRECTIONS[2], DIRECTIONS[4]);
        let mean: [f32; 3] = std::array::from_fn(|k| (a * d2[k] + b * d4[k]) / (a + b));
        let expected = [
            ((mean[0] * 0.5 + 0.5) * 255.0) as u8,
            ((mean[1] * 0.5 + 0.5) * 255.0) as u8,
            ((mean[2] * 0.5 + 0.5) * 255.0) as u8,
            // The half Lambert term of the block's normal (+x) with that direction.
            ((mean[0] * 0.5 + 0.5) * 255.0) as u8,
        ];
        // A block without neighbours of its chart: its four texels are the block's value.
        for (x, y) in [(0, 0), (1, 0), (0, 1), (1, 1)] {
            assert_eq!(texel(&d, x, y), expected, "({x},{y})");
        }
        assert!(expected[0] > 230 && expected[2] > 170, "{expected:?}");

        // Light from behind the surface: the fourth channel stops at 1/2.
        let back = transport(2, 2, &[(0, 0, [-1.0, 0.0, 0.0])], &[&[2, 4]], &[&[10, 30]]);
        let mut d = Directionality::new(&back);
        d.solve(&back.segments[0], &gathered);
        d.finish(&back);
        assert_eq!(texel(&d, 0, 0)[..3], expected[..3]);
        assert_eq!(texel(&d, 0, 0)[3], 127);

        // Hardly any light: the direction shortens in proportion; none, or light that is not
        // a number: no direction.
        let mut d = Directionality::new(&t);
        let dim = [[0.0; 3], [0.0001 / (30.0 * 0.2126) * 5.0, 0.0, 0.0]];
        d.solve(&t.segments[0], &dim);
        d.finish(&t);
        let half = ((d4[0] * 0.5 * 0.5 + 0.5) * 255.0) as u8;
        assert!((texel(&d, 0, 0)[0] as i32 - half as i32).abs() <= 1);
        for gathered in [[[0.0; 3]; 2], [[f32::NAN; 3]; 2]] {
            d.solve(&t.segments[0], &gathered);
            d.finish(&t);
            assert!(
                texel(&d, 1, 1) == [127; 4] || texel(&d, 1, 1) == [0, 0, 0, 127],
                "{:?}",
                texel(&d, 1, 1)
            );
        }
    }

    #[test]
    fn texels_interpolate_between_the_blocks_of_a_chart() {
        // Two blocks side by side, lit along -z (direction 1) and along direction 2 (about
        // +x); both face +y, so their fourth channels are about 1/2.
        let up = [0.0, 1.0, 0.0];
        let owned = [(0, 0, up), (1, 0, up)];
        let mut t = transport(4, 2, &owned, &[&[1], &[2]], &[&[1], &[1]]);
        let light = [[1.0f32; 3]];
        let solve = |t: &Transport| {
            let mut d = Directionality::new(t);
            d.solve(&t.segments[0], &light);
            d.finish(t);
            d
        };
        // Of different charts: each block's texels are its own value.
        let apart = solve(&t);
        let (left, right) = (texel(&apart, 0, 0), texel(&apart, 3, 1));
        assert_eq!(left[..3], [127, 127, 0]);
        assert_eq!(right[0], 254);
        assert_eq!(texel(&apart, 1, 1), left);
        assert_eq!(texel(&apart, 2, 0), right);
        // Of one chart: the texels beside the other block take 3/16 + 1/16 of it (its side and
        // diagonal weights), the outer ones stay.
        t.block_neighbours = vec![NEIGHBOUR_RIGHT, NEIGHBOUR_LEFT];
        let joined = solve(&t);
        assert_eq!(texel(&joined, 0, 0), left);
        assert_eq!(texel(&joined, 3, 0), right);
        let mix = |a: u8, b: u8| (a as f32 * 0.75 + b as f32 * 0.25) / 255.0;
        for y in 0..2 {
            let (inner_left, inner_right) = (texel(&joined, 1, y), texel(&joined, 2, y));
            for k in 0..3 {
                assert!((inner_left[k] as f32 / 255.0 - mix(left[k], right[k])).abs() < 0.006);
                assert!((inner_right[k] as f32 / 255.0 - mix(right[k], left[k])).abs() < 0.006);
            }
        }
        // A neighbour bit that points outside the grid is no neighbour.
        t.block_neighbours = vec![NEIGHBOUR_LEFT | NEIGHBOUR_BELOW | NEIGHBOUR_ABOVE_LEFT, 0];
        let outside = solve(&t);
        assert_eq!(texel(&outside, 0, 0), left);
        assert_eq!(texel(&outside, 0, 1), left);
    }

    #[test]
    fn kept_blocks_keep_their_texels_and_copies_follow() {
        let up = [0.0, 1.0, 0.0];
        let owned = [(0, 0, up), (1, 0, up)];
        let mut t = transport(8, 2, &owned, &[&[1], &[2]], &[&[1], &[1]]);
        t.block_neighbours = vec![NEIGHBOUR_RIGHT, NEIGHBOUR_LEFT, 0, 0];
        // The second block's quad is copied to the third block, and one texel of the first to
        // the last column.
        t.segments[0].copies = vec![((4, 0), (2, 0)), ((7, 1), (0, 0))];
        t.segments[0].block_copies = 1;
        let mut d = Directionality::new(&t);
        d.solve(&t.segments[0], &[[1.0; 3]]);
        d.finish(&t);
        let first = d.texels().to_vec();
        // The copied block has the source block's value (flat: it has no neighbours), the
        // copied texel its source's.
        assert_eq!(texel(&d, 4, 0), texel(&d, 3, 0));
        assert_eq!(texel(&d, 5, 1), texel(&d, 3, 0));
        assert_eq!(texel(&d, 7, 1), texel(&d, 0, 0));
        assert_eq!(texel(&d, 6, 0), [0; 4]);
        // An update that does not solve the segment leaves every texel.
        d.keep(&t.segments[0]);
        d.finish(&t);
        assert_eq!(d.texels(), &first[..]);
        // Solved again with other light, the texels follow.
        let mut other = t.clone();
        other.segments[0].block_directions = vec![3, 3];
        d.solve(&other.segments[0], &[[1.0; 3]]);
        d.finish(&other);
        assert_ne!(d.texels(), &first[..]);
    }
}
