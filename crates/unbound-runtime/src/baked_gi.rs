//! `baked-gi-bake`: Unbound's bake of a scene's baked lightmaps for the renderers, meshes and
//! lights the managed side sends: the layout of the lightmaps (unbound_gi::baked::atlas) and,
//! of each lightmap, its texels (unbound_gi::baked::gbuffer), their direct light
//! (unbound_gi::baked::direct) and, with the renderers' materials, their indirect light
//! (unbound_gi::baked::indirect). The lightmaps themselves are not made yet; the texels and their
//! light are written out only where `UNBOUND_BAKED_GI_BUFFERS` names a folder
//! (tests/projects/bakedbuffers).
use anyhow::{Context, Result, ensure};
use base64::Engine;
use serde_json::{Value as Json, json};
use std::path::Path;
use unbound_gi::baked::composite;
use unbound_gi::baked::direct::{self, MixedMode, Receiver};
use unbound_gi::baked::directional;
use unbound_gi::baked::grid::{Bounds, Grid};
use unbound_gi::baked::images;
use unbound_gi::baked::indirect::{self, Material, Surface, Texture, World};
use unbound_gi::baked::light::{Kind, Light, Mode};
use unbound_gi::baked::light_probes;
use unbound_gi::baked::probes;
use unbound_gi::baked::sampling::{self, Samples};
use unbound_gi::baked::seams::{self, EdgePair};
use unbound_gi::baked::shadowmask;
use unbound_gi::baked::sky::{IMPORTANCE_DIRECTIONS, Importance, Sky};
use unbound_gi::baked::trace::Scene;
use unbound_gi::baked::{atlas, gbuffer, levels};

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
    /// The scene's lights that take part in the bake (mixed and baked ones).
    #[serde(default)]
    lights: Vec<LightData>,
    /// The Lighting Mode of mixed lights: 0 Baked Indirect, 1 Subtractive, 2 Shadowmask.
    #[serde(default)]
    mixed_mode: u8,
    /// Only the layout is asked for (the sizes of the renderers' material images come with it).
    #[serde(default)]
    layout_only: bool,
    /// The bake's lightmaps are asked for, as the files the Editor writes beside the scene
    /// (Lightmap-N_comp_light.exr), in the reply's `images`.
    #[serde(default)]
    lightmap_images: bool,
    /// The project stitches the shadowmask's seams (EditorSettings.shadowmaskStitching).
    #[serde(default)]
    shadowmask_stitching: bool,
    /// The Lighting Settings' Directional Mode is Directional: the bake keeps where its light
    /// comes from.
    #[serde(default)]
    directional: bool,
    /// What the indirect light is baked with; without it only the direct light is.
    #[serde(default)]
    indirect: Option<IndirectData>,
    /// The scene's light probe groups.
    #[serde(default)]
    probes: Vec<ProbeGroupData>,
}

/// A light probe group: its file ID in the scene and its probes' points in world space, three
/// numbers each.
#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct ProbeGroupData {
    file_id: i64,
    points: Vec<f32>,
    /// Its Remove Ringing (on by default, as a new group has it).
    #[serde(default = "removes_ringing")]
    dering: bool,
}

/// The light probes' points as the bake has them, group by group: the groups with probes in
/// their objects' order in the scene file (their file IDs'), each group's points in its order,
/// where the bake puts them (tests/projects/bakedbuffers `Probes*`).
fn probe_points(groups: &[ProbeGroupData]) -> Vec<Vec<[f32; 3]>> {
    probe_groups(groups)
        .iter()
        .map(|g| {
            g.points
                .chunks_exact(3)
                .map(|c| probes::snap([c[0], c[1], c[2]]))
                .collect()
        })
        .collect()
}

fn removes_ringing() -> bool {
    true
}

/// The light probe groups with probes, in their objects' order in the scene file.
fn probe_groups(groups: &[ProbeGroupData]) -> Vec<&ProbeGroupData> {
    let mut groups: Vec<&ProbeGroupData> = groups.iter().filter(|g| g.points.len() >= 3).collect();
    groups.sort_by_key(|g| g.file_id);
    groups
}

/// How far a light probe's paths start off the surfaces they meet (what the bake asks of its
/// probes; tests/projects/bakedbuffers `Probes*`).
const PROBE_PUSHOFF: f32 = 0.0001;

/// A light probe's samples for a texel's count asked for: four times the count a texel takes
/// before its supersampling (tests/projects/bakedbuffers `Probes*`, the bake inputs' counts).
fn probe_samples(asked: u32) -> u32 {
    4 * sampling::sample_count(asked, 1)
}

/// The settings and materials of the bake's indirect light.
#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct IndirectData {
    /// Indirect Samples, Max Bounces and Min Bounces of the Lighting Settings.
    samples: u32,
    max_bounces: u32,
    min_bounces: u32,
    /// The blue noise data of the Unity installation (`Resources/GI/SamplingData/bluenoisedata`).
    blue_noise: String,
    /// Each renderer's material images, in the renderers' order.
    materials: Vec<MaterialData>,
    /// The sky, if the bake takes light from it.
    #[serde(default)]
    environment: Option<EnvironmentData>,
}

/// The sky of the bake: Environment Samples, whether directions are picked where it is bright
/// (the Lighting Settings' environment importance sampling), and its six faces (+x, -x, +y,
/// -y, +z, -z) of `side` texels a side, three 32-bit floats a texel, rows from the top of a
/// face as a cube map has it, base64.
#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct EnvironmentData {
    samples: u32,
    importance_sampling: bool,
    side: usize,
    faces: String,
}

impl EnvironmentData {
    fn sky(&self) -> Result<Sky> {
        let bytes = base64::engine::general_purpose::STANDARD
            .decode(&self.faces)
            .context("the sky's faces")?;
        let texels = self.side.checked_mul(self.side).context("the sky's side")?;
        ensure!(
            self.side > 0 && Some(bytes.len()) == texels.checked_mul(6 * 12),
            "a sky of 6 faces of {} texels a side has {} bytes",
            self.side,
            bytes.len()
        );
        let mut values = bytes
            .chunks_exact(4)
            .map(|b| f32::from_le_bytes([b[0], b[1], b[2], b[3]]));
        let faces: [Texture; 6] = std::array::from_fn(|_| Texture {
            width: self.side,
            height: self.side,
            texels: (0..texels)
                .map(|_| {
                    let mut next = || values.next().unwrap_or(0.0);
                    [next(), next(), next(), 1.0]
                })
                .collect(),
        });
        let importance = self
            .importance_sampling
            .then(|| Importance::of(&faces, IMPORTANCE_DIRECTIONS));
        Ok(Sky { faces, importance })
    }
}

/// A renderer's albedo and emission over its lightmap UVs' bounds, drawn by its materials' meta
/// passes at the size the layout gave, and each submesh's Double Sided Global Illumination.
#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct MaterialData {
    albedo: ImageData,
    emissive: ImageData,
    #[serde(default)]
    double_sided: Vec<bool>,
}

/// An image: rows from the bottom (the first row is the lowest lightmap coordinate), base64.
/// Albedo is four bytes a texel (sRGB levels and coverage), emission four 32-bit floats.
#[derive(serde::Deserialize)]
struct ImageData {
    width: usize,
    height: usize,
    data: String,
}

impl ImageData {
    fn bytes(&self, what: &str, renderer: &str, texel: usize) -> Result<Vec<u8>> {
        let bytes = base64::engine::general_purpose::STANDARD
            .decode(&self.data)
            .with_context(|| format!("'{renderer}': its {what} image"))?;
        ensure!(
            self.width > 0 && self.height > 0 && bytes.len() == self.width * self.height * texel,
            "'{renderer}': its {what} image of {} x {} has {} bytes",
            self.width,
            self.height,
            bytes.len()
        );
        Ok(bytes)
    }

    /// The image as the bake reads it; one texel where all its texels are the same.
    fn texture(&self, texels: Vec<[f32; 4]>) -> Texture {
        if texels
            .iter()
            .all(|t| t.map(f32::to_bits) == texels[0].map(f32::to_bits))
        {
            Texture::uniform(texels[0])
        } else {
            Texture {
                width: self.width,
                height: self.height,
                texels,
            }
        }
    }

    /// An albedo image: each level's linear value ([`levels::albedo`]), and 1 or 0 for a texel
    /// that is drawn or not.
    fn albedo(&self, renderer: &str) -> Result<Texture> {
        let bytes = self.bytes("albedo", renderer, 4)?;
        Ok(self.texture(
            bytes
                .chunks_exact(4)
                .map(|t| {
                    [
                        levels::albedo(t[0]),
                        levels::albedo(t[1]),
                        levels::albedo(t[2]),
                        t[3] as f32 / 255.0,
                    ]
                })
                .collect(),
        ))
    }

    fn emissive(&self, renderer: &str) -> Result<Texture> {
        let bytes = self.bytes("emission", renderer, 16)?;
        Ok(self.texture(
            bytes
                .chunks_exact(16)
                .map(|t| {
                    std::array::from_fn(|k| {
                        f32::from_le_bytes([t[4 * k], t[4 * k + 1], t[4 * k + 2], t[4 * k + 3]])
                    })
                })
                .collect(),
        ))
    }
}

/// A light of the bake.
#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct LightData {
    /// Its name, for the written description.
    name: String,
    /// Its file ID in the scene.
    file_id: i64,
    /// 0 directional, 1 point, 2 spot.
    kind: u8,
    /// 1 mixed, 2 baked.
    mode: u8,
    /// Its colour times its intensity, linear, and the same times its Indirect Multiplier.
    color: [f32; 3],
    indirect_color: [f32; 3],
    position: [f32; 3],
    /// Its rotation (x, y, z, w).
    rotation: [f32; 4],
    range: f32,
    /// Spot Angle, in degrees.
    spot_angle: f32,
    casts_shadows: bool,
}

