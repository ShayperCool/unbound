//! The denoising of a lightmap's light by Intel Open Image Denoise 1.4.2, as the Unity 6000.0.71f1
//! Editor's Progressive Lightmapper denoises it (tests/projects/bakedbuffers `Denoise*`): the
//! library's lightmap filter (`RTLightmap`, high dynamic range) over the whole image at once, its
//! red, green and blue from the image's texels of four numbers, each texel's fourth number kept.
//! The library is loaded from the path the caller gives (`unbound_project::denoiser`).
use anyhow::{Context, Result, bail};
use libloading::{Library, Symbol};
use std::{
    ffi::{CStr, c_char, c_int, c_void},
    path::Path,
    sync::OnceLock,
};

type Device = *mut c_void;
type Filter = *mut c_void;

/// `OIDN_FORMAT_FLOAT3`.
const FORMAT_FLOAT3: c_int = 3;
/// `OIDN_DEVICE_TYPE_DEFAULT`.
const DEVICE_DEFAULT: c_int = 0;
/// `OIDN_ERROR_NONE`.
const ERROR_NONE: c_int = 0;

/// The library's entry points the denoising calls.
struct Api {
    new_device: unsafe extern "C" fn(c_int) -> Device,
    commit_device: unsafe extern "C" fn(Device),
    release_device: unsafe extern "C" fn(Device),
    get_device_error: unsafe extern "C" fn(Device, *mut *const c_char) -> c_int,
    new_filter: unsafe extern "C" fn(Device, *const c_char) -> Filter,
    release_filter: unsafe extern "C" fn(Filter),
    set_shared_filter_image: unsafe extern "C" fn(
        Filter,
        *const c_char,
        *mut c_void,
        c_int,
        usize,
        usize,
        usize,
        usize,
        usize,
    ),
    commit_filter: unsafe extern "C" fn(Filter),
    execute_filter: unsafe extern "C" fn(Filter),
    // Kept loaded for as long as the entry points are used (the process).
    _library: Library,
}

/// The library at `path`, loaded once per process (a later path is not loaded: the first one's
/// library is used, or its failure reported).
fn api(path: &Path) -> Result<&'static Api> {
    static API: OnceLock<std::result::Result<Api, String>> = OnceLock::new();
    API.get_or_init(|| load(path).map_err(|e| format!("{e:#}")))
        .as_ref()
        .map_err(|e| anyhow::anyhow!("loading Open Image Denoise: {e}"))
}

fn load(path: &Path) -> Result<Api> {
    // SAFETY: Open Image Denoise's initializers have no preconditions; the entry points are
    // looked up with the signatures of its 1.4 C API (OpenImageDenoise/oidn.h).
    unsafe {
        let library = Library::new(path).with_context(|| format!("{}", path.display()))?;
        macro_rules! entry {
            ($name:literal) => {{
                let symbol: Symbol<_> = library
                    .get(concat!($name, "\0").as_bytes())
                    .with_context(|| format!("{} lacks {}", path.display(), $name))?;
                *symbol
            }};
        }
        Ok(Api {
            new_device: entry!("oidnNewDevice"),
            commit_device: entry!("oidnCommitDevice"),
            release_device: entry!("oidnReleaseDevice"),
            get_device_error: entry!("oidnGetDeviceError"),
            new_filter: entry!("oidnNewFilter"),
            release_filter: entry!("oidnReleaseFilter"),
            set_shared_filter_image: entry!("oidnSetSharedFilterImage"),
            commit_filter: entry!("oidnCommitFilter"),
            execute_filter: entry!("oidnExecuteFilter"),
            _library: library,
        })
    }
}

/// `image` (`width` × `height` texels of four numbers, rows from the first) denoised into
/// `output`: its red, green and blue by the lightmap filter, its fourth numbers copied. The
/// library at `library` is Open Image Denoise 1.4.2's.
pub fn denoise(
    library: &Path,
    image: &[[f32; 4]],
    output: &mut [[f32; 4]],
    width: usize,
    height: usize,
) -> Result<()> {
    assert!(image.len() == width * height && output.len() == image.len());
    let api = api(library)?;
    output.copy_from_slice(image);
    let pixel = std::mem::size_of::<[f32; 4]>();
    // SAFETY: both images outlive the filter's execution; each is `width` × `height` texels of
    // `pixel` bytes, rows `width × pixel` bytes apart, of which the filter reads or writes the
    // first three floats; the device and filter are released before returning.
    unsafe {
        let device = (api.new_device)(DEVICE_DEFAULT);
        if device.is_null() {
            bail!("Open Image Denoise made no device");
        }
        (api.commit_device)(device);
        let filter = (api.new_filter)(device, c"RTLightmap".as_ptr());
        let result = if filter.is_null() {
            error(api, device)
                .and_then(|()| Err(anyhow::anyhow!("without an error")))
                .context("Open Image Denoise made no lightmap filter")
        } else {
            (api.set_shared_filter_image)(
                filter,
                c"color".as_ptr(),
                image.as_ptr() as *mut c_void,
                FORMAT_FLOAT3,
                width,
                height,
                0,
                pixel,
                width * pixel,
            );
            (api.set_shared_filter_image)(
                filter,
                c"output".as_ptr(),
                output.as_mut_ptr() as *mut c_void,
                FORMAT_FLOAT3,
                width,
                height,
                0,
                pixel,
                width * pixel,
            );
            // (The Editor also sets "hdr", which the lightmap filter of 1.4.2 does not have: it
            // is high dynamic range unless directional.)
            (api.commit_filter)(filter);
            (api.execute_filter)(filter);
            let result = error(api, device);
            (api.release_filter)(filter);
            result
        };
        (api.release_device)(device);
        result
    }
}

/// The device's first error since the last asked for, if any.
///
/// # Safety
/// `device` is a live device of `api`'s library.
unsafe fn error(api: &Api, device: Device) -> Result<()> {
    let mut message: *const c_char = std::ptr::null();
    // SAFETY: as the function's contract; the message lives until the next call on the device.
    let code = unsafe { (api.get_device_error)(device, &mut message) };
    if code == ERROR_NONE {
        return Ok(());
    }
    let text = if message.is_null() {
        String::new()
    } else {
        // SAFETY: a nul-terminated string of the library's.
        unsafe { CStr::from_ptr(message) }
            .to_string_lossy()
            .into_owned()
    };
    bail!("Open Image Denoise failed (error {code}): {text}")
}
