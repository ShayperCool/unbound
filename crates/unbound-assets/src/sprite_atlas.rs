//! Sprite Atlas packing as the Unity 6000.0.71f1 Editor packs atlases before it enters Play
//! mode (tests/projects/spriteatlas): where each sprite goes on which page, and the pages'
//! pixels.
//!
//! The layout, in the Editor's terms:
//! - Sprites are ordered by their secondary-texture set, then by texture rect area (largest
//!   first), then by texture (by GUID; on one texture by the FNV-1a hash of the name).
//! - Each sprite's mask is its render mesh rasterized over the texture rect's pixel bounds (the
//!   whole covered box unless the atlas packs tightly), grown by the padding towards +x and +y.
//! - A sprite tries the pages from the last to the first, then opens a new page, where it goes to
//!   the origin. On a page it searches a power-of-two region that starts at the region already
//!   used: 50 random positions (a xorshift128 generator seeded from the MD4 of the previous
//!   sprite's seed and this one's rect size), each one narrowing the search to below the last
//!   free one, then one corner position; the region grows by doubling until the page size. A free
//!   position slides towards the origin, diagonally, then left, then down. With rotation allowed
//!   a position that does not fit as is is tried flipped horizontally, vertically, then both.
//! - Mask bits may hang off a page's right or top edge by less than the padding.
//! - A page's texture is the power of two around its sprites (at least 32). The sprites' texels
//!   are copied under their masks, then the empty texels within 7 texels of a sprite take the
//!   colour of the nearest sprite texel (a jump flood of steps 4, 2, 1).
use std::cmp::Ordering;

/// The largest jump of the border fill.
const FILL_MAX_STEP: usize = 4;
/// Random positions tried in each search region.
const RANDOM_TRIES: u32 = 50;
/// The smallest page texture side.
const MIN_PAGE_SIZE: i32 = 32;
/// The generator's seeding multiplier (the Mersenne Twister's).
const SEED_MULTIPLIER: u32 = 0x6c07_8965;

/// An atlas's packing settings.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, serde::Serialize, serde::Deserialize)]
pub struct PackSettings {
    /// The page size (the target's maximum texture size).
    pub max_size: u32,
    pub padding: u32,
    /// Positions are multiples of this (`blockOffset`; 0 reads as 1).
    pub block_offset: u32,
    pub allow_rotation: bool,
    pub tight: bool,
    /// The texture format needs square textures (PVRTC).
    pub square: bool,
    /// `enableAlphaDilation`: the border fill copies colour only, the filled texels stay
    /// transparent (otherwise it copies the whole texel).
    pub alpha_dilation: bool,
}

/// A sprite to pack.
#[derive(Debug, Clone, Copy)]
pub struct PackSprite<'a> {
    pub name: &'a str,
    /// The texture's GUID and which texture it is among the inputs (same index, same texture).
    pub texture_guid: u128,
    pub texture: usize,
    /// The hash of the sprite's secondary textures (0 without any).
    pub secondary_hash: u64,
    /// The source texture's size.
    pub texture_size: [u32; 2],
    /// `textureRect` in the source texture's pixels.
    pub rect: [f32; 4],
    /// The render data's `uvTransform`: (pixels per unit, x offset, pixels per unit, y offset),
    /// mapping a vertex to texture pixels.
    pub uv_transform: [f32; 4],
    /// The render mesh: vertices in units from the pivot.
    pub vertices: &'a [[f32; 2]],
    pub indices: &'a [u16],
}

/// Packing failed before anything was placed.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum PackError {
    /// A sprite's rect is larger than the page: the size the atlas would need.
    Oversize { needed: u32 },
}

/// SpritePackingRotation.
pub mod rotation {
    pub const NONE: u8 = 0;
    pub const FLIP_HORIZONTAL: u8 = 1;
    pub const FLIP_VERTICAL: u8 = 2;
    pub const ROTATE_180: u8 = 3;
}

/// Where a sprite went.
#[derive(Debug, Clone, Copy, PartialEq, Eq, serde::Serialize, serde::Deserialize)]
pub struct Placement {
    pub page: u32,
    /// The mask's position on the page.
    pub x: i32,
    pub y: i32,
    /// [`rotation`].
    pub rotation: u8,
    /// The mask's origin in the source texture (the rect's pixel bounds).
    pub origin: [i32; 2],
}

/// Packed sprites.
#[derive(Debug, Clone)]
pub struct Layout {
    /// The sprites (input indices) in packing order.
    pub order: Vec<usize>,
    /// By input index.
    pub placements: Vec<Placement>,
    /// Page texture sizes.
    pub pages: Vec<[u32; 2]>,
    /// The rotated (unpadded) masks, by input index.
    masks: Vec<Bits>,
    /// The source textures' sizes, by input index.
    sources: Vec<[u32; 2]>,
}

impl Layout {
    /// The sprite's `textureRect` in its page.
    pub fn texture_rect(&self, sprite: usize, rect: [f32; 4]) -> [f32; 4] {
        let p = &self.placements[sprite];
        [
            rect[0] + p.x as f32 - p.origin[0] as f32,
            rect[1] + p.y as f32 - p.origin[1] as f32,
            rect[2],
            rect[3],
        ]
    }

    /// The sprite's `uvTransform` in its page (unflipped: the flips apply to the UVs).
    pub fn uv_transform(&self, sprite: usize, uv_transform: [f32; 4]) -> [f32; 4] {
        let p = &self.placements[sprite];
        [
            uv_transform[0],
            p.x as f32 + uv_transform[1] - p.origin[0] as f32,
            uv_transform[2],
            p.y as f32 + uv_transform[3] - p.origin[1] as f32,
        ]
    }

