//! Native rendering plugins in the renderer: their callbacks in stream order
//! (`Command::Plugin`), the OpenGL textures they see for the renderer's
//! (`Texture.GetNativeTexturePtr`), and textures that show a plugin's own
//! (`Texture2D.CreateExternalTexture`). Behaviour matches the Unity 6000.0.71f1 Editor on OpenGL
//! Core (tests/projects/nativegfx); see `gl_interop` for the plugins' OpenGL context, which
//! shares the renderer's objects.
//!
//! A texture's name is a texture of its shape's target (2D, cube, array, 3D, cube array) in the
//! format Unity's device stores it in; a multisampled render texture's is its resolved image,
//! which shaders sample.
//!
//! A callback runs when the work before it is done on the GPU, on the caller's thread (the
//! game's: the Editor runs them on its main thread), with the current target bound in OpenGL:
//! its color texture's level, its depth texture and the viewport; a multisampled target's
//! samples, which are resolved again after the callback. The screen is not shared: a callback
//! while it is the target finds a framebuffer without attachments (logged once).
//! Nothing here runs, and nothing is allocated, while no plugin calls the renderer.
use super::{UnityRenderer, clamp_viewport, depth_owner};
use crate::render::gl_interop;
use crate::render::resources::{self, GpuTexture};
use rustc_hash::{FxHashMap as HashMap, FxHashSet as HashSet};
use std::ffi::c_void;
use unbound_runtime::plugin_api;
use unbound_runtime::render_commands::PluginCall;
use unbound_runtime::render_protocol::{owned, texture_format};

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

/// The OpenGL names of a renderer texture, as plugins see them.
#[derive(Clone, Copy)]
struct Names {
    /// The color's: the texture's, or a multisampled target's resolved image's.
    color: u32,
    /// A single-sample depth buffer's.
    depth: Option<u32>,
    /// A multisampled target's samples: its color's and its depth buffer's.
    samples: Option<(u32, Option<u32>)>,
}

/// A renderer texture native plugins were given, by its handle: the storage they saw (the
/// texture's current one while they are the same) and its OpenGL names.
struct Native {
    texture: wgpu::Texture,
    /// The texture the color names: the storage, or a multisampled target's resolved image.
    named: wgpu::Texture,
    names: Names,
    /// The names that are views made here, where Unity's device has the texture with another
    /// target or in another format than the renderer's storage: they go with the texture.
    views: [Option<u32>; 2],
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
    /// The callback running found a multisampled target's samples bound: they are resolved
    /// again after it.
    pub drew_into_samples: bool,
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

/// A texture's shape, as its OpenGL target follows from it.
#[derive(Clone, Copy)]
struct Shape {
    cube: bool,
    cube_array: bool,
    volume: bool,
    array: bool,
    layers: u32,
}

impl Shape {
    fn of(gpu: &GpuTexture) -> Self {
        Shape {
            cube: gpu.cube,
            cube_array: gpu.cube_array,
            volume: gpu.volume,
            array: gpu.array,
            layers: gpu.layers,
        }
    }

    /// A render target's depth buffer of this shape: a layer for each of the color's, but a
    /// 3D target's is one image.
    fn depth(self) -> Self {
        Shape {
            volume: false,
            layers: if self.volume { 1 } else { self.layers },
            ..self
        }
    }

    /// The OpenGL target and the layers a view of all of the texture has: Unity's device makes
    /// a Cubemap a cube map, a CubemapArray a cube map array, a texture array a 2D array
    /// whatever its size (one layer, six square ones), a Texture3D a 3D texture
    /// (tests/projects/nativegfx `shape.*`).
    fn target(self) -> (u32, u32) {
        if self.cube {
            (gl_interop::TEXTURE_CUBE_MAP, 6)
        } else if self.cube_array {
            (gl_interop::TEXTURE_CUBE_MAP_ARRAY, self.layers)
        } else if self.volume {
            (gl_interop::TEXTURE_3D, 1)
        } else if self.array || self.layers > 1 {
            (gl_interop::TEXTURE_2D_ARRAY, self.layers)
        } else {
            (gl_interop::TEXTURE_2D, 1)
        }
    }

