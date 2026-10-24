//! The functions the managed engine calls into: the layout of `NativeApi` is shared with
//! `managed/UnityEngine/Runtime/Native.cs` and versioned.
use crate::{
    audio::{
        Clip, SourceParams,
        protocol::{
            AudioClipInfo, AudioFramePacket, AudioSourceCommand, AudioSourcePacket,
            AudioSourceState, AudioStatus, Callback,
        },
    },
    render_protocol::{
        ImportedTextureInfo, MaterialPacket, MeshPacket, TexturePacket, commands, owned,
    },
    state,
};

pub const NATIVE_API_VERSION: u32 = 9;

/// A rasterized glyph (`font_glyph`), in pixels: FreeType's `bitmap_left`/`bitmap_top` and size.
#[repr(C)]
#[derive(Debug, Clone, Copy, Default)]
pub struct GlyphInfo {
    pub advance: f32,
    pub left: i32,
    pub top: i32,
    pub width: i32,
    pub height: i32,
}

/// A glyph rendered by index (`font_render_glyph`): the bitmap as in [`GlyphInfo`] plus the
/// outline's unrounded control box in pixels, which TextCore reports as the glyph metrics.
#[repr(C)]
#[derive(Debug, Clone, Copy, Default)]
pub struct GlyphOutlineInfo {
    pub advance: f32,
    pub x_min: f32,
    pub y_min: f32,
    pub x_max: f32,
    pub y_max: f32,
    pub left: i32,
    pub top: i32,
    pub width: i32,
    pub height: i32,
}

/// A buffer handed to the managed side; it gives it back to `free_buffer`.
#[repr(C)]
pub struct NativeBuffer {
    pub data: *mut u8,
    pub length: i64,
}

impl NativeBuffer {
    pub fn from_vec(bytes: Vec<u8>) -> Self {
        let boxed = bytes.into_boxed_slice();
        let length = boxed.len() as i64;
        let data = Box::into_raw(boxed) as *mut u8;
        Self { data, length }
    }

    pub const fn empty() -> Self {
        Self {
            data: std::ptr::null_mut(),
            length: 0,
        }
    }
}

