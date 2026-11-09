//! The low-level native plugin interface: what a native plugin's `UnityPluginLoad` receives
//! (`IUnityInterfaces`) and the interfaces it finds there, laid out as Unity's public plugin
//! headers declare them (`IUnityInterface.h`, `IUnityGraphics.h`, `IUnityLog.h`,
//! `IUnityProfiler.h`). Behaviour matches the Unity 6000.0.71f1 Editor on OpenGL Core
//! (tests/projects/nativegfx): the renderer is the run's graphics device, as
//! `SystemInfo.graphicsDeviceType` reports; the first reserved event ID is 12; registering a null interface keeps the one
//! registered before; `IUnityLog` messages are script log messages. `IUnityMemoryManager`
//! allocates from the process heap; the profiler interfaces behave as with no profiler
//! recording.
//!
//! The rendering callbacks themselves (`GL.IssuePluginEvent` and the command buffer's plugin
//! commands) run in the renderer, in stream order ([`crate::render_commands::PluginCall`]).
use std::collections::HashMap;
use std::ffi::{CStr, CString, c_char, c_void};
use std::sync::atomic::{AtomicI32, Ordering};
use std::sync::{Mutex, OnceLock};

/// `UnityInterfaceGUID`.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct Guid {
    pub high: u64,
    pub low: u64,
}

/// `IUnityInterfaces`. `UnityInterfaceGUID` has a user-declared copy constructor, so C++ plugins
/// pass it to `GetInterface` and `RegisterInterface` by reference (the Itanium C++ ABI's rule for
/// a class that is not trivially copyable, on x86-64 and AArch64 alike): those two take a pointer.
#[repr(C)]
pub struct IUnityInterfaces {
    pub get_interface: unsafe extern "C" fn(*const Guid) -> *mut c_void,
    pub register_interface: unsafe extern "C" fn(*const Guid, *mut c_void),
    pub get_interface_split: extern "C" fn(u64, u64) -> *mut c_void,
    pub register_interface_split: extern "C" fn(u64, u64, *mut c_void),
}

/// `UnityGfxDeviceEventType`.
pub mod device_event {
    pub const INITIALIZE: i32 = 0;
    pub const SHUTDOWN: i32 = 1;
    pub const BEFORE_RESET: i32 = 2;
    pub const AFTER_RESET: i32 = 3;
}

/// `kUnityGfxRendererOpenGLCore`.
pub const RENDERER_OPENGL_CORE: i32 = 17;

/// The first ID `ReserveEventIDRange` returns: the Unity 6000.0.71f1 Editor's first range for a
/// plugin starts at 12 (tests/projects/nativegfx `event.base`).
pub const FIRST_EVENT_ID: i32 = 12;

pub type DeviceEventCallback = extern "C" fn(i32);

/// `IUnityGraphics`.
#[repr(C)]
pub struct IUnityGraphics {
    pub get_renderer: extern "C" fn() -> i32,
    pub register_device_event_callback: extern "C" fn(Option<DeviceEventCallback>),
    pub unregister_device_event_callback: extern "C" fn(Option<DeviceEventCallback>),
    pub reserve_event_id_range: extern "C" fn(i32) -> i32,
}

/// `IUnityLog`.
#[repr(C)]
pub struct IUnityLog {
    pub log: extern "C" fn(i32, *const c_char, *const c_char, i32),
}

/// `UnityProfilerMarkerDesc`.
#[repr(C)]
pub struct MarkerDesc {
    pub callback: *const c_void,
    pub id: u32,
    pub flags: u16,
    pub category: u16,
    pub name: *const c_char,
    pub metadata: *const c_void,
}

type EmitEvent = extern "C" fn(*const MarkerDesc, u16, u16, *const c_void);
type CreateMarker = extern "C" fn(*mut *const MarkerDesc, *const c_char, u16, u16, i32) -> i32;
type SetMarkerMetadataName = extern "C" fn(*const MarkerDesc, i32, *const c_char, u8, u8) -> i32;
type RegisterThread = extern "C" fn(*mut u64, *const c_char, *const c_char) -> i32;
type UnregisterThread = extern "C" fn(u64) -> i32;