impl LightData {
    fn light(&self) -> Result<Light> {
        let [x, y, z, w] = self.rotation;
        Ok(Light {
            kind: match self.kind {
                0 => Kind::Directional,
                1 => Kind::Point,
                2 => Kind::Spot,
                other => anyhow::bail!("'{}': a light of kind {other}", self.name),
            },
            mode: match self.mode {
                1 => Mode::Mixed,
                2 => Mode::Baked,
                other => anyhow::bail!("'{}': a light of mode {other}", self.name),
            },
            color: self.color,
            indirect_color: self.indirect_color,
            position: self.position,
            // Its rotation's forward axis.
            forward: [
                2.0 * (x * z + w * y),
                2.0 * (y * z - w * x),
                1.0 - 2.0 * (x * x + y * y),
            ],
            // And its right axis.
            right: [
                1.0 - 2.0 * (y * y + z * z),
                2.0 * (x * y + w * z),
                2.0 * (x * z - w * y),
            ],
            range: self.range,
            cone_angle: self.spot_angle.to_radians(),
            casts_shadows: self.casts_shadows,
        })
    }
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
    /// Per submesh whether the renderer has a material for it (a submesh past the list has
    /// none). Without the list every submesh has one.
    #[serde(default)]
    has_material: Option<Vec<bool>>,
    scale_in_lightmap: f32,
    /// The most detailed LOD level it is in, if it is in a LOD Group.
    lod_level: Option<u32>,
    /// It contributes to global illumination but is lit by light probes.
    #[serde(default)]
    probe_lit: bool,
    /// Cast Shadows is not Off, and Receive Shadows.
    #[serde(default = "yes")]
    casts_shadows: bool,
    #[serde(default = "yes")]
    receives_shadows: bool,
    /// Stitch Seams (MeshRenderer.stitchLightmapSeams).
    #[serde(default)]
    stitch_seams: bool,
    parameters: Parameters,
}

impl Renderer {
    /// Whether rays meet the submesh: one without a material is not there for them, for a
    /// shadow, a bounce or the sky (matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/bakedbuffers `Spare`).
    fn stops_rays(&self, submesh: usize) -> bool {
        match &self.has_material {
            Some(has) => has.get(submesh).copied().unwrap_or(false),
            None => true,
        }
    }
}

fn yes() -> bool {
    true
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

/// The files of a bake's texel buffers in `folder`: `buffers.json` (each lightmap's size,
/// samples per texel side and renderers, by their number in the bake), and per lightmap
/// `occupancy<N>.u8` (how many of each texel's samples have a surface: none or all of them),
/// `chartIndex<N>.i32` (each texel's chart), `normal<N>.f32` (per sample the world normal and
/// the sample's number, or [`gbuffer::EMPTY`] without a surface), `irradianceDirect<N>.f32`
/// (per texel the direct light's red, green and blue and 1, zeros for an unused texel) and,
/// where the indirect light is baked, `irradianceIndirect<N>.f32` (the same of the indirect
/// light), `irradianceEnvironment<N>.f32` (the same of the sky's light straight on the texel)
/// and `validity<N>.f32` (per texel the part of its rays that met a back face); `lightmap<N>.f32`
/// (the lightmap the bake makes of them, [`composite::composite`]: per texel red, green, blue
/// and 1 where it holds light); and of a bake
/// with a sky `sky.f32` and, where directions are picked by its brightness, `skyDirections.f32`.
struct Buffers<'a> {
    /// Where the texel buffers go; none when only the lightmaps' images are asked for.
    folder: Option<&'a Path>,
    lightmaps: Vec<Json>,
    /// The lightmaps' images (EXR, base64), when asked for, and their directions' and
    /// shadowmasks' (PNG, base64) where the bake makes them.
    images: Option<Vec<String>>,
    directions: Vec<String>,
    shadowmasks: Vec<String>,
}

impl<'a> Buffers<'a> {
    const DESCRIPTION: &'static str = "buffers.json";
    /// The sky's faces (+x, -x, +y, -y, +z, -z; three floats a texel, rows from a face's top).
    const SKY: &'static str = "sky.f32";
    /// The directions picked by the sky's brightness: three floats each, then their light.
    const SKY_DIRECTIONS: &'static str = "skyDirections.f32";
    /// The light probes' direct and indirect light (per probe nine coefficients of three
    /// floats) and per probe the part of its paths that met a back face first.
    const PROBES: [&'static str; 3] = [
        "probesDirect.f32",
        "probesIndirect.f32",
        "probesValidity.f32",
    ];
    const PER_LIGHTMAP: [(&'static str, &'static str); 11] = [
        ("occupancy", ".u8"),
        ("chartIndex", ".i32"),
        ("normal", ".f32"),
        ("irradianceDirect", ".f32"),
        ("irradianceIndirect", ".f32"),
        ("irradianceEnvironment", ".f32"),
        ("validity", ".f32"),
        ("directionalityDirect", ".f32"),
        ("directionalityIndirect", ".f32"),
        ("shadowmask", ".f32"),
        ("lightmap", ".f32"),
    ];

    /// Starts a bake's buffers in the folder, without those an earlier bake left there.
    fn start(folder: Option<&'a Path>, images: bool) -> Result<Buffers<'a>> {
        let images = images.then(Vec::new);
        let Some(folder) = folder else {
            return Ok(Buffers {
                folder: None,
                lightmaps: Vec::new(),
                images,
                directions: Vec::new(),
                shadowmasks: Vec::new(),
            });
        };
        std::fs::create_dir_all(folder)
            .with_context(|| format!("creating {}", folder.display()))?;
        for entry in
            std::fs::read_dir(folder).with_context(|| format!("reading {}", folder.display()))?
        {
            let entry = entry?;
            let name = entry.file_name();
            let name = name.to_string_lossy();
            let ours = name == Self::DESCRIPTION
                || name == Self::SKY
                || name == Self::SKY_DIRECTIONS
                || Self::PROBES.contains(&name.as_ref())
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
            folder: Some(folder),
            lightmaps: Vec::new(),
            images,
            directions: Vec::new(),
            shadowmasks: Vec::new(),
        })
    }

    fn write(&self, name: String, bytes: Vec<u8>) -> Result<()> {
        let Some(folder) = self.folder else {
            return Ok(());
        };
        let path = folder.join(name);
        std::fs::write(&path, bytes).with_context(|| format!("writing {}", path.display()))
    }

    /// The next lightmap: its texels, its renderers by their number in the bake, its texels'
    /// direct light and, where it is baked, their indirect light.
    fn lightmap(
        &mut self,
        g: &gbuffer::GBuffer,
        members: &[usize],
        renderers: &[&Renderer],
        lit: &direct::Direct,
        bounced: Option<&indirect::Indirect>,
        directional: bool,
        backface_tolerance: f32,
        padding: u32,
        stitched: &[(&[EdgePair], [f32; 4])],
        shadowmask: Option<bool>,
    ) -> Result<()> {
        let index = self.lightmaps.len();
        self.write(format!("occupancy{index}.u8"), g.occupancy.clone())?;
        self.write(
            format!("chartIndex{index}.i32"),
            g.charts.iter().flat_map(|c| c.to_le_bytes()).collect(),
        )?;
        let matrices: Vec<[[f32; 3]; 3]> = members
            .iter()
            .map(|&k| direct::normal_matrix(&renderers[k].local_to_world))
            .collect();
        let normals = g.normals.iter().zip(&g.positions).flat_map(|(n, p)| {
            let local = [n[0], n[1], n[2]];
            let world = if p[3] < 0.0 {
                local
            } else {
                direct::world_normal(&matrices[p[3] as usize], local)
            };
            [world[0], world[1], world[2], n[3]]
        });
        self.write(
            format!("normal{index}.f32"),
            normals.flat_map(|v| v.to_le_bytes()).collect(),
        )?;
        self.write(
            format!("irradianceDirect{index}.f32"),
            lit.irradiance
                .iter()
                .flatten()
                .flat_map(|v| v.to_le_bytes())
                .collect(),
        )?;
        // Where the light comes from, in a directional bake; elsewhere the value of no light.
        let texels = g.width * g.height;
        let toward = |values: Option<&[[f32; 4]]>| -> Vec<u8> {
            match values.filter(|_| directional) {
                Some(values) => values
                    .iter()
                    .flatten()
                    .flat_map(|v| v.to_le_bytes())
                    .collect(),
                None => std::iter::repeat_n(directional::NONE, texels)
                    .flatten()
                    .flat_map(|v| v.to_le_bytes())
                    .collect(),
            }
        };
        self.write(
            format!("shadowmask{index}.f32"),
            lit.shadowmask
                .iter()
                .flatten()
                .flat_map(|v| v.to_le_bytes())
                .collect(),
        )?;
        self.write(
            format!("directionalityDirect{index}.f32"),
            toward(Some(&lit.directionality)),
        )?;
        self.write(
            format!("directionalityIndirect{index}.f32"),
            toward(bounced.map(|b| b.directionality.as_slice())),
        )?;
        if let Some(bounced) = bounced {
            self.write(
                format!("irradianceIndirect{index}.f32"),
                bounced
                    .irradiance
                    .iter()
                    .flatten()
                    .flat_map(|v| v.to_le_bytes())
                    .collect(),
            )?;
            self.write(
                format!("irradianceEnvironment{index}.f32"),
                bounced
                    .environment
                    .iter()
                    .flatten()
                    .flat_map(|v| v.to_le_bytes())
                    .collect(),
            )?;
            self.write(
                format!("validity{index}.f32"),
                bounced
                    .validity
                    .iter()
                    .flat_map(|v| v.to_le_bytes())
                    .collect(),
            )?;
        }
        // The lightmap the bake makes of them.
        let used: Vec<bool> = g.occupancy.iter().map(|&o| o > 0).collect();
        let mut lightmap = composite::composite(
            &composite::Buffers {
                width: g.width,
                height: g.height,
                used: &used,
                direct: &lit.irradiance,
                indirect: bounced.map(|b| b.irradiance.as_slice()),
                environment: bounced.map(|b| b.environment.as_slice()),
                validity: bounced.map(|b| b.validity.as_slice()),
            },
            backface_tolerance,
            padding,
        );
        // Then the seams of the renderers that stitch them: red, green and blue, as high as
        // they go.
        for (pairs, scale_offset) in stitched {
            seams::stitch(
                &mut lightmap,
                g.width,
                g.height,
                pairs,
                *scale_offset,
                3,
                f32::MAX,
            );
        }
        if let Some(files) = &mut self.images {
            files.push(
                base64::engine::general_purpose::STANDARD
                    .encode(lightmap_image(g.width, g.height, &lightmap)),
            );
            let made = images::Buffers {
                width: g.width,
                height: g.height,
                used: &used,
                validity: bounced.map(|b| b.validity.as_slice()),
                direct: &lit.irradiance,
                indirect: bounced.map(|b| b.irradiance.as_slice()),
                environment: bounced.map(|b| b.environment.as_slice()),
            };
            let png = |image: &[[f32; 4]]| {
                base64::engine::general_purpose::STANDARD.encode(unbound_assets::png_writer::write(
                    g.width,
                    g.height,
                    &images::to_rgba8(image, g.width, g.height),
                ))
            };
            // The directions (stitched with all four values within [0, 1]) and the shadowmask
            // (red, green and blue, as high as they go, where the project stitches it).
            if directional {
                let mut image = images::directional(
                    &made,
                    &lit.directionality,
                    bounced.map(|b| b.directionality.as_slice()),
                    backface_tolerance,
                    padding,
                );
                for (pairs, scale_offset) in stitched {
                    seams::stitch(&mut image, g.width, g.height, pairs, *scale_offset, 4, 1.0);
                }
                self.directions.push(png(&image));
            }
            if let Some(stitching) = shadowmask {
                let mut image =
                    images::shadowmask(&made, &lit.shadowmask, backface_tolerance, padding);
                if stitching {
                    for (pairs, scale_offset) in stitched {
                        seams::stitch(
                            &mut image,
                            g.width,
                            g.height,
                            pairs,
                            *scale_offset,
                            3,
                            f32::MAX,
                        );
                    }
                }
                self.shadowmasks.push(png(&image));
            }
        }
        self.write(
            format!("lightmap{index}.f32"),
            lightmap
                .iter()
                .flatten()
                .flat_map(|v| v.to_le_bytes())
                .collect(),
        )?;
        self.lightmaps.push(json!({
            "width": g.width, "height": g.height, "supersampling": g.supersampling, "instances": members,
        }));
        Ok(())
    }

