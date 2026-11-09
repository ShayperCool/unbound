//! The precomputed realtime GI data of a scene's LightingData asset: the scene mapping (systems,
//! their atlases and renderers) and, per system, the input samples, the transport and the
//! cluster material samples, read from the payload bundle the Editor stores in the asset (see
//! docs/REALTIME_GI.md). Only the reference (`.ref`) layouts are read.
use anyhow::{Context, Result, bail, ensure};
use unbound_assets::bundle::Bundle;
use unbound_yaml::Value;

/// Header magic of the per-system files.
const FILE_MAGIC: u32 = 0xAD10_5174;
/// File format versions this reader knows (input samples, transport, cluster materials).
const INPUT_VERSION: i32 = 17;
const TRANSPORT_VERSION: i32 = 37;
const MATERIAL_VERSION: i32 = 12;
/// Block types inside the files.
const INPUT_MAIN_BLOCK: u16 = 4;
const TRANSPORT_BLOCK: u16 = 1;
const BOUNCE_BLOCK: u16 = 27;
const MATERIAL_BLOCK: u16 = 6;
/// Magics of the sections inside the blocks.
const INPUT_MAGIC: u32 = 0x5749_4547;
const TREE_MAGIC: u32 = 0x4744_4547;
const TRANSPORT_MAGIC: u32 = 0x4753_4547;
const BOUNCE_MAGIC: u32 = 0x4452_4547;
/// The environment's dependency id in a transport.
pub const ENVIRONMENT_ID: [u8; 16] = [0, 0, 0, 0, 0, 0, 0, 0, 0xFE, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0];
/// The dependency id of an input that is never present (its references add nothing).
pub const ABSENT_ID: [u8; 16] = [0xFF; 16];
/// The environment's values: six faces of 8x8, 4x4, 2x2 and 1x1.
pub const ENVIRONMENT_VALUES: usize = 6 * (64 + 16 + 4 + 1);

/// Little-endian reads with bounds checks.
struct Reader<'a> {
    bytes: &'a [u8],
}

impl<'a> Reader<'a> {
    fn slice(&self, at: usize, len: usize) -> Result<&'a [u8]> {
        self.bytes
            .get(at..at.checked_add(len).context("offset overflow")?)
            .with_context(|| format!("{len} bytes at {at} past the end ({})", self.bytes.len()))
    }
    fn u16(&self, at: usize) -> Result<u16> {
        Ok(u16::from_le_bytes(self.slice(at, 2)?.try_into().unwrap()))
    }
    fn u32(&self, at: usize) -> Result<u32> {
        Ok(u32::from_le_bytes(self.slice(at, 4)?.try_into().unwrap()))
    }
    fn i32(&self, at: usize) -> Result<i32> {
        Ok(self.u32(at)? as i32)
    }
    fn usize(&self, at: usize) -> Result<usize> {
        let v = self.i32(at)?;
        usize::try_from(v).with_context(|| format!("negative count {v} at {at}"))
    }
    fn f32(&self, at: usize) -> Result<f32> {
        Ok(f32::from_bits(self.u32(at)?))
    }
}

fn align(x: usize, a: usize) -> usize {
    x.div_ceil(a) * a
}

/// The blocks of a per-system file after its header: `(type, data)`.
fn file_blocks(bytes: &[u8], version: i32, header: usize) -> Result<Vec<(u16, &[u8])>> {
    let r = Reader { bytes };
    ensure!(r.u32(0)? == FILE_MAGIC, "not precomputed realtime GI data");
    let found = r.i32(4)?;
    ensure!(
        found == version,
        "precomputed realtime GI data version {found}, not {version}"
    );
    let mut at = 8 + header;
    let mut out = Vec::new();
    while at + 8 <= bytes.len() {
        let kind = r.u16(at)?;
        let len = r.usize(at + 4)?;
        out.push((kind, r.slice(at + 8, len)?));
        at += 8 + len;
    }
    Ok(out)
}

