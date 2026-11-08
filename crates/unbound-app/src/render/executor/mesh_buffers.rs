//! Translate mesh-owned GPU streams into the renderer's interleaved layout after a write.
//! Layouts, pipelines and bindings live with the mesh; unchanged draws do no conversion work.
use super::{Op, UnityRenderer, compute::DispatchOp};
use unbound_runtime::render_protocol::commands::MeshBuffers;

struct Conversion {
    source: u64,
    index: bool,
    revision: u64,
    pipeline: wgpu::ComputePipeline,
    group: wgpu::BindGroup,
    count: u32,
}

pub(super) struct Binding {
    description: MeshBuffers,
    vertex: wgpu::Buffer,
    extra: Option<wgpu::Buffer>,
    index: wgpu::Buffer,
    conversions: Vec<Conversion>,
}

fn width(format: u32) -> u32 {
    match format {
        0 | 10 | 11 => 4,
        1 | 4 | 5 | 8 | 9 => 2,
        _ => 1,
    }
}

/// Read one packed component without alignment assumptions beyond the layout's whole words.
fn component(format: u32, offset: u32) -> String {
    let word = format!("src[(base + {offset}u) / 4u]");
    let shift = (offset % 4) * 8;
    let bits = width(format) * 8;
    let value = if bits == 32 {
        word
    } else {
        format!("(({word} >> {shift}u) & {}u)", (1u32 << bits) - 1)
    };
    match format {
        0 => format!("bitcast<f32>({value})"),
        1 => format!("unpack2x16float({value}).x"),
        2 | 4 => format!("(f32({value}) / {}.0)", (1u32 << bits) - 1),
        3 | 5 => format!(
            "max(f32(bitcast<i32>({value} << {}u) >> {}u) / {}.0, -1.0)",
            32 - bits,
            32 - bits,
            (1u32 << (bits - 1)) - 1
        ),
        7 | 9 => format!(
            "f32(bitcast<i32>({value} << {}u) >> {}u)",
            32 - bits,
            32 - bits
        ),
        11 => format!("f32(bitcast<i32>({value}))"),
        _ => format!("f32({value})"),
    }
}

const VERTEX_WORDS: u32 = (size_of::<super::resources::Vertex>() / 4) as u32;
const EXTRA_WORDS: u32 = (size_of::<super::resources::ExtraVertex>() / 4) as u32;

const HEADER: &str = "@group(0) @binding(0) var<storage, read> src: array<u32>;\n@group(0) @binding(1) var<storage, read_write> dst: array<u32>;\n";

/// The renderer supplies absent UVs from the nearest lower channel. Update those aliases too.
fn vertex_shader(m: &MeshBuffers, stream: u32, extra: bool) -> String {
    let stride = m.streams[stream as usize].1;
    let mut code = format!(
        "{HEADER}@compute @workgroup_size(64) fn main(@builtin(global_invocation_id) id: vec3<u32>) {{\nlet v = id.x; if (v >= {}u) {{ return; }}\nlet base = v * {stride}u;\n",
        m.vertices
    );
    let mut offset = 0;
    for &[attribute, format, dimension, source] in &m.attributes {
        if source != stream {
            continue;
        }
        let values: Vec<String> = (0..4)
            .map(|c| {
                if c < dimension {
                    component(format, offset + c * width(format))
                } else if c == 3 {
                    "1.0".into()
                } else {
                    "0.0".into()
                }
            })
            .collect();
        offset += dimension * width(format);
        let mut targets = Vec::new();
        if !extra {
            match attribute {
                0 => targets.push((0, 3)),
                1 => targets.push((3, 3)),
                2 => targets.push((6, 4)),
                3 => targets.push((10, 4)),
                4..=7 => {
                    for uv in attribute..=7 {
                        if uv > attribute && m.attributes.iter().any(|a| a[0] == uv) {
                            break;
                        }
                        targets.push((14 + (uv - 4) * 4, 4));
                    }
                }
                _ => {}
            }
        } else if (8..=11).contains(&attribute) {
            targets.push(((attribute - 8) * 4, 4));
        }
        for (target, count) in targets {
            for c in 0..count.min(dimension) {
                code += &format!(
                    "dst[v * {}u + {}u] = bitcast<u32>({});\n",
                    if extra { EXTRA_WORDS } else { VERTEX_WORDS },
                    target + c,
                    values[c as usize]
                );
            }
        }
    }
    code + "}\n"
}

