//! FBX smoothing masks before a geometry reader reduces them to smooth/hard booleans.
use anyhow::{Result, bail, ensure};
use std::collections::HashMap;

pub struct Groups {
    pub mapping: String,
    pub reference: String,
    pub values: Vec<u32>,
}

struct Node<'a> {
    name: &'a [u8],
    props: &'a [u8],
    children: usize,
    end: usize,
}

fn u32_at(bytes: &[u8], at: usize) -> Result<u32> {
    Ok(u32::from_le_bytes(
        bytes
            .get(at..at + 4)
            .ok_or_else(|| anyhow::anyhow!("truncated FBX smoothing data"))?
            .try_into()?,
    ))
}

fn node(bytes: &[u8], at: usize, wide: bool) -> Result<Option<Node<'_>>> {
    let width = if wide { 8 } else { 4 };
    let integer = |at| -> Result<usize> {
        let value = if wide {
            u64::from_le_bytes(
                bytes
                    .get(at..at + 8)
                    .ok_or_else(|| anyhow::anyhow!("truncated FBX node"))?
                    .try_into()?,
            )
        } else {
            u32_at(bytes, at)? as u64
        };
        Ok(usize::try_from(value)?)
    };
    let end = integer(at)?;
    if end == 0 {
        return Ok(None);
    }
    let prop_bytes = integer(at + 2 * width)?;
    let name_start = at + 3 * width + 1;
    let name_len = *bytes
        .get(name_start - 1)
        .ok_or_else(|| anyhow::anyhow!("truncated FBX node name"))? as usize;
    let props_start = name_start
        .checked_add(name_len)
        .ok_or_else(|| anyhow::anyhow!("FBX node size overflow"))?;
    let children = props_start
        .checked_add(prop_bytes)
        .ok_or_else(|| anyhow::anyhow!("FBX property size overflow"))?;
    ensure!(
        children <= end && end <= bytes.len() && end > at,
        "invalid FBX node range"
    );
    Ok(Some(Node {
        name: &bytes[name_start..props_start],
        props: &bytes[props_start..children],
        children,
        end,
    }))
}

fn string(props: &[u8]) -> Result<String> {
    ensure!(props.first() == Some(&b'S'), "invalid FBX smoothing string");
    let len = u32_at(props, 1)? as usize;
    Ok(std::str::from_utf8(
        props
            .get(5..5 + len)
            .ok_or_else(|| anyhow::anyhow!("truncated FBX smoothing string"))?,
    )?
    .to_owned())
}

fn masks(props: &[u8], limit: usize) -> Result<Vec<u32>> {
    let width = match props.first() {
        Some(b'i') => 4,
        Some(b'l') => 8,
        Some(b'b' | b'c') => 1,
        _ => bail!("unsupported FBX smoothing array"),
    };
    let count = u32_at(props, 1)? as usize;
    ensure!(count <= limit, "FBX smoothing array exceeds geometry size");
    let encoding = u32_at(props, 5)?;
    let length = u32_at(props, 9)? as usize;
    let raw = props
        .get(13..13 + length)
        .ok_or_else(|| anyhow::anyhow!("truncated FBX smoothing array"))?;
    let decoded;
    let data = match encoding {
        0 => raw,
        1 => {
            decoded = miniz_oxide::inflate::decompress_to_vec_zlib_with_limit(raw, count * width)
                .map_err(|_| anyhow::anyhow!("invalid compressed FBX smoothing array"))?;
            &decoded
        }
        _ => bail!("unsupported FBX smoothing encoding"),
    };
    ensure!(
        data.len() == count * width,
        "invalid FBX smoothing array length"
    );
    data.chunks_exact(width)
        .map(|v| {
            Ok(match width {
                1 => v[0] as u32,
                4 => u32::from_le_bytes(v.try_into().unwrap()),
                _ => {
                    let value = i64::from_le_bytes(v.try_into().unwrap());
                    ensure!(
                        value >= i32::MIN as i64 && value <= u32::MAX as i64,
                        "FBX smoothing masks wider than 32 bits are not supported"
                    );
                    value as u32
                }
            })
        })
        .collect()
}

