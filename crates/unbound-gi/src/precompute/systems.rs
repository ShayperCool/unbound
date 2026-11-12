//! Systems: which renderers of a scene are solved together, where each renderer's realtime
//! lightmap UV space lies in its system's output, and where each system lies in the scene's
//! realtime lightmap atlases (as the Unity 6000.0.71f1 Editor's precompute lays them out,
//! tests/projects/realtimegi; docs/REALTIME_GI.md).
use crate::realtime_uv::{BLOCK_TEXELS, Mask, Output, pack_in_order};
use anyhow::{Result, ensure};

/// A system's output width is a multiple of this many texels (its height of a block).
const SYSTEM_WIDTH_TEXELS: u32 = 4;

/// The blocks an instance's charts use, in its own UV space.
fn block_mask(output: &Output) -> Result<Mask> {
    ensure!(
        output.width % BLOCK_TEXELS == 0 && output.height % BLOCK_TEXELS == 0,
        "realtime lightmap UVs of {}x{} texels are not of whole blocks",
        output.width,
        output.height
    );
    let (width, height) = (
        (output.width / BLOCK_TEXELS) as usize,
        (output.height / BLOCK_TEXELS) as usize,
    );
    let mut mask = Mask::new(width.max(1), height.max(1));
    for chart in &output.charts {
        let [left, bottom, w, h] = chart.rect.map(|v| (v / BLOCK_TEXELS) as usize);
        ensure!(
            left + w <= width && bottom + h <= height && chart.blocks.len() == w * h,
            "a realtime lightmap chart at {:?} outside UVs of {}x{} texels",
            chart.rect,
            output.width,
            output.height
        );
        for y in 0..h {
            for x in 0..w {
                if chart.blocks[y * w + x] {
                    mask.cells[(bottom + y) * width + left + x] = true;
                }
            }
        }
    }
    Ok(mask)
}

/// A system's output: its size in texels and, per instance, the lower left corner of its UV
/// space in it.
#[derive(Debug, Clone, PartialEq)]
pub struct SystemLayout {
    pub width: u32,
    pub height: u32,
    pub origins: Vec<[u32; 2]>,
}

impl SystemLayout {
    /// Instance `k`'s realtime lightmap scale and offset within the system's output
    /// (`dynamicLightmapSTInSystem`), for UVs of `output`'s size.
    pub fn scale_offset(&self, k: usize, output: &Output) -> [f32; 4] {
        let (w, h) = (self.width as f32, self.height as f32);
        let [x, y] = self.origins[k];
        [
            output.width as f32 / w,
            output.height as f32 / h,
            x as f32 / w,
            y as f32 / h,
        ]
    }
}

/// Packs a system's instances: each one's charts move as one shape (its blocks; another
/// instance's charts may take the blocks it leaves free), packed as an instance's charts are
/// but ordered by an unstable sort on the blocks they use, most first (the order of equal
/// shapes is the sort's, [`unbound_assets::sort::introsort`]), the output's width then rounded
/// up to whole multiples of [`SYSTEM_WIDTH_TEXELS`].
pub fn pack_system(outputs: &[&Output]) -> Result<SystemLayout> {
    ensure!(!outputs.is_empty(), "a system without instances");
    let masks = outputs
        .iter()
        .map(|o| block_mask(o))
        .collect::<Result<Vec<_>>>()?;
    let refs: Vec<&Mask> = masks.iter().collect();
    let usage: Vec<usize> = masks.iter().map(Mask::usage).collect();
    let mut order: Vec<usize> = (0..masks.len()).collect();
    unbound_assets::sort::introsort(&mut order, |&a, &b| usage[a] > usage[b]);
    let (positions, width, height) = pack_in_order(&refs, &order)?;
    let mut width = width as u32 * BLOCK_TEXELS;
    width = width.div_ceil(SYSTEM_WIDTH_TEXELS) * SYSTEM_WIDTH_TEXELS;
    Ok(SystemLayout {
        width,
        height: height as u32 * BLOCK_TEXELS,
        origins: positions
            .iter()
            .map(|&(x, y)| [x as u32 * BLOCK_TEXELS, y as u32 * BLOCK_TEXELS])
            .collect(),
    })
}