#[repr(C)]
pub struct NativeApi {
    pub version: u32,
    pub log: extern "C" fn(level: i32, text: *const u8, length: i32),
    pub call: extern "C" fn(*const u8, i32, *const u8, i32, *mut NativeBuffer) -> i32,
    pub free_buffer: extern "C" fn(NativeBuffer),
    pub load_documents: extern "C" fn(*const u8, i32, *mut NativeBuffer) -> i32,
    /// (command stream, length): executes rendering commands in order (render_commands.rs).
    pub submit_commands: extern "C" fn(*const u8, i64),
    pub upload_mesh: extern "C" fn(u64, *const MeshPacket),
    pub release_mesh: extern "C" fn(u64),
    pub upload_texture: extern "C" fn(u64, *const TexturePacket),
    pub import_texture: extern "C" fn(u64, *const u8, i32, *mut ImportedTextureInfo) -> i32,
    pub release_texture: extern "C" fn(u64),
    pub update_material: extern "C" fn(u64, *const MaterialPacket),
    pub release_material: extern "C" fn(u64),
    /// (font handle, code point, pixel size, FontStyle, out info, out coverage): 1 when the font
    /// has the glyph, else 0. The coverage is `width * height` bytes, top row first.
    pub font_glyph: extern "C" fn(u64, u32, f32, i32, *mut GlyphInfo, *mut NativeBuffer) -> i32,
    // Version 4: audio (docs/AUDIO.md).
    pub audio_import_clip: extern "C" fn(u64, *const u8, i32, *mut AudioClipInfo) -> i32,
    pub audio_create_clip: extern "C" fn(u64, i32, i32, i32, i32),
    pub audio_clip_data: extern "C" fn(u64, i32, *mut f32, i32, i32) -> i32,
    pub audio_release_clip: extern "C" fn(u64),
    pub audio_update_source: extern "C" fn(u64, *const AudioSourcePacket),
    pub audio_source_command: extern "C" fn(u64, *const AudioSourceCommand),
    pub audio_source_state: extern "C" fn(u64, *mut AudioSourceState),
    pub audio_release_source: extern "C" fn(u64),
    pub audio_update_group: extern "C" fn(u64, f32, f32),
    pub audio_frame: extern "C" fn(*const AudioFramePacket, *mut AudioStatus),
    pub audio_dsp_time: extern "C" fn() -> f64,
    pub audio_output_data: extern "C" fn(u64, i32, *mut f32, i32, i32) -> i32,
    pub audio_set_callback: extern "C" fn(Option<Callback>),
    // Version 5: image bytes (ImageConversion).
    /// (bytes, length, out width, out height, out is-JPEG, out RGBA8 rows bottom first): 1 when decoded.
    pub image_decode:
        extern "C" fn(*const u8, i32, *mut i32, *mut i32, *mut i32, *mut NativeBuffer) -> i32,
    /// (encoding: 0 PNG, 1 JPG, 2 TGA, 3 EXR; JPG quality; pixels; width; height; pixels are float
    /// RGBA (else RGBA8), rows bottom first; out file bytes): 1 when encoded.
    pub image_encode: extern "C" fn(i32, i32, *const u8, i32, i32, i32, *mut NativeBuffer) -> i32,
    // Version 6: glyphs by index (TextCore's FontEngine).
    /// (font handle, code point): the glyph index, 0 when the face lacks it.
    pub font_glyph_index: extern "C" fn(u64, u32) -> u32,
    /// (font handle, glyph index, pixel size, SDF padding, out info, out pixels): 1 when the face
    /// has the glyph. With a negative padding the pixels are coverage, as `font_glyph`'s; else a
    /// signed distance field grown by the padding on every side (`FontFace::render_glyph_sdf`).
    pub font_render_glyph:
        extern "C" fn(u64, u32, f32, i32, *mut GlyphOutlineInfo, *mut NativeBuffer) -> i32,
    // Version 7: no new entries; `FrameInput` gained the frame's ordered key events.
    // Version 9: the command stream replaces the frame packet.
    /// (texture handle, 0 for the screen; x, y, width, height; float; out pixels): 1 with the
    /// rectangle's RGBA pixels, rows bottom first, 4 bytes each (16 when float).
    pub read_pixels: extern "C" fn(u64, i32, i32, i32, i32, i32, *mut NativeBuffer) -> i32,
}

pub fn api() -> NativeApi {
    NativeApi {
        version: NATIVE_API_VERSION,
        log,
        call,
        free_buffer,
        load_documents,
        submit_commands,
        upload_mesh,
        release_mesh,
        upload_texture,
        import_texture,
        release_texture,
        update_material,
        release_material,
        font_glyph,
        audio_import_clip,
        audio_create_clip,
        audio_clip_data,
        audio_release_clip,
        audio_update_source,
        audio_source_command,
        audio_source_state,
        audio_release_source,
        audio_update_group,
        audio_frame,
        audio_dsp_time,
        audio_output_data,
        audio_set_callback,
        image_decode,
        image_encode,
        font_glyph_index,
        font_render_glyph,
        read_pixels,
    }
}

extern "C" fn font_glyph_index(font: u64, codepoint: u32) -> u32 {
    std::panic::catch_unwind(|| crate::fonts::glyph_index(font, codepoint)).unwrap_or(0)
}

extern "C" fn font_render_glyph(
    font: u64,
    glyph: u32,
    size: f32,
    sdf_padding: i32,
    info: *mut GlyphOutlineInfo,
    pixels: *mut NativeBuffer,
) -> i32 {
    let sdf = (sdf_padding >= 0).then_some(sdf_padding);
    let rendered =
        std::panic::catch_unwind(|| crate::fonts::glyph_by_index(font, glyph, size, sdf));
    unsafe {
        let Ok(Some(glyph)) = rendered else {
            *info = GlyphOutlineInfo::default();
            *pixels = NativeBuffer::empty();
            return 0;
        };
        let (x_min, y_min, x_max, y_max) = glyph.bounds;
        *info = GlyphOutlineInfo {
            advance: glyph.advance,
            x_min,
            y_min,
            x_max,
            y_max,
            left: glyph.left,
            top: glyph.top,
            width: glyph.width,
            height: glyph.height,
        };
        *pixels = if glyph.coverage.is_empty() {
            NativeBuffer::empty()
        } else {
            NativeBuffer::from_vec(glyph.coverage)
        };
    }
    1
}