/// `IUnityProfiler`.
#[repr(C)]
pub struct IUnityProfiler {
    pub emit_event: EmitEvent,
    pub is_enabled: extern "C" fn() -> i32,
    pub is_available: extern "C" fn() -> i32,
    pub create_marker: CreateMarker,
    pub set_marker_metadata_name: SetMarkerMetadataName,
    pub register_thread: RegisterThread,
    pub unregister_thread: UnregisterThread,
}

/// `IUnityProfilerV2`.
#[repr(C)]
pub struct IUnityProfilerV2 {
    pub emit_event: EmitEvent,
    pub is_enabled: extern "C" fn() -> i32,
    pub is_available: extern "C" fn() -> i32,
    pub create_marker: CreateMarker,
    pub set_marker_metadata_name: SetMarkerMetadataName,
    pub create_category: extern "C" fn(*mut u16, *const c_char, u32) -> i32,
    pub register_thread: RegisterThread,
    pub unregister_thread: UnregisterThread,
    #[allow(clippy::type_complexity)]
    pub create_counter_value: extern "C" fn(
        u16,
        *const c_char,
        u16,
        u8,
        u8,
        usize,
        u16,
        *const c_void,
        *const c_void,
        *mut c_void,
    ) -> *mut c_void,
    pub flush_counter_value: extern "C" fn(*mut c_void),
}

/// `IUnityMemoryManager`.
#[repr(C)]
pub struct IUnityMemoryManager {
    pub create_allocator: extern "C" fn(*const c_char, *const c_char) -> *mut c_void,
    pub destroy_allocator: extern "C" fn(*mut c_void),
    pub allocate: extern "C" fn(*mut c_void, usize, usize, *const c_char, i32) -> *mut c_void,
    pub deallocate: extern "C" fn(*mut c_void, *mut c_void, *const c_char, i32),
    pub reallocate:
        extern "C" fn(*mut c_void, *mut c_void, usize, usize, *const c_char, i32) -> *mut c_void,
}

/// Interface GUIDs of the plugin headers.
pub mod guid {
    use super::Guid;
    pub const GRAPHICS: Guid = Guid {
        high: 0x7CBA0A9CA4DDB544,
        low: 0x8C5AD4926EB17B11,
    };
    pub const LOG: Guid = Guid {
        high: 0x9E7507FA5B444D5D,
        low: 0x92FB979515EA83FC,
    };
    pub const PROFILER: Guid = Guid {
        high: 0x2CE79ED8316A4833,
        low: 0x87076B2013E1571F,
    };
    pub const PROFILER_V2: Guid = Guid {
        high: 0xB957E0189CB6A30B,
        low: 0x83CE589AE85B9068,
    };
    pub const MEMORY_MANAGER: Guid = Guid {
        high: 0xBAF9E57C61A811EC,
        low: 0xC5A7CC7861A811EC,
    };
    pub const GRAPHICS_VULKAN: Guid = Guid {
        high: 0x95355348D4EF4E11,
        low: 0x9789313DFCFFCC87,
    };
    pub const GRAPHICS_VULKAN_V2: Guid = Guid {
        high: 0x329334C09DCA4787,
        low: 0xB347DD92A0097FFC,
    };
}

/// Interfaces the Editor gives on OpenGL Core that Unbound does not provide: a plugin asking for
/// one gets null and the engine log names it (once).
const NOT_PROVIDED: [(Guid, &str); 5] = [
    (
        Guid {
            high: 0x572FDB38CE3C4B1F,
            low: 0xA6071A9A7C4F52D8,
        },
        "IUnityProfilerCallbacks",
    ),
    (
        Guid {
            high: 0x5DEB59E88F2D4571,
            low: 0x81E8583069A5E33C,
        },
        "IUnityProfilerCallbacksV2",
    ),
    (
        Guid {
            high: 0x9959C347F5AE374D,
            low: 0x9BADE6FC8EF49E7F,
        },
        "IUnityEventQueue",
    ),
    (
        Guid {
            high: 0x95355348D4EF4E11,
            low: 0x9789313DFCFFCC87,
        },
        "IUnityGraphicsVulkan",
    ),
    (
        Guid {
            high: 0x329334C09DCA4787,
            low: 0xB347DD92A0097FFC,
        },
        "IUnityGraphicsVulkanV2",
    ),
];

