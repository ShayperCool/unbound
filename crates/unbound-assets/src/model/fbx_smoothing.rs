//! FBX smoothing layers as the model importer reads them (matches the Unity 6000.0.71f1 Editor,
//! tests/projects/modelimport `smoothing.py` and tests/projects/lightmapuvs).
//!
//! The importer uses the smoothing element of layer 0 (`Layer: 0` names it by its typed index):
//! - layer `Version` 102 and later holds integer group masks, earlier versions hold flags (any
//!   non-zero value is 1); `ReferenceInformationType` and an index array are not read, values are
//!   always used directly;
//! - the mapping (`ByPolygon`, `ByPolygonVertex`, `ByVertice`, `AllSame`, `ByEdge`; any other
//!   token is "no mapping") spreads the values over the face corners: a corner without a value
//!   gets 0, "no mapping" gives every corner 0, and `ByVertice` needs one value per control point;
//! - edge smoothing is first converted to polygon groups ([`edge_groups`]); when that needs more
//!   than 32 groups every corner gets 0 (hard edges everywhere).
//!
//! The FBX loader keeps smoothing arrays only as smooth/hard flags, so the values are read from
//! the file again: one array per `LayerElementSmoothing` node in document order, paired with the
//! loader's retained DOM nodes in the same order.
use anyhow::{Result, anyhow, bail, ensure};
use std::collections::{HashMap, HashSet};

/// Nesting deeper than this is refused (FBX files nest a handful of levels).
const MAX_DEPTH: usize = 64;
/// The first layer version whose smoothing values are group masks rather than flags.
const MASK_VERSION: i64 = 102;
const LAYER_NODE: &str = "LayerElementSmoothing";

// ------------------------------------------------------------------------------- raw values

fn u32_at(bytes: &[u8], at: usize) -> Result<u32> {
    Ok(u32::from_le_bytes(
        bytes
            .get(at..at + 4)
            .ok_or_else(|| anyhow!("truncated FBX smoothing data"))?
            .try_into()?,
    ))
}

struct Node<'a> {
    name: &'a [u8],
    props: &'a [u8],
    children: usize,
    end: usize,
}

