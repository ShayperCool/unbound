//! The baked lightmap atlases of a bake: which lightmap each renderer goes to, where, and how
//! big each lightmap is, as the Unity 6000.0.71f1 Editor lays them out for its Progressive
//! lightmappers (tests/projects/bakedatlas). A renderer's rectangle is square in texels
//! (its surface area, its lightmap UVs' bounds, the scene's Lightmap Resolution and its Scale In
//! Lightmap) shaped as its UV bounds; renderers are sorted by their scene, their Lightmap
//! Parameters, their place in space and their size, packed in that order into binary-tree
//! atlases of the Max Lightmap Size, first fit, and the last atlas of each group is made as
//! small as it can be. All arithmetic is in `f32` in the Editor's order, so that the same
//! inputs give the same layout.
use super::spooky;
use anyhow::{Result, bail, ensure};

/// The scene's settings that lay out its atlases (Lighting Settings).
#[derive(Clone, Copy, Debug)]
pub struct Settings {
    /// Max Lightmap Size (texels; the atlases' width and height before reduction).
    pub max_size: i32,
    /// Lightmap Padding (texels).
    pub padding: i32,
    /// Lightmap Resolution (texels per unit).
    pub resolution: f32,
    /// Atlases stay at the Max Lightmap Size (Fixed Lightmap Size, or a project using Entities
    /// Graphics).
    pub fixed_size: bool,
}

/// The fields of a renderer's Lightmap Parameters that keep apart the atlases of the
/// Progressive lightmappers.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Parameters {
    pub back_face_tolerance: f32,
    pub anti_aliasing_samples: i32,
    pub pushoff: f32,
    pub baked_lightmap_tag: i32,
    /// Max Lightmap Count where Limit Lightmap Count is on, else 0.
    pub max_lightmap_count: i32,
}

impl Parameters {
    /// Default-Medium's (a new Lightmap Parameters asset's too).
    pub const DEFAULT: Parameters = Parameters {
        back_face_tolerance: 0.9,
        anti_aliasing_samples: 2,
        pushoff: 0.0001,
        baked_lightmap_tag: -1,
        max_lightmap_count: 0,
    };

    /// What the atlases group by: the 128-bit hash of the fields in their order.
    pub fn hash(&self) -> (u64, u64) {
        let mut bytes = [0u8; 20];
        bytes[0..4].copy_from_slice(&self.back_face_tolerance.to_le_bytes());
        bytes[4..8].copy_from_slice(&self.anti_aliasing_samples.to_le_bytes());
        bytes[8..12].copy_from_slice(&self.pushoff.to_le_bytes());
        bytes[12..16].copy_from_slice(&self.baked_lightmap_tag.to_le_bytes());
        bytes[16..20].copy_from_slice(&self.max_lightmap_count.to_le_bytes());
        spooky::hash128_short(&bytes, (0, 0))
    }
}

/// A renderer of the bake.
#[derive(Clone, Debug)]
pub struct Instance {
    /// Its scene's GUID (16 bytes as Unity keeps a GUID: four 32-bit words).
    pub scene: [u32; 4],
    pub parameters: Parameters,
    /// Its rectangle in texels ([`rectangle`]).
    pub rect: [f32; 2],
    /// The bounds of its lightmap UVs: min x, min y, width, height ([`uv_bounds`]).
    pub uv_bounds: [f32; 4],
    /// [`morton`] of its position.
    pub morton: u32,
}

/// Where a renderer goes: its lightmap and its lightmap scale and offset (UV × xy + zw).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Placement {
    pub lightmap: u32,
    pub scale_offset: [f32; 4],
}

#[derive(Clone, Debug, PartialEq)]
pub struct Layout {
    /// Each lightmap's width and height.
    pub lightmaps: Vec<[i32; 2]>,
    /// Per instance, in the order given.
    pub placements: Vec<Placement>,
}

/// The world surface area of a mesh: its vertices through `local_to_world` (column-major) and
/// the area of each triangle whose indices are all in range, summed in order.
pub fn surface_area(
    positions: &[[f32; 3]],
    triangles: &[[u32; 3]],
    local_to_world: &[f32; 16],
) -> f32 {
    let m = local_to_world;
    let world: Vec<[f32; 3]> = positions
        .iter()
        .map(|&[x, y, z]| {
            let row = |r: usize| m[8 + r] * z + (m[4 + r] * y + m[r] * x) + m[12 + r];
            [row(0), row(1), row(2)]
        })
        .collect();
    let n = world.len();
    let mut area = 0f32;
    for t in triangles {
        if t.iter().any(|&i| i as usize >= n) {
            continue;
        }
        let [p0, p1, p2] = [
            world[t[0] as usize],
            world[t[1] as usize],
            world[t[2] as usize],
        ];
        let e1 = [p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]];
        let e2 = [p2[0] - p0[0], p2[1] - p0[1], p2[2] - p0[2]];
        let cx = e1[1] * e2[2] - e1[2] * e2[1];
        let cy = e1[2] * e2[0] - e2[2] * e1[0];
        let cz = e2[1] * e1[0] - e2[0] * e1[1];
        area += ((cy * cy + cx * cx) + cz * cz).sqrt() * 0.5;
    }
    area
}