/// Where `IUnityLog` and the renderer's plugin messages go: the managed engine's log, as script
/// messages of a `LogType` (Error 0, Assert 1, Warning 2, Log 3, Exception 4: `UnityLogType`'s
/// values are the same).
pub type LogSink = extern "C" fn(i32, *const u8, i32);

/// `LogType.Assert`.
pub const LOG_ASSERT: i32 = 1;

struct Registry {
    /// Interfaces registered by GUID, as addresses.
    interfaces: HashMap<Guid, usize>,
    device_callbacks: Vec<DeviceEventCallback>,
    log: Option<LogSink>,
    /// GUIDs of interfaces not provided that were asked for (named once in the engine log).
    reported: Vec<Guid>,
    /// Markers made by name (their leaked `MarkerDesc`s, as addresses).
    markers: HashMap<String, usize>,
    next_marker: u32,
    next_category: u16,
}

static REGISTRY: OnceLock<Mutex<Registry>> = OnceLock::new();
static NEXT_EVENT_ID: AtomicI32 = AtomicI32::new(FIRST_EVENT_ID);

static GRAPHICS: IUnityGraphics = IUnityGraphics {
    get_renderer,
    register_device_event_callback,
    unregister_device_event_callback,
    reserve_event_id_range,
};
static LOG: IUnityLog = IUnityLog { log: plugin_log };
static PROFILER: IUnityProfiler = IUnityProfiler {
    emit_event,
    is_enabled: profiler_enabled,
    is_available: profiler_available,
    create_marker,
    set_marker_metadata_name,
    register_thread,
    unregister_thread,
};
static PROFILER_V2: IUnityProfilerV2 = IUnityProfilerV2 {
    emit_event,
    is_enabled: profiler_enabled,
    is_available: profiler_available,
    create_marker,
    set_marker_metadata_name,
    create_category,
    register_thread,
    unregister_thread,
    create_counter_value,
    flush_counter_value,
};
static MEMORY_MANAGER: IUnityMemoryManager = IUnityMemoryManager {
    create_allocator,
    destroy_allocator,
    allocate: memory::allocate,
    deallocate: memory::deallocate,
    reallocate: memory::reallocate,
};
static INTERFACES: IUnityInterfaces = IUnityInterfaces {
    get_interface,
    register_interface,
    get_interface_split,
    register_interface_split,
};

fn registry() -> std::sync::MutexGuard<'static, Registry> {
    REGISTRY
        .get_or_init(|| {
            let mut interfaces = HashMap::new();
            interfaces.insert(guid::GRAPHICS, &GRAPHICS as *const _ as usize);
            interfaces.insert(guid::LOG, &LOG as *const _ as usize);
            interfaces.insert(guid::PROFILER, &PROFILER as *const _ as usize);
            interfaces.insert(guid::PROFILER_V2, &PROFILER_V2 as *const _ as usize);
            interfaces.insert(guid::MEMORY_MANAGER, &MEMORY_MANAGER as *const _ as usize);
            Mutex::new(Registry {
                interfaces,
                device_callbacks: Vec::new(),
                log: None,
                reported: Vec::new(),
                markers: HashMap::new(),
                next_marker: 1,
                // After the built-in categories (`kUnityProfilerCategoryText` is 38).
                next_category: 39,
            })
        })
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner())
}

/// Offers an interface the renderer implements (the graphics device's own, as
/// `IUnityGraphicsVulkan` on the Vulkan device): plugins find it by its GUID.
pub fn provide(guid: Guid, interface: usize) {
    registry().interfaces.insert(guid, interface);
}

/// The `IUnityInterfaces` a plugin's `UnityPluginLoad` receives.
pub fn interfaces() -> *const IUnityInterfaces {
    &INTERFACES
}

/// Sets where plugin log messages go (the managed engine's log).
pub fn set_log_sink(sink: Option<LogSink>) {
    registry().log = sink;
}