fn index_shader(m: &MeshBuffers) -> (String, u32) {
    let read = if m.index_size == 4 {
        "src[i]"
    } else {
        "((src[i / 2u] >> ((i % 2u) * 16u)) & 65535u)"
    };
    let mut code = format!(
        "{HEADER}fn index(i: u32) -> u32 {{ return {read}; }}\n@compute @workgroup_size(64) fn main(@builtin(global_invocation_id) id: vec3<u32>) {{ let n = id.x;\n"
    );
    let mut destination = 0u32;
    let mut quad_tail = m.index_count;
    for &[start, count, topology] in &m.submeshes {
        let count = if topology == 2 { count / 4 * 6 } else { count };
        if count == 0 {
            continue;
        }
        let source = if topology == 2 {
            let tail = quad_tail;
            quad_tail += count;
            tail
        } else {
            start
        };
        code += &format!(
            "if (n >= {destination}u && n < {}u) {{ let k = n - {destination}u; dst[n] = index({source}u + k); }}\n",
            destination + count
        );
        destination += count;
    }
    (code + "}\n", destination)
}

impl UnityRenderer {
    pub(super) fn bind_mesh_buffers(&mut self, m: MeshBuffers) {
        let Some(mesh) = self.meshes.get(&m.mesh) else {
            return;
        };
        let bindings = if m.indices_only {
            &mut self.mesh_index_sources
        } else {
            &mut self.mesh_buffers
        };
        if bindings.get(&m.mesh).is_some_and(|b| {
            b.description == m
                && b.vertex == mesh.vertex
                && b.extra == mesh.extra
                && b.index == mesh.index
        }) {
            return;
        }
        let mut binding = Binding {
            description: m.clone(),
            vertex: mesh.vertex.clone(),
            extra: mesh.extra.clone(),
            index: mesh.index.clone(),
            conversions: Vec::new(),
        };
        let mut add = |source, output: &wgpu::Buffer, code: String, count, index| {
            let Some(input) = self.buffers.get(&source) else {
                return;
            };
            if count == 0 {
                return;
            }
            let module = self
                .device
                .create_shader_module(wgpu::ShaderModuleDescriptor {
                    label: Some("unbound mesh stream conversion"),
                    source: wgpu::ShaderSource::Wgsl(code.into()),
                });
            let pipeline = self
                .device
                .create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
                    label: Some("unbound mesh stream conversion"),
                    layout: None,
                    module: &module,
                    entry_point: Some("main"),
                    compilation_options: Default::default(),
                    cache: None,
                });
            let group = self.device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("unbound mesh stream conversion"),
                layout: &pipeline.get_bind_group_layout(0),
                entries: &[
                    wgpu::BindGroupEntry {
                        binding: 0,
                        resource: input.buffer.as_entire_binding(),
                    },
                    wgpu::BindGroupEntry {
                        binding: 1,
                        resource: output.as_entire_binding(),
                    },
                ],
            });
            binding.conversions.push(Conversion {
                source,
                index,
                revision: u64::MAX,
                pipeline,
                group,
                count,
            });
        };
        for stream in 0..4 {
            let source = m.streams[stream as usize].0;
            if source == 0 {
                continue;
            }
            if m.attributes.iter().any(|a| a[3] == stream && a[0] <= 7) {
                add(
                    source,
                    &binding.vertex,
                    vertex_shader(&m, stream, false),
                    m.vertices,
                    false,
                );
            }
            if let Some(extra) = &binding.extra {
                if m.attributes
                    .iter()
                    .any(|a| a[3] == stream && (8..=11).contains(&a[0]))
                {
                    add(
                        source,
                        extra,
                        vertex_shader(&m, stream, true),
                        m.vertices,
                        false,
                    );
                }
            }
        }
        let (code, count) = index_shader(&m);
        add(m.indices, &binding.index, code, count, true);
        bindings.insert(m.mesh, binding);
    }

    pub(super) fn invalidate_mesh_buffers(&mut self, mesh: u64, keep_indices: bool) {
        for bindings in [&mut self.mesh_buffers, &mut self.mesh_index_sources] {
            if let Some(binding) = bindings.get_mut(&mesh) {
                for conversion in &mut binding.conversions {
                    if !keep_indices || !conversion.index {
                        conversion.revision = u64::MAX;
                    }
                }
            }
        }
    }

    pub(super) fn sync_mesh_buffers(&mut self, mesh: u64, ops: &mut Vec<Op>) -> bool {
        let external_indices = self.mesh_index_sources.contains_key(&mesh);
        let mut changed = false;
        for (bindings, skip_indices) in [
            (&mut self.mesh_buffers, external_indices),
            (&mut self.mesh_index_sources, false),
        ] {
            let Some(binding) = bindings.get_mut(&mesh) else {
                continue;
            };
            for conversion in &mut binding.conversions {
                if skip_indices && conversion.index {
                    continue;
                }
                let Some(source) = self.buffers.get(&conversion.source) else {
                    continue;
                };
                if source.revision == conversion.revision {
                    continue;
                }
                conversion.revision = source.revision;
                ops.push(Op::Dispatch(DispatchOp {
                    pipeline: conversion.pipeline.clone(),
                    bind_group: conversion.group.clone(),
                    groups: [conversion.count.div_ceil(64), 1, 1],
                    indirect: None,
                }));
                changed = true;
            }
        }
        changed
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn validate(code: &str) {
        let module = naga::front::wgsl::parse_str(code)
            .unwrap_or_else(|e| panic!("{}", e.emit_to_string(code)));
        naga::valid::Validator::new(
            naga::valid::ValidationFlags::all(),
            naga::valid::Capabilities::all(),
        )
        .validate(&module)
        .unwrap_or_else(|e| panic!("{e:?}\n{code}"));
    }

    #[test]
    fn conversions_accept_packed_formats_and_uv_aliases() {
        for format in 0..12 {
            let m = MeshBuffers {
                mesh: 1,
                indices_only: false,
                vertices: 4,
                streams: [(1, 64); 4],
                attributes: vec![
                    [0, 0, 3, 0],
                    [3, format, 4, 1],
                    [4, format, 4, 1],
                    [8, format, 4, 2],
                ],
                indices: 2,
                index_size: 2,
                index_count: 6,
                submeshes: vec![[0, 4, 2], [0, 2, 3]],
            };
            for stream in 0..2 {
                validate(&vertex_shader(&m, stream, false));
            }
            validate(&vertex_shader(&m, 2, true));
            validate(&index_shader(&m).0);
        }
    }

    #[test]
    fn index_conversion_preserves_ranges_and_expands_quads() {
        let m = MeshBuffers {
            mesh: 1,
            indices_only: false,
            vertices: 4,
            streams: [(0, 0); 4],
            attributes: vec![],
            indices: 2,
            index_size: 2,
            index_count: 7,
            submeshes: vec![[3, 4, 2], [0, 2, 3], [0, 0, 0]],
        };
        let (code, count) = index_shader(&m);
        assert_eq!(count, 8);
        assert!(code.contains("index(7u +"));
        assert!(code.contains("n >= 6u && n < 8u"));
    }
}
