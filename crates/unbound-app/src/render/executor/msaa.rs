//! Retained resolve images of multisampled targets. Leaving a color attachment and explicit
//! self-resolves publish the current image; readback and compute reads use the published image.
use super::*;

// The render stream accepts at most eight color attachments.
const MAX_COLOR_ATTACHMENTS: usize = 8;
use crate::render::resources::TEX2D_ARRAY;
pub(super) type ResolveTargets = [Option<wgpu::Texture>; MAX_COLOR_ATTACHMENTS];

pub(super) struct ResolveHistory {
    pub(super) texture: wgpu::Texture,
    views: [wgpu::TextureView; 2],
    array_views: Option<[wgpu::TextureView; 2]>,
    dirty: bool,
    automatic: bool,
}

pub(super) struct ResolveCopies {
    source: wgpu::Texture,
    destination: wgpu::Texture,
    next: u32,
}

impl ResolveHistory {
    pub(super) fn view_for(&self, space: i32) -> &wgpu::TextureView {
        &self.views[space.clamp(0, 1) as usize]
    }
}

impl Iterator for ResolveCopies {
    type Item = Copy;

    fn next(&mut self) -> Option<Copy> {
        let mip = self.next;
        if mip >= self.source.mip_level_count() {
            return None;
        }
        self.next += 1;
        Some(Copy {
            channels: None,
            source: self.source.clone(),
            source_mip: mip,
            source_origin: wgpu::Origin3d::ZERO,
            destination: self.destination.clone(),
            destination_mip: mip,
            destination_origin: wgpu::Origin3d::ZERO,
            size: self
                .source
                .size()
                .mip_level_size(mip, self.source.dimension()),
        })
    }
}

impl UnityRenderer {
    /// Called only when a texture is uploaded. A stable target keeps its retained image and views.
    pub(super) fn prepare_resolve_history(
        &mut self,
        gpu: &GpuTexture,
        enabled: bool,
        dimension: i32,
    ) {
        let automatic = dimension != TEX2D_ARRAY || gpu.layers == 1;
        if !enabled || gpu.samples == 1 || gpu.format == 5 {
            self.release_resolve_history(&gpu.texture);
            return;
        }
        let history = self
            .resolve_history
            .entry(gpu.texture.clone())
            .or_insert_with(|| {
                let format = gpu.texture.format();
                let srgb = gpu.srgb_view.is_some().then_some(format.add_srgb_suffix());
                let formats = [srgb.unwrap_or(format)];
                let texture = self.device.create_texture(&wgpu::TextureDescriptor {
                    label: Some("unbound retained resolve"),
                    size: gpu.texture.size(),
                    mip_level_count: gpu.texture.mip_level_count(),
                    sample_count: 1,
                    dimension: gpu.texture.dimension(),
                    format,
                    usage: wgpu::TextureUsages::COPY_SRC
                        | wgpu::TextureUsages::COPY_DST
                        | wgpu::TextureUsages::TEXTURE_BINDING,
                    view_formats: if srgb.is_some() { &formats } else { &[] },
                });
                let view = wgpu::TextureViewDescriptor {
                    dimension: if gpu.cube {
                        Some(wgpu::TextureViewDimension::Cube)
                    } else if gpu.cube_array {
                        Some(wgpu::TextureViewDimension::CubeArray)
                    } else {
                        None
                    },
                    ..Default::default()
                };
                let raw = texture.create_view(&view);
                let linear = match srgb {
                    Some(format) => texture.create_view(&wgpu::TextureViewDescriptor {
                        format: Some(format),
                        ..view
                    }),
                    None => raw.clone(),
                };
                let array_views = (!gpu.cube && !gpu.cube_array).then(|| {
                    let raw = texture.create_view(&wgpu::TextureViewDescriptor {
                        dimension: Some(wgpu::TextureViewDimension::D2Array),
                        ..Default::default()
                    });
                    let linear = match srgb {
                        Some(format) => texture.create_view(&wgpu::TextureViewDescriptor {
                            format: Some(format),
                            dimension: Some(wgpu::TextureViewDimension::D2Array),
                            ..Default::default()
                        }),
                        None => raw.clone(),
                    };
                    [raw, linear]
                });
                ResolveHistory {
                    texture,
                    views: [raw, linear],
                    array_views,
                    dirty: false,
                    automatic,
                }
            });
        history.automatic = automatic;
    }

    pub(super) fn release_resolve_history(&mut self, texture: &wgpu::Texture) {
        self.resolve_history.remove(texture);
        for bound in &mut self.resolve_targets {
            if bound.as_ref().is_some_and(|source| source == texture) {
                *bound = None;
            }
        }
    }

