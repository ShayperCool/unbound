//! The automatic slicing a 2D project's new texture gets at its first import: the sprite rects the
//! Editor writes into its `.meta` (matches the Unity 6000.0.71f1 Editor, tests/projects/spritemeta
//! sprites.New*, sprites.Slice*, sprites.Sess*).
//!
//! The measured rule: the regions of pixels whose alpha is not 0, 8-connected, listed as a scan
//! from the bottom row up, each row left to right, meets them; a region narrower or lower than
//! [`MIN_SIZE`] is small: the small ones, from the last listed back, each join the nearest region
//! that is not small (the rect around both), nearest by the squared gap to that region as earlier
//! joins grew it, of equal ones the first listed (spritemeta sprites.NearChebyshev, NearCentre,
//! NearTieRow, NearGrown, NearChain, SliceSmallBetween); without any region that is not small the
//! texture is one sprite of its whole size. A region that is not small whose rect lies inside a
//! larger such region's rect is part of it (OrderEqualLeft; one only overlapping is its own,
//! OrderTieClamp). Each rect grows by one pixel on every side, within the
//! texture. The sprites come in rows: the last listed rect left and every rect whose rows touch its
//! rows form a row, ordered left to right, of equal left the one of the higher top first; then the
//! next row of what is left (OrderRows, OrderStair, OrderTall, OrderGrid1-3, OrderClamp,
//! OrderAnchorJoin, OrderTieJoin, SliceChain and the others; of equal left the higher top first,
//! not the later listed: OrderTieClamp, OrderTieJoined).

/// The size under which a region is small (in either direction).
pub const MIN_SIZE: u32 = 4;

/// A rect in texture pixels, bottom-left origin.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Rect {
    pub x: u32,
    pub y: u32,
    pub width: u32,
    pub height: u32,
}

/// The rects of a `width` x `height` texture's sprites; `alpha(x, y)` (bottom-left origin) the
/// texel's alpha.
pub fn slice(width: u32, height: u32, alpha: impl Fn(u32, u32) -> u8) -> Vec<Rect> {
    let whole = vec![Rect {
        x: 0,
        y: 0,
        width,
        height,
    }];
    if width == 0 || height == 0 {
        return whole;
    }
    let regions = regions(width, height, &alpha);
    let small = |r: &[u32; 4]| r[2] - r[0] + 1 < MIN_SIZE || r[3] - r[1] + 1 < MIN_SIZE;
    if regions.iter().all(small) {
        return whole;
    }
    // A region that is not small whose rect lies inside a larger one's is part of it, not a sprite
    // (OrderEqualLeft; one whose rect only overlaps another's is its own, OrderTieClamp, and so is
    // one inside a rect a join grew, OrderTieJoin, OrderTieJoined).
    let contained = contained_regions(&regions, &small);
    let target = |j: usize| !small(&regions[j]) && !contained[j];
    // The small regions, from the last listed back, each join the nearest region that is not
    // small as earlier joins grew it; of equal ones the first listed.
    let mut grown = regions.clone();
    let mut kept: Vec<bool> = contained.iter().map(|c| !c).collect();
    for (i, s) in regions.iter().enumerate().rev() {
        if !small(s) {
            continue;
        }
        let mut best: Option<(usize, u64)> = None;
        for (j, _) in regions.iter().enumerate() {
            if !target(j) {
                continue;
            }
            let d = gap(s, &grown[j]);
            if best.is_none_or(|(_, b)| d < b) {
                best = Some((j, d));
            }
        }
        let (j, _) = best.expect("a region that is not small exists");
        let b = grown[j];
        grown[j] = [
            s[0].min(b[0]),
            s[1].min(b[1]),
            s[2].max(b[2]),
            s[3].max(b[3]),
        ];
        kept[i] = false;
    }
    let mut left: Vec<Rect> = grown
        .iter()
        .zip(&kept)
        .filter(|(_, kept)| **kept)
        .map(|(r, _)| {
            let (x0, y0) = (r[0].saturating_sub(1), r[1].saturating_sub(1));
            let (x1, y1) = ((r[2] + 2).min(width), (r[3] + 2).min(height));
            Rect {
                x: x0,
                y: y0,
                width: x1 - x0,
                height: y1 - y0,
            }
        })
        .collect();
    // Rows: the last listed rect and every rect whose rows touch its rows, left to right, of equal
    // left the one of the higher top first.
    let mut rects = Vec::with_capacity(left.len());
    while let Some(last) = left.last().copied() {
        let (lo, hi) = (last.y, last.y + last.height);
        let mut row: Vec<Rect> = Vec::new();
        left.retain(|r| {
            let touches = r.y <= hi && lo <= r.y + r.height;
            if touches {
                row.push(*r);
            }
            !touches
        });
        row.sort_by_key(|r| (r.x, std::cmp::Reverse(r.y + r.height)));
        rects.extend(row);
    }
    rects
}

