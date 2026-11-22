//! `baked-gi-bake`: Unbound's bake of a scene's baked lightmaps for the renderers and meshes the
//! managed side sends: the layout of the lightmaps (unbound_gi::baked::atlas) and, of each
//! lightmap, its texels before lighting (unbound_gi::baked::gbuffer). The lightmaps are not lit
//! yet; the texel buffers are written out only where `UNBOUND_BAKED_GI_BUFFERS` names a folder
//! (tests/projects/bakedbuffers).
use anyhow::{Context, Result, ensure};
use base64::Engine;
use serde_json::{Value as Json, json};
use std::path::Path;
use unbound_gi::baked::{atlas, gbuffer};

/// The folder the texel buffers of each bake go to, when set: `<folder>/<scene GUID>/`.
pub const BUFFERS_VARIABLE: &str = "UNBOUND_BAKED_GI_BUFFERS";

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BakeRequest {
    max_size: i32,
    padding: i32,
    resolution: f32,
    fixed_size: bool,
    /// The scene's GUID as text.
    scene: String,
    meshes: Vec<MeshData>,
    renderers: Vec<Renderer>,
}

/// A mesh of the bake. Its arrays are little-endian values in base64, so that every float
/// arrives as it is (also one that is not a number).
#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct MeshData {
    /// Local positions, three floats each.
    positions: String,
    /// Three floats each; empty for a mesh without normals.
    normals: String,
    /// Two floats each; empty for a mesh without that channel.
    uv: String,
    uv2: String,
    /// The triangles of each submesh, three 32-bit indices each.
    submeshes: Vec<String>,
}

/// A renderer of the bake: its mesh, where it is and its lightmapping settings.
#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct Renderer {
    /// Its name, for messages.
    name: String,
    /// Its file ID in the scene.
    file_id: i64,
    /// Which of the request's meshes.
    mesh: usize,
    /// Column-major.
    local_to_world: [f32; 16],
    scale_in_lightmap: f32,
    /// The most detailed LOD level it is in, if it is in a LOD Group.
    lod_level: Option<u32>,
    /// It contributes to global illumination but is lit by light probes.
    #[serde(default)]
    probe_lit: bool,
    parameters: Parameters,
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct Parameters {
    back_face_tolerance: f32,
    anti_aliasing_samples: i32,
    pushoff: f32,
    baked_lightmap_tag: i32,
    max_lightmap_count: i32,
}

/// A GUID's text as Unity keeps it: four 32-bit words of eight hex digits each, the lowest
/// digit first.
fn guid_words(text: &str) -> Result<[u32; 4]> {
    ensure!(
        text.len() == 32,
        "a scene GUID of {} characters",
        text.len()
    );
    let mut words = [0u32; 4];
    for (k, c) in text.chars().enumerate() {
        let digit = c
            .to_digit(16)
            .with_context(|| format!("a scene GUID '{text}'"))?;
        words[k / 8] |= digit << (4 * (k % 8));
    }
    Ok(words)
}

/// Values of `N` four-byte numbers each from base64.
fn values<T: Copy, const N: usize>(
    text: &str,
    what: &str,
    read: fn([u8; 4]) -> T,
) -> Result<Vec<[T; N]>> {
    let bytes = base64::engine::general_purpose::STANDARD
        .decode(text)
        .with_context(|| format!("a mesh's {what}"))?;
    ensure!(
        bytes.len() % (4 * N) == 0,
        "a mesh's {what} of {} bytes",
        bytes.len()
    );
    Ok(bytes
        .chunks_exact(4 * N)
        .map(|c| {
            std::array::from_fn(|k| read([c[4 * k], c[4 * k + 1], c[4 * k + 2], c[4 * k + 3]]))
        })
        .collect())
}

/// A mesh as sent.
struct SourceMesh {
    positions: Vec<[f32; 3]>,
    normals: Vec<[f32; 3]>,
    uv: Vec<[f32; 2]>,
    uv2: Vec<[f32; 2]>,
    submeshes: Vec<Vec<[u32; 3]>>,
}

impl SourceMesh {
    fn read(data: &MeshData) -> Result<SourceMesh> {
        Ok(SourceMesh {
            positions: values(&data.positions, "positions", f32::from_le_bytes)?,
            normals: values(&data.normals, "normals", f32::from_le_bytes)?,
            uv: values(&data.uv, "UVs", f32::from_le_bytes)?,
            uv2: values(&data.uv2, "lightmap UVs", f32::from_le_bytes)?,
            submeshes: data
                .submeshes
                .iter()
                .map(|s| values(s, "triangles", u32::from_le_bytes))
                .collect::<Result<_>>()?,
        })
    }