thread_local! {
    /// Messages logged while this thread runs the renderer (`while_rendering`), delivered after.
    static HELD: std::cell::RefCell<Option<Vec<(i32, String)>>> = const { std::cell::RefCell::new(None) };
}

/// Runs `render` (which holds the renderer) and then delivers the plugin messages logged
/// meanwhile, in order: a script's log handler may render, which needs the renderer.
pub fn while_rendering<R>(render: impl FnOnce() -> R) -> R {
    let outer = HELD.with(|held| {
        let mut held = held.borrow_mut();
        let outer = held.is_some();
        held.get_or_insert_with(Vec::new);
        outer
    });
    let result = render();
    if !outer {
        let messages = HELD
            .with(|held| held.borrow_mut().take())
            .unwrap_or_default();
        for (kind, message) in messages {
            deliver(kind, &message);
        }
    }
    result
}

/// Writes a message to the script log (the managed sink), or the engine log without one; held
/// until the renderer returns when it runs (`while_rendering`).
pub fn log(kind: i32, message: &str) {
    let held = HELD.with(|held| match held.borrow_mut().as_mut() {
        Some(messages) => {
            messages.push((kind, message.to_string()));
            true
        }
        None => false,
    });
    if !held {
        deliver(kind, message);
    }
}

fn deliver(kind: i32, message: &str) {
    let sink = registry().log;
    match sink {
        Some(sink) => sink(kind, message.as_ptr(), message.len() as i32),
        None => match kind {
            2 => tracing::warn!(target: "game", "{message}"),
            3 => tracing::info!(target: "game", "{message}"),
            _ => tracing::error!(target: "game", "{message}"),
        },
    }
}

/// Calls every registered device event callback with `event` (a [`device_event`]), in
/// registration order. A callback may unregister itself or others meanwhile.
pub fn send_device_event(event: i32) {
    let callbacks = registry().device_callbacks.clone();
    for callback in callbacks {
        callback(event);
    }
}

/// What the application's end sends plugins: the device's Shutdown event twice, as the Unity
/// 6000.0.71f1 Editor does at exit; no plugin is unloaded, so `UnityPluginUnload` is not called
/// (tests/projects/nativegfx `plugin.exit`, also for a preloaded plugin).
pub fn shutdown() {
    send_device_event(device_event::SHUTDOWN);
    send_device_event(device_event::SHUTDOWN);
}

fn lookup(guid: Guid) -> *mut c_void {
    let mut registry = registry();
    if let Some(&address) = registry.interfaces.get(&guid) {
        return address as *mut c_void;
    }
    if let Some((_, name)) = NOT_PROVIDED.iter().find(|(g, _)| *g == guid)
        && !registry.reported.contains(&guid)
    {
        registry.reported.push(guid);
        tracing::warn!(
            "a native plugin asked for {name}, which Unbound does not provide: it gets null"
        );
    }
    std::ptr::null_mut()
}

fn register(guid: Guid, interface: *mut c_void) {
    // Registering null keeps what was registered (the Editor's GetInterface still finds it).
    if !interface.is_null() {
        registry().interfaces.insert(guid, interface as usize);
    }
}

unsafe extern "C" fn get_interface(guid: *const Guid) -> *mut c_void {
    if guid.is_null() {
        return std::ptr::null_mut();
    }
    lookup(unsafe { *guid })
}

unsafe extern "C" fn register_interface(guid: *const Guid, interface: *mut c_void) {
    if !guid.is_null() {
        register(unsafe { *guid }, interface);
    }
}

extern "C" fn get_interface_split(high: u64, low: u64) -> *mut c_void {
    lookup(Guid { high, low })
}

extern "C" fn register_interface_split(high: u64, low: u64, interface: *mut c_void) {
    register(Guid { high, low }, interface);
}

/// The run's graphics device (`SystemInfo.graphicsDeviceType`).
extern "C" fn get_renderer() -> i32 {
    crate::graphics_device::get().plugin_renderer()
}