    /// A page's RGBA8 texels (rows bottom-up). `source(i)` is sprite `i`'s texture as RGBA8,
    /// rows bottom-up, of its [`PackSprite::texture_size`].
    pub fn page_pixels<'s>(
        &self,
        page: u32,
        settings: &PackSettings,
        source: impl Fn(usize) -> &'s [u8],
    ) -> Vec<u8> {
        let [w, h] = self.pages[page as usize];
        let (w, h) = (w as usize, h as usize);
        let mut out = vec![0u8; w * h * 4];
        let mut solid = vec![false; w * h];
        for (i, p) in self.placements.iter().enumerate() {
            if p.page != page {
                continue;
            }
            let mask = &self.masks[i];
            let texels = source(i);
            let [tw, th] = self.sources[i].map(|v| v as usize);
            if texels.len() < tw * th * 4 {
                continue;
            }
            for row in 0..mask.h {
                for col in 0..mask.w {
                    if !mask.get(col, row) {
                        continue;
                    }
                    let (dx, dy) = (p.x + col, p.y + row);
                    let sx =
                        if matches!(p.rotation, rotation::FLIP_HORIZONTAL | rotation::ROTATE_180) {
                            mask.w - 1 - col
                        } else {
                            col
                        } + p.origin[0];
                    let sy = if matches!(p.rotation, rotation::FLIP_VERTICAL | rotation::ROTATE_180)
                    {
                        mask.h - 1 - row
                    } else {
                        row
                    } + p.origin[1];
                    if dx < 0 || dy < 0 || dx as usize >= w || dy as usize >= h {
                        continue;
                    }
                    let d = dy as usize * w + dx as usize;
                    // The burned-in masks are the fill's solid texels, also where the source
                    // texel is missing.
                    solid[d] = true;
                    if sx < 0 || sy < 0 || sx as usize >= tw || sy as usize >= th {
                        continue;
                    }
                    let s = (sy as usize * tw + sx as usize) * 4;
                    out[d * 4..d * 4 + 4].copy_from_slice(&texels[s..s + 4]);
                }
            }
        }
        let seeds = crate::dilate::jump_flood(w, h, FILL_MAX_STEP, |x, y| solid[y * w + x]);
        let filled = out.clone();
        for (i, &(sx, sy)) in seeds.iter().enumerate() {
            if (sx, sy) == crate::dilate::NONE || solid[i] {
                continue;
            }
            let s = (sy as usize * w + sx as usize) * 4;
            out[i * 4..i * 4 + 3].copy_from_slice(&filled[s..s + 3]);
            out[i * 4 + 3] = if settings.alpha_dilation {
                0
            } else {
                filled[s + 3]
            };
        }
        out
    }
}

/// The packing order: secondary textures, then area (largest first), then texture.
fn compare(a: &PackSprite, b: &PackSprite) -> Ordering {
    a.secondary_hash
        .cmp(&b.secondary_hash)
        .then_with(|| {
            let area = |s: &PackSprite| s.rect[2] * s.rect[3];
            area(b).partial_cmp(&area(a)).unwrap_or(Ordering::Equal)
        })
        .then_with(|| compare_texture_only(a, b))
}

/// Sprites by texture: one texture's by the FNV-1a hash of their names (signed), others by
/// their textures' GUIDs.
fn compare_texture_only(a: &PackSprite, b: &PackSprite) -> Ordering {
    a.secondary_hash.cmp(&b.secondary_hash).then_with(|| {
        if a.texture == b.texture {
            (fnv1a(a.name) as i32).cmp(&(fnv1a(b.name) as i32))
        } else {
            guid_words(a.texture_guid)
                .cmp(&guid_words(b.texture_guid))
                .then(a.texture.cmp(&b.texture))
        }
    })
}

/// FNV-1a, 32 bits.
fn fnv1a(text: &str) -> u32 {
    text.bytes()
        .fold(0x811c_9dc5, |h, b| (h ^ b as u32).wrapping_mul(0x0100_0193))
}

/// A GUID's four 32-bit words as the Editor holds them: each word's nibbles are its eight hex
/// digits, the first digit the lowest nibble.
pub fn guid_words(guid: u128) -> [u32; 4] {
    let mut words = [0u32; 4];
    for (i, word) in words.iter_mut().enumerate() {
        let digits = (guid >> (96 - 32 * i)) as u32;
        // Digit j of the group (from the left) is nibble j of the word.
        for j in 0..8 {
            let digit = (digits >> (28 - 4 * j)) & 0xf;
            *word |= digit << (4 * j);
        }
    }
    words
}

/// The search's random generator (xorshift128), seeded as the Editor seeds it.
struct Rng([u32; 4]);

impl Rng {
    fn new(seed: u32) -> Self {
        let mut s = [seed, 0, 0, 0];
        for i in 1..4 {
            s[i] = s[i - 1].wrapping_mul(SEED_MULTIPLIER).wrapping_add(1);
        }
        Self(s)
    }

    fn next(&mut self) -> u32 {
        let [x, y, z, w] = self.0;
        let t = x ^ (x << 11);
        let next = t ^ w ^ (t >> 8) ^ (w >> 19);
        self.0 = [y, z, w, next];
        next
    }

    /// A position in `0..range / step` (negative for a negative range; 0 without drawing for a
    /// range under one step).
    fn coordinate(&mut self, range: i32, step: i32) -> i32 {
        let n = range / step;
        match n.cmp(&0) {
            Ordering::Greater => (self.next() % n as u32) as i32,
            Ordering::Less => -((self.next() % n.unsigned_abs()) as i32),
            Ordering::Equal => 0,
        }
    }
}

/// The seed of a sprite's search: the MD4 of the previous seed and the rect's size (each as
/// thousandths).
fn seed(previous: u32, width: f32, height: f32) -> u32 {
    let mut message = Vec::with_capacity(20);
    message.extend_from_slice(&previous.to_le_bytes());
    for v in [width, height] {
        message.extend_from_slice(&float_feed(v));
    }
    let digest = md4(&message);
    let word = |i: usize| u32::from_le_bytes(digest[i * 4..i * 4 + 4].try_into().unwrap());
    word(0) ^ word(1) ^ word(2) ^ word(3)
}

/// How a float enters the hash: finite values as thousandths (64-bit), infinities as text.
fn float_feed(v: f32) -> Vec<u8> {
    if v == f32::NEG_INFINITY {
        b"-inf".to_vec()
    } else if v < f32::INFINITY {
        ((v as f64 * 1000.0) as i64).to_le_bytes().to_vec()
    } else {
        b"+inf".to_vec()
    }
}

