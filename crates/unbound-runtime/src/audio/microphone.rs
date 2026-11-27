//! Live microphone audio capture for `UnityEngine.Microphone`.
//!
//! On Linux, devices and PCM streams come from the system's PulseAudio client library
//! (`libpulse.so.0`, which also connects to PipeWire's PulseAudio server), loaded at runtime so
//! binaries still run on systems without PulseAudio. Entry 0 is `"Default Input Device"` followed
//! by each PulseAudio source description, deduplicated; recordings capture mono float32 PCM at the
//! requested frequency directly into the mixer's `AudioClip` buffer (matches the Unity 6000.0.71f1
//! Editor, `tests/projects/audiomic`).
use crate::audio::mixer::Mixer;
use std::collections::HashMap;
use std::sync::{
    Arc, Mutex, RwLock,
    atomic::{AtomicBool, Ordering},
};

/// A recording device exposed by `Microphone.devices`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DeviceInfo {
    /// Display name exposed to scripts (`"Default Input Device"` for index 0, then source names).
    pub name: String,
    /// Backend source identifier (`None` selects the server's default input source).
    pub target: Option<String>,
}

/// Stable identifier for an active microphone stream, independent of list reordering.
#[derive(Debug, Clone, PartialEq, Eq, Hash)]
pub enum DeviceKey {
    /// Index 0 (`"Default Input Device"` / `null` / `""`).
    Default,
    /// A specific backend source identifier.
    Source(String),
}

/// Resolves a script device name against a cached device list into a borrowed target without
/// allocating: `None` when not found, `Some(None)` for the default device (`DeviceKey::Default`),
/// or `Some(Some(target))` for a specific backend source (`DeviceKey::Source`).
pub fn resolve_device_target<'a>(
    devices: &'a [DeviceInfo],
    device_name: Option<&str>,
) -> Option<Option<&'a str>> {
    if devices.is_empty() {
        return None;
    }
    match device_name {
        None | Some("") => Some(None),
        Some(name) => {
            let idx = devices.iter().position(|d| d.name == name)?;
            if idx == 0 {
                Some(None)
            } else {
                Some(devices[idx].target.as_deref())
            }
        }
    }
}

/// Resolves a script device name against a cached device list into a stable [`DeviceKey`].
pub fn resolve_device_key(devices: &[DeviceInfo], device_name: Option<&str>) -> Option<DeviceKey> {
    resolve_device_target(devices, device_name)
        .map(|target| target.map_or(DeviceKey::Default, |t| DeviceKey::Source(t.to_string())))
}

/// Pure ring-buffer state shared between a capture callback and `Microphone.GetPosition`/`IsRecording`.
#[derive(Debug)]
pub struct RecordingRing {
    total_frames: usize,
    looping: bool,
    write_pos: usize,
    active: bool,
}

impl RecordingRing {
    pub fn new(total_frames: usize, looping: bool) -> Self {
        Self {
            total_frames: total_frames.max(1),
            looping,
            write_pos: 0,
            active: true,
        }
    }

    pub fn is_active(&self) -> bool {
        self.active
    }

    pub fn position(&self) -> usize {
        if self.active { self.write_pos } else { 0 }
    }

    /// Writes mono `incoming` frames into `clip_samples` (which must have at least `total_frames`
    /// elements). Returns `true` if recording remains active, `false` once a non-looping capture
    /// finishes (resetting its reported position to 0, as observed in `tests/projects/audiomic`).
    pub fn push_mono(&mut self, incoming: &[f32], clip_samples: &mut [f32]) -> bool {
        if !self.active || incoming.is_empty() || clip_samples.is_empty() {
            return self.active;
        }
        let cap = self.total_frames.min(clip_samples.len());
        if cap == 0 {
            self.active = false;
            self.write_pos = 0;
            return false;
        }
        let mut src = 0;
        while src < incoming.len() && self.active {
            let space = cap - self.write_pos;
            let take = (incoming.len() - src).min(space);
            clip_samples[self.write_pos..self.write_pos + take]
                .copy_from_slice(&incoming[src..src + take]);
            src += take;
            self.write_pos += take;
            if self.write_pos >= cap {
                if self.looping {
                    self.write_pos = 0;
                } else {
                    self.write_pos = 0;
                    self.active = false;
                }
            }
        }
        self.active
    }
}

/// Deduplicates display names by appending `" (1)"`, `" (2)"`, ... when a name already occurred.
pub fn deduplicate_names(
    raw: impl IntoIterator<Item = (String, Option<String>)>,
) -> Vec<DeviceInfo> {
    let mut seen: HashMap<String, usize> = HashMap::new();
    let mut out = Vec::new();
    for (base, target) in raw {
        let mut candidate = base.clone();
        if seen.contains_key(&candidate) {
            let mut n = 1usize;
            loop {
                candidate = format!("{base} ({n})");
                if !seen.contains_key(&candidate) {
                    break;
                }
                n += 1;
            }
        }
        seen.insert(candidate.clone(), out.len());
        out.push(DeviceInfo {
            name: candidate,
            target,
        });
    }
    out
}

/// Result of starting a recording.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct StartedRecording {
    pub channels: i32,
    pub frames: i32,
    pub frequency: i32,
}

#[cfg(target_os = "linux")]
mod backend {
    use super::*;
    use crate::audio::mixer::Clip;
    use std::ffi::{CStr, CString, c_char, c_int, c_void};
    use std::sync::atomic::AtomicUsize;

    const PA_CONTEXT_READY: c_int = 4;
    const PA_CONTEXT_FAILED: c_int = 5;
    const PA_CONTEXT_TERMINATED: c_int = 6;

    const PA_STREAM_READY: c_int = 2;
    const PA_STREAM_FAILED: c_int = 3;
    const PA_STREAM_TERMINATED: c_int = 4;