extern "C" fn image_decode(
    bytes: *const u8,
    length: i32,
    width: *mut i32,
    height: *mut i32,
    jpeg: *mut i32,
    pixels: *mut NativeBuffer,
) -> i32 {
    let input = if bytes.is_null() || length <= 0 {
        &[][..]
    } else {
        unsafe { std::slice::from_raw_parts(bytes, length as usize) }
    };
    let decoded = std::panic::catch_unwind(|| unbound_assets::image_io::decode(input));
    unsafe {
        match decoded {
            Ok(Ok(image)) => {
                *width = image.width as i32;
                *height = image.height as i32;
                *jpeg = image.jpeg as i32;
                *pixels = NativeBuffer::from_vec(image.rgba);
                1
            }
            _ => {
                *width = 0;
                *height = 0;
                *jpeg = 0;
                *pixels = NativeBuffer::empty();
                0
            }
        }
    }
}

extern "C" fn image_encode(
    encoding: i32,
    quality: i32,
    pixels: *const u8,
    width: i32,
    height: i32,
    float: i32,
    output: *mut NativeBuffer,
) -> i32 {
    use unbound_assets::image_io::{Encoding, encode_rgba_f32, encode_rgba8};
    let (w, h) = (width.max(0) as u32, height.max(0) as u32);
    let count = (w as usize) * (h as usize) * 4;
    let encoded = std::panic::catch_unwind(|| {
        if pixels.is_null() || count == 0 {
            anyhow::bail!("no pixels");
        }
        if float != 0 {
            let data = unsafe { std::slice::from_raw_parts(pixels as *const f32, count) };
            if encoding == 3 {
                return encode_rgba_f32(w, h, data, false);
            }
            let bytes: Vec<u8> = data
                .iter()
                .map(|v| (v.clamp(0.0, 1.0) * 255.0 + 0.5) as u8)
                .collect();
            return encode_rgba8(encoding_of(encoding, quality), w, h, &bytes);
        }
        let data = unsafe { std::slice::from_raw_parts(pixels, count) };
        encode_rgba8(encoding_of(encoding, quality), w, h, data)
    });
    fn encoding_of(encoding: i32, quality: i32) -> Encoding {
        match encoding {
            1 => Encoding::Jpg(quality.clamp(1, 100) as u8),
            2 => Encoding::Tga,
            3 => Encoding::Exr,
            _ => Encoding::Png,
        }
    }
    unsafe {
        match encoded {
            Ok(Ok(bytes)) => {
                *output = NativeBuffer::from_vec(bytes);
                1
            }
            _ => {
                *output = NativeBuffer::empty();
                0
            }
        }
    }
}

extern "C" fn font_glyph(
    font: u64,
    codepoint: u32,
    size: f32,
    style: i32,
    info: *mut GlyphInfo,
    pixels: *mut NativeBuffer,
) -> i32 {
    let rendered = std::panic::catch_unwind(|| crate::fonts::glyph(font, codepoint, size, style));
    let Ok(Some(glyph)) = rendered else {
        unsafe {
            *info = GlyphInfo::default();
            *pixels = NativeBuffer::empty();
        }
        return 0;
    };
    unsafe {
        *info = GlyphInfo {
            advance: glyph.advance,
            left: glyph.left,
            top: glyph.top,
            width: glyph.width,
            height: glyph.height,
        };
        *pixels = if glyph.coverage.is_empty() {
            NativeBuffer::empty()
        } else {
            NativeBuffer::from_vec(glyph.coverage)
        };
    }
    1
}

unsafe fn text<'a>(data: *const u8, length: i32) -> &'a str {
    if data.is_null() || length <= 0 {
        return "";
    }
    let bytes = unsafe { std::slice::from_raw_parts(data, length as usize) };
    std::str::from_utf8(bytes).unwrap_or("")
}