/// Binary FBX 7.x geometry IDs to the first smoothing layer. Other representations stay absent.
pub fn read(bytes: &[u8], limit: usize) -> Result<HashMap<i64, Groups>> {
    let mut out = HashMap::new();
    if !bytes.starts_with(b"Kaydara FBX Binary  ") {
        return Ok(out);
    }
    let wide = u32_at(bytes, 23)? >= 7500;
    ensure!(
        bytes.len() >= 27 + if wide { 25 } else { 13 },
        "truncated FBX root node"
    );
    fn walk(
        bytes: &[u8],
        mut at: usize,
        end: usize,
        id: Option<i64>,
        wide: bool,
        limit: usize,
        depth: usize,
        out: &mut HashMap<i64, Groups>,
    ) -> Result<()> {
        ensure!(depth < 64, "FBX smoothing nesting limit exceeded");
        while at < end {
            let Some(n) = node(bytes, at, wide)? else {
                break;
            };
            ensure!(n.end <= end, "invalid nested FBX smoothing node");
            if n.name == b"LayerElementSmoothing" {
                let mut child = n.children;
                let (mut mapping, mut reference, mut values) = (None, None, None);
                while child < n.end {
                    let Some(c) = node(bytes, child, wide)? else {
                        break;
                    };
                    ensure!(c.end <= n.end, "invalid FBX smoothing layer");
                    match c.name {
                        b"MappingInformationType" => mapping = Some(string(c.props)?),
                        b"ReferenceInformationType" => reference = Some(string(c.props)?),
                        b"Smoothing" => values = Some(masks(c.props, limit)?),
                        _ => {}
                    }
                    child = c.end;
                }
                if let (Some(id), Some(mapping), Some(values)) = (id, mapping, values) {
                    out.entry(id).or_insert(Groups {
                        mapping,
                        reference: reference.unwrap_or_default(),
                        values,
                    });
                }
            } else {
                let id = if n.name == b"Geometry" && n.props.first() == Some(&b'L') {
                    Some(i64::from_le_bytes(
                        n.props
                            .get(1..9)
                            .ok_or_else(|| anyhow::anyhow!("truncated FBX geometry ID"))?
                            .try_into()?,
                    ))
                } else {
                    id
                };
                if n.children < n.end {
                    walk(bytes, n.children, n.end, id, wide, limit, depth + 1, out)?;
                }
            }
            at = n.end;
        }
        Ok(())
    }
    walk(bytes, 27, bytes.len(), None, wide, limit, 0, &mut out)?;
    Ok(out)
}

/// The lowest group bit no mask in `used` has; `None` when all 32 are taken.
fn lowest_free(used: u32) -> Option<u32> {
    (!used != 0).then(|| 1 << (!used).trailing_zeros())
}

/// Smoothing group masks per face from edge smoothing (`smooth` per mesh edge), as the Unity
/// 6000.0.71f1 Editor converts them (tests/projects/lightmapuvs `Edges`, `Slit`, `Seam`):
/// - faces joined by smooth edges form regions; in face order, each region takes the lowest
///   bit no hard-adjacent region has taken yet;
/// - then, in face order, a hard edge whose faces still share a bit (a hard edge that does not
///   enclose its region, such as a Blender "mark sharp" edge) is resolved: the later face drops
///   the shared bits (or, left with none, takes the lowest bit none of its hard neighbours has),
///   and its smooth neighbours add its bits so they stay smooth with it.
/// A face that finds no free bit among the 32 keeps an empty mask (hard on every edge).
///
/// The measured fixtures have sharp edges that do not interact (chains apart from each other,
/// or beside enclosing rims). Where a later resolution gives bits back to faces an earlier hard
/// edge separated, that edge ends up smooth; the Editor is not measured there, so such edges are
/// counted for a warning.
fn edge_groups(mesh: &ufbx::Mesh, smooth: &[bool]) -> Result<EdgeGroups> {
    ensure!(
        smooth.len() == mesh.edges.len(),
        "unsupported FBX edge smoothing array size"
    );
    let mut topology = vec![ufbx::TopoEdge::default(); mesh.num_indices];
    ufbx::compute_topology(mesh, &mut topology);
    let faces = mesh.faces.len();
    // Each face's neighbours across its edges in corner order, and whether that edge is smooth.
    let mut neighbours: Vec<Vec<(usize, bool)>> = vec![Vec::new(); faces];
    for e in &topology {
        let Some(other) = topology.get(e.twin as usize) else {
            continue;
        };
        let smooth = smooth.get(e.edge as usize).copied().unwrap_or(true);
        neighbours[e.face as usize].push((other.face as usize, smooth));
    }
    // Regions: union-find with path halving; the smallest face is the root.
    let mut parent: Vec<usize> = (0..faces).collect();
    fn root(parent: &mut [usize], mut face: usize) -> usize {
        while parent[face] != face {
            parent[face] = parent[parent[face]];
            face = parent[face];
        }
        face
    }
    for face in 0..faces {
        for &(other, smooth) in &neighbours[face] {
            if smooth {
                let (a, b) = (root(&mut parent, face), root(&mut parent, other));
                parent[a.max(b)] = a.min(b);
            }
        }
    }
    let roots: Vec<usize> = (0..faces).map(|f| root(&mut parent, f)).collect();
    let mut hard_regions: Vec<Vec<usize>> = vec![Vec::new(); faces];
    for face in 0..faces {
        for &(other, smooth) in &neighbours[face] {
            if !smooth && roots[face] != roots[other] {
                hard_regions[roots[face]].push(roots[other]);
            }
        }
    }
    let mut full = 0;
    let mut colors = vec![0u32; faces];
    for region in 0..faces {
        if roots[region] == region {
            let used = hard_regions[region].iter().fold(0, |m, &r| m | colors[r]);
            colors[region] = lowest_free(used).unwrap_or_else(|| {
                full += 1;
                0
            });
        }
    }
    let mut groups: Vec<u32> = roots.iter().map(|&r| colors[r]).collect();
    for a in 0..faces {
        for k in 0..neighbours[a].len() {
            let (b, smooth) = neighbours[a][k];
            if smooth || b < a || groups[a] & groups[b] == 0 {
                continue;
            }
            groups[b] &= !groups[a];
            if groups[b] == 0 {
                let used = neighbours[b]
                    .iter()
                    .filter(|n| !n.1)
                    .fold(0, |m, n| m | groups[n.0]);
                groups[b] = lowest_free(used).unwrap_or_else(|| {
                    full += 1;
                    0
                });
            }
            let bits = groups[b];
            for &(other, smooth) in &neighbours[b] {
                if smooth {
                    groups[other] |= bits;
                }
            }
        }
    }
    let smoothed = (0..faces)
        .flat_map(|a| neighbours[a].iter().map(move |&(b, smooth)| (a, b, smooth)))
        .filter(|&(a, b, smooth)| !smooth && a < b && groups[a] & groups[b] != 0)
        .count();
    Ok(EdgeGroups {
        groups,
        full,
        smoothed,
    })
}