fn block<'a>(blocks: &[(u16, &'a [u8])], kind: u16) -> Result<&'a [u8]> {
    blocks
        .iter()
        .find(|(k, _)| *k == kind)
        .map(|(_, b)| *b)
        .with_context(|| format!("no block {kind}"))
}

/// A system's input samples: points (position and normal) grouped in clusters, and the cluster
/// tree whose parents follow the leaves in the input values.
#[derive(Debug, Clone)]
pub struct InputSamples {
    pub positions: Vec<[f32; 3]>,
    pub normals: Vec<[f32; 3]>,
    /// Per cluster, its first point; one more entry ends the last cluster.
    pub cluster_points: Vec<u32>,
    /// Per parent, its number of children; children are the next values in order.
    pub tree_children: Vec<u8>,
    /// Per child, its 8-bit weight.
    pub tree_weights: Vec<u8>,
}

impl InputSamples {
    pub fn clusters(&self) -> usize {
        self.cluster_points.len() - 1
    }
    /// Leaves, then parents, then one unused value.
    pub fn values(&self) -> usize {
        self.clusters() + self.tree_children.len() + 1
    }
}

pub fn read_input_samples(bytes: &[u8]) -> Result<InputSamples> {
    let blocks = file_blocks(bytes, INPUT_VERSION, 16)?;
    let main = block(&blocks, INPUT_MAIN_BLOCK)?;
    let r = Reader { bytes: main };
    ensure!(r.u32(0x20)? == INPUT_MAGIC, "malformed input samples");
    let clusters = r.usize(0x28)?;
    ensure!(clusters <= main.len(), "malformed input samples");
    let quads_at = r.usize(0x30)?;
    let boxes_at = r.usize(0x34)?;
    let tree_at = r.usize(0x40)?;
    ensure!(r.u32(tree_at)? == TREE_MAGIC, "malformed cluster tree");
    ensure!(
        r.usize(tree_at + 8)? == clusters,
        "cluster tree of another size"
    );
    let parents = r.usize(tree_at + 12)?;
    let quad_counts = r.slice(tree_at + 0x10, clusters)?;
    let mut positions = Vec::new();
    let mut normals = Vec::new();
    let mut cluster_points = Vec::with_capacity(clusters + 1);
    let mut quad = quads_at;
    for (c, &count) in quad_counts.iter().enumerate() {
        cluster_points.push(positions.len() as u32);
        let at = boxes_at + 32 * c;
        let lo = [r.f32(at)?, r.f32(at + 4)?, r.f32(at + 8)?];
        let hi = [r.f32(at + 16)?, r.f32(at + 20)?, r.f32(at + 24)?];
        for _ in 0..count {
            let q = r.slice(quad, 16)?;
            // Four points quantized to 8 bits in the cluster's box, one normal for the four.
            let normal = [q[3], q[7], q[11]].map(|b| b as f32 * (2.0 / 255.0) - 1.0);
            for p in 0..4 {
                let xyz = &q[4 * p..4 * p + 3];
                positions.push(std::array::from_fn(|k| {
                    lo[k] + xyz[k] as f32 * ((hi[k] - lo[k]) / 255.0)
                }));
                normals.push(normal);
            }
            quad += 16;
        }
    }
    cluster_points.push(positions.len() as u32);
    let counts_at = tree_at + 0x10 + clusters;
    let tree_children = r.slice(counts_at, parents)?.to_vec();
    // Children are the next values in order: each parent's must come before the parent.
    let mut children = 0usize;
    for (p, &count) in tree_children.iter().enumerate() {
        children += count as usize;
        ensure!(
            children <= clusters + p,
            "a cluster tree parent before its children"
        );
    }
    let tree_weights = r.slice(counts_at + parents, children)?.to_vec();
    Ok(InputSamples {
        positions,
        normals,
        cluster_points,
        tree_children,
        tree_weights,
    })
}