    const PA_OPERATION_RUNNING: c_int = 0;

    const PA_SAMPLE_FLOAT32LE: c_int = 5;
    const PA_STREAM_ADJUST_LATENCY: u32 = 0x2000;

    #[repr(C)]
    #[derive(Clone, Copy)]
    struct PaSampleSpec {
        format: c_int,
        rate: u32,
        channels: u8,
    }

    #[repr(C)]
    #[derive(Clone, Copy)]
    struct PaBufferAttr {
        maxlength: u32,
        tlength: u32,
        prebuf: u32,
        minreq: u32,
        fragsize: u32,
    }

    /// Prefix of `pa_source_info` (PulseAudio ABI `PULSE_0`).
    #[repr(C)]
    struct PaSourceInfoHead {
        name: *const c_char,
        index: u32,
        description: *const c_char,
        sample_spec: PaSampleSpec,
    }

    /// Prefix of `pa_server_info` (PulseAudio ABI `PULSE_0`).
    #[repr(C)]
    struct PaServerInfoHead {
        user_name: *const c_char,
        host_name: *const c_char,
        server_version: *const c_char,
        server_name: *const c_char,
        sample_spec: PaSampleSpec,
        default_sink_name: *const c_char,
        default_source_name: *const c_char,
    }

    type PaContextNotifyCb = unsafe extern "C" fn(*mut c_void, *mut c_void);
    type PaServerInfoCb = unsafe extern "C" fn(*mut c_void, *const PaServerInfoHead, *mut c_void);
    type PaSourceInfoCb =
        unsafe extern "C" fn(*mut c_void, *const PaSourceInfoHead, c_int, *mut c_void);
    type PaStreamNotifyCb = unsafe extern "C" fn(*mut c_void, *mut c_void);
    type PaStreamRequestCb = unsafe extern "C" fn(*mut c_void, usize, *mut c_void);

    macro_rules! pulse_api {
        ($($name:ident: fn($($arg:ty),*) $(-> $ret:ty)?;)*) => {
            #[allow(non_snake_case)]
            struct PulseApi {
                _library: libloading::Library,
                $($name: unsafe extern "C" fn($($arg),*) $(-> $ret)?,)*
            }

            impl PulseApi {
                unsafe fn load(library: libloading::Library) -> Result<Self, libloading::Error> {
                    Ok(Self {
                        $($name: unsafe { *library.get(concat!(stringify!($name), "\0").as_bytes())? },)*
                        _library: library,
                    })
                }
            }
        };
    }

    pulse_api! {
        pa_threaded_mainloop_new: fn() -> *mut c_void;
        pa_threaded_mainloop_free: fn(*mut c_void);
        pa_threaded_mainloop_start: fn(*mut c_void) -> c_int;
        pa_threaded_mainloop_stop: fn(*mut c_void);
        pa_threaded_mainloop_lock: fn(*mut c_void);
        pa_threaded_mainloop_unlock: fn(*mut c_void);
        pa_threaded_mainloop_wait: fn(*mut c_void);
        pa_threaded_mainloop_signal: fn(*mut c_void, c_int);
        pa_threaded_mainloop_get_api: fn(*mut c_void) -> *mut c_void;

        pa_context_new: fn(*mut c_void, *const c_char) -> *mut c_void;
        pa_context_unref: fn(*mut c_void);
        pa_context_set_state_callback: fn(*mut c_void, Option<PaContextNotifyCb>, *mut c_void);
        pa_context_connect: fn(*mut c_void, *const c_char, c_int, *const c_void) -> c_int;
        pa_context_disconnect: fn(*mut c_void);
        pa_context_get_state: fn(*mut c_void) -> c_int;
        pa_context_get_server_info: fn(*mut c_void, PaServerInfoCb, *mut c_void) -> *mut c_void;
        pa_context_get_source_info_list: fn(*mut c_void, PaSourceInfoCb, *mut c_void) -> *mut c_void;

        pa_operation_get_state: fn(*mut c_void) -> c_int;
        pa_operation_unref: fn(*mut c_void);

        pa_stream_new: fn(*mut c_void, *const c_char, *const PaSampleSpec, *const c_void) -> *mut c_void;
        pa_stream_unref: fn(*mut c_void);
        pa_stream_set_state_callback: fn(*mut c_void, Option<PaStreamNotifyCb>, *mut c_void);
        pa_stream_set_read_callback: fn(*mut c_void, Option<PaStreamRequestCb>, *mut c_void);
        pa_stream_connect_record: fn(*mut c_void, *const c_char, *const PaBufferAttr, u32) -> c_int;
        pa_stream_disconnect: fn(*mut c_void) -> c_int;
        pa_stream_get_state: fn(*mut c_void) -> c_int;
        pa_stream_peek: fn(*mut c_void, *mut *const c_void, *mut usize) -> c_int;
        pa_stream_drop: fn(*mut c_void) -> c_int;
    }

    /// Atomic polling state shared between the capture callback and `QueryTable`.
    struct StreamQueryState {
        active: AtomicBool,
        position: AtomicUsize,
    }

    struct StreamCallbackState {
        api: Arc<PulseApi>,
        mixer: Arc<Mutex<Mixer>>,
        clip_handle: u64,
        ring: Mutex<RecordingRing>,
        query: Arc<StreamQueryState>,
        stopped: AtomicBool,
        scratch: Mutex<Vec<f32>>,
    }

    struct ActiveStream {
        stream: *mut c_void,
        clip_handle: u64,
        state: Arc<StreamCallbackState>,
        /// Raw `Arc::into_raw` pointer passed as `userdata`; reclaimed after callback is cleared.
        userdata: *const StreamCallbackState,
    }

    unsafe impl Send for ActiveStream {}