/// MD4 (RFC 1320).
fn md4(message: &[u8]) -> [u8; 16] {
    let mut h: [u32; 4] = [0x6745_2301, 0xefcd_ab89, 0x98ba_dcfe, 0x1032_5476];
    let mut data = message.to_vec();
    let bits = (message.len() as u64).wrapping_mul(8);
    data.push(0x80);
    while data.len() % 64 != 56 {
        data.push(0);
    }
    data.extend_from_slice(&bits.to_le_bytes());
    for block in data.chunks_exact(64) {
        let x: Vec<u32> = block
            .chunks_exact(4)
            .map(|c| u32::from_le_bytes(c.try_into().unwrap()))
            .collect();
        let [mut a, mut b, mut c, mut d] = h;
        let f = |x: u32, y: u32, z: u32| (x & y) | (!x & z);
        let g = |x: u32, y: u32, z: u32| (x & y) | (x & z) | (y & z);
        let hh = |x: u32, y: u32, z: u32| x ^ y ^ z;
        for i in 0..16 {
            let s = [3, 7, 11, 19][i % 4];
            let t = a.wrapping_add(f(b, c, d)).wrapping_add(x[i]).rotate_left(s);
            (a, b, c, d) = (d, t, b, c);
        }
        for i in 0..16 {
            let s = [3, 5, 9, 13][i % 4];
            let k = (i % 4) * 4 + i / 4;
            let t = a
                .wrapping_add(g(b, c, d))
                .wrapping_add(x[k])
                .wrapping_add(0x5a82_7999)
                .rotate_left(s);
            (a, b, c, d) = (d, t, b, c);
        }
        const ORDER: [usize; 16] = [0, 8, 4, 12, 2, 10, 6, 14, 1, 9, 5, 13, 3, 11, 7, 15];
        for (i, &k) in ORDER.iter().enumerate() {
            let s = [3, 9, 11, 15][i % 4];
            let t = a
                .wrapping_add(hh(b, c, d))
                .wrapping_add(x[k])
                .wrapping_add(0x6ed9_eba1)
                .rotate_left(s);
            (a, b, c, d) = (d, t, b, c);
        }
        h = [
            h[0].wrapping_add(a),
            h[1].wrapping_add(b),
            h[2].wrapping_add(c),
            h[3].wrapping_add(d),
        ];
    }
    let mut out = [0u8; 16];
    for (i, v) in h.iter().enumerate() {
        out[i * 4..i * 4 + 4].copy_from_slice(&v.to_le_bytes());
    }
    out
}

/// The smallest power of two above `v` (`v` = 2^k - 1 gives 2^k, as the Editor rounds sizes).
fn power_of_two_above(v: i32) -> i32 {
    let mut v = v as u32;
    v |= v >> 16;
    v |= v >> 8;
    v |= v >> 4;
    v |= v >> 2;
    v |= v >> 1;
    v.wrapping_add(1) as i32
}

/// The smallest power of two at or above `v` (at least 1).
fn power_of_two_at_least(v: i32) -> i32 {
    power_of_two_above(v - 1)
}

/// A bit grid, rows bottom-up.
#[derive(Debug, Clone, PartialEq, Eq)]
struct Bits {
    w: i32,
    h: i32,
    words: usize,
    data: Vec<u64>,
}

impl Bits {
    fn new(w: i32, h: i32) -> Self {
        let (w, h) = (w.max(0), h.max(0));
        let words = (w as usize).div_ceil(64).max(1);
        Self {
            w,
            h,
            words,
            data: vec![0; words * h as usize],
        }
    }

    fn get(&self, x: i32, y: i32) -> bool {
        self.data[y as usize * self.words + x as usize / 64] >> (x % 64) & 1 != 0
    }

    fn set(&mut self, x: i32, y: i32) {
        self.data[y as usize * self.words + x as usize / 64] |= 1 << (x % 64);
    }

    fn row(&self, y: i32) -> &[u64] {
        &self.data[y as usize * self.words..(y as usize + 1) * self.words]
    }

    /// Bits `start..start + 64` of row `y` (zero beyond the grid).
    fn window(&self, y: i32, start: i32) -> u64 {
        let row = self.row(y);
        let (word, bit) = (start as usize / 64, start as u32 % 64);
        let low = row.get(word).copied().unwrap_or(0) >> bit;
        let high = if bit == 0 {
            0
        } else {
            row.get(word + 1).copied().unwrap_or(0) << (64 - bit)
        };
        let bits = low | high;
        // Bits past the row's end are not part of the grid.
        let remaining = self.w - start;
        if remaining >= 64 {
            bits
        } else if remaining <= 0 {
            0
        } else {
            bits & ((1u64 << remaining) - 1)
        }
    }

    /// The highest set column of row `y`.
    fn last_set(&self, y: i32) -> Option<i32> {
        let row = self.row(y);
        row.iter()
            .enumerate()
            .rev()
            .find(|(_, w)| **w != 0)
            .map(|(i, w)| i as i32 * 64 + 63 - w.leading_zeros() as i32)
    }

    fn first_set(&self, y: i32) -> Option<i32> {
        let row = self.row(y);
        row.iter()
            .enumerate()
            .find(|(_, w)| **w != 0)
            .map(|(i, w)| i as i32 * 64 + w.trailing_zeros() as i32)
    }

    /// This grid mirrored as [`rotation`] says.
    fn rotated(&self, rotation: u8) -> Bits {
        if rotation == rotation::NONE {
            return self.clone();
        }
        let mut out = Bits::new(self.w, self.h);
        let flip_x = matches!(rotation, rotation::FLIP_HORIZONTAL | rotation::ROTATE_180);
        let flip_y = matches!(rotation, rotation::FLIP_VERTICAL | rotation::ROTATE_180);
        for y in 0..self.h {
            for x in 0..self.w {
                if self.get(x, y) {
                    let tx = if flip_x { self.w - 1 - x } else { x };
                    let ty = if flip_y { self.h - 1 - y } else { y };
                    out.set(tx, ty);
                }
            }
        }
        out
    }

