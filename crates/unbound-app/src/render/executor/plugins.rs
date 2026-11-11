//! Native rendering plugins in the renderer: their callbacks in stream order
//! (`Command::Plugin`), the OpenGL textures they see for the renderer's
//! (`Texture.GetNativeTexturePtr`), and textures that show a plugin's own
//! (`Texture2D.CreateExternalTexture`). Behaviour matches the Unity 6000.0.71f1 Editor on OpenGL
//! Core (tests/projects/nativegfx); see `gl_interop` for the plugins' OpenGL context, which
//! shares the renderer's objects.
//!
//! A callback runs when the work before it is done on the GPU, on the caller's thread (the
//! game's: the Editor runs them on its main thread), with the current target bound in OpenGL:
//! its color texture's level, its depth texture and the viewport. The screen is not shared: a
//! callback while it is the target finds a framebuffer without attachments (logged once).
//! Nothing here runs, and nothing is allocated, while no plugin calls the renderer.
use super::{UnityRenderer, clamp_viewport, depth_owner};
use crate::render::gl_interop;
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

/// A renderer texture native plugins were given, by its handle: the storage they saw (the
/// texture's current one while they are the same) and its OpenGL names.
struct Native {
    texture: wgpu::Texture,
    /// The color's name: the storage's, or a view of it made here (`view`) in the format Unity's
    /// device stores the texture in.
    color: u32,
    view: bool,
    depth: Option<u32>,
}

#[derive(Default)]
pub(super) struct PluginState {
    natives: HashMap<u64, Native>,
    /// Textures that could not be shared, with the storage that was refused and why (not tried
    /// again until the storage changes).
    refused: HashMap<u64, (wgpu::Texture, String)>,
    /// Textures showing another texture's storage (`CreateExternalTexture` of its pointer).
    pub aliases: HashSet<u64>,
    /// The current target's mip and slice (`SetTarget`).
    pub target: (i32, i32),
}

/// The callback's function types (the plugin headers' `UnityRenderingEvent`,
/// `UnityRenderingEventAndData`).
type RenderingEvent = extern "C" fn(i32);
type RenderingEventAndData = extern "C" fn(i32, *mut c_void);

