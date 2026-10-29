//! Meshes stored in Unity's serialized files (the install's built-in meshes: the primitives,
//! the sky's icosphere), read by their type trees and decoded from Unity's vertex layout.
use crate::{Importer, serialized::SerializedFile};
use anyhow::{Context, Result, bail};
use serde::{Deserialize, Serialize};
use std::path::Path;
use unbound_yaml::Value;

#[derive(Debug, Clone, Default, Serialize, Deserialize, PartialEq)]
pub struct SerializedMesh {
    pub name: String,
    pub positions: Vec<[f32; 3]>,
    pub normals: Vec<[f32; 3]>,
    pub tangents: Vec<[f32; 4]>,
    pub colors: Vec<[f32; 4]>,
    /// uv0..uv3 (two components each; absent channels empty).
    pub uvs: Vec<Vec<[f32; 2]>>,
    pub indices: Vec<u32>,
    /// Per submesh: first index, index count, base vertex, topology (Unity's MeshTopology).
    pub submeshes: Vec<[i32; 4]>,
}

fn int(v: &Value, key: &str) -> i64 {
    v.get(key)
        .and_then(Value::as_str)
        .and_then(|s| s.parse().ok())
        .unwrap_or(0)
}

fn hex(v: &Value, key: &str) -> Result<Vec<u8>> {
    let h = v
        .get(key)
        .and_then(Value::as_str)
        .with_context(|| format!("no {key}"))?;
    (0..h.len() / 2)
        .map(|i| u8::from_str_radix(&h[2 * i..2 * i + 2], 16).context("bad hex"))
        .collect()
}

fn half(bits: u16) -> f32 {
    let sign = if bits & 0x8000 != 0 { -1.0 } else { 1.0 };
    let exp = ((bits >> 10) & 0x1f) as i32;
    let man = (bits & 0x3ff) as f32;
    sign * match exp {
        0 => man * 2f32.powi(-24),
        31 => f32::INFINITY,
        e => (1.0 + man / 1024.0) * 2f32.powi(e - 15),
    }
}

/// One component of a vertex attribute in Unity's VertexAttributeFormat.
fn component(format: i64, b: &[u8]) -> f32 {
    match format {
        0 => f32::from_le_bytes([b[0], b[1], b[2], b[3]]),
        1 => half(u16::from_le_bytes([b[0], b[1]])),
        2 => b[0] as f32 / 255.0,
        3 => (b[0] as i8 as f32 / 127.0).max(-1.0),
        4 => u16::from_le_bytes([b[0], b[1]]) as f32 / 65535.0,
        5 => (i16::from_le_bytes([b[0], b[1]]) as f32 / 32767.0).max(-1.0),
        6 => b[0] as f32,
        7 => b[0] as i8 as f32,
        8 => u16::from_le_bytes([b[0], b[1]]) as f32,
        9 => i16::from_le_bytes([b[0], b[1]]) as f32,
        10 => u32::from_le_bytes([b[0], b[1], b[2], b[3]]) as f32,
        _ => i32::from_le_bytes([b[0], b[1], b[2], b[3]]) as f32,
    }
}

fn format_size(format: i64) -> usize {
    match format {
        0 | 10 | 11 => 4,
        1 | 4 | 5 | 8 | 9 => 2,
        _ => 1,
    }
}