/// Unity log types: 0 error, 1 assert, 2 warning, 3 log, 4 exception.
extern "C" fn log(level: i32, data: *const u8, length: i32) {
    let message = unsafe { text(data, length) };
    match level {
        0 | 1 | 4 => tracing::error!(target: "game", "{message}"),
        2 => tracing::warn!(target: "game", "{message}"),
        _ => tracing::info!(target: "game", "{message}"),
    }
}

extern "C" fn call(
    method: *const u8,
    method_length: i32,
    request: *const u8,
    request_length: i32,
    out: *mut NativeBuffer,
) -> i32 {
    let method = unsafe { text(method, method_length) };
    let request = unsafe { text(request, request_length) };
    let result = std::panic::catch_unwind(|| state::dispatch(method, request));
    let (status, body) = match result {
        Ok(Ok(body)) => (0, body),
        Ok(Err(error)) => (1, format!("{error:#}")),
        Err(_) => (1, format!("native `{method}` panicked")),
    };
    unsafe { *out = NativeBuffer::from_vec(body.into_bytes()) };
    status
}

extern "C" fn free_buffer(buffer: NativeBuffer) {
    if buffer.data.is_null() {
        return;
    }
    unsafe {
        drop(Box::from_raw(std::ptr::slice_from_raw_parts_mut(
            buffer.data,
            buffer.length as usize,
        )));
    }
}

extern "C" fn load_documents(key: *const u8, length: i32, out: *mut NativeBuffer) -> i32 {
    let key = unsafe { text(key, length) };
    let result = std::panic::catch_unwind(|| state::load_documents(key));
    let (status, bytes) = match result {
        Ok(Ok(bytes)) => (0, bytes),
        Ok(Err(error)) => (1, format!("{error:#}").into_bytes()),
        Err(_) => (1, b"loading documents panicked".to_vec()),
    };
    unsafe { *out = NativeBuffer::from_vec(bytes) };
    status
}

extern "C" fn submit_commands(bytes: *const u8, length: i64) {
    if bytes.is_null() || length <= 0 {
        return;
    }
    let bytes = unsafe { std::slice::from_raw_parts(bytes, length as usize) };
    match commands::parse(bytes) {
        Ok(commands) => state::backend().submit_commands(commands),
        Err(error) => tracing::error!("a rendering command stream is malformed: {error}"),
    }
}

extern "C" fn read_pixels(
    texture: u64,
    x: i32,
    y: i32,
    width: i32,
    height: i32,
    float: i32,
    out: *mut NativeBuffer,
) -> i32 {
    match state::backend().read_pixels(texture, [x, y, width, height], float != 0) {
        Some(pixels) => {
            unsafe { *out = NativeBuffer::from_vec(pixels) };
            1
        }
        None => 0,
    }
}

extern "C" fn upload_mesh(handle: u64, mesh: *const MeshPacket) {
    if mesh.is_null() {
        return;
    }
    let mesh = unsafe { owned::Mesh::from_packet(&*mesh) };
    state::backend().upload_mesh(handle, mesh);
}

extern "C" fn release_mesh(handle: u64) {
    state::backend().release_mesh(handle);
}

extern "C" fn upload_texture(handle: u64, texture: *const TexturePacket) {
    if texture.is_null() {
        return;
    }
    let texture = unsafe { owned::Texture::from_packet(&*texture) };
    state::backend().upload_texture(handle, texture);
}

extern "C" fn import_texture(
    handle: u64,
    guid: *const u8,
    length: i32,
    info: *mut ImportedTextureInfo,
) -> i32 {
    let guid = unsafe { text(guid, length) };
    match std::panic::catch_unwind(|| state::import_texture(handle, guid)) {
        Ok(Ok(imported)) => {
            unsafe { *info = imported };
            0
        }
        Ok(Err(error)) => {
            tracing::warn!("importing texture {guid}: {error:#}");
            1
        }
        Err(_) => 1,
    }
}

extern "C" fn release_texture(handle: u64) {
    state::backend().release_texture(handle);
}

extern "C" fn update_material(handle: u64, material: *const MaterialPacket) {
    if material.is_null() {
        return;
    }
    let material = unsafe { owned::Material::from_packet(&*material) };
    state::backend().update_material(handle, material);
}

extern "C" fn release_material(handle: u64) {
    state::backend().release_material(handle);
}

