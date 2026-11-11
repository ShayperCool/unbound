//! `IUnityProfilerCallbacks` and `IUnityProfilerCallbacksV2` (`IUnityProfilerCallbacks.h`): a
//! plugin's view of the profiler's categories, markers, threads, frames and marker events.
//! Behaviour matches the Unity 6000.0.71f1 Editor (tests/projects/nativegfx `profiler.*`,
//! `frames.sequence`):
//!
//! - registering a category, marker or thread callback calls it at once for everything that
//!   exists, then for each new one; a callback registered twice is called twice; four category
//!   callbacks and four marker callbacks can be registered, a fifth is refused (the header
//!   says so of thread and frame callbacks too, of which the Editor takes more);
//! - a marker event callback makes the marker's `callback` non-null, and `EmitEvent` calls it
//!   whether or not a profiler records; it is unregistered for one marker, or (no marker) for
//!   every marker it was registered for with the user data, or (no user data either) at all;
//! - frame callbacks are called once a frame, when the frame ends;
//! - unregistering a category, marker event or flow callback that is not registered answers
//!   -1; a marker, thread or frame callback, 0.
//!
//! What exists here is Unbound's: the Editor's built-in categories (their IDs, names and
//! colors as it reports them), the markers and categories plugins make, the main thread and
//! the threads plugins register. Not modelled: the markers scripts make (`ProfilerMarker`,
//! `Profiler.BeginSample`: the Editor reports them and their samples), the Editor's own
//! markers, worker threads and job flows; flow callbacks register and are never called.
use super::MarkerDesc;
use std::ffi::{CStr, c_char, c_void};
use std::sync::Mutex;
use std::sync::atomic::{AtomicPtr, AtomicU64, Ordering};

/// `UnityProfilerCategoryDesc`.
#[repr(C)]
pub struct CategoryDesc {
    pub id: u16,
    pub reserved0: u16,
    pub rgba_color: u32,
    pub name: *const c_char,
}

/// `UnityProfilerThreadDesc`.
#[repr(C)]
pub struct ThreadDesc {
    pub thread_id: u64,
    pub group_name: *const c_char,
    pub name: *const c_char,
}

type CategoryCallback = extern "C" fn(*const CategoryDesc, *mut c_void);
type MarkerCallback = extern "C" fn(*const MarkerDesc, *mut c_void);
type MarkerEventCallback = extern "C" fn(*const MarkerDesc, u16, u16, *const c_void, *mut c_void);
type FrameCallback = extern "C" fn(*mut c_void);
type ThreadCallback = extern "C" fn(*const ThreadDesc, *mut c_void);
type FlowCallback = extern "C" fn(u8, u32, *mut c_void);

/// `IUnityProfilerCallbacks`.
#[repr(C)]
pub struct IUnityProfilerCallbacks {
    pub(super) register_create_category:
        extern "C" fn(Option<CategoryCallback>, *mut c_void) -> i32,
    pub(super) unregister_create_category:
        extern "C" fn(Option<CategoryCallback>, *mut c_void) -> i32,
    pub(super) register_create_marker: extern "C" fn(Option<MarkerCallback>, *mut c_void) -> i32,
    pub(super) unregister_create_marker: extern "C" fn(Option<MarkerCallback>, *mut c_void) -> i32,
    pub(super) register_marker_event:
        extern "C" fn(*const MarkerDesc, Option<MarkerEventCallback>, *mut c_void) -> i32,
    pub(super) unregister_marker_event:
        extern "C" fn(*const MarkerDesc, Option<MarkerEventCallback>, *mut c_void) -> i32,
    pub(super) register_frame: extern "C" fn(Option<FrameCallback>, *mut c_void) -> i32,
    pub(super) unregister_frame: extern "C" fn(Option<FrameCallback>, *mut c_void) -> i32,
    pub(super) register_create_thread: extern "C" fn(Option<ThreadCallback>, *mut c_void) -> i32,
    pub(super) unregister_create_thread: extern "C" fn(Option<ThreadCallback>, *mut c_void) -> i32,
}

/// `IUnityProfilerCallbacksV2`: the same, and flow events.
#[repr(C)]
pub struct IUnityProfilerCallbacksV2 {
    v1: IUnityProfilerCallbacks,
    pub(super) register_flow_event: extern "C" fn(Option<FlowCallback>, *mut c_void) -> i32,
    pub(super) unregister_flow_event: extern "C" fn(Option<FlowCallback>, *mut c_void) -> i32,
}

