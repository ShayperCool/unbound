//! Native rendering plugins in the renderer: their callbacks in stream order
//! (`Command::Plugin`), the OpenGL textures they see for the renderer's
//! (`Texture.GetNativeTexturePtr`), and textures that show a plugin's own
//! (`Texture2D.CreateExternalTexture`). Behaviour matches the Unity 6000.0.71f1 Editor on OpenGL
//! Core (tests/projects/nativegfx); see `gl_interop` for how the plugins' OpenGL context shares
//! the renderer's memory.
//!
//! A callback runs when the work before it is done on the GPU, on the caller's thread (the
//! game's: the Editor runs them on its main thread), with the current target bound in OpenGL:
//! its color texture's level, its depth texture and the viewport. The screen is not shared: a
//! callback while it is the target finds a framebuffer without attachments (logged once).
//! Nothing here runs, and nothing is allocated, while no plugin calls the renderer.
use super::{UnityRenderer, clamp_viewport, depth_owner};
use crate::render::gl_interop::{self, GlTexture};
use crate::render::resources::{self, GpuTexture};
use rustc_hash::{FxHashMap as HashMap, FxHashSet as HashSet};
use std::ffi::c_void;
use unbound_runtime::plugin_api;
use unbound_runtime::render_commands::PluginCall;
use unbound_runtime::render_protocol::owned;

/// `UnityRenderingExtEventType` of the events Unity sends with these calls.
mod ext_event {
    pub const CUSTOM_BLIT: i32 = 6;
    pub const UPDATE_TEXTURE_BEGIN_V1: i32 = 7;
    pub const UPDATE_TEXTURE_END_V1: i32 = 8;
    pub const UPDATE_TEXTURE_BEGIN_V2: i32 = 9;
    pub const UPDATE_TEXTURE_END_V2: i32 = 10;
}

/// `UnityRenderingExtTextureUpdateParamsV1`.
#[repr(C)]
struct TextureUpdateV1 {
    data: *mut c_void,
    user_data: u32,
    texture_id: u32,
    format: u32,
    width: u32,
    height: u32,
    bytes_per_pixel: u32,
}

/// `UnityRenderingExtTextureUpdateParamsV2`.
#[repr(C)]
struct TextureUpdateV2 {
    data: *mut c_void,
    texture_id: isize,
    user_data: u32,
    format: u32,
    width: u32,
    height: u32,
    bytes_per_pixel: u32,
}

/// `UnityRenderingExtCustomBlitParams`.
#[repr(C)]
struct CustomBlitParams {
    source: u32,
    destination: *mut c_void,
    command: u32,
    param: u32,
    flags: u32,
}

/// A renderer texture native plugins see, by its handle: the storage it was shared with (the
/// texture's current one while they are the same) and its OpenGL textures.
struct Shared {
    texture: wgpu::Texture,
    color: GlTexture,
    depth: Option<GlTexture>,
}

/// A texture that shows a plugin's own OpenGL texture: a copy of it, made again after every
/// callback (the plugin draws into it in callbacks).
struct External {
    source: u32,
    mirror: GlTexture,
    width: u32,
    height: u32,
}

/// The renderer's GPU as the interop sees it, looked up once.
struct Device {
    /// `VkPhysicalDeviceIDProperties::deviceUUID`: the OpenGL context must be on this GPU.
    uuid: Option<[u8; 16]>,
    /// Why the interop is not used on this GPU (None: it is).
    refused: Option<String>,
    /// Depth24PlusStencil8 is stored as D24S8 (else D32S8), as wgpu-hal decides.
    d24s8: bool,
}

/// The PCI vendor of the GPUs the interop was measured on (NVIDIA).
const MEASURED_VENDOR: u32 = 0x10DE;

#[derive(Default)]
pub(super) struct PluginState {
    shared: HashMap<u64, Shared>,
    externals: HashMap<u64, External>,
    /// Textures that could not be shared, with the storage that was refused and why (not tried
    /// again until the storage changes).
    refused: HashMap<u64, (wgpu::Texture, String)>,
    /// Textures showing another texture's storage (`CreateExternalTexture` of its pointer).
    pub aliases: HashSet<u64>,
    device: Option<Device>,
    /// The current target's mip and slice (`SetTarget`).
    pub target: (i32, i32),
}

/// The callback's function types (the plugin headers' `UnityRenderingEvent`,
/// `UnityRenderingEventAndData`).
type RenderingEvent = extern "C" fn(i32);
type RenderingEventAndData = extern "C" fn(i32, *mut c_void);

/// Calls a rendering event or custom blit's callback (`TextureUpdate`s run their own sequence).
pub(super) fn invoke(call: &PluginCall) {
    match *call {
        PluginCall::Event { callback, id } => {
            // SAFETY: a plugin's `UnityRenderingEvent`, as GL.IssuePluginEvent and
            // CommandBuffer.IssuePluginEvent take it (not null: checked by the caller).
            let callback: RenderingEvent = unsafe { std::mem::transmute(callback as usize) };
            callback(id);
        }
        PluginCall::EventAndData { callback, id, data } => {
            let callback: RenderingEventAndData = unsafe { std::mem::transmute(callback as usize) };
            callback(id, data as usize as *mut c_void);
        }
        PluginCall::CustomBlit {
            callback,
            command,
            source,
            destination,
            param,
            flags,
        } => {
            let callback: RenderingEventAndData = unsafe { std::mem::transmute(callback as usize) };
            let mut params = CustomBlitParams {
                source,
                destination: destination as usize as *mut c_void,
                command,
                param,
                flags,
            };
            callback(ext_event::CUSTOM_BLIT, &mut params as *mut _ as *mut c_void);
        }
        PluginCall::TextureUpdate { .. } => {}
    }
}

impl UnityRenderer {
    /// The renderer GPU's UUID for the plugins' OpenGL context; why there is none otherwise.
    pub fn plugin_device(&mut self) -> Result<Option<[u8; 16]>, String> {
        self.interop_device()
    }