    /// This grid `pad` larger, each set bit spread `pad` times to its right, upper-right and
    /// upper neighbours.
    fn padded(&self, pad: i32) -> Bits {
        let mut out = Bits::new(self.w + pad, self.h + pad);
        for y in 0..self.h {
            for x in 0..self.w {
                if self.get(x, y) {
                    out.set(x, y);
                }
            }
        }
        for pass in 0..pad {
            let previous = out.clone();
            for y in 0..self.h + pass {
                for x in 0..self.w + pass {
                    if previous.get(x, y) {
                        out.set(x, y + 1);
                        out.set(x + 1, y + 1);
                        out.set(x + 1, y);
                    }
                }
            }
        }
        out
    }
}

/// A sprite being placed: its padded mask and the flips tried so far.
struct Candidate {
    padded: [Option<Bits>; 4],
    rotation: u8,
}

impl Candidate {
    fn variant(&mut self, rotation: u8) -> &Bits {
        if self.padded[rotation as usize].is_none() {
            let rotated = self.padded[0].as_ref().unwrap().rotated(rotation);
            self.padded[rotation as usize] = Some(rotated);
        }
        self.padded[rotation as usize].as_ref().unwrap()
    }
}

/// A page being filled.
struct Page {
    bits: Bits,
    /// The highest column and row its sprites use (without their padding).
    used: [i32; 2],
}

/// Whether `mask` at (`x`, `y`) overlaps the page: an occupied bit, or a bit off the page beyond
/// the padding's overhang.
fn overlaps(mask: &Bits, page: &Bits, pad: i32, x: i32, y: i32) -> bool {
    if x < 0 || y < 0 {
        return true;
    }
    for row in 0..mask.h {
        let py = y + row;
        let (Some(first), Some(last)) = (mask.first_set(row), mask.last_set(row)) else {
            continue;
        };
        if py >= page.h {
            // Rows above the page: only bits within the padding band above it are allowed,
            // except those also within the band to its right.
            if py - page.h < pad {
                continue;
            }
            if x + first < page.w || x + last >= page.w + pad {
                return true;
            }
            continue;
        }
        if x + last >= page.w + pad {
            return true;
        }
        let mut col = 0;
        while col < mask.w && x + col < page.w {
            let bits = mask.window(row, col) & page.window(py, x + col);
            if bits != 0 {
                return true;
            }
            col += 64;
        }
    }
    false
}

fn burn(page: &mut Page, mask: &Bits, pad: i32, x: i32, y: i32) {
    let (mut max_x, mut max_y) = (0, 0);
    for row in 0..mask.h {
        for col in 0..mask.w {
            if mask.get(col, row) {
                let (px, py) = (x + col, y + row);
                if px < page.bits.w && py < page.bits.h {
                    page.bits.set(px, py);
                    max_x = max_x.max(px);
                    max_y = max_y.max(py);
                }
            }
        }
    }
    page.used[0] = page.used[0].max(max_x - pad);
    page.used[1] = page.used[1].max(max_y - pad);
}

/// Tests a position, flipping the mask when rotation is allowed; the fitting flip is kept.
fn fits(candidate: &mut Candidate, page: &Bits, pad: i32, x: i32, y: i32, rotate: bool) -> bool {
    if !overlaps(candidate.variant(rotation::NONE), page, pad, x, y) {
        candidate.rotation = rotation::NONE;
        return true;
    }
    if !rotate {
        return false;
    }
    for r in [
        rotation::FLIP_HORIZONTAL,
        rotation::FLIP_VERTICAL,
        rotation::ROTATE_180,
    ] {
        if !overlaps(candidate.variant(r), page, pad, x, y) {
            candidate.rotation = r;
            return true;
        }
    }
    false
}

/// Searches a position on `page`; `origin` places a page's first sprite at the origin.
fn fit(
    rng: &mut Rng,
    candidate: &mut Candidate,
    size: [i32; 2],
    page: &Page,
    settings: &PackSettings,
    origin: bool,
) -> Option<(i32, i32)> {
    let pad = settings.padding as i32;
    let step = settings.block_offset.max(1) as i32;
    let [w, h] = size;
    let (max_w, max_h) = (page.bits.w, page.bits.h);
    let mut region_w = power_of_two_above(page.used[0]).max(power_of_two_above(w - 1));
    let mut region_h = power_of_two_above(page.used[1]).max(power_of_two_above(h - 1));
    let (x, y) = loop {
        let (rw, rh) = if settings.square {
            let side = region_w.max(region_h);
            (side, side)
        } else {
            (region_w, region_h)
        };
        let (mut cx, mut cy) = (rw - w, rh - h);
        let mut found = false;
        if origin {
            (cx, cy) = (0, 0);
        } else {
            for _ in 0..RANDOM_TRIES {
                let rx = rng.coordinate(cx, step) * step;
                let ry = rng.coordinate(cy, step) * step;
                if fits(candidate, &page.bits, pad, rx, ry, settings.allow_rotation) {
                    found = true;
                    (cx, cy) = (rx, ry);
                }
            }
        }
        if !found
            && (step == 1 || origin)
            && fits(candidate, &page.bits, pad, cx, cy, settings.allow_rotation)
        {
            found = true;
        }
        if found {
            break (cx, cy);
        }
        if max_w <= rw && max_h <= rh {
            return None;
        }
        let grow_height = if settings.square {
            rw >= max_w
        } else if rw != rh {
            max_w <= rw || (rh <= rw && rh < max_h)
        } else {
            !(rw < max_w && (w <= h || max_h <= rh))
        };
        if grow_height {
            (region_w, region_h) = (rw, power_of_two_above(rh).min(max_h));
        } else {
            (region_w, region_h) = (power_of_two_above(rw).min(max_w), rh);
        }
    };
    // Slide towards the origin (without flipping again).
    let mask = candidate.variant(candidate.rotation).clone();
    let (mut x, mut y) = (x, y);
    loop {
        if x - step >= 0 && y - step >= 0 && !overlaps(&mask, &page.bits, pad, x - step, y - step) {
            (x, y) = (x - step, y - step);
        } else if x - step >= 0 && !overlaps(&mask, &page.bits, pad, x - step, y) {
            x -= step;
        } else if y - step >= 0 && !overlaps(&mask, &page.bits, pad, x, y - step) {
            y -= step;
        } else {
            break;
        }
    }
    Some((x, y))
}