const V1_FUNCTIONS: IUnityProfilerCallbacks = IUnityProfilerCallbacks {
    register_create_category,
    unregister_create_category,
    register_create_marker,
    unregister_create_marker,
    register_marker_event,
    unregister_marker_event,
    register_frame,
    unregister_frame,
    register_create_thread,
    unregister_create_thread,
};
pub static CALLBACKS: IUnityProfilerCallbacks = V1_FUNCTIONS;
pub static CALLBACKS_V2: IUnityProfilerCallbacksV2 = IUnityProfilerCallbacksV2 {
    v1: V1_FUNCTIONS,
    register_flow_event,
    unregister_flow_event,
};

/// The Editor's built-in categories in ID order (`UnityBuiltinProfilerCategory_`): the color
/// and the name it reports for each.
const BUILTIN_CATEGORIES: [(u32, &CStr); 39] = [
    (0xff059e7b, c"Render"),
    (0xffa78834, c"Scripts"),
    (0xffa78834, c"Managed Jobs"),
    (0xff71cc2e, c"Burst Jobs"),
    (0xff1e7b7a, c"Gui"),
    (0xff0070cc, c"Physics"),
    (0xffbcb25a, c"Animation"),
    (0xff1e7b7a, c"Ai"),
    (0xff5da050, c"Audio"),
    (0xffb74271, c"Audio Job"),
    (0xffb742a2, c"Audio Update Job"),
    (0xff1e7b7a, c"Video"),
    (0xff1e7b7a, c"Particles"),
    (0xff072888, c"Lighting"),
    (0xff1e7b7a, c"Network"),
    (0xff1e7b7a, c"Loading"),
    (0xff1e7b7a, c"Other"),
    (0xff0c6872, c"GC"),
    (0xff06a2c5, c"VSync"),
    (0xff1e7b7a, c"Overhead"),
    (0xff1e7b7a, c"PlayerLoop"),
    (0xff1e7b7a, c"Director"),
    (0xff1e7b7a, c"Vr"),
    (0xff4c19b2, c"Memory"),
    (0xbf646464, c"Internal"),
    (0xff1e7b7a, c"FileIO"),
    (0xff854961, c"UI Layout"),
    (0xff854961, c"UI Render"),
    (0xff059e7b, c"VFX"),
    (0xffb0c94e, c"Build Interface"),
    (0xfff0c94e, c"Input"),
    (0xff1e7b7a, c"Virtual Texturing"),
    (0xff1e7b7a, c"GPU"),
    (0xff1e7b7a, c"Physics2D"),
    (0xff1e7b7a, c"Network Operations"),
    (0xff1e7b7a, c"UI Details"),
    (0xff1e7b7a, c"Debug"),
    (0xff1e7b7a, c"Jobs"),
    (0xff854961, c"Text"),
];

/// `kUnityProfilerCategoryScripts`: a category a plugin makes has its color.
const SCRIPTS: usize = 1;
/// The main thread as the Editor reports it: no group.
const MAIN_THREAD: (&CStr, &CStr) = (c"", c"Main Thread");
/// The category and the marker callbacks that can be registered at a time.
const MAX_CALLBACKS: usize = 4;
/// The main thread's ID (the thread the player loop runs on: [`on_main_thread`]).
static MAIN_THREAD_ID: AtomicU64 = AtomicU64::new(0);

unsafe extern "C" {
    /// The C library's: the calling thread's handle (an integer or an address, of the size of
    /// one, on the platforms Unbound runs on).
    fn pthread_self() -> usize;
}

/// The calling thread's ID as plugins see it: the thread's own handle, as the header says
/// and the Editor reports (tests/projects/nativegfx `profiler.load` `self=1`).
fn thread_id() -> u64 {
    // SAFETY: no arguments; the calling thread's own handle.
    unsafe { pthread_self() as u64 }
}

/// Remembers the calling thread as the main thread (the first call's).
pub(super) fn on_main_thread() {
    let _ = MAIN_THREAD_ID.compare_exchange(0, thread_id(), Ordering::Relaxed, Ordering::Relaxed);
}