/// The bounds of a mesh's lightmap UVs (all its vertices): min x, min y, width, height; zeros
/// for none.
pub fn uv_bounds(uvs: &[[f32; 2]]) -> [f32; 4] {
    let Some(first) = uvs.first() else {
        return [0.0; 4];
    };
    let (mut min, mut max) = (*first, *first);
    for uv in &uvs[1..] {
        for k in 0..2 {
            if uv[k] < min[k] {
                min[k] = uv[k];
            }
            if uv[k] > max[k] {
                max[k] = uv[k];
            }
        }
    }
    [min[0], min[1], max[0] - min[0], max[1] - min[1]]
}

/// A renderer's rectangle in texels: a square side of its texels per unit (Scale In Lightmap ×
/// Lightmap Resolution, halved for each LOD level below the most detailed it is in) times the
/// longer side of its UV bounds stretched to its surface area, floored and kept between 1 and
/// the Max Lightmap Size; then shaped as its UV bounds (the shorter side scaled down).
pub fn rectangle(
    scale_in_lightmap: f32,
    resolution: f32,
    lod_level: Option<u32>,
    area: f32,
    uv_bounds: [f32; 4],
    max_size: i32,
) -> [f32; 2] {
    let lod_scale = match lod_level {
        Some(level) => (0..level).fold(1f32, |s, _| s * 0.5),
        None => 1.0,
    };
    let mut s = scale_in_lightmap * resolution * lod_scale;
    if s <= 0.0 {
        s = 1e-7;
    }
    let [_, _, w, h] = uv_bounds;
    let r = (area / (w * h)).sqrt();
    let (rw, rh) = (r * w, r * h);
    let v = if rw > rh { rw } else { rh };
    let t = (s * v).floor();
    let max = max_size as f32;
    let side = if t < 1.0 {
        1.0
    } else if max < t {
        max
    } else {
        t
    };
    let side = if side.is_finite() { side } else { 1.0 };
    let aspect = if w > h {
        let a = h / w;
        if a.is_finite() { [1.0, a] } else { [1.0, 1.0] }
    } else {
        let a = w / h;
        if a.is_finite() { [a, 1.0] } else { [1.0, 1.0] }
    };
    [aspect[0] * side, aspect[1] * side]
}

/// The Morton code the atlases sort renderers by: their position in tenths of a unit (offset by
/// 65536 units, or its magnitude where a coordinate is below -65536), the low 10 bits of each
/// axis interleaved (x lowest).
pub fn morton(position: [f32; 3]) -> u32 {
    let p = if position.iter().any(|&c| c < -65536.0) {
        position.map(f32::abs)
    } else {
        position.map(|c| c + 65536.0)
    };
    let spread = |c: f32| -> u32 {
        let mut x = ((c * 10.0) as i64 as u32) & 0x3ff;
        x = (x | x << 16) & 0x0300_00ff;
        x = (x | x << 8) & 0x0300_f00f;
        x = (x | x << 4) & 0x030c_30c3;
        (x | x << 2) & 0x0924_9249
    };
    spread(p[0]) | spread(p[1]) << 1 | spread(p[2]) << 2
}

/// A node of an atlas's binary tree.
struct Node {
    /// The first of its two children (the second follows it), or none for a leaf.
    children: Option<usize>,
    rect: [f32; 4],
    filled: bool,
    /// Which sides of the atlas it touches: 1 left, 2 right, 4 bottom, 8 top (see [`insert`]).
    edges: u32,
}

impl Node {
    fn new(rect: [f32; 4], edges: u32) -> Node {
        Node {
            children: None,
            rect,
            filled: false,
            edges,
        }
    }
}