    /// Ends the bake's buffers with their description: the lightmaps, the Lighting Mode of
    /// mixed lights, the bake's lights in its order and, where the indirect light is baked, its
    /// settings and each renderer's material images as the bake read them (where they are
    /// written); and gives the lightmaps' images.
    fn finish(
        self,
        mixed_mode: u8,
        lights: Json,
        indirect: Json,
        probes: Json,
    ) -> Result<Option<(Vec<String>, Vec<String>, Vec<String>)>> {
        self.write(
            Self::DESCRIPTION.into(),
            serde_json::to_vec_pretty(&json!({
                "lightmaps": self.lightmaps, "mixedMode": mixed_mode, "lights": lights, "indirect": indirect,
                "probes": probes,
            }))?,
        )?;
        Ok(self.images.map(|i| (i, self.directions, self.shadowmasks)))
    }
}

/// What the indirect light of a bake is made with.
struct Bounce {
    samples: Samples,
    surfaces: Vec<Surface>,
    materials: Vec<Material>,
    scene: Scene,
    /// Indirect Samples, Max Bounces, Min Bounces.
    settings: (u32, u32, u32),
    /// The sky and Environment Samples.
    sky: Option<(Sky, u32)>,
}

/// An image for the buffers' description: its size and its texels' four numbers, rows from the
/// first.
fn described_image(image: &Texture) -> Json {
    json!({ "width": image.width, "height": image.height, "texels": image.texels })
}

/// The size of a renderer's material images: the whole texels of its rectangle in its
/// lightmap, at least one each way (tests/projects/bakedbuffers materials). A renderer
/// without a lightmap takes its rectangle as the layout sized it (not measured where the layout
/// shrinks rectangles to fit).
fn image_size(
    instance: &atlas::Instance,
    placement: &atlas::Placement,
    layout: &atlas::Layout,
) -> [usize; 2] {
    let rectangle = match layout.lightmaps.get(placement.lightmap as usize) {
        Some(size) if placement.lightmap != atlas::NO_LIGHTMAP => [
            instance.uv_bounds[2] * placement.scale_offset[0] * size[0] as f32,
            instance.uv_bounds[3] * placement.scale_offset[1] * size[1] as f32,
        ],
        _ => instance.rect,
    };
    // A side that is a whole number of texels but for rounding keeps them all.
    rectangle.map(|side| ((side + IMAGE_SIDE_SLACK).floor().max(1.0)) as usize)
}

/// How far below a whole number of texels a rectangle's side may be computed and still count
/// as that many (a side of 12 texels comes out of its scale and the lightmap's size as
/// 11.99999: tests/projects/bakedbuffers `Textured`, the cube).
const IMAGE_SIDE_SLACK: f32 = 1e-3;