/// A marker's `callback` field: plugins read it while callbacks are registered and removed.
fn listening(marker: *const MarkerDesc) -> &'static AtomicPtr<c_void> {
    // SAFETY: markers are leaked allocations of this registry's, and the field is only read
    // and written through this view.
    unsafe { &*(std::ptr::addr_of!((*marker).callback) as *const AtomicPtr<c_void>) }
}

/// A registered callback: its address and the user data it is called with.
#[derive(Clone, Copy, PartialEq, Eq)]
struct Registered(usize, usize);

/// A thread plugins see: its ID and its leaked group and name.
#[derive(Clone, Copy)]
struct Thread {
    id: u64,
    group: usize,
    name: usize,
}

#[derive(Default)]
struct State {
    /// Categories plugins made, after the built-in ones: their leaked descriptions.
    categories: Vec<usize>,
    /// Markers in the order they were made (leaked `MarkerDesc`s).
    markers: Vec<usize>,
    /// Threads plugins registered, after the main thread.
    threads: Vec<Thread>,
    category_callbacks: Vec<Registered>,
    marker_callbacks: Vec<Registered>,
    thread_callbacks: Vec<Registered>,
    frame_callbacks: Vec<Registered>,
    flow_callbacks: Vec<Registered>,
    /// Marker event callbacks, each with its marker.
    event_callbacks: Vec<(usize, Registered)>,
}

static STATE: Mutex<Option<State>> = Mutex::new(None);

/// Runs `f` on the state. Callbacks are never called inside: they may call back in.
fn with<R>(f: impl FnOnce(&mut State) -> R) -> R {
    let mut state = STATE
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner());
    f(state.get_or_insert_with(State::default))
}

/// A callback of one of this module's callback types, with its user data.
fn key<F: Copy>(callback: F, user: *mut c_void) -> Registered {
    assert_eq!(size_of::<F>(), size_of::<usize>());
    // SAFETY: `F` is a function pointer, the size of an address (checked above).
    Registered(
        unsafe { std::mem::transmute_copy::<F, usize>(&callback) },
        user as usize,
    )
}

/// Removes one registration of the callback; one that is not registered is -1.
fn remove(list: &mut Vec<Registered>, registered: Registered) -> i32 {
    match list.iter().position(|r| *r == registered) {
        Some(at) => {
            list.remove(at);
            0
        }
        None => -1,
    }
}

/// What scripts make is not reported: said once, to whoever listens for markers or for a
/// marker's events.
fn script_markers_unreported() {
    static SAID: std::sync::Once = std::sync::Once::new();
    SAID.call_once(|| {
        tracing::warn!(
            "a native plugin listens for profiler markers (IUnityProfilerCallbacks): it hears of the markers plugins make and of their events, not of the ones scripts make (ProfilerMarker, Profiler.BeginSample)"
        );
    });
}

/// A null callback crashes the Editor; here it is refused.
fn refuse_null(what: &str) -> i32 {
    tracing::error!(
        "a native plugin registered a null {what} callback (IUnityProfilerCallbacks): refused"
    );
    -1
}

fn builtin_category(id: usize) -> CategoryDesc {
    let (rgba_color, name) = BUILTIN_CATEGORIES[id];
    CategoryDesc {
        id: id as u16,
        reserved0: 0,
        rgba_color,
        name: name.as_ptr(),
    }
}

/// The category of a name: a built-in one, else the one plugins made under it, if any.
pub(super) fn category_named(name: &CStr) -> Option<u16> {
    if let Some(id) = BUILTIN_CATEGORIES.iter().position(|(_, n)| *n == name) {
        return Some(id as u16);
    }
    with(|state| {
        state
            .categories
            .iter()
            .map(|&address| unsafe { &*(address as *const CategoryDesc) })
            .find(|desc| unsafe { CStr::from_ptr(desc.name) } == name)
            .map(|desc| desc.id)
    })
}

/// A plugin made a category (`CreateCategory` with a new name): its ID, the next after the
/// built-in ones; the category callbacks hear of it.
pub(super) fn category_created(name: &CStr) -> u16 {
    let (desc, callbacks) = with(|state| {
        let desc: &'static CategoryDesc = Box::leak(Box::new(CategoryDesc {
            id: (BUILTIN_CATEGORIES.len() + state.categories.len()) as u16,
            reserved0: 0,
            rgba_color: BUILTIN_CATEGORIES[SCRIPTS].0,
            name: Box::leak(name.to_owned().into_boxed_c_str()).as_ptr(),
        }));
        state.categories.push(desc as *const CategoryDesc as usize);
        (desc, state.category_callbacks.clone())
    });
    for Registered(callback, user) in callbacks {
        let callback: CategoryCallback = unsafe { std::mem::transmute(callback) };
        callback(desc, user as *mut c_void);
    }
    desc.id
}