/// The pixel bounds of a texture rect: its rounded-out box, clamped to the texture's right and
/// top.
fn pixel_bounds(rect: [f32; 4], texture: [u32; 2]) -> [i32; 4] {
    let x0 = rect[0].floor() as i32;
    let y0 = rect[1].floor() as i32;
    let x1 = ((rect[0] + rect[2]).ceil() as i32).min(texture[0] as i32);
    let y1 = ((rect[1] + rect[3]).ceil() as i32).min(texture[1] as i32);
    [x0, y0, x1, y1]
}

/// The sprite's mask: its mesh's triangles rasterized in texture pixels within its pixel bounds
/// (each covered pixel counts 8 per triangle in a byte, as the Editor counts them), and the
/// bounding box of the covered bits; unless packing tightly, the whole box.
fn sprite_mask(sprite: &PackSprite, tight: bool) -> (Bits, [i32; 2], [i32; 4]) {
    let [x0, y0, x1, y1] = pixel_bounds(sprite.rect, sprite.texture_size);
    let (w, h) = ((x1 - x0).max(0), (y1 - y0).max(0));
    let mut coverage = vec![0u8; (w * h) as usize];
    let [tw, th] = [sprite.texture_size[0] as i32, sprite.texture_size[1] as i32];
    let t = sprite.uv_transform;
    let pixel =
        |v: [f32; 2]| -> [i32; 2] { [(v[0] * t[0] + t[1]) as i32, (v[1] * t[2] + t[3]) as i32] };
    for tri in sprite.indices.chunks_exact(3) {
        let (Some(a), Some(b), Some(c)) = (
            sprite.vertices.get(tri[0] as usize),
            sprite.vertices.get(tri[1] as usize),
            sprite.vertices.get(tri[2] as usize),
        ) else {
            continue;
        };
        let (p0, mut p1, mut p2) = (pixel(*a), pixel(*b), pixel(*c));
        if (p2[1] - p0[1]) * (p1[0] - p0[0]) < (p2[0] - p0[0]) * (p1[1] - p0[1]) {
            std::mem::swap(&mut p1, &mut p2);
        }
        rasterize(p0, p1, p2, [tw, th], [x0, y0, w, h], &mut coverage);
    }
    let mut bits = Bits::new(w, h);
    let mut bbox = [i32::MAX, i32::MAX, i32::MIN, i32::MIN];
    for row in 0..h {
        for col in 0..w {
            if coverage[(row * w + col) as usize] != 0 {
                bits.set(col, row);
                bbox = [
                    bbox[0].min(col),
                    bbox[1].min(row),
                    bbox[2].max(col),
                    bbox[3].max(row),
                ];
            }
        }
    }
    if !tight && bbox[0] <= bbox[2] {
        for row in bbox[1]..=bbox[3] {
            for col in bbox[0]..=bbox[2] {
                bits.set(col, row);
            }
        }
    }
    (bits, [x0, y0], bbox)
}

/// A triangle's pixels (edge functions on the integer pixel coordinates, edges inclusive) within
/// the texture, counted into the window `[x, y, w, h]` of it.
fn rasterize(
    p0: [i32; 2],
    p1: [i32; 2],
    p2: [i32; 2],
    texture: [i32; 2],
    window: [i32; 4],
    coverage: &mut [u8],
) {
    let [x0, y0] = p0;
    let [x1, y1] = p1;
    let [x2, y2] = p2;
    let min_x = x0.min(x1).min(x2).max(0).max(window[0]);
    let min_y = y0.min(y1).min(y2).max(0).max(window[1]);
    let max_x = x0
        .max(x1)
        .max(x2)
        .min(texture[0] - 1)
        .min(window[0] + window[2] - 1);
    let max_y = y0
        .max(y1)
        .max(y2)
        .min(texture[1] - 1)
        .min(window[1] + window[3] - 1);
    for y in min_y..=max_y {
        for x in min_x..=max_x {
            let w0 = (y - y0)
                .wrapping_mul(x1 - x0)
                .wrapping_sub((y1 - y0).wrapping_mul(x - x0));
            let w1 = (y - y2)
                .wrapping_mul(x0 - x2)
                .wrapping_sub((y0 - y2).wrapping_mul(x - x2));
            let w2 = (y - y1)
                .wrapping_mul(x2 - x1)
                .wrapping_sub((y2 - y1).wrapping_mul(x - x1));
            if (w0 | w1 | w2) >= 0 {
                let i = ((y - window[1]) * window[2] + (x - window[0])) as usize;
                coverage[i] = coverage[i].wrapping_add(8);
            }
        }
    }
}

/// The size an atlas needs for its sprites' rects (`max_size` when they fit).
pub fn needed_size(rects: impl IntoIterator<Item = [f32; 2]>, max_size: u32) -> u32 {
    let mut needed = max_size;
    for [w, h] in rects {
        for side in [w, h] {
            let side = side as i64 as u32;
            if needed < side {
                needed = power_of_two_at_least(side as i32) as u32;
            }
        }
    }
    needed
}