fn node(bytes: &[u8], at: usize, wide: bool) -> Result<Option<Node<'_>>> {
    let width = if wide { 8 } else { 4 };
    let integer = |at| -> Result<usize> {
        let value = if wide {
            u64::from_le_bytes(
                bytes
                    .get(at..at + 8)
                    .ok_or_else(|| anyhow!("truncated FBX node"))?
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
        .ok_or_else(|| anyhow!("truncated FBX node name"))? as usize;
    let props_start = name_start
        .checked_add(name_len)
        .ok_or_else(|| anyhow!("FBX node size overflow"))?;
    let children = props_start
        .checked_add(prop_bytes)
        .ok_or_else(|| anyhow!("FBX property size overflow"))?;
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

/// A binary array property as 32-bit integers (wider values keep their low bits, as an integer
/// array read does).
fn binary_values(props: &[u8], limit: usize) -> Result<Vec<u32>> {
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
        .ok_or_else(|| anyhow!("truncated FBX smoothing array"))?;
    let decoded;
    let data = match encoding {
        0 => raw,
        1 => {
            decoded = miniz_oxide::inflate::decompress_to_vec_zlib_with_limit(raw, count * width)
                .map_err(|_| anyhow!("invalid compressed FBX smoothing array"))?;
            &decoded
        }
        _ => bail!("unsupported FBX smoothing encoding"),
    };
    ensure!(
        data.len() == count * width,
        "invalid FBX smoothing array length"
    );
    Ok(data
        .chunks_exact(width)
        .map(|v| match width {
            1 => v[0] as u32,
            4 => u32::from_le_bytes(v.try_into().unwrap()),
            _ => i64::from_le_bytes(v.try_into().unwrap()) as u32,
        })
        .collect())
}

fn binary_arrays(bytes: &[u8], limit: usize) -> Result<Vec<Vec<u32>>> {
    let wide = u32_at(bytes, 23)? >= 7500;
    ensure!(
        bytes.len() >= 27 + if wide { 25 } else { 13 },
        "truncated FBX root node"
    );
    fn walk(
        bytes: &[u8],
        mut at: usize,
        end: usize,
        wide: bool,
        limit: usize,
        depth: usize,
        out: &mut Vec<Vec<u32>>,
    ) -> Result<()> {
        ensure!(depth < MAX_DEPTH, "FBX nesting limit exceeded");
        while at < end {
            let Some(n) = node(bytes, at, wide)? else {
                break;
            };
            ensure!(n.end <= end, "invalid nested FBX node");
            if n.name == LAYER_NODE.as_bytes() {
                let mut values = Vec::new();
                let mut child = n.children;
                while child < n.end {
                    let Some(c) = node(bytes, child, wide)? else {
                        break;
                    };
                    ensure!(c.end <= n.end, "invalid FBX smoothing layer");
                    if c.name == b"Smoothing" {
                        values = binary_values(c.props, limit)?;
                    }
                    child = c.end;
                }
                out.push(values);
            } else if n.children < n.end {
                walk(bytes, n.children, n.end, wide, limit, depth + 1, out)?;
            }
            at = n.end;
        }
        Ok(())
    }
    let mut out = Vec::new();
    walk(bytes, 27, bytes.len(), wide, limit, 0, &mut out)?;
    Ok(out)
}

/// Where a node of an ASCII file sits relative to a smoothing layer.
#[derive(Clone, Copy, PartialEq)]
enum Within {
    Elsewhere,
    Layer,
    /// The layer's `Smoothing` node: its values (FBX 6) or its `a` child's (FBX 7).
    Values,
}

struct Ascii<'a> {
    bytes: &'a [u8],
    at: usize,
}

impl Ascii<'_> {
    fn peek(&self) -> Option<u8> {
        self.bytes.get(self.at).copied()
    }

    /// Skips spaces and comments, and line ends too when `lines`.
    fn blank(&mut self, lines: bool) {
        while let Some(c) = self.peek() {
            match c {
                b' ' | b'\t' | b'\r' => self.at += 1,
                b'\n' if lines => self.at += 1,
                b';' => {
                    while self.peek().is_some_and(|c| c != b'\n') {
                        self.at += 1;
                    }
                }
                _ => break,
            }
        }
    }

    fn token(&mut self) -> &[u8] {
        let start = self.at;
        if self.peek() == Some(b'"') {
            self.at += 1;
            while self.peek().is_some_and(|c| c != b'"') {
                self.at += 1;
            }
            self.at = (self.at + 1).min(self.bytes.len());
        } else {
            while self.peek().is_some_and(|c| {
                !matches!(c, b',' | b'{' | b'}' | b'\n' | b' ' | b'\t' | b'\r' | b';')
            }) {
                self.at += 1;
            }
        }
        &self.bytes[start..self.at]
    }

    /// The nodes up to the closing brace of their parent (or the end of the file).
    fn nodes(&mut self, within: Within, depth: usize, out: &mut Vec<Vec<u32>>) -> Result<()> {
        ensure!(depth < MAX_DEPTH, "FBX nesting limit exceeded");
        loop {
            self.blank(true);
            match self.peek() {
                None => return Ok(()),
                Some(b'}') => {
                    self.at += 1;
                    return Ok(());
                }
                _ => {}
            }
            let start = self.at;
            while self.peek().is_some_and(|c| c != b':' && c != b'\n') {
                self.at += 1;
            }
            ensure!(self.peek() == Some(b':'), "invalid ASCII FBX node");
            let name = self.bytes[start..self.at].trim_ascii();
            self.at += 1;
            let child = match (within, name) {
                (_, n) if n == LAYER_NODE.as_bytes() => {
                    out.push(Vec::new());
                    Within::Layer
                }
                (Within::Layer, b"Smoothing") => Within::Values,
                (Within::Values, b"a") => Within::Values,
                _ => Within::Elsewhere,
            };
            let capture = child == Within::Values;
            loop {
                self.blank(false);
                match self.peek() {
                    None | Some(b'\n' | b'{' | b'}') => break,
                    Some(b',') => {
                        self.at += 1;
                        self.blank(true);
                    }
                    Some(_) => {
                        let token = self.token();
                        if capture && !token.starts_with(b"*") {
                            let text = std::str::from_utf8(token)?;
                            let value = text
                                .parse::<i64>()
                                .or_else(|_| text.parse::<f64>().map(|v| v as i64))
                                .map_err(|_| anyhow!("invalid FBX smoothing value '{text}'"))?;
                            out.last_mut().unwrap().push(value as u32);
                        }
                    }
                }
            }
            if self.peek() == Some(b'{') {
                self.at += 1;
                self.nodes(child, depth + 1, out)?;
            }
        }
    }
}

/// The values of every smoothing layer of the file, in document order.
fn raw_arrays(bytes: &[u8], limit: usize) -> Result<Vec<Vec<u32>>> {
    if bytes.starts_with(b"Kaydara FBX Binary  ") {
        return binary_arrays(bytes, limit);
    }
    let mut out = Vec::new();
    Ascii { bytes, at: 0 }.nodes(Within::Elsewhere, 0, &mut out)?;
    Ok(out)
}

// ------------------------------------------------------------------------------ the layers

/// A layer's mapping, from its `MappingInformationType` token.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Mapping {
    None,
    ByControlPoint,
    ByPolygonVertex,
    ByPolygon,
    ByEdge,
    AllSame,
}