/// The bake: each lightmap's size, each renderer's lightmap and scale and offset in the order
/// sent, with the size of its material images and what brings its lightmap UVs to their 0..1
/// (`null` for a renderer the bake leaves out, with `invalidNormals` set: one whose mesh
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
    if let Some(data) = &request.indirect {
        ensure!(
            data.materials.len() == request.renderers.len(),
            "{} material images for {} renderers",
            data.materials.len(),
            request.renderers.len()
        );
    }

    let mut images = None;
    let mut light_outputs = Json::Null;
    // The light probes as the lighting data keeps them (where the indirect light is baked).
    let mut light_probes = Json::Null;
    if !request.layout_only && (buffers.is_some() || request.lightmap_images) {
        // Each lightmap's renderers in the bake's order, their meshes made ready once. The
        // indirect light's rays meet every renderer of the bake, with or without a lightmap.
        let mut ready: Vec<Option<gbuffer::Mesh>> = meshes.iter().map(|_| None).collect();
        for (&i, placement) in taken.iter().zip(&layout.placements) {
            let r = &request.renderers[i];
            let wanted = placement.lightmap != atlas::NO_LIGHTMAP || request.indirect.is_some();
            if wanted && ready[r.mesh].is_none() {
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
        // The bake's lights in its order (as its renderers: by their file IDs' hashes), and
        // what stops their light: every renderer of the bake that casts shadows.
        let mut lights: Vec<&LightData> = request.lights.iter().collect();
        lights.sort_by_key(|l| atlas::object_key(l.file_id, 0));
        let mut baked_lights = lights
            .iter()
            .map(|l| l.light())
            .collect::<Result<Vec<Light>>>()?;
        let mixed = match request.mixed_mode {
            0 => MixedMode::IndirectOnly,
            1 => MixedMode::Subtractive,
            2 => MixedMode::Shadowmask,
            other => anyhow::bail!("a Lighting Mode of {other}"),
        };
        // The mixed lights' shadowmask channels; in the Shadowmask Lighting Mode a mixed light
        // left without one is baked instead.
        let channels = shadowmask::assign(&mut baked_lights, mixed == MixedMode::Shadowmask);
        // What the bake did with each light, as its lighting data keeps it (Light.bakingOutput),
        // by the lights' file IDs (tests/projects/bakedbuffers lightingData.json): mixed or
        // baked as baked, its shadowmask channel, its place in the bake's order, and the
        // Lighting Mode (a mixed light without shadows: Baked Indirect, seen in Shadowmask).
        let mut outputs: Vec<(i64, Json)> = lights
            .iter()
            .zip(&baked_lights)
            .zip(&channels)
            .enumerate()
            .map(|(index, ((data, light), channel))| {
                let mixed = light.mode == Mode::Mixed;
                let unshadowed = mixed && !light.casts_shadows;
                let mode = if unshadowed { 0 } else { request.mixed_mode };
                // (Seen in the Shadowmask and Baked Indirect Lighting Modes: bakedbuffers Masks,
                // MasksIndirect, MasksBright; not in Subtractive.)
                let measured = !unshadowed || request.mixed_mode != 1;
                (data.file_id, json!({
                    "fileId": data.file_id, "bakeType": if mixed { 1 } else { 2 }, "mixedMode": mode,
                    "channel": channel.map_or(-1, |c| c as i32), "probeIndex": index,
                    "measured": measured,
                }))
            })
            .collect();
        outputs.sort_by_key(|(id, _)| *id);
        light_outputs = Json::Array(outputs.into_iter().map(|(_, o)| o).collect());
        let baked: Vec<&Renderer> = taken.iter().map(|&i| &request.renderers[i]).collect();
        // The seams of each mesh a renderer stitches, found once per mesh in its own space.
        let mut seams_of: std::collections::HashMap<usize, Vec<EdgePair>> = Default::default();
        let stitches = |r: &Renderer| r.stitch_seams && r.scale_in_lightmap > 0.0 && !r.probe_lit;
        for r in baked.iter().filter(|r| stitches(r)) {
            seams_of.entry(r.mesh).or_insert_with(|| {
                let mesh = &meshes[r.mesh];
                seams::edge_pairs(
                    &mesh.positions,
                    &mesh.normals,
                    mesh.lightmap_uvs(),
                    mesh.submeshes.iter().flatten().copied(),
                )
            });
        }
        let groups = probe_points(&request.probes);
        let points = groups.concat();
        // Which lights a surface of the scene or a probe takes at a bounce: the light grid over
        // the box around the bake's renderers and probes.
        let around = Bounds::around(
            baked
                .iter()
                .flat_map(|r| {
                    let (mesh, m) = (&meshes[r.mesh], r.local_to_world);
                    mesh.positions.iter().map(move |&p| direct::point(&m, p))
                })
                .chain(points.iter().copied()),
        )
        .unwrap_or(Bounds {
            min: [0.0; 3],
            max: [0.0; 3],
        });
        let grid = Grid::new(&baked_lights, around);
        let scene = Scene::new(baked.iter().filter(|r| r.casts_shadows).flat_map(|r| {
            let (mesh, m) = (&meshes[r.mesh], r.local_to_world);
            let world = move |v: u32| direct::point(&m, mesh.positions[v as usize]);
            let count = mesh.positions.len() as u32;
            mesh.submeshes
                .iter()
                .enumerate()
                .filter(|(submesh, _)| r.stops_rays(*submesh))
                .flat_map(|(_, triangles)| triangles)
                .filter(move |t| t.iter().all(|&v| v < count))
                .map(move |t| t.map(world))
        }));
        // What the indirect light's paths run through: every triangle of the bake's renderers,
        // with its renderer's material images.
        let bounce = request
            .indirect
            .as_ref()
            .map(|data| -> Result<Bounce> {
                let blue_noise = std::fs::read(&data.blue_noise).with_context(|| {
                    format!(
                        "reading the blue noise data {} of the Unity installation",
                        data.blue_noise
                    )
                })?;
                let samples = Samples::new(&blue_noise).map_err(|size| {
                    anyhow::anyhow!(
                        "the blue noise data {} has {} bytes",
                        data.blue_noise,
                        size.0
                    )
                })?;
                let mut surfaces = Vec::new();
                let mut materials = Vec::new();
                for (&i, r) in taken.iter().zip(&baked) {
                    let mesh = ready[r.mesh].as_ref().expect("made above");
                    let images = &data.materials[i];
                    let m = &r.local_to_world;
                    let rows = direct::normal_matrix(m);
                    let determinant = m[0] * (m[5] * m[10] - m[6] * m[9])
                        - m[4] * (m[1] * m[10] - m[2] * m[9])
                        + m[8] * (m[1] * m[6] - m[2] * m[5]);
                    for (submesh, triangles) in mesh.submeshes.iter().enumerate() {
                        if !r.stops_rays(submesh) {
                            continue;
                        }
                        let double_sided =
                            images.double_sided.get(submesh).copied().unwrap_or(false);
                        surfaces.extend(triangles.iter().map(|t| Surface {
                            material: materials.len() as u32,
                            mirrored: determinant < 0.0,
                            double_sided,
                            corners: t.map(|v| direct::point(m, mesh.positions[v as usize])),
                            normals:
                                t.map(|v| direct::world_normal(&rows, mesh.normals[v as usize])),
                            uvs: t.map(|v| mesh.uvs[v as usize]),
                        }));
                    }
                    materials.push(Material {
                        albedo: images.albedo.albedo(&r.name)?,
                        emissive: images.emissive.emissive(&r.name)?,
                    });
                }
                let scene = Scene::new(surfaces.iter().map(|s| s.corners));
                Ok(Bounce {
                    samples,
                    surfaces,
                    materials,
                    scene,
                    settings: (data.samples, data.max_bounces, data.min_bounces),
                    sky: data
                        .environment
                        .as_ref()
                        .map(|e| e.sky().map(|sky| (sky, e.samples)))
                        .transpose()?,
                })
            })
            .transpose()?;
        // One lightmap at a time: its texels are written and let go before the next.
        let folder = buffers.map(|f| f.join(&request.scene));
        let mut written = Buffers::start(folder.as_deref(), request.lightmap_images)?;
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
            let receivers: Vec<Receiver> = members
                .iter()
                .map(|&k| Receiver {
                    local_to_world: baked[k].local_to_world,
                    receives_shadows: baked[k].receives_shadows,
                })
                .collect();
            // The renderers of a lightmap share their Lightmap Parameters.
            let parameters = members
                .first()
                .map_or(atlas::Parameters::DEFAULT, |&k| instances[k].parameters);
            let g = gbuffer::gbuffer(
                size[0] as usize,
                size[1] as usize,
                parameters.supersampling as usize,
                &placed,
            );
            let lit = direct::direct(
                &g,
                &receivers,
                &baked_lights,
                &channels,
                mixed,
                &scene,
                parameters.pushoff,
            );
            let bounced = bounce.as_ref().map(|b| {
                indirect::indirect(
                    &g,
                    &receivers,
                    &World {
                        scene: &b.scene,
                        casters: &scene,
                        surfaces: &b.surfaces,
                        materials: &b.materials,
                        lights: &baked_lights,
                        grid: &grid,
                        sky: b.sky.as_ref().map(|s| &s.0),
                    },
                    &b.samples,
                    indirect::Settings {
                        samples: b.settings.0,
                        max_bounces: b.settings.1,
                        min_bounces: b.settings.2,
                        pushoff: parameters.pushoff,
                        environment_samples: b.sky.as_ref().map_or(0, |s| s.1),
                    },
                )
            });
            // The renderers whose seams are stitched in this lightmap, in the bake's order (the
            // Editor stitches them in parallel; where two read the same texels the result is
            // not measured).
            let stitched: Vec<(&[EdgePair], [f32; 4])> = members
                .iter()
                .filter(|&&k| stitches(baked[k]))
                .map(|&k| {
                    (
                        seams_of[&baked[k].mesh].as_slice(),
                        layout.placements[k].scale_offset,
                    )
                })
                .collect();
            written.lightmap(
                &g,
                &members,
                &baked,
                &lit,
                bounced.as_ref(),
                request.directional,
                parameters.back_face_tolerance,
                request.padding.max(0) as u32,
                &stitched,
                // (The Shadowmask Lighting Mode with a light on a shadowmask channel.)
                (mixed == MixedMode::Shadowmask && channels.iter().any(|c| c.is_some()))
                    .then_some(request.shadowmask_stitching),
            )?;
        }
        let described = bounce.as_ref().map_or(Json::Null, |b| {
            json!({
                "samples": b.settings.0, "maxBounces": b.settings.1, "minBounces": b.settings.2,
                // Each renderer's images in the bake's order, as the bake reads them.
                "materials": b.materials.iter().map(|m| json!({
                    "albedo": described_image(&m.albedo), "emissive": described_image(&m.emissive),
                })).collect::<Vec<_>>(),
                // The sky: its faces are in `sky.f32`, the directions picked by its brightness
                // and their light in `skyDirections.f32`.
                "sky": b.sky.as_ref().map(|(sky, samples)| json!({
                    "samples": samples, "side": sky.faces[0].width,
                    "directions": sky.importance.as_ref().map_or(0, |i| i.directions.len()),
                    "brightness": sky.importance.as_ref().map_or(0.0, |i| i.integral),
                })),
            })
        });
        if let Some((sky, _)) = bounce.as_ref().and_then(|b| b.sky.as_ref()) {
            written.write(
                Buffers::SKY.into(),
                sky.faces
                    .iter()
                    .flat_map(|face| face.texels.iter())
                    .flat_map(|t| [t[0], t[1], t[2]])
                    .flat_map(f32::to_le_bytes)
                    .collect(),
            )?;
            if let Some(importance) = &sky.importance {
                written.write(
                    Buffers::SKY_DIRECTIONS.into(),
                    importance
                        .directions
                        .iter()
                        .zip(&importance.weighted)
                        .flat_map(|(d, w)| [d[0], d[1], d[2], w[0], w[1], w[2]])
                        .flat_map(f32::to_le_bytes)
                        .collect(),
                )?;
            }
        }
        // The light probes: their direct light and, where the indirect light is
        // baked, the light from other surfaces and the sky.
        let mut described_probes = Json::Null;
        if !points.is_empty() {
            let flat = |coefficients: &[probes::Coefficients]| -> Vec<u8> {
                coefficients
                    .iter()
                    .flatten()
                    .flatten()
                    .flat_map(|v| v.to_le_bytes())
                    .collect()
            };
            let lit = probes::direct(&points, &baked_lights, &grid, &scene);
            written.write(Buffers::PROBES[0].into(), flat(&lit))?;
            let mut counts = Json::Null;
            if let Some(b) = &bounce {
                let settings = indirect::Settings {
                    samples: probe_samples(b.settings.0),
                    max_bounces: b.settings.1,
                    min_bounces: b.settings.2,
                    pushoff: PROBE_PUSHOFF,
                    environment_samples: probe_samples(b.sky.as_ref().map_or(0, |s| s.1)),
                };
                let world = World {
                    scene: &b.scene,
                    casters: &scene,
                    surfaces: &b.surfaces,
                    materials: &b.materials,
                    lights: &baked_lights,
                    grid: &grid,
                    sky: b.sky.as_ref().map(|s| &s.0),
                };
                let probed = indirect::probes(
                    groups.iter().map(Vec::as_slice),
                    &world,
                    &b.samples,
                    settings,
                );
                written.write(Buffers::PROBES[1].into(), flat(&probed.radiance))?;
                written.write(
                    Buffers::PROBES[2].into(),
                    probed
                        .validity
                        .iter()
                        .flat_map(|v| v.to_le_bytes())
                        .collect(),
                )?;
                counts = json!({ "indirect": settings.samples, "environment": settings.environment_samples });
                light_probes = light_probes_data(
                    &groups,
                    &probe_groups(&request.probes),
                    &lit,
                    &probed.radiance,
                    (mixed != MixedMode::IndirectOnly).then(|| {
                        light_probes::occlusion(&points, &baked_lights, &channels, &scene)
                    }),
                    light_probes::occluding_lights(&baked_lights, &channels).len(),
                );
            }
            described_probes =
                json!({ "count": points.len(), "points": points, "samples": counts });
        }
        images = written.finish(
            request.mixed_mode,
            json!(
                lights
                    .iter()
                    .zip(&baked_lights)
                    .zip(&channels)
                    .map(|((data, light), channel)| json!({
                        "name": data.name,
                        "type": data.kind,
                        // As baked: a mixed light without a shadowmask channel is baked in the
                        // Shadowmask Lighting Mode.
                        "mode": match light.mode { Mode::Mixed => 1, Mode::Baked => 2 },
                        "shadowMaskChannel": channel.map_or(-1, |c| c as i32),
                        "color": light.color,
                        "indirectColor": light.indirect_color,
                        "position": light.position,
                        "orientation": data.rotation,
                        // As a bake input's lights are described (the probe's lights.json): no range
                        // and a cone of 1 for a directional light, no cone for a point light.
                        "range": if light.kind == Kind::Directional { 0.0 } else { light.range },
                        "coneAngle": match light.kind {
                            Kind::Directional => 1.0,
                            Kind::Point => 0.0,
                            Kind::Spot => light.cone_angle,
                        },
                        "castsShadows": light.casts_shadows,
                    }))
                    .collect::<Vec<_>>()
            ),
            described,
            described_probes,
        )?;
    }

    let mut placements = vec![Json::Null; request.renderers.len()];
    for ((&i, p), instance) in taken.iter().zip(&layout.placements).zip(&instances) {
        // One with Scale In Lightmap 0 is packed like any other, without a lightmap of its own.
        let lightmap = if request.renderers[i].scale_in_lightmap > 0.0 {
            p.lightmap
        } else {
            atlas::NO_LIGHTMAP
        };
        placements[i] = json!({
            "lightmap": lightmap, "scaleOffset": p.scale_offset,
            "image": image_size(instance, p, &layout),
            "imageScaleOffset": gbuffer::uv_scale_offset(instance.uv_bounds),
        });
    }
    Ok(json!({
        "lightmaps": layout.lightmaps,
        "placements": placements,
        "order": taken,
        "invalidNormals": taken.len() != request.renderers.len(),
        "images": images.as_ref().map(|i| &i.0),
        "directionImages": images.as_ref().map(|i| &i.1),
        "shadowmaskImages": images.as_ref().map(|i| &i.2),
        "lights": light_outputs,
        "lightProbes": light_probes,
    }))
}