/// The largest realtime lightmap atlas, in texels per side.
pub const MAX_ATLAS: u32 = 1024;
/// The smallest size the last atlas is reduced to.
const MIN_ATLAS: u32 = 16;

/// The scene's realtime lightmap atlases: each one's size (square), and per system its atlas
/// and lower left corner.
#[derive(Debug, Clone, PartialEq)]
pub struct AtlasLayout {
    pub sizes: Vec<u32>,
    pub placements: Vec<(usize, [u32; 2])>,
}

/// A node of a rectangle packing tree: a leaf is free or used; an inner node is split in two.
struct Node {
    rect: [u32; 4],
    used: bool,
    children: Option<Box<[Node; 2]>>,
}

impl Node {
    fn leaf(rect: [u32; 4]) -> Self {
        Self {
            rect,
            used: false,
            children: None,
        }
    }

    /// Places a `w` x `h` rectangle in the first free leaf (children in order) that holds it,
    /// splitting that leaf along the side it leaves less of: the rectangle's row (or column)
    /// first, then the rectangle itself.
    fn insert(&mut self, w: u32, h: u32) -> Option<[u32; 2]> {
        if let Some(children) = &mut self.children {
            let [first, second] = &mut **children;
            return first.insert(w, h).or_else(|| second.insert(w, h));
        }
        let [x, y, nw, nh] = self.rect;
        if self.used || w > nw || h > nh {
            return None;
        }
        let (dw, dh) = (nw - w, nh - h);
        if dw == 0 && dh == 0 {
            self.used = true;
            return Some([x, y]);
        }
        let halves = if dw <= dh {
            [
                Node::leaf([x, y, nw, h]),
                Node::leaf([x, y + h, nw, nh - h]),
            ]
        } else {
            [
                Node::leaf([x, y, w, nh]),
                Node::leaf([x + w, y, nw - w, nh]),
            ]
        };
        self.children = Some(Box::new(halves));
        self.insert(w, h)
    }
}

/// Packs the rectangles `order` names (in that order) into atlases of `size` texels per side,
/// each into the first atlas that holds it; returns per rectangle its atlas and corner, and how
/// many atlases, or None when a rectangle is larger than an atlas.
fn pack_rectangles(
    sizes: &[[u32; 2]],
    order: &[usize],
    size: u32,
) -> Option<(Vec<(usize, [u32; 2])>, usize)> {
    let mut atlases: Vec<Node> = Vec::new();
    let mut out = vec![(0, [0, 0]); sizes.len()];
    for &i in order {
        let [w, h] = sizes[i];
        let placed = atlases
            .iter_mut()
            .enumerate()
            .find_map(|(a, root)| root.insert(w, h).map(|at| (a, at)));
        out[i] = match placed {
            Some(placed) => placed,
            None => {
                let mut root = Node::leaf([0, 0, size, size]);
                let at = root.insert(w, h)?;
                atlases.push(root);
                (atlases.len() - 1, at)
            }
        };
    }
    Some((out, atlases.len()))
}