    struct PulseClient {
        api: Arc<PulseApi>,
        mainloop: *mut c_void,
        context: *mut c_void,
        /// Heap-allocated context state callback userdata; kept alive until Drop clears callback.
        context_signal: *mut SignalData,
        streams: HashMap<DeviceKey, ActiveStream>,
        cached_devices: Vec<DeviceInfo>,
    }

    unsafe impl Send for PulseClient {}

    struct SignalData {
        api: *const PulseApi,
        mainloop: *mut c_void,
    }

    unsafe extern "C" fn context_state_cb(_ctx: *mut c_void, userdata: *mut c_void) {
        if userdata.is_null() {
            return;
        }
        unsafe {
            let sig = &*(userdata as *const SignalData);
            ((*sig.api).pa_threaded_mainloop_signal)(sig.mainloop, 0);
        }
    }

    unsafe extern "C" fn stream_state_cb(_stream: *mut c_void, userdata: *mut c_void) {
        if userdata.is_null() {
            return;
        }
        unsafe {
            let sig = &*(userdata as *const SignalData);
            ((*sig.api).pa_threaded_mainloop_signal)(sig.mainloop, 0);
        }
    }

    struct ServerInfoCollect {
        api: *const PulseApi,
        mainloop: *mut c_void,
        default_source: Option<String>,
    }

    unsafe extern "C" fn server_info_cb(
        _ctx: *mut c_void,
        info: *const PaServerInfoHead,
        userdata: *mut c_void,
    ) {
        unsafe {
            let out = &mut *(userdata as *mut ServerInfoCollect);
            if !info.is_null() && !(*info).default_source_name.is_null() {
                let s = CStr::from_ptr((*info).default_source_name)
                    .to_string_lossy()
                    .into_owned();
                if !s.is_empty() {
                    out.default_source = Some(s);
                }
            }
            ((*out.api).pa_threaded_mainloop_signal)(out.mainloop, 0);
        }
    }

    struct SourceListCollect {
        api: *const PulseApi,
        mainloop: *mut c_void,
        sources: Vec<(String, String)>,
    }

    unsafe extern "C" fn source_list_cb(
        _ctx: *mut c_void,
        info: *const PaSourceInfoHead,
        eol: c_int,
        userdata: *mut c_void,
    ) {
        unsafe {
            let out = &mut *(userdata as *mut SourceListCollect);
            if eol != 0 {
                ((*out.api).pa_threaded_mainloop_signal)(out.mainloop, 0);
                return;
            }
            if !info.is_null() {
                let name = if (*info).name.is_null() {
                    String::new()
                } else {
                    CStr::from_ptr((*info).name).to_string_lossy().into_owned()
                };
                let desc = if (*info).description.is_null() {
                    String::new()
                } else {
                    CStr::from_ptr((*info).description)
                        .to_string_lossy()
                        .into_owned()
                };
                let display = if desc.is_empty() { name.clone() } else { desc };
                if !display.is_empty() {
                    out.sources.push((display, name));
                }
            }
        }
    }

    unsafe extern "C" fn stream_read_cb(
        stream: *mut c_void,
        _nbytes: usize,
        userdata: *mut c_void,
    ) {
        if userdata.is_null() {
            return;
        }
        let state = unsafe { &*(userdata as *const StreamCallbackState) };
        if state.stopped.load(Ordering::Acquire) || !state.query.active.load(Ordering::Acquire) {
            return;
        }
        loop {
            let mut ptr: *const c_void = std::ptr::null();
            let mut len: usize = 0;
            let rc = unsafe { (state.api.pa_stream_peek)(stream, &mut ptr, &mut len) };
            if rc < 0 || len == 0 {
                break;
            }
            let frames = len / std::mem::size_of::<f32>();
            if frames > 0 && !state.stopped.load(Ordering::Acquire) {
                let mut scratch = state.scratch.lock().unwrap_or_else(|e| e.into_inner());
                scratch.clear();
                scratch.reserve(frames);
                if ptr.is_null() {
                    scratch.resize(frames, 0.0);
                } else {
                    let bytes = unsafe { std::slice::from_raw_parts(ptr as *const u8, frames * 4) };
                    for chunk in bytes.chunks_exact(4) {
                        scratch.push(f32::from_le_bytes([chunk[0], chunk[1], chunk[2], chunk[3]]));
                    }
                }
                let mut ring = state.ring.lock().unwrap_or_else(|e| e.into_inner());
                let still_active = {
                    let mut mixer = state.mixer.lock().unwrap_or_else(|e| e.into_inner());
                    if let Some(clip) = mixer.clip_mut(state.clip_handle) {
                        ring.push_mono(&scratch, &mut clip.samples)
                    } else {
                        ring.active = false;
                        ring.write_pos = 0;
                        false
                    }
                };
                state
                    .query
                    .position
                    .store(ring.position(), Ordering::Release);
                state.query.active.store(still_active, Ordering::Release);
                drop(ring);
                drop(scratch);
            }
            unsafe {
                (state.api.pa_stream_drop)(stream);
            }
            if !state.query.active.load(Ordering::Acquire) {
                unsafe {
                    (state.api.pa_stream_set_read_callback)(stream, None, std::ptr::null_mut());
                    (state.api.pa_stream_disconnect)(stream);
                }
                break;
            }
        }
    }