    /// The renderer GPU's UUID when plugins share its textures; why not otherwise. The interop
    /// hands no image layouts between Vulkan and OpenGL (it waits for each side instead), which
    /// the measured NVIDIA driver does not need; other drivers may keep layout-dependent
    /// compression, so they are refused rather than risk wrong texels.
    fn interop_device(&mut self) -> Result<Option<[u8; 16]>, String> {
        let device = self.plugins.device.get_or_insert_with(|| {
            let (uuid, vendor, d24s8) = vulkan::device_info(&self.device);
            let refused = (vendor != Some(MEASURED_VENDOR)).then(|| {
                "native plugins share textures with the renderer on NVIDIA GPUs only (measured \
                 there; other drivers may need image layouts handed over)"
                    .to_string()
            });
            if let Some(reason) = &refused {
                tracing::error!("{reason}");
            }
            Device {
                uuid,
                refused,
                d24s8,
            }
        });
        match &device.refused {
            Some(reason) => Err(reason.clone()),
            None => Ok(device.uuid),
        }
    }

    fn d24s8(&self) -> bool {
        self.plugins.device.as_ref().is_none_or(|d| d.d24s8)
    }

    /// The OpenGL texture name of a texture (`depth`: its depth buffer's), 0 when it cannot be
    /// shared (logged once per texture).
    pub fn native_texture(&mut self, handle: u64, depth: bool) -> u64 {
        #[cfg(target_os = "linux")]
        if self.vulkan_plugins.is_some() {
            return self.vulkan_native_texture(handle, depth);
        }
        match self.share(handle) {
            Ok((color, depth_texture)) => {
                if depth {
                    depth_texture.map_or(0, |d| d.name as u64)
                } else {
                    color.name as u64
                }
            }
            Err(reason) => {
                self.warn_once(format!("plugin-share{handle}"), || {
                    format!("texture {handle} is not shared with native plugins: {reason}")
                });
                0
            }
        }
    }

    /// The texture's OpenGL textures, sharing its memory: the texture is moved into memory
    /// OpenGL can import the first time (its texels copied), and again after its storage changed.
    fn share(&mut self, handle: u64) -> Result<(GlTexture, Option<GlTexture>), String> {
        let uuid = self.interop_device()?;
        gl_interop::with(uuid, |_| ())?;
        let gpu = self
            .textures
            .get(&handle)
            .ok_or_else(|| format!("no texture {handle}"))?;
        if let Some(shared) = self.plugins.shared.get(&handle)
            && shared.texture == gpu.texture
        {
            return Ok((shared.color, shared.depth));
        }
        if let Some((texture, reason)) = self.plugins.refused.get(&handle)
            && *texture == gpu.texture
        {
            return Err(reason.clone());
        }
        match self.share_storage(handle, uuid) {
            Ok(shared) => Ok(shared),
            Err(reason) => {
                if let Some(gpu) = self.textures.get(&handle) {
                    self.plugins
                        .refused
                        .insert(handle, (gpu.texture.clone(), reason.clone()));
                }
                Err(reason)
            }
        }
    }

    /// [`Self::share`] for storage not shared yet.
    fn share_storage(
        &mut self,
        handle: u64,
        uuid: Option<[u8; 16]>,
    ) -> Result<(GlTexture, Option<GlTexture>), String> {
        let d24s8 = self.d24s8();
        let gpu = &self.textures[&handle];
        if let Some(stale) = self.plugins.shared.remove(&handle) {
            delete_shared(uuid, stale);
        }
        if gpu.samples > 1 || gpu.layers > 1 || gpu.volume || gpu.cube || gpu.cube_array {
            return Err("only single-sample 2D textures are shared".into());
        }
        let color_format = gl_format(gpu.texture.format(), gpu.format, self.color_space, d24s8)
            .ok_or_else(|| {
                format!(
                    "its format {:?} has no OpenGL counterpart",
                    gpu.texture.format()
                )
            })?;
        let depth_format = match &gpu.depth_texture {
            Some(depth) => Some(gl_format(depth.format(), -1, 0, d24s8).ok_or_else(|| {
                format!(
                    "its depth format {:?} has no OpenGL counterpart",
                    depth.format()
                )
            })?),
            None => None,
        };
        // The same storage again, in exported memory.
        let packet = descriptor(gpu);
        let device = self.device.clone();
        let mut exported = Vec::new();
        let mut failure = None;
        let replacement = resources::upload_texture_with(
            &device,
            &self.queue,
            None,
            &packet,
            self.generation + 1,
            &mut |desc| match vulkan::exported_texture(&device, desc) {
                Ok((texture, memory)) => {
                    exported.push(memory);
                    texture
                }
                Err(reason) => {
                    failure.get_or_insert(reason);
                    device.create_texture(desc)
                }
            },
        );
        if let Some(reason) = failure {
            return Err(reason);
        }
        self.generation += 1;
        let old = self.textures.remove(&handle).expect("looked up above");
        self.copy_contents(&old, &replacement);
        let sampler = replacement.sampler_key;
        let (width, height, levels) =
            (replacement.width, replacement.height, replacement.mip_count);
        let depth_levels = replacement
            .depth_texture
            .as_ref()
            .map_or(1, |d| d.mip_level_count());
        let texture = replacement.texture.clone();
        let previous = Some((old.texture.clone(), old.sampler_key));
        self.install_texture(handle, replacement, previous, &packet);
        self.refresh_target(handle);
        let mut memories = exported.into_iter();
        let imported = gl_interop::with(uuid, |interop| -> Result<_, String> {
            let wrap = [sampler.wrap_u, sampler.wrap_v];
            let memory = memories.next().ok_or("the color memory was not exported")?;
            let color = interop.import(
                memory.fd,
                memory.size,
                width,
                height,
                levels,
                color_format,
                sampler.filter,
                wrap,
            )?;
            let depth = match (depth_format, memories.next()) {
                (Some(format), Some(memory)) => {
                    match interop.import(
                        memory.fd,
                        memory.size,
                        width,
                        height,
                        depth_levels,
                        format,
                        0,
                        [1, 1],
                    ) {
                        Ok(depth) => Some(depth),
                        Err(reason) => {
                            interop.delete(color);
                            return Err(reason);
                        }
                    }
                }
                _ => None,
            };
            Ok((color, depth))
        })??;
        self.plugins.shared.insert(
            handle,
            Shared {
                texture,
                color: imported.0,
                depth: imported.1,
            },
        );
        Ok(imported)
    }