/// Puts a `w` × `h` rectangle with `padding` on its right and top into the tree below `node`:
/// the leaf it fills, or none. With `shrink` a rectangle too big for a leaf spanning the atlas
/// is scaled down to it (`w` and `h` change). Leaves are split along their longer leftover side,
/// at a whole texel; at the atlas's right and top sides the padding may be left out.
fn insert(
    nodes: &mut Vec<Node>,
    mut node: usize,
    mut pad_x: f32,
    mut pad_y: f32,
    w: &mut f32,
    h: &mut f32,
    shrink: bool,
) -> Option<usize> {
    loop {
        while let Some(first) = nodes[node].children {
            if let Some(found) = insert(nodes, first, pad_x, pad_y, w, h, shrink) {
                return Some(found);
            }
            node = first + 1;
        }
        let Node {
            rect: [x, y, nw, nh],
            filled,
            edges,
            ..
        } = nodes[node];
        if filled {
            return None;
        }
        if shrink {
            let across = if edges & 3 == 3 && *w > nw {
                nw / *w
            } else {
                1.0
            };
            let along = if edges & 0xc == 0xc && *h > nh {
                nh / *h
            } else {
                1.0
            };
            let s = if along < across { along } else { across };
            *w *= s;
            *h = s * *h;
        }
        let (rw, rh) = (*w, *h);
        let avail_w = (nw - pad_x) + 0.5;
        let avail_h = (nh - pad_y) + 0.5;
        let left_h = nh - rh;
        let left_w = nw - rw;
        if rw > avail_w || rh > avail_h {
            let mut refused = false;
            if rw > avail_w {
                refused = edges & 2 == 0 || nw < rw;
                if !refused {
                    pad_x = left_w;
                }
            }
            if rh > avail_h {
                if nh < rh || edges & 8 == 0 {
                    return None;
                }
                pad_y = left_h;
            }
            if refused {
                return None;
            }
        }
        let floor = |p: f32| if 1e-5 > p { 1e-5 } else { p };
        if floor(pad_y + pad_y) >= left_h && floor(pad_x + pad_x) >= left_w {
            nodes[node].filled = true;
            return Some(node);
        }
        let (first, second, second_xy, inherit, first_only, second_only);
        if left_w > left_h {
            let c = (rw + pad_x).ceil();
            let v = [(c + x) - x, (nh + y) - y];
            if (v[0] - nw).abs() < 1e-5 && (v[1] - nh).abs() < 1e-5 {
                nodes[node].filled = true;
                return Some(node);
            }
            first = [x, y, v[0], v[1]];
            second_xy = [x + c, y];
            (inherit, first_only, second_only) = (4 | 8, 1, 2);
        } else {
            let c = (rh + pad_y).ceil();
            let v = [(nw + x) - x, (c + y) - y];
            if (v[0] - nw).abs() < 1e-5 && (v[1] - nh).abs() < 1e-5 {
                nodes[node].filled = true;
                return Some(node);
            }
            first = [x, y, v[0], v[1]];
            second_xy = [x, y + c];
            (inherit, first_only, second_only) = (1 | 2, 4, 8);
        }
        second = [
            second_xy[0],
            second_xy[1],
            (nw + x) - second_xy[0],
            (nh + y) - second_xy[1],
        ];
        let at = nodes.len();
        nodes.push(Node::new(first, edges & (inherit | first_only)));
        nodes.push(Node::new(second, edges & (inherit | second_only)));
        nodes[node].children = Some(at);
        node = at;
    }
}

/// Packs `rects` in order into atlases of `size`, first fit: each one's atlas and position, and
/// how many atlases; none when one does not fit (only without `shrink`).
fn pack(
    padding: f32,
    rects: &mut [[f32; 2]],
    size: [i32; 2],
    shrink: bool,
) -> Option<(Vec<(u32, [f32; 2])>, u32)> {
    let mut nodes = Vec::new();
    let mut roots: Vec<usize> = Vec::new();
    let mut placed = Vec::with_capacity(rects.len());
    let full = [size[0] as f32, size[1] as f32];
    for rect in rects.iter_mut() {
        let mut k = 0;
        loop {
            if !shrink && (full[0] < rect[0] || full[1] < rect[1]) {
                return None;
            }
            let fresh = k == roots.len();
            if fresh {
                roots.push(nodes.len());
                nodes.push(Node::new([0.0, 0.0, full[0], full[1]], 0xf));
            }
            let [w, h] = rect;
            if let Some(leaf) = insert(&mut nodes, roots[k], padding, padding, w, h, shrink) {
                let [x, y, ..] = nodes[leaf].rect;
                placed.push((k as u32, [x, y]));
                break;
            }
            if fresh {
                // Only a size that is not a number fails an empty atlas.
                return None;
            }
            k += 1;
        }
    }
    Some((placed, roots.len() as u32))
}