/// A Mesh's fields: vertex channels decoded from their streams, indices, submeshes.
pub fn read_mesh(object: &Value) -> Result<SerializedMesh> {
    if int(object, "m_MeshCompression") != 0 {
        bail!("compressed meshes are not read yet");
    }
    let vd = object.get("m_VertexData").context("no vertex data")?;
    let count = int(vd, "m_VertexCount") as usize;
    let data = hex(vd, "m_DataSize")?;
    let channels: Vec<(usize, usize, i64, usize)> = vd
        .get("m_Channels")
        .and_then(Value::as_seq)
        .unwrap_or_default()
        .iter()
        .map(|c| {
            (
                int(c, "stream") as usize,
                int(c, "offset") as usize,
                int(c, "format"),
                int(c, "dimension") as usize & 0xf,
            )
        })
        .collect();
    // Streams: each holds its channels interleaved; a stream starts 16-byte aligned.
    let streams = channels.iter().map(|c| c.0 + 1).max().unwrap_or(0);
    let mut stride = vec![0usize; streams];
    for &(s, offset, format, dim) in &channels {
        if dim > 0 {
            stride[s] = stride[s].max(offset + format_size(format) * dim);
        }
    }
    let mut start = vec![0usize; streams];
    let mut at = 0usize;
    for s in 0..streams {
        start[s] = at;
        at += stride[s] * count;
        at = (at + 15) & !15;
    }
    let read = |channel: usize| -> Option<Vec<[f32; 4]>> {
        let &(s, offset, format, dim) = channels.get(channel)?;
        if dim == 0 {
            return None;
        }
        let size = format_size(format);
        let mut out = Vec::with_capacity(count);
        for v in 0..count {
            let base = start[s] + v * stride[s] + offset;
            let mut value = [
                0.0,
                0.0,
                0.0,
                if channel == 2 || channel == 3 {
                    1.0
                } else {
                    0.0
                },
            ];
            for (k, slot) in value.iter_mut().enumerate().take(dim.min(4)) {
                let b = data.get(base + k * size..base + (k + 1) * size)?;
                *slot = component(format, b);
            }
            out.push(value);
        }
        Some(out)
    };
    let mut mesh = SerializedMesh {
        name: object
            .get("m_Name")
            .and_then(Value::as_str)
            .unwrap_or("")
            .to_string(),
        ..Default::default()
    };
    mesh.positions = read(0)
        .unwrap_or_default()
        .iter()
        .map(|v| [v[0], v[1], v[2]])
        .collect();
    mesh.normals = read(1)
        .unwrap_or_default()
        .iter()
        .map(|v| [v[0], v[1], v[2]])
        .collect();
    mesh.tangents = read(2).unwrap_or_default();
    mesh.colors = read(3).unwrap_or_default();
    for uv in 4..8 {
        mesh.uvs.push(
            read(uv)
                .unwrap_or_default()
                .iter()
                .map(|v| [v[0], v[1]])
                .collect(),
        );
    }
    let indices = hex(object, "m_IndexBuffer")?;
    mesh.indices = if int(object, "m_IndexFormat") == 1 {
        indices
            .chunks_exact(4)
            .map(|b| u32::from_le_bytes([b[0], b[1], b[2], b[3]]))
            .collect()
    } else {
        indices
            .chunks_exact(2)
            .map(|b| u16::from_le_bytes([b[0], b[1]]) as u32)
            .collect()
    };
    let index_size = if int(object, "m_IndexFormat") == 1 {
        4
    } else {
        2
    };
    for sub in object
        .get("m_SubMeshes")
        .and_then(Value::as_seq)
        .unwrap_or_default()
    {
        mesh.submeshes.push([
            (int(sub, "firstByte") as usize / index_size) as i32,
            int(sub, "indexCount") as i32,
            int(sub, "baseVertex") as i32,
            int(sub, "topology") as i32,
        ]);
    }
    Ok(mesh)
}

/// The mesh `path_id` of a serialized file.
pub struct SerializedMeshImporter {
    pub path_id: i64,
}

impl Importer for SerializedMeshImporter {
    type Output = SerializedMesh;
    const KIND: &'static str = "serialized-mesh";
    const VERSION: u32 = 1;

    fn import(&self, _source: &Path, bytes: &[u8], _meta: &Value) -> Result<SerializedMesh> {
        read_mesh(&SerializedFile::parse(bytes)?.read(self.path_id)?)
    }

    fn options_key(&self) -> String {
        self.path_id.to_string()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The installed 6000.0.71f1's built-in Cube (skipped without it): 24 vertices, 36 indices.
    #[test]
    fn the_builtin_cube_decodes() {
        let Some(home) = std::env::var_os("HOME") else {
            return;
        };
        let file = Path::new(&home)
            .join("Unity/Hub/Editor/6000.0.71f1/Editor/Data/Resources/unity default resources");
        let Ok(bytes) = std::fs::read(&file) else {
            eprintln!("no 6000.0.71f1 install: skipped");
            return;
        };
        let cube = SerializedMeshImporter { path_id: 10202 }
            .import(&file, &bytes, &Value::Null)
            .unwrap();
        assert_eq!(cube.name, "Cube");
        assert_eq!((cube.positions.len(), cube.indices.len()), (24, 36));
        assert!(
            cube.positions
                .iter()
                .all(|p| p.iter().all(|c| (c.abs() - 0.5).abs() < 1e-6))
        );
        assert_eq!(cube.normals.len(), 24);
        assert_eq!(cube.uvs[0].len(), 24);
    }
}