/// One output segment of the transport: its texels take a weighted sum of referenced input
/// values (per reference a 16-bit weight, per texel and reference an 8-bit weight).
#[derive(Debug, Clone)]
pub struct Segment {
    /// Per reference: the dependency (index into [`Transport::dependencies`]) and value index.
    pub references: Vec<(u16, u32)>,
    pub reference_weights: Vec<u16>,
    /// `texels * references` 8-bit weights, texel-major.
    pub weights: Vec<u8>,
    pub texels: Vec<(u16, u16)>,
    /// Texels copied from others after the segment: `(destination, source)`.
    pub copies: Vec<((u16, u16), (u16, u16))>,
}

/// A system's transport: the output texels' segments, the dependencies they reference (the
/// environment, this or other systems) and the bounce resampling.
#[derive(Debug, Clone)]
pub struct Transport {
    pub width: u32,
    pub height: u32,
    pub dependencies: Vec<[u8; 16]>,
    pub segments: Vec<Segment>,
    /// Per cluster, its first bounce sample; one more entry ends the last.
    pub bounce_samples: Vec<u32>,
    /// Bounce samples: output texel and weight.
    pub bounce: Vec<((u16, u16), f32)>,
}

pub fn read_transport(bytes: &[u8]) -> Result<Transport> {
    let blocks = file_blocks(bytes, TRANSPORT_VERSION, 48)?;
    let main = block(&blocks, TRANSPORT_BLOCK)?;
    let r = Reader { bytes: main };
    ensure!(r.u32(0)? == TRANSPORT_MAGIC, "malformed transport");
    let segments_count = r.usize(8)?;
    let dependency_count = r.usize(12)?;
    // Counts are checked against the data before anything is sized by them.
    ensure!(
        0x20 + 0x20 * segments_count + 16 * dependency_count <= main.len(),
        "malformed transport"
    );
    let width = r.u16(16)? as u32;
    let height = r.u16(18)? as u32;
    let mut segments = Vec::with_capacity(segments_count);
    for s in 0..segments_count {
        let h = 0x20 + 0x20 * s;
        let texels = r.u16(h)? as usize;
        let blocks4 = r.u16(h + 2)? as usize;
        let copies = r.u16(h + 4)? as usize;
        let refs = r.u16(h + 8)? as usize;
        let shift = r.u16(h + 10)?;
        let deps = r.u16(h + 12)? as usize;
        ensure!(
            shift == 0,
            "packed transport weights (shift {shift}) are not supported"
        );
        ensure!(
            deps == dependency_count,
            "segment with {deps} dependencies, not {dependency_count}"
        );
        let base = h + r.usize(h + 16)?;
        let reference_weights = (0..refs)
            .map(|k| r.u16(base + 2 * k))
            .collect::<Result<Vec<_>>>()?;
        let indices_at = align(base + 2 * refs, 4);
        let counts_at = indices_at + 4 * refs;
        let mut references = Vec::with_capacity(refs);
        let mut k = 0;
        for d in 0..deps {
            for _ in 0..r.u16(counts_at + 2 * d)? {
                ensure!(k < refs, "segment references past its count");
                references.push((d as u16, r.u32(indices_at + 4 * k)?));
                k += 1;
            }
        }
        ensure!(k == refs, "segment dependency counts do not add up");
        // A coarse weight block per four texels precedes the per-texel weights.
        let weights_at = align(counts_at + 4 * deps, 16) + blocks4 * refs;
        let weights = r.slice(weights_at, texels * refs)?.to_vec();
        let xs_at = align(weights_at + texels * refs, 4);
        let ys_at = xs_at + 2 * texels;
        let inside = |x: u16, y: u16| (x as u32) < width && (y as u32) < height;
        let texel_list = (0..texels)
            .map(|t| {
                let texel = (r.u16(xs_at + 2 * t)?, r.u16(ys_at + 2 * t)?);
                ensure!(
                    inside(texel.0, texel.1),
                    "a transport texel outside {width}x{height}"
                );
                Ok(texel)
            })
            .collect::<Result<Vec<_>>>()?;
        let copies_at = ys_at + 2 * texels;
        let copy_list = (0..copies)
            .map(|c| {
                let at = |row: usize| r.u16(copies_at + 2 * (row * copies + c));
                let copy = ((at(0)?, at(1)?), (at(2)?, at(3)?));
                ensure!(
                    inside(copy.0.0, copy.0.1) && inside(copy.1.0, copy.1.1),
                    "a transport copy outside {width}x{height}"
                );
                Ok(copy)
            })
            .collect::<Result<Vec<_>>>()?;
        segments.push(Segment {
            references,
            reference_weights,
            weights,
            texels: texel_list,
            copies: copy_list,
        });
    }
    let dependencies_at = 0x20 + 0x20 * segments_count;
    let dependencies = (0..dependency_count)
        .map(|d| Ok(r.slice(dependencies_at + 16 * d, 16)?.try_into().unwrap()))
        .collect::<Result<Vec<[u8; 16]>>>()?;

    let bounce = block(&blocks, BOUNCE_BLOCK)?;
    let b = Reader { bytes: bounce };
    ensure!(b.u32(0)? == BOUNCE_MAGIC, "malformed bounce resampling");
    let clusters = b.usize(8)?;
    let total = b.usize(16)?;
    ensure!(
        0x20 + 2 * clusters + 8 * total <= bounce.len(),
        "malformed bounce resampling"
    );
    let mut bounce_samples = Vec::with_capacity(clusters + 1);
    let mut sum = 0u32;
    for c in 0..clusters {
        bounce_samples.push(sum);
        sum += b.u16(0x20 + 2 * c)? as u32;
    }
    bounce_samples.push(sum);
    ensure!(sum as usize == total, "bounce sample counts do not add up");
    let at = align(0x20 + 2 * clusters, 4);
    let bounce = (0..total)
        .map(|k| {
            let e = at + 8 * k;
            let (x, y) = (b.u16(e)?, b.u16(e + 2)?);
            ensure!(
                (x as u32) < width && (y as u32) < height,
                "a bounce sample outside {width}x{height}"
            );
            Ok(((x, y), b.f32(e + 4)?))
        })
        .collect::<Result<Vec<_>>>()?;
    Ok(Transport {
        width,
        height,
        dependencies,
        segments,
        bounce_samples,
        bounce,
    })
}