    /// The UVs its lightmap takes: its lightmap UVs where it has them, else its UVs.
    fn lightmap_uvs(&self) -> &[[f32; 2]] {
        if self.uv2.len() == self.positions.len() {
            &self.uv2
        } else {
            &self.uv
        }
    }

    /// The Editor leaves a mesh without normals out of the bake (tests/projects/bakedbuffers
    /// `Bare`).
    fn has_normals(&self) -> bool {
        self.normals.len() == self.positions.len()
    }
}

/// A unit normal of a renderer in world space: through the inverse transpose of its matrix.
fn world_normal(m: &[f32; 16], n: [f32; 3]) -> [f32; 3] {
    let m = |r: usize, c: usize| m[4 * c + r] as f64;
    let cofactor = |r: usize, c: usize| {
        let (r1, r2, c1, c2) = ((r + 1) % 3, (r + 2) % 3, (c + 1) % 3, (c + 2) % 3);
        m(r1, c1) * m(r2, c2) - m(r1, c2) * m(r2, c1)
    };
    // The cofactors are the inverse transpose times the determinant, whose sign turns the
    // normal with a mirroring matrix.
    let determinant: f64 = (0..3).map(|c| m(0, c) * cofactor(0, c)).sum();
    let sign = if determinant < 0.0 { -1.0 } else { 1.0 };
    let v: [f64; 3] =
        std::array::from_fn(|r| sign * (0..3).map(|c| cofactor(r, c) * n[c] as f64).sum::<f64>());
    let length = (v[0] * v[0] + v[1] * v[1] + v[2] * v[2]).sqrt();
    if length > 0.0 {
        v.map(|x| (x / length) as f32)
    } else {
        n
    }
}

/// The files of a bake's texel buffers in `folder`: `buffers.json` (each lightmap's size,
/// samples per texel side and renderers, by their number in the bake), and per lightmap
/// `occupancy<N>.u8` (how many of each texel's samples have a surface: none or all of them),
/// `chartIndex<N>.i32` (each texel's chart) and `normal<N>.f32` (per sample the world normal and
/// the sample's number, or [`gbuffer::EMPTY`] without a surface).
struct Buffers<'a> {
    folder: &'a Path,
    lightmaps: Vec<Json>,
}

impl<'a> Buffers<'a> {
    const DESCRIPTION: &'static str = "buffers.json";
    const PER_LIGHTMAP: [(&'static str, &'static str); 3] = [
        ("occupancy", ".u8"),
        ("chartIndex", ".i32"),
        ("normal", ".f32"),
    ];