// ---------------------------------------------------------------------- audio

extern "C" fn audio_import_clip(
    handle: u64,
    guid: *const u8,
    length: i32,
    info: *mut AudioClipInfo,
) -> i32 {
    let guid = unsafe { text(guid, length) };
    match std::panic::catch_unwind(|| state::import_audio(handle, guid)) {
        Ok(Ok(imported)) => {
            unsafe { *info = imported };
            0
        }
        Ok(Err(error)) => {
            tracing::error!("importing audio clip {guid}: {error:#}");
            1
        }
        Err(_) => 1,
    }
}

/// `AudioClip.Create`: a silent clip (or, `stream` 1, one whose data comes from its PCM reader).
extern "C" fn audio_create_clip(
    handle: u64,
    frames: i32,
    channels: i32,
    frequency: i32,
    stream: i32,
) {
    let channels = channels.max(1) as usize;
    let frames = frames.max(0) as usize;
    let clip = if stream != 0 {
        Clip {
            channels,
            rate: frequency.max(1) as u32,
            frames,
            samples: Vec::new(),
            stream: true,
        }
    } else {
        Clip::new(
            channels,
            frequency.max(1) as u32,
            vec![0.0; frames * channels],
        )
    };
    state::state().audio.mixer().add_clip(handle, clip);
}

/// `GetData` (write 0) / `SetData` (write 1) of `count` samples from frame `offset`.
extern "C" fn audio_clip_data(
    handle: u64,
    offset: i32,
    data: *mut f32,
    count: i32,
    write: i32,
) -> i32 {
    if data.is_null() || count <= 0 {
        return 0;
    }
    let data = unsafe { std::slice::from_raw_parts_mut(data, count as usize) };
    let ok =
        state::state()
            .audio
            .mixer()
            .clip_data(handle, offset.max(0) as usize, data, write != 0);
    ok as i32
}

extern "C" fn audio_release_clip(handle: u64) {
    state::state().audio.mixer().release_clip(handle);
}

extern "C" fn audio_update_source(handle: u64, packet: *const AudioSourcePacket) {
    if packet.is_null() {
        return;
    }
    let params = unsafe { SourceParams::from_packet(&*packet) };
    state::state().audio.mixer().update_source(handle, params);
}

extern "C" fn audio_source_command(handle: u64, command: *const AudioSourceCommand) {
    if command.is_null() {
        return;
    }
    let command = unsafe { *command };
    state::state().audio.mixer().command(handle, &command);
}

extern "C" fn audio_source_state(handle: u64, out: *mut AudioSourceState) {
    if out.is_null() {
        return;
    }
    let source = state::state().audio.mixer().source_state(handle);
    unsafe { *out = source };
}

extern "C" fn audio_release_source(handle: u64) {
    state::state().audio.mixer().release_source(handle);
}

extern "C" fn audio_update_group(handle: u64, gain: f32, pitch: f32) {
    state::state()
        .audio
        .mixer()
        .update_group(handle, gain, pitch);
}

extern "C" fn audio_frame(packet: *const AudioFramePacket, status: *mut AudioStatus) {
    if packet.is_null() {
        return;
    }
    let result = state::state().audio.frame(unsafe { &*packet });
    if !status.is_null() {
        unsafe { *status = result };
    }
}

/// `AudioSettings.dspTime`, without taking the mixer's lock.
extern "C" fn audio_dsp_time() -> f64 {
    state::state().audio.dsp_time()
}

/// `GetOutputData` (`window` < 0) or `GetSpectrumData` (`window`: `FFTWindow`) of a source
/// (0: the listener).
extern "C" fn audio_output_data(
    handle: u64,
    channel: i32,
    out: *mut f32,
    count: i32,
    window: i32,
) -> i32 {
    if out.is_null() || count <= 0 {
        return 0;
    }
    let out = unsafe { std::slice::from_raw_parts_mut(out, count as usize) };
    state::state()
        .audio
        .mixer()
        .output_data(handle, channel.max(0) as usize, out, window) as i32
}

extern "C" fn audio_set_callback(callback: Option<Callback>) {
    state::state().audio.set_callback(callback);
}