    impl PulseClient {
        fn connect() -> Option<Self> {
            let names = ["libpulse.so.0", "libpulse.so"];
            let library = names
                .iter()
                .find_map(|name| unsafe { libloading::Library::new(name) }.ok())?;
            let api = Arc::new(unsafe { PulseApi::load(library) }.ok()?);
            unsafe {
                let mainloop = (api.pa_threaded_mainloop_new)();
                if mainloop.is_null() {
                    return None;
                }
                let ml_api = (api.pa_threaded_mainloop_get_api)(mainloop);
                let app_name = CString::new("Unbound").unwrap();
                let context = (api.pa_context_new)(ml_api, app_name.as_ptr());
                if context.is_null() {
                    (api.pa_threaded_mainloop_free)(mainloop);
                    return None;
                }
                let context_signal = Box::into_raw(Box::new(SignalData {
                    api: Arc::as_ptr(&api),
                    mainloop,
                }));
                (api.pa_context_set_state_callback)(
                    context,
                    Some(context_state_cb),
                    context_signal as *mut c_void,
                );
                if (api.pa_context_connect)(context, std::ptr::null(), 0, std::ptr::null()) < 0 {
                    (api.pa_context_set_state_callback)(context, None, std::ptr::null_mut());
                    (api.pa_context_unref)(context);
                    (api.pa_threaded_mainloop_free)(mainloop);
                    drop(Box::from_raw(context_signal));
                    return None;
                }
                if (api.pa_threaded_mainloop_start)(mainloop) < 0 {
                    (api.pa_context_set_state_callback)(context, None, std::ptr::null_mut());
                    (api.pa_context_disconnect)(context);
                    (api.pa_context_unref)(context);
                    (api.pa_threaded_mainloop_free)(mainloop);
                    drop(Box::from_raw(context_signal));
                    return None;
                }
                (api.pa_threaded_mainloop_lock)(mainloop);
                let mut ready = false;
                loop {
                    let st = (api.pa_context_get_state)(context);
                    if st == PA_CONTEXT_READY {
                        ready = true;
                        break;
                    }
                    if st == PA_CONTEXT_FAILED || st == PA_CONTEXT_TERMINATED {
                        break;
                    }
                    (api.pa_threaded_mainloop_wait)(mainloop);
                }
                if !ready {
                    (api.pa_context_set_state_callback)(context, None, std::ptr::null_mut());
                    (api.pa_threaded_mainloop_unlock)(mainloop);
                    (api.pa_threaded_mainloop_stop)(mainloop);
                    (api.pa_context_disconnect)(context);
                    (api.pa_context_unref)(context);
                    (api.pa_threaded_mainloop_free)(mainloop);
                    drop(Box::from_raw(context_signal));
                    return None;
                }
                (api.pa_threaded_mainloop_unlock)(mainloop);
                Some(Self {
                    api,
                    mainloop,
                    context,
                    context_signal,
                    streams: HashMap::new(),
                    cached_devices: Vec::new(),
                })
            }
        }

        fn refresh_devices(&mut self) -> &[DeviceInfo] {
            self.reap_finished();
            let api = &self.api;
            unsafe {
                (api.pa_threaded_mainloop_lock)(self.mainloop);
                if (api.pa_context_get_state)(self.context) != PA_CONTEXT_READY {
                    (api.pa_threaded_mainloop_unlock)(self.mainloop);
                    self.cached_devices.clear();
                    return &self.cached_devices;
                }
                let mut server = ServerInfoCollect {
                    api: Arc::as_ptr(api),
                    mainloop: self.mainloop,
                    default_source: None,
                };
                let op = (api.pa_context_get_server_info)(
                    self.context,
                    server_info_cb,
                    &mut server as *mut _ as *mut c_void,
                );
                if !op.is_null() {
                    while (api.pa_operation_get_state)(op) == PA_OPERATION_RUNNING
                        && (api.pa_context_get_state)(self.context) == PA_CONTEXT_READY
                    {
                        (api.pa_threaded_mainloop_wait)(self.mainloop);
                    }
                    (api.pa_operation_unref)(op);
                }
                let mut list = SourceListCollect {
                    api: Arc::as_ptr(api),
                    mainloop: self.mainloop,
                    sources: Vec::new(),
                };
                if (api.pa_context_get_state)(self.context) == PA_CONTEXT_READY {
                    let op = (api.pa_context_get_source_info_list)(
                        self.context,
                        source_list_cb,
                        &mut list as *mut _ as *mut c_void,
                    );
                    if !op.is_null() {
                        while (api.pa_operation_get_state)(op) == PA_OPERATION_RUNNING
                            && (api.pa_context_get_state)(self.context) == PA_CONTEXT_READY
                        {
                            (api.pa_threaded_mainloop_wait)(self.mainloop);
                        }
                        (api.pa_operation_unref)(op);
                    }
                }
                (api.pa_threaded_mainloop_unlock)(self.mainloop);

                if list.sources.is_empty() {
                    self.cached_devices.clear();
                } else {
                    let mut raw = Vec::with_capacity(list.sources.len() + 1);
                    raw.push((
                        "Default Input Device".to_string(),
                        server.default_source.clone(),
                    ));
                    for (display, pa_name) in list.sources {
                        raw.push((display, Some(pa_name)));
                    }
                    self.cached_devices = deduplicate_names(raw);
                }
            }
            &self.cached_devices
        }

        /// Resolves a device name from cached devices only (no PulseAudio lock or IPC).
        fn resolve_device_key_cached(&self, device_name: Option<&str>) -> Option<DeviceKey> {
            resolve_device_key(&self.cached_devices, device_name)
        }

        /// Resolves a device name, lazily populating the device cache once when empty.
        fn resolve_device_key_or_init(&mut self, device_name: Option<&str>) -> Option<DeviceKey> {
            if self.cached_devices.is_empty() {
                self.refresh_devices();
            }
            resolve_device_key(&self.cached_devices, device_name)
        }

        fn target_for_key<'a>(&'a self, key: &'a DeviceKey) -> Option<&'a str> {
            match key {
                DeviceKey::Default => self
                    .cached_devices
                    .first()
                    .and_then(|d| d.target.as_deref()),
                DeviceKey::Source(s) => Some(s.as_str()),
            }
        }