    pub(super) fn mark_resolve_copied(&mut self, destination: &wgpu::Texture) {
        // Leaving a bound attachment publishes its samples again after a texture copy.
        if self
            .resolve_targets
            .iter()
            .flatten()
            .any(|bound| bound == destination)
        {
            if let Some(history) = self.resolve_history.get_mut(destination) {
                history.dirty = true;
            }
        }
    }

    pub(super) fn mark_msaa_written(&mut self, target: &Target) {
        for image in
            std::iter::once(&target.texture).chain(target.extra.iter().map(|extra| &extra.texture))
        {
            if let Some((texture, _, _)) = image {
                if let Some(history) = self.resolve_history.get_mut(texture) {
                    history.dirty = true;
                }
            }
        }
    }

    pub(super) fn msaa_targets(&self, target: Option<&Target>) -> ResolveTargets {
        std::array::from_fn(|index| {
            let target = target?;
            let image = if index == 0 {
                &target.texture
            } else {
                &target.extra.get(index - 1)?.texture
            };
            let (texture, _, _) = image.as_ref()?;
            self.resolve_history
                .contains_key(texture)
                .then(|| texture.clone())
        })
    }

    /// A resource remaining in any color slot is still bound. Depth-only changes and changes
    /// between its mip or slice attachments do not leave that resource.
    pub(super) fn switch_msaa_targets(&mut self, next: ResolveTargets, operations: &mut Vec<Op>) {
        let preceding = std::mem::replace(&mut self.resolve_targets, next);
        for source in preceding.into_iter().flatten() {
            let remains = self
                .resolve_targets
                .iter()
                .flatten()
                .any(|texture| *texture == source);
            if !remains {
                if let Some(copies) = self.resolve_msaa_source(&source, false) {
                    operations.extend(copies.map(Op::Copy));
                }
            }
        }
    }

    /// Resolve copies follow the completed passes that wrote their source.
    fn resolve_msaa_source(
        &mut self,
        source: &wgpu::Texture,
        force: bool,
    ) -> Option<ResolveCopies> {
        let history = self.resolve_history.get_mut(source)?;
        if !force && !history.automatic {
            return None;
        }
        if !std::mem::take(&mut history.dirty) && !force {
            return None;
        }
        Some(ResolveCopies {
            source: source.clone(),
            destination: history.texture.clone(),
            next: 0,
        })
    }

    pub(super) fn resolve_msaa(&mut self, handle: u64) -> Option<ResolveCopies> {
        let (base, depth) = handle::split(handle);
        if depth {
            return None;
        }
        let source = self.textures.get(&base)?.texture.clone();
        self.resolve_msaa_source(&source, true)
    }

    /// Cached attachment views expose the samples without allocating a view during a draw.
    pub(super) fn multisampled_view(
        &self,
        handle: u64,
        dim: unbound_shaders::TextureDim,
        sample: unbound_shaders::SampleKind,
        space: i32,
    ) -> Option<wgpu::TextureView> {
        use unbound_shaders::{SampleKind, TextureDim};
        let (base, depth_element) = handle::split(handle);
        let t = self.textures.get(&base)?;
        if !t.bind_ms || t.samples == 1 || dim != TextureDim::D2 || t.layers != 1 || t.volume {
            return None;
        }
        if depth_element || t.format == 5 || sample == SampleKind::Depth {
            return matches!(sample, SampleKind::Float | SampleKind::Depth)
                .then(|| t.depth_msaa.clone())
                .flatten();
        }
        let (raw, srgb) = t.msaa.as_ref()?;
        let has = raw.texture().format().sample_type(None, None)?;
        let compatible = match sample {
            SampleKind::Float => matches!(has, wgpu::TextureSampleType::Float { .. }),
            SampleKind::Sint => has == wgpu::TextureSampleType::Sint,
            SampleKind::Uint => has == wgpu::TextureSampleType::Uint,
            SampleKind::Depth => false,
        };
        compatible.then(|| {
            if space == 1 {
                srgb.as_ref().unwrap_or(raw)
            } else {
                raw
            }
            .clone()
        })
    }

    pub(super) fn resolved_array_view(
        &self,
        texture: &GpuTexture,
        space: i32,
    ) -> Option<wgpu::TextureView> {
        self.resolve_history
            .get(&texture.texture)?
            .array_views
            .as_ref()
            .map(|views| views[space.clamp(0, 1) as usize].clone())
    }

    pub(super) fn sampled_texture_view(
        &self,
        texture: &GpuTexture,
        space: i32,
    ) -> wgpu::TextureView {
        self.resolve_history.get(&texture.texture).map_or_else(
            || texture.view_for(space).clone(),
            |history| history.view_for(space).clone(),
        )
    }
}