/// A plugin made a marker (`CreateMarker` with a new name): the marker callbacks hear of it.
pub(super) fn marker_created(marker: *const MarkerDesc) {
    let callbacks = with(|state| {
        state.markers.push(marker as usize);
        state.marker_callbacks.clone()
    });
    for Registered(callback, user) in callbacks {
        let callback: MarkerCallback = unsafe { std::mem::transmute(callback) };
        callback(marker, user as *mut c_void);
    }
}

/// A plugin registered a thread (`RegisterThread`): its ID; the thread callbacks hear of it.
pub(super) fn thread_registered(group: &CStr, name: &CStr) -> u64 {
    let (thread, callbacks) = with(|state| {
        let thread = Thread {
            id: thread_id(),
            group: Box::leak(group.to_owned().into_boxed_c_str()).as_ptr() as usize,
            name: Box::leak(name.to_owned().into_boxed_c_str()).as_ptr() as usize,
        };
        state.threads.push(thread);
        (thread, state.thread_callbacks.clone())
    });
    call_thread(thread, &callbacks);
    thread.id
}

fn call_thread(thread: Thread, callbacks: &[Registered]) {
    let desc = ThreadDesc {
        thread_id: thread.id,
        group_name: thread.group as *const c_char,
        name: thread.name as *const c_char,
    };
    for &Registered(callback, user) in callbacks {
        let callback: ThreadCallback = unsafe { std::mem::transmute(callback) };
        callback(&desc, user as *mut c_void);
    }
}

/// `EmitEvent`: the marker's event callbacks, at once.
pub(super) fn marker_event(marker: *const MarkerDesc, kind: u16, count: u16, data: *const c_void) {
    // Nothing listens to a marker whose `callback` is null: the common case, without the lock.
    if marker.is_null() || listening(marker).load(Ordering::Acquire).is_null() {
        return;
    }
    let callbacks: Vec<Registered> = with(|state| {
        state
            .event_callbacks
            .iter()
            .filter(|(m, _)| *m == marker as usize)
            .map(|(_, registered)| *registered)
            .collect()
    });
    for Registered(callback, user) in callbacks {
        let callback: MarkerEventCallback = unsafe { std::mem::transmute(callback) };
        callback(marker, kind, count, data, user as *mut c_void);
    }
}

/// A frame ended: every frame callback, in registration order.
pub(super) fn frame() {
    let callbacks = with(|state| state.frame_callbacks.clone());
    for Registered(callback, user) in callbacks {
        let callback: FrameCallback = unsafe { std::mem::transmute(callback) };
        callback(user as *mut c_void);
    }
}

extern "C" fn register_create_category(
    callback: Option<CategoryCallback>,
    user: *mut c_void,
) -> i32 {
    let Some(callback) = callback else {
        return refuse_null("category");
    };
    let Some(made) = with(|state| {
        (state.category_callbacks.len() < MAX_CALLBACKS).then(|| {
            state.category_callbacks.push(key(callback, user));
            state.categories.clone()
        })
    }) else {
        return -1;
    };
    for id in 0..BUILTIN_CATEGORIES.len() {
        callback(&builtin_category(id), user);
    }
    for address in made {
        callback(address as *const CategoryDesc, user);
    }
    0
}

extern "C" fn unregister_create_category(
    callback: Option<CategoryCallback>,
    user: *mut c_void,
) -> i32 {
    let Some(callback) = callback else { return -1 };
    with(|state| remove(&mut state.category_callbacks, key(callback, user)))
}

extern "C" fn register_create_marker(callback: Option<MarkerCallback>, user: *mut c_void) -> i32 {
    let Some(callback) = callback else {
        return refuse_null("marker");
    };
    let Some(made) = with(|state| {
        (state.marker_callbacks.len() < MAX_CALLBACKS).then(|| {
            state.marker_callbacks.push(key(callback, user));
            state.markers.clone()
        })
    }) else {
        return -1;
    };
    script_markers_unreported();
    for address in made {
        callback(address as *const MarkerDesc, user);
    }
    0
}