        fn stop_stream_entry(&mut self, key: &DeviceKey) {
            let Some(active) = self.streams.remove(key) else {
                return;
            };
            active.state.stopped.store(true, Ordering::Release);
            let api = &self.api;
            unsafe {
                (api.pa_threaded_mainloop_lock)(self.mainloop);
                (api.pa_stream_set_read_callback)(active.stream, None, std::ptr::null_mut());
                (api.pa_stream_set_state_callback)(active.stream, None, std::ptr::null_mut());
                (api.pa_stream_disconnect)(active.stream);
                (api.pa_stream_unref)(active.stream);
                (api.pa_threaded_mainloop_unlock)(self.mainloop);
                // Safe: callbacks were cleared under the mainloop lock so `stream_read_cb`
                // cannot run again. Clear the query state only now so `StreamQueryState`
                // always has a single writer and never flips false -> true -> false.
                active.state.query.active.store(false, Ordering::Release);
                active.state.query.position.store(0, Ordering::Release);
                drop(Arc::from_raw(active.userdata));
            }
        }

        fn reap_finished(&mut self) {
            let finished: Vec<DeviceKey> = self
                .streams
                .iter()
                .filter_map(|(k, s)| {
                    (!s.state.query.active.load(Ordering::Acquire)).then(|| k.clone())
                })
                .collect();
            for key in finished {
                self.stop_stream_entry(&key);
            }
        }

        fn stop_all(&mut self) {
            let keys: Vec<DeviceKey> = self.streams.keys().cloned().collect();
            for key in keys {
                self.stop_stream_entry(&key);
            }
        }

        fn stop_for_clip(&mut self, clip_handle: u64) {
            let keys: Vec<DeviceKey> = self
                .streams
                .iter()
                .filter_map(|(k, s)| (s.clip_handle == clip_handle).then(|| k.clone()))
                .collect();
            for key in keys {
                self.stop_stream_entry(&key);
            }
        }

        fn start_recording(
            &mut self,
            device_name: Option<&str>,
            looping: bool,
            length_sec: i32,
            frequency: i32,
            clip_handle: u64,
            mixer: Arc<Mutex<Mixer>>,
        ) -> Result<StartedRecording, String> {
            self.reap_finished();
            let Some(key) = self.resolve_device_key_or_init(device_name) else {
                return Err(format!(
                    "Couldn't acquire device ID for device name {}",
                    device_name.unwrap_or("")
                ));
            };
            if length_sec <= 0 || frequency <= 0 {
                return Err("Recording buffer size out of range".to_string());
            }
            let total_frames = (length_sec as usize).saturating_mul(frequency as usize);
            if total_frames == 0 || total_frames > i32::MAX as usize {
                return Err("Recording buffer size out of range".to_string());
            }
            // Replace any active recording on this device after validating parameters
            // (matches audiomic rec3.double_start).
            self.stop_stream_entry(&key);

            // Insert the backing clip BEFORE taking the PulseAudio mainloop lock (lock order!).
            {
                let mut m = mixer.lock().unwrap_or_else(|e| e.into_inner());
                m.add_clip(
                    clip_handle,
                    Clip::new(1, frequency as u32, vec![0.0; total_frames]),
                );
            }

            let target = self.target_for_key(&key).and_then(|s| CString::new(s).ok());
            let spec = PaSampleSpec {
                format: PA_SAMPLE_FLOAT32LE,
                rate: frequency as u32,
                channels: 1,
            };
            let frag_frames = ((frequency as u32) / 50).clamp(256, 2048);
            let attr = PaBufferAttr {
                maxlength: u32::MAX,
                tlength: u32::MAX,
                prebuf: u32::MAX,
                minreq: u32::MAX,
                fragsize: frag_frames * 4,
            };
            let query = Arc::new(StreamQueryState {
                active: AtomicBool::new(true),
                position: AtomicUsize::new(0),
            });
            let state = Arc::new(StreamCallbackState {
                api: self.api.clone(),
                mixer: mixer.clone(),
                clip_handle,
                ring: Mutex::new(RecordingRing::new(total_frames, looping)),
                query,
                stopped: AtomicBool::new(false),
                scratch: Mutex::new(Vec::with_capacity(frag_frames as usize)),
            });
            let userdata = Arc::into_raw(state.clone());
            let api = &self.api;
            let stream_name = CString::new("Unity Microphone").unwrap();
            let stream = unsafe {
                (api.pa_threaded_mainloop_lock)(self.mainloop);
                let stream = (api.pa_stream_new)(
                    self.context,
                    stream_name.as_ptr(),
                    &spec,
                    std::ptr::null(),
                );
                if stream.is_null() {
                    (api.pa_threaded_mainloop_unlock)(self.mainloop);
                    drop(Arc::from_raw(userdata));
                    mixer
                        .lock()
                        .unwrap_or_else(|e| e.into_inner())
                        .release_clip(clip_handle);
                    return Err("PulseAudio failed to create recording stream".to_string());
                }
                let sig = SignalData {
                    api: Arc::as_ptr(api),
                    mainloop: self.mainloop,
                };
                (api.pa_stream_set_state_callback)(
                    stream,
                    Some(stream_state_cb),
                    &sig as *const _ as *mut c_void,
                );
                (api.pa_stream_set_read_callback)(
                    stream,
                    Some(stream_read_cb),
                    userdata as *mut c_void,
                );
                let target_ptr = target.as_ref().map_or(std::ptr::null(), |c| c.as_ptr());
                let rc = (api.pa_stream_connect_record)(
                    stream,
                    target_ptr,
                    &attr,
                    PA_STREAM_ADJUST_LATENCY,
                );
                let mut ready = false;
                if rc == 0 {
                    loop {
                        let st = (api.pa_stream_get_state)(stream);
                        if st == PA_STREAM_READY {
                            ready = true;
                            break;
                        }
                        if st == PA_STREAM_FAILED || st == PA_STREAM_TERMINATED {
                            break;
                        }
                        (api.pa_threaded_mainloop_wait)(self.mainloop);
                    }
                }
                (api.pa_stream_set_state_callback)(stream, None, std::ptr::null_mut());
                if !ready {
                    (api.pa_stream_set_read_callback)(stream, None, std::ptr::null_mut());
                    (api.pa_stream_disconnect)(stream);
                    (api.pa_stream_unref)(stream);
                    (api.pa_threaded_mainloop_unlock)(self.mainloop);
                    drop(Arc::from_raw(userdata));
                    mixer
                        .lock()
                        .unwrap_or_else(|e| e.into_inner())
                        .release_clip(clip_handle);
                    return Err("PulseAudio failed to connect recording stream".to_string());
                }
                (api.pa_threaded_mainloop_unlock)(self.mainloop);
                stream
            };

            self.streams.insert(
                key,
                ActiveStream {
                    stream,
                    clip_handle,
                    state,
                    userdata,
                },
            );
            Ok(StartedRecording {
                channels: 1,
                frames: total_frames as i32,
                frequency,
            })
        }
    }