/// Packs `sprites`.
pub fn layout(sprites: &[PackSprite], settings: &PackSettings) -> Result<Layout, PackError> {
    let needed = needed_size(
        sprites.iter().map(|s| [s.rect[2], s.rect[3]]),
        settings.max_size,
    );
    if needed != settings.max_size {
        return Err(PackError::Oversize { needed });
    }
    let mut order: Vec<usize> = (0..sprites.len()).collect();
    order.sort_by(|&a, &b| compare(&sprites[a], &sprites[b]));
    let pad = settings.padding as i32;
    let max = settings.max_size as i32;
    let mut pages: Vec<Page> = Vec::new();
    let mut placements = vec![
        Placement {
            page: 0,
            x: 0,
            y: 0,
            rotation: 0,
            origin: [0, 0],
        };
        sprites.len()
    ];
    let mut masks = vec![Bits::new(0, 0); sprites.len()];
    let mut boxes = vec![[0i32; 4]; sprites.len()];
    let mut previous_seed = 0u32;
    for &i in &order {
        let sprite = &sprites[i];
        previous_seed = seed(previous_seed, sprite.rect[2], sprite.rect[3]);
        let mut rng = Rng::new(previous_seed);
        let (mask, origin, bbox) = sprite_mask(sprite, settings.tight);
        let size = [mask.w, mask.h];
        let mut candidate = Candidate {
            padded: [Some(mask.padded(pad)), None, None, None],
            rotation: rotation::NONE,
        };
        let mut placed = None;
        for p in (0..pages.len()).rev() {
            if let Some(at) = fit(&mut rng, &mut candidate, size, &pages[p], settings, false) {
                placed = Some((p, at));
                break;
            }
        }
        let (p, (x, y)) = match placed {
            Some(placed) => placed,
            None => {
                pages.push(Page {
                    bits: Bits::new(max, max),
                    used: [0, 0],
                });
                let p = pages.len() - 1;
                // A first sprite always fits at the origin: the size check came first.
                let at = fit(&mut rng, &mut candidate, size, &pages[p], settings, true)
                    .unwrap_or((0, 0));
                (p, at)
            }
        };
        let padded = candidate.variant(candidate.rotation).clone();
        burn(&mut pages[p], &padded, pad, x, y);
        placements[i] = Placement {
            page: p as u32,
            x,
            y,
            rotation: candidate.rotation,
            origin,
        };
        masks[i] = mask.rotated(candidate.rotation);
        boxes[i] = bbox;
    }
    let page_sizes = (0..pages.len())
        .map(|p| {
            let (mut w, mut h) = (MIN_PAGE_SIZE, MIN_PAGE_SIZE);
            for (i, placement) in placements.iter().enumerate() {
                // An empty mask's box is inverted: it adds nothing.
                if placement.page as usize == p {
                    w = w.max(placement.x.wrapping_add(boxes[i][2]).wrapping_add(1));
                    h = h.max(placement.y.wrapping_add(boxes[i][3]).wrapping_add(1));
                }
            }
            if settings.square {
                let side = w.max(h);
                (w, h) = (side, side);
            }
            [
                power_of_two_at_least(w) as u32,
                power_of_two_at_least(h) as u32,
            ]
        })
        .collect();
    Ok(Layout {
        order,
        placements,
        pages: page_sizes,
        masks,
        sources: sprites.iter().map(|s| s.texture_size).collect(),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A sprite of the Editor capture these tests replay, with where the Editor put it.
    struct Case {
        name: &'static str,
        texture: usize,
        size: [u32; 2],
        rect: [f32; 4],
        uv_transform: [f32; 4],
        vertices: &'static [[f32; 2]],
        indices: &'static [u16],
        page: u32,
        at: [f32; 2],
        rotation: u8,
    }

    // Unity 6000.0.71f1 Editor, an atlas of eight single-sprite textures (Tight and Full Rect
    // meshes; padding 4, no rotation, no tight packing, 128 x 128 pages: two pages), and one of
    // a 64 x 64 sheet's four 32 x 32 Tight cells (padding 2, rotation and tight packing allowed).
    const B: &[Case] = &[
        Case {
            name: "Big",
            texture: 0,
            size: [100, 60],
            rect: [0.0, 0.0, 100.0, 60.0],
            uv_transform: [100.0, 50.0, 100.0, 30.0],
            vertices: &[
                [0.5f32, 0.3f32],
                [0.44f32, -0.3f32],
                [0.5f32, -0.13f32],
                [-0.33f32, -0.3f32],
                [-0.46f32, 0.3f32],
                [-0.5f32, 0.16f32],
                [-0.5f32, -0.14f32],
            ],
            indices: &[6, 5, 3, 4, 3, 5, 0, 3, 4, 1, 3, 0, 2, 1, 0],
            page: 0,
            at: [0.0, 0.0],
            rotation: 0,
        },
        Case {
            name: "Wide",
            texture: 1,
            size: [90, 20],
            rect: [0.0, 0.0, 90.0, 20.0],
            uv_transform: [100.0, 45.0, 100.0, 10.0],
            vertices: &[
                [-0.45f32, 0.1f32],
                [0.45f32, 0.1f32],
                [-0.45f32, -0.1f32],
                [0.45f32, -0.1f32],
            ],
            indices: &[0, 1, 2, 2, 1, 3],
            page: 1,
            at: [0.0, 0.0],
            rotation: 0,
        },
        Case {
            name: "Tall",
            texture: 2,
            size: [18, 70],
            rect: [0.0, 0.0, 18.0, 70.0],
            uv_transform: [100.0, 9.0, 100.0, 35.0],
            vertices: &[
                [-0.09f32, 0.35f32],
                [0.09f32, 0.35f32],
                [-0.09f32, -0.35f32],
                [0.09f32, -0.35f32],
            ],
            indices: &[0, 1, 2, 2, 1, 3],
            page: 1,
            at: [0.0, 24.0],
            rotation: 0,
        },
        Case {
            name: "Small",
            texture: 3,
            size: [12, 12],
            rect: [0.0, 0.0, 12.0, 12.0],
            uv_transform: [100.0, 6.0, 100.0, 6.0],
            vertices: &[
                [-0.06f32, 0.06f32],
                [0.06f32, 0.06f32],
                [-0.06f32, -0.06f32],
                [0.06f32, -0.06f32],
            ],
            indices: &[0, 1, 2, 2, 1, 3],
            page: 1,
            at: [0.0, 98.0],
            rotation: 0,
        },
        Case {
            name: "Ring",
            texture: 4,
            size: [48, 48],
            rect: [0.0, 0.0, 48.0, 48.0],
            uv_transform: [100.0, 24.0, 100.0, 24.0],
            vertices: &[
                [0.2f32, 0.24f32],
                [0.24f32, -0.19f32],
                [0.24f32, 0.15f32],
                [0.19f32, -0.24f32],
                [-0.17f32, 0.24f32],
                [-0.22f32, -0.24f32],
                [-0.24f32, 0.18f32],
                [-0.24f32, -0.16f32],
            ],
            indices: &[7, 6, 5, 4, 5, 6, 3, 5, 4, 0, 3, 4, 1, 3, 0, 2, 1, 0],
            page: 0,
            at: [0.0, 64.0],
            rotation: 0,
        },
        Case {
            name: "Tri",
            texture: 5,
            size: [40, 30],
            rect: [0.0, 0.0, 40.0, 30.0],
            uv_transform: [100.0, 20.0, 100.0, 15.0],
            vertices: &[
                [-0.2f32, 0.15f32],
                [0.2f32, 0.15f32],
                [-0.2f32, -0.15f32],
                [0.2f32, -0.15f32],
            ],
            indices: &[0, 1, 2, 2, 1, 3],
            page: 1,
            at: [22.0, 24.0],
            rotation: 0,
        },
        Case {
            name: "Odd",
            texture: 6,
            size: [33, 17],
            rect: [0.0, 0.0, 33.0, 17.0],
            uv_transform: [100.0, 16.5, 100.0, 8.5],
            vertices: &[
                [-0.165f32, 0.085f32],
                [0.165f32, 0.085f32],
                [-0.165f32, -0.085f32],
                [0.165f32, -0.085f32],
            ],
            indices: &[0, 1, 2, 2, 1, 3],
            page: 1,
            at: [22.0, 58.0],
            rotation: 0,
        },
        Case {
            name: "Tiny",
            texture: 7,
            size: [5, 7],
            rect: [0.0, 0.0, 5.0, 7.0],
            uv_transform: [100.0, 2.5, 100.0, 3.5],
            vertices: &[
                [-0.025f32, 0.035f32],
                [0.025f32, 0.035f32],
                [-0.025f32, -0.035f32],
                [0.025f32, -0.035f32],
            ],
            indices: &[0, 1, 2, 2, 1, 3],
            page: 1,
            at: [59.0, 58.0],
            rotation: 0,
        },
    ];
    const C: &[Case] = &[
        Case {
            name: "Cell0",
            texture: 0,
            size: [64, 64],
            rect: [0.0, 0.0, 32.0, 32.0],
            uv_transform: [100.0, 16.0, 100.0, 16.0],
            vertices: &[
                [-0.16f32, 0.16f32],
                [0.16f32, -0.16f32],
                [0.16f32, 0.16f32],
                [-0.16f32, -0.16f32],
            ],
            indices: &[3, 0, 1, 2, 1, 0],
            page: 0,
            at: [34.0, 0.0],
            rotation: 0,
        },
        Case {
            name: "Cell1",
            texture: 0,
            size: [64, 64],
            rect: [32.0, 0.0, 32.0, 32.0],
            uv_transform: [100.0, 48.0, 100.0, 16.0],
            vertices: &[
                [0.16f32, 0.14f32],
                [0.15f32, -0.16f32],
                [0.16f32, -0.14f32],
                [0.13f32, 0.16f32],
                [-0.13f32, 0.16f32],
                [-0.15f32, -0.16f32],
                [-0.16f32, 0.14f32],
                [-0.16f32, -0.13f32],
            ],
            indices: &[7, 6, 5, 4, 5, 6, 1, 5, 4, 3, 1, 4, 0, 1, 3, 2, 1, 0],
            page: 0,
            at: [0.0, 0.0],
            rotation: 0,
        },
        Case {
            name: "Cell2",
            texture: 0,
            size: [64, 64],
            rect: [0.0, 32.0, 32.0, 32.0],
            uv_transform: [100.0, 16.0, 100.0, 48.0],
            vertices: &[
                [0.02f32, 0.16f32],
                [0.16f32, -0.16f32],
                [0.16f32, -0.11f32],
                [-0.03f32, 0.16f32],
                [-0.16f32, -0.09f32],
                [-0.16f32, -0.16f32],
            ],
            indices: &[5, 4, 1, 3, 1, 4, 0, 1, 3, 2, 1, 0],
            page: 0,
            at: [94.0, 22.0],
            rotation: 2,
        },
        Case {
            name: "Cell3",
            texture: 0,
            size: [64, 64],
            rect: [32.0, 32.0, 32.0, 32.0],
            uv_transform: [100.0, 48.0, 100.0, 48.0],
            vertices: &[
                [0.16f32, 0.14f32],
                [0.15f32, -0.16f32],
                [0.16f32, -0.14f32],
                [0.13f32, 0.16f32],
                [-0.13f32, 0.16f32],
                [-0.15f32, -0.16f32],
                [-0.16f32, 0.14f32],
                [-0.16f32, -0.13f32],
            ],
            indices: &[7, 6, 5, 4, 5, 6, 1, 5, 4, 3, 1, 4, 0, 1, 3, 2, 1, 0],
            page: 0,
            at: [68.0, 0.0],
            rotation: 0,
        },
    ];

    fn sprites(cases: &[Case]) -> Vec<PackSprite<'_>> {
        cases
            .iter()
            .map(|c| PackSprite {
                name: c.name,
                texture_guid: c.texture as u128,
                texture: c.texture,
                secondary_hash: 0,
                texture_size: c.size,
                rect: c.rect,
                uv_transform: c.uv_transform,
                vertices: c.vertices,
                indices: c.indices,
            })
            .collect()
    }

    fn settings(max_size: u32, padding: u32, rotate: bool, tight: bool) -> PackSettings {
        PackSettings {
            max_size,
            padding,
            block_offset: 1,
            allow_rotation: rotate,
            tight,
            square: false,
            alpha_dilation: false,
        }
    }

    fn check(cases: &[Case], settings: &PackSettings, pages: &[[u32; 2]]) {
        let sprites = sprites(cases);
        let layout = layout(&sprites, settings).unwrap();
        for (i, case) in cases.iter().enumerate() {
            let rect = layout.texture_rect(i, case.rect);
            let p = layout.placements[i];
            assert_eq!(
                (p.page, [rect[0], rect[1]], p.rotation),
                (case.page, case.at, case.rotation),
                "{}",
                case.name
            );
        }
        assert_eq!(layout.pages, pages);
    }

    #[test]
    fn places_sprites_as_the_editor() {
        check(
            B,
            &settings(128, 4, false, false),
            &[[128, 128], [128, 128]],
        );
        check(C, &settings(128, 2, true, true), &[[128, 64]]);
    }

    #[test]
    fn orders_by_area_then_name_hash() {
        let sprites = sprites(C);
        let layout = layout(&sprites, &settings(128, 2, true, true)).unwrap();
        // Equal areas on one texture: by the signed FNV-1a of the names.
        let names: Vec<_> = layout.order.iter().map(|&i| C[i].name).collect();
        assert_eq!(names, ["Cell1", "Cell0", "Cell3", "Cell2"]);
        let sprites = sprites_b();
        let layout = layout_b(&sprites);
        let names: Vec<_> = layout.order.iter().map(|&i| B[i].name).collect();
        assert_eq!(
            names,
            ["Big", "Ring", "Wide", "Tall", "Tri", "Odd", "Small", "Tiny"]
        );
    }

    fn sprites_b() -> Vec<PackSprite<'static>> {
        sprites(B)
    }

    fn layout_b(sprites: &[PackSprite]) -> Layout {
        layout(sprites, &settings(128, 4, false, false)).unwrap()
    }

    #[test]
    fn rejects_sprites_larger_than_the_page() {
        let sprites = sprites(B);
        // Big is 100 wide: a 64 page needs 128.
        assert_eq!(
            layout(&sprites, &settings(64, 2, false, false)).unwrap_err(),
            PackError::Oversize { needed: 128 }
        );
        assert_eq!(needed_size([[64.0, 64.0]], 64), 64);
        assert_eq!(needed_size([[65.0, 3.0], [3.0, 300.0]], 64), 512);
    }

    #[test]
    fn hashes_and_words() {
        assert_eq!(
            md4(b"abc"),
            [
                0xa4, 0x48, 0x01, 0x7a, 0xaf, 0x21, 0xd8, 0x52, 0x5f, 0xc1, 0x0a, 0xe8, 0x7a, 0xa6,
                0x72, 0x9d
            ]
        );
        assert_eq!(md4(b"")[..4], [0x31, 0xd6, 0xcf, 0xe0]);
        assert_eq!(fnv1a(""), 0x811c_9dc5);
        assert_eq!(fnv1a("a"), 0xe40c_292c);
        // The first hex digit of each group is its word's lowest nibble.
        let guid = u128::from_str_radix("10000000200000003000000040000000", 16).unwrap();
        assert_eq!(guid_words(guid), [1, 2, 3, 4]);
        assert_eq!(float_feed(f32::INFINITY), b"+inf");
        assert_eq!(float_feed(1.5), 1500i64.to_le_bytes());
    }

    #[test]
    fn masks_pad_and_flip() {
        let mut bits = Bits::new(2, 1);
        bits.set(0, 0);
        let padded = bits.padded(2);
        assert_eq!((padded.w, padded.h), (4, 3));
        let set: Vec<_> = (0..3)
            .flat_map(|y| (0..4).map(move |x| (x, y)))
            .filter(|&(x, y)| padded.get(x, y))
            .collect();
        assert_eq!(
            set,
            [
                (0, 0),
                (1, 0),
                (2, 0),
                (0, 1),
                (1, 1),
                (2, 1),
                (0, 2),
                (1, 2),
                (2, 2)
            ]
        );
        let flipped = bits.rotated(rotation::FLIP_HORIZONTAL);
        assert!(flipped.get(1, 0) && !flipped.get(0, 0));
        // A mask may hang off the page by less than the padding.
        let mut page = Bits::new(4, 4);
        assert!(!overlaps(&padded, &page, 1, 2, 2));
        assert!(overlaps(&padded, &page, 1, 3, 3));
        assert!(!overlaps(&padded, &page, 2, 3, 3));
        page.set(2, 2);
        assert!(overlaps(&padded, &page, 2, 0, 0));
        assert!(overlaps(&padded, &page, 2, -1, 0));
    }

    #[test]
    fn page_pixels_copy_and_fill() {
        let vertices: &[[f32; 2]] = &[[-0.5, 0.5], [0.5, 0.5], [-0.5, -0.5], [0.5, -0.5]];
        let sprite = PackSprite {
            name: "S",
            texture_guid: 1,
            texture: 0,
            secondary_hash: 0,
            texture_size: [2, 2],
            rect: [0.0, 0.0, 2.0, 2.0],
            uv_transform: [2.0, 1.0, 2.0, 1.0],
            vertices,
            indices: &[0, 1, 2, 2, 1, 3],
        };
        let texels: Vec<u8> = (0..16).map(|i| i as u8 * 10 + 1).collect();
        for dilation in [false, true] {
            let settings = PackSettings {
                alpha_dilation: dilation,
                ..settings(32, 2, false, false)
            };
            let layout = layout(&[sprite], &settings).unwrap();
            assert_eq!(layout.pages, [[32, 32]]);
            let pixels = layout.page_pixels(0, &settings, |_| &texels);
            assert_eq!(&pixels[..8], &texels[..8]);
            // The texel right of the sprite takes its neighbour's colour; with alpha dilation
            // it stays transparent.
            let right = &pixels[8..12];
            assert_eq!(&right[..3], &texels[4..7]);
            assert_eq!(right[3], if dilation { 0 } else { texels[7] });
            // Beyond 7 texels nothing is filled.
            assert_eq!(&pixels[9 * 4..10 * 4], &[0, 0, 0, 0]);
        }
    }
}