/// The format Unity's OpenGL Core device stores a texture in where that is not the renderer's
/// storage format `stored` (None: it is): an sRGB color texture (`srgb`) of a Linear color space
/// project (`color_space` 1) is sRGB there, `GL_SRGB8_ALPHA8` (tests/projects/nativegfx
/// `name ...`); in a Gamma project no texture is (unmeasured: the probe's project is Linear).
fn device_format(
    stored: gl_interop::InternalFormat,
    srgb: bool,
    color_space: i32,
) -> Option<gl_interop::InternalFormat> {
    (srgb && color_space == 1)
        .then(|| gl_interop::format::srgb(stored))
        .flatten()
}

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
    /// The OpenGL texture name of a texture (`depth`: its depth buffer's), 0 when it cannot be
    /// shared (logged once per texture).
    pub fn native_texture(&mut self, handle: u64, depth: bool) -> u64 {
        #[cfg(target_os = "linux")]
        if self.vulkan_plugins.is_some() {
            return self.vulkan_native_texture(handle, depth);
        }
        match self.share(handle) {
            Ok((color, depth_name)) => {
                if depth {
                    depth_name.map_or(0, u64::from)
                } else {
                    color as u64
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

    /// The texture's OpenGL names (color, depth), as plugins see them.
    fn share(&mut self, handle: u64) -> Result<(u32, Option<u32>), String> {
        let gpu = self
            .textures
            .get(&handle)
            .ok_or_else(|| format!("no texture {handle}"))?;
        if let Some(native) = self.plugins.natives.get(&handle)
            && native.texture == gpu.texture
        {
            return Ok((native.color, native.depth));
        }
        if let Some((texture, reason)) = self.plugins.refused.get(&handle)
            && *texture == gpu.texture
        {
            return Err(reason.clone());
        }
        if let Some(stale) = self.plugins.natives.remove(&handle) {
            forget(stale);
        }
        let texture = gpu.texture.clone();
        match self.native_names(handle) {
            Ok(native) => {
                let names = (native.color, native.depth);
                self.plugins.natives.insert(handle, native);
                Ok(names)
            }
            Err(reason) => {
                self.plugins
                    .refused
                    .insert(handle, (texture, reason.clone()));
                Err(reason)
            }
        }
    }

    /// The OpenGL names of a texture not shared yet: its storage's, else, where Unity's device
    /// stores the texture in another format than the renderer (sRGB color textures of a Linear
    /// project: `GL_SRGB8_ALPHA8`, tests/projects/nativegfx `name ...`), a view of it in that
    /// format.
    fn native_names(&mut self, handle: u64) -> Result<Native, String> {
        let gpu = &self.textures[&handle];
        if gpu.samples > 1 || gpu.layers > 1 || gpu.volume || gpu.cube || gpu.cube_array {
            return Err("only single-sample 2D textures are shared".into());
        }
        let storage = gl_name(&gpu.texture)?;
        let depth = gpu.depth_texture.as_ref().map(gl_name).transpose()?;
        let (srgb, color_space) = (gpu.srgb_view.is_some(), self.color_space);
        let texture = gpu.texture.clone();
        gl_interop::with(|interop| {
            let info = interop
                .texture_info(storage)
                .ok_or("its OpenGL texture is not in the plugins' share group")?;
            let format = device_format(info.format, srgb, color_space);
            let (color, view) = match format {
                Some(format) => (interop.view(storage, info, format)?, true),
                None => (storage, false),
            };
            Ok(Native {
                texture,
                color,
                view,
                depth,
            })
        })?
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
            .natives
            .get(&handle)
            .is_some_and(|native| native.texture != *texture)
        {
            forget(self.plugins.natives.remove(&handle).expect("checked"));
        }
    }

    /// A texture was released: what plugins saw of it goes (a plugin's own texture stays its
    /// own).
    pub(super) fn release_plugin_texture(&mut self, handle: u64) {
        #[cfg(target_os = "linux")]
        if let Some(plugins) = self.vulkan_plugins.as_mut() {
            plugins.release(handle);
        }
        self.plugins.refused.remove(&handle);
        self.plugins.aliases.remove(&handle);
        if let Some(native) = self.plugins.natives.remove(&handle) {
            forget(native);
        }
    }

    /// Makes `texture` show `native`: another texture's OpenGL name (the same storage), or a
    /// plugin's own 2D texture, sampled as it is: in its own format, as OpenGL samples it (the
    /// `linear` argument does not reinterpret it; unmeasured for sRGB arguments).
    pub fn external_texture(
        &mut self,
        handle: u64,
        native: u64,
        width: u32,
        height: u32,
        _srgb: bool,
    ) -> Result<(), String> {
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
            .natives
            .iter()
            .find(|(_, native)| native.color == name)
            .map(|(&source, native)| (source, native.texture.clone()));
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
        // A plugin's texture: the renderer samples it (its 8-bit RGBA level 0).
        let info = gl_interop::with(|interop| interop.texture_info(name))?
            .ok_or_else(|| format!("{name} is not an OpenGL texture of the plugins' context"))?;
        if info.target != gl_interop::TEXTURE_2D
            || !matches!(
                info.format,
                gl_interop::format::RGBA8 | gl_interop::format::SRGB8_ALPHA8
            )
        {
            return Err(format!(
                "texture {name} is not shown: only 2D textures of 8-bit RGBA texels are"
            ));
        }
        let packet = owned::Texture {
            width: width as i32,
            height: height as i32,
            // Sampled through its own format only (no views in another).
            format: 1,
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
        self.generation += 1;
        let gpu = resources::upload_texture_with(
            &device,
            &self.queue,
            None,
            &packet,
            self.generation,
            &mut |desc| plugin_texture(&device, name, desc),
        );
        let previous = self
            .textures
            .remove(&handle)
            .map(|old| (old.texture.clone(), old.sampler_key));
        self.install_texture(handle, gpu, previous, &packet);
        self.plugins.aliases.remove(&handle);
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
        let context = gl_interop::with(|_| ());
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
            self.bind_plugin_target();
            let _ = gl_interop::with(|interop| interop.clear_errors());
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
            self.after_plugin_callback();
        }
    }

    /// The plugin's OpenGL work is done before the renderer goes on; errors it left are logged
    /// as the Editor logs them.
    fn after_plugin_callback(&mut self) {
        let errors = gl_interop::with(|interop| {
            interop.finish();
            interop.clear_errors()
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
    fn bind_plugin_target(&mut self) {
        let (color, depth) = self.target_handles;
        let (mip, _) = self.plugins.target;
        let mut bound = None;
        if color == 0 {
            self.warn_once("plugin-screen".into(), || {
                "a native plugin callback while the screen is the target finds no target bound (the screen is not shared with plugins)".into()
            });
        } else if self.textures.contains_key(&color) {
            match self.share(color) {
                Ok((color_name, own_depth)) => {
                    let attached = match depth_owner(color, depth) {
                        Some(owner) if owner == color => own_depth,
                        Some(owner) => self.share(owner).ok().and_then(|(_, d)| d),
                        None => None,
                    };
                    bound = Some((color_name, attached));
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
        let _ = gl_interop::with(|interop| match bound {
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

/// What plugins saw of a texture goes: the framebuffers made of its names, and a view made of
/// it.
fn forget(native: Native) {
    let _ = gl_interop::with(|interop| {
        interop.forget(native.color, native.view);
        if let Some(depth) = native.depth {
            interop.forget(depth, false);
        }
    });
}

/// The OpenGL texture name of a renderer texture (wgpu's OpenGL backend).
#[cfg(target_os = "linux")]
fn gl_name(texture: &wgpu::Texture) -> Result<u32, String> {
    // SAFETY: the hal texture is only read, while the wgpu texture lives.
    match unsafe { texture.as_hal::<wgpu::hal::api::Gles>() }.map(|hal| hal.inner.clone()) {
        Some(wgpu::hal::gles::TextureInner::Texture { raw, .. }) => Ok(raw.0.get()),
        Some(_) => Err("it is not an OpenGL texture (a render buffer)".into()),
        None => Err("the renderer does not draw with OpenGL".into()),
    }
}

#[cfg(not(target_os = "linux"))]
fn gl_name(_: &wgpu::Texture) -> Result<u32, String> {
    Err("textures are shared with native plugins on Linux only".into())
}

/// A renderer texture over a plugin's own OpenGL texture `name` (2D, `desc`'s shape): the
/// renderer samples it and never deletes it.
#[cfg(target_os = "linux")]
fn plugin_texture(
    device: &wgpu::Device,
    name: u32,
    desc: &wgpu::TextureDescriptor,
) -> wgpu::Texture {
    use wgpu::hal::api::Gles;
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
    let wrapped = std::num::NonZeroU32::new(name).and_then(|name| {
        // SAFETY: `name` is a 2D texture of the share group (checked by the caller) of `desc`'s
        // shape; the plugin owns it (the drop callback leaves it to the plugin).
        let hal = unsafe { device.as_hal::<Gles>() }?;
        Some(unsafe { hal.texture_from_raw(name, &hal_desc, Some(Box::new(|| {}))) })
    });
    match wrapped {
        Some(hal_texture) => unsafe {
            device.create_texture_from_hal::<Gles>(
                hal_texture,
                desc,
                wgpu::TextureUses::UNINITIALIZED,
            )
        },
        None => device.create_texture(desc),
    }
}

#[cfg(not(target_os = "linux"))]
fn plugin_texture(device: &wgpu::Device, _: u32, desc: &wgpu::TextureDescriptor) -> wgpu::Texture {
    device.create_texture(desc)
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

#[cfg(test)]
mod tests {
    use super::*;
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
    fn plugins_see_srgb_color_textures_as_the_device_stores_them() {
        use crate::render::gl_interop::format as gl;
        // sRGB color textures are sRGB on the device in a Linear project (measured); a Gamma
        // project's stay as stored (Unbound's choice, unmeasured).
        assert_eq!(device_format(gl::RGBA8, true, 1), Some(gl::SRGB8_ALPHA8));
        assert_eq!(device_format(gl::RGBA8, true, 0), None);
        assert_eq!(device_format(gl::RGBA8, false, 1), None);
        // Stored sRGB already: the storage itself.
        assert_eq!(device_format(gl::SRGB8_ALPHA8, true, 1), None);
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