    /// Copies a texture's texels (and depth) into its replacement of the same shape.
    fn copy_contents(&mut self, from: &GpuTexture, to: &GpuTexture) {
        let mut encoder = self
            .device
            .create_command_encoder(&wgpu::CommandEncoderDescriptor {
                label: Some("unbound plugin share"),
            });
        for mip in 0..from.mip_count.min(to.mip_count) {
            let size = wgpu::Extent3d {
                width: (from.width >> mip).max(1),
                height: (from.height >> mip).max(1),
                depth_or_array_layers: 1,
            };
            let at = |texture| wgpu::TexelCopyTextureInfo {
                texture,
                mip_level: mip,
                origin: wgpu::Origin3d::ZERO,
                aspect: wgpu::TextureAspect::All,
            };
            encoder.copy_texture_to_texture(at(&from.texture), at(&to.texture), size);
        }
        // Depth formats are not copied: drawn with the depth blit (the stencil is cleared).
        if let (Some(source), Some(destination)) = (&from.depth_sample, &to.depth) {
            let (pipeline, layout) = self.depth_blit_pipeline(false, 1, false);
            let group = self.device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("unbound plugin share depth"),
                layout: &layout,
                entries: &[wgpu::BindGroupEntry {
                    binding: 0,
                    resource: wgpu::BindingResource::TextureView(source),
                }],
            });
            let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                label: Some("unbound plugin share depth"),
                color_attachments: &[],
                depth_stencil_attachment: Some(wgpu::RenderPassDepthStencilAttachment {
                    view: destination,
                    depth_ops: Some(wgpu::Operations {
                        load: wgpu::LoadOp::Clear(1.0),
                        store: wgpu::StoreOp::Store,
                    }),
                    stencil_ops: Some(wgpu::Operations {
                        load: wgpu::LoadOp::Clear(0),
                        store: wgpu::StoreOp::Store,
                    }),
                }),
                timestamp_writes: None,
                occlusion_query_set: None,
                multiview_mask: None,
            });
            pass.set_pipeline(&pipeline);
            pass.set_bind_group(0, &group, &[]);
            pass.draw(0..3, 0..1);
        }
        self.queue.submit([encoder.finish()]);
    }

    /// Whether a texture still holds this storage (an external texture showing another's, or
    /// the one it shows): what refers to the storage stays while one does.
    pub(super) fn storage_held(&self, texture: &wgpu::Texture) -> bool {
        !self.plugins.aliases.is_empty() && self.textures.values().any(|t| t.texture == *texture)
    }

    /// A texture's storage changed to `texture`: what plugins saw of the old one goes.
    pub(super) fn plugin_texture_replaced(&mut self, handle: u64, texture: &wgpu::Texture) {
        // A refusal was of the old storage (and must not keep it alive).
        self.plugins.refused.remove(&handle);
        if self
            .plugins
            .shared
            .get(&handle)
            .is_some_and(|shared| shared.texture != *texture)
        {
            let stale = self.plugins.shared.remove(&handle).expect("checked");
            delete_shared(self.plugins.device.as_ref().and_then(|d| d.uuid), stale);
        }
    }

    /// A texture was released: its OpenGL textures go (a plugin's own texture stays its own).
    pub(super) fn release_plugin_texture(&mut self, handle: u64) {
        let uuid = self.plugins.device.as_ref().and_then(|d| d.uuid);
        self.plugins.refused.remove(&handle);
        self.plugins.aliases.remove(&handle);
        if let Some(shared) = self.plugins.shared.remove(&handle) {
            delete_shared(uuid, shared);
        }
        if let Some(external) = self.plugins.externals.remove(&handle) {
            let _ = gl_interop::with(uuid, |interop| interop.delete(external.mirror));
        }
    }

    /// The current target uses the texture whose storage was replaced: its views are made again.
    fn refresh_target(&mut self, handle: u64) {
        let (color, depth) = self.target_handles;
        if self.state.target.is_some()
            && (color == handle || depth_owner(color, depth) == Some(handle))
        {
            let (mip, slice) = self.plugins.target;
            self.state.target = self.resolve_target(color, depth, mip, slice);
        }
    }

    /// Makes `texture` show `native`: another texture's OpenGL name (the same storage), or a
    /// plugin's own 2D texture (copied after every callback).
    pub fn external_texture(
        &mut self,
        handle: u64,
        native: u64,
        width: u32,
        height: u32,
        srgb: bool,
    ) -> Result<(), String> {
        let uuid = self.interop_device()?;
        gl_interop::with(uuid, |_| ())?;
        let name =
            u32::try_from(native).map_err(|_| format!("{native} is not an OpenGL texture name"))?;
        // What plugins saw of the texture goes; while it showed another's storage it stays an
        // alias until that storage is replaced below.
        let was_alias = self.plugins.aliases.contains(&handle);
        self.release_plugin_texture(handle);
        if was_alias {
            self.plugins.aliases.insert(handle);
        }
        let device = self.device.clone();
        // Another texture's name: the same storage.
        let source = self
            .plugins
            .shared
            .iter()
            .find(|(_, shared)| shared.color.name == name)
            .map(|(&source, shared)| (source, shared.texture.clone()));
        if let Some((source, texture)) = source {
            // A sampled texture over the source's storage: none of its target state (depth,
            // multisampling, automatic mips), which stays the source's.
            let packet = owned::Texture {
                render_target: false,
                random_write: false,
                auto_generate_mips: false,
                defer_mips: false,
                depth_bits: 0,
                ..descriptor(&self.textures[&source])
            };
            self.generation += 1;
            let gpu = resources::upload_texture_with(
                &device,
                &self.queue,
                None,
                &packet,
                self.generation,
                &mut |_| texture.clone(),
            );
            let previous = self
                .textures
                .remove(&handle)
                .map(|old| (old.texture.clone(), old.sampler_key));
            self.plugins.aliases.insert(handle);
            self.install_texture(handle, gpu, previous, &packet);
            return Ok(());
        }
        // A plugin's texture: a mirror the renderer samples.
        let packet = owned::Texture {
            width: width as i32,
            height: height as i32,
            format: if srgb { 0 } else { 1 },
            mip_count: 1,
            filter_mode: 1,
            wrap_u: 1,
            wrap_v: 1,
            wrap_w: 1,
            samples: 1,
            layers: 1,
            dimension: 2,
            ..Default::default()
        };
        let mut exported = None;
        let mut failure = None;
        self.generation += 1;
        let gpu = resources::upload_texture_with(
            &device,
            &self.queue,
            None,
            &packet,
            self.generation,
            &mut |desc| match vulkan::exported_texture(&device, desc) {
                Ok((texture, memory)) => {
                    exported = Some(memory);
                    texture
                }
                Err(reason) => {
                    failure.get_or_insert(reason);
                    device.create_texture(desc)
                }
            },
        );
        let memory = match (failure, exported) {
            (None, Some(memory)) => memory,
            (failure, _) => return Err(failure.unwrap_or_else(|| "nothing was exported".into())),
        };
        let format = gl_format(
            gpu.texture.format(),
            packet.format,
            self.color_space,
            self.d24s8(),
        )
        .ok_or("the mirror's format has no OpenGL counterpart")?;
        let mirror = gl_interop::with(uuid, |interop| -> Result<GlTexture, String> {
            if !interop.is_texture(name) {
                return Err(format!(
                    "{name} is not an OpenGL texture of the plugins' context"
                ));
            }
            let mirror =
                interop.import(memory.fd, memory.size, width, height, 1, format, 1, [1, 1])?;
            interop.copy(name, mirror.name, width, height);
            interop.finish();
            let errors = interop.clear_errors();
            if !errors.is_empty() {
                interop.delete(mirror);
                return Err(format!(
                    "it cannot be copied ({}): only 2D textures of 8-bit RGBA texels and its size are shown",
                    errors
                        .iter()
                        .map(|e| gl_interop::error_text(*e))
                        .collect::<Vec<_>>()
                        .join(", ")
                ));
            }
            Ok(mirror)
        })??;
        let previous = self
            .textures
            .remove(&handle)
            .map(|old| (old.texture.clone(), old.sampler_key));
        self.install_texture(handle, gpu, previous, &packet);
        self.plugins.aliases.remove(&handle);
        self.plugins.externals.insert(
            handle,
            External {
                source: name,
                mirror,
                width,
                height,
            },
        );
        Ok(())
    }

    /// Runs a plugin's callback (`Command::Plugin`): the work before it has been submitted.
    pub(super) fn run_plugin(&mut self, call: &PluginCall) {
        let callback = match *call {
            PluginCall::Event { callback, .. }
            | PluginCall::EventAndData { callback, .. }
            | PluginCall::TextureUpdate { callback, .. }
            | PluginCall::CustomBlit { callback, .. } => callback,
        };
        if callback == 0 {
            self.warn_once("plugin-null".into(), || {
                "a native plugin call without a callback (null) is skipped".into()
            });
            return;
        }
        #[cfg(target_os = "linux")]
        if self.vulkan_plugins.is_some() {
            // The Vulkan device: the plugin records into a command buffer of the renderer's
            // (`plugins_vulkan`); texture updates upload as on every device.
            match *call {
                PluginCall::TextureUpdate {
                    callback,
                    texture,
                    user_data,
                    v2,
                    format,
                    bytes_per_pixel,
                } => self.texture_update(callback, texture, user_data, v2, format, bytes_per_pixel),
                _ => {
                    self.run_plugin_vulkan(call, || invoke(call));
                }
            }
            return;
        }
        let device = self.interop_device();
        let uuid = device.as_ref().ok().copied().flatten();
        let context = device.and_then(|uuid| gl_interop::with(uuid, |_| ()));
        if let (Err(reason), false) = (&context, matches!(call, PluginCall::TextureUpdate { .. })) {
            let reason = reason.clone();
            self.warn_once("plugin-context".into(), || {
                format!("native rendering plugin callbacks are skipped: {reason}")
            });
            return;
        }
        // The plugin may read anything drawn so far (and texture updates written).
        self.queue.submit(std::iter::empty());
        if let Err(error) = self.device.poll(wgpu::PollType::wait_indefinitely()) {
            tracing::warn!("waiting for the GPU before a plugin callback failed: {error}");
        }
        if context.is_ok() {
            self.bind_plugin_target(uuid);
            let _ = gl_interop::with(uuid, |interop| interop.clear_errors());
        }
        match *call {
            PluginCall::Event { .. }
            | PluginCall::EventAndData { .. }
            | PluginCall::CustomBlit { .. } => invoke(call),
            PluginCall::TextureUpdate {
                callback,
                texture,
                user_data,
                v2,
                format,
                bytes_per_pixel,
            } => self.texture_update(callback, texture, user_data, v2, format, bytes_per_pixel),
        }
        if context.is_ok() {
            self.after_plugin_callback(uuid);
        }
    }

    /// The plugin's OpenGL work is done before the renderer goes on; errors it left are logged
    /// as the Editor logs them; plugins' own textures shown by textures are copied again.
    fn after_plugin_callback(&mut self, uuid: Option<[u8; 16]>) {
        let externals: Vec<(u32, u32, u32, u32)> = self
            .plugins
            .externals
            .values()
            .map(|e| (e.source, e.mirror.name, e.width, e.height))
            .collect();
        let errors = gl_interop::with(uuid, |interop| {
            interop.finish();
            let errors = interop.clear_errors();
            for &(source, mirror, width, height) in &externals {
                interop.copy(source, mirror, width, height);
            }
            if !externals.is_empty() {
                interop.finish();
                interop.clear_errors();
            }
            errors
        })
        .unwrap_or_default();
        for error in errors {
            plugin_api::log(
                plugin_api::LOG_ASSERT,
                &format!(
                    "OPENGL NATIVE PLUG-IN ERROR: {}",
                    gl_interop::error_text(error)
                ),
            );
        }
    }

    /// Binds the current target for a callback: its color texture's level, the depth texture
    /// attached to it and the viewport; a framebuffer without attachments when the target is
    /// the screen or cannot be shared.
    fn bind_plugin_target(&mut self, uuid: Option<[u8; 16]>) {
        let (color, depth) = self.target_handles;
        let (mip, _) = self.plugins.target;
        let mut bound = None;
        if color == 0 {
            self.warn_once("plugin-screen".into(), || {
                "a native plugin callback while the screen is the target finds no target bound (the screen is not shared with plugins)".into()
            });
        } else if self.textures.contains_key(&color) {
            match self.share(color) {
                Ok((color_texture, own_depth)) => {
                    let attached = match depth_owner(color, depth) {
                        Some(owner) if owner == color => own_depth,
                        Some(owner) => self.share(owner).ok().and_then(|(_, d)| d),
                        None => None,
                    };
                    bound = Some((color_texture.name, attached.map(|d| d.name)));
                }
                Err(reason) => self.warn_once(format!("plugin-target{color}"), || {
                    format!("a native plugin callback finds no target bound: render texture {color} is not shared ({reason})")
                }),
            }
        }
        // The target's size at its level (the screen's for the screen).
        let (width, height) = match self.textures.get(&color) {
            Some(gpu) => (
                (gpu.width >> mip.max(0)).max(1),
                (gpu.height >> mip.max(0)).max(1),
            ),
            None => self.screen_size,
        };
        let v = clamp_viewport(self.state.viewport, width, height);
        let viewport = [v[0] as i32, v[1] as i32, v[2] as i32, v[3] as i32];
        let _ = gl_interop::with(uuid, |interop| match bound {
            Some((color, depth)) => interop.bind_target(
                Some((color, mip.max(0) as u32)),
                depth.map(|d| (d, true)),
                viewport,
            ),
            None => interop.bind_target(None, None, viewport),
        });
    }

    /// `IssuePluginCustomTextureUpdateV1`/`V2`: the begin event, the upload of the data the
    /// plugin gave into the first level, the end event with the same parameters.
    #[allow(clippy::too_many_arguments)]
    fn texture_update(
        &mut self,
        callback: u64,
        handle: u64,
        user_data: u32,
        v2: bool,
        format: u32,
        bytes_per_pixel: u32,
    ) {
        let Some(gpu) = self.textures.get(&handle) else {
            return;
        };
        let (width, height) = (gpu.width, gpu.height);
        let texture = gpu.texture.clone();
        let callback: RenderingEventAndData = unsafe { std::mem::transmute(callback as usize) };
        let id = plugin_texture_id(handle);
        if v2 {
            let mut params = TextureUpdateV2 {
                data: std::ptr::null_mut(),
                texture_id: id as isize,
                user_data,
                format,
                width,
                height,
                bytes_per_pixel,
            };
            callback(
                ext_event::UPDATE_TEXTURE_BEGIN_V2,
                &mut params as *mut _ as *mut c_void,
            );
            let data = params.data;
            self.write_update(&texture, data, width, height, bytes_per_pixel);
            callback(
                ext_event::UPDATE_TEXTURE_END_V2,
                &mut params as *mut _ as *mut c_void,
            );
        } else {
            let mut params = TextureUpdateV1 {
                data: std::ptr::null_mut(),
                user_data,
                texture_id: id,
                format,
                width,
                height,
                bytes_per_pixel,
            };
            callback(
                ext_event::UPDATE_TEXTURE_BEGIN_V1,
                &mut params as *mut _ as *mut c_void,
            );
            let data = params.data;
            self.write_update(&texture, data, width, height, bytes_per_pixel);
            callback(
                ext_event::UPDATE_TEXTURE_END_V1,
                &mut params as *mut _ as *mut c_void,
            );
        }
    }

    /// Writes a plugin's texels (RGBA8, or RGB8 widened with opaque alpha) into level 0.
    fn write_update(
        &mut self,
        texture: &wgpu::Texture,
        data: *mut c_void,
        width: u32,
        height: u32,
        bytes_per_pixel: u32,
    ) {
        if data.is_null() {
            return;
        }
        if !update_writable(texture.format()) {
            let format = texture.format();
            self.warn_once(format!("plugin-update-format{format:?}"), || {
                format!(
                    "a plugin texture update of a texture stored as {format:?} is not supported \
                     (8-bit RGBA only): its data is not written"
                )
            });
            return;
        }
        let length = (width * height * bytes_per_pixel) as usize;
        // SAFETY: the plugin's data for the texture's first level, in the format and size the
        // begin event announced; it stays valid until the end event.
        let texels = unsafe { std::slice::from_raw_parts(data as *const u8, length) };
        let widened;
        let rgba = match bytes_per_pixel {
            4 => texels,
            3 => {
                widened = texels
                    .chunks_exact(3)
                    .flat_map(|t| [t[0], t[1], t[2], 255])
                    .collect::<Vec<u8>>();
                &widened
            }
            other => {
                self.warn_once(format!("plugin-update-bpp{other}"), || {
                    format!("a plugin texture update of {other} bytes per pixel is not supported")
                });
                return;
            }
        };
        self.queue.write_texture(
            wgpu::TexelCopyTextureInfo {
                texture,
                mip_level: 0,
                origin: wgpu::Origin3d::ZERO,
                aspect: wgpu::TextureAspect::All,
            },
            rgba,
            wgpu::TexelCopyBufferLayout {
                offset: 0,
                bytes_per_row: Some(width * 4),
                rows_per_image: Some(height),
            },
            wgpu::Extent3d {
                width,
                height,
                depth_or_array_layers: 1,
            },
        );
    }
}