/// Where each cluster takes its albedo and emission from: per cluster, groups of points of one
/// renderer (instance), each group sampling texels of the system's material textures (system
/// resolution, row 0 at v = 0) with integer weights that add up to its point count.
#[derive(Debug, Clone)]
pub struct ClusterMaterials {
    /// Per instance, the renderer it is within the system's renderers.
    pub instance_renderers: Vec<u16>,
    /// Per cluster, its first group; one more entry ends the last.
    pub cluster_groups: Vec<u32>,
    /// Groups: an instance and how many of the cluster's points it covers.
    pub groups: Vec<(u16, u16)>,
    /// Per group, its first sample; one more entry ends the last.
    pub group_samples: Vec<u32>,
    /// Samples: a texel and its weight.
    pub samples: Vec<((u16, u16), u16)>,
}

/// `width`, `height`: the system's resolution, which the samples' texels must be inside.
pub fn read_cluster_materials(bytes: &[u8], width: u32, height: u32) -> Result<ClusterMaterials> {
    let blocks = file_blocks(bytes, MATERIAL_VERSION, 16)?;
    let main = block(&blocks, MATERIAL_BLOCK)?;
    let r = Reader { bytes: main };
    let instances = r.usize(4)?;
    let clusters = r.usize(8)?;
    let sample_count = r.usize(12)?;
    let instances_at = r.usize(16)?;
    ensure!(
        8 * instances + 2 * clusters + 6 * sample_count <= main.len(),
        "malformed cluster materials"
    );
    let counts_at = r.usize(20)?;
    let groups_at = r.usize(24)?;
    let samples_at = r.usize(28)?;
    let weights_at = r.usize(32)?;
    let instance_renderers = (0..instances)
        .map(|i| Ok((r.u32(instances_at + 8 * i + 4)? >> 16) as u16))
        .collect::<Result<Vec<_>>>()?;
    let mut cluster_groups = Vec::with_capacity(clusters + 1);
    let mut sum = 0u32;
    for c in 0..clusters {
        cluster_groups.push(sum);
        sum += r.u16(counts_at + 2 * c)? as u32;
    }
    cluster_groups.push(sum);
    let groups = (0..sum as usize)
        .map(|g| {
            let points = r.u16(groups_at + 4 * g)?;
            let instance = r.u16(groups_at + 4 * g + 2)?;
            ensure!(
                (instance as usize) < instances,
                "group of an unknown instance"
            );
            Ok((instance, points))
        })
        .collect::<Result<Vec<_>>>()?;
    let samples = (0..sample_count)
        .map(|k| {
            let texel = (r.u16(samples_at + 4 * k)?, r.u16(samples_at + 4 * k + 2)?);
            ensure!(
                (texel.0 as u32) < width && (texel.1 as u32) < height,
                "a material sample outside {width}x{height}"
            );
            Ok((texel, r.u16(weights_at + 2 * k)?))
        })
        .collect::<Result<Vec<_>>>()?;
    // A group takes samples until their weights reach its point count.
    let mut group_samples = Vec::with_capacity(groups.len() + 1);
    let mut next = 0usize;
    for &(_, points) in &groups {
        group_samples.push(next as u32);
        let mut sum = 0u32;
        while sum < points as u32 {
            let (_, weight) = samples
                .get(next)
                .context("material samples end before their groups")?;
            sum += *weight as u32;
            next += 1;
        }
        ensure!(
            sum == points as u32,
            "material sample weights of {sum} for {points} points"
        );
    }
    group_samples.push(next as u32);
    ensure!(
        next == samples.len(),
        "{} material samples, {next} of them in groups",
        samples.len()
    );
    Ok(ClusterMaterials {
        instance_renderers,
        cluster_groups,
        groups,
        group_samples,
        samples,
    })
}