/// Which regions that are not small lie inside the rect of a larger one that is not small (its
/// area larger, so equal rects are not each other's); those of equal size are not compared,
/// which keeps a texture of many like regions linear after the sort.
fn contained_regions(regions: &[[u32; 4]], small: &impl Fn(&[u32; 4]) -> bool) -> Vec<bool> {
    let area = |r: &[u32; 4]| u64::from(r[2] - r[0] + 1) * u64::from(r[3] - r[1] + 1);
    let mut big: Vec<usize> = (0..regions.len())
        .filter(|&i| !small(&regions[i]))
        .collect();
    big.sort_by_key(|&i| std::cmp::Reverse(area(&regions[i])));
    let mut contained = vec![false; regions.len()];
    for (n, &i) in big.iter().enumerate() {
        let a = &regions[i];
        for &j in &big[..n] {
            let b = &regions[j];
            if area(b) == area(a) {
                break;
            }
            if b[0] <= a[0] && b[1] <= a[1] && a[2] <= b[2] && a[3] <= b[3] {
                contained[i] = true;
                break;
            }
        }
    }
    contained
}

/// The squared gap between two inclusive pixel rects (0 when they touch or overlap).
fn gap(a: &[u32; 4], b: &[u32; 4]) -> u64 {
    let dx = (a[0].max(b[0]) as i64 - a[2].min(b[2]) as i64 - 1).max(0) as u64;
    let dy = (a[1].max(b[1]) as i64 - a[3].min(b[3]) as i64 - 1).max(0) as u64;
    dx * dx + dy * dy
}