impl Mapping {
    fn parse(token: &str) -> Self {
        match token {
            "ByVertice" => Self::ByControlPoint,
            "ByPolygonVertex" => Self::ByPolygonVertex,
            "ByPolygon" => Self::ByPolygon,
            "AllSame" => Self::AllSame,
            "ByEdge" => Self::ByEdge,
            _ => Self::None,
        }
    }
}

fn child<'a>(node: &'a ufbx::DomNode, name: &str) -> Option<&'a ufbx::DomNode> {
    node.children
        .iter()
        .map(|c| c.as_ref())
        .find(|c| c.name == name)
}

fn first_int(node: &ufbx::DomNode) -> Option<i64> {
    node.values.first().map(|v| v.value_int)
}

/// The smoothing element layer 0 names, among a geometry's DOM children.
fn layer_zero(geometry: &ufbx::DomNode) -> Option<&ufbx::DomNode> {
    let layer = geometry
        .children
        .iter()
        .find(|c| c.name == "Layer" && first_int(c) == Some(0))?;
    let typed = layer
        .children
        .iter()
        .filter(|c| c.name == "LayerElement")
        .find(|c| {
            child(c, "Type")
                .and_then(|t| t.values.first())
                .is_some_and(|v| v.value_str == LAYER_NODE)
        })
        .and_then(|c| child(c, "TypedIndex").and_then(first_int))?;
    geometry
        .children
        .iter()
        .map(|c| c.as_ref())
        .find(|c| c.name == LAYER_NODE && first_int(c) == Some(typed))
}

/// Every smoothing layer node of the DOM in document order.
fn dom_layers(root: &ufbx::DomNode) -> Vec<*const ufbx::DomNode> {
    let mut out = Vec::new();
    let mut pending = vec![root];
    while let Some(node) = pending.pop() {
        if node.name == LAYER_NODE {
            out.push(node as *const _);
            continue;
        }
        pending.extend(node.children.iter().rev().map(|c| c.as_ref()));
    }
    out
}

/// Each mesh's smoothing group mask per face corner (`mesh.vertex_indices` order), or `None`
/// without smoothing; meshes whose normals are imported (or that have no triangles) are skipped.
pub fn per_mesh(
    bytes: &[u8],
    scene: &ufbx::Scene,
    import_normals: bool,
    warnings: &mut Vec<String>,
) -> Result<Vec<Option<Vec<u32>>>> {
    let wanted =
        |m: &ufbx::Mesh| m.num_triangles > 0 && !(import_normals && m.vertex_normal.exists);
    let layers: Vec<Option<&ufbx::DomNode>> = scene
        .meshes
        .iter()
        .map(|m| {
            wanted(m)
                .then(|| m.element.dom_node.as_ref().and_then(|n| layer_zero(n)))
                .flatten()
        })
        .collect();
    if layers.iter().all(Option::is_none) {
        return Ok(vec![None; scene.meshes.len()]);
    }
    let root = scene
        .dom_root
        .as_ref()
        .ok_or_else(|| anyhow!("the FBX smoothing layers need the loader's DOM"))?;
    let order: HashMap<*const ufbx::DomNode, usize> = dom_layers(root)
        .into_iter()
        .enumerate()
        .map(|(i, n)| (n, i))
        .collect();
    let limit = scene
        .meshes
        .iter()
        .map(|m| m.num_indices.max(m.num_vertices).max(m.edges.len()))
        .max()
        .unwrap_or(1)
        .max(1);
    let arrays = raw_arrays(bytes, limit)?;
    ensure!(
        arrays.len() == order.len(),
        "the FBX smoothing layers of the file ({}) do not match the loader's ({})",
        arrays.len(),
        order.len()
    );
    scene
        .meshes
        .iter()
        .zip(layers)
        .map(|(mesh, layer)| {
            let Some(layer) = layer else {
                return Ok(None);
            };
            let mut values = arrays[order[&(layer as *const _)]].clone();
            if child(layer, "Version").and_then(first_int).unwrap_or(0) < MASK_VERSION {
                values.iter_mut().for_each(|v| *v = (*v != 0) as u32);
            }
            let mapping = child(layer, "MappingInformationType")
                .and_then(|m| m.values.first())
                .map_or(Mapping::None, |v| Mapping::parse(&v.value_str));
            corners(mesh, mapping, &values, warnings)
        })
        .collect()
}