extern "C" fn register_device_event_callback(callback: Option<DeviceEventCallback>) {
    let Some(callback) = callback else { return };
    let mut registry = registry();
    if !registry
        .device_callbacks
        .iter()
        .any(|c| std::ptr::fn_addr_eq(*c, callback))
    {
        registry.device_callbacks.push(callback);
    }
}

extern "C" fn unregister_device_event_callback(callback: Option<DeviceEventCallback>) {
    let Some(callback) = callback else { return };
    registry()
        .device_callbacks
        .retain(|c| !std::ptr::fn_addr_eq(*c, callback));
}

extern "C" fn reserve_event_id_range(count: i32) -> i32 {
    NEXT_EVENT_ID.fetch_add(count.max(0), Ordering::Relaxed)
}

/// A plugin-owned C string, empty when null.
fn c_text(text: *const c_char) -> String {
    if text.is_null() {
        return String::new();
    }
    unsafe { CStr::from_ptr(text) }
        .to_string_lossy()
        .into_owned()
}

extern "C" fn plugin_log(kind: i32, message: *const c_char, _file: *const c_char, _line: i32) {
    log(kind, &c_text(message));
}

// The profiler: none is attached, as in an Editor that is not recording. Markers and categories
// are made (and kept for the run) so that plugins can emit them; events go nowhere.

extern "C" fn emit_event(_: *const MarkerDesc, _: u16, _: u16, _: *const c_void) {}

extern "C" fn profiler_enabled() -> i32 {
    0
}

extern "C" fn profiler_available() -> i32 {
    1
}

extern "C" fn create_marker(
    desc: *mut *const MarkerDesc,
    name: *const c_char,
    category: u16,
    flags: u16,
    _event_data_count: i32,
) -> i32 {
    if desc.is_null() {
        return 1;
    }
    let name = c_text(name);
    let mut registry = registry();
    let marker = match registry.markers.get(&name) {
        Some(&marker) => marker as *const MarkerDesc,
        None => {
            let id = registry.next_marker;
            registry.next_marker += 1;
            let text = CString::new(name.replace('\0', "")).unwrap_or_default();
            let marker: &'static MarkerDesc = Box::leak(Box::new(MarkerDesc {
                callback: std::ptr::null(),
                id,
                flags,
                category,
                name: Box::leak(text.into_boxed_c_str()).as_ptr(),
                metadata: std::ptr::null(),
            }));
            registry
                .markers
                .insert(name, marker as *const MarkerDesc as usize);
            marker as *const MarkerDesc
        }
    };
    unsafe { *desc = marker };
    0
}

extern "C" fn set_marker_metadata_name(
    _: *const MarkerDesc,
    _: i32,
    _: *const c_char,
    _: u8,
    _: u8,
) -> i32 {
    0
}

extern "C" fn create_category(category: *mut u16, _name: *const c_char, _: u32) -> i32 {
    if category.is_null() {
        return 1;
    }
    let mut registry = registry();
    unsafe { *category = registry.next_category };
    registry.next_category = registry.next_category.wrapping_add(1);
    0
}

extern "C" fn register_thread(thread: *mut u64, _: *const c_char, _: *const c_char) -> i32 {
    if !thread.is_null() {
        unsafe { *thread = 0 };
    }
    0
}

extern "C" fn unregister_thread(_: u64) -> i32 {
    0
}

/// A counter's value storage (at most 8 bytes, `CreateCounterValue`'s limit), kept for the run.
#[allow(clippy::too_many_arguments)]
extern "C" fn create_counter_value(
    _: u16,
    _: *const c_char,
    _: u16,
    _: u8,
    _: u8,
    size: usize,
    _: u16,
    _: *const c_void,
    _: *const c_void,
    _: *mut c_void,
) -> *mut c_void {
    if size > 8 {
        return std::ptr::null_mut();
    }
    Box::leak(Box::new(0u64)) as *mut u64 as *mut c_void
}

extern "C" fn flush_counter_value(_: *mut c_void) {}

/// The allocator handle plugins receive: every allocator is the process heap.
static HEAP: u8 = 0;

extern "C" fn create_allocator(_area: *const c_char, _object: *const c_char) -> *mut c_void {
    &HEAP as *const u8 as *mut c_void
}