/// A system of the scene mapping and its data.
#[derive(Debug, Clone)]
pub struct System {
    pub hash: [u8; 16],
    /// Its renderers: a range of [`Precomputed::renderers`].
    pub first_renderer: usize,
    pub renderer_count: usize,
    pub atlas: usize,
    pub atlas_offset: (u32, u32),
    pub input: InputSamples,
    pub transport: Transport,
    pub materials: ClusterMaterials,
}

/// A scene's precomputed realtime GI.
#[derive(Debug, Clone)]
pub struct Precomputed {
    pub systems: Vec<System>,
    /// Realtime lightmap atlas sizes.
    pub atlas_sizes: Vec<u32>,
    /// The scene mapping's renderers (scene object id, prefab id) in mapping order.
    pub renderers: Vec<(i64, i64)>,
    /// Per renderer, its realtime lightmap scale and offset within its system's texture (where
    /// its meta pass draws).
    pub renderer_st: Vec<[f32; 4]>,
}

fn hash128(v: &Value) -> Result<[u8; 16]> {
    let mut out = [0u8; 16];
    for (k, o) in out.iter_mut().enumerate() {
        *o = v
            .get(&format!("bytes[{k}]"))
            .and_then(Value::as_str)
            .and_then(|s| s.parse().ok())
            .context("a malformed hash")?;
    }
    Ok(out)
}

fn int(v: &Value, key: &str) -> Result<i64> {
    v.get(key)
        .and_then(Value::as_str)
        .and_then(|s| s.parse().ok())
        .with_context(|| format!("no {key}"))
}

fn count(v: &Value, key: &str) -> Result<usize> {
    let n = int(v, key)?;
    usize::try_from(n).with_context(|| format!("negative {key} {n}"))
}

fn hex(bytes: &[u8]) -> String {
    bytes.iter().map(|b| format!("{b:02x}")).collect()
}

fn from_hex(text: &str) -> Result<Vec<u8>> {
    ensure!(text.len() % 2 == 0, "odd hex length");
    (0..text.len() / 2)
        .map(|k| u8::from_str_radix(&text[2 * k..2 * k + 2], 16).context("bad hex"))
        .collect()
}