/// Whether a plugin's texture update can be written into storage of this format (8-bit RGBA;
/// a compressed import's blocks are refused).
fn update_writable(format: wgpu::TextureFormat) -> bool {
    matches!(
        format,
        wgpu::TextureFormat::Rgba8Unorm | wgpu::TextureFormat::Rgba8UnormSrgb
    )
}

/// The texture ID a texture update gives the plugin: the renderer's handle (the managed side's
/// `NativePlugins.TextureId`).
fn plugin_texture_id(handle: u64) -> u32 {
    handle as u32
}

/// Deletes a shared texture's OpenGL textures.
fn delete_shared(uuid: Option<[u8; 16]>, shared: Shared) {
    let _ = gl_interop::with(uuid, |interop| {
        interop.delete(shared.color);
        if let Some(depth) = shared.depth {
            interop.delete(depth);
        }
    });
}

/// The packet that makes a texture of the same shape again (without texels).
fn descriptor(gpu: &GpuTexture) -> owned::Texture {
    owned::Texture {
        width: gpu.width as i32,
        height: gpu.height as i32,
        format: gpu.format,
        mip_count: gpu.mip_count as i32,
        filter_mode: gpu.sampler_key.filter,
        wrap_u: gpu.sampler_key.wrap_u,
        wrap_v: gpu.sampler_key.wrap_v,
        wrap_w: gpu.sampler_key.wrap_w,
        aniso_level: gpu.sampler_key.aniso,
        render_target: gpu.render_target,
        random_write: gpu.random_write,
        auto_generate_mips: gpu.auto_generate_mips,
        defer_mips: gpu.defer_mips,
        samples: gpu.samples,
        depth_bits: if gpu.depth_texture.is_some() { 24 } else { 0 },
        layers: 1,
        dimension: 2,
        ..Default::default()
    }
}