/// The atlas a group's last atlas shrinks to: halved (both sides) while its rectangles still fit
/// one atlas, from at least 32 texels; its size and their new positions, or none when it keeps
/// its size.
fn reduce(padding: f32, rects: &[[f32; 2]], size: [i32; 2]) -> Option<([i32; 2], Vec<[f32; 2]>)> {
    let [mut w, mut h] = size;
    let mut best = None;
    if w < 32 || h < 32 {
        return None;
    }
    loop {
        let half = [w / 2, h / 2];
        let mut trial = rects.to_vec();
        match pack(padding, &mut trial, half, false) {
            Some((placed, count)) if count <= 1 => {
                best = Some((half, placed.into_iter().map(|(_, p)| p).collect()));
            }
            _ => break,
        }
        if w < 64 || h < 64 {
            break;
        }
        (w, h) = (half[0], half[1]);
    }
    best
}

fn compare(a: &Instance, ha: (u64, u64), b: &Instance, hb: (u64, u64)) -> std::cmp::Ordering {
    a.scene
        .cmp(&b.scene)
        .then(ha.cmp(&hb))
        .then(a.morton.cmp(&b.morton))
        .then_with(|| {
            let (sa, sb) = (a.rect[0] + a.rect[1], b.rect[0] + b.rect[1]);
            // The larger first; ties (and NaN) keep their order.
            if sb < sa {
                std::cmp::Ordering::Less
            } else if sa < sb {
                std::cmp::Ordering::Greater
            } else {
                std::cmp::Ordering::Equal
            }
        })
}

/// Lays out the atlases of `instances` (the bake's renderers in the order the Editor numbers
/// them).
pub fn layout(settings: &Settings, instances: &[Instance]) -> Result<Layout> {
    ensure!(
        settings.max_size > 0 && settings.padding >= 0,
        "a Max Lightmap Size of {} and a Lightmap Padding of {}",
        settings.max_size,
        settings.padding
    );
    if let Some(i) = instances
        .iter()
        .find(|i| i.parameters.max_lightmap_count != 0)
    {
        bail!(
            "Lightmap Parameters with Limit Lightmap Count (a Max Lightmap Count of {}) are not supported",
            i.parameters.max_lightmap_count
        );
    }
    let hashes: Vec<(u64, u64)> = instances.iter().map(|i| i.parameters.hash()).collect();
    let mut order: Vec<usize> = (0..instances.len()).collect();
    // Stable: ties stay in the given order.
    order.sort_by(|&a, &b| compare(&instances[a], hashes[a], &instances[b], hashes[b]));
    let size = [settings.max_size, settings.max_size];
    let padding = settings.padding as f32;
    let mut lightmaps = Vec::new();
    let mut placements = vec![
        Placement {
            lightmap: 0,
            scale_offset: [0.0; 4]
        };
        instances.len()
    ];
    let mut start = 0;
    while start < order.len() {
        let first = order[start];
        let end = start
            + order[start..]
                .iter()
                .take_while(|&&i| {
                    instances[i].scene == instances[first].scene && hashes[i] == hashes[first]
                })
                .count();
        let group = &order[start..end];
        let mut rects: Vec<[f32; 2]> = group.iter().map(|&i| instances[i].rect).collect();
        let Some((mut placed, count)) = pack(padding, &mut rects, size, true) else {
            bail!("a renderer does not fit a lightmap");
        };
        let mut sizes = vec![size; count as usize];
        if !settings.fixed_size {
            let last = count - 1;
            let members: Vec<usize> = (0..group.len()).filter(|&k| placed[k].0 == last).collect();
            let last_rects: Vec<[f32; 2]> = members.iter().map(|&k| rects[k]).collect();
            if let Some((reduced, positions)) = reduce(padding, &last_rects, size) {
                sizes[last as usize] = reduced;
                for (&k, p) in members.iter().zip(positions) {
                    placed[k].1 = p;
                }
            }
        }
        let base = lightmaps.len() as u32;
        for (k, &i) in group.iter().enumerate() {
            let (atlas, [px, py]) = placed[k];
            let [aw, ah] = sizes[atlas as usize];
            let inv = 1.0 / (if aw < ah { ah } else { aw }) as f32;
            let [ux, uy, uw, uh] = instances[i].uv_bounds;
            let k = if uw <= uh {
                rects[k][1] / uh
            } else {
                rects[k][0] / uw
            };
            placements[i] = Placement {
                lightmap: base + atlas,
                scale_offset: [inv * k, inv * k, inv * (px - k * ux), inv * (py - k * uy)],
            };
        }
        lightmaps.extend(sizes);
        start = end;
    }
    Ok(Layout {
        lightmaps,
        placements,
    })
}
