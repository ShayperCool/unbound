//! The functions the managed engine calls into: the layout of `NativeApi` is shared with
//! `managed/UnityEngine/Runtime/Native.cs` and versioned.
use crate::{
    render_protocol::{
        FramePacket, ImportedTextureInfo, MaterialPacket, MeshPacket, TexturePacket, owned,
    },
    state,
};
use std::ffi::c_void;

pub const NATIVE_API_VERSION: u32 = 2;

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
    pub submit_frame: extern "C" fn(*const c_void),
    pub upload_mesh: extern "C" fn(u64, *const MeshPacket),
    pub release_mesh: extern "C" fn(u64),
    pub upload_texture: extern "C" fn(u64, *const TexturePacket),
    pub import_texture: extern "C" fn(u64, *const u8, i32, *mut ImportedTextureInfo) -> i32,
    pub release_texture: extern "C" fn(u64),
    pub update_material: extern "C" fn(u64, *const MaterialPacket),
    pub release_material: extern "C" fn(u64),
}

pub fn api() -> NativeApi {
    NativeApi {
        version: NATIVE_API_VERSION,
        log,
        call,
        free_buffer,
        load_documents,
        submit_frame,
        upload_mesh,
        release_mesh,
        upload_texture,
        import_texture,
        release_texture,
        update_material,
        release_material,
    }
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

extern "C" fn submit_frame(frame: *const c_void) {
    if frame.is_null() {
        return;
    }
    let frame = unsafe { owned::Frame::from_packet(&*(frame as *const FramePacket)) };
    state::backend().submit_frame(frame);
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