/// The layer's values spread over the mesh's face corners.
fn corners(
    mesh: &ufbx::Mesh,
    mapping: Mapping,
    values: &[u32],
    warnings: &mut Vec<String>,
) -> Result<Option<Vec<u32>>> {
    if values.is_empty() {
        return Ok(None);
    }
    let value = |i: usize| values.get(i).copied().unwrap_or(0);
    let mut out = vec![0u32; mesh.num_indices];
    match mapping {
        Mapping::None => {}
        Mapping::AllSame => out.fill(values[0]),
        Mapping::ByPolygonVertex => out.iter_mut().enumerate().for_each(|(i, v)| *v = value(i)),
        Mapping::ByControlPoint => {
            if values.len() != mesh.num_vertices {
                warnings.push(format!(
                    "the smoothing layer of mesh '{}' has {} values for {} control points; it is \
                     ignored, as the Unity importer does",
                    mesh.element.name,
                    values.len(),
                    mesh.num_vertices
                ));
                return Ok(None);
            }
            for (v, &point) in out.iter_mut().zip(mesh.vertex_indices.iter()) {
                *v = value(point as usize);
            }
        }
        Mapping::ByPolygon => spread(mesh, &mut out, |f| value(f)),
        Mapping::ByEdge => {
            ensure!(
                mesh.edges.len() == values.len(),
                "the edge smoothing of mesh '{}' has {} values for {} edges (not supported)",
                mesh.element.name,
                values.len(),
                mesh.edges.len()
            );
            let smooth: Vec<bool> = values.iter().map(|&v| v != 0).collect();
            match edge_groups(mesh, &smooth) {
                Some(groups) => spread(mesh, &mut out, |f| groups[f]),
                None => warnings.push(format!(
                    "the edge smoothing of mesh '{}' needs more than 32 smoothing groups; every \
                     edge is hard, as the Unity importer does",
                    mesh.element.name
                )),
            }
        }
    }
    Ok(Some(out))
}

fn spread(mesh: &ufbx::Mesh, out: &mut [u32], group: impl Fn(usize) -> u32) {
    for (f, face) in mesh.faces.iter().enumerate() {
        let begin = face.index_begin as usize;
        out[begin..begin + face.num_indices as usize].fill(group(f));
    }
}

// ------------------------------------------------------------------- edges to polygon groups

/// The lowest group bit no mask in `used` has; `None` when all 32 are taken.
fn lowest_free(used: u32) -> Option<u32> {
    (!used != 0).then(|| 1 << (!used).trailing_zeros())
}

/// Compressed rows: the items of row `i` are `items[start[i]..start[i + 1]]`.
struct Rows {
    start: Vec<u32>,
    items: Vec<u32>,
}

impl Rows {
    fn row(&self, i: usize) -> &[u32] {
        &self.items[self.start[i] as usize..self.start[i + 1] as usize]
    }
}

