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

fn edge_groups(mesh: &ufbx::Mesh, values: &[u32]) -> Result<Vec<u32>> {
    ensure!(
        values.len() == mesh.edges.len(),
        "unsupported FBX edge smoothing array size"
    );
    let mut topology = vec![ufbx::TopoEdge::default(); mesh.num_indices];
    ufbx::compute_topology(mesh, &mut topology);
    let mut parents: Vec<usize> = (0..mesh.faces.len()).collect();
    fn root(parents: &[usize], mut face: usize) -> usize {
        while parents[face] != face {
            face = parents[face];
        }
        face
    }
    let pairs: Vec<_> = topology
        .iter()
        .filter(|e| e.twin != u32::MAX)
        .filter_map(|e| {
            let other = topology.get(e.twin as usize)?;
            (e.face < other.face).then_some((
                e.face as usize,
                other.face as usize,
                values.get(e.edge as usize).is_some_and(|&v| v != 0),
            ))
        })
        .collect();
    for &(a, b, smooth) in &pairs {
        if smooth {
            let (a, b) = (root(&parents, a), root(&parents, b));
            parents[a.max(b)] = a.min(b);
        }
    }
    let mut hard = vec![std::collections::HashSet::new(); mesh.faces.len()];
    for &(a, b, smooth) in &pairs {
        if !smooth {
            let (a, b) = (root(&parents, a), root(&parents, b));
            ensure!(
                a != b,
                "overlapping FBX edge smoothing regions are not supported"
            );
            hard[a].insert(b);
            hard[b].insert(a);
        }
    }
    let mut colors = vec![0u32; mesh.faces.len()];
    for face in 0..mesh.faces.len() {
        if root(&parents, face) != face {
            continue;
        }
        let used = hard[face]
            .iter()
            .fold(0, |mask, &other| mask | colors[other]);
        colors[face] = (0..32)
            .map(|bit| 1u32 << bit)
            .find(|&mask| mask & used == 0)
            .ok_or_else(|| anyhow::anyhow!("FBX edge smoothing needs more than 32 groups"))?;
    }
    Ok((0..mesh.faces.len())
        .map(|face| colors[root(&parents, face)])
        .collect())
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
            let values = match group.mapping.as_str() {
                "ByPolygon" => {
                    ensure!(
                        group.values.len() == mesh.faces.len(),
                        "unsupported FBX polygon smoothing array size"
                    );
                    group.values.clone()
                }
                "AllSame" if group.values.len() == 1 => vec![group.values[0]; mesh.faces.len()],
                "ByEdge" => edge_groups(mesh, &group.values)?,
                _ => bail!("unsupported FBX smoothing mapping '{}'", group.mapping),
            };
            Ok(Some(values))
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

    #[test]
    fn binary_masks_preserve_group_bits_and_edge_regions() {
        let load = |name: &str| {
            let bytes = bytes(name);
            let scene = ufbx::load_memory(
                &bytes,
                ufbx::LoadOpts {
                    retain_dom: true,
                    ..Default::default()
                },
            )
            .unwrap();
            per_mesh(&bytes, &scene, false).unwrap().remove(0).unwrap()
        };
        let authored = load("Groups");
        assert!(authored.contains(&1) && authored.contains(&2));
        assert_eq!(authored, load("Edges"));
    }

    #[test]
    fn truncated_binary_nodes_are_rejected() {
        let bytes = bytes("Groups");
        for length in [23, 27, 128, bytes.len() / 2] {
            assert!(read(&bytes[..length], 1000).is_err(), "truncation {length}");
        }
    }
    #[test]
    fn overlapping_edge_regions_are_refused_explicitly() {
        let bytes = bytes("Edges");
        let scene = ufbx::load_memory(&bytes, ufbx::LoadOpts::default()).unwrap();
        let mesh = &scene.meshes[0];
        // One hard edge with a smooth route around it needs overlapping group masks.
        let mut values = vec![1; mesh.edges.len()];
        values[0] = 0;
        let error = edge_groups(mesh, &values).unwrap_err();
        assert!(
            error
                .to_string()
                .contains("overlapping FBX edge smoothing regions are not supported"),
            "{error}"
        );
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
        let error = per_mesh(bytes, &scene, false).unwrap_err();
        assert!(
            error
                .to_string()
                .contains("smoothing representation is not supported"),
            "{error}"
        );
    }
}