/// The 8-connected regions of non-zero alpha as inclusive rects `[x0, y0, x1, y1]`, in the order a
/// scan from the bottom row up, left to right, meets them.
fn regions(width: u32, height: u32, alpha: &impl Fn(u32, u32) -> u8) -> Vec<[u32; 4]> {
    let (w, h) = (width as usize, height as usize);
    let mut seen = vec![false; w * h];
    let mut out = Vec::new();
    let mut stack = Vec::new();
    for y in 0..height {
        for x in 0..width {
            let at = y as usize * w + x as usize;
            if seen[at] || alpha(x, y) == 0 {
                continue;
            }
            seen[at] = true;
            stack.push((x, y));
            let mut r = [x, y, x, y];
            while let Some((px, py)) = stack.pop() {
                r = [r[0].min(px), r[1].min(py), r[2].max(px), r[3].max(py)];
                for dy in -1i64..=1 {
                    for dx in -1i64..=1 {
                        let (qx, qy) = (px as i64 + dx, py as i64 + dy);
                        if qx < 0 || qy < 0 || qx >= w as i64 || qy >= h as i64 {
                            continue;
                        }
                        let q = qy as usize * w + qx as usize;
                        if !seen[q] && alpha(qx as u32, qy as u32) != 0 {
                            seen[q] = true;
                            stack.push((qx as u32, qy as u32));
                        }
                    }
                }
            }
            out.push(r);
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::{Rect, slice};
    use std::collections::HashSet;

    fn rect(x: u32, y: u32, width: u32, height: u32) -> Rect {
        Rect {
            x,
            y,
            width,
            height,
        }
    }

    /// Blocks `(x0, x1, y0, y1)` inclusive, opaque.
    fn image(blocks: &[(u32, u32, u32, u32)]) -> impl Fn(u32, u32) -> u8 {
        let set: HashSet<(u32, u32)> = blocks
            .iter()
            .flat_map(|&(x0, x1, y0, y1)| {
                (x0..=x1).flat_map(move |x| (y0..=y1).map(move |y| (x, y)))
            })
            .collect();
        move |x, y| if set.contains(&(x, y)) { 255 } else { 0 }
    }

    /// The Editor's rects (tests/projects/spritemeta Reference): grown by a pixel, clamped;
    /// small regions joined to the nearest; all small: the whole texture.
    #[test]
    fn the_editors_rects() {
        let s = |blocks: &[(u32, u32, u32, u32)]| slice(16, 16, image(blocks));
        // SliceEdge: two far blocks, top first.
        assert_eq!(
            s(&[(0, 3, 12, 15), (10, 13, 2, 5)]),
            [rect(0, 11, 5, 5), rect(9, 1, 6, 6)]
        );
        // SliceDiag: blocks touching at a corner are one region.
        assert_eq!(s(&[(2, 4, 2, 4), (5, 7, 5, 7)]), [rect(1, 1, 8, 8)]);
        // SliceDot: a pixel joins the block.
        assert_eq!(s(&[(12, 12, 12, 12), (2, 5, 2, 5)]), [rect(1, 1, 13, 13)]);
        // SliceAlone3 / SliceAlone4: the threshold.
        assert_eq!(s(&[(6, 8, 6, 8)]), [rect(0, 0, 16, 16)]);
        assert_eq!(s(&[(6, 9, 6, 9)]), [rect(5, 5, 6, 6)]);
        // SliceEmpty.
        assert_eq!(s(&[]), [rect(0, 0, 16, 16)]);
        // SliceThin: narrow is small.
        assert_eq!(s(&[(2, 4, 2, 11), (9, 14, 9, 14)]), [rect(1, 1, 15, 15)]);
        // SliceChain: rows from the top.
        assert_eq!(
            s(&[(6, 9, 1, 4), (12, 15, 5, 8), (0, 3, 9, 12)]),
            [rect(0, 8, 5, 6), rect(11, 4, 5, 6), rect(5, 0, 6, 6)]
        );
        // SliceTwoBigDot: one row.
        assert_eq!(
            s(&[(1, 5, 1, 5), (10, 14, 10, 14), (12, 12, 7, 7)]),
            [rect(0, 0, 7, 7), rect(9, 6, 7, 10)]
        );
        // SliceSmallBetween: a tie joins the first listed (the lower).
        assert_eq!(
            s(&[(1, 4, 1, 4), (11, 14, 11, 14), (7, 8, 7, 8)]),
            [rect(0, 0, 10, 10), rect(10, 10, 6, 6)]
        );
        // SliceRowMerge.
        assert_eq!(
            s(&[(1, 4, 1, 4), (11, 14, 1, 4), (6, 9, 12, 15), (6, 7, 6, 7)]),
            [rect(5, 11, 6, 5), rect(0, 0, 9, 9), rect(10, 0, 6, 6)]
        );
        // OrderRows / OrderStair: rects of touching rows are one row, left to right.
        assert_eq!(
            s(&[(10, 13, 8, 11), (0, 3, 6, 9)]),
            [rect(0, 5, 5, 6), rect(9, 7, 6, 6)]
        );
        assert_eq!(
            s(&[(0, 3, 0, 3), (5, 8, 3, 6), (10, 13, 6, 9)]),
            [rect(0, 0, 5, 5), rect(4, 2, 6, 6), rect(9, 5, 6, 6)]
        );
        // NearChebyshev / NearCentre: the squared gap decides; NearTieRow: a tie on one row joins
        // the left one.
        assert_eq!(
            s(&[(0, 3, 0, 3), (12, 15, 4, 10), (7, 7, 7, 7)]),
            [rect(0, 0, 5, 5), rect(6, 3, 10, 9)]
        );
        assert_eq!(
            s(&[(0, 3, 0, 3), (8, 15, 8, 15), (6, 6, 6, 6)]),
            [rect(0, 0, 5, 5), rect(5, 5, 11, 11)]
        );
        assert_eq!(
            s(&[(0, 3, 10, 13), (10, 13, 10, 13), (6, 7, 4, 4)]),
            [rect(0, 3, 9, 12), rect(9, 9, 6, 6)]
        );
        // OrderTall: the last listed rect starts the row; OrderGrid2: of equal left the higher
        // first; NearGrown: the later pixel joins first.
        assert_eq!(
            s(&[(0, 3, 1, 4), (6, 9, 0, 15), (12, 15, 10, 13)]),
            [rect(5, 0, 6, 16), rect(11, 9, 5, 6), rect(0, 0, 5, 6)]
        );
        assert_eq!(
            s(&[(0, 3, 0, 3), (6, 9, 0, 3), (0, 3, 6, 9), (6, 9, 6, 9)]),
            [
                rect(0, 5, 5, 6),
                rect(0, 0, 5, 5),
                rect(5, 5, 6, 6),
                rect(5, 0, 6, 5)
            ]
        );
        assert_eq!(
            slice(
                16,
                8,
                image(&[(0, 3, 0, 3), (12, 15, 0, 3), (6, 6, 0, 0), (9, 9, 0, 0)])
            ),
            [rect(0, 0, 8, 5), rect(8, 0, 8, 5)]
        );
        // NearChain: pixels join from the last listed back, to the regions as grown;
        // OrderTieJoin: of equal left the higher top first.
        assert_eq!(
            slice(
                16,
                8,
                image(&[
                    (0, 3, 0, 3),
                    (12, 15, 0, 3),
                    (5, 5, 0, 0),
                    (7, 7, 0, 0),
                    (9, 9, 0, 0)
                ])
            ),
            [rect(0, 0, 7, 5), rect(6, 0, 10, 5)]
        );
        assert_eq!(
            s(&[(0, 12, 8, 11), (0, 3, 2, 5), (12, 12, 0, 0)]),
            [rect(0, 0, 14, 13), rect(0, 1, 5, 6)]
        );
        // SliceNpot: a 10x6 texture of small regions.
        assert_eq!(
            slice(10, 6, image(&[(1, 2, 1, 2), (6, 8, 2, 4)])),
            [rect(0, 0, 10, 6)]
        );
        // An opaque texture: one sprite of its size.
        assert_eq!(slice(6, 5, |_, _| 255), [rect(0, 0, 6, 5)]);
        // A faint texel is opaque.
        assert_eq!(slice(4, 4, |_, _| 1), [rect(0, 0, 4, 4)]);
    }

    /// A region inside a larger one's rect is part of it (spritemeta sprites.OrderEqualLeft); one
    /// only overlapping it is its own, and of equal left the higher top comes first
    /// (OrderTieClamp, OrderTieJoined), not the later listed.
    #[test]
    fn contained_regions_join_their_container_and_ties_take_the_higher_top() {
        let c_with_block = image(&[(2, 9, 1, 2), (9, 10, 1, 14), (4, 10, 13, 14), (2, 5, 5, 9)]);
        assert_eq!(slice(16, 16, c_with_block), [rect(1, 0, 11, 16)]);
        let clamp = image(&[(1, 6, 0, 2), (5, 6, 0, 11), (1, 6, 9, 11), (0, 3, 4, 7)]);
        assert_eq!(slice(16, 16, clamp), [rect(0, 0, 8, 13), rect(0, 3, 5, 6)]);
        let joined = image(&[(0, 14, 0, 3), (14, 14, 11, 11), (0, 3, 6, 9)]);
        assert_eq!(
            slice(16, 16, joined),
            [rect(0, 0, 16, 13), rect(0, 5, 5, 6)]
        );
    }
}