/// The OpenGL internal format of a renderer format, as Unity's OpenGL Core device stores the
/// texture: sRGB color textures (`format` 0) in a Linear color space project are
/// `GL_SRGB8_ALPHA8`, render textures `GL_RGBA8` (tests/projects/nativegfx `name ...`).
fn gl_format(
    texture: wgpu::TextureFormat,
    format: i32,
    color_space: i32,
    d24s8: bool,
) -> Option<u32> {
    use gl_interop::format as gl;
    use wgpu::TextureFormat as F;
    Some(match texture {
        F::Rgba8Unorm if format == 0 && color_space == 1 => gl::SRGB8_ALPHA8,
        F::Rgba8Unorm => gl::RGBA8,
        F::Rgba8UnormSrgb => gl::SRGB8_ALPHA8,
        F::Rgba16Float => gl::RGBA16F,
        F::Rgba32Float => gl::RGBA32F,
        F::R8Unorm => gl::R8,
        F::Rg8Unorm => gl::RG8,
        F::R16Float => gl::R16F,
        F::Rg16Float => gl::RG16F,
        F::R32Float => gl::R32F,
        F::Rg32Float => gl::RG32F,
        F::R16Unorm => gl::R16,
        F::Rg16Unorm => gl::RG16,
        F::Rgba16Unorm => gl::RGBA16,
        F::Rgb10a2Unorm => gl::RGB10_A2,
        F::Rg11b10Ufloat => gl::R11F_G11F_B10F,
        F::Depth32Float => gl::DEPTH_COMPONENT32F,
        F::Depth24PlusStencil8 if d24s8 => gl::DEPTH24_STENCIL8,
        F::Depth24PlusStencil8 => gl::DEPTH32F_STENCIL8,
        _ => return None,
    })
}

