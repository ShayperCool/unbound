//! Prints a model file's blend shapes as ufbx reads them (offsets per control point), for
//! comparing with Unity's import.
//!
//! ```text
//! cargo run --release -p unbound-assets --example fbx_shapes -- <model file>
//! ```
use anyhow::{Context, Result, anyhow};

fn main() -> Result<()> {
    let path = std::env::args().nth(1).context("usage: fbx_shapes <model file>")?;
    let scene = ufbx::load_file(&path, ufbx::LoadOpts::default()).map_err(|e| anyhow!("{e:?}"))?;
    for mesh in scene.meshes.iter() {
        println!("mesh {} control points {}", mesh.element.name, mesh.num_vertices);
        if std::env::args().any(|a| a == "--faces") {
            for (i, v) in mesh.vertices.iter().enumerate() {
                println!("cp {i} {} {} {}", v.x, v.y, v.z);
            }
            for face in mesh.faces.iter() {
                let indices: Vec<String> = (face.index_begin..face.index_begin + face.num_indices)
                    .map(|c| mesh.vertex_indices[c as usize].to_string())
                    .collect();
                println!("face {}", indices.join(" "));
            }
        }
        for deformer in mesh.blend_deformers.iter() {
            for channel in deformer.channels.iter() {
                for key in channel.keyframes.iter() {
                    let shape = &key.shape;
                    println!(
                        "  channel {} weight {} key {} offsets {} normal offsets {}",
                        channel.element.name, channel.weight, key.target_weight, shape.num_offsets, shape.normal_offsets.len()
                    );
                    for i in 0..shape.num_offsets {
                        let v = shape.offset_vertices[i];
                        let p = mesh.vertices[v as usize];
                        let d = shape.position_offsets[i];
                        let n = shape.normal_offsets.get(i).copied().unwrap_or(ufbx::Vec3 { x: 0.0, y: 0.0, z: 0.0 });
                        println!("    {v} p {:.4} {:.4} {:.4} d {:.4} {:.4} {:.4} n {:.4} {:.4} {:.4}", p.x, p.y, p.z, d.x, d.y, d.z, n.x, n.y, n.z);
                    }
                }
            }
        }
    }
    Ok(())
}
