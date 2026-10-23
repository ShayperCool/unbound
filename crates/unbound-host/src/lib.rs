//! Hosting CoreCLR: the managed engine and the game's code run inside this process.
use anyhow::{Context, Result, bail};
use netcorehost::{hostfxr::Hostfxr, pdcstr, pdcstring::PdCString};
use serde::{Serialize, de::DeserializeOwned};
use std::path::{Path, PathBuf};

type CallFn = unsafe extern "system" fn(
    method: *const u8,
    method_length: i32,
    request: *const u8,
    request_length: i32,
    response: *mut *mut u8,
    response_length: *mut i32,
) -> i32;
type FreeFn = unsafe extern "system" fn(buffer: *mut u8);

/// A running CoreCLR with the managed engine loaded.
pub struct ManagedRuntime {
    call: CallFn,
    free: FreeFn,
    managed_dir: PathBuf,
    loader: netcorehost::hostfxr::DelegateLoader,
    // The context must outlive the function pointers.
    _context:
        netcorehost::hostfxr::HostfxrContext<netcorehost::hostfxr::InitializedForRuntimeConfig>,
}

// The function pointers are plain native entry points, callable from any thread.
unsafe impl Send for ManagedRuntime {}
unsafe impl Sync for ManagedRuntime {}

/// The .NET installation: `DOTNET_ROOT`, `~/.dotnet`, or a system location.
pub fn dotnet_root() -> Result<PathBuf> {
    let mut candidates = Vec::new();
    if let Some(root) = std::env::var_os("DOTNET_ROOT") {
        candidates.push(PathBuf::from(root));
    }
    if let Some(home) = std::env::var_os("HOME") {
        candidates.push(PathBuf::from(home).join(".dotnet"));
    }
    candidates.extend(
        [
            "/usr/share/dotnet",
            "/usr/lib/dotnet",
            "/usr/local/share/dotnet",
        ]
        .map(PathBuf::from),
    );
    candidates
        .into_iter()
        .find(|root| root.join("host/fxr").is_dir())
        .context("no .NET installation found; install the .NET SDK or set DOTNET_ROOT")
}

/// Where the managed engine was built: `UNBOUND_MANAGED_DIR`, `managed/` beside the executable,
/// or the workspace's `target/managed`.
pub fn managed_dir() -> Result<PathBuf> {
    if let Some(dir) = std::env::var_os("UNBOUND_MANAGED_DIR") {
        return Ok(dir.into());
    }
    let mut candidates = Vec::new();
    if let Ok(exe) = std::env::current_exe()
        && let Some(dir) = exe.parent()
    {
        candidates.push(dir.join("managed"));
        candidates.push(dir.join("../managed"));
    }
    candidates.push(Path::new(env!("CARGO_MANIFEST_DIR")).join("../../target/managed"));
    candidates
        .into_iter()
        .find(|dir| dir.join("Unbound.Host.dll").is_file())
        .context("the managed engine (Unbound.Host.dll) was not found; build with cargo build")
}

fn latest_hostfxr(root: &Path) -> Result<PathBuf> {
    let fxr = root.join("host/fxr");
    let mut versions: Vec<(Vec<u32>, PathBuf)> = std::fs::read_dir(&fxr)?
        .flatten()
        .filter_map(|e| {
            let name = e.file_name().to_string_lossy().to_string();
            let numbers = name
                .split(['.', '-'])
                .map_while(|p| p.parse().ok())
                .collect();
            Some((numbers, e.path()))
        })
        .collect();
    versions.sort();
    let dir = versions.pop().context("no hostfxr")?.1;
    Ok(dir.join("libhostfxr.so"))
}

impl ManagedRuntime {
    pub fn start() -> Result<Self> {
        let root = dotnet_root()?;
        let managed_dir = managed_dir()?.canonicalize()?;
        let hostfxr = Hostfxr::load_from_path(latest_hostfxr(&root)?).context("loading hostfxr")?;
        let config = PdCString::from_os_str(managed_dir.join("Unbound.Host.runtimeconfig.json"))?;
        let dotnet_exe = PdCString::from_os_str(root.join("dotnet"))?;
        let context = hostfxr
            .initialize_for_runtime_config_with_host_path(config, dotnet_exe)
            .context("initializing the .NET runtime")?;
        let assembly = PdCString::from_os_str(managed_dir.join("Unbound.Host.dll"))?;
        context
            .load_assembly_from_path(&assembly)
            .context("loading Unbound.Host")?;
        let loader = context.get_delegate_loader()?;
        let call = loader
            .get_function_with_unmanaged_callers_only::<CallFn>(
                pdcstr!("Unbound.Host.Entry, Unbound.Host"),
                pdcstr!("Call"),
            )
            .context("Unbound.Host.Entry.Call")?;
        let free = loader
            .get_function_with_unmanaged_callers_only::<FreeFn>(
                pdcstr!("Unbound.Host.Entry, Unbound.Host"),
                pdcstr!("Free"),
            )
            .context("Unbound.Host.Entry.Free")?;
        Ok(Self {
            call: *call,
            free: *free,
            managed_dir,
            loader,
            _context: context,
        })
    }

    /// A native pointer to a static `[UnmanagedCallersOnly]` method of the managed engine.
    ///
    /// # Safety
    /// `F` must be an `extern "system"` function pointer type matching the method's signature.
    pub unsafe fn function<F: Copy>(&self, type_name: &str, method: &str) -> Result<F> {
        assert_eq!(std::mem::size_of::<F>(), std::mem::size_of::<*const ()>());
        let type_pd = PdCString::from_os_str(std::ffi::OsStr::new(type_name))?;
        let method_pd = PdCString::from_os_str(std::ffi::OsStr::new(method))?;
        let function = self
            .loader
            .get_function_with_unmanaged_callers_only::<unsafe extern "system" fn()>(
                &type_pd, &method_pd,
            )
            .with_context(|| format!("{type_name}::{method}"))?;
        let raw: unsafe extern "system" fn() = *function;
        Ok(unsafe { std::mem::transmute_copy(&raw) })
    }

    pub fn managed_dir(&self) -> &Path {
        &self.managed_dir
    }

    /// Calls a control method of the managed engine with a JSON request.
    pub fn call_raw(&self, method: &str, request: &[u8]) -> Result<Vec<u8>> {
        let mut response: *mut u8 = std::ptr::null_mut();
        let mut length: i32 = 0;
        let status = unsafe {
            (self.call)(
                method.as_ptr(),
                method.len() as i32,
                request.as_ptr(),
                request.len() as i32,
                &mut response,
                &mut length,
            )
        };
        let bytes = unsafe { std::slice::from_raw_parts(response, length as usize) }.to_vec();
        unsafe { (self.free)(response) };
        if status != 0 {
            bail!(
                "managed `{method}` failed: {}",
                String::from_utf8_lossy(&bytes)
            );
        }
        Ok(bytes)
    }

    pub fn call<Req: Serialize, Resp: DeserializeOwned>(
        &self,
        method: &str,
        request: &Req,
    ) -> Result<Resp> {
        let request = serde_json::to_vec(request)?;
        let response = self.call_raw(method, &request)?;
        serde_json::from_slice(&response)
            .with_context(|| format!("decoding the response of `{method}`"))
    }
}