/// 0 also for a callback that was not registered, as the Editor answers.
extern "C" fn unregister_create_marker(callback: Option<MarkerCallback>, user: *mut c_void) -> i32 {
    if let Some(callback) = callback {
        with(|state| remove(&mut state.marker_callbacks, key(callback, user)));
    }
    0
}

extern "C" fn register_marker_event(
    marker: *const MarkerDesc,
    callback: Option<MarkerEventCallback>,
    user: *mut c_void,
) -> i32 {
    let Some(callback) = callback else {
        return refuse_null("marker event");
    };
    if marker.is_null() {
        tracing::error!(
            "a native plugin registered a marker event callback for no marker (IUnityProfilerCallbacks): refused"
        );
        return -1;
    }
    with(|state| {
        state
            .event_callbacks
            .push((marker as usize, key(callback, user)));
        // The marker says that something listens (plugins read it before they emit).
        listening(marker).store(marker as *mut c_void, Ordering::Release);
    });
    script_markers_unreported();
    0
}

/// One registration for a marker; without a marker every registration of the callback with
/// the user data, and without user data either every registration of the callback.
extern "C" fn unregister_marker_event(
    marker: *const MarkerDesc,
    callback: Option<MarkerEventCallback>,
    user: *mut c_void,
) -> i32 {
    let Some(callback) = callback else { return -1 };
    let wanted = key(callback, user);
    with(|state| {
        let before = state.event_callbacks.len();
        if marker.is_null() {
            state
                .event_callbacks
                .retain(|(_, r)| r.0 != wanted.0 || (!user.is_null() && r.1 != wanted.1));
        } else if let Some(at) = state
            .event_callbacks
            .iter()
            .position(|r| *r == (marker as usize, wanted))
        {
            state.event_callbacks.remove(at);
        }
        if state.event_callbacks.len() == before {
            return -1;
        }
        // The markers nothing listens to any more say so: the named one, or any.
        let quiet = |address: usize| {
            if !state.event_callbacks.iter().any(|(m, _)| *m == address) {
                listening(address as *const MarkerDesc)
                    .store(std::ptr::null_mut(), Ordering::Release);
            }
        };
        match marker.is_null() {
            true => state.markers.iter().copied().for_each(quiet),
            false => quiet(marker as usize),
        }
        0
    })
}

extern "C" fn register_frame(callback: Option<FrameCallback>, user: *mut c_void) -> i32 {
    let Some(callback) = callback else {
        return refuse_null("frame");
    };
    with(|state| state.frame_callbacks.push(key(callback, user)));
    0
}

/// 0 also for a callback that was not registered, as the Editor answers.
extern "C" fn unregister_frame(callback: Option<FrameCallback>, user: *mut c_void) -> i32 {
    if let Some(callback) = callback {
        with(|state| remove(&mut state.frame_callbacks, key(callback, user)));
    }
    0
}

extern "C" fn register_create_thread(callback: Option<ThreadCallback>, user: *mut c_void) -> i32 {
    let Some(callback) = callback else {
        return refuse_null("thread");
    };
    let registered = key(callback, user);
    let threads = with(|state| {
        state.thread_callbacks.push(registered);
        state.threads.clone()
    });
    let main = Thread {
        id: MAIN_THREAD_ID.load(Ordering::Relaxed),
        group: MAIN_THREAD.0.as_ptr() as usize,
        name: MAIN_THREAD.1.as_ptr() as usize,
    };
    for thread in std::iter::once(main).chain(threads) {
        call_thread(thread, &[registered]);
    }
    0
}

/// 0 also for a callback that was not registered, as the Editor answers.
extern "C" fn unregister_create_thread(callback: Option<ThreadCallback>, user: *mut c_void) -> i32 {
    if let Some(callback) = callback {
        with(|state| remove(&mut state.thread_callbacks, key(callback, user)));
    }
    0
}

extern "C" fn register_flow_event(callback: Option<FlowCallback>, user: *mut c_void) -> i32 {
    let Some(callback) = callback else {
        return refuse_null("flow event");
    };
    with(|state| state.flow_callbacks.push(key(callback, user)));
    0
}

extern "C" fn unregister_flow_event(callback: Option<FlowCallback>, user: *mut c_void) -> i32 {
    let Some(callback) = callback else { return -1 };
    with(|state| remove(&mut state.flow_callbacks, key(callback, user)))
}