/// Packs the systems of a scene (their sizes in texels) into realtime lightmap atlases: the
/// largest (by width plus height, ties in order) first, each into the first atlas of
/// [`MAX_ATLAS`] that holds it, the last atlas then halved while its systems still fit in one.
pub fn pack_atlases(systems: &[[u32; 2]]) -> Result<AtlasLayout> {
    if let Some(s) = systems
        .iter()
        .find(|s| s[0] > MAX_ATLAS || s[1] > MAX_ATLAS)
    {
        anyhow::bail!(
            "a system of {}x{} texels is larger than a realtime lightmap atlas of {MAX_ATLAS}",
            s[0],
            s[1]
        );
    }
    let mut order: Vec<usize> = (0..systems.len()).collect();
    order.sort_by_key(|&i| std::cmp::Reverse(systems[i][0] + systems[i][1]));
    let (mut placements, count) =
        pack_rectangles(systems, &order, MAX_ATLAS).expect("systems no larger than an atlas");
    let mut sizes = vec![MAX_ATLAS; count];
    if let Some(last) = count.checked_sub(1) {
        let mine: Vec<usize> = order
            .iter()
            .copied()
            .filter(|&i| placements[i].0 == last)
            .collect();
        let mut size = MAX_ATLAS;
        while size / 2 >= MIN_ATLAS {
            let Some((smaller, 1)) = pack_rectangles(systems, &mine, size / 2) else {
                break;
            };
            size /= 2;
            for &i in &mine {
                placements[i] = (last, smaller[i].1);
            }
        }
        sizes[last] = size;
    }
    Ok(AtlasLayout { sizes, placements })
}

/// The most texels a system's instances may take, estimated, before they are split.
pub const MAX_SYSTEM_TEXELS: u64 = 4096;

/// An instance as systems are grouped: its world bounds and the texels its UV space takes (its
/// size with each side rounded up to whole blocks).
#[derive(Debug, Clone, Copy)]
pub struct Member {
    pub min: [f32; 3],
    pub max: [f32; 3],
    pub texels: u64,
}

impl Member {
    pub fn new(min: [f32; 3], max: [f32; 3], output: &Output) -> Self {
        let even = |v: u32| v.div_ceil(BLOCK_TEXELS) as u64 * BLOCK_TEXELS as u64;
        Self {
            min,
            max,
            texels: even(output.width) * even(output.height),
        }
    }

    fn centre(&self, axis: usize) -> f32 {
        (self.min[axis] + self.max[axis]) * 0.5
    }
}

/// Splits a group of instances in two along an axis of their bounds: the longest, or one at
/// least half as long, at the position (one of an instance's bounds) that divides their texels
/// most evenly (by their bounds' centres; the first of equally even ones); halves by their order
/// when every centre falls on one side.
fn split(members: &[Member], group: &[usize]) -> [Vec<usize>; 2] {
    let mut lo = [f32::INFINITY; 3];
    let mut hi = [f32::NEG_INFINITY; 3];
    for &i in group {
        for a in 0..3 {
            lo[a] = lo[a].min(members[i].min[a]);
            hi[a] = hi[a].max(members[i].max[a]);
        }
    }
    let mut axes = [0usize, 1, 2];
    let extent = |a: usize| hi[a] - lo[a];
    axes.sort_by(|&a, &b| extent(b).total_cmp(&extent(a)));
    let longest = extent(axes[0]);
    let candidates: Vec<usize> = axes
        .iter()
        .copied()
        .take_while(|&a| a == axes[0] || longest < extent(a) + extent(a))
        .collect();
    let mut best: Option<(f32, usize, f32)> = None;
    let positions = group.iter().flat_map(|&i| {
        let m = members[i];
        candidates
            .iter()
            .map(move |&a| (a, m.min[a]))
            .chain(candidates.iter().map(move |&a| (a, m.max[a])))
    });
    for (axis, at) in positions {
        let (mut below, mut above) = (0u64, 0u64);
        for &i in group {
            if at <= members[i].centre(axis) {
                above += members[i].texels;
            } else {
                below += members[i].texels;
            }
        }
        let total = (below + above) as f32;
        let score = 2.0 * (below as f32 / total - 0.5).abs();
        if best.is_none_or(|(s, _, _)| score < s) {
            best = Some((score, axis, at));
        }
    }
    let (_, axis, at) = best.expect("a group to split");
    let (mut first, mut second): (Vec<usize>, Vec<usize>) =
        group.iter().partition(|&&i| members[i].centre(axis) < at);
    if first.is_empty() || second.is_empty() {
        let all = if first.is_empty() { second } else { first };
        let half = all.len() / 2;
        second = all[half..].to_vec();
        first = all[..half].to_vec();
    }
    [first, second]
}