extern "C" fn destroy_allocator(_: *mut c_void) {}

/// `IUnityMemoryManager`'s blocks: aligned heap blocks that record their layout just before the
/// pointer handed out, so that freeing and growing need only the pointer.
mod memory {
    use std::alloc::Layout;
    use std::ffi::{c_char, c_void};

    /// The bytes before a block: its allocation's total size and alignment.
    const HEADER: usize = 2 * size_of::<usize>();

    fn header_size(align: usize) -> usize {
        align.max(HEADER)
    }

    pub(super) fn block(size: usize, align: usize) -> *mut c_void {
        let align = align.max(align_of::<usize>()).next_power_of_two();
        let offset = header_size(align);
        let Some(total) = size.checked_add(offset) else {
            return std::ptr::null_mut();
        };
        let Ok(layout) = Layout::from_size_align(total, align) else {
            return std::ptr::null_mut();
        };
        // SAFETY: the layout is nonzero (it holds the header); the header is written inside it.
        unsafe {
            let base = std::alloc::alloc(layout);
            if base.is_null() {
                return std::ptr::null_mut();
            }
            let block = base.add(offset);
            let header = block.sub(HEADER) as *mut usize;
            header.write_unaligned(total);
            header.add(1).write_unaligned(align);
            block as *mut c_void
        }
    }

    /// The block's usable size and its allocation's layout and start.
    ///
    /// # Safety
    /// `block` came from [`block`] and was not freed.
    unsafe fn layout_of(block: *mut c_void) -> (usize, Layout, *mut u8) {
        unsafe {
            let header = (block as *mut u8).sub(HEADER) as *const usize;
            let total = header.read_unaligned();
            let align = header.add(1).read_unaligned();
            let offset = header_size(align);
            (
                total - offset,
                Layout::from_size_align_unchecked(total, align),
                (block as *mut u8).sub(offset),
            )
        }
    }

    pub(super) extern "C" fn allocate(
        _: *mut c_void,
        size: usize,
        align: usize,
        _: *const c_char,
        _: i32,
    ) -> *mut c_void {
        block(size, align)
    }

    pub(super) extern "C" fn deallocate(
        _: *mut c_void,
        block: *mut c_void,
        _: *const c_char,
        _: i32,
    ) {
        if block.is_null() {
            return;
        }
        // SAFETY: plugins free what `allocate` or `reallocate` gave them, once.
        unsafe {
            let (_, layout, base) = layout_of(block);
            std::alloc::dealloc(base, layout);
        }
    }