/// The precomputed realtime GI of a LightingDataAsset object, or None without any.
pub fn read_precomputed(lighting_data: &Value) -> Result<Option<Precomputed>> {
    let payload = match lighting_data.get("m_EnlightenData").and_then(Value::as_str) {
        Some(text) if !text.is_empty() => from_hex(text)?,
        _ => return Ok(None),
    };
    let bundle = Bundle::parse(&payload).context("reading the precomputed realtime GI payload")?;
    let file = |hash: &[u8; 16], ext: &str| -> Result<&[u8]> {
        let name = hex(hash);
        let path = format!("{}/{name}.{ext}", &name[..2]);
        bundle
            .file(&path)
            .map(|f| f.data.as_slice())
            .with_context(|| format!("no {path} in the precomputed realtime GI payload"))
    };
    let mapping = lighting_data
        .get("m_EnlightenSceneMapping")
        .context("no scene mapping")?;
    let seq = |v: &Value, key: &str| {
        v.get(key)
            .and_then(Value::as_seq)
            .unwrap_or_default()
            .to_vec()
    };
    let renderers = seq(lighting_data, "m_EnlightenSceneMappingRendererIDs")
        .iter()
        .map(|r| Ok((int(r, "targetObject")?, int(r, "targetPrefab")?)))
        .collect::<Result<Vec<_>>>()?;
    let renderer_st = seq(mapping, "m_Renderers")
        .iter()
        .map(|r| {
            let st = r
                .get("dynamicLightmapSTInSystem")
                .context("no dynamicLightmapSTInSystem")?;
            ["x", "y", "z", "w"]
                .map(|k| {
                    st.get(k)
                        .and_then(Value::as_str)
                        .and_then(|s| s.parse::<f32>().ok())
                })
                .into_iter()
                .collect::<Option<Vec<f32>>>()
                .map(|v| [v[0], v[1], v[2], v[3]])
                .context("a malformed dynamicLightmapSTInSystem")
        })
        .collect::<Result<Vec<_>>>()?;
    ensure!(
        renderer_st.len() == renderers.len(),
        "{} mapped renderers for {} renderer ids",
        renderer_st.len(),
        renderers.len()
    );
    let atlas_sizes = seq(mapping, "m_SystemAtlases")
        .iter()
        .map(|a| Ok(count(a, "atlasSize")?.min(u32::MAX as usize) as u32))
        .collect::<Result<Vec<_>>>()?;
    let mut systems = Vec::new();
    for s in seq(mapping, "m_Systems") {
        let input_hash = hash128(s.get("inputSystemHash").context("no input hash")?)?;
        let hash = hash128(s.get("radiositySystemHash").context("no system hash")?)?;
        let first_renderer = count(&s, "rendererIndex")?;
        let renderer_count = count(&s, "rendererSize")?;
        let atlas = count(&s, "atlasIndex")?;
        let offset = (count(&s, "atlasOffsetX")?, count(&s, "atlasOffsetY")?);
        if first_renderer
            .checked_add(renderer_count)
            .is_none_or(|end| end > renderers.len())
            || atlas >= atlas_sizes.len()
        {
            bail!("a system outside the scene mapping");
        }
        let input = read_input_samples(file(&input_hash, "iws.ref")?)?;
        let transport = read_transport(file(&hash, "rsc.ref")?)?;
        let materials =
            read_cluster_materials(file(&input_hash, "caw")?, transport.width, transport.height)?;
        let size = atlas_sizes[atlas] as usize;
        ensure!(
            offset.0 + transport.width as usize <= size
                && offset.1 + transport.height as usize <= size,
            "a system outside its {size}x{size} atlas"
        );
        ensure!(
            transport.bounce_samples.len() == input.cluster_points.len()
                && materials.cluster_groups.len() == input.cluster_points.len(),
            "system files of different cluster counts"
        );
        systems.push(System {
            hash,
            first_renderer,
            renderer_count,
            atlas,
            atlas_offset: (offset.0 as u32, offset.1 as u32),
            input,
            transport,
            materials,
        });
    }
    Ok(Some(Precomputed {
        systems,
        atlas_sizes,
        renderers,
        renderer_st,
    }))
}