/// The faces on each edge and the edges of each face (in edge order). An edge belongs to every
/// face with a corner at its first point whose next or previous corner is its second point; an
/// edge repeating an earlier edge's points in the same direction belongs to no face.
fn edge_maps(mesh: &ufbx::Mesh) -> (Rows, Rows) {
    let indices = &mesh.vertex_indices;
    // The faces' corners at each control point, in face and corner order.
    let mut start = vec![0u32; mesh.num_vertices + 1];
    for &p in indices.iter() {
        start[p as usize + 1] += 1;
    }
    for i in 0..mesh.num_vertices {
        start[i + 1] += start[i];
    }
    let mut fill = start.clone();
    let mut corners = vec![0u32; indices.len()];
    for face in mesh.faces.iter() {
        for c in face.index_begin..face.index_begin + face.num_indices {
            let p = indices[c as usize] as usize;
            corners[fill[p] as usize] = c;
            fill[p] += 1;
        }
    }
    let mut face_of = vec![0u32; indices.len()];
    for (f, face) in mesh.faces.iter().enumerate() {
        let begin = face.index_begin as usize;
        face_of[begin..begin + face.num_indices as usize].fill(f as u32);
    }
    let mut seen = HashSet::with_capacity(mesh.edges.len());
    let mut edge_faces = Rows {
        start: vec![0],
        items: Vec::with_capacity(mesh.edges.len() * 2),
    };
    let mut pairs: Vec<(u32, u32)> = Vec::with_capacity(mesh.edges.len() * 2);
    for (e, edge) in mesh.edges.iter().enumerate() {
        let (p, q) = (indices[edge.a as usize], indices[edge.b as usize]);
        if seen.insert((p, q)) {
            for &c in &corners[start[p as usize] as usize..start[p as usize + 1] as usize] {
                let face = &mesh.faces[face_of[c as usize] as usize];
                let (begin, n) = (face.index_begin, face.num_indices);
                let k = c - begin;
                let next = indices[(begin + (k + 1) % n) as usize];
                let prev = indices[(begin + (k + n - 1) % n) as usize];
                if next == q || prev == q {
                    edge_faces.items.push(face_of[c as usize]);
                    pairs.push((face_of[c as usize], e as u32));
                }
            }
        }
        edge_faces.start.push(edge_faces.items.len() as u32);
    }
    let mut face_edges = Rows {
        start: vec![0u32; mesh.faces.len() + 1],
        items: vec![0; pairs.len()],
    };
    for &(f, _) in &pairs {
        face_edges.start[f as usize + 1] += 1;
    }
    for i in 0..mesh.faces.len() {
        face_edges.start[i + 1] += face_edges.start[i];
    }
    let mut fill = face_edges.start.clone();
    for &(f, e) in &pairs {
        face_edges.items[fill[f as usize] as usize] = e;
        fill[f as usize] += 1;
    }
    (edge_faces, face_edges)
}