    /// Whether a target of this shape is drawn an image at a time (a face, a layer, a slice).
    fn layered(self) -> bool {
        self.target().0 != gl_interop::TEXTURE_2D
    }
}

/// The texture packet format a plugin's texture of an OpenGL internal format is described by:
/// the formats shaders sample as floats, which the renderer samples through the plugin's own
/// texture (so as OpenGL samples it: an sRGB format decoded, three channels with alpha 1). The
/// description only sizes copies of it; a color code would ask for a view in another format,
/// which a plugin's texture cannot have. Measured in the Editor (tests/projects/nativegfx
/// `ext.*`): `GL_RGBA8`, `GL_SRGB8_ALPHA8`, `GL_R8`, `GL_RG8`, `GL_RGB8`, `GL_RGBA16F`,
/// `GL_RGBA32F`, DXT5 and the unsized `GL_RGBA` (which the texture reports as it was asked
/// for); the others follow from the rule (OpenGL samples the texture itself). A format of
/// three channels is described by the one of four.
/// None: a format that is not shown (integers and depth, which shaders do not sample as
/// colors; formats the packet has no code for: DXT3, 16-bit signed normalized ones, the
/// signed blocks).
fn external_format(format: gl_interop::InternalFormat) -> Option<i32> {
    use gl_interop::format as gl;
    use unbound_runtime::render_protocol::texture_format as code;
    Some(match format {
        gl::RGBA8 | gl::SRGB8_ALPHA8 | gl::RGB8 | gl::SRGB8 => code::LINEAR_RGBA8,
        gl::RGBA | gl::SRGB_ALPHA | gl::RGB | gl::SRGB => code::LINEAR_RGBA8,
        gl::R8 | gl::RED => code::R8_UNORM,
        gl::RG8 | gl::RG => code::RG8_UNORM,
        gl::R8_SNORM => code::R8_SNORM,
        gl::RG8_SNORM => code::RG8_SNORM,
        gl::RGBA8_SNORM | gl::RGB8_SNORM => code::RGBA8_SNORM,
        gl::R16 => code::R16_UNORM,
        gl::RG16 => code::RG16_UNORM,
        gl::RGBA16 | gl::RGB16 => code::RGBA16_UNORM,
        gl::RGB10_A2 => code::RGB10A2_UNORM,
        gl::R11F_G11F_B10F => code::RG11B10_FLOAT,
        gl::R16F => code::R16_FLOAT,
        gl::RG16F => code::RG16_FLOAT,
        gl::RGBA16F | gl::RGB16F => code::RGBA_HALF,
        gl::R32F => code::R32_FLOAT,
        gl::RG32F => code::RG32_FLOAT,
        gl::RGBA32F | gl::RGB32F => code::RGBA32_FLOAT,
        gl::COMPRESSED_RGB_S3TC_DXT1
        | gl::COMPRESSED_RGBA_S3TC_DXT1
        | gl::COMPRESSED_SRGB_S3TC_DXT1
        | gl::COMPRESSED_SRGB_ALPHA_S3TC_DXT1 => code::BC1_DATA,
        gl::COMPRESSED_RGBA_S3TC_DXT5 | gl::COMPRESSED_SRGB_ALPHA_S3TC_DXT5 => code::BC3_DATA,
        gl::COMPRESSED_RED_RGTC1 => code::BC4,
        gl::COMPRESSED_RG_RGTC2 => code::BC5,
        gl::COMPRESSED_RGB_BPTC_UNSIGNED_FLOAT => code::BC6H,
        gl::COMPRESSED_RGBA_BPTC_UNORM | gl::COMPRESSED_SRGB_ALPHA_BPTC_UNORM => code::BC7_DATA,
        _ => return None,
    })
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
    /// The OpenGL texture name of a texture, 0 when it cannot be shared (logged once per
    /// texture). `depth`: what a render texture's depth buffer pointer names
    /// ([`Self::depth_pointer`]).
    pub fn native_texture(&mut self, handle: u64, depth: bool) -> u64 {
        #[cfg(target_os = "linux")]
        if self.vulkan_plugins.is_some() {
            return self.vulkan_native_texture(handle, depth);
        }
        let shared = self.share(handle).and_then(|names| {
            if depth || (self.textures[&handle].format == 5 && !self.textures[&handle].cube) {
                self.depth_pointer(handle, names)
            } else {
                Ok(if self.textures[&handle].bind_ms {
                    names.samples.map_or(names.color, |s| s.0)
                } else {
                    names.color
                })
            }
        });
        match shared {
            Ok(name) => name as u64,
            Err(reason) => {
                self.warn_once(format!("plugin-share{handle}{depth}"), || {
                    format!("texture {handle} is not shared with native plugins: {reason}")
                });
                0
            }
        }
    }

    /// Native depth names the published depth image, or the samples when bindTextureMS is set.
    /// Without depth, OpenGL returns the multisampled color image (tests/projects/nativegfx).
    fn depth_pointer(&self, handle: u64, names: Names) -> Result<u32, String> {
        let gpu = &self.textures[&handle];
        if gpu.bind_ms {
            return Ok(names
                .samples
                .map_or(names.depth.unwrap_or(names.color), |(color, depth)| {
                    depth.unwrap_or(color)
                }));
        }
        match names.samples {
            None if gpu.cube => Ok(names.color),
            None => Ok(names.depth.unwrap_or(names.color)),
            Some((samples, None)) => Ok(samples),
            Some((_, Some(_))) => names
                .depth
                .ok_or_else(|| "the layered multisampled depth image cannot be shared".into()),
        }
    }

    /// The texture a texture's name names: its storage, or the resolved image of a
    /// multisampled target that keeps one (what shaders sample).
    fn named_texture(&self, gpu: &GpuTexture) -> wgpu::Texture {
        self.resolve_history
            .get(&gpu.texture)
            .map_or(&gpu.texture, |history| &history.texture)
            .clone()
    }

    /// The texture's OpenGL names, as plugins see them.
    fn share(&mut self, handle: u64) -> Result<Names, String> {
        let gpu = self
            .textures
            .get(&handle)
            .ok_or_else(|| format!("no texture {handle}"))?;
        let named = self.named_texture(gpu);
        if let Some(native) = self.plugins.natives.get(&handle)
            && native.texture == gpu.texture
            && native.named == named
        {
            return Ok(native.names);
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
        match self.native_names(handle, named) {
            Ok(native) => {
                let names = native.names;
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

    /// The OpenGL names of a texture not shared yet: its storage's (`named`), else, where
    /// Unity's device has the texture with another target or in another format than the
    /// renderer's storage, a view of it with them: a square array of six layers is a cube map
    /// in the renderer's backend, an sRGB color texture of a Linear project `GL_SRGB8_ALPHA8`
    /// on Unity's device, a multisampled target's samples too (tests/projects/nativegfx
    /// `name ...`, `shape.*`, `shape.msaa.srgb.target.color`).
    fn native_names(&mut self, handle: u64, named: wgpu::Texture) -> Result<Native, String> {
        let gpu = &self.textures[&handle];
        let storage = gl_name(&named)?;
        let depth = gpu.depth_texture.as_ref().map(gl_name).transpose()?;
        let samples = gpu.msaa.as_ref().map(|(samples, _)| samples.texture());
        let samples = samples.map(gl_name).transpose()?;
        let (srgb, color_space) = (gpu.srgb_view.is_some(), self.color_space);
        let shape = Shape::of(gpu);
        let texture = gpu.texture.clone();
        gl_interop::with(|interop| {
            let mut views = [None; 2];
            let names = {
                // A texture's name with a target (None: its storage's, with one layer) and, a
                // color's, in the device's format: a view, kept in `views`, where they differ.
                let mut name = |slot: usize,
                                name: u32,
                                target: Option<(u32, u32)>,
                                color: bool|
                 -> Result<u32, String> {
                    let info = interop
                        .texture_info(name)
                        .ok_or("its OpenGL texture is not in the plugins' share group")?;
                    let (target, layers) = target.unwrap_or((info.target, 1));
                    let format = color
                        .then(|| device_format(info.format, srgb, color_space))
                        .flatten()
                        .unwrap_or(info.format);
                    if format == info.format && target == info.target {
                        return Ok(name);
                    }
                    let view = interop.view(name, info, target, format, layers)?;
                    views[slot] = Some(view);
                    Ok(view)
                };
                name(0, storage, Some(shape.target()), true).and_then(|color| {
                    Ok(match samples {
                        // A multisampled target: its samples, and their depth buffer as it is.
                        Some(samples) => Names {
                            color,
                            depth: gpu
                                .native_depth
                                .as_ref()
                                .map(|r| gl_name(r.view.texture()))
                                .transpose()?,
                            samples: Some((name(1, samples, None, true)?, depth)),
                        },
                        None => Names {
                            color,
                            depth: depth
                                .map(|depth| name(1, depth, Some(shape.depth().target()), false))
                                .transpose()?,
                            samples: None,
                        },
                    })
                })
            };
            match names {
                Ok(names) => Ok(Native {
                    texture,
                    named,
                    names,
                    views,
                }),
                Err(reason) => {
                    for view in views.into_iter().flatten() {
                        interop.forget(view, true);
                    }
                    Err(reason)
                }
            }
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
    /// plugin's own 2D texture, sampled as OpenGL samples it: in its own format whatever the
    /// external texture declares (sRGB texels are decoded, 8-bit ones declared sRGB are not),
    /// and through `mips` of its levels (a texture declared without a mip chain shows its first
    /// level only). Matches tests/projects/nativegfx `external.*`, `ext.*`.
    pub fn external_texture(&mut self, handle: u64, native: u64, mips: u32) -> Result<(), String> {
        #[cfg(target_os = "linux")]
        if self.vulkan_plugins.is_some() {
            return Err(
                "external textures are not supported on the Vulkan device (unmeasured)".into(),
            );
        }
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
            .find(|(_, native)| native.names.color == name)
            .map(|(&source, native)| (source, native.named.clone()));
        if let Some((source, texture)) = source {
            let shown = &self.textures[&source];
            if Shape::of(shown).layered() {
                return Err(format!(
                    "texture {name} is not shown: a 2D texture cannot show a cube, array or 3D texture"
                ));
            }
            // A sampled texture over the source's storage: none of its target state (depth,
            // multisampling, automatic mips), which stays the source's.
            let packet = owned::Texture {
                render_target: false,
                random_write: false,
                auto_generate_mips: false,
                defer_mips: false,
                depth_bits: 0,
                samples: 1,
                ..descriptor(shown)
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
        // A plugin's texture: the renderer samples the texture itself.
        let info = gl_interop::with(|interop| interop.texture_info(name))?
            .ok_or_else(|| format!("{name} is not an OpenGL texture of the plugins' context"))?;
        if info.target != gl_interop::TEXTURE_2D {
            return Err(format!("texture {name} is not shown: only 2D textures are"));
        }
        let format = external_format(info.format).ok_or_else(|| {
            format!(
                "texture {name} is not shown: its format (OpenGL internal format {:#x}) is an integer or depth format, or one the renderer has no description for",
                info.format
            )
        })?;
        if texture_format::block_compressed(format)
            && (info.width % 4 != 0
                || info.height % 4 != 0
                || !device
                    .features()
                    .contains(wgpu::Features::TEXTURE_COMPRESSION_BC))
        {
            return Err(format!(
                "texture {name} is not shown: a block-compressed texture needs a size in whole blocks and a device with these formats"
            ));
        }
        let packet = owned::Texture {
            width: info.width.max(1) as i32,
            height: info.height.max(1) as i32,
            format,
            mip_count: mips.clamp(1, info.levels) as i32,
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
    /// attached to it and the viewport; of a cube, an array or a 3D target the image being
    /// drawn, and of a multisampled target its samples, as the Editor binds them
    /// (tests/projects/nativegfx `shape.cube.target*`, `shape.array.target*`,
    /// `shape.msaa.target*`). A framebuffer without attachments when the target is the screen,
    /// is not drawn into or cannot be shared.
    fn bind_plugin_target(&mut self) {
        let (color, depth) = self.target_handles;
        let (mip, slice) = self.plugins.target;
        let mut bound = None;
        let mut samples = false;
        if color == 0 {
            self.warn_once("plugin-screen".into(), || {
                "a native plugin callback while the screen is the target finds no target bound (the screen is not shared with plugins)".into()
            });
        } else if self.state.target.is_none() {
            // Nothing is drawn there either (`resolve_target` said why).
            self.warn_once(format!("plugin-target-none{color}"), || {
                format!("a native plugin callback finds no target bound: render texture {color} is not a target as it was set (its mip or slice is out of range)")
            });
        } else if self.textures.contains_key(&color) {
            match self.share(color) {
                Ok(names) => {
                    let owner = depth_owner(color, depth);
                    let owner_names = match owner {
                        Some(owner) if owner == color => Some(names),
                        Some(owner) => self.share(owner).ok(),
                        None => None,
                    };
                    bound = Some(match names.samples {
                        Some((color, _)) => {
                            samples = true;
                            let depth = owner_names.and_then(|n| n.samples).and_then(|s| s.1);
                            gl_interop::TargetImages {
                                color,
                                mip: 0,
                                layer: None,
                                depth: depth.map(|d| (d, true)),
                                depth_layer: None,
                            }
                        }
                        None => {
                            let attached = owner_names.and_then(|n| n.depth);
                            // The image being drawn of a cube, an array or a 3D target, and
                            // of a depth buffer with as many.
                            let image = |handle: u64, depth: bool| {
                                let shape = Shape::of(&self.textures[&handle]);
                                let shape = if depth { shape.depth() } else { shape };
                                shape.layered().then_some(slice.max(0) as u32)
                            };
                            gl_interop::TargetImages {
                                color: names.color,
                                mip: mip.max(0) as u32,
                                layer: image(color, false),
                                depth: attached.map(|d| (d, true)),
                                depth_layer: owner
                                    .filter(|_| attached.is_some())
                                    .and_then(|owner| image(owner, true)),
                            }
                        }
                    });
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
        let done = gl_interop::with(|interop| interop.bind_target(bound, viewport));
        self.plugins.drew_into_samples = samples && done.is_ok();
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
    let Names {
        color,
        depth,
        samples,
    } = native.names;
    let names = [
        Some(color),
        depth,
        samples.map(|s| s.0),
        samples.and_then(|s| s.1),
    ];
    let _ = gl_interop::with(|interop| {
        for name in names.into_iter().flatten() {
            interop.forget(name, native.views.contains(&Some(name)));
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
        depth_bits: match gpu.depth_format() {
            Some(wgpu::TextureFormat::Depth16Unorm) => 16,
            Some(wgpu::TextureFormat::Depth32Float) => 32,
            Some(wgpu::TextureFormat::Depth32FloatStencil8) => {
                unbound_runtime::render_protocol::DEPTH32_STENCIL_BITS
            }
            Some(_) => 24,
            None => 0,
        },
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
    fn a_name_has_the_target_of_the_textures_shape() {
        use gl_interop::{
            TEXTURE_2D, TEXTURE_2D_ARRAY, TEXTURE_3D, TEXTURE_CUBE_MAP, TEXTURE_CUBE_MAP_ARRAY,
        };
        let plain = Shape {
            cube: false,
            cube_array: false,
            volume: false,
            array: false,
            layers: 1,
        };
        assert_eq!(plain.target(), (TEXTURE_2D, 1));
        assert!(!plain.layered());
        let cube = Shape {
            cube: true,
            layers: 6,
            ..plain
        };
        assert_eq!(cube.target(), (TEXTURE_CUBE_MAP, 6));
        let cubes = Shape {
            cube_array: true,
            layers: 12,
            ..plain
        };
        assert_eq!(cubes.target(), (TEXTURE_CUBE_MAP_ARRAY, 12));
        // A volume's slices are not layers; its depth buffer is one image.
        let volume = Shape {
            volume: true,
            layers: 4,
            ..plain
        };
        assert_eq!(volume.target(), (TEXTURE_3D, 1));
        assert!(volume.layered());
        assert_eq!(volume.depth().target(), (TEXTURE_2D, 1));
        assert!(!volume.depth().layered());
        // An array is an array whatever its size: one layer, and six or twelve square ones,
        // which the renderer's backend stores as cube maps.
        for layers in [1, 3, 6, 12] {
            let array = Shape {
                array: true,
                layers,
                ..plain
            };
            assert_eq!(array.target(), (TEXTURE_2D_ARRAY, layers));
            assert!(array.layered());
            assert_eq!(array.depth().target(), (TEXTURE_2D_ARRAY, layers));
        }
        // A cube target's depth buffer has a face for each of the color's.
        assert_eq!(cube.depth().target(), (TEXTURE_CUBE_MAP, 6));
    }

    #[test]
    fn a_plugins_texture_is_described_without_a_view_in_another_format() {
        use gl_interop::format as gl;
        use texture_format as code;
        // sRGB texels and three channels are sampled by OpenGL itself: no color code, whose
        // sRGB view a plugin's texture cannot have.
        for format in [gl::RGBA8, gl::SRGB8_ALPHA8, gl::RGB8, gl::SRGB8] {
            assert_eq!(external_format(format), Some(code::LINEAR_RGBA8));
        }
        // An unsized format, as a texture made with one reports it: its 8-bit channels.
        for format in [gl::RGBA, gl::SRGB_ALPHA, gl::RGB, gl::SRGB] {
            assert_eq!(external_format(format), Some(code::LINEAR_RGBA8));
        }
        assert_eq!(external_format(gl::RED), Some(code::R8_UNORM));
        assert_eq!(external_format(gl::RG), Some(code::RG8_UNORM));
        // Three channels are described by four.
        assert_eq!(external_format(gl::RGB16F), Some(code::RGBA_HALF));
        assert_eq!(external_format(gl::RGB32F), Some(code::RGBA32_FLOAT));
        assert_eq!(external_format(gl::RGB16), Some(code::RGBA16_UNORM));
        assert_eq!(external_format(gl::R8), Some(code::R8_UNORM));
        assert_eq!(external_format(gl::RG8), Some(code::RG8_UNORM));
        assert_eq!(external_format(gl::RGBA16F), Some(code::RGBA_HALF));
        assert_eq!(external_format(gl::RGBA32F), Some(code::RGBA32_FLOAT));
        assert_eq!(external_format(gl::RGBA16), Some(code::RGBA16_UNORM));
        assert_eq!(external_format(gl::RGB10_A2), Some(code::RGB10A2_UNORM));
        assert_eq!(external_format(gl::RG8_SNORM), Some(code::RG8_SNORM));
        for (format, block) in [
            (gl::COMPRESSED_SRGB_ALPHA_S3TC_DXT1, code::BC1_DATA),
            (gl::COMPRESSED_RGBA_S3TC_DXT5, code::BC3_DATA),
            (gl::COMPRESSED_SRGB_ALPHA_S3TC_DXT5, code::BC3_DATA),
            (gl::COMPRESSED_SRGB_ALPHA_BPTC_UNORM, code::BC7_DATA),
        ] {
            assert_eq!(external_format(format), Some(block));
            assert!(!code::block_color(block));
        }
        // Not sampled as colors, or without a code: refused, not shown as another format.
        const GL_DEPTH24_STENCIL8: u32 = 0x88F0;
        const GL_RGBA8UI: u32 = 0x8D7C;
        const GL_COMPRESSED_RGBA_S3TC_DXT3: u32 = 0x83F2;
        const GL_RGBA16_SNORM: u32 = 0x8F9B;
        const GL_COMPRESSED_SIGNED_RED_RGTC1: u32 = 0x8DBC;
        const GL_DEPTH_COMPONENT: u32 = 0x1902;
        for format in [
            GL_DEPTH24_STENCIL8,
            GL_DEPTH_COMPONENT,
            GL_RGBA8UI,
            GL_COMPRESSED_RGBA_S3TC_DXT3,
            GL_RGBA16_SNORM,
            GL_COMPRESSED_SIGNED_RED_RGTC1,
            0,
        ] {
            assert_eq!(external_format(format), None);
        }
    }

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