#[cfg(target_os = "linux")]
mod vulkan {
    //! Textures in memory OpenGL can import: Vulkan images with exportable dedicated memory
    //! (`VK_KHR_external_memory_fd`), made as wgpu-hal makes them and given to wgpu.
    use ash::vk;
    use std::os::fd::{FromRawFd, OwnedFd};
    use wgpu::hal::{api::Vulkan, vulkan as hal};

    /// Exported memory: its descriptor (OpenGL takes it) and size.
    pub struct Exported {
        pub fd: OwnedFd,
        pub size: u64,
    }

    /// The GPU's UUID and PCI vendor, and whether it stores Depth24PlusStencil8 as D24S8.
    pub fn device_info(device: &wgpu::Device) -> (Option<[u8; 16]>, Option<u32>, bool) {
        let Some(hal) = (unsafe { device.as_hal::<Vulkan>() }) else {
            return (None, None, true);
        };
        let instance = hal.shared_instance().raw_instance();
        let physical = hal.raw_physical_device();
        let mut id = vk::PhysicalDeviceIDProperties::default();
        let mut properties = vk::PhysicalDeviceProperties2::default().push_next(&mut id);
        unsafe { instance.get_physical_device_properties2(physical, &mut properties) };
        let vendor = properties.properties.vendor_id;
        let uuid = id.device_uuid;
        (Some(uuid), Some(vendor), d24s8(instance, physical))
    }

    /// wgpu-hal's choice for Depth24PlusStencil8: D24S8 where it can be sampled and drawn into.
    fn d24s8(instance: &ash::Instance, physical: vk::PhysicalDevice) -> bool {
        let properties = unsafe {
            instance.get_physical_device_format_properties(physical, vk::Format::D24_UNORM_S8_UINT)
        };
        properties.optimal_tiling_features.contains(
            vk::FormatFeatureFlags::SAMPLED_IMAGE
                | vk::FormatFeatureFlags::DEPTH_STENCIL_ATTACHMENT,
        )
    }