/// The light probes of a bake as its lighting data keeps them (the LightProbes object): their
/// points, each group's set (its place among them, its size and its hash), each probe's
/// coefficients (of its direct light `lit` and its indirect light `bounced`; each group's
/// Remove Ringing), the probes' occlusion entries where the Lighting Mode has them; and what
/// of it is not measured: probes within 5 cm of each other, a group without Remove Ringing,
/// more than one mixed light in the occlusion.
fn light_probes_data(
    points: &[Vec<[f32; 3]>],
    groups: &[&ProbeGroupData],
    lit: &[probes::Coefficients],
    bounced: &[probes::Coefficients],
    occlusion: Option<Vec<light_probes::Occlusion>>,
    occluding: usize,
) -> Json {
    let mut sets = Vec::new();
    let mut coefficients = Vec::new();
    let mut offset = 0;
    for (set, group) in points.iter().zip(groups) {
        sets.push(json!({
            "offset": offset, "size": set.len(),
            "hash": light_probes::set_hash(set),
        }));
        for p in offset..offset + set.len() {
            coefficients.extend(light_probes::coefficients(
                &lit[p],
                &bounced[p],
                group.dering,
            ));
        }
        offset += set.len();
    }
    let all = points.concat();
    let unmeasured = if light_probes::has_duplicates(&all) {
        Some("light probes within 5 cm of each other")
    } else if groups.iter().any(|g| !g.dering) {
        Some("a Light Probe Group without Remove Ringing")
    } else if occlusion.is_some() && occluding > 1 {
        Some("the probes' occlusion of more than one mixed light")
    } else {
        None
    };
    json!({
        "positions": all,
        "sets": sets,
        "coefficients": coefficients,
        "occlusion": occlusion.unwrap_or_default().iter().map(|o| json!({
            "lights": o.lights, "channels": o.channels, "occlusion": o.occlusion,
        })).collect::<Vec<_>>(),
        "unmeasured": unmeasured,
    })
}

/// A lightmap's image as the Editor writes it (Lightmap-N_comp_light.exr): red, green, blue and
/// alpha as 32-bit floats, ZIP-compressed, rows from the top (the lightmap's last row first).
fn lightmap_image(width: usize, height: usize, lightmap: &[[f32; 4]]) -> Vec<u8> {
    use unbound_assets::exr_writer::{Compression, Image, Samples, write};
    let plane = |c: usize| -> Vec<f32> {
        (0..height)
            .rev()
            .flat_map(|y| (0..width).map(move |x| (y, x)))
            .map(|(y, x)| lightmap[y * width + x][c])
            .collect()
    };
    let (r, g, b, a) = (plane(0), plane(1), plane(2), plane(3));
    write(
        &Image {
            width,
            height,
            channels: &[
                ("A", Samples::Float(&a)),
                ("B", Samples::Float(&b)),
                ("G", Samples::Float(&g)),
                ("R", Samples::Float(&r)),
            ],
            screen_window_width: width as f32,
        },
        Compression::Zip,
    )
}

/// The image of a bake's default reflection, as the Editor writes it beside the scene
/// (ReflectionProbe-0.exr): the skybox's faces (as [`DefaultReflectionRequest`] gives them) side
/// by side, +x to -z, red, green and blue as half floats.
pub fn reflection_image(request: DefaultReflectionRequest) -> Result<Json> {
    let size = request.size as usize;
    let floats = faces_of(&request)?;
    let mut planes = [
        vec![0u16; 6 * size * size],
        vec![0u16; 6 * size * size],
        vec![0u16; 6 * size * size],
    ];
    for face in 0..6 {
        for y in 0..size {
            for x in 0..size {
                let from = 3 * (face * size * size + y * size + x);
                let to = y * 6 * size + face * size + x;
                // (OpenEXR stores the channels by name: blue, green, red.)
                for (plane, c) in planes.iter_mut().zip([2, 1, 0]) {
                    plane[to] = half::f16::from_f32(floats[from + c]).to_bits();
                }
            }
        }
    }
    use unbound_assets::exr_writer::Samples;
    let image = unbound_assets::exr_writer::write(
        &unbound_assets::exr_writer::Image {
            width: 6 * size,
            height: size,
            channels: &[
                ("B", Samples::Half(&planes[0])),
                ("G", Samples::Half(&planes[1])),
                ("R", Samples::Half(&planes[2])),
            ],
            screen_window_width: 1.0,
        },
        unbound_assets::exr_writer::Compression::Piz,
    );
    Ok(json!({ "image": base64::engine::general_purpose::STANDARD.encode(image) }))
}

/// The lighting data of a bake of the environment alone, as the Editor writes it beside the
/// scene (LightingData.asset; [`unbound_assets::lighting_data`]).
#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LightingDataRequest {
    unity_version: String,
    scene_guid: String,
    lightmaps_mode: i32,
    ambient_probe: Vec<f32>,
    reflection_guid: Option<String>,
    /// A bake with baked GI: its lightmaps' images (their GUIDs), its lightmapped renderers
    /// and its lights (as the bake's reply gives them).
    #[serde(default)]
    lightmaps: Vec<LightmapImagesData>,
    #[serde(default)]
    renderers: Vec<LightmappedRendererData>,
    #[serde(default)]
    lights: Vec<LightOutputData>,
    /// Its light probes: as the bake's reply gives them, with their cells.
    #[serde(default)]
    light_probes: Option<LightProbesData>,
}

/// A bake's light probes as the bake's reply gives them (`lightProbes`), with the cells their
/// points make (inner, then outer) and their hull rays.
#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct LightProbesData {
    positions: Vec<[f32; 3]>,
    sets: Vec<ProbeSetData>,
    /// 27 per probe.
    coefficients: Vec<f32>,
    #[serde(default)]
    occlusion: Vec<ProbeOcclusionData>,
    tetrahedra: Vec<TetrahedronData>,
    hull_rays: Vec<[f32; 3]>,
}

#[derive(serde::Deserialize)]
struct ProbeSetData {
    offset: i32,
    size: i32,
    hash: [u8; 16],
}

