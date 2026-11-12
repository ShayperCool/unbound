//! A scene's realtime GI made by Unbound's own precompute from its renderers: the systems and
//! their layout ([`super::systems`]), each system's clusters, the transport, the directional
//! visibility and the light probes' transport through every system ([`super::scene`]), in the
//! form the runtime reads the Editor's data in ([`crate::data::Precomputed`]).
use super::clusters::{Clustering, Instance, cluster};
use super::math::V3;
use super::probes::{DEFAULT_PROBE_RAYS, PROBE_BUDGET, probe_set};
use super::scene::Scene;
use super::systems::{Member, SystemLayout, chart_mask, pack_atlases, pack_system, scene_systems};
use super::transport::{Settings, transport};
use super::visibility::visibility;
use crate::data::{Precomputed, System};
use crate::realtime_uv::Output;
use anyhow::{Context, Result, ensure};

/// One renderer of a scene as the precompute takes it.
pub struct BakeRenderer<'a> {
    /// What the runtime names it by (the scene mapping's object and prefab ids).
    pub id: (i64, i64),
    /// World space.
    pub positions: &'a [V3],
    pub normals: &'a [V3],
    pub triangles: &'a [[u32; 3]],
    /// Its realtime lightmap UVs ([`crate::realtime_uv::realtime_uvs`]).
    pub uvs: &'a Output,
    /// Its world bounds (Renderer.bounds).
    pub bounds: ([f32; 3], [f32; 3]),
    /// The voxel size of its clusters (world units).
    pub cluster_size: f32,
    pub modelling_tolerance: f32,
    /// Renderers share a system only with renderers of the same group (their Lightmap
    /// Parameters and System Tag).
    pub group: u64,
    /// Its Lightmap Parameters' Irradiance Quality and Budget (one per group).
    pub settings: Settings,
}

/// A baked scene and the renderers left out of it, with why (their ids, as
/// [`BakeRenderer::id`]).
pub struct Baked {
    pub data: Precomputed,
    pub refused: Vec<((i64, i64), String)>,
}

crate::codec::codec_struct!(Baked { data, refused });

/// The first bytes of an encoded [`Baked`].
const ENCODED_MAGIC: &[u8; 8] = b"UBGIBAK1";

impl Baked {
    /// Unbound's own binary encoding of the bake (its cache; not the Editor's format).
    pub fn encode(&self) -> Vec<u8> {
        let mut w = crate::codec::Writer(ENCODED_MAGIC.to_vec());
        crate::codec::Codec::write(self, &mut w);
        w.0
    }

    /// The bake [`Baked::encode`] wrote; fails on anything else (a damaged entry).
    pub fn decode(bytes: &[u8]) -> Result<Self> {
        ensure!(
            bytes.starts_with(ENCODED_MAGIC),
            "not Unbound's encoding of a realtime GI bake"
        );
        let mut r = crate::codec::Reader::new(&bytes[ENCODED_MAGIC.len()..]);
        let baked: Baked = crate::codec::Codec::read(&mut r)?;
        r.finished()?;
        Ok(baked)
    }
}

/// Why the precompute cannot take a renderer, or None.
fn refusal(r: &BakeRenderer) -> Option<String> {
    if r.uvs.uvs.len() != r.positions.len() || r.normals.len() != r.positions.len() {
        return Some(format!(
            "{} UVs and {} normals for {} vertices",
            r.uvs.uvs.len(),
            r.normals.len(),
            r.positions.len()
        ));
    }
    if r.triangles.is_empty() {
        return Some("no triangles".to_string());
    }
    if let Some(t) = r
        .triangles
        .iter()
        .find(|t| t.iter().any(|&v| v as usize >= r.positions.len()))
    {
        return Some(format!(
            "a triangle {t:?} past its {} vertices",
            r.positions.len()
        ));
    }
    if !r.bounds.0.iter().chain(&r.bounds.1).all(|v| v.is_finite()) {
        return Some("bounds that are not finite".to_string());
    }
    if !(r.cluster_size > 0.0 && r.cluster_size.is_finite())
        || !(r.modelling_tolerance >= 0.0 && r.modelling_tolerance.is_finite())
    {
        return Some(format!(
            "a cluster size of {} and a Modelling Tolerance of {}",
            r.cluster_size, r.modelling_tolerance
        ));
    }
    if r.settings.rays == 0 || r.settings.budget == 0 {
        return Some(format!(
            "an Irradiance Quality of {} and an Irradiance Budget of {}",
            r.settings.rays, r.settings.budget
        ));
    }
    None
}

/// An id of Unbound's own for system or probe set `index` of a baked scene (never the
/// environment's or an absent dependency's).
fn own_id(kind: u8, index: usize) -> [u8; 16] {
    let mut id = [0u8; 16];
    id[..4].copy_from_slice(b"UBGI");
    id[4] = kind;
    id[8..16].copy_from_slice(&(index as u64).to_le_bytes());
    id
}