    fn vk_format(
        format: wgpu::TextureFormat,
        instance: &ash::Instance,
        physical: vk::PhysicalDevice,
    ) -> Option<vk::Format> {
        use wgpu::TextureFormat as F;
        Some(match format {
            F::Rgba8Unorm => vk::Format::R8G8B8A8_UNORM,
            F::Rgba8UnormSrgb => vk::Format::R8G8B8A8_SRGB,
            F::Rgba16Float => vk::Format::R16G16B16A16_SFLOAT,
            F::Rgba32Float => vk::Format::R32G32B32A32_SFLOAT,
            F::R8Unorm => vk::Format::R8_UNORM,
            F::Rg8Unorm => vk::Format::R8G8_UNORM,
            F::R16Float => vk::Format::R16_SFLOAT,
            F::Rg16Float => vk::Format::R16G16_SFLOAT,
            F::R32Float => vk::Format::R32_SFLOAT,
            F::Rg32Float => vk::Format::R32G32_SFLOAT,
            F::R16Unorm => vk::Format::R16_UNORM,
            F::Rg16Unorm => vk::Format::R16G16_UNORM,
            F::Rgba16Unorm => vk::Format::R16G16B16A16_UNORM,
            F::Rgb10a2Unorm => vk::Format::A2B10G10R10_UNORM_PACK32,
            F::Rg11b10Ufloat => vk::Format::B10G11R11_UFLOAT_PACK32,
            F::Depth32Float => vk::Format::D32_SFLOAT,
            F::Depth24PlusStencil8 if d24s8(instance, physical) => vk::Format::D24_UNORM_S8_UINT,
            F::Depth24PlusStencil8 => vk::Format::D32_SFLOAT_S8_UINT,
            _ => return None,
        })
    }

    /// A 2D texture as `desc` describes it, in exported dedicated memory.
    pub fn exported_texture(
        device: &wgpu::Device,
        desc: &wgpu::TextureDescriptor,
    ) -> Result<(wgpu::Texture, Exported), String> {
        if desc.dimension != wgpu::TextureDimension::D2
            || desc.size.depth_or_array_layers != 1
            || desc.sample_count != 1
        {
            return Err("only single-sample 2D textures are exported".into());
        }
        let hal_device =
            unsafe { device.as_hal::<Vulkan>() }.ok_or("the renderer does not draw with Vulkan")?;
        if !hal_device
            .enabled_device_extensions()
            .contains(&ash::khr::external_memory_fd::NAME)
        {
            return Err(
                "the Vulkan device cannot export memory (VK_KHR_external_memory_fd)".into(),
            );
        }
        let raw = hal_device.raw_device();
        let instance = hal_device.shared_instance().raw_instance();
        let physical = hal_device.raw_physical_device();
        let format = vk_format(desc.format, instance, physical)
            .ok_or_else(|| format!("the format {:?} is not exported", desc.format))?;
        let depth = desc.format.is_depth_stencil_format();
        let mut usage = vk::ImageUsageFlags::TRANSFER_SRC
            | vk::ImageUsageFlags::TRANSFER_DST
            | vk::ImageUsageFlags::SAMPLED;
        if desc.usage.contains(wgpu::TextureUsages::RENDER_ATTACHMENT) {
            usage |= if depth {
                vk::ImageUsageFlags::DEPTH_STENCIL_ATTACHMENT
            } else {
                vk::ImageUsageFlags::COLOR_ATTACHMENT
            };
        }
        if desc.usage.contains(wgpu::TextureUsages::STORAGE_BINDING) {
            usage |= vk::ImageUsageFlags::STORAGE;
        }
        let flags = if desc.view_formats.is_empty() {
            vk::ImageCreateFlags::empty()
        } else {
            vk::ImageCreateFlags::MUTABLE_FORMAT
        };
        let mut external = vk::ExternalMemoryImageCreateInfo::default()
            .handle_types(vk::ExternalMemoryHandleTypeFlags::OPAQUE_FD);
        // The formats the renderer views it in, listed as wgpu-hal lists them (with its own).
        let mut view_formats = desc
            .view_formats
            .iter()
            .filter_map(|f| vk_format(*f, instance, physical))
            .collect::<Vec<_>>();
        view_formats.push(format);
        let mut format_list = vk::ImageFormatListCreateInfo::default().view_formats(&view_formats);
        let mut info = vk::ImageCreateInfo::default()
            .flags(flags)
            .image_type(vk::ImageType::TYPE_2D)
            .format(format)
            .extent(vk::Extent3D {
                width: desc.size.width,
                height: desc.size.height,
                depth: 1,
            })
            .mip_levels(desc.mip_level_count)
            .array_layers(1)
            .samples(vk::SampleCountFlags::TYPE_1)
            .tiling(vk::ImageTiling::OPTIMAL)
            .usage(usage)
            .sharing_mode(vk::SharingMode::EXCLUSIVE)
            .initial_layout(vk::ImageLayout::UNDEFINED)
            .push_next(&mut external);
        if !desc.view_formats.is_empty() {
            info = info.push_next(&mut format_list);
        }
        let image = unsafe { raw.create_image(&info, None) }
            .map_err(|e| format!("an exportable image cannot be made: {e}"))?;
        let requirements = unsafe { raw.get_image_memory_requirements(image) };
        let properties = unsafe { instance.get_physical_device_memory_properties(physical) };
        let Some(memory_type) = (0..properties.memory_type_count).find(|&i| {
            requirements.memory_type_bits & (1 << i) != 0
                && properties.memory_types[i as usize]
                    .property_flags
                    .contains(vk::MemoryPropertyFlags::DEVICE_LOCAL)
        }) else {
            unsafe { raw.destroy_image(image, None) };
            return Err("no device memory type holds the image".into());
        };
        let mut dedicated = vk::MemoryDedicatedAllocateInfo::default().image(image);
        let mut export = vk::ExportMemoryAllocateInfo::default()
            .handle_types(vk::ExternalMemoryHandleTypeFlags::OPAQUE_FD);
        let allocation = vk::MemoryAllocateInfo::default()
            .allocation_size(requirements.size)
            .memory_type_index(memory_type)
            .push_next(&mut export)
            .push_next(&mut dedicated);
        let memory = match unsafe { raw.allocate_memory(&allocation, None) } {
            Ok(memory) => memory,
            Err(e) => {
                unsafe { raw.destroy_image(image, None) };
                return Err(format!("exportable memory cannot be allocated: {e}"));
            }
        };
        let fd = unsafe { raw.bind_image_memory(image, memory, 0) }.and_then(|()| {
            let loader = ash::khr::external_memory_fd::Device::new(instance, raw);
            unsafe {
                loader.get_memory_fd(
                    &vk::MemoryGetFdInfoKHR::default()
                        .memory(memory)
                        .handle_type(vk::ExternalMemoryHandleTypeFlags::OPAQUE_FD),
                )
            }
        });
        let fd = match fd {
            Ok(fd) => unsafe { OwnedFd::from_raw_fd(fd) },
            Err(e) => {
                unsafe {
                    raw.destroy_image(image, None);
                    raw.free_memory(memory, None);
                }
                return Err(format!("the memory cannot be exported: {e}"));
            }
        };
        let hal_desc = wgpu::hal::TextureDescriptor {
            label: desc.label,
            size: desc.size,
            mip_level_count: desc.mip_level_count,
            sample_count: 1,
            dimension: desc.dimension,
            format: desc.format,
            usage: wgpu::TextureUses::empty(),
            memory_flags: wgpu::hal::MemoryFlags::empty(),
            view_formats: desc.view_formats.to_vec(),
        };
        // SAFETY: the image was made as `desc` describes (MUTABLE_FORMAT with view formats), and
        // wgpu-hal owns it and its dedicated memory from now on.
        let texture = unsafe {
            let hal_texture = hal_device.texture_from_raw(
                image,
                &hal_desc,
                None,
                hal::TextureMemory::Dedicated(memory),
            );
            drop(hal_device);
            device.create_texture_from_hal::<Vulkan>(
                hal_texture,
                desc,
                wgpu::TextureUses::UNINITIALIZED,
            )
        };
        Ok((
            texture,
            Exported {
                fd,
                size: requirements.size,
            },
        ))
    }
}