#[derive(serde::Deserialize)]
struct ProbeOcclusionData {
    lights: [i32; 4],
    channels: [u8; 4],
    occlusion: [f32; 4],
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct TetrahedronData {
    indices: [i32; 4],
    neighbors: [i32; 4],
    /// Three rows of four.
    matrix: [f32; 12],
    is_valid: bool,
}

/// A lightmap's images by their GUIDs: its light, and its directions or shadowmask if made.
#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct LightmapImagesData {
    light: String,
    #[serde(default)]
    directions: Option<String>,
    #[serde(default)]
    shadowmask: Option<String>,
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct LightmappedRendererData {
    file_id: i64,
    lightmap_index: i32,
    scale_offset: [f32; 4],
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct LightOutputData {
    file_id: i64,
    bake_type: i32,
    mixed_mode: i32,
    channel: i32,
    probe_index: i32,
}

pub fn lighting_data_file(request: LightingDataRequest) -> Result<Json> {
    use unbound_assets::lighting_data;
    let ambient_probe: [f32; 27] = request
        .ambient_probe
        .as_slice()
        .try_into()
        .context("an ambient probe of 27 coefficients")?;
    let baked = lighting_data::Baked {
        lightmaps: request
            .lightmaps
            .iter()
            .map(|images| -> Result<lighting_data::LightmapImages> {
                let guid =
                    |g: &Option<String>| g.as_deref().map(lighting_data::guid_bytes).transpose();
                Ok(lighting_data::LightmapImages {
                    light: lighting_data::guid_bytes(&images.light)?,
                    directions: guid(&images.directions)?,
                    shadowmask: guid(&images.shadowmask)?,
                })
            })
            .collect::<Result<_>>()?,
        renderers: request
            .renderers
            .iter()
            .map(|r| lighting_data::LightmappedRenderer {
                file_id: r.file_id,
                lightmap_index: r.lightmap_index,
                scale_offset: r.scale_offset,
            })
            .collect(),
        lights: request
            .lights
            .iter()
            .map(|l| lighting_data::BakedLight {
                file_id: l.file_id,
                bake_type: l.bake_type,
                mixed_mode: l.mixed_mode,
                occlusion_mask_channel: l.channel,
                probe_occlusion_light_index: l.probe_index,
            })
            .collect(),
        light_probes: request
            .light_probes
            .map(|p| -> Result<lighting_data::LightProbes> {
                ensure!(
                    p.coefficients.len() == 27 * p.positions.len(),
                    "{} coefficients for {} light probes",
                    p.coefficients.len(),
                    p.positions.len()
                );
                Ok(lighting_data::LightProbes {
                    coefficients: p
                        .coefficients
                        .chunks_exact(27)
                        .map(|c| c.try_into().unwrap())
                        .collect(),
                    positions: p.positions,
                    sets: p
                        .sets
                        .iter()
                        .map(|s| lighting_data::ProbeSet {
                            offset: s.offset,
                            size: s.size,
                            hash: s.hash,
                        })
                        .collect(),
                    occlusion: p
                        .occlusion
                        .iter()
                        .map(|o| lighting_data::ProbeOcclusion {
                            lights: o.lights,
                            channels: o.channels,
                            occlusion: o.occlusion,
                        })
                        .collect(),
                    tetrahedra: p
                        .tetrahedra
                        .iter()
                        .map(|t| lighting_data::Tetrahedron {
                            indices: t.indices,
                            neighbors: t.neighbors,
                            matrix: std::array::from_fn(|r| {
                                std::array::from_fn(|c| t.matrix[4 * r + c])
                            }),
                            valid: t.is_valid,
                        })
                        .collect(),
                    hull_rays: p.hull_rays,
                })
            })
            .transpose()?,
    };
    let bytes = lighting_data::baked(
        &lighting_data::Environment {
            unity_version: request.unity_version,
            scene_guid: lighting_data::guid_bytes(&request.scene_guid)?,
            lightmaps_mode: request.lightmaps_mode,
            ambient_probe,
            reflection_guid: request
                .reflection_guid
                .as_deref()
                .map(lighting_data::guid_bytes)
                .transpose()?,
        },
        &baked,
    )?;
    Ok(json!({ "data": base64::engine::general_purpose::STANDARD.encode(bytes) }))
}

/// The faces of a default reflection request as floats (checked against its size).
fn faces_of(request: &DefaultReflectionRequest) -> Result<Vec<f32>> {
    let size = request.size;
    ensure!(
        size.is_power_of_two(),
        "a default reflection of {size} texels a side: its levels halve down to one texel, so its size is a power of two"
    );
    let floats: Vec<f32> = base64::engine::general_purpose::STANDARD
        .decode(&request.faces)?
        .chunks_exact(4)
        .map(|b| f32::from_le_bytes([b[0], b[1], b[2], b[3]]))
        .collect();
    let texels = (size * size) as usize;
    ensure!(
        floats.len() == 6 * texels * 3,
        "the default reflection's faces have {} values for 6 faces of {size} texels a side",
        floats.len()
    );
    Ok(floats)
}

/// What the bake makes of the sky for the default reflection: the skybox drawn into six faces
/// of `size` texels a side (linear RGB floats, Unity's face order, rows from the top, base64).
#[derive(serde::Deserialize)]
pub struct DefaultReflectionRequest {
    size: u32,
    faces: String,
}

/// The default reflection a bake makes of the sky's faces (matches the Unity 6000.0.71f1
/// Editor, tests/projects/bakeenvironment): the faces as the bake stores them (half floats of
/// red, green and blue, alpha 1), glossy-convolved as the texture importer convolves a
/// reflection cube. `{ size, levels, texels }`: `texels` base64 half floats of RGBA, level by
/// level and face by face within each level, rows from the top.
pub fn default_reflection(request: DefaultReflectionRequest) -> Result<Json> {
    let size = request.size;
    let floats = faces_of(&request)?;
    let texels = (size * size) as usize;
    let stored = |v: f32| half::f16::from_f32(v).to_f32();
    let faces: [Vec<f32>; 6] = std::array::from_fn(|face| {
        floats[face * texels * 3..(face + 1) * texels * 3]
            .chunks_exact(3)
            .flat_map(|c| [stored(c[0]), stored(c[1]), stored(c[2]), 1.0])
            .collect()
    });
    let levels = unbound_assets::texture::convolve_cube(size, &faces);
    let bytes: Vec<u8> = levels
        .iter()
        .flatten()
        .flatten()
        .flat_map(|&v| half::f16::from_f32(v).to_le_bytes())
        .collect();
    Ok(json!({
        "size": size,
        "levels": levels.len(),
        "texels": base64::engine::general_purpose::STANDARD.encode(bytes),
    }))
}

#[cfg(test)]
mod tests {
    #[test]
    fn a_lightmaps_image_holds_its_texels_from_the_top_row() {
        // Two by two texels: row 0 (the lightmap's first) dark, row 1 bright.
        let lightmap = [
            [0.1, 0.2, 0.3, 1.0],
            [0.1, 0.2, 0.3, 1.0],
            [5.0, 6.0, 7.0, 0.0],
            [5.0, 6.0, 7.0, 0.0],
        ];
        let image = super::lightmap_image(2, 2, &lightmap);
        // One ZIP chunk after the header and its offset: the lines, each channel (A, B, G, R)
        // as two 32-bit floats; its bytes split and differenced (OpenEXR's predictor).
        let mut at = 8;
        while image[at] != 0 {
            at = image[at..].iter().position(|&b| b == 0).unwrap() + at + 1;
            at = image[at..].iter().position(|&b| b == 0).unwrap() + at + 1;
            let size = i32::from_le_bytes(image[at..at + 4].try_into().unwrap()) as usize;
            at += 4 + size;
        }
        let chunk = u64::from_le_bytes(image[at + 1..at + 9].try_into().unwrap()) as usize;
        let size = i32::from_le_bytes(image[chunk + 4..chunk + 8].try_into().unwrap()) as usize;
        let mut split = Vec::new();
        std::io::Read::read_to_end(
            &mut flate2::read::ZlibDecoder::new(&image[chunk + 8..chunk + 8 + size]),
            &mut split,
        )
        .unwrap();
        for k in 1..split.len() {
            split[k] = split[k].wrapping_add(split[k - 1]).wrapping_sub(128);
        }
        let half = split.len().div_ceil(2);
        let bytes: Vec<u8> = (0..split.len())
            .map(|k| {
                if k % 2 == 0 {
                    split[k / 2]
                } else {
                    split[half + k / 2]
                }
            })
            .collect();
        let floats: Vec<f32> = bytes
            .chunks_exact(4)
            .map(|b| f32::from_le_bytes(b.try_into().unwrap()))
            .collect();
        // The file's first line is the lightmap's last row: A 0 0, B 7 7, G 6 6, R 5 5.
        assert_eq!(&floats[..8], &[0.0, 0.0, 7.0, 7.0, 6.0, 6.0, 5.0, 5.0]);
        assert_eq!(&floats[8..], &[1.0, 1.0, 0.3, 0.3, 0.2, 0.2, 0.1, 0.1]);
    }

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
    fn the_default_reflection_stores_the_sky_as_half_floats_and_convolves_every_level() {
        let size = 4u32;
        let texels = (size * size) as usize;
        // A face of 0.1 (not a half float) and the others of 2.
        let faces: Vec<f32> = (0..6 * texels * 3)
            .map(|i| if i < texels * 3 { 0.1 } else { 2.0 })
            .collect();
        let bytes: Vec<u8> = faces.iter().flat_map(|v| v.to_le_bytes()).collect();
        let encode = |b: &[u8]| base64::engine::general_purpose::STANDARD.encode(b);
        let out = default_reflection(DefaultReflectionRequest {
            size,
            faces: encode(&bytes),
        })
        .unwrap();
        assert_eq!(
            (out["size"].as_u64(), out["levels"].as_u64()),
            (Some(4), Some(3))
        );
        let halves: Vec<f32> = base64::engine::general_purpose::STANDARD
            .decode(out["texels"].as_str().unwrap())
            .unwrap()
            .chunks_exact(2)
            .map(|b| half::f16::from_le_bytes([b[0], b[1]]).to_f32())
            .collect();
        // Levels of 4, 2 and 1 texels a side, six faces of four channels.
        assert_eq!(halves.len(), 6 * 4 * (16 + 4 + 1));
        // The first level is the faces as stored: red, green and blue as half floats, alpha 1.
        assert_eq!(
            &halves[..4],
            &[
                half::f16::from_f32(0.1).to_f32(),
                half::f16::from_f32(0.1).to_f32(),
                half::f16::from_f32(0.1).to_f32(),
                1.0
            ]
        );
        assert_eq!(&halves[texels * 4..texels * 4 + 4], &[2.0, 2.0, 2.0, 1.0]);
        // Faces of another size, and sizes that do not halve down to one texel, are refused.
        let short = default_reflection(DefaultReflectionRequest {
            size: 8,
            faces: encode(&bytes),
        });
        assert!(short.is_err());
        let odd = default_reflection(DefaultReflectionRequest {
            size: 3,
            faces: encode(&vec![0u8; 6 * 9 * 3 * 4]),
        });
        assert!(odd.is_err());
    }

    #[test]
    fn the_light_probes_reply_holds_each_sets_place_and_hash_and_says_what_is_not_measured() {
        let group = |file_id, dering| ProbeGroupData {
            file_id,
            points: Vec::new(),
            dering,
        };
        let (a, b) = (group(1, true), group(2, true));
        let points = vec![vec![[0.0f32; 3], [1.0, 0.0, 0.0]], vec![[0.0, 1.0, 0.0]]];
        let light = vec![probes::Coefficients::default(); 3];
        let reply = light_probes_data(&points, &[&a, &b], &light, &light, None, 0);
        assert_eq!(reply["sets"][1]["offset"], 2);
        assert_eq!(reply["sets"][1]["size"], 1);
        assert_eq!(
            reply["sets"][0]["hash"],
            json!(light_probes::set_hash(&points[0]))
        );
        assert_eq!(reply["coefficients"].as_array().unwrap().len(), 27 * 3);
        assert_eq!(reply["occlusion"], json!([]));
        assert!(reply["unmeasured"].is_null());
        // Probes within 5 cm, a group without Remove Ringing, two mixed lights' occlusion.
        let close = vec![vec![[0.0f32; 3], [0.01, 0.0, 0.0]], vec![[0.0, 1.0, 0.0]]];
        let reply = light_probes_data(&close, &[&a, &b], &light, &light, None, 0);
        assert_eq!(
            reply["unmeasured"],
            "light probes within 5 cm of each other"
        );
        let plain = group(2, false);
        let reply = light_probes_data(&points, &[&a, &plain], &light, &light, None, 0);
        assert_eq!(
            reply["unmeasured"],
            "a Light Probe Group without Remove Ringing"
        );
        let reply = light_probes_data(&points, &[&a, &b], &light, &light, Some(Vec::new()), 2);
        assert_eq!(
            reply["unmeasured"],
            "the probes' occlusion of more than one mixed light"
        );
        // One mixed light's occlusion is measured.
        let reply = light_probes_data(&points, &[&a, &b], &light, &light, Some(Vec::new()), 1);
        assert!(reply["unmeasured"].is_null());
    }

    #[test]
    fn probe_groups_are_baked_in_their_scene_file_order_at_their_cut_points() {
        // tests/projects/bakedbuffers ProbesGrid: the groups by file ID, whatever the order they
        // come in (their order in the hierarchy) or their file IDs' hashes give.
        let group = |file_id, points: &[f32]| ProbeGroupData {
            file_id,
            points: points.to_vec(),
            dering: true,
        };
        let groups = [
            group(862829824, &[0.5, 9.0, 0.2]),
            group(283887366, &[-0.235, 0.4507, 1.0179, 0.0239, 0.294, 0.0079]),
            group(498991141, &[-1.84, 0.929, -0.2449]),
            // A group without probes is no group of the bake.
            group(100, &[]),
        ];
        let points = probe_points(&groups);
        assert_eq!(points.iter().map(Vec::len).collect::<Vec<_>>(), [2, 1, 1]);
        let hundredths: Vec<[f32; 3]> = points
            .iter()
            .flatten()
            .map(|p| p.map(|v| (v * 100.0).round()))
            .collect();
        assert_eq!(
            hundredths,
            [
                [-23.0, 45.0, 101.0],
                [2.0, 29.0, 0.0],
                [-184.0, 92.0, -24.0],
                [50.0, 900.0, 20.0]
            ]
        );
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
    fn a_mixed_light_without_shadows_is_not_measured_in_the_subtractive_mode() {
        for (mode, measured) in [(0u8, true), (1, false), (2, true)] {
            let mut r = request(
                vec![quad_mesh(&UNIT, &BACK)],
                vec![renderer("quad", 1, 0, 1.0, 0.0)],
            );
            r.lightmap_images = true;
            r.mixed_mode = mode;
            r.lights = vec![
                serde_json::from_value(json!({
                    "name": "Sun", "fileId": 5, "kind": 0, "mode": 1, "color": [1, 1, 1],
                    "indirectColor": [1, 1, 1],
                    "position": [0, 0, 0], "rotation": [0, 0, 0, 1], "range": 10.0,
                    "spotAngle": 30.0, "castsShadows": false,
                }))
                .unwrap(),
            ];
            let reply = bake(r, None).unwrap();
            let light = &reply["lights"][0];
            assert_eq!(light["measured"], json!(measured), "mode {mode}");
            assert_eq!(light["mixedMode"], json!(0), "mode {mode}");
            assert_eq!(light["bakeType"], json!(1));
        }
    }

    #[test]
    fn a_bake_asked_for_its_lightmaps_images_answers_one_per_lightmap() {
        let mut r = request(
            vec![quad_mesh(&UNIT, &BACK)],
            vec![renderer("quad", 1, 0, 1.0, 0.0)],
        );
        // Without a buffers folder: the images alone.
        r.lightmap_images = true;
        let reply = bake(r, None).unwrap();
        let images = reply["images"].as_array().unwrap();
        assert_eq!(images.len(), reply["lightmaps"].as_array().unwrap().len());
        let image = base64::engine::general_purpose::STANDARD
            .decode(images[0].as_str().unwrap())
            .unwrap();
        assert_eq!(&image[..4], &[0x76, 0x2f, 0x31, 0x01]);
        // Not asked: none.
        let reply = bake(
            request(
                vec![quad_mesh(&UNIT, &BACK)],
                vec![renderer("quad", 1, 0, 1.0, 0.0)],
            ),
            None,
        )
        .unwrap();
        assert!(reply["images"].is_null());
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

    fn light(name: &str, file_id: i64, kind: u8, mode: u8) -> Json {
        json!({
            "name": name, "fileId": file_id, "kind": kind, "mode": mode,
            "color": [2.0, 1.0, 0.5], "indirectColor": [4.0, 2.0, 1.0],
            // Unturned: it shines along +z, onto the quad that faces -z.
            "position": [0.5, 0.5, -3.0], "rotation": [0, 0, 0, 1],
            "range": 10.0, "spotAngle": 60.0, "castsShadows": true,
        })
    }

    #[test]
    fn the_bakes_lights_and_each_lightmaps_direct_light_are_written() {
        let folder =
            std::env::temp_dir().join(format!("unbound-baked-gi-lights-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&folder);
        let scene = folder.join("10000000200000003000000040000000");
        let bake_with = |lights: Vec<Json>, mixed: u8| {
            let mut r = serde_json::to_value(json!({
                "maxSize": 64, "padding": 2, "resolution": 10.0, "fixedSize": true,
                "scene": "10000000200000003000000040000000", "meshes": [quad_mesh(&UNIT, &BACK)],
                "renderers": [renderer("lit", 1, 0, 1.0, 0.0)], "lights": lights, "mixedMode": mixed,
            }))
            .unwrap();
            r["renderers"][0]["receivesShadows"] = json!(false);
            bake(serde_json::from_value(r).unwrap(), Some(&folder)).map(|_| {
                let direct = std::fs::read(scene.join("irradianceDirect0.f32")).unwrap();
                let texel: Vec<f32> = direct[..16]
                    .chunks_exact(4)
                    .map(|c| f32::from_le_bytes(c.try_into().unwrap()))
                    .collect();
                let description: Json =
                    serde_json::from_slice(&std::fs::read(scene.join("buffers.json")).unwrap())
                        .unwrap();
                (texel, direct.len(), description["lights"].clone())
            })
        };
        // A baked sun and a mixed one (Baked Indirect: its direct light is not baked), in the
        // bake's order: by the hashes of their file IDs (two of tests/projects/bakedbuffers).
        let (texel, length, lights) = bake_with(
            vec![
                light("mixed", 244122856, 0, 1),
                light("baked", 879057353, 0, 2),
            ],
            0,
        )
        .unwrap();
        assert_eq!((texel, length), (vec![2.0, 1.0, 0.5, 1.0], 64 * 64 * 16));
        assert_eq!(lights[0]["name"], json!("baked"));
        assert_eq!(
            (
                &lights[1]["name"],
                &lights[1]["mode"],
                &lights[1]["indirectColor"]
            ),
            (&json!("mixed"), &json!(1), &json!([4.0, 2.0, 1.0]))
        );
        // A directional light is described without a range and with a cone of 1.
        assert_eq!(
            (
                &lights[0]["range"],
                &lights[0]["coneAngle"],
                &lights[0]["type"]
            ),
            (&json!(0.0), &json!(1.0), &json!(0))
        );
        // Subtractive: both suns' direct light.
        assert_eq!(
            bake_with(vec![light("mixed", 1, 0, 1), light("baked", 2, 0, 2)], 1)
                .unwrap()
                .0,
            [4.0, 2.0, 1.0, 1.0]
        );
        // A point light is described with its range and no cone, a spot light with its cone
        // in radians.
        let lights = bake_with(vec![light("spot", 1, 2, 2)], 2).unwrap().2;
        assert_eq!(lights[0]["range"], json!(10.0));
        assert!(
            (lights[0]["coneAngle"].as_f64().unwrap() - std::f64::consts::FRAC_PI_3).abs() < 1e-6
        );
        assert_eq!(
            bake_with(vec![light("bulb", 1, 1, 2)], 2).unwrap().2[0]["coneAngle"],
            json!(0.0)
        );
        // No lights: no light. Kinds, modes and Lighting Modes that are none of the bake's
        // are refused.
        assert_eq!(bake_with(vec![], 0).unwrap().0, [0.0, 0.0, 0.0, 1.0]);
        assert!(bake_with(vec![light("area", 1, 3, 2)], 0).is_err());
        assert!(bake_with(vec![light("realtime", 1, 0, 0)], 0).is_err());
        assert!(bake_with(vec![], 3).is_err());
        let _ = std::fs::remove_dir_all(&folder);
    }

    #[test]
    fn a_mirroring_matrix_turns_the_normal_with_it() {
        // x mirrored: a normal along +x points along -x, one along +y stays.
        let m = [
            -1.0, 0.0, 0.0, 0.0, 0.0, 2.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
        ];
        let rows = direct::normal_matrix(&m);
        let world_normal = |_: &[f32; 16], n: [f32; 3]| direct::world_normal(&rows, n);
        assert_eq!(world_normal(&m, [1.0, 0.0, 0.0]), [-1.0, 0.0, 0.0]);
        assert_eq!(world_normal(&m, [0.0, 1.0, 0.0]), [0.0, 1.0, 0.0]);
        // Unequal scale: the normal leans away from the stretched axis.
        let n = world_normal(&m, [0.0, 0.6, 0.8]);
        assert!(
            (n[1] - 0.351_123_4).abs() < 1e-6 && (n[2] - 0.936_329_2).abs() < 1e-6,
            "{n:?}"
        );
    }

    fn image(width: usize, height: usize, bytes: &[u8]) -> ImageData {
        ImageData {
            width,
            height,
            data: base64::engine::general_purpose::STANDARD.encode(bytes),
        }
    }

    #[test]
    fn material_images_are_read_by_level_and_one_value_is_one_texel() {
        // Albedo: each level's measured value, and whether the texel is drawn.
        let two = image(2, 1, &[0, 127, 252, 255, 255, 15, 0, 0])
            .albedo("r")
            .unwrap();
        assert_eq!((two.width, two.height), (2, 1));
        assert_eq!(
            two.texels[0],
            [
                levels::albedo(0),
                levels::albedo(127),
                levels::albedo(252),
                1.0
            ]
        );
        // No level above 252 occurs; one takes the last value.
        assert_eq!(
            two.texels[1],
            [
                levels::albedo(252),
                levels::albedo(15),
                levels::albedo(0),
                0.0
            ]
        );
        assert!((levels::albedo(127) - 0.213_277_2).abs() < 1e-7 && levels::albedo(0) > 0.0);
        // Every texel the same: one texel.
        let same = image(2, 2, &[9, 9, 9, 255].repeat(4)).albedo("r").unwrap();
        assert_eq!((same.width, same.height, same.texels.len()), (1, 1, 1));
        // Emission: floats as they are.
        let floats: Vec<u8> = [1.5f32, 0.25, 0.0, 1.0, 3.0, 0.0, 0.0, 1.0]
            .iter()
            .flat_map(|v| v.to_le_bytes())
            .collect();
        let glow = image(1, 2, &floats).emissive("r").unwrap();
        assert_eq!(
            glow.texels,
            vec![[1.5, 0.25, 0.0, 1.0], [3.0, 0.0, 0.0, 1.0]]
        );
        // A size the data does not have, and no size, are refused with the renderer's name.
        let error = image(2, 2, &[0; 8]).albedo("Wall").unwrap_err().to_string();
        assert!(
            error.contains("'Wall'") && error.contains("2 x 2"),
            "{error}"
        );
        assert!(image(0, 1, &[]).emissive("r").is_err());
        assert!(
            ImageData {
                width: 1,
                height: 1,
                data: "?".into()
            }
            .albedo("r")
            .is_err()
        );
    }

    #[test]
    fn the_layout_gives_each_renderer_its_material_images_size() {
        let out = bake(
            request(
                vec![quad_mesh(
                    &[0.0, 0.0, 0.5, 0.0, 0.5, 0.25, 0.0, 0.25],
                    &BACK,
                )],
                vec![
                    renderer("lit", 244122856, 0, 1.0, 0.2),
                    renderer("zero", 879057353, 0, 0.0, 0.0),
                ],
            ),
            None,
        )
        .unwrap();
        // A unit quad at 10 texels a unit is 100 texels, laid out as its lightmap UVs' bounds
        // are shaped, twice as wide as high: 14 by 7 whole texels. The bounds are brought to
        // 0..1.
        assert_eq!(out["placements"][0]["image"], json!([14, 7]));
        let st: Vec<f64> = out["placements"][0]["imageScaleOffset"]
            .as_array()
            .unwrap()
            .iter()
            .map(|v| v.as_f64().unwrap())
            .collect();
        assert_eq!(st, [2.0, 4.0, 0.0, 0.0]);
        // Scale In Lightmap 0: one texel.
        assert_eq!(out["placements"][1]["image"], json!([1, 1]));
    }

    #[test]
    fn the_indirect_light_is_baked_from_the_material_images() {
        let folder =
            std::env::temp_dir().join(format!("unbound-baked-gi-indirect-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&folder);
        std::fs::create_dir_all(&folder).unwrap();
        let blue_noise = folder.join("bluenoisedata");
        std::fs::write(
            &blue_noise,
            vec![0u8; unbound_gi::baked::sampling::BLUE_NOISE_BYTES],
        )
        .unwrap();
        // A floor facing up (z) and a ceiling over it facing down that glows.
        let floor = json!({
            "positions": floats(&[0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 1.0, 0.0, 0.0, 1.0, 0.0]),
            "normals": floats(&[0.0, 0.0, 1.0].repeat(4)),
            "uv": floats(&[]),
            "uv2": floats(&UNIT),
            "submeshes": [encoded(&[0u32, 1, 2, 0, 2, 3], u32::to_le_bytes)],
        });
        let ceiling = json!({
            "positions": floats(&[-50.0, -50.0, 2.0, -50.0, 50.0, 2.0, 50.0, 50.0, 2.0, 50.0, -50.0, 2.0]),
            "normals": floats(&[0.0, 0.0, -1.0].repeat(4)),
            "uv": floats(&[]),
            "uv2": floats(&UNIT),
            "submeshes": [encoded(&[0u32, 1, 2, 0, 2, 3], u32::to_le_bytes)],
        });
        let black =
            json!({ "width": 1, "height": 1, "data": encoded(&[0u8, 0, 0, 255], u8::to_le_bytes) });
        let dark = json!({ "width": 1, "height": 1, "data": floats(&[0.0, 0.0, 0.0, 1.0]) });
        let glow = json!({ "width": 1, "height": 1, "data": floats(&[3.0, 2.0, 1.0, 1.0]) });
        let run = |materials: Json, blue: &Path, layout_only: bool| -> Result<Json> {
            let mut value = json!({
                "maxSize": 64, "padding": 2, "resolution": 4.0, "fixedSize": false,
                "scene": "10000000200000003000000040000000",
                "meshes": [floor, ceiling],
                "renderers": [renderer("floor", 11, 0, 1.0, 0.0), renderer("ceiling", 22, 1, 0.0, 0.0)],
                "layoutOnly": layout_only,
            });
            if !materials.is_null() {
                value["indirect"] = json!({
                    "samples": 16, "maxBounces": 1, "minBounces": 1,
                    "blueNoise": blue.to_string_lossy(), "materials": materials,
                });
            }
            bake(serde_json::from_value(value).unwrap(), Some(&folder))
        };
        let both = json!([
            { "albedo": black, "emissive": dark, "doubleSided": [false] },
            { "albedo": black, "emissive": glow, "doubleSided": [false] },
        ]);
        let out = run(both.clone(), &blue_noise, false).unwrap();
        let dump = folder.join("10000000200000003000000040000000");
        let description: Json =
            serde_json::from_slice(&std::fs::read(dump.join("buffers.json")).unwrap()).unwrap();
        assert_eq!(description["indirect"]["samples"], json!(16));
        assert_eq!(
            description["indirect"]["materials"]
                .as_array()
                .unwrap()
                .len(),
            2
        );
        // The floor's lightmap: every used texel has the ceiling's glow, and no ray met a back.
        let order: Vec<u64> = out["order"]
            .as_array()
            .unwrap()
            .iter()
            .map(|v| v.as_u64().unwrap())
            .collect();
        let lightmap = out["placements"][0]["lightmap"].as_u64().unwrap();
        let light = read_floats(&dump.join(format!("irradianceIndirect{lightmap}.f32")));
        let used: Vec<&[f32]> = light.chunks_exact(4).filter(|t| t[3] == 1.0).collect();
        assert!(!used.is_empty() && order.len() == 2);
        // (The ceiling, with Scale In Lightmap 0, keeps one texel of the lightmap: its rays
        // go down to the black floor.)
        let glowing = |t: &&[f32]| {
            (t[0] - 3.0).abs() < 1e-4 && (t[1] - 2.0).abs() < 1e-4 && (t[2] - 1.0).abs() < 1e-4
        };
        let others: Vec<&[f32]> = used.iter().copied().filter(|t| !glowing(t)).collect();
        assert!(used.len() > 4 && others.len() <= 1, "{others:?}");
        assert!(
            others.iter().all(|t| t[..3] == [0.0, 0.0, 0.0]),
            "{others:?}"
        );
        assert!(
            read_floats(&dump.join(format!("validity{lightmap}.f32")))
                .iter()
                .all(|&v| v == 0.0)
        );
        // Only the layout: nothing is written. Without materials: the direct light alone,
        // and an earlier bake's indirect light is gone.
        std::fs::remove_dir_all(&dump).unwrap();
        run(both.clone(), &blue_noise, true).unwrap();
        assert!(!dump.exists());
        run(Json::Null, &blue_noise, false).unwrap();
        assert!(
            dump.join(format!("irradianceDirect{lightmap}.f32"))
                .is_file()
        );
        assert!(
            !dump
                .join(format!("irradianceIndirect{lightmap}.f32"))
                .exists()
        );
        // Images for another number of renderers, and blue noise data that is not there or
        // of another size, are refused, named.
        let error = run(json!([both[0]]), &blue_noise, false)
            .unwrap_err()
            .to_string();
        assert!(
            error.contains("1 material images for 2 renderers"),
            "{error}"
        );
        let error = format!(
            "{:#}",
            run(both.clone(), &folder.join("none"), false).unwrap_err()
        );
        assert!(error.contains("blue noise data"), "{error}");
        std::fs::write(&blue_noise, [0u8; 10]).unwrap();
        let error = run(both, &blue_noise, false).unwrap_err().to_string();
        assert!(error.contains("10 bytes"), "{error}");
        let _ = std::fs::remove_dir_all(&folder);
    }

    #[test]
    fn the_skys_light_is_baked_and_a_submesh_without_a_material_lets_it_through() {
        let folder =
            std::env::temp_dir().join(format!("unbound-baked-gi-sky-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&folder);
        std::fs::create_dir_all(&folder).unwrap();
        let blue_noise = folder.join("bluenoisedata");
        std::fs::write(
            &blue_noise,
            vec![0u8; unbound_gi::baked::sampling::BLUE_NOISE_BYTES],
        )
        .unwrap();
        // A floor facing up (z) under a wide ceiling, and a sky of one colour.
        let floor = json!({
            "positions": floats(&[0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 1.0, 0.0, 0.0, 1.0, 0.0]),
            "normals": floats(&[0.0, 0.0, 1.0].repeat(4)),
            "uv": floats(&[]),
            "uv2": floats(&UNIT),
            "submeshes": [encoded(&[0u32, 1, 2, 0, 2, 3], u32::to_le_bytes)],
        });
        let ceiling = json!({
            "positions": floats(&[-50.0, -50.0, 2.0, -50.0, 50.0, 2.0, 50.0, 50.0, 2.0, 50.0, -50.0, 2.0]),
            "normals": floats(&[0.0, 0.0, -1.0].repeat(4)),
            "uv": floats(&[]),
            "uv2": floats(&UNIT),
            "submeshes": [encoded(&[0u32, 1, 2, 0, 2, 3], u32::to_le_bytes)],
        });
        let black =
            json!({ "width": 1, "height": 1, "data": encoded(&[0u8, 0, 0, 255], u8::to_le_bytes) });
        let dark = json!({ "width": 1, "height": 1, "data": floats(&[0.0, 0.0, 0.0, 1.0]) });
        let run = |has_material: Json, faces: &[f32]| -> Result<Json> {
            let mut covering = renderer("ceiling", 22, 1, 0.0, 0.0);
            if !has_material.is_null() {
                covering["hasMaterial"] = has_material;
            }
            let image = json!({ "albedo": black, "emissive": dark, "doubleSided": [false] });
            let value = json!({
                "maxSize": 64, "padding": 2, "resolution": 4.0, "fixedSize": false,
                "scene": "10000000200000003000000040000000",
                "meshes": [floor, ceiling],
                "renderers": [renderer("floor", 11, 0, 1.0, 0.0), covering],
                "indirect": {
                    "samples": 16, "maxBounces": 1, "minBounces": 1,
                    "blueNoise": blue_noise.to_string_lossy(), "materials": [image, image],
                    "environment": { "samples": 16, "importanceSampling": false, "side": 1, "faces": floats(faces) },
                },
            });
            bake(serde_json::from_value(value).unwrap(), Some(&folder))
        };
        let dump = folder.join("10000000200000003000000040000000");
        let even = [1.0f32, 2.0, 3.0].repeat(6);
        // The lightmap's used texels and those with the sky's colour (the ceiling keeps one
        // texel, which faces down and sees the sky past the small floor).
        let lit = |out: &Json| -> (usize, usize) {
            let lightmap = out["placements"][0]["lightmap"].as_u64().unwrap();
            let light = read_floats(&dump.join(format!("irradianceEnvironment{lightmap}.f32")));
            let used = light.chunks_exact(4).filter(|t| t[3] == 1.0);
            let under = |t: &&[f32]| {
                (t[0] - 1.0).abs() < 1e-5 && (t[1] - 2.0).abs() < 1e-5 && (t[2] - 3.0).abs() < 1e-5
            };
            (used.clone().count(), used.filter(under).count())
        };
        // The ceiling stops the sky, with a material named or with no word of materials.
        for has_material in [json!([true]), Json::Null] {
            let (used, under) = lit(&run(has_material, &even).unwrap());
            assert!(used > 4 && under <= 1, "{used} {under}");
        }
        let description: Json =
            serde_json::from_slice(&std::fs::read(dump.join("buffers.json")).unwrap()).unwrap();
        assert_eq!(description["indirect"]["sky"]["samples"], json!(16));
        assert_eq!(description["indirect"]["sky"]["side"], json!(1));
        assert_eq!(description["indirect"]["sky"]["directions"], json!(0));
        assert_eq!(read_floats(&dump.join("sky.f32")), even);
        // Without a material for its submesh (none in the list, or the list short of it) it
        // lets the sky through.
        for has_material in [json!([false]), json!([])] {
            let (used, under) = lit(&run(has_material, &even).unwrap());
            assert!(used > 4 && under == used, "{used} {under}");
        }
        // Faces of another size than said are refused.
        let error = format!("{:#}", run(Json::Null, &even[..15]).unwrap_err());
        assert!(
            error.contains("a sky of 6 faces of 1 texels a side"),
            "{error}"
        );
        let _ = std::fs::remove_dir_all(&folder);
    }

    fn read_floats(path: &Path) -> Vec<f32> {
        std::fs::read(path)
            .unwrap()
            .chunks_exact(4)
            .map(|b| f32::from_le_bytes([b[0], b[1], b[2], b[3]]))
            .collect()
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