    impl Drop for PulseClient {
        fn drop(&mut self) {
            self.stop_all();
            let api = &self.api;
            unsafe {
                (api.pa_threaded_mainloop_lock)(self.mainloop);
                (api.pa_context_set_state_callback)(self.context, None, std::ptr::null_mut());
                (api.pa_threaded_mainloop_unlock)(self.mainloop);
                (api.pa_threaded_mainloop_stop)(self.mainloop);
                (api.pa_context_disconnect)(self.context);
                (api.pa_context_unref)(self.context);
                (api.pa_threaded_mainloop_free)(self.mainloop);
                drop(Box::from_raw(self.context_signal));
            }
        }
    }

    enum ClientState {
        Uninitialized,
        Unavailable,
        Ready(PulseClient),
    }

    /// Snapshot read by polling queries (`is_recording`, `is_any_recording`, `position`).
    /// Guarded by a leaf `RwLock` that is never held across `Mixer` or PulseAudio locks, so
    /// polling from `OnAudioFilterRead` (which holds `Mixer`) cannot deadlock with control ops.
    /// Split into `default_stream` + `source_streams` so lookups borrow `&str` with zero
    /// heap allocations on the audio/per-frame hot path.
    #[derive(Default)]
    struct QueryTable {
        devices: Vec<DeviceInfo>,
        default_stream: Option<Arc<StreamQueryState>>,
        source_streams: HashMap<String, Arc<StreamQueryState>>,
    }

    impl QueryTable {
        fn stream_for(&self, device_name: Option<&str>) -> Option<&Arc<StreamQueryState>> {
            match resolve_device_target(&self.devices, device_name)? {
                None => self.default_stream.as_ref(),
                Some(target) => self.source_streams.get(target),
            }
        }
    }

    pub struct PlatformRecorder {
        state: Mutex<ClientState>,
        query: RwLock<QueryTable>,
    }

    impl PlatformRecorder {
        pub fn new() -> Self {
            Self {
                state: Mutex::new(ClientState::Uninitialized),
                query: RwLock::new(QueryTable::default()),
            }
        }

        /// Publishes `client`'s cached devices and active stream atomics to the leaf query table.
        /// Callers must not hold `Mixer` or `pa_threaded_mainloop_lock`.
        fn sync_query(&self, client: &PulseClient) {
            let mut q = self.query.write().unwrap_or_else(|e| e.into_inner());
            q.devices.clone_from(&client.cached_devices);
            q.default_stream = None;
            q.source_streams.clear();
            for (key, active) in &client.streams {
                let entry = Arc::clone(&active.state.query);
                match key {
                    DeviceKey::Default => q.default_stream = Some(entry),
                    DeviceKey::Source(source) => {
                        q.source_streams.insert(source.clone(), entry);
                    }
                }
            }
        }

        fn with_client<T>(&self, f: impl FnOnce(&mut PulseClient) -> T, fallback: T) -> T {
            let mut guard = self.state.lock().unwrap_or_else(|e| e.into_inner());
            if matches!(*guard, ClientState::Uninitialized) {
                *guard = match PulseClient::connect() {
                    Some(client) => ClientState::Ready(client),
                    None => {
                        tracing::warn!(
                            "libpulse is unavailable or no PulseAudio server is running: Microphone has no input devices"
                        );
                        ClientState::Unavailable
                    }
                };
            }
            match &mut *guard {
                ClientState::Ready(client) => {
                    let result = f(client);
                    self.sync_query(client);
                    result
                }
                _ => fallback,
            }
        }

        pub fn devices(&self) -> Vec<String> {
            self.with_client(
                |c| c.refresh_devices().iter().map(|d| d.name.clone()).collect(),
                Vec::new(),
            )
        }

        pub fn device_caps(&self, _device_name: Option<&str>) -> (i32, i32) {
            // PulseAudio / PipeWire resamples arbitrary rates, matching Unity 6000.0.71f1
            // (tests/projects/audiomic caps.*: minFreq=0, maxFreq=0).
            (0, 0)
        }

        pub fn start(
            &self,
            device_name: Option<&str>,
            looping: bool,
            length_sec: i32,
            frequency: i32,
            clip_handle: u64,
            mixer: Arc<Mutex<Mixer>>,
        ) -> Result<StartedRecording, String> {
            self.with_client(
                |c| {
                    c.start_recording(
                        device_name,
                        looping,
                        length_sec,
                        frequency,
                        clip_handle,
                        mixer,
                    )
                },
                Err(format!(
                    "Couldn't acquire device ID for device name {}",
                    device_name.unwrap_or("")
                )),
            )
        }

        pub fn end(&self, device_name: Option<&str>) {
            let mut guard = self.state.lock().unwrap_or_else(|e| e.into_inner());
            if let ClientState::Ready(client) = &mut *guard {
                client.reap_finished();
                if let Some(key) = client.resolve_device_key_cached(device_name) {
                    client.stop_stream_entry(&key);
                }
                self.sync_query(client);
            }
        }