/// Polygon group masks from edge smoothing (`smooth` per mesh edge), as the FBX importer converts
/// them (matches the Unity 6000.0.71f1 Editor, tests/projects/modelimport `smoothing.py` Edges*
/// and tests/projects/lightmapuvs Edges, Slit, Seam). Faces are gathered into regions depth first
/// from the lowest unvisited face across smooth edges (the edges of a face in edge order, the last
/// face found first). A face with a hard edge to a face already in its region leaves it again
/// (it starts or joins a later region). A region with a smooth edge takes the lowest bit that
/// none of its hard neighbours has; a smooth edge to a face of an earlier region gives the
/// region's face a further bit that the earlier face can take (none of its hard neighbours has
/// it), and the earlier face takes it too. `None` when a region finds no free bit.
fn edge_groups(mesh: &ufbx::Mesh, smooth: &[bool]) -> Option<Vec<u32>> {
    let faces = mesh.faces.len();
    let (edge_faces, face_edges) = edge_maps(mesh);
    let mut groups = vec![0u32; faces];
    let mut visited = vec![false; faces];
    let mut queued = vec![false; faces];
    // The region (seed + 1) a face currently belongs to.
    let mut region = vec![0u32; faces];
    let (mut members, mut pairs, mut stack, mut smooth_edges) =
        (Vec::new(), Vec::new(), Vec::new(), Vec::new());
    // Whether `face` can take `bit` (none of its hard neighbours has it); it does if so.
    let take = |groups: &mut [u32], face: usize, bit: u32| -> bool {
        if groups[face] & bit != 0 {
            return true;
        }
        for &e in face_edges.row(face) {
            if !smooth[e as usize]
                && edge_faces
                    .row(e as usize)
                    .iter()
                    .any(|&g| g as usize != face && groups[g as usize] & bit != 0)
            {
                return false;
            }
        }
        groups[face] |= bit;
        true
    };
    for seed in 0..faces {
        if visited[seed] {
            continue;
        }
        let id = seed as u32 + 1;
        members.clear();
        pairs.clear();
        stack.clear();
        stack.push(seed);
        let (mut neighbours, mut any_smooth) = (0u32, false);
        let (mut low, mut high) = (usize::MAX, None);
        while let Some(face) = stack.pop() {
            members.push(face);
            visited[face] = true;
            region[face] = id;
            smooth_edges.clear();
            let (mut conflict, mut bits) = (false, 0u32);
            for &e in face_edges.row(face) {
                if smooth[e as usize] {
                    smooth_edges.push(e);
                    any_smooth = true;
                    continue;
                }
                for &g in edge_faces.row(e as usize) {
                    let g = g as usize;
                    if g != face && visited[g] {
                        conflict |= region[g] == id;
                        bits |= groups[g];
                    }
                }
            }
            if conflict {
                members.pop();
                visited[face] = false;
                region[face] = 0;
                continue;
            }
            neighbours |= bits;
            for &e in &smooth_edges {
                for &g in edge_faces.row(e as usize) {
                    let g = g as usize;
                    if queued[g] {
                        continue;
                    }
                    if visited[g] {
                        pairs.push((g, face));
                    } else {
                        stack.push(g);
                        queued[g] = true;
                        low = low.min(g);
                        high = Some(high.map_or(g, |h: usize| h.max(g)));
                    }
                }
            }
        }
        // The highest face queued in a region stays marked as queued for later regions (a
        // smooth edge from a later region to it adds no shared bit; matches the Editor).
        if let Some(high) = high {
            queued[low..high].fill(false);
        }
        if any_smooth {
            let bit = lowest_free(neighbours)?;
            for &m in &members {
                groups[m] = bit;
            }
            for &(earlier, face) in &pairs {
                let bit = (0..32)
                    .map(|i| 1u32 << i)
                    .find(|&b| neighbours & b == 0 && take(&mut groups, earlier, b))?;
                groups[face] |= bit;
            }
        }
    }
    Some(groups)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn source(name: &str) -> Vec<u8> {
        std::fs::read(
            std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join(format!(
                "../../tests/projects/lightmapuvs/Sources/Normals/{name}.fbx"
            )),
        )
        .unwrap()
    }

    fn fixture(name: &str) -> Vec<u8> {
        std::fs::read(
            std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join(format!(
                "../../tests/projects/modelimport/Assets/Resources/Smoothing/{name}.fbx"
            )),
        )
        .unwrap()
    }

    fn scene(bytes: &[u8]) -> ufbx::SceneRoot {
        ufbx::load_memory(
            bytes,
            ufbx::LoadOpts {
                retain_dom: true,
                ..Default::default()
            },
        )
        .unwrap()
    }

    fn load(bytes: &[u8]) -> (Vec<u32>, Vec<String>) {
        let scene = scene(bytes);
        let mut warnings = Vec::new();
        let corners = per_mesh(bytes, &scene, false, &mut warnings)
            .unwrap()
            .remove(0)
            .unwrap();
        // One group per face (the fixtures here are not per corner).
        let faces = scene.meshes[0]
            .faces
            .iter()
            .map(|f| corners[f.index_begin as usize])
            .collect();
        (faces, warnings)
    }

    #[test]
    fn binary_masks_preserve_group_bits_and_edge_regions() {
        let (authored, _) = load(&source("Groups"));
        assert!(authored.contains(&1) && authored.contains(&2));
        // Hard rims that enclose the caps join and separate the same faces as the authored masks
        // (a cap whose every edge is hard gets no group at all).
        let bytes = source("Edges");
        let (edges, _) = load(&bytes);
        let scene = scene(&bytes);
        let (edge_faces, _) = edge_maps(&scene.meshes[0]);
        for e in 0..scene.meshes[0].edges.len() {
            if let &[a, b] = edge_faces.row(e) {
                let (a, b) = (a as usize, b as usize);
                assert_eq!(
                    authored[a] & authored[b] != 0,
                    edges[a] & edges[b] != 0,
                    "edge {e}"
                );
            }
        }
    }

    #[test]
    fn measured_sharp_edges_convert_without_warnings() {
        for name in ["Edges", "Slit", "Seam"] {
            let (_, warnings) = load(&source(name));
            assert!(warnings.is_empty(), "{name}: {warnings:?}");
        }
    }

    #[test]
    fn ascii_masks_keep_their_bits() {
        let (groups, _) = load(&fixture("GroupsAscii"));
        assert_eq!(groups[12], 0x8000_0000);
        assert_eq!(groups[13], 0x8000_0001);
        assert_eq!(groups[5], 3);
        // The reference type is not read: an index array changes nothing.
        assert_eq!(load(&fixture("GroupsIndexed")).0, groups);
        // Layer version 101 holds flags.
        assert!(load(&fixture("GroupsFlags")).0.iter().all(|&g| g <= 1));
    }

    #[test]
    fn legacy_and_per_corner_layers() {
        assert_eq!(
            load(&fixture("GroupsLegacy")).0,
            load(&fixture("GroupsAscii")).0
        );
        assert!(load(&fixture("GroupsAllSame")).0.iter().all(|&g| g == 6));
        assert!(load(&fixture("GroupsAllZero")).0.iter().all(|&g| g == 0));
        let bytes = fixture("GroupsPoints");
        let scene = scene(&bytes);
        let corners = per_mesh(&bytes, &scene, false, &mut Vec::new())
            .unwrap()
            .remove(0)
            .unwrap();
        let mesh = &scene.meshes[0];
        for (c, &p) in mesh.vertex_indices.iter().enumerate() {
            assert_eq!(corners[c], 1 << (p % 3));
        }
    }

    #[test]
    fn edge_groups_keep_every_sharp_edge_of_simple_patterns_hard() {
        // Without junctions or loops, every hard edge separates its faces and every smooth edge
        // joins them.
        for name in ["EdgesL", "EdgesT", "EdgesCross"] {
            let bytes = fixture(name);
            let scene = scene(&bytes);
            let mesh = &scene.meshes[0];
            let (groups, warnings) = load(&bytes);
            assert!(warnings.is_empty());
            let (edge_faces, _) = edge_maps(mesh);
            let smooth = raw_arrays(&bytes, 1 << 20).unwrap().remove(0);
            for e in 0..mesh.edges.len() {
                if let [a, b] = edge_faces.row(e) {
                    let shared = groups[*a as usize] & groups[*b as usize] != 0;
                    assert_eq!(shared, smooth[e] != 0, "{name}: edge {e}");
                }
            }
        }
    }

    #[test]
    fn edge_order_matters_but_converts() {
        let (dense, _) = load(&fixture("EdgesDense"));
        let (reordered, _) = load(&fixture("EdgesReordered"));
        assert_eq!(dense.len(), reordered.len());
    }

    #[test]
    fn lowest_free_bits() {
        assert_eq!(lowest_free(0), Some(1));
        assert_eq!(lowest_free(0b1011), Some(0b100));
        assert_eq!(lowest_free(u32::MAX), None);
    }

    #[test]
    fn truncated_binary_nodes_are_rejected() {
        let bytes = source("Groups");
        for length in [27, 128, bytes.len() / 2] {
            assert!(
                raw_arrays(&bytes[..length], 1000).is_err(),
                "truncation {length}"
            );
        }
    }

    #[test]
    fn ascii_comments_strings_and_braces_do_not_confuse_the_reader() {
        let bytes = br#"; a comment with { and LayerElementSmoothing:
A: "text, with } and ;", 1 {
 LayerElementSmoothing: 0 {
  Smoothing: *3 {
   a: 1,
    2,3
  }
 }
 LayerElementSmoothing: 1 {
  Smoothing: 4,5
 }
}
"#;
        assert_eq!(
            raw_arrays(bytes, 10).unwrap(),
            vec![vec![1, 2, 3], vec![4, 5]]
        );
    }

    #[test]
    fn a_layer_outside_layer_zero_is_not_used() {
        let bytes = br#"; FBX 7.4.0 project file
FBXHeaderExtension: { FBXVersion: 7400 }
Objects: {
 Geometry: 1, "Geometry::Mesh", "Mesh" {
  Vertices: *12 { a: 0,0,0,1,0,0,0,1,0,0,0,1 }
  PolygonVertexIndex: *6 { a: 0,1,-3,0,2,-4 }
  LayerElementSmoothing: 0 {
   Version: 102
   MappingInformationType: "ByPolygon"
   ReferenceInformationType: "Direct"
   Smoothing: *2 { a: 1,2 }
  }
 }
 Model: 2, "Model::Mesh", "Mesh" { }
}
Connections: { C: "OO",1,2 }
"#;
        let scene = scene(bytes);
        assert!(per_mesh(bytes, &scene, false, &mut Vec::new()).unwrap()[0].is_none());
    }
}