/// Groups instances that may share a system (equal Lightmap Parameters and System Tag) into
/// systems: all of them while their texels stay within [`MAX_SYSTEM_TEXELS`] and they `fit` one
/// system's output, else split in two ([`split`]) until each part does or holds one instance.
/// Parts are taken last in, first out; systems come in the order they are made.
pub fn group_systems(members: &[Member], fits: impl Fn(&[usize]) -> bool) -> Vec<Vec<usize>> {
    let accepts = |group: &[usize]| {
        group.iter().map(|&i| members[i].texels).sum::<u64>() <= MAX_SYSTEM_TEXELS && fits(group)
    };
    let mut systems = Vec::new();
    if members.is_empty() {
        return systems;
    }
    let all: Vec<usize> = (0..members.len()).collect();
    let mut pending = Vec::new();
    if all.len() > 1 && !accepts(&all) {
        pending.push(all);
    } else {
        systems.push(all);
    }
    while let Some(group) = pending.pop() {
        for part in split(members, &group) {
            if part.is_empty() {
                continue;
            }
            if part.len() > 1 && !accepts(&part) {
                pending.push(part);
            } else {
                systems.push(part);
            }
        }
    }
    systems
}

/// A system's chart mask (row-major from v = 0, [`crate::data::NO_CHART`] where no chart is):
/// every texel of a chart's blocks holds the chart's number, the charts numbered instance by
/// instance in the system's order, each instance's in its own order.
pub fn chart_mask(layout: &SystemLayout, outputs: &[&Output]) -> Vec<u32> {
    let (w, h) = (layout.width as usize, layout.height as usize);
    let mut mask = vec![crate::data::NO_CHART; w * h];
    let mut id = 0u32;
    for (output, origin) in outputs.iter().zip(&layout.origins) {
        for chart in &output.charts {
            let [left, bottom, cw, _] = chart.rect;
            let across = (cw / BLOCK_TEXELS) as usize;
            for (b, _) in chart.blocks.iter().enumerate().filter(|(_, used)| **used) {
                let bx = origin[0] + left + (b % across) as u32 * BLOCK_TEXELS;
                let by = origin[1] + bottom + (b / across) as u32 * BLOCK_TEXELS;
                for y in by..by + BLOCK_TEXELS {
                    for x in bx..bx + BLOCK_TEXELS {
                        if (x as usize) < w && (y as usize) < h {
                            mask[y as usize * w + x as usize] = id;
                        }
                    }
                }
            }
            id += 1;
        }
    }
    mask
}

/// A scene's systems: its instances grouped by `keys` (what makes instances share a system or
/// not: equal Lightmap Parameters and System Tag, `LightmapParameters.systemTag`; groups in the
/// order of their first instance), each group split by size ([`group_systems`]); each system's
/// instances in their order. (The Editor orders groups and their instances by content hashes
/// of its own, which Unbound does not make: its systems may come in another order and equal
/// instances of one system may swap places, which changes no instance's lighting.)
pub fn scene_systems<K: PartialEq>(
    members: &[Member],
    keys: &[K],
    fits: impl Fn(&[usize]) -> bool,
) -> Vec<Vec<usize>> {
    assert_eq!(members.len(), keys.len());
    let mut groups: Vec<Vec<usize>> = Vec::new();
    for (i, key) in keys.iter().enumerate() {
        match groups.iter_mut().find(|g| keys[g[0]] == *key) {
            Some(group) => group.push(i),
            None => groups.push(vec![i]),
        }
    }
    let mut systems = Vec::new();
    for group in groups {
        let local: Vec<Member> = group.iter().map(|&i| members[i]).collect();
        let fits_local = |part: &[usize]| fits(&part.iter().map(|&j| group[j]).collect::<Vec<_>>());
        for system in group_systems(&local, fits_local) {
            systems.push(system.into_iter().map(|j| group[j]).collect());
        }
    }
    systems
}