        pub fn is_recording(&self, device_name: Option<&str>) -> bool {
            let q = self.query.read().unwrap_or_else(|e| e.into_inner());
            q.stream_for(device_name)
                .is_some_and(|s| s.active.load(Ordering::Acquire))
        }

        pub fn is_any_recording(&self) -> bool {
            let q = self.query.read().unwrap_or_else(|e| e.into_inner());
            q.default_stream
                .as_ref()
                .is_some_and(|s| s.active.load(Ordering::Acquire))
                || q.source_streams
                    .values()
                    .any(|s| s.active.load(Ordering::Acquire))
        }

        pub fn position(&self, device_name: Option<&str>) -> i32 {
            let q = self.query.read().unwrap_or_else(|e| e.into_inner());
            match q.stream_for(device_name) {
                Some(s) if s.active.load(Ordering::Acquire) => {
                    s.position.load(Ordering::Acquire) as i32
                }
                _ => 0,
            }
        }

        pub fn stop_all(&self) {
            let mut guard = self.state.lock().unwrap_or_else(|e| e.into_inner());
            if let ClientState::Ready(client) = &mut *guard {
                client.stop_all();
                self.sync_query(client);
            }
        }

        pub fn on_clip_released(&self, clip_handle: u64) {
            let mut guard = self.state.lock().unwrap_or_else(|e| e.into_inner());
            if let ClientState::Ready(client) = &mut *guard {
                client.stop_for_clip(clip_handle);
                self.sync_query(client);
            }
        }

        #[cfg(test)]
        pub(super) fn is_uninitialized(&self) -> bool {
            matches!(
                *self.state.lock().unwrap_or_else(|e| e.into_inner()),
                ClientState::Uninitialized
            )
        }

        #[cfg(test)]
        pub(super) fn test_with_state_locked(
            &self,
            devices: Vec<DeviceInfo>,
            streams: &[(DeviceKey, usize, bool)],
            f: impl FnOnce(),
        ) {
            let _guard = self.state.lock().unwrap();
            {
                let mut q = self.query.write().unwrap();
                q.devices = devices;
                q.default_stream = None;
                q.source_streams.clear();
                for (key, position, active) in streams {
                    let query_state = Arc::new(StreamQueryState {
                        active: AtomicBool::new(*active),
                        position: AtomicUsize::new(*position),
                    });
                    match key {
                        DeviceKey::Default => q.default_stream = Some(query_state),
                        DeviceKey::Source(source) => {
                            q.source_streams.insert(source.clone(), query_state);
                        }
                    }
                }
            }
            f();
        }
    }
}

#[cfg(not(target_os = "linux"))]
mod backend {
    use super::*;

    pub struct PlatformRecorder {
        warned: AtomicBool,
    }

    impl PlatformRecorder {
        pub fn new() -> Self {
            Self {
                warned: AtomicBool::new(false),
            }
        }

        fn warn_once(&self) {
            if !self.warned.swap(true, Ordering::Relaxed) {
                tracing::warn!(
                    "Unbound: UnityEngine.Microphone audio capture is not implemented on this OS"
                );
            }
        }

        pub fn devices(&self) -> Vec<String> {
            self.warn_once();
            Vec::new()
        }

        pub fn device_caps(&self, _device_name: Option<&str>) -> (i32, i32) {
            (0, 0)
        }

        pub fn start(
            &self,
            device_name: Option<&str>,
            _looping: bool,
            _length_sec: i32,
            _frequency: i32,
            _clip_handle: u64,
            _mixer: Arc<Mutex<Mixer>>,
        ) -> Result<StartedRecording, String> {
            self.warn_once();
            Err(format!(
                "Couldn't acquire device ID for device name {}",
                device_name.unwrap_or("")
            ))
        }

        pub fn end(&self, _device_name: Option<&str>) {}
        pub fn is_recording(&self, _device_name: Option<&str>) -> bool {
            false
        }
        pub fn is_any_recording(&self) -> bool {
            false
        }
        pub fn position(&self, _device_name: Option<&str>) -> i32 {
            0
        }
        pub fn stop_all(&self) {}
        pub fn on_clip_released(&self, _clip_handle: u64) {}
    }
}

/// Owns active microphone capture streams for an audio [`super::Engine`].
pub struct Recorder {
    inner: backend::PlatformRecorder,
}

impl Default for Recorder {
    fn default() -> Self {
        Self::new()
    }
}

impl Recorder {
    pub fn new() -> Self {
        Self {
            inner: backend::PlatformRecorder::new(),
        }
    }

    pub fn devices(&self) -> Vec<String> {
        self.inner.devices()
    }

    pub fn device_caps(&self, device_name: Option<&str>) -> (i32, i32) {
        self.inner.device_caps(device_name)
    }

    pub fn start(
        &self,
        device_name: Option<&str>,
        looping: bool,
        length_sec: i32,
        frequency: i32,
        clip_handle: u64,
        mixer: Arc<Mutex<Mixer>>,
    ) -> Result<StartedRecording, String> {
        self.inner.start(
            device_name,
            looping,
            length_sec,
            frequency,
            clip_handle,
            mixer,
        )
    }

    pub fn end(&self, device_name: Option<&str>) {
        self.inner.end(device_name);
    }

    pub fn is_recording(&self, device_name: Option<&str>) -> bool {
        self.inner.is_recording(device_name)
    }

    pub fn is_any_recording(&self) -> bool {
        self.inner.is_any_recording()
    }

    pub fn position(&self, device_name: Option<&str>) -> i32 {
        self.inner.position(device_name)
    }

    pub fn stop_all(&self) {
        self.inner.stop_all();
    }