    /// Starts a bake's buffers in the folder, without those an earlier bake left there.
    fn start(folder: &'a Path) -> Result<Buffers<'a>> {
        std::fs::create_dir_all(folder)
            .with_context(|| format!("creating {}", folder.display()))?;
        for entry in
            std::fs::read_dir(folder).with_context(|| format!("reading {}", folder.display()))?
        {
            let entry = entry?;
            let name = entry.file_name();
            let name = name.to_string_lossy();
            let ours = name == Self::DESCRIPTION
                || Self::PER_LIGHTMAP.iter().any(|(prefix, suffix)| {
                    name.strip_prefix(prefix)
                        .and_then(|rest| rest.strip_suffix(suffix))
                        .is_some_and(|number| {
                            !number.is_empty() && number.bytes().all(|b| b.is_ascii_digit())
                        })
                });
            if ours {
                std::fs::remove_file(entry.path())
                    .with_context(|| format!("removing {}", entry.path().display()))?;
            }
        }
        Ok(Buffers {
            folder,
            lightmaps: Vec::new(),
        })
    }

    fn write(&self, name: String, bytes: Vec<u8>) -> Result<()> {
        let path = self.folder.join(name);
        std::fs::write(&path, bytes).with_context(|| format!("writing {}", path.display()))
    }

    /// The next lightmap: its texels, and its renderers by their number in the bake.
    fn lightmap(
        &mut self,
        g: &gbuffer::GBuffer,
        members: &[usize],
        renderers: &[&Renderer],
    ) -> Result<()> {
        let index = self.lightmaps.len();
        self.write(format!("occupancy{index}.u8"), g.occupancy.clone())?;
        self.write(
            format!("chartIndex{index}.i32"),
            g.charts.iter().flat_map(|c| c.to_le_bytes()).collect(),
        )?;
        let normals = g.normals.iter().zip(&g.positions).flat_map(|(n, p)| {
            let local = [n[0], n[1], n[2]];
            let world = if p[3] < 0.0 {
                local
            } else {
                world_normal(&renderers[members[p[3] as usize]].local_to_world, local)
            };
            [world[0], world[1], world[2], n[3]]
        });
        self.write(
            format!("normal{index}.f32"),
            normals.flat_map(|v| v.to_le_bytes()).collect(),
        )?;
        self.lightmaps.push(json!({
            "width": g.width, "height": g.height, "supersampling": g.supersampling, "instances": members,
        }));
        Ok(())
    }

    fn finish(self) -> Result<()> {
        self.write(
            Self::DESCRIPTION.into(),
            serde_json::to_vec_pretty(&json!({ "lightmaps": self.lightmaps }))?,
        )
    }
}

/// The bake: each lightmap's size, each renderer's lightmap and scale and offset in the order
/// sent (`null` for a renderer the bake leaves out, with `invalidNormals` set: one whose mesh
/// has no normals), and `order`, the bake's renderers by their place in the request in the
/// order the Editor numbers them ([`atlas::object_key`]; renderers of prefab instances are not
/// measured and take their own file ID).
pub fn bake_request(request: BakeRequest) -> Result<Json> {
    bake(
        request,
        std::env::var_os(BUFFERS_VARIABLE).as_deref().map(Path::new),
    )
}

fn bake(request: BakeRequest, buffers: Option<&Path>) -> Result<Json> {
    let settings = atlas::Settings {
        max_size: request.max_size,
        padding: request.padding,
        resolution: request.resolution,
        fixed_size: request.fixed_size,
    };
    let scene = guid_words(&request.scene)?;
    let meshes = request
        .meshes
        .iter()
        .map(SourceMesh::read)
        .collect::<Result<Vec<_>>>()?;
    for r in &request.renderers {
        ensure!(
            r.mesh < meshes.len(),
            "'{}': mesh {} of {}",
            r.name,
            r.mesh,
            meshes.len()
        );
    }
    // The renderers of the bake, by their place in the request, in the bake's order.
    let mut taken: Vec<usize> = (0..request.renderers.len())
        .filter(|&i| meshes[request.renderers[i].mesh].has_normals())
        .collect();
    taken.sort_by_key(|&i| atlas::object_key(request.renderers[i].file_id, 0));
    let parameters = |r: &Renderer| atlas::Parameters {
        back_face_tolerance: r.parameters.back_face_tolerance,
        supersampling: atlas::Parameters::supersampling_of(r.parameters.anti_aliasing_samples),
        pushoff: r.parameters.pushoff,
        baked_lightmap_tag: r.parameters.baked_lightmap_tag,
        max_lightmap_count: r.parameters.max_lightmap_count,
    };
    let instances = taken
        .iter()
        .map(|&i| -> Result<atlas::Instance> {
            let r = &request.renderers[i];
            let mesh = &meshes[r.mesh];
            let triangles: Vec<[u32; 3]> = mesh.submeshes.iter().flatten().copied().collect();
            let area = atlas::surface_area(&mesh.positions, &triangles, &r.local_to_world);
            // What the Editor makes of such a mesh is not measured.
            ensure!(
                mesh.lightmap_uvs().iter().flatten().all(|v| v.is_finite()),
                "'{}': a lightmap UV of its mesh is infinite or not a number, which the bake does not support",
                r.name
            );
            let uv_bounds = atlas::uv_bounds(mesh.lightmap_uvs());
            ensure!(
                uv_bounds[2] > 0.0 && uv_bounds[3] > 0.0,
                "'{}': its lightmap UVs span no area (a mesh without UVs, or all of one value), which the layout does not support",
                r.name
            );
            let m = &r.local_to_world;
            Ok(atlas::Instance {
                probe_lit: r.probe_lit,
                scene,
                parameters: parameters(r),
                rect: atlas::rectangle(r.scale_in_lightmap, settings.resolution, r.lod_level, area, uv_bounds, settings.max_size),
                uv_bounds,
                morton: atlas::morton([m[12], m[13], m[14]]),
            })
        })
        .collect::<Result<Vec<_>>>()?;
    let layout = atlas::layout(&settings, &instances)?;

    if let Some(folder) = buffers {
        // Each lightmap's renderers in the bake's order, their meshes made ready once.
        let mut ready: Vec<Option<gbuffer::Mesh>> = meshes.iter().map(|_| None).collect();
        for (&i, placement) in taken.iter().zip(&layout.placements) {
            let r = &request.renderers[i];
            if placement.lightmap != atlas::NO_LIGHTMAP && ready[r.mesh].is_none() {
                let mesh = &meshes[r.mesh];
                let made = gbuffer::Mesh::new(
                    mesh.positions.clone(),
                    &mesh.normals,
                    &mesh.uv,
                    &mesh.uv2,
                    mesh.submeshes.clone(),
                )
                .map_err(|e| anyhow::anyhow!("'{}': its mesh cannot be baked: {e}", r.name))?;
                ready[r.mesh] = Some(made);
            }
        }
        // One lightmap at a time: its texels are written and let go before the next.
        let baked: Vec<&Renderer> = taken.iter().map(|&i| &request.renderers[i]).collect();
        let folder = folder.join(&request.scene);
        let mut written = Buffers::start(&folder)?;
        for (index, size) in layout.lightmaps.iter().enumerate() {
            let members: Vec<usize> = (0..taken.len())
                .filter(|&k| layout.placements[k].lightmap == index as u32)
                .collect();
            let placed: Vec<gbuffer::Instance> = members
                .iter()
                .enumerate()
                .map(|(place, &k)| gbuffer::Instance {
                    mesh: ready[baked[k].mesh].as_ref().expect("made above"),
                    scale_offset: layout.placements[k].scale_offset,
                    index: place as i32,
                })
                .collect();
            // The renderers of a lightmap share their Lightmap Parameters.
            let supersampling = members
                .first()
                .map_or(1, |&k| instances[k].parameters.supersampling);
            let g = gbuffer::gbuffer(
                size[0] as usize,
                size[1] as usize,
                supersampling as usize,
                &placed,
            );
            written.lightmap(&g, &members, &baked)?;
        }
        written.finish()?;
    }

    let mut placements = vec![Json::Null; request.renderers.len()];
    for (&i, p) in taken.iter().zip(&layout.placements) {
        // One with Scale In Lightmap 0 is packed like any other, without a lightmap of its own.
        let lightmap = if request.renderers[i].scale_in_lightmap > 0.0 {
            p.lightmap
        } else {
            atlas::NO_LIGHTMAP
        };
        placements[i] = json!({ "lightmap": lightmap, "scaleOffset": p.scale_offset });
    }
    Ok(json!({
        "lightmaps": layout.lightmaps,
        "placements": placements,
        "order": taken,
        "invalidNormals": taken.len() != request.renderers.len(),
    }))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn encoded<T: Copy, const N: usize>(values: &[T], bytes: fn(T) -> [u8; N]) -> String {
        base64::engine::general_purpose::STANDARD
            .encode(values.iter().flat_map(|&v| bytes(v)).collect::<Vec<u8>>())
    }

    fn floats(values: &[f32]) -> String {
        encoded(values, f32::to_le_bytes)
    }

    /// A unit quad facing -z with the given lightmap UVs and normals.
    fn quad_mesh(uv2: &[f32], normals: &[f32]) -> Json {
        json!({
            "positions": floats(&[0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 1.0, 0.0, 0.0, 1.0, 0.0]),
            "normals": floats(normals),
            "uv": floats(&[]),
            "uv2": floats(uv2),
            "submeshes": [encoded(&[0u32, 2, 1, 0, 3, 2], u32::to_le_bytes)],
        })
    }

    const UNIT: [f32; 8] = [0.0, 0.0, 1.0, 0.0, 1.0, 1.0, 0.0, 1.0];
    const BACK: [f32; 12] = [
        0.0, 0.0, -1.0, 0.0, 0.0, -1.0, 0.0, 0.0, -1.0, 0.0, 0.0, -1.0,
    ];

    fn renderer(name: &str, file_id: i64, mesh: usize, scale: f32, x: f32) -> Json {
        json!({
            "name": name,
            "fileId": file_id,
            "mesh": mesh,
            "localToWorld": [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, 0, 0, 1],
            "scaleInLightmap": scale,
            "lodLevel": null,
            "parameters": {
                "backFaceTolerance": 0.9, "antiAliasingSamples": 2, "pushoff": 0.0001,
                "bakedLightmapTag": -1, "maxLightmapCount": 0,
            },
        })
    }

    fn request(meshes: Vec<Json>, renderers: Vec<Json>) -> BakeRequest {
        serde_json::from_value(json!({
            "maxSize": 64, "padding": 2, "resolution": 10.0, "fixedSize": true,
            "scene": "10000000200000003000000040000000", "meshes": meshes, "renderers": renderers,
        }))
        .unwrap()
    }

    #[test]
    fn scale_in_lightmap_0_is_packed_without_a_lightmap() {
        // The scale-0 quad sorts first (its Morton code) and takes a one-texel slot at the
        // origin, which moves the other above it (a 3-texel strip with the padding).
        let out = bake(
            request(
                vec![quad_mesh(&UNIT, &BACK)],
                vec![
                    renderer("lit", 244122856, 0, 1.0, 0.2),
                    renderer("zero", 879057353, 0, 0.0, 0.0),
                ],
            ),
            None,
        )
        .unwrap();
        let placements = out["placements"].as_array().unwrap();
        assert_eq!(placements[1]["lightmap"], json!(atlas::NO_LIGHTMAP));
        assert_eq!(placements[0]["lightmap"], json!(0));
        assert_eq!(placements[0]["scaleOffset"][3], json!(3.0 / 64.0));
        assert_eq!(out["invalidNormals"], json!(false));
        // The bake numbers them by the hashes of their file IDs: the second before the first
        // here (two of tests/projects/bakedbuffers Shapes).
        assert!(atlas::object_key(879057353, 0) < atlas::object_key(244122856, 0));
        assert_eq!(out["order"], json!([1, 0]));
    }

    #[test]
    fn lightmap_uvs_without_area_are_refused_by_name() {
        let error = bake(
            request(
                vec![quad_mesh(&[], &BACK)],
                vec![renderer("bare", 1, 0, 1.0, 0.0)],
            ),
            None,
        )
        .err()
        .unwrap();
        assert!(error.to_string().contains("'bare'"), "{error}");
        assert!(
            bake(
                request(
                    vec![quad_mesh(&[0.5; 8], &BACK)],
                    vec![renderer("flat", 1, 0, 1.0, 0.0)]
                ),
                None
            )
            .is_err()
        );
        // Not a number and infinite arrive as they are and are refused, wherever they are in
        // the mesh (a later number would hide one from the UVs' bounds) and whether or not the
        // texels are written.
        for (vertex, value) in [
            (3, f32::NAN),
            (1, f32::NAN),
            (1, f32::INFINITY),
            (0, f32::NEG_INFINITY),
        ] {
            let mut uvs = UNIT;
            uvs[2 * vertex] = value;
            let error = bake(
                request(
                    vec![quad_mesh(&uvs, &BACK)],
                    vec![renderer("odd", 1, 0, 1.0, 0.0)],
                ),
                None,
            )
            .err()
            .unwrap();
            assert!(
                error.to_string().contains("'odd'") && error.to_string().contains("not a number"),
                "{vertex} {value}: {error}"
            );
        }
    }

    #[test]
    fn a_mesh_without_normals_is_left_out_of_the_bake() {
        // tests/projects/bakedbuffers Bare: the others are laid out as if it were not there.
        let meshes = vec![quad_mesh(&UNIT, &BACK), quad_mesh(&UNIT, &[])];
        let with = bake(
            request(
                meshes.clone(),
                vec![
                    renderer("bare", 1, 1, 1.0, 0.0),
                    renderer("lit", 2, 0, 1.0, 0.2),
                ],
            ),
            None,
        )
        .unwrap();
        let alone = bake(request(meshes, vec![renderer("lit", 2, 0, 1.0, 0.2)]), None).unwrap();
        assert_eq!(with["invalidNormals"], json!(true));
        assert_eq!(with["placements"][0], Json::Null);
        assert_eq!(with["order"], json!([1]));
        assert_eq!(with["placements"][1], alone["placements"][0]);
        assert_eq!(with["lightmaps"], alone["lightmaps"]);
        assert_eq!(alone["invalidNormals"], json!(false));
    }

    #[test]
    fn the_buffers_of_each_lightmap_are_written_where_asked() {
        let folder = std::env::temp_dir().join(format!("unbound-baked-gi-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&folder);
        // Turned a quarter about y: the quad's normal -z goes to -x in the world.
        let mut turned = renderer("turned", 1, 0, 1.0, 0.0);
        turned["localToWorld"] = json!([0, 0, -1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1]);
        bake(
            request(vec![quad_mesh(&UNIT, &BACK)], vec![turned]),
            Some(&folder),
        )
        .unwrap();
        let scene = folder.join("10000000200000003000000040000000");
        let description: Json =
            serde_json::from_slice(&std::fs::read(scene.join("buffers.json")).unwrap()).unwrap();
        let lightmap = &description["lightmaps"][0];
        assert_eq!(
            (
                lightmap["width"].as_u64(),
                lightmap["supersampling"].as_u64()
            ),
            (Some(64), Some(2))
        );
        assert_eq!(lightmap["instances"], json!([0]));
        // A 10-texel square at the origin of the 64-texel lightmap.
        let occupancy = std::fs::read(scene.join("occupancy0.u8")).unwrap();
        assert_eq!(occupancy.len(), 64 * 64);
        assert_eq!(occupancy.iter().filter(|&&v| v > 0).count(), 100);
        assert_eq!(
            (
                occupancy[0],
                occupancy[9],
                occupancy[10],
                occupancy[64 * 9],
                occupancy[64 * 10]
            ),
            (4, 4, 0, 4, 0)
        );
        let normals = std::fs::read(scene.join("normal0.f32")).unwrap();
        let first: Vec<f32> = normals[..16]
            .chunks_exact(4)
            .map(|c| f32::from_le_bytes(c.try_into().unwrap()))
            .collect();
        assert_eq!(&first[..3], &[-1.0, 0.0, 0.0]);
        let empty = &normals[16 * (128 * 127 + 127)..][..16];
        assert_eq!(
            f32::from_le_bytes(empty[12..].try_into().unwrap()),
            gbuffer::EMPTY
        );
        let charts = std::fs::read(scene.join("chartIndex0.i32")).unwrap();
        assert_eq!(i32::from_le_bytes(charts[..4].try_into().unwrap()), 0);
        assert_eq!(
            i32::from_le_bytes(charts[4 * 10..][..4].try_into().unwrap()),
            -1
        );
        // A bake of fewer lightmaps leaves none of the earlier one's files, and no other file
        // of the folder is touched.
        std::fs::write(scene.join("occupancy7.u8"), [1]).unwrap();
        std::fs::write(scene.join("normal12.f32"), [1]).unwrap();
        std::fs::write(scene.join("occupancy.u8.txt"), [1]).unwrap();
        let mut turned = renderer("turned", 1, 0, 1.0, 0.0);
        turned["probeLit"] = json!(true);
        bake(
            request(vec![quad_mesh(&UNIT, &BACK)], vec![turned]),
            Some(&folder),
        )
        .unwrap();
        let mut left: Vec<String> = std::fs::read_dir(&scene)
            .unwrap()
            .map(|e| e.unwrap().file_name().into_string().unwrap())
            .collect();
        left.sort();
        assert_eq!(left, ["buffers.json", "occupancy.u8.txt"]);
        let description: Json =
            serde_json::from_slice(&std::fs::read(scene.join("buffers.json")).unwrap()).unwrap();
        assert_eq!(description["lightmaps"], json!([]));
        let _ = std::fs::remove_dir_all(&folder);
    }

    #[test]
    fn a_mirroring_matrix_turns_the_normal_with_it() {
        // x mirrored: a normal along +x points along -x, one along +y stays.
        let m = [
            -1.0, 0.0, 0.0, 0.0, 0.0, 2.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
        ];
        assert_eq!(world_normal(&m, [1.0, 0.0, 0.0]), [-1.0, 0.0, 0.0]);
        assert_eq!(world_normal(&m, [0.0, 1.0, 0.0]), [0.0, 1.0, 0.0]);
        // Unequal scale: the normal leans away from the stretched axis.
        let n = world_normal(&m, [0.0, 0.6, 0.8]);
        assert!(
            (n[1] - 0.351_123_4).abs() < 1e-6 && (n[2] - 0.936_329_2).abs() < 1e-6,
            "{n:?}"
        );
    }

    #[test]
    fn guid_text_is_low_digit_first() {
        assert_eq!(
            guid_words("10000000200000003000000040000000").unwrap(),
            [1, 2, 3, 4]
        );
        assert_eq!(
            guid_words("f0000000000000000000000000000001").unwrap()[3],
            0x1000_0000
        );
        assert!(guid_words("xyz").is_err());
    }
}