/// Polygon groups converted from edge smoothing ([`edge_groups`]).
struct EdgeGroups {
    groups: Vec<u32>,
    /// Faces without a free group bit.
    full: usize,
    /// Hard edges whose faces still share a group (unmeasured interacting sharp edges).
    smoothed: usize,
}

fn has_smoothing_layer(node: &ufbx::DomNode) -> bool {
    let mut pending = vec![node];
    while let Some(node) = pending.pop() {
        if node.name == "LayerElementSmoothing" {
            return true;
        }
        pending.extend(node.children.iter().map(|child| child.as_ref()));
    }
    false
}

pub fn per_mesh(
    bytes: &[u8],
    scene: &ufbx::Scene,
    import_normals: bool,
    warnings: &mut Vec<String>,
) -> Result<Vec<Option<Vec<u32>>>> {
    if scene
        .meshes
        .iter()
        .all(|m| m.num_triangles == 0 || (import_normals && m.vertex_normal.exists))
    {
        return Ok(vec![None; scene.meshes.len()]);
    }
    let limit = scene
        .meshes
        .iter()
        .map(|m| m.num_indices)
        .max()
        .unwrap_or(1)
        .max(1);
    let raw = read(bytes, limit)?;
    let mut from_edges = |mesh: &ufbx::Mesh, smooth: &[bool]| -> Result<Vec<u32>> {
        let converted = edge_groups(mesh, smooth)?;
        if converted.full > 0 {
            warnings.push(format!(
                "{} face(s) of mesh '{}' need more than 32 smoothing groups for their FBX \
                 edge smoothing; they are shaded flat (not supported)",
                converted.full, mesh.element.name
            ));
        }
        if converted.smoothed > 0 {
            warnings.push(format!(
                "{} sharp edge(s) of mesh '{}' are shaded smooth: they interact with other sharp \
                 edges in a way the Unity Editor's FBX edge smoothing was not measured for",
                converted.smoothed, mesh.element.name
            ));
        }
        Ok(converted.groups)
    };
    scene
        .meshes
        .iter()
        .map(|mesh| {
            if mesh.num_triangles == 0 || (import_normals && mesh.vertex_normal.exists) {
                return Ok(None);
            }
            let id = mesh
                .element
                .dom_node
                .as_ref()
                .and_then(|n| n.values.first())
                .map(|v| v.value_int);
            let Some(group) = id.and_then(|id| raw.get(&id)) else {
                // Edge smoothing needs no masks, so ufbx's flags serve where the raw layer is
                // not read (ASCII files).
                if mesh.face_smoothing.is_empty() && !mesh.edge_smoothing.is_empty() {
                    return from_edges(mesh, &mesh.edge_smoothing).map(Some);
                }
                ensure!(
                    mesh.face_smoothing.is_empty()
                        && mesh.edge_smoothing.is_empty()
                        && !mesh
                            .element
                            .dom_node
                            .as_ref()
                            .is_some_and(|node| has_smoothing_layer(node)),
                    "this FBX smoothing representation is not supported for calculated normals"
                );
                return Ok(None);
            };
            ensure!(
                group.reference == "Direct",
                "indexed FBX smoothing layers are not supported"
            );
            Ok(Some(match group.mapping.as_str() {
                "ByPolygon" => {
                    ensure!(
                        group.values.len() == mesh.faces.len(),
                        "unsupported FBX polygon smoothing array size"
                    );
                    group.values.clone()
                }
                "AllSame" if group.values.len() == 1 => vec![group.values[0]; mesh.faces.len()],
                "ByEdge" => {
                    let smooth: Vec<bool> = group.values.iter().map(|&v| v != 0).collect();
                    from_edges(mesh, &smooth)?
                }
                _ => bail!("unsupported FBX smoothing mapping '{}'", group.mapping),
            }))
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn bytes(name: &str) -> Vec<u8> {
        std::fs::read(
            std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join(format!(
                "../../tests/projects/lightmapuvs/Sources/Normals/{name}.fbx"
            )),
        )
        .unwrap()
    }

    fn load(name: &str) -> Vec<u32> {
        let bytes = bytes(name);
        let scene = ufbx::load_memory(
            &bytes,
            ufbx::LoadOpts {
                retain_dom: true,
                ..Default::default()
            },
        )
        .unwrap();
        per_mesh(&bytes, &scene, false, &mut Vec::new())
            .unwrap()
            .remove(0)
            .unwrap()
    }

    #[test]
    fn binary_masks_preserve_group_bits_and_edge_regions() {
        let authored = load("Groups");
        assert!(authored.contains(&1) && authored.contains(&2));
        // Hard rims that enclose the caps give the same groups as the authored masks.
        assert_eq!(authored, load("Edges"));
    }

    #[test]
    fn measured_sharp_edges_convert_without_warnings() {
        for name in ["Edges", "Slit", "Seam"] {
            let bytes = bytes(name);
            let opts = ufbx::LoadOpts {
                retain_dom: true,
                ..Default::default()
            };
            let scene = ufbx::load_memory(&bytes, opts).unwrap();
            let mut warnings = Vec::new();
            assert!(per_mesh(&bytes, &scene, false, &mut warnings).unwrap()[0].is_some());
            assert!(warnings.is_empty(), "{name}: {warnings:?}");
        }
    }

    #[test]
    fn interacting_sharp_edges_outside_the_measurements_are_counted() {
        // Slit's 6x6 grid (control point y * 7 + x) with two sharp edges a row apart:
        // (3,2)-(3,3) and (2,3)-(2,4). The second resolution gives bits back across the first.
        let bytes = bytes("Slit");
        let scene = ufbx::load_memory(&bytes, ufbx::LoadOpts::default()).unwrap();
        let mesh = &scene.meshes[0];
        let point = |c: u32| mesh.vertex_indices[c as usize];
        let sharp = [(17, 24), (23, 30)];
        let smooth: Vec<bool> = mesh
            .edges
            .iter()
            .map(|e| {
                let (a, b) = (point(e.a), point(e.b));
                !sharp.contains(&(a.min(b), a.max(b)))
            })
            .collect();
        assert_eq!(smooth.iter().filter(|s| !**s).count(), 2);
        let converted = edge_groups(mesh, &smooth).unwrap();
        assert_eq!((converted.full, converted.smoothed), (0, 1));
    }

    #[test]
    fn the_retained_dom_keeps_smoothing_only_as_flags() {
        // The loader reads smoothing arrays as booleans, so its DOM cannot give the masks the
        // normals need (Groups uses bit 2); the raw layer is read from the file instead.
        let bytes = bytes("Groups");
        let opts = ufbx::LoadOpts {
            retain_dom: true,
            ..Default::default()
        };
        let scene = ufbx::load_memory(&bytes, opts).unwrap();
        let mesh = &scene.meshes[0];
        let mut pending = vec![mesh.element.dom_node.as_ref().unwrap().as_ref()];
        let mut arrays = Vec::new();
        while let Some(node) = pending.pop() {
            if node.name == "Smoothing" {
                arrays.extend(node.values.first());
            }
            pending.extend(node.children.iter().map(|child| child.as_ref()));
        }
        let [array] = arrays[..] else {
            panic!("Groups has one smoothing layer, found {}", arrays.len())
        };
        assert_eq!(array.type_, ufbx::DomValueType::Blob);
        assert_eq!(array.value_blob.len(), mesh.faces.len());
        assert!(array.value_blob.iter().all(|&flag| flag <= 1));
        assert!(load("Groups").contains(&2));
    }

    #[test]
    fn lowest_free_bits() {
        assert_eq!(lowest_free(0), Some(1));
        assert_eq!(lowest_free(0b1011), Some(0b100));
        assert_eq!(lowest_free(u32::MAX), None);
    }

    #[test]
    fn truncated_binary_nodes_are_rejected() {
        let bytes = bytes("Groups");
        for length in [23, 27, 128, bytes.len() / 2] {
            assert!(read(&bytes[..length], 1000).is_err(), "truncation {length}");
        }
    }
    #[test]
    fn a_hard_edge_without_an_enclosed_region_stays_hard() {
        let bytes = bytes("Edges");
        let scene = ufbx::load_memory(&bytes, ufbx::LoadOpts::default()).unwrap();
        let mesh = &scene.meshes[0];
        // One hard edge with a smooth route around it: its faces get disjoint groups while
        // every smooth edge keeps a shared group.
        let mut smooth = vec![true; mesh.edges.len()];
        smooth[0] = false;
        let EdgeGroups {
            groups,
            full,
            smoothed,
        } = edge_groups(mesh, &smooth).unwrap();
        assert_eq!((full, smoothed), (0, 0));
        let mut topology = vec![ufbx::TopoEdge::default(); mesh.num_indices];
        ufbx::compute_topology(mesh, &mut topology);
        for e in topology.iter().filter(|e| e.twin != u32::MAX) {
            let (a, b) = (e.face as usize, topology[e.twin as usize].face as usize);
            assert_eq!(groups[a] & groups[b] != 0, e.edge != 0, "faces {a} and {b}");
        }
        assert!(edge_groups(mesh, &smooth[1..]).is_err());
    }
    #[test]
    fn ascii_edge_smoothing_uses_the_loaders_flags() {
        // Two triangles sharing the hard edge 1-2 (edge 1).
        let bytes = br#"; FBX 7.4.0 project file
FBXHeaderExtension: { FBXVersion: 7400 }
Objects: {
 Geometry: 1, "Geometry::Mesh", "Mesh" {
  Vertices: *12 { a: 0,0,0,1,0,0,0,1,0,1,1,1 }
  PolygonVertexIndex: *6 { a: 0,1,-3,2,1,-4 }
  Edges: *5 { a: 0,1,2,4,5 }
  LayerElementSmoothing: 0 {
   Version: 102
   MappingInformationType: "ByEdge"
   ReferenceInformationType: "Direct"
   Smoothing: *5 { a: 1,0,1,1,1 }
  }
 }
 Model: 2, "Model::Mesh", "Mesh" { }
}
Connections: { C: "OO",1,2 }
"#;
        let scene = ufbx::load_memory(
            bytes,
            ufbx::LoadOpts {
                retain_dom: true,
                ..Default::default()
            },
        )
        .unwrap();
        let groups = per_mesh(bytes, &scene, false, &mut Vec::new())
            .unwrap()
            .remove(0)
            .expect("ByEdge smoothing is read");
        // The shared edge is hard: the triangles share no group.
        assert_eq!(groups[0] & groups[1], 0, "{groups:?}");
    }
    #[test]
    fn unsupported_ascii_smoothing_cannot_disappear_from_the_normalized_arrays() {
        let bytes = br#"; FBX 7.4.0 project file
FBXHeaderExtension: { FBXVersion: 7400 }
Objects: {
 Geometry: 1, "Geometry::Mesh", "Mesh" {
  Vertices: *12 { a: 0,0,0,1,0,0,0,1,0,0,0,1 }
  PolygonVertexIndex: *6 { a: 0,1,-3,0,2,-4 }
  LayerElementSmoothing: 0 {
   Version: 102
   MappingInformationType: "AllSame"
   ReferenceInformationType: "Direct"
   Smoothing: *1 { a: 0 }
  }
 }
 Model: 2, "Model::Mesh", "Mesh" { }
}
Connections: { C: "OO",1,2 }
"#;
        let scene = ufbx::load_memory(
            bytes,
            ufbx::LoadOpts {
                retain_dom: true,
                ..Default::default()
            },
        )
        .unwrap();
        let error = per_mesh(bytes, &scene, false, &mut Vec::new()).unwrap_err();
        assert!(
            error
                .to_string()
                .contains("smoothing representation is not supported"),
            "{error}"
        );
    }
}