    pub fn on_clip_released(&self, clip_handle: u64) {
        self.inner.on_clip_released(clip_handle);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn non_looping_ring_writes_sequentially_and_stops_at_end_with_position_zero() {
        let mut ring = RecordingRing::new(6, false);
        let mut clip = vec![0.0f32; 6];

        assert!(ring.push_mono(&[0.1, 0.2, 0.3], &mut clip));
        assert!(ring.is_active());
        assert_eq!(ring.position(), 3);
        assert_eq!(&clip[..3], &[0.1, 0.2, 0.3]);

        // Writing past the end fills the remaining slots, stops, and resets position to 0.
        assert!(!ring.push_mono(&[0.4, 0.5, 0.6, 0.7, 0.8], &mut clip));
        assert!(!ring.is_active());
        assert_eq!(ring.position(), 0);
        assert_eq!(clip, vec![0.1, 0.2, 0.3, 0.4, 0.5, 0.6]);

        // Further writes after completion are ignored.
        assert!(!ring.push_mono(&[0.9], &mut clip));
        assert_eq!(clip[0], 0.1);
    }

    #[test]
    fn looping_ring_wraps_around_and_keeps_recording() {
        let mut ring = RecordingRing::new(4, true);
        let mut clip = vec![0.0f32; 4];

        assert!(ring.push_mono(&[1.0, 2.0, 3.0], &mut clip));
        assert_eq!(ring.position(), 3);

        assert!(ring.push_mono(&[4.0, 5.0, 6.0], &mut clip));
        assert!(ring.is_active());
        assert_eq!(ring.position(), 2);
        assert_eq!(clip, vec![5.0, 6.0, 3.0, 4.0]);
    }

    #[test]
    fn duplicate_device_names_get_numbered_suffixes() {
        let list = deduplicate_names([
            ("Default Input Device".into(), None),
            ("USB Mic".into(), Some("src0".into())),
            ("USB Mic".into(), Some("src1".into())),
            ("USB Mic".into(), Some("src2".into())),
        ]);
        let names: Vec<&str> = list.iter().map(|d| d.name.as_str()).collect();
        assert_eq!(
            names,
            vec![
                "Default Input Device",
                "USB Mic",
                "USB Mic (1)",
                "USB Mic (2)"
            ]
        );
    }

    #[test]
    fn device_key_stays_stable_when_other_devices_disconnect() {
        let before = deduplicate_names([
            ("Default Input Device".into(), Some("src0".into())),
            ("Headset Mic".into(), Some("src1".into())),
            ("USB Mic".into(), Some("src2".into())),
        ]);
        let after = deduplicate_names([
            ("Default Input Device".into(), Some("src0".into())),
            ("USB Mic".into(), Some("src2".into())),
        ]);
        let key_before = resolve_device_key(&before, Some("USB Mic"));
        let key_after = resolve_device_key(&after, Some("USB Mic"));
        assert_eq!(key_before, Some(DeviceKey::Source("src2".into())));
        assert_eq!(key_before, key_after);
        assert_eq!(resolve_device_key(&before, None), Some(DeviceKey::Default));
        assert_eq!(
            resolve_device_key(&before, Some("")),
            Some(DeviceKey::Default)
        );
        assert_eq!(
            resolve_device_key(&before, Some("Default Input Device")),
            Some(DeviceKey::Default)
        );
        assert_eq!(resolve_device_key(&before, Some("Missing")), None);
    }

    #[test]
    fn idle_queries_return_defaults_without_connecting() {
        let recorder = Recorder::new();
        assert!(!recorder.is_recording(None));
        assert!(!recorder.is_any_recording());
        assert_eq!(recorder.position(None), 0);
        assert_eq!(recorder.position(Some("NoSuchDevice")), 0);
        #[cfg(target_os = "linux")]
        assert!(recorder.inner.is_uninitialized());
    }

    #[cfg(target_os = "linux")]
    #[test]
    fn polling_queries_do_not_block_on_control_state_lock() {
        let recorder = Recorder::new();
        let devices = deduplicate_names([
            ("Default Input Device".into(), Some("src0".into())),
            ("USB Mic".into(), Some("src1".into())),
            ("Line In".into(), Some("src2".into())),
        ]);
        recorder.inner.test_with_state_locked(
            devices,
            &[
                (DeviceKey::Default, 42, true),
                (DeviceKey::Source("src1".into()), 99, true),
            ],
            || {
                // Holding PlatformRecorder.state simulates a main-thread control op (e.g. Start/End/devices)
                // blocked on Mixer or PulseAudio; polling queries must read only the leaf QueryTable
                // and perform zero heap allocations for both default and non-default device names.
                crate::audio::tests::allocations::start();
                let rec_none = recorder.is_recording(None);
                let rec_default = recorder.is_recording(Some("Default Input Device"));
                let rec_usb = recorder.is_recording(Some("USB Mic"));
                let rec_line = recorder.is_recording(Some("Line In"));
                let rec_missing = recorder.is_recording(Some("Missing"));
                let rec_any = recorder.is_any_recording();
                let pos_none = recorder.position(None);
                let pos_default = recorder.position(Some("Default Input Device"));
                let pos_usb = recorder.position(Some("USB Mic"));
                let pos_line = recorder.position(Some("Line In"));
                let pos_missing = recorder.position(Some("Missing"));
                let allocs = crate::audio::tests::allocations::finish();

                assert_eq!(allocs, 0, "microphone polling queries must not allocate");
                assert!(rec_none);
                assert!(rec_default);
                assert!(rec_usb);
                assert!(!rec_line);
                assert!(!rec_missing);
                assert!(rec_any);
                assert_eq!(pos_none, 42);
                assert_eq!(pos_default, 42);
                assert_eq!(pos_usb, 99);
                assert_eq!(pos_line, 0);
                assert_eq!(pos_missing, 0);
            },
        );
    }
}