#[cfg(not(target_os = "linux"))]
mod vulkan {
    use std::os::fd::OwnedFd;

    pub struct Exported {
        pub fd: OwnedFd,
        pub size: u64,
    }

    pub fn device_info(_: &wgpu::Device) -> (Option<[u8; 16]>, Option<u32>, bool) {
        (None, None, true)
    }

    pub fn exported_texture(
        _: &wgpu::Device,
        _: &wgpu::TextureDescriptor,
    ) -> Result<(wgpu::Texture, Exported), String> {
        Err("textures are shared with native plugins on Linux only".into())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::render::gl_interop::format as gl;
    use wgpu::TextureFormat as F;

    #[test]
    fn texture_updates_write_only_8_bit_rgba_storage() {
        assert!(update_writable(F::Rgba8Unorm));
        assert!(update_writable(F::Rgba8UnormSrgb));
        // A compressed import whose managed format is RGBA32: its blocks are not overwritten.
        assert!(!update_writable(F::Bc7RgbaUnorm));
        assert!(!update_writable(F::Bc7RgbaUnormSrgb));
        assert!(!update_writable(F::Rgba16Float));
    }

    #[test]
    fn opengl_formats_follow_the_devices_storage() {
        // sRGB color textures are sRGB on the device in a Linear project only.
        assert_eq!(gl_format(F::Rgba8Unorm, 0, 1, true), Some(gl::SRGB8_ALPHA8));
        assert_eq!(gl_format(F::Rgba8Unorm, 0, 0, true), Some(gl::RGBA8));
        assert_eq!(gl_format(F::Rgba8Unorm, 1, 1, true), Some(gl::RGBA8));
        // Depth as wgpu-hal stores it on this device.
        assert_eq!(
            gl_format(F::Depth24PlusStencil8, -1, 0, true),
            Some(gl::DEPTH24_STENCIL8)
        );
        assert_eq!(
            gl_format(F::Depth24PlusStencil8, -1, 0, false),
            Some(gl::DEPTH32F_STENCIL8)
        );
        // Nothing to import as: refused.
        assert_eq!(gl_format(F::Bc7RgbaUnorm, 7, 0, true), None);
        assert_eq!(gl_format(F::Bgra8Unorm, 1, 0, true), None);
    }

    #[test]
    fn plugin_parameter_blocks_have_the_headers_layout() {
        // UnityRenderingExtTextureUpdateParamsV1/V2 and UnityRenderingExtCustomBlitParams on a
        // 64-bit target: the data pointer first, V2's texture ID pointer-sized after it.
        assert_eq!(size_of::<TextureUpdateV1>(), 32);
        assert_eq!(std::mem::offset_of!(TextureUpdateV1, texture_id), 12);
        assert_eq!(size_of::<TextureUpdateV2>(), 40);
        assert_eq!(std::mem::offset_of!(TextureUpdateV2, user_data), 16);
        assert_eq!(std::mem::offset_of!(TextureUpdateV2, bytes_per_pixel), 32);
        assert_eq!(size_of::<CustomBlitParams>(), 32);
        assert_eq!(std::mem::offset_of!(CustomBlitParams, destination), 8);
        assert_eq!(std::mem::offset_of!(CustomBlitParams, command), 16);
    }
}