/// The scene's realtime GI: its renderers' systems (in the order [`scene_systems`] makes them),
/// and the transport of light probes at `probes` when there are any. A renderer the precompute
/// cannot take is left out (and listed); the others are baked without it.
pub fn bake(all: &[BakeRenderer], probes: &[V3]) -> Result<Baked> {
    let mut refused = Vec::new();
    let mut renderers = Vec::with_capacity(all.len());
    for r in all {
        match refusal(r) {
            Some(why) => refused.push((r.id, why)),
            None => renderers.push(r),
        }
    }
    ensure!(
        !renderers.is_empty(),
        "a scene without realtime GI renderers{}",
        refused
            .iter()
            .map(|((object, _), why)| format!("; renderer {object}: {why}"))
            .collect::<String>()
    );
    let members: Vec<Member> = renderers
        .iter()
        .map(|r| Member::new(r.bounds.0, r.bounds.1, r.uvs))
        .collect();
    let keys: Vec<u64> = renderers.iter().map(|r| r.group).collect();
    let groups = scene_systems(&members, &keys, |group| {
        let outputs: Vec<&Output> = group.iter().map(|&k| renderers[k].uvs).collect();
        pack_system(&outputs).is_ok()
    });
    let layouts: Vec<SystemLayout> = groups
        .iter()
        .map(|g| pack_system(&g.iter().map(|&k| renderers[k].uvs).collect::<Vec<_>>()))
        .collect::<Result<_>>()?;
    // A system's renderers share their Lightmap Parameters ([`BakeRenderer::group`]).
    for (s, g) in groups.iter().enumerate() {
        let settings = renderers[g[0]].settings;
        ensure!(
            g.iter().all(|&k| renderers[k].settings == settings),
            "system {s}: renderers of one group with other irradiance settings"
        );
    }
    // Each renderer's positions in its system's texels.
    let texels: Vec<Vec<Vec<[f32; 2]>>> = groups
        .iter()
        .zip(&layouts)
        .map(|(g, layout)| {
            g.iter()
                .enumerate()
                .map(|(i, &k)| {
                    let (uvs, [x, y]) = (renderers[k].uvs, layout.origins[i]);
                    uvs.uvs
                        .iter()
                        .map(|uv| {
                            [
                                x as f32 + uv[0] * uvs.width as f32,
                                y as f32 + uv[1] * uvs.height as f32,
                            ]
                        })
                        .collect()
                })
                .collect()
        })
        .collect();
    let instances: Vec<Vec<Instance>> = groups
        .iter()
        .zip(&texels)
        .map(|(g, texels)| {
            g.iter()
                .zip(texels)
                .map(|(&k, texels)| {
                    let r = &renderers[k];
                    Instance {
                        positions: r.positions,
                        normals: r.normals,
                        texels,
                        triangles: r.triangles,
                        cluster_size: r.cluster_size,
                        modelling_tolerance: r.modelling_tolerance,
                    }
                })
                .collect()
        })
        .collect();
    let clusterings: Vec<Clustering> = instances
        .iter()
        .zip(&layouts)
        .enumerate()
        .map(|(s, (instances, layout))| {
            cluster(instances, layout.width, layout.height)
                .with_context(|| format!("the clusters of system {s}"))
        })
        .collect::<Result<_>>()?;
    let hashes: Vec<[u8; 16]> = (0..groups.len()).map(|s| own_id(b'S', s)).collect();
    let scene = Scene::new(clusterings.iter().collect(), hashes.clone())?;
    let atlases = pack_atlases(
        &layouts
            .iter()
            .map(|l| [l.width, l.height])
            .collect::<Vec<_>>(),
    )?;
    let (mut systems, mut ids, mut st) = (Vec::new(), Vec::new(), Vec::new());
    for (s, g) in groups.iter().enumerate() {
        let layout = &layouts[s];
        let outputs: Vec<&Output> = g.iter().map(|&k| renderers[k].uvs).collect();
        let mask = chart_mask(layout, &outputs);
        let settings = renderers[g[0]].settings;
        let transport = transport(
            &instances[s],
            &scene,
            s,
            layout.width,
            layout.height,
            &mask,
            settings,
        )
        .with_context(|| format!("the transport of system {s}"))?;
        let clustering = &clusterings[s];
        let (atlas, [ax, ay]) = atlases.placements[s];
        systems.push(System {
            hash: hashes[s],
            input_hash: hashes[s],
            first_renderer: ids.len(),
            renderer_count: g.len(),
            atlas,
            atlas_offset: (ax, ay),
            input: clustering.input.clone(),
            transport,
            materials: clustering.materials.clone(),
            chart_mask: mask,
            visibility: visibility(&clustering.input, &scene).map_err(|e| format!("{e:#}")),
        });
        for (i, &k) in g.iter().enumerate() {
            ids.push(renderers[k].id);
            st.push(layout.scale_offset(i, renderers[k].uvs));
        }
    }
    let probe_sets = if probes.is_empty() {
        Vec::new()
    } else {
        vec![probe_set(
            probes,
            &scene,
            own_id(b'P', 0),
            DEFAULT_PROBE_RAYS,
            PROBE_BUDGET,
        )?]
    };
    Ok(Baked {
        data: Precomputed {
            systems,
            probe_sets,
            probe_errors: Vec::new(),
            atlas_sizes: atlases.sizes,
            renderers: ids,
            renderer_st: st,
        },
        refused,
    })
}