    pub(super) extern "C" fn reallocate(
        allocator: *mut c_void,
        old: *mut c_void,
        size: usize,
        align: usize,
        file: *const c_char,
        line: i32,
    ) -> *mut c_void {
        let new = block(size, align);
        if !old.is_null() && !new.is_null() {
            // SAFETY: as `deallocate`; the copy stays within both blocks.
            unsafe {
                let (old_size, _, _) = layout_of(old);
                std::ptr::copy_nonoverlapping(old as *const u8, new as *mut u8, old_size.min(size));
            }
            deallocate(allocator, old, file, line);
        }
        new
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::AtomicUsize;

    #[test]
    fn interfaces_are_found_by_reference_and_split_guid() {
        let table = unsafe { &*interfaces() };
        let by_reference = unsafe { (table.get_interface)(&guid::GRAPHICS) };
        let split = (table.get_interface_split)(guid::GRAPHICS.high, guid::GRAPHICS.low);
        assert_eq!(by_reference, split);
        assert_eq!(by_reference as *const IUnityGraphics, &GRAPHICS as *const _);
        let graphics = unsafe { &*(by_reference as *const IUnityGraphics) };
        assert_eq!((graphics.get_renderer)(), RENDERER_OPENGL_CORE);
        // Interfaces Unbound does not provide, and unknown ones, are null.
        let vulkan = NOT_PROVIDED[3].0;
        assert!((table.get_interface_split)(vulkan.high, vulkan.low).is_null());
        assert!((table.get_interface_split)(1, 2).is_null());
    }

    #[test]
    fn a_registered_interface_is_kept_when_null_is_registered_after_it() {
        let table = unsafe { &*interfaces() };
        let own = Guid { high: 7, low: 9 };
        let mut value = 42u32;
        unsafe { (table.register_interface)(&own, &mut value as *mut u32 as *mut c_void) };
        assert_eq!(
            (table.get_interface_split)(7, 9),
            &mut value as *mut u32 as *mut c_void
        );
        (table.register_interface_split)(7, 9, std::ptr::null_mut());
        assert_eq!(
            unsafe { (table.get_interface)(&own) },
            &mut value as *mut u32 as *mut c_void
        );
    }

    static EVENTS: AtomicUsize = AtomicUsize::new(0);
    extern "C" fn count_shutdowns(event: i32) {
        if event == device_event::SHUTDOWN {
            EVENTS.fetch_add(1, Ordering::SeqCst);
        }
    }

    #[test]
    fn device_callbacks_receive_two_shutdowns_once_each() {
        (GRAPHICS.register_device_event_callback)(Some(count_shutdowns));
        // Registering the same callback again does not call it twice.
        (GRAPHICS.register_device_event_callback)(Some(count_shutdowns));
        shutdown();
        (GRAPHICS.unregister_device_event_callback)(Some(count_shutdowns));
        shutdown();
        assert_eq!(EVENTS.load(Ordering::SeqCst), 2);
    }

    #[test]
    fn memory_blocks_are_aligned_and_keep_their_bytes_when_grown() {
        let heap = create_allocator(std::ptr::null(), std::ptr::null());
        assert!(!heap.is_null());
        let block = memory::allocate(heap, 24, 64, std::ptr::null(), 0);
        assert_eq!(block as usize % 64, 0);
        unsafe { std::ptr::write_bytes(block as *mut u8, 7, 24) };
        let grown = memory::reallocate(heap, block, 4096, 16, std::ptr::null(), 0);
        assert_eq!(grown as usize % 16, 0);
        let bytes = unsafe { std::slice::from_raw_parts(grown as *const u8, 24) };
        assert!(bytes.iter().all(|&b| b == 7));
        memory::deallocate(heap, grown, std::ptr::null(), 0);
        // Null is ignored; zero bytes is a valid block.
        memory::deallocate(heap, std::ptr::null_mut(), std::ptr::null(), 0);
        let empty = memory::allocate(heap, 0, 1, std::ptr::null(), 0);
        assert!(!empty.is_null());
        memory::deallocate(heap, empty, std::ptr::null(), 0);
    }

    #[test]
    fn messages_logged_while_rendering_are_delivered_after_it_in_order() {
        static SEEN: Mutex<Vec<String>> = Mutex::new(Vec::new());
        static RENDERING: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);
        extern "C" fn sink(kind: i32, text: *const u8, length: i32) {
            let text = unsafe { std::slice::from_raw_parts(text, length as usize) };
            let rendering = RENDERING.load(Ordering::SeqCst);
            SEEN.lock().unwrap().push(format!(
                "{kind} {} {rendering}",
                String::from_utf8_lossy(text)
            ));
        }
        set_log_sink(Some(sink));
        let value = while_rendering(|| {
            RENDERING.store(true, Ordering::SeqCst);
            log(LOG_ASSERT, "first");
            while_rendering(|| log(3, "nested"));
            RENDERING.store(false, Ordering::SeqCst);
            7
        });
        log(2, "after");
        set_log_sink(None);
        assert_eq!(value, 7);
        assert_eq!(
            *SEEN.lock().unwrap(),
            ["1 first false", "3 nested false", "2 after false"]
        );
    }

    #[test]
    fn markers_are_made_once_per_name() {
        let (mut a, mut b) = (std::ptr::null(), std::ptr::null());
        let name = c"q220 marker";
        assert_eq!(create_marker(&mut a, name.as_ptr(), 1, 0, 0), 0);
        assert_eq!(create_marker(&mut b, name.as_ptr(), 1, 0, 0), 0);
        assert_eq!(a, b);
        assert_eq!(c_text(unsafe { (*a).name }), "q220 marker");
        assert_eq!((PROFILER_V2.is_enabled)(), 0);
    }
}
